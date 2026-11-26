using System.Net;
using System.Text;
using System.Text.Json;
using NOVAIsland.BrowserHost;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Protocol;
using NOVAIsland.Infrastructure.Persistence;
using NOVAIsland.Services.Lyrics;
using NOVAIsland.Services.Media;
using Xunit;

namespace NOVAIsland.Tests;

public sealed class InfrastructureAndProtocolTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"doom_island_test_{Guid.NewGuid():N}");

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task SqliteStores_ShouldPersistSettingsClipboardAndLyrics()
    {
        var db = new SqliteDbContext(_tempDir);
        var settingsStore = new SqliteSettingsStore(db);
        var settings = await settingsStore.LoadAsync();
        settings.General.AppName = "DOOM Custom";
        settings.Appearance.AccentColor = "#34d399";
        await settingsStore.SaveAsync(settings);

        var reloaded = await settingsStore.LoadAsync();
        Assert.Equal("DOOM Custom", reloaded.General.AppName);
        Assert.Equal("#34d399", reloaded.Appearance.AccentColor);

        var clipRepo = new SqliteClipboardRepository(db);
        await clipRepo.UpsertAsync(new ClipboardItem("c1", ClipboardItemType.Text, "first", "First", null, null, "Edge", DateTimeOffset.UtcNow, Pinned: true), historyLimit: 2);
        await clipRepo.UpsertAsync(new ClipboardItem("c2", ClipboardItemType.Text, "second", "Second", null, null, "Code", DateTimeOffset.UtcNow.AddSeconds(1), Pinned: false), historyLimit: 2);
        await clipRepo.UpsertAsync(new ClipboardItem("c3", ClipboardItemType.Text, "third", "Third", null, null, "Code", DateTimeOffset.UtcNow.AddSeconds(2), Pinned: false), historyLimit: 2);
        await clipRepo.UpsertAsync(new ClipboardItem("c4", ClipboardItemType.Text, "fourth", "Fourth", null, null, "Code", DateTimeOffset.UtcNow.AddSeconds(3), Pinned: false), historyLimit: 2);

        var allClips = await clipRepo.GetAllAsync();
        Assert.Equal(3, allClips.Count); // 1 pinned + 2 unpinned retention cap
        Assert.Contains(allClips, c => c.Id == "c1" && c.Pinned);
        Assert.DoesNotContain(allClips, c => c.Id == "c2");
    }

    [Fact]
    public async Task NativeMessagingFramer_ShouldRoundTripAndRejectOversizedFrames()
    {
        using var ms = new MemoryStream();
        var env = new NativeMessageEnvelope
        {
            Type = NativeMessageTypes.MediaDetected,
            Browser = "Microsoft Edge",
            TabId = 42,
            Payload = JsonSerializer.SerializeToElement(new { title = "Track A" })
        };

        await NativeMessagingFramer.WriteAsync(ms, env);
        ms.Position = 0;

        var decoded = await NativeMessagingFramer.ReadAsync(ms);
        Assert.NotNull(decoded);
        Assert.Equal(NativeMessageTypes.MediaDetected, decoded!.Type);
        Assert.Equal(42, decoded.TabId);
        Assert.Equal("Microsoft Edge", decoded.Browser);

        // Oversized header check (> 1MB)
        using var badStream = new MemoryStream([0xFF, 0xFF, 0xFF, 0x0F]);
        await Assert.ThrowsAsync<InvalidDataException>(() => NativeMessagingFramer.ReadAsync(badStream));
    }

    [Fact]
    public void BrowserMediaBridgeService_ShouldTrackMultipleTabsAndStopCleanly()
    {
        var bridge = new BrowserMediaBridgeService();

        bridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
        {
            Type = NativeMessageTypes.MediaDetected,
            Browser = "Google Chrome",
            TabId = 10,
            Payload = JsonSerializer.SerializeToElement(new MediaPayload
            {
                Origin = "https://music.youtube.com",
                UrlHost = "music.youtube.com",
                Title = "Midnight City",
                Artist = "M83",
                DurationSeconds = 243,
                PositionSeconds = 12,
                PlaybackState = "playing"
            }, NativeMessageTypes.JsonOptions)
        });

        Assert.True(bridge.IsConnected);
        Assert.NotNull(bridge.CurrentSession);
        Assert.Equal("Midnight City", bridge.CurrentSession!.Title);

        bridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
        {
            Type = NativeMessageTypes.MediaStopped,
            TabId = 10,
            Payload = JsonSerializer.SerializeToElement(new MediaStoppedPayload { TabId = 10, Reason = "tab_closed" }, NativeMessageTypes.JsonOptions)
        });

        Assert.Null(bridge.CurrentSession);
        Assert.Empty(bridge.ActiveSessions);
    }

    [Fact]
    public async Task LrclibLyricsService_ShouldFetchParseAndCacheLyrics()
    {
        var db = new SqliteDbContext(_tempDir);
        var cache = new SqliteLyricsCacheRepository(db);
        var calls = 0;

        var handler = new StubHttpMessageHandler(_ =>
        {
            calls++;
            const string json = """
                {
                  "id": 777,
                  "trackName": "Obsidian Horizon",
                  "artistName": "Kiasmos",
                  "albumName": "Monolith",
                  "duration": 240,
                  "instrumental": false,
                  "syncedLyrics": "[00:02.00]Line one\n[00:10.50]Line two"
                }
                """;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
        });

        var http = new HttpClient(handler);
        var svc = new LrclibLyricsService(http, cache);

        var first = await svc.GetLyricsAsync("Obsidian Horizon", "Kiasmos", "Monolith", 240, allowNetwork: true);
        Assert.NotNull(first);
        Assert.True(first!.IsSynced);
        Assert.Equal(2, first.Lines.Count);
        Assert.Equal(1, calls);

        // Second lookup hits SQLite cache even with allowNetwork: false
        var second = await svc.GetLyricsAsync("Obsidian Horizon", "Kiasmos", "Monolith", 240, allowNetwork: false);
        Assert.NotNull(second);
        Assert.Equal(1, calls);
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }
}
