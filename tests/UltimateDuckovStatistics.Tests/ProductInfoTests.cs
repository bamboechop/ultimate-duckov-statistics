using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void CurrentReleaseRetainsV1SchemaBaseline()
    {
        Assert.Equal("1.1.1", ProductInfo.Version);
        Assert.Equal("uds-profile-v1", ProductInfo.ProfileFormatId);
        Assert.Equal(1, ProductInfo.SchemaVersion);
        Assert.Equal("v1", ProductInfo.DataDirectory);
    }
}
