namespace NOVAIsland.Core.Configuration;

/// <summary>
/// Centralized application identity and storage defaults.
/// Mirrors <c>src/config/appConfig.ts</c> so the entire application can be rebranded
/// from one location or via General Settings.
/// </summary>
public static class AppConstants
{
    public const string DefaultAppName = "DOOM Island";
    public const string DefaultShortName = "DOOM";
    public const string PublisherName = "ArcSubash";
    public const string StorePackageName = "ArcSubash.DOOMIsland-DynamicIsland";
    public const string StorePackagePublisher = "CN=AAFF5C79-617E-404B-893B-24B04B60D21C";
    public const string StorePackageFamilyName = "ArcSubash.DOOMIsland-DynamicIsland_vafw60knffsyr";
    public const string StorePackageSid = "S-1-15-2-1756612410-509526401-1413686036-2906866903-3647860765-3449130613-1752073460";
    public const string AppVersion = "2.5.2";
    public const string AppDataFolderName = "DOOMIsland";
    public const string DatabaseFileName = "doom_island.db";
    public const string NativeMessagingHostName = "com.doomisland.host";
    public const string DefaultBrowserExtensionId = "amplhmapoeandecddkchambamhafdcgp";
    public const string PipeNamePrefix = "DOOMIsland.BrowserBridge";
    public const string SingleInstanceMutexName = "Local\\DOOMIsland.SingleInstance.Mutex";
    public const string ShowDashboardEventName = "Local\\DOOMIsland.ShowDashboard";
}

