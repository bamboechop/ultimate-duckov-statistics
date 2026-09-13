using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public sealed partial class RunLifecycleTracker
{
    private IncrementalCheckpointState? incrementalCheckpoint;

    internal IncrementalCheckpointCapture? CaptureIncrementalCheckpoint(DateTime timestamp, double monotonicSeconds,
        RunOutcome? terminalOutcome, ProfileRecordCodec codec)
    {
        var view = CreateCheckpoint(timestamp, monotonicSeconds, detached: false);
        if (view == null || active == null) return null;
        view.PendingTerminalOutcome = terminalOutcome;
        if (incrementalCheckpoint == null || incrementalCheckpoint.RunId != active.RunId)
            incrementalCheckpoint = new IncrementalCheckpointState(active);
        return incrementalCheckpoint.Capture(view, codec);
    }

    private sealed class IncrementalCheckpointState
    {
        private readonly object gate = new();
        private readonly ActiveState state;
        private readonly Dictionary<string, Scope> scopes = new(StringComparer.Ordinal);
        private readonly HashSet<Scope> dirty = new();
        private readonly Dictionary<int, long> associations = new();
        private int knownSegments, knownAssociations;
        private long associationVersion;
        private bool associationsAcknowledged;
        private List<string> observedContainers;
        private EntryChangeLedger containers;
        internal IncrementalCheckpointState(ActiveState state)
        {
            this.state = state;
            Add(new Scope("", null, () => state.WeaponStatistics, () => state.CombatStatistics,
                () => state.ItemStatistics, () => state.EquipmentStatistics, Changed));
            observedContainers = state.ContainerState.LootedContainerIdentities;
            containers = EntryChanges.Track(observedContainers, observedContainers, () => { });
        }
        internal string RunId => state.RunId;
        private void Add(Scope scope) { scopes.Add(scope.Id, scope); lock (gate) dirty.Add(scope); }
        private void Changed(Scope scope) { lock (gate) { scope.Version = checked(scope.Version + 1); dirty.Add(scope); } }

        internal IncrementalCheckpointCapture Capture(ActiveRunCheckpoint view, ProfileRecordCodec codec)
        {
            while (knownSegments < state.Segments.Count)
            {
                var segment = state.Segments[knownSegments++];
                var scope = new Scope(segment.SegmentId, segment, () => segment.WeaponStatistics,
                    () => segment.CombatStatistics, () => segment.ItemStatistics, () => segment.EquipmentStatistics, Changed);
                Add(scope); EntryChanges.WatchScope(segment, () => Changed(scope));
            }
            while (knownAssociations < state.EventAssociations.Count)
            {
                var index = knownAssociations++; var row = state.EventAssociations[index];
                MarkAssociation(index); EntryChanges.WatchScope(row, () => MarkAssociation(index));
            }
            if (!ReferenceEquals(observedContainers, state.ContainerState.LootedContainerIdentities))
            {
                observedContainers = state.ContainerState.LootedContainerIdentities;
                containers = EntryChanges.Track(observedContainers, observedContainers, () => { });
            }
            Scope[] pending; KeyValuePair<int, long>[] associationMarks; bool replaceAssociations;
            CheckpointScopeCapture[] capturedScopes;
            lock (gate)
            {
                pending = dirty.Where(scope => scope.Id.Length > 0).Prepend(scopes[""]).ToArray();
                associationMarks = associations.ToArray();
                replaceAssociations = !associationsAcknowledged;
                capturedScopes = pending.Select(scope => scope.Capture(codec)).ToArray();
            }
            var capturedAssociations = associationMarks.Select(mark => new KeyValuePair<int, byte[]>(mark.Key, codec.Encode(state.EventAssociations[mark.Key]))).ToArray();
            var containerReceipt = containers.Capture();
            ValidateHeader(view, associationMarks, containerReceipt);
            var capturedContainers = containerReceipt.Entries.Select(mark => new KeyValuePair<string, byte[]?>(mark.Key,
                observedContainers.BinarySearch(mark.Key, StringComparer.Ordinal) >= 0 ? codec.Encode(mark.Key) : null)).ToArray();
            var root = new CheckpointRootRecord
            {
                Header = view,
                SegmentCount = state.Segments.Count,
                AssociationCount = state.EventAssociations.Count,
                ContainerIdentityCount = observedContainers.Count
            };
            view.WeaponStatistics = CheckpointRecordHeaders.Weapon(state.WeaponStatistics);
            view.CombatStatistics = CheckpointRecordHeaders.Combat(state.CombatStatistics);
            view.ItemStatistics = CheckpointRecordHeaders.Items(state.ItemStatistics);
            view.EquipmentStatistics = CheckpointRecordHeaders.Equipment(state.EquipmentStatistics);
            view.ContainerState = CheckpointRecordHeaders.Containers(state.ContainerState);
            view.Segments = new List<MapSegmentSummary>(); view.SegmentEventAssociations = new List<SegmentEventAssociation>();
            ProfileFormat.ValidateRecordMembers(root);
            var bytes = codec.Encode(root);
            return new IncrementalCheckpointCapture(view.SaveGenerationId, view.RunId, bytes, capturedScopes, capturedAssociations,
                replaceAssociations, containerReceipt, capturedContainers, () =>
                {
                    containerReceipt.Owner.Acknowledge(containerReceipt);
                    lock (gate)
                    {
                        for (var index = 0; index < pending.Length; index++)
                        {
                            pending[index].Acknowledge(capturedScopes[index]);
                            if (pending[index].Version <= capturedScopes[index].Version) dirty.Remove(pending[index]);
                        }
                        foreach (var mark in associationMarks)
                            if (associations.TryGetValue(mark.Key, out var current) && current <= mark.Value) associations.Remove(mark.Key);
                        associationsAcknowledged = true;
                    }
                });
        }

        private void MarkAssociation(int index) { lock (gate) associations[index] = checked(++associationVersion); }

        private void ValidateHeader(ActiveRunCheckpoint view, KeyValuePair<int, long>[] associationMarks, EntryChangeCapture containerReceipt)
        {
            if (view.SchemaVersion != ProductInfo.SchemaVersion || view.FormatId != ProductInfo.ProfileFormatId
                || string.IsNullOrWhiteSpace(view.RunId) || view.SaveGenerationId != state.Context.SaveGenerationId
                || view.PendingTerminalOutcome.HasValue && !Enum.IsDefined(typeof(RunOutcome), view.PendingTerminalOutcome.Value))
                throw new ArgumentException("Incremental checkpoint ownership or schema is invalid.");
            view.TerminalLoadout.Validate(view.PendingTerminalOutcome);
            if (state.EventAssociations.Count != state.EventAssociationsByKey.Count
                || state.EventAssociations.Count > RouteStatisticsReducer.EventAssociationFamilyCount * state.Segments.Count * state.Segments.Count)
                throw new ArgumentException("Incremental checkpoint route association ownership is inconsistent.");
            // The append-only private route owns the unique association keys.
            // Validate changed values against the bounded segment/map headers.
            view.SegmentEventAssociations = associationMarks.Select(mark => state.EventAssociations[mark.Key]).ToList();
            ProfileRepository.ValidateCheckpointRouteHeader(view);
            RunReducer.ValidateCheckpointStructure(view.ToCheckpointStructureSummary());
            var containersState = view.ContainerState;
            ContainerStatisticsReducer.ValidateRecoveryCandidate(containersState);
            ContainerStatisticsReducer.ValidateAggregate(containersState.Statistics);
            if (observedContainers.Count > ContainerRunCheckpointState.DeduplicationCapacity
                || containersState.Statistics.UniqueContainersLooted != observedContainers.Count
                || containersState.DeduplicationSaturated && observedContainers.Count != ContainerRunCheckpointState.DeduplicationCapacity
                || containerReceipt.Entries.Any(entry => string.IsNullOrWhiteSpace(entry.Key)))
                throw new ArgumentException("Incremental checkpoint container key set is inconsistent.");
        }

        private sealed class Scope
        {
            internal string Id { get; }
            internal long Version { get; set; } = 1;
            private readonly MapSegmentSummary? segment;
            private readonly Func<EquipmentStatisticsAggregate> equipment;
            private readonly Func<WeaponStatisticsAggregate> weapon;
            private readonly Func<CombatStatisticsAggregate> combat;
            private readonly Func<ItemStatisticsAggregate> items;
            private readonly CheckpointScopeValidation validation = new();
            private readonly IReadOnlyList<CheckpointEntryCollection> collections;
            private bool transitionsAcknowledged;
            private long lastTransition = -1;
            private EquipmentTransition? lastTransitionObject;
            internal Scope(string id, MapSegmentSummary? segment, Func<WeaponStatisticsAggregate> weapon,
                Func<CombatStatisticsAggregate> combat, Func<ItemStatisticsAggregate> items,
                Func<EquipmentStatisticsAggregate> equipment, Action<Scope> changed)
            {
                Id = id; this.segment = segment; this.equipment = equipment;
                this.weapon = weapon; this.combat = combat; this.items = items;
                collections = CheckpointEntryCollection.Bind(weapon, combat, items, equipment, () => changed(this));
            }
            internal CheckpointScopeCapture Capture(ProfileRecordCodec codec)
            {
                var capturedCollections = collections.Select(collection => collection.Capture(codec)).ToArray();
                validation.Validate(weapon(), combat(), items(), equipment(), capturedCollections);
                if (segment != null)
                {
                    ContainerStatisticsReducer.ValidateAggregate(segment.ContainerStatistics);
                    foreach (var collection in capturedCollections.Where(collection => collection.Kind is CheckpointEntryKind.EquipmentItems
                        or CheckpointEntryKind.SelectedWeapons or CheckpointEntryKind.Loadouts or CheckpointEntryKind.TotemSets
                        or CheckpointEntryKind.TotemStates or CheckpointEntryKind.Slots or CheckpointEntryKind.SlottedWeapons))
                        foreach (var entry in collection.Entries.Where(entry => entry.Value != null))
                            if (ProfileRecordCodec.Decode<EquipmentDurationAggregate>(entry.Value!).RunOccurrences != 0)
                                throw new ArgumentException("Checkpoint segment equipment cannot contain completed-run occurrences.");
                }
                var current = equipment(); var count = current.Transitions.Count;
                var first = current.TransitionCount - count;
                if (first < 0) throw new ArgumentException("Equipment transition count cannot precede retained transitions.");
                var tail = count == 0 ? null : current.Transitions[count - 1];
                var replace = !transitionsAcknowledged || current.TransitionCount == long.MaxValue && !ReferenceEquals(tail, lastTransitionObject);
                var entries = new List<KeyValuePair<long, byte[]>>();
                for (var index = replace ? 0 : (int)Math.Min(count, Math.Max(0, lastTransition - first + 1)); index < count; index++)
                    entries.Add(new KeyValuePair<long, byte[]>(first + index, codec.Encode(current.Transitions[index])));
                return new CheckpointScopeCapture(Id, Version, segment == null ? null : codec.Encode(CheckpointRecordHeaders.Segment(segment)),
                    capturedCollections, replace, count, first,
                    current.TransitionCount - 1, tail, entries);
            }
            internal void Acknowledge(CheckpointScopeCapture captured)
            {
                foreach (var collection in captured.Collections) collection.Receipt.Owner.Acknowledge(collection.Receipt);
                if (captured.LastTransition >= lastTransition)
                { lastTransition = captured.LastTransition; lastTransitionObject = captured.LastTransitionObject; transitionsAcknowledged = true; }
            }
        }
    }
}
