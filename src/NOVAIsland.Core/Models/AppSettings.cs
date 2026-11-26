using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Enums;

namespace NOVAIsland.Core.Models;

public sealed class GeneralSettings
{
    public string AppName { get; set; } = AppConstants.DefaultAppName;
    public string AppShortName { get; set; } = AppConstants.DefaultShortName;
    public bool LaunchOnStartup { get; set; }
    public bool AlwaysOnTop { get; set; } = true;
    public int AutoCollapseSeconds { get; set; }
    public CollapseBehavior CollapseBehavior { get; set; } = CollapseBehavior.OutsideClick;
    public DoubleClickAction DoubleClickAction { get; set; } = DoubleClickAction.ToggleLyrics;
    public bool IslandEnabled { get; set; } = true;
    public bool FocusDndMode { get; set; }
}

public sealed class PositionSettings
{
    public ScreenPosition ScreenPosition { get; set; } = ScreenPosition.TopCenter;
    public double CustomX { get; set; } = 50;
    public double CustomY { get; set; } = 12;
    public string Monitor { get; set; } = "Primary";
    public bool RememberLastPosition { get; set; } = true;
}

public sealed class AppearanceSettings
{
    public ColorMode ColorMode { get; set; } = ColorMode.Dark;
    public ThemePreset Theme { get; set; } = ThemePreset.PureBlack;
    public string AccentColor { get; set; } = "#7aa2f7";
    public FontFamilyOption FontFamily { get; set; } = FontFamilyOption.PlusJakartaSans;
    public double FontSize { get; set; } = 13;
    public int FontWeight { get; set; } = 500;
    public double IslandWidth { get; set; } = 216;
    public double IslandHeight { get; set; } = 36;
    public double CornerRadius { get; set; } = 22;
    public double Opacity { get; set; } = 96;
    public double Blur { get; set; } = 18;
    public double Spacing { get; set; } = 14;
    public double AnimationSpeed { get; set; } = 1.0;
}

public sealed class AnimationSettings
{
    public AnimationPreset Preset { get; set; } = AnimationPreset.Fluid;
    public bool ReduceMotion { get; set; }
    public bool HoverExpand { get; set; } = true;
    public bool NotificationBounce { get; set; } = true;
    public bool SmoothProgressRings { get; set; } = true;
    public bool IdleBreathing { get; set; } = true;
}

public sealed class MusicSettings
{
    public bool ShowAlbumArtInCompact { get; set; } = true;
    public bool ShowAudioVisualizer { get; set; } = true;
    public bool ShowCompactProgress { get; set; } = true;
    public bool AutoExpandOnTrackChange { get; set; }
    public bool ScrollLongTitles { get; set; } = true;
    public bool InterceptMediaKeys { get; set; } = true;
}

public sealed class BrowserIntegrationSettings
{
    public bool Enabled { get; set; } = true;
    public bool PreferChrome { get; set; } = true;
    public bool PreferEdge { get; set; } = true;
    public bool AutoFocusAudibleTab { get; set; } = true;
    public bool SyncArtworkAndMetadata { get; set; } = true;
    public bool EnableDownloadWatcher { get; set; } = true;
    public int PositionSyncIntervalMs { get; set; } = 1000;
    public string? ChromeExtensionId { get; set; }
    public string? EdgeExtensionId { get; set; }
}

public sealed class LyricsSettings
{
    public bool PreferSynchronized { get; set; } = true;
    public bool LrclibFallback { get; set; } = true;
    public LyricsSourcePriority SourcePriority { get; set; } = LyricsSourcePriority.LrclibThenLocal;
    public double FontSize { get; set; } = 16;
    public double LineSpacing { get; set; } = 1.6;
    public bool SmoothScroll { get; set; } = true;
    public bool BlurInactiveLines { get; set; } = true;
    public bool ShowTranslations { get; set; }
    public double FullscreenBackdropOpacity { get; set; } = 60;
}

public sealed class ClipboardSettings
{
    public bool Enabled { get; set; } = true;
    public int HistoryLimit { get; set; } = 100;
    public ClipboardAutoClear AutoClear { get; set; } = ClipboardAutoClear.TwentyFourHours;
    public bool ExcludeSensitive { get; set; } = true;
    public bool CaptureImages { get; set; } = true;
    public bool PersistPinned { get; set; } = true;
    public List<string> ExcludedApps { get; set; } = ["1Password.exe", "KeePassXC.exe", "Bitwarden.exe"];
}

