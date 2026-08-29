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

    private static SaveIdentitySnapshot Identity() => new()
    {
        Slot = 1,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = 100,
        ObservedWriteUtcTicks = 100,
        ObservedLength = 10,
        GameVersion = "2.3.30",
        ContentSha256 = 100.ToString("x", CultureInfo.InvariantCulture).PadLeft(64, '0')
    };
}
