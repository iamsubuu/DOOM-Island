using Microsoft.Extensions.DependencyInjection;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.ViewModels;
using NOVAIsland.Infrastructure.Caching;
using NOVAIsland.Infrastructure.Persistence;
using NOVAIsland.Infrastructure.Platform;
using NOVAIsland.Services.Media;
using Serilog;

namespace NOVAIsland.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNovaInfrastructure(this IServiceCollection services, bool enableFileLogs = false)
    {
        ArgumentNullException.ThrowIfNull(services);

        var dbContext = new SqliteDbContext();
        services.AddSingleton(dbContext);
        services.AddSingleton<ISettingsStore, SqliteSettingsStore>();
        services.AddSingleton<IClipboardRepository, SqliteClipboardRepository>();
        services.AddSingleton<ILyricsCacheRepository, SqliteLyricsCacheRepository>();
        services.AddSingleton<StorageFootprintService>();

        services.AddSingleton<MultiMonitorPositioner>();
        services.AddSingleton<IWindowPositioningService>(sp => sp.GetRequiredService<MultiMonitorPositioner>());
        services.AddSingleton<IClipboardMonitorService>(sp =>
            new Win32ClipboardMonitor(
                sp.GetRequiredService<StorageFootprintService>(),
                () => sp.GetRequiredService<SettingsViewModel>().Settings));
        services.AddSingleton<IStartupService, StartupRegistrationService>();
        services.AddSingleton<SessionLockWatcher>();
        services.AddSingleton<IHotkeyService, GlobalHotkeyService>();
        services.AddSingleton<INotificationListenerService, WinRtNotificationListenerService>();
        services.AddSingleton<ISystemMonitorService, WindowsSystemMonitorService>();
        services.AddSingleton<IAudioVisualizerService, WasapiAudioVisualizerService>();
        services.AddSingleton<IActivityMonitorService, WindowsActivityMonitorService>();
        services.AddSingleton<CompositeBrowserMediaBridge>(sp =>
            new CompositeBrowserMediaBridge(
                sp.GetRequiredService<BrowserMediaBridgeService>(),
                sp.GetRequiredService<SqliteDbContext>(),
                () => sp.GetRequiredService<SettingsViewModel>().Settings.BrowserIntegration,
                () => sp.GetRequiredService<SettingsViewModel>().Settings.Privacy.LocalOnlyMode));
        services.AddSingleton<IBrowserMediaBridge>(sp => sp.GetRequiredService<CompositeBrowserMediaBridge>());

        if (enableFileLogs)
        {
            var logPath = Path.Combine(dbContext.DataDirectory, "logs", "doom-island-.log");
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 7)
                .CreateLogger();
        }

        return services;
    }
}
