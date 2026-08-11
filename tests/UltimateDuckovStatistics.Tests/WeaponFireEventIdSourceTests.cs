using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class WeaponFireEventIdSourceTests
{
    [Fact]
    [Trait("Category", "Weapon")]
    public void EveryNativeFiringCallbackGetsAUniqueIdentityAcrossReloadEquivalentStates()
    {
        var source = new WeaponFireEventIdSource(() => "activation");

        var firstPostShotCountSeven = source.NextEventId();
        var secondPostReloadCountSeven = source.NextEventId();
        var infiniteAmmunitionFollowUp = source.NextEventId();

        Assert.Equal(3, new[]
        {
            firstPostShotCountSeven,
            secondPostReloadCountSeven,
            infiniteAmmunitionFollowUp
        }.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    [Trait("Category", "Weapon")]
    public void EmptyIdentitySeedIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => new WeaponFireEventIdSource(() => string.Empty));
    }
}
