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

    private sealed class EnumOption<T>(T value, string label) where T : struct, Enum
    {
        public T Value { get; } = value;
        public string Label { get; } = label;
        public override string ToString() => Label;
    }

    private static readonly SectionMeta[] Sections =
    [
        new(SettingsSectionId.General, "General", "Stealth top bar, hover open, startup, and collapse behavior", ["stealth", "hover", "startup", "top", "collapse", "double"]),
        new(SettingsSectionId.Appearance, "Appearance", "Dark/Light/System theme, accent color, font, dimensions & live preview", ["theme", "accent", "font", "width", "height", "radius", "opacity", "black"]),
        new(SettingsSectionId.Animation, "Animation", "Composition spring presets, speed, hover scale, bounce & breathing", ["animation", "spring", "fluid", "snappy", "silk", "motion"]),
        new(SettingsSectionId.Music, "Music", "Album artwork, WASAPI equalizer, progress hairline & title scrolling", ["music", "equalizer", "artwork", "progress"]),
        new(SettingsSectionId.BrowserIntegration, "Browser Integration", "Automatic Chrome/Edge/Opera media + optional MV3 Extension", ["browser", "chrome", "edge", "opera", "extension", "native", "download", "github"]),
        new(SettingsSectionId.Lyrics, "Lyrics", "Synchronized LRCLIB lookup, scrolling & fullscreen backdrop", ["lyrics", "lrclib", "sync", "scroll", "fullscreen"]),
        new(SettingsSectionId.Clipboard, "Clipboard", "History retention, sensitive exclusions & excluded apps", ["clipboard", "history", "sensitive", "excluded"]),
        new(SettingsSectionId.Notifications, "Notifications", "Windows system notification routing, previews & Focus Assist sync", ["notifications", "alerts", "dnd", "focus", "system"]),
        new(SettingsSectionId.Widgets, "Widgets", "Battery, volume, brightness, Wi-Fi, audio devices, CPU & RAM", ["widgets", "battery", "volume", "cpu", "ram"]),
        new(SettingsSectionId.Shortcuts, "Shortcuts", "Global Windows keyboard bindings", ["shortcuts", "hotkeys", "alt", "ctrl"]),
        new(SettingsSectionId.Privacy, "Privacy", "Local-only processing, private browsing pause & lock screen masking", ["privacy", "local", "lock", "logs"]),
        new(SettingsSectionId.Storage, "Storage", "Real SQLite & disk cache footprint, cleanup, JSON export & reset", ["storage", "cache", "sqlite", "export", "reset"]),
        new(SettingsSectionId.About, "About", "Version, publisher, WinUI 3 + Chrome/Edge/Opera media & stack details", ["about", "version", "publisher", "arcsubash", "winui"])
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
    private readonly Services.ThemeManager _themeManager;
    private readonly DispatcherTimer _saveDebounceTimer = new();
    private readonly List<Action> _comboResyncActions = [];
    private Border? _appearancePreviewHost;
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
        IClipboardRepository clipboardRepo,
        Services.ThemeManager themeManager)
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
        _themeManager = themeManager;
        void OnArtworkPaletteUpdated(object? s, EventArgs e) => DispatcherQueue.TryEnqueue(RefreshAppearancePreview);
        _themeManager.ArtworkPaletteUpdated += OnArtworkPaletteUpdated;
        Closed += (_, _) =>
        {
            _saveDebounceTimer.Stop();
            _themeManager.ArtworkPaletteUpdated -= OnArtworkPaletteUpdated;
        };

        _saveDebounceTimer.Interval = TimeSpan.FromMilliseconds(180);
        _saveDebounceTimer.Tick += async (_, _) =>
        {
            _saveDebounceTimer.Stop();
            await _settingsVm.SaveAsync();
        };

        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        try
        {
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "DOOMIsland-Logo.png");
            if (File.Exists(logoPath))
            {
                TitleBarLogo.Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath));
            }
        }
        catch { }

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        Services.WindowIconHelper.TryApply(hwnd, appWindow);
        appWindow.Resize(new SizeInt32(1020, 680));

        foreach (var sec in Sections)
        {
            CompactSectionCombo.Items.Add(sec);
        }

        SizeChanged += OnWindowSizeChanged;
        Activated += (_, _) => ResyncVisibleComboBoxesDeferred();
        UpdateResponsiveLayout(Bounds.Width);
        RebuildSidebar();
        RenderActiveSection();
    }

    public void NavigateTo(SettingsSectionId section)
    {
        _settingsVm.ActiveSection = section;
        RebuildSidebar();
        RenderActiveSection();
        ResyncVisibleComboBoxesDeferred();
    }

    private void ResyncVisibleComboBoxesDeferred()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var resync in _comboResyncActions)
            {
                resync();
            }

            if (CompactSectionCombo.Items.Count > 0)
            {
                _syncingCompactNav = true;
                try
                {
                    var idx = Array.FindIndex(Sections, s => s.Id == _settingsVm.ActiveSection);
                    if (idx >= 0)
                    {
                        CompactSectionCombo.SelectedIndex = -1;
                        CompactSectionCombo.SelectedIndex = idx;
                    }
                }
                finally
                {
                    _syncingCompactNav = false;
                }
            }
        });
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
        _comboResyncActions.Clear();
        _appearancePreviewHost = null;
        var s = _settingsVm.Settings;

        switch (meta.Id)
        {
            case SettingsSectionId.General:
                AddToggleRow("Stealth top-edge invisible bar", "Hide the idle island as a sleek glowing bar along the top screen edge.", s.General.StealthTopBar, v =>
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
                AddInfoBanner("Themes include GlossyPink, Venom, SpiderMan, DrDoom, CyberViolet, and ArtworkDynamic (auto-colors the island from live media cover art).");
                AddButtonRow("Reset Appearance", "Restore default colors, typography, and pill dimensions.", "Reset", async () => { await _settingsVm.ResetAppearanceAsync(); RenderActiveSection(); });
                AddEnumRow("Application theme", Enum.GetValues<ColorMode>(), s.Appearance.ColorMode, v => { s.Appearance.ColorMode = v; RefreshAppearancePreview(); });
                AddEnumRow("Island material & theme", Enum.GetValues<ThemePreset>(), s.Appearance.Theme, v => { s.Appearance.Theme = v; RefreshAppearancePreview(); });
                AddAccentSwatches(s.Appearance);
                AddEnumRow("Font family", Enum.GetValues<FontFamilyOption>(), s.Appearance.FontFamily, v => s.Appearance.FontFamily = v);
                AddSliderRow("Compact island width (px)", 140, 300, s.Appearance.IslandWidth, v => { s.Appearance.IslandWidth = v; RefreshAppearancePreview(); });
                AddSliderRow("Compact island height (px)", 32, 48, s.Appearance.IslandHeight, v => { s.Appearance.IslandHeight = v; RefreshAppearancePreview(); });
                AddSliderRow("Corner radius (px)", 14, 32, s.Appearance.CornerRadius, v => { s.Appearance.CornerRadius = v; RefreshAppearancePreview(); });
                AddSliderRow("Surface opacity (%)", 75, 100, s.Appearance.Opacity, v => { s.Appearance.Opacity = v; RefreshAppearancePreview(); });
                AddSliderRow("Animation speed (x10)", 5, 18, s.Appearance.AnimationSpeed * 10, v => s.Appearance.AnimationSpeed = Math.Round(v / 10.0, 1));
                break;

            case SettingsSectionId.Animation:
                AddButtonRow("Test Spring Morph", "Toggle the live floating island between collapsed and expanded.", "Test Morph", () => { _islandVm.ToggleExpand(_islandVm.Music.HasActiveMedia, s.General.StealthTopBar); return Task.CompletedTask; });
                AddEnumRow("Physics preset", Enum.GetValues<AnimationPreset>(), s.Animation.Preset, v => s.Animation.Preset = v);
                AddToggleRow("Reduce motion", "Replace spring motion with minimal transitions.", s.Animation.ReduceMotion, v => s.Animation.ReduceMotion = v);
                AddToggleRow("Hover scale", "Subtly scale compact pill on hover.", s.Animation.HoverExpand, v => s.Animation.HoverExpand = v);
                AddToggleRow("Notification arrival bounce", "Spring settle pulse when new notifications arrive.", s.Animation.NotificationBounce, v => s.Animation.NotificationBounce = v);
                AddToggleRow("Idle ambient breathing", "Display subtle breathing bar while idle.", s.Animation.IdleBreathing, v => s.Animation.IdleBreathing = v);
                AddToggleRow("Ambient aura glow & pulse", "Animate the dynamic ambient glow inside the island shell.", s.Animation.GlowPulse, v => s.Animation.GlowPulse = v);
                AddToggleRow("Bottom energy sweep beam", "Animate the theme-colored rim light sweep across the notch bottom.", s.Animation.EnergySweep, v => s.Animation.EnergySweep = v);
                AddToggleRow("Glossy top glass sheen", "Render specular top glass reflection across the notch.", s.Animation.ShowGlossySheen, v => s.Animation.ShowGlossySheen = v);
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
                SectionContentHost.Children.Add(CreateAboutLinkRow(
                    title: "Browser Extension (Optional)",
                    subtitle: "Media already syncs automatically. Install DOOMIsland-BrowserExtension-MV3.zip to add multi-tab switching, exact seeking, and Focus Tab.",
                    glyph: "\uE896",
                    linkText: "Get on GitHub Releases",
                    uri: new Uri("https://github.com/iamsubuu/DOOM-Island/releases")));
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
                AddAboutCard();
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
        var options = values.Select(v => new EnumOption<T>(v, v.ToString())).ToList();
        var activeValue = current;
        var isSyncing = false;

        var combo = new ComboBox
        {
            Width = StandardControlWidth,
            HorizontalAlignment = HorizontalAlignment.Right,
            Foreground = (Brush)Application.Current.Resources["WindowTextPrimaryBrush"],
            ItemsSource = options,
            DisplayMemberPath = nameof(EnumOption<T>.Label)
        };

        void SyncSelection(bool forceRefresh)
        {
            var idx = options.FindIndex(o => EqualityComparer<T>.Default.Equals(o.Value, activeValue));
            if (idx < 0 && options.Count > 0) idx = 0;

            isSyncing = true;
            try
            {
                if (forceRefresh)
                {
                    combo.SelectedIndex = -1;
                }
                combo.SelectedIndex = idx;
            }
            finally
            {
                isSyncing = false;
            }
        }

        SyncSelection(forceRefresh: false);

        combo.Loaded += (_, _) => combo.DispatcherQueue.TryEnqueue(() => SyncSelection(forceRefresh: true));
        _comboResyncActions.Add(() => SyncSelection(forceRefresh: true));

        combo.SelectionChanged += async (_, _) =>
        {
            if (isSyncing) return;
            if (combo.SelectedItem is EnumOption<T> selected)
            {
                activeValue = selected.Value;
                onChanged(selected.Value);
                await _settingsVm.SaveAsync();
            }
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
        string[] hexes = ["#2ee58b", "#a855f7", "#22d3ee", "#ff4da6", "#ef4444", "#3b82f6", "#f59e0b"];
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
        _appearancePreviewHost = new Border
        {
            MinHeight = 250,
            CornerRadius = new CornerRadius(14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            BorderThickness = new Thickness(1)
        };
        SectionContentHost.Children.Add(_appearancePreviewHost);
        RefreshAppearancePreview();
    }

    private void RefreshAppearancePreview()
    {
        if (_appearancePreviewHost is null || _settingsVm.ActiveSection != SettingsSectionId.Appearance)
        {
            return;
        }

        var app = _settingsVm.Settings.Appearance;
        var anim = _settingsVm.Settings.Animation;
        var palette = _themeManager.ResolveIslandPalette(app);
        var previewRadius = Math.Clamp(app.CornerRadius + 2, 18, 32);
        var stealthFlush = _settingsVm.Settings.General.StealthTopBar;
        var surfaceColor = palette.SurfaceColor;
        var primary = palette.PrimaryAccent;
        var secondary = palette.SecondaryAccent;

        var notchCorner = stealthFlush
            ? new CornerRadius(0, 0, previewRadius, previewRadius)
            : new CornerRadius(previewRadius);

        var notchBody = new Grid { MaxWidth = 588 };

        // Live theme ambient glow, glossy sheen & bottom sweep layers matching DynamicIslandWindow
        if (anim.GlowPulse)
        {
            notchBody.Children.Add(new Border
            {
                CornerRadius = notchCorner,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(palette.AmbientTopAlpha, primary.R, primary.G, primary.B), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(palette.AmbientMidAlpha, secondary.R, secondary.G, secondary.B), Offset = 0.52 },
                        new GradientStop { Color = Color.FromArgb(0, 0, 0, 0), Offset = 1 }
                    }
                }
            });
        }

        if (anim.ShowGlossySheen)
        {
            notchBody.Children.Add(new Border
            {
                CornerRadius = notchCorner,
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(0, 1),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(palette.GlossySheenAlpha, 255, 255, 255), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb((byte)(palette.GlossySheenAlpha / 4), 255, 255, 255), Offset = 0.28 },
                        new GradientStop { Color = Color.FromArgb(0, 255, 255, 255), Offset = 1 }
                    }
                }
            });
        }

        var contentStack = new StackPanel
        {
            Padding = new Thickness(16, 10, 16, 12),
            Spacing = 10
        };

        // Row 0: Actual Top Tab Bar (Notch, Tray, Lyrics, Clipboard, Alerts, Timer, System) + icons
        var topBar = new Grid();
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tabsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var tabLabel in new[] { "Notch", "Tray", "Lyrics", "Clipboard", "Alerts", "Timer", "System" })
        {
            var isActive = tabLabel == "Notch";
            tabsRow.Children.Add(new Border
            {
                Padding = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(7),
                Background = isActive
                    ? new SolidColorBrush(Color.FromArgb(64, primary.R, primary.G, primary.B))
                    : new SolidColorBrush(Colors.Transparent),
                Child = new TextBlock
                {
                    Text = tabLabel,
                    FontSize = 11,
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Medium,
                    Foreground = new SolidColorBrush(isActive ? Colors.White : Color.FromArgb(180, 200, 208, 220))
                }
            });
        }
        Grid.SetColumn(tabsRow, 0);
        topBar.Children.Add(tabsRow);

        var topIcons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        topIcons.Children.Add(new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE713", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(185, 220, 228, 240)) });
        topIcons.Children.Add(new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE70E", FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(185, 220, 228, 240)) });
        Grid.SetColumn(topIcons, 1);
        topBar.Children.Add(topIcons);
        contentStack.Children.Add(topBar);

        // Row 1: 3-Column Actual Notch Layout (Track + Calendar/Lyrics + Equalizer/Volume)
        var session = _islandVm.Music.CurrentSession;
        var mainCols = new Grid { ColumnSpacing = 12 };
        mainCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        mainCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        mainCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(116) });

        // Column 0: Artwork + Title/Artist + Transport Controls
        var mediaGrid = new Grid { ColumnSpacing = 10 };
        mediaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
        mediaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var artHost = new Grid { Width = 58, Height = 58, VerticalAlignment = VerticalAlignment.Center };
        var artFrame = new Border
        {
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(100, primary.R, primary.G, primary.B)),
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(200, primary.R, primary.G, primary.B), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(210, secondary.R, secondary.G, secondary.B), Offset = 1 }
                }
            }
        };

        if (!string.IsNullOrWhiteSpace(session?.ArtworkUrl) && Uri.TryCreate(session.ArtworkUrl, UriKind.Absolute, out var artUri))
        {
            artFrame.Child = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(artUri)
            };
        }
        else
        {
            artFrame.Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\uE8D6",
                FontSize = 20,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
        artHost.Children.Add(artFrame);
        artHost.Children.Add(new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, -2, -2),
            Background = new SolidColorBrush(Color.FromArgb(230, 16, 19, 28)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = "\uE8D6",
                FontSize = 9,
                Foreground = new SolidColorBrush(primary)
            }
        });
        Grid.SetColumn(artHost, 0);
        mediaGrid.Children.Add(artHost);

        var trackMeta = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        trackMeta.Children.Add(new TextBlock
        {
            Text = session is not null && !string.IsNullOrWhiteSpace(session.Title) ? session.Title : "DOOM Island Live Notch",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        trackMeta.Children.Add(new TextBlock
        {
            Text = session is not null && !string.IsNullOrWhiteSpace(session.Artist) ? session.Artist : $"{app.Theme} · Theme Preview",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromArgb(210, 200, 210, 224)),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        trackMeta.Children.Add(new TextBlock
        {
            Text = $"{app.Theme} · {app.Opacity:0}% · R{app.CornerRadius:0}",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromArgb(165, 175, 185, 205)),
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        var transportRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        transportRow.Children.Add(new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE892", FontSize = 10, Foreground = new SolidColorBrush(Colors.White) });
        transportRow.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Colors.White),
            Child = new FontIcon
            {
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                Glyph = _islandVm.Music.IsPlaying ? "\uE769" : "\uE768",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 9, 9, 11))
            }
        });
        transportRow.Children.Add(new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE893", FontSize = 10, Foreground = new SolidColorBrush(Colors.White) });
        transportRow.Children.Add(new Border
        {
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255)),
            Child = new TextBlock { Text = "Focus", FontSize = 9.5, Foreground = new SolidColorBrush(Color.FromArgb(215, 225, 232, 240)) }
        });
        trackMeta.Children.Add(transportRow);
        Grid.SetColumn(trackMeta, 1);
        mediaGrid.Children.Add(trackMeta);
        Grid.SetColumn(mediaGrid, 0);
        mainCols.Children.Add(mediaGrid);

        // Column 1: Live Date/Week Strip + Lyric Glance Card
        var centerBorder = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(44, primary.R, primary.G, primary.B)),
            BorderThickness = new Thickness(1, 0, 1, 0),
            Padding = new Thickness(10, 2, 10, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        var centerStack = new StackPanel { Spacing = 5 };

        var calHeader = new Grid();
        calHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        calHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var now = DateTime.Now;
        var today = now.Date;
        calHeader.Children.Add(new TextBlock
        {
            Text = today.ToString("MMM d"),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center
        });
        var timeLbl = new TextBlock
        {
            Text = now.ToString("hh:mm:ss tt"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(primary),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(timeLbl, 1);
        calHeader.Children.Add(timeLbl);
        centerStack.Children.Add(calHeader);

        var calRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, VerticalAlignment = VerticalAlignment.Center };
        var startDay = today.AddDays(-2);
        for (var d = 0; d < 5; d++)
        {
            var dt = startDay.AddDays(d);
            var isToday = dt.Date == today;
            var dayCol = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
            dayCol.Children.Add(new TextBlock
            {
                Text = dt.ToString("ddd")[..2],
                FontSize = 8.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(isToday ? secondary : Color.FromArgb(165, 190, 200, 215))
            });
            dayCol.Children.Add(new TextBlock
            {
                Text = dt.Day.ToString("00"),
                FontSize = 10,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(isToday ? primary : Color.FromArgb(215, 225, 232, 240))
            });
            calRow.Children.Add(dayCol);
        }
        centerStack.Children.Add(calRow);

        var lyricSnippet = _islandVm.Lyrics.ActiveLineIndex >= 0 && _islandVm.Lyrics.ActiveLineIndex < _islandVm.Lyrics.Lines.Count
            ? _islandVm.Lyrics.Lines[_islandVm.Lyrics.ActiveLineIndex].Text
            : null;
        centerStack.Children.Add(new Border
        {
            Padding = new Thickness(8, 5, 8, 5),
            CornerRadius = new CornerRadius(9),
            Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(52, primary.R, primary.G, primary.B)),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    new TextBlock
                    {
                        Text = !string.IsNullOrWhiteSpace(lyricSnippet) ? lyricSnippet : "DOOM Notch Ready",
                        FontSize = 10,
                        FontWeight = FontWeights.Medium,
                        Foreground = new SolidColorBrush(Colors.White),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    },
                    new TextBlock
                    {
                        Text = "Lrclib · Live synced lyrics",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromArgb(165, 180, 190, 210)),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                }
            }
        });
        centerBorder.Child = centerStack;
        Grid.SetColumn(centerBorder, 1);
        mainCols.Children.Add(centerBorder);

        // Column 2: Equalizer + Volume Bar + Live Lyrics Button
        var rightStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 6 };
        var eqCard = new Border
        {
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, primary.R, primary.G, primary.B)),
            BorderThickness = new Thickness(1)
        };
        var eqCardStack = new StackPanel { Spacing = 6 };
        var barsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2.5 };
        var barHeights = new[] { 6.0, 12.0, 9.0, 14.0, 7.0, 11.0, 5.0 };
        for (var i = 0; i < barHeights.Length; i++)
        {
            barsPanel.Children.Add(new Border
            {
                Width = 3,
                Height = barHeights[i],
                VerticalAlignment = VerticalAlignment.Bottom,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(i % 2 == 0 ? primary : secondary)
            });
        }
        eqCardStack.Children.Add(barsPanel);

        var volRow = new Grid { ColumnSpacing = 6 };
        volRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        volRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        volRow.Children.Add(new FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE767", FontSize = 10, Foreground = new SolidColorBrush(Colors.White) });
        var volTrack = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center };
        volTrack.Children.Add(new Border { Height = 3, CornerRadius = new CornerRadius(1.5), Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255)) });
        volTrack.Children.Add(new Border { Width = 34, Height = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(primary) });
        volTrack.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(30, 0, 0, 0), Background = new SolidColorBrush(secondary) });
        Grid.SetColumn(volTrack, 1);
        volRow.Children.Add(volTrack);
        eqCardStack.Children.Add(volRow);

        eqCard.Child = eqCardStack;
        rightStack.Children.Add(eqCard);
        rightStack.Children.Add(new Border
        {
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(7),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            Child = new TextBlock { Text = "Live Lyrics", FontSize = 9.5, HorizontalAlignment = HorizontalAlignment.Center, Foreground = new SolidColorBrush(Colors.White) }
        });
        Grid.SetColumn(rightStack, 2);
        mainCols.Children.Add(rightStack);
        contentStack.Children.Add(mainCols);

        // Row 2: Scrubber Progress Bar
        var progressGrid = new Grid { ColumnSpacing = 8 };
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        progressGrid.Children.Add(new TextBlock
        {
            Text = session is not null ? _islandVm.Music.FormattedPosition : "2:28",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromArgb(190, 210, 220, 232))
        });

        var pct = session is not null && session.DurationSeconds > 0
            ? Math.Clamp(_islandVm.Music.ProgressPercentage / 100.0, 0.08, 0.96)
            : 0.62;
        var seekBar = new Grid { VerticalAlignment = VerticalAlignment.Center, Height = 12 };
        seekBar.Children.Add(new Border
        {
            Height = 3,
            CornerRadius = new CornerRadius(1.5),
            Background = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255))
        });
        var fillCols = new Grid();
        fillCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(pct, GridUnitType.Star) });
        fillCols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0 - pct, GridUnitType.Star) });
        var activeTrack = new Border
        {
            Height = 3.5,
            CornerRadius = new CornerRadius(1.75),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 0),
                GradientStops =
                {
                    new GradientStop { Color = primary, Offset = 0 },
                    new GradientStop { Color = secondary, Offset = 1 }
                }
            }
        };
        Grid.SetColumn(activeTrack, 0);
        fillCols.Children.Add(activeTrack);
        fillCols.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(5),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, -5, 0),
            Background = new SolidColorBrush(secondary)
        });
        seekBar.Children.Add(fillCols);
        Grid.SetColumn(seekBar, 1);
        progressGrid.Children.Add(seekBar);

        var durText = new TextBlock
        {
            Text = session is not null ? _islandVm.Music.FormattedDuration : "3:42",
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromArgb(190, 210, 220, 232))
        };
        Grid.SetColumn(durText, 2);
        progressGrid.Children.Add(durText);
        contentStack.Children.Add(progressGrid);

        notchBody.Children.Add(contentStack);

        if (anim.EnergySweep)
        {
            notchBody.Children.Add(new Border
            {
                Width = 200,
                Height = 2,
                CornerRadius = new CornerRadius(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 2),
                Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0, 0),
                    EndPoint = new Windows.Foundation.Point(1, 0),
                    GradientStops =
                    {
                        new GradientStop { Color = Color.FromArgb(0, primary.R, primary.G, primary.B), Offset = 0 },
                        new GradientStop { Color = Color.FromArgb(220, primary.R, primary.G, primary.B), Offset = 0.35 },
                        new GradientStop { Color = Color.FromArgb(220, secondary.R, secondary.G, secondary.B), Offset = 0.65 },
                        new GradientStop { Color = Color.FromArgb(0, secondary.R, secondary.G, secondary.B), Offset = 1 }
                    }
                }
            });
        }

        var shell = new Border
        {
            CornerRadius = notchCorner,
            Background = new SolidColorBrush(surfaceColor),
            BorderBrush = new LinearGradientBrush
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                EndPoint = new Windows.Foundation.Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Color.FromArgb(palette.BorderAlpha, primary.R, primary.G, primary.B), Offset = 0 },
                    new GradientStop { Color = Color.FromArgb(palette.BorderAlpha, secondary.R, secondary.G, secondary.B), Offset = 1 }
                }
            },
            BorderThickness = stealthFlush ? new Thickness(1, 0, 1, 1) : new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = stealthFlush ? VerticalAlignment.Top : VerticalAlignment.Center,
            Child = notchBody
        };

        var assembly = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = stealthFlush ? VerticalAlignment.Top : VerticalAlignment.Center
        };

        if (stealthFlush)
        {
            assembly.Children.Add(CreateNotchEarPath(isLeft: true, surfaceColor));
        }
        assembly.Children.Add(shell);
        if (stealthFlush)
        {
            assembly.Children.Add(CreateNotchEarPath(isLeft: false, surfaceColor));
        }

        _appearancePreviewHost.Padding = stealthFlush ? new Thickness(12, 0, 12, 16) : new Thickness(12, 14, 12, 14);
        _appearancePreviewHost.BorderBrush = new SolidColorBrush(Color.FromArgb(85, primary.R, primary.G, primary.B));
        _appearancePreviewHost.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop { Color = Color.FromArgb(255, (byte)(primary.R / 20 + 6), (byte)(primary.G / 20 + 8), (byte)(primary.B / 20 + 12)), Offset = 0 },
                new GradientStop { Color = Color.FromArgb(255, (byte)(secondary.R / 18 + 8), (byte)(secondary.G / 18 + 10), (byte)(secondary.B / 18 + 14)), Offset = 1 }
            }
        };
        _appearancePreviewHost.Child = assembly;
    }

    private static Microsoft.UI.Xaml.Shapes.Path CreateNotchEarPath(bool isLeft, Color fillColor)
    {
        var figure = isLeft
            ? new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                IsClosed = true,
                Segments =
                {
                    new LineSegment { Point = new Windows.Foundation.Point(12, 0) },
                    new LineSegment { Point = new Windows.Foundation.Point(12, 12) },
                    new ArcSegment
                    {
                        Point = new Windows.Foundation.Point(0, 0),
                        Size = new Windows.Foundation.Size(12, 12),
                        SweepDirection = SweepDirection.Counterclockwise
                    }
                }
            }
            : new PathFigure
            {
                StartPoint = new Windows.Foundation.Point(0, 0),
                IsClosed = true,
                Segments =
                {
                    new LineSegment { Point = new Windows.Foundation.Point(12, 0) },
                    new ArcSegment
                    {
                        Point = new Windows.Foundation.Point(0, 12),
                        Size = new Windows.Foundation.Size(12, 12),
                        SweepDirection = SweepDirection.Counterclockwise
                    }
                }
            };

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return new Microsoft.UI.Xaml.Shapes.Path
        {
            Width = 12,
            Height = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Fill = new SolidColorBrush(fillColor),
            Data = geometry
        };
    }

    private void AddAboutCard()
    {
        var stack = new StackPanel { Spacing = 10 };

        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        try
        {
            var logoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "DOOMIsland-Logo.png");
            if (File.Exists(logoPath))
            {
                headerRow.Children.Add(new Image
                {
                    Width = 44,
                    Height = 44,
                    Stretch = Stretch.Uniform,
                    Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri(logoPath))
                });
            }
        }
        catch { }

        var titleCol = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        titleCol.Children.Add(new TextBlock
        {
            Text = $"{AppConstants.DefaultAppName} · v{AppConstants.AppVersion}",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["WindowTextPrimaryBrush"]
        });
        titleCol.Children.Add(new TextBlock
        {
            Text = $"Publisher: {AppConstants.PublisherName}",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["NovaAccentBrush"]
        });
        headerRow.Children.Add(titleCol);
        stack.Children.Add(headerRow);

        stack.Children.Add(new TextBlock
        {
            Text = "Native WinUI 3 (.NET 10 / Windows App SDK 2.7) Dynamic Island with Chrome/Edge/Opera Media Session sync, Windows System Action Center notifications, LRCLIB synced lyrics, clipboard vault, and local SQLite persistence.",
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["WindowTextSecondaryBrush"]
        });

        SectionContentHost.Children.Add(new Border
        {
            Padding = new Thickness(18, 16, 18, 16),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["WindowCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["WindowBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        });

        SectionContentHost.Children.Add(CreateAboutLinkRow(
            title: "Open Source & Updates",
            subtitle: "Check for new releases or inspect the DOOM Island repository on GitHub.",
            glyph: "\uE8A7",
            linkText: "github.com/iamsubuu/DOOM-Island",
            uri: new Uri("https://github.com/iamsubuu/DOOM-Island")));

        SectionContentHost.Children.Add(CreateAboutLinkRow(
            title: "Contact Developer",
            subtitle: "Direct email support for feedback, feature requests, and bug reports.",
            glyph: "\uE715",
            linkText: "lynchkiller0337@gmail.com",
            uri: new Uri("mailto:lynchkiller0337@gmail.com")));
    }

    private static Border CreateAboutLinkRow(string title, string subtitle, string glyph, string linkText, Uri uri)
    {
        var row = new Grid { ColumnSpacing = 14 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["WindowTextPrimaryBrush"]
        });
        left.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Brush)Application.Current.Resources["WindowTextSecondaryBrush"]
        });
        Grid.SetColumn(left, 0);
        row.Children.Add(left);

        var linkContent = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        linkContent.Children.Add(new FontIcon
        {
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            Glyph = glyph,
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["NovaAccentBrush"]
        });
        linkContent.Children.Add(new TextBlock
        {
            Text = linkText,
            FontSize = 12.5,
            FontWeight = FontWeights.Medium,
            Foreground = (Brush)Application.Current.Resources["NovaAccentBrush"]
        });

        var linkBtn = new HyperlinkButton
        {
            NavigateUri = uri,
            Padding = new Thickness(12, 6, 12, 6),
            CornerRadius = new CornerRadius(8),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Content = linkContent
        };
        Grid.SetColumn(linkBtn, 1);
        row.Children.Add(linkBtn);

        return new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["WindowCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["WindowBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = row
        };
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
