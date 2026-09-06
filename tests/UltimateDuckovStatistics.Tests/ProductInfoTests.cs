using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void NativeUiVersionRetainsM17WithRunsDataSchema17()
    {
        Assert.Equal("0.17.0", ProductInfo.Version);
        Assert.Equal(18, ProductInfo.SchemaVersion);
    }
}
