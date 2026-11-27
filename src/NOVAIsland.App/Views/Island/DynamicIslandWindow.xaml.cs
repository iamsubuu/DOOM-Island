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
    private readonly Services.ThemeManager _themeManager;
    private readonly CompositionSpringAnimator _animator = new();
    private readonly DispatcherTimer _autoCollapseTimer = new();
    private readonly DispatcherTimer _hoverCloseTimer = new();
    private readonly DispatcherTimer _hoverTrackTimer = new();

    private IntPtr _hwnd;
    private Win32Interop.WndProcDelegate? _subProc;
    private IntPtr _prevWndProc;
    private IntPtr _outsideMouseHook;
    private LowLevelMouseProc? _outsideMouseHookProc;
    private string? _lastCompactArtUri;
    private bool _wasExpanded;
    private bool _isRetracting;
    private bool _allowShutdownClose;
    private bool _openedByShortcut;
    private ShortcutAction? _activeShortcutAction;
    private bool _isSessionLocked;
    private bool? _lastEnergyRunning;
    private bool? _lastIdleBreathing;
    private bool? _lastIslandShown;
    private double _lastEnergyWidth;
    private int _lastHostWidth;
    private int _lastHostHeight;
    private bool? _lastFlushTop;
    private bool? _lastNoActivate;
    private DateTime _hoverOpenCommitUntilUtc = DateTime.MinValue;
    private CancellationTokenSource? _retractCts;

    public event EventHandler? OpenDashboardRequested;
    public event EventHandler? OpenSettingsRequested;

    public DynamicIslandWindow(
        IslandViewModel island,
        SettingsViewModel settings,
        MultiMonitorPositioner positioner,
        Services.ThemeManager themeManager)
    {
        InitializeComponent();
        _island = island;
        _settings = settings;
        _positioner = positioner;
        _themeManager = themeManager;

        this.SystemBackdrop = new TransparentTintBackdrop(Color.FromArgb(0, 0, 0, 0));

        _autoCollapseTimer.Tick += async (_, _) =>
        {
            _autoCollapseTimer.Stop();
            try
            {
                if (_island.IsExpanded && !_openedByShortcut && !_island.IsHovered && !IsTextBoxFocused() && DateTime.UtcNow >= _hoverOpenCommitUntilUtc)
                {
                    await BeginSmoothCollapseAsync();
                }
            }
            catch { }
        };

        _hoverCloseTimer.Interval = TimeSpan.FromMilliseconds(260);
        _hoverCloseTimer.Tick += async (_, _) =>
        {
            _hoverCloseTimer.Stop();
            try
            {
                if (_island.IsExpanded && !_openedByShortcut && !IsCursorInsideExpandedNotch() && !IsTextBoxFocused() && DateTime.UtcNow >= _hoverOpenCommitUntilUtc)
                {
                    await BeginSmoothCollapseAsync();
                }
            }
            catch { }
        };

        _hoverTrackTimer.Interval = TimeSpan.FromMilliseconds(75);
        _hoverTrackTimer.Tick += (_, _) => TrackExpandedHoverState();

        MusicPanel.Bind(_island.Music, _island.System, _island, _island.Lyrics);
        LyricsPanel.Bind(_island.Lyrics, _island.Music, _settings);
        LyricsPanel.TextInputRequested += (_, tb) => FocusTextInput(tb);
        ClipboardPanel.Bind(_island.Clipboard);
        NotificationsPanel.Bind(_island.Notifications, _settings);
        TimerPanel.Bind(_island.Timer);
        SystemPanel.Bind(_island.System);
        ActivityPanel.Bind(_island.Activity);
        RootHost.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnRootHostPointerPressed), handledEventsToo: true);

        _island.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderState);
        _island.Music.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() => RenderMusicSummary(e.PropertyName));
        _island.Timer.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderBadges);
        _island.Notifications.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderBadges);
        _settings.SettingsChanged += (_, _) => DispatcherQueue.TryEnqueue(ApplySettingsAndReposition);
        _themeManager.ArtworkPaletteUpdated += (_, _) => DispatcherQueue.TryEnqueue(ApplySettingsAndReposition);
        NotchAssembly.SizeChanged += (_, _) => RepositionForActualShellSize();

        ConfigureWindowChrome();
        ApplySettingsAndReposition();
        RenderState();
    }

    public void PrepareForShutdown()
    {
        _allowShutdownClose = true;
        UninstallOutsideClickHook();
    }

    public void SetSessionLocked(bool isLocked)
    {
        _isSessionLocked = isLocked;
        _lastEnergyRunning = null;
        _lastIdleBreathing = null;
        if (!isLocked)
        {
            _lastHostWidth = 0;
            _lastHostHeight = 0;
            _positioner.ResetBoundsCache();
        }
        ApplySettingsAndReposition();
    }

    public void EnsureStealthVisibleAndTopmost()
    {
        if (_isSessionLocked || _hwnd == IntPtr.Zero || !_settings.Settings.General.IslandEnabled)
        {
            return;
        }

        try
        {
            if (!Win32Interop.IsWindowVisible(_hwnd))
            {
                _lastIslandShown = null;
                _lastHostWidth = 0;
                _positioner.ResetBoundsCache();
                ApplySettingsAndReposition();
            }
            else if (_settings.Settings.General.AlwaysOnTop)
            {
                _positioner.EnsureTopmost();
            }

            if (!_island.IsExpanded && !_isRetracting)
            {
                _animator.RestoreShellImmediate(NotchAssembly);
            }
        }
        catch { }
    }

    public async Task HandleShortcutAsync(ShortcutAction action)
    {
        var targetMode = action switch
        {
            ShortcutAction.OpenMusic => IslandMode.Expanded,
            ShortcutAction.OpenLyrics => IslandMode.Lyrics,
            ShortcutAction.OpenClipboard => IslandMode.Clipboard,
            ShortcutAction.OpenTimer => IslandMode.Timer,
            ShortcutAction.ToggleIsland => _island.Music.HasActiveMedia ? IslandMode.Expanded : IslandMode.System,
            _ => IslandMode.Expanded
        };

        var shouldCollapse = _island.IsExpanded && (
            action == ShortcutAction.ToggleIsland ||
            _activeShortcutAction == action ||
            _island.Mode == targetMode);

        if (shouldCollapse)
        {
            await BeginSmoothCollapseAsync(force: true);
            return;
        }

        _openedByShortcut = true;
        _activeShortcutAction = action;
        _hoverTrackTimer.Stop();
        _hoverCloseTimer.Stop();
        _autoCollapseTimer.Stop();
        _hoverOpenCommitUntilUtc = DateTime.MinValue;
        _island.SetMode(targetMode);
        InstallOutsideClickHook();
    }

    public void UpdateSpectrum(ReadOnlySpan<float> bands)
    {
        CompactEqualizer.UpdateSpectrum(bands);
        MusicPanel.UpdateSpectrum(bands);
    }

    public void UpdateClock()
    {
        if (_island.Mode == IslandMode.Expanded)
        {
            MusicPanel.UpdateClock();
        }
    }

    public void PulseNotification() =>
        _animator.PulseNotificationArrival(NotchAssembly, _settings.Settings.Animation);

    private void ConfigureWindowChrome()
    {
        _hwnd = WindowNative.GetWindowHandle(this);
        _positioner.AttachWindow(_hwnd);

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        try
        {
            appWindow.IsShownInSwitchers = false;
        }
        catch
        {
            // WS_EX_TOOLWINDOW hides the window from switchers even if SetShownInSwitchers is unsupported.
        }

        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
            presenter.IsResizable = false;
            presenter.IsAlwaysOnTop = _settings.Settings.General.AlwaysOnTop;
        }

        var style = Win32Interop.GetWindowLongPtr(_hwnd, Win32Interop.GWL_STYLE).ToInt64();
        style &= ~(Win32Interop.WS_CAPTION | Win32Interop.WS_THICKFRAME | Win32Interop.WS_MINIMIZEBOX | Win32Interop.WS_MAXIMIZEBOX | Win32Interop.WS_SYSMENU);
        style |= Win32Interop.WS_POPUP;
        Win32Interop.SetWindowLongPtr(_hwnd, Win32Interop.GWL_STYLE, new IntPtr(style));

        var exStyle = Win32Interop.GetWindowLongPtr(_hwnd, Win32Interop.GWL_EXSTYLE).ToInt64();
        exStyle |= Win32Interop.WS_EX_LAYERED | Win32Interop.WS_EX_TOOLWINDOW | Win32Interop.WS_EX_NOACTIVATE;
        Win32Interop.SetWindowLongPtr(_hwnd, Win32Interop.GWL_EXSTYLE, new IntPtr(exStyle));
        _lastNoActivate = true;

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

    private void SetWindowNoActivate(bool noActivate)
    {
        if (_hwnd == IntPtr.Zero || _lastNoActivate == noActivate)
        {
            return;
        }

        _lastNoActivate = noActivate;
        var exStyle = Win32Interop.GetWindowLongPtr(_hwnd, Win32Interop.GWL_EXSTYLE).ToInt64();
        exStyle = noActivate
            ? (exStyle | Win32Interop.WS_EX_NOACTIVATE)
            : (exStyle & ~Win32Interop.WS_EX_NOACTIVATE);
        Win32Interop.SetWindowLongPtr(_hwnd, Win32Interop.GWL_EXSTYLE, new IntPtr(exStyle));
        Win32Interop.SetWindowPos(
            _hwnd,
            Win32Interop.HWND_TOPMOST,
            0, 0, 0, 0,
            Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_NOZORDER | Win32Interop.SWP_NOACTIVATE | Win32Interop.SWP_FRAMECHANGED);
    }

    public void FocusTextInput(TextBox input)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        SetWindowNoActivate(noActivate: false);
        _hoverTrackTimer.Stop();
        _hoverCloseTimer.Stop();
        _autoCollapseTimer.Stop();

        Win32Interop.SetForegroundWindow(_hwnd);
        input.Focus(FocusState.Programmatic);
        input.SelectAll();

        DispatcherQueue.TryEnqueue(() =>
        {
            if (_hwnd != IntPtr.Zero && input.Visibility == Visibility.Visible)
            {
                Win32Interop.SetForegroundWindow(_hwnd);
                input.Focus(FocusState.Programmatic);
            }
        });
    }

    private void OnRootHostPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (FindAncestorTextBox(e.OriginalSource as DependencyObject) is { } tb)
        {
            SetWindowNoActivate(noActivate: false);
            Win32Interop.SetForegroundWindow(_hwnd);
            tb.Focus(FocusState.Pointer);
        }
    }

    private bool IsCursorOverTextBox()
    {
        if (_hwnd == IntPtr.Zero || RootHost.XamlRoot is null || !Win32Interop.GetCursorPos(out var pt))
        {
            return false;
        }

        if (!Win32Interop.ScreenToClient(_hwnd, ref pt))
        {
            return false;
        }

        var scale = RootHost.XamlRoot.RasterizationScale;
        if (scale <= 0)
        {
            scale = Math.Max(96u, Win32Interop.GetDpiForWindow(_hwnd)) / 96.0;
        }

        var dipPoint = new Windows.Foundation.Point(pt.X / scale, pt.Y / scale);
        foreach (var element in VisualTreeHelper.FindElementsInHostCoordinates(dipPoint, RootHost, includeAllElements: true))
        {
            if (FindAncestorTextBox(element) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private static TextBox? FindAncestorTextBox(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is TextBox tb)
            {
                return tb;
            }
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private IntPtr CustomWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == 0x0010 /* WM_CLOSE */ && !_allowShutdownClose)
            {
                return IntPtr.Zero;
            }

            if (msg == Win32Interop.WM_MOUSEACTIVATE)
            {
                if (_island.IsExpanded && (IsCursorOverTextBox() || IsTextBoxFocused()))
                {
                    return new IntPtr(Win32Interop.MA_ACTIVATE);
                }
                return new IntPtr(Win32Interop.MA_NOACTIVATE);
            }

            if (msg is 0x007E /* WM_DISPLAYCHANGE */ or 0x02E0 /* WM_DPICHANGED */)
            {
                _lastHostWidth = 0;
                _lastHostHeight = 0;
                _positioner.ResetBoundsCache();
                DispatcherQueue.TryEnqueue(ApplySettingsAndReposition);
            }

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
                var hitHeight = NotchAssembly.ActualHeight > 0 ? NotchAssembly.ActualHeight : (stealth ? 10 : 40);
                var left = (RootHost.ActualWidth - hitWidth) / 2.0;

                if (dipX < left || dipX > left + hitWidth || dipY < 0 || dipY > hitHeight)
                {
                    return Win32Interop.HTTRANSPARENT;
                }
            }
        }
        catch
        {
            // Never allow unhandled WndProc exceptions to crash the CoreMessaging dispatcher
        }

        return Win32Interop.CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    public void ApplySettingsAndReposition()
    {
        var s = _settings.Settings;
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
            IslandMode.Compact => isStealthCompact ? 10 : s.Appearance.IslandHeight,
            IslandMode.Music => Math.Max(40, s.Appearance.IslandHeight + 4),
            IslandMode.Expanded => Math.Max(180, actualHeight),
            _ => Math.Max(220, actualHeight)
        };

        PillShell.Width = targetWidth;
        PillShell.Height = isStealthCompact ? 10 : double.NaN;

        var showEars = !isStealthCompact && s.General.StealthTopBar;
        LeftNotchEar.Visibility = showEars ? Visibility.Visible : Visibility.Collapsed;
        RightNotchEar.Visibility = showEars ? Visibility.Visible : Visibility.Collapsed;

        var radius = _island.IsExpanded
            ? Math.Clamp(s.Appearance.CornerRadius + 2, 22, 32)
            : Math.Max(18, s.Appearance.CornerRadius);

        var palette = _themeManager.ResolveIslandPalette(s.Appearance);
        StealthLineStartStop.Color = Color.FromArgb(40, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B);
        StealthLineMidStop.Color = Color.FromArgb(240, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B);
        StealthLineMid2Stop.Color = Color.FromArgb(240, palette.SecondaryAccent.R, palette.SecondaryAccent.G, palette.SecondaryAccent.B);
        StealthLineEndStop.Color = Color.FromArgb(40, palette.SecondaryAccent.R, palette.SecondaryAccent.G, palette.SecondaryAccent.B);

        if (isStealthCompact)
        {
            DoomAmbientGlow.Visibility = Visibility.Collapsed;
            DoomEnergySweep.Visibility = Visibility.Collapsed;
            GlassSheenLayer.Visibility = Visibility.Collapsed;
            if (_lastEnergyRunning != false)
            {
                _animator.StopDoomEnergyAnimations(DoomAmbientGlow, DoomEnergySweep);
                _lastEnergyRunning = false;
            }
            PillShell.CornerRadius = new CornerRadius(0, 0, 10, 10);
            PillShell.BorderThickness = new Thickness(0);
            PillShell.Background = new SolidColorBrush(Color.FromArgb(2, 0, 0, 0));
        }
        else
        {
            AmbientStop0.Color = Color.FromArgb(palette.AmbientTopAlpha, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B);
            AmbientStop1.Color = Color.FromArgb(palette.AmbientMidAlpha, palette.SecondaryAccent.R, palette.SecondaryAccent.G, palette.SecondaryAccent.B);
            GlassSheenTopStop.Color = Color.FromArgb(palette.GlossySheenAlpha, 255, 255, 255);
            GlassSheenMidStop.Color = Color.FromArgb((byte)(palette.GlossySheenAlpha / 4), 255, 255, 255);
            EnergySweepStartStop.Color = Color.FromArgb(0, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B);
            EnergySweepMidStop.Color = Color.FromArgb(200, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B);
            EnergySweepMid2Stop.Color = Color.FromArgb(200, palette.SecondaryAccent.R, palette.SecondaryAccent.G, palette.SecondaryAccent.B);
            EnergySweepEndStop.Color = Color.FromArgb(0, palette.SecondaryAccent.R, palette.SecondaryAccent.G, palette.SecondaryAccent.B);

            DoomAmbientGlow.Visibility = s.Animation.GlowPulse ? Visibility.Visible : Visibility.Collapsed;
            DoomEnergySweep.Visibility = s.Animation.EnergySweep ? Visibility.Visible : Visibility.Collapsed;
            GlassSheenLayer.Visibility = s.Animation.ShowGlossySheen ? Visibility.Visible : Visibility.Collapsed;

            var wantEnergy = !_isSessionLocked && (s.Animation.GlowPulse || s.Animation.EnergySweep);
            if (wantEnergy)
            {
                if (_lastEnergyRunning != true || Math.Abs(_lastEnergyWidth - targetWidth) > 2.0)
                {
                    _animator.StartDoomEnergyAnimations(DoomAmbientGlow, DoomEnergySweep, targetWidth, s.Animation.ReduceMotion);
                    _lastEnergyRunning = true;
                    _lastEnergyWidth = targetWidth;
                }
            }
            else if (_lastEnergyRunning != false)
            {
                _animator.StopDoomEnergyAnimations(DoomAmbientGlow, DoomEnergySweep);
                _lastEnergyRunning = false;
            }

            PillShell.CornerRadius = s.General.StealthTopBar
                ? new CornerRadius(0, 0, radius, radius)
                : new CornerRadius(radius);
            PillShell.BorderThickness = s.General.StealthTopBar
                ? new Thickness(1, 0, 1, 1)
                : new Thickness(1);
            PillShell.Background = new SolidColorBrush(palette.SurfaceColor);
            PillShell.BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(palette.BorderAlpha, palette.PrimaryAccent.R, palette.PrimaryAccent.G, palette.PrimaryAccent.B), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb((byte)(palette.BorderAlpha * 0.85), palette.SecondaryAccent.R, palette.SecondaryAccent.G, palette.SecondaryAccent.B), Offset = 1 }
                }
            };
        }

        CompactView.Height = s.Appearance.IslandHeight;
        MusicPillView.Height = Math.Max(40, s.Appearance.IslandHeight + 4);

        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = s.General.AlwaysOnTop;
        }

        if (_lastIslandShown != s.General.IslandEnabled)
        {
            _lastIslandShown = s.General.IslandEnabled;
            if (s.General.IslandEnabled)
            {
                appWindow.Show(false);
            }
            else
            {
                appWindow.Hide();
            }
        }

        var earAllowance = showEars ? 24 : 0;
        var hostWidth = (int)Math.Ceiling(targetWidth + earAllowance + 24);
        var hostHeight = (int)Math.Ceiling(targetHeight + (isStealthCompact ? 0 : 18));
        if (_lastHostWidth != hostWidth || _lastHostHeight != hostHeight || _lastFlushTop != s.General.StealthTopBar)
        {
            _lastHostWidth = hostWidth;
            _lastHostHeight = hostHeight;
            _lastFlushTop = s.General.StealthTopBar;
            _positioner.ApplyIslandPosition(s.Position, hostWidth, hostHeight, flushTopEdge: s.General.StealthTopBar);
        }

        var wantIdleBreathe = isStealthCompact && !_isSessionLocked && s.Animation.IdleBreathing && !s.Animation.ReduceMotion;
        if (_lastIdleBreathing != wantIdleBreathe)
        {
            _lastIdleBreathing = wantIdleBreathe;
            _animator.SetIdleBreathing(IdleBreatheBar, wantIdleBreathe, s.Animation.ReduceMotion);
        }
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
        SetWindowNoActivate(!_island.IsExpanded);
        HighlightActiveTab();

        if (_island.IsExpanded)
        {
            if (_island.Mode == IslandMode.Expanded)
            {
                MusicPanel.UpdateClock();
            }
            NotchAssembly.UpdateLayout();
            RepositionForActualShellSize();
            if (!_wasExpanded)
            {
                _hoverOpenCommitUntilUtc = _openedByShortcut
                    ? DateTime.MinValue
                    : DateTime.UtcNow.AddMilliseconds(480);
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

            if (_openedByShortcut)
            {
                _hoverTrackTimer.Stop();
                _hoverCloseTimer.Stop();
                _autoCollapseTimer.Stop();
                InstallOutsideClickHook();
            }
            else
            {
                UninstallOutsideClickHook();
                _hoverTrackTimer.Start();
                RestartAutoCollapseTimer();
            }
        }
        else
        {
            _openedByShortcut = false;
            _activeShortcutAction = null;
            UninstallOutsideClickHook();
            _hoverTrackTimer.Stop();
            _hoverCloseTimer.Stop();
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

        var wasRetracting = _isRetracting;
        CancelPendingRetract();

        if (_island.IsExpanded)
        {
            if (wasRetracting)
            {
                _animator.RestoreShellImmediate(NotchAssembly);
            }
            return;
        }

        if (_settings.Settings.General.HoverToOpen)
        {
            _openedByShortcut = false;
            _activeShortcutAction = null;
            UninstallOutsideClickHook();
            _hoverOpenCommitUntilUtc = DateTime.UtcNow.AddMilliseconds(480);
            _island.SetMode(IslandMode.Expanded);
            _hoverTrackTimer.Start();
        }
        else if (!_settings.Settings.General.StealthTopBar)
        {
            _animator.AnimateHoverScale(NotchAssembly, true, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);
        }
    }

    private async Task BeginSmoothCollapseAsync(bool force = false)
    {
        if (!_island.IsExpanded || _isRetracting)
        {
            return;
        }

        _openedByShortcut = false;
        _activeShortcutAction = null;
        UninstallOutsideClickHook();
        _hoverTrackTimer.Stop();
        _hoverCloseTimer.Stop();
        _autoCollapseTimer.Stop();

        CancelPendingRetract();
        _isRetracting = true;
        _retractCts = new CancellationTokenSource();
        try
        {
            var completed = await _animator.AnimateVenomRetractAsync(
                NotchAssembly,
                NotchAssembly.ActualWidth > 0 ? NotchAssembly.ActualWidth : PillShell.Width,
                _settings.Settings.Animation,
                _settings.Settings.Appearance.AnimationSpeed,
                _retractCts.Token);

            if (completed && (force || (!_island.IsHovered && !IsCursorInsideExpandedNotch())))
            {
                _island.Collapse(_island.Music.HasActiveMedia, _settings.Settings.General.StealthTopBar);
            }
        }
        finally
        {
            _isRetracting = false;
        }
    }

    private void CancelPendingRetract()
    {
        _isRetracting = false;
        if (_retractCts is not null)
        {
            _retractCts.Cancel();
            _retractCts.Dispose();
            _retractCts = null;
        }
    }

    private void OnPillPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_island.IsExpanded)
        {
            if (DateTime.UtcNow >= _hoverOpenCommitUntilUtc && !IsCursorInsideExpandedNotch())
            {
                _island.IsHovered = false;
            }
            return;
        }

        _island.IsHovered = false;
        if (!_settings.Settings.General.StealthTopBar)
        {
            _animator.AnimateHoverScale(NotchAssembly, false, _settings.Settings.Animation, _settings.Settings.Appearance.AnimationSpeed);
        }
    }

    private void TrackExpandedHoverState()
    {
        if (!_island.IsExpanded || _openedByShortcut)
        {
            _hoverTrackTimer.Stop();
            _hoverCloseTimer.Stop();
            return;
        }

        if (DateTime.UtcNow < _hoverOpenCommitUntilUtc)
        {
            _island.IsHovered = true;
            _hoverCloseTimer.Stop();
            return;
        }

        if (IsCursorInsideExpandedNotch() || IsTextBoxFocused())
        {
            _island.IsHovered = true;
            _hoverCloseTimer.Stop();
            _autoCollapseTimer.Stop();
            return;
        }

        _island.IsHovered = false;
        if (_settings.Settings.General.CollapseBehavior == CollapseBehavior.MouseLeave || _settings.Settings.General.HoverToOpen)
        {
            if (!_hoverCloseTimer.IsEnabled && !_isRetracting)
            {
                _hoverCloseTimer.Start();
            }
        }
        else
        {
            _hoverTrackTimer.Stop();
            RestartAutoCollapseTimer();
        }
    }

    private bool IsCursorInsideExpandedNotch() =>
        Win32Interop.GetCursorPos(out var pt) && IsScreenPointInsideNotch(pt.X, pt.Y, marginPx: 18);

    private bool IsScreenPointInsideNotch(int screenX, int screenY, int marginPx)
    {
        if (_hwnd == IntPtr.Zero || !Win32Interop.GetWindowRect(_hwnd, out var winRect))
        {
            return false;
        }

        var scale = Math.Max(96u, Win32Interop.GetDpiForWindow(_hwnd)) / 96.0;
        var notchWidthPx = (NotchAssembly.ActualWidth > 0 ? NotchAssembly.ActualWidth : PillShell.Width) * scale;
        var notchHeightPx = (NotchAssembly.ActualHeight > 0 ? NotchAssembly.ActualHeight : PillShell.ActualHeight) * scale;

        var centerX = (winRect.Left + winRect.Right) / 2.0;
        var left = centerX - (notchWidthPx / 2.0) - marginPx;
        var right = centerX + (notchWidthPx / 2.0) + marginPx;
        var top = winRect.Top;
        var bottom = winRect.Top + Math.Max(40.0, notchHeightPx) + marginPx;

        return screenX >= left && screenX <= right && screenY >= top && screenY <= bottom;
    }

    private void InstallOutsideClickHook()
    {
        if (_outsideMouseHook != IntPtr.Zero)
        {
            return;
        }

        _outsideMouseHookProc ??= OutsideMouseHookCallback;
        _outsideMouseHook = SetWindowsHookExW(WH_MOUSE_LL, _outsideMouseHookProc, IntPtr.Zero, 0);
    }

    private void UninstallOutsideClickHook()
    {
        if (_outsideMouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_outsideMouseHook);
            _outsideMouseHook = IntPtr.Zero;
        }
    }

    private IntPtr OutsideMouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (nCode >= 0 && _openedByShortcut && _island.IsExpanded)
            {
                var msg = wParam.ToInt32();
                if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN)
                {
                    var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    if (!IsScreenPointInsideNotch(hookStruct.pt.X, hookStruct.pt.Y, marginPx: 4))
                    {
                        DispatcherQueue.TryEnqueue(() => _ = BeginSmoothCollapseAsync(force: true));
                    }
                }
            }
        }
        catch
        {
            // Never interfere with system mouse input routing
        }

        return CallNextHookEx(_outsideMouseHook, nCode, wParam, lParam);
    }

    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_MBUTTONDOWN = 0x0207;

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public Win32Interop.POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private bool IsTextBoxFocused()
    {
        return RootHost.XamlRoot is not null &&
               FocusManager.GetFocusedElement(RootHost.XamlRoot) is TextBox;
    }

    private void OnCompactTapped(object sender, TappedRoutedEventArgs e)
    {
        _island.SetMode(_island.Music.HasActiveMedia ? IslandMode.Expanded : IslandMode.System);
    }

    private async void OnPillDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        try
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
        catch { }
    }

    private async void OnCompactPlayPauseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _island.Music.TogglePlayPauseAsync();
        }
        catch { }
    }

    private void OnTabClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<IslandMode>(tag, out var mode))
        {
            if (_openedByShortcut)
            {
                _activeShortcutAction = mode switch
                {
                    IslandMode.Expanded => ShortcutAction.OpenMusic,
                    IslandMode.Lyrics => ShortcutAction.OpenLyrics,
                    IslandMode.Clipboard => ShortcutAction.OpenClipboard,
                    IslandMode.Timer => ShortcutAction.OpenTimer,
                    _ => ShortcutAction.ToggleIsland
                };
            }
            _island.SetMode(mode);
        }
    }

    private async void OnCollapseClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _hoverOpenCommitUntilUtc = DateTime.MinValue;
            _hoverCloseTimer.Stop();
            _hoverTrackTimer.Stop();
            _island.IsHovered = false;
            await BeginSmoothCollapseAsync(force: true);
        }
        catch { }
    }

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) =>
        OpenSettingsRequested?.Invoke(this, EventArgs.Empty);
}
