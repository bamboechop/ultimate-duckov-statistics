using System.Globalization;
using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class EquipmentCompositionEvidence
{
    [DataMember(Order = 1, IsRequired = true)] public Dictionary<string, LoadoutDefinition> Loadouts { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 2, IsRequired = true)] public Dictionary<string, ActiveTotemSetDefinition> ActiveTotemSets { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 3, IsRequired = true)] public Dictionary<string, TotemStateDuration> TotemStates { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 4, IsRequired = true)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 5, IsRequired = true)] public Dictionary<string, EquipmentDurationAggregate> EmptyDirectSlots { get; set; } = new(StringComparer.Ordinal);
}

[DataContract]
public sealed class LoadoutDefinition
{
    [DataMember(Order = 1, IsRequired = true)] public string LoadoutId { get; set; } = string.Empty;
    [DataMember(Order = 2, IsRequired = true)] public List<CharacterEquipmentSlotSnapshot> Roots { get; set; } = new();
    [DataMember(Order = 3, IsRequired = true)] public List<EquippedItemSnapshot> Items { get; set; } = new();
    [DataMember(Order = 4, IsRequired = true)] public bool RootsComplete { get; set; }
    [DataMember(Order = 5, IsRequired = true)] public bool NestedComplete { get; set; }
    [DataMember(Order = 6, IsRequired = true)] public bool Conflicting { get; set; }
}

[DataContract]
public sealed class ActiveTotemSetDefinition
{
    [DataMember(Order = 1, IsRequired = true)] public string TotemSetId { get; set; } = string.Empty;
    [DataMember(Order = 2, IsRequired = true)] public List<TotemSnapshot> Members { get; set; } = new();
    [DataMember(Order = 3, IsRequired = true)] public bool Conflicting { get; set; }
}

[DataContract]
public sealed class TotemStateDuration
{
    [DataMember(Order = 1, IsRequired = true)] public TotemSnapshot Totem { get; set; } = new();
    [DataMember(Order = 2, IsRequired = true)] public int CopyOrdinal { get; set; }
    [DataMember(Order = 3, IsRequired = true)] public decimal DurationSeconds { get; set; }
}

/// <summary>One definition per exact identity, and one counter per typed presence/copy identity. No observation journal.</summary>
public static class EquipmentCompositionReducer
{
    private static decimal Add(decimal left, decimal right)
    {
        if (left < 0 || right < 0) throw new OverflowException("Equipment duration is negative.");
        return checked(left + right);
    }

    public static bool Observe(EquipmentCompositionEvidence target, EquipmentSnapshot snapshot)
    {
        var changed = false;
        if (Exact(snapshot.LoadoutId) && snapshot.CharacterSlotStateComplete)
        {
            var copy = EquipmentStatisticsReducer.CloneSnapshot(snapshot);
            var definition = new LoadoutDefinition
            {
                LoadoutId = copy.LoadoutId,
                Roots = copy.CharacterSlots,
                Items = copy.Items,
                RootsComplete = copy.CharacterSlotStateComplete,
                NestedComplete = copy.NestedSlotStateComplete
            };
            changed |= Register(target.Loadouts, definition);
        }
        if (Exact(snapshot.TotemSetId))
        {
            var definition = new ActiveTotemSetDefinition
            {
                TotemSetId = snapshot.TotemSetId,
                Members = snapshot.Totems.Where(t => t.ActivationState == TotemActivationState.ProvenActive)
                    .Select(CloneTotem).OrderBy(MemberKey, StringComparer.Ordinal).ToList()
            };
            // Direct-slot attribution is state evidence; the existing set identity deliberately excludes it.
            foreach (var member in definition.Members) member.DirectSlotId = string.Empty;
            changed |= Register(target.ActiveTotemSets, definition);
        }
        return changed;
    }

    public static void Advance(EquipmentCompositionEvidence target, EquipmentSnapshot snapshot, decimal delta)
        => Advance(target, new DurationPlan(snapshot), delta);

