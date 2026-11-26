using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.ViewModels;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace NOVAIsland.App.Views.Dashboard;

public sealed partial class DashboardWindow : Window
{
    private readonly IslandViewModel _island;
    private readonly SettingsViewModel _settings;
    private bool _updatingUi;
    private string? _lastArtworkUri;

    public event EventHandler<SettingsSectionId>? OpenSettingsSectionRequested;

    public DashboardWindow(IslandViewModel island, SettingsViewModel settings)
    {
        InitializeComponent();
        _island = island;
        _settings = settings;

        SystemBackdrop = new DesktopAcrylicBackdrop();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Resize(new SizeInt32(1140, 760));

        SizeChanged += OnWindowSizeChanged;
        UpdateResponsiveLayout(Bounds.Width);

        _island.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(HighlightActiveStateButton);
        _island.Music.PropertyChanged += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            if (e.PropertyName == nameof(MusicViewModel.LivePositionSeconds))
            {
                DashProgress.Value = _island.Music.ProgressPercentage;
                DashElapsed.Text = _island.Music.FormattedPosition;
                DashDuration.Text = _island.Music.FormattedDuration;
            }
            else
            {
                Refresh();
            }
        });
        _island.Timer.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        _island.System.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        _island.Clipboard.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        _island.Notifications.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        _island.Activity.PropertyChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);
        _settings.SettingsChanged += (_, _) => DispatcherQueue.TryEnqueue(Refresh);

        Refresh();
    }

    private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs args) =>
        UpdateResponsiveLayout(args.Size.Width);

    private void UpdateResponsiveLayout(double width)
    {
        if (width <= 0) return;

        var isNarrowHeader = width < 640;
        Grid.SetColumn(HeroActionsPanel, isNarrowHeader ? 0 : 1);
        Grid.SetRow(HeroActionsPanel, isNarrowHeader ? 1 : 0);
        HeroActionsPanel.HorizontalAlignment = isNarrowHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        Grid.SetColumn(StateQuickActions, isNarrowHeader ? 0 : 1);
        Grid.SetRow(StateQuickActions, isNarrowHeader ? 1 : 0);
        StateQuickActions.HorizontalAlignment = isNarrowHeader ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        var isCompactTransport = width < 560;
        Grid.SetColumn(DashTransportPanel, isCompactTransport ? 0 : 2);
        Grid.SetRow(DashTransportPanel, isCompactTransport ? 1 : 0);
        Grid.SetColumnSpan(DashTransportPanel, isCompactTransport ? 3 : 1);
        DashTransportPanel.HorizontalAlignment = isCompactTransport ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        var stackOverview = width < 880;
        OverviewCol0.Width = stackOverview ? new GridLength(1, GridUnitType.Star) : new GridLength(7, GridUnitType.Star);
        OverviewCol1.Width = stackOverview ? new GridLength(0) : new GridLength(5, GridUnitType.Star);
        Grid.SetColumn(LiveControlsCard, stackOverview ? 0 : 1);
        Grid.SetRow(LiveControlsCard, stackOverview ? 1 : 0);

        if (stackOverview)
        {
            CardsCol2.Width = new GridLength(0);
            CardsCol3.Width = new GridLength(0);
            Grid.SetColumn(CardClip, 0);
            Grid.SetRow(CardClip, 1);
            Grid.SetColumn(CardActivity, 1);
            Grid.SetRow(CardActivity, 1);
        }
        else
        {
            CardsCol2.Width = new GridLength(1, GridUnitType.Star);
            CardsCol3.Width = new GridLength(1, GridUnitType.Star);
            Grid.SetColumn(CardClip, 2);
            Grid.SetRow(CardClip, 0);
            Grid.SetColumn(CardActivity, 3);
            Grid.SetRow(CardActivity, 0);
        }

        FeedsCol1.Width = stackOverview ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Grid.SetColumn(RecentActivityCard, stackOverview ? 0 : 1);
        Grid.SetRow(RecentActivityCard, stackOverview ? 1 : 0);
    }

    private void HighlightActiveStateButton()
    {
        var activeBg = (Brush)Application.Current.Resources["IslandControlActiveBrush"];
        var activeFg = (Brush)Application.Current.Resources["WindowTextPrimaryBrush"];
        var inactiveFg = (Brush)Application.Current.Resources["WindowTextSecondaryBrush"];
        var transparent = new SolidColorBrush(Colors.Transparent);

        foreach (var child in StateButtonsPanel.Children)
        {
            if (child is Button btn && btn.Tag is string tag && Enum.TryParse<IslandMode>(tag, out var mode))
            {
                var active = mode == _island.Mode;
                btn.Background = active ? activeBg : transparent;
                btn.Foreground = active ? activeFg : inactiveFg;
                btn.FontWeight = active ? FontWeights.SemiBold : FontWeights.Medium;
            }
        }
    }

    private void Refresh()
    {
        _updatingUi = true;
        try
        {
            var appName = _settings.ResolvedAppName;
            Title = appName;
            TitleBarBrandText.Text = appName;
            HeroTitle.Text = $"{appName} Dashboard";

            var s = _settings.Settings;
            IslandVisibleToggle.IsOn = s.General.IslandEnabled;
            BrowserBridgeToggle.IsOn = s.BrowserIntegration.Enabled;
            LrclibToggle.IsOn = s.Lyrics.LrclibFallback;
            ClipboardToggle.IsOn = s.Clipboard.Enabled;
            DndToggle.IsOn = s.General.FocusDndMode;

            var m = _island.Music.CurrentSession;
            NowPlayingMeta.Text = m is null
                ? (_island.Music.IsBrowserConnected ? $"{_island.Music.ConnectedBrowserName} · Ready" : "Browser Media · Disabled")
                : $"{m.UrlHost} · {m.Browser} · {m.PlaybackState}";
            if (!string.Equals(_lastArtworkUri, m?.ArtworkUrl, StringComparison.Ordinal))
            {
                _lastArtworkUri = m?.ArtworkUrl;
                DashArtwork.SourceUri = _lastArtworkUri;
            }
            DashPlayIcon.Glyph = _island.Music.IsPlaying ? "\uE769" : "\uE768";
            DashTitle.Text = m is null ? "No Active Browser Media" : (string.IsNullOrWhiteSpace(m.Title) ? m.TabTitle : m.Title);
            DashArtist.Text = m?.Artist ?? "Start playback in Chrome, Edge, or Opera";
            DashAlbum.Text = m?.Album ?? string.Empty;
            DashProgress.Value = _island.Music.ProgressPercentage;
            DashElapsed.Text = _island.Music.FormattedPosition;
            DashDuration.Text = _island.Music.FormattedDuration;

            var sys = _island.System.Metrics;
            CardSystemValue.Text = $"CPU {sys.CpuUsage}% · RAM {sys.RamUsage}%";
            CardTimerValue.Text = $"{_island.Timer.DisplayTime} · {_island.Timer.PhaseOrModeLabel}";
            CardClipValue.Text = $"{_island.Clipboard.TotalCount} Saved ({_island.Clipboard.PinnedCount} pinned)";
            CardActivityValue.Text = $"{_island.Activity.ActiveCount} Active · {_island.Notifications.ActiveCount} Alerts";

            RecentClipboardList.Children.Clear();
            foreach (var clip in _island.Clipboard.FilteredItems.Take(4))
            {
                RecentClipboardList.Children.Add(new TextBlock
                {
                    Text = $"{clip.PreviewTitle ?? clip.Type.ToString()} — {clip.SourceApp}",
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["WindowTextSecondaryBrush"]
                });
            }

            RecentActivityList.Children.Clear();
            foreach (var act in _island.Activity.Items.Take(3))
            {
                RecentActivityList.Children.Add(new TextBlock
                {
                    Text = $"{act.Title} ({act.Progress:0}%)",
                    FontSize = 12.5,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = (Brush)Application.Current.Resources["WindowTextSecondaryBrush"]
                });
            }

            HighlightActiveStateButton();
        }
        finally
        {
            _updatingUi = false;
        }
    }

    private void OnStateButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tag && Enum.TryParse<IslandMode>(tag, out var mode))
        {
            _island.SetMode(mode);
        }
    }

    private void OnOpenAppearanceClick(object sender, RoutedEventArgs e) =>
        OpenSettingsSectionRequested?.Invoke(this, SettingsSectionId.Appearance);

    private void OnOpenSettingsClick(object sender, RoutedEventArgs e) =>
        OpenSettingsSectionRequested?.Invoke(this, SettingsSectionId.General);

    private void OnJumpLyricsClick(object sender, RoutedEventArgs e) =>
        _island.SetMode(IslandMode.Lyrics);

    private void OnOpenFullscreenLyricsClick(object sender, RoutedEventArgs e) =>
        _island.Lyrics.IsFullscreenOpen = true;

    private async void OnCaptureSnipClick(object sender, RoutedEventArgs e) =>
        await _island.Activity.CaptureScreenshotCommand.ExecuteAsync(null);

    private async void OnPlayPauseClick(object sender, RoutedEventArgs e) => await _island.Music.TogglePlayPauseAsync();
    private async void OnPrevClick(object sender, RoutedEventArgs e) => await _island.Music.PreviousTrackAsync();
    private async void OnNextClick(object sender, RoutedEventArgs e) => await _island.Music.NextTrackAsync();

    private async void OnIslandVisibleToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        _settings.Settings.General.IslandEnabled = IslandVisibleToggle.IsOn;
        await _settings.SaveAsync();
    }

    private async void OnBrowserBridgeToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        _settings.Settings.BrowserIntegration.Enabled = BrowserBridgeToggle.IsOn;
        await _settings.SaveAsync();
    }

    private async void OnLrclibToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        _settings.Settings.Lyrics.LrclibFallback = LrclibToggle.IsOn;
        await _settings.SaveAsync();
    }

    private async void OnClipboardToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        _settings.Settings.Clipboard.Enabled = ClipboardToggle.IsOn;
        await _settings.SaveAsync();
    }

    private async void OnDndToggled(object sender, RoutedEventArgs e)
    {
        if (_updatingUi) return;
        _settings.Settings.General.FocusDndMode = DndToggle.IsOn;
        await _settings.SaveAsync();
    }
}
