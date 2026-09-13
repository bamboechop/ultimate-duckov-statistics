using System.Numerics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

// This proof belongs to one private tracker scope. It indexes identities and
// pairing counts, never the validity of caller-supplied mutable objects. Changed
// values are validated on every capture; replacement collections rebuild their
// affected indexes. A failed proof discards all indexes before another attempt.
internal sealed class CheckpointScopeValidation
{
    private readonly Dictionary<string, (string Weapon, string Ammo, long Count)> pairs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> weapons = new(StringComparer.Ordinal), ammunition = new(StringComparer.Ordinal);
    private readonly ParentIndex character = new(), nested = new();
    private readonly SortedSet<string> equipmentItems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> nestedByEquipmentPrefix = new(StringComparer.Ordinal);
    private BigInteger pairTotal;
    private AdapterCapabilityState? pairing;
    private bool initialized;
    private ItemStatisticsAggregate? itemComposition;

    internal void Validate(WeaponStatisticsAggregate weapon, CombatStatisticsAggregate combat, ItemStatisticsAggregate items,
        EquipmentStatisticsAggregate equipment, IReadOnlyList<CheckpointCollectionCapture> captures)
    {
        try
        {
            WeaponStatisticsReducer.ValidateCheckpointHeader(weapon);
            CombatStatisticsReducer.ValidateAggregate(CheckpointRecordHeaders.Combat(combat));
            CombatStatisticsReducer.ValidatePlayerKills(CheckpointRecordHeaders.Combat(combat));
            ItemStatisticsAggregateReducer.Validate(CheckpointRecordHeaders.Items(items));
            // The bounded snapshot, capability and transition checks use the
            // existing contract. Growing families are validated below/by entry.
            var equipmentHeader = CheckpointRecordHeaders.Equipment(equipment);
            equipmentHeader.Transitions = equipment.Transitions;
            EquipmentStatisticsReducer.ValidateRecoveryCandidate(equipmentHeader);
            var changed = captures.ToDictionary(value => value.Kind);
            var itemChanges = changed[CheckpointEntryKind.Items];
            if (!initialized || itemComposition == null || itemChanges.Receipt.ReplacesCollection || itemChanges.Entries.Count > 0)
                itemComposition = ItemStatisticsAggregateReducer.CaptureCheckpointComposition(items);
            ItemStatisticsAggregateReducer.ValidateCheckpointComposition(items, itemComposition);
            ValidateWeapon(weapon, changed);
            ValidateEquipment(equipment, changed);
            initialized = true;
        }
        catch { initialized = false; throw; }
    }

