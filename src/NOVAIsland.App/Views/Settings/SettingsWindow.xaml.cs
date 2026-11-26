using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.ViewModels;
using NOVAIsland.Infrastructure.Caching;
using Windows.Graphics;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace NOVAIsland.App.Views.Settings;

public sealed partial class SettingsWindow : Window
{
    private sealed record SectionMeta(SettingsSectionId Id, string Label, string Subtitle, string[] Keywords)
    {
        public override string ToString() => Label;
    }

    private static readonly SectionMeta[] Sections =
    [
        new(SettingsSectionId.General, "General", "Brand name, stealth top bar, hover open, startup, and collapse behavior", ["name", "stealth", "hover", "startup", "top", "collapse", "double"]),
        new(SettingsSectionId.Appearance, "Appearance", "Dark/Light/System theme, accent color, font, dimensions & live preview", ["theme", "accent", "font", "width", "height", "radius", "opacity", "black"]),
        new(SettingsSectionId.Position, "Position & Display", "Top center, left, right, custom X/Y, monitor & interactive canvas", ["position", "monitor", "custom", "x", "y"]),
        new(SettingsSectionId.Animation, "Animation", "Composition spring presets, speed, hover scale, bounce & breathing", ["animation", "spring", "fluid", "snappy", "silk", "motion"]),
        new(SettingsSectionId.Music, "Music", "Album artwork, WASAPI equalizer, progress hairline & title scrolling", ["music", "equalizer", "artwork", "progress"]),
        new(SettingsSectionId.BrowserIntegration, "Browser Integration", "Automatic Chrome/Edge/Opera media + optional MV3 Extension", ["browser", "chrome", "edge", "opera", "extension", "native"]),
        new(SettingsSectionId.Lyrics, "Lyrics", "Synchronized LRCLIB lookup, scrolling & fullscreen backdrop", ["lyrics", "lrclib", "sync", "scroll", "fullscreen"]),
        new(SettingsSectionId.Clipboard, "Clipboard", "History retention, sensitive exclusions & excluded apps", ["clipboard", "history", "sensitive", "excluded"]),
        new(SettingsSectionId.Notifications, "Notifications", "Windows notification access status, previews & Focus Assist sync", ["notifications", "alerts", "dnd", "focus"]),
        new(SettingsSectionId.Widgets, "Widgets", "Battery, volume, brightness, Wi-Fi, audio devices, CPU & RAM", ["widgets", "battery", "volume", "cpu", "ram"]),
        new(SettingsSectionId.Shortcuts, "Shortcuts", "Global Windows keyboard bindings", ["shortcuts", "hotkeys", "alt", "ctrl"]),
        new(SettingsSectionId.Privacy, "Privacy", "Local-only processing, private browsing pause & lock screen masking", ["privacy", "local", "lock", "logs"]),
        new(SettingsSectionId.Storage, "Storage", "Real SQLite & disk cache footprint, cleanup, JSON export & reset", ["storage", "cache", "sqlite", "export", "reset"]),
        new(SettingsSectionId.About, "About", "Version, WinUI 3 + Chrome/Edge/Opera media & stack details", ["about", "version", "winui"])
    ];

    private const double CompactLayoutThreshold = 760.0;
    private const double StandardControlWidth = 220.0;

    private readonly SettingsViewModel _settingsVm;
    private readonly IslandViewModel _islandVm;
    private readonly ISettingsStore _settingsStore;
    private readonly IStartupService _startup;
    private readonly IHotkeyService _hotkeys;
    private readonly IWindowPositioningService _positioner;
    private readonly StorageFootprintService _footprint;
    private readonly ILyricsCacheRepository _lyricsCache;
    private readonly IClipboardRepository _clipboardRepo;
    private readonly DispatcherTimer _saveDebounceTimer = new();
    private bool _syncingCompactNav;
    private bool _isCompactWidth;

