using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForCombatAttribution()
    {
        Assert.Equal("0.5.0", ProductInfo.Version);
        Assert.Equal(5, ProductInfo.SchemaVersion);
    }
}
