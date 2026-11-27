using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.ViewModels;
using Windows.Foundation;
using Windows.System;
using Windows.UI;

namespace NOVAIsland.App.Views.Island;

public sealed partial class LyricsPanelView : UserControl
{
    private LyricsViewModel? _lyrics;
    private MusicViewModel? _music;
    private SettingsViewModel? _settings;
    private string? _lastArtworkUri;
    private string _renderedLinesSignature = string.Empty;
    private string _lastSearchSeedTrackKey = string.Empty;
    private readonly List<TextBlock> _lineTextBlocks = [];

    public event EventHandler<TextBox>? TextInputRequested;

    public LyricsPanelView()
    {
        InitializeComponent();
        Loaded += (_, _) => ScrollActiveLineIntoCenter();
    }

    public void Bind(LyricsViewModel lyrics, MusicViewModel music, SettingsViewModel settings)
    {
        _lyrics = lyrics;
        _music = music;
        _settings = settings;

        _lyrics.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(LyricsViewModel.ActiveLineIndex))
            {
                UpdateLineHighlightsInPlace();
                ScrollActiveLineIntoCenter();
            }
            else
            {
                RefreshAll();
            }
        });
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

        RebuildLinesIfChanged();
        UpdateLineHighlightsInPlace();
        ScrollActiveLineIntoCenter();
        RebuildSearchResults();
    }

    private void RebuildLinesIfChanged()
    {
        if (_lyrics is null) return;
        var sig = _lyrics.IsLoading
            ? "__loading__"
            : _lyrics.Lines.Count == 0
                ? $"__empty_{_lyrics.IsInstrumental}__"
                : $"{_lyrics.TrackTitle}|{_lyrics.Source}|{_lyrics.Lines.Count}|{_lyrics.Lines[0].Text}";

        if (string.Equals(_renderedLinesSignature, sig, StringComparison.Ordinal))
        {
            return;
        }

        _renderedLinesSignature = sig;
        LyricsLinesPanel.Children.Clear();
        _lineTextBlocks.Clear();

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
                Foreground = new SolidColorBrush(Color.FromArgb(255, 132, 132, 146)),
                FontSize = 12.5
            });
            return;
        }

        for (var i = 0; i < _lyrics.Lines.Count; i++)
        {
            var line = _lyrics.Lines[i];
            var tb = new TextBlock
            {
                Text = line.Text,
                TextWrapping = TextWrapping.Wrap
            };
            _lineTextBlocks.Add(tb);

            var btn = new Button
            {
                Content = tb,
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
        }
    }

    private void UpdateLineHighlightsInPlace()
    {
        if (_lyrics is null || _lineTextBlocks.Count == 0) return;

        var baseFontSize = _settings?.Settings.Lyrics.FontSize ?? 16;
        var accent = (Brush)Application.Current.Resources["NovaAccentBrush"];
        var nearBrush = new SolidColorBrush(Color.FromArgb(255, 212, 216, 222));
        var farBrush = new SolidColorBrush(Color.FromArgb(255, 132, 138, 148));
        var activeBrush = new SolidColorBrush(Colors.White);

        for (var i = 0; i < _lineTextBlocks.Count; i++)
        {
            var tb = _lineTextBlocks[i];
            var isActive = i == _lyrics.ActiveLineIndex;
            var dist = Math.Abs(i - _lyrics.ActiveLineIndex);

            tb.FontSize = isActive ? baseFontSize : Math.Max(12.5, baseFontSize - 2);
            tb.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            tb.Foreground = isActive ? activeBrush : dist == 1 ? nearBrush : farBrush;

            if (LyricsLinesPanel.Children[i] is Button btn)
            {
                btn.BorderThickness = isActive ? new Thickness(2, 0, 0, 0) : new Thickness(0);
                btn.BorderBrush = isActive ? accent : null;
            }
        }
    }

    private void ScrollActiveLineIntoCenter()
    {
        if (_lyrics is null || _lyrics.ActiveLineIndex < 0 || _lyrics.ActiveLineIndex >= LyricsLinesPanel.Children.Count)
        {
            return;
        }

        if (LyricsScrollViewer.Visibility != Visibility.Visible || LyricsScrollViewer.ViewportHeight <= 0)
        {
            return;
        }

        if (LyricsLinesPanel.Children[_lyrics.ActiveLineIndex] is FrameworkElement fe)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var pt = fe.TransformToVisual(LyricsLinesPanel).TransformPoint(new Point(0, 0));
                    var targetY = Math.Max(0, pt.Y - (LyricsScrollViewer.ViewportHeight / 2.0) + (fe.ActualHeight / 2.0));
                    LyricsScrollViewer.ChangeView(null, targetY, null, disableAnimation: !(_settings?.Settings.Lyrics.SmoothScroll ?? true));
                }
                catch { }
            });
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
        if (_lyrics.IsSearchOpen)
        {
            var currentKey = LyricsViewModel.BuildTrackKey(_music?.CurrentSession);
            if (string.IsNullOrWhiteSpace(SearchInput.Text) ||
                !string.Equals(_lastSearchSeedTrackKey, currentKey, StringComparison.Ordinal))
            {
                _lastSearchSeedTrackKey = currentKey;
                SearchInput.Text = $"{_lyrics.TrackTitle} {_lyrics.ArtistName}".Trim();
            }

            SearchPanel.Visibility = Visibility.Visible;
            LyricsScrollViewer.Visibility = Visibility.Collapsed;
            SearchToggleLabel.Text = "Close Search";
            TextInputRequested?.Invoke(this, SearchInput);
        }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e) => await RunSearchAsync();

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            await RunSearchAsync();
        }
        else if (e.Key == VirtualKey.Escape && _lyrics is not null && _lyrics.IsSearchOpen)
        {
            _lyrics.IsSearchOpen = false;
        }
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
