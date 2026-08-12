using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeCombatEquipmentAssociationResolverTests
{
    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void ProjectileHealthTransitionKeepsFireTimeAssociationAfterImpactTimeSwap()
    {
        var firedWith = Association("loadout:a", "weapon:a");
        var impactWith = Association("loadout:b", "weapon:b");

        var resolved = NativeCombatEquipmentAssociationResolver.ResolveHealthTransition(firedWith, impactWith);

        Assert.Equal("loadout:a", resolved.LoadoutId);
        Assert.Equal("weapon:a", resolved.SelectedWeaponId);
        Assert.NotSame(firedWith, resolved);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void UnscopedHealthTransitionUsesTheImpactTimeAssociation()
    {
        var impactWith = Association("loadout:b", "weapon:b");

        var resolved = NativeCombatEquipmentAssociationResolver.ResolveHealthTransition(null, impactWith);

        Assert.Equal("loadout:b", resolved.LoadoutId);
        Assert.Equal("weapon:b", resolved.SelectedWeaponId);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void DelayedEffectTicksKeepProvenApplicationAssociationAfterLoadoutSwap()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var trigger = new object();
        var current = Association("loadout:b", "weapon:b");

        resolver.CaptureDelayedEffectOrigin(
            trigger, Association("loadout:a", "weapon:a"), "generation", "run", "map");
        var tick = resolver.ResolveEffect(
            trigger, delayed: true, originatingScope: null, () => current,
            "generation", "run", "map");

        Assert.Equal("loadout:a", tick.LoadoutId);
        Assert.Equal("weapon:a", tick.SelectedWeaponId);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void DelayedEffectWithoutProvenOriginIsUnavailableInsteadOfUsingTickTimeLoadout()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var providerCalls = 0;

        var tick = resolver.ResolveEffect(
            new object(), delayed: true, originatingScope: null,
            () => { providerCalls++; return Association("loadout:b", "weapon:b"); },
            "generation", "run", "map");

        Assert.Equal(0, providerCalls);
        Assert.Equal(EquipmentEventAssociation.UnavailableId, tick.LoadoutId);
        Assert.Equal(EquipmentEventAssociation.UnavailableId, tick.TotemSetId);
        Assert.Empty(tick.SelectedWeaponId);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void ReusedDelayedTriggerWithConflictingOriginsBecomesUnavailable()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var trigger = new object();
        resolver.CaptureDelayedEffectOrigin(
            trigger, Association("loadout:a", "weapon:a"), "generation", "run", "map");

        resolver.CaptureDelayedEffectOrigin(
            trigger, Association("loadout:b", "weapon:b"), "generation", "run", "map");
        var laterTick = resolver.ResolveEffect(trigger, true, null,
            () => Association("loadout:current", "weapon:current"), "generation", "run", "map");

        Assert.Equal(EquipmentEventAssociation.UnavailableId, laterTick.LoadoutId);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void DelayedEffectOriginCannotCrossRunContext()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var trigger = new object();
        resolver.CaptureDelayedEffectOrigin(
            trigger, Association("loadout:a", "weapon:a"), "generation", "run:a", "map");

        var differentRunTick = resolver.ResolveEffect(
            trigger, true, null, () => Association("loadout:b", "weapon:b"),
            "generation", "run:b", "map");

        Assert.Equal(EquipmentEventAssociation.UnavailableId, differentRunTick.LoadoutId);
    }

    private static EquipmentEventAssociation Association(string loadoutId, string weaponId) => new()
    {
        LoadoutId = loadoutId,
        SelectedWeaponSlotId = "slot:primary",
        SelectedWeaponId = weaponId,
        TotemSetId = "totems:a"
    };
}
