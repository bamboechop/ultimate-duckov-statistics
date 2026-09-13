using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

internal sealed class CheckpointRecordAssembler
{
    private CheckpointRootRecord? root;
    private readonly Dictionary<string, MapSegmentSummary> segments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<CheckpointEntryCollection>> collections = new(StringComparer.Ordinal);
    private readonly Dictionary<(string Scope, CheckpointEntryKind Kind), CheckpointCollectionState> expected = new();
    private readonly Dictionary<string, SortedDictionary<long, EquipmentTransition>> transitions = new(StringComparer.Ordinal);
    private readonly SortedDictionary<int, SegmentEventAssociation> associations = new();
    private readonly SortedSet<string> containers = new(StringComparer.Ordinal);
    internal void Read(ProfileRecordChange record)
    {
        if (record.Bytes == null) throw new InvalidDataException("A retained checkpoint record has a null payload.");
        var address = record.Address;
        if (address.Kind == ProfileRecordKind.CheckpointRoot)
        {
            if (root != null) throw new InvalidDataException("Checkpoint root is duplicated.");
            root = ProfileRecordCodec.Decode<CheckpointRootRecord>(record.Bytes);
            ProfileFormat.ValidateRecordMembers(root);
            if (root.Header.Segments.Count != 0 || root.Header.SegmentEventAssociations.Count != 0 || root.Header.ContainerState.LootedContainerIdentities.Count != 0)
                throw new InvalidDataException("Incremental checkpoint root contains inline history.");
            RequireEmptyCollections("", root.Header.WeaponStatistics, root.Header.CombatStatistics, root.Header.ItemStatistics, root.Header.EquipmentStatistics);
            return;
        }
        if (root == null) throw new InvalidDataException("Checkpoint records have no owner header.");
        if (address.Kind == ProfileRecordKind.CheckpointSegment)
        {
            var segment = ProfileRecordCodec.Decode<MapSegmentSummary>(record.Bytes);
            ProfileFormat.ValidateRecordMembers(segment);
            if (segment.SegmentId != address.First || string.IsNullOrWhiteSpace(segment.SegmentId)) throw new InvalidDataException("Checkpoint segment identity is invalid.");
            RequireEmptyCollections(address.First, segment.WeaponStatistics, segment.CombatStatistics, segment.ItemStatistics, segment.EquipmentStatistics);
            segments.Add(address.First, segment); return;
        }
        var kind = CheckpointRecordChanges.Kind(address.Second);
        if (address.Kind == ProfileRecordKind.CheckpointCollection)
        {
            var state = ProfileRecordCodec.Decode<CheckpointCollectionState>(record.Bytes);
            if (state.Count < 0 || state.MinimumSequence < 0 || state.MinimumSequence.HasValue && kind != CheckpointEntryKind.EquipmentTransitions)
                throw new InvalidDataException("Checkpoint collection count is invalid.");
            expected.Add((address.First, kind), state); return;
        }
        if (address.Kind != ProfileRecordKind.CheckpointEntry) throw new InvalidDataException("Checkpoint record kind is invalid.");
        switch (kind)
        {
            case CheckpointEntryKind.EquipmentTransitions:
                if (!long.TryParse(address.Third, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence)) throw new InvalidDataException("Transition sequence is invalid.");
                if (!transitions.TryGetValue(address.First, out var values)) transitions.Add(address.First, values = new());
                values.Add(sequence, ProfileRecordCodec.Decode<EquipmentTransition>(record.Bytes)); break;
            case CheckpointEntryKind.RouteAssociations:
                if (address.First.Length != 0 || !int.TryParse(address.Third, NumberStyles.None, CultureInfo.InvariantCulture, out var index)) throw new InvalidDataException("Route association sequence is invalid.");
                associations.Add(index, ProfileRecordCodec.Decode<SegmentEventAssociation>(record.Bytes)); break;
            case CheckpointEntryKind.ContainerIdentities:
                var identity = ProfileRecordCodec.Decode<string>(record.Bytes);
                if (address.First.Length != 0 || identity != address.Third || !containers.Add(identity)) throw new InvalidDataException("Container identity is inconsistent.");
                break;
            default: Collections(address.First).Single(value => value.Kind == kind).Restore(address.Third, record.Bytes); break;
        }
    }

