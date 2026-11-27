using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Services;

namespace NOVAIsland.Core.ViewModels;

public sealed partial class MusicViewModel : ObservableObject
{
    private readonly IBrowserMediaBridge _bridge;
    private readonly Lock _syncLock = new();
    private long _lastClockTicks = Stopwatch.GetTimestamp();

    public ObservableCollection<BrowserMediaSession> BrowserTabs { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveMedia))]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(FormattedPosition))]
    [NotifyPropertyChangedFor(nameof(FormattedDuration))]
    [NotifyPropertyChangedFor(nameof(FormattedRemaining))]
    [NotifyPropertyChangedFor(nameof(IsPlaying))]
    private BrowserMediaSession? _currentSession;

    [ObservableProperty]
    private bool _isBrowserConnected;

    [ObservableProperty]
    private string? _connectedBrowserName;

    [ObservableProperty]
    private string? _lastCommandStatus;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentage))]
    [NotifyPropertyChangedFor(nameof(FormattedPosition))]
    [NotifyPropertyChangedFor(nameof(FormattedRemaining))]
    private double _livePositionSeconds;

    public bool HasActiveMedia => CurrentSession is not null;
    public bool IsPlaying => CurrentSession?.PlaybackState == BrowserPlaybackState.Playing;
    public string FormattedPosition => TimeFormatter.FormatTime(LivePositionSeconds);
    public string FormattedDuration => TimeFormatter.FormatTime(CurrentSession?.DurationSeconds ?? 0);
    public string FormattedRemaining => $"-{TimeFormatter.FormatTime(Math.Max(0, (CurrentSession?.DurationSeconds ?? 0) - LivePositionSeconds))}";

    public double ProgressPercentage
    {
        get
        {
            var dur = CurrentSession?.DurationSeconds ?? 0;
            return dur > 0 ? Math.Clamp((LivePositionSeconds / dur) * 100.0, 0, 100) : 0;
        }
    }

    public MusicViewModel(IBrowserMediaBridge bridge)
    {
        _bridge = bridge;
        _bridge.StateChanged += (_, _) => SyncFromBridge();
    }

    public event EventHandler<BrowserMediaSession?>? TrackChanged;

    public void SyncFromBridge()
    {
        bool connected;
        string? browserName;
        BrowserMediaSession? current;
        List<BrowserMediaSession> sessions;

        lock (_syncLock)
        {
            connected = _bridge.IsConnected;
            browserName = _bridge.ConnectedBrowserName;
            current = _bridge.CurrentSession;
            sessions = _bridge.ActiveSessions.ToList();
        }

        NovaUiDispatcher.Post(() =>
        {
            var prevTrackKey = LyricsViewModel.BuildTrackKey(_currentSession);
            IsBrowserConnected = connected;
            ConnectedBrowserName = browserName;
            CurrentSession = current;
            LivePositionSeconds = current?.PositionSeconds ?? 0;
            _lastClockTicks = Stopwatch.GetTimestamp();

            BrowserTabs.Clear();
            foreach (var session in sessions)
            {
                BrowserTabs.Add(session);
            }

            var nextTrackKey = LyricsViewModel.BuildTrackKey(current);
            if (!string.Equals(prevTrackKey, nextTrackKey, StringComparison.Ordinal))
            {
                TrackChanged?.Invoke(this, current);
            }
        });
    }

    public void AdvancePlaybackByClock()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = Stopwatch.GetElapsedTime(_lastClockTicks, now).TotalSeconds;
        _lastClockTicks = now;
        if (elapsed > 0 && elapsed < 5.0)
        {
            AdvancePlaybackPosition(elapsed);
        }
    }

    public void AdvancePlaybackPosition(double deltaSeconds)
    {
        if (CurrentSession is null || CurrentSession.PlaybackState != BrowserPlaybackState.Playing)
        {
            return;
        }

        var rate = CurrentSession.PlaybackRate > 0 ? CurrentSession.PlaybackRate : 1.0;
        var next = LivePositionSeconds + deltaSeconds * rate;
        LivePositionSeconds = CurrentSession.DurationSeconds > 0
            ? Math.Min(next, CurrentSession.DurationSeconds)
            : next;
    }

    [RelayCommand]
    public Task TogglePlayPauseAsync() => DispatchCommandAsync(BrowserMediaAction.Toggle);

    [RelayCommand]
    public Task NextTrackAsync() => DispatchCommandAsync(BrowserMediaAction.Next);

    [RelayCommand]
    public Task PreviousTrackAsync() => DispatchCommandAsync(BrowserMediaAction.Previous);

    [RelayCommand]
    public Task FocusSourceTabAsync() => DispatchCommandAsync(BrowserMediaAction.FocusTab);

    public Task SeekAsync(double positionSeconds)
    {
        LivePositionSeconds = Math.Max(0, positionSeconds);
        _lastClockTicks = Stopwatch.GetTimestamp();
        return DispatchCommandAsync(BrowserMediaAction.Seek, positionSeconds);
    }

    private async Task DispatchCommandAsync(BrowserMediaAction action, double? seekSeconds = null)
    {
        if (CurrentSession is null)
        {
            LastCommandStatus = "No active browser media tab";
            return;
        }

        var result = await _bridge.SendCommandAsync(CurrentSession.TabId, action, seekSeconds);
        LastCommandStatus = result.Success ? null : $"Action not supported ({result.Reason})";
    }
}

