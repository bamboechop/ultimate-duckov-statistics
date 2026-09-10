using Duckov.Scenes;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeBaseMovementTests
{
    [Fact]
    public void PhysicalBaseMovementIsCadencedAndSurvivesExportSaveAndReopenWithoutCreatingRuns()
    {
        using var h = new Harness();
        h.Tick(0, 0);
        Assert.Equal(0, h.Meters);
        h.Tick(.25, 1);
        Assert.Equal(0, h.Meters); // Repository publication is slower than position sampling.
        h.Tick(1, 2);
        Assert.Equal(2, h.Meters);
        Assert.True(h.Coordinator.FlushBaseMovement());
        var persisted = h.ReadProfile();
        Assert.Equal(2, persisted.Statistics.BaseMovement!.RecordedMeters);
        var revision = persisted.Revision;
        for (var i = 0; i < 100; i++) h.Tick(1 + i * .25, 2);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(revision, h.ReadProfile().Revision);
        Assert.Empty(h.Coordinator.Current!.Statistics.Runs);
        Assert.Equal(0, h.Coordinator.Current.Statistics.RunTotals.PhysicalDistance);
        Assert.False(h.Lifecycle.IsActive);
        Assert.NotEmpty(h.Coordinator.ExportCurrent().Directory);
    }

    [Fact]
    public void PauseAndFrozenTimeExcludeMovementButInputBlockingAloneDoesNot()
    {
        using var h = new Harness();
        h.Tick(0, 0);
        InputManager.InputActived = false;
        h.Tick(.25, 1);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(1, h.Meters);
        GameManager.Paused = true;
        h.Tick(.5, 50);
        GameManager.Paused = false;
        h.Tick(.75, 100);
        h.Tick(1, 101);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(2, h.Meters);
        Time.timeScale = 0;
        h.Tick(1.25, 200);
        Time.timeScale = 1;
        h.Tick(1.5, 300);
        h.Tick(1.75, 301);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(3, h.Meters);
    }

    [Fact]
    public void FinalInitializationRepositionReplacementAndLoadingAllResetTheBaseline()
    {
        using var h = new Harness();
        LevelManager.AfterInit = false;
        h.Tick(0, 0);
        h.Tick(.25, 100);
        Assert.Null(h.Coordinator.Current!.Statistics.BaseMovement);
        LevelManager.AfterInit = true;
        h.Tick(.5, 200);
        h.Tick(.75, 201);
        h.Main.SetPosition(new Vector3(202, 0, 0)); // Even a plausible one-metre teleport is excluded.
        h.Tick(1, 202);
        h.Tick(1.25, 203);
        var replacement = new CharacterMainControl { IsMainCharacter = true, CharacterItem = new Item() };
        h.Main = replacement;
        CharacterMainControl.Main = replacement;
        LevelManager.Instance!.MainCharacter = replacement;
        h.Tick(1.5, 400);
        h.Tick(1.75, 401);
        SceneLoader.IsSceneLoading = true;
        SceneLoader.RaiseStarted();
        h.Tick(2, 800);
        SceneLoader.IsSceneLoading = false;
        SceneLoader.RaiseFinished();
        h.Tick(2.25, 1000);
        h.Tick(2.5, 1001);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(4, h.Meters);
    }

    [Fact]
    public void SlotChangeFlushesOldGenerationAndQuarantinesItsNativeLevel()
    {
        using var h = new Harness();
        h.Tick(0, 0);
        h.Tick(.25, 1);
        var oldPath = h.Coordinator.CurrentProfilePath;
        var oldGeneration = h.Coordinator.CurrentGenerationId;
        h.CreateSave(4);
        Saves.SavesSystem.SetFile(4);
        h.Coordinator.RetryPendingProfileTransition();
        Assert.NotEqual(oldGeneration, h.Coordinator.CurrentGenerationId);
        Assert.Equal(1, new AtomicJsonStore<ProfileDocument>().Load(oldPath).Value!.Statistics.BaseMovement!.RecordedMeters);
        h.Tick(.5, 2);
        Assert.Null(h.Coordinator.Current!.Statistics.BaseMovement);
        LevelManager.Instance = new LevelManagerInstance { MainCharacter = h.Main, IsBaseLevel = true };
        h.Tick(.75, 100);
        h.Tick(1, 101);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(1, h.Meters);
    }

    [Fact]
    public void LeavingBaseNeverAddsMenuOrRaidCoordinatesToBaseDistance()
    {
        using var h = new Harness();
        h.Tick(0, 0);
        h.Tick(.25, 1);
        LevelManager.Instance!.IsBaseLevel = false;
        NativeRaidContext.GameplayContext = GameplayContext.Unknown;
        h.Tick(.5, 100);
        h.Tick(.75, 101);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(1, h.Meters);
        NativeRaidContext.GameplayContext = GameplayContext.Raid;
        LevelManager.Instance.IsRaidMap = true;
        RaidUtilities.RaiseNewRaid(new RaidUtilities.RaidInfo { valid = true, ID = 42 });
        h.Tick(1, 200);
        h.Tick(1.25, 201);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(1, h.Meters);
    }

    [Fact]
    public void BlockedWriteRetainsDistanceAndPreventsProfileTransitionUntilRetrySucceeds()
    {
        using var h = new Harness();
        h.Tick(0, 0);
        h.Tick(.25, 1);
        var oldGeneration = h.Coordinator.CurrentGenerationId;
        var oldPath = h.Coordinator.CurrentProfilePath;
        using (var blocker = new FileStream(oldPath + ".tmp", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(h.Coordinator.FlushBaseMovement());
            Assert.Equal(1, h.Meters);
            h.CreateSave(4);
            Saves.SavesSystem.SetFile(4);
            Assert.True(h.Coordinator.HasPendingProfileTransition);
            Assert.Equal(oldGeneration, h.Coordinator.CurrentGenerationId);
            h.Tick(.5, 100);
            Assert.Equal(1, h.Meters);
        }
        h.Tick(61, 100); // Allow the existing transition retry backoff to elapse.
        Assert.True(h.Coordinator.DrainPendingProfileTransitions());
        Assert.NotEqual(oldGeneration, h.Coordinator.CurrentGenerationId);
        Assert.Equal(1, new AtomicJsonStore<ProfileDocument>().Load(oldPath).Value!.Statistics.BaseMovement!.RecordedMeters);
        Assert.Null(h.Coordinator.Current!.Statistics.BaseMovement);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        private readonly string previousPath = Application.persistentDataPath;
        private readonly int previousSlot = Saves.SavesSystem.CurrentSlot;
        public double Now { get; private set; }
        public CharacterMainControl Main { get; set; } = new() { IsMainCharacter = true, CharacterItem = new Item() };
        public NativeProfileCoordinator Coordinator { get; }
        public NativeRunLifecycleAdapter Lifecycle { get; }
        public double Meters => Coordinator.Current!.Statistics.BaseMovement!.RecordedMeters;
        public Harness()
        {
            Reset();
            Application.persistentDataPath = directory.Path;
            Saves.SavesSystem.CurrentSlot = 3;
            CreateSave(3);
            Coordinator = new NativeProfileCoordinator(() => Now);
            Coordinator.Initialize();
            CharacterMainControl.Main = Main;
            LevelManager.Instance = new LevelManagerInstance { MainCharacter = Main, IsBaseLevel = true };
            MultiSceneCore.Instance = new MultiSceneCore();
            MultiSceneCore.MainSceneID = "Base";
            MultiSceneCore.ActiveSubSceneID = "Base";
            Lifecycle = new NativeRunLifecycleAdapter(() => Coordinator.CurrentGenerationId,
                Coordinator.HandleRunCheckpoint, Coordinator.HandleRunCompleted, Coordinator.SetRunCapabilities, _ => { },
                checkpointCompletionPoller: Coordinator.PollRunCheckpoint,
                checkpointCompletionFlusher: Coordinator.FlushRunCheckpoint, monotonicSecondsProvider: () => Now);
            Lifecycle.ConfigureBaseMovement(Coordinator.HandleBaseMovement, Coordinator.FlushBaseMovement,
                () => Coordinator.HasPendingProfileTransition);
            Coordinator.SetBaseMovementBoundaryPublisher(Lifecycle.PublishBaseMovementForBoundary);
            Coordinator.ProfileChanging += Lifecycle.InterruptForProfileTransition;
            Coordinator.WorldTimeProfileChangeAwaitingNativeLoadStarted += Lifecycle.BeginBaseMovementNativeLoad;
            Lifecycle.Initialize();
        }
        public void CreateSave(int slot)
        {
            var path = Path.Combine(directory.Path, Saves.SavesSystem.GetFilePath(slot));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{\"SaveTime\":{\"value\":1}}");
        }
        public void Tick(double now, float x)
        {
            Now = now;
            Main.transform.position = new Vector3(x, 0, 0);
            Lifecycle.Tick();
        }
        public ProfileDocument ReadProfile() => new AtomicJsonStore<ProfileDocument>().Load(Coordinator.CurrentProfilePath).Value!;
        public void Dispose()
        {
            Lifecycle.Dispose();
            Coordinator.Dispose();
            Reset();
            Application.persistentDataPath = previousPath;
            Saves.SavesSystem.CurrentSlot = previousSlot;
            directory.Dispose();
        }
        private static void Reset()
        {
            CharacterMainControl.ResetNativeState(); LevelManager.ResetNativeState(); RaidUtilities.ResetNativeState();
            SceneLoader.ResetNativeState(); Saves.SavesSystem.ResetNativeState();
            MultiSceneCore.Instance = null; MultiSceneCore.MainSceneID = "test-map"; MultiSceneCore.ActiveSubSceneID = string.Empty;
            InputManager.InputActived = true; GameManager.Paused = false; Time.timeScale = 1;
            NativeRaidContext.GameplayContext = GameplayContext.Base; Application.version = "2.3.30";
        }
    }
}
