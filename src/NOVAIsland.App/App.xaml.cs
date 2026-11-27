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
using NOVAIsland.Core.Services;
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
    private EventWaitHandle? _showDashboardSignal;
    private RegisteredWaitHandle? _showDashboardWait;
    private bool _isDashboardVisible;
    private bool _isSessionLocked;
    private bool _visualizerRunning;
    private bool _isShuttingDown;

    public App()
    {
        _singleInstanceMutex = new Mutex(true, AppConstants.SingleInstanceMutexName, out _ownsMutex);
        InitializeComponent();

        UnhandledException += (_, e) =>
        {
            TryWriteCrashLog(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                TryWriteCrashLog(ex);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            TryWriteCrashLog(e.Exception);
            e.SetObserved();
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
            if (!IsLaunchedFromWindowsStartup())
            {
                try
                {
                    using var signal = EventWaitHandle.OpenExisting(AppConstants.ShowDashboardEventName);
                    signal.Set();
                }
                catch { }
            }
            Exit();
            return;
        }

        _appCts = new CancellationTokenSource();
        var uiQueue = DispatcherQueue.GetForCurrentThread();
        NovaUiDispatcher.Initialize(action =>
        {
            if (uiQueue is null || _isShuttingDown) return;
            if (uiQueue.HasThreadAccess)
            {
                try { action(); } catch (Exception ex) { TryWriteCrashLog(ex); }
            }
            else
            {
                try
                {
                    uiQueue.TryEnqueue(() =>
                    {
                        try { action(); } catch (Exception ex) { TryWriteCrashLog(ex); }
                    });
                }
                catch (Exception ex)
                {
                    TryWriteCrashLog(ex);
                }
            }
        });
        var sp = _host.Services;

        var settingsVm = sp.GetRequiredService<SettingsViewModel>();
        await settingsVm.LoadAsync(_appCts.Token);
        var startupService = sp.GetRequiredService<IStartupService>();
        if (settingsVm.Settings.General.LaunchOnStartup && !startupService.IsStartupEnabled())
        {
            startupService.SetStartupEnabled(true);
        }

        var themeManager = sp.GetRequiredService<ThemeManager>();
        themeManager.ApplyAppearance(settingsVm.Settings.Appearance);
        settingsVm.SettingsChanged += (_, _) =>
            SafeUiEnqueue(() =>
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
            try
            {
                await islandVm.Clipboard.RecordCapturedItemAsync(item, settingsVm.Settings.Clipboard.HistoryLimit);
            }
            catch (Exception ex)
            {
                TryWriteCrashLog(ex);
            }
        };
        clipMonitor.Start();

        // Wire real Windows Notifications (marshal ViewModel mutations onto UI thread)
        var notifService = sp.GetRequiredService<INotificationListenerService>();
        notifService.NotificationReceived += (_, item) =>
        {
            islandVm.Notifications.AddOrUpdate(item);
            SafeUiEnqueue(() => _islandWindow?.PulseNotification());
        };
        notifService.NotificationRemoved += (_, nativeId) =>
        {
            islandVm.Notifications.RemoveByPlatformId(nativeId);
        };

        // Wire Activity monitor (Screenshots + Downloads)
        var activityMonitor = sp.GetRequiredService<IActivityMonitorService>();
        await activityMonitor.StartAsync(_appCts.Token);

        // Wire Media Bridge -> MusicViewModel (single source of truth) -> Auto-LRCLIB Lyrics
        var bridge = sp.GetRequiredService<IBrowserMediaBridge>();
        string? lastLyricsTrackKey = null;
        islandVm.Music.TrackChanged += async (_, session) =>
        {
            try
            {
                var key = LyricsViewModel.BuildTrackKey(session);
                if (!string.Equals(key, lastLyricsTrackKey, StringComparison.Ordinal))
                {
                    lastLyricsTrackKey = key;
                    if (session is not null && islandVm.Mode == IslandMode.Compact && !settingsVm.Settings.General.StealthTopBar)
                    {
                        islandVm.SetMode(IslandMode.Music);
                    }
                    await islandVm.Lyrics.LoadForSessionAsync(
                        session,
                        !settingsVm.Settings.Privacy.LocalOnlyMode,
                        _appCts?.Token ?? CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                TryWriteCrashLog(ex);
            }
        };
        var compositeBridge = sp.GetRequiredService<CompositeBrowserMediaBridge>();
        compositeBridge.ArtworkPaletteChanged += (_, p) =>
        {
            SafeUiEnqueue(() =>
            {
                themeManager.UpdateArtworkPalette(p.R1, p.G1, p.B1, p.R2, p.G2, p.B2);
                if (settingsVm.Settings.Appearance.Theme == ThemePreset.ArtworkDynamic)
                {
                    themeManager.ApplyAppearance(settingsVm.Settings.Appearance);
                }
            });
        };

        await bridge.StartAsync(_appCts.Token);

        // Wire WASAPI Loopback Audio Equalizer (on-demand for power/CPU efficiency)
        var visualizer = sp.GetRequiredService<IAudioVisualizerService>();
        visualizer.SpectrumUpdated += (_, bands) =>
        {
            if (!_isSessionLocked && settingsVm.Settings.Music.ShowAudioVisualizer && islandVm.Music.IsPlaying)
            {
                var copy = bands.ToArray();
                SafeUiEnqueue(() => _islandWindow?.UpdateSpectrum(copy));
            }
        };

        void SyncVisualizerPowerState()
        {
            var shouldRun = !_isSessionLocked
                && settingsVm.Settings.Music.ShowAudioVisualizer
                && islandVm.Music.IsPlaying
                && (!settingsVm.Settings.General.StealthTopBar || islandVm.Mode != IslandMode.Compact);

            if (shouldRun && !_visualizerRunning)
            {
                visualizer.Start();
                _visualizerRunning = true;
            }
            else if (!shouldRun && _visualizerRunning)
            {
                visualizer.Stop();
                _visualizerRunning = false;
            }
        }

        islandVm.PropertyChanged += (_, _) => SyncVisualizerPowerState();
        islandVm.Music.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MusicViewModel.IsPlaying) or nameof(MusicViewModel.CurrentSession))
            {
                SyncVisualizerPowerState();
            }
        };
        settingsVm.SettingsChanged += (_, _) => SyncVisualizerPowerState();

        // Create floating Dynamic Island window
        var positioner = sp.GetRequiredService<MultiMonitorPositioner>();
        _islandWindow = new DynamicIslandWindow(islandVm, settingsVm, positioner, themeManager);
        _islandWindow.OpenDashboardRequested += (_, _) => OpenDashboard();
        _islandWindow.OpenSettingsRequested += (_, _) => OpenSettings(SettingsSectionId.General);
        HideOnCloseInsteadOfExit(_islandWindow, hideWindow: false);
        _islandWindow.Activate();

        // Listen for second-instance "show dashboard" requests when launched from Start Menu / Desktop
        try
        {
            _showDashboardSignal = new EventWaitHandle(false, EventResetMode.AutoReset, AppConstants.ShowDashboardEventName);
            _showDashboardWait = ThreadPool.RegisterWaitForSingleObject(
                _showDashboardSignal,
                (_, _) => _islandWindow?.DispatcherQueue.TryEnqueue(OpenDashboard),
                null,
                Timeout.Infinite,
                executeOnlyOnce: false);
        }
        catch { }

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
        tray.OpenDashboardRequested += (_, _) => SafeUiEnqueue(OpenDashboard);
        tray.OpenSettingsRequested += (_, _) => SafeUiEnqueue(() => OpenSettings(SettingsSectionId.General));
        tray.OpenFullscreenLyricsRequested += (_, _) => SafeUiEnqueue(OpenFullscreenLyrics);
        tray.SwitchModeRequested += (_, mode) => SafeUiEnqueue(() => islandVm.SetMode(mode));
        tray.ToggleIslandVisibilityRequested += (_, _) => SafeUiEnqueueAsync(async () =>
        {
            settingsVm.Settings.General.IslandEnabled = !settingsVm.Settings.General.IslandEnabled;
            await settingsVm.SaveAsync();
        });
        tray.ExitRequested += (_, _) => Shutdown();
        try
        {
            tray.Initialize(settingsVm.ResolvedAppName);
        }
        catch (Exception ex)
        {
            TryWriteCrashLog(ex);
        }

        var hotkeys = sp.GetRequiredService<IHotkeyService>();
        hotkeys.ShortcutTriggered += (_, action) => SafeUiEnqueueAsync(async () =>
        {
            switch (action)
            {
                case ShortcutAction.ToggleIsland:
                case ShortcutAction.OpenMusic:
                case ShortcutAction.OpenLyrics:
                case ShortcutAction.OpenClipboard:
                case ShortcutAction.OpenTimer:
                    if (_islandWindow is not null)
                    {
                        await _islandWindow.HandleShortcutAsync(action);
                    }
                    break;
                case ShortcutAction.OpenDashboard:
                    OpenDashboard();
                    break;
                case ShortcutAction.OpenSettings:
                    OpenSettings(SettingsSectionId.General);
                    break;
                case ShortcutAction.TriggerScreenshot:
                    await islandVm.Activity.CaptureScreenshotCommand.ExecuteAsync(null);
                    break;
            }
        });
        hotkeys.RegisterBindings(settingsVm.Settings.Shortcuts);

        var lockWatcher = sp.GetRequiredService<SessionLockWatcher>();
        lockWatcher.LockStateChanged += (_, locked) =>
        {
            _isSessionLocked = locked;
            SyncVisualizerPowerState();
            SafeUiEnqueue(() => _islandWindow?.SetSessionLocked(locked));
        };
        lockWatcher.Start();

        // Start adaptive power-saving background loop
        StartBackgroundLoops(islandVm, settingsVm, _appCts.Token);

        // Show Dashboard only on explicit interactive launch; Windows startup shows only the stealth bar
        if (!IsLaunchedFromWindowsStartup())
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

    private static bool IsLaunchedFromWindowsStartup()
    {
        if (Environment.GetCommandLineArgs().Any(a =>
            a.Equals("--minimized", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--background", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("--startup", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        try
        {
            var activated = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
            return activated.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    private void SafeUiEnqueue(Action action)
    {
        if (_isShuttingDown || _islandWindow is null) return;
        try
        {
            _islandWindow.DispatcherQueue.TryEnqueue(() =>
            {
                if (_isShuttingDown) return;
                try { action(); } catch (Exception ex) { TryWriteCrashLog(ex); }
            });
        }
        catch (Exception ex)
        {
            TryWriteCrashLog(ex);
        }
    }

    private void SafeUiEnqueueAsync(Func<Task> action)
    {
        SafeUiEnqueue(async () =>
        {
            try { await action(); } catch (Exception ex) { TryWriteCrashLog(ex); }
        });
    }

    private void StartBackgroundLoops(IslandViewModel islandVm, SettingsViewModel settingsVm, CancellationToken ct)
    {
        var dq = _islandWindow!.DispatcherQueue;
        var lastSecondTicks = Environment.TickCount64;
        var lastSystemRefreshTicks = Environment.TickCount64;
        var lastWatchdogTicks = Environment.TickCount64;
        var wasSystemPanelActive = false;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var stopwatchRunning = islandVm.Timer.IsStopwatchRunning;
                    var oneSecondTimerRunning = islandVm.Timer.IsCountdownRunning || islandVm.Timer.IsPomodoroRunning;
                    var visualPlaybackActive = islandVm.Music.IsPlaying && (
                        _isDashboardVisible ||
                        _fullscreenWindow is not null ||
                        !settingsVm.Settings.General.StealthTopBar ||
                        islandVm.Mode is IslandMode.Music or IslandMode.Expanded or IslandMode.Lyrics);

                    var needsFastTick = !_isSessionLocked && (stopwatchRunning || visualPlaybackActive);
                    await Task.Delay(needsFastTick ? 100 : 1000, ct);

                    var now = Environment.TickCount64;
                    var elapsedSecond = now - lastSecondTicks >= 1000;
                    if (elapsedSecond)
                    {
                        lastSecondTicks = now;
                    }

                    var clockVisible = !_isSessionLocked && islandVm.Mode == IslandMode.Expanded;
                    if (stopwatchRunning || islandVm.Music.IsPlaying || (elapsedSecond && (oneSecondTimerRunning || clockVisible)))
                    {
                        dq.TryEnqueue(() =>
                        {
                            try
                            {
                                if (stopwatchRunning)
                                {
                                    islandVm.Timer.TickStopwatchTenth();
                                }
                                if (islandVm.Music.IsPlaying)
                                {
                                    islandVm.Music.AdvancePlaybackByClock();
                                    islandVm.Lyrics.UpdateActiveLine(islandVm.Music.LivePositionSeconds);
                                }
                                if (elapsedSecond)
                                {
                                    if (oneSecondTimerRunning)
                                    {
                                        islandVm.Timer.TickOneSecond();
                                    }
                                    if (clockVisible)
                                    {
                                        _islandWindow?.UpdateClock();
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                TryWriteCrashLog(ex);
                            }
                        });
                    }

                    var systemUiVisible = !_isSessionLocked && (_isDashboardVisible || islandVm.Mode == IslandMode.System);
                    var refreshMs = systemUiVisible
                        ? Math.Clamp(settingsVm.Settings.Widgets.RefreshIntervalMs, 1500, 10000)
                        : 30000;

                    if ((systemUiVisible && !wasSystemPanelActive) || (now - lastSystemRefreshTicks >= refreshMs && !_isSessionLocked))
                    {
                        lastSystemRefreshTicks = now;
                        await islandVm.System.RefreshAsync(ct);
                    }
                    wasSystemPanelActive = systemUiVisible;

                    if (now - lastWatchdogTicks >= 5000 && !_isSessionLocked)
                    {
                        lastWatchdogTicks = now;
                        SafeUiEnqueue(() => _islandWindow?.EnsureStealthVisibleAndTopmost());
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    TryWriteCrashLog(ex);
                }
            }
        }, ct);
    }

    private void HideOnCloseInsteadOfExit(Window window, bool hideWindow)
    {
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
            appWindow.Closing += (sender, args) =>
            {
                if (_isShuttingDown)
                {
                    return;
                }

                args.Cancel = true;
                if (hideWindow)
                {
                    if (ReferenceEquals(window, _dashboardWindow))
                    {
                        _isDashboardVisible = false;
                    }
                    sender.Hide();
                }
            };
        }
        catch (Exception ex)
        {
            TryWriteCrashLog(ex);
        }
    }

    private void OpenDashboard()
    {
        var sp = _host.Services;
        if (_dashboardWindow is null)
        {
            _dashboardWindow = new DashboardWindow(sp.GetRequiredService<IslandViewModel>(), sp.GetRequiredService<SettingsViewModel>());
            _dashboardWindow.OpenSettingsSectionRequested += (_, sec) => OpenSettings(sec);
            HideOnCloseInsteadOfExit(_dashboardWindow, hideWindow: true);
            _dashboardWindow.Closed += (_, _) =>
            {
                _isDashboardVisible = false;
                _dashboardWindow = null;
            };
        }
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_dashboardWindow);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            appWindow.Show(true);
        }
        catch { }
        _isDashboardVisible = true;
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
                sp.GetRequiredService<IClipboardRepository>(),
                sp.GetRequiredService<ThemeManager>());
            HideOnCloseInsteadOfExit(_settingsWindow, hideWindow: true);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        try
        {
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_settingsWindow);
            var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd));
            appWindow.Show(true);
        }
        catch { }
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
        _isShuttingDown = true;
        NovaUiDispatcher.Reset();
        _appCts?.Cancel();
        _showDashboardWait?.Unregister(null);
        _showDashboardWait = null;
        _showDashboardSignal?.Dispose();
        _showDashboardSignal = null;
        _islandWindow?.PrepareForShutdown();
        _host.Services.GetRequiredService<TrayHostService>().DisposeTray();
        _host.Services.GetRequiredService<IHotkeyService>().UnregisterAll();
        _host.Services.GetRequiredService<IClipboardMonitorService>().Stop();
        _host.Services.GetRequiredService<IAudioVisualizerService>().Stop();
        try { _dashboardWindow?.Close(); } catch { }
        try { _settingsWindow?.Close(); } catch { }
        try { _fullscreenWindow?.Close(); } catch { }
        try { _islandWindow?.Close(); } catch { }
        if (_ownsMutex)
        {
            _singleInstanceMutex.ReleaseMutex();
        }
        _singleInstanceMutex.Dispose();
        Exit();
    }
}
