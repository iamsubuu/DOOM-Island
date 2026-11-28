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
    internal sealed record MediaIdentity(string DisplayName, string[] ProcessNames, int SyntheticTabId, bool IsBrowser);

    private sealed class SmtcTrackedSession
    {
        public required GlobalSystemMediaTransportControlsSession SmtcSession { get; init; }
        public required MediaIdentity Identity { get; init; }
        public required BrowserMediaSession Model { get; set; }
    }

    private readonly BrowserMediaBridgeService _extensionBridge;
    private readonly Func<BrowserIntegrationSettings> _settingsProvider;
    private readonly Func<bool> _localOnlyProvider;
    private readonly ArtworkEnhancementService _artworkService;
    private readonly ConcurrentDictionary<string, SmtcTrackedSession> _smtcSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GlobalSystemMediaTransportControlsSession> _subscribedSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _upgradedArtworkMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    private GlobalSystemMediaTransportControlsSessionManager? _smtcManager;
    private CancellationTokenSource? _cts;
    private string? _lastPaletteSourceUri;
    private string _lastSmtcSnapshotSignature = string.Empty;
    private int? _selectedTabId;
    private bool _started;
    private bool _smtcReady;

    public (byte R1, byte G1, byte B1, byte R2, byte G2, byte B2)? CurrentArtworkPalette { get; private set; }
    public event EventHandler<(byte R1, byte G1, byte B1, byte R2, byte G2, byte B2)>? ArtworkPaletteChanged;

    public CompositeBrowserMediaBridge(
        BrowserMediaBridgeService extensionBridge,
        SqliteDbContext dbContext,
        Func<BrowserIntegrationSettings> settingsProvider,
        Func<bool>? localOnlyProvider = null)
    {
        _extensionBridge = extensionBridge;
        _settingsProvider = settingsProvider;
        _localOnlyProvider = localOnlyProvider ?? (() => false);
        _artworkService = new ArtworkEnhancementService(Path.Combine(dbContext.DataDirectory, "cache", "artwork"));

        _extensionBridge.StateChanged += (_, _) =>
        {
            _ = EnhanceCurrentSessionAndPaletteAsync();
            RaiseStateChanged();
        };
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

            return _smtcReady ? "Browsers & System Media" : null;
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
            var combined = new List<BrowserMediaSession>(extSessions.Count + _smtcSessions.Count);
            combined.AddRange(extSessions);

            foreach (var smtc in _smtcSessions.Values)
            {
                var smtcKey = NormalizeBrowserKey(smtc.Identity.DisplayName);
                var matchingExt = extSessions
                    .Where(s => string.Equals(NormalizeBrowserKey(s.Browser), smtcKey, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // Suppress SMTC duplicate only when MV3 extension already has a valid session and isn't superseded by an actively playing SMTC stream.
                var extHasValid = matchingExt.Any(s =>
                    s.PlaybackState == BrowserPlaybackState.Playing ||
                    (!string.IsNullOrWhiteSpace(s.Title) && smtc.Model.PlaybackState != BrowserPlaybackState.Playing));
                if (extHasValid)
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
                .ThenBy(s => s.Browser, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.TabId)
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

            var sessions = ActiveSessions;
            if (_selectedTabId is int selectedId)
            {
                var selected = sessions.FirstOrDefault(s => s.TabId == selectedId);
                if (selected is not null)
                {
                    return selected;
                }
                _selectedTabId = null;
            }

            var extCurrent = _extensionBridge.CurrentSession;
            if (extCurrent is not null && extCurrent.PlaybackState == BrowserPlaybackState.Playing)
            {
                return extCurrent;
            }

            return sessions.FirstOrDefault() ?? extCurrent;
        }
    }

    public bool SelectSession(int tabId)
    {
        if (!_settingsProvider().Enabled)
        {
            return false;
        }

        var exists = ActiveSessions.Any(s => s.TabId == tabId);
        if (!exists)
        {
            return false;
        }

        _selectedTabId = tabId;
        if (tabId >= 0 && _extensionBridge.SelectSession(tabId))
        {
            return true;
        }

        RaiseStateChanged();
        _ = EnhanceCurrentSessionAndPaletteAsync();
        return true;
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
            _ = Task.Run(() => PollSmtcTimelinesAsync(_cts.Token), _cts.Token);
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

    public async Task ForceRefreshAsync()
    {
        if (_cts?.IsCancellationRequested ?? true)
        {
            return;
        }

        if (_smtcManager is null)
        {
            try
            {
                _smtcManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _smtcManager.SessionsChanged += OnSmtcSessionsChanged;
                _smtcManager.CurrentSessionChanged += OnSmtcCurrentSessionChanged;
                _smtcReady = true;
            }
            catch
            {
                _smtcReady = false;
            }
        }

        _lastSmtcSnapshotSignature = string.Empty;
        await RefreshSmtcSessionsAsync();
        RaiseStateChanged();
        _ = EnhanceCurrentSessionAndPaletteAsync();
    }

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

    private async Task<bool> RefreshSmtcSessionsAsync()
    {
        if (_smtcManager is null || (_cts?.IsCancellationRequested ?? true))
        {
            return false;
        }

        await _refreshLock.WaitAsync();
        try
        {
            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions;
            string? currentAumid;
            try
            {
                sessions = _smtcManager.GetSessions();
                currentAumid = _smtcManager.GetCurrentSession()?.SourceAppUserModelId;
            }
            catch
            {
                return false;
            }

            var bestByAumid = new Dictionary<string, SmtcTrackedSession>(StringComparer.OrdinalIgnoreCase);

            foreach (var session in sessions)
            {
                try
                {
                    var aumid = session.SourceAppUserModelId;
                    var identity = TryResolveMediaIdentity(aumid);
                    if (identity is null)
                    {
                        continue;
                    }

                    _smtcSessions.TryGetValue(aumid, out var previousTracked);
                    var model = await BuildSessionModelAsync(
                        session,
                        identity,
                        string.Equals(aumid, currentAumid, StringComparison.OrdinalIgnoreCase),
                        previousTracked?.Model);

                    var candidate = new SmtcTrackedSession
                    {
                        SmtcSession = session,
                        Identity = identity,
                        Model = model
                    };

                    if (!bestByAumid.TryGetValue(aumid, out var existingBest) ||
                        ShouldPreferSmtcCandidate(candidate.Model, existingBest.Model))
                    {
                        bestByAumid[aumid] = candidate;
                    }
                }
                catch
                {
                    // Isolate transient COM failures per SMTC session so healthy tabs/players still sync
                }
            }

            foreach (var (aumid, best) in bestByAumid)
            {
                _smtcSessions[aumid] = best;
                SubscribeSessionEvents(best.SmtcSession);
            }

            foreach (var staleKey in _smtcSessions.Keys.Where(k => !bestByAumid.ContainsKey(k)).ToList())
            {
                _smtcSessions.TryRemove(staleKey, out _);
                if (_subscribedSessions.TryRemove(staleKey, out var oldSession))
                {
                    UnsubscribeSessionEvents(oldSession);
                }
            }

            var sig = BuildSmtcSnapshotSignature();
            var changed = !string.Equals(sig, _lastSmtcSnapshotSignature, StringComparison.Ordinal);
            _lastSmtcSnapshotSignature = sig;
            return changed;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    internal static int ScoreSmtcSession(BrowserMediaSession session)
    {
        var score = session.PlaybackState switch
        {
            BrowserPlaybackState.Playing => 100,
            BrowserPlaybackState.Paused => 40,
            _ => 0
        };
        if (!string.IsNullOrWhiteSpace(session.Title)) score += 20;
        if (session.IsActiveBrowserTab) score += 10;
        if (session.DurationSeconds > 0) score += 5;
        return score;
    }

    internal static bool ShouldPreferSmtcCandidate(BrowserMediaSession candidate, BrowserMediaSession existing) =>
        ScoreSmtcSession(candidate) > ScoreSmtcSession(existing);

    private string BuildSmtcSnapshotSignature()
    {
        if (_smtcSessions.IsEmpty)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            _smtcSessions
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => $"{kv.Key}:{kv.Value.Model.PlaybackState}:{kv.Value.Model.Title}:{kv.Value.Model.Artist}:{(int)Math.Round(kv.Value.Model.DurationSeconds)}"));
    }

    private async Task RefreshSingleSessionAsync(GlobalSystemMediaTransportControlsSession session)
    {
        string aumid;
        try
        {
            aumid = session.SourceAppUserModelId;
        }
        catch
        {
            return;
        }

        var identity = TryResolveMediaIdentity(aumid);
        if (identity is null)
        {
            return;
        }

        await _refreshLock.WaitAsync();
        try
        {
            var isCurrent = string.Equals(aumid, _smtcManager?.GetCurrentSession()?.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase);
            _smtcSessions.TryGetValue(aumid, out var previous);
            var model = await BuildSessionModelAsync(session, identity, isCurrent, previous?.Model);

            // Don't let a secondary/empty SMTC tab overwrite an active titled/playing entry for the same AUMID
            if (previous is not null &&
                !ReferenceEquals(previous.SmtcSession, session) &&
                !ShouldPreferSmtcCandidate(model, previous.Model))
            {
                return;
            }

            _smtcSessions[aumid] = new SmtcTrackedSession
            {
                SmtcSession = session,
                Identity = identity,
                Model = model
            };
            _lastSmtcSnapshotSignature = BuildSmtcSnapshotSignature();
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
        MediaIdentity identity,
        bool isCurrent,
        BrowserMediaSession? previousModel = null)
    {
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();
        GlobalSystemMediaTransportControlsSessionMediaProperties? props = null;
        try
        {
            props = await session.TryGetMediaPropertiesAsync();
        }
        catch
        {
            // Keep previously fetched metadata if SMTC throws transiently during track swap
        }

        var state = playback.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => BrowserPlaybackState.Playing,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => BrowserPlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Opened => BrowserPlaybackState.Paused,
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing => BrowserPlaybackState.Paused,
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

        var title = props?.Title?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(title) && props is null && previousModel is not null)
        {
            title = previousModel.Title;
        }

        var artist = props?.Artist?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(artist))
        {
            artist = props?.Subtitle?.Trim() ?? string.Empty;
        }
        if (string.IsNullOrWhiteSpace(artist) && props is null && previousModel is not null)
        {
            artist = previousModel.Artist;
        }
        if (string.IsNullOrWhiteSpace(artist))
        {
            artist = identity.DisplayName;
        }

        var album = props?.AlbumTitle?.Trim() ?? (props is null ? previousModel?.Album ?? string.Empty : string.Empty);

        var settings = _settingsProvider();
        string? artworkUri = null;
        uint thumbW = 0;
        uint thumbH = 0;
        if (settings.SyncArtworkAndMetadata && props?.Thumbnail is not null)
        {
            (artworkUri, thumbW, thumbH) = await _artworkService.CacheSmtcThumbnailAsync(props.Thumbnail);
        }
        else if (props is null && previousModel is not null)
        {
            artworkUri = previousModel.ArtworkUrl;
        }

        var trackCacheKey = $"{identity.DisplayName}::{artist}::{title}";
        if (_upgradedArtworkMap.TryGetValue(trackCacheKey, out var cachedHqUri))
        {
            artworkUri = cachedHqUri;
        }

        var sessionModel = new BrowserMediaSession
        {
            Browser = identity.DisplayName,
            TabId = identity.SyntheticTabId,
            WindowId = 0,
            Origin = identity.IsBrowser ? "smtc://auto" : "smtc://system",
            UrlHost = identity.IsBrowser ? $"{identity.DisplayName} · Auto" : "Local Media · System",
            TabTitle = title,
            Title = title,
            Artist = artist,
            Album = album,
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

        if (settings.SyncArtworkAndMetadata && !string.IsNullOrWhiteSpace(title))
        {
            var aumid = session.SourceAppUserModelId;
            _ = EnhanceSmtcArtworkAndPaletteAsync(aumid, trackCacheKey, title, artist, artworkUri, thumbW, thumbH);
        }

        return sessionModel;
    }

    private async Task EnhanceSmtcArtworkAndPaletteAsync(
        string aumid,
        string trackCacheKey,
        string title,
        string artist,
        string? initialUri,
        uint width,
        uint height)
    {
        try
        {
            var allowNet = !_localOnlyProvider() && _settingsProvider().SyncArtworkAndMetadata;
            var upgraded = await _artworkService.TryFetchHighQualityArtworkAsync(
                title, artist, initialUri, width, height, allowNet, _cts?.Token ?? CancellationToken.None);

            var finalUri = upgraded ?? initialUri;
            if (upgraded is not null && !string.Equals(upgraded, initialUri, StringComparison.Ordinal))
            {
                _upgradedArtworkMap[trackCacheKey] = upgraded;
                if (_smtcSessions.TryGetValue(aumid, out var tracked) &&
                    string.Equals(tracked.Model.Title, title, StringComparison.Ordinal))
                {
                    tracked.Model.ArtworkUrl = upgraded;
                    RaiseStateChanged();
                }
            }

            await UpdatePaletteFromArtworkUriAsync(finalUri);
        }
        catch
        {
            // Keep existing artwork when background HQ upgrade fails
        }
    }

    private async Task EnhanceCurrentSessionAndPaletteAsync()
    {
        try
        {
            var current = CurrentSession;
            if (current is null)
            {
                return;
            }

            var allowNet = !_localOnlyProvider() && _settingsProvider().SyncArtworkAndMetadata;
            var upgraded = await _artworkService.TryFetchHighQualityArtworkAsync(
                current.Title, current.Artist, current.ArtworkUrl, 0, 0, allowNet, _cts?.Token ?? CancellationToken.None);

            var finalUri = upgraded ?? current.ArtworkUrl;
            if (upgraded is not null && !string.Equals(upgraded, current.ArtworkUrl, StringComparison.Ordinal))
            {
                current.ArtworkUrl = upgraded;
                RaiseStateChanged();
            }

            await UpdatePaletteFromArtworkUriAsync(finalUri);
        }
        catch { }
    }

    private async Task UpdatePaletteFromArtworkUriAsync(string? uri)
    {
        if (string.IsNullOrWhiteSpace(uri) || string.Equals(_lastPaletteSourceUri, uri, StringComparison.Ordinal))
        {
            return;
        }

        var palette = await _artworkService.ExtractGradientPaletteAsync(uri);
        if (palette is { } p)
        {
            _lastPaletteSourceUri = uri;
            CurrentArtworkPalette = p;
            ArtworkPaletteChanged?.Invoke(this, p);
        }
    }

    private void SubscribeSessionEvents(GlobalSystemMediaTransportControlsSession session)
    {
        var key = session.SourceAppUserModelId;
        if (_subscribedSessions.TryGetValue(key, out var existing))
        {
            if (Equals(existing, session))
            {
                return;
            }
            try { UnsubscribeSessionEvents(existing); } catch { }
        }

        _subscribedSessions[key] = session;
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

    internal static MediaIdentity? TryResolveMediaIdentity(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid))
        {
            return null;
        }

        var trimmed = aumid.Trim();
        var lower = trimmed.ToLowerInvariant();
        if (lower.Contains("doomisland") || lower.Contains("novaisland"))
        {
            return null;
        }

        if (lower.Contains("msedge") || lower.Contains("microsoftedge"))
        {
            return new MediaIdentity("Microsoft Edge", ["msedge"], CreateSyntheticTabId("msedge"), IsBrowser: true);
        }
        if (lower.Contains("operagx") || lower.Contains("opera_gx"))
        {
            return new MediaIdentity("Opera GX", ["opera"], CreateSyntheticTabId("operagx"), IsBrowser: true);
        }
        if (lower.Contains("opera"))
        {
            return new MediaIdentity("Opera", ["opera"], CreateSyntheticTabId("opera"), IsBrowser: true);
        }
        if (lower.Contains("chrome"))
        {
            return new MediaIdentity("Google Chrome", ["chrome"], CreateSyntheticTabId("chrome"), IsBrowser: true);
        }
        if (lower.Contains("brave"))
        {
            return new MediaIdentity("Brave", ["brave"], CreateSyntheticTabId("brave"), IsBrowser: true);
        }
        if (lower.Contains("vivaldi"))
        {
            return new MediaIdentity("Vivaldi", ["vivaldi"], CreateSyntheticTabId("vivaldi"), IsBrowser: true);
        }
        if (lower.Contains("firefox"))
        {
            return new MediaIdentity("Firefox", ["firefox"], CreateSyntheticTabId("firefox"), IsBrowser: true);
        }

        // System / local media players (Spotify, VLC, Windows Media Player, Movies & TV, PotPlayer, mpv, foobar2000, etc.)
        if (lower.Contains("spotify"))
        {
            return new MediaIdentity("Spotify", ["Spotify"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("zunemusic") || lower.Contains("microsoft.media.player"))
        {
            return new MediaIdentity("Media Player", ["Microsoft.Media.Player", "Music.UI"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("zunevideo"))
        {
            return new MediaIdentity("Movies & TV", ["Video.UI"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("vlc"))
        {
            return new MediaIdentity("VLC Media Player", ["vlc"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("potplayer"))
        {
            return new MediaIdentity("PotPlayer", ["PotPlayerMini64", "PotPlayerMini", "PotPlayer64", "PotPlayer"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("mpc-hc") || lower.Contains("mpc-be"))
        {
            return new MediaIdentity("MPC Player", ["mpc-hc64", "mpc-hc", "mpc-be64", "mpc-be"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower == "mpv.exe" || lower.Contains("\\mpv.exe") || lower == "mpv")
        {
            return new MediaIdentity("mpv", ["mpv"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("foobar2000"))
        {
            return new MediaIdentity("foobar2000", ["foobar2000"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("aimp"))
        {
            return new MediaIdentity("AIMP", ["AIMP"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("musicbee"))
        {
            return new MediaIdentity("MusicBee", ["MusicBee"], CreateSyntheticTabId(lower), IsBrowser: false);
        }
        if (lower.Contains("applemusic") || lower.Contains("itunes"))
        {
            return new MediaIdentity("Apple Music", ["AppleMusic", "iTunes"], CreateSyntheticTabId(lower), IsBrowser: false);
        }

        var friendly = FormatSystemMediaName(trimmed, out var procStem);
        string[] procs = string.IsNullOrWhiteSpace(procStem) ? [] : [procStem];
        return new MediaIdentity(friendly, procs, CreateSyntheticTabId(lower), IsBrowser: false);
    }

    private static string FormatSystemMediaName(string aumid, out string? processStem)
    {
        var raw = aumid.Trim();
        var bang = raw.IndexOf('!');
        if (bang > 0)
        {
            raw = raw[..bang];
        }

        var underscore = raw.IndexOf('_');
        if (underscore > 0 && raw.Length - underscore >= 10)
        {
            raw = raw[..underscore];
        }

        var slash = Math.Max(raw.LastIndexOf('\\'), raw.LastIndexOf('/'));
        if (slash >= 0 && slash < raw.Length - 1)
        {
            raw = raw[(slash + 1)..];
        }

        if (raw.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processStem = Path.GetFileNameWithoutExtension(raw);
            raw = processStem;
        }
        else
        {
            processStem = raw;
        }

        if (raw.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase))
        {
            raw = raw["Microsoft.".Length..];
        }

        raw = raw.Replace('.', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(raw) ? "System Media" : raw;
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

    private async Task PollSmtcTimelinesAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1500));
        var ticks = 0;
        while (await timer.WaitForNextTickAsync(ct))
        {
            ticks++;

            // Reconcile SMTC sessions periodically or whenever no titled session is tracked yet so missed WinRT events recover automatically.
            var needsResync = ticks % 2 == 0 || _smtcSessions.IsEmpty || _smtcSessions.Values.Any(s => string.IsNullOrWhiteSpace(s.Model.Title));
            if (needsResync && _smtcManager is not null)
            {
                try
                {
                    var rawCount = _smtcManager.GetSessions().Count;
                    if (rawCount != _smtcSessions.Count || rawCount > 0)
                    {
                        if (await RefreshSmtcSessionsAsync())
                        {
                            RaiseStateChanged();
                        }
                    }
                }
                catch { }
            }

            var anyPlaying = _smtcSessions.Values.Any(s => s.Model.PlaybackState == BrowserPlaybackState.Playing);
            if (!anyPlaying) continue;

            var changed = false;
            foreach (var tracked in _smtcSessions.Values.ToList())
            {
                if (tracked.Model.PlaybackState != BrowserPlaybackState.Playing) continue;
                try
                {
                    var timeline = tracked.SmtcSession.GetTimelineProperties();
                    var playback = tracked.SmtcSession.GetPlaybackInfo();
                    var rate = playback.PlaybackRate is > 0 ? playback.PlaybackRate.Value : 1.0;
                    var dur = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
                    var pos = Math.Max(0, (timeline.Position - timeline.StartTime).TotalSeconds);
                    if (timeline.LastUpdatedTime != default)
                    {
                        var elapsed = (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds;
                        if (elapsed > 0 && elapsed < 3600)
                        {
                            pos += elapsed * rate;
                        }
                    }
                    if (dur > 0) pos = Math.Clamp(pos, 0, dur);

                    if (Math.Abs(pos - tracked.Model.PositionSeconds) > 0.35 || Math.Abs(dur - tracked.Model.DurationSeconds) > 0.5)
                    {
                        tracked.Model.PositionSeconds = pos;
                        if (dur > 0) tracked.Model.DurationSeconds = dur;
                        tracked.Model.PlaybackRate = rate;
                        tracked.Model.LastUpdatedUtc = DateTimeOffset.UtcNow;
                        changed = true;
                    }
                }
                catch { }
            }

            if (changed)
            {
                RaiseStateChanged();
            }
        }
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
