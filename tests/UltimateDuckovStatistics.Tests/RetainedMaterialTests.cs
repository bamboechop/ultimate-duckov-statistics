using UltimateDuckovStatistics.UI;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

public sealed class RetainedMaterialTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NativePresentationDifferencesPreserveOwnedMaterialAndSource(bool shadowSupported)
    {
        var source = new Material { name = "Long localized native material name", UnderlaySupported = shadowSupported, Ratio = .125f };
        source.Keywords.Add("RATIOS_OFF");
        var originalColor = source.Underlay;
        var owner = RetainedTabLabelMaterial.Create(source);
        var instance = owner.Instance;

        Assert.NotSame(source, instance);
        Assert.Same(source.Atlas, instance.Atlas);
        Assert.Equal(.125f, source.Ratio);
        Assert.Equal(originalColor, source.Underlay);
        Assert.Single(source.Keywords);
        Assert.Equal(shadowSupported ? .314f : .125f, instance.Ratio);
        Assert.Equal(0, source.DestroyCount);
        owner.Dispose();
        owner.Dispose();
        Assert.Equal(1, instance.DestroyCount);
        Assert.Equal(0, source.DestroyCount);
        Assert.Throws<ObjectDisposedException>(() => owner.Instance);
    }

    [Fact]
    public void MissingRequiredMaterialRemainsAConstructionFailure() =>
        Assert.Throws<ArgumentNullException>(() => RetainedTabLabelMaterial.Create(null!));
}
