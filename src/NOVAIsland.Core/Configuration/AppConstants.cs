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
    public const string AppVersion = "2.3.0";
    public const string AppDataFolderName = "DOOMIsland";
    public const string DatabaseFileName = "doom_island.db";
    public const string NativeMessagingHostName = "com.doomisland.host";
    public const string DefaultBrowserExtensionId = "amplhmapoeandecddkchambamhafdcgp";
    public const string PipeNamePrefix = "DOOMIsland.BrowserBridge";
    public const string SingleInstanceMutexName = "Local\\DOOMIsland.SingleInstance.Mutex";
}

