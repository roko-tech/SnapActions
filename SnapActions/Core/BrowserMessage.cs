using System.Buffers.Binary;
using System.IO;
using System.Text.Json;

namespace SnapActions.Core;

// Native messaging uses a byte count, not a UTF-16 character count.
internal static class BrowserMessage
{
    internal const int MaximumBytes = 256 * 1024;
    internal const int MaximumTextLength = SelectionSnapshot.MaximumTextLength;
    internal const int ProtocolVersion = 1;

    internal static async Task<byte[]?> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[4];
        if (await stream.ReadAsync(header.AsMemory(0, 1), cancellationToken) == 0) return null;
        await stream.ReadExactlyAsync(header.AsMemory(1), cancellationToken);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumBytes) throw new InvalidDataException("Invalid browser message size.");
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    internal static async Task WriteAsync(Stream stream, byte[] payload, CancellationToken cancellationToken)
    {
        if (payload.Length == 0 || payload.Length > MaximumBytes)
            throw new InvalidDataException("Invalid browser message size.");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    internal static byte[] Encode<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value);
}
