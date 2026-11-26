using Microsoft.UI;
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

    public MusicPanelView()
    {
        InitializeComponent();
    }

    public void Bind(MusicViewModel music, SystemViewModel system, IslandViewModel island, LyricsViewModel lyrics)
    {
        _music = music;
        _system = system;
        _island = island;
        _lyrics = lyrics;

        _music.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        _system.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshVolume);
        Refresh();
        RefreshVolume();
    }

    public void UpdateSpectrum(ReadOnlySpan<float> bands) => Equalizer.UpdateSpectrum(bands);

    public void Refresh()
    {
        if (_music is null) return;
        _updatingUi = true;
        try
        {
            var session = _music.CurrentSession;
            Equalizer.IsActive = _music.IsPlaying;

            if (session is null)
            {
                AlbumArtwork.SourceUri = null;
                SourceBadgeText.Text = _music.IsBrowserConnected
                    ? $"{_music.ConnectedBrowserName} · Ready"
                    : "Browser Media Disabled";
                TrackTitleText.Text = "No active browser media";
                TrackArtistText.Text = "Play audio or video in Chrome, Edge, or Opera";
                SeekSlider.Maximum = 100;
                SeekSlider.Value = 0;
                ElapsedTimeText.Text = "0:00";
                RemainingTimeText.Text = "-0:00 / 0:00";
                PlayPauseIcon.Glyph = "\uE768";
            }
            else
            {
                AlbumArtwork.SourceUri = session.ArtworkUrl;
                var host = string.IsNullOrWhiteSpace(session.UrlHost) ? session.Browser : session.UrlHost;
                var stateLabel = session.PlaybackState == BrowserPlaybackState.Playing ? "Playing" : "Paused";
                SourceBadgeText.Text = $"{host} · {stateLabel} · {session.Browser}";
                TrackTitleText.Text = string.IsNullOrWhiteSpace(session.Title) ? session.TabTitle : session.Title;
                TrackArtistText.Text = string.IsNullOrWhiteSpace(session.Album)
                    ? session.Artist
                    : $"{session.Artist} · {session.Album}";

                SeekSlider.Maximum = Math.Max(1, session.DurationSeconds);
                SeekSlider.Value = Math.Clamp(_music.LivePositionSeconds, 0, SeekSlider.Maximum);
                ElapsedTimeText.Text = _music.FormattedPosition;
                RemainingTimeText.Text = $"{_music.FormattedRemaining} / {_music.FormattedDuration}";
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
        foreach (var tab in _music.BrowserTabs)
        {
            var active = _music.CurrentSession?.TabId == tab.TabId;
            var btn = new Button
            {
                Content = string.IsNullOrWhiteSpace(tab.Title) ? tab.UrlHost : tab.Title,
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
        if (_updatingUi || _music is null) return;
        await _music.SeekAsync(SeekSlider.Value);
    }

    private async void OnVolumeChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (_updatingUi || _system is null) return;
        var val = (int)Math.Round(e.NewValue);
        await _system.SetVolumeAsync(val, val == 0);
    }

    private async void OnToggleMuteClick(object sender, RoutedEventArgs e)
    {
        if (_system is not null) await _system.ToggleMuteAsync();
    }

    private void OnOpenLyricsClick(object sender, RoutedEventArgs e) =>
        _island?.SetMode(IslandMode.Lyrics);

    private void OnOpenFullscreenLyricsClick(object sender, RoutedEventArgs e)
    {
        if (_lyrics is not null) _lyrics.IsFullscreenOpen = true;
    }
}
