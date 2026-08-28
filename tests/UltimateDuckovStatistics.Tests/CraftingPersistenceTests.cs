using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class CraftingPersistenceTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Persistence")]
    public void SchemaTwelveMigrationPreservesPriorDataAndMarksPreM13CraftingUnavailable()
    {
        var profile = Document("generation-1");
        profile.SchemaVersion = 12;
        profile.Statistics.SchemaVersion = 12;
        profile.Statistics.Overall.ActivationCount = 42;
        profile.Statistics.Crafting = null!;

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(15, profile.SchemaVersion);
        Assert.Equal(15, profile.Statistics.SchemaVersion);
        Assert.Equal(42, profile.Statistics.Overall.ActivationCount);
        Assert.True(profile.Statistics.Crafting.HistoricalUnavailable);
        Assert.Contains("predates M13", profile.Statistics.Crafting.HistoricalProvenance, StringComparison.Ordinal);
        Assert.Equal(0, profile.Statistics.Crafting.CompletionActions);
        Assert.Equal(0, profile.Statistics.Crafting.ProducedQuantity);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void DeferredCraftingMutationSurvivesInterruptedAndCleanRestartExactlyOnce()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var identity = Identity(1, 100);
        var first = Repository(temporaryDirectory.Path, "generation-1", "session-1");
        first.Open(identity);
        first.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));
        Assert.True(first.RecordCraftingDeferred(Mutation("generation-1", "100", "Bandage", "bandage", 3)));
        first.Flush();

        var interrupted = Repository(temporaryDirectory.Path, "unused-generation", "session-2");
        var open = interrupted.Open(identity);
        Assert.True(open.InterruptedSessionRecovered);
        AssertCrafting(interrupted.Current.Statistics.Crafting, 1, 3);
        interrupted.CloseClean();

        var clean = Repository(temporaryDirectory.Path, "unused-generation-2", "session-3");
        clean.Open(identity);
        AssertCrafting(clean.Current.Statistics.Crafting, 1, 3);
        clean.CloseClean();
    }

    [Fact]
    [Trait("Category", "M13")]
    [Trait("Category", "Persistence")]
    public void CapabilitySnapshotPersistsGenericAndCraftingDegradationInOneRevisionAcrossReopen()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var identity = Identity(1, 100);
        var first = Repository(temporaryDirectory.Path, "generation-1", "session-1");
        first.Open(identity);
        var supported = CraftingNativeContractPolicy.Supported("completion", "formula");
        PublishCapabilities(first, supported);
        var supportedRevision = first.Current.Revision;

        const string degradation = "Runtime crafting contract drifted.";
        var unavailable = CraftingNativeContractPolicy.Unavailable(degradation);
        PublishCapabilities(first, unavailable);

        Assert.Equal(supportedRevision + 1, first.Current.Revision);
        var firstCraftingRecords = first.Current.Capabilities
            .Where(capability => CraftingCapabilityIds.All.Contains(capability.AdapterId))
            .ToList();
        Assert.Equal(CraftingCapabilityIds.All.Count, firstCraftingRecords.Count);
        Assert.All(
            firstCraftingRecords,
            capability => Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capability.State));
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            first.Current.Statistics.Crafting.Capabilities.CompletionActions.State);

        var reopened = Repository(temporaryDirectory.Path, "session-2");
        var open = reopened.Open(identity);

        Assert.True(open.InterruptedSessionRecovered);
        var reopenedCraftingRecords = reopened.Current.Capabilities
            .Where(capability => CraftingCapabilityIds.All.Contains(capability.AdapterId))
            .ToList();
        Assert.Equal(CraftingCapabilityIds.All.Count, reopenedCraftingRecords.Count);
        Assert.All(
            reopenedCraftingRecords,
            capability => Assert.Equal(AdapterCapabilityState.DisabledIncompatible, capability.State));
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            reopened.Current.Statistics.Crafting.Capabilities.CompletionActions.State);
        Assert.Equal(degradation, reopened.Current.Statistics.Crafting.Capabilities.CompletionActions.Provenance);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SaveSlotAndGenerationReplacementKeepCraftingTotalsIndependent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>([
            "generation-slot-1", "session-slot-1",
            "generation-slot-2", "session-slot-2",
            "session-slot-1-reopen", "generation-slot-1-new", "session-slot-1-new"]);
        var repository = Repository(temporaryDirectory.Path, ids);
        repository.Open(Identity(1, 100));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));
        repository.RecordCraftingDeferred(Mutation("generation-slot-1", "100", "Bandage", "bandage", 2));
        repository.Flush();

        repository.Open(Identity(2, 200));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));
        AssertCrafting(repository.Current.Statistics.Crafting, 0, 0);
        repository.RecordCraftingDeferred(Mutation("generation-slot-2", "200", "Med Kit", "med_kit", 5));
        repository.Flush();

        repository.Open(Identity(1, 100));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));
        AssertCrafting(repository.Current.Statistics.Crafting, 1, 2);
        repository.Rotate(Identity(1, 300), "DuckovNewGame");
        AssertCrafting(repository.Current.Statistics.Crafting, 0, 0);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void CurrentSchemaMissingCraftingRootLosesPrimarySelectionToIntactBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(temporaryDirectory.Path, "profile.json");
        var backup = Document("generation-1");
        backup.Statistics.Crafting = Aggregate(1, 2);
        var invalid = Document("generation-1");
        invalid.Revision = 2;
        invalid.Statistics.Crafting = null!;
        store.Save(path, backup);
        store.Save(path, invalid);

        var loaded = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);

        Assert.Equal(AtomicJsonLoadSource.Backup, loaded.Source);
        AssertCrafting(loaded.Value!.Statistics.Crafting, 1, 2);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void CurrentSchemaBatchQuantityMismatchLosesPrimarySelectionToIntactBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(temporaryDirectory.Path, "profile.json");
        var backup = Document("generation-1");
        backup.Statistics.Crafting = Aggregate(1, 2);
        var invalid = Document("generation-1");
        invalid.Revision = 2;
        invalid.Statistics.Crafting = Aggregate(1, 2);
        invalid.Statistics.Crafting.Outputs["100"].Recipes["bandage"].BatchActions = new() { ["3"] = 1 };
        store.Save(path, backup);
        store.Save(path, invalid);

        var loaded = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);

        Assert.Equal(AtomicJsonLoadSource.Backup, loaded.Source);
        AssertCrafting(loaded.Value!.Statistics.Crafting, 1, 2);
        Assert.Equal(1, loaded.Value.Statistics.Crafting.Outputs["100"].Recipes["bandage"].BatchActions["2"]);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void CurrentSchemaMissingSupportedRecipeCompositionLosesPrimarySelectionToIntactBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(temporaryDirectory.Path, "profile.json");
        var backup = Document("generation-1");
        backup.Statistics.Crafting = Aggregate(1, 2);
        var invalid = Document("generation-1");
        invalid.Revision = 2;
        invalid.Statistics.Crafting = Aggregate(1, 2);
        invalid.Statistics.Crafting.Outputs["100"].Recipes.Clear();
        store.Save(path, backup);
        store.Save(path, invalid);

        var loaded = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);

        Assert.Equal(AtomicJsonLoadSource.Backup, loaded.Source);
        AssertCrafting(loaded.Value!.Statistics.Crafting, 1, 2);
        Assert.Equal("bandage", Assert.Single(loaded.Value.Statistics.Crafting.Outputs["100"].Recipes).Key);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void CurrentSchemaInvalidCraftingCapabilityStateLosesPrimarySelectionToIntactBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(temporaryDirectory.Path, "profile.json");
        var backup = Document("generation-1");
        backup.Statistics.Crafting = Aggregate(1, 2);
        var invalid = Document("generation-1");
        invalid.Revision = 2;
        invalid.Statistics.Crafting = Aggregate(1, 2);
        invalid.Statistics.Crafting.Capabilities.CompletionActions.State = (AdapterCapabilityState)999;
        store.Save(path, backup);
        store.Save(path, invalid);

        var loaded = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);

        Assert.Equal(AtomicJsonLoadSource.Backup, loaded.Source);
        AssertCrafting(loaded.Value!.Statistics.Crafting, 1, 2);
        Assert.Equal(AdapterCapabilityState.Supported, loaded.Value.Statistics.Crafting.Capabilities.CompletionActions.State);
    }

    private static ProfileRepository Repository(string root, params string[] ids) =>
        Repository(root, new Queue<string>(ids));

    private static ProfileRepository Repository(string root, Queue<string> ids) =>
        new(root, () => Now, () => ids.Dequeue());

    private static void PublishCapabilities(ProfileRepository repository, CraftingMetricCapabilities craftingCapabilities)
    {
        repository.SetCapabilitySnapshot(
            CraftingNativeContractPolicy.ToRecords(craftingCapabilities, "native-crafting/test"),
            EconomyNativeContractPolicy.Unavailable("not under test"),
            WorldTimeNativeContractPolicy.Unavailable("not under test"),
            craftingCapabilities);
    }

    private static SaveIdentitySnapshot Identity(int slot, long creationTicks) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = creationTicks,
        ObservedWriteUtcTicks = creationTicks,
        ObservedLength = 10,
        GameVersion = "2.3.30",
        ContentSha256 = creationTicks.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0')
    };

    private static ProfileDocument Document(string generationId) => new()
    {
        GenerationId = generationId,
        Slot = 1,
        GenerationReason = "test",
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Identity = Identity(1, 100),
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = generationId,
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Holdings = new EconomyHoldingsSnapshot { SaveGenerationId = generationId }
        }
    };

    private static CraftingMutation Mutation(
        string generationId,
        string itemId,
        string displayName,
        string recipeId,
        long quantity) => new(
            generationId,
            Now,
            [new CraftingMutationRow(itemId, displayName, recipeId, 1, quantity, new() { [quantity.ToString(System.Globalization.CultureInfo.InvariantCulture)] = 1 })]);

    private static CraftingStatisticsAggregate Aggregate(long actions, long quantity)
    {
        var aggregate = new CraftingStatisticsAggregate();
        CraftingStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            CraftingNativeContractPolicy.Supported("completion", "formula"));
        CraftingStatisticsReducer.Apply(aggregate, Mutation("generation-1", "100", "Bandage", "bandage", quantity));
        Assert.Equal(actions, aggregate.CompletionActions);
        return aggregate;
    }

    private static void AssertCrafting(CraftingStatisticsAggregate value, long actions, long quantity)
    {
        Assert.Equal(actions, value.CompletionActions);
        Assert.Equal(quantity, value.ProducedQuantity);
    }
}
