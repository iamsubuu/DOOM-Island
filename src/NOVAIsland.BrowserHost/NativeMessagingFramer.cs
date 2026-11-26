using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using NOVAIsland.Core.Protocol;

namespace NOVAIsland.BrowserHost;

/// <summary>
/// Implements Chrome/Edge Native Messaging 4-byte little-endian length-prefixed UTF-8 JSON framing.
/// </summary>
public static class NativeMessagingFramer
{
    public static async Task<NativeMessageEnvelope?> ReadAsync(Stream input, CancellationToken cancellationToken = default)
    {
        var lenBuf = new byte[4];
        var read = await ReadExactAsync(input, lenBuf, cancellationToken);
        if (read == 0)
        {
            return null; // EOF: browser closed native port
        }

        if (read < 4)
        {
            throw new InvalidDataException("Truncated Native Messaging length prefix.");
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf);
        if (length == 0 || length > NativeMessageTypes.MaxPayloadBytes)
        {
            throw new InvalidDataException($"Invalid Native Messaging payload length: {length}.");
        }

        var payload = new byte[length];
        var bodyRead = await ReadExactAsync(input, payload, cancellationToken);
        if (bodyRead < length)
        {
            throw new InvalidDataException("Truncated Native Messaging payload body.");
        }

        var envelope = JsonSerializer.Deserialize<NativeMessageEnvelope>(payload, NativeMessageTypes.JsonOptions);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Type))
        {
            throw new InvalidDataException("Invalid Native Messaging JSON envelope.");
        }

        return envelope;
    }

    public static async Task WriteAsync(Stream output, NativeMessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(envelope, NativeMessageTypes.JsonOptions);
        if (jsonBytes.Length > NativeMessageTypes.MaxPayloadBytes)
        {
            throw new InvalidOperationException("Outbound Native Messaging payload exceeds 1 MB limit.");
        }

        var lenBuf = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(lenBuf, (uint)jsonBytes.Length);
        await output.WriteAsync(lenBuf, cancellationToken);
        await output.WriteAsync(jsonBytes, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private static async Task<int> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total, buffer.Length - total), cancellationToken);
            if (n == 0)
            {
                return total;
            }
            total += n;
        }
        return total;
    }
}
