using Saves;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeSaveBoundaryTests
{
    private static readonly DateTime Now = new(2026, 9, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NativeCallbackPersistsAllPublicationsCapabilitiesAndIntentInOneWholeProfileWrite()
    {
        using var h = new Harness();
        var order = new List<string>();
        h.Coordinator.SetBaseMovementBoundaryPublisher(() => { order.Add("base"); return h.PublishMeters(3); });
        h.Coordinator.SetEconomyHoldingsBoundaryBarrier(() =>
        {
            order.Add("holdings");
            h.Coordinator.SetEconomyHoldingsCapabilities([], EconomyHoldingsNativeContractPolicy.Supported("money", "cash", "liquid"));
            return h.Coordinator.HandleEconomyHoldings(new(h.Coordinator.CurrentGenerationId, Now, 750, 25, "native"));
        });
        h.Coordinator.SetWorldTimeBoundaryBarrier(() =>
        {
            order.Add("world");
            h.Coordinator.SetWorldTimeCapabilities([], WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
            return h.Coordinator.HandleWorldTime(new(1, 100, 0, 0)) && h.Coordinator.RequestWorldTimePersistence();
        });
        h.Coordinator.SetCraftingBoundaryBarrier(() =>
        {
            order.Add("crafting");
            h.Coordinator.SetCraftingCapabilities([], CraftingNativeContractPolicy.Supported("completion", "formula"));
            var accepted = h.Coordinator.HandleCrafting(new(h.Coordinator.CurrentGenerationId, Now,
                [new("100", "Bandage", "bandage", 1, 3, new() { ["3"] = 1 })]));
            SavesSystem.RaiseCollectSaveData(); // Reentrant notification is covered by this outer snapshot.
            return accepted && h.Coordinator.RequestCraftingPersistence();
        });
        h.Coordinator.SetActiveRunCheckpointBarrier(() =>
        {
            order.Add("checkpoint");
            Assert.Equal(1, h.Coordinator.Current!.Statistics.Crafting.CompletionActions);
            Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().ProfileStoreAttempts);
            Assert.Equal(h.InitialBytes, File.ReadAllBytes(h.ProfilePath));
            return true;
        });

        SavesSystem.RaiseCollectSaveData();

        Assert.Equal(["base", "holdings", "world", "crafting", "checkpoint"], order);
        var saved = h.Read();
        Assert.Equal(3, saved.Statistics.BaseMovement!.RecordedMeters);
        Assert.Equal(750, saved.Statistics.Holdings.Money.Value);
        Assert.Equal(100, saved.Statistics.WorldTime.ObservedGameTimeTicks);
        Assert.Equal(3, saved.Statistics.Crafting.ProducedQuantity);
        Assert.Equal(AdapterCapabilityState.Supported, saved.Statistics.Crafting.Capabilities.CompletionActions.State);
        Assert.NotNull(saved.PendingSave);
        Assert.Equal(saved.Identity.ContentSha256, saved.PendingSave.ContentSha256BeforeSave);
        Assert.Equal(saved.Identity.SaveTimeBinary, saved.PendingSave.SaveTimeBinaryBeforeSave);
        h.AssertSingleWrite();
        Assert.Equal(saved.Revision, h.Coordinator.LastSaveReceipt!.Revision);
    }

    [Theory]
    [InlineData("holdings")]
    [InlineData("world")]
    [InlineData("crafting")]
    public void FailedPublicationPersistsAcceptedSubsetWithoutCreatingIntent(string failingStage)
    {
        using var h = new Harness();
        h.Coordinator.SetBaseMovementBoundaryPublisher(() => h.PublishMeters(2));
        h.Coordinator.SetEconomyHoldingsBoundaryBarrier(() => failingStage != "holdings");
        h.Coordinator.SetWorldTimeBoundaryBarrier(() =>
        {
            Assert.True(h.Coordinator.HandleWorldTime(new(1, 100, 0, 0)));
            return failingStage != "world"; // Accepted aggregate before its durability request fails.
        });
        h.Coordinator.SetCraftingBoundaryBarrier(() => failingStage != "crafting");
        SavesSystem.RaiseCollectSaveData();
        var saved = h.Read();
        Assert.Equal(2, saved.Statistics.BaseMovement!.RecordedMeters);
        Assert.Equal(failingStage == "holdings" ? 0 : 100, saved.Statistics.WorldTime.ObservedGameTimeTicks);
        Assert.Null(saved.PendingSave);
        h.AssertSingleWrite();
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("missing-save-time")]
    [InlineData("locked-file")]
    public void UnavailableIdentityStillPersistsAcceptedAggregatesAndPreservesOldLineage(string unavailable)
    {
        using var h = new Harness();
        h.Coordinator.SetBaseMovementBoundaryPublisher(() => h.PublishMeters(2));
        var originalIdentity = h.Coordinator.Current!.Identity.ContentSha256;
        if (unavailable == "missing-file") File.Delete(h.NativeSavePath);
        else if (unavailable == "missing-save-time") File.WriteAllText(h.NativeSavePath, "{}");
        using var locked = unavailable == "locked-file"
            ? new FileStream(h.NativeSavePath, FileMode.Open, FileAccess.Read, FileShare.None) : null;
        SavesSystem.RaiseCollectSaveData();
        var saved = h.Read();
        Assert.Equal(2, saved.Statistics.BaseMovement!.RecordedMeters);
        Assert.Equal(originalIdentity, saved.Identity.ContentSha256);
        Assert.Null(saved.PendingSave);
        h.AssertSingleWrite();
    }

    [Fact]
    public void CheckpointRefusalCannotSaveOrAcknowledgePublishedDistance()
    {
        using var h = new Harness();
        h.Coordinator.SetBaseMovementBoundaryPublisher(() => h.PublishMeters(4));
        h.Coordinator.SetActiveRunCheckpointBarrier(() => false);
        var receipt = h.Coordinator.LastSaveReceipt;
        SavesSystem.RaiseCollectSaveData();
        Assert.Equal(h.InitialBytes, File.ReadAllBytes(h.ProfilePath));
        Assert.Same(receipt, h.Coordinator.LastSaveReceipt);
        Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().ProfileStoreAttempts);
        Assert.False(h.Coordinator.FlushBaseMovement());
        h.Coordinator.SetActiveRunCheckpointBarrier(() => true);
        Assert.True(h.Coordinator.FlushBaseMovement());
        Assert.Equal(4, h.Read().Statistics.BaseMovement!.RecordedMeters);
        h.AssertSingleWrite();
    }

    [Fact]
    public void ActiveRaidCheckpointFailureBlocksCombinedProfileUntilActualCheckpointSucceeds()
    {
        using var h = new Harness();
        InputManager.InputActived = true;
        GameManager.Paused = false;
        NativeRaidContext.GameplayContext = GameplayContext.Raid;
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
        var main = new CharacterMainControl { IsMainCharacter = true, CharacterItem = new ItemStatsSystem.Item() };
        CharacterMainControl.Main = main;
        LevelManager.Instance = new LevelManagerInstance { MainCharacter = main };
        RaidUtilities.CurrentRaid = new RaidUtilities.RaidInfo { ID = 1, valid = true };
        using var lifecycle = new NativeRunLifecycleAdapter(() => h.Coordinator.CurrentGenerationId,
            h.Coordinator.HandleRunCheckpoint, h.Coordinator.HandleRunCompleted,
            h.Coordinator.SetRunCapabilities, _ => { },
            checkpointCompletionPoller: h.Coordinator.PollRunCheckpoint,
            checkpointCompletionFlusher: h.Coordinator.FlushRunCheckpoint,
            monotonicSecondsProvider: () => h.Clock);
        lifecycle.Initialize();
        lifecycle.Tick();
        Assert.True(lifecycle.IsActive);
        Assert.True(lifecycle.FlushCheckpoint());
        h.Coordinator.SetActiveRunCheckpointBarrier(lifecycle.FlushCheckpoint);
        var published = false;
        h.Coordinator.SetWorldTimeBoundaryBarrier(() =>
        {
            if (published) return true;
            published = h.Coordinator.HandleWorldTime(new(1, 100, 0, 0));
            return published && h.Coordinator.RequestWorldTimePersistence();
        });
        var previous = File.ReadAllBytes(h.ProfilePath);
        var receipt = h.Coordinator.LastSaveReceipt;
        NativeHotPathDiagnostics.Reset();
        var checkpointPath = Path.Combine(Path.GetDirectoryName(h.ProfilePath)!, "active-run.json");
        using (var blocker = new FileStream(checkpointPath + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            h.Clock = 2;
            SavesSystem.RaiseCollectSaveData();
            Assert.Equal(previous, File.ReadAllBytes(h.ProfilePath));
            Assert.Same(receipt, h.Coordinator.LastSaveReceipt);
            Assert.Equal(0, NativeHotPathDiagnostics.Snapshot().ProfileStoreAttempts);
        }
        h.Clock = 3;
        SavesSystem.RaiseCollectSaveData();
        Assert.True(NativeHotPathDiagnostics.Snapshot().CheckpointStoreSuccesses > 0);
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().ProfileStoreSuccesses);
        Assert.Equal(100, h.Read().Statistics.WorldTime.ObservedGameTimeTicks);
        Assert.NotNull(h.Read().PendingSave);
        Assert.Equal(h.Coordinator.CurrentGenerationId,
            new AtomicJsonStore<ActiveRunCheckpoint>().Load(checkpointPath).Value!.SaveGenerationId);
    }

    [Fact]
    public void ReentrantLifecycleDrainCannotMergeSharedCompletedTotalsDuringPreparation()
    {
        using var h = new Harness();
        h.Coordinator.SetCraftingBoundaryBarrier(() =>
        {
            Assert.False(h.Coordinator.HandleRunCompleted(new RunSummary { RunId = "pending-completion" }));
            Assert.Empty(h.Coordinator.Current!.Statistics.Runs);
            return true;
        });
        SavesSystem.RaiseCollectSaveData();
        Assert.Empty(h.Read().Statistics.Runs);
        h.AssertSingleWrite();
    }

    [Fact]
    public void FailedStoreRetainsOneStagedIntentAndDistanceUntilBoundedRetrySucceeds()
    {
        using var h = new Harness();
        h.Coordinator.SetBaseMovementBoundaryPublisher(() => h.PublishMeters(5));
        var receipt = h.Coordinator.LastSaveReceipt;
        long stagedRevision;
        DateTime stagedUtc;
        using (var blocker = new FileStream(h.ProfilePath + ".tmp", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            SavesSystem.RaiseCollectSaveData();
            Assert.True(h.Coordinator.HasProfilePersistenceFailure);
            Assert.Equal(2, NativeHotPathDiagnostics.Snapshot().ProfileStoreAttempts);
            stagedRevision = h.Coordinator.Current!.Revision;
            stagedUtc = h.Coordinator.Current.PendingSave!.CollectedUtc;
            Assert.Same(receipt, h.Coordinator.LastSaveReceipt);
            Assert.Equal(h.InitialBytes, File.ReadAllBytes(h.ProfilePath));
            SavesSystem.RaiseCollectSaveData();
            Assert.Equal(stagedRevision, h.Coordinator.Current.Revision);
            Assert.Equal(stagedUtc, h.Coordinator.Current.PendingSave.CollectedUtc);
            Assert.False(h.Coordinator.FlushBaseMovement());
        }
        h.Clock = 1;
        Assert.True(h.Coordinator.FlushBaseMovement());
        var saved = h.Read();
        Assert.Equal(5, saved.Statistics.BaseMovement!.RecordedMeters);
        Assert.Equal(stagedRevision, saved.Revision);
        // DataContractJsonSerializer persists DateTime at millisecond precision.
        Assert.Equal(stagedUtc.Ticks / TimeSpan.TicksPerMillisecond,
            saved.PendingSave!.CollectedUtc.Ticks / TimeSpan.TicksPerMillisecond);
        Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().ProfileStoreSuccesses);
        Assert.False(h.Coordinator.HasProfilePersistenceFailure);
    }

    [Fact]
    public void SameSlotReopenAfterNativeSaveKeepsGenerationUsingCombinedIntent()
    {
        using var h = new Harness();
        var generation = h.Coordinator.CurrentGenerationId;
        h.Coordinator.SetBaseMovementBoundaryPublisher(() => h.PublishMeters(6));
        SavesSystem.RaiseCollectSaveData();
        var intended = h.Read().PendingSave!;
        File.WriteAllText(h.NativeSavePath, "{\"SaveTime\":{\"value\":" + intended.CollectedUtc.AddSeconds(1).ToBinary() + "}}");
        SavesSystem.SetFile(1);
        Assert.Equal(generation, h.Coordinator.CurrentGenerationId);
        Assert.False(h.Coordinator.LastOpenResult!.RotatedGeneration);
        Assert.Equal(6, h.Coordinator.Current!.Statistics.BaseMovement!.RecordedMeters);
    }

    [Fact]
    public void SameMetadataReplacementStillRotatesUsingFreshContentFingerprint()
    {
        using var h = new Harness();
        Assert.True(h.PublishMeters(2));
        Assert.True(h.Coordinator.FlushBaseMovement());
        var generation = h.Coordinator.CurrentGenerationId;
        var creation = File.GetCreationTimeUtc(h.NativeSavePath);
        var write = File.GetLastWriteTimeUtc(h.NativeSavePath);
        var length = new FileInfo(h.NativeSavePath).Length;
        File.WriteAllText(h.NativeSavePath, "{\"SaveTime\":{\"value\":" + Now.AddDays(-2).ToBinary() + "}}");
        File.SetLastWriteTimeUtc(h.NativeSavePath, write);
        Assert.Equal(length, new FileInfo(h.NativeSavePath).Length);
        Assert.Equal(creation, File.GetCreationTimeUtc(h.NativeSavePath));
        SavesSystem.SetFile(1);
        Assert.NotEqual(generation, h.Coordinator.CurrentGenerationId);
        Assert.True(h.Coordinator.LastOpenResult!.RotatedGeneration);
    }

    private sealed class Harness : IDisposable
    {
        private readonly TemporaryDirectory directory = new();
        private readonly string previousPath = Application.persistentDataPath;
        private readonly int previousSlot = SavesSystem.CurrentSlot;
        public NativeProfileCoordinator Coordinator { get; }
        public double Clock { get; set; }
        public string NativeSavePath { get; }
        public string ProfilePath => Coordinator.CurrentProfilePath;
        public byte[] InitialBytes { get; }

        public Harness()
        {
            Application.persistentDataPath = directory.Path;
            SavesSystem.ResetNativeState();
            NativeSavePath = Path.Combine(directory.Path, SavesSystem.GetFilePath(1));
            Directory.CreateDirectory(Path.GetDirectoryName(NativeSavePath)!);
            File.WriteAllText(NativeSavePath, "{\"SaveTime\":{\"value\":" + Now.AddDays(-1).ToBinary() + "}}");
            Coordinator = new NativeProfileCoordinator(() => Clock, repositoryFactory: NativeJsonRepositoryFixture.Create);
            Coordinator.Initialize();
            InitialBytes = File.ReadAllBytes(ProfilePath);
            NativeHotPathDiagnostics.Reset();
        }

        public bool PublishMeters(double meters) => Coordinator.HandleBaseMovement(new()
        {
            GenerationId = Coordinator.CurrentGenerationId,
            CaptureId = "base-capture",
            CapturedMeters = meters,
            CollectionStartedUtc = Now
        });

        public ProfileDocument Read() => new AtomicJsonStore<ProfileDocument>().Load(ProfilePath, ProfileFormat.ValidateRecoveryCandidate).Value!;

        public void AssertSingleWrite()
        {
            Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().ProfileSnapshotCaptures);
            Assert.Equal(1, NativeHotPathDiagnostics.Snapshot().ProfileStoreSuccesses);
            // Also detects uncounted direct SaveCurrent writes between initialization
            // and the final snapshot; only one replacement can retain these bytes.
            Assert.Equal(InitialBytes, File.ReadAllBytes(ProfilePath + ".bak"));
        }

        public void Dispose()
        {
            Coordinator.SetActiveRunCheckpointBarrier(() => true);
            Coordinator.Dispose();
            SavesSystem.ResetNativeState();
            CharacterMainControl.ResetNativeState();
            LevelManager.ResetNativeState();
            RaidUtilities.ResetNativeState();
            NativeRaidContext.GameplayContext = GameplayContext.Unknown;
            Application.persistentDataPath = previousPath;
            SavesSystem.CurrentSlot = previousSlot;
            directory.Dispose();
        }
    }
}
