using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.ViewModels;
using Windows.UI;

namespace NOVAIsland.App.Views.Island;

public sealed partial class MusicPanelView : UserControl
{
    private MusicViewModel? _music;
    private SystemViewModel? _system;
    private IslandViewModel? _island;
    private LyricsViewModel? _lyrics;
    private bool _updatingUi;
    private string? _lastArtworkUri;
    private DateTime _lastCalendarDate = DateTime.MinValue;

    public MusicPanelView()
    {
        InitializeComponent();
        UpdateClock();
    }

    public void Bind(MusicViewModel music, SystemViewModel system, IslandViewModel island, LyricsViewModel lyrics)
    {
        _music = music;
        _system = system;
        _island = island;
        _lyrics = lyrics;

        _music.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(MusicViewModel.LivePositionSeconds))
            {
                RefreshTimelineOnly();
            }
            else
            {
                Refresh();
            }
        });
        _system.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshVolume);
        _lyrics.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshGlance);
        Refresh();
        RefreshVolume();
        RefreshGlance();
    }

    public void UpdateSpectrum(ReadOnlySpan<float> bands) => Equalizer.UpdateSpectrum(bands);

    public void UpdateClock()
    {
        var now = DateTime.Now;
        ExactTimeText.Text = now.ToString("hh:mm:ss tt");
        if (now.Date != _lastCalendarDate)
        {
            BuildCalendarStrip(now);
        }
    }

    private void BuildCalendarStrip(DateTime? referenceNow = null)
    {
        var today = (referenceNow ?? DateTime.Now).Date;
        _lastCalendarDate = today;
        CalendarMonthText.Text = today.ToString("MMM d");
        WeekDaysStrip.Children.Clear();

        for (var offset = -2; offset <= 3; offset++)
        {
            var day = today.AddDays(offset);
            var isToday = offset == 0;

            var col = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
            col.Children.Add(new TextBlock
            {
                Text = day.ToString("ddd")[..2],
                FontSize = 9.5,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 113, 113, 122))
            });
            col.Children.Add(new TextBlock
            {
                Text = day.Day.ToString("00"),
                FontSize = 11.5,
                FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = isToday
                    ? (Brush)Application.Current.Resources["NovaAccentBrush"]
                    : new SolidColorBrush(Color.FromArgb(255, 180, 184, 196))
            });
            WeekDaysStrip.Children.Add(col);
        }
    }

    private void RefreshGlance()
    {
        if (_lyrics is null) return;
        if (_lyrics.Lines.Count > 0 && _lyrics.ActiveLineIndex >= 0 && _lyrics.ActiveLineIndex < _lyrics.Lines.Count)
        {
            GlanceHeadlineText.Text = _lyrics.Lines[_lyrics.ActiveLineIndex].Text;
            GlanceSubtextText.Text = $"{_lyrics.Source} · Live synced lyrics";
        }
        else if (!string.IsNullOrWhiteSpace(_lyrics.TrackTitle))
        {
            GlanceHeadlineText.Text = _lyrics.TrackTitle;
            GlanceSubtextText.Text = _lyrics.IsLoading ? "Fetching LRCLIB lyrics..." : "Tap Live Lyrics to view";
        }
        else
        {
            GlanceHeadlineText.Text = DateTime.Now.ToString("dddd, MMM d");
            GlanceSubtextText.Text = "Chrome · Edge · Opera ready";
        }
    }

    private void RefreshTimelineOnly()
    {
        if (_music is null || _music.CurrentSession is null) return;
        _updatingUi = true;
        try
        {
            SeekSlider.Maximum = Math.Max(1, _music.CurrentSession.DurationSeconds);
            SeekSlider.Value = Math.Clamp(_music.LivePositionSeconds, 0, SeekSlider.Maximum);
            ElapsedTimeText.Text = _music.FormattedPosition;
            RemainingTimeText.Text = _music.FormattedDuration;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    public void Refresh()
    {
        UpdateClock();
        if (_music is null) return;
        _updatingUi = true;
        try
        {
            var session = _music.CurrentSession;
            Equalizer.IsActive = _music.IsPlaying;

            if (session is null)
            {
                if (_lastArtworkUri is not null)
                {
                    _lastArtworkUri = null;
                    AlbumArtwork.SourceUri = null;
                }
                SourceBadgeText.Text = _music.IsBrowserConnected
                    ? $"{_music.ConnectedBrowserName} · Ready"
                    : "Browser Media Disabled";
                TrackTitleText.Text = "No active browser media";
                TrackArtistText.Text = "Play in Chrome, Edge, or Opera";
                SeekSlider.Maximum = 100;
                SeekSlider.Value = 0;
                ElapsedTimeText.Text = "0:00";
                RemainingTimeText.Text = "0:00";
                PlayPauseIcon.Glyph = "\uE768";
            }
            else
            {
                if (!string.Equals(_lastArtworkUri, session.ArtworkUrl, StringComparison.Ordinal))
                {
                    _lastArtworkUri = session.ArtworkUrl;
                    AlbumArtwork.SourceUri = session.ArtworkUrl;
                }
                var host = string.IsNullOrWhiteSpace(session.UrlHost) ? session.Browser : session.UrlHost;
                SourceBadgeText.Text = $"{host} · {session.Browser}";
                TrackTitleText.Text = string.IsNullOrWhiteSpace(session.Title) ? session.TabTitle : session.Title;
                TrackArtistText.Text = string.IsNullOrWhiteSpace(session.Album)
                    ? session.Artist
                    : $"{session.Artist} · {session.Album}";

                SeekSlider.Maximum = Math.Max(1, session.DurationSeconds);
                SeekSlider.Value = Math.Clamp(_music.LivePositionSeconds, 0, SeekSlider.Maximum);
                ElapsedTimeText.Text = _music.FormattedPosition;
                RemainingTimeText.Text = _music.FormattedDuration;
                PlayPauseIcon.Glyph = _music.IsPlaying ? "\uE769" : "\uE768";

                PrevButton.IsEnabled = session.CanPrevious || session.CanSeek;
                NextButton.IsEnabled = session.CanNext;
                SeekSlider.IsEnabled = session.CanSeek;
            }

            if (!string.IsNullOrWhiteSpace(_music.LastCommandStatus))
            {
                CommandStatusText.Text = _music.LastCommandStatus;
                CommandStatusText.Visibility = Visibility.Visible;
            }
            else
            {
                CommandStatusText.Visibility = Visibility.Collapsed;
            }

            RebuildTabsStrip();
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void RebuildTabsStrip()
    {
        if (_music is null) return;
        BrowserTabsStrip.Children.Clear();
        var tabsSnapshot = _music.BrowserTabs.ToList();
        BrowserTabsRow.Visibility = tabsSnapshot.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var tab in tabsSnapshot)
        {
            var active = _music.CurrentSession?.TabId == tab.TabId;
            var label = string.IsNullOrWhiteSpace(tab.Title) ? tab.UrlHost : tab.Title;
            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = label,
                    MaxWidth = 170,
                    TextTrimming = TextTrimming.CharacterEllipsis
                },
                Padding = new Thickness(10, 4, 10, 4),
                CornerRadius = new CornerRadius(6),
                FontSize = 11.5,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(active ? Color.FromArgb(36, 255, 255, 255) : Color.FromArgb(12, 255, 255, 255)),
                Foreground = new SolidColorBrush(active ? Colors.White : Color.FromArgb(255, 161, 161, 170))
            };
            btn.Click += async (_, _) => await _music.FocusSourceTabAsync();
            BrowserTabsStrip.Children.Add(btn);
        }
    }

    private void RefreshVolume()
    {
        if (_system is null) return;
        _updatingUi = true;
        try
        {
            VolumeSlider.Value = _system.Metrics.IsMuted ? 0 : _system.Metrics.Volume;
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_music is not null) await _music.TogglePlayPauseAsync();
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (_music is not null) await _music.PreviousTrackAsync();
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_music is not null) await _music.NextTrackAsync();
    }

    private async void OnFocusTabClick(object sender, RoutedEventArgs e)
    {
        if (_music is not null) await _music.FocusSourceTabAsync();
    }

    private async void OnSeekCompleted(object sender, PointerRoutedEventArgs e)
    {
        if (_music is not null) await _music.SeekAsync(SeekSlider.Value);
    }

    private async void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingUi || _system is null) return;
        var vol = (int)Math.Round(e.NewValue);
        await _system.SetVolumeAsync(vol, vol == 0);
    }

    private async void OnToggleMuteClick(object sender, RoutedEventArgs e)
    {
        if (_system is not null) await _system.ToggleMuteAsync();
    }

    private void OnOpenLyricsClick(object sender, RoutedEventArgs e)
    {
        _island?.SetMode(IslandMode.Lyrics);
    }

    private void OnOpenFullscreenLyricsClick(object sender, RoutedEventArgs e)
    {
        if (_lyrics is not null) _lyrics.IsFullscreenOpen = true;
    }
}
