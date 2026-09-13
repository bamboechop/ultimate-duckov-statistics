using System.Collections;
using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

internal sealed class RunMetricScope(WeaponStatisticsAggregate weapon, CombatStatisticsAggregate combat, ItemStatisticsAggregate items, EquipmentStatisticsAggregate equipment)
{
    internal WeaponStatisticsAggregate Weapon { get; } = weapon;
    internal CombatStatisticsAggregate Combat { get; } = combat;
    internal ItemStatisticsAggregate Items { get; } = items;
    internal EquipmentStatisticsAggregate Equipment { get; } = equipment;
    internal IDictionary Dictionary(CheckpointEntryKind kind) => kind switch
    {
        CheckpointEntryKind.Weapons => Weapon.Weapons,
        CheckpointEntryKind.Ammunition => Weapon.AmmunitionTypes,
        CheckpointEntryKind.WeaponAmmunitionPairs => Weapon.WeaponAmmunitionPairs,
        CheckpointEntryKind.UncorrelatedWeapons => Weapon.UncorrelatedWeaponFiringActions,
        CheckpointEntryKind.UncorrelatedAmmunition => Weapon.UncorrelatedAmmunitionFiringActions,
        CheckpointEntryKind.CombatEnemies => Combat.Enemies,
        CheckpointEntryKind.CombatKillers => Combat.Killers,
        CheckpointEntryKind.CombatFamilies => Combat.Families,
        CheckpointEntryKind.CombatCauses => Combat.Causes,
        CheckpointEntryKind.CombatWeapons => Combat.Weapons,
        CheckpointEntryKind.CombatAmmunition => Combat.Ammunition,
        CheckpointEntryKind.CombatOwnership => Combat.Ownership,
        CheckpointEntryKind.Items => Items.Items,
        CheckpointEntryKind.EquipmentItems => Equipment.Items,
        CheckpointEntryKind.SelectedWeapons => Equipment.SelectedWeapons,
        CheckpointEntryKind.Loadouts => Equipment.Loadouts,
        CheckpointEntryKind.TotemSets => Equipment.TotemSets,
        CheckpointEntryKind.EquipmentCombat => Equipment.CombatAssociations,
        CheckpointEntryKind.TotemStates => Equipment.TotemStates,
        CheckpointEntryKind.Slots => Equipment.Slots,
        CheckpointEntryKind.SlottedWeapons => Equipment.SlottedWeapons,
        CheckpointEntryKind.CharacterSlotObserved => Equipment.CharacterSlotObservedDurations,
        CheckpointEntryKind.CharacterSlotStates => Equipment.CharacterSlotStates,
        CheckpointEntryKind.NestedSlotObserved => Equipment.NestedSlotObservedDurations,
        CheckpointEntryKind.NestedSlotStates => Equipment.NestedSlotStates,
        CheckpointEntryKind.LoadoutDefinitions => Equipment.Composition.Loadouts,
        CheckpointEntryKind.TotemSetDefinitions => Equipment.Composition.ActiveTotemSets,
        CheckpointEntryKind.TypedTotemStates => Equipment.Composition.TotemStates,
        CheckpointEntryKind.EmptyDirectSlots => Equipment.Composition.EmptyDirectSlots,
        _ => throw new InvalidDataException("Unsupported maintained run metric family.")
    };
}

