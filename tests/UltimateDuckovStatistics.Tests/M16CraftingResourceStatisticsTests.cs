using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class M16CraftingResourceStatisticsTests
{
    private static readonly DateTime Now = new(2026, 8, 29, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "ProductionComposition")]
    public void EventTimeCostsFlowThroughProductionBoundaryRepositoryPersistenceAndExports()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repository = Repository(temporaryDirectory.Path, "generation-1", "session-1", "session-2");
        var identity = Identity();
        repository.Open(identity);
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money"));
        var boundary = new CraftingCompletionBoundary();
        var eventTimeCosts = new List<CraftingResourceCostEvidence>
        {
            new("764", "High-tier Parts", 2),
            new("764", "High-tier Parts", 3),
            new("mod:resource/-7", "Modded Flux", 4)
        };
        var token = boundary.Begin(new CraftingCompletionEvidence(
            "131",
            "Audited Output",
            "1026",
            1,
            eventTimeCosts,
            currencyCharged: 150));

        eventTimeCosts.Clear();
        eventTimeCosts.Add(new CraftingResourceCostEvidence("999", "Later recipe metadata", 999));
        Assert.Empty(repository.Current.Statistics.Crafting.Outputs);
        Assert.Empty(repository.Current.Statistics.Crafting.Resources);

        Assert.True(boundary.TryComplete(token, "generation-1", Now, out var mutation));
        Assert.True(repository.RecordCraftingDeferred(mutation));
        Assert.True(boundary.FinishPublication(token));
        repository.Flush();

        var crafting = repository.Current.Statistics.Crafting;
        Assert.Equal(1, crafting.CompletionActions);
        Assert.Equal(1, crafting.ProducedQuantity);
        Assert.Equal(1, crafting.CurrencyChargeActions);
        Assert.Equal(150, crafting.CurrencyCharged);
        Assert.Equal(5, crafting.Resources["764"].ConsumedQuantity);
        Assert.Equal(4, crafting.Resources["mod:resource/-7"].ConsumedQuantity);
        Assert.DoesNotContain("999", crafting.Resources.Keys);
        var recipe = crafting.Outputs["131"].Recipes["1026"];
        Assert.Equal(1, recipe.Resources["764"].ConsumptionActions);
        Assert.Equal(5, recipe.Resources["764"].ConsumedQuantity);
        Assert.Equal(150, recipe.CurrencyCharged);
        CraftingStatisticsReducer.Validate(crafting);

        var bundle = StatisticsExporter.Create(repository.Current, Now);
        using var json = JsonDocument.Parse(bundle.Json);
        var jsonCrafting = json.RootElement.GetProperty("Crafting");
        Assert.Equal(150, jsonCrafting.GetProperty("CurrencyCharged").GetInt64());
        Assert.Equal(5, jsonCrafting.GetProperty("Resources").GetProperty("764").GetProperty("ConsumedQuantity").GetInt64());
        Assert.Contains("764,High-tier Parts,5", bundle.CraftingResourcesCsv, StringComparison.Ordinal);
        Assert.Contains("131,Audited Output,1026,764,High-tier Parts,1,5", bundle.CraftingResourceAssociationsCsv, StringComparison.Ordinal);
        Assert.Contains("1026,1,1,1,150", bundle.CraftingRecipesCsv, StringComparison.Ordinal);

        var reopened = Repository(temporaryDirectory.Path, "unused-generation", "session-reopen");
        Assert.True(reopened.Open(identity).InterruptedSessionRecovered);
        Assert.Equal(5, reopened.Current.Statistics.Crafting.Resources["764"].ConsumedQuantity);
        Assert.Equal(150, reopened.Current.Statistics.Crafting.CurrencyCharged);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "M16")]
    public void AbandonedOrUnprovenCraftNeverPublishesItsCapturedCosts()
    {
        var boundary = new CraftingCompletionBoundary();
        var token = boundary.Begin(Evidence(3, 250));

        Assert.Equal(1, boundary.PendingCount);
        Assert.True(boundary.Abandon(token));
        Assert.False(boundary.TryComplete(token, "generation-1", Now, out var mutation));
        Assert.True(mutation.IsEmpty);
        Assert.Equal(0, boundary.OutstandingCount);
    }

    [Fact]
    [Trait("Category", "M16")]
    public void FreeItemOnlyMoneyOnlyAndCombinedCostsRemainIndependentWithSharedResourceAssociations()
    {
        var aggregate = SupportedAggregate();
        var boundary = new CraftingCompletionBoundary();
        Complete("free-output", "free-recipe", Array.Empty<CraftingResourceCostEvidence>(), 0);
        Complete("item-output", "item-recipe", [new CraftingResourceCostEvidence("764", "Shared Parts", 2)], 0);
        Complete("money-output", "money-recipe", Array.Empty<CraftingResourceCostEvidence>(), 75);
        Complete("combined-output", "combined-recipe", [new CraftingResourceCostEvidence("764", "Shared Parts", 3)], 125);
        Complete("combined-output", "alternate-recipe", [new CraftingResourceCostEvidence("764", "Shared Parts", 7)], 0);

        Assert.Equal(5, aggregate.CompletionActions);
        Assert.Equal(12, aggregate.Resources["764"].ConsumedQuantity);
        Assert.Equal(2, aggregate.CurrencyChargeActions);
        Assert.Equal(200, aggregate.CurrencyCharged);
        Assert.Empty(aggregate.Outputs["free-output"].Recipes["free-recipe"].Resources);
        Assert.Empty(aggregate.Outputs["money-output"].Recipes["money-recipe"].Resources);
        Assert.Equal(2, aggregate.Outputs["item-output"].Recipes["item-recipe"].Resources["764"].ConsumedQuantity);
        Assert.Equal(3, aggregate.Outputs["combined-output"].Recipes["combined-recipe"].Resources["764"].ConsumedQuantity);
        Assert.Equal(7, aggregate.Outputs["combined-output"].Recipes["alternate-recipe"].Resources["764"].ConsumedQuantity);
        Assert.Equal(0, aggregate.Outputs["item-output"].CurrencyCharged);
        Assert.Equal(75, aggregate.Outputs["money-output"].CurrencyCharged);
        CraftingStatisticsReducer.Validate(aggregate);

        void Complete(
            string outputItemId,
            string recipeId,
            IReadOnlyList<CraftingResourceCostEvidence> resources,
            long currencyCharged)
        {
            var token = boundary.Begin(new CraftingCompletionEvidence(
                outputItemId,
                outputItemId,
                recipeId,
                1,
                resources,
                currencyCharged));
            Assert.True(boundary.TryComplete(token, "generation-1", Now, out var mutation));
            Assert.True(CraftingStatisticsReducer.Apply(aggregate, mutation));
            Assert.True(boundary.FinishPublication(token));
        }
    }

    [Fact]
    [Trait("Category", "M16")]
    public void OverlappingBatchedCraftsAndDuplicateCompletionKeepEventTimeCostsIsolated()
    {
        var aggregate = SupportedAggregate();
        var boundary = new CraftingCompletionBoundary();
        var first = boundary.Begin(new CraftingCompletionEvidence(
            "output-a",
            "Output A",
            "recipe-a",
            2,
            [
                new CraftingResourceCostEvidence("shared", "Shared", 3),
                new CraftingResourceCostEvidence("exclusive", "Exclusive", 4)
            ],
            10));
        var second = boundary.Begin(new CraftingCompletionEvidence(
            "output-b",
            "Output B",
            "recipe-b",
            7,
            [new CraftingResourceCostEvidence("shared", "Shared", 5)],
            20));

        Assert.True(boundary.TryComplete(second, "generation-1", Now, out var secondMutation));
        Assert.True(boundary.TryComplete(first, "generation-1", Now.AddSeconds(1), out var firstMutation));
        Assert.False(boundary.TryComplete(first, "generation-1", Now.AddSeconds(2), out var duplicate));
        Assert.True(duplicate.IsEmpty);
        Assert.True(CraftingStatisticsReducer.Apply(aggregate, secondMutation));
        Assert.True(CraftingStatisticsReducer.Apply(aggregate, firstMutation));

        Assert.Equal(2, aggregate.CompletionActions);
        Assert.Equal(9, aggregate.ProducedQuantity);
        Assert.Equal(8, aggregate.Resources["shared"].ConsumedQuantity);
        Assert.Equal(4, aggregate.Resources["exclusive"].ConsumedQuantity);
        Assert.Equal(30, aggregate.CurrencyCharged);
        var firstRecipe = aggregate.Outputs["output-a"].Recipes["recipe-a"];
        Assert.Equal(1, firstRecipe.BatchActions["2"]);
        Assert.Equal(3, firstRecipe.Resources["shared"].ConsumedQuantity);
        Assert.Equal(4, firstRecipe.Resources["exclusive"].ConsumedQuantity);
        Assert.Equal(10, firstRecipe.CurrencyCharged);
        var secondRecipe = aggregate.Outputs["output-b"].Recipes["recipe-b"];
        Assert.Equal(1, secondRecipe.BatchActions["7"]);
        Assert.Equal(5, secondRecipe.Resources["shared"].ConsumedQuantity);
        Assert.Equal(20, secondRecipe.CurrencyCharged);
        Assert.True(boundary.FinishPublication(second));
        Assert.True(boundary.FinishPublication(first));
        Assert.False(boundary.FinishPublication(first));
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    [Trait("Category", "M16")]
    public void ProfileHandoffRetainsCostPayloadAndRebindsOnlyItsGeneration()
    {
        var completion = new CraftingCompletionBoundary();
        var handoff = new CraftingProfileHandoffBoundary();
        handoff.Begin(17);
        var token = completion.Begin(Evidence(6, 150));

        Assert.True(completion.TryComplete(
            token,
            CraftingProfileHandoffBoundary.StagedGenerationId,
            Now,
            out var staged));
        Assert.True(handoff.Stage(17, staged));
        Assert.True(completion.FinishPublication(token));
        Assert.True(handoff.Complete(17, "generation-target"));
        CraftingMutation? published = null;
        Assert.True(handoff.TryFlushCompleted(mutation =>
        {
            published = mutation;
            return true;
        }));

        Assert.Equal("generation-target", published!.SaveGenerationId);
        var row = Assert.Single(published.Rows);
        var resource = Assert.Single(row.Resources);
        Assert.Equal("764", resource.ResourceItemId);
        Assert.Equal(1, resource.ConsumptionActions);
        Assert.Equal(6, resource.ConsumedQuantity);
        Assert.Equal(1, row.CurrencyChargeActions);
        Assert.Equal(150, row.CurrencyCharged);
        Assert.False(handoff.HasUncommittedData);
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    public void SlotNewGameAndResetKeepM16CostsInTheirExactSaveGeneration()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var repository = Repository(
            temporaryDirectory.Path,
            "generation-slot-1", "session-slot-1",
            "generation-slot-2", "session-slot-2",
            "session-slot-1-reopen",
            "generation-slot-1-new", "session-slot-1-new",
            "generation-slot-1-reset", "session-slot-1-reset");
        var boundary = new CraftingCompletionBoundary();

        repository.Open(Identity(1, 100));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money"));
        Complete("output-slot-1", "recipe-slot-1", "764", 4, 150);

        repository.Open(Identity(2, 200));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money"));
        AssertEmpty(repository.Current.Statistics.Crafting);
        Complete("output-slot-2", "recipe-slot-2", "662", 3, 0);

        repository.Open(Identity(1, 100));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money"));
        Assert.Equal(4, repository.Current.Statistics.Crafting.Resources["764"].ConsumedQuantity);
        Assert.DoesNotContain("662", repository.Current.Statistics.Crafting.Resources.Keys);
        Assert.Equal(150, repository.Current.Statistics.Crafting.CurrencyCharged);

        repository.Rotate(Identity(1, 300), "DuckovNewGame");
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money"));
        AssertEmpty(repository.Current.Statistics.Crafting);
        Complete("output-new-game", "recipe-new-game", "21", 2, 0);

        repository.Rotate(Identity(1, 300), "UserReset");
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money"));
        AssertEmpty(repository.Current.Statistics.Crafting);
        repository.CloseClean();

        void Complete(
            string outputItemId,
            string recipeId,
            string resourceItemId,
            long resourceQuantity,
            long currencyCharged)
        {
            var token = boundary.Begin(new CraftingCompletionEvidence(
                outputItemId,
                outputItemId,
                recipeId,
                1,
                [new CraftingResourceCostEvidence(resourceItemId, resourceItemId, resourceQuantity)],
                currencyCharged));
            Assert.True(boundary.TryComplete(token, repository.Current.GenerationId, Now, out var mutation));
            Assert.True(repository.RecordCraftingDeferred(mutation));
            Assert.True(boundary.FinishPublication(token));
            repository.Flush();
        }

        static void AssertEmpty(CraftingStatisticsAggregate crafting)
        {
            Assert.Equal(0, crafting.CompletionActions);
            Assert.Empty(crafting.Outputs);
            Assert.Empty(crafting.Resources);
            Assert.Equal(0, crafting.CurrencyChargeActions);
            Assert.Equal(0, crafting.CurrencyCharged);
        }
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    public void SchemaFifteenCraftingTotalsRemainWhilePreM16CostHistoryIsUnavailableAndNotReconstructed()
    {
        var profile = new ProfileDocument
        {
            SchemaVersion = 15,
            GenerationId = "generation-1",
            Slot = 1,
            Identity = Identity(),
            Statistics = new ProfileStatistics
            {
                SchemaVersion = 15,
                SaveGenerationId = "generation-1",
                Holdings = new EconomyHoldingsSnapshot { SaveGenerationId = "generation-1" }
            }
        };
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            profile.Statistics.Crafting,
            CraftingNativeContractPolicy.Supported("delivery", "formula"));
        Assert.True(CraftingStatisticsReducer.Apply(
            profile.Statistics.Crafting,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow("100", "Bandage", "bandage", 2, 4, new() { ["2"] = 2 })])));

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(16, profile.SchemaVersion);
        Assert.Equal(2, profile.Statistics.Crafting.CompletionActions);
        Assert.Equal(4, profile.Statistics.Crafting.ProducedQuantity);
        Assert.Empty(profile.Statistics.Crafting.Resources);
        Assert.Equal(0, profile.Statistics.Crafting.CurrencyCharged);
        Assert.True(profile.Statistics.Crafting.ResourceHistoryUnavailable);
        Assert.Contains("not reconstructed", profile.Statistics.Crafting.ResourceHistoryProvenance, StringComparison.Ordinal);
        Assert.True(profile.Statistics.Crafting.CurrencyHistoryUnavailable);
        Assert.Contains("not reconstructed", profile.Statistics.Crafting.CurrencyHistoryProvenance, StringComparison.Ordinal);
        CraftingStatisticsReducer.Validate(profile.Statistics.Crafting);
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    public void SerializedSchemaFifteenMissingM16CapabilityMembersMigratesWithoutClaimingRepairOrHistory()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = Path.Combine(temporaryDirectory.Path, "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var profile = new ProfileDocument
        {
            GenerationId = "generation-1",
            Slot = 1,
            Identity = Identity(),
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = "generation-1",
                CreatedUtc = Now,
                UpdatedUtc = Now,
                Holdings = new EconomyHoldingsSnapshot { SaveGenerationId = "generation-1" }
            }
        };
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            profile.Statistics.Crafting,
            CraftingNativeContractPolicy.Supported("delivery", "formula"));
        Assert.True(CraftingStatisticsReducer.Apply(
            profile.Statistics.Crafting,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 })])));
        store.Save(path, profile);
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        root["SchemaVersion"] = 15;
        var statistics = root["Statistics"]!.AsObject();
        statistics["SchemaVersion"] = 15;
        var crafting = statistics["Crafting"]!.AsObject();
        Assert.True(crafting.Remove("Resources"));
        Assert.True(crafting.Remove("ResourceHistoryProvenance"));
        Assert.True(crafting.Remove("CurrencyHistoryProvenance"));
        Assert.True(crafting["Outputs"]!["100"]!["Recipes"]!["bandage"]!.AsObject().Remove("Resources"));
        var capabilities = crafting["Capabilities"]!.AsObject();
        Assert.True(capabilities.Remove("ItemResourceIdentity"));
        Assert.True(capabilities.Remove("OutputResourceAssociation"));
        Assert.True(capabilities.Remove("CurrencyCharge"));
        Assert.True(capabilities.Remove("CurrencyMoneyCashSplit"));
        File.WriteAllText(path, root.ToJsonString());
        var loaded = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate).Value!;

        Assert.True(ProfileMigrator.Migrate(loaded));

        Assert.Equal(16, loaded.SchemaVersion);
        Assert.False(loaded.Statistics.Crafting.WasRepairedFromInvalidState);
        Assert.Equal(1, loaded.Statistics.Crafting.CompletionActions);
        Assert.Equal(2, loaded.Statistics.Crafting.ProducedQuantity);
        Assert.True(loaded.Statistics.Crafting.ResourceHistoryUnavailable);
        Assert.True(loaded.Statistics.Crafting.CurrencyHistoryUnavailable);
        Assert.NotNull(loaded.Statistics.Crafting.Capabilities.ItemResourceIdentity);
        Assert.NotNull(loaded.Statistics.Crafting.Capabilities.CurrencyCharge);
    }

    [Fact]
    [Trait("Category", "M16")]
    public void ResourceAndCurrencyArithmeticDegradeIndependentlyWithoutLosingOtherExactMetrics()
    {
        var resourceOverflow = SupportedAggregate();
        Apply(resourceOverflow, long.MaxValue, 100);
        Apply(resourceOverflow, 1, 100);
        Assert.Equal(2, resourceOverflow.CompletionActions);
        Assert.Equal(long.MaxValue, resourceOverflow.Resources["764"].ConsumedQuantity);
        Assert.True(resourceOverflow.ResourceQuantityArithmeticUnavailable);
        Assert.True(resourceOverflow.ResourceHistoryUnavailable);
        Assert.Equal(200, resourceOverflow.CurrencyCharged);
        Assert.False(resourceOverflow.CurrencyAmountArithmeticUnavailable);
        CraftingStatisticsReducer.Validate(resourceOverflow);

        var currencyOverflow = SupportedAggregate();
        Apply(currencyOverflow, 1, long.MaxValue);
        Apply(currencyOverflow, 1, 1);
        Assert.Equal(2, currencyOverflow.CompletionActions);
        Assert.Equal(2, currencyOverflow.Resources["764"].ConsumedQuantity);
        Assert.Equal(long.MaxValue, currencyOverflow.CurrencyCharged);
        Assert.True(currencyOverflow.CurrencyAmountArithmeticUnavailable);
        Assert.True(currencyOverflow.CurrencyHistoryUnavailable);
        Assert.False(currencyOverflow.ResourceQuantityArithmeticUnavailable);
        CraftingStatisticsReducer.Validate(currencyOverflow);
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Persistence")]
    public void CurrentSchemaValidationRejectsExactZeroPairsAndAcceptsArithmeticUnavailablePairs()
    {
        var valid = SupportedAggregate();
        Apply(valid, 2, 150);

        var zeroResourceActions = CraftingStatisticsReducer.Clone(valid);
        zeroResourceActions.Outputs["131"].Recipes["1026"].Resources["764"].ConsumptionActions = 0;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(zeroResourceActions));

        var zeroResourceQuantity = CraftingStatisticsReducer.Clone(valid);
        zeroResourceQuantity.Resources["764"].ConsumedQuantity = 0;
        zeroResourceQuantity.Outputs["131"].Recipes["1026"].Resources["764"].ConsumedQuantity = 0;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(zeroResourceQuantity));

        var zeroLifetimeResourceWithQuantityUnavailable = CraftingStatisticsReducer.Clone(valid);
        zeroLifetimeResourceWithQuantityUnavailable.ResourceQuantityArithmeticUnavailable = true;
        zeroLifetimeResourceWithQuantityUnavailable.Resources["764"].ConsumedQuantity = 0;
        zeroLifetimeResourceWithQuantityUnavailable.Outputs["131"].Recipes["1026"].Resources["764"].ConsumedQuantity = 0;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(zeroLifetimeResourceWithQuantityUnavailable));

        var actionsExceedExactResourceQuantity = SupportedAggregate();
        Apply(actionsExceedExactResourceQuantity, 6, 150);
        Apply(actionsExceedExactResourceQuantity, 6, 150);
        actionsExceedExactResourceQuantity.Resources["764"].ConsumedQuantity = 1;
        actionsExceedExactResourceQuantity.Outputs["131"].Recipes["1026"].Resources["764"].ConsumedQuantity = 1;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(actionsExceedExactResourceQuantity));

        var actionsExceedExactCurrencyAmount = SupportedAggregate();
        Apply(actionsExceedExactCurrencyAmount, 2, 150);
        Apply(actionsExceedExactCurrencyAmount, 2, 150);
        actionsExceedExactCurrencyAmount.CurrencyCharged = 1;
        actionsExceedExactCurrencyAmount.Outputs["131"].CurrencyCharged = 1;
        actionsExceedExactCurrencyAmount.Outputs["131"].Recipes["1026"].CurrencyCharged = 1;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(actionsExceedExactCurrencyAmount));

        var saturatedActionsStillRequireExactCurrencyLowerBound =
            CraftingStatisticsReducer.Clone(actionsExceedExactCurrencyAmount);
        saturatedActionsStillRequireExactCurrencyLowerBound.CurrencyActionArithmeticUnavailable = true;
        Assert.Throws<ArgumentException>(() =>
            CraftingStatisticsReducer.Validate(saturatedActionsStillRequireExactCurrencyLowerBound));

        var zeroCurrencyActions = CraftingStatisticsReducer.Clone(valid);
        zeroCurrencyActions.CurrencyChargeActions = 0;
        zeroCurrencyActions.Outputs["131"].CurrencyChargeActions = 0;
        zeroCurrencyActions.Outputs["131"].Recipes["1026"].CurrencyChargeActions = 0;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(zeroCurrencyActions));

        var zeroCurrencyAmount = CraftingStatisticsReducer.Clone(valid);
        zeroCurrencyAmount.CurrencyCharged = 0;
        zeroCurrencyAmount.Outputs["131"].CurrencyCharged = 0;
        zeroCurrencyAmount.Outputs["131"].Recipes["1026"].CurrencyCharged = 0;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(zeroCurrencyAmount));

        var inverseUnavailableActions = CraftingStatisticsReducer.Clone(valid);
        inverseUnavailableActions.CurrencyActionArithmeticUnavailable = true;
        inverseUnavailableActions.CurrencyCharged = 0;
        inverseUnavailableActions.Outputs["131"].CurrencyCharged = 0;
        inverseUnavailableActions.Outputs["131"].Recipes["1026"].CurrencyCharged = 0;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(inverseUnavailableActions));

        var inverseUnavailableAmount = CraftingStatisticsReducer.Clone(valid);
        inverseUnavailableAmount.CurrencyAmountArithmeticUnavailable = true;
        inverseUnavailableAmount.CurrencyChargeActions = 0;
        inverseUnavailableAmount.Outputs["131"].CurrencyChargeActions = 0;
        inverseUnavailableAmount.Outputs["131"].Recipes["1026"].CurrencyChargeActions = 0;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(inverseUnavailableAmount));

        var degradedResourceSubset = CraftingStatisticsReducer.Clone(valid);
        degradedResourceSubset.Capabilities.ItemResourceIdentity = CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            "resource evidence degraded");
        degradedResourceSubset.Capabilities.OutputResourceAssociation = CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            "resource evidence degraded");
        degradedResourceSubset.Outputs["131"].Recipes["1026"].Resources["764"].ConsumedQuantity--;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(degradedResourceSubset));

        var degradedCurrencySubset = CraftingStatisticsReducer.Clone(valid);
        degradedCurrencySubset.Capabilities.CurrencyCharge = CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            "currency evidence degraded");
        degradedCurrencySubset.Outputs["131"].Recipes["1026"].CurrencyCharged--;
        Assert.Throws<ArgumentException>(() => CraftingStatisticsReducer.Validate(degradedCurrencySubset));

        var unavailableActions = SupportedAggregate();
        Assert.True(CraftingStatisticsReducer.Apply(
            unavailableActions,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "limit",
                    "limit",
                    "limit-recipe",
                    long.MaxValue,
                    long.MaxValue,
                    new() { ["1"] = long.MaxValue })])));
        ApplyDifferentOutput(unavailableActions, "next", "next-recipe", 2, 10);
        Assert.True(unavailableActions.CompletionArithmeticUnavailable);
        Assert.True(unavailableActions.ResourceActionArithmeticUnavailable);
        Assert.True(unavailableActions.CurrencyActionArithmeticUnavailable);
        Assert.Equal(0, unavailableActions.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumptionActions);
        Assert.Equal(2, unavailableActions.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumedQuantity);
        Assert.Equal(0, unavailableActions.Outputs["next"].CurrencyChargeActions);
        Assert.Equal(10, unavailableActions.Outputs["next"].CurrencyCharged);
        Assert.True(
            unavailableActions.Outputs["next"].CurrencyCharged
            > unavailableActions.Outputs["next"].CurrencyChargeActions);
        CraftingStatisticsReducer.Validate(unavailableActions);

        var unavailableAmounts = SupportedAggregate();
        ApplyDifferentOutput(unavailableAmounts, "limit", "limit-recipe", long.MaxValue, long.MaxValue);
        ApplyDifferentOutput(unavailableAmounts, "next", "next-recipe", 1, 1);
        Assert.True(unavailableAmounts.ResourceQuantityArithmeticUnavailable);
        Assert.True(unavailableAmounts.CurrencyAmountArithmeticUnavailable);
        Assert.Equal(1, unavailableAmounts.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumptionActions);
        Assert.Equal(0, unavailableAmounts.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumedQuantity);
        Assert.Equal(1, unavailableAmounts.Outputs["next"].CurrencyChargeActions);
        Assert.Equal(0, unavailableAmounts.Outputs["next"].CurrencyCharged);
        Assert.True(
            unavailableAmounts.Outputs["next"].CurrencyChargeActions
            > unavailableAmounts.Outputs["next"].CurrencyCharged);
        Assert.True(
            unavailableAmounts.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumptionActions
            > unavailableAmounts.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumedQuantity);
        CraftingStatisticsReducer.Validate(unavailableAmounts);

        Assert.True(CraftingStatisticsReducer.Apply(
            unavailableAmounts,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "new-resource",
                    "New Resource Output",
                    "new-resource-recipe",
                    1,
                    1,
                    new() { ["1"] = 1 },
                    resources: [new CraftingResourceMutation("765", "New Resource", 1, 2)])])));
        Assert.DoesNotContain("765", unavailableAmounts.Resources.Keys);
        var frozenNewResource =
            unavailableAmounts.Outputs["new-resource"].Recipes["new-resource-recipe"].Resources["765"];
        Assert.Equal(1, frozenNewResource.ConsumptionActions);
        Assert.Equal(0, frozenNewResource.ConsumedQuantity);
        CraftingStatisticsReducer.Validate(unavailableAmounts);

        var missingPositiveLifetimeResource = CraftingStatisticsReducer.Clone(unavailableAmounts);
        missingPositiveLifetimeResource.Outputs["new-resource"].Recipes["new-resource-recipe"].Resources["765"]
            .ConsumedQuantity = 1;
        Assert.Throws<ArgumentException>(() =>
            CraftingStatisticsReducer.Validate(missingPositiveLifetimeResource));

        var bothUnavailable = CraftingStatisticsReducer.Clone(unavailableActions);
        bothUnavailable.CurrencyAmountArithmeticUnavailable = true;
        CraftingStatisticsReducer.Validate(bothUnavailable);
    }

    [Fact]
    [Trait("Category", "M16")]
    public void UnprovenSuccessfulCostEvidenceMarksOnlyItsIndependentHistoryIncomplete()
    {
        var aggregate = SupportedAggregate();
        var current = CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money");
        current.ItemResourceIdentity = CraftingNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, "resource snapshot failed");
        current.OutputResourceAssociation = CraftingNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, "resource snapshot failed");
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(aggregate, current);

        Assert.True(CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "131",
                    "Audited Output",
                    "1026",
                    1,
                    1,
                    new() { ["1"] = 1 },
                    resourceEvidenceProven: false)])));

        Assert.True(aggregate.ResourceHistoryUnavailable);
        Assert.Equal("resource snapshot failed", aggregate.ResourceHistoryProvenance);
        Assert.False(aggregate.CurrencyHistoryUnavailable);
        Assert.Equal(1, aggregate.CompletionActions);
        Assert.Empty(aggregate.Resources);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    [Trait("Category", "M16")]
    public void UnprovenCurrencyEvidenceMarksOnlyCurrencyHistoryIncomplete()
    {
        var aggregate = SupportedAggregate();
        var current = CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money");
        current.CurrencyCharge = CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            "currency snapshot failed");
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(aggregate, current);

        Assert.True(CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "131",
                    "Audited Output",
                    "1026",
                    1,
                    1,
                    new() { ["1"] = 1 },
                    resources: [new CraftingResourceMutation("764", "Parts", 1, 2)],
                    currencyEvidenceProven: false)])));

        Assert.False(aggregate.ResourceHistoryUnavailable);
        Assert.Equal(2, aggregate.Resources["764"].ConsumedQuantity);
        Assert.True(aggregate.CurrencyHistoryUnavailable);
        Assert.Equal("currency snapshot failed", aggregate.CurrencyHistoryProvenance);
        Assert.Equal(0, aggregate.CurrencyChargeActions);
        Assert.Equal(0, aggregate.CurrencyCharged);
        Assert.Equal(1, aggregate.CompletionActions);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    [Trait("Category", "M16")]
    public void CompletionActionOverflowAlsoStopsDependentResourceAndCurrencyActionCounts()
    {
        var aggregate = SupportedAggregate();
        Assert.True(CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "existing",
                    "Existing",
                    "existing-recipe",
                    long.MaxValue,
                    long.MaxValue,
                    new() { ["1"] = long.MaxValue })])));

        Assert.True(CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "overflow",
                    "Overflow",
                    "overflow-recipe",
                    1,
                    1,
                    new() { ["1"] = 1 })])));

        Assert.True(aggregate.CompletionArithmeticUnavailable);
        Assert.False(aggregate.ResourceActionArithmeticUnavailable);
        Assert.False(aggregate.CurrencyActionArithmeticUnavailable);

        Assert.True(CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "next",
                    "Next",
                    "next-recipe",
                    1,
                    1,
                    new() { ["1"] = 1 },
                    resources: [new CraftingResourceMutation("764", "Parts", 1, 2)],
                    currencyChargeActions: 1,
                    currencyCharged: 10)])));

        Assert.True(aggregate.CompletionArithmeticUnavailable);
        Assert.True(aggregate.ResourceActionArithmeticUnavailable);
        Assert.True(aggregate.CurrencyActionArithmeticUnavailable);
        Assert.Equal(0, aggregate.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumptionActions);
        Assert.Equal(2, aggregate.Outputs["next"].Recipes["next-recipe"].Resources["764"].ConsumedQuantity);
        Assert.Equal(0, aggregate.Outputs["next"].CurrencyChargeActions);
        Assert.Equal(10, aggregate.Outputs["next"].CurrencyCharged);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    [Trait("Category", "M16")]
    [Trait("Category", "Performance")]
    public void OneHundredThousandCraftsCoalesceToBoundedAggregateStateWithoutRawHistory()
    {
        const int actionCount = 100_000;
        var boundary = new CraftingCompletionBoundary();
        var pending = new CraftingPendingAccumulator();
        for (var index = 0; index < actionCount; index++)
        {
            var token = boundary.Begin(Evidence(3, 0));
            Assert.True(boundary.TryComplete(token, "generation-1", Now, out var mutation));
            pending.Add(mutation);
            Assert.True(boundary.FinishPublication(token));
        }
        var aggregate = SupportedAggregate();

        Assert.True(pending.TryFlush(mutation => CraftingStatisticsReducer.Apply(aggregate, mutation)));

        Assert.Equal(actionCount, aggregate.CompletionActions);
        Assert.Equal(actionCount, aggregate.ProducedQuantity);
        Assert.Single(aggregate.Outputs);
        Assert.Single(aggregate.Resources);
        Assert.Equal(actionCount * 3L, aggregate.Resources["764"].ConsumedQuantity);
        var recipe = aggregate.Outputs["131"].Recipes["1026"];
        Assert.Equal(actionCount, recipe.Resources["764"].ConsumptionActions);
        Assert.Equal(actionCount * 3L, recipe.Resources["764"].ConsumedQuantity);
        Assert.True(pending.IsEmpty);
        Assert.Equal(0, boundary.OutstandingCount);
        CraftingStatisticsReducer.Validate(aggregate);
    }

    private static void Apply(CraftingStatisticsAggregate aggregate, long resourceQuantity, long currencyCharged)
    {
        Assert.True(CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    "131",
                    "Audited Output",
                    "1026",
                    1,
                    1,
                    new() { ["1"] = 1 },
                    resources: [new CraftingResourceMutation("764", "High-tier Parts", 1, resourceQuantity)],
                    currencyChargeActions: 1,
                    currencyCharged: currencyCharged)])));
    }

    private static void ApplyDifferentOutput(
        CraftingStatisticsAggregate aggregate,
        string outputItemId,
        string recipeId,
        long resourceQuantity,
        long currencyCharged)
    {
        Assert.True(CraftingStatisticsReducer.Apply(
            aggregate,
            new CraftingMutation(
                "generation-1",
                Now,
                [new CraftingMutationRow(
                    outputItemId,
                    outputItemId,
                    recipeId,
                    1,
                    1,
                    new() { ["1"] = 1 },
                    resources: [new CraftingResourceMutation("764", "High-tier Parts", 1, resourceQuantity)],
                    currencyChargeActions: 1,
                    currencyCharged: currencyCharged)])));
    }

    private static CraftingCompletionEvidence Evidence(long resourceQuantity, long currencyCharged) => new(
        "131",
        "Audited Output",
        "1026",
        1,
        [new CraftingResourceCostEvidence("764", "High-tier Parts", resourceQuantity)],
        currencyCharged);

    private static CraftingStatisticsAggregate SupportedAggregate()
    {
        var aggregate = new CraftingStatisticsAggregate();
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.Supported("delivery", "formula", "event items", "event money"));
        return aggregate;
    }

    private static ProfileRepository Repository(string root, params string[] ids)
    {
        var queue = new Queue<string>(ids);
        return new ProfileRepository(root, () => Now, () => queue.Dequeue());
    }

    private static SaveIdentitySnapshot Identity() => Identity(1, 100);

    private static SaveIdentitySnapshot Identity(int slot, long creationTicks) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = creationTicks,
        ObservedWriteUtcTicks = creationTicks,
        ObservedLength = 10,
        GameVersion = "2.3.30",
        ContentSha256 = creationTicks.ToString("x", CultureInfo.InvariantCulture).PadLeft(64, '0')
    };
}
