using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class EconomyHoldingsTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 8, 28, 8, 0, 0, DateTimeKind.Utc);

    public EconomyHoldingsTests() => ResetNative();
    public void Dispose() => ResetNative();

    [Fact]
    [Trait("Category", "M15")]
    public void ProvenZeroIsCurrentAndParticipatesInLiquidWealth()
    {
        var holdings = Supported("generation-1");

        Assert.True(EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, 0, 0, "test")));

        var projection = EconomyHoldingsReducer.Project(holdings);
        Assert.Equal(EconomyHoldingObservationState.Current, projection.Money.State);
        Assert.Equal(0, projection.Money.Value);
        Assert.Equal(EconomyHoldingObservationState.Current, projection.Cash.State);
        Assert.Equal(0, projection.Cash.Value);
        Assert.Equal(EconomyHoldingObservationState.Current, projection.LiquidWealth.State);
        Assert.Equal(0, projection.LiquidWealth.Value);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void LiquidWealthRequiresBothComponentsToBeCurrent()
    {
        var holdings = Supported("generation-1");
        EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, 125, null, "money only"));

        var projection = EconomyHoldingsReducer.Project(holdings);

        Assert.Equal(125, projection.Money.Value);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, projection.Cash.State);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, projection.LiquidWealth.State);
        Assert.Null(projection.LiquidWealth.Value);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void LiquidWealthOverflowIsUnavailableWithoutChangingComponents()
    {
        var holdings = Supported("generation-1");
        EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, long.MaxValue, 1, "overflow"));

        var projection = EconomyHoldingsReducer.Project(holdings);

        Assert.Equal(long.MaxValue, projection.Money.Value);
        Assert.Equal(1, projection.Cash.Value);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, projection.LiquidWealth.State);
        Assert.Contains("exceeded Int64", projection.LiquidWealth.FreshnessProvenance, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void PersistedCurrentObservationsDowngradeToLastObservedOnRestart()
    {
        var holdings = Supported("generation-1");
        EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, 100, 20, "current process"));

        Assert.True(EconomyHoldingsReducer.NormalizePersisted(
            holdings,
            "generation-1",
            downgradeCurrent: true));

        Assert.Equal(EconomyHoldingObservationState.LastObserved, holdings.Money.State);
        Assert.Equal(EconomyHoldingObservationState.LastObserved, holdings.Cash.State);
        Assert.Equal(100, holdings.Money.Value);
        Assert.Equal(20, holdings.Cash.Value);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, EconomyHoldingsReducer.Project(holdings).LiquidWealth.State);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void SceneOrProfileBoundaryRetainsExactValueAsLastObserved()
    {
        var holdings = Supported("generation-1");
        EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, 100, 20, "current process"));

        Assert.True(EconomyHoldingsReducer.MarkNotCurrent(
            holdings,
            "generation-1",
            money: true,
            cash: true,
            "scene loading"));

        Assert.Equal(EconomyHoldingObservationState.LastObserved, holdings.Money.State);
        Assert.Equal(100, holdings.Money.Value);
        Assert.Equal("scene loading", holdings.Money.FreshnessProvenance);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void SameCurrentTotalDoesNotCreateAFalseHoldingChange()
    {
        var holdings = Supported("generation-1");
        Assert.True(EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, 100, 20, "initial")));

        Assert.False(EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now.AddSeconds(1), null, 20, "internal transfer")));

        Assert.Equal(Now, holdings.Cash.ObservedUtc);
        Assert.Contains("initial", holdings.Cash.ObservationProvenance, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void DifferentGenerationObservationIsRejected()
    {
        var holdings = Supported("generation-1");

        Assert.Throws<InvalidOperationException>(() => EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-2", Now, 10, 5, "wrong generation")));
    }

    [Fact]
    [Trait("Category", "M15")]
    public void UnavailableObservationCannotExposeAStaleValueInRecovery()
    {
        var holdings = Supported("generation-1");
        holdings.Money.Value = 99;

        var exception = Assert.Throws<ArgumentException>(() =>
            EconomyHoldingsReducer.ValidateRecoveryCandidate(holdings, "generation-1"));

        Assert.Contains("stale value", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Recovery")]
    public void CurrentObservationWithoutSupportedCapabilityIsRejected()
    {
        var holdings = Supported("generation-1");
        EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, 10, null, "native"));
        holdings.Capabilities.Money = EconomyHoldingsNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            "drift");

        var exception = Assert.Throws<ArgumentException>(() =>
            EconomyHoldingsReducer.ValidateRecoveryCandidate(holdings, "generation-1"));

        Assert.Contains("no supported current capability", exception.Message, StringComparison.Ordinal);
        Assert.Equal("Unavailable (unsupported)", UI.UiText.FormatHolding(holdings.Money, holdings.Capabilities.Money));
    }

    [Fact]
    [Trait("Category", "M15")]
    public void SchemaFourteenMigrationDoesNotReconstructHoldingsFromM9Flows()
    {
        var profile = new ProfileDocument
        {
            SchemaVersion = 14,
            GenerationId = "generation-1",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Statistics = new ProfileStatistics
            {
                SchemaVersion = 14,
                SaveGenerationId = "generation-1",
                CreatedUtc = Now,
                UpdatedUtc = Now
            }
        };
        profile.Statistics.Economy.Currencies[CurrencyKind.Money.ToString()] = new CurrencyEconomyAggregate
        {
            Currency = CurrencyKind.Money,
            Totals = new CurrencyFlowTotals { GrossInflow = 999 }
        };
        profile.Statistics.Economy.Currencies[CurrencyKind.Cash.ToString()] = new CurrencyEconomyAggregate
        {
            Currency = CurrencyKind.Cash,
            Totals = new CurrencyFlowTotals { GrossInflow = 888 }
        };

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(15, profile.SchemaVersion);
        Assert.True(profile.Statistics.Holdings.HistoricalUnavailable);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, profile.Statistics.Holdings.Money.State);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, profile.Statistics.Holdings.Cash.State);
        Assert.Null(profile.Statistics.Holdings.Money.Value);
        Assert.Null(profile.Statistics.Holdings.Cash.Value);
        Assert.Contains("not reconstructed", profile.Statistics.Holdings.HistoricalProvenance, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void CashBuilderSumsExactTopLevelRootsAndDeduplicatesIdentityOverlap()
    {
        var roots = ConfigureReadyRoots();
        roots.Main.Content.Add(Cash(10));
        roots.Storage.Content.Add(Cash(20));
        var overlapping = Cash(30);
        roots.Storage.Content.Add(overlapping);
        roots.Pet.Content.Add(overlapping);
        roots.Pet.Content.Add(new Item { TypeID = 999, StackCount = 500 });
        var nestedOnly = Cash(1000);
        roots.Main.Content.Add(new Item
        {
            TypeID = 800,
            StackCount = 1,
            Inventory = new Inventory()
        });
        roots.Main.Content[^1].Inventory!.Content.Add(nestedOnly);

        Assert.True(NativeEconomyHoldingsSnapshotBuilder.TryReadCash(out var value, out var reason), reason);
        Assert.Equal(60, value);
    }

    [Theory]
    [Trait("Category", "M15")]
    [InlineData("main")]
    [InlineData("storage")]
    [InlineData("pet")]
    public void CashBuilderKeepsMissingOrHydratingRootUnavailable(string root)
    {
        var roots = ConfigureReadyRoots();
        if (root == "main") roots.Main.Loading = true;
        if (root == "storage") PlayerStorage.Loading = true;
        if (root == "pet") roots.Pet.Loading = true;

        Assert.False(NativeEconomyHoldingsSnapshotBuilder.TryReadCash(out var value, out var reason));
        Assert.Equal(0, value);
        Assert.Contains("hydrat", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void CashBuilderReportsAProvenEmptySetAsZero()
    {
        ConfigureReadyRoots();

        Assert.True(NativeEconomyHoldingsSnapshotBuilder.TryReadCash(out var value, out var reason), reason);
        Assert.Equal(0, value);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void CashBuilderFailsClosedAtIndependentInventoryBound()
    {
        var roots = ConfigureReadyRoots();
        for (var index = 0; index <= NativeEconomyHoldingsSnapshotBuilder.MaximumItemsPerOwnedInventory; index++)
            roots.Main.Content.Add(new Item { TypeID = 999, StackCount = 1 });

        Assert.False(NativeEconomyHoldingsSnapshotBuilder.TryReadCash(out _, out var reason));
        Assert.Contains("defensive", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void MoneyBuilderDistinguishesMissingManagerFromProvenZero()
    {
        Assert.False(NativeEconomyHoldingsSnapshotBuilder.TryReadMoney(out var missing, out _));
        Assert.Equal(0, missing);

        Duckov.Economy.EconomyManager.Instance = new Duckov.Economy.EconomyManager();
        Duckov.Economy.EconomyManager.Money = 0;

        Assert.True(NativeEconomyHoldingsSnapshotBuilder.TryReadMoney(out var current, out var reason), reason);
        Assert.Equal(0, current);
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Export")]
    public void JsonAndFlattenedCsvExportCurrentHoldingsAndCheckedLiquidWealth()
    {
        var profile = CurrentProfile("generation-1");
        profile.Statistics.Holdings = Supported("generation-1");
        EconomyHoldingsReducer.Apply(
            profile.Statistics.Holdings,
            new EconomyHoldingsMutation("generation-1", Now, 40, 2, "export"));

        var bundle = StatisticsExporter.Create(profile, Now);

        Assert.Contains("\"Holdings\"", bundle.Json, StringComparison.Ordinal);
        Assert.Contains("\"LiquidWealth\"", bundle.Json, StringComparison.Ordinal);
        Assert.Contains("\"Value\":42", bundle.Json, StringComparison.Ordinal);
        Assert.Contains("money_state,money_value", bundle.EconomyHoldingsCsv, StringComparison.Ordinal);
        Assert.Contains("generation-1,Current,40", bundle.EconomyHoldingsCsv, StringComparison.Ordinal);
        Assert.Contains(",Current,42,", bundle.EconomyHoldingsCsv, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Export")]
    public void UnavailableExportLeavesValuesBlankRatherThanWritingZero()
    {
        var profile = CurrentProfile("generation-1");
        profile.Statistics.Holdings = Supported("generation-1");

        var csv = StatisticsExporter.Create(profile, Now).EconomyHoldingsCsv;
        var data = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[1];

        Assert.StartsWith("generation-1,Unavailable,,", data, StringComparison.Ordinal);
        Assert.Contains(",Unavailable,,,", data, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Performance")]
    public void DirtySignalsCoalesceUntilTheNextTickAndRemainIndependent()
    {
        var gate = new EconomyHoldingsObservationGate();

        gate.SignalCash();
        gate.SignalCash();
        Assert.True(gate.HasPending);
        Assert.False(gate.IsCashDue(force: false));
        Assert.True(gate.IsCashDue(force: true));
        Assert.False(gate.IsMoneyDue(force: true));

        gate.Advance();
        Assert.True(gate.IsCashDue(force: false));
        gate.ClearCash();
        Assert.False(gate.HasPending);

        gate.SignalMoney();
        Assert.False(gate.IsMoneyDue(force: false));
        gate.Advance();
        Assert.True(gate.IsMoneyDue(force: false));
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "UI")]
    public void TemporaryUiDistinguishesUnavailableZeroCurrentAndLastObserved()
    {
        var capability = EconomyHoldingsNativeContractPolicy.Supported("money", "cash", "liquid").Money;
        var holdings = Supported("generation-1");

        Assert.Equal("Unavailable", UI.UiText.FormatHolding(holdings.Money, capability));
        EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation("generation-1", Now, 0, null, "ui"));
        Assert.Equal("0 (current)", UI.UiText.FormatHolding(holdings.Money, capability));
        EconomyHoldingsReducer.MarkNotCurrent(
            holdings,
            "generation-1",
            money: true,
            cash: false,
            "restart");
        Assert.Contains("0 (last observed", UI.UiText.FormatHolding(holdings.Money, capability), StringComparison.Ordinal);
    }

    private static EconomyHoldingsSnapshot Supported(string generation) => new()
    {
        SaveGenerationId = generation,
        Capabilities = EconomyHoldingsNativeContractPolicy.Supported("money", "cash", "liquid")
    };

    private static ProfileDocument CurrentProfile(string generation) => new()
    {
        GenerationId = generation,
        Slot = 1,
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = generation,
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Holdings = new EconomyHoldingsSnapshot { SaveGenerationId = generation }
        }
    };

    private static Item Cash(int count) => new()
    {
        TypeID = Duckov.Economy.EconomyManager.CashItemID,
        StackCount = count
    };

    private static (Inventory Main, Inventory Storage, Inventory Pet) ConfigureReadyRoots()
    {
        var mainInventory = new Inventory();
        var storageInventory = new Inventory();
        var petInventory = new Inventory();
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = new Item { Inventory = mainInventory }
        };
        var petProxy = new PetProxy { Inventory = petInventory };
        CharacterMainControl.Main = main;
        PlayerStorage.Inventory = storageInventory;
        PetProxy.PetInventory = petInventory;
        LevelManager.Instance = new LevelManagerInstance
        {
            MainCharacter = main,
            PetProxy = petProxy
        };
        LevelManager.LevelInited = true;
        LevelManager.LevelInitializing = false;
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
        return (mainInventory, storageInventory, petInventory);
    }

    private static void ResetNative()
    {
        CharacterMainControl.ResetNativeState();
        PlayerStorage.ResetNativeState();
        LevelManager.ResetNativeState();
        PetProxy.PetInventory = null;
        Duckov.Economy.EconomyManager.ResetNativeState();
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
    }
}
