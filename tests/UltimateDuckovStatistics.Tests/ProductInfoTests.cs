using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void ReleaseCandidateHasDistinctV1FormatIdentity()
    {
        Assert.Equal("1.0.0-rc.1", ProductInfo.Version);
        Assert.Equal("uds-profile-v1", ProductInfo.ProfileFormatId);
        Assert.Equal("v1", ProductInfo.DataDirectory);
    }
}
