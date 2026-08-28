using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForLosslessWeaponAndEquipmentSlotAssociations()
    {
        Assert.Equal("0.14.0", ProductInfo.Version);
        Assert.Equal(14, ProductInfo.SchemaVersion);
    }
}
