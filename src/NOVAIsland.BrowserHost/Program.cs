using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Protocol;

namespace NOVAIsland.BrowserHost;

internal static class PipeIdentityHelper
{
    public static string GetUserPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..12];
        return $"{AppConstants.PipeNamePrefix}.{hash}";
    }
}

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Contains("--self-check", StringComparer.OrdinalIgnoreCase))
        {
            return 0;
        }

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        await using var stdin = Console.OpenStandardInput();
        await using var stdout = Console.OpenStandardOutput();
        var stdoutLock = new SemaphoreSlim(1, 1);

        var pipeArg = args.FirstOrDefault(a => a.StartsWith("--pipe=", StringComparison.OrdinalIgnoreCase));
        var pipeName = pipeArg is not null && pipeArg.Length > 7
            ? pipeArg[7..]
            : Environment.GetEnvironmentVariable("DOOMISLAND_PIPE") ?? PipeIdentityHelper.GetUserPipeName();

        // Buffer outbound envelopes until the Named Pipe connects so startup browser_connected/media_detected frames are never dropped.
        var outboundChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true
        });
        string? pendingRetryLine = null;

        // Background task maintaining Named Pipe connection to DOOM Island desktop app
        var pipeLoop = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await client.ConnectAsync(2000, cts.Token);

                    using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    var pipeReadTask = Task.Run(async () =>
                    {
                        using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
                        while (client.IsConnected && !readCts.IsCancellationRequested)
                        {
                            var line = await reader.ReadLineAsync(readCts.Token);
                            if (line is null) break;

                            var env = JsonSerializer.Deserialize<NativeMessageEnvelope>(line, NativeMessageTypes.JsonOptions);
                            if (env is null || string.IsNullOrWhiteSpace(env.Type)) continue;

                            await stdoutLock.WaitAsync(readCts.Token);
                            try
                            {
                                await NativeMessagingFramer.WriteAsync(stdout, env, readCts.Token);
                            }
                            finally
                            {
                                stdoutLock.Release();
                            }
                        }
                    }, readCts.Token);

                    try
                    {
                        while (client.IsConnected && !cts.IsCancellationRequested)
                        {
                            var line = pendingRetryLine ?? await outboundChannel.Reader.ReadAsync(cts.Token);
                            pendingRetryLine = line;
                            var bytes = Encoding.UTF8.GetBytes(line);
                            await client.WriteAsync(bytes, cts.Token);
                            await client.FlushAsync(cts.Token);
                            pendingRetryLine = null;
                        }
                    }
                    finally
                    {
                        await readCts.CancelAsync();
                        await pipeReadTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(500, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
            }
        }, cts.Token);

        // Read stdin frames from browser until EOF
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var envelope = await NativeMessagingFramer.ReadAsync(stdin, cts.Token);
                if (envelope is null)
                {
                    break; // Browser disconnected
                }

                var line = JsonSerializer.Serialize(envelope, NativeMessageTypes.JsonOptions) + "\n";
                outboundChannel.Writer.TryWrite(line);
            }
        }
        catch
        {
            // Clean shutdown on framing EOF/cancellation
        }
        finally
        {
            await cts.CancelAsync();
            await pipeLoop.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        return 0;
    }
}
