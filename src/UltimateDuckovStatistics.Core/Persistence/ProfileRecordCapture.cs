using System.Globalization;
using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

internal static class ProfileRecordCapture
{
    internal static ProfileRecordChange Capture(ProfileDocument profile, ProfileRecordAddress address, long version,
        byte[]? session, byte[]? checkpoint, ProfileRecordCodec codec)
    {
        var statistics = profile.Statistics;
        var deferred = profile.DeferredItemPersistence;
        if (address.Kind == ProfileRecordKind.Session) return new(address, version, session);
        if (address.Kind == ProfileRecordKind.ActiveCheckpoint) return new(address, version, checkpoint);
        object? value = address.Kind switch
        {
            ProfileRecordKind.Metadata => ProfileMetadataRecord.From(profile),
            ProfileRecordKind.Statistics => StatisticsMetadataRecord.From(statistics),
            ProfileRecordKind.BaseMovement => statistics.BaseMovement,
            ProfileRecordKind.WorldTime => statistics.WorldTime,
            ProfileRecordKind.Holdings => statistics.Holdings,
            ProfileRecordKind.Economy => statistics.Economy,
            ProfileRecordKind.Item => Get(statistics.Items, address.First),
            ProfileRecordKind.Groups => statistics.Groups,
            ProfileRecordKind.Crafting => CraftingHeader(statistics.Crafting),
            ProfileRecordKind.CraftingOutput => OutputHeader(Get(statistics.Crafting.Outputs, address.First)),
            ProfileRecordKind.CraftingRecipe => RecipeHeader(Recipe(statistics.Crafting, address)),
            ProfileRecordKind.CraftingBatch => Batch(statistics.Crafting, address),
            ProfileRecordKind.CraftingResource => Get(statistics.Crafting.Resources, address.First),
            ProfileRecordKind.CraftingAssociation => Get(Recipe(statistics.Crafting, address)?.Resources, address.Third),
            ProfileRecordKind.RunTotals => RunMetricRecords.Header(statistics.RunTotals),
            ProfileRecordKind.RunRecords => new RunDurationRecords { Extraction = statistics.RunRecords.Extraction, Death = statistics.RunRecords.Death },
            ProfileRecordKind.RunMap => Get(statistics.RunTotals.Maps, address.First) is { } map ? RunMetricRecords.Header(map) : null,
            ProfileRecordKind.RouteMap => Get(statistics.RunTotals.RouteMaps, address.First) is { } routeMap ? RunMetricRecords.Header(routeMap) : null,
            ProfileRecordKind.MapRunRecords => Get(statistics.RunRecords.Maps, address.First),
            ProfileRecordKind.RunMetricCollection => new CheckpointCollectionState { Count = RunMetricRecords.Scope(profile, address.First).Dictionary(CheckpointRecordChanges.Kind(address.Second)).Count },
            ProfileRecordKind.RunMetricEntry => RunMetricRecords.Scope(profile, address.First).Dictionary(CheckpointRecordChanges.Kind(address.Second))[address.Third],
            ProfileRecordKind.CompletedRun => RunHistory.GetById(statistics.Runs, address.First),
            ProfileRecordKind.DeferredHeader => deferred == null ? null : new DeferredMetadataRecord
            {
                RunId = deferred.RunId,
                Overall = deferred.AppliedLifetimeStatistics.Overall,
                RecentEventIds = deferred.AppliedLifetimeStatistics.RecentEventIds,
                WasRepairedFromInvalidState = deferred.AppliedLifetimeStatistics.WasRepairedFromInvalidState
            },
            ProfileRecordKind.DeferredItem => Get(deferred?.AppliedLifetimeStatistics.Items, address.First),
            ProfileRecordKind.DeferredGroups => deferred?.AppliedLifetimeStatistics.Groups,
            ProfileRecordKind.DeferredEconomy => deferred?.AppliedLifetimeEconomy,
            _ => throw new InvalidOperationException("Unknown incremental profile record.")
        };
        if (value == null) return new(address, version, null);
        if (address.Kind == ProfileRecordKind.RunMetricEntry)
        {
            var kind = CheckpointRecordChanges.Kind(address.Second);
            CheckpointEntryContracts.Validate(kind, address.Third, value);
            return new(address, version, codec.Encode(value, RunMetricRecords.EntryType(kind)));
        }
        Validate(profile.GenerationId, address, value);
        return new(address, version, codec.Encode(value, RecordType(address.Kind)));
    }

