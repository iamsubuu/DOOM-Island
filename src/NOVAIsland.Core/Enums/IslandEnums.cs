namespace NOVAIsland.Core.Enums;

public enum IslandMode
{
    Compact,
    Music,
    Expanded,
    Lyrics,
    Clipboard,
    Notifications,
    Timer,
    System,
    Activity
}

public enum ClipboardItemType
{
    Text,
    Link,
    Code,
    Image
}

public enum ActivityType
{
    Download,
    Transfer,
    Screenshot
}

public enum ActivityStatus
{
    Active,
    Paused,
    Completed
}

public enum TimerTab
{
    Countdown,
    Stopwatch,
    Pomodoro
}

public enum PomodoroPhase
{
    Focus,
    ShortBreak,
    LongBreak
}

public enum AudioDeviceKind
{
    Speakers,
    Headphones,
    Interface
}

public enum ColorMode
{
    Dark,
    Light,
    System
}

public enum ThemePreset
{
    PureBlack,
    DrDoom,
    Obsidian,
    Graphite,
    GlossyPink,
    Venom,
    SpiderMan,
    CyberViolet,
    ArtworkDynamic,
    Contrast
}

public enum FontFamilyOption
{
    PlusJakartaSans,
    Manrope,
    Outfit,
    SegoeUiVariable
}

public enum AnimationPreset
{
    Fluid,
    Snappy,
    Silk,
    VenomLiquid,
    GlossyBounce,
    ZeroGravity,
    Instant
}

public enum ScreenPosition
{
    TopLeft,
    TopCenter,
    TopRight,
    Custom
}

public enum CollapseBehavior
{
    OutsideClick,
    MouseLeave,
    Manual
}

public enum DoubleClickAction
{
    ToggleLyrics,
    OpenFullscreenLyrics,
    OpenDashboard,
    TogglePlay
}

public enum LyricsSourcePriority
{
    LrclibThenLocal,
    LocalThenLrclib
}

public enum LyricsSourceKind
{
    None,
    Lrclib,
    LocalLrc
}

public enum ClipboardAutoClear
{
    Never,
    OneHour,
    TwentyFourHours,
    Restart
}

public enum BrowserPlaybackState
{
    None,
    Playing,
    Paused
}

public enum BrowserMediaAction
{
    Play,
    Pause,
    Toggle,
    Next,
    Previous,
    Seek,
    FocusTab
}

public enum ShortcutAction
{
    ToggleIsland,
    OpenMusic,
    OpenLyrics,
    OpenClipboard,
    OpenTimer,
    OpenDashboard,
    OpenSettings,
    TriggerScreenshot
}

public enum SettingsSectionId
{
    General,
    Appearance,
    Position,
    Animation,
    Music,
    BrowserIntegration,
    Lyrics,
    Clipboard,
    Notifications,
    Widgets,
    Shortcuts,
    Privacy,
    Storage,
    About
}
