using System.Text.Json;
using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Protocol;
using NOVAIsland.Infrastructure.Persistence;
using NOVAIsland.Infrastructure.Platform;
using NOVAIsland.Services.Media;
using Xunit;

namespace NOVAIsland.Tests;

public sealed class CompositeBrowserMediaBridgeTests
{
    [Fact]
    public async Task CompositeBridge_RespectsDisabledSettingAndSurfacesExtensionSession()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"doom_bridge_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = new BrowserIntegrationSettings { Enabled = true };
            var db = new SqliteDbContext(Path.Combine(tempDir, "test.db"));
            await using var extBridge = new BrowserMediaBridgeService();
            await using var composite = new CompositeBrowserMediaBridge(extBridge, db, () => settings);

            extBridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.BrowserConnected,
                Browser = "Opera",
                Payload = JsonSerializer.SerializeToElement(new BrowserConnectedPayload
                {
                    ExtensionVersion = "1.0.0",
                    BrowserName = "Opera"
                }, NativeMessageTypes.JsonOptions)
            });

            extBridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.MediaDetected,
                Browser = "Opera",
                TabId = 42,
                WindowId = 1,
                Payload = JsonSerializer.SerializeToElement(new MediaPayload
                {
                    Origin = "https://music.youtube.com",
                    UrlHost = "music.youtube.com",
                    TabTitle = "YouTube Music",
                    Title = "Test Track",
                    Artist = "Test Artist",
                    PlaybackState = "playing",
                    IsAudible = true,
                    IsActiveBrowserTab = true
                }, NativeMessageTypes.JsonOptions)
            });

            Assert.True(composite.IsConnected);
            Assert.Equal("Opera", composite.ConnectedBrowserName);
            Assert.Single(composite.ActiveSessions);
            Assert.Equal(42, composite.CurrentSession!.TabId);

            settings.Enabled = false;
            Assert.False(composite.IsConnected);
            Assert.Empty(composite.ActiveSessions);
            Assert.Null(composite.CurrentSession);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CompositeBridge_AllowsManualSwitchingBetweenSimultaneousPlayingTabs()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"doom_switch_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var settings = new BrowserIntegrationSettings { Enabled = true };
            var db = new SqliteDbContext(Path.Combine(tempDir, "test.db"));
            await using var extBridge = new BrowserMediaBridgeService();
            await using var composite = new CompositeBrowserMediaBridge(extBridge, db, () => settings);

            extBridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.MediaDetected,
                Browser = "Google Chrome",
                TabId = 101,
                WindowId = 1,
                Payload = JsonSerializer.SerializeToElement(new MediaPayload
                {
                    Origin = "https://www.youtube.com",
                    UrlHost = "www.youtube.com",
                    TabTitle = "YouTube Track 1",
                    Title = "Track One",
                    Artist = "Artist 1",
                    PlaybackState = "playing",
                    IsAudible = true,
                    IsActiveBrowserTab = true
                }, NativeMessageTypes.JsonOptions)
            });

            extBridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.MediaDetected,
                Browser = "Google Chrome",
                TabId = 202,
                WindowId = 1,
                Payload = JsonSerializer.SerializeToElement(new MediaPayload
                {
                    Origin = "https://open.spotify.com",
                    UrlHost = "open.spotify.com",
                    TabTitle = "Spotify Web",
                    Title = "Track Two",
                    Artist = "Artist 2",
                    PlaybackState = "playing",
                    IsAudible = true,
                    IsActiveBrowserTab = false
                }, NativeMessageTypes.JsonOptions)
            });

            Assert.Equal(2, composite.ActiveSessions.Count);
            Assert.Equal(101, composite.CurrentSession!.TabId);

            // Switch directly to the second simultaneous playing tab
            Assert.True(composite.SelectSession(202));
            Assert.Equal(202, composite.CurrentSession!.TabId);
            Assert.Equal("Track Two", composite.CurrentSession.Title);

            // Background timeline update on Tab 101 must not steal focus away from user-selected Tab 202
            extBridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.PlaybackPositionUpdated,
                Browser = "Google Chrome",
                TabId = 101,
                Payload = JsonSerializer.SerializeToElement(new PlaybackPositionPayload
                {
                    PositionSeconds = 42,
                    DurationSeconds = 180,
                    PlaybackRate = 1.0
                }, NativeMessageTypes.JsonOptions)
            });
            Assert.Equal(202, composite.CurrentSession!.TabId);

            // Closing selected tab 202 falls back cleanly to remaining playing tab 101
            extBridge.ProcessIncomingEnvelope(new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.MediaStopped,
                Browser = "Google Chrome",
                TabId = 202,
                Payload = JsonSerializer.SerializeToElement(new { tabId = 202, reason = "tab_closed" }, NativeMessageTypes.JsonOptions)
            });
            Assert.Single(composite.ActiveSessions);
            Assert.Equal(101, composite.CurrentSession!.TabId);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void DefaultExtensionId_MatchesExpectedDeterministicId()
    {
        Assert.Equal("amplhmapoeandecddkchambamhafdcgp", AppConstants.DefaultBrowserExtensionId);
    }
}