    internal static Type RecordType(ProfileRecordKind kind) => kind switch
    {
        ProfileRecordKind.Metadata => typeof(ProfileMetadataRecord),
        ProfileRecordKind.Statistics => typeof(StatisticsMetadataRecord),
        ProfileRecordKind.BaseMovement => typeof(BaseMovementStatistics),
        ProfileRecordKind.WorldTime => typeof(WorldTimeStatisticsAggregate),
        ProfileRecordKind.Holdings => typeof(EconomyHoldingsSnapshot),
        ProfileRecordKind.Economy or ProfileRecordKind.DeferredEconomy => typeof(EconomyStatisticsAggregate),
        ProfileRecordKind.Item or ProfileRecordKind.DeferredItem => typeof(ItemAggregate),
        ProfileRecordKind.Groups or ProfileRecordKind.DeferredGroups => typeof(Dictionary<string, AggregateTotals>),
        ProfileRecordKind.Crafting => typeof(CraftingStatisticsAggregate),
        ProfileRecordKind.CraftingOutput => typeof(CraftedOutputAggregate),
        ProfileRecordKind.CraftingRecipe => typeof(CraftingRecipeAggregate),
        ProfileRecordKind.CraftingBatch => typeof(long),
        ProfileRecordKind.CraftingResource => typeof(CraftingResourceAggregate),
        ProfileRecordKind.CraftingAssociation => typeof(CraftingResourceAssociationAggregate),
        ProfileRecordKind.RunTotals => typeof(RunAggregateTotals),
        ProfileRecordKind.RunRecords => typeof(RunDurationRecords),
        ProfileRecordKind.RunMap => typeof(MapRunAggregate),
        ProfileRecordKind.RouteMap => typeof(RouteAwareMapAggregate),
        ProfileRecordKind.MapRunRecords => typeof(MapRunDurationRecords),
        ProfileRecordKind.RunMetricCollection => typeof(CheckpointCollectionState),
        ProfileRecordKind.CompletedRun => typeof(RunSummary),
        ProfileRecordKind.DeferredHeader => typeof(DeferredMetadataRecord),
        ProfileRecordKind.Session => typeof(SessionCheckpoint),
        ProfileRecordKind.ActiveCheckpoint => typeof(ActiveRunCheckpoint),
        _ => throw new InvalidOperationException("Unknown incremental profile record.")
    };

    private static void Validate(string generation, ProfileRecordAddress address, object value)
    {
        ProfileFormat.ValidateRecordMembers(value);
        switch (value)
        {
            case ProfileMetadataRecord metadata:
                if (metadata.GenerationId != generation || metadata.SchemaVersion != ProductInfo.SchemaVersion
                    || metadata.FormatId != ProductInfo.ProfileFormatId || metadata.Revision < 0 || metadata.InterruptedSessionCount < 0)
                    throw new ArgumentException("Profile metadata is invalid.");
                break;
            case StatisticsMetadataRecord metadata:
                if (metadata.SaveGenerationId != generation || metadata.SchemaVersion != ProductInfo.SchemaVersion)
                    throw new ArgumentException("Statistics metadata is invalid.");
                ValidateTotals(metadata.Overall);
                break;
            case BaseMovementStatistics movement: BaseMovementStatistics.Validate(movement); break;
            case WorldTimeStatisticsAggregate world: WorldTimeStatisticsReducer.Validate(world); break;
            case EconomyHoldingsSnapshot holdings: EconomyHoldingsReducer.ValidateRecoveryCandidate(holdings, generation); break;
            case EconomyStatisticsAggregate economy: EconomyStatisticsReducer.ValidateRecoveryCandidate(economy); break;
            case ItemAggregate item:
                if (item.ItemId != address.First || string.IsNullOrWhiteSpace(item.ItemId)) throw new ArgumentException("Item identity is invalid.");
                ValidateTotals(item.Totals); break;
            case Dictionary<string, AggregateTotals> groups:
                foreach (var group in groups) { if (string.IsNullOrWhiteSpace(group.Key)) throw new ArgumentException("Item group is invalid."); ValidateTotals(group.Value); }
                break;
            case CraftedOutputAggregate output:
                if (output.OutputItemId != address.First) throw new ArgumentException("Crafted output identity is invalid.");
                ValidateCraftCounts(output.CompletionActions, output.ProducedQuantity, output.CurrencyChargeActions, output.CurrencyCharged); break;
            case CraftingRecipeAggregate recipe:
                if (recipe.RecipeId != address.Second) throw new ArgumentException("Crafting recipe identity is invalid.");
                ValidateCraftCounts(recipe.CompletionActions, recipe.ProducedQuantity, recipe.CurrencyChargeActions, recipe.CurrencyCharged); break;
            case CraftingResourceAggregate resource:
                if (resource.ResourceItemId != address.First || resource.ConsumedQuantity < 0) throw new ArgumentException("Crafting resource is invalid."); break;
            case CraftingResourceAssociationAggregate resource:
                if (resource.ResourceItemId != address.Third || resource.ConsumedQuantity < 0 || resource.ConsumptionActions < 0) throw new ArgumentException("Crafting resource association is invalid."); break;
            case CraftingStatisticsAggregate crafting:
                ValidateCraftCounts(crafting.CompletionActions, crafting.ProducedQuantity, crafting.CurrencyChargeActions, crafting.CurrencyCharged); break;
            case long count:
                if (count < 0 || !long.TryParse(address.Third, NumberStyles.None, CultureInfo.InvariantCulture, out var batch) || batch <= 0)
                    throw new ArgumentException("Crafting batch is invalid."); break;
            case DeferredMetadataRecord watermark: ValidateTotals(watermark.Overall); break;
            case RunSummary run:
                if (run.RunId != address.First || run.SaveGenerationId != generation) throw new ArgumentException("Completed run identity is invalid.");
                RunReducer.Validate(run); break;
        }
    }

