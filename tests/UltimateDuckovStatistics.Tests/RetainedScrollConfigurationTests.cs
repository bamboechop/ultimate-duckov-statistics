using Duckov.Utilities;
using UltimateDuckovStatistics.UI;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class RetainedScrollConfigurationTests
{
    [Theory]
    [InlineData(ScrollRect.MovementType.Unrestricted)]
    [InlineData(ScrollRect.MovementType.Elastic)]
    [InlineData(ScrollRect.MovementType.Clamped)]
    public void RetainedPagesClampRegardlessOfNativeMovementModeAndKeepNativeInputFeel(ScrollRect.MovementType mode)
    {
        var source = GameplayDataSettings.UIPrefabs.ScrollRect;
        source.movementType = mode; source.elasticity = .17f; source.inertia = true;
        source.decelerationRate = .22f; source.scrollSensitivity = 37;
        var target = new ScrollRect();
        RunsNativeScrollConfiguration.Apply(target);
        Assert.Equal(ScrollRect.MovementType.Clamped, target.movementType);
        Assert.Equal(mode, source.movementType);
        Assert.Equal(source.inertia, target.inertia);
        Assert.Equal(source.elasticity, target.elasticity);
        Assert.Equal(source.decelerationRate, target.decelerationRate);
        Assert.Equal(source.scrollSensitivity, target.scrollSensitivity);
    }
}