    internal sealed class DurationPlan
    {
        internal CharacterEquipmentSlotSnapshot[] EmptySlots { get; }
        internal (string Key, TotemSnapshot Totem, int Ordinal)[] Totems { get; }

        internal DurationPlan(EquipmentSnapshot snapshot)
        {
            EmptySlots = snapshot.CharacterSlots.Where(s => s.State == EquipmentSlotState.Empty && s.IsDirectTotemSlot).ToArray();
            Totems = snapshot.Totems.GroupBy(StateKey, StringComparer.Ordinal)
                .SelectMany(group => group.Select((totem, index) => (CopyKey(totem, index + 1), totem, index + 1))).ToArray();
        }
    }

    internal static void Advance(EquipmentCompositionEvidence target, DurationPlan plan, decimal delta)
    {
        foreach (var slot in plan.EmptySlots)
            _ = Add(target.EmptyDirectSlots.TryGetValue(slot.SlotId, out var old) ? old.ActiveDurationSeconds : 0, delta);
        foreach (var entry in plan.Totems)
            _ = Add(target.TotemStates.TryGetValue(entry.Key, out var old) ? old.DurationSeconds : 0, delta);
        foreach (var entry in plan.Totems)
        {
            if (target.TotemStates.TryGetValue(entry.Key, out var old))
            {
                old.DurationSeconds = Add(old.DurationSeconds, delta);
                old.Totem.DisplayName = Name(old.Totem.DisplayName, entry.Totem.DisplayName, old.Totem.ItemId);
            }
            else target.TotemStates.Add(entry.Key, new TotemStateDuration
            { Totem = CloneTotem(entry.Totem), CopyOrdinal = entry.Ordinal, DurationSeconds = delta });
        }
        foreach (var slot in plan.EmptySlots)
        {
            if (!target.EmptyDirectSlots.TryGetValue(slot.SlotId, out var row))
                target.EmptyDirectSlots.Add(slot.SlotId, row = new EquipmentDurationAggregate { Id = slot.SlotId });
            row.DisplayName = slot.SlotDisplayName; row.ActiveDurationSeconds = Add(row.ActiveDurationSeconds, delta);
        }
    }

    public static void Merge(EquipmentCompositionEvidence target, EquipmentCompositionEvidence source)
    {
        Validate(target); Validate(source);
        foreach (var pair in source.EmptyDirectSlots)
            _ = Add(target.EmptyDirectSlots.TryGetValue(pair.Key, out var old) ? old.ActiveDurationSeconds : 0, pair.Value.ActiveDurationSeconds);
        foreach (var pair in source.TotemStates)
            _ = Add(target.TotemStates.TryGetValue(pair.Key, out var old) ? old.DurationSeconds : 0, pair.Value.DurationSeconds);
        foreach (var definition in source.Loadouts.Values) Register(target.Loadouts, CloneDefinition(definition));
        foreach (var definition in source.ActiveTotemSets.Values)
            Register(target.ActiveTotemSets, new ActiveTotemSetDefinition
            {
                TotemSetId = definition.TotemSetId,
                Conflicting = definition.Conflicting,
                Members = definition.Members.Select(CloneTotem).ToList()
            });
        foreach (var pair in source.TotemStates)
        {
            if (target.TotemStates.TryGetValue(pair.Key, out var old))
            {
                old.DurationSeconds = Add(old.DurationSeconds, pair.Value.DurationSeconds);
                old.Totem.DisplayName = Name(old.Totem.DisplayName, pair.Value.Totem.DisplayName, old.Totem.ItemId);
            }
            else target.TotemStates.Add(pair.Key, new TotemStateDuration
            {
                Totem = CloneTotem(pair.Value.Totem),
                CopyOrdinal = pair.Value.CopyOrdinal,
                DurationSeconds = pair.Value.DurationSeconds
            });
        }
        target.HistoricalUnavailable |= source.HistoricalUnavailable;
        foreach (var pair in source.EmptyDirectSlots)
        {
            if (!target.EmptyDirectSlots.TryGetValue(pair.Key, out var row))
                target.EmptyDirectSlots.Add(pair.Key, row = new EquipmentDurationAggregate { Id = pair.Key });
            row.DisplayName = Name(row.DisplayName, pair.Value.DisplayName, pair.Key);
            row.ActiveDurationSeconds = Add(row.ActiveDurationSeconds, pair.Value.ActiveDurationSeconds);
        }
    }

