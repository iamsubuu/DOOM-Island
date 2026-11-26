using System.Runtime.Versioning;
using Microsoft.Extensions.DependencyInjection;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.ViewModels;
using NOVAIsland.Services.Lyrics;
using NOVAIsland.Services.Media;

namespace NOVAIsland.Services;

public static class ServiceCollectionExtensions
{
    [SupportedOSPlatform("windows")]
    public static IServiceCollection AddNovaServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<BrowserMediaBridgeService>();
        services.AddSingleton<IBrowserMediaBridge>(sp => sp.GetRequiredService<BrowserMediaBridgeService>());

        services.AddHttpClient<ILyricsService, LrclibLyricsService>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(8);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DOOMIsland/2.3 (+https://github.com/doom-island)");
        });

        // Core MVVM ViewModels
        services.AddSingleton<TimerViewModel>();
        services.AddSingleton<ClipboardViewModel>();
        services.AddSingleton<MusicViewModel>();
        services.AddSingleton<LyricsViewModel>();
        services.AddSingleton<NotificationsViewModel>();
        services.AddSingleton<SystemViewModel>();
        services.AddSingleton<ActivityViewModel>();
        services.AddSingleton<IslandViewModel>();
        services.AddSingleton<SettingsViewModel>();

        return services;
    }
}
