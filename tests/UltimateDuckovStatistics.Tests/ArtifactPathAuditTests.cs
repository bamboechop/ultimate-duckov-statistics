using System.Buffers.Binary;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ArtifactAudit;

namespace UltimateDuckovStatistics.Tests;

public sealed class ArtifactPathAuditTests
{
    [Theory]
    [InlineData("UltimateDuckovStatistics.EncounterPrototype")]
    [InlineData("UltimateDuckovStatistics.Encounters.Diagnostics")]
    public void OrdinaryReleaseRejectsEncounterDiagnosticsEvenWithoutCallSites(string typeNamespace)
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("EncounterPrototype"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddTypeDefinition(System.Reflection.TypeAttributes.Public,
            metadata.GetOrAddString(typeNamespace), metadata.GetOrAddString("Probe"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        using var directory = new TemporaryDirectory();
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(), new MetadataRootBuilder(metadata), new BlobBuilder());
        var bytes = new BlobBuilder(); pe.Serialize(bytes);
        var path = Path.Combine(directory.Path, "prototype.dll");
        File.WriteAllBytes(path, bytes.ToArray());
        var error = Assert.Throws<InvalidDataException>(() => OrdinaryReleaseAudit.Verify(path));
        Assert.Contains("encounter diagnostic", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryNativeModCannotSilentlyOmitEncounterRuntime()
    {
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("UltimateDuckovStatistics"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        metadata.AddAssembly(metadata.GetOrAddString("UltimateDuckovStatistics"), new Version(1, 0, 0, 0), default, default, 0, 0);
        metadata.AddTypeDefinition(System.Reflection.TypeAttributes.Public,
            metadata.GetOrAddString("UltimateDuckovStatistics"), metadata.GetOrAddString("ModBehaviour"),
            default, MetadataTokens.FieldDefinitionHandle(1), MetadataTokens.MethodDefinitionHandle(1));
        using var directory = new TemporaryDirectory();
        var pe = new ManagedPEBuilder(new PEHeaderBuilder(), new MetadataRootBuilder(metadata), new BlobBuilder());
        var bytes = new BlobBuilder(); pe.Serialize(bytes);
        var path = Path.Combine(directory.Path, "UltimateDuckovStatistics.dll");
        File.WriteAllBytes(path, bytes.ToArray());
        var error = Assert.Throws<InvalidDataException>(() => OrdinaryReleaseAudit.Verify(path));
        Assert.Contains("missing encounter runtime", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PackageAuditsRequireTheExactPinnedNativeDependency()
    {
        var native = Path.Combine(AppContext.BaseDirectory, "sqlite3.dll");
        Assert.Equal(1, ArtifactPathAudit.Audit([native], []));
        OrdinaryReleaseAudit.Verify(native);
        using var directory = new TemporaryDirectory();
        var changed = Path.Combine(directory.Path, "sqlite3.dll");
        var bytes = File.ReadAllBytes(native);
        bytes[^1] ^= 1;
        File.WriteAllBytes(changed, bytes);
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([changed], []));
        Assert.Throws<InvalidDataException>(() => OrdinaryReleaseAudit.Verify(changed));
        var renamed = Path.Combine(directory.Path, "unknown.dll");
        File.Copy(native, renamed);
        Assert.Throws<BadImageFormatException>(() => ArtifactPathAudit.Audit([renamed], []));
    }

    [Theory]
    [InlineData("UdsPrototype.SQLiteRaw.Core.dll")]
    [InlineData("UdsPrototype.SQLiteRaw.Provider.dll")]
    public void ProviderVersionIsIndependentlyPinnedWhilePackagePathsAndIlAreStillAudited(string name)
    {
        var provider = Path.Combine(AppContext.BaseDirectory, name);
        Assert.Equal(1, ArtifactPathAudit.Audit([provider], [], Core.ProductInfo.Version));
        OrdinaryReleaseAudit.Verify(provider);
        using var directory = new TemporaryDirectory();
        var changed = Path.Combine(directory.Path, name);
        var bytes = File.ReadAllBytes(provider);
        bytes[^1] ^= 1;
        File.WriteAllBytes(changed, bytes);
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([changed], [], Core.ProductInfo.Version));
        Assert.Throws<InvalidDataException>(() => OrdinaryReleaseAudit.Verify(changed));
    }

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
    public void NumericMetadataIndexesAreNotTextButUncPathsInPeHeapsAndPayloadsStillReject()
    {
        using var directory = new TemporaryDirectory();
        var metadata = new MetadataBuilder();
        metadata.AddModule(0, metadata.GetOrAddString("NumericIndexes"), metadata.GetOrAddGuid(Guid.NewGuid()), default, default);
        // Valid local-variable signatures whose blob indexes happen to spell
        // the same UNC-like bytes observed in an actual diagnostic Core build.
        metadata.GetOrAddBlob(new byte[0x5c5c - 5]);
        foreach (var count in new[] { 3, 12, 30, 1 })
        {
            var signature = new byte[] { 0x07, (byte)count }.Concat(Enumerable.Repeat((byte)0x08, count)).ToArray();
            metadata.AddStandaloneSignature(metadata.GetOrAddBlob(signature));
        }
        var binary = WritePe(metadata);
        Assert.Contains("\\\\b\\q\\", Encoding.UTF8.GetString(File.ReadAllBytes(binary)), StringComparison.Ordinal);
        Assert.Equal(1, ArtifactPathAudit.Audit([binary], []));
        var withPath = new MetadataBuilder();
        withPath.AddModule(0, withPath.GetOrAddString("PathPayload"), withPath.GetOrAddGuid(Guid.NewGuid()), default, default);
        withPath.GetOrAddUserString("\\\\server\\share\\builder\\Mod.cs");
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([WritePe(withPath)], []));
        var opaque = Path.Combine(directory.Path, "payload.bin");
        File.WriteAllText(opaque, "\\\\b\\q\\Mod.cs");
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([opaque], []));
        var pdb = WritePdb(directory.Path, "\\\\server\\share\\Mod.cs");
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([pdb], []));

        string WritePe(MetadataBuilder builder)
        {
            var pe = new ManagedPEBuilder(new PEHeaderBuilder(), new MetadataRootBuilder(builder), new BlobBuilder());
            var bytes = new BlobBuilder(); pe.Serialize(bytes);
            var path = Path.Combine(directory.Path, "indexes.dll");
            File.WriteAllBytes(path, bytes.ToArray());
            return path;
        }
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
