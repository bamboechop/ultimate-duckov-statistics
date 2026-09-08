using Duckov.Economy;
using Duckov.Scenes;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeTutorialRunTests
{
    [Theory]
    [InlineData("Level_Guide_1")]
    [InlineData("Level_Guide_2")]
    public void FirstTutorialRetainsItemsCashMovementAndExtractionThroughExportAndReopen(string subSceneId)
    {
        using var h = new Harness();
        MultiSceneCore.ActiveSubSceneID = subSceneId;
        h.Lifecycle.Initialize();
        h.Lifecycle.Tick();
        h.Lifecycle.Tick();
        Assert.True(h.Lifecycle.IsActive);
        var id = h.Lifecycle.CurrentRunId;
        var generation = h.Coordinator.CurrentGenerationId;
        Assert.False(RaidUtilities.CurrentRaid.valid);
        Assert.Null(Assert.Single(h.Checkpoints).NativeRaidId);
        foreach (var itemId in new[] { "403", "10" })
        {
            var use = new ItemUseRecorded
            {
                EventId = "use-" + itemId,
                TimestampUtc = DateTime.UtcNow,
                SaveGenerationId = generation,
                RunId = id,
                MapId = h.Lifecycle.CurrentMapId,
                SegmentId = h.Lifecycle.CurrentSegmentId,
                GameplayContext = GameplayContext.Raid,
                ItemId = itemId,
                DisplayName = itemId,
                Group = CanonicalItemGroup.OtherUnknown,
                ActivationCount = 1,
                AmountConsumed = 1,
                ConsumptionUnit = ConsumptionUnit.Item
            };
            Assert.True(ItemUsePublication.PublishIndependently(
                () => h.Coordinator.HandleItemUse(new ItemUseCompletion(ItemUseCompletionDisposition.Counted, use)),
                () => h.Lifecycle.RecordItemUse(use)));
        }
        ItemUtilities.OwnedItems.Add(new Item { TypeID = EconomyManager.CashItemID, StackCount = 55 });
        ItemUtilities.RaisePlayerItemOperation();
        h.Economy.Tick();
        Assert.Equal(GameplayContext.Raid, Assert.Single(h.Flows).GameplayContext);
        h.Now = 0.2;
        h.Main.transform.position = new Vector3(1, 0, 0);
        h.Lifecycle.Tick();
        h.Now = 10;
        LevelManager.RaiseEvacuated();
        for (var frame = 0; frame < 10; frame++) h.Lifecycle.Tick();
        Assert.False(h.Lifecycle.IsActive);
        LevelManager.RaiseEvacuated();
        SceneLoader.RaiseStarted();
        NativeRaidContext.GameplayContext = GameplayContext.Base;
        RaidUtilities.RaiseRaidEnd();
        SceneLoader.RaiseFinished();
        h.Lifecycle.Tick();

        void AssertRun(RunSummary run)
        {
            Assert.Equal(id, run.RunId);
            Assert.Null(run.NativeRaidId);
            Assert.Equal(RunOutcome.Extracted, run.Outcome);
            Assert.Equal("duckov:map:" + subSceneId, run.StartingMapId);
            Assert.Equal(10, run.ActiveDurationSeconds);
            Assert.Equal(1, run.PhysicalDistance);
            Assert.Equal(2, run.ItemStatistics.Overall.ActivationCount);
            Assert.Equal(55, run.Economy.Currencies["Cash"].Totals.GrossInflow);
            Assert.Single(run.Segments);
        }
        AssertRun(Assert.Single(h.Coordinator.Current!.Statistics.Runs));
        var export = h.Coordinator.ExportCurrent();
        var json = new AtomicJsonStore<StatisticsExportDocument>().Load(Path.Combine(export.Directory, "statistics.json"));
        AssertRun(Assert.Single(json.Value!.Runs));
        var rows = File.ReadAllLines(Path.Combine(export.Directory, "runs.csv"));
        Assert.Equal(2, rows.Length);
        Assert.StartsWith($"{id},{generation},,duckov:map:{subSceneId},", rows[1], StringComparison.Ordinal);
        h.Lifecycle.Dispose();
        h.Economy.Dispose();
        h.Coordinator.Dispose();
        using var reopened = new NativeProfileCoordinator();
        reopened.Initialize();
        Assert.Equal(generation, reopened.CurrentGenerationId);
        AssertRun(Assert.Single(reopened.Current!.Statistics.Runs));
        Assert.Equal(2, reopened.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(55, reopened.Current.Statistics.Economy.Currencies["Cash"].Totals.GrossInflow);
    }

    [Fact]
    public void TutorialSubsceneTransitionPreservesOneRunAndExcludesLoadingAndPlacement()
    {
        using var h = new Harness();
        h.Lifecycle.Tick();
        Assert.True(h.Lifecycle.IsActive);
        var id = h.Lifecycle.CurrentRunId;
        h.Now = 2;
        SceneLoader.IsSceneLoading = true;
        SceneLoader.RaiseStarted();
        h.Now = 20;
        MultiSceneCore.ActiveSubSceneID = "Level_Guide_2";
        h.Main.SetPosition(new Vector3(500, 0, 0));
        SceneLoader.IsSceneLoading = false;
        SceneLoader.RaiseFinished();
        h.Lifecycle.Tick();
        Assert.Equal(id, h.Lifecycle.CurrentRunId);
        h.Now = 23;
        LevelManager.RaiseEvacuated();
        var run = Assert.Single(h.Coordinator.Current!.Statistics.Runs);
        Assert.Equal(id, run.RunId);
        Assert.Null(run.NativeRaidId);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(0, run.PhysicalDistance);
        Assert.Collection(run.Segments,
            segment => Assert.Equal("duckov:map:Level_Guide_1", segment.MapId),
            segment => Assert.Equal("duckov:map:Level_Guide_2", segment.MapId));
    }

    [Fact]
    public void TutorialStartWaitsForRaidGameplayAndCompletedLoading()
    {
        using var h = new Harness();
        NativeRaidContext.GameplayContext = GameplayContext.Base;
        h.Lifecycle.Tick();
        Assert.False(h.Lifecycle.IsActive);
        NativeRaidContext.GameplayContext = GameplayContext.Raid;
        SceneLoader.IsSceneLoading = true;
        h.Lifecycle.Tick();
        Assert.False(h.Lifecycle.IsActive);
        SceneLoader.IsSceneLoading = false;
        h.Lifecycle.Tick();
        Assert.True(h.Lifecycle.IsActive);
        Assert.Single(h.Checkpoints);
    }

    [Fact]
    public void TutorialDeathUsesNativeBoundaryAndRetryReceivesItsRealRaidId()
    {
        using var h = new Harness();
        h.Lifecycle.Tick();
        Assert.True(h.Lifecycle.IsActive);
        h.Now = 2;
        RaidUtilities.RaiseRaidEnd(dead: true);
        RaidUtilities.RaiseRaidDead();
        LevelManager.RaiseMainCharacterDead();
        h.Lifecycle.Tick();
        Assert.Equal(RunOutcome.Died, Assert.Single(h.Coordinator.Current!.Statistics.Runs).Outcome);
        Assert.False(h.Lifecycle.IsActive);
        h.Lifecycle.Tick();
        Assert.False(h.Lifecycle.IsActive);

        LevelManager.Instance = new LevelManagerInstance { MainCharacter = h.Main };
        RaidUtilities.RaiseNewRaid(new RaidUtilities.RaidInfo { ID = 1, valid = true });
        h.Lifecycle.Tick();
        Assert.True(h.Lifecycle.IsActive);
        Assert.Equal("1", h.Checkpoints[^1].NativeRaidId);
        LevelManager.RaiseEvacuated();
        Assert.Equal(2, h.Coordinator.Current.Statistics.Runs.Count);
        Assert.Equal(2, h.Coordinator.Current.Statistics.Runs.Select(run => run.RunId).Distinct().Count());
    }

    [Theory]
    [InlineData("ordinary-map", false, false, true, false)]
    [InlineData("Prologue", false, false, true, false)]
    [InlineData("Level_Guide_Main", true, false, true, false)]
    [InlineData("Level_Guide_Main", false, true, true, false)]
    [InlineData("Level_Guide_Main", false, false, false, false)]
    [InlineData("Level_Guide_Main", false, false, true, true)]
    public void TutorialFallbackRequiresItsExactSceneLiveRaidStateAndPlayerControl(
        string sceneId, bool ended, bool dead, bool input, bool paused)
    {
        using var h = new Harness();
        MultiSceneCore.MainSceneID = sceneId;
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ended = ended, dead = dead };
        InputManager.InputActived = input;
        GameManager.Paused = paused;
        h.Lifecycle.Tick();
        Assert.False(h.Lifecycle.IsActive);
        Assert.Empty(h.Checkpoints);
    }

    [Fact]
    public void TutorialTerminalWriteFailureKeepsOneOriginalOutcomeWithoutRestarting()
    {
        using var h = new Harness();
        h.Lifecycle.Tick();
        Assert.True(h.Lifecycle.IsActive);
        var path = Path.Combine(Path.GetDirectoryName(h.Coordinator.CurrentProfilePath)!, "active-run.json.tmp");
        using (var held = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            h.Now = 2;
            LevelManager.RaiseEvacuated();
            Assert.False(h.Lifecycle.IsActive);
            Assert.Empty(h.Coordinator.Current!.Statistics.Runs);
            h.Now = 2.5;
            for (var frame = 0; frame < 100; frame++) h.Lifecycle.Tick();
        }
        h.Now = 3;
        h.Lifecycle.Tick();
        for (var frame = 0; frame < 100; frame++) h.Lifecycle.Tick();
        var run = Assert.Single(h.Coordinator.Current!.Statistics.Runs);
        Assert.Equal(RunOutcome.Extracted, run.Outcome);
        Assert.Equal(2, run.ActiveDurationSeconds);
        Assert.Null(run.NativeRaidId);
        Assert.False(h.Lifecycle.IsActive);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        private readonly string previousPath = Application.persistentDataPath;
        private readonly int previousSlot = Saves.SavesSystem.CurrentSlot;
        public double Now { get; set; }
        public CharacterMainControl Main { get; } = new() { IsMainCharacter = true, CharacterItem = new Item() };
        public List<ActiveRunCheckpoint> Checkpoints { get; } = new();
        public List<CurrencyFlowRecorded> Flows { get; } = new();
        public NativeProfileCoordinator Coordinator { get; }
        public NativeRunLifecycleAdapter Lifecycle { get; }
        public NativeEconomyAdapter Economy { get; }

        public Harness()
        {
            Reset();
            Application.persistentDataPath = directory.Path;
            Saves.SavesSystem.CurrentSlot = 3;
            var save = Path.Combine(directory.Path, Saves.SavesSystem.GetFilePath(3));
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllText(save, "{\"SaveTime\":{\"value\":1}}");
            Coordinator = new NativeProfileCoordinator(() => Now);
            Coordinator.Initialize();
            CharacterMainControl.Main = Main;
            LevelManager.Instance = new LevelManagerInstance { MainCharacter = Main };
            MultiSceneCore.Instance = new MultiSceneCore();
            MultiSceneCore.MainSceneID = "Level_Guide_Main";
            MultiSceneCore.ActiveSubSceneID = "Level_Guide_1";
            Lifecycle = new NativeRunLifecycleAdapter(() => Coordinator.CurrentGenerationId,
                checkpoint => { Checkpoints.Add(checkpoint); return Coordinator.HandleRunCheckpoint(checkpoint); },
                Coordinator.HandleRunCompleted, Coordinator.SetRunCapabilities, _ => { },
                checkpointCompletionPoller: Coordinator.PollRunCheckpoint,
                checkpointCompletionFlusher: Coordinator.FlushRunCheckpoint, monotonicSecondsProvider: () => Now);
            var publication = new EconomyFlowPublication(Coordinator.HandleCurrencyFlow, Lifecycle.RecordCurrencyFlow, _ => { });
            Economy = new NativeEconomyAdapter(() => Coordinator.CurrentGenerationId,
                () => Lifecycle.CurrentRunId, () => Lifecycle.CurrentMapId, () => Lifecycle.CurrentSegmentId,
                () => Lifecycle.IsActive, flow => { Flows.Add(flow); return publication.Publish(flow); },
                _ => { }, _ => { }, Coordinator.RetryPendingEconomyActivation);
            Coordinator.BeginEconomyActivation(Economy.ActivationId);
            Economy.Initialize();
            Coordinator.SetEconomyCapabilities([], Economy.MetricCapabilities);
            Economy.Tick();
            Lifecycle.SetTerminalObserver(Economy.FlushPendingForBoundary);
            Lifecycle.Initialize();
        }

        public void Dispose()
        {
            Lifecycle.Dispose();
            Economy.Dispose();
            Coordinator.Dispose();
            Reset();
            Application.persistentDataPath = previousPath;
            Saves.SavesSystem.CurrentSlot = previousSlot;
            directory.Dispose();
        }

        private static void Reset()
        {
            CharacterMainControl.ResetNativeState(); LevelManager.ResetNativeState(); RaidUtilities.ResetNativeState();
            EconomyManager.ResetNativeState(); ItemUtilities.ResetNativeState(); SceneLoader.ResetNativeState();
            MultiSceneCore.Instance = null; MultiSceneCore.MainSceneID = "test-map"; MultiSceneCore.ActiveSubSceneID = string.Empty;
            InputManager.InputActived = true; GameManager.Paused = false;
            NativeRaidContext.GameplayContext = GameplayContext.Raid; Application.version = "2.3.30";
        }
    }
}
