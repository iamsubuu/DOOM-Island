using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using NOVAIsland.App.Services;
using NOVAIsland.App.Views.Dashboard;
using NOVAIsland.App.Views.Island;
using NOVAIsland.App.Views.Lyrics;
using NOVAIsland.App.Views.Settings;
using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.ViewModels;
using NOVAIsland.Infrastructure;
using NOVAIsland.Infrastructure.Caching;
using NOVAIsland.Infrastructure.Platform;
using NOVAIsland.Services;

namespace NOVAIsland.App;

public partial class App : Application
{
    private readonly IHost _host;
    private readonly Mutex _singleInstanceMutex;
    private readonly bool _ownsMutex;

    private DynamicIslandWindow? _islandWindow;
    private DashboardWindow? _dashboardWindow;
    private SettingsWindow? _settingsWindow;
    private FullscreenLyricsWindow? _fullscreenWindow;
    private CancellationTokenSource? _appCts;

    public App()
    {
        _singleInstanceMutex = new Mutex(true, AppConstants.SingleInstanceMutexName, out _ownsMutex);
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            TryWriteCrashLog(e.Exception);
            e.Handled = true;
        };

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                services.AddNovaServices();
                services.AddNovaInfrastructure();
                services.AddSingleton<ThemeManager>();
                services.AddSingleton<TrayHostService>();
                services.AddSingleton<ITrayService>(sp => sp.GetRequiredService<TrayHostService>());
            })
            .Build();
    }

    private static void TryWriteCrashLog(Exception ex)
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                AppConstants.AppDataFolderName,
                "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(
                Path.Combine(dir, "crash.log"),
                $"[{DateTimeOffset.UtcNow:O}] {ex}\n\n");
        }
        catch { }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
        if (!_ownsMutex)
        {
            Exit();
            return;
        }

        _appCts = new CancellationTokenSource();
        var sp = _host.Services;

        var settingsVm = sp.GetRequiredService<SettingsViewModel>();
        await settingsVm.LoadAsync(_appCts.Token);

        var themeManager = sp.GetRequiredService<ThemeManager>();
        themeManager.ApplyAppearance(settingsVm.Settings.Appearance);
        settingsVm.SettingsChanged += (_, _) =>
            DispatcherQueue.GetForCurrentThread()?.TryEnqueue(() =>
            {
                themeManager.ApplyAppearance(settingsVm.Settings.Appearance);
                sp.GetRequiredService<TrayHostService>().UpdateTooltip(settingsVm.ResolvedAppName);
                sp.GetRequiredService<CompositeBrowserMediaBridge>().NotifySettingsChanged();
            });

        var islandVm = sp.GetRequiredService<IslandViewModel>();
        await islandVm.Clipboard.LoadAsync(_appCts.Token);
        await islandVm.Notifications.InitializeAsync(_appCts.Token);
        await islandVm.System.RefreshAsync(_appCts.Token);

        // Wire real Clipboard monitor -> SQLite + ViewModel
        var clipMonitor = sp.GetRequiredService<IClipboardMonitorService>();
        clipMonitor.ItemCaptured += async (_, item) =>
        {
            await islandVm.Clipboard.RecordCapturedItemAsync(item, settingsVm.Settings.Clipboard.HistoryLimit);
        };
        clipMonitor.Start();

        // Wire real Windows Notifications
        var notifService = sp.GetRequiredService<INotificationListenerService>();
        notifService.NotificationReceived += (_, item) =>
        {
            islandVm.Notifications.AddOrUpdate(item);
            _islandWindow?.DispatcherQueue.TryEnqueue(() => _islandWindow.PulseNotification());
        };

        // Wire Activity monitor (Screenshots + Downloads)
        var activityMonitor = sp.GetRequiredService<IActivityMonitorService>();
        await activityMonitor.StartAsync(_appCts.Token);

        // Wire Browser Native Messaging Bridge -> Music + Auto-LRCLIB Lyrics
        var bridge = sp.GetRequiredService<IBrowserMediaBridge>();
        string? lastLyricsTrackKey = null;
        bridge.StateChanged += async (_, _) =>
        {
            var session = bridge.CurrentSession;
            var key = session is null ? null : $"{session.Artist}::{session.Title}";
            if (key != lastLyricsTrackKey)
            {
                lastLyricsTrackKey = key;
                await islandVm.Lyrics.LoadForCurrentTrackAsync(!settingsVm.Settings.Privacy.LocalOnlyMode);
                if (session is not null && islandVm.Mode == IslandMode.Compact && !settingsVm.Settings.General.StealthTopBar)
                {
                    _islandWindow?.DispatcherQueue.TryEnqueue(() => islandVm.SetMode(IslandMode.Music));
                }
            }
        };
        await bridge.StartAsync(_appCts.Token);

        // Wire WASAPI Loopback Audio Equalizer
        var visualizer = sp.GetRequiredService<IAudioVisualizerService>();
        visualizer.SpectrumUpdated += (_, bands) =>
        {
            if (settingsVm.Settings.Music.ShowAudioVisualizer && islandVm.Music.IsPlaying)
            {
                _islandWindow?.DispatcherQueue.TryEnqueue(() => _islandWindow.UpdateSpectrum(bands));
            }
        };
        visualizer.Start();

        // Create floating Dynamic Island window
        var positioner = sp.GetRequiredService<MultiMonitorPositioner>();
        _islandWindow = new DynamicIslandWindow(islandVm, settingsVm, positioner);
        _islandWindow.OpenDashboardRequested += (_, _) => OpenDashboard();
        _islandWindow.OpenSettingsRequested += (_, _) => OpenSettings(SettingsSectionId.General);
        _islandWindow.Activate();

        // Open Fullscreen Lyrics when requested by ViewModel
        islandVm.Lyrics.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LyricsViewModel.IsFullscreenOpen) && islandVm.Lyrics.IsFullscreenOpen)
            {
                _islandWindow.DispatcherQueue.TryEnqueue(OpenFullscreenLyrics);
            }
        };

        // Tray & Global Hotkeys & Lock Watcher
        var tray = sp.GetRequiredService<TrayHostService>();
        tray.OpenDashboardRequested += (_, _) => _islandWindow.DispatcherQueue.TryEnqueue(OpenDashboard);
        tray.OpenSettingsRequested += (_, _) => _islandWindow.DispatcherQueue.TryEnqueue(() => OpenSettings(SettingsSectionId.General));
        tray.OpenFullscreenLyricsRequested += (_, _) => _islandWindow.DispatcherQueue.TryEnqueue(OpenFullscreenLyrics);
        tray.SwitchModeRequested += (_, mode) => _islandWindow.DispatcherQueue.TryEnqueue(() => islandVm.SetMode(mode));
        tray.ToggleIslandVisibilityRequested += (_, _) => _islandWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            settingsVm.Settings.General.IslandEnabled = !settingsVm.Settings.General.IslandEnabled;
            await settingsVm.SaveAsync();
        });
        tray.ExitRequested += (_, _) => Shutdown();
        tray.Initialize(settingsVm.ResolvedAppName);

        var hotkeys = sp.GetRequiredService<IHotkeyService>();
        hotkeys.ShortcutTriggered += (_, action) => _islandWindow.DispatcherQueue.TryEnqueue(async () =>
        {
            switch (action)
            {
                case ShortcutAction.ToggleIsland: islandVm.ToggleExpand(islandVm.Music.HasActiveMedia, settingsVm.Settings.General.StealthTopBar); break;
                case ShortcutAction.OpenMusic: islandVm.SetMode(IslandMode.Expanded); break;
                case ShortcutAction.OpenLyrics: islandVm.SetMode(IslandMode.Lyrics); break;
                case ShortcutAction.OpenClipboard: islandVm.SetMode(IslandMode.Clipboard); break;
                case ShortcutAction.OpenTimer: islandVm.SetMode(IslandMode.Timer); break;
                case ShortcutAction.OpenDashboard: OpenDashboard(); break;
                case ShortcutAction.OpenSettings: OpenSettings(SettingsSectionId.General); break;
                case ShortcutAction.TriggerScreenshot: await islandVm.Activity.CaptureScreenshotCommand.ExecuteAsync(null); break;
            }
        });
        hotkeys.RegisterBindings(settingsVm.Settings.Shortcuts);

        var lockWatcher = sp.GetRequiredService<SessionLockWatcher>();
        lockWatcher.Start();

        // Start background timers (100ms stopwatch, 1s playback/lyrics/timer, system metrics refresh)
        StartBackgroundLoops(islandVm, settingsVm, _appCts.Token);

        // Show Dashboard on normal launch unless --minimized passed (e.g., Windows startup)
        if (!Environment.GetCommandLineArgs().Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase)))
        {
            OpenDashboard();
        }
        }
        catch (Exception ex)
        {
            TryWriteCrashLog(ex);
            throw;
        }
    }

    private void StartBackgroundLoops(IslandViewModel islandVm, SettingsViewModel settingsVm, CancellationToken ct)
    {
        var dq = _islandWindow!.DispatcherQueue;

        _ = Task.Run(async () =>
        {
            using var timer100ms = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
            var tickCount = 0;
            while (await timer100ms.WaitForNextTickAsync(ct))
            {
                tickCount++;
                dq.TryEnqueue(() =>
                {
                    islandVm.Timer.TickStopwatchTenth();
                    islandVm.Music.AdvancePlaybackByClock();
                    islandVm.Lyrics.UpdateActiveLine(islandVm.Music.LivePositionSeconds);
                    if (tickCount % 10 == 0)
                    {
                        islandVm.Timer.TickOneSecond();
                    }
                });

                if (tickCount % Math.Max(10, settingsVm.Settings.Widgets.RefreshIntervalMs / 100) == 0)
                {
                    await islandVm.System.RefreshAsync(ct);
                }
            }
        }, ct);
    }

    private void OpenDashboard()
    {
        var sp = _host.Services;
        if (_dashboardWindow is null)
        {
            _dashboardWindow = new DashboardWindow(sp.GetRequiredService<IslandViewModel>(), sp.GetRequiredService<SettingsViewModel>());
            _dashboardWindow.OpenSettingsSectionRequested += (_, sec) => OpenSettings(sec);
            _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
        }
        _dashboardWindow.Activate();
    }

    private void OpenSettings(SettingsSectionId section)
    {
        var sp = _host.Services;
        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(
                sp.GetRequiredService<SettingsViewModel>(),
                sp.GetRequiredService<IslandViewModel>(),
                sp.GetRequiredService<ISettingsStore>(),
                sp.GetRequiredService<IStartupService>(),
                sp.GetRequiredService<IHotkeyService>(),
                sp.GetRequiredService<IWindowPositioningService>(),
                sp.GetRequiredService<StorageFootprintService>(),
                sp.GetRequiredService<ILyricsCacheRepository>(),
                sp.GetRequiredService<IClipboardRepository>());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.NavigateTo(section);
        _settingsWindow.Activate();
    }

    private void OpenFullscreenLyrics()
    {
        var sp = _host.Services;
        if (_fullscreenWindow is null)
        {
            _fullscreenWindow = new FullscreenLyricsWindow(
                sp.GetRequiredService<LyricsViewModel>(),
                sp.GetRequiredService<MusicViewModel>(),
                sp.GetRequiredService<SettingsViewModel>());
            _fullscreenWindow.Closed += (_, _) =>
            {
                sp.GetRequiredService<LyricsViewModel>().IsFullscreenOpen = false;
                _fullscreenWindow = null;
            };
        }
        _fullscreenWindow.Activate();
    }

    private void Shutdown()
    {
        _appCts?.Cancel();
        _host.Services.GetRequiredService<TrayHostService>().DisposeTray();
        _host.Services.GetRequiredService<IHotkeyService>().UnregisterAll();
        _host.Services.GetRequiredService<IClipboardMonitorService>().Stop();
        _host.Services.GetRequiredService<IAudioVisualizerService>().Stop();
        if (_ownsMutex)
        {
            _singleInstanceMutex.ReleaseMutex();
        }
        _singleInstanceMutex.Dispose();
        Exit();
    }
}
