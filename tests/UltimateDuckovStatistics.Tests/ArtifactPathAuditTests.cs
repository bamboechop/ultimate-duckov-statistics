using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ArtifactAudit;

namespace UltimateDuckovStatistics.Tests;

public sealed class ArtifactPathAuditTests
{
    [Fact]
    public void OrdinaryReleaseIlRejectsTheInstrumentedAdapterComposition()
    {
        OrdinaryReleaseAudit.Verify(typeof(Core.ProductInfo).Assembly.Location);
        var error = Assert.Throws<InvalidDataException>(() => OrdinaryReleaseAudit.Verify(typeof(ArtifactPathAuditTests).Assembly.Location));
        Assert.Contains("performance-diagnostic call site", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("C:\\Users\\builder\\checkout\\Mod.cs")]
    [InlineData("/home/builder/checkout/Mod.cs")]
    [InlineData("/unusual-build-root/checkout/Mod.cs")]
    public void RejectsAbsoluteSourceIdentityDecodedFromPortablePdb(string source)
    {
        using var directory = new TemporaryDirectory();
        var path = WritePdb(directory.Path, source);
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], []));
    }

    [Fact]
    public void AllowsNormalizedSourceIdentityAndScansRealManagedPe()
    {
        using var directory = new TemporaryDirectory();
        var path = WritePdb(directory.Path, "/_/uds/src/Mod.cs");
        Assert.Equal(2, ArtifactPathAudit.Audit([path, typeof(Core.ProductInfo).Assembly.Location], []));
        Assert.Equal(1, ArtifactPathAudit.Audit([typeof(Core.ProductInfo).Assembly.Location], [], Core.ProductInfo.Version));
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([typeof(Core.ProductInfo).Assembly.Location], [], "9.9.9"));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    public void RejectsPathsAndExplicitBuilderNamesInsideOpaquePayloads(bool unicode, int alignment)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "payload.bin");
        var encoding = unicode ? Encoding.Unicode : Encoding.UTF8;
        File.WriteAllBytes(path, encoding.GetBytes("/unusual-build-root/checkouts/Mod.cs"));
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], []));
        File.WriteAllBytes(path, new byte[alignment].Concat(encoding.GetBytes("E:\\game-install\\Duckov_Data\\Managed\\native.dll")).ToArray());
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], []));
        File.WriteAllBytes(path, encoding.GetBytes("built by buildtester"));
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], ["buildtester"]));
        File.WriteAllText(path, "Documentation: https://example.org/install and <game>/Duckov_Data/Mods.");
        Assert.Equal(1, ArtifactPathAudit.Audit([path], []));
    }

    [Fact]
    public void PngPixelBytesAreNotMistakenForMachinePaths()
    {
        using var directory = new TemporaryDirectory();
        var path = WritePng(directory.Path, null, []);
        // Stored-deflate pixel bytes deliberately look like a builder path.
        Assert.Contains("C:\\Users\\buildtester", Encoding.UTF8.GetString(File.ReadAllBytes(path)), StringComparison.Ordinal);
        Assert.Equal(1, ArtifactPathAudit.Audit([path], ["buildtester"]));
    }

    [Theory]
    [InlineData("tEXt", false)]
    [InlineData("zTXt", true)]
    [InlineData("iTXt", false)]
    [InlineData("iTXt", true)]
    [InlineData("iCCP", true)]
    public void PngMetadataStillRejectsPathsAndBuilderNames(string type, bool compressed)
    {
        using var directory = new TemporaryDirectory();
        foreach (var text in new[] { "C:\\Users\\buildtester\\image.png", "Created by buildtester", "Public artwork" })
        {
            var data = Encoding.UTF8.GetBytes(text);
            if (compressed) data = Compress(data);
            var header = type == "iTXt" ? new byte[] { 0, compressed ? (byte)1 : (byte)0, 0, 0, 0 }
                : compressed ? [0, 0] : new byte[] { 0 };
            var path = WritePng(directory.Path, type, Encoding.ASCII.GetBytes("Description").Concat(header).Concat(data).ToArray());
            if (text == "Public artwork") Assert.Equal(1, ArtifactPathAudit.Audit([path], ["buildtester"]));
            else Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], ["buildtester"]));
        }
    }

    [Fact]
    public void PngAuditRejectsInvalidSignatureTruncationAndTrailingData()
    {
        using var directory = new TemporaryDirectory();
        var path = WritePng(directory.Path, null, []);
        var valid = File.ReadAllBytes(path);
        foreach (var invalid in new[] { Encoding.UTF8.GetBytes("Renamed text file"), valid[..^1], valid.Concat(new byte[] { 1 }).ToArray() })
        {
            File.WriteAllBytes(path, invalid);
            Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], []));
        }
    }

    private static string WritePng(string directory, string? metadataType, byte[] metadata)
    {
        using var png = new MemoryStream();
        png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 16);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), 1);
        header[8] = 8;
        header[9] = 2;
        WriteChunk("IHDR", header);
        var pixels = new byte[49]; // One unfiltered row of sixteen RGB pixels.
        Encoding.ASCII.GetBytes("C:\\Users\\buildtester\\image.png").CopyTo(pixels, 1);
        WriteChunk("IDAT", Compress(pixels));
        if (metadataType != null) WriteChunk(metadataType, metadata);
        WriteChunk("IEND", []);
        var path = Path.Combine(directory, "preview.png");
        File.WriteAllBytes(path, png.ToArray());
        return path;

        void WriteChunk(string type, byte[] data)
        {
            var payload = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            var field = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(field, (uint)data.Length);
            png.Write(field);
            png.Write(payload);
            var crc = uint.MaxValue;
            foreach (var value in payload)
            {
                crc ^= value;
                for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xedb88320u : crc >> 1;
            }
            BinaryPrimitives.WriteUInt32BigEndian(field, ~crc);
            png.Write(field);
        }
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var compressor = new ZLibStream(output, CompressionLevel.NoCompression, leaveOpen: true)) compressor.Write(data);
        return output.ToArray();
    }

    private static string WritePdb(string directory, string source)
    {
        var metadata = new MetadataBuilder();
        metadata.AddDocument(metadata.GetOrAddDocumentName(source), default, default, default);
        var builder = new PortablePdbBuilder(metadata, ImmutableArray.Create(new int[64]), default);
        var blob = new BlobBuilder();
        builder.Serialize(blob);
        var path = Path.Combine(directory, "source.pdb");
        File.WriteAllBytes(path, blob.ToArray());
        return path;
    }
}
