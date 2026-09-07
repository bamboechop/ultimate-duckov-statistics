using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeGrenadeAttributionTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void UnprovenExplosionsKeepTheirExistingClassification(int missing)
    {
        var source = new CharacterMainControl { IsMainCharacter = missing != 0 };
        var damage = new DamageInfo { fromCharacter = source, fromWeaponItemID = missing == 1 ? 0 : 123, isExplosion = missing != 2 };
        var grenade = new Grenade { damageInfo = damage, createExplosion = missing != 3 };
        var scope = NativeGrenadeAttribution.Begin(grenade);
        try { Assert.Equal(CombatAttackKind.Effect, NativeGrenadeAttribution.Classify(missing != 4, damage, CombatAttackKind.Effect)); }
        finally { NativeGrenadeAttribution.End(scope); }
    }

    [Fact]
    public void SourceMismatchAndNestedActorlessExplosionCannotBorrowOuterThrowableEvidence()
    {
        var player = new CharacterMainControl { IsMainCharacter = true };
        var damage = new DamageInfo { fromCharacter = player, fromWeaponItemID = 123, isExplosion = true };
        var outer = NativeGrenadeAttribution.Begin(new Grenade { damageInfo = damage });
        try
        {
            Assert.True(NativeGrenadeAttribution.Matches(damage));
            Assert.False(NativeGrenadeAttribution.Matches(new DamageInfo { fromCharacter = new CharacterMainControl { IsMainCharacter = true }, fromWeaponItemID = 123, isExplosion = true }));
            var inner = NativeGrenadeAttribution.Begin(new Grenade());
            try { Assert.False(NativeGrenadeAttribution.Matches(damage)); }
            finally { NativeGrenadeAttribution.End(inner); }
            Assert.True(NativeGrenadeAttribution.Matches(damage));
        }
        finally { NativeGrenadeAttribution.End(outer); }
        Assert.False(NativeGrenadeAttribution.Matches(damage));
    }

    [Fact]
    public void GrenadeHookFailureOnlyDisablesThrowableClassification()
    {
        var hooks = new CombatHookSupport { HealthHurt = true, BuffApplication = true, EffectTrigger = true };
        var capabilities = CombatNativeContractPolicy.CreateCapabilities(hooks);
        Assert.Equal(AdapterCapabilityState.Supported, capabilities.KillsByYou.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capabilities.ThrowableKills.State);
        hooks.GrenadeExplosion = true;
        Assert.Equal(AdapterCapabilityState.Supported, CombatNativeContractPolicy.CreateCapabilities(hooks).ThrowableKills.State);
    }
}
