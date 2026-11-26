using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.ViewModels;
using Windows.System;
using Windows.UI;

namespace NOVAIsland.App.Views.Island;

public sealed partial class LyricsPanelView : UserControl
{
    private LyricsViewModel? _lyrics;
    private MusicViewModel? _music;
    private SettingsViewModel? _settings;
    private string? _lastArtworkUri;

    public LyricsPanelView()
    {
        InitializeComponent();
    }

    public void Bind(LyricsViewModel lyrics, MusicViewModel music, SettingsViewModel settings)
    {
        _lyrics = lyrics;
        _music = music;
        _settings = settings;

        _lyrics.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(RefreshAll);
        _music.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(MusicViewModel.LivePositionSeconds))
            {
                ProgressText.Text = $"{_music.FormattedPosition} / {_music.FormattedDuration}";
            }
            else
            {
                RefreshHeaderAndTime();
            }
        });
        RefreshAll();
    }

    private void RefreshHeaderAndTime()
    {
        if (_music is null) return;
        var nextArt = _music.CurrentSession?.ArtworkUrl;
        if (!string.Equals(_lastArtworkUri, nextArt, StringComparison.Ordinal))
        {
            _lastArtworkUri = nextArt;
            MiniArt.SourceUri = nextArt;
        }
        ProgressText.Text = $"{_music.FormattedPosition} / {_music.FormattedDuration}";
        MiniPlayIcon.Glyph = _music.IsPlaying ? "\uE769" : "\uE768";
    }

    private void RefreshAll()
    {
        if (_lyrics is null || _music is null) return;
        RefreshHeaderAndTime();

        TrackTitleText.Text = string.IsNullOrWhiteSpace(_lyrics.TrackTitle) ? "No Active Track" : _lyrics.TrackTitle;
        var syncState = _lyrics.IsInstrumental ? "Instrumental" : _lyrics.IsSynced ? "Synced LRC" : "Plain Text";
        MetaText.Text = $"{_lyrics.ArtistName} · {_lyrics.Source} · {syncState}";

        SearchPanel.Visibility = _lyrics.IsSearchOpen ? Visibility.Visible : Visibility.Collapsed;
        LyricsScrollViewer.Visibility = _lyrics.IsSearchOpen ? Visibility.Collapsed : Visibility.Visible;
        SearchToggleLabel.Text = _lyrics.IsSearchOpen ? "Close Search" : "LRCLIB Search";

        RebuildLines();
        RebuildSearchResults();
    }

    private void RebuildLines()
    {
        if (_lyrics is null) return;
        LyricsLinesPanel.Children.Clear();

        if (_lyrics.IsLoading)
        {
            LyricsLinesPanel.Children.Add(new TextBlock
            {
                Text = "Fetching lyrics from LRCLIB...",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170)),
                FontSize = 13
            });
            return;
        }

        if (_lyrics.Lines.Count == 0)
        {
            LyricsLinesPanel.Children.Add(new TextBlock
            {
                Text = _lyrics.IsInstrumental ? "Instrumental track." : "No lyrics available. Try manual LRCLIB search.",
                Foreground = new SolidColorBrush(Color.FromArgb(255, 113, 113, 122)),
                FontSize = 12.5
            });
            return;
        }

        var baseFontSize = _settings?.Settings.Lyrics.FontSize ?? 16;
        UIElement? activeElement = null;

        for (var i = 0; i < _lyrics.Lines.Count; i++)
        {
            var line = _lyrics.Lines[i];
            var isActive = i == _lyrics.ActiveLineIndex;
            var dist = Math.Abs(i - _lyrics.ActiveLineIndex);

            var btn = new Button
            {
                Content = new TextBlock
                {
                    Text = line.Text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = isActive ? baseFontSize : Math.Max(12.5, baseFontSize - 2),
                    FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(
                        isActive ? Colors.White :
                        dist == 1 ? Color.FromArgb(255, 200, 200, 210) :
                        Color.FromArgb(255, 132, 132, 146))
                },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Padding = new Thickness(6, 4, 6, 4)
            };

            var capturedTime = line.TimeSeconds;
            btn.Click += async (_, _) =>
            {
                if (_music is not null && _lyrics.IsSynced)
                {
                    await _music.SeekAsync(capturedTime);
                }
            };

            LyricsLinesPanel.Children.Add(btn);
            if (isActive) activeElement = btn;
        }

        if (activeElement is FrameworkElement fe)
        {
            fe.StartBringIntoView(new BringIntoViewOptions { AnimationDesired = _settings?.Settings.Lyrics.SmoothScroll ?? true });
        }
    }

    private void RebuildSearchResults()
    {
        if (_lyrics is null) return;
        SearchResultsPanel.Children.Clear();
        foreach (var item in _lyrics.SearchResults)
        {
            var row = new Grid { ColumnSpacing = 8, Padding = new Thickness(4, 6, 4, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = item.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(Colors.White),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stack.Children.Add(new TextBlock
            {
                Text = $"{item.Artist} · {(item.IsSynced ? "Synced" : "Plain")}",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var applyBtn = new Button { Content = "Apply", FontSize = 11, Padding = new Thickness(10, 4, 10, 4) };
            var captured = item;
            applyBtn.Click += (_, _) => _lyrics.ApplySearchResult(captured);

            Grid.SetColumn(stack, 0);
            Grid.SetColumn(applyBtn, 1);
            row.Children.Add(stack);
            row.Children.Add(applyBtn);
            SearchResultsPanel.Children.Add(row);
        }
    }

    private void OnToggleSearchClick(object sender, RoutedEventArgs e)
    {
        if (_lyrics is null) return;
        _lyrics.IsSearchOpen = !_lyrics.IsSearchOpen;
        if (_lyrics.IsSearchOpen && string.IsNullOrWhiteSpace(SearchInput.Text))
        {
            SearchInput.Text = $"{_lyrics.TrackTitle} {_lyrics.ArtistName}".Trim();
        }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter) await RunSearchAsync();
    }

    private async Task RunSearchAsync()
    {
        if (_lyrics is null) return;
        _lyrics.SearchQuery = SearchInput.Text;
        var allowNet = !(_settings?.Settings.Privacy.LocalOnlyMode ?? false);
        await _lyrics.SearchManualAsync(allowNet);
    }

    private void OnOpenFullscreenClick(object sender, RoutedEventArgs e)
    {
        if (_lyrics is not null) _lyrics.IsFullscreenOpen = true;
    }

    private async void OnPrevClick(object sender, RoutedEventArgs e)
    {
        if (_music is not null) await _music.PreviousTrackAsync();
    }

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e)
    {
        if (_music is not null) await _music.TogglePlayPauseAsync();
    }

    private async void OnNextClick(object sender, RoutedEventArgs e)
    {
        if (_music is not null) await _music.NextTrackAsync();
    }
}