    internal ActiveRunCheckpoint? Finish(string generation)
    {
        if (root == null) return null;
        if (root.Header.SaveGenerationId != generation || root.SegmentCount != segments.Count || root.AssociationCount != associations.Count
            || root.ContainerIdentityCount != containers.Count) throw new InvalidDataException("Checkpoint ownership or retained counts disagree.");
        if (expected.Count != 30 * (segments.Count + 1) + 2) throw new InvalidDataException("Checkpoint has orphan completeness metadata.");
        root.Header.Segments = segments.Values.OrderBy(segment => segment.SegmentIndex).ToList();
        if (associations.Keys.Where((key, index) => key != index).Any()) throw new InvalidDataException("Checkpoint route associations have a sequence gap.");
        root.Header.SegmentEventAssociations = associations.Values.ToList(); root.Header.ContainerState.LootedContainerIdentities = containers.ToList();
        foreach (var pair in transitions)
        {
            var equipment = pair.Key.Length == 0 ? root.Header.EquipmentStatistics : segments[pair.Key].EquipmentStatistics;
            equipment.Transitions = pair.Value.Values.ToList();
        }
        foreach (var scope in segments.Keys.Prepend(""))
        {
            foreach (var collection in Collections(scope)) CheckCount(scope, collection.Kind, collection.Count);
            CheckCount(scope, CheckpointEntryKind.EquipmentTransitions, transitions.TryGetValue(scope, out var rows) ? rows.Count : 0);
            var equipment = scope.Length == 0 ? root.Header.EquipmentStatistics : segments[scope].EquipmentStatistics;
            var first = equipment.TransitionCount - equipment.Transitions.Count;
            if (first < 0 || expected[(scope, CheckpointEntryKind.EquipmentTransitions)].MinimumSequence != first
                || rows != null && rows.Keys.Where((sequence, index) => sequence != first + index).Any())
                throw new InvalidDataException("Checkpoint transition window contains a sequence gap.");
        }
        CheckCount("", CheckpointEntryKind.RouteAssociations, associations.Count); CheckCount("", CheckpointEntryKind.ContainerIdentities, containers.Count);
        return root.Header;
    }
    private void CheckCount(string scope, CheckpointEntryKind kind, int count)
    { if (!expected.TryGetValue((scope, kind), out var state) || state.Count != count) throw new InvalidDataException("Checkpoint collection completeness cannot be proven."); }
    private void RequireEmptyCollections(string scope, WeaponStatisticsAggregate weapon, CombatStatisticsAggregate combat, ItemStatisticsAggregate items, EquipmentStatisticsAggregate equipment)
    {
        var bound = CheckpointEntryCollection.Bind(() => weapon, () => combat, () => items, () => equipment, () => { });
        if (equipment.Transitions.Count != 0 || bound.Any(collection => collection.Count != 0))
            throw new InvalidDataException("Checkpoint header contains inline metric entries.");
        collections.Add(scope, bound);
    }
    private IReadOnlyList<CheckpointEntryCollection> Collections(string scope)
    {
        if (collections.TryGetValue(scope, out var existing)) return existing;
        var header = root!.Header;
        var result = scope.Length == 0 ? CheckpointEntryCollection.Bind(() => header.WeaponStatistics, () => header.CombatStatistics, () => header.ItemStatistics, () => header.EquipmentStatistics, () => { })
            : segments.TryGetValue(scope, out var segment) ? CheckpointEntryCollection.Bind(() => segment.WeaponStatistics, () => segment.CombatStatistics, () => segment.ItemStatistics, () => segment.EquipmentStatistics, () => { })
            : throw new InvalidDataException("Checkpoint entries have an unknown segment owner.");
        collections.Add(scope, result); return result;
    }
}
