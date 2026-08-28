using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class EquipmentDurationAggregate
{
    [DataMember(Order = 1)] public string Id { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public double ActiveDurationSeconds { get; set; }
    [DataMember(Order = 4)] public long RunOccurrences { get; set; }
}

[DataContract]
public sealed class CharacterSlotStateDurationAggregate
{
    [DataMember(Order = 1)] public string SlotId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string SlotDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 3)] public EquipmentSlotState State { get; set; }
    [DataMember(Order = 4)] public string ItemId { get; set; } = string.Empty;
    [DataMember(Order = 5)] public string ItemDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 6)] public EquipmentItemKind ItemKind { get; set; }
    [DataMember(Order = 7)] public double ActiveDurationSeconds { get; set; }
}

[DataContract]
public sealed class NestedSlotStateDurationAggregate
{
    [DataMember(Order = 1)] public string ParentSlotId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string ParentItemId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public string ParentItemDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 4)] public EquipmentItemKind ParentItemKind { get; set; }
    [DataMember(Order = 5)] public string Path { get; set; } = string.Empty;
    [DataMember(Order = 6)] public string SlotKey { get; set; } = string.Empty;
    [DataMember(Order = 7)] public string SlotDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 8)] public EquipmentSlotState State { get; set; }
    [DataMember(Order = 9)] public string ItemId { get; set; } = string.Empty;
    [DataMember(Order = 10)] public string ItemDisplayName { get; set; } = string.Empty;
    [DataMember(Order = 11)] public double ActiveDurationSeconds { get; set; }
}

[DataContract]
public sealed class EquipmentTransition
{
    [DataMember(Order = 1)] public double ActiveTimeSeconds { get; set; }
    [DataMember(Order = 2)] public string FromSnapshotId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public string ToSnapshotId { get; set; } = string.Empty;
    [DataMember(Order = 4)] public string FromLoadoutId { get; set; } = EquipmentEventAssociation.UnavailableId;
    [DataMember(Order = 5)] public string ToLoadoutId { get; set; } = EquipmentEventAssociation.UnavailableId;
    [DataMember(Order = 6)] public string SelectedWeaponSlotId { get; set; } = string.Empty;
    [DataMember(Order = 7)] public string SelectedWeaponId { get; set; } = string.Empty;
    [DataMember(Order = 8)] public string TotemSetId { get; set; } = EquipmentEventAssociation.UnavailableId;
}

[DataContract]
public sealed class EquipmentCombatAssociationAggregate
{
    [DataMember(Order = 1)] public string LoadoutId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string SelectedWeaponId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public string TotemSetId { get; set; } = string.Empty;
    [DataMember(Order = 4)] public long FiringActions { get; set; }
    [DataMember(Order = 5)] public long AmmunitionUnitsConsumed { get; set; }
    [DataMember(Order = 6)] public long Projectiles { get; set; }
    [DataMember(Order = 7)] public double DamageDealt { get; set; }
    [DataMember(Order = 8)] public double DamageReceived { get; set; }
    [DataMember(Order = 9)] public long RangedHits { get; set; }
    [DataMember(Order = 10)] public long MeleeHits { get; set; }
    [DataMember(Order = 11, EmitDefaultValue = false)] public long EnemiesKilled { get; set; }
    [DataMember(Order = 12)] public long PlayerDeaths { get; set; }
    [DataMember(Order = 13)] public string SelectedWeaponSlotId { get; set; } = string.Empty;
    [DataMember(Order = 14)] public long KillsByYou { get; set; }
    [DataMember(Order = 15)] public long LegacyUnclassifiedDeathCredit { get; set; }
}

[DataContract]
public sealed class EquipmentStatisticsAggregate
{
    public const int TransitionCapacity = 256;
    public const string ObservationUnavailableSnapshotId = "duckov:equipment-snapshot:unavailable";

    [DataMember(Order = 1)] public EquipmentMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 2)] public Dictionary<string, EquipmentDurationAggregate> Items { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 3)] public Dictionary<string, EquipmentDurationAggregate> SelectedWeapons { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 4)] public Dictionary<string, EquipmentDurationAggregate> Loadouts { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 5)] public Dictionary<string, EquipmentDurationAggregate> TotemSets { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 6)] public Dictionary<string, EquipmentCombatAssociationAggregate> CombatAssociations { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 7)] public List<EquipmentTransition> Transitions { get; set; } = new();
    [DataMember(Order = 8)] public long TransitionCount { get; set; }
    [DataMember(Order = 9)] public bool TransitionsTruncated { get; set; }
    [DataMember(Order = 10)] public double ObservedActiveDurationSeconds { get; set; }
    [DataMember(Order = 11, EmitDefaultValue = false)] public EquipmentSnapshot? CurrentSnapshot { get; set; }
    [DataMember(Order = 12)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 13)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 14)] public Dictionary<string, EquipmentDurationAggregate> TotemStates { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 15)] public Dictionary<string, EquipmentDurationAggregate> Slots { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 16)] public Dictionary<string, EquipmentDurationAggregate> SlottedWeapons { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 17)] public bool HistoricalCombatOwnershipUnavailable { get; set; }
    [DataMember(Order = 18)] public string HistoricalCombatOwnershipProvenance { get; set; } = string.Empty;
    [DataMember(Order = 19)] public Dictionary<string, EquipmentDurationAggregate> CharacterSlotObservedDurations { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 20)] public Dictionary<string, CharacterSlotStateDurationAggregate> CharacterSlotStates { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 21)] public Dictionary<string, EquipmentDurationAggregate> NestedSlotObservedDurations { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 22)] public Dictionary<string, NestedSlotStateDurationAggregate> NestedSlotStates { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 23)] public bool HistoricalCharacterSlotStateUnavailable { get; set; }
    [DataMember(Order = 24)] public string HistoricalCharacterSlotStateProvenance { get; set; } = string.Empty;
    [DataMember(Order = 25)] public bool HistoricalNestedSlotStateUnavailable { get; set; }
    [DataMember(Order = 26)] public string HistoricalNestedSlotStateProvenance { get; set; } = string.Empty;
}

public static class EquipmentStatisticsReducer
{
    public static bool Observe(EquipmentStatisticsAggregate target, EquipmentSnapshot snapshot, double activeSeconds)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        ValidateSnapshot(snapshot);
        Advance(target, activeSeconds);
        ApplySnapshotCompleteness(target, snapshot);
        if (string.Equals(target.CurrentSnapshot?.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal))
        {
            var enriched = EnrichDisplayMetadata(target, snapshot);
            target.CurrentSnapshot = Clone(snapshot);
            return enriched;
        }

        var from = target.CurrentSnapshot;
        target.CurrentSnapshot = Clone(snapshot);
        RecordTransition(target, activeSeconds, from, snapshot);

