using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class CraftingStatisticsTests
{
    [Fact]
    public void ReducerKeepsCompletionActionsQuantityRecipesAndBatchDistributionExact()
    {
        var aggregate = SupportedAggregate();

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 }),
            new CraftingMutationRow("100", "Bandage", "field_bandage", 1, 5, new() { ["5"] = 1 }))));

        Assert.Equal(2, aggregate.CompletionActions);
        Assert.Equal(7, aggregate.ProducedQuantity);
        var output = Assert.Single(aggregate.Outputs).Value;
        Assert.Equal(2, output.CompletionActions);
        Assert.Equal(7, output.ProducedQuantity);
        Assert.Equal(2, output.Recipes.Count);
        Assert.Equal(1, output.Recipes["bandage"].BatchActions["2"]);
        Assert.Equal(1, output.Recipes["field_bandage"].BatchActions["5"]);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void CompletionOverflowPreservesPriorExactTotalsWhenQuantityWasAlreadyUnavailable()
    {
        var aggregate = SupportedAggregate();
        aggregate.CompletionActions = long.MaxValue;
        aggregate.ProducedQuantity = long.MaxValue;
        aggregate.QuantityArithmeticUnavailable = true;
        aggregate.Capabilities.ProducedQuantity = CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            "quantity unavailable");
        aggregate.Outputs["100"] = new CraftedOutputAggregate
        {
            OutputItemId = "100",
            DisplayName = "Bandage",
            CompletionActions = long.MaxValue,
            ProducedQuantity = long.MaxValue,
            Recipes = new()
            {
                ["bandage"] = new CraftingRecipeAggregate
                {
                    RecipeId = "bandage",
                    CompletionActions = long.MaxValue,
                    ProducedQuantity = long.MaxValue,
                    BatchActions = new() { ["1"] = long.MaxValue }
                }
            }
        };

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 1, new() { ["1"] = 1 }))));

        Assert.Equal(long.MaxValue, aggregate.CompletionActions);
        Assert.Equal(long.MaxValue, aggregate.ProducedQuantity);
        Assert.True(aggregate.CompletionArithmeticUnavailable);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.CompletionActions.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.BatchMetadata.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.ProducedQuantity.State);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void QuantityOverflowPreservesPriorExactQuantityAndLeavesActionsAvailable()
    {
        var aggregate = SupportedAggregate();
        aggregate.ProducedQuantity = long.MaxValue;
        aggregate.Outputs["100"] = new CraftedOutputAggregate
        {
            OutputItemId = "100",
            DisplayName = "Bandage",
            CompletionActions = 1,
            ProducedQuantity = long.MaxValue,
            Recipes = new()
            {
                ["bandage"] = new CraftingRecipeAggregate
                {
                    RecipeId = "bandage",
                    CompletionActions = 1,
                    ProducedQuantity = long.MaxValue,
                    BatchActions = new()
                    {
                        [long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 1
                    }
                }
            }
        };
        aggregate.CompletionActions = 1;

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 1, new() { ["1"] = 1 }))));

        Assert.Equal(2, aggregate.CompletionActions);
        Assert.Equal(long.MaxValue, aggregate.ProducedQuantity);
        Assert.True(aggregate.QuantityArithmeticUnavailable);
        Assert.Equal(AdapterCapabilityState.Supported, aggregate.Capabilities.CompletionActions.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.ProducedQuantity.State);
        Assert.Equal(1, aggregate.Outputs["100"].Recipes["bandage"].BatchActions["1"]);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void RecipeIdentifiersCannotCollideWithStructuredOverflowAccountingKeys()
    {
        var aggregate = SupportedAggregate();

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", "Bandage", "base", 1, 2, new() { ["2"] = 1 }),
            new CraftingMutationRow("100", "Bandage", "base\u001f2", 1, 3, new() { ["3"] = 1 }))));

        Assert.Equal(2, aggregate.CompletionActions);
        Assert.Equal(5, aggregate.ProducedQuantity);
        Assert.Equal(1, aggregate.Outputs["100"].Recipes["base"].BatchActions["2"]);
        Assert.Equal(1, aggregate.Outputs["100"].Recipes["base\u001f2"].BatchActions["3"]);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void RecipeAndBatchDegradationDoesNotDisableExactOutputTotals()
    {
        var aggregate = new CraftingStatisticsAggregate();
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.OutputTotalsSupportedMetadataUnavailable(
                "direct task completion",
                "metadata unavailable"));

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow(
                "100",
                "Bandage",
                "bandage",
                1,
                4,
                new(),
                recipeIdentityProven: false,
                batchMetadataProven: false))));

        Assert.Equal(1, aggregate.CompletionActions);
        Assert.Equal(4, aggregate.ProducedQuantity);
        Assert.Empty(aggregate.Outputs["100"].Recipes);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.RecipeIdentity.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.BatchMetadata.State);
    }

    [Fact]
    public void PreviouslyRecordedRecipeDetailRemainsAValidSubsetAfterMetadataDegradation()
    {
        var aggregate = SupportedAggregate();
        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 }))));
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.OutputTotalsSupportedMetadataUnavailable(
                "completion remains supported",
                "formula metadata is no longer compatible"));

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow(
                "100",
                "Bandage",
                "bandage",
                1,
                2,
                new(),
                recipeIdentityProven: false,
                batchMetadataProven: false))));

        Assert.Equal(2, aggregate.CompletionActions);
        Assert.Equal(4, aggregate.ProducedQuantity);
        Assert.Equal(1, aggregate.Outputs["100"].Recipes["bandage"].CompletionActions);
        Assert.Equal(2, aggregate.Outputs["100"].Recipes["bandage"].ProducedQuantity);
        Assert.Equal(1, aggregate.Outputs["100"].Recipes["bandage"].BatchActions["2"]);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void ProvenPendingDetailSurvivesRuntimeCapabilityDegradationBeforeRetry()
    {
        var aggregate = SupportedAggregate();
        var pending = new CraftingPendingAccumulator();
        pending.Add(Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 })));
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.Unavailable("runtime patch drift"));

        Assert.True(pending.TryFlush(mutation => CraftingStatisticsReducer.Apply(aggregate, mutation)));

        Assert.Equal(1, aggregate.CompletionActions);
        Assert.Equal(2, aggregate.ProducedQuantity);
        var recipe = Assert.Single(Assert.Single(aggregate.Outputs).Value.Recipes).Value;
        Assert.Equal(1, recipe.CompletionActions);
        Assert.Equal(2, recipe.ProducedQuantity);
        Assert.Equal(1, recipe.BatchActions["2"]);
        Assert.True(pending.IsEmpty);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void PendingFullAndDegradedRowsDoNotMergeTheirProofScopes()
    {
        var aggregate = new CraftingStatisticsAggregate();
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.OutputTotalsSupportedMetadataUnavailable(
                "completion remains supported",
                "formula metadata is unavailable"));
        var pending = new CraftingPendingAccumulator();
        pending.Add(Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 })));
        pending.Add(Mutation(
            new CraftingMutationRow(
                "100",
                "Bandage",
                "bandage",
                1,
                2,
                new(),
                recipeIdentityProven: false,
                batchMetadataProven: false)));

        Assert.True(pending.TryFlush(mutation => CraftingStatisticsReducer.Apply(aggregate, mutation)));

        Assert.Equal(2, aggregate.CompletionActions);
        Assert.Equal(4, aggregate.ProducedQuantity);
        var recipe = Assert.Single(aggregate.Outputs["100"].Recipes).Value;
        Assert.Equal(1, recipe.CompletionActions);
        Assert.Equal(2, recipe.ProducedQuantity);
        Assert.Equal(1, recipe.BatchActions["2"]);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void SupportedBatchMetadataRejectsARecipeWhoseBatchCompositionIsMissing()
    {
        var aggregate = SupportedAggregate();
        aggregate.CompletionActions = 1;
        aggregate.ProducedQuantity = 2;
        aggregate.Outputs["100"] = new CraftedOutputAggregate
        {
            OutputItemId = "100",
            DisplayName = "Bandage",
            CompletionActions = 1,
            ProducedQuantity = 2,
            Recipes = new()
            {
                ["bandage"] = new CraftingRecipeAggregate
                {
                    RecipeId = "bandage",
                    CompletionActions = 1,
                    ProducedQuantity = 2
                }
            }
        };

        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(aggregate));
    }

    [Fact]
    public void InconsistentBatchQuantityIsRejectedBeforeAnyAggregateChange()
    {
        var aggregate = SupportedAggregate();
        var mutation = Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 2, 14, new() { ["5"] = 2 }));

        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Apply(aggregate, mutation));

        Assert.Equal(0, aggregate.CompletionActions);
        Assert.Equal(0, aggregate.ProducedQuantity);
        Assert.Empty(aggregate.Outputs);
    }

    [Fact]
    public void SupportedBatchMetadataRejectsAMutationWithoutBatchEvidence()
    {
        var aggregate = SupportedAggregate();
        var mutation = Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new()));

        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Apply(aggregate, mutation));

        Assert.Equal(0, aggregate.CompletionActions);
        Assert.Equal(0, aggregate.ProducedQuantity);
        Assert.Empty(aggregate.Outputs);
    }

    [Fact]
    public void MutationFromAnotherSaveGenerationIsRejectedBeforeAnyAggregateChange()
    {
        var aggregate = SupportedAggregate();
        var mutation = Mutation(new CraftingMutationRow("100", "Bandage", "bandage", 1, 1, new() { ["1"] = 1 }));

        Assert.Throws<InvalidOperationException>(() =>
            CraftingStatisticsReducer.Apply(aggregate, "generation-2", mutation));
        Assert.Equal(0, aggregate.CompletionActions);
        Assert.Empty(aggregate.Outputs);
    }

    [Fact]
    public void PendingPublicationAggregatesFailuresAndRetriesWithoutEventHistoryCeiling()
    {
        var pending = new CraftingPendingAccumulator();
        pending.Add(Mutation(new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 })));
        pending.Add(Mutation(new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 })));
        CraftingMutation? observed = null;

        Assert.False(pending.TryFlush(_ => false));
        Assert.False(pending.IsEmpty);
        Assert.True(pending.TryFlush(value => { observed = value; return true; }));

        var row = Assert.Single(observed!.Rows);
        Assert.Equal(2, row.CompletionActions);
        Assert.Equal(4, row.ProducedQuantity);
        Assert.Equal(2, row.BatchActions["2"]);
        Assert.True(pending.IsEmpty);
    }

    [Fact]
    public void MoreThanTwoThousandFortyEightDistinctOutputsAggregateWithoutAHistoryCeiling()
    {
        var aggregate = SupportedAggregate();
        var rows = Enumerable.Range(1, 4096)
            .Select(index => new CraftingMutationRow(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"Output {index}",
                $"recipe:{index}",
                1,
                index,
                new() { [index.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 1 }))
            .ToArray();

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(rows)));

        Assert.Equal(4096, aggregate.CompletionActions);
        Assert.Equal(4096, aggregate.Outputs.Count);
        Assert.Equal(rows.Sum(row => row.ProducedQuantity), aggregate.ProducedQuantity);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void MoreThanTwoThousandFortyEightPendingOutputsCoalesceWithoutAHistoryCeiling()
    {
        var pending = new CraftingPendingAccumulator();
        for (var index = 1; index <= 4096; index++)
        {
            var identity = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            pending.Add(Mutation(new CraftingMutationRow(
                identity,
                $"Output {identity}",
                $"recipe:{identity}",
                1,
                index,
                new() { [identity] = 1 })));
        }
        CraftingMutation? observed = null;

        Assert.True(pending.TryFlush(value => { observed = value; return true; }));

        Assert.Equal(4096, observed!.Rows.Count);
        Assert.Equal(4096, observed.Rows.Sum(row => row.CompletionActions));
        Assert.Equal(Enumerable.Range(1, 4096).Sum(index => (long)index), observed.Rows.Sum(row => row.ProducedQuantity));
        Assert.True(pending.IsEmpty);
    }

    [Fact]
    public void PendingMergeOverflowRejectsTheWholeIncomingMutationWithoutPartialRows()
    {
        var pending = new CraftingPendingAccumulator();
        pending.Add(Mutation(new CraftingMutationRow(
            "100",
            "Bandage",
            "bandage",
            long.MaxValue,
            long.MaxValue,
            new() { ["1"] = long.MaxValue })));

        Assert.Throws<OverflowException>(() => pending.Add(Mutation(
            new CraftingMutationRow("200", "Med Kit", "med_kit", 1, 1, new() { ["1"] = 1 }),
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 1, new() { ["1"] = 1 }))));

        CraftingMutation? observed = null;
        Assert.True(pending.TryFlush(value => { observed = value; return true; }));
        var row = Assert.Single(observed!.Rows);
        Assert.Equal("100", row.OutputItemId);
        Assert.Equal(long.MaxValue, row.CompletionActions);
        Assert.Equal(long.MaxValue, row.ProducedQuantity);
    }

    [Fact]
    public void InstalledSingularResultContractKeepsUnprovenDimensionsExplicitlyUnavailable()
    {
        var capabilities = CraftingNativeContractPolicy.Supported("completion", "formula");

        Assert.Equal(AdapterCapabilityState.Supported, capabilities.CompletionActions.State);
        Assert.Equal(AdapterCapabilityState.Supported, capabilities.ProducedQuantity.State);
        Assert.Equal(AdapterCapabilityState.Supported, capabilities.OutputIdentity.State);
        Assert.Equal(AdapterCapabilityState.Supported, capabilities.RecipeIdentity.State);
        Assert.Equal(AdapterCapabilityState.Supported, capabilities.BatchMetadata.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capabilities.WorkstationIdentity.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capabilities.ContextAttribution.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capabilities.MultipleOutputRecipes.State);
        var records = CraftingNativeContractPolicy.ToRecords(capabilities, "test");
        Assert.Equal(CraftingCapabilityIds.All, records.Select(record => record.AdapterId));
        Assert.Equal(CraftingCapabilityIds.All.Count, records.Select(record => record.AdapterId).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MissingDisplayEnrichmentUsesFallbackWithoutReplacingAPriorReliableName()
    {
        var aggregate = SupportedAggregate();
        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", string.Empty, "bandage", 1, 1, new() { ["1"] = 1 }))));
        Assert.Equal("Unknown item 100", aggregate.Outputs["100"].DisplayName);

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", "Bandage", "bandage", 1, 1, new() { ["1"] = 1 }))));
        Assert.Equal("Bandage", aggregate.Outputs["100"].DisplayName);

        Assert.True(CraftingStatisticsReducer.Apply(aggregate, Mutation(
            new CraftingMutationRow("100", string.Empty, "bandage", 1, 1, new() { ["1"] = 1 }))));
        Assert.Equal("Bandage", aggregate.Outputs["100"].DisplayName);
    }

    private static CraftingStatisticsAggregate SupportedAggregate()
    {
        var aggregate = new CraftingStatisticsAggregate();
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.Supported("completion", "formula"));
        return aggregate;
    }

    private static CraftingMutation Mutation(params CraftingMutationRow[] rows) => new(
        "generation-1",
        new DateTime(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc),
        rows);
}
