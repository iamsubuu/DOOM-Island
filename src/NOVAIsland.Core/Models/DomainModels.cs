using NOVAIsland.Core.Enums;

namespace NOVAIsland.Core.Models;

public sealed record LyricLine(
    double TimeSeconds,
    string Text,
    string? Translation = null);

public sealed record LyricsSearchResult(
    string Title,
    string Artist,
    string? Album,
    double DurationSeconds,
    LyricsSourceKind Source,
    bool IsSynced,
    bool IsInstrumental,
    IReadOnlyList<LyricLine> Lines,
    string? RawLrc = null);

public sealed class BrowserMediaSession
{
    public string SessionKey => $"{Browser}:{TabId}";
    public string Browser { get; set; } = "chrome";
    public int TabId { get; set; }
    public int WindowId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public string UrlHost { get; set; } = string.Empty;
    public string TabTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? ArtworkUrl { get; set; }
    public string? CachedArtworkPath { get; set; }
    public double DurationSeconds { get; set; }
    public double PositionSeconds { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
    public BrowserPlaybackState PlaybackState { get; set; } = BrowserPlaybackState.None;
    public bool IsAudible { get; set; }
    public bool IsActiveBrowserTab { get; set; }
    public bool CanPlayPause { get; set; } = true;
    public bool CanNext { get; set; }
    public bool CanPrevious { get; set; }
    public bool CanSeek { get; set; }
    public DateTimeOffset LastUpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ClipboardItem(
    string Id,
    ClipboardItemType Type,
    string Content,
    string? PreviewTitle,
    string? Language,
    string? ImagePath,
    string SourceApp,
    DateTimeOffset CapturedAtUtc,
    bool Pinned = false,
    bool Sensitive = false,
    string? ContentHash = null);

public sealed record NotificationItem(
    string Id,
    string AppName,
    string Title,
    string Body,
    DateTimeOffset TimestampUtc,
    bool Read = false,
    bool Dismissed = false,
    string? ActionLabel = null,
    uint? PlatformNotificationId = null);

public sealed record ActivityItem(
    string Id,
    ActivityType Type,
    string Title,
    string Subtitle,
    double Progress,
    string? SpeedText,
    string SizeText,
    ActivityStatus Status,
    string? FilePath,
    DateTimeOffset TimestampUtc);

public sealed record AudioDevice(
    string Id,
    string Name,
    AudioDeviceKind Kind,
    string SampleRate,
    bool IsDefault = false);

public sealed record SystemMetrics(
    int BatteryLevel,
    bool HasBattery,
    bool IsCharging,
    int Volume,
    bool IsMuted,
    int Brightness,
    bool BrightnessSupported,
    bool WifiConnected,
    string WifiNetwork,
    int WifiSignal,
    bool MicActive,
    bool MicMuted,
    bool CameraActive,
    int CpuUsage,
    int RamUsage,
    double RamUsedGb,
    double RamTotalGb,
    string? ActiveAudioDeviceId,
    IReadOnlyList<AudioDevice> AudioDevices,
    string BrightnessStatus = "");

public sealed class ShortcutBinding
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Keys { get; set; } = string.Empty;
    public ShortcutAction Action { get; set; }
}

public sealed class StorageFootprint
{
    public double AlbumCacheMb { get; set; }
    public double LyricsCacheMb { get; set; }
    public double ClipboardImagesMb { get; set; }
    public double ConfigDbMb { get; set; }
    public double TotalMb => Math.Round(AlbumCacheMb + LyricsCacheMb + ClipboardImagesMb + ConfigDbMb, 2);
}
