using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.App.Animations;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.ViewModels;
using NOVAIsland.Infrastructure.Platform;
using WinRT.Interop;
using WinUIEx;
using Windows.UI;

namespace NOVAIsland.App.Views.Island;

public sealed partial class DynamicIslandWindow : Window
{
    private readonly IslandViewModel _island;
    private readonly SettingsViewModel _settings;
    private readonly MultiMonitorPositioner _positioner;
    private readonly CompositionSpringAnimator _animator = new();
    private readonly DispatcherTimer _autoCollapseTimer = new();

    private IntPtr _hwnd;
    private Win32Interop.WndProcDelegate? _subProc;
    private IntPtr _prevWndProc;
    private bool _isDragging;
    private Win32Interop.POINT _dragStartScreen;
    private bool _dragMoved;

    public event EventHandler? OpenDashboardRequested;
    public event EventHandler? OpenSettingsRequested;

    public DynamicIslandWindow(
        IslandViewModel island,
        SettingsViewModel settings,
        MultiMonitorPositioner positioner)
    {
        InitializeComponent();
        _island = island;
        _settings = settings;
        _positioner = positioner;

        this.SystemBackdrop = new TransparentTintBackdrop(Color.FromArgb(0, 0, 0, 0));

        _autoCollapseTimer.Tick += (_, _) =>
        {
            _autoCollapseTimer.Stop();
            if (_island.IsExpanded && !_island.IsHovered)
            {
                _island.Collapse(_island.Music.HasActiveMedia);
            }
        };

        MusicPanel.Bind(_island.Music, _island.System, _island, _island.Lyrics);
        LyricsPanel.Bind(_island.Lyrics, _island.Music, _settings);
        ClipboardPanel.Bind(_island.Clipboard);
        NotificationsPanel.Bind(_island.Notifications, _settings);
        TimerPanel.Bind(_island.Timer);
        SystemPanel.Bind(_island.System);
        ActivityPanel.Bind(_island.Activity);

        _island.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderState);
        _island.Music.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderMusicSummary);
        _island.Timer.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderBadges);
        _island.Notifications.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderBadges);
        _settings.SettingsChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplySettingsAndReposition);

        ConfigureWindowChrome();
        ApplySettingsAndReposition();
        RenderState();
    }

    public void UpdateSpectrum(ReadOnlySpan<float> bands)
    {
        CompactEqualizer.UpdateSpectrum(bands);
        MusicPanel.UpdateSpectrum(bands);
    }

    public void PulseNotification() =>
        _animator.PulseNotificationArrival(PillShell, _settings.Settings.Animation);

    private void ConfigureWindowChrome()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        _positioner.AttachWindow(_hwnd);

        var exStyle = Win32Interop.GetWindowLongPtr(_hwnd, Win32Interop.GWL_EXSTYLE).ToInt64();
        exStyle |= Win32Interop.WS_EX_LAYERED | Win32Interop.WS_EX_TOOLWINDOW | Win32Interop.WS_EX_NOACTIVATE;
        Win32Interop.SetWindowLongPtr(_hwnd, Win32Interop.GWL_EXSTYLE, new IntPtr(exStyle));

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.IsShownInSwitchers = false;

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = _settings.Settings.General.AlwaysOnTop;
        }

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            var pref = Win32Interop.DWMWCP_DONOTROUND;
            Win32Interop.DwmSetWindowAttribute(_hwnd, Win32Interop.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(uint));
        }

        _subProc = CustomWndProc;
        _prevWndProc = Win32Interop.SetWindowLongPtr(_hwnd, -4, Marshal.GetFunctionPointerForDelegate(_subProc));
    }

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32Interop.WM_NCHITTEST)
        {
            var x = (short)(lParam.ToInt64() & 0xFFFF);
            var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
            var pt = new Win32Interop.POINT { X = x, Y = y };
            Win32Interop.ScreenToClient(hWnd, ref pt);

            var dpi = Math.Max(96u, Win32Interop.GetDpiForWindow(hWnd));
            var scale = dpi / 96.0;
            var dipX = pt.X / scale;
            var dipY = pt.Y / scale;

            var pillWidth = PillShell.ActualWidth > 0 ? PillShell.ActualWidth : PillShell.Width;
            var pillHeight = PillShell.ActualHeight > 0 ? PillShell.ActualHeight : 40;
            var left = (RootHost.ActualWidth - pillWidth) / 2.0;

            if (dipX < left || dipX > left + pillWidth || dipY < 0 || dipY > pillHeight)
            {
                return Win32Interop.HTTRANSPARENT;
            }
        }

        return Win32Interop.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    public void ApplySettingsAndReposition()
    {
        var s = _settings.Settings;
        CompactWordmark.Text = _settings.ResolvedShortName;

        var targetWidth = _island.Mode switch
        {
            IslandMode.Compact => s.Appearance.IslandWidth,
            IslandMode.Music => Math.Max(s.Appearance.IslandWidth + 124, 344),
            IslandMode.System or IslandMode.Clipboard => 560,
            _ => 524
        };

        var targetHeight = _island.Mode switch
        {
            IslandMode.Compact => s.Appearance.IslandHeight,
            IslandMode.Music => Math.Max(40, s.Appearance.IslandHeight + 4),
            _ => 420
        };

        PillShell.Width = targetWidth;
        var radius = _island.IsExpanded
            ? Math.Clamp(s.Appearance.CornerRadius + 4, 22, 34)
            : Math.Max(18, s.Appearance.CornerRadius);
        PillShell.CornerRadius = new CornerRadius(radius);
        PillShell.BorderBrush = (Brush)Application.Current.Resources[
            s.Appearance.Theme == ThemePreset.Contrast ? "IslandBorderContrastBrush" : "IslandBorderSubtleBrush"];

        CompactView.Height = s.Appearance.IslandHeight;
        MusicPillView.Height = Math.Max(40, s.Appearance.IslandHeight + 4);

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = s.General.AlwaysOnTop;
        }

        if (s.General.IslandEnabled)
        {
            appWindow.Show(false);
        }
        else
        {
            appWindow.Hide();
        }

        var hostWidth = (int)Math.Ceiling(targetWidth + 24);
        var hostHeight = (int)Math.Ceiling(targetHeight + 24);
        _positioner.ApplyIslandPosition(s.Position, hostWidth, hostHeight);

        _animator.SetIdleBreathing(IdleBreatheBar, s.Animation.IdleBreathing, s.Animation.ReduceMotion);
    }

    private void RenderState()
    {
        CompactView.Visibility = _island.Mode == IslandMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        MusicPillView.Visibility = _island.Mode == IslandMode.Music ? Visibility.Visible : Visibility.Collapsed;
        ExpandedShellView.Visibility = _island.IsExpanded ? Visibility.Visible : Visibility.Collapsed;

        MusicPanel.Visibility = _island.Mode == IslandMode.Expanded ? Visibility.Visible : Visibility.Collapsed;
        LyricsPanel.Visibility = _island.Mode == IslandMode.Lyrics ? Visibility.Visible : Visibility.Collapsed;
        ClipboardPanel.Visibility = _island.Mode == IslandMode.Clipboard ? Visibility.Visible : Visibility.Collapsed;
        NotificationsPanel.Visibility = _island.Mode == IslandMode.Notifications ? Visibility.Visible : Visibility.Collapsed;
        TimerPanel.Visibility = _island.Mode == IslandMode.Timer ? Visibility.Visible : Visibility.Collapsed;
        SystemPanel.Visibility = _island.Mode == IslandMode.System ? Visibility.Visible : Visibility.Collapsed;
        ActivityPanel.Visibility = _island.Mode == IslandMode.Activity ? Visibility.Visible : Visibility.Collapsed;

        ApplySettingsAndReposition();
        if (_island.IsExpanded)
        {
            _animator.AnimateContentEntrance(PanelContainer, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);
            RestartAutoCollapseTimer();
        }
        else
        {
            _autoCollapseTimer.Stop();
        }

        RenderMusicSummary();
        RenderBadges();
    }

    private void RenderMusicSummary()
    {
        var session = _island.Music.CurrentSession;
        CompactEqualizer.IsActive = _island.Music.IsPlaying;
        CompactPlayIcon.Glyph = _island.Music.IsPlaying ? "\uE769" : "\uE768";
        if (session is null)
        {
            if (_island.Mode == IslandMode.Music)
            {
                _island.SetMode(IslandMode.Compact);
            }
            return;
        }

        MusicPillArt.SourceUri = session.ArtworkUrl;
        MusicPillTitle.Text = string.IsNullOrWhiteSpace(session.Title) ? session.TabTitle : session.Title;
        CompactProgressLine.Value = _island.Music.ProgressPercentage;
    }

    private void RenderBadges()
    {
        var timerText = _island.Timer.CompactBadgeText;
        CompactTimerBadge.Text = timerText ?? string.Empty;
        CompactTimerBadge.Visibility = timerText is not null ? Visibility.Visible : Visibility.Collapsed;

        var alerts = _island.Notifications.UnreadCount;
        var showAlert = alerts > 0 && !_settings.Settings.General.FocusDndMode;
        CompactAlertBadge.Text = $"{alerts} alerts";
        CompactAlertBadge.Visibility = showAlert ? Visibility.Visible : Visibility.Collapsed;

        CompactIdleGroup.Visibility = timerText is null && !showAlert ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RestartAutoCollapseTimer()
    {
        _autoCollapseTimer.Stop();
        var secs = _settings.Settings.General.AutoCollapseSeconds;
        if (secs > 0 && _island.IsExpanded && !_island.IsHovered)
        {
            _autoCollapseTimer.Interval = TimeSpan.FromSeconds(secs);
            _autoCollapseTimer.Start();
        }
    }

    private void OnPillPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _island.IsHovered = true;
        _autoCollapseTimer.Stop();
        if (!_island.IsExpanded)
        {
            _animator.AnimateHoverScale(PillShell, true, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);
        }
    }

    private void OnPillPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _island.IsHovered = false;
        _animator.AnimateHoverScale(PillShell, false, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);

        if (_island.IsExpanded)
        {
            if (_settings.Settings.General.CollapseBehavior == CollapseBehavior.MouseLeave)
            {
                _island.Collapse(_island.Music.HasActiveMedia);
            }
            else
            {
                RestartAutoCollapseTimer();
            }
        }
    }

    private void OnPillPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.GetCurrentPoint(PillShell).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragMoved = false;
            Win32Interop.GetCursorPos(out _dragStartScreen);
            PillShell.CapturePointer(e.Pointer);
        }
    }

    private void OnPillPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        Win32Interop.GetCursorPos(out var current);
        var dx = Math.Abs(current.X - _dragStartScreen.X);
        var dy = Math.Abs(current.Y - _dragStartScreen.Y);
        if (!_dragMoved && dx < 6 && dy < 6) return;

        _dragMoved = true;
        var (pctX, pxY, _) = _positioner.ComputeScreenPointToCustomPercent(current.X, current.Y, _settings.Settings.Position);
        _settings.Settings.Position.ScreenPosition = ScreenPosition.Custom;
        _settings.Settings.Position.CustomX = pctX;
        _settings.Settings.Position.CustomY = pxY;
        ApplySettingsAndReposition();
    }

    private async void OnPillPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        PillShell.ReleasePointerCapture(e.Pointer);
        if (_dragMoved)
        {
            await _settings.SaveAsync();
        }
    }

    private void OnCompactTapped(object sender, TappedRoutedEventArgs e)
    {
        if (_dragMoved) return;
        _island.SetMode(_island.Music.HasActiveMedia ? IslandMode.Expanded : IslandMode.System);
    }

    private async void OnPillDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        switch (_settings.Settings.General.DoubleClickAction)
        {
            case DoubleClickAction.ToggleLyrics:
                _island.SetMode(_island.Mode == IslandMode.Lyrics ? IslandMode.Expanded : IslandMode.Lyrics);
                break;
            case DoubleClickAction.OpenFullscreenLyrics:
                _island.Lyrics.IsFullscreenOpen = true;
                break;
            case DoubleClickAction.OpenDashboard:
                OpenDashboardRequested?.Invoke(this, EventArgs.Empty);
                break;
            case DoubleClickAction.TogglePlay:
                await _island.Music.TogglePlayPauseAsync();
                break;
        }
    }

    private async void OnCompactPlayPauseClick(object sender, RoutedEventArgs e)
    {
        await _island.Music.TogglePlayPauseAsync();
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<IslandMode>(tag, out var mode))
        {
            _island.SetMode(mode);
        }
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e) =>
        _island.Collapse(_island.Music.HasActiveMedia);

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) =>
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
}
