using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeUserResetSafetyTests : IDisposable
{
    private readonly string originalPersistentDataPath = Application.persistentDataPath;

    public NativeUserResetSafetyTests() => ResetNative();

    public void Dispose()
    {
        ResetNative();
        Application.persistentDataPath = originalPersistentDataPath;
    }

    [Fact]
    public void FailedResetSettlesExactHandoffsBeforeFailureAndDoesNotRetryWhenStorageRecovers()
    {
        using var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        using var coordinator = new NativeProfileCoordinator();
        coordinator.Initialize();
        var generation = coordinator.CurrentGenerationId;
        long craftingStarted = 0, craftingCompleted = 0, holdingsStarted = 0, holdingsCompleted = 0;
        var worldClockResets = 0;
        var profileChanged = 0;
        coordinator.CraftingProfileChangeStarted += token => craftingStarted = token;
        coordinator.CraftingProfileChangeCompleted += token =>
        {
            craftingCompleted = token;
            Assert.Equal(NativeUserResetOutcome.Pending, coordinator.LastUserResetAttempt!.Outcome);
        };
        coordinator.EconomyHoldingsProfileResetStarted += token => holdingsStarted = token;
        coordinator.EconomyHoldingsProfileResetCompleted += token => holdingsCompleted = token;
        coordinator.WorldTimeProfileChangedWithCurrentClock += () => worldClockResets++;
        coordinator.ProfileChanged += () => profileChanged++;

        using (var held = new FileStream(coordinator.CurrentProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.False(coordinator.ResetCurrent());

        var failed = coordinator.LastUserResetAttempt!;
        Assert.Equal(NativeUserResetOutcome.Failure, failed.Outcome);
        Assert.Equal(generation, failed.RequestedGenerationId);
        Assert.Equal(generation, failed.CompletedGenerationId);
        Assert.NotEmpty(failed.Detail);
        Assert.Equal(failed.TransitionId, craftingStarted);
        Assert.Equal(craftingStarted, craftingCompleted);
        Assert.Equal(failed.TransitionId, holdingsStarted);
        Assert.Equal(holdingsStarted, holdingsCompleted);
        Assert.Equal(1, profileChanged);
        Assert.Equal(0, worldClockResets);
        Assert.False(coordinator.HasPendingProfileTransition);
        Assert.Equal(0, coordinator.CompletedUserResetVersion);
        Assert.True(coordinator.RetryPendingProfileTransition());
        Assert.Equal(generation, coordinator.CurrentGenerationId);
        Assert.Same(failed, coordinator.LastUserResetAttempt);

        Assert.True(coordinator.ResetCurrent());
        Assert.True(coordinator.LastUserResetAttempt!.TransitionId > failed.TransitionId);
        Assert.Equal(NativeUserResetOutcome.Success, coordinator.LastUserResetAttempt.Outcome);
        Assert.NotEqual(generation, coordinator.CurrentGenerationId);
        Assert.Equal(1, coordinator.CompletedUserResetVersion);
    }

    [Fact]
    public void FailureAfterCommitRemainsPendingAndRetriesOnlyRemainingCapabilityPersistence()
    {
        using var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        using var coordinator = new NativeProfileCoordinator();
        coordinator.Initialize();
        var generation = coordinator.CurrentGenerationId;
        var changed = 0;
        string? blockedTemporary = null;
        coordinator.ProfileChanged += () =>
        {
            changed++;
            blockedTemporary = AtomicJsonPaths.GetTemporaryPath(coordinator.CurrentProfilePath);
            Directory.CreateDirectory(blockedTemporary);
        };

        Assert.False(coordinator.ResetCurrent());
        var pending = coordinator.LastUserResetAttempt!;
        Assert.Equal(NativeUserResetOutcome.Pending, pending.Outcome);
        Assert.True(coordinator.HasPendingProfileTransition);
        Assert.NotEqual(generation, coordinator.CurrentGenerationId);
        var committedGeneration = coordinator.CurrentGenerationId;
        Assert.Equal(0, coordinator.CompletedUserResetVersion);
        Directory.Delete(blockedTemporary!);

        Assert.True(coordinator.RetryPendingProfileTransition());
        var completed = coordinator.LastUserResetAttempt!;
        Assert.Equal(pending.TransitionId, completed.TransitionId);
        Assert.Equal(NativeUserResetOutcome.Success, completed.Outcome);
        Assert.Equal(generation, completed.RequestedGenerationId);
        Assert.Equal(committedGeneration, completed.CompletedGenerationId);
        Assert.Equal(committedGeneration, coordinator.CurrentGenerationId);
        Assert.Equal(1, changed);
        Assert.Equal(1, coordinator.CompletedUserResetVersion);
        Assert.Equal(committedGeneration, coordinator.LastCompletedUserResetGeneration);
        var slot = Path.GetDirectoryName(Path.GetDirectoryName(coordinator.CurrentProfilePath)!)!;
        Assert.Single(Directory.EnumerateDirectories(Path.Combine(slot, "archives")));
    }

    [Fact]
    public void ExistingBoundaryDeferralKeepsAttemptPendingWithoutChangingGeneration()
    {
        using var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        using var coordinator = new NativeProfileCoordinator();
        coordinator.Initialize();
        var generation = coordinator.CurrentGenerationId;
        var boundaryReady = false;
        coordinator.SetCraftingProfileTransitionBoundaryBarrier(() => boundaryReady);

        Assert.False(coordinator.ResetCurrent());
        var token = coordinator.LastUserResetAttempt!.TransitionId;
        Assert.Equal(NativeUserResetOutcome.Pending, coordinator.LastUserResetAttempt.Outcome);
        Assert.Equal(generation, coordinator.CurrentGenerationId);
        Assert.Throws<InvalidOperationException>(() => coordinator.ResetCurrent());
        Assert.Equal(token, coordinator.LastUserResetAttempt.TransitionId);

        boundaryReady = true;
        Assert.True(coordinator.RetryPendingProfileTransition());
        Assert.Equal(token, coordinator.LastUserResetAttempt.TransitionId);
        Assert.Equal(NativeUserResetOutcome.Success, coordinator.LastUserResetAttempt.Outcome);
        Assert.NotEqual(generation, coordinator.CurrentGenerationId);
    }

    [Fact]
    public void ProfileWriterFailurePreservesStagedCraftAndRestoresTrustedLiveHoldingsInOriginalGeneration()
    {
        using var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        var main = new CharacterMainControl
        {
            IsMainCharacter = true, CharacterItem = new Item { Inventory = new Inventory() }
        };
        CharacterMainControl.Main = main;
        PlayerStorage.Inventory = new Inventory();
        PetProxy.PetInventory = new Inventory();
        LevelManager.Instance = new LevelManagerInstance
        {
            MainCharacter = main, PetProxy = new PetProxy { Inventory = PetProxy.PetInventory }
        };
        LevelManager.LevelInited = true;
        LevelManager.LevelInitializing = false;
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 333;
        Saves.SavesSystem.EconomyDataExists = false;
        var monotonicSeconds = 0.0;
        using var coordinator = new NativeProfileCoordinator(() => monotonicSeconds);
        coordinator.Initialize();
        using var holdings = new NativeEconomyHoldingsAdapter(
            () => coordinator.CurrentGenerationId, coordinator.HandleEconomyHoldings,
            coordinator.MarkEconomyHoldingsNotCurrent, coordinator.MarkEconomyHoldingsUnavailable,
            coordinator.SetEconomyHoldingsCapabilities, _ => { });
        holdings.Initialize();
        coordinator.SetEconomyHoldingsBoundaryBarrier(holdings.FlushPending);
        coordinator.EconomyHoldingsProfileResetStarted += holdings.BeginProfileReset;
        coordinator.EconomyHoldingsProfileResetCompleted += holdings.CompleteProfileReset;
        coordinator.ProfileChanging += holdings.BeginProfileChange;
        coordinator.ProfileChanged += holdings.CompleteProfileChange;
        EconomyManager.RaiseMoneyChanged(0, 333);
        holdings.Tick();
        coordinator.SetCraftingCapabilities(Array.Empty<CapabilityRecord>(),
            CraftingNativeContractPolicy.Supported("completion", "formula"));
        coordinator.Flush();
        var generation = coordinator.CurrentGenerationId;
        Assert.Equal(EconomyHoldingObservationState.Current, coordinator.Current!.Statistics.Holdings.Money.State);

        var handoff = new CraftingProfileHandoffBoundary();
        coordinator.CraftingProfileChangeStarted += handoff.Begin;
        coordinator.CraftingProfileChangeCompleted += token =>
        {
            Assert.True(handoff.Complete(token, coordinator.CurrentGenerationId));
            Assert.True(handoff.TryFlushCompleted(coordinator.HandleCrafting));
        };
        var boundaryReady = false;
        coordinator.SetCraftingProfileTransitionBoundaryBarrier(() => boundaryReady);
        Assert.False(coordinator.ResetCurrent());
        var attempt = coordinator.LastUserResetAttempt!;
        var completion = new CraftingCompletionBoundary();
        var completionToken = completion.Begin(new CraftingCompletionEvidence("595", "Ammo", "2301", 30));
        Assert.True(completion.TryComplete(completionToken, CraftingProfileHandoffBoundary.StagedGenerationId,
            DateTime.UtcNow, out var staged));
        Assert.True(handoff.Stage(attempt.TransitionId, staged));
        Assert.True(completion.FinishPublication(completionToken));

        boundaryReady = true;
        using (var held = new FileStream(coordinator.CurrentProfilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.True(coordinator.RetryPendingProfileTransition());

        Assert.Equal(NativeUserResetOutcome.Failure, coordinator.LastUserResetAttempt!.Outcome);
        Assert.Equal(attempt.TransitionId, coordinator.LastUserResetAttempt.TransitionId);
        Assert.Equal(generation, coordinator.CurrentGenerationId);
        Assert.False(handoff.TryGetActiveTransitionId(out _));
        Assert.False(handoff.HasUncommittedData);
        Assert.Equal(1, coordinator.Current.Statistics.Crafting.CompletionActions);
        Assert.Equal(30, coordinator.Current.Statistics.Crafting.ProducedQuantity);
        holdings.Tick();
        Assert.Equal(EconomyHoldingObservationState.Current, coordinator.Current.Statistics.Holdings.Money.State);
        Assert.Equal(333, coordinator.Current.Statistics.Holdings.Money.Value);
        monotonicSeconds += 60; // Existing persistence backoff retains the dirty mutation until due.
        coordinator.Flush();
        var saved = new AtomicJsonStore<ProfileDocument>().Load(coordinator.CurrentProfilePath).Value!;
        Assert.Equal(generation, saved.GenerationId);
        Assert.Equal(30, saved.Statistics.Crafting.ProducedQuantity);
        Assert.Equal(333, saved.Statistics.Holdings.Money.Value);
        Assert.Equal(0, coordinator.CompletedUserResetVersion);
    }

    [Fact]
    public void UnacceptedActiveRunBarrierRemainsPendingBeforeResetAndCanFinishLater()
    {
        using var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        using var coordinator = new NativeProfileCoordinator();
        coordinator.Initialize();
        var ready = false;
        coordinator.SetActiveRunCheckpointBarrier(() => ready);
        var generation = coordinator.CurrentGenerationId;
        Assert.False(coordinator.ResetCurrent());
        Assert.Equal(NativeUserResetOutcome.Pending, coordinator.LastUserResetAttempt!.Outcome);
        Assert.Equal(generation, coordinator.CurrentGenerationId);
        ready = true;
        Assert.True(coordinator.RetryPendingProfileTransition());
        Assert.Equal(NativeUserResetOutcome.Success, coordinator.LastUserResetAttempt.Outcome);
    }

    private static void ResetNative()
    {
        Application.version = "2.3.30";
        Saves.SavesSystem.ResetNativeState();
        LevelManager.ResetNativeState();
        CharacterMainControl.ResetNativeState();
        ItemUtilities.ResetNativeState();
        PlayerStorage.ResetNativeState();
        PetProxy.PetInventory = null;
        EconomyManager.ResetNativeState();
        Duckov.Scenes.SceneLoader.ResetNativeState();
    }
}
