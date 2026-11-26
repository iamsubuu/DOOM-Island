using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.ViewModels;
using NSubstitute;
using NOVAIsland.Core.Interfaces;
using Xunit;

namespace NOVAIsland.Tests;

public sealed class AppConstantsTests
{
    [Fact]
    public void Defaults_ShouldReflectDoomIslandV2BrandingAndStealthDefaults()
    {
        Assert.Equal("DOOM Island", AppConstants.DefaultAppName);
        Assert.Equal("DOOM", AppConstants.DefaultShortName);
        Assert.Equal("2.3.1", AppConstants.AppVersion);
        Assert.Equal("com.doomisland.host", AppConstants.NativeMessagingHostName);

        var settings = new AppSettings();
        Assert.True(settings.General.StealthTopBar);
        Assert.True(settings.General.HoverToOpen);
        Assert.Equal(CollapseBehavior.MouseLeave, settings.General.CollapseBehavior);
        Assert.Equal(0, settings.Position.CustomY);
    }

    [Fact]
    public void IslandViewModel_StealthCollapse_ReturnsToCompactEvenWithActiveMedia()
    {
        var bridge = Substitute.For<IBrowserMediaBridge>();
        var lyrics = Substitute.For<ILyricsService>();
        var clipRepo = Substitute.For<IClipboardRepository>();
        var clipMon = Substitute.For<IClipboardMonitorService>();
        var notif = Substitute.For<INotificationListenerService>();
        var sys = Substitute.For<ISystemMonitorService>();
        var act = Substitute.For<IActivityMonitorService>();

        var music = new MusicViewModel(bridge);
        var vm = new IslandViewModel(
            music,
            new LyricsViewModel(lyrics, music),
            new ClipboardViewModel(clipRepo, clipMon),
            new NotificationsViewModel(notif),
            new TimerViewModel(),
            new SystemViewModel(sys),
            new ActivityViewModel(act));

        vm.SetMode(IslandMode.Expanded);
        vm.Collapse(hasActiveMedia: true, stealthMode: true);
        Assert.Equal(IslandMode.Compact, vm.Mode);
    }

    [Fact]
    public async Task SqliteSettingsStore_MigratesLegacyPre230PositionToTopCenter()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"doom_mig_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var db = new NOVAIsland.Infrastructure.Persistence.SqliteDbContext(tempDir);
            var store = new NOVAIsland.Infrastructure.Persistence.SqliteSettingsStore(db);

            var legacy = new AppSettings { SchemaVersion = 0 };
            legacy.Position.ScreenPosition = ScreenPosition.Custom;
            legacy.Position.CustomY = 24;
            legacy.General.StealthTopBar = false;
            legacy.General.CollapseBehavior = CollapseBehavior.OutsideClick;
            await store.SaveAsync(legacy);

            var loaded = await store.LoadAsync();
            Assert.Equal(240, loaded.SchemaVersion);
            Assert.Equal(ScreenPosition.TopCenter, loaded.Position.ScreenPosition);
            Assert.Equal(0, loaded.Position.CustomY);
            Assert.True(loaded.General.StealthTopBar);
            Assert.True(loaded.General.HoverToOpen);
            Assert.Equal(CollapseBehavior.MouseLeave, loaded.General.CollapseBehavior);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch { }
        }
    }
}
