using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace EchoMix.Shared;

/// [1-byte frame type][4-byte big-endian length][payload] framing shared by EchoMix.Relay, the broadcast host
/// connection, and the listen client - one helper instead of three copies of the same read-loop, over
/// whatever Stream (plain or SslStream) the caller already has open.
public static class FrameIO
{
    public const byte ControlFrame = 0;
    public const byte AudioFrame = 1;

    private const int MaxFrameLength = 1 * 1024 * 1024;

    public static async Task WriteFrameAsync(Stream stream, byte type, byte[] payload, CancellationToken token = default)
    {
        var header = new byte[5];
        header[0] = type;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1), (uint)payload.Length);
        await stream.WriteAsync(header, token);
        if (payload.Length > 0)
            await stream.WriteAsync(payload, token);
        await stream.FlushAsync(token);
    }

    /// Returns null on a clean stream close at a frame boundary (normal disconnect).
    public static async Task<(byte Type, byte[] Payload)?> ReadFrameAsync(Stream stream, CancellationToken token = default)
    {
        var header = new byte[5];
        if (!await ReadExactAsync(stream, header, token))
            return null;

        var type = header[0];
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
        if (length > MaxFrameLength)
            throw new InvalidDataException($"Frame length {length} exceeds the {MaxFrameLength} byte limit.");

        var payload = length == 0 ? Array.Empty<byte>() : new byte[length];
        if (length > 0 && !await ReadExactAsync(stream, payload, token))
            return null;

        return (type, payload);
    }

    /// False only on a clean close with zero bytes read yet (a real frame boundary) - anything else
    /// mid-header/mid-payload is a genuinely truncated stream, not a normal end.
    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken token)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset), token);
            if (read == 0)
            {
                if (offset == 0)
                    return false;
                throw new IOException("Stream ended mid-frame.");
            }

            offset += read;
        }

        return true;
    }
}