        return true;
    }

    public static bool Suspend(EquipmentStatisticsAggregate target, double activeSeconds)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        Advance(target, activeSeconds);
        var current = target.CurrentSnapshot;
        if (current == null) return false;
        target.CurrentSnapshot = null;
        RecordTransition(target, activeSeconds, current, null);
        return true;
    }

    public static void Advance(EquipmentStatisticsAggregate target, double activeSeconds)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (!IsFinite(activeSeconds) || activeSeconds < target.ObservedActiveDurationSeconds) return;
        var delta = activeSeconds - target.ObservedActiveDurationSeconds;
        var snapshot = target.CurrentSnapshot;
        if (delta <= 0 || snapshot == null)
        {
            target.ObservedActiveDurationSeconds = activeSeconds;
            return;
        }
        PreflightSlotStateAdvance(target, snapshot, delta);
        target.ObservedActiveDurationSeconds = activeSeconds;

        AddDuration(target.Loadouts, snapshot.LoadoutId, DescribeLoadout(snapshot), delta);
        if (!string.IsNullOrWhiteSpace(snapshot.SelectedWeaponId))
            AddDuration(target.SelectedWeapons, snapshot.SelectedWeaponSlotId + "|" + snapshot.SelectedWeaponId, snapshot.SelectedWeaponId, delta);
        if (snapshot.Totems.Any(value => value.ActivationState == TotemActivationState.ProvenActive))
            AddDuration(target.TotemSets, snapshot.TotemSetId, DescribeActiveTotemSet(snapshot), delta);
        foreach (var item in snapshot.Items)
        {
            AddDuration(target.Slots, item.SlotId, item.SlotDisplayName, delta);
            AddDuration(target.Items, item.SlotId + "|" + item.ItemId + "|" + item.AttachmentSignature, item.ItemDisplayName, delta);
            if (item.Kind == EquipmentItemKind.Weapon)
                AddDuration(target.SlottedWeapons, item.SlotId + "|" + item.ItemId, item.ItemDisplayName, delta);
        }
        // Completeness gates the family capability, not the truth of slots that
        // were individually retained. A damaged sibling must not erase a slot
        // whose key and current state were still proven by the native snapshot.
        foreach (var slot in snapshot.CharacterSlots)
        {
            AddDuration(
                target.CharacterSlotObservedDurations,
                CharacterSlotObservationKey(slot.SlotId),
                slot.SlotDisplayName,
                delta);
            AddCharacterSlotStateDuration(target.CharacterSlotStates, slot, delta);
        }
        foreach (var parent in snapshot.Items.Where(value => value.NestedSlotStateComplete))
        {
            foreach (var slot in parent.NestedSlots)
            {
                AddDuration(
                    target.NestedSlotObservedDurations,
                    NestedSlotObservationKey(parent.SlotId, parent.ItemId, slot.Path),
                    slot.SlotDisplayName,
                    delta);
                AddNestedSlotStateDuration(target.NestedSlotStates, parent, slot, delta);
            }
        }
        foreach (var group in snapshot.Totems
                     .GroupBy(TotemStateKey, StringComparer.Ordinal)
                     .OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var index = 0;
            foreach (var totem in group)
            {
                index++;
                AddDuration(
                    target.TotemStates,
                    group.Key + "|copy:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    DescribeTotem(totem),
                    delta);
            }
        }
    }

    public static void RecordShot(EquipmentStatisticsAggregate target, ShotRecorded shot)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (shot == null) throw new ArgumentNullException(nameof(shot));
        var row = Association(target, shot.EquipmentAssociation);
        row.FiringActions = SaturatingAdd(row.FiringActions, shot.FiringActionCount ?? 0);
        row.AmmunitionUnitsConsumed = SaturatingAdd(row.AmmunitionUnitsConsumed, shot.AmmunitionUnitsConsumed ?? 0);
        row.Projectiles = SaturatingAdd(row.Projectiles, shot.ProjectileCount ?? 0);
    }

    public static void RecordCombat(EquipmentStatisticsAggregate target, CombatRecorded value)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (value == null) throw new ArgumentNullException(nameof(value));
        var playerKills = value.Ownership == CombatOwnership.Player ? value.KillsByYou : 0;
        if (value.ActualDamageDealt <= 0 && value.ActualDamageReceived <= 0
            && value.RangedHits == 0 && value.MeleeHits == 0 && playerKills == 0
            && value.PlayerDeaths == 0) return;
        var row = Association(target, value.EquipmentAssociation);
        row.DamageDealt = SaturatingAdd(row.DamageDealt, value.ActualDamageDealt);
        row.DamageReceived = SaturatingAdd(row.DamageReceived, value.ActualDamageReceived);
        row.RangedHits = SaturatingAdd(row.RangedHits, value.RangedHits);
        row.MeleeHits = SaturatingAdd(row.MeleeHits, value.MeleeHits);
        row.KillsByYou = SaturatingAdd(row.KillsByYou, playerKills);
        row.PlayerDeaths = SaturatingAdd(row.PlayerDeaths, value.PlayerDeaths);
    }

    public static void Merge(
        EquipmentStatisticsAggregate target,
        EquipmentStatisticsAggregate source,
        bool countRunOccurrence = true)
    {
        var preserveUnavailable = target.HistoricalUnavailable || HasObservations(target);
        NormalizePersisted(target);
        NormalizePersisted(source);
        PreflightSlotStateMerge(target, source);
        target.Capabilities = preserveUnavailable
            ? RestrictCapabilities(target.Capabilities, source.Capabilities, preferSourceOnTie: !target.HistoricalUnavailable)
            : CloneCapabilities(source.Capabilities);
        MergeDurations(target.Items, source.Items);
        MergeDurations(target.SelectedWeapons, source.SelectedWeapons);
        MergeDurations(target.Loadouts, source.Loadouts, countRun: countRunOccurrence);
        MergeDurations(target.TotemSets, source.TotemSets, countRun: countRunOccurrence);
        MergeDurations(target.TotemStates, source.TotemStates);
        MergeDurations(target.Slots, source.Slots);
        MergeDurations(target.SlottedWeapons, source.SlottedWeapons);
        MergeDurations(target.CharacterSlotObservedDurations, source.CharacterSlotObservedDurations);
        MergeCharacterSlotStates(target.CharacterSlotStates, source.CharacterSlotStates);
        MergeDurations(target.NestedSlotObservedDurations, source.NestedSlotObservedDurations);
        MergeNestedSlotStates(target.NestedSlotStates, source.NestedSlotStates);
        foreach (var pair in source.CombatAssociations)
        {
            var row = Association(target, new EquipmentEventAssociation
            {
                LoadoutId = pair.Value.LoadoutId,
                SelectedWeaponId = pair.Value.SelectedWeaponId,
                TotemSetId = pair.Value.TotemSetId,
                SelectedWeaponSlotId = pair.Value.SelectedWeaponSlotId
            });
            var value = pair.Value;
            row.FiringActions = SaturatingAdd(row.FiringActions, value.FiringActions);
            row.AmmunitionUnitsConsumed = SaturatingAdd(row.AmmunitionUnitsConsumed, value.AmmunitionUnitsConsumed);
            row.Projectiles = SaturatingAdd(row.Projectiles, value.Projectiles);
            row.DamageDealt = SaturatingAdd(row.DamageDealt, value.DamageDealt);
            row.DamageReceived = SaturatingAdd(row.DamageReceived, value.DamageReceived);
            row.RangedHits = SaturatingAdd(row.RangedHits, value.RangedHits);
            row.MeleeHits = SaturatingAdd(row.MeleeHits, value.MeleeHits);
            row.EnemiesKilled = SaturatingAdd(row.EnemiesKilled, value.EnemiesKilled);
            row.KillsByYou = SaturatingAdd(row.KillsByYou, value.KillsByYou);
            row.LegacyUnclassifiedDeathCredit = SaturatingAdd(
                row.LegacyUnclassifiedDeathCredit,
                value.LegacyUnclassifiedDeathCredit);
            row.PlayerDeaths = SaturatingAdd(row.PlayerDeaths, value.PlayerDeaths);
        }
        target.HistoricalUnavailable |= source.HistoricalUnavailable;
        target.HistoricalCombatOwnershipUnavailable |= source.HistoricalCombatOwnershipUnavailable;
        target.HistoricalCombatOwnershipProvenance = MergeProvenance(
            target.HistoricalCombatOwnershipProvenance,
            source.HistoricalCombatOwnershipProvenance);
        target.HistoricalCharacterSlotStateUnavailable |= source.HistoricalCharacterSlotStateUnavailable;
        target.HistoricalCharacterSlotStateProvenance = MergeProvenance(
            target.HistoricalCharacterSlotStateProvenance,
            source.HistoricalCharacterSlotStateProvenance);
        target.HistoricalNestedSlotStateUnavailable |= source.HistoricalNestedSlotStateUnavailable;
        target.HistoricalNestedSlotStateProvenance = MergeProvenance(
            target.HistoricalNestedSlotStateProvenance,
            source.HistoricalNestedSlotStateProvenance);
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
    }

    public static EquipmentStatisticsAggregate Clone(EquipmentStatisticsAggregate source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        NormalizePersisted(source);
        var clone = new EquipmentStatisticsAggregate
        {
            Capabilities = CloneCapabilities(source.Capabilities),
            TransitionCount = source.TransitionCount,
            TransitionsTruncated = source.TransitionsTruncated,
            ObservedActiveDurationSeconds = source.ObservedActiveDurationSeconds,
            CurrentSnapshot = source.CurrentSnapshot == null ? null : Clone(source.CurrentSnapshot),
            HistoricalUnavailable = source.HistoricalUnavailable,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
            HistoricalCombatOwnershipUnavailable = source.HistoricalCombatOwnershipUnavailable,
            HistoricalCombatOwnershipProvenance = source.HistoricalCombatOwnershipProvenance,
            HistoricalCharacterSlotStateUnavailable = source.HistoricalCharacterSlotStateUnavailable,
            HistoricalCharacterSlotStateProvenance = source.HistoricalCharacterSlotStateProvenance,
            HistoricalNestedSlotStateUnavailable = source.HistoricalNestedSlotStateUnavailable,
            HistoricalNestedSlotStateProvenance = source.HistoricalNestedSlotStateProvenance
        };
        MergeDurations(clone.Items, source.Items);
        MergeDurations(clone.SelectedWeapons, source.SelectedWeapons);
        MergeDurations(clone.Loadouts, source.Loadouts);
        MergeDurations(clone.TotemSets, source.TotemSets);
        MergeDurations(clone.TotemStates, source.TotemStates);
        MergeDurations(clone.Slots, source.Slots);
        MergeDurations(clone.SlottedWeapons, source.SlottedWeapons);
        MergeDurations(clone.CharacterSlotObservedDurations, source.CharacterSlotObservedDurations);
        MergeCharacterSlotStates(clone.CharacterSlotStates, source.CharacterSlotStates);
        MergeDurations(clone.NestedSlotObservedDurations, source.NestedSlotObservedDurations);
        MergeNestedSlotStates(clone.NestedSlotStates, source.NestedSlotStates);
        clone.Transitions = source.Transitions.Select(x => new EquipmentTransition
        {
            ActiveTimeSeconds = x.ActiveTimeSeconds,
            FromSnapshotId = x.FromSnapshotId,
            ToSnapshotId = x.ToSnapshotId,
            FromLoadoutId = x.FromLoadoutId,
            ToLoadoutId = x.ToLoadoutId,
            SelectedWeaponSlotId = x.SelectedWeaponSlotId,
            SelectedWeaponId = x.SelectedWeaponId,
            TotemSetId = x.TotemSetId
        }).ToList();
        foreach (var pair in source.CombatAssociations)
        {
            var value = pair.Value;
            clone.CombatAssociations[pair.Key] = new EquipmentCombatAssociationAggregate
            {
                LoadoutId = value.LoadoutId,
                SelectedWeaponId = value.SelectedWeaponId,
                TotemSetId = value.TotemSetId,
                SelectedWeaponSlotId = value.SelectedWeaponSlotId,
                FiringActions = value.FiringActions,
                AmmunitionUnitsConsumed = value.AmmunitionUnitsConsumed,
                Projectiles = value.Projectiles,
                DamageDealt = value.DamageDealt,
                DamageReceived = value.DamageReceived,
                RangedHits = value.RangedHits,
                MeleeHits = value.MeleeHits,
                EnemiesKilled = value.EnemiesKilled,
                KillsByYou = value.KillsByYou,
                LegacyUnclassifiedDeathCredit = value.LegacyUnclassifiedDeathCredit,
                PlayerDeaths = value.PlayerDeaths
            };
        }
        return clone;
    }

    public static bool MigrateLegacyCombatOwnership(
        EquipmentStatisticsAggregate target,
        string provenance)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        NormalizePersisted(target);
        var changed = false;
        foreach (var row in target.CombatAssociations.Values)
        {
            if (row.EnemiesKilled <= 0) continue;
            row.LegacyUnclassifiedDeathCredit = SaturatingAdd(
                row.LegacyUnclassifiedDeathCredit,
                row.EnemiesKilled);
            row.EnemiesKilled = 0;
            changed = true;
        }
        if (changed)
        {
            target.HistoricalCombatOwnershipUnavailable = true;
            target.HistoricalCombatOwnershipProvenance = MergeProvenance(
                target.HistoricalCombatOwnershipProvenance,
                provenance);
        }
        return changed;
    }

    public static EquipmentMetricCapabilities CloneCapabilities(EquipmentMetricCapabilities value) => new()
    {
        EquipmentSlots = Clone(value?.EquipmentSlots),
        SelectedWeapon = Clone(value?.SelectedWeapon),
        AttachmentMetadata = Clone(value?.AttachmentMetadata),
        DirectTotems = Clone(value?.DirectTotems),
        ToteContents = Clone(value?.ToteContents),
        ToteActivation = Clone(value?.ToteActivation),
        CharacterSlotState = Clone(value?.CharacterSlotState),
        NestedSlotState = Clone(value?.NestedSlotState)
    };

    public static bool NormalizePersisted(EquipmentStatisticsAggregate target)
    {
        if (target == null) return false;
        var changed = false;
        var repaired = false;
        if (target.HistoricalCombatOwnershipProvenance == null)
        {
            target.HistoricalCombatOwnershipProvenance = string.Empty;
            changed = true;
        }
        if (target.HistoricalCharacterSlotStateProvenance == null)
        {
            target.HistoricalCharacterSlotStateProvenance = string.Empty;
            changed = true;
        }
        if (target.HistoricalNestedSlotStateProvenance == null)
        {
            target.HistoricalNestedSlotStateProvenance = string.Empty;
            changed = true;
        }
        if (target.Capabilities == null)
        {
            target.Capabilities = new EquipmentMetricCapabilities();
            repaired = true;
        }
        NormalizeCapabilities(target.Capabilities, ref repaired);
        target.Items ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.SelectedWeapons ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.Loadouts ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.TotemSets ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.TotemStates ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.Slots ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.SlottedWeapons ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.CharacterSlotObservedDurations ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.CharacterSlotStates ??= Repair(new Dictionary<string, CharacterSlotStateDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.NestedSlotObservedDurations ??= Repair(new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.NestedSlotStates ??= Repair(new Dictionary<string, NestedSlotStateDurationAggregate>(StringComparer.Ordinal), ref repaired);
        target.CombatAssociations ??= Repair(new Dictionary<string, EquipmentCombatAssociationAggregate>(StringComparer.Ordinal), ref repaired);
        target.Transitions ??= Repair(new List<EquipmentTransition>(), ref repaired);
        if (!IsFinite(target.ObservedActiveDurationSeconds) || target.ObservedActiveDurationSeconds < 0)
        { target.ObservedActiveDurationSeconds = 0; repaired = true; }
        if (target.TransitionCount < 0) { target.TransitionCount = 0; repaired = true; }
        target.Items = NormalizeDurations(target.Items, ref repaired);
        target.SelectedWeapons = NormalizeDurations(target.SelectedWeapons, ref repaired);
        target.Loadouts = NormalizeDurations(target.Loadouts, ref repaired);
        target.TotemSets = NormalizeDurations(target.TotemSets, ref repaired);
        target.TotemStates = NormalizeDurations(target.TotemStates, ref repaired);
        target.Slots = NormalizeDurations(target.Slots, ref repaired);
        target.SlottedWeapons = NormalizeDurations(target.SlottedWeapons, ref repaired);
        target.CharacterSlotObservedDurations = NormalizeDurations(target.CharacterSlotObservedDurations, ref repaired);
        target.CharacterSlotStates = NormalizeCharacterSlotStates(target.CharacterSlotStates, ref repaired);
        target.NestedSlotObservedDurations = NormalizeDurations(target.NestedSlotObservedDurations, ref repaired);
        target.NestedSlotStates = NormalizeNestedSlotStates(target.NestedSlotStates, ref repaired);
        target.CombatAssociations = NormalizeCombatAssociations(target.CombatAssociations, ref repaired);
        var previousTransitionTime = 0d;
        var validTransitions = new List<EquipmentTransition>();
        foreach (var row in target.Transitions)
        {
            if (row == null || !IsFinite(row.ActiveTimeSeconds)
                || row.ActiveTimeSeconds < previousTransitionTime
                || row.ActiveTimeSeconds > target.ObservedActiveDurationSeconds
                || string.IsNullOrWhiteSpace(row.ToSnapshotId))
            {
                repaired = true;
                continue;
            }
            var from = row.FromSnapshotId?.Trim() ?? string.Empty;
            var to = row.ToSnapshotId.Trim();
            var fromLoadout = EmptyToUnavailable(row.FromLoadoutId?.Trim());
            var toLoadout = EmptyToUnavailable(row.ToLoadoutId?.Trim());
            var selectedSlot = row.SelectedWeaponSlotId?.Trim() ?? string.Empty;
            var selected = row.SelectedWeaponId?.Trim() ?? string.Empty;
            var totems = EmptyToUnavailable(row.TotemSetId?.Trim());
            if (string.IsNullOrWhiteSpace(selected) != string.IsNullOrWhiteSpace(selectedSlot))
            {
                selected = string.Empty;
                selectedSlot = string.Empty;
                repaired = true;
            }
            if (!string.Equals(from, row.FromSnapshotId, StringComparison.Ordinal)
                || !string.Equals(to, row.ToSnapshotId, StringComparison.Ordinal)
                || !string.Equals(fromLoadout, row.FromLoadoutId, StringComparison.Ordinal)
                || !string.Equals(toLoadout, row.ToLoadoutId, StringComparison.Ordinal)
                || !string.Equals(selectedSlot, row.SelectedWeaponSlotId, StringComparison.Ordinal)
                || !string.Equals(selected, row.SelectedWeaponId, StringComparison.Ordinal)
                || !string.Equals(totems, row.TotemSetId, StringComparison.Ordinal)) repaired = true;
            row.FromSnapshotId = from;
            row.ToSnapshotId = to;
            row.FromLoadoutId = fromLoadout;
            row.ToLoadoutId = toLoadout;
            row.SelectedWeaponSlotId = selectedSlot;
            row.SelectedWeaponId = selected;
            row.TotemSetId = totems;
            previousTransitionTime = row.ActiveTimeSeconds;
            validTransitions.Add(row);
        }
        if (validTransitions.Count != target.Transitions.Count)
        {
            target.Transitions = validTransitions;
            target.TransitionsTruncated = true;
            repaired = true;
        }
        if (target.CurrentSnapshot != null)
        {
            try
            {
                ValidateSnapshot(target.CurrentSnapshot);
            }
            catch (ArgumentException)
            {
                target.CurrentSnapshot = null;
                repaired = true;
            }
        }
        if (target.Transitions.Count > EquipmentStatisticsAggregate.TransitionCapacity)
        {
            target.Transitions = target.Transitions.TakeLast(EquipmentStatisticsAggregate.TransitionCapacity).ToList();
            target.TransitionsTruncated = true;
            repaired = true;
        }
        if (target.TransitionCount < target.Transitions.Count)
        {
            target.TransitionCount = target.Transitions.Count;
            repaired = true;
        }
        target.WasRepairedFromInvalidState |= repaired;
        return changed || repaired;
    }

    public static void ValidateAggregate(EquipmentStatisticsAggregate target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (target.Capabilities == null || target.Items == null || target.SelectedWeapons == null
            || target.Loadouts == null || target.TotemSets == null || target.CombatAssociations == null
            || target.TotemStates == null || target.Slots == null || target.SlottedWeapons == null
            || target.CharacterSlotObservedDurations == null || target.CharacterSlotStates == null
            || target.NestedSlotObservedDurations == null || target.NestedSlotStates == null
            || target.Transitions == null || !IsFinite(target.ObservedActiveDurationSeconds)
            || target.ObservedActiveDurationSeconds < 0 || target.Transitions.Count > EquipmentStatisticsAggregate.TransitionCapacity
            || target.Items.Values.Any(row => row == null) || target.SelectedWeapons.Values.Any(row => row == null)
            || target.Loadouts.Values.Any(row => row == null) || target.TotemSets.Values.Any(row => row == null)
            || target.TotemStates.Values.Any(row => row == null)
            || target.Slots.Values.Any(row => row == null) || target.SlottedWeapons.Values.Any(row => row == null)
            || target.CharacterSlotObservedDurations.Values.Any(row => row == null)
            || target.CharacterSlotStates.Values.Any(row => row == null)
            || target.NestedSlotObservedDurations.Values.Any(row => row == null)
            || target.NestedSlotStates.Values.Any(row => row == null)
            || target.CombatAssociations.Values.Any(row => row == null))
            throw new ArgumentException("Equipment statistics are invalid.", nameof(target));
        ValidateSlotStateReconciliation(target);
    }

    public static void ValidateRecoveryCandidate(EquipmentStatisticsAggregate? target, int schemaVersion)
    {
        if (schemaVersion >= 6 && (target == null || target.Capabilities == null
            || target.Capabilities.EquipmentSlots == null || target.Capabilities.SelectedWeapon == null
            || target.Capabilities.AttachmentMetadata == null || target.Capabilities.DirectTotems == null
            || target.Capabilities.ToteContents == null || target.Capabilities.ToteActivation == null
            || target.Items == null || target.SelectedWeapons == null || target.Loadouts == null
            || target.TotemSets == null || target.TotemStates == null || target.Slots == null
            || target.SlottedWeapons == null || target.CombatAssociations == null || target.Transitions == null))
            throw new ArgumentException("Current-schema equipment checkpoint is incomplete.", nameof(target));
        if (schemaVersion >= 14 && (target == null || target.Capabilities.CharacterSlotState == null
            || target.Capabilities.NestedSlotState == null
            || target.CharacterSlotObservedDurations == null || target.CharacterSlotStates == null
            || target.NestedSlotObservedDurations == null || target.NestedSlotStates == null
            || target.HistoricalCharacterSlotStateProvenance == null
            || target.HistoricalNestedSlotStateProvenance == null))
            throw new ArgumentException("Schema-14 equipment-slot checkpoint is incomplete.", nameof(target));
        if (target == null) return;
        if (!IsFinite(target.ObservedActiveDurationSeconds) || target.ObservedActiveDurationSeconds < 0
            || target.TransitionCount < 0 || target.Transitions?.Count > EquipmentStatisticsAggregate.TransitionCapacity
            || target.Transitions != null && target.TransitionCount < target.Transitions.Count)
            throw new ArgumentException("Equipment checkpoint contains invalid duration or transition counters.", nameof(target));
        if (target.CurrentSnapshot != null) ValidateSnapshot(target.CurrentSnapshot);
        var previousTransitionTime = 0d;
        foreach (var row in target.Transitions ?? Enumerable.Empty<EquipmentTransition>())
        {
            if (row == null || !IsFinite(row.ActiveTimeSeconds)
                || row.ActiveTimeSeconds < previousTransitionTime
                || row.ActiveTimeSeconds > target.ObservedActiveDurationSeconds
                || string.IsNullOrWhiteSpace(row.ToSnapshotId)
                || schemaVersion >= 6 && (row.FromSnapshotId == null
                    || string.IsNullOrWhiteSpace(row.FromLoadoutId)
                    || string.IsNullOrWhiteSpace(row.ToLoadoutId)
                    || string.IsNullOrWhiteSpace(row.TotemSetId)
                    || string.IsNullOrWhiteSpace(row.SelectedWeaponId)
                       != string.IsNullOrWhiteSpace(row.SelectedWeaponSlotId)))
                throw new ArgumentException("Equipment checkpoint contains invalid transitions.", nameof(target));
            previousTransitionTime = row.ActiveTimeSeconds;
        }
        foreach (var values in new[] { target.Items, target.SelectedWeapons, target.Loadouts, target.TotemSets, target.TotemStates, target.Slots, target.SlottedWeapons })
        {
            if (values == null) continue;
            if (values.Values.Any(row => row == null || !IsFinite(row.ActiveDurationSeconds)
                                         || row.ActiveDurationSeconds < 0 || row.RunOccurrences < 0))
                throw new ArgumentException("Equipment checkpoint contains invalid aggregate durations.", nameof(target));
        }
        foreach (var values in new[] { target.CharacterSlotObservedDurations, target.NestedSlotObservedDurations })
        {
            if (values == null) continue;
            if (values.Values.Any(row => row == null || !IsFinite(row.ActiveDurationSeconds)
                                         || row.ActiveDurationSeconds < 0 || row.RunOccurrences < 0))
                throw new ArgumentException("Equipment checkpoint contains invalid slot observation durations.", nameof(target));
        }
        if (target.CharacterSlotStates?.Values.Any(row => !ValidCharacterSlotState(row)) == true
            || target.NestedSlotStates?.Values.Any(row => !ValidNestedSlotState(row)) == true)
            throw new ArgumentException("Equipment checkpoint contains invalid slot-state durations.", nameof(target));
        if (target.CombatAssociations?.Values.Any(row => row == null || row.FiringActions < 0
                || row.AmmunitionUnitsConsumed < 0 || row.Projectiles < 0 || row.RangedHits < 0
                || row.MeleeHits < 0 || row.EnemiesKilled < 0 || row.KillsByYou < 0
                || row.LegacyUnclassifiedDeathCredit < 0 || row.PlayerDeaths < 0
                || !IsFinite(row.DamageDealt) || row.DamageDealt < 0
                || !IsFinite(row.DamageReceived) || row.DamageReceived < 0) == true)
            throw new ArgumentException("Equipment checkpoint contains invalid combat-association counters.", nameof(target));
        if (schemaVersion >= 14) ValidateSlotStateReconciliation(target);
    }

    public static bool IsEmpty(EquipmentStatisticsAggregate value) => value != null
        && !value.WasRepairedFromInvalidState
        && value.Items.Count == 0 && value.SelectedWeapons.Count == 0 && value.Loadouts.Count == 0
        && value.TotemSets.Count == 0 && value.TotemStates.Count == 0
        && value.Slots.Count == 0 && value.SlottedWeapons.Count == 0
        && value.CharacterSlotObservedDurations.Count == 0 && value.CharacterSlotStates.Count == 0
        && value.NestedSlotObservedDurations.Count == 0 && value.NestedSlotStates.Count == 0
        && value.CombatAssociations.Count == 0 && value.TransitionCount == 0
        && value.CurrentSnapshot == null;

    public static AdapterCapabilityState ResolveCurrentAvailability(
        EquipmentStatisticsAggregate aggregate,
        MetricAvailability recorded,
        AdapterCapabilityState current,
        bool allowUninitializedFallback)
    {
        if (allowUninitializedFallback && !aggregate.HistoricalUnavailable && IsEmpty(aggregate))
            return current;
        return (int)recorded.State >= (int)current ? recorded.State : current;
    }

    public static void ApplyCurrentAvailability(
        EquipmentStatisticsAggregate aggregate,
        MetricAvailability recorded,
        AdapterCapabilityState current,
        string? currentProvenance,
        bool allowUninitializedFallback)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (recorded == null) throw new ArgumentNullException(nameof(recorded));
        var resolved = ResolveCurrentAvailability(aggregate, recorded, current, allowUninitializedFallback);
        if (aggregate.WasRepairedFromInvalidState
            && resolved == AdapterCapabilityState.DisabledIncompatible
            && string.IsNullOrWhiteSpace(recorded.Provenance))
            recorded.Provenance = "Persisted equipment data was repaired; capability remains unavailable.";
        if (!aggregate.HistoricalUnavailable && resolved == current && !string.IsNullOrWhiteSpace(currentProvenance))
            recorded.Provenance = currentProvenance;
        recorded.State = resolved;
    }

    private static void ValidateSnapshot(EquipmentSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotId) || string.IsNullOrWhiteSpace(snapshot.LoadoutId)
            || string.IsNullOrWhiteSpace(snapshot.TotemSetId) || snapshot.Items == null || snapshot.Totems == null
            || snapshot.CharacterSlots == null)
            throw new ArgumentException("Equipment snapshot is incomplete.", nameof(snapshot));
        if (snapshot.Items.Any(value => value == null || string.IsNullOrWhiteSpace(value.SlotId)
                || string.IsNullOrWhiteSpace(value.ItemId))
            || snapshot.Totems.Any(value => value == null || string.IsNullOrWhiteSpace(value.ItemId)
                || string.IsNullOrWhiteSpace(value.ContainerId)))
            throw new ArgumentException("Equipment snapshot contains an invalid item.", nameof(snapshot));
        if (snapshot.Items.Select(value => value.SlotId).Distinct(StringComparer.Ordinal).Count() != snapshot.Items.Count
            || snapshot.Items.Any(value => !Enum.IsDefined(typeof(EquipmentItemKind), value.Kind))
            || snapshot.Totems.Any(value => !Enum.IsDefined(typeof(TotemCarryKind), value.CarryKind)
                || !Enum.IsDefined(typeof(TotemActivationState), value.ActivationState)))
            throw new ArgumentException("Equipment snapshot contains duplicate slots or invalid states.", nameof(snapshot));
        if (snapshot.CharacterSlots.Any(value => value == null || string.IsNullOrWhiteSpace(value.SlotId)
                || !Enum.IsDefined(typeof(EquipmentSlotState), value.State)
                || !Enum.IsDefined(typeof(EquipmentItemKind), value.ItemKind)
                || value.State == EquipmentSlotState.Occupied && string.IsNullOrWhiteSpace(value.ItemId)
                || value.State == EquipmentSlotState.Empty && (!string.IsNullOrEmpty(value.ItemId)
                    || !string.IsNullOrEmpty(value.ItemDisplayName)))
            || snapshot.CharacterSlots.Select(value => value.SlotId).Distinct(StringComparer.Ordinal).Count()
               != snapshot.CharacterSlots.Count)
            throw new ArgumentException("Equipment snapshot contains invalid character-slot states.", nameof(snapshot));
        if (snapshot.CharacterSlotStateComplete
            && (snapshot.CharacterSlots.Count == 0 && snapshot.Items.Count > 0
                || snapshot.Items.Any(item => !snapshot.CharacterSlots.Any(slot =>
                    slot.State == EquipmentSlotState.Occupied
                    && string.Equals(slot.SlotId, item.SlotId, StringComparison.Ordinal)
                    && string.Equals(slot.ItemId, item.ItemId, StringComparison.Ordinal)))))
            throw new ArgumentException("Equipment snapshot character-slot state is incomplete.", nameof(snapshot));
        foreach (var parent in snapshot.Items)
        {
            if (parent.NestedSlots == null
                || parent.NestedSlots.Any(value => value == null || string.IsNullOrWhiteSpace(value.Path)
                    || string.IsNullOrWhiteSpace(value.SlotKey)
                    || !Enum.IsDefined(typeof(EquipmentSlotState), value.State)
                    || value.State == EquipmentSlotState.Occupied && string.IsNullOrWhiteSpace(value.ItemId)
                    || value.State == EquipmentSlotState.Empty && (!string.IsNullOrEmpty(value.ItemId)
                        || !string.IsNullOrEmpty(value.ItemDisplayName)))
                || parent.NestedSlots.Select(value => value.Path).Distinct(StringComparer.Ordinal).Count()
                   != parent.NestedSlots.Count)
                throw new ArgumentException("Equipment snapshot contains invalid nested-slot states.", nameof(snapshot));
        }
        var hasSelectedId = !string.IsNullOrWhiteSpace(snapshot.SelectedWeaponId);
        var hasSelectedSlot = !string.IsNullOrWhiteSpace(snapshot.SelectedWeaponSlotId);
        if (hasSelectedId != hasSelectedSlot
            || (hasSelectedId && !snapshot.Items.Any(value =>
                string.Equals(value.ItemId, snapshot.SelectedWeaponId, StringComparison.Ordinal)
                && string.Equals(value.SlotId, snapshot.SelectedWeaponSlotId, StringComparison.Ordinal)
                && value.Kind == EquipmentItemKind.Weapon)))
            throw new ArgumentException("Selected weapon is not a current slotted item.", nameof(snapshot));
    }

    private static EquipmentSnapshot Clone(EquipmentSnapshot source) => new()
    {
        SnapshotId = source.SnapshotId,
        LoadoutId = source.LoadoutId,
        SelectedWeaponId = source.SelectedWeaponId,
        SelectedWeaponSlotId = source.SelectedWeaponSlotId,
        TotemSetId = source.TotemSetId,
        Items = source.Items.Select(x => new EquippedItemSnapshot
        {
            SlotId = x.SlotId,
            SlotDisplayName = x.SlotDisplayName,
            ItemId = x.ItemId,
            ItemDisplayName = x.ItemDisplayName,
            Kind = x.Kind,
            AttachmentSignature = x.AttachmentSignature,
            NestedSlotStateComplete = x.NestedSlotStateComplete,
            NestedSlots = x.NestedSlots.Select(slot => new NestedEquipmentSlotSnapshot
            {
                Path = slot.Path,
                SlotKey = slot.SlotKey,
                SlotDisplayName = slot.SlotDisplayName,
                State = slot.State,
                ItemId = slot.ItemId,
                ItemDisplayName = slot.ItemDisplayName
            }).ToList()
        }).ToList(),
        Totems = source.Totems.Select(x => new TotemSnapshot
        {
            ItemId = x.ItemId,
            DisplayName = x.DisplayName,
            CarryKind = x.CarryKind,
            ContainerId = x.ContainerId,
            ActivationState = x.ActivationState
        }).ToList(),
        CharacterSlotStateComplete = source.CharacterSlotStateComplete,
        NestedSlotStateComplete = source.NestedSlotStateComplete,
        CharacterSlots = source.CharacterSlots.Select(slot => new CharacterEquipmentSlotSnapshot
        {
            SlotId = slot.SlotId,
            SlotDisplayName = slot.SlotDisplayName,
            State = slot.State,
            ItemId = slot.ItemId,
            ItemDisplayName = slot.ItemDisplayName,
            ItemKind = slot.ItemKind
        }).ToList()
    };

    private static void ApplySnapshotCompleteness(EquipmentStatisticsAggregate target, EquipmentSnapshot snapshot)
    {
        if (!snapshot.CharacterSlotStateComplete)
        {
            target.Capabilities.CharacterSlotState = new MetricAvailability
            {
                State = AdapterCapabilityState.DisabledIncompatible,
                Provenance = "The current character-slot collection could not be enumerated completely; missing evidence is unavailable, not empty."
            };
        }
        if (!snapshot.NestedSlotStateComplete || snapshot.Items.Any(value => !value.NestedSlotStateComplete))
        {
            target.Capabilities.NestedSlotState = new MetricAvailability
            {
                State = AdapterCapabilityState.DisabledIncompatible,
                Provenance = "At least one equipped-item nested-slot tree could not be enumerated completely; missing evidence is unavailable, not empty."
            };
        }
    }

    private static void PreflightSlotStateAdvance(
        EquipmentStatisticsAggregate target,
        EquipmentSnapshot snapshot,
        double delta)
    {
        foreach (var slot in snapshot.CharacterSlots)
        {
            target.CharacterSlotStates.TryGetValue(CharacterSlotStateKey(slot), out var row);
            _ = CheckedDurationAdd(row?.ActiveDurationSeconds ?? 0, delta);
        }
        foreach (var parent in snapshot.Items.Where(value => value.NestedSlotStateComplete))
        {
            foreach (var slot in parent.NestedSlots)
            {
                target.NestedSlotStates.TryGetValue(NestedSlotStateKey(parent.SlotId, parent.ItemId, slot), out var row);
                _ = CheckedDurationAdd(row?.ActiveDurationSeconds ?? 0, delta);
            }
        }
    }

    private static void PreflightSlotStateMerge(
        EquipmentStatisticsAggregate target,
        EquipmentStatisticsAggregate source)
    {
        foreach (var row in source.CharacterSlotStates.Values)
        {
            var key = CharacterSlotStateKey(new CharacterEquipmentSlotSnapshot
            {
                SlotId = row.SlotId,
                State = row.State,
                ItemId = row.ItemId
            });
            target.CharacterSlotStates.TryGetValue(key, out var existing);
            _ = CheckedDurationAdd(existing?.ActiveDurationSeconds ?? 0, row.ActiveDurationSeconds);
        }
        foreach (var row in source.NestedSlotStates.Values)
        {
            var key = NestedSlotStateKey(row.ParentSlotId, row.ParentItemId, new NestedEquipmentSlotSnapshot
            {
                Path = row.Path,
                State = row.State,
                ItemId = row.ItemId
            });
            target.NestedSlotStates.TryGetValue(key, out var existing);
            _ = CheckedDurationAdd(existing?.ActiveDurationSeconds ?? 0, row.ActiveDurationSeconds);
        }
    }

    private static void AddCharacterSlotStateDuration(
        Dictionary<string, CharacterSlotStateDurationAggregate> target,
        CharacterEquipmentSlotSnapshot slot,
        double delta)
    {
        var key = CharacterSlotStateKey(slot);
        if (!target.TryGetValue(key, out var row))
        {
            row = new CharacterSlotStateDurationAggregate
            {
                SlotId = slot.SlotId,
                SlotDisplayName = slot.SlotDisplayName,
                State = slot.State,
                ItemId = slot.ItemId,
                ItemDisplayName = slot.ItemDisplayName,
                ItemKind = slot.ItemKind
            };
            target[key] = row;
        }
        if (!string.IsNullOrWhiteSpace(slot.SlotDisplayName)) row.SlotDisplayName = slot.SlotDisplayName;
        if (!string.IsNullOrWhiteSpace(slot.ItemDisplayName)) row.ItemDisplayName = slot.ItemDisplayName;
        row.ActiveDurationSeconds = CheckedDurationAdd(row.ActiveDurationSeconds, delta);
    }

    private static void AddNestedSlotStateDuration(
        Dictionary<string, NestedSlotStateDurationAggregate> target,
        EquippedItemSnapshot parent,
        NestedEquipmentSlotSnapshot slot,
        double delta)
    {
        var key = NestedSlotStateKey(parent.SlotId, parent.ItemId, slot);
        if (!target.TryGetValue(key, out var row))
        {
            row = new NestedSlotStateDurationAggregate
            {
                ParentSlotId = parent.SlotId,
                ParentItemId = parent.ItemId,
                ParentItemDisplayName = parent.ItemDisplayName,
                ParentItemKind = parent.Kind,
                Path = slot.Path,
                SlotKey = slot.SlotKey,
                SlotDisplayName = slot.SlotDisplayName,
                State = slot.State,
                ItemId = slot.ItemId,
                ItemDisplayName = slot.ItemDisplayName
            };
            target[key] = row;
        }
        if (!string.IsNullOrWhiteSpace(parent.ItemDisplayName)) row.ParentItemDisplayName = parent.ItemDisplayName;
        if (!string.IsNullOrWhiteSpace(slot.SlotDisplayName)) row.SlotDisplayName = slot.SlotDisplayName;
        if (!string.IsNullOrWhiteSpace(slot.ItemDisplayName)) row.ItemDisplayName = slot.ItemDisplayName;
        row.ActiveDurationSeconds = CheckedDurationAdd(row.ActiveDurationSeconds, delta);
    }

    private static bool EnrichDisplayMetadata(EquipmentStatisticsAggregate target, EquipmentSnapshot snapshot)
    {
        var changed = EnrichDurationDisplayName(target.Loadouts, snapshot.LoadoutId, DescribeLoadout(snapshot));
        if (snapshot.Totems.Any(value => value.ActivationState == TotemActivationState.ProvenActive))
            changed |= EnrichDurationDisplayName(target.TotemSets, snapshot.TotemSetId, DescribeActiveTotemSet(snapshot));
        foreach (var item in snapshot.Items)
        {
            changed |= EnrichDurationDisplayName(target.Slots, item.SlotId, item.SlotDisplayName);
            changed |= EnrichDurationDisplayName(
                target.Items,
                item.SlotId + "|" + item.ItemId + "|" + item.AttachmentSignature,
                item.ItemDisplayName);
            if (item.Kind == EquipmentItemKind.Weapon)
            {
                changed |= EnrichDurationDisplayName(
                    target.SlottedWeapons,
                    item.SlotId + "|" + item.ItemId,
                    item.ItemDisplayName);
            }
        }
        foreach (var slot in snapshot.CharacterSlots)
        {
            changed |= EnrichDurationDisplayName(
                target.CharacterSlotObservedDurations,
                CharacterSlotObservationKey(slot.SlotId),
                slot.SlotDisplayName);
            if (!target.CharacterSlotStates.TryGetValue(CharacterSlotStateKey(slot), out var row)) continue;
            row.SlotDisplayName = EnrichedDisplayName(
                row.SlotDisplayName,
                slot.SlotDisplayName,
                out var slotChanged);
            row.ItemDisplayName = EnrichedDisplayName(
                row.ItemDisplayName,
                slot.ItemDisplayName,
                out var itemChanged);
            changed |= slotChanged || itemChanged;
        }
        foreach (var parent in snapshot.Items.Where(value => value.NestedSlotStateComplete))
        {
            foreach (var slot in parent.NestedSlots)
            {
                changed |= EnrichDurationDisplayName(
                    target.NestedSlotObservedDurations,
                    NestedSlotObservationKey(parent.SlotId, parent.ItemId, slot.Path),
                    slot.SlotDisplayName);
                if (!target.NestedSlotStates.TryGetValue(
                        NestedSlotStateKey(parent.SlotId, parent.ItemId, slot),
                        out var row))
                {
                    continue;
                }
                row.ParentItemDisplayName = EnrichedDisplayName(
                    row.ParentItemDisplayName,
                    parent.ItemDisplayName,
                    out var parentChanged);
                row.SlotDisplayName = EnrichedDisplayName(
                    row.SlotDisplayName,
                    slot.SlotDisplayName,
                    out var slotChanged);
                row.ItemDisplayName = EnrichedDisplayName(
                    row.ItemDisplayName,
                    slot.ItemDisplayName,
                    out var itemChanged);
                changed |= parentChanged || slotChanged || itemChanged;
            }
        }
        foreach (var group in snapshot.Totems
                     .GroupBy(TotemStateKey, StringComparer.Ordinal)
                     .OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var index = 0;
            foreach (var totem in group)
            {
                index++;
                changed |= EnrichDurationDisplayName(
                    target.TotemStates,
                    group.Key + "|copy:" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    DescribeTotem(totem));
            }
        }
        return changed;
    }

    private static bool EnrichDurationDisplayName(
        Dictionary<string, EquipmentDurationAggregate> target,
        string key,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)
            || !target.TryGetValue(key, out var row)
            || string.Equals(row.DisplayName, displayName, StringComparison.Ordinal))
        {
            return false;
        }
        row.DisplayName = displayName;
        return true;
    }

    private static string EnrichedDisplayName(
        string existing,
        string candidate,
        out bool changed)
    {
        changed = !string.IsNullOrWhiteSpace(candidate)
                  && !string.Equals(existing, candidate, StringComparison.Ordinal);
        return changed ? candidate : existing;
    }

    private static void MergeCharacterSlotStates(
        Dictionary<string, CharacterSlotStateDurationAggregate> target,
        Dictionary<string, CharacterSlotStateDurationAggregate> source)
    {
        foreach (var value in source.Values)
        {
            var snapshot = new CharacterEquipmentSlotSnapshot
            {
                SlotId = value.SlotId,
                SlotDisplayName = value.SlotDisplayName,
                State = value.State,
                ItemId = value.ItemId,
                ItemDisplayName = value.ItemDisplayName,
                ItemKind = value.ItemKind
            };
            AddCharacterSlotStateDuration(target, snapshot, value.ActiveDurationSeconds);
        }
    }

    private static void MergeNestedSlotStates(
        Dictionary<string, NestedSlotStateDurationAggregate> target,
        Dictionary<string, NestedSlotStateDurationAggregate> source)
    {
        foreach (var value in source.Values)
        {
            var parent = new EquippedItemSnapshot
            {
                SlotId = value.ParentSlotId,
                ItemId = value.ParentItemId,
                ItemDisplayName = value.ParentItemDisplayName,
                Kind = value.ParentItemKind
            };
            var slot = new NestedEquipmentSlotSnapshot
            {
                Path = value.Path,
                SlotKey = value.SlotKey,
                SlotDisplayName = value.SlotDisplayName,
                State = value.State,
                ItemId = value.ItemId,
                ItemDisplayName = value.ItemDisplayName
            };
            AddNestedSlotStateDuration(target, parent, slot, value.ActiveDurationSeconds);
        }
    }

    private static string CharacterSlotObservationKey(string slotId) => Component(slotId);

    private static string CharacterSlotStateKey(CharacterEquipmentSlotSnapshot slot) =>
        CharacterSlotObservationKey(slot.SlotId) + "|" + ((int)slot.State).ToString(System.Globalization.CultureInfo.InvariantCulture)
        + "|" + Component(slot.State == EquipmentSlotState.Occupied ? slot.ItemId : string.Empty);

    private static string NestedSlotObservationKey(string parentSlotId, string parentItemId, string path) =>
        Component(parentSlotId) + "|" + Component(parentItemId) + "|" + Component(path);

    private static string NestedSlotStateKey(string parentSlotId, string parentItemId, NestedEquipmentSlotSnapshot slot) =>
        NestedSlotObservationKey(parentSlotId, parentItemId, slot.Path) + "|"
        + ((int)slot.State).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|"
        + Component(slot.State == EquipmentSlotState.Occupied ? slot.ItemId : string.Empty);

    private static string Component(string? value)
    {
        value ??= string.Empty;
        return value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value;
    }

    private static double CheckedDurationAdd(double left, double right)
    {
        if (!IsFinite(left) || left < 0 || !IsFinite(right) || right < 0 || left > double.MaxValue - right)
            throw new OverflowException("Equipment slot-state duration overflowed.");
        return left + right;
    }

    private static EquipmentCombatAssociationAggregate Association(EquipmentStatisticsAggregate target, EquipmentEventAssociation? association)
    {
        association ??= new EquipmentEventAssociation();
        var loadout = EmptyToUnavailable(association.LoadoutId);
        var totems = EmptyToUnavailable(association.TotemSetId);
        var selected = association.SelectedWeaponId ?? string.Empty;
        var selectedSlot = association.SelectedWeaponSlotId ?? string.Empty;
        var key = loadout + "|" + selectedSlot + "|" + selected + "|" + totems;
        if (!target.CombatAssociations.TryGetValue(key, out var row))
        {
            row = new EquipmentCombatAssociationAggregate { LoadoutId = loadout, SelectedWeaponSlotId = selectedSlot, SelectedWeaponId = selected, TotemSetId = totems };
            target.CombatAssociations[key] = row;
        }
        return row;
    }

    private static string EmptyToUnavailable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? EquipmentEventAssociation.UnavailableId : value;

    private static void AddDuration(Dictionary<string, EquipmentDurationAggregate> target, string id, string name, double delta)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (!target.TryGetValue(id, out var row))
        { row = new EquipmentDurationAggregate { Id = id, DisplayName = name }; target[id] = row; }
        if (!string.IsNullOrWhiteSpace(name)) row.DisplayName = name;
        row.ActiveDurationSeconds = SaturatingAdd(row.ActiveDurationSeconds, delta);
    }

    private static void MergeDurations(Dictionary<string, EquipmentDurationAggregate> target, Dictionary<string, EquipmentDurationAggregate> source, bool countRun = false)
    {
        foreach (var pair in source)
        {
            if (!target.TryGetValue(pair.Key, out var row))
            { row = new EquipmentDurationAggregate { Id = pair.Value.Id, DisplayName = pair.Value.DisplayName }; target[pair.Key] = row; }
            row.DisplayName = string.IsNullOrWhiteSpace(pair.Value.DisplayName) ? row.DisplayName : pair.Value.DisplayName;
            row.ActiveDurationSeconds = SaturatingAdd(row.ActiveDurationSeconds, pair.Value.ActiveDurationSeconds);
            row.RunOccurrences = SaturatingAdd(row.RunOccurrences, countRun ? 1 : pair.Value.RunOccurrences);
        }
    }

    private static EquipmentMetricCapabilities RestrictCapabilities(
        EquipmentMetricCapabilities target,
        EquipmentMetricCapabilities source,
        bool preferSourceOnTie) => new()
        {
            EquipmentSlots = Restrict(target.EquipmentSlots, source.EquipmentSlots, preferSourceOnTie),
            SelectedWeapon = Restrict(target.SelectedWeapon, source.SelectedWeapon, preferSourceOnTie),
            AttachmentMetadata = Restrict(target.AttachmentMetadata, source.AttachmentMetadata, preferSourceOnTie),
            DirectTotems = Restrict(target.DirectTotems, source.DirectTotems, preferSourceOnTie),
            ToteContents = Restrict(target.ToteContents, source.ToteContents, preferSourceOnTie),
            ToteActivation = Restrict(target.ToteActivation, source.ToteActivation, preferSourceOnTie),
            CharacterSlotState = Restrict(target.CharacterSlotState, source.CharacterSlotState, preferSourceOnTie),
            NestedSlotState = Restrict(target.NestedSlotState, source.NestedSlotState, preferSourceOnTie)
        };

    private static bool HasObservations(EquipmentStatisticsAggregate value) => value.Items?.Count > 0
        || value.SelectedWeapons?.Count > 0 || value.Loadouts?.Count > 0 || value.TotemSets?.Count > 0
        || value.TotemStates?.Count > 0 || value.Slots?.Count > 0 || value.SlottedWeapons?.Count > 0
        || value.CharacterSlotObservedDurations?.Count > 0 || value.CharacterSlotStates?.Count > 0
        || value.NestedSlotObservedDurations?.Count > 0 || value.NestedSlotStates?.Count > 0
        || value.CombatAssociations?.Count > 0 || value.TransitionCount > 0;

    private static MetricAvailability Restrict(MetricAvailability a, MetricAvailability b, bool preferSourceOnTie) =>
        (int)a.State > (int)b.State || (!preferSourceOnTie && a.State == b.State) ? Clone(a) : Clone(b);

    private static MetricAvailability Clone(MetricAvailability? value) => new()
    {
        State = value?.State ?? AdapterCapabilityState.DisabledIncompatible,
        Provenance = value?.Provenance ?? "Capability metadata was missing."
    };

    private static void NormalizeCapabilities(EquipmentMetricCapabilities value, ref bool repaired)
    {
        value.EquipmentSlots ??= Repair(new MetricAvailability(), ref repaired);
        value.SelectedWeapon ??= Repair(new MetricAvailability(), ref repaired);
        value.AttachmentMetadata ??= Repair(new MetricAvailability(), ref repaired);
        value.DirectTotems ??= Repair(new MetricAvailability(), ref repaired);
        value.ToteContents ??= Repair(new MetricAvailability(), ref repaired);
        value.ToteActivation ??= Repair(new MetricAvailability(), ref repaired);
        value.CharacterSlotState ??= Repair(new MetricAvailability(), ref repaired);
        value.NestedSlotState ??= Repair(new MetricAvailability(), ref repaired);
        NormalizeAvailability(value.EquipmentSlots, ref repaired);
        NormalizeAvailability(value.SelectedWeapon, ref repaired);
        NormalizeAvailability(value.AttachmentMetadata, ref repaired);
        NormalizeAvailability(value.DirectTotems, ref repaired);
        NormalizeAvailability(value.ToteContents, ref repaired);
        NormalizeAvailability(value.ToteActivation, ref repaired);
        NormalizeAvailability(value.CharacterSlotState, ref repaired);
        NormalizeAvailability(value.NestedSlotState, ref repaired);
    }

    private static Dictionary<string, EquipmentDurationAggregate> NormalizeDurations(
        Dictionary<string, EquipmentDurationAggregate> source,
        ref bool repaired)
    {
        var normalized = new Dictionary<string, EquipmentDurationAggregate>(StringComparer.Ordinal);
        var changed = false;
        foreach (var pair in source.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var row = pair.Value;
            if (row == null)
            {
                changed = true;
                continue;
            }
            var key = string.IsNullOrWhiteSpace(row.Id) ? pair.Key?.Trim() ?? string.Empty : row.Id.Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                changed = true;
                continue;
            }
            var displayName = row.DisplayName?.Trim() ?? string.Empty;
            var duration = FiniteNonNegative(row.ActiveDurationSeconds);
            var occurrences = Math.Max(0, row.RunOccurrences);
            if (!string.Equals(pair.Key, key, StringComparison.Ordinal)
                || !string.Equals(row.Id, key, StringComparison.Ordinal)
                || !string.Equals(row.DisplayName, displayName, StringComparison.Ordinal)
                || duration != row.ActiveDurationSeconds || occurrences != row.RunOccurrences) changed = true;
            if (!normalized.TryGetValue(key, out var existing))
            {
                normalized[key] = new EquipmentDurationAggregate
                {
                    Id = key,
                    DisplayName = displayName,
                    ActiveDurationSeconds = duration,
                    RunOccurrences = occurrences
                };
                continue;
            }
            changed = true;
            existing.ActiveDurationSeconds = SaturatingAdd(existing.ActiveDurationSeconds, duration);
            existing.RunOccurrences = SaturatingAdd(existing.RunOccurrences, occurrences);
            if (string.IsNullOrWhiteSpace(existing.DisplayName)) existing.DisplayName = displayName;
        }
        if (changed) repaired = true;
        return changed ? normalized : source;
    }

    private static Dictionary<string, EquipmentCombatAssociationAggregate> NormalizeCombatAssociations(
        Dictionary<string, EquipmentCombatAssociationAggregate> source,
        ref bool repaired)
    {
        var normalized = new Dictionary<string, EquipmentCombatAssociationAggregate>(StringComparer.Ordinal);
        var changed = false;
        foreach (var pair in source.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var row = pair.Value;
            if (row == null)
            {
                changed = true;
                continue;
            }
            var loadout = EmptyToUnavailable(row.LoadoutId?.Trim());
            var totems = EmptyToUnavailable(row.TotemSetId?.Trim());
            var selected = row.SelectedWeaponId?.Trim() ?? string.Empty;
            var selectedSlot = row.SelectedWeaponSlotId?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(selected) != string.IsNullOrWhiteSpace(selectedSlot))
            {
                selected = string.Empty;
                selectedSlot = string.Empty;
                changed = true;
            }
            var canonicalKey = loadout + "|" + selectedSlot + "|" + selected + "|" + totems;
            var firing = Math.Max(0, row.FiringActions);
            var ammunition = Math.Max(0, row.AmmunitionUnitsConsumed);
            var projectiles = Math.Max(0, row.Projectiles);
            var ranged = Math.Max(0, row.RangedHits);
            var melee = Math.Max(0, row.MeleeHits);
            var kills = Math.Max(0, row.EnemiesKilled);
            var playerKills = Math.Max(0, row.KillsByYou);
            var legacyDeathCredit = Math.Max(0, row.LegacyUnclassifiedDeathCredit);
            var deaths = Math.Max(0, row.PlayerDeaths);
            var dealt = FiniteNonNegative(row.DamageDealt);
            var received = FiniteNonNegative(row.DamageReceived);
            if (!string.Equals(pair.Key, canonicalKey, StringComparison.Ordinal)
                || !string.Equals(row.LoadoutId, loadout, StringComparison.Ordinal)
                || !string.Equals(row.TotemSetId, totems, StringComparison.Ordinal)
                || !string.Equals(row.SelectedWeaponId, selected, StringComparison.Ordinal)
                || !string.Equals(row.SelectedWeaponSlotId, selectedSlot, StringComparison.Ordinal)
                || firing != row.FiringActions || ammunition != row.AmmunitionUnitsConsumed
                || projectiles != row.Projectiles || ranged != row.RangedHits || melee != row.MeleeHits
                || kills != row.EnemiesKilled || playerKills != row.KillsByYou
                || legacyDeathCredit != row.LegacyUnclassifiedDeathCredit || deaths != row.PlayerDeaths
                || dealt != row.DamageDealt || received != row.DamageReceived) changed = true;
            if (!normalized.TryGetValue(canonicalKey, out var existing))
            {
                existing = new EquipmentCombatAssociationAggregate
                {
                    LoadoutId = loadout,
                    TotemSetId = totems,
                    SelectedWeaponId = selected,
                    SelectedWeaponSlotId = selectedSlot
                };
                normalized[canonicalKey] = existing;
            }
            else
            {
                changed = true;
            }
            existing.FiringActions = SaturatingAdd(existing.FiringActions, firing);
            existing.AmmunitionUnitsConsumed = SaturatingAdd(existing.AmmunitionUnitsConsumed, ammunition);
            existing.Projectiles = SaturatingAdd(existing.Projectiles, projectiles);
            existing.RangedHits = SaturatingAdd(existing.RangedHits, ranged);
            existing.MeleeHits = SaturatingAdd(existing.MeleeHits, melee);
            existing.EnemiesKilled = SaturatingAdd(existing.EnemiesKilled, kills);
            existing.KillsByYou = SaturatingAdd(existing.KillsByYou, playerKills);
            existing.LegacyUnclassifiedDeathCredit = SaturatingAdd(
                existing.LegacyUnclassifiedDeathCredit,
                legacyDeathCredit);
            existing.PlayerDeaths = SaturatingAdd(existing.PlayerDeaths, deaths);
            existing.DamageDealt = SaturatingAdd(existing.DamageDealt, dealt);
            existing.DamageReceived = SaturatingAdd(existing.DamageReceived, received);
        }
        if (changed) repaired = true;
        return changed ? normalized : source;
    }

    private static Dictionary<string, CharacterSlotStateDurationAggregate> NormalizeCharacterSlotStates(
        Dictionary<string, CharacterSlotStateDurationAggregate> source,
        ref bool repaired)
    {
        var normalized = new Dictionary<string, CharacterSlotStateDurationAggregate>(StringComparer.Ordinal);
        foreach (var pair in source.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var row = pair.Value;
            if (!ValidCharacterSlotState(row))
            {
                repaired = true;
                continue;
            }
            row.SlotId = row.SlotId.Trim();
            row.SlotDisplayName = row.SlotDisplayName?.Trim() ?? string.Empty;
            row.ItemId = row.State == EquipmentSlotState.Occupied ? row.ItemId.Trim() : string.Empty;
            row.ItemDisplayName = row.State == EquipmentSlotState.Occupied
                ? row.ItemDisplayName?.Trim() ?? string.Empty : string.Empty;
            var key = CharacterSlotStateKey(new CharacterEquipmentSlotSnapshot
            {
                SlotId = row.SlotId,
                State = row.State,
                ItemId = row.ItemId
            });
            if (!string.Equals(pair.Key, key, StringComparison.Ordinal)) repaired = true;
            if (!normalized.TryGetValue(key, out var existing))
            {
                normalized[key] = row;
                continue;
            }
            existing.ActiveDurationSeconds = SaturatingAdd(existing.ActiveDurationSeconds, row.ActiveDurationSeconds);
            if (string.IsNullOrWhiteSpace(existing.SlotDisplayName)) existing.SlotDisplayName = row.SlotDisplayName;
            if (string.IsNullOrWhiteSpace(existing.ItemDisplayName)) existing.ItemDisplayName = row.ItemDisplayName;
            repaired = true;
        }
        return normalized;
    }

    private static Dictionary<string, NestedSlotStateDurationAggregate> NormalizeNestedSlotStates(
        Dictionary<string, NestedSlotStateDurationAggregate> source,
        ref bool repaired)
    {
        var normalized = new Dictionary<string, NestedSlotStateDurationAggregate>(StringComparer.Ordinal);
        foreach (var pair in source.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var row = pair.Value;
            if (!ValidNestedSlotState(row))
            {
                repaired = true;
                continue;
            }
            row.ParentSlotId = row.ParentSlotId.Trim();
            row.ParentItemId = row.ParentItemId.Trim();
            row.ParentItemDisplayName = row.ParentItemDisplayName?.Trim() ?? string.Empty;
            row.Path = row.Path.Trim();
            row.SlotKey = row.SlotKey.Trim();
            row.SlotDisplayName = row.SlotDisplayName?.Trim() ?? string.Empty;
            row.ItemId = row.State == EquipmentSlotState.Occupied ? row.ItemId.Trim() : string.Empty;
            row.ItemDisplayName = row.State == EquipmentSlotState.Occupied
                ? row.ItemDisplayName?.Trim() ?? string.Empty : string.Empty;
            var key = NestedSlotStateKey(row.ParentSlotId, row.ParentItemId, new NestedEquipmentSlotSnapshot
            {
                Path = row.Path,
                State = row.State,
                ItemId = row.ItemId
            });
            if (!string.Equals(pair.Key, key, StringComparison.Ordinal)) repaired = true;
            if (!normalized.TryGetValue(key, out var existing))
            {
                normalized[key] = row;
                continue;
            }
            existing.ActiveDurationSeconds = SaturatingAdd(existing.ActiveDurationSeconds, row.ActiveDurationSeconds);
            if (string.IsNullOrWhiteSpace(existing.ParentItemDisplayName)) existing.ParentItemDisplayName = row.ParentItemDisplayName;
            if (string.IsNullOrWhiteSpace(existing.SlotDisplayName)) existing.SlotDisplayName = row.SlotDisplayName;
            if (string.IsNullOrWhiteSpace(existing.ItemDisplayName)) existing.ItemDisplayName = row.ItemDisplayName;
            repaired = true;
        }
        return normalized;
    }

    private static bool ValidCharacterSlotState(CharacterSlotStateDurationAggregate? row) => row != null
        && !string.IsNullOrWhiteSpace(row.SlotId)
        && Enum.IsDefined(typeof(EquipmentSlotState), row.State)
        && Enum.IsDefined(typeof(EquipmentItemKind), row.ItemKind)
        && (row.State == EquipmentSlotState.Occupied
            ? !string.IsNullOrWhiteSpace(row.ItemId)
            : string.IsNullOrEmpty(row.ItemId) && string.IsNullOrEmpty(row.ItemDisplayName))
        && IsFinite(row.ActiveDurationSeconds) && row.ActiveDurationSeconds >= 0;

    private static bool ValidNestedSlotState(NestedSlotStateDurationAggregate? row) => row != null
        && !string.IsNullOrWhiteSpace(row.ParentSlotId)
        && !string.IsNullOrWhiteSpace(row.ParentItemId)
        && Enum.IsDefined(typeof(EquipmentItemKind), row.ParentItemKind)
        && !string.IsNullOrWhiteSpace(row.Path)
        && !string.IsNullOrWhiteSpace(row.SlotKey)
        && Enum.IsDefined(typeof(EquipmentSlotState), row.State)
        && (row.State == EquipmentSlotState.Occupied
            ? !string.IsNullOrWhiteSpace(row.ItemId)
            : string.IsNullOrEmpty(row.ItemId) && string.IsNullOrEmpty(row.ItemDisplayName))
        && IsFinite(row.ActiveDurationSeconds) && row.ActiveDurationSeconds >= 0;

    private static void ValidateSlotStateReconciliation(EquipmentStatisticsAggregate target)
    {
        foreach (var row in target.CharacterSlotStates.Values)
        {
            if (!ValidCharacterSlotState(row))
                throw new ArgumentException("Character-slot state is invalid.", nameof(target));
            var observationKey = CharacterSlotObservationKey(row.SlotId);
            if (!target.CharacterSlotObservedDurations.TryGetValue(observationKey, out var observed)
                || row.ActiveDurationSeconds > observed.ActiveDurationSeconds)
                throw new ArgumentException("Character-slot state exceeds observable slot duration.", nameof(target));
        }
        foreach (var observed in target.CharacterSlotObservedDurations)
        {
            var total = target.CharacterSlotStates.Values
                .Where(row => string.Equals(CharacterSlotObservationKey(row.SlotId), observed.Key, StringComparison.Ordinal))
                .Select(row => row.ActiveDurationSeconds)
                .Aggregate(0d, CheckedDurationAdd);
            if (!DurationsEqual(total, observed.Value.ActiveDurationSeconds))
                throw new ArgumentException("Character-slot occupied and empty states do not reconcile.", nameof(target));
        }

        foreach (var row in target.NestedSlotStates.Values)
        {
            if (!ValidNestedSlotState(row))
                throw new ArgumentException("Nested-slot state is invalid.", nameof(target));
            var observationKey = NestedSlotObservationKey(row.ParentSlotId, row.ParentItemId, row.Path);
            if (!target.NestedSlotObservedDurations.TryGetValue(observationKey, out var observed)
                || row.ActiveDurationSeconds > observed.ActiveDurationSeconds)
                throw new ArgumentException("Nested-slot state exceeds observable path duration.", nameof(target));
            var parentPrefix = row.ParentSlotId + "|" + row.ParentItemId + "|";
            var parentDuration = target.Items
                .Where(pair => pair.Key.StartsWith(parentPrefix, StringComparison.Ordinal))
                .Select(pair => pair.Value.ActiveDurationSeconds)
                .Aggregate(0d, CheckedDurationAdd);
            if (observed.ActiveDurationSeconds > parentDuration)
                throw new ArgumentException("Nested-slot observation exceeds parent equipped duration.", nameof(target));
        }
        foreach (var observed in target.NestedSlotObservedDurations)
        {
            var total = target.NestedSlotStates.Values.Where(row => string.Equals(
                    NestedSlotObservationKey(row.ParentSlotId, row.ParentItemId, row.Path),
                    observed.Key,
                    StringComparison.Ordinal))
                .Select(row => row.ActiveDurationSeconds)
                .Aggregate(0d, CheckedDurationAdd);
            if (!DurationsEqual(total, observed.Value.ActiveDurationSeconds))
                throw new ArgumentException("Nested-slot occupied and empty states do not reconcile.", nameof(target));
        }
    }

    private static bool DurationsEqual(double left, double right)
    {
        if (left == right) return true;
        var scale = Math.Max(1d, Math.Max(Math.Abs(left), Math.Abs(right)));
        return Math.Abs(left - right) <= scale * 1e-12;
    }

    private static void NormalizeAvailability(MetricAvailability value, ref bool repaired)
    {
        if (!Enum.IsDefined(typeof(AdapterCapabilityState), value.State))
        {
            value.State = AdapterCapabilityState.DisabledIncompatible;
            value.Provenance = "Capability state was invalid and was disabled during normalization.";
            repaired = true;
        }
        else if (value.Provenance == null)
        {
            value.Provenance = string.Empty;
            repaired = true;
        }
    }

    private static T Repair<T>(T value, ref bool repaired) { repaired = true; return value; }
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double FiniteNonNegative(double value) => IsFinite(value) ? Math.Max(0, value) : 0;
    private static double SaturatingAdd(double left, double right)
    {
        left = FiniteNonNegative(left);
        right = FiniteNonNegative(right);
        return left > double.MaxValue - right ? double.MaxValue : left + right;
    }

    private static long SaturatingAdd(long left, long right)
    {
        left = Math.Max(0, left);
        if (right <= 0) return left;
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }

    private static string MergeProvenance(string? left, string? right) => string.Join(
        " | ",
        new[] { left, right }.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal));

    private static void RecordTransition(
        EquipmentStatisticsAggregate target,
        double activeSeconds,
        EquipmentSnapshot? from,
        EquipmentSnapshot? to)
    {
        target.TransitionCount = SaturatingAdd(target.TransitionCount, 1);
        target.Transitions.Add(new EquipmentTransition
        {
            ActiveTimeSeconds = activeSeconds,
            FromSnapshotId = from?.SnapshotId ?? string.Empty,
            ToSnapshotId = to?.SnapshotId ?? EquipmentStatisticsAggregate.ObservationUnavailableSnapshotId,
            FromLoadoutId = from?.LoadoutId ?? EquipmentEventAssociation.UnavailableId,
            ToLoadoutId = to?.LoadoutId ?? EquipmentEventAssociation.UnavailableId,
            SelectedWeaponSlotId = to?.SelectedWeaponSlotId ?? string.Empty,
            SelectedWeaponId = to?.SelectedWeaponId ?? string.Empty,
            TotemSetId = to?.TotemSetId ?? EquipmentEventAssociation.UnavailableId
        });
        if (target.Transitions.Count <= EquipmentStatisticsAggregate.TransitionCapacity) return;
        target.Transitions.RemoveAt(0);
        target.TransitionsTruncated = true;
    }

    private static string DescribeLoadout(EquipmentSnapshot snapshot) => snapshot.Items.Count == 0
        ? "Empty loadout"
        : string.Join("; ", snapshot.Items.OrderBy(value => value.SlotId, StringComparer.Ordinal).Select(value =>
            $"{(string.IsNullOrWhiteSpace(value.SlotDisplayName) ? value.SlotId : value.SlotDisplayName)}: "
            + $"{(string.IsNullOrWhiteSpace(value.ItemDisplayName) ? value.ItemId : value.ItemDisplayName)} "
            + $"[{value.ItemId}; attachments={value.AttachmentSignature}]"));

    private static string DescribeActiveTotemSet(EquipmentSnapshot snapshot) => string.Join(
        "; ",
        snapshot.Totems.Where(value => value.ActivationState == TotemActivationState.ProvenActive)
            .OrderBy(TotemStateKey, StringComparer.Ordinal)
            .Select(DescribeTotem));

    private static string DescribeTotem(TotemSnapshot value) =>
        $"{(string.IsNullOrWhiteSpace(value.DisplayName) ? value.ItemId : value.DisplayName)} "
        + $"[{value.ItemId}; {value.CarryKind}; {value.ActivationState}; container={value.ContainerId}]";

    private static string TotemStateKey(TotemSnapshot value) =>
        ((int)value.CarryKind).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|"
        + value.ContainerId + "|" + value.ItemId + "|"
        + ((int)value.ActivationState).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