    public static EquipmentCompositionEvidence Clone(EquipmentCompositionEvidence source)
    { var result = new EquipmentCompositionEvidence(); Merge(result, source); return result; }

    public static void Validate(EquipmentCompositionEvidence? evidence)
    {
        if (evidence?.Loadouts == null || evidence.ActiveTotemSets == null || evidence.TotemStates == null || evidence.EmptyDirectSlots == null)
            throw new ArgumentException("Structured equipment evidence is missing.");
        foreach (var pair in evidence.EmptyDirectSlots)
            if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Key) || pair.Key != pair.Value.Id
                || pair.Value.ActiveDurationSeconds < 0)
                throw new ArgumentException("Invalid proven-empty direct totem slot duration.");
        foreach (var pair in evidence.Loadouts)
        {
            var d = pair.Value;
            if (d == null || !Exact(d.LoadoutId) || pair.Key != d.LoadoutId || d.Roots == null || d.Items == null || !d.RootsComplete)
                throw new ArgumentException("Invalid loadout definition identity or roots.");
            var snapshot = Snapshot(d);
            EquipmentStatisticsReducer.ValidateSnapshot(snapshot);
            if (d.LoadoutId.StartsWith("duckov:loadout:", StringComparison.Ordinal) && d.LoadoutId != EquipmentIdentity.LoadoutId(d.Items))
                throw new ArgumentException("Loadout definition does not match its exact identity.");
            if (d.Roots.Any(r => r.State == EquipmentSlotState.Occupied && !d.Items.Any(i => i.SlotId == r.SlotId && i.ItemId == r.ItemId && i.Kind == r.ItemKind))
                || d.Items.Any(i => !d.Roots.Any(r => r.State == EquipmentSlotState.Occupied && r.SlotId == i.SlotId && r.ItemId == i.ItemId))
                || d.NestedComplete && d.Items.Any(i => !i.NestedSlotStateComplete))
                throw new ArgumentException("Loadout definition evidence is inconsistent.");
        }
        foreach (var pair in evidence.ActiveTotemSets)
        {
            var d = pair.Value;
            if (d == null || !Exact(d.TotemSetId) || pair.Key != d.TotemSetId || d.Members == null
                || d.Members.Any(t => !ValidTotem(t) || t.ActivationState != TotemActivationState.ProvenActive || !string.IsNullOrEmpty(t.DirectSlotId)))
                throw new ArgumentException("Invalid proven-active totem definition.");
            if (d.TotemSetId.StartsWith("duckov:totem-set:", StringComparison.Ordinal) && d.TotemSetId != EquipmentIdentity.ActiveTotemSetId(d.Members))
                throw new ArgumentException("Totem definition does not match its exact identity.");
        }
        foreach (var pair in evidence.TotemStates)
        {
            var row = pair.Value;
            if (row == null || !ValidTotem(row.Totem) || row.CopyOrdinal < 1 || pair.Key != CopyKey(row.Totem, row.CopyOrdinal)
                || row.DurationSeconds < 0)
                throw new ArgumentException("Invalid typed totem duration evidence.");
        }
    }

    private static bool Register(Dictionary<string, LoadoutDefinition> target, LoadoutDefinition incoming)
    {
        if (!target.TryGetValue(incoming.LoadoutId, out var old)) { target.Add(incoming.LoadoutId, incoming); return true; }
        if (old.Conflicting) return false;
        // Root membership is exact. Nested readable siblings can enrich incomplete observations, but contradictory states cannot.
        if (incoming.Conflicting || RootSignature(old) != RootSignature(incoming)) { old.Conflicting = true; return true; }
        var changed = false;
        foreach (var root in incoming.Roots)
        {
            var previous = old.Roots.Single(r => r.SlotId == root.SlotId);
            var name = Name(previous.ItemDisplayName, root.ItemDisplayName, root.ItemId);
            changed |= name != previous.ItemDisplayName; previous.ItemDisplayName = name;
            name = Name(previous.SlotDisplayName, root.SlotDisplayName, root.SlotId);
            changed |= name != previous.SlotDisplayName; previous.SlotDisplayName = name;
        }
        foreach (var item in incoming.Items)
        {
            var previous = old.Items.Single(i => i.SlotId == item.SlotId);
            if (previous.AttachmentSignature != item.AttachmentSignature
                || previous.NestedSlotStateComplete && item.NestedSlotStateComplete && NestedSignature(previous) != NestedSignature(item))
            { old.Conflicting = true; return true; }
            foreach (var slot in item.NestedSlots)
            {
                var prior = previous.NestedSlots.FirstOrDefault(s => s.Path == slot.Path);
                if (prior == null)
                {
                    if (previous.NestedSlotStateComplete) { old.Conflicting = true; return true; }
                    previous.NestedSlots.Add(slot); changed = true;
                }
                else
                {
                    if (prior.State != slot.State || prior.ItemId != slot.ItemId || prior.SlotKey != slot.SlotKey)
                    { old.Conflicting = true; return true; }
                    var name = Name(prior.ItemDisplayName, slot.ItemDisplayName, slot.ItemId);
                    changed |= name != prior.ItemDisplayName; prior.ItemDisplayName = name;
                    name = Name(prior.SlotDisplayName, slot.SlotDisplayName, slot.SlotKey);
                    changed |= name != prior.SlotDisplayName; prior.SlotDisplayName = name;
                }
            }
            if (item.NestedSlotStateComplete && previous.NestedSlots.Any(s => !item.NestedSlots.Any(n => n.Path == s.Path)))
            { old.Conflicting = true; return true; }
            changed |= item.NestedSlotStateComplete && !previous.NestedSlotStateComplete;
            previous.NestedSlotStateComplete |= item.NestedSlotStateComplete;
            previous.NestedSlots = previous.NestedSlots.OrderBy(s => s.Path, StringComparer.Ordinal).ToList();
            var enriched = Name(previous.ItemDisplayName, item.ItemDisplayName, item.ItemId);
            changed |= enriched != previous.ItemDisplayName; previous.ItemDisplayName = enriched;
            enriched = Name(previous.SlotDisplayName, item.SlotDisplayName, item.SlotId);
            changed |= enriched != previous.SlotDisplayName; previous.SlotDisplayName = enriched;
        }
        old.NestedComplete = old.Items.All(i => i.NestedSlotStateComplete);
        return changed;
    }

    private static bool Register(Dictionary<string, ActiveTotemSetDefinition> target, ActiveTotemSetDefinition incoming)
    {
        if (!target.TryGetValue(incoming.TotemSetId, out var old)) { target.Add(incoming.TotemSetId, incoming); return true; }
        if (old.Conflicting) return false;
        var left = old.Members.OrderBy(MemberKey, StringComparer.Ordinal).ToList();
        var right = incoming.Members.OrderBy(MemberKey, StringComparer.Ordinal).ToList();
        if (incoming.Conflicting || !left.Select(MemberKey).SequenceEqual(right.Select(MemberKey), StringComparer.Ordinal))
        { old.Conflicting = true; return true; }
        var changed = false;
        for (var i = 0; i < left.Count; i++)
        { var name = Name(left[i].DisplayName, right[i].DisplayName, left[i].ItemId); changed |= name != left[i].DisplayName; left[i].DisplayName = name; }
        return changed;
    }

    private static string RootSignature(LoadoutDefinition d) => string.Concat(d.Roots.OrderBy(r => r.SlotId, StringComparer.Ordinal)
        .Select(r => Part(r.SlotId) + Part(r.ItemId) + Part(((int)r.State).ToString(CultureInfo.InvariantCulture))
            + Part(((int)r.ItemKind).ToString(CultureInfo.InvariantCulture)) + Part(r.IsDirectTotemSlot.ToString())));
    private static string NestedSignature(EquippedItemSnapshot i) => string.Concat(i.NestedSlots.OrderBy(s => s.Path, StringComparer.Ordinal)
        .Select(s => Part(s.Path) + Part(s.SlotKey) + Part(s.ItemId) + Part(((int)s.State).ToString(CultureInfo.InvariantCulture))));
    private static EquipmentSnapshot Snapshot(LoadoutDefinition d) => new()
    {
        SnapshotId = "definition",
        LoadoutId = d.LoadoutId,
        TotemSetId = EquipmentEventAssociation.UnavailableId,
        CharacterSlots = d.Roots,
        Items = d.Items,
        CharacterSlotStateComplete = d.RootsComplete,
        NestedSlotStateComplete = d.NestedComplete
    };
    private static LoadoutDefinition CloneDefinition(LoadoutDefinition d)
    {
        var copy = EquipmentStatisticsReducer.CloneSnapshot(Snapshot(d));
        return new LoadoutDefinition
        {
            LoadoutId = d.LoadoutId,
            Roots = copy.CharacterSlots,
            Items = copy.Items,
            RootsComplete = d.RootsComplete,
            NestedComplete = d.NestedComplete,
            Conflicting = d.Conflicting
        };
    }
    private static bool Exact(string id) => !string.IsNullOrWhiteSpace(id) && id != EquipmentEventAssociation.UnavailableId;
    private static bool ValidTotem(TotemSnapshot? t) => t != null && !string.IsNullOrWhiteSpace(t.ItemId)
        && !string.IsNullOrWhiteSpace(t.ContainerId) && t.DisplayName != null && t.DirectSlotId != null
        && Enum.IsDefined(typeof(TotemCarryKind), t.CarryKind) && Enum.IsDefined(typeof(TotemActivationState), t.ActivationState)
        && (t.CarryKind == TotemCarryKind.DirectSlot || t.DirectSlotId.Length == 0);
    private static TotemSnapshot CloneTotem(TotemSnapshot t) => new()
    {
        ItemId = t.ItemId,
        DisplayName = t.DisplayName ?? string.Empty,
        CarryKind = t.CarryKind,
        ContainerId = t.ContainerId,
        DirectSlotId = t.DirectSlotId ?? string.Empty,
        ActivationState = t.ActivationState
    };
    private static string Part(string value) => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    private static string MemberKey(TotemSnapshot t) => Part(((int)t.CarryKind).ToString(CultureInfo.InvariantCulture)) + Part(t.ContainerId) + Part(t.ItemId);
    private static string StateKey(TotemSnapshot t) => MemberKey(t) + Part(t.DirectSlotId ?? string.Empty) + Part(((int)t.ActivationState).ToString(CultureInfo.InvariantCulture));
    private static string CopyKey(TotemSnapshot t, int copy) => StateKey(t) + Part(copy.ToString(CultureInfo.InvariantCulture));
    private static string Name(string old, string next, string id) => !string.IsNullOrWhiteSpace(next) && next != id ? next : old;
    public static double Add(double a, double b)
    {
        var value = a + b;
        if (a < 0 || b < 0 || double.IsNaN(value) || double.IsInfinity(value)) throw new OverflowException("Equipment duration overflow.");
        return value;
    }
}
