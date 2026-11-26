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
    private readonly DispatcherTimer _hoverCloseTimer = new();

    private IntPtr _hwnd;
    private Win32Interop.WndProcDelegate? _subProc;
    private IntPtr _prevWndProc;
    private bool _isDragging;
    private Win32Interop.POINT _dragStartScreen;
    private bool _dragMoved;
    private string? _lastCompactArtUri;
    private bool _wasExpanded;
    private CancellationTokenSource? _retractCts;

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

        _autoCollapseTimer.Tick += async (_, _) =>
        {
            _autoCollapseTimer.Stop();
            if (_island.IsExpanded && !_island.IsHovered && !IsTextBoxFocused())
            {
                await BeginSmoothCollapseAsync();
            }
        };

        _hoverCloseTimer.Interval = TimeSpan.FromMilliseconds(340);
        _hoverCloseTimer.Tick += async (_, _) =>
        {
            _hoverCloseTimer.Stop();
            if (_island.IsExpanded && !_island.IsHovered && !_isDragging && !IsTextBoxFocused())
            {
                await BeginSmoothCollapseAsync();
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
        _island.Music.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() => RenderMusicSummary(e.PropertyName));
        _island.Timer.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderBadges);
        _island.Notifications.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderBadges);
        _settings.SettingsChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplySettingsAndReposition);
        NotchAssembly.SizeChanged += (_, _) => RepositionForActualShellSize();

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
        _animator.PulseNotificationArrival(NotchAssembly, _settings.Settings.Animation);

    private void ConfigureWindowChrome()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        _positioner.AttachWindow(_hwnd);

        var style = Win32Interop.GetWindowLongPtr(_hwnd, Win32Interop.GWL_STYLE).ToInt64();
        style &= ~(Win32Interop.WS_CAPTION | Win32Interop.WS_THICKFRAME | Win32Interop.WS_MINIMIZEBOX | Win32Interop.WS_MAXIMIZEBOX | Win32Interop.WS_SYSMENU);
        style |= Win32Interop.WS_POPUP;
        Win32Interop.SetWindowLongPtr(_hwnd, Win32Interop.GWL_STYLE, new IntPtr(style));

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
        Win32Interop.SetWindowPos(_hwnd, Win32Interop.HWND_TOPMOST, 0, 0, 0, 0,
            Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_FRAMECHANGED);
    }

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == Win32Interop.WM_NCCALCSIZE && wParam != IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        if (msg == Win32Interop.WM_GETMINMAXINFO && lParam != IntPtr.Zero)
        {
            var mmi = Marshal.PtrToStructure<Win32Interop.MINMAXINFO>(lParam);
            mmi.ptMinTrackSize = new Win32Interop.POINT { X = 1, Y = 1 };
            Marshal.StructureToPtr(mmi, lParam, false);
            return IntPtr.Zero;
        }

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

            var stealth = _island.Mode == IslandMode.Compact && _settings.Settings.General.StealthTopBar;
            var hitWidth = NotchAssembly.ActualWidth > 0 ? NotchAssembly.ActualWidth : PillShell.Width;
            var hitHeight = NotchAssembly.ActualHeight > 0 ? NotchAssembly.ActualHeight : (stealth ? 14 : 40);
            var left = (RootHost.ActualWidth - hitWidth) / 2.0;

            if (dipX < left || dipX > left + hitWidth || dipY < 0 || dipY > hitHeight)
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
        var isStealthCompact = _island.Mode == IslandMode.Compact && s.General.StealthTopBar;

        var targetWidth = _island.Mode switch
        {
            IslandMode.Compact => isStealthCompact ? Math.Clamp(s.Appearance.IslandWidth, 140, 260) : s.Appearance.IslandWidth,
            IslandMode.Music => Math.Max(s.Appearance.IslandWidth + 124, 344),
            IslandMode.Expanded => 620,
            IslandMode.System or IslandMode.Clipboard => 580,
            _ => 560
        };

        var actualHeight = PillShell.ActualHeight > 0 ? PillShell.ActualHeight : 40;
        var targetHeight = _island.Mode switch
        {
            IslandMode.Compact => isStealthCompact ? 14 : s.Appearance.IslandHeight,
            IslandMode.Music => Math.Max(40, s.Appearance.IslandHeight + 4),
            IslandMode.Expanded => Math.Max(180, actualHeight),
            _ => Math.Max(220, actualHeight)
        };

        PillShell.Width = targetWidth;
        PillShell.Height = isStealthCompact ? 14 : double.NaN;

        var showEars = !isStealthCompact && s.General.StealthTopBar;
        LeftNotchEar.Visibility = showEars ? Visibility.Visible : Visibility.Collapsed;
        RightNotchEar.Visibility = showEars ? Visibility.Visible : Visibility.Collapsed;

        var radius = _island.IsExpanded
            ? Math.Clamp(s.Appearance.CornerRadius + 2, 22, 32)
            : Math.Max(18, s.Appearance.CornerRadius);

        if (isStealthCompact)
        {
            GlassSheenLayer.Visibility = Visibility.Collapsed;
            PillShell.CornerRadius = new CornerRadius(0, 0, 10, 10);
            PillShell.BorderThickness = new Thickness(0);
            PillShell.Background = new SolidColorBrush(Color.FromArgb(2, 0, 0, 0));
        }
        else
        {
            GlassSheenLayer.Visibility = Visibility.Visible;
            PillShell.CornerRadius = s.General.StealthTopBar
                ? new CornerRadius(0, 0, radius, radius)
                : new CornerRadius(radius);
            PillShell.BorderThickness = s.General.StealthTopBar
                ? new Thickness(1, 0, 1, 1)
                : new Thickness(1);
            PillShell.Background = (Brush)Application.Current.Resources["IslandSurfaceBrush"];
            PillShell.BorderBrush = (Brush)Application.Current.Resources[
                s.Appearance.Theme == ThemePreset.Contrast ? "IslandBorderContrastBrush" : "IslandBorderStrongBrush"];
        }

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

        var earAllowance = showEars ? 24 : 0;
        var hostWidth = (int)Math.Ceiling(targetWidth + earAllowance + 24);
        var hostHeight = (int)Math.Ceiling(targetHeight + (isStealthCompact ? 0 : 18));
        _positioner.ApplyIslandPosition(s.Position, hostWidth, hostHeight, flushTopEdge: s.General.StealthTopBar);

        _animator.SetIdleBreathing(IdleBreatheBar, s.Animation.IdleBreathing, s.Animation.ReduceMotion);
    }

    private void RepositionForActualShellSize()
    {
        if (!_island.IsExpanded || PillShell.ActualWidth <= 0 || PillShell.ActualHeight <= 0)
        {
            return;
        }

        var earAllowance = _settings.Settings.General.StealthTopBar ? 24 : 0;
        var hostWidth = (int)Math.Ceiling(Math.Max(PillShell.Width, PillShell.ActualWidth) + earAllowance + 24);
        var hostHeight = (int)Math.Ceiling(PillShell.ActualHeight + 18);
        _positioner.ApplyIslandPosition(_settings.Settings.Position, hostWidth, hostHeight, flushTopEdge: _settings.Settings.General.StealthTopBar);
    }

    private void RenderState()
    {
        CancelPendingRetract();

        var isStealthCompact = _island.Mode == IslandMode.Compact && _settings.Settings.General.StealthTopBar;
        StealthBarView.Visibility = isStealthCompact ? Visibility.Visible : Visibility.Collapsed;
        CompactView.Visibility = _island.Mode == IslandMode.Compact && !isStealthCompact ? Visibility.Visible : Visibility.Collapsed;
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
        HighlightActiveTab();

        if (_island.IsExpanded)
        {
            if (!_wasExpanded)
            {
                _animator.AnimateVenomOpen(
                    NotchAssembly,
                    PanelContainer,
                    PillShell.Width + (_settings.Settings.General.StealthTopBar ? 24 : 0),
                    _settings.Settings.Animation,
                    _settings.Settings.Appearance.AnimationSpeed);
            }
            else
            {
                _animator.AnimateContentEntrance(PanelContainer, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);
            }
            RestartAutoCollapseTimer();
        }
        else
        {
            _animator.RestoreShellImmediate(NotchAssembly);
            _autoCollapseTimer.Stop();
        }

        _wasExpanded = _island.IsExpanded;
        RenderMusicSummary(null);
        RenderBadges();
    }

    private void HighlightActiveTab()
    {
        var activeBg = (Brush)Application.Current.Resources["IslandControlActiveBrush"];
        var activeFg = (Brush)Application.Current.Resources["IslandTextPrimaryBrush"];
        var inactiveFg = (Brush)Application.Current.Resources["IslandTextSecondaryBrush"];
        var transparent = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        foreach (var child in TopTabsPanel.Children)
        {
            if (child is Button btn && btn.Tag is string tag && Enum.TryParse<IslandMode>(tag, out var mode))
            {
                var isActive = mode == _island.Mode;
                btn.Background = isActive ? activeBg : transparent;
                btn.Foreground = isActive ? activeFg : inactiveFg;
                btn.FontWeight = isActive ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Medium;
            }
        }
    }

    private void RenderMusicSummary(string? propertyName)
    {
        if (propertyName == nameof(MusicViewModel.LivePositionSeconds))
        {
            CompactProgressLine.Value = _island.Music.ProgressPercentage;
            return;
        }

        var session = _island.Music.CurrentSession;
        CompactEqualizer.IsActive = _island.Music.IsPlaying;
        CompactPlayIcon.Glyph = _island.Music.IsPlaying ? "\uE769" : "\uE768";
        if (session is null)
        {
            _lastCompactArtUri = null;
            if (_island.Mode == IslandMode.Music)
            {
                _island.SetMode(IslandMode.Compact);
            }
            return;
        }

        if (!string.Equals(_lastCompactArtUri, session.ArtworkUrl, StringComparison.Ordinal))
        {
            _lastCompactArtUri = session.ArtworkUrl;
            MusicPillArt.SourceUri = session.ArtworkUrl;
        }
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
        _hoverCloseTimer.Stop();
        _autoCollapseTimer.Stop();
        CancelPendingRetract();

        if (_island.IsExpanded)
        {
            _animator.RestoreShellImmediate(NotchAssembly);
            return;
        }

        if (_settings.Settings.General.HoverToOpen)
        {
            _island.SetMode(IslandMode.Expanded);
        }
        else if (!_settings.Settings.General.StealthTopBar)
        {
            _animator.AnimateHoverScale(NotchAssembly, true, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);
        }
    }

    private async Task BeginSmoothCollapseAsync()
    {
        if (!_island.IsExpanded)
        {
            return;
        }

        CancelPendingRetract();
        _retractCts = new CancellationTokenSource();
        var completed = await _animator.AnimateVenomRetractAsync(
            NotchAssembly,
            NotchAssembly.ActualWidth > 0 ? NotchAssembly.ActualWidth : PillShell.Width,
            _settings.Settings.Animation,
            _settings.Settings.Appearance.AnimationSpeed,
            _retractCts.Token);

        if (completed && !_island.IsHovered)
        {
            _island.Collapse(_island.Music.HasActiveMedia, _settings.Settings.General.StealthTopBar);
        }
    }

    private void CancelPendingRetract()
    {
        if (_retractCts is not null)
        {
            _retractCts.Cancel();
            _retractCts.Dispose();
            _retractCts = null;
        }
    }

    private void OnPillPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _island.IsHovered = false;
        _animator.AnimateHoverScale(NotchAssembly, false, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);

        if (_island.IsExpanded)
        {
            if (_settings.Settings.General.CollapseBehavior == CollapseBehavior.MouseLeave || _settings.Settings.General.HoverToOpen)
            {
                _hoverCloseTimer.Stop();
                _hoverCloseTimer.Start();
            }
            else
            {
                RestartAutoCollapseTimer();
            }
        }
    }

    private bool IsTextBoxFocused()
    {
        return RootHost.XamlRoot is not null &&
               FocusManager.GetFocusedElement(RootHost.XamlRoot) is TextBox;
    }

    private void OnPillPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (IsInteractiveControl(e.OriginalSource as DependencyObject))
        {
            return;
        }

        if (e.GetCurrentPoint(NotchAssembly).Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragMoved = false;
            Win32Interop.GetCursorPos(out _dragStartScreen);
        }
    }

    private void OnPillPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging) return;
        Win32Interop.GetCursorPos(out var current);
        var dx = Math.Abs(current.X - _dragStartScreen.X);
        var dy = Math.Abs(current.Y - _dragStartScreen.Y);
        if (!_dragMoved && dx < 6 && dy < 6) return;

        if (!_dragMoved)
        {
            _dragMoved = true;
            NotchAssembly.CapturePointer(e.Pointer);
        }
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
        if (_dragMoved)
        {
            NotchAssembly.ReleasePointerCapture(e.Pointer);
            await _settings.SaveAsync();
        }
    }

    private static bool IsInteractiveControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Microsoft.UI.Xaml.Controls.Primitives.ButtonBase
                or Slider
                or TextBox
                or Microsoft.UI.Xaml.Controls.Primitives.ScrollBar)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
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

    private async void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        _hoverCloseTimer.Stop();
        _island.IsHovered = false;
        await BeginSmoothCollapseAsync();
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) =>
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
}
