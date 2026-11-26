using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
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

        var pipeName = PipeIdentityHelper.GetUserPipeName();
        NamedPipeClientStream? activePipe = null;
        var pipeWriteLock = new SemaphoreSlim(1, 1);

        // Background task maintaining Named Pipe connection to DOOM Island desktop app
        var pipeLoop = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                    await client.ConnectAsync(2000, cts.Token);
                    activePipe = client;

                    using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);
                    while (client.IsConnected && !cts.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync(cts.Token);
                        if (line is null) break;

                        var env = JsonSerializer.Deserialize<NativeMessageEnvelope>(line, NativeMessageTypes.JsonOptions);
                        if (env is null || string.IsNullOrWhiteSpace(env.Type)) continue;

                        await stdoutLock.WaitAsync(cts.Token);
                        try
                        {
                            await NativeMessagingFramer.WriteAsync(stdout, env, cts.Token);
                        }
                        finally
                        {
                            stdoutLock.Release();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    activePipe = null;
                    await Task.Delay(1500, cts.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
                }
                finally
                {
                    activePipe = null;
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

                var pipe = activePipe;
                if (pipe is { IsConnected: true })
                {
                    var line = JsonSerializer.Serialize(envelope, NativeMessageTypes.JsonOptions) + "\n";
                    var bytes = Encoding.UTF8.GetBytes(line);
                    await pipeWriteLock.WaitAsync(cts.Token);
                    try
                    {
                        await pipe.WriteAsync(bytes, cts.Token);
                        await pipe.FlushAsync(cts.Token);
                    }
                    catch
                    {
                        // Pipe reconnect loop will recover
                    }
                    finally
                    {
                        pipeWriteLock.Release();
                    }
                }
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
