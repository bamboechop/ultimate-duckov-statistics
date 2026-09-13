using System.Security.Cryptography;
using System.Text;
using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeSaveFingerprintTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FingerprintCoversEveryByteIncludingDataAfterTimestamp(bool native)
    {
        var prefix = "{\"SaveTime\":{\"value\":-1234567890123456789},\"items\":\"";
        var bytes = Encoding.UTF8.GetBytes(prefix + new string('a', 200_000) + "\"}");
        using var stream = new MemoryStream(bytes);
        var actual = NativeSaveFingerprint.Read(stream, native);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), actual.Hash);
        Assert.Equal(-1234567890123456789, actual.SaveTime);
        bytes[^3] = (byte)'b';
        stream.Position = 0;
        Assert.NotEqual(actual.Hash, NativeSaveFingerprint.Read(stream, native).Hash);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(32)]
    [InlineData(65536)]
    public void TimestampAndUtf8MayCrossAnyReadBoundary(int chunk)
    {
        var bytes = Encoding.UTF8.GetBytes(new string(' ', 65531) + "{\"Name\":\"Ä 鸭\",\"SaveTime\" : { \"__type\": \"System.Int64\", \"value\" : 9223372036854775807 }}");
        using var stream = new ShortReadStream(bytes, chunk);
        var actual = NativeSaveFingerprint.Read(stream);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), actual.Hash);
        Assert.Equal(long.MaxValue, actual.SaveTime);
        Assert.Equal(0, stream.Rewinds);
    }

    [Theory]
    [InlineData("{}", null)]
    [InlineData("{\"SaveTime\":{\"value\":9223372036854775808}}", null)]
    [InlineData("{\"SaveTime\":{\"value\":-9223372036854775808}}", long.MinValue)]
    [InlineData("{\"Other\":\"SaveTime\",\"SaveTime\":{\"value\":42}}", 42L)]
    public void MissingMalformedAndAmbiguousEntriesRetainExistingParserBehavior(string text, long? expected)
    {
        using var stream = new ShortReadStream(Encoding.UTF8.GetBytes(text), 3);
        Assert.Equal(expected, NativeSaveFingerprint.Read(stream).SaveTime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnusualEncodingOrOversizedEntryUsesCompatibleFallback(bool utf16)
    {
        var text = "{\"SaveTime\":{\"padding\":\"" + new string('x', 5000) + "\",\"value\":123}}";
        var encoding = utf16 ? Encoding.Unicode : Encoding.UTF8;
        var bytes = encoding.GetPreamble().Concat(encoding.GetBytes(text)).ToArray();
        using var stream = new MemoryStream(bytes);
        var actual = NativeSaveFingerprint.Read(stream);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), actual.Hash);
        Assert.Equal(123, actual.SaveTime);
    }

    [Fact]
    public void EmptyFileHasStandardSha256AndNoTimestamp()
    {
        using var stream = new MemoryStream();
        Assert.Equal(("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", (long?)null), NativeSaveFingerprint.Read(stream));
    }

    [Fact]
    public void ReadFailurePropagatesWithoutPublishingAnIncompleteFingerprint()
    {
        using var stream = new FailingStream();
        Assert.Throws<IOException>(() => NativeSaveFingerprint.Read(stream));
    }

    private sealed class ShortReadStream(byte[] bytes, int chunk) : MemoryStream(bytes)
    {
        internal int Rewinds { get; private set; }
        public override long Position
        {
            get => base.Position;
            set { if (value == 0) Rewinds++; base.Position = value; }
        }
        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunk));
    }

    private sealed class FailingStream : MemoryStream
    {
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("Simulated read interruption.");
    }
}
