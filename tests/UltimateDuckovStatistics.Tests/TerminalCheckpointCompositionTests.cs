using Duckov.Economy;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class TerminalCheckpointCompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedTerminalWriteRetriesOnCadenceAndKeepsOriginalBoundary(bool returnToBase)
    {
        var originalPath = Application.persistentDataPath;
        var originalSlot = Saves.SavesSystem.CurrentSlot;
        using var directory = new TemporaryDirectory();
        CharacterMainControl.ResetNativeState();
        LevelManager.ResetNativeState();
        RaidUtilities.ResetNativeState();
        EconomyManager.ResetNativeState();
        ItemUtilities.ResetNativeState();
        try
        {
            Application.persistentDataPath = directory.Path;
            Saves.SavesSystem.CurrentSlot = 1;
            var save = Path.Combine(directory.Path, Saves.SavesSystem.GetFilePath(1));
            Directory.CreateDirectory(Path.GetDirectoryName(save)!);
            File.WriteAllText(save, "{\"SaveTime\":{\"value\":1}}");
            var now = 0d;
            using var coordinator = new NativeProfileCoordinator(() => now);
            coordinator.Initialize();
            InputManager.InputActived = true;
            GameManager.Paused = false;
            NativeRaidContext.GameplayContext = GameplayContext.Raid;
            Duckov.Scenes.SceneLoader.IsSceneLoading = false;
            var main = new CharacterMainControl { IsMainCharacter = true, CharacterItem = new ItemStatsSystem.Item() };
            CharacterMainControl.Main = main;
            LevelManager.Instance = new LevelManagerInstance { MainCharacter = main };
            RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };
            var messages = new List<string>();
            var checkpoints = new List<ActiveRunCheckpoint>();
            using var lifecycle = new NativeRunLifecycleAdapter(() => coordinator.CurrentGenerationId,
                checkpoint => { checkpoints.Add(checkpoint); return coordinator.HandleRunCheckpoint(checkpoint); },
                coordinator.HandleRunCompleted, coordinator.SetRunCapabilities, messages.Add,
                checkpointCompletionPoller: coordinator.PollRunCheckpoint,
                checkpointCompletionFlusher: coordinator.FlushRunCheckpoint, monotonicSecondsProvider: () => now);
            lifecycle.Initialize();
            lifecycle.Tick();
            Assert.True(lifecycle.IsActive);
            var flows = new List<CurrencyFlowRecorded>();
            var publication = new EconomyFlowPublication(coordinator.HandleCurrencyFlow, lifecycle.RecordCurrencyFlow, messages.Add);
            using var economy = new NativeEconomyAdapter(() => coordinator.CurrentGenerationId,
                () => lifecycle.CurrentRunId, () => lifecycle.CurrentMapId, () => lifecycle.CurrentSegmentId,
                () => lifecycle.IsActive, flow =>
                {
                    if (!publication.Publish(flow)) return false;
                    flows.Add(flow);
                    return true;
                }, _ => { }, messages.Add, coordinator.RetryPendingEconomyActivation);
            coordinator.BeginEconomyActivation(economy.ActivationId);
            economy.Initialize();
            coordinator.SetEconomyCapabilities([], economy.MetricCapabilities);
            lifecycle.UpdateEconomyCapabilities(economy.MetricCapabilities);
            economy.Tick();
            lifecycle.SetTerminalObserver(economy.FlushPendingForBoundary);
            Assert.True(lifecycle.FlushCheckpoint());
            checkpoints.Clear();
            messages.Clear();
            var temporary = Path.Combine(Path.GetDirectoryName(coordinator.CurrentProfilePath)!, "active-run.json.tmp");
            DateTime terminalUtc;
            using (var locked = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                EconomyManager.RaiseMoneyChanged(0, 7);
                now = 2;
                LevelManager.RaiseEvacuated();
                terminalUtc = Assert.Single(checkpoints).LastObservedUtc;
                Assert.False(lifecycle.IsActive);
                Assert.Null(lifecycle.CurrentEventContext);
                Assert.Empty(coordinator.Current!.Statistics.Runs);
                if (returnToBase)
                {
                    now = 2.25;
                    Duckov.Scenes.SceneLoader.RaiseStarted();
                    NativeRaidContext.GameplayContext = GameplayContext.Base;
                    Duckov.Scenes.SceneLoader.RaiseFinished();
                    LevelManager.RaiseAfterLevelInitialized();
                    EconomyManager.RaiseMoneyChanged(7, 18);
                    economy.Tick();
                    var baseFlow = flows[^1];
                    Assert.Null(baseFlow.RunId);
                    Assert.Equal(GameplayContext.Base, baseFlow.GameplayContext);
                    Assert.Equal(11, baseFlow.Amount);
                }
                for (var frame = 0; frame < 1000; frame++)
                {
                    now = 2.5;
                    lifecycle.Tick();
                    Assert.False(lifecycle.FlushCheckpoint());
                }
                Assert.Single(checkpoints);
                now = 3;
                lifecycle.Tick();
                Assert.Equal(2, checkpoints.Count);
                for (var frame = 0; frame < 1000; frame++) { now = 4.5; lifecycle.Tick(); }
                Assert.Equal(2, checkpoints.Count);
                Assert.Single(messages, message => message.Contains("terminalization deferred", StringComparison.Ordinal));
            }
            now = 5;
            lifecycle.Tick();
            var run = Assert.Single(coordinator.Current!.Statistics.Runs);
            Assert.Equal(RunOutcome.Extracted, run.Outcome);
            Assert.Equal(terminalUtc, run.EndedUtc);
            Assert.Equal(2, run.ActiveDurationSeconds);
            Assert.Equal(7, run.Economy.Currencies["Money"].Totals.GrossInflow);
            Assert.Equal(returnToBase ? 18 : 7, coordinator.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
            NativeRaidContext.GameplayContext = GameplayContext.Base;
            lifecycle.Tick();
            Assert.Single(coordinator.Current.Statistics.Runs);
            Assert.False(lifecycle.IsActive);
        }
        finally
        {
            CharacterMainControl.ResetNativeState();
            LevelManager.ResetNativeState();
            RaidUtilities.ResetNativeState();
            EconomyManager.ResetNativeState();
            ItemUtilities.ResetNativeState();
            Application.persistentDataPath = originalPath;
            Saves.SavesSystem.CurrentSlot = originalSlot;
            NativeRaidContext.GameplayContext = GameplayContext.Unknown;
        }
    }
}

