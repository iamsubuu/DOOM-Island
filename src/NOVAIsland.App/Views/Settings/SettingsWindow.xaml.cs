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
        new(SettingsSectionId.General, "General", "Stealth top bar, hover open, startup, and collapse behavior", ["stealth", "hover", "startup", "top", "collapse", "double"]),
        new(SettingsSectionId.Appearance, "Appearance", "Dark/Light/System theme, accent color, font, dimensions & live preview", ["theme", "accent", "font", "width", "height", "radius", "opacity", "black"]),
        new(SettingsSectionId.Animation, "Animation", "Composition spring presets, speed, hover scale, bounce & breathing", ["animation", "spring", "fluid", "snappy", "silk", "motion"]),
        new(SettingsSectionId.Music, "Music", "Album artwork, WASAPI equalizer, progress hairline & title scrolling", ["music", "equalizer", "artwork", "progress"]),
        new(SettingsSectionId.BrowserIntegration, "Browser Integration", "Automatic Chrome/Edge/Opera media + optional MV3 Extension", ["browser", "chrome", "edge", "opera", "extension", "native"]),
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
        _themeManager.ArtworkPaletteUpdated += (_, _) => DispatcherQueue.TryEnqueue(RefreshAppearancePreview);

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
            Height = 128,
            CornerRadius = new CornerRadius(12),
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
        var palette = _themeManager.ResolveIslandPalette(app);
        var previewRadius = Math.Clamp(app.CornerRadius, 14, 32);
        var stealthFlush = _settingsVm.Settings.General.StealthTopBar;
        var surfaceColor = palette.SurfaceColor;
        var primary = palette.PrimaryAccent;
        var secondary = palette.SecondaryAccent;

        var notchBody = new Grid
        {
            Width = Math.Clamp(app.IslandWidth + 68, 250, 360),
            Height = Math.Clamp(app.IslandHeight + 20, 54, 74)
        };

        // Top ambient dual gradient + glossy sheen + bottom energy sweep matching live DynamicIslandWindow
        notchBody.Children.Add(new Border
        {
            CornerRadius = stealthFlush ? new CornerRadius(0, 0, previewRadius, previewRadius) : new CornerRadius(previewRadius),
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

        notchBody.Children.Add(new Border
        {
            CornerRadius = stealthFlush ? new CornerRadius(0, 0, previewRadius, previewRadius) : new CornerRadius(previewRadius),
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

        var inner = new Grid { Padding = new Thickness(16, 8, 16, 10) };
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var statusStack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        statusStack.Children.Add(new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 8,
            Height = 8,
            Fill = new SolidColorBrush(primary)
        });
        statusStack.Children.Add(new Border
        {
            Width = 24,
            Height = 3,
            CornerRadius = new CornerRadius(2),
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
        });
        Grid.SetColumn(statusStack, 0);
        inner.Children.Add(statusStack);

        var centerTitle = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0) };
        centerTitle.Children.Add(new TextBlock
        {
            Text = $"{app.Theme} · Notch Preview",
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        centerTitle.Children.Add(new TextBlock
        {
            Text = $"{app.Opacity:0}% · R{app.CornerRadius:0} · {app.IslandWidth:0}×{app.IslandHeight:0}",
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 210, 220, 230))
        });
        Grid.SetColumn(centerTitle, 1);
        inner.Children.Add(centerTitle);

        var barsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var barIdx = 0;
        foreach (var h in new[] { 7.0, 13.0, 10.0, 15.0, 8.0 })
        {
            barsPanel.Children.Add(new Border
            {
                Width = 3,
                Height = h,
                CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(barIdx % 2 == 0 ? primary : secondary)
            });
            barIdx++;
        }
        Grid.SetColumn(barsPanel, 2);
        inner.Children.Add(barsPanel);

        notchBody.Children.Add(inner);
        notchBody.Children.Add(new Border
        {
            Width = 136,
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

        var shell = new Border
        {
            CornerRadius = stealthFlush ? new CornerRadius(0, 0, previewRadius, previewRadius) : new CornerRadius(previewRadius),
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

        _appearancePreviewHost.Padding = stealthFlush ? new Thickness(0, 0, 0, 12) : new Thickness(0);
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

        var linksPanel = new StackPanel { Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        linksPanel.Children.Add(new TextBlock
        {
            Text = "Developer Profiles & Contact",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.Resources["WindowTextPrimaryBrush"]
        });
        linksPanel.Children.Add(new HyperlinkButton
        {
            Content = "1. github.com/iamsubuu",
            NavigateUri = new Uri("https://github.com/iamsubuu"),
            Padding = new Thickness(0, 2, 0, 2)
        });
        linksPanel.Children.Add(new HyperlinkButton
        {
            Content = "2. github.com/ArcSubash",
            NavigateUri = new Uri("https://github.com/ArcSubash"),
            Padding = new Thickness(0, 2, 0, 2)
        });
        linksPanel.Children.Add(new HyperlinkButton
        {
            Content = "Contact dev : lynchkiller0337@gmail.com",
            NavigateUri = new Uri("mailto:lynchkiller0337@gmail.com"),
            Padding = new Thickness(0, 2, 0, 2)
        });
        stack.Children.Add(linksPanel);

        SectionContentHost.Children.Add(new Border
        {
            Padding = new Thickness(16, 14, 16, 14),
            CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Background = (Brush)Application.Current.Resources["WindowCardBrush"],
            BorderBrush = (Brush)Application.Current.Resources["WindowBorderBrush"],
            BorderThickness = new Thickness(1),
            Child = stack
        });
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
