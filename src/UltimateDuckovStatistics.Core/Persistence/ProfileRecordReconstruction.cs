using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Persistence;

internal static class ProfileRecordReconstruction
{
    // A recovery/import/export operation, never a routine write. Input ordering is
    // explicit so retained run order and dictionary insertion order survive.
    internal static IncrementalProfileState Read(IEnumerable<ProfileRecordChange> records)
    {
        ProfileDocument? profile = null;
        SessionCheckpoint? session = null;
        ActiveRunCheckpoint? checkpoint = null;
        var checkpointRecords = new CheckpointRecordAssembler();
        var runCollections = new Dictionary<(string Scope, CheckpointEntryKind Kind), int>();
        foreach (var record in records)
        {
            if (record.Bytes == null) continue;
            var key = record.Address;
            if (key.Kind >= ProfileRecordKind.CheckpointRoot && key.Kind <= ProfileRecordKind.CheckpointEntry)
            { checkpointRecords.Read(record); continue; }
            if (key.Kind == ProfileRecordKind.RunMetricEntry)
            {
                if (profile == null) throw new InvalidDataException("Maintained run entries have no profile owner.");
                var kind = CheckpointRecordChanges.Kind(key.Second);
                var entry = ProfileRecordCodec.Decode(record.Bytes, RunMetricRecords.EntryType(kind)) ?? throw new InvalidDataException("Maintained run entry is null.");
                CheckpointEntryContracts.Validate(kind, key.Third, entry);
                RunMetricRecords.Scope(profile, key.First).Dictionary(kind).Add(key.Third, entry);
                continue;
            }
            var value = ProfileRecordCodec.Decode(record.Bytes, ProfileRecordCapture.RecordType(key.Kind))
                ?? throw new InvalidDataException("A persisted record contained a null root.");
            if (key.Kind == ProfileRecordKind.Metadata) { profile = ((ProfileMetadataRecord)value).ToProfile(); continue; }
            if (profile == null) throw new InvalidDataException("Profile metadata must precede its records.");
            var stats = profile.Statistics;
            switch (key.Kind)
            {
                case ProfileRecordKind.Statistics:
                    var header = (StatisticsMetadataRecord)value;
                    stats.SchemaVersion = header.SchemaVersion; stats.SaveGenerationId = header.SaveGenerationId;
                    stats.CreatedUtc = header.CreatedUtc; stats.UpdatedUtc = header.UpdatedUtc;
                    stats.Overall = header.Overall; stats.RecentEventIds = header.RecentEventIds; stats.HealingCaptureComplete = header.HealingCaptureComplete;
                    break;
                case ProfileRecordKind.BaseMovement: stats.BaseMovement = (BaseMovementStatistics)value; break;
                case ProfileRecordKind.WorldTime: stats.WorldTime = (WorldTimeStatisticsAggregate)value; break;
                case ProfileRecordKind.Holdings: stats.Holdings = (EconomyHoldingsSnapshot)value; break;
                case ProfileRecordKind.Economy: stats.Economy = (EconomyStatisticsAggregate)value; break;
                case ProfileRecordKind.Item: stats.Items.Add(key.First, (ItemAggregate)value); break;
                case ProfileRecordKind.Groups: stats.Groups = (Dictionary<string, AggregateTotals>)value; break;
                case ProfileRecordKind.Crafting: stats.Crafting = (CraftingStatisticsAggregate)value; break;
                case ProfileRecordKind.CraftingOutput: stats.Crafting.Outputs.Add(key.First, (CraftedOutputAggregate)value); break;
                case ProfileRecordKind.CraftingRecipe: stats.Crafting.Outputs[key.First].Recipes.Add(key.Second, (CraftingRecipeAggregate)value); break;
                case ProfileRecordKind.CraftingBatch: stats.Crafting.Outputs[key.First].Recipes[key.Second].BatchActions.Add(key.Third, (long)value); break;
                case ProfileRecordKind.CraftingResource: stats.Crafting.Resources.Add(key.First, (CraftingResourceAggregate)value); break;
                case ProfileRecordKind.CraftingAssociation: stats.Crafting.Outputs[key.First].Recipes[key.Second].Resources.Add(key.Third, (CraftingResourceAssociationAggregate)value); break;
                case ProfileRecordKind.RunTotals: stats.RunTotals = (RunAggregateTotals)value; break;
                case ProfileRecordKind.RunRecords: stats.RunRecords = (RunDurationRecords)value; break;
                case ProfileRecordKind.RunMap: stats.RunTotals.Maps.Add(key.First, (MapRunAggregate)value); break;
                case ProfileRecordKind.RouteMap: stats.RunTotals.RouteMaps.Add(key.First, (RouteAwareMapAggregate)value); break;
                case ProfileRecordKind.MapRunRecords: stats.RunRecords.Maps.Add(key.First, (MapRunDurationRecords)value); break;
                case ProfileRecordKind.RunMetricCollection:
                    var collection = (CheckpointCollectionState)value;
                    if (collection.Count < 0 || collection.MinimumSequence.HasValue) throw new InvalidDataException("Maintained run collection count is invalid.");
                    runCollections.Add((key.First, CheckpointRecordChanges.Kind(key.Second)), collection.Count); break;
                case ProfileRecordKind.CompletedRun: stats.Runs.Add((RunSummary)value); break;
                case ProfileRecordKind.DeferredHeader:
                    var watermark = (DeferredMetadataRecord)value;
                    profile.DeferredItemPersistence = new DeferredItemPersistenceState { RunId = watermark.RunId };
                    profile.DeferredItemPersistence.AppliedLifetimeStatistics.Overall = watermark.Overall;
                    profile.DeferredItemPersistence.AppliedLifetimeStatistics.RecentEventIds = watermark.RecentEventIds;
                    profile.DeferredItemPersistence.AppliedLifetimeStatistics.WasRepairedFromInvalidState = watermark.WasRepairedFromInvalidState;
                    break;
                case ProfileRecordKind.DeferredItem: RequireDeferred(profile).AppliedLifetimeStatistics.Items.Add(key.First, (ItemAggregate)value); break;
                case ProfileRecordKind.DeferredGroups: RequireDeferred(profile).AppliedLifetimeStatistics.Groups = (Dictionary<string, AggregateTotals>)value; break;
                case ProfileRecordKind.DeferredEconomy: RequireDeferred(profile).AppliedLifetimeEconomy = (EconomyStatisticsAggregate)value; break;
                case ProfileRecordKind.Session: session = (SessionCheckpoint)value; break;
                case ProfileRecordKind.ActiveCheckpoint: checkpoint = (ActiveRunCheckpoint)value; break;
                default: throw new InvalidDataException("Unsupported profile record.");
            }
        }
        if (profile == null) throw new InvalidDataException("Profile metadata is missing.");
        var expectedCollections = 0;
        foreach (var scope in RunMetricRecords.AllScopes(profile))
            foreach (var kind in RunMetricRecords.Kinds)
            {
                expectedCollections++;
                if (!runCollections.TryGetValue((scope, kind), out var expected) || expected != RunMetricRecords.Scope(profile, scope).Dictionary(kind).Count)
                    throw new InvalidDataException("Maintained run collection completeness cannot be proven.");
            }
        if (runCollections.Count != expectedCollections) throw new InvalidDataException("Maintained run collection has an unknown owner.");
        var assembled = checkpointRecords.Finish(profile.GenerationId);
        if (assembled != null && checkpoint != null) throw new InvalidDataException("Two checkpoint representations claim the active run.");
        return new IncrementalProfileState(profile, session, assembled ?? checkpoint);
    }

    private static DeferredItemPersistenceState RequireDeferred(ProfileDocument profile) =>
        profile.DeferredItemPersistence ?? throw new InvalidDataException("Deferred state has no owner header.");
}
