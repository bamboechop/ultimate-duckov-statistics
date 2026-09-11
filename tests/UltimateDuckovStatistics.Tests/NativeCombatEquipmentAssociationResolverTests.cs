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
        var providerCalls = 0;

        resolver.CaptureDelayedEffectOrigin(
            trigger, Association("loadout:a", "weapon:a"), "generation", "run", "map", "source-segment");
        var tick = resolver.ResolveEffect(
            trigger, delayed: true, () => { providerCalls++; return current; },
            "generation", "run");

        Assert.Equal("loadout:a", tick.LoadoutId);
        Assert.Equal("weapon:a", tick.SelectedWeaponId);
        Assert.Equal(0, providerCalls);
        Assert.True(resolver.TryGetOrigin(trigger, "generation", "run", out var sourceMap, out var sourceSegment));
        Assert.Equal("map", sourceMap);
        Assert.Equal("source-segment", sourceSegment);
    }

    [Fact]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void DelayedEffectWithoutProvenOriginIsUnavailableInsteadOfUsingTickTimeLoadout()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var providerCalls = 0;

        var tick = resolver.ResolveEffect(
            new object(), delayed: true,
            () => { providerCalls++; return Association("loadout:b", "weapon:b"); },
            "generation", "run");

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
            trigger, Association("loadout:a", "weapon:a"), "generation", "run", "map", "source-segment");

        resolver.CaptureDelayedEffectOrigin(
            trigger, Association("loadout:b", "weapon:b"), "generation", "run", "map");
        var laterTick = resolver.ResolveEffect(trigger, true,
            () => Association("loadout:current", "weapon:current"), "generation", "run");

        Assert.Equal(EquipmentEventAssociation.UnavailableId, laterTick.LoadoutId);
    }

    [Fact]
    public void EquipmentConflictKeepsSameSegmentOrigin()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var trigger = new object();
        resolver.CaptureDelayedEffectOrigin(trigger, Association("a", "weapon:a"), "g", "r", "map", "segment");
        resolver.CaptureDelayedEffectOrigin(trigger, Association("b", "weapon:b"), "g", "r", "map", "segment");
        Assert.Equal(EquipmentEventAssociation.UnavailableId,
            resolver.ResolveEffect(trigger, true, () => throw new InvalidOperationException(), "g", "r").LoadoutId);
        Assert.True(resolver.TryGetOrigin(trigger, "g", "r", out var map, out var segment));
        Assert.Equal("map", map);
        Assert.Equal("segment", segment);
    }

    [Theory]
    [InlineData("map-b", "segment-b")]
    [InlineData("map-a", "revisit-segment")]
    public void DifferentApplicationSegmentKeepsEquipmentButCannotReuseFirstOrigin(string laterMap, string laterSegment)
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var trigger = new object();
        var equipment = Association("a", "weapon:a");
        resolver.CaptureDelayedEffectOrigin(trigger, equipment, "g", "r", "map-a", "segment-a");
        resolver.CaptureDelayedEffectOrigin(trigger, equipment, "g", "r", laterMap, laterSegment);
        Assert.False(resolver.TryGetOrigin(trigger, "g", "r", out var map, out var segment));
        Assert.Equal(MapIdentity.UnknownId, map);
        Assert.Empty(segment);
        Assert.Equal("a", resolver.ResolveEffect(trigger, true, () => throw new InvalidOperationException(), "g", "r").LoadoutId);
        // Returning to the original segment cannot erase the conflicting application.
        resolver.CaptureDelayedEffectOrigin(trigger, equipment, "g", "r", "map-a", "segment-a");
        Assert.False(resolver.TryGetOrigin(trigger, "g", "r", out _, out _));
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
            trigger, true, () => Association("loadout:b", "weapon:b"),
            "generation", "run:b");

        Assert.Equal(EquipmentEventAssociation.UnavailableId, differentRunTick.LoadoutId);
    }

    [Fact]
    [Trait("Category", "M8")]
    [Trait("Category", "Combat")]
    [Trait("Category", "Equipment")]
    public void DelayedEffectOriginCrossesMapsWithinOneRunWithoutRewritingSourceSegment()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var trigger = new object();
        resolver.CaptureDelayedEffectOrigin(
            trigger,
            Association("loadout:a", "weapon:a"),
            "generation",
            "run",
            "duckov:map:A",
            "run:segment:0");

        var tick = resolver.ResolveEffect(
            trigger,
            true,
            () => Association("loadout:b", "weapon:b"),
            "generation",
            "run");

        Assert.Equal("loadout:a", tick.LoadoutId);
        Assert.True(resolver.TryGetOrigin(trigger, "generation", "run", out var mapId, out var segmentId));
        Assert.Equal("duckov:map:A", mapId);
        Assert.Equal("run:segment:0", segmentId);
    }

    [Fact]
    public void ImmediateEffectEvaluatesItsNativeScopeProviderOnceAndClonesIt()
    {
        var resolver = new NativeCombatEquipmentAssociationResolver();
        var association = Association("loadout:current", "weapon:current");
        var calls = 0;
        var resolved = resolver.ResolveEffect(new object(), delayed: false,
            () => { calls++; return association; }, "generation", "run");
        Assert.Equal(1, calls);
        Assert.Equal(association.LoadoutId, resolved.LoadoutId);
        Assert.NotSame(association, resolved);
    }

    private static EquipmentEventAssociation Association(string loadoutId, string weaponId) => new()
    {
        LoadoutId = loadoutId,
        SelectedWeaponSlotId = "duckov:slot:PrimaryWeapon",
        SelectedWeaponId = weaponId,
        TotemSetId = "totems:a"
    };
}
