using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForCombatOwnership()
    {
        Assert.Equal("0.11.0", ProductInfo.Version);
        Assert.Equal(11, ProductInfo.SchemaVersion);
    }
}
