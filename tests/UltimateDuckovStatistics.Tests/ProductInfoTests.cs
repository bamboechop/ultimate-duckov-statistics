using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForHealingAttribution()
    {
        Assert.Equal("0.2.0", ProductInfo.Version);
        Assert.Equal(2, ProductInfo.SchemaVersion);
    }
}
