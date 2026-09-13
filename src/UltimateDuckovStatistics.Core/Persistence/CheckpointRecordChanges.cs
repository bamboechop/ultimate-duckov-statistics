using System.Globalization;
using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Persistence;

[DataContract]
internal sealed class CheckpointCollectionState
{
    [DataMember(Order = 1, IsRequired = true)] public int Count { get; set; }
    [DataMember(Order = 2, IsRequired = true)] public bool Replace { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public long? MinimumSequence { get; set; }
}

internal static class CheckpointRecordChanges
{
    internal static IEnumerable<ProfileRecordChange> From(IncrementalCheckpointCapture captured, ProfileRecordCodec codec)
    {
        var root = ProfileRecordCodec.Decode<CheckpointRootRecord>(captured.Header);
        yield return new ProfileRecordChange(new ProfileRecordAddress(ProfileRecordKind.CheckpointRoot), 0, captured.Header);
        foreach (var scope in captured.Scopes)
        {
            if (scope.Header != null) yield return new ProfileRecordChange(new ProfileRecordAddress(ProfileRecordKind.CheckpointSegment, scope.Scope), 0, scope.Header);
            foreach (var collection in scope.Collections)
            {
                if (!collection.Receipt.ReplacesCollection && collection.Entries.Count == 0) continue;
                yield return Collection(scope.Scope, collection.Kind, collection.Count, collection.Receipt.ReplacesCollection, null, codec);
                foreach (var entry in collection.Entries) yield return Entry(scope.Scope, collection.Kind, entry.Key, entry.Value);
            }
            yield return Collection(scope.Scope, CheckpointEntryKind.EquipmentTransitions, scope.TransitionCount, scope.ReplaceTransitions, scope.FirstTransition, codec);
            foreach (var entry in scope.Transitions) yield return Entry(scope.Scope, CheckpointEntryKind.EquipmentTransitions, entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value);
        }
        yield return Collection("", CheckpointEntryKind.RouteAssociations, root.AssociationCount, captured.ReplaceAssociations, null, codec);
        foreach (var entry in captured.Associations) yield return Entry("", CheckpointEntryKind.RouteAssociations, entry.Key.ToString(CultureInfo.InvariantCulture), entry.Value);
        yield return Collection("", CheckpointEntryKind.ContainerIdentities, root.ContainerIdentityCount, captured.ContainerReceipt.ReplacesCollection, null, codec);
        foreach (var entry in captured.Containers) yield return Entry("", CheckpointEntryKind.ContainerIdentities, entry.Key, entry.Value);
    }
    private static ProfileRecordChange Collection(string scope, CheckpointEntryKind kind, int count, bool replace, long? minimumSequence, ProfileRecordCodec codec) =>
        new(new ProfileRecordAddress(ProfileRecordKind.CheckpointCollection, scope, ((int)kind).ToString(CultureInfo.InvariantCulture)), 0,
            codec.Encode(new CheckpointCollectionState { Count = count, Replace = replace, MinimumSequence = minimumSequence }));
    private static ProfileRecordChange Entry(string scope, CheckpointEntryKind kind, string key, byte[]? bytes) =>
        new(new ProfileRecordAddress(ProfileRecordKind.CheckpointEntry, scope, ((int)kind).ToString(CultureInfo.InvariantCulture), key), 0, bytes);
    internal static CheckpointEntryKind Kind(string value)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var kind)
            || !Enum.IsDefined(typeof(CheckpointEntryKind), kind)) throw new InvalidDataException("Checkpoint collection kind is invalid.");
        return (CheckpointEntryKind)kind;
    }
}
