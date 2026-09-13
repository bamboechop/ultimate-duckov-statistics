using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

[DataContract]
internal sealed class CheckpointRootRecord
{
    [DataMember(Order = 1, IsRequired = true)] public ActiveRunCheckpoint Header { get; set; } = new();
    [DataMember(Order = 2, IsRequired = true)] public int SegmentCount { get; set; }
    [DataMember(Order = 3, IsRequired = true)] public int AssociationCount { get; set; }
    [DataMember(Order = 4, IsRequired = true)] public int ContainerIdentityCount { get; set; }
}

internal sealed class IncrementalCheckpointCapture
{
    internal IncrementalCheckpointCapture(string generation, string runId, byte[] header, IReadOnlyList<CheckpointScopeCapture> scopes,
        IReadOnlyList<KeyValuePair<int, byte[]>> associations, bool replaceAssociations, EntryChangeCapture containerReceipt,
        IReadOnlyList<KeyValuePair<string, byte[]?>> containers, Action acknowledge)
    { Generation = generation; RunId = runId; Header = header; Scopes = scopes; Associations = associations; ReplaceAssociations = replaceAssociations; ContainerReceipt = containerReceipt; Containers = containers; this.acknowledge = acknowledge; }
    internal string Generation { get; }
    internal string RunId { get; }
    internal byte[] Header { get; }
    internal IReadOnlyList<CheckpointScopeCapture> Scopes { get; }
    internal IReadOnlyList<KeyValuePair<int, byte[]>> Associations { get; }
    internal bool ReplaceAssociations { get; }
    internal EntryChangeCapture ContainerReceipt { get; }
    internal IReadOnlyList<KeyValuePair<string, byte[]?>> Containers { get; }
    private readonly Action acknowledge;
    internal void Acknowledge() => acknowledge();
}

internal sealed class CheckpointScopeCapture
{
    internal CheckpointScopeCapture(string scope, long version, byte[]? header, IReadOnlyList<CheckpointCollectionCapture> collections,
        bool replaceTransitions, int transitionCount, long firstTransition, long lastTransition, EquipmentTransition? lastTransitionObject,
        IReadOnlyList<KeyValuePair<long, byte[]>> transitions)
    {
        Scope = scope; Version = version; Header = header; Collections = collections; ReplaceTransitions = replaceTransitions;
        TransitionCount = transitionCount; FirstTransition = firstTransition; LastTransition = lastTransition;
        LastTransitionObject = lastTransitionObject; Transitions = transitions;
    }
    internal string Scope { get; }
    internal long Version { get; }
    internal byte[]? Header { get; }
    internal IReadOnlyList<CheckpointCollectionCapture> Collections { get; }
    internal bool ReplaceTransitions { get; }
    internal int TransitionCount { get; }
    internal long FirstTransition { get; }
    internal long LastTransition { get; }
    internal EquipmentTransition? LastTransitionObject { get; }
    internal IReadOnlyList<KeyValuePair<long, byte[]>> Transitions { get; }
}
