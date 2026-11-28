using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using NOVAIsland.Core.Configuration;
using NOVAIsland.Core.Enums;
using NOVAIsland.Core.Interfaces;
using NOVAIsland.Core.Models;
using NOVAIsland.Core.Protocol;

namespace NOVAIsland.Services.Media;

/// <summary>
/// Hosts a user-SID-restricted Named Pipe server receiving validated Native Messaging events
/// from NOVAIsland.BrowserHost and dispatching commands back to active browser tabs.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BrowserMediaBridgeService : IBrowserMediaBridge, IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, BrowserMediaSession> _sessions = new();
    private readonly ConcurrentDictionary<int, NamedPipeServerStream> _sessionOwners = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<CommandResultInfo>> _pendingCommands = new();
    private readonly List<NamedPipeServerStream> _connectedClients = [];
    private readonly Lock _clientsLock = new();
    private readonly string _pipeName;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoop;
    private int? _preferredTabId;

    public BrowserMediaBridgeService() : this(null)
    {
    }

    public BrowserMediaBridgeService(string? customPipeName)
    {
        _pipeName = string.IsNullOrWhiteSpace(customPipeName) ? GetCurrentUserPipeName() : customPipeName;
    }

    public bool IsConnected { get; private set; }
    public string? ConnectedBrowserName { get; private set; }

    public IReadOnlyList<BrowserMediaSession> ActiveSessions =>
        _sessions.Values
            .OrderByDescending(s => s.PlaybackState == BrowserPlaybackState.Playing)
            .ThenByDescending(s => s.IsActiveBrowserTab)
            .ThenBy(s => s.Browser, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.TabId)
            .ToList();

    public BrowserMediaSession? CurrentSession
    {
        get
        {
            if (_preferredTabId is int id && _sessions.TryGetValue(id, out var preferred))
            {
                return preferred;
            }
            return ActiveSessions.FirstOrDefault();
        }
    }

    public bool SelectSession(int tabId)
    {
        if (!_sessions.ContainsKey(tabId))
        {
            return false;
        }

        _preferredTabId = tabId;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public event EventHandler? StateChanged;

    public static string GetCurrentUserPipeName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)))[..12];
        return $"{AppConstants.PipeNamePrefix}.{hash}";
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_acceptLoop is not null)
        {
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _acceptLoop = Task.Run(() => AcceptClientsAsync(_cts.Token), _cts.Token);
        return Task.CompletedTask;
    }

    public async Task<CommandResultInfo> SendCommandAsync(
        int targetTabId,
        BrowserMediaAction action,
        double? seekSeconds = null,
        CancellationToken cancellationToken = default)
    {
        var cmdId = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<CommandResultInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[cmdId] = tcs;

        var actionName = action switch
        {
            BrowserMediaAction.Play => "play",
            BrowserMediaAction.Pause => "pause",
            BrowserMediaAction.Toggle => "toggle",
            BrowserMediaAction.Next => "next",
            BrowserMediaAction.Previous => "previous",
            BrowserMediaAction.Seek => "seek",
            BrowserMediaAction.FocusTab => "focus_tab",
            _ => "toggle"
        };

        var payload = JsonSerializer.SerializeToElement(new MediaCommandPayload
        {
            CommandId = cmdId,
            TargetTabId = targetTabId,
            Action = actionName,
            SeekPositionSeconds = seekSeconds
        }, NativeMessageTypes.JsonOptions);

        var env = new NativeMessageEnvelope
        {
            Type = NativeMessageTypes.MediaCommand,
            TabId = targetTabId,
            Payload = payload
        };

        var line = JsonSerializer.Serialize(env, NativeMessageTypes.JsonOptions) + "\n";
        var bytes = Encoding.UTF8.GetBytes(line);

        List<NamedPipeServerStream> clientsSnapshot;
        lock (_clientsLock)
        {
            clientsSnapshot = _connectedClients.Where(c => c.IsConnected).ToList();
        }

        if (clientsSnapshot.Count == 0)
        {
            _pendingCommands.TryRemove(cmdId, out _);
            return new CommandResultInfo(false, "browser_not_connected");
        }

        foreach (var client in clientsSnapshot)
        {
            try
            {
                await client.WriteAsync(bytes, cancellationToken);
                await client.FlushAsync(cancellationToken);
            }
            catch
            {
                // Ignore disconnected client
            }
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            _pendingCommands.TryRemove(cmdId, out _);
            return new CommandResultInfo(false, "timeout");
        }
    }

    public void ProcessIncomingEnvelope(NativeMessageEnvelope envelope) =>
        ProcessIncomingEnvelope(envelope, ownerClient: null);

    private void ProcessIncomingEnvelope(NativeMessageEnvelope envelope, NamedPipeServerStream? ownerClient)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        switch (envelope.Type)
        {
            case NativeMessageTypes.BrowserConnected:
            {
                var info = envelope.Payload.Deserialize<BrowserConnectedPayload>(NativeMessageTypes.JsonOptions);
                IsConnected = true;
                ConnectedBrowserName = !string.IsNullOrWhiteSpace(info?.BrowserName) ? info.BrowserName : envelope.Browser;
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case NativeMessageTypes.BrowserDisconnected:
            {
                if (ownerClient is not null)
                {
                    RemoveSessionsOwnedBy(ownerClient);
                }
                else
                {
                    IsConnected = false;
                    _sessions.Clear();
                    _sessionOwners.Clear();
                    _preferredTabId = null;
                }
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case NativeMessageTypes.MediaDetected:
            case NativeMessageTypes.MediaMetadataUpdated:
            {
                var dto = envelope.Payload.Deserialize<MediaPayload>(NativeMessageTypes.JsonOptions);
                if (dto is null) return;

                var session = new BrowserMediaSession
                {
                    Browser = envelope.Browser,
                    TabId = envelope.TabId,
                    WindowId = envelope.WindowId,
                    Origin = dto.Origin,
                    UrlHost = dto.UrlHost,
                    TabTitle = dto.TabTitle,
                    Title = string.IsNullOrWhiteSpace(dto.Title) ? dto.TabTitle : dto.Title,
                    Artist = dto.Artist,
                    Album = dto.Album,
                    ArtworkUrl = dto.ArtworkUrl,
                    DurationSeconds = dto.DurationSeconds,
                    PositionSeconds = dto.PositionSeconds,
                    PlaybackRate = dto.PlaybackRate > 0 ? dto.PlaybackRate : 1.0,
                    PlaybackState = ParseState(dto.PlaybackState),
                    IsAudible = dto.IsAudible,
                    IsActiveBrowserTab = dto.IsActiveBrowserTab,
                    CanPlayPause = dto.Capabilities?.CanPlayPause ?? true,
                    CanNext = dto.Capabilities?.CanNext ?? false,
                    CanPrevious = dto.Capabilities?.CanPrevious ?? false,
                    CanSeek = dto.Capabilities?.CanSeek ?? false,
                    LastUpdatedUtc = DateTimeOffset.UtcNow
                };

                _sessions[envelope.TabId] = session;
                if (ownerClient is not null)
                {
                    _sessionOwners[envelope.TabId] = ownerClient;
                }
                IsConnected = true;
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case NativeMessageTypes.PlaybackStateChanged:
            {
                if (!_sessions.TryGetValue(envelope.TabId, out var existing)) return;
                var dto = envelope.Payload.Deserialize<PlaybackStatePayload>(NativeMessageTypes.JsonOptions);
                if (dto is null) return;

                existing.PlaybackState = ParseState(dto.PlaybackState);
                existing.PositionSeconds = dto.PositionSeconds;
                existing.PlaybackRate = dto.PlaybackRate > 0 ? dto.PlaybackRate : 1.0;
                existing.LastUpdatedUtc = DateTimeOffset.UtcNow;
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case NativeMessageTypes.PlaybackPositionUpdated:
            {
                if (!_sessions.TryGetValue(envelope.TabId, out var existing)) return;
                var dto = envelope.Payload.Deserialize<PlaybackPositionPayload>(NativeMessageTypes.JsonOptions);
                if (dto is null) return;

                existing.PositionSeconds = dto.PositionSeconds;
                if (dto.DurationSeconds > 0) existing.DurationSeconds = dto.DurationSeconds;
                if (dto.PlaybackRate > 0) existing.PlaybackRate = dto.PlaybackRate;
                existing.LastUpdatedUtc = DateTimeOffset.UtcNow;
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case NativeMessageTypes.ActiveTabChanged:
            {
                var dto = envelope.Payload.Deserialize<ActiveTabChangedPayload>(NativeMessageTypes.JsonOptions);
                if (dto is not null)
                {
                    _preferredTabId = dto.TabId;
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
                break;
            }

            case NativeMessageTypes.MediaStopped:
            {
                _sessions.TryRemove(envelope.TabId, out _);
                _sessionOwners.TryRemove(envelope.TabId, out _);
                if (_preferredTabId == envelope.TabId) _preferredTabId = null;
                StateChanged?.Invoke(this, EventArgs.Empty);
                break;
            }

            case NativeMessageTypes.CommandResult:
            {
                var res = envelope.Payload.Deserialize<CommandResultPayload>(NativeMessageTypes.JsonOptions);
                if (res is not null && _pendingCommands.TryRemove(res.CommandId, out var tcs))
                {
                    tcs.TrySetResult(new CommandResultInfo(res.Success, res.Reason));
                }
                break;
            }
        }
    }

    private async Task AcceptClientsAsync(CancellationToken ct)
    {
        var pipeName = _pipeName;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                var security = BuildCurrentUserPipeSecurity();
                server = NamedPipeServerStreamAcl.Create(
                    pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous,
                    inBufferSize: 65536,
                    outBufferSize: 65536,
                    security);

                await server.WaitForConnectionAsync(ct);

                lock (_clientsLock)
                {
                    _connectedClients.Add(server);
                }

                _ = HandleClientAsync(server, ct);
            }
            catch (OperationCanceledException)
            {
                server?.Dispose();
                break;
            }
            catch
            {
                server?.Dispose();
                await Task.Delay(500, ct).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream server, CancellationToken ct)
    {
        try
        {
            using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: true);
            while (server.IsConnected && !ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                if (Encoding.UTF8.GetByteCount(line) > NativeMessageTypes.MaxPayloadBytes) continue;

                var env = JsonSerializer.Deserialize<NativeMessageEnvelope>(line, NativeMessageTypes.JsonOptions);
                if (env is not null && !string.IsNullOrWhiteSpace(env.Type))
                {
                    ProcessIncomingEnvelope(env, server);
                }
            }
        }
        catch { }
        finally
        {
            var removedAny = RemoveSessionsOwnedBy(server);
            lock (_clientsLock)
            {
                _connectedClients.Remove(server);
                if (_connectedClients.Count == 0)
                {
                    IsConnected = false;
                    _sessions.Clear();
                    _sessionOwners.Clear();
                    _preferredTabId = null;
                    removedAny = true;
                }
            }
            if (removedAny)
            {
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
            server.Dispose();
        }
    }

    private bool RemoveSessionsOwnedBy(NamedPipeServerStream ownerClient)
    {
        var removed = false;
        foreach (var kv in _sessionOwners.ToArray())
        {
            if (ReferenceEquals(kv.Value, ownerClient))
            {
                _sessionOwners.TryRemove(kv.Key, out _);
                if (_sessions.TryRemove(kv.Key, out _))
                {
                    removed = true;
                }
                if (_preferredTabId == kv.Key)
                {
                    _preferredTabId = null;
                }
            }
        }
        return removed;
    }

    private static PipeSecurity BuildCurrentUserPipeSecurity()
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User;
        if (user is not null)
        {
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        }
        return security;
    }

    private static BrowserPlaybackState ParseState(string? raw) => raw?.ToLowerInvariant() switch
    {
        "playing" => BrowserPlaybackState.Playing,
        "paused" => BrowserPlaybackState.Paused,
        _ => BrowserPlaybackState.None
    };

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
            _cts.Dispose();
        }
        lock (_clientsLock)
        {
            foreach (var c in _connectedClients) c.Dispose();
            _connectedClients.Clear();
        }
    }
}
