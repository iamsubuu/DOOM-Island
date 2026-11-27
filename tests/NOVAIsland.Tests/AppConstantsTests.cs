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
        Assert.Equal("ArcSubash", AppConstants.PublisherName);
        Assert.Equal("ArcSubash.DOOMIsland-DynamicIsland", AppConstants.StorePackageName);
        Assert.Equal("CN=AAFF5C79-617E-404B-893B-24B04B60D21C", AppConstants.StorePackagePublisher);
        Assert.Equal("2.5.2", AppConstants.AppVersion);
        Assert.Equal("com.doomisland.host", AppConstants.NativeMessagingHostName);

        var settings = new AppSettings();
        Assert.True(settings.General.StealthTopBar);
        Assert.True(settings.General.HoverToOpen);
        Assert.True(settings.General.LaunchOnStartup);
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
            Assert.Equal(244, loaded.SchemaVersion);
            Assert.Equal(ScreenPosition.TopCenter, loaded.Position.ScreenPosition);
            Assert.Equal(0, loaded.Position.CustomY);
            Assert.True(loaded.General.StealthTopBar);
            Assert.True(loaded.General.HoverToOpen);
            Assert.True(loaded.General.LaunchOnStartup);
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

    [Fact]
    public void SystemNotificationReader_ParsesSystemToastsAndExcludesBrowserHandlers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"doom_wpn_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var dbPath = Path.Combine(tempDir, "wpndatabase.db");
        try
        {
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = """
                    CREATE TABLE NotificationHandler (RecordId INTEGER PRIMARY KEY, PrimaryId TEXT);
                    CREATE TABLE Notification (
                        Id INTEGER PRIMARY KEY,
                        HandlerId INTEGER,
                        Type TEXT,
                        Payload BLOB,
                        ExpiryTime INTEGER,
                        ArrivalTime INTEGER
                    );
                    INSERT INTO NotificationHandler (RecordId, PrimaryId) VALUES (1, 'Microsoft.ScreenSketch_8wekyb3d8bbwe!App');
                    INSERT INTO NotificationHandler (RecordId, PrimaryId) VALUES (2, 'Chrome');
                    INSERT INTO NotificationHandler (RecordId, PrimaryId) VALUES (3, 'Microsoft.Windows.SecHealthUI_8wekyb3d8bbwe!SecHealthUI');
                    """;
                cmd.ExecuteNonQuery();

                var nowFt = DateTimeOffset.UtcNow.ToFileTime();
                var futureFt = DateTimeOffset.UtcNow.AddMinutes(10).ToFileTime();
                var pastFt = DateTimeOffset.UtcNow.AddMinutes(-10).ToFileTime();

                using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT INTO Notification (Id, HandlerId, Type, Payload, ExpiryTime, ArrivalTime) VALUES (@id, @h, 'toast', @p, @exp, @arr)";
                ins.Parameters.AddWithValue("@id", 101);
                ins.Parameters.AddWithValue("@h", 1);
                ins.Parameters.AddWithValue("@p", System.Text.Encoding.UTF8.GetBytes("<toast><visual><binding template=\"ToastGeneric\"><text>Screenshot captured</text><text>Saved to Pictures</text></binding></visual></toast>\0"));
                ins.Parameters.AddWithValue("@exp", futureFt);
                ins.Parameters.AddWithValue("@arr", nowFt);
                ins.ExecuteNonQuery();

                ins.Parameters["@id"].Value = 102;
                ins.Parameters["@h"].Value = 2; // Chrome browser toast -> must be excluded
                ins.Parameters["@p"].Value = System.Text.Encoding.UTF8.GetBytes("<toast><visual><binding template=\"ToastGeneric\"><text>AI Studio</text><text>Browser alert</text></binding></visual></toast>");
                ins.Parameters["@exp"].Value = futureFt;
                ins.Parameters["@arr"].Value = nowFt;
                ins.ExecuteNonQuery();

                ins.Parameters["@id"].Value = 103;
                ins.Parameters["@h"].Value = 3; // Expired system toast -> must be excluded
                ins.Parameters["@p"].Value = System.Text.Encoding.UTF8.GetBytes("<toast><visual><binding template=\"ToastGeneric\"><text>Old scan</text></binding></visual></toast>");
                ins.Parameters["@exp"].Value = pastFt;
                ins.Parameters["@arr"].Value = nowFt;
                ins.ExecuteNonQuery();
            }

            var items = NOVAIsland.Infrastructure.Platform.WinRtNotificationListenerService.ReadSystemToastsFromSqlite(dbPath);
            var single = Assert.Single(items);
            Assert.Equal("Snipping Tool", single.AppName);
            Assert.Equal("Screenshot captured", single.Title);
            Assert.Equal("Saved to Pictures", single.Body);
            Assert.Equal((uint)101, single.PlatformNotificationId);
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

    [Fact]
    public void ArtworkAndThemes_UpgradeLowResCdnUrlsAndExtractGradientAndMapPresets()
    {
        Assert.Equal(
            "https://i.ytimg.com/vi/abc123XYZ/maxresdefault.jpg",
            NOVAIsland.Infrastructure.Platform.ArtworkEnhancementService.UpgradeKnownCdnUrl("https://i.ytimg.com/vi/abc123XYZ/hqdefault.jpg"));
        Assert.Equal(
            "https://is1-ssl.mzstatic.com/image/thumb/Music/600x600bb.jpg",
            NOVAIsland.Infrastructure.Platform.ArtworkEnhancementService.UpgradeKnownCdnUrl("https://is1-ssl.mzstatic.com/image/thumb/Music/100x100bb.jpg"));

        // 2 vibrant color regions: magenta + cyan
        var pixels = new byte[4 * 4];
        // Pixel 0 & 1: vibrant magenta (#FF2E93 -> B=147,G=46,R=255,A=255)
        pixels[0] = 147; pixels[1] = 46; pixels[2] = 255; pixels[3] = 255;
        pixels[4] = 147; pixels[5] = 46; pixels[6] = 255; pixels[7] = 255;
        // Pixel 2: cyan (#22D3EE -> B=238,G=211,R=34,A=255)
        pixels[8] = 238; pixels[9] = 211; pixels[10] = 34; pixels[11] = 255;
        pixels[12] = 238; pixels[13] = 211; pixels[14] = 34; pixels[15] = 255;

        var grad = NOVAIsland.Infrastructure.Platform.ArtworkEnhancementService.ComputeGradientFromBgraPixels(pixels);
        Assert.NotNull(grad);
        Assert.NotEqual((grad!.Value.R1, grad.Value.G1, grad.Value.B1), (grad.Value.R2, grad.Value.G2, grad.Value.B2));

        var venomCfg = NOVAIsland.Core.Services.SpringPhysicsConfigMapper.FromPreset(AnimationPreset.VenomLiquid, 1.0, false);
        var zeroG = NOVAIsland.Core.Services.SpringPhysicsConfigMapper.FromPreset(AnimationPreset.ZeroGravity, 1.0, false);
        Assert.False(venomCfg.Instant);
        Assert.True(zeroG.Period > venomCfg.Period);
    }

    [Fact]
    public async Task NovaUiDispatcher_MarshalsBackgroundUpdatesOntoDispatcherBeforeMutatingCollections()
    {
        var queued = new System.Collections.Concurrent.ConcurrentQueue<Action>();
        using var scope = NOVAIsland.Core.Services.NovaUiDispatcher.ScopedOverrideForTests(a => queued.Enqueue(a));
        var bridge = Substitute.For<IBrowserMediaBridge>();
        bridge.IsConnected.Returns(true);
        bridge.ActiveSessions.Returns(
        [
            new NOVAIsland.Core.Models.BrowserMediaSession
            {
                TabId = 1,
                Title = "Queued Track",
                Artist = "ArcSubash",
                PlaybackState = BrowserPlaybackState.Playing
            }
        ]);

        var music = new MusicViewModel(bridge);
        await Task.Run(() => music.SyncFromBridge());

        // Mutation must NOT happen off-thread before UI dispatcher drains queue
        Assert.Empty(music.BrowserTabs);
        Assert.NotEmpty(queued);

        while (queued.TryDequeue(out var act))
        {
            act();
        }

        var item = Assert.Single(music.BrowserTabs);
        Assert.Equal("Queued Track", item.Title);
    }

    [Fact]
    public void CompositeBrowserMediaBridge_ResolvesLocalSystemPlayersAndBrowsers()
    {
        var spotify = NOVAIsland.Infrastructure.Platform.CompositeBrowserMediaBridge.TryResolveMediaIdentity("Spotify.exe");
        Assert.NotNull(spotify);
        Assert.Equal("Spotify", spotify!.DisplayName);
        Assert.False(spotify.IsBrowser);

        var zune = NOVAIsland.Infrastructure.Platform.CompositeBrowserMediaBridge.TryResolveMediaIdentity("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic");
        Assert.NotNull(zune);
        Assert.Equal("Media Player", zune!.DisplayName);
        Assert.False(zune.IsBrowser);

        var vlc = NOVAIsland.Infrastructure.Platform.CompositeBrowserMediaBridge.TryResolveMediaIdentity(@"C:\Program Files\VideoLAN\VLC\vlc.exe");
        Assert.NotNull(vlc);
        Assert.Equal("VLC Media Player", vlc!.DisplayName);
        Assert.False(vlc.IsBrowser);

        var edge = NOVAIsland.Infrastructure.Platform.CompositeBrowserMediaBridge.TryResolveMediaIdentity("MSEdge");
        Assert.NotNull(edge);
        Assert.Equal("Microsoft Edge", edge!.DisplayName);
        Assert.True(edge.IsBrowser);

        Assert.Null(NOVAIsland.Infrastructure.Platform.CompositeBrowserMediaBridge.TryResolveMediaIdentity("DOOMIsland.exe"));
    }

    [Fact]
    public async Task LyricsViewModel_CancelsSupersededTrackLoadAndMatchesActiveTrack()
    {
        using var scope = NOVAIsland.Core.Services.NovaUiDispatcher.ScopedOverrideForTests(a => a());
        var bridge = Substitute.For<IBrowserMediaBridge>();
        var firstDelayTcs = new TaskCompletionSource<LyricsSearchResult?>();
        var lyricsService = Substitute.For<ILyricsService>();

        lyricsService.GetLyricsAsync("Rathinamo", "Sony Music South", Arg.Any<string?>(), Arg.Any<double>(), true, Arg.Any<CancellationToken>())
            .Returns(ci => firstDelayTcs.Task.WaitAsync(ci.Arg<CancellationToken>()));

        var secondLyrics = new LyricsSearchResult(
            Title: "Aachaley",
            Artist: "Sean Roldan, Mohan Rajan",
            Album: null,
            DurationSeconds: 205,
            Source: LyricsSourceKind.Lrclib,
            IsSynced: true,
            IsInstrumental: false,
            Lines: [new LyricLine(10, "Aachaley line 1")],
            RawLrc: "[00:10.00]Aachaley line 1");
        lyricsService.GetLyricsAsync("Aachaley", "Sean Roldan, Mohan Rajan", Arg.Any<string?>(), Arg.Any<double>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<LyricsSearchResult?>(secondLyrics));

        var music = new MusicViewModel(bridge);
        var lyrics = new LyricsViewModel(lyricsService, music);
        NOVAIsland.Core.Models.BrowserMediaSession? changedTo = null;
        music.TrackChanged += (_, s) => changedTo = s;

        var track1 = new NOVAIsland.Core.Models.BrowserMediaSession
        {
            TabId = -1001,
            Title = "Rathinamo",
            Artist = "Sony Music South",
            PlaybackState = BrowserPlaybackState.Playing
        };
        bridge.CurrentSession.Returns(track1);
        bridge.ActiveSessions.Returns([track1]);
        music.SyncFromBridge();
        Assert.Same(track1, changedTo);

        var load1 = lyrics.LoadForSessionAsync(track1, allowNetwork: true);

        var track2 = new NOVAIsland.Core.Models.BrowserMediaSession
        {
            TabId = -1001,
            Title = "Aachaley",
            Artist = "Sean Roldan, Mohan Rajan",
            PlaybackState = BrowserPlaybackState.Playing
        };
        bridge.CurrentSession.Returns(track2);
        bridge.ActiveSessions.Returns([track2]);
        music.SyncFromBridge();
        Assert.Same(track2, changedTo);

        await lyrics.LoadForSessionAsync(track2, allowNetwork: true);
        firstDelayTcs.TrySetResult(new LyricsSearchResult("Rathinamo", "Sony Music South", null, 180, LyricsSourceKind.Lrclib, true, false, [new LyricLine(1, "Old line")], null));
        await load1;

        Assert.Equal("Aachaley", lyrics.TrackTitle);
        Assert.Equal("Sean Roldan, Mohan Rajan", lyrics.ArtistName);
        var line = Assert.Single(lyrics.Lines);
        Assert.Equal("Aachaley line 1", line.Text);
        Assert.False(lyrics.IsLoading);
    }

    [Fact]
    public void CompositeBrowserMediaBridge_PrefersPlayingTitledSessionOverIdleDuplicate()
    {
        var playingSession = new NOVAIsland.Core.Models.BrowserMediaSession
        {
            TabId = -1001,
            Browser = "Google Chrome",
            Title = "Bombay Ponnu",
            Artist = "Vijay Antony",
            DurationSeconds = 234,
            PlaybackState = BrowserPlaybackState.Playing,
            IsActiveBrowserTab = true
        };
        var emptyZombie = new NOVAIsland.Core.Models.BrowserMediaSession
        {
            TabId = -1001,
            Browser = "Google Chrome",
            Title = string.Empty,
            PlaybackState = BrowserPlaybackState.None,
            IsActiveBrowserTab = false
        };

        Assert.True(NOVAIsland.Infrastructure.Platform.CompositeBrowserMediaBridge.ShouldPreferSmtcCandidate(playingSession, emptyZombie));
        Assert.False(NOVAIsland.Infrastructure.Platform.CompositeBrowserMediaBridge.ShouldPreferSmtcCandidate(emptyZombie, playingSession));
    }
}
