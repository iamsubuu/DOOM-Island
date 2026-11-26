using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Infrastructure.Persistence;
using NOVAIsland.Services.Media;
using Windows.Media.Control;

namespace NOVAIsland.Infrastructure.Platform;

/// <summary>
/// Unified media bridge combining:
/// 1. Native Messaging MV3 extension sessions (Chrome/Edge/Opera when extension is loaded)
/// 2. Automatic zero-config Windows System Media Transport Controls (SMTC) for Chrome, Opera, Edge, and other browsers.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class CompositeBrowserMediaBridge : IBrowserMediaBridge, IAsyncDisposable
{
    private sealed record BrowserIdentity(string DisplayName, string[] ProcessNames, int SyntheticTabId);

    private sealed class SmtcTrackedSession
    {
        public required GlobalSystemMediaTransportControlsSession SmtcSession { get; init; }
        public required BrowserIdentity Identity { get; init; }
        public required BrowserMediaSession Model { get; set; }
    }

    private readonly BrowserMediaBridgeService _extensionBridge;
    private readonly Func<BrowserIntegrationSettings> _settingsProvider;
    private readonly string _artworkCacheDir;
    private readonly ConcurrentDictionary<string, SmtcTrackedSession> _smtcSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GlobalSystemMediaTransportControlsSession> _subscribedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private GlobalSystemMediaTransportControlsSessionManager? _smtcManager;
    private CancellationTokenSource? _cts;
    private bool _started;
    private bool _smtcReady;

    public CompositeBrowserMediaBridge(
        BrowserMediaBridgeService extensionBridge,
        SqliteDbContext dbContext,
        Func<BrowserIntegrationSettings> settingsProvider)
    {
        _extensionBridge = extensionBridge;
        _settingsProvider = settingsProvider;
        _artworkCacheDir = Path.Combine(dbContext.DataDirectory, "cache", "artwork");
        Directory.CreateDirectory(_artworkCacheDir);

        _extensionBridge.StateChanged += (_, _) => RaiseStateChanged();
    }

    public bool IsConnected
    {
        get
        {
            if (!_settingsProvider().Enabled)
            {
                return false;
            }
            return _extensionBridge.IsConnected || _smtcReady;
        }
    }

    public string? ConnectedBrowserName
    {
        get
        {
            if (!_settingsProvider().Enabled)
            {
                return null;
            }

            if (_extensionBridge.IsConnected && !string.IsNullOrWhiteSpace(_extensionBridge.ConnectedBrowserName))
            {
                return _extensionBridge.ConnectedBrowserName;
            }

            var currentSmtc = _smtcSessions.Values
                .OrderByDescending(s => s.Model.PlaybackState == BrowserPlaybackState.Playing)
                .ThenByDescending(s => s.Model.LastUpdatedUtc)
                .FirstOrDefault();

            if (currentSmtc is not null)
            {
                return $"{currentSmtc.Identity.DisplayName} (Auto)";
            }

            return _smtcReady ? "Chrome · Edge · Opera (Auto)" : null;
        }
    }

    public IReadOnlyList<BrowserMediaSession> ActiveSessions
    {
        get
        {
            if (!_settingsProvider().Enabled)
            {
                return [];
            }

            var extSessions = _extensionBridge.ActiveSessions;
            var extBrowsers = extSessions
                .Select(s => NormalizeBrowserKey(s.Browser))
                .Where(k => !string.IsNullOrEmpty(k))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var combined = new List<BrowserMediaSession>(extSessions.Count + _smtcSessions.Count);
            combined.AddRange(extSessions);

            foreach (var smtc in _smtcSessions.Values)
            {
                if (extBrowsers.Contains(NormalizeBrowserKey(smtc.Identity.DisplayName)))
                {
                    continue;
                }
                if (smtc.Model.PlaybackState == BrowserPlaybackState.None && string.IsNullOrWhiteSpace(smtc.Model.Title))
                {
                    continue;
                }
                combined.Add(smtc.Model);
            }

            return combined
                .OrderByDescending(s => s.PlaybackState == BrowserPlaybackState.Playing)
                .ThenByDescending(s => s.TabId >= 0)
                .ThenByDescending(s => s.IsActiveBrowserTab)
                .ThenByDescending(s => s.LastUpdatedUtc)
                .ToList();
        }
    }

    public BrowserMediaSession? CurrentSession
    {
        get
        {
            if (!_settingsProvider().Enabled)
            {
                return null;
            }

            var extCurrent = _extensionBridge.CurrentSession;
            if (extCurrent is not null && extCurrent.PlaybackState == BrowserPlaybackState.Playing)
            {
                return extCurrent;
            }

            return ActiveSessions.FirstOrDefault() ?? extCurrent;
        }
    }

    public event EventHandler? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_started)
        {
            return;
        }
        _started = true;
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await _extensionBridge.StartAsync(_cts.Token);

        try
        {
            _smtcManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _smtcManager.SessionsChanged += OnSmtcSessionsChanged;
            _smtcManager.CurrentSessionChanged += OnSmtcCurrentSessionChanged;
            _smtcReady = true;

            await RefreshSmtcSessionsAsync();
            RaiseStateChanged();
        }
        catch
        {
            // Keep Named Pipe extension bridge operational even if WinRT SMTC is unavailable
            _smtcReady = false;
        }
    }

    public async Task<CommandResultInfo> SendCommandAsync(
        int targetTabId,
        BrowserMediaAction action,
        double? seekSeconds = null,
        CancellationToken cancellationToken = default)
    {
        if (targetTabId >= 0)
        {
            return await _extensionBridge.SendCommandAsync(targetTabId, action, seekSeconds, cancellationToken);
        }

        var tracked = _smtcSessions.Values.FirstOrDefault(s => s.Model.TabId == targetTabId);
        if (tracked is null)
        {
            return new CommandResultInfo(false, "browser_not_connected");
        }

        try
        {
            var smtc = tracked.SmtcSession;
            var ok = action switch
            {
                BrowserMediaAction.Play => await smtc.TryPlayAsync(),
                BrowserMediaAction.Pause => await smtc.TryPauseAsync(),
                BrowserMediaAction.Toggle => await smtc.TryTogglePlayPauseAsync(),
                BrowserMediaAction.Next => await smtc.TrySkipNextAsync(),
                BrowserMediaAction.Previous => await smtc.TrySkipPreviousAsync(),
                BrowserMediaAction.Seek => await smtc.TryChangePlaybackPositionAsync(
                    TimeSpan.FromSeconds(Math.Max(0, seekSeconds ?? 0)).Ticks),
                BrowserMediaAction.FocusTab => FocusBrowserWindow(tracked.Identity.ProcessNames),
                _ => false
            };

            if (ok && action is BrowserMediaAction.Play or BrowserMediaAction.Pause or BrowserMediaAction.Toggle or BrowserMediaAction.Seek)
            {
                await RefreshSingleSessionAsync(smtc);
                RaiseStateChanged();
            }

            return new CommandResultInfo(ok, ok ? "ok" : "unsupported");
        }
        catch
        {
            return new CommandResultInfo(false, "command_failed");
        }
    }

    public void NotifySettingsChanged() => RaiseStateChanged();

    private void OnSmtcSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            await RefreshSmtcSessionsAsync();
            RaiseStateChanged();
        });
    }

    private void OnSmtcCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        _ = Task.Run(async () =>
        {
            await RefreshSmtcSessionsAsync();
            RaiseStateChanged();
        });
    }

    private async Task RefreshSmtcSessionsAsync()
    {
        if (_smtcManager is null || (_cts?.IsCancellationRequested ?? true))
        {
            return;
        }

        await _refreshLock.WaitAsync();
        try
        {
            var sessions = _smtcManager.GetSessions();
            var currentAumid = _smtcManager.GetCurrentSession()?.SourceAppUserModelId;
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in sessions)
            {
                var aumid = session.SourceAppUserModelId;
                var identity = TryResolveBrowserIdentity(aumid);
                if (identity is null)
                {
                    continue;
                }

                seenKeys.Add(aumid);
                SubscribeSessionEvents(session);

                var model = await BuildSessionModelAsync(session, identity, string.Equals(aumid, currentAumid, StringComparison.OrdinalIgnoreCase));
                _smtcSessions[aumid] = new SmtcTrackedSession
                {
                    SmtcSession = session,
                    Identity = identity,
                    Model = model
                };
            }

            foreach (var staleKey in _smtcSessions.Keys.Where(k => !seenKeys.Contains(k)).ToList())
            {
                _smtcSessions.TryRemove(staleKey, out _);
                if (_subscribedSessions.TryRemove(staleKey, out var oldSession))
                {
                    UnsubscribeSessionEvents(oldSession);
                }
            }
        }
        catch
        {
            // Ignore transient WinRT COM exceptions when browsers close abruptly
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task RefreshSingleSessionAsync(GlobalSystemMediaTransportControlsSession session)
    {
        var aumid = session.SourceAppUserModelId;
        var identity = TryResolveBrowserIdentity(aumid);
        if (identity is null)
        {
            return;
        }

        await _refreshLock.WaitAsync();
        try
        {
            var isCurrent = string.Equals(aumid, _smtcManager?.GetCurrentSession()?.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase);
            var model = await BuildSessionModelAsync(session, identity, isCurrent);
            _smtcSessions[aumid] = new SmtcTrackedSession
            {
                SmtcSession = session,
                Identity = identity,
                Model = model
            };
        }
        catch
        {
            // Session closed mid-refresh
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<BrowserMediaSession> BuildSessionModelAsync(
        GlobalSystemMediaTransportControlsSession session,
        BrowserIdentity identity,
        bool isCurrent)
    {
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        var props = await session.TryGetMediaPropertiesAsync();

        var state = playback.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => BrowserPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => BrowserPlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => BrowserPlaybackState.Paused,
            _ => BrowserPlaybackState.None
        };

        var rate = playback.PlaybackRate is > 0 ? playback.PlaybackRate.Value : 1.0;
        var durationSeconds = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        var posSeconds = Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);
        if (state == BrowserPlaybackState.Playing && timeline.LastUpdatedTime != default)
        {
            var elapsed = (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds;
            if (elapsed > 0 && elapsed < 3600)
            {
                posSeconds += elapsed * rate;
            }
        }
        if (durationSeconds > 0)
        {
            posSeconds = Math.Clamp(posSeconds, 0, durationSeconds);
        }

        var settings = _settingsProvider();
        string? artworkUri = null;
        if (settings.SyncArtworkAndMetadata && props.Thumbnail is not null)
        {
            artworkUri = await CacheThumbnailAsync(props.Thumbnail);
        }

        var title = props.Title?.Trim() ?? string.Empty;
        var artist = props.Artist?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(artist))
        {
            artist = identity.DisplayName;
        }

        return new BrowserMediaSession
        {
            Browser = identity.DisplayName,
            TabId = identity.SyntheticTabId,
            WindowId = 0,
            Origin = "smtc://auto",
            UrlHost = $"{identity.DisplayName} · Auto",
            TabTitle = title,
            Title = title,
            Artist = artist,
            Album = props.AlbumTitle?.Trim() ?? string.Empty,
            ArtworkUrl = artworkUri,
            DurationSeconds = durationSeconds,
            PositionSeconds = posSeconds,
            PlaybackRate = rate,
            PlaybackState = state,
            IsAudible = state == BrowserPlaybackState.Playing,
            IsActiveBrowserTab = isCurrent,
            CanPlayPause = playback.Controls.IsPlayPauseToggleEnabled || playback.Controls.IsPlayEnabled || playback.Controls.IsPauseEnabled,
            CanNext = playback.Controls.IsNextEnabled,
            CanPrevious = playback.Controls.IsPreviousEnabled,
            CanSeek = playback.Controls.IsPlaybackPositionEnabled && durationSeconds > 0,
            LastUpdatedUtc = DateTimeOffset.UtcNow
        };
    }

    private async Task<string?> CacheThumbnailAsync(Windows.Storage.Streams.IRandomAccessStreamReference thumbnail)
    {
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            using var ms = new MemoryStream();
            await netStream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length == 0)
            {
                return null;
            }

            var hash = Convert.ToHexString(SHA256.HashData(bytes))[..16];
            var ext = bytes.Length >= 4 && bytes[0] == 0x89 && bytes[1] == 0x50 ? ".png" : ".jpg";
            var path = Path.Combine(_artworkCacheDir, $"smtc_{hash}{ext}");
            if (!File.Exists(path))
            {
                await File.WriteAllBytesAsync(path, bytes);
            }

            return new Uri(path).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private void SubscribeSessionEvents(GlobalSystemMediaTransportControlsSession session)
    {
        var key = session.SourceAppUserModelId;
        if (!_subscribedSessions.TryAdd(key, session))
        {
            return;
        }

        session.MediaPropertiesChanged += OnSmtcSessionUpdated;
        session.PlaybackInfoChanged += OnSmtcSessionUpdated;
        session.TimelinePropertiesChanged += OnSmtcSessionUpdated;
    }

    private void UnsubscribeSessionEvents(GlobalSystemMediaTransportControlsSession session)
    {
        session.MediaPropertiesChanged -= OnSmtcSessionUpdated;
        session.PlaybackInfoChanged -= OnSmtcSessionUpdated;
        session.TimelinePropertiesChanged -= OnSmtcSessionUpdated;
    }

    private void OnSmtcSessionUpdated(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        _ = Task.Run(async () =>
        {
            await RefreshSingleSessionAsync(sender);
            RaiseStateChanged();
        });
    }

    private static BrowserIdentity? TryResolveBrowserIdentity(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return null;
        }

        var lower = aumid.ToLowerInvariant();
        if (lower.Contains("msedge") || lower.Contains("microsoftedge"))
        {
            return new BrowserIdentity("Microsoft Edge", ["msedge"], CreateSyntheticTabId("msedge"));
        }
        if (lower.Contains("operagx") || lower.Contains("opera_gx"))
        {
            return new BrowserIdentity("Opera GX", ["opera"], CreateSyntheticTabId("operagx"));
        }
        if (lower.Contains("opera"))
        {
            return new BrowserIdentity("Opera", ["opera"], CreateSyntheticTabId("opera"));
        }
        if (lower.Contains("chrome"))
        {
            return new BrowserIdentity("Google Chrome", ["chrome"], CreateSyntheticTabId("chrome"));
        }
        if (lower.Contains("brave"))
        {
            return new BrowserIdentity("Brave", ["brave"], CreateSyntheticTabId("brave"));
        }
        if (lower.Contains("vivaldi"))
        {
            return new BrowserIdentity("Vivaldi", ["vivaldi"], CreateSyntheticTabId("vivaldi"));
        }
        if (lower.Contains("firefox"))
        {
            return new BrowserIdentity("Firefox", ["firefox"], CreateSyntheticTabId("firefox"));
        }

        return null;
    }

    private static string NormalizeBrowserKey(string? browser)
    {
        if (string.IsNullOrWhiteSpace(browser)) return string.Empty;
        var lower = browser.ToLowerInvariant();
        if (lower.Contains("edge")) return "edge";
        if (lower.Contains("opera")) return "opera";
        if (lower.Contains("chrome")) return "chrome";
        if (lower.Contains("brave")) return "brave";
        if (lower.Contains("vivaldi")) return "vivaldi";
        if (lower.Contains("firefox")) return "firefox";
        return lower;
    }

    private static int CreateSyntheticTabId(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        var value = BitConverter.ToInt32(hash, 0) & 0x0FFFFFFF;
        return -(1000 + (value % 100000));
    }

    private static bool FocusBrowserWindow(string[] processNames)
    {
        foreach (var name in processNames)
        {
            try
            {
                var proc = Process.GetProcessesByName(name)
                    .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
                if (proc is null) continue;

                var hwnd = proc.MainWindowHandle;
                if (Win32Interop.IsIconic(hwnd))
                {
                    Win32Interop.ShowWindow(hwnd, Win32Interop.SW_RESTORE);
                }
                return Win32Interop.SetForegroundWindow(hwnd);
            }
            catch
            {
                // Continue trying other process names
            }
        }
        return false;
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }

        if (_smtcManager is not null)
        {
            _smtcManager.SessionsChanged -= OnSmtcSessionsChanged;
            _smtcManager.CurrentSessionChanged -= OnSmtcCurrentSessionChanged;
        }

        foreach (var session in _subscribedSessions.Values)
        {
            UnsubscribeSessionEvents(session);
        }
        _subscribedSessions.Clear();
        _smtcSessions.Clear();

        await _extensionBridge.DisposeAsync();
    }
}
