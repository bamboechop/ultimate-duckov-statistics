using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForEquipmentAndTotems()
    {
        Assert.Equal("0.6.0", ProductInfo.Version);
        Assert.Equal(6, ProductInfo.SchemaVersion);
    }
}