    private void ValidateWeapon(WeaponStatisticsAggregate source, Dictionary<CheckpointEntryKind, CheckpointCollectionCapture> changes)
    {
        var affectedWeapons = new HashSet<string>(StringComparer.Ordinal);
        var affectedAmmo = new HashSet<string>(StringComparer.Ordinal);
        var pairChanges = changes[CheckpointEntryKind.WeaponAmmunitionPairs];
        if (!initialized || pairChanges.Receipt.ReplacesCollection)
        {
            pairs.Clear(); weapons.Clear(); ammunition.Clear(); pairTotal = BigInteger.Zero;
            foreach (var entry in source.WeaponAmmunitionPairs) AddPair(entry.Key, entry.Value);
            affectedWeapons.UnionWith(source.Weapons.Keys); affectedAmmo.UnionWith(source.AmmunitionTypes.Keys);
        }
        else foreach (var entry in pairChanges.Entries)
            {
                if (pairs.TryGetValue(entry.Key, out var old))
                {
                    affectedWeapons.Add(old.Weapon); affectedAmmo.Add(old.Ammo);
                    weapons[old.Weapon].Remove(entry.Key); ammunition[old.Ammo].Remove(entry.Key);
                    pairTotal -= old.Count; pairs.Remove(entry.Key);
                }
                if (!source.WeaponAmmunitionPairs.TryGetValue(entry.Key, out var next)) continue;
                AddPair(entry.Key, next); affectedWeapons.Add(next.WeaponId); affectedAmmo.Add(next.AmmunitionId);
            }
        foreach (var kind in new[] { CheckpointEntryKind.Weapons, CheckpointEntryKind.UncorrelatedWeapons })
            affectedWeapons.UnionWith(changes[kind].Entries.Select(value => value.Key));
        foreach (var kind in new[] { CheckpointEntryKind.Ammunition, CheckpointEntryKind.UncorrelatedAmmunition })
            affectedAmmo.UnionWith(changes[kind].Entries.Select(value => value.Key));
        if (pairing != source.Capabilities.WeaponAmmunitionPairing.State
            || changes[CheckpointEntryKind.Weapons].Receipt.ReplacesCollection
            || changes[CheckpointEntryKind.Ammunition].Receipt.ReplacesCollection
            || changes[CheckpointEntryKind.UncorrelatedWeapons].Receipt.ReplacesCollection
            || changes[CheckpointEntryKind.UncorrelatedAmmunition].Receipt.ReplacesCollection)
        {
            affectedWeapons.UnionWith(source.Weapons.Keys); affectedWeapons.UnionWith(weapons.Keys);
            affectedWeapons.UnionWith(source.UncorrelatedWeaponFiringActions.Keys);
            affectedAmmo.UnionWith(source.AmmunitionTypes.Keys); affectedAmmo.UnionWith(ammunition.Keys);
            affectedAmmo.UnionWith(source.UncorrelatedAmmunitionFiringActions.Keys);
        }
        var state = source.Capabilities.WeaponAmmunitionPairing.State;
        if (pairTotal < 0 || pairTotal > long.MaxValue) throw new OverflowException("Checkpoint pair count overflowed.");
        WeaponStatisticsReducer.ValidateCheckpointPairing(source.Totals.FiringActions, (long)pairTotal, source.UncorrelatedFiringActions, state);
        foreach (var key in affectedWeapons)
        {
            var paired = SumPairs(weapons, key); var uncorrelated = source.UncorrelatedWeaponFiringActions.GetValueOrDefault(key);
            if (!source.Weapons.TryGetValue(key, out var row))
            { if (HasChildren(weapons, key) || source.UncorrelatedWeaponFiringActions.ContainsKey(key)) throw new ArgumentException("Checkpoint pair has no weapon owner."); continue; }
            WeaponStatisticsReducer.ValidateCheckpointPairing(row.Totals.FiringActions, paired, uncorrelated, state);
        }
        foreach (var key in affectedAmmo)
        {
            var paired = SumPairs(ammunition, key); var uncorrelated = source.UncorrelatedAmmunitionFiringActions.GetValueOrDefault(key);
            if (!source.AmmunitionTypes.TryGetValue(key, out var row))
            { if (HasChildren(ammunition, key) || source.UncorrelatedAmmunitionFiringActions.ContainsKey(key)) throw new ArgumentException("Checkpoint pair has no ammunition owner."); continue; }
            WeaponStatisticsReducer.ValidateCheckpointPairing(row.Totals.FiringActions, paired, uncorrelated, state);
        }
        pairing = state;
    }

    private void AddPair(string key, WeaponAmmunitionPairAggregate pair)
    {
        pairs.Add(key, (pair.WeaponId, pair.AmmunitionId, pair.FiringActions)); pairTotal += pair.FiringActions;
        AddChild(weapons, pair.WeaponId, key); AddChild(ammunition, pair.AmmunitionId, key);
    }
    private long SumPairs(Dictionary<string, HashSet<string>> index, string key)
    {
        var result = 0L;
        if (index.TryGetValue(key, out var children)) foreach (var child in children) result = checked(result + pairs[child].Count);
        return result;
    }
    private static bool HasChildren(Dictionary<string, HashSet<string>> index, string key) => index.TryGetValue(key, out var children) && children.Count > 0;
    private static void AddChild(Dictionary<string, HashSet<string>> index, string parent, string key)
    { if (!index.TryGetValue(parent, out var children)) index.Add(parent, children = new(StringComparer.Ordinal)); children.Add(key); }

