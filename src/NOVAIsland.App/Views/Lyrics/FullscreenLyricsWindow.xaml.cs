using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.ViewModels;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using WinRT.Interop;

namespace NOVAIsland.App.Views.Lyrics;

public sealed partial class FullscreenLyricsWindow : Window
{
    private readonly LyricsViewModel _lyrics;
    private readonly MusicViewModel _music;
    private readonly SettingsViewModel _settings;
    private string? _lastArtworkUri;
    private string _renderedLinesSignature = string.Empty;
    private readonly List<TextBlock> _lineTextBlocks = [];
    private bool _isClosed;

    public FullscreenLyricsWindow(LyricsViewModel lyrics, MusicViewModel music, SettingsViewModel settings)
    {
        InitializeComponent();
        _lyrics = lyrics;
        _music = music;
        _settings = settings;

        SystemBackdrop = new DesktopAcrylicBackdrop();
        Title = $"{_settings.ResolvedAppName} · Fullscreen Lyrics";
        HeaderBrandText.Text = $"{_settings.ResolvedShortName} Lyrics";

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        Services.WindowIconHelper.TryApply(hwnd, appWindow);
        appWindow.SetPresenter(AppWindowPresenterKind.FullScreen);

        void OnLyricsChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_isClosed) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                if (e.PropertyName == nameof(LyricsViewModel.ActiveLineIndex))
                {
                    UpdateLineHighlightsInPlace();
                    ScrollActiveLineIntoCenter();
                }
                else
                {
                    RefreshLyrics();
                }
            });
        }

        void OnMusicChanged(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_isClosed) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_isClosed) return;
                if (e.PropertyName == nameof(MusicViewModel.LivePositionSeconds))
                {
                    RefreshTimelineOnly();
                }
                else
                {
                    RefreshTrack();
                }
            });
        }

        _lyrics.PropertyChanged += OnLyricsChanged;
        _music.PropertyChanged += OnMusicChanged;
        Closed += (_, _) =>
        {
            _isClosed = true;
            _lyrics.PropertyChanged -= OnLyricsChanged;
            _music.PropertyChanged -= OnMusicChanged;
        };

        RefreshTrack();
        RefreshLyrics();
    }

    private void RefreshTimelineOnly()
    {
        var s = _music.CurrentSession;
        SeekSlider.Maximum = Math.Max(1, s?.DurationSeconds ?? 100);
        SeekSlider.Value = Math.Clamp(_music.LivePositionSeconds, 0, SeekSlider.Maximum);
        ElapsedText.Text = _music.FormattedPosition;
        DurationText.Text = _music.FormattedDuration;
    }

    private void RefreshTrack()
    {
        var s = _music.CurrentSession;
        var nextArt = s?.ArtworkUrl;
        if (!string.Equals(_lastArtworkUri, nextArt, StringComparison.Ordinal))
        {
            _lastArtworkUri = nextArt;
            CoverArt.SourceUri = nextArt;
            BackdropImage.SourceUri = nextArt;
        }
        PlayIcon.Glyph = _music.IsPlaying ? "\uE769" : "\uE768";
        BackdropImage.Opacity = Math.Clamp(_settings.Settings.Lyrics.FullscreenBackdropOpacity / 160.0, 0.12, 0.65);

        TitleText.Text = s is null ? "No Active Media" : (string.IsNullOrWhiteSpace(s.Title) ? s.TabTitle : s.Title);
        ArtistText.Text = s?.Artist ?? "Play music in Chrome, Edge, or Opera";
        AlbumText.Text = s?.Album ?? string.Empty;

        RefreshTimelineOnly();
    }

    private void RefreshLyrics()
    {
        SourceLabel.Text = $"{_lyrics.Source} · {(_lyrics.IsSynced ? "Time-Synced LRC" : "Plain")}";
        RebuildLinesIfChanged();
        UpdateLineHighlightsInPlace();
        ScrollActiveLineIntoCenter();

        DrawerResults.Children.Clear();
        foreach (var item in _lyrics.SearchResults)
        {
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = item.Title, Foreground = new SolidColorBrush(Colors.White), FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(new TextBlock { Text = $"{item.Artist} · {item.Album}", Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170)), FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis });

            var btn = new Button { Content = "Use LRC", FontSize = 11.5 };
            var captured = item;
            btn.Click += (_, _) =>
            {
                _lyrics.ApplySearchResult(captured);
                SearchDrawer.Visibility = Visibility.Collapsed;
            };

            Grid.SetColumn(stack, 0);
            Grid.SetColumn(btn, 1);
            row.Children.Add(stack);
            row.Children.Add(btn);
            DrawerResults.Children.Add(row);
        }
    }

    private void RebuildLinesIfChanged()
    {
        var sig = _lyrics.Lines.Count == 0
            ? $"__empty_{_lyrics.IsInstrumental}__"
            : $"{_lyrics.TrackTitle}|{_lyrics.Source}|{_lyrics.Lines.Count}|{_lyrics.Lines[0].Text}";

        if (string.Equals(_renderedLinesSignature, sig, StringComparison.Ordinal))
        {
            return;
        }

        _renderedLinesSignature = sig;
        LyricsStreamPanel.Children.Clear();
        _lineTextBlocks.Clear();

        if (_lyrics.Lines.Count == 0)
        {
            LyricsStreamPanel.Children.Add(new TextBlock
            {
                Text = _lyrics.IsInstrumental ? "Instrumental track." : "No lyrics loaded. Open Manual LRCLIB Search.",
                FontSize = 22,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 161, 161, 170))
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
                BorderThickness = new Thickness(0)
            };
            var t = line.TimeSeconds;
            btn.Click += async (_, _) =>
            {
                if (_lyrics.IsSynced) await _music.SeekAsync(t);
            };
            LyricsStreamPanel.Children.Add(btn);
        }
    }

    private void UpdateLineHighlightsInPlace()
    {
        if (_lineTextBlocks.Count == 0) return;

        var activeSize = Math.Max(28, _settings.Settings.Lyrics.FontSize + 12);
        var inactiveSize = Math.Max(20, _settings.Settings.Lyrics.FontSize + 4);
        var activeBrush = new SolidColorBrush(Colors.White);
        var nearBrush = new SolidColorBrush(Color.FromArgb(255, 196, 204, 212));
        var farBrush = new SolidColorBrush(Color.FromArgb(255, 110, 118, 128));

        for (var i = 0; i < _lineTextBlocks.Count; i++)
        {
            var tb = _lineTextBlocks[i];
            var isActive = i == _lyrics.ActiveLineIndex;
            var dist = Math.Abs(i - _lyrics.ActiveLineIndex);

            tb.FontSize = isActive ? activeSize : inactiveSize;
            tb.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            tb.Foreground = isActive ? activeBrush : dist == 1 ? nearBrush : farBrush;
        }
    }

    private void ScrollActiveLineIntoCenter()
    {
        if (_lyrics.ActiveLineIndex < 0 || _lyrics.ActiveLineIndex >= LyricsStreamPanel.Children.Count) return;
        if (LyricsScrollViewer.ViewportHeight <= 0) return;

        if (LyricsStreamPanel.Children[_lyrics.ActiveLineIndex] is FrameworkElement fe)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    var pt = fe.TransformToVisual(LyricsStreamPanel).TransformPoint(new Point(0, 0));
                    var targetY = Math.Max(0, pt.Y - (LyricsScrollViewer.ViewportHeight / 2.0) + (fe.ActualHeight / 2.0));
                    LyricsScrollViewer.ChangeView(null, targetY, null, disableAnimation: !(_settings.Settings.Lyrics.SmoothScroll));
                }
                catch { }
            });
        }
    }

    private void OnToggleDrawerClick(object sender, RoutedEventArgs e)
    {
        SearchDrawer.Visibility = SearchDrawer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (SearchDrawer.Visibility == Visibility.Visible && string.IsNullOrWhiteSpace(DrawerQuery.Text))
        {
            DrawerQuery.Text = $"{_lyrics.TrackTitle} {_lyrics.ArtistName}".Trim();
        }
    }

    private async void OnDrawerSearchClick(object sender, RoutedEventArgs e)
    {
        _lyrics.SearchQuery = DrawerQuery.Text;
        await _lyrics.SearchManualAsync(!_settings.Settings.Privacy.LocalOnlyMode);
    }

    private async void OnDrawerKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            _lyrics.SearchQuery = DrawerQuery.Text;
            await _lyrics.SearchManualAsync(!_settings.Settings.Privacy.LocalOnlyMode);
        }
    }

    private async void OnSeekCompleted(object sender, PointerRoutedEventArgs e) =>
        await _music.SeekAsync(SeekSlider.Value);

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) => await _music.TogglePlayPauseAsync();
    private async void OnPrevClick(object sender, RoutedEventArgs e) => await _music.PreviousTrackAsync();
    private async void OnNextClick(object sender, RoutedEventArgs e) => await _music.NextTrackAsync();
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _lyrics.IsFullscreenOpen = false;
        Close();
    }
}
