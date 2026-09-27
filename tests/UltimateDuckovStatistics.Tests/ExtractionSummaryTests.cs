using Duckov.Economy;
using Duckov.Scenes;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class ExtractionSummaryTests
{
    [Fact]
    public void SnapshotCopiesOnlyMatchedTotalsAndSubtractsStartingValue()
    {
        var session = new ExtractionSummarySession();
        session.Begin("g", "r", 500);
        session.ObserveTerminal(RunOutcome.Extracted, 725.5m);
        var run = Run();
        session.Complete(run);
        var snapshot = Assert.IsType<ExtractionSummarySnapshot>(session.Completed);
        Assert.Equal(225.5m, snapshot.EstimatedNetValue);
        Assert.Equal(3, snapshot.Kills);
        Assert.Equal(62, snapshot.ActiveSeconds);
        run.CombatStatistics.Totals.KillsByYou = 999;
        Assert.Equal(3, snapshot.Kills);
        Assert.Equal(["00:01:02", "3", 225.5m.ToString("+#,0.##;-#,0.##;0", System.Globalization.CultureInfo.CurrentCulture)], ExtractionSummaryPresentation.Values(snapshot, key => key));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void WrongProfileOrRunCannotPublish(bool wrongProfile, bool wrongRun)
    {
        var session = new ExtractionSummarySession();
        session.Begin("g", "r", 0);
        session.ObserveTerminal(RunOutcome.Extracted, 1);
        var run = Run();
        if (wrongProfile) run.SaveGenerationId = "other";
        if (wrongRun) run.RunId = "other";
        session.Complete(run);
        Assert.Null(session.Completed);
    }

    [Fact]
    public void ResetAndNewRunNeverReuseAnOldResultOrBaseline()
    {
        var session = new ExtractionSummarySession();
        session.Begin("g", "r", 100);
        session.ObserveTerminal(RunOutcome.Extracted, 200);
        session.Complete(Run());
        session.Clear();
        session.Complete(Run());
        Assert.Null(session.Completed);
        session.Begin("g", "r2", null);
        var next = Run(); next.RunId = "r2";
        session.ObserveTerminal(RunOutcome.Extracted, 500);
        session.Complete(next);
        Assert.Null(session.Completed!.EstimatedNetValue);
    }

    [Fact]
    public void DeathNeverTreatsPreDeathGearAsExtractedAndPartialKillsStayPartial()
    {
        var session = new ExtractionSummarySession();
        session.Begin("g", "r", 100);
        session.ObserveTerminal(RunOutcome.Died, 9999);
        var run = Run(); run.Outcome = RunOutcome.Died;
        run.CombatStatistics.Capabilities.KillsByYou.State = AdapterCapabilityState.DisabledIncompatible;
        session.Complete(run);
        Assert.Null(session.Completed!.EstimatedNetValue);
        Assert.Equal("3 (partial)", ExtractionSummaryPresentation.Values(session.Completed,
            key => key == "ui.results_partial" ? "partial" : key)[1]);
        Assert.Equal("ui.results_death_value", ExtractionSummaryPresentation.Note(session.Completed, key => key));
    }

    [Fact]
    public void ZeroOnlyShownWhenProvenAndMissingTerminalNeverPublishes()
    {
        var session = new ExtractionSummarySession();
        session.Begin("g", "r", 0);
        var run = Run(); run.CombatStatistics.Totals.KillsByYou = 0;
        session.Complete(run);
        Assert.Null(session.Completed);
        session.ObserveTerminal(RunOutcome.Extracted, 0);
        session.Complete(run);
        Assert.Equal("0", ExtractionSummaryPresentation.Values(session.Completed, key => key)[1]);
        Assert.Equal(0, session.Completed!.EstimatedNetValue);
        run.CombatStatistics.Capabilities.KillsByYou.State = AdapterCapabilityState.DisabledIncompatible;
        session.Begin("g", "r", null);
        session.ObserveTerminal(RunOutcome.Extracted, null);
        session.Complete(run);
        Assert.Equal("ui.unavailable", ExtractionSummaryPresentation.Values(session.Completed, key => key)[1]);
    }

    [Fact]
    public void TerminalRetryDoesNotReplaceOriginalValuation()
    {
        var session = new ExtractionSummarySession();
        session.Begin("g", "r", 200);
        session.ObserveTerminal(RunOutcome.Extracted, 100);
        session.ObserveTerminal(RunOutcome.Extracted, 10000);
        session.Complete(Run());
        Assert.Equal(-100, session.Completed!.EstimatedNetValue);
    }

    [Fact]
    public void ValuationCountsEquipmentAmmoAttachmentsPetCashAndWalletOnce()
    {
        var character = new Item { Value = 999999, Inventory = new Inventory() };
        var gun = new Item { Value = 100, Stackable = false, Inventory = new Inventory() };
        gun.Inventory.Content.Add(new Item { Value = 2, StackCount = 30 }); // loaded ammunition
        gun.Slots.Add(new Slot { Content = new Item { Value = 20, Stackable = false } });
        character.Slots.Add(new Slot { Content = gun });
        character.Inventory.Content.Add(new Item { TypeID = EconomyManager.CashItemID, Value = 2, StackCount = 15 });
        character.Inventory.Content.Add(new Item { Value = 100, Stackable = false, UseDurability = true, MaxDurability = 100, Durability = 50 });
        var pet = new Inventory();
        pet.Content.Add(new Item { Value = 40, StackCount = 2 });
        // Character root is not loot; descendants total 340 raw -> 170 estimated, plus 200 wallet.
        Assert.Equal(370, NativeExtractionValuation.ReadOwnedTree(character, pet, 200));
        gun.Inventory.Content.Clear();
        character.Inventory.Content.Add(new Item { Value = 2, StackCount = 30 });
        Assert.Equal(370, NativeExtractionValuation.ReadOwnedTree(character, pet, 200)); // unloading is not profit
    }

    [Fact]
    public void PartialStackDurabilityUsesNativePerUnitFloorAndCheckedWideTotals()
    {
        var character = new Item { Inventory = new Inventory() };
        character.Inventory.Content.Add(new Item { Value = 11, StackCount = 3, UseDurability = true, MaxDurability = 10, Durability = 5 });
        Assert.Equal(7.5m, NativeExtractionValuation.ReadOwnedTree(character, new Inventory(), 0));
        character.Inventory.Content.Clear();
        character.Inventory.Content.Add(new Item { Value = int.MaxValue, StackCount = 100 });
        Assert.True(NativeExtractionValuation.ReadOwnedTree(character, new Inventory(), 0) > int.MaxValue);
    }

    [Fact]
    public void DuplicateCyclesUnhydratedAndInvalidValueCannotProducePartialNumericEstimate()
    {
        var character = new Item { Inventory = new Inventory() };
        character.Inventory.Content.Add(character);
        Assert.Throws<InvalidOperationException>(() => NativeExtractionValuation.ReadOwnedTree(character, new Inventory(), 0));
        character.Inventory.Content.Clear();
        character.Inventory.Content.Add(new Item { Value = 100, GunSetting = new ItemSetting_Gun { LoadingBullets = true } });
        Assert.Throws<InvalidOperationException>(() => NativeExtractionValuation.ReadOwnedTree(character, new Inventory(), 0));
        character.Inventory.Content.Clear(); character.Inventory.Loading = true;
        Assert.Throws<InvalidOperationException>(() => NativeExtractionValuation.ReadOwnedTree(character, new Inventory(), 0));
        character.Inventory.Loading = false;
        character.Inventory.Content.Add(new Item { Value = -1 });
        Assert.Throws<InvalidOperationException>(() => NativeExtractionValuation.ReadOwnedTree(character, new Inventory(), 0));
    }

    [Fact]
    public void NativeReadExcludesStashAndRequiresAllCarriedRoots()
    {
        Reset();
        try
        {
            var main = new CharacterMainControl { IsMainCharacter = true, CharacterItem = new Item { Inventory = new Inventory() } };
            CharacterMainControl.Main = main;
            LevelManager.Instance = new LevelManagerInstance { MainCharacter = main, PetProxy = new PetProxy { Inventory = new Inventory() } };
            EconomyManager.Instance = new EconomyManager(); EconomyManager.Money = 100;
            PlayerStorage.Inventory = new Inventory();
            PlayerStorage.Inventory.Content.Add(new Item { Value = 1000000 });
            Assert.Equal(100, NativeExtractionValuation.Read(_ => { }));
            LevelManager.Instance.PetProxy.Inventory.Loading = true;
            Assert.Null(NativeExtractionValuation.Read(_ => { }));
        }
        finally { Reset(); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionLifecycleCapturesFreshStartOnceAndFreezesTerminalBeforeDurabilityRetry(bool failTerminal)
    {
        using var harness = new Harness();
        var starts = new List<bool>(); var terminals = 0; var completed = new List<RunSummary>();
        harness.Lifecycle.ConfigureResults((_, _, fresh) => starts.Add(fresh), _ => terminals++, completed.Add, () => { });
        RaidUtilities.RaiseNewRaid(new RaidUtilities.RaidInfo { ID = 1, valid = true });
        harness.Lifecycle.Tick();
        harness.Now = 2;
        harness.Lifecycle.Tick();
        harness.Now = 5;
        harness.Persist = !failTerminal;
        LevelManager.RaiseEvacuated();
        Assert.Equal([true], starts);
        Assert.Equal(1, terminals);
        if (failTerminal)
        {
            Assert.Empty(completed);
            harness.Persist = true;
            harness.Now = 7;
            harness.Lifecycle.Tick();
        }
        Assert.Equal(5, Assert.Single(completed).ActiveDurationSeconds);
        Assert.Equal(1, terminals);
        Assert.Equal([true], starts);
    }

    [Fact]
    public void MidRaidActivationCannotInventFreshValuationAndProfileChangeClearsResults()
    {
        using var harness = new Harness();
        var fresh = true; var resets = 0;
        harness.Lifecycle.ConfigureResults((_, _, observed) => fresh = observed, _ => { }, _ => { }, () => resets++);
        harness.Lifecycle.Tick();
        Assert.False(fresh);
        harness.Lifecycle.InterruptForProfileTransition();
        Assert.Equal(1, resets);
    }

    [Fact]
    public void DeathSummaryWaitsUntilPostHurtTickAndObserverFailureCannotBlockRun()
    {
        using var harness = new Harness();
        var completed = new List<RunSummary>();
        harness.Lifecycle.ConfigureResults((_, _, _) => { }, _ => throw new IOException("UI failure"), completed.Add, () => { });
        harness.Lifecycle.Tick();
        RaidUtilities.RaiseRaidEnd(dead: true); RaidUtilities.RaiseRaidDead(); LevelManager.RaiseMainCharacterDead();
        Assert.Empty(completed);
        harness.Now = 3;
        harness.Lifecycle.Tick();
        Assert.Equal(RunOutcome.Died, Assert.Single(completed).Outcome);
        Assert.False(harness.Lifecycle.IsActive);
    }

    [Fact]
    public void NewRaidWhilePreviousCompletionIsBlockedCannotClaimAnOnTimeValueBaseline()
    {
        using var harness = new Harness();
        var starts = new List<bool>();
        harness.Lifecycle.ConfigureResults((_, _, fresh) => starts.Add(fresh), _ => { }, _ => { }, () => { });
        RaidUtilities.RaiseNewRaid(new RaidUtilities.RaidInfo { ID = 1, valid = true });
        harness.Lifecycle.Tick();
        harness.Persist = false; harness.Now = 2;
        LevelManager.RaiseEvacuated();
        RaidUtilities.RaiseNewRaid(new RaidUtilities.RaidInfo { ID = 2, valid = true });
        harness.Persist = true; harness.Now = 5;
        harness.Lifecycle.Tick();
        harness.Now = 6;
        harness.Lifecycle.Tick();
        Assert.Equal([true, false], starts);
    }

    private static RunSummary Run() => new()
    {
        RunId = "r",
        SaveGenerationId = "g",
        NativeRaidId = "1",
        Outcome = RunOutcome.Extracted,
        ActiveDurationSeconds = 62,
        LifecycleCapability = AdapterCapabilityState.Supported,
        CombatStatistics = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals { KillsByYou = 3 },
            Capabilities = new CombatMetricCapabilities { KillsByYou = new MetricAvailability { State = AdapterCapabilityState.Supported } }
        }
    };

    private static void Reset()
    {
        CharacterMainControl.ResetNativeState(); LevelManager.ResetNativeState(); RaidUtilities.ResetNativeState();
        EconomyManager.ResetNativeState(); ItemUtilities.ResetNativeState(); SceneLoader.ResetNativeState();
        MultiSceneCore.Instance = null; MultiSceneCore.MainSceneID = "test-map"; MultiSceneCore.ActiveSubSceneID = "";
        InputManager.InputActived = true; GameManager.Paused = false;
        NativeRaidContext.GameplayContext = GameplayContext.Unknown; Application.version = "2.3.30";
    }

    private sealed class Harness : IDisposable
    {
        public NativeRunLifecycleAdapter Lifecycle { get; }
        public double Now { get; set; }
        public bool Persist { get; set; } = true;

        public Harness()
        {
            Reset();
            NativeRaidContext.GameplayContext = GameplayContext.Raid;
            var main = new CharacterMainControl { IsMainCharacter = true, CharacterItem = new Item() };
            CharacterMainControl.Main = main; LevelManager.Instance = new LevelManagerInstance { MainCharacter = main };
            RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };
            Lifecycle = new NativeRunLifecycleAdapter(() => "g", _ => Persist, _ => true, _ => { }, _ => { },
                monotonicSecondsProvider: () => Now);
            Lifecycle.Initialize();
        }

        public void Dispose() { Persist = true; Lifecycle.Dispose(); Reset(); }
    }
}