    public SettingsWindow(
        SettingsViewModel settingsVm,
        IslandViewModel islandVm,
        ISettingsStore settingsStore,
        IStartupService startup,
        IHotkeyService hotkeys,
        IWindowPositioningService positioner,
        StorageFootprintService footprint,
        ILyricsCacheRepository lyricsCache,
        IClipboardRepository clipboardRepo)
    {
        InitializeComponent();
        _settingsVm = settingsVm;
        _islandVm = islandVm;
        _settingsStore = settingsStore;
        _startup = startup;
        _hotkeys = hotkeys;
        _positioner = positioner;
        _footprint = footprint;
        _lyricsCache = lyricsCache;
        _clipboardRepo = clipboardRepo;

        _saveDebounceTimer.Interval = TimeSpan.FromMilliseconds(180);
        _saveDebounceTimer.Tick += async (_, _) =>
        {
            _saveDebounceTimer.Stop();
            await _settingsVm.SaveAsync();
        };

        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        AppWindow.GetFromWindowId(windowId).Resize(new SizeInt32(1020, 680));

        foreach (var sec in Sections)
        {
            CompactSectionCombo.Items.Add(sec);
        }

        SizeChanged += OnWindowSizeChanged;
        UpdateResponsiveLayout(Bounds.Width);
        RebuildSidebar();
        RenderActiveSection();
    }

    public void NavigateTo(SettingsSectionId section)
    {
        _settingsVm.ActiveSection = section;
        RebuildSidebar();
        RenderActiveSection();
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args)
    {
        UpdateResponsiveLayout(args.Size.Width);
    }

    private void UpdateResponsiveLayout(double width)
    {
        var compact = width > 0 && width < CompactLayoutThreshold;
        SidebarColumn.Width = compact ? new GridLength(0) : new GridLength(236);
        SidebarContainer.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactNavBar.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;

        if (_isCompactWidth != compact)
        {
            _isCompactWidth = compact;
            RenderActiveSection();
        }
    }