internal static class RunMetricRecords
{
    internal static IEnumerable<CheckpointEntryKind> Kinds => Enumerable.Range(1, 29).Select(value => (CheckpointEntryKind)value);
    internal static string ScopeId(ProfileRecordKind kind, string key = "") => ((int)kind).ToString(CultureInfo.InvariantCulture) + ":" + key;
    internal static (ProfileRecordKind Kind, string Key) ParseScope(string scope)
    {
        var separator = scope.IndexOf(':');
        if (separator < 0 || !int.TryParse(scope.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || number != (int)ProfileRecordKind.RunTotals && number != (int)ProfileRecordKind.RunMap && number != (int)ProfileRecordKind.RouteMap)
            throw new InvalidDataException("Maintained run metric scope is invalid.");
        var key = scope.Substring(separator + 1);
        if (number == (int)ProfileRecordKind.RunTotals ? key.Length != 0 : string.IsNullOrWhiteSpace(key)) throw new InvalidDataException("Maintained run metric owner is invalid.");
        return ((ProfileRecordKind)number, key);
    }
    internal static RunMetricScope Scope(ProfileDocument profile, string scope)
    {
        var (kind, key) = ParseScope(scope); var totals = profile.Statistics.RunTotals;
        return kind switch
        {
            ProfileRecordKind.RunTotals => new(totals.WeaponStatistics, totals.CombatStatistics, totals.ItemStatistics, totals.EquipmentStatistics),
            ProfileRecordKind.RunMap when totals.Maps.TryGetValue(key, out var map) => new(map.WeaponStatistics, map.CombatStatistics, map.ItemStatistics, map.EquipmentStatistics),
            ProfileRecordKind.RouteMap when totals.RouteMaps.TryGetValue(key, out var map) => new(map.WeaponStatistics, map.CombatStatistics, map.ItemStatistics, map.EquipmentStatistics),
            _ => throw new InvalidDataException("Maintained run metric entries have no owner header.")
        };
    }
    internal static Type EntryType(CheckpointEntryKind kind) => kind switch
    {
        CheckpointEntryKind.Weapons => typeof(WeaponAggregate),
        CheckpointEntryKind.Ammunition => typeof(AmmunitionAggregate),
        CheckpointEntryKind.WeaponAmmunitionPairs => typeof(WeaponAmmunitionPairAggregate),
        CheckpointEntryKind.UncorrelatedWeapons or CheckpointEntryKind.UncorrelatedAmmunition => typeof(long),
        >= CheckpointEntryKind.CombatEnemies and <= CheckpointEntryKind.CombatOwnership => typeof(CombatBreakdownAggregate),
        CheckpointEntryKind.Items => typeof(ItemAggregate),
        CheckpointEntryKind.EquipmentCombat => typeof(EquipmentCombatAssociationAggregate),
        CheckpointEntryKind.CharacterSlotStates => typeof(CharacterSlotStateDurationAggregate),
        CheckpointEntryKind.NestedSlotStates => typeof(NestedSlotStateDurationAggregate),
        CheckpointEntryKind.LoadoutDefinitions => typeof(LoadoutDefinition),
        CheckpointEntryKind.TotemSetDefinitions => typeof(ActiveTotemSetDefinition),
        CheckpointEntryKind.TypedTotemStates => typeof(TotemStateDuration),
        >= CheckpointEntryKind.EquipmentItems and <= CheckpointEntryKind.EmptyDirectSlots => typeof(EquipmentDurationAggregate),
        _ => throw new InvalidDataException("Maintained run metric entry kind is invalid.")
    };
    internal static IEnumerable<string> AllScopes(ProfileDocument profile)
    {
        yield return ScopeId(ProfileRecordKind.RunTotals);
        foreach (var key in profile.Statistics.RunTotals.Maps.Keys) yield return ScopeId(ProfileRecordKind.RunMap, key);
        foreach (var key in profile.Statistics.RunTotals.RouteMaps.Keys) yield return ScopeId(ProfileRecordKind.RouteMap, key);
    }

    internal static RunAggregateTotals Header(RunAggregateTotals value) => new()
    {
        TotalRuns = value.TotalRuns,
        Outcomes = value.Outcomes,
        PhysicalDistance = value.PhysicalDistance,
        TeleportDistance = value.TeleportDistance,
        TransitionExcludedDistance = value.TransitionExcludedDistance,
        RouteAwareHistoryUnavailable = value.RouteAwareHistoryUnavailable,
        WeaponStatistics = CheckpointRecordHeaders.Weapon(value.WeaponStatistics),
        CombatStatistics = CheckpointRecordHeaders.Combat(value.CombatStatistics),
        ItemStatistics = CheckpointRecordHeaders.Items(value.ItemStatistics),
        EquipmentStatistics = EquipmentHeader(value.EquipmentStatistics),
        ContainerStatistics = value.ContainerStatistics,
        Economy = value.Economy
    };
    internal static MapRunAggregate Header(MapRunAggregate value) => new()
    {
        MapId = value.MapId,
        DisplayName = value.DisplayName,
        IsKnown = value.IsKnown,
        TotalRuns = value.TotalRuns,
        Outcomes = value.Outcomes,
        PhysicalDistance = value.PhysicalDistance,
        TeleportDistance = value.TeleportDistance,
        WeaponStatistics = CheckpointRecordHeaders.Weapon(value.WeaponStatistics),
        CombatStatistics = CheckpointRecordHeaders.Combat(value.CombatStatistics),
        ItemStatistics = CheckpointRecordHeaders.Items(value.ItemStatistics),
        EquipmentStatistics = EquipmentHeader(value.EquipmentStatistics),
        ContainerStatistics = value.ContainerStatistics,
        Economy = value.Economy
    };
    internal static RouteAwareMapAggregate Header(RouteAwareMapAggregate value) => new()
    {
        MapId = value.MapId,
        DisplayName = value.DisplayName,
        IsKnown = value.IsKnown,
        RunsVisited = value.RunsVisited,
        SegmentVisits = value.SegmentVisits,
        ActiveDurationSeconds = value.ActiveDurationSeconds,
        PhysicalDistance = value.PhysicalDistance,
        TeleportDistance = value.TeleportDistance,
        TransitionExcludedDistance = value.TransitionExcludedDistance,
        HistoricalUnavailable = value.HistoricalUnavailable,
        WasRepairedFromInvalidState = value.WasRepairedFromInvalidState,
        WeaponStatistics = CheckpointRecordHeaders.Weapon(value.WeaponStatistics),
        CombatStatistics = CheckpointRecordHeaders.Combat(value.CombatStatistics),
        ItemStatistics = CheckpointRecordHeaders.Items(value.ItemStatistics),
        EquipmentStatistics = EquipmentHeader(value.EquipmentStatistics),
        ContainerStatistics = value.ContainerStatistics,
        Economy = value.Economy
    };
    private static EquipmentStatisticsAggregate EquipmentHeader(EquipmentStatisticsAggregate value)
    { var header = CheckpointRecordHeaders.Equipment(value); header.Transitions = value.Transitions; return header; }
}
