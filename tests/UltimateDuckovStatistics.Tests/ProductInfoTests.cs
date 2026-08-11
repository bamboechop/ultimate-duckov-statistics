using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForWeaponsAndAmmunition()
    {
        Assert.Equal("0.4.0", ProductInfo.Version);
        Assert.Equal(4, ProductInfo.SchemaVersion);
    }
}