    private void ValidateEquipment(EquipmentStatisticsAggregate source, Dictionary<CheckpointEntryKind, CheckpointCollectionCapture> changes)
    {
        var characterChanges = changes[CheckpointEntryKind.CharacterSlotStates]; var nestedChanges = changes[CheckpointEntryKind.NestedSlotStates];
        var affectedCharacter = character.Update(source.CharacterSlotStates, characterChanges, !initialized, EquipmentStatisticsReducer.CheckpointCharacterObservation);
        var affectedNested = nested.Update(source.NestedSlotStates, nestedChanges, !initialized, EquipmentStatisticsReducer.CheckpointNestedObservation);
        if (!initialized || nestedChanges.Receipt.ReplacesCollection)
        {
            nestedByEquipmentPrefix.Clear();
            foreach (var row in source.NestedSlotStates.Values) IndexNestedParent(row);
        }
        else foreach (var entry in nestedChanges.Entries)
                if (source.NestedSlotStates.TryGetValue(entry.Key, out var row)) IndexNestedParent(row);
        AddObservations(affectedCharacter, source.CharacterSlotObservedDurations, changes[CheckpointEntryKind.CharacterSlotObserved], !initialized);
        AddObservations(affectedNested, source.NestedSlotObservedDurations, changes[CheckpointEntryKind.NestedSlotObserved], !initialized);
        var itemChanges = changes[CheckpointEntryKind.EquipmentItems];
        if (!initialized || itemChanges.Receipt.ReplacesCollection)
        { equipmentItems.Clear(); equipmentItems.UnionWith(source.Items.Keys); affectedNested.UnionWith(nested.Parents); }
        else
        {
            foreach (var entry in itemChanges.Entries)
            { if (source.Items.ContainsKey(entry.Key)) equipmentItems.Add(entry.Key); else equipmentItems.Remove(entry.Key); }
            // An equipment duration can affect nested observations even when
            // their own value did not change. Parent keys come from validated
            // nested rows, preserving the existing prefix matching semantics.
            foreach (var entry in itemChanges.Entries)
            {
                for (var index = entry.Key.IndexOf('|'); index >= 0; index = entry.Key.IndexOf('|', index + 1))
                    if (nestedByEquipmentPrefix.TryGetValue(entry.Key.Substring(0, index + 1), out var parents)) affectedNested.UnionWith(parents);
            }
        }
        var affected = new EquipmentStatisticsAggregate();
        foreach (var parent in affectedCharacter)
        {
            if (source.CharacterSlotObservedDurations.TryGetValue(parent, out var observed)) affected.CharacterSlotObservedDurations.Add(parent, observed);
            foreach (var key in character.Children(parent)) affected.CharacterSlotStates.Add(key, source.CharacterSlotStates[key]);
        }
        foreach (var parent in affectedNested)
        {
            if (source.NestedSlotObservedDurations.TryGetValue(parent, out var observed)) affected.NestedSlotObservedDurations.Add(parent, observed);
            foreach (var key in nested.Children(parent))
            {
                var row = source.NestedSlotStates[key]; affected.NestedSlotStates.Add(key, row);
                var prefix = row.ParentSlotId + "|" + row.ParentItemId + "|";
                // Prefix ends in '|'; its exclusive ordinal successor is '}'.
                var upper = prefix.Substring(0, prefix.Length - 1) + "}";
                foreach (var item in equipmentItems.GetViewBetween(prefix, upper))
                    if (item.StartsWith(prefix, StringComparison.Ordinal)) affected.Items[item] = source.Items[item];
            }
        }
        EquipmentStatisticsReducer.ValidateCheckpointSlotFanOut(affected);
    }
    private void IndexNestedParent(NestedSlotStateDurationAggregate row) => AddChild(nestedByEquipmentPrefix,
        row.ParentSlotId + "|" + row.ParentItemId + "|", EquipmentStatisticsReducer.CheckpointNestedObservation(row));
    private static void AddObservations(HashSet<string> affected, Dictionary<string, EquipmentDurationAggregate> source, CheckpointCollectionCapture changes, bool reset)
    {
        if (reset || changes.Receipt.ReplacesCollection) affected.UnionWith(source.Keys);
        affected.UnionWith(changes.Entries.Select(entry => entry.Key));
    }

    private sealed class ParentIndex
    {
        private readonly Dictionary<string, string> parents = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> children = new(StringComparer.Ordinal);
        internal IEnumerable<string> Parents => children.Keys;
        internal IEnumerable<string> Children(string parent) => children.TryGetValue(parent, out var found) ? found : Enumerable.Empty<string>();
        internal HashSet<string> Update<T>(Dictionary<string, T> source, CheckpointCollectionCapture changes, bool reset, Func<T, string> parent)
        {
            var affected = new HashSet<string>(StringComparer.Ordinal);
            if (reset || changes.Receipt.ReplacesCollection)
            {
                affected.UnionWith(children.Keys); parents.Clear(); children.Clear();
                foreach (var entry in source) { var owner = parent(entry.Value); parents.Add(entry.Key, owner); AddChild(children, owner, entry.Key); affected.Add(owner); }
            }
            else foreach (var entry in changes.Entries)
                {
                    if (parents.TryGetValue(entry.Key, out var old)) { children[old].Remove(entry.Key); parents.Remove(entry.Key); affected.Add(old); }
                    if (!source.TryGetValue(entry.Key, out var row)) continue;
                    var owner = parent(row); parents.Add(entry.Key, owner); AddChild(children, owner, entry.Key); affected.Add(owner);
                }
            return affected;
        }
    }
}
