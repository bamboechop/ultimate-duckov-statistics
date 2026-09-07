using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ArtifactAudit;

namespace UltimateDuckovStatistics.Tests;

public sealed class ArtifactPathAuditTests
{
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
        File.WriteAllBytes(path, new byte[alignment].Concat(encoding.GetBytes("E:\\game-install\\Duckov_Data\\Managed\\native.dll")).ToArray());
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], []));
        File.WriteAllBytes(path, encoding.GetBytes("built by buildtester"));
        Assert.Throws<InvalidDataException>(() => ArtifactPathAudit.Audit([path], ["buildtester"]));
        File.WriteAllText(path, "Documentation: https://example.org/install and <game>/Duckov_Data/Mods.");
        Assert.Equal(1, ArtifactPathAudit.Audit([path], []));
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
