using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForMvp()
    {
        Assert.Equal("0.1.0", ProductInfo.Version);
        Assert.Equal(1, ProductInfo.SchemaVersion);
    }
}