public sealed partial class LyricsViewModel : ObservableObject
{
    private readonly ILyricsService _lyricsService;
    private readonly MusicViewModel _music;
    private readonly Lock _loadLock = new();
    private CancellationTokenSource? _trackLoadCts;
    private long _trackLoadSeq;

    public ObservableCollection<LyricLine> Lines { get; } = [];
    public ObservableCollection<LyricsSearchResult> SearchResults { get; } = [];

    [ObservableProperty]
    private string _trackTitle = string.Empty;

    [ObservableProperty]
    private string _artistName = string.Empty;

    [ObservableProperty]
    private LyricsSourceKind _source = LyricsSourceKind.None;

    [ObservableProperty]
    private bool _isSynced;

    [ObservableProperty]
    private bool _isInstrumental;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isSearchOpen;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _activeLineIndex = -1;

    [ObservableProperty]
    private bool _isFullscreenOpen;

    public LyricsViewModel(ILyricsService lyricsService, MusicViewModel music)
    {
        _lyricsService = lyricsService;
        _music = music;
    }

    public static string BuildTrackKey(BrowserMediaSession? session) =>
        session is null || string.IsNullOrWhiteSpace(session.Title)
            ? string.Empty
            : $"{session.Artist?.Trim()}::{session.Title.Trim()}";

    public Task LoadForCurrentTrackAsync(bool allowNetwork, CancellationToken cancellationToken = default) =>
        LoadForSessionAsync(_music.CurrentSession, allowNetwork, cancellationToken);

    public async Task LoadForSessionAsync(
        BrowserMediaSession? session,
        bool allowNetwork,
        CancellationToken cancellationToken = default)
    {
        long seq;
        CancellationTokenSource linkedCts;
        lock (_loadLock)
        {
            seq = Interlocked.Increment(ref _trackLoadSeq);
            _trackLoadCts?.Cancel();
            _trackLoadCts?.Dispose();
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _trackLoadCts = linkedCts;
        }

        if (session is null || string.IsNullOrWhiteSpace(session.Title))
        {
            NovaUiDispatcher.Post(() =>
            {
                if (Volatile.Read(ref _trackLoadSeq) != seq) return;
                TrackTitle = string.Empty;
                ArtistName = string.Empty;
                Lines.Clear();
                Source = LyricsSourceKind.None;
                IsSynced = false;
                IsInstrumental = false;
                ActiveLineIndex = -1;
                IsLoading = false;
            });
            return;
        }

        var requestedKey = BuildTrackKey(session);
        NovaUiDispatcher.Post(() =>
        {
            if (Volatile.Read(ref _trackLoadSeq) != seq) return;
            TrackTitle = session.Title;
            ArtistName = session.Artist;
            Lines.Clear();
            Source = LyricsSourceKind.None;
            IsSynced = false;
            IsInstrumental = false;
            ActiveLineIndex = -1;
            IsLoading = true;
        });

        try
        {
            var result = await _lyricsService.GetLyricsAsync(
                session.Title,
                session.Artist,
                session.Album,
                session.DurationSeconds,
                allowNetwork,
                linkedCts.Token);

            NovaUiDispatcher.Post(() =>
            {
                if (Volatile.Read(ref _trackLoadSeq) != seq) return;
                var current = _music.CurrentSession;
                if (current is null || BuildTrackKey(current) == requestedKey)
                {
                    ApplyResult(result);
                }
                IsLoading = false;
            });
        }
        catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
        {
            // Superseded by newer track load or shutdown
        }
        catch
        {
            NovaUiDispatcher.Post(() =>
            {
                if (Volatile.Read(ref _trackLoadSeq) == seq)
                {
                    IsLoading = false;
                }
            });
        }
    }

    [RelayCommand]
    public async Task SearchManualAsync(bool allowNetwork)
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        IsLoading = true;
        try
        {
            var results = await _lyricsService.SearchLyricsAsync(SearchQuery, allowNetwork);
            SearchResults.Clear();
            foreach (var item in results)
            {
                SearchResults.Add(item);
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public void ApplySearchResult(LyricsSearchResult? result)
    {
        ApplyResult(result);
        IsSearchOpen = false;
    }

    public void UpdateActiveLine(double positionSeconds)
    {
        if (!IsSynced || Lines.Count == 0)
        {
            ActiveLineIndex = -1;
            return;
        }

        ActiveLineIndex = LrcParser.FindActiveLineIndex(Lines, positionSeconds);
    }

    private void ApplyResult(LyricsSearchResult? result)
    {
        Lines.Clear();
        if (result is null)
        {
            Source = LyricsSourceKind.None;
            IsSynced = false;
            IsInstrumental = false;
            ActiveLineIndex = -1;
            return;
        }

        Source = result.Source;
        IsSynced = result.IsSynced;
        IsInstrumental = result.IsInstrumental;
        foreach (var line in result.Lines)
        {
            Lines.Add(line);
        }

        UpdateActiveLine(_music.LivePositionSeconds);
    }
}
