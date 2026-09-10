using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeGrenadeHazardOriginsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealBuffPrefixDistinguishesNewInstanceFromSameIdReapplication(bool existing)
    {
        var manager = new CharacterBuffManager();
        var prefab = new Duckov.Buffs.Buff { ID = 42 };
        if (existing) manager.Buffs.Add(new Duckov.Buffs.Buff { ID = 42 });
        object?[] arguments = [manager, prefab, false];
        HealingHarmonyCallbacks.BuffPrefixMethod.Invoke(null, arguments);
        Assert.Equal(!existing, arguments[2]);
    }

    [Fact]
    public void SpawnedZoneAndRepeatedBurnKeepThrowerItemAndLaunchEquipmentAcrossSwitches()
    {
        var tracker = new NativeGrenadeHazardOrigins();
        tracker.Synchronize("generation-a", "run-a");
        var player = new CharacterMainControl { IsMainCharacter = true };
        var grenade = new object();
        var equipment = Equipment("sr3m");
        tracker.CaptureLaunch(grenade, new(player, 0, "map-a", "segment-a", equipment));
        equipment.SelectedWeaponId = "bow";
        var origin = Assert.IsType<NativeGrenadeHazardOrigins.Origin>(tracker.ResolveLaunch(grenade, player, 941));
        Assert.Equal("sr3m", origin.Equipment.SelectedWeaponId);
        var zone = new object();
        tracker.CaptureZone(zone, origin);
        var buff = new object();
        tracker.CaptureBuff(buff, tracker.ResolveZone(zone), null, 0, newlyCreated: true);
        equipment.SelectedWeaponId = "sr3m";
        tracker.CaptureBuff(buff, tracker.ResolveZone(zone), null, 0, newlyCreated: false);
        Assert.True(tracker.TryResolveBuff(buff, null, 0, out var burning));
        Assert.Same(origin, burning);
        Assert.Equal(941, burning!.ItemId);
        Assert.Equal("segment-a", burning.SegmentId);
        Assert.Same(player, burning.Actor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameFirePrefabNeverMakesAnEnemyGrenadePlayerOwned(bool nest)
    {
        var prefab = new UnityEngine.Object();
        var player = new CharacterMainControl { IsMainCharacter = true };
        var npc = new CharacterMainControl();
        var grenade = new Grenade { createOnExlode = prefab, damageInfo = new() { fromCharacter = player } };
        var outer = NativeGrenadeAttribution.Begin(grenade);
        NativeGrenadeAttribution.Scope? inner = null;
        try
        {
            if (nest) inner = NativeGrenadeAttribution.Begin(new Grenade { createOnExlode = prefab, damageInfo = new() { fromCharacter = npc } });
            Assert.Same(nest ? npc : player, NativeGrenadeAttribution.MatchCreatedPrefab(prefab)!.Source);
            Assert.Null(NativeGrenadeAttribution.MatchCreatedPrefab(new UnityEngine.Object()));
        }
        finally
        {
            NativeGrenadeAttribution.End(inner);
            NativeGrenadeAttribution.End(outer);
        }
        Assert.Null(NativeGrenadeAttribution.MatchCreatedPrefab(prefab));
    }

    [Theory]
    [InlineData(0)] // Earlier unobserved/native buff already existed when UDS first saw a hazard.
    [InlineData(1)] // Actorless application preceded the player hazard.
    [InlineData(2)] // Actorless application followed the player hazard.
    [InlineData(3)] // Enemy fire overlapped player fire.
    public void MissingOrMixedOwnersNeverTurnAnAmbiguousBurnIntoAPlayerKill(int scenario)
    {
        var tracker = new NativeGrenadeHazardOrigins();
        var buff = new object();
        var player = new CharacterMainControl { IsMainCharacter = true };
        var origin = new NativeGrenadeHazardOrigins.Origin(player, 941, "map", "segment", Equipment("sr3m"));
        if (scenario == 1) tracker.CaptureBuff(buff, null, null, 0, newlyCreated: true);
        tracker.CaptureBuff(buff, origin, null, 0, newlyCreated: scenario != 0 && scenario != 1);
        if (scenario == 2) tracker.CaptureBuff(buff, null, null, 0, newlyCreated: false);
        if (scenario == 3) tracker.CaptureBuff(buff, new(new CharacterMainControl(), 941, "map", "segment", Equipment("npc")), null, 0, false);
        tracker.CaptureBuff(buff, origin, null, 0, newlyCreated: false);
        Assert.True(tracker.TryResolveBuff(buff, null, 0, out var resolved));
        Assert.Null(resolved);
    }

    [Fact]
    public void DifferentPlayerThrowLoadoutsLoseOnlyTheConflictingDimensions()
    {
        var tracker = new NativeGrenadeHazardOrigins();
        var buff = new object();
        var player = new CharacterMainControl { IsMainCharacter = true };
        tracker.CaptureBuff(buff, new(player, 941, "map-a", "segment-a", Equipment("sr3m")), null, 0, true);
        tracker.CaptureBuff(buff, new(player, 941, "map-a", "segment-a", Equipment("bow")), null, 0, false);
        Assert.True(tracker.TryResolveBuff(buff, null, 0, out var resolved));
        Assert.Same(player, resolved!.Actor);
        Assert.Equal(941, resolved.ItemId);
        Assert.Equal(string.Empty, resolved.Equipment.SelectedWeaponId);
        tracker.CaptureBuff(buff, new(player, 942, "map-b", "segment-b", Equipment("bow")), null, 0, false);
        Assert.True(tracker.TryResolveBuff(buff, null, 0, out resolved));
        Assert.Same(player, resolved!.Actor);
        Assert.Equal(0, resolved.ItemId);
        Assert.Equal(MapIdentity.UnknownId, resolved.MapId);
        Assert.Equal(string.Empty, resolved.SegmentId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownPlayerBuffAndPlayerFireRetainCommonOwnershipInEitherOrder(bool grenadeFirst)
    {
        var tracker = new NativeGrenadeHazardOrigins();
        var buff = new object();
        var actor = new CharacterMainControl { IsMainCharacter = true };
        var grenade = new NativeGrenadeHazardOrigins.Origin(actor, 941, "map", "segment", Equipment("sr3m"));
        var other = new NativeGrenadeHazardOrigins.Origin(actor, 123, "map", "segment", Equipment("bow"));
        tracker.CaptureBuff(buff, grenadeFirst ? grenade : other, null, 0, true, grenadeFirst);
        Assert.Equal(grenadeFirst, tracker.TryResolveBuff(buff, null, 0, out _));
        tracker.CaptureBuff(buff, grenadeFirst ? other : grenade, null, 0, false, !grenadeFirst);
        Assert.True(tracker.TryResolveBuff(buff, null, 0, out var resolved));
        Assert.Same(actor, resolved!.Actor);
        Assert.Equal(0, resolved.ItemId);
        Assert.Equal(string.Empty, resolved.Equipment.SelectedWeaponId);
    }

    [Theory]
    [InlineData("generation-b", "run-a")]
    [InlineData("generation-a", "run-b")]
    [InlineData("", "")]
    public void ProfileOrRunChangeCannotBorrowPriorGrenadeZoneOrBuff(string generation, string run)
    {
        var tracker = new NativeGrenadeHazardOrigins();
        tracker.Synchronize("generation-a", "run-a");
        var actor = new CharacterMainControl { IsMainCharacter = true };
        var origin = new NativeGrenadeHazardOrigins.Origin(actor, 941, "map", "segment", Equipment("sr3m"));
        var grenade = new object();
        var zone = new object();
        var buff = new object();
        tracker.CaptureLaunch(grenade, origin);
        tracker.CaptureZone(zone, origin);
        tracker.CaptureBuff(buff, origin, null, 0, true);
        tracker.Synchronize(generation, run);
        Assert.Null(tracker.ResolveLaunch(grenade, actor, 941));
        Assert.Null(tracker.ResolveZone(zone));
        Assert.False(tracker.TryResolveBuff(buff, null, 0, out _));
    }

    [Fact]
    public void NativeActorContradictionFailsClosedButWeaponConflictPreservesOwnership()
    {
        var tracker = new NativeGrenadeHazardOrigins();
        var actor = new CharacterMainControl { IsMainCharacter = true };
        var origin = new NativeGrenadeHazardOrigins.Origin(actor, 941, "map", "segment", Equipment("sr3m"));
        var buff = new object();
        tracker.CaptureBuff(buff, origin, null, 0, true);
        Assert.True(tracker.TryResolveBuff(buff, actor, 942, out var differentWeapon));
        Assert.Same(actor, differentWeapon!.Actor);
        Assert.Equal(0, differentWeapon.ItemId);
        Assert.True(tracker.TryResolveBuff(buff, new CharacterMainControl(), 941, out var conflict));
        Assert.Null(conflict);
        Assert.Null(tracker.ResolveZone(new object()));
        Assert.Null(tracker.ResolveLaunch(new object(), actor, 941));
    }

    private static EquipmentEventAssociation Equipment(string weapon) => new()
    {
        LoadoutId = "loadout",
        SelectedWeaponId = weapon,
        SelectedWeaponSlotId = "primary",
        TotemSetId = "totems"
    };
}
