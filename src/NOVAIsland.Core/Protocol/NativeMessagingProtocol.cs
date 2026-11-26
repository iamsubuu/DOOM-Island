using System.Text.Json;
using System.Text.Json.Serialization;

namespace NOVAIsland.Core.Protocol;

public static class NativeMessageTypes
{
    public const int ProtocolVersion = 1;
    public const int MaxPayloadBytes = 1024 * 1024; // 1 MB Chrome/Edge Native Messaging cap

    public const string BrowserConnected = "browser_connected";
    public const string BrowserDisconnected = "browser_disconnected";
    public const string MediaDetected = "media_detected";
    public const string MediaMetadataUpdated = "media_metadata_updated";
    public const string PlaybackStateChanged = "playback_state_changed";
    public const string PlaybackPositionUpdated = "playback_position_updated";
    public const string ActiveTabChanged = "active_tab_changed";
    public const string MediaStopped = "media_stopped";
    public const string MediaCommand = "media_command";
    public const string CommandResult = "command_result";

    public static readonly IReadOnlySet<string> AllowedIncomingTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        BrowserConnected,
        BrowserDisconnected,
        MediaDetected,
        MediaMetadataUpdated,
        PlaybackStateChanged,
        PlaybackPositionUpdated,
        ActiveTabChanged,
        MediaStopped,
        CommandResult
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class NativeMessageEnvelope
{
    public int Version { get; set; } = NativeMessageTypes.ProtocolVersion;
    public string Type { get; set; } = string.Empty;
    public string MessageId { get; set; } = Guid.NewGuid().ToString("D");
    public string TimestampUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string Browser { get; set; } = "chrome";
    public int TabId { get; set; }
    public int WindowId { get; set; }
    public JsonElement Payload { get; set; }
}

public sealed class BrowserConnectedPayload
{
    public string ExtensionVersion { get; set; } = string.Empty;
    public string BrowserName { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;
}

public sealed class BrowserDisconnectedPayload
{
    public string Reason { get; set; } = "disconnected";
}

public sealed class MediaCapabilitiesPayload
{
    public bool CanPlayPause { get; set; }
    public bool CanNext { get; set; }
    public bool CanPrevious { get; set; }
    public bool CanSeek { get; set; }
}

public sealed class MediaPayload
{
    public string Origin { get; set; } = string.Empty;
    public string UrlHost { get; set; } = string.Empty;
    public string TabTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Album { get; set; } = string.Empty;
    public string? ArtworkUrl { get; set; }
    public double DurationSeconds { get; set; }
    public double PositionSeconds { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
    public string PlaybackState { get; set; } = "paused";
    public bool IsAudible { get; set; }
    public bool IsActiveBrowserTab { get; set; }
    public bool IsIncognito { get; set; }
    public MediaCapabilitiesPayload? Capabilities { get; set; }
}

public sealed class PlaybackStatePayload
{
    public string PlaybackState { get; set; } = "none";
    public double PositionSeconds { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
}

public sealed class PlaybackPositionPayload
{
    public double PositionSeconds { get; set; }
    public double DurationSeconds { get; set; }
    public double PlaybackRate { get; set; } = 1.0;
}

public sealed class ActiveTabChangedPayload
{
    public int TabId { get; set; }
    public string Origin { get; set; } = string.Empty;
    public bool IsAudible { get; set; }
    public bool IsActiveBrowserTab { get; set; }
}

public sealed class MediaStoppedPayload
{
    public int TabId { get; set; }
    public string Reason { get; set; } = "stopped";
}

public sealed class MediaCommandPayload
{
    public string CommandId { get; set; } = Guid.NewGuid().ToString("D");
    public int TargetTabId { get; set; }
    public string Action { get; set; } = string.Empty;
    public double? SeekPositionSeconds { get; set; }
}

public sealed class CommandResultPayload
{
    public string CommandId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Reason { get; set; } = "ok";
}

public static class NativeMessageValidator
{
    public static bool TryValidateIncoming(ReadOnlySpan<byte> utf8Json, out NativeMessageEnvelope? envelope, out string? error)
    {
        envelope = null;
        error = null;

        if (utf8Json.IsEmpty || utf8Json.Length > NativeMessageTypes.MaxPayloadBytes)
        {
            error = $"Payload size {utf8Json.Length} exceeds valid Native Messaging bounds.";
            return false;
        }

        try
        {
            envelope = JsonSerializer.Deserialize<NativeMessageEnvelope>(utf8Json, NativeMessageTypes.JsonOptions);
        }
        catch (JsonException ex)
        {
            error = $"Malformed JSON: {ex.Message}";
            return false;
        }

        if (envelope is null)
        {
            error = "Envelope deserialized to null.";
            return false;
        }

        if (envelope.Version != NativeMessageTypes.ProtocolVersion)
        {
            error = $"Unsupported protocol version '{envelope.Version}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(envelope.Type) ||
            (!NativeMessageTypes.AllowedIncomingTypes.Contains(envelope.Type) && envelope.Type != NativeMessageTypes.MediaCommand))
        {
            error = $"Unsupported message type '{envelope.Type}'.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(envelope.Browser))
        {
            envelope.Browser = "chrome";
        }

        return true;
    }

    public static byte[] SerializeEnvelope<TPayload>(string type, string browser, int tabId, int windowId, TPayload payload)
    {
        var payloadElement = JsonSerializer.SerializeToElement(payload, NativeMessageTypes.JsonOptions);
        var envelope = new NativeMessageEnvelope
        {
            Version = NativeMessageTypes.ProtocolVersion,
            Type = type,
            MessageId = Guid.NewGuid().ToString("D"),
            TimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
            Browser = browser,
            TabId = tabId,
            WindowId = windowId,
            Payload = payloadElement
        };

        return JsonSerializer.SerializeToUtf8Bytes(envelope, NativeMessageTypes.JsonOptions);
    }
}
