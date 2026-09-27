using UltimateDuckovStatistics.UI;
using UnityEngine;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class KillFeedIconGeometryTests
{
    [Fact]
    public void TransparentMarginsAreRemovedWithoutClippingFaintEdgesOrChangingPixels()
    {
        var pixels = new Color32[10 * 8];
        pixels[2 * 10 + 3] = new Color32(20, 30, 40, 1);
        pixels[5 * 10 + 8] = new Color32(50, 60, 70, 255);
        var before = pixels.ToArray();
        Assert.Equal(new Rect(3, 2, 6, 4), KillFeedIconGeometry.VisibleBounds(pixels, 10, 8));
        Assert.Equal(before, pixels);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public void EmptyAndFullyOpaqueImagesKeepValidFullBounds(byte alpha)
    {
        var pixels = Enumerable.Repeat(new Color32(255, 255, 255, alpha), 8 * 5).ToArray();
        Assert.Equal(new Rect(0, 0, 8, 5), KillFeedIconGeometry.VisibleBounds(pixels, 8, 5));
    }

    [Fact]
    public void SingleVisiblePixelRemainsAValidSprite()
    {
        var pixels = new Color32[8 * 5];
        pixels[39] = new Color32(255, 255, 255, 255);
        Assert.Equal(new Rect(7, 4, 1, 1), KillFeedIconGeometry.VisibleBounds(pixels, 8, 5));
    }
}
