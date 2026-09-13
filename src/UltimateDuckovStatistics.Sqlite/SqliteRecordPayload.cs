using System.Buffers;
using System.Buffers.Binary;
using System.IO.Compression;

namespace UltimateDuckovStatistics.Sqlite;

// Storage-only representation: receipt and history hashes still cover the exact
// original JSON bytes. Small or poorly compressible records stay unwrapped.
internal static class SqliteRecordPayload
{
    private const int HeaderLength = 8;

    internal static byte[] Encode(byte[] bytes)
    {
        if (bytes.Length < 256) return bytes;
        using var output = new MemoryStream();
        output.Write(new byte[] { (byte)'U', (byte)'D', (byte)'S', 1, 0, 0, 0, 0 }, 0, HeaderLength);
        using (var deflate = new DeflateStream(output, CompressionLevel.Fastest, leaveOpen: true))
            deflate.Write(bytes, 0, bytes.Length);
        if (output.Length + 32 >= bytes.Length) return bytes;
        var packed = output.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(packed.AsSpan(4, 4), bytes.Length);
        return packed;
    }

    internal static byte[] Decode(byte[] stored)
    {
        // No JSON value starts with 'U'. The database version gates the format;
        // this discriminator lets short records retain their original bytes.
        if (stored.Length == 0 || stored[0] != (byte)'U') return stored;
        if (stored.Length <= HeaderLength || stored[1] != (byte)'D' || stored[2] != (byte)'S' || stored[3] != 1)
            throw new InvalidDataException("Stored record compression header is invalid.");
        var expected = BinaryPrimitives.ReadInt32LittleEndian(stored.AsSpan(4, 4));
        if (expected <= 0) throw new InvalidDataException("Stored record decompressed length is invalid.");
        using var input = new MemoryStream(stored, HeaderLength, stored.Length - HeaderLength, writable: false);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        // Do not allocate an arbitrary size from a potentially damaged header.
        using var output = new MemoryStream(Math.Min(expected, 65536));
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            int read;
            while ((read = deflate.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (output.Length + read > expected) throw new InvalidDataException("Stored record exceeds its decompressed length.");
                output.Write(buffer, 0, read);
            }
            if (output.Length != expected) throw new InvalidDataException("Stored record is truncated.");
            return output.ToArray();
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }
}
