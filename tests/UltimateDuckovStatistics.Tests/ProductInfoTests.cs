using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForLosslessRouteAssociation()
    {
        Assert.Equal("0.10.0", ProductInfo.Version);
        Assert.Equal(10, ProductInfo.SchemaVersion);
    }
}
