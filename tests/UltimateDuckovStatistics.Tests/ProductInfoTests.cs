using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAdvancesForNativeUiWithoutSchemaChange()
    {
        Assert.Equal("0.17.0", ProductInfo.Version);
        Assert.Equal(16, ProductInfo.SchemaVersion);
    }
}
