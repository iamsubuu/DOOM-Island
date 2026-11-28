using System.Diagnostics;
using System.Text.Json;
using NOVAIsland.BrowserHost;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Protocol;
using NOVAIsland.Services.Media;
using Xunit;

namespace NOVAIsland.Tests;

public sealed class NativeMessagingEndToEndTests
{
    [Fact]
    public async Task BrowserHostProcess_ShouldBridgeBidirectionallyWithBrowserMediaBridgeService()
    {
        var hostExe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "NOVAIsland.BrowserHost", "bin", "Debug", "net10.0-windows10.0.19041.0", "NOVAIsland.BrowserHost.exe"));

        Assert.True(File.Exists(hostExe), $"Host exe missing at {hostExe}");

        var pipeName = $"DOOMIsland.TestPipe.{Guid.NewGuid():N}";
        await using var bridge = new BrowserMediaBridgeService(pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await bridge.StartAsync(cts.Token);

        var psi = new ProcessStartInfo(hostExe)
        {
            Arguments = $"--pipe={pipeName}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = Process.Start(psi)!;
        try
        {
            var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bridge.StateChanged += (_, _) =>
            {
                if (bridge.CurrentSession is not null)
                {
                    connectedTcs.TrySetResult();
                }
            };

            // Give BrowserHost a brief moment to connect to the isolated test Named Pipe
            await Task.Delay(500, cts.Token);

            // 1. Send browser_connected + media_detected over Native Messaging stdin
            await NativeMessagingFramer.WriteAsync(proc.StandardInput.BaseStream, new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.BrowserConnected,
                Browser = "Microsoft Edge",
                Payload = JsonSerializer.SerializeToElement(new BrowserConnectedPayload
                {
                    ExtensionVersion = "1.0.0",
                    BrowserName = "Microsoft Edge"
                }, NativeMessageTypes.JsonOptions)
            }, cts.Token);

            await NativeMessagingFramer.WriteAsync(proc.StandardInput.BaseStream, new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.MediaDetected,
                Browser = "Microsoft Edge",
                TabId = 99,
                WindowId = 1,
                Payload = JsonSerializer.SerializeToElement(new MediaPayload
                {
                    Origin = "https://www.youtube.com",
                    UrlHost = "www.youtube.com",
                    TabTitle = "YouTube",
                    Title = "DOOM E2E Test Track",
                    Artist = "Test Artist",
                    DurationSeconds = 180,
                    PositionSeconds = 10,
                    PlaybackState = "playing",
                    IsAudible = true,
                    IsActiveBrowserTab = true,
                    Capabilities = new MediaCapabilitiesPayload { CanPlayPause = true, CanSeek = true }
                }, NativeMessageTypes.JsonOptions)
            }, cts.Token);

            await connectedTcs.Task.WaitAsync(cts.Token);
            Assert.NotNull(bridge.CurrentSession);
            Assert.Equal("DOOM E2E Test Track", bridge.CurrentSession!.Title);
            Assert.Equal(BrowserPlaybackState.Playing, bridge.CurrentSession.PlaybackState);

            // 2. Dispatch media_command from WinUI bridge -> Pipe -> BrowserHost -> stdout
            var cmdTask = bridge.SendCommandAsync(99, BrowserMediaAction.Pause, cancellationToken: cts.Token);
            var outbound = await NativeMessagingFramer.ReadAsync(proc.StandardOutput.BaseStream, cts.Token);

            Assert.NotNull(outbound);
            Assert.Equal(NativeMessageTypes.MediaCommand, outbound!.Type);
            var cmdPayload = outbound.Payload.Deserialize<MediaCommandPayload>(NativeMessageTypes.JsonOptions)!;
            Assert.Equal("pause", cmdPayload.Action);
            Assert.Equal(99, cmdPayload.TargetTabId);

            // 3. Extension replies with command_result over stdin -> Pipe -> completes SendCommandAsync
            await NativeMessagingFramer.WriteAsync(proc.StandardInput.BaseStream, new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.CommandResult,
                Browser = "Microsoft Edge",
                TabId = 99,
                Payload = JsonSerializer.SerializeToElement(new CommandResultPayload
                {
                    CommandId = cmdPayload.CommandId,
                    Success = true,
                    Reason = "ok"
                }, NativeMessageTypes.JsonOptions)
            }, cts.Token);

            var result = await cmdTask;
            Assert.True(result.Success);
            Assert.Equal("ok", result.Reason);
        }
        finally
        {
            try { proc.StandardInput.Close(); } catch { }
            if (!proc.HasExited) proc.Kill();
        }
    }

    [Fact]
    public async Task BrowserHostProcess_ShouldBufferEarlyStartupMessagesBeforePipeConnects()
    {
        var hostExe = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "src", "NOVAIsland.BrowserHost", "bin", "Debug", "net10.0-windows10.0.19041.0", "NOVAIsland.BrowserHost.exe"));

        Assert.True(File.Exists(hostExe), $"Host exe missing at {hostExe}");

        var pipeName = $"DOOMIsland.EarlyBufferPipe.{Guid.NewGuid():N}";
        await using var bridge = new BrowserMediaBridgeService(pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var connectedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        bridge.StateChanged += (_, _) =>
        {
            if (bridge.CurrentSession is not null)
            {
                connectedTcs.TrySetResult();
            }
        };

        var psi = new ProcessStartInfo(hostExe)
        {
            Arguments = $"--pipe={pipeName}",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Start BrowserHost and send frames BEFORE Named Pipe server even starts listening
        using var proc = Process.Start(psi)!;
        try
        {
            await NativeMessagingFramer.WriteAsync(proc.StandardInput.BaseStream, new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.BrowserConnected,
                Browser = "Google Chrome",
                Payload = JsonSerializer.SerializeToElement(new BrowserConnectedPayload
                {
                    ExtensionVersion = "2.5.3",
                    BrowserName = "Google Chrome"
                }, NativeMessageTypes.JsonOptions)
            }, cts.Token);

            await NativeMessagingFramer.WriteAsync(proc.StandardInput.BaseStream, new NativeMessageEnvelope
            {
                Type = NativeMessageTypes.MediaDetected,
                Browser = "Google Chrome",
                TabId = 77,
                WindowId = 1,
                Payload = JsonSerializer.SerializeToElement(new MediaPayload
                {
                    Origin = "https://music.youtube.com",
                    UrlHost = "music.youtube.com",
                    TabTitle = "Early Buffered Tab",
                    Title = "Early Buffered Track",
                    Artist = "Buffered Artist",
                    PlaybackState = "playing",
                    IsAudible = true,
                    IsActiveBrowserTab = true
                }, NativeMessageTypes.JsonOptions)
            }, cts.Token);

            await Task.Delay(250, cts.Token);
            await bridge.StartAsync(cts.Token);

            await connectedTcs.Task.WaitAsync(cts.Token);
            Assert.True(bridge.IsConnected);
            Assert.Equal("Google Chrome", bridge.ConnectedBrowserName);
            Assert.Equal(77, bridge.CurrentSession!.TabId);
            Assert.Equal("Early Buffered Track", bridge.CurrentSession.Title);
        }
        finally
        {
            try { proc.StandardInput.Close(); } catch { }
            if (!proc.HasExited) proc.Kill();
        }
    }
}
