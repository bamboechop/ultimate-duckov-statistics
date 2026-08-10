using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForRunLifecycleAndMovement()
    {
        Assert.Equal("0.3.0", ProductInfo.Version);
        Assert.Equal(3, ProductInfo.SchemaVersion);
    }
}
