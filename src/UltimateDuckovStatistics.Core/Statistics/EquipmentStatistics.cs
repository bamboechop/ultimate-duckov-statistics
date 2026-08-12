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

        var from = target.CurrentSnapshot?.SnapshotId ?? string.Empty;
        target.CurrentSnapshot = Clone(snapshot);
        target.TransitionCount = SaturatingAdd(target.TransitionCount, 1);
        target.Transitions.Add(new EquipmentTransition
        {
            ActiveTimeSeconds = activeSeconds,
            FromSnapshotId = from,
            ToSnapshotId = snapshot.SnapshotId
        });
        if (target.Transitions.Count > EquipmentStatisticsAggregate.TransitionCapacity)
        {
            target.Transitions.RemoveAt(0);
            target.TransitionsTruncated = true;
        }

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

        AddDuration(target.Loadouts, snapshot.LoadoutId, snapshot.LoadoutId, delta);
        if (!string.IsNullOrWhiteSpace(snapshot.SelectedWeaponId))
            AddDuration(target.SelectedWeapons, snapshot.SelectedWeaponSlotId + "|" + snapshot.SelectedWeaponId, snapshot.SelectedWeaponId, delta);
        if (snapshot.Totems.Any(value => value.ActivationState == TotemActivationState.ProvenActive))
            AddDuration(target.TotemSets, snapshot.TotemSetId, snapshot.TotemSetId, delta);
        foreach (var item in snapshot.Items)
            AddDuration(target.Items, item.SlotId + "|" + item.ItemId + "|" + item.AttachmentSignature, item.ItemDisplayName, delta);
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
        row.DamageDealt += FiniteNonNegative(value.ActualDamageDealt);
        row.DamageReceived += FiniteNonNegative(value.ActualDamageReceived);
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
            row.DamageDealt += FiniteNonNegative(value.DamageDealt);
            row.DamageReceived += FiniteNonNegative(value.DamageReceived);
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
        clone.Transitions = source.Transitions.Select(x => new EquipmentTransition
        {
            ActiveTimeSeconds = x.ActiveTimeSeconds,
            FromSnapshotId = x.FromSnapshotId,
            ToSnapshotId = x.ToSnapshotId
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
        target.CombatAssociations ??= Repair(new Dictionary<string, EquipmentCombatAssociationAggregate>(StringComparer.Ordinal), ref repaired);
        target.Transitions ??= Repair(new List<EquipmentTransition>(), ref repaired);
        if (!IsFinite(target.ObservedActiveDurationSeconds) || target.ObservedActiveDurationSeconds < 0)
        { target.ObservedActiveDurationSeconds = 0; repaired = true; }
        foreach (var values in new[] { target.Items, target.SelectedWeapons, target.Loadouts, target.TotemSets })
        {
            foreach (var pair in values.ToArray())
            {
                var row = pair.Value;
                if (row == null)
                {
                    values.Remove(pair.Key);
                    repaired = true;
                    continue;
                }
                if (!IsFinite(row.ActiveDurationSeconds) || row.ActiveDurationSeconds < 0)
                { row.ActiveDurationSeconds = 0; repaired = true; }
                if (row.RunOccurrences < 0) { row.RunOccurrences = 0; repaired = true; }
            }
        }
        foreach (var pair in target.CombatAssociations.ToArray())
        {
            var row = pair.Value;
            if (row == null)
            {
                target.CombatAssociations.Remove(pair.Key);
                repaired = true;
                continue;
            }
            if (row.FiringActions < 0) { row.FiringActions = 0; repaired = true; }
            if (row.AmmunitionUnitsConsumed < 0) { row.AmmunitionUnitsConsumed = 0; repaired = true; }
            if (row.Projectiles < 0) { row.Projectiles = 0; repaired = true; }
            if (row.RangedHits < 0) { row.RangedHits = 0; repaired = true; }
            if (row.MeleeHits < 0) { row.MeleeHits = 0; repaired = true; }
            if (row.EnemiesKilled < 0) { row.EnemiesKilled = 0; repaired = true; }
            if (row.PlayerDeaths < 0) { row.PlayerDeaths = 0; repaired = true; }
            if (!IsFinite(row.DamageDealt) || row.DamageDealt < 0) { row.DamageDealt = 0; repaired = true; }
            if (!IsFinite(row.DamageReceived) || row.DamageReceived < 0) { row.DamageReceived = 0; repaired = true; }
        }
        var validTransitions = target.Transitions.Where(row => row != null && IsFinite(row.ActiveTimeSeconds)
            && row.ActiveTimeSeconds >= 0 && row.ActiveTimeSeconds <= target.ObservedActiveDurationSeconds
            && !string.IsNullOrWhiteSpace(row.ToSnapshotId)).ToList();
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
        target.WasRepairedFromInvalidState |= repaired;
        return repaired;
    }

    public static void ValidateAggregate(EquipmentStatisticsAggregate target)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (target.Capabilities == null || target.Items == null || target.SelectedWeapons == null
            || target.Loadouts == null || target.TotemSets == null || target.CombatAssociations == null
            || target.Transitions == null || !IsFinite(target.ObservedActiveDurationSeconds)
            || target.ObservedActiveDurationSeconds < 0 || target.Transitions.Count > EquipmentStatisticsAggregate.TransitionCapacity
            || target.Items.Values.Any(row => row == null) || target.SelectedWeapons.Values.Any(row => row == null)
            || target.Loadouts.Values.Any(row => row == null) || target.TotemSets.Values.Any(row => row == null)
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
        foreach (var values in new[] { target.Items, target.SelectedWeapons, target.Loadouts, target.TotemSets })
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
        && value.TotemSets.Count == 0 && value.CombatAssociations.Count == 0 && value.TransitionCount == 0
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
        var hasSelectedId = !string.IsNullOrWhiteSpace(snapshot.SelectedWeaponId);
        var hasSelectedSlot = !string.IsNullOrWhiteSpace(snapshot.SelectedWeaponSlotId);
        if (hasSelectedId != hasSelectedSlot
            || (hasSelectedId && !snapshot.Items.Any(value =>
                string.Equals(value.ItemId, snapshot.SelectedWeaponId, StringComparison.Ordinal)
                && string.Equals(value.SlotId, snapshot.SelectedWeaponSlotId, StringComparison.Ordinal))))
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
        row.ActiveDurationSeconds += delta;
    }

    private static void MergeDurations(Dictionary<string, EquipmentDurationAggregate> target, Dictionary<string, EquipmentDurationAggregate> source, bool countRun = false)
    {
        foreach (var pair in source)
        {
            if (!target.TryGetValue(pair.Key, out var row))
            { row = new EquipmentDurationAggregate { Id = pair.Value.Id, DisplayName = pair.Value.DisplayName }; target[pair.Key] = row; }
            row.DisplayName = string.IsNullOrWhiteSpace(pair.Value.DisplayName) ? row.DisplayName : pair.Value.DisplayName;
            row.ActiveDurationSeconds += FiniteNonNegative(pair.Value.ActiveDurationSeconds);
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
    }

    private static T Repair<T>(T value, ref bool repaired) { repaired = true; return value; }
    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static double FiniteNonNegative(double value) => IsFinite(value) ? Math.Max(0, value) : 0;
    private static long SaturatingAdd(long left, long right) => right > 0 && left > long.MaxValue - right ? long.MaxValue : Math.Max(0, left + right);
}