public sealed class NotificationSettings
{
    public bool Enabled { get; set; } = true;
    public int DisplayDurationSeconds { get; set; } = 6;
    public bool SyncWindowsDnd { get; set; } = true;
    public bool GroupByApp { get; set; } = true;
    public bool ShowPreviews { get; set; } = true;
    public Dictionary<string, bool> MutedApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WidgetSettings
{
    public bool Battery { get; set; } = true;
    public bool Volume { get; set; } = true;
    public bool Brightness { get; set; } = true;
    public bool Wifi { get; set; } = true;
    public bool AudioDevices { get; set; } = true;
    public bool Microphone { get; set; } = true;
    public bool Camera { get; set; } = true;
    public bool Cpu { get; set; } = true;
    public bool Ram { get; set; } = true;
    public bool Downloads { get; set; } = true;
    public bool Transfers { get; set; } = true;
    public bool Screenshots { get; set; } = true;
    public int RefreshIntervalMs { get; set; } = 2000;
}

public sealed class PrivacySettings
{
    public bool PauseInPrivateBrowsing { get; set; } = true;
    public bool MaskWhenLocked { get; set; } = true;
    public bool DiagnosticLogs { get; set; }
    public bool LocalOnlyMode { get; set; }
}

public sealed class AppSettings
{
    public GeneralSettings General { get; set; } = new();
    public PositionSettings Position { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
    public AnimationSettings Animation { get; set; } = new();
    public MusicSettings Music { get; set; } = new();
    public BrowserIntegrationSettings BrowserIntegration { get; set; } = new();
    public LyricsSettings Lyrics { get; set; } = new();
    public ClipboardSettings Clipboard { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
    public WidgetSettings Widgets { get; set; } = new();
    public List<ShortcutBinding> Shortcuts { get; set; } = CreateDefaultShortcuts();
    public PrivacySettings Privacy { get; set; } = new();

    public static List<ShortcutBinding> CreateDefaultShortcuts() =>
    [
        new ShortcutBinding
        {
            Id = "toggle-island",
            Label = "Toggle Dynamic Island",
            Description = "Expand or collapse the floating island from any app.",
            Keys = "Alt+Space",
            Action = ShortcutAction.ToggleIsland
        },
        new ShortcutBinding
        {
            Id = "open-music",
            Label = "Open Music Panel",
            Description = "Jump directly to browser media playback controls.",
            Keys = "Ctrl+Shift+M",
            Action = ShortcutAction.OpenMusic
        },
        new ShortcutBinding
        {
            Id = "open-lyrics",
            Label = "Open Synchronized Lyrics",
            Description = "Toggle synchronized lyrics view for the active track.",
            Keys = "Ctrl+Shift+L",
            Action = ShortcutAction.OpenLyrics
        },
        new ShortcutBinding
        {
            Id = "open-clipboard",
            Label = "Open Clipboard History",
            Description = "Search and paste recent text, links, code, and images.",
            Keys = "Ctrl+Shift+V",
            Action = ShortcutAction.OpenClipboard
        },
        new ShortcutBinding
        {
            Id = "open-timer",
            Label = "Open Timer & Pomodoro",
            Description = "Show timer, stopwatch, and focus session controls.",
            Keys = "Ctrl+Shift+T",
            Action = ShortcutAction.OpenTimer
        },
        new ShortcutBinding
        {
            Id = "open-dashboard",
            Label = "Open Dashboard Hub",
            Description = "Show or focus the desktop control dashboard.",
            Keys = "Ctrl+Shift+D",
            Action = ShortcutAction.OpenDashboard
        },
        new ShortcutBinding
        {
            Id = "open-settings",
            Label = "Open Settings",
            Description = "Open DOOM Island settings.",
            Keys = "Ctrl+Shift+,",
            Action = ShortcutAction.OpenSettings
        },
        new ShortcutBinding
        {
            Id = "trigger-screenshot",
            Label = "Capture Quick Screenshot",
            Description = "Capture screen region and index it in Activity & Clipboard.",
            Keys = "Ctrl+Shift+S",
            Action = ShortcutAction.TriggerScreenshot
        }
    ];
}
