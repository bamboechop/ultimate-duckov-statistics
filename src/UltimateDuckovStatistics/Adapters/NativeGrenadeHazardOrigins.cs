using System.Runtime.CompilerServices;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

// Weak instance keys follow native grenade, zone and buff lifetimes. Nothing is written
// back to native DamageInfo/Buff fields, and a later held weapon is never an origin.
internal sealed class NativeGrenadeHazardOrigins
{
    private ConditionalWeakTable<object, Origin> grenades = new();
    private ConditionalWeakTable<object, Origin> zones = new();
    private ConditionalWeakTable<object, BuffOrigin> buffs = new();
    private string generation = string.Empty;
    private string run = string.Empty;

    internal void Synchronize(string generationId, string runId)
    {
        if (generation == generationId && run == runId) return;
        Clear();
        generation = generationId;
        run = runId;
    }

    internal void CaptureLaunch(object grenade, Origin origin)
    {
        grenades.Remove(grenade);
        grenades.Add(grenade, origin);
    }

    internal Origin? ResolveLaunch(object grenade, CharacterMainControl? source, int itemId)
    {
        if (source == null || itemId <= 0 || !grenades.TryGetValue(grenade, out var origin)
            || !ReferenceEquals(source, origin.Actor)) return null;
        return new Origin(origin.Actor, itemId, origin.MapId, origin.SegmentId, origin.Equipment);
    }

    internal void CaptureZone(object zone, Origin origin)
    {
        zones.Remove(zone);
        zones.Add(zone, origin);
    }

    internal Origin? ResolveZone(object zone) => zones.TryGetValue(zone, out var origin) ? origin : null;

    internal void CaptureBuff(object buff, Origin? incoming, CharacterMainControl? nativeActor, int nativeWeaponId, bool newlyCreated, bool grenadeOrigin = true)
    {
        if (!buffs.TryGetValue(buff, out var state))
        {
            state = new BuffOrigin(newlyCreated ? incoming : null);
            buffs.Add(buff, state);
        }
        else
        {
            state.Origin = Merge(state.Origin, incoming);
        }
        state.HadHazard |= grenadeOrigin && incoming != null;
        if (state.Origin is { } origin)
        {
            state.Origin = ReconcileNative(origin, nativeActor, nativeWeaponId);
        }
    }

    internal bool TryResolveBuff(object buff, CharacterMainControl? nativeActor, int nativeWeaponId, out Origin? origin)
    {
        origin = null;
        if (!buffs.TryGetValue(buff, out var state) || !state.HadHazard) return false;
        if (state.Origin is { } value) origin = ReconcileNative(value, nativeActor, nativeWeaponId);
        return true;
    }

    internal void Clear()
    {
        grenades = new();
        zones = new();
        buffs = new();
        generation = string.Empty;
        run = string.Empty;
    }

    private static Origin? ReconcileNative(Origin value, CharacterMainControl? actor, int weaponId)
    {
        if (actor != null && !ReferenceEquals(actor, value.Actor)) return null;
        return weaponId > 0 && value.ItemId != weaponId
            ? new Origin(value.Actor, 0, value.MapId, value.SegmentId, value.Equipment) : value;
    }

    private static Origin? Merge(Origin? left, Origin? right)
    {
        if (left == null || right == null || !ReferenceEquals(left.Actor, right.Actor)) return null;
        if (ReferenceEquals(left, right)) return left;
        return new Origin(left.Actor, left.ItemId == right.ItemId ? left.ItemId : 0,
            left.MapId == right.MapId ? left.MapId : MapIdentity.UnknownId,
            left.SegmentId == right.SegmentId ? left.SegmentId : string.Empty,
            SameEquipment(left, right) ? left.Equipment : new EquipmentEventAssociation());
    }

    private static bool SameEquipment(Origin left, Origin right) =>
        left.Equipment.LoadoutId == right.Equipment.LoadoutId
        && left.Equipment.SelectedWeaponId == right.Equipment.SelectedWeaponId
        && left.Equipment.SelectedWeaponSlotId == right.Equipment.SelectedWeaponSlotId
        && left.Equipment.TotemSetId == right.Equipment.TotemSetId;

    private sealed class BuffOrigin(Origin? origin)
    {
        internal Origin? Origin = origin;
        internal bool HadHazard;
    }

    internal sealed class Origin
    {
        internal Origin(CharacterMainControl actor, int itemId, string mapId, string segmentId, EquipmentEventAssociation equipment)
        {
            Actor = actor;
            ItemId = itemId;
            MapId = mapId;
            SegmentId = segmentId;
            Equipment = new EquipmentEventAssociation
            {
                LoadoutId = equipment.LoadoutId,
                SelectedWeaponId = equipment.SelectedWeaponId,
                SelectedWeaponSlotId = equipment.SelectedWeaponSlotId,
                TotemSetId = equipment.TotemSetId
            };
        }

        internal CharacterMainControl Actor { get; }
        internal int ItemId { get; }
        internal string MapId { get; }
        internal string SegmentId { get; }
        internal EquipmentEventAssociation Equipment { get; }
    }
}