    private void OnCompactSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCompactNav) return;
        if (CompactSectionCombo.SelectedItem is SectionMeta meta)
        {
            NavigateTo(meta.Id);
        }
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e) => RebuildSidebar();

    private void RebuildSidebar()
    {
        SidebarNavPanel.Children.Clear();
        var q = SidebarSearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(q)
            ? Sections
            : Sections.Where(s =>
                s.Label.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Subtitle.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                s.Keywords.Any(k => k.Contains(q, StringComparison.OrdinalIgnoreCase))).ToArray();

        foreach (var sec in filtered)
        {
            var active = sec.Id == _settingsVm.ActiveSection;
            var btn = new Button
            {
                Content = sec.Label,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 8, 12, 8),
                CornerRadius = new CornerRadius(8),
                FontSize = 13,
                FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                BorderThickness = new Thickness(0),
                Background = active
                    ? (Brush)Application.Current.Resources["WindowCardHoverBrush"]
                    : new SolidColorBrush(Colors.Transparent)
            };
            var id = sec.Id;
            btn.Click += (_, _) => NavigateTo(id);
            SidebarNavPanel.Children.Add(btn);
        }

        _syncingCompactNav = true;
        try
        {
            CompactSectionCombo.SelectedItem = Sections.FirstOrDefault(s => s.Id == _settingsVm.ActiveSection);
        }
        finally
        {
            _syncingCompactNav = false;
        }
    }

    private void ScheduleSave()
    {
        _saveDebounceTimer.Stop();
        _saveDebounceTimer.Start();
    }

    private async void RenderActiveSection()
    {
        var meta = Sections.First(s => s.Id == _settingsVm.ActiveSection);
        Title = $"{_settingsVm.ResolvedAppName} Settings";
        TitleBarBrandText.Text = $"{_settingsVm.ResolvedAppName} Settings";
        TitleBarSectionText.Text = meta.Label;
        SectionTitleText.Text = meta.Label;
        SectionSubtitleText.Text = meta.Subtitle;

        SectionContentHost.Children.Clear();
        var s = _settingsVm.Settings;

        switch (meta.Id)
        {
            case SettingsSectionId.General:
                AddTextRow("Application display name", "Configurable brand name propagated across windows and tray.", s.General.AppName, v => s.General.AppName = v);
                AddTextRow("Compact pill wordmark", "Short label shown when stealth mode is off.", s.General.AppShortName, v => s.General.AppShortName = v);
                AddToggleRow("Stealth top-edge invisible bar", "Hide the idle island as an invisible bar along the top screen edge.", s.General.StealthTopBar, v =>
                {
                    s.General.StealthTopBar = v;
                    _islandVm.Collapse(_islandVm.Music.HasActiveMedia, v);
                });
                AddToggleRow("Hover cursor to open island", "Automatically open the island when hovering the top bar.", s.General.HoverToOpen, v => s.General.HoverToOpen = v);
                AddToggleRow("Launch on Windows startup", "Register under current user Run key to start with Windows.", s.General.LaunchOnStartup, v =>
                {
                    s.General.LaunchOnStartup = v;
                    _startup.SetStartupEnabled(v);
                });
                AddToggleRow("Always on top", "Keep the floating island above active desktop windows.", s.General.AlwaysOnTop, v => s.General.AlwaysOnTop = v);
                AddSliderRow("Auto-collapse duration (seconds, 0 = off)", 0, 30, s.General.AutoCollapseSeconds, v => s.General.AutoCollapseSeconds = (int)v);
                AddEnumRow("Collapse behavior", Enum.GetValues<CollapseBehavior>(), s.General.CollapseBehavior, v => s.General.CollapseBehavior = v);
                AddEnumRow("Double-click action", Enum.GetValues<DoubleClickAction>(), s.General.DoubleClickAction, v => s.General.DoubleClickAction = v);
                break;

            case SettingsSectionId.Appearance:
                AddAppearancePreview(s.Appearance);
                AddButtonRow("Reset Appearance", "Restore default colors, typography, and pill dimensions.", "Reset", async () => { await _settingsVm.ResetAppearanceAsync(); RenderActiveSection(); });
                AddEnumRow("Application theme", Enum.GetValues<ColorMode>(), s.Appearance.ColorMode, v => s.Appearance.ColorMode = v);
                AddEnumRow("Island material preset", Enum.GetValues<ThemePreset>(), s.Appearance.Theme, v => s.Appearance.Theme = v);
                AddAccentSwatches(s.Appearance);
                AddEnumRow("Font family", Enum.GetValues<FontFamilyOption>(), s.Appearance.FontFamily, v => s.Appearance.FontFamily = v);
                AddSliderRow("Compact island width (px)", 140, 300, s.Appearance.IslandWidth, v => s.Appearance.IslandWidth = v);
                AddSliderRow("Compact island height (px)", 32, 48, s.Appearance.IslandHeight, v => s.Appearance.IslandHeight = v);
                AddSliderRow("Corner radius (px)", 14, 32, s.Appearance.CornerRadius, v => s.Appearance.CornerRadius = v);
                AddSliderRow("Surface opacity (%)", 80, 100, s.Appearance.Opacity, v => s.Appearance.Opacity = v);
                AddSliderRow("Animation speed (x10)", 5, 18, s.Appearance.AnimationSpeed * 10, v => s.Appearance.AnimationSpeed = Math.Round(v / 10.0, 1));
                break;

            case SettingsSectionId.Position:
                AddPositionPreview(s.Position);
                AddButtonRow("Reset Position", "Return island to Top Center.", "Reset", async () => { await _settingsVm.ResetPositionAsync(); RenderActiveSection(); });
                AddEnumRow("Screen position preset", Enum.GetValues<ScreenPosition>(), s.Position.ScreenPosition, v => s.Position.ScreenPosition = v);
                AddSliderRow("Custom horizontal X (%)", 12, 88, s.Position.CustomX, v => { s.Position.ScreenPosition = ScreenPosition.Custom; s.Position.CustomX = v; });
                AddSliderRow("Custom vertical Y (px)", 0, 140, s.Position.CustomY, v => { s.Position.ScreenPosition = ScreenPosition.Custom; s.Position.CustomY = v; });
                AddMonitorSelector(s.Position);
                AddToggleRow("Remember last position", "Persist custom X/Y across launches.", s.Position.RememberLastPosition, v => s.Position.RememberLastPosition = v);
                break;

            case SettingsSectionId.Animation:
                AddButtonRow("Test Spring Morph", "Toggle the live floating island between collapsed and expanded.", "Test Morph", () => { _islandVm.ToggleExpand(_islandVm.Music.HasActiveMedia, s.General.StealthTopBar); return Task.CompletedTask; });
                AddEnumRow("Physics preset", Enum.GetValues<AnimationPreset>(), s.Animation.Preset, v => s.Animation.Preset = v);
                AddToggleRow("Reduce motion", "Replace spring motion with minimal transitions.", s.Animation.ReduceMotion, v => s.Animation.ReduceMotion = v);
                AddToggleRow("Hover scale", "Subtly scale compact pill on hover.", s.Animation.HoverExpand, v => s.Animation.HoverExpand = v);
                AddToggleRow("Notification arrival bounce", "Spring settle pulse when new notifications arrive.", s.Animation.NotificationBounce, v => s.Animation.NotificationBounce = v);
                AddToggleRow("Idle ambient breathing", "Display subtle breathing bar while idle.", s.Animation.IdleBreathing, v => s.Animation.IdleBreathing = v);
                break;

            case SettingsSectionId.Music:
                AddToggleRow("Show album artwork in compact pill", null, s.Music.ShowAlbumArtInCompact, v => s.Music.ShowAlbumArtInCompact = v);
                AddToggleRow("Audio-reactive WASAPI equalizer", null, s.Music.ShowAudioVisualizer, v => s.Music.ShowAudioVisualizer = v);
                AddToggleRow("Compact bottom progress hairline", null, s.Music.ShowCompactProgress, v => s.Music.ShowCompactProgress = v);
                AddToggleRow("Auto-expand on track change", null, s.Music.AutoExpandOnTrackChange, v => s.Music.AutoExpandOnTrackChange = v);
                break;

            case SettingsSectionId.BrowserIntegration:
                AddInfoBanner(_islandVm.Music.IsBrowserConnected
                    ? $"Connected: {_islandVm.Music.ConnectedBrowserName} ({_islandVm.Music.BrowserTabs.Count} active media session(s)). Chrome, Edge & Opera auto-connect via Windows Media Session; optional MV3 extension adds per-tab switching."
                    : "Browser media integration is paused. Enable the toggle below to connect to Chrome, Edge, and Opera.");
                AddToggleRow("Enable browser media integration", "Auto-detect Chrome, Edge, and Opera media + optional MV3 extension.", s.BrowserIntegration.Enabled, v => s.BrowserIntegration.Enabled = v);
                AddToggleRow("Auto-focus audible tab", "Prioritize actively playing browser tabs.", s.BrowserIntegration.AutoFocusAudibleTab, v => s.BrowserIntegration.AutoFocusAudibleTab = v);
                AddToggleRow("Sync artwork & metadata", null, s.BrowserIntegration.SyncArtworkAndMetadata, v => s.BrowserIntegration.SyncArtworkAndMetadata = v);
                AddToggleRow("Enable download watcher", null, s.BrowserIntegration.EnableDownloadWatcher, v => s.BrowserIntegration.EnableDownloadWatcher = v);
                break;

            case SettingsSectionId.Lyrics:
                AddToggleRow("Prefer synchronized LRC", null, s.Lyrics.PreferSynchronized, v => s.Lyrics.PreferSynchronized = v);
                AddToggleRow("Enable LRCLIB lookup", "Automatically fetch lyrics from lrclib.net (disabled in Local-Only mode).", s.Lyrics.LrclibFallback, v => s.Lyrics.LrclibFallback = v);
                AddEnumRow("Source priority", Enum.GetValues<LyricsSourcePriority>(), s.Lyrics.SourcePriority, v => s.Lyrics.SourcePriority = v);
                AddSliderRow("Lyrics font size (px)", 13, 22, s.Lyrics.FontSize, v => s.Lyrics.FontSize = v);
                AddToggleRow("Smooth auto-scroll", null, s.Lyrics.SmoothScroll, v => s.Lyrics.SmoothScroll = v);
                AddToggleRow("Blur inactive lines", null, s.Lyrics.BlurInactiveLines, v => s.Lyrics.BlurInactiveLines = v);
                AddSliderRow("Fullscreen backdrop intensity (%)", 20, 90, s.Lyrics.FullscreenBackdropOpacity, v => s.Lyrics.FullscreenBackdropOpacity = v);
                break;

            case SettingsSectionId.Clipboard:
                AddToggleRow("Enable clipboard history monitoring", null, s.Clipboard.Enabled, v => s.Clipboard.Enabled = v);
                AddEnumRow("Auto-clear unpinned items", Enum.GetValues<ClipboardAutoClear>(), s.Clipboard.AutoClear, v => s.Clipboard.AutoClear = v);
                AddSliderRow("History limit", 25, 250, s.Clipboard.HistoryLimit, v => s.Clipboard.HistoryLimit = (int)v);
                AddToggleRow("Exclude sensitive formats & tokens", null, s.Clipboard.ExcludeSensitive, v => s.Clipboard.ExcludeSensitive = v);
                AddToggleRow("Capture images & screenshots", null, s.Clipboard.CaptureImages, v => s.Clipboard.CaptureImages = v);
                AddToggleRow("Persist pinned items", null, s.Clipboard.PersistPinned, v => s.Clipboard.PersistPinned = v);
                AddExcludedAppsManager(s.Clipboard);
                break;

            case SettingsSectionId.Notifications:
                AddInfoBanner(_islandVm.Notifications.StatusMessage);
                AddToggleRow("Enable Windows notification routing", null, s.Notifications.Enabled, v => s.Notifications.Enabled = v);
                AddSliderRow("Preview duration (seconds)", 2, 15, s.Notifications.DisplayDurationSeconds, v => s.Notifications.DisplayDurationSeconds = (int)v);
                AddToggleRow("Sync with Windows DND / Focus Assist", null, s.Notifications.SyncWindowsDnd, v => s.Notifications.SyncWindowsDnd = v);
                AddToggleRow("Show message body previews", null, s.Notifications.ShowPreviews, v => s.Notifications.ShowPreviews = v);
                break;

            case SettingsSectionId.Widgets:
                AddToggleRow("Battery & AC Status", null, s.Widgets.Battery, v => s.Widgets.Battery = v);
                AddToggleRow("Master Volume Slider", null, s.Widgets.Volume, v => s.Widgets.Volume = v);
                AddToggleRow("Display Brightness (WMI / DDC-CI)", null, s.Widgets.Brightness, v => s.Widgets.Brightness = v);
                AddToggleRow("Wi-Fi Network", null, s.Widgets.Wifi, v => s.Widgets.Wifi = v);
                AddToggleRow("Audio Output Devices", null, s.Widgets.AudioDevices, v => s.Widgets.AudioDevices = v);
                AddToggleRow("Microphone Indicator & Mute", null, s.Widgets.Microphone, v => s.Widgets.Microphone = v);
                AddToggleRow("Camera Privacy Indicator", null, s.Widgets.Camera, v => s.Widgets.Camera = v);
                AddToggleRow("CPU & RAM Telemetry", null, s.Widgets.Cpu && s.Widgets.Ram, v => { s.Widgets.Cpu = v; s.Widgets.Ram = v; });
                break;

            case SettingsSectionId.Shortcuts:
                AddButtonRow("Reset Shortcuts", "Restore default global hotkeys.", "Reset", async () =>
                {
                    await _settingsVm.ResetShortcutsAsync();
                    _hotkeys.RegisterBindings(_settingsVm.Settings.Shortcuts);
                    RenderActiveSection();
                });
                foreach (var sc in s.Shortcuts)
                {
                    AddTextRow(sc.Label, sc.Description, sc.Keys, v =>
                    {
                        sc.Keys = v;
                        _hotkeys.RegisterBindings(s.Shortcuts);
                    });
                }
                break;

            case SettingsSectionId.Privacy:
                AddToggleRow("Local-only processing mode", "Disable outbound LRCLIB requests; keep everything strictly offline.", s.Privacy.LocalOnlyMode, v => s.Privacy.LocalOnlyMode = v);
                AddToggleRow("Pause clipboard in InPrivate/Incognito", null, s.Privacy.PauseInPrivateBrowsing, v => s.Privacy.PauseInPrivateBrowsing = v);
                AddToggleRow("Mask sensitive previews when locked", null, s.Privacy.MaskWhenLocked, v => s.Privacy.MaskWhenLocked = v);
                AddToggleRow("Local diagnostic file logging", "Write rolling diagnostic logs to AppData (takes effect next launch).", s.Privacy.DiagnosticLogs, v => s.Privacy.DiagnosticLogs = v);
                break;

            case SettingsSectionId.Storage:
                var fp = await _footprint.MeasureAsync();
                AddInfoBanner($"Local SQLite & Cache Footprint: {fp.TotalMb:0.00} MB (DB: {fp.ConfigDbMb:0.00} MB · Lyrics: {fp.LyricsCacheMb:0.00} MB · Clipboard Images: {fp.ClipboardImagesMb:0.00} MB · Artwork: {fp.AlbumCacheMb:0.00} MB)");
                AddButtonRow("Clear Lyrics Cache", "Delete cached offline LRCLIB lyrics from SQLite.", "Clear", async () => { await _lyricsCache.ClearAsync(); RenderActiveSection(); });
                AddButtonRow("Clear Clipboard Images", "Delete cached clipboard image PNGs from disk.", "Clear", () => { _footprint.ClearDirectory(_footprint.ClipboardImagesDir); RenderActiveSection(); return Task.CompletedTask; });
                AddButtonRow("Export Settings JSON", "Save current configuration to a JSON file.", "Export", async () => await ExportJsonAsync());
                AddButtonRow("Factory Reset Preferences", "Restore all settings to defaults.", "Reset All", async () => { await _settingsVm.ResetAllAsync(); RenderActiveSection(); });
                break;

            case SettingsSectionId.About:
                AddInfoBanner($"{_settingsVm.ResolvedAppName} v{AppConstants.AppVersion} · Native WinUI 3 (.NET 10 / Windows App SDK 2.7) · Chrome/Edge/Opera Auto Media + MV3 Extension + LRCLIB + SQLite.");
                break;
        }
    }

    private void AddToggleRow(string title, string? desc, bool initial, Action<bool> onChanged)
    {
        var toggle = new ToggleSwitch
        {
            Style = (Style)Application.Current.Resources["NovaToggleSwitchStyle"],
            IsOn = initial,
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(0)
        };
        toggle.Toggled += async (_, _) => { onChanged(toggle.IsOn); await _settingsVm.SaveAsync(); };
        AddSettingRow(title, desc, toggle);
    }

    private void AddSliderRow(string title, double min, double max, double initial, Action<double> onChanged)
    {
        var panel = new Grid
        {
            Width = StandardControlWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
            ColumnSpacing = 10
        };
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });

        var label = new TextBlock
        {
            Text = $"{initial:0}",
            TextAlignment = TextAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["MonoTabularTextStyle"]
        };
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = Math.Clamp(initial, min, max),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            Style = (Style)Application.Current.Resources["NovaSliderStyle"]
        };
        slider.ValueChanged += (_, e) =>
        {
            label.Text = $"{e.NewValue:0}";
            onChanged(e.NewValue);
            ScheduleSave();
        };

        Grid.SetColumn(slider, 0);
        Grid.SetColumn(label, 1);
        panel.Children.Add(slider);
        panel.Children.Add(label);
        AddSettingRow(title, null, panel);
    }

    private void AddTextRow(string title, string? desc, string initial, Action<string> onChanged)
    {
        var box = new TextBox
        {
            Text = initial,
            Width = StandardControlWidth,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        box.LostFocus += async (_, _) => { onChanged(box.Text); await _settingsVm.SaveAsync(); };
        AddSettingRow(title, desc, box);
    }

    private void AddEnumRow<T>(string title, T[] values, T current, Action<T> onChanged) where T : struct, Enum
    {
        var combo = new ComboBox
        {
            Width = StandardControlWidth,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var v in values) combo.Items.Add(v);
        combo.SelectedItem = current;
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.SelectedItem is T selected) { onChanged(selected); await _settingsVm.SaveAsync(); }
        };
        AddSettingRow(title, null, combo);
    }

    private void AddButtonRow(string title, string desc, string btnLabel, Func<Task> onClick)
    {
        var btn = new Button
        {
            Content = btnLabel,
            MinWidth = 96,
            Padding = new Thickness(14, 6, 14, 6),
            CornerRadius = new CornerRadius(8),
            HorizontalAlignment = HorizontalAlignment.Right
        };
        btn.Click += async (_, _) => await onClick();
        AddSettingRow(title, desc, btn);
    }

    private void AddSettingRow(string title, string? desc, FrameworkElement control)
    {
        var border = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["WindowDividerBrush"],
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(0, 10, 0, 12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["WindowTextPrimaryBrush"]
        });
        if (!string.IsNullOrWhiteSpace(desc))
        {
            left.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["WindowTextSecondaryBrush"]
            });
        }

        if (_isCompactWidth && control is not ToggleSwitch && control is not Button)
        {
            var stack = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
            control.HorizontalAlignment = HorizontalAlignment.Left;
            stack.Children.Add(left);
            stack.Children.Add(control);
            border.Child = stack;
        }
        else
        {
            var grid = new Grid { ColumnSpacing = 20, HorizontalAlignment = HorizontalAlignment.Stretch };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            control.VerticalAlignment = VerticalAlignment.Center;
            if (control.HorizontalAlignment == HorizontalAlignment.Stretch)
            {
                control.HorizontalAlignment = HorizontalAlignment.Right;
            }

            Grid.SetColumn(left, 0);
            Grid.SetColumn(control, 1);
            grid.Children.Add(left);
            grid.Children.Add(control);
            border.Child = grid;
        }

        SectionContentHost.Children.Add(border);
    }

    private void AddInfoBanner(string text)
    {
        SectionContentHost.Children.Add(new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["WindowCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["WindowBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["WindowTextSecondaryBrush"] }
        });
    }

    private void AddAccentSwatches(AppearanceSettings app)
    {
        string[] hexes = ["#2ee58b", "#10b981", "#34d399", "#7aa2f7", "#38bdf8", "#a78bfa", "#f59e0b"];
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var hex in hexes)
        {
            NOVAIsland.App.Services.ThemeManager.TryParseHexColor(hex, out var col);
            var selected = string.Equals(app.AccentColor, hex, StringComparison.OrdinalIgnoreCase);
            var btn = new Button
            {
                Width = 24,
                Height = 24,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(col),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = new SolidColorBrush(selected ? Colors.White : Color.FromArgb(110, 255, 255, 255))
            };
            var captured = hex;
            btn.Click += async (_, _) => { app.AccentColor = captured; await _settingsVm.SaveAsync(); RenderActiveSection(); };
            panel.Children.Add(btn);
        }
        AddSettingRow("Accent color", "Applied live across progress bars, equalizer, and switches.", panel);
    }

    private void AddAppearancePreview(AppearanceSettings app)
    {
        var pill = new Border
        {
            Width = app.IslandWidth,
            Height = app.IslandHeight,
            CornerRadius = new CornerRadius(app.CornerRadius),
            Background = new SolidColorBrush(Colors.Black),
            BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(14, 0, 14, 0),
            Child = new Grid
            {
                Children =
                {
                    new TextBlock { Text = _settingsVm.ResolvedShortName, Foreground = new SolidColorBrush(Colors.White), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 },
                    new TextBlock { Text = "Ready", Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170)), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right, FontSize = 11 }
                }
            }
        };

        SectionContentHost.Children.Add(new Border
        {
            Height = 96,
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["WindowCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["WindowBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = pill
        });
    }

    private void AddPositionPreview(PositionSettings pos)
    {
        var canvas = new Border
        {
            Height = 120,
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Color.FromArgb(255, 10, 12, 18)),
            BorderBrush = (Brush)Application.Current.Resources["WindowBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = $"Preview Anchor: {pos.ScreenPosition} (X: {pos.CustomX:0}% · Y: {pos.CustomY:0}px) — Click to position",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(12, 0, 12, 0)
            }
        };

        canvas.PointerPressed += async (_, e) =>
        {
            var pt = e.GetCurrentPoint(canvas).Position;
            pos.ScreenPosition = ScreenPosition.Custom;
            pos.CustomX = Math.Clamp(Math.Round(pt.X / Math.Max(1, canvas.ActualWidth) * 100), 12, 88);
            pos.CustomY = Math.Clamp(Math.Round(pt.Y / Math.Max(1, canvas.ActualHeight) * 140), 0, 140);
            await _settingsVm.SaveAsync();
            RenderActiveSection();
        };
        SectionContentHost.Children.Add(canvas);
    }

    private void AddMonitorSelector(PositionSettings pos)
    {
        var combo = new ComboBox
        {
            Width = StandardControlWidth,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        combo.Items.Add("Primary");
        combo.Items.Add("Follow active cursor monitor");
        foreach (var m in _positioner.GetMonitors()) combo.Items.Add(m.Id);
        combo.SelectedItem = combo.Items.Contains(pos.Monitor) ? pos.Monitor : "Primary";
        combo.SelectionChanged += async (_, _) =>
        {
            if (combo.SelectedItem is string val) { pos.Monitor = val; await _settingsVm.SaveAsync(); }
        };
        AddSettingRow("Target monitor", "Select display or follow cursor across monitors.", combo);
    }

    private void AddExcludedAppsManager(ClipboardSettings clip)
    {
        var row = new Grid
        {
            Width = 260,
            HorizontalAlignment = HorizontalAlignment.Right,
            ColumnSpacing = 8
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var input = new TextBox
        {
            PlaceholderText = "Add app (e.g. KeePass.exe)",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var addBtn = new Button { Content = "Exclude" };
        addBtn.Click += async (_, _) =>
        {
            var val = input.Text.Trim();
            if (!string.IsNullOrEmpty(val) && !clip.ExcludedApps.Contains(val, StringComparer.OrdinalIgnoreCase))
            {
                clip.ExcludedApps.Add(val);
                await _settingsVm.SaveAsync();
                RenderActiveSection();
            }
        };

        Grid.SetColumn(input, 0);
        Grid.SetColumn(addBtn, 1);
        row.Children.Add(input);
        row.Children.Add(addBtn);
        AddSettingRow("Excluded applications", string.Join(", ", clip.ExcludedApps), row);
    }

    private async Task ExportJsonAsync()
    {
        var picker = new FileSavePicker { SuggestedFileName = "doom-island-settings" };
        picker.FileTypeChoices.Add("JSON", [".json"]);
        InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));
        var file = await picker.PickSaveFileAsync();
        if (file is null) return;

        var json = await _settingsStore.ExportJsonAsync(_settingsVm.Settings);
        await File.WriteAllTextAsync(file.Path, json);
    }
}
