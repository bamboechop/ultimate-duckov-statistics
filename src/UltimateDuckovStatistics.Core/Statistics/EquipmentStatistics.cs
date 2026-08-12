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
    [DataMember(Order = 11)] public long EnemiesKilled { get; set; }
    [DataMember(Order = 12)] public long PlayerDeaths { get; set; }
    [DataMember(Order = 13)] public string SelectedWeaponSlotId { get; set; } = string.Empty;
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
}

public static class EquipmentStatisticsReducer
{
    public static bool Observe(EquipmentStatisticsAggregate target, EquipmentSnapshot snapshot, double activeSeconds)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        ValidateSnapshot(snapshot);
        Advance(target, activeSeconds);
        if (string.Equals(target.CurrentSnapshot?.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal))
        {
            target.CurrentSnapshot = Clone(snapshot);
            return false;
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
        target.ObservedActiveDurationSeconds = activeSeconds;
        var snapshot = target.CurrentSnapshot;
        if (delta <= 0 || snapshot == null) return;

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
        var row = Association(target, value.EquipmentAssociation);
        row.DamageDealt = SaturatingAdd(row.DamageDealt, value.ActualDamageDealt);
        row.DamageReceived = SaturatingAdd(row.DamageReceived, value.ActualDamageReceived);
        row.RangedHits = SaturatingAdd(row.RangedHits, value.RangedHits);
        row.MeleeHits = SaturatingAdd(row.MeleeHits, value.MeleeHits);
        row.EnemiesKilled = SaturatingAdd(row.EnemiesKilled, value.EnemiesKilled);
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
        target.Capabilities = preserveUnavailable
            ? RestrictCapabilities(target.Capabilities, source.Capabilities)
            : CloneCapabilities(source.Capabilities);
        MergeDurations(target.Items, source.Items);
        MergeDurations(target.SelectedWeapons, source.SelectedWeapons);
        MergeDurations(target.Loadouts, source.Loadouts, countRun: countRunOccurrence);
        MergeDurations(target.TotemSets, source.TotemSets, countRun: countRunOccurrence);
        MergeDurations(target.TotemStates, source.TotemStates);
        MergeDurations(target.Slots, source.Slots);
        MergeDurations(target.SlottedWeapons, source.SlottedWeapons);
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
            row.PlayerDeaths = SaturatingAdd(row.PlayerDeaths, value.PlayerDeaths);
        }
        target.HistoricalUnavailable |= source.HistoricalUnavailable;
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
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
        };
        MergeDurations(clone.Items, source.Items);
        MergeDurations(clone.SelectedWeapons, source.SelectedWeapons);
        MergeDurations(clone.Loadouts, source.Loadouts);
        MergeDurations(clone.TotemSets, source.TotemSets);
        MergeDurations(clone.TotemStates, source.TotemStates);
        MergeDurations(clone.Slots, source.Slots);
        MergeDurations(clone.SlottedWeapons, source.SlottedWeapons);
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
                PlayerDeaths = value.PlayerDeaths
            };
        }
        return clone;
    }

    public static EquipmentMetricCapabilities CloneCapabilities(EquipmentMetricCapabilities value) => new()
    {
        EquipmentSlots = Clone(value?.EquipmentSlots),
        SelectedWeapon = Clone(value?.SelectedWeapon),
        AttachmentMetadata = Clone(value?.AttachmentMetadata),
        DirectTotems = Clone(value?.DirectTotems),
        ToteContents = Clone(value?.ToteContents),
        ToteActivation = Clone(value?.ToteActivation)
    };

    public static bool NormalizePersisted(EquipmentStatisticsAggregate target)
    {
        if (target == null) return false;
        var repaired = false;
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
        return repaired;
    }

    public static void ValidateAggregate(EquipmentStatisticsAggregate target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (target.Capabilities == null || target.Items == null || target.SelectedWeapons == null
            || target.Loadouts == null || target.TotemSets == null || target.CombatAssociations == null
            || target.TotemStates == null || target.Slots == null || target.SlottedWeapons == null
            || target.Transitions == null || !IsFinite(target.ObservedActiveDurationSeconds)
            || target.ObservedActiveDurationSeconds < 0 || target.Transitions.Count > EquipmentStatisticsAggregate.TransitionCapacity
            || target.Items.Values.Any(row => row == null) || target.SelectedWeapons.Values.Any(row => row == null)
            || target.Loadouts.Values.Any(row => row == null) || target.TotemSets.Values.Any(row => row == null)
            || target.TotemStates.Values.Any(row => row == null)
            || target.Slots.Values.Any(row => row == null) || target.SlottedWeapons.Values.Any(row => row == null)
            || target.CombatAssociations.Values.Any(row => row == null))
            throw new ArgumentException("Equipment statistics are invalid.", nameof(target));
    }

    public static void ValidateRecoveryCandidate(EquipmentStatisticsAggregate? target)
    {
        if (target == null) return;
        if (!IsFinite(target.ObservedActiveDurationSeconds) || target.ObservedActiveDurationSeconds < 0
            || target.TransitionCount < 0 || target.Transitions?.Count > EquipmentStatisticsAggregate.TransitionCapacity)
            throw new ArgumentException("Equipment checkpoint contains invalid duration or transition counters.", nameof(target));
        if (target.CurrentSnapshot != null) ValidateSnapshot(target.CurrentSnapshot);
        if (target.Transitions?.Any(row => row == null || !IsFinite(row.ActiveTimeSeconds)
                || row.ActiveTimeSeconds < 0 || row.ActiveTimeSeconds > target.ObservedActiveDurationSeconds
                || string.IsNullOrWhiteSpace(row.ToSnapshotId)) == true)
            throw new ArgumentException("Equipment checkpoint contains invalid transitions.", nameof(target));
        foreach (var values in new[] { target.Items, target.SelectedWeapons, target.Loadouts, target.TotemSets, target.TotemStates, target.Slots, target.SlottedWeapons })
        {
            if (values == null) continue;
            if (values.Values.Any(row => row == null || !IsFinite(row.ActiveDurationSeconds)
                                         || row.ActiveDurationSeconds < 0 || row.RunOccurrences < 0))
                throw new ArgumentException("Equipment checkpoint contains invalid aggregate durations.", nameof(target));
        }
        if (target.CombatAssociations?.Values.Any(row => row == null || row.FiringActions < 0
                || row.AmmunitionUnitsConsumed < 0 || row.Projectiles < 0 || row.RangedHits < 0
                || row.MeleeHits < 0 || row.EnemiesKilled < 0 || row.PlayerDeaths < 0
                || !IsFinite(row.DamageDealt) || row.DamageDealt < 0
                || !IsFinite(row.DamageReceived) || row.DamageReceived < 0) == true)
            throw new ArgumentException("Equipment checkpoint contains invalid combat-association counters.", nameof(target));
    }

    public static bool IsEmpty(EquipmentStatisticsAggregate value) => value != null
        && value.Items.Count == 0 && value.SelectedWeapons.Count == 0 && value.Loadouts.Count == 0
        && value.TotemSets.Count == 0 && value.TotemStates.Count == 0
        && value.Slots.Count == 0 && value.SlottedWeapons.Count == 0
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

    private static void ValidateSnapshot(EquipmentSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        if (string.IsNullOrWhiteSpace(snapshot.SnapshotId) || string.IsNullOrWhiteSpace(snapshot.LoadoutId)
            || string.IsNullOrWhiteSpace(snapshot.TotemSetId) || snapshot.Items == null || snapshot.Totems == null)
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
            AttachmentSignature = x.AttachmentSignature
        }).ToList(),
        Totems = source.Totems.Select(x => new TotemSnapshot
        {
            ItemId = x.ItemId,
            DisplayName = x.DisplayName,
            CarryKind = x.CarryKind,
            ContainerId = x.ContainerId,
            ActivationState = x.ActivationState
        }).ToList()
    };

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
        EquipmentMetricCapabilities source) => new()
        {
            EquipmentSlots = Restrict(target.EquipmentSlots, source.EquipmentSlots),
            SelectedWeapon = Restrict(target.SelectedWeapon, source.SelectedWeapon),
            AttachmentMetadata = Restrict(target.AttachmentMetadata, source.AttachmentMetadata),
            DirectTotems = Restrict(target.DirectTotems, source.DirectTotems),
            ToteContents = Restrict(target.ToteContents, source.ToteContents),
            ToteActivation = Restrict(target.ToteActivation, source.ToteActivation)
        };

    private static bool HasObservations(EquipmentStatisticsAggregate value) => value.Items?.Count > 0
        || value.SelectedWeapons?.Count > 0 || value.Loadouts?.Count > 0 || value.TotemSets?.Count > 0
        || value.TotemStates?.Count > 0 || value.Slots?.Count > 0 || value.SlottedWeapons?.Count > 0
        || value.CombatAssociations?.Count > 0 || value.TransitionCount > 0;

    private static MetricAvailability Restrict(MetricAvailability a, MetricAvailability b) =>
        (int)a.State >= (int)b.State ? Clone(a) : Clone(b);

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
        NormalizeAvailability(value.EquipmentSlots, ref repaired);
        NormalizeAvailability(value.SelectedWeapon, ref repaired);
        NormalizeAvailability(value.AttachmentMetadata, ref repaired);
        NormalizeAvailability(value.DirectTotems, ref repaired);
        NormalizeAvailability(value.ToteContents, ref repaired);
        NormalizeAvailability(value.ToteActivation, ref repaired);
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
                || kills != row.EnemiesKilled || deaths != row.PlayerDeaths
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
            existing.PlayerDeaths = SaturatingAdd(existing.PlayerDeaths, deaths);
            existing.DamageDealt = SaturatingAdd(existing.DamageDealt, dealt);
            existing.DamageReceived = SaturatingAdd(existing.DamageReceived, received);
        }
        if (changed) repaired = true;
        return changed ? normalized : source;
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
