using System.Collections.ObjectModel;
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

    public void SyncFromBridge()
    {
        lock (_syncLock)
        {
            IsBrowserConnected = _bridge.IsConnected;
            ConnectedBrowserName = _bridge.ConnectedBrowserName;
            CurrentSession = _bridge.CurrentSession;
            LivePositionSeconds = _bridge.CurrentSession?.PositionSeconds ?? 0;

            BrowserTabs.Clear();
            foreach (var session in _bridge.ActiveSessions)
            {
                BrowserTabs.Add(session);
            }
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

    public async Task LoadForCurrentTrackAsync(bool allowNetwork, CancellationToken cancellationToken = default)
    {
        var session = _music.CurrentSession;
        if (session is null || string.IsNullOrWhiteSpace(session.Title))
        {
            Lines.Clear();
            Source = LyricsSourceKind.None;
            IsSynced = false;
            ActiveLineIndex = -1;
            return;
        }

        TrackTitle = session.Title;
        ArtistName = session.Artist;
        IsLoading = true;
        try
        {
            var result = await _lyricsService.GetLyricsAsync(
                session.Title,
                session.Artist,
                session.Album,
                session.DurationSeconds,
                allowNetwork,
                cancellationToken);

            ApplyResult(result);
        }
        finally
        {
            IsLoading = false;
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
