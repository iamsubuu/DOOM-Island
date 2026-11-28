using NOVAIsland.Core.Models;
using NOVAIsland.Core.Enums;

namespace NOVAIsland.Core.Interfaces;

public interface IBrowserMediaBridge
{
    bool IsConnected { get; }
    string? ConnectedBrowserName { get; }
    IReadOnlyList<BrowserMediaSession> ActiveSessions { get; }
    BrowserMediaSession? CurrentSession { get; }

    event EventHandler? StateChanged;

    Task StartAsync(CancellationToken cancellationToken);
    bool SelectSession(int tabId);
    Task<CommandResultInfo> SendCommandAsync(int targetTabId, BrowserMediaAction action, double? seekSeconds = null, CancellationToken cancellationToken = default);
}

public sealed record CommandResultInfo(bool Success, string Reason);

public interface ILyricsService
{
    Task<LyricsSearchResult?> GetLyricsAsync(
        string title,
        string artist,
        string? album,
        double durationSeconds,
        bool allowNetwork,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LyricsSearchResult>> SearchLyricsAsync(
        string query,
        bool allowNetwork,
        CancellationToken cancellationToken = default);
}

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<string> ExportJsonAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
}

public interface IClipboardRepository
{
    Task<IReadOnlyList<ClipboardItem>> GetAllAsync(CancellationToken cancellationToken = default);
    Task UpsertAsync(ClipboardItem item, int historyLimit, CancellationToken cancellationToken = default);
    Task SetPinnedAsync(string id, bool pinned, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task ClearAsync(bool keepPinned, CancellationToken cancellationToken = default);
    Task PurgeExpiredAsync(TimeSpan maxAge, CancellationToken cancellationToken = default);
}

public interface ILyricsCacheRepository
{
    Task<LyricsSearchResult?> GetCachedAsync(string title, string artist, CancellationToken cancellationToken = default);
    Task SaveCachedAsync(LyricsSearchResult result, CancellationToken cancellationToken = default);
    Task ClearAsync(CancellationToken cancellationToken = default);
    Task<double> GetSizeMbAsync(CancellationToken cancellationToken = default);
}

public interface IClipboardMonitorService
{
    event EventHandler<ClipboardItem>? ItemCaptured;
    void Start();
    void Stop();
    Task CopyToClipboardAsync(ClipboardItem item, CancellationToken cancellationToken = default);
}

public interface INotificationListenerService
{
    bool IsSupported { get; }
    bool HasPermission { get; }
    string StatusMessage { get; }
    event EventHandler<NotificationItem>? NotificationReceived;
    event EventHandler<uint>? NotificationRemoved;
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NotificationItem>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task DismissAsync(NotificationItem item, CancellationToken cancellationToken = default);
    bool IsFocusAssistActive();
}

public interface ISystemMonitorService
{
    Task<SystemMetrics> CaptureMetricsAsync(CancellationToken cancellationToken = default);
    Task SetMasterVolumeAsync(int volumePercent, bool muted, CancellationToken cancellationToken = default);
    Task SetMicrophoneMutedAsync(bool muted, CancellationToken cancellationToken = default);
    Task<bool> SetBrightnessAsync(int brightnessPercent, CancellationToken cancellationToken = default);
    Task<bool> SetDefaultAudioDeviceAsync(string deviceId, CancellationToken cancellationToken = default);
}

public interface IAudioVisualizerService
{
    event EventHandler<float[]>? SpectrumUpdated;
    void Start();
    void Stop();
}

public interface IActivityMonitorService
{
    event EventHandler<IReadOnlyList<ActivityItem>>? ActivitiesChanged;
    Task StartAsync(CancellationToken cancellationToken = default);
    Task<ActivityItem?> CaptureQuickScreenshotAsync(CancellationToken cancellationToken = default);
    Task<bool> SaveScreenshotAsAsync(ActivityItem item, CancellationToken cancellationToken = default);
}

public interface IWindowPositioningService
{
    IReadOnlyList<DisplayMonitorInfo> GetMonitors();
    void ApplyIslandPosition(PositionSettings settings, int widthPx, int heightPx);
}

public sealed record DisplayMonitorInfo(string Id, string DisplayName, int Width, int Height, bool IsPrimary);

public interface IHotkeyService
{
    event EventHandler<ShortcutAction>? ShortcutTriggered;
    void RegisterBindings(IReadOnlyList<ShortcutBinding> bindings);
    void UnregisterAll();
}

public interface IStartupService
{
    bool IsStartupEnabled();
    void SetStartupEnabled(bool enabled);
}

public interface ITrayService
{
    void Initialize(string appName);
    void UpdateTooltip(string text);
    void DisposeTray();
}