    private static void ValidateCraftCounts(long actions, long quantity, long charges, long cost)
    { if (actions < 0 || quantity < 0 || charges < 0 || cost < 0 || charges > actions) throw new ArgumentException("Crafting counters are invalid."); }
    private static void ValidateTotals(AggregateTotals totals)
    {
        if (totals.ActivationCount < 0 || !Finite(totals.ActualHealthRestored) || totals.AmountsByUnit.Values.Any(value => !Finite(value)))
            throw new ArgumentException("Item counters are invalid.");
    }
    private static bool Finite(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
    private static T? Get<T>(Dictionary<string, T>? values, string key) where T : class => values != null && values.TryGetValue(key, out var value) ? value : null;
    private static CraftingRecipeAggregate? Recipe(CraftingStatisticsAggregate crafting, ProfileRecordAddress address) => Get(Get(crafting.Outputs, address.First)?.Recipes, address.Second);
    private static object? Batch(CraftingStatisticsAggregate crafting, ProfileRecordAddress address) => Recipe(crafting, address)?.BatchActions.TryGetValue(address.Third, out var count) == true ? count : null;

    private static CraftedOutputAggregate? OutputHeader(CraftedOutputAggregate? output) => output == null ? null : new()
    { OutputItemId = output.OutputItemId, DisplayName = output.DisplayName, CompletionActions = output.CompletionActions, ProducedQuantity = output.ProducedQuantity, CurrencyChargeActions = output.CurrencyChargeActions, CurrencyCharged = output.CurrencyCharged };
    private static CraftingRecipeAggregate? RecipeHeader(CraftingRecipeAggregate? recipe) => recipe == null ? null : new()
    { RecipeId = recipe.RecipeId, CompletionActions = recipe.CompletionActions, ProducedQuantity = recipe.ProducedQuantity, CurrencyChargeActions = recipe.CurrencyChargeActions, CurrencyCharged = recipe.CurrencyCharged };
    private static CraftingStatisticsAggregate CraftingHeader(CraftingStatisticsAggregate value) => new()
    {
        CompletionActions = value.CompletionActions,
        ProducedQuantity = value.ProducedQuantity,
        Capabilities = value.Capabilities,
        CompletionArithmeticUnavailable = value.CompletionArithmeticUnavailable,
        QuantityArithmeticUnavailable = value.QuantityArithmeticUnavailable,
        WasRepairedFromInvalidState = value.WasRepairedFromInvalidState,
        CurrencyChargeActions = value.CurrencyChargeActions,
        CurrencyCharged = value.CurrencyCharged,
        ResourceHistoryUnavailable = value.ResourceHistoryUnavailable,
        ResourceHistoryProvenance = value.ResourceHistoryProvenance,
        ResourceActionArithmeticUnavailable = value.ResourceActionArithmeticUnavailable,
        ResourceQuantityArithmeticUnavailable = value.ResourceQuantityArithmeticUnavailable,
        CurrencyActionArithmeticUnavailable = value.CurrencyActionArithmeticUnavailable,
        CurrencyAmountArithmeticUnavailable = value.CurrencyAmountArithmeticUnavailable,
        CurrencyHistoryUnavailable = value.CurrencyHistoryUnavailable,
        CurrencyHistoryProvenance = value.CurrencyHistoryProvenance
    };
}

[DataContract]
internal sealed class ProfileMetadataRecord
{
    [DataMember(Order = 1)] public string FormatId { get; set; } = ProductInfo.ProfileFormatId;
    [DataMember(Order = 2)] public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;
    [DataMember(Order = 3)] public string GenerationId { get; set; } = "";
    [DataMember(Order = 4)] public int Slot { get; set; }
    [DataMember(Order = 5)] public string GenerationReason { get; set; } = "";
    [DataMember(Order = 6)] public DateTime CreatedUtc { get; set; }
    [DataMember(Order = 7)] public DateTime UpdatedUtc { get; set; }
    [DataMember(Order = 8)] public long Revision { get; set; }
    [DataMember(Order = 9)] public long InterruptedSessionCount { get; set; }
    [DataMember(Order = 10)] public SaveIdentitySnapshot Identity { get; set; } = new();
    [DataMember(Order = 11)] public List<CapabilityRecord> Capabilities { get; set; } = new();
    [DataMember(Order = 12, EmitDefaultValue = false)] public PendingSaveObservation? PendingSave { get; set; }
    internal static ProfileMetadataRecord From(ProfileDocument value) => new()
    {
        FormatId = value.FormatId,
        SchemaVersion = value.SchemaVersion,
        GenerationId = value.GenerationId,
        Slot = value.Slot,
        GenerationReason = value.GenerationReason,
        CreatedUtc = value.CreatedUtc,
        UpdatedUtc = value.UpdatedUtc,
        Revision = value.Revision,
        InterruptedSessionCount = value.InterruptedSessionCount,
        Identity = value.Identity,
        Capabilities = value.Capabilities,
        PendingSave = value.PendingSave
    };
    internal ProfileDocument ToProfile() => new()
    {
        FormatId = FormatId,
        SchemaVersion = SchemaVersion,
        GenerationId = GenerationId,
        Slot = Slot,
        GenerationReason = GenerationReason,
        CreatedUtc = CreatedUtc,
        UpdatedUtc = UpdatedUtc,
        Revision = Revision,
        InterruptedSessionCount = InterruptedSessionCount,
        Identity = Identity,
        Capabilities = Capabilities,
        PendingSave = PendingSave
    };
}

[DataContract]
internal sealed class StatisticsMetadataRecord
{
    [DataMember(Order = 1)] public int SchemaVersion { get; set; }
    [DataMember(Order = 2)] public string SaveGenerationId { get; set; } = "";
    [DataMember(Order = 3)] public DateTime CreatedUtc { get; set; }
    [DataMember(Order = 4)] public DateTime UpdatedUtc { get; set; }
    [DataMember(Order = 5)] public AggregateTotals Overall { get; set; } = new();
    [DataMember(Order = 6)] public List<string> RecentEventIds { get; set; } = new();
    [DataMember(Order = 7)] public bool HealingCaptureComplete { get; set; }
    internal static StatisticsMetadataRecord From(ProfileStatistics value) => new()
    {
        SchemaVersion = value.SchemaVersion,
        SaveGenerationId = value.SaveGenerationId,
        CreatedUtc = value.CreatedUtc,
        UpdatedUtc = value.UpdatedUtc,
        Overall = value.Overall,
        RecentEventIds = value.RecentEventIds,
        HealingCaptureComplete = value.HealingCaptureComplete
    };
}

[DataContract]
internal sealed class DeferredMetadataRecord
{
    [DataMember(Order = 1, EmitDefaultValue = false)] public string? RunId { get; set; }
    [DataMember(Order = 2)] public AggregateTotals Overall { get; set; } = new();
    [DataMember(Order = 3)] public List<string> RecentEventIds { get; set; } = new();
    [DataMember(Order = 4)] public bool WasRepairedFromInvalidState { get; set; }
}
