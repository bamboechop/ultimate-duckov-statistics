using System;
using System.Diagnostics.CodeAnalysis;
using Duckov.Modding;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.UI;
using UnityEngine;

namespace UltimateDuckovStatistics;

[SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "Unity owns ModBehaviour lifetime; OnBeforeDeactivate and OnDestroy perform idempotent cleanup.")]
public sealed class ModBehaviour : Duckov.Modding.ModBehaviour
{
    private const string LogPrefix = "[UDS]";
    private bool initialized;
    private NativeProfileCoordinator? profileCoordinator;
    private NativeHealingAttributionAdapter? healingAttributionAdapter;
    private NativeItemUseAdapter? itemUseAdapter;
    private NativeEconomyAdapter? economyAdapter;
    private Action? economyActivationForProfileChange;
    private readonly ProcessLifetimeCleanupOwner<NativeRunLifecycleAdapter> runLifecycleAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeWeaponFireAdapter> weaponFireAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeCombatAttributionAdapter> combatAttributionAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeEquipmentAdapter> equipmentAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeContainerAdapter> containerAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeWorldTimeAdapter> worldTimeAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeCraftingAdapter> craftingAdapter = new();
    private NativeStatisticsPanel? statisticsPanel;

    protected override void OnAfterSetup()
    {
        if (initialized)
        {
            Debug.LogWarning($"{LogPrefix} duplicate setup ignored");
            return;
        }

        try
        {
            NativeHotPathDiagnostics.Reset();
            if (runLifecycleAdapter.HasValue
                && (!runLifecycleAdapter.HasPendingCleanup || !runLifecycleAdapter.TryCleanupPending()))
            {
                Debug.LogError(
                    $"{LogPrefix} activation blocked while another run-lifecycle owner is active "
                    + "or prior subscriptions await cleanup.");
                return;
            }

            if (weaponFireAdapter.HasValue
                && (!weaponFireAdapter.HasPendingCleanup || !weaponFireAdapter.TryCleanupPending()))
            {
                Debug.LogError(
                    $"{LogPrefix} activation blocked while another weapon-fire owner is active "
                    + "or prior subscriptions await cleanup.");
                return;
            }

            if (combatAttributionAdapter.HasValue
                && (!combatAttributionAdapter.HasPendingCleanup || !combatAttributionAdapter.TryCleanupPending()))
            {
                Debug.LogError(
                    $"{LogPrefix} activation blocked while another combat-attribution owner is active "
                    + "or prior patches await cleanup.");
                return;
            }

            if (equipmentAdapter.HasValue
                && (!equipmentAdapter.HasPendingCleanup || !equipmentAdapter.TryCleanupPending()))
            {
                Debug.LogError(
                    $"{LogPrefix} activation blocked while another equipment owner is active "
                    + "or prior subscriptions await cleanup.");
                return;
            }

            if (containerAdapter.HasValue
                && (!containerAdapter.HasPendingCleanup || !containerAdapter.TryCleanupPending()))
            {
                Debug.LogError(
                    $"{LogPrefix} activation blocked while another container owner is active "
                    + "or prior subscriptions/patches await cleanup.");
                return;
            }

            if (worldTimeAdapter.HasValue
                && (!worldTimeAdapter.HasPendingCleanup || !worldTimeAdapter.TryCleanupPending()))
            {
                Debug.LogError(
                    $"{LogPrefix} activation blocked while another world-time owner is active "
                    + "or prior subscriptions/patches await cleanup.");
                return;
            }

            if (craftingAdapter.HasValue
                && (!craftingAdapter.HasPendingCleanup || !craftingAdapter.TryCleanupPending()))
            {
                Debug.LogError(
                    $"{LogPrefix} activation blocked while another crafting owner is active "
                    + "or prior patches/completions await cleanup.");
                return;
            }

            var newProfileCoordinator = new NativeProfileCoordinator();
            profileCoordinator = newProfileCoordinator;
            newProfileCoordinator.Initialize();
            var worldTimeGenerationProvider = NativeWorldTimeProfileBinding.CaptureGenerationProvider(
                newProfileCoordinator,
                static coordinator => coordinator.CurrentGenerationId);
            var newWorldTimeAdapter = new NativeWorldTimeAdapter(
                worldTimeGenerationProvider,
                newProfileCoordinator.HandleWorldTime,
                newProfileCoordinator.RequestWorldTimePersistence,
                newProfileCoordinator.SetWorldTimeCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"));
            worldTimeAdapter.Assign(newWorldTimeAdapter);
            newWorldTimeAdapter.Initialize();
            newProfileCoordinator.SetWorldTimeBoundaryBarrier(newWorldTimeAdapter.FlushPending);
            newProfileCoordinator.WorldTimeProfileChangeAwaitingNativeLoadStarted += newWorldTimeAdapter.BeginProfileChangeAwaitingNativeLoad;
            newProfileCoordinator.WorldTimeNewGameProfileChangeStarted += newWorldTimeAdapter.BeginNewGameProfileChange;
            newProfileCoordinator.WorldTimeProfileChangeCompleted += newWorldTimeAdapter.CompleteProfileChange;
            newProfileCoordinator.WorldTimeSameProfileReopenCompleted += newWorldTimeAdapter.CompleteProfileChangeWithCurrentClock;
            newProfileCoordinator.WorldTimeProfileChangedWithCurrentClock += newWorldTimeAdapter.ResetForProfileChangeWithCurrentClock;
            newWorldTimeAdapter.SetProfileTransitionCleanupBarrier(newProfileCoordinator.DrainPendingProfileTransitions);
            var newCraftingAdapter = new NativeCraftingAdapter(
                () => newProfileCoordinator.CurrentGenerationId,
                newProfileCoordinator.HandleCrafting,
                newProfileCoordinator.RequestCraftingPersistence,
                newProfileCoordinator.SetCraftingCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"),
                craftingAdapter.TryCleanupPending);
            craftingAdapter.Assign(newCraftingAdapter);
            newCraftingAdapter.Initialize();
            newProfileCoordinator.SetCraftingBoundaryBarrier(newCraftingAdapter.FlushPending);
            newProfileCoordinator.SetCraftingProfileTransitionBoundaryBarrier(
                newCraftingAdapter.FlushPendingForProfileTransition);
            newProfileCoordinator.CraftingProfileChangeStarted += newCraftingAdapter.BeginProfileChange;
            newProfileCoordinator.CraftingProfileChangeCompleted += newCraftingAdapter.CompleteProfileChange;
            newCraftingAdapter.SetProfileTransitionCleanupBarrier(newProfileCoordinator.DrainPendingProfileTransitions);
            var economyFlowPublication = new EconomyFlowPublication(
                profileCoordinator.HandleCurrencyFlow,
                flow => runLifecycleAdapter.OwnedValue?.RecordCurrencyFlow(flow) == true,
                message => Debug.LogError($"{LogPrefix} {message}"));
            NativeEconomyAdapter? newEconomyAdapter = null;
            newEconomyAdapter = new NativeEconomyAdapter(
                () => profileCoordinator.CurrentGenerationId,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                () => runLifecycleAdapter.OwnedValue?.CurrentSegmentId,
                () => runLifecycleAdapter.OwnedValue?.IsActive == true,
                flow => profileCoordinator.RetryPendingEconomyActivation()
                        && economyFlowPublication.Publish(flow),
                capabilities =>
                {
                    if (newEconomyAdapter == null) return;
                    NativeContainerAdapter.PublishIndependently(
                        () => profileCoordinator.SetEconomyCapabilities(capabilities, newEconomyAdapter.MetricCapabilities),
                        () => runLifecycleAdapter.OwnedValue?.UpdateEconomyCapabilities(newEconomyAdapter.MetricCapabilities));
                },
                message => Debug.Log($"{LogPrefix} {message}"),
                profileCoordinator.RetryPendingEconomyActivation);
            economyAdapter = newEconomyAdapter;
            profileCoordinator.BeginEconomyActivation(newEconomyAdapter.ActivationId);
            newEconomyAdapter.Initialize();
            profileCoordinator.SetEconomyBoundaryBarrier(newEconomyAdapter.FlushPendingForBoundary);
            var buffApplicationObservationBoundary = new NativeBuffApplicationObservationBoundary();
            healingAttributionAdapter = new NativeHealingAttributionAdapter(
                healing =>
                {
                    profileCoordinator.HandleHealing(healing);
                    runLifecycleAdapter.OwnedValue?.RecordHealing(healing);
                },
                message => Debug.Log($"{LogPrefix} {message}"),
                buffApplicationObservationBoundary,
                () => runLifecycleAdapter.OwnedValue?.CurrentEventContext);
            profileCoordinator.SetHealingCapability(healingAttributionAdapter.Initialize());
            healingAttributionAdapter.CapabilityChanged += profileCoordinator.SetHealingCapability;
            var newRunLifecycleAdapter = new NativeRunLifecycleAdapter(
                () => profileCoordinator.CurrentGenerationId,
                profileCoordinator.HandleRunCheckpoint,
                profileCoordinator.HandleRunCompleted,
                profileCoordinator.SetRunCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"),
                () => weaponFireAdapter.OwnedValue?.MetricCapabilities ?? new Core.Domain.WeaponMetricCapabilities(),
                () => combatAttributionAdapter.OwnedValue?.MetricCapabilities ?? new Core.Domain.CombatMetricCapabilities(),
                () => equipmentAdapter.OwnedValue?.CaptureCapabilitiesForRunStart()
                    ?? new Core.Domain.EquipmentMetricCapabilities(),
                () => containerAdapter.OwnedValue?.MetricCapabilities ?? new Core.Statistics.ContainerMetricCapabilities(),
                () => economyAdapter?.MetricCapabilities ?? new Core.Domain.EconomyMetricCapabilities(),
                profileCoordinator.PollRunCheckpoint,
                profileCoordinator.FlushRunCheckpoint);
            runLifecycleAdapter.Assign(newRunLifecycleAdapter);
            profileCoordinator.SetActiveRunCheckpointBarrier(newRunLifecycleAdapter.FlushCheckpoint);
            var newContainerAdapter = new NativeContainerAdapter(
                () => profileCoordinator.CurrentGenerationId,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                () => runLifecycleAdapter.OwnedValue?.IsActive == true,
                value => runLifecycleAdapter.OwnedValue?.RecordContainer(value) == true,
                profileCoordinator.SetContainerCapabilities,
                capabilities => runLifecycleAdapter.OwnedValue?.UpdateContainerCapabilities(capabilities),
                message => Debug.Log($"{LogPrefix} {message}"),
                () => runLifecycleAdapter.OwnedValue?.CurrentSegmentId);
            containerAdapter.Assign(newContainerAdapter);
            newContainerAdapter.Initialize();
            var newEquipmentAdapter = new NativeEquipmentAdapter(
                () => runLifecycleAdapter.OwnedValue?.IsActive == true,
                snapshot => runLifecycleAdapter.OwnedValue?.ObserveEquipment(snapshot) == true,
                () => runLifecycleAdapter.OwnedValue?.InvalidateEquipmentObservation() == true,
                profileCoordinator.SetEquipmentCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"),
                observationContextProvider: () => newRunLifecycleAdapter.CurrentSegmentId);
            equipmentAdapter.Assign(newEquipmentAdapter);
            newEquipmentAdapter.Initialize();
            newRunLifecycleAdapter.SetDestinationReadyObserver(() => newEquipmentAdapter.CaptureAssociation());
            newRunLifecycleAdapter.SetTerminalObserver(newEconomyAdapter.FlushPendingForBoundary);
            var newWeaponFireAdapter = new NativeWeaponFireAdapter(
                () => profileCoordinator.CurrentGenerationId,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                shot => runLifecycleAdapter.OwnedValue?.RecordShot(shot) == true,
                profileCoordinator.SetWeaponCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"),
                newEquipmentAdapter.CaptureAssociation,
                () => runLifecycleAdapter.OwnedValue?.CurrentSegmentId);
            weaponFireAdapter.Assign(newWeaponFireAdapter);
            newWeaponFireAdapter.Initialize();
            NativeCombatAttributionAdapter? newCombatAttributionAdapter = null;
            newCombatAttributionAdapter = new NativeCombatAttributionAdapter(
                () => profileCoordinator.CurrentGenerationId,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                value => runLifecycleAdapter.OwnedValue?.RecordCombat(value) == true,
                capabilities =>
                {
                    NativeContainerAdapter.PublishIndependently(
                        () => profileCoordinator.SetCombatCapabilities(capabilities),
                        () =>
                        {
                            if (newCombatAttributionAdapter != null)
                            {
                                runLifecycleAdapter.OwnedValue?.UpdateCombatCapabilities(
                                    newCombatAttributionAdapter.MetricCapabilities);
                            }
                        });
                },
                message => Debug.Log($"{LogPrefix} {message}"),
                buffApplicationObservationBoundary,
                newEquipmentAdapter.CaptureAssociation,
                () => runLifecycleAdapter.OwnedValue?.CurrentSegmentId);
            combatAttributionAdapter.Assign(newCombatAttributionAdapter);
            newCombatAttributionAdapter.Initialize();
            newRunLifecycleAdapter.SetPlayerDeathObserver(newCombatAttributionAdapter.RecordPlayerDeath);
            newRunLifecycleAdapter.Initialize();
            profileCoordinator.ProfileChanging += newRunLifecycleAdapter.InterruptForProfileTransition;
            itemUseAdapter = new NativeItemUseAdapter(
                () => profileCoordinator.CurrentGenerationId,
                completion =>
                {
                    return ItemUsePublication.PublishIndependently(
                        () => profileCoordinator.HandleItemUse(completion),
                        () => completion.NormalizedEvent != null
                              && runLifecycleAdapter.OwnedValue?.RecordItemUse(completion.NormalizedEvent) == true);
                },
                message => Debug.Log($"{LogPrefix} {message}"),
                healingAttributionAdapter,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                () => runLifecycleAdapter.OwnedValue?.CurrentSegmentId);
            profileCoordinator.ProfileChanged += itemUseAdapter.ResetPending;
            economyActivationForProfileChange = () =>
                profileCoordinator.BeginEconomyActivation(newEconomyAdapter.ActivationId);
            profileCoordinator.ProfileChanged += economyActivationForProfileChange;
            profileCoordinator.ProfileChanged += newEconomyAdapter.ResetBaselines;
            itemUseAdapter.Subscribe();
            statisticsPanel = new NativeStatisticsPanel(profileCoordinator);
            initialized = true;
            Debug.Log(
                $"{LogPrefix} activated utc={DateTime.UtcNow:O} " +
                $"instance={GetInstanceID()} packageVersion={info.version} coreVersion={Core.ProductInfo.Version} " +
                $"dataRoot={profileCoordinator.DataRoot}");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"{LogPrefix} activation failed; item tracking is disabled.");
            Cleanup();
        }
    }

    protected override void OnBeforeDeactivate()
    {
        if (!initialized
            && runLifecycleAdapter.OwnedValue == null
            && weaponFireAdapter.OwnedValue == null
            && combatAttributionAdapter.OwnedValue == null
            && equipmentAdapter.OwnedValue == null
            && containerAdapter.OwnedValue == null
            && worldTimeAdapter.OwnedValue == null
            && craftingAdapter.OwnedValue == null)
        {
            return;
        }

        Cleanup();
        Debug.Log($"{LogPrefix} deactivated utc={DateTime.UtcNow:O} instance={GetInstanceID()}");
    }

    private void Update()
    {
        profileCoordinator?.RetryPendingProfileTransition();
        var economyActivationReady = profileCoordinator?.RetryPendingEconomyActivation() != false;
        NativeHotPathDiagnostics.HandleControl(
            Input.GetKeyDown(KeyCode.F9),
            Input.GetKeyDown(KeyCode.F10),
            message => Debug.Log($"{LogPrefix} {message}"));
        runLifecycleAdapter.OwnedValue?.Tick();
        equipmentAdapter.OwnedValue?.Tick();
        itemUseAdapter?.Tick(DateTime.UtcNow);
        if (economyActivationReady) economyAdapter?.Tick();
        healingAttributionAdapter?.Tick();
        combatAttributionAdapter.OwnedValue?.Tick();
        containerAdapter.OwnedValue?.Tick();
        worldTimeAdapter.OwnedValue?.Tick(DateTime.UtcNow);
        craftingAdapter.OwnedValue?.Tick(DateTime.UtcNow);
        profileCoordinator?.TickProfilePersistence(
            runLifecycleAdapter.OwnedValue?.HasUncheckpointedRunMutations != true);
        statisticsPanel?.Tick();
    }

    private void OnGUI()
    {
        statisticsPanel?.Draw();
    }

    private void OnApplicationQuit()
    {
        DrainPendingProfileTransitions("application quit");
        FlushPendingEconomy("application quit");
        FlushPendingWorldTime("application quit");
        FinalizeCraftingForTerminalShutdown();
        runLifecycleAdapter.OwnedValue?.FlushCheckpoint();
        profileCoordinator?.Flush();
        Debug.Log(
            $"{LogPrefix} application-quitting utc={DateTime.UtcNow:O} " +
            $"instance={GetInstanceID()} active={initialized}");
    }

    private void OnDestroy()
    {
        Debug.Log(
            $"{LogPrefix} destroyed utc={DateTime.UtcNow:O} " +
            $"instance={GetInstanceID()} active={initialized}");
        Cleanup();
    }

    private void Cleanup()
    {
        var profileTransitionsDrained = DrainPendingProfileTransitions("deactivation");
        FlushPendingEconomy("deactivation");
        FlushPendingWorldTime("deactivation");
        FlushPendingCrafting("deactivation");
        var ownedRunLifecycleAdapter = runLifecycleAdapter.OwnedValue;
        var ownedWeaponFireAdapter = weaponFireAdapter.OwnedValue;
        var ownedCraftingAdapter = craftingAdapter.OwnedValue;
        if (profileCoordinator != null)
        {
            if (ownedRunLifecycleAdapter != null)
                profileCoordinator.ProfileChanging -= ownedRunLifecycleAdapter.InterruptForProfileTransition;
            if (profileTransitionsDrained && worldTimeAdapter.OwnedValue != null)
            {
                profileCoordinator.WorldTimeProfileChangeAwaitingNativeLoadStarted -= worldTimeAdapter.OwnedValue.BeginProfileChangeAwaitingNativeLoad;
                profileCoordinator.WorldTimeNewGameProfileChangeStarted -= worldTimeAdapter.OwnedValue.BeginNewGameProfileChange;
                profileCoordinator.WorldTimeProfileChangeCompleted -= worldTimeAdapter.OwnedValue.CompleteProfileChange;
                profileCoordinator.WorldTimeSameProfileReopenCompleted -= worldTimeAdapter.OwnedValue.CompleteProfileChangeWithCurrentClock;
                profileCoordinator.WorldTimeProfileChangedWithCurrentClock -= worldTimeAdapter.OwnedValue.ResetForProfileChangeWithCurrentClock;
            }
        }

        if (!weaponFireAdapter.TryCleanupOwned())
        {
            Debug.LogWarning($"{LogPrefix} weapon-fire adapter retained for a later cleanup retry.");
        }

        if (!combatAttributionAdapter.TryCleanupOwned())
        {
            Debug.LogWarning($"{LogPrefix} combat-attribution adapter retained for a later cleanup retry.");
        }

        if (!equipmentAdapter.TryCleanupOwned())
        {
            Debug.LogWarning($"{LogPrefix} equipment adapter retained for a later cleanup retry.");
        }

        if (!containerAdapter.TryCleanupOwned())
        {
            Debug.LogWarning($"{LogPrefix} container adapter retained for a later cleanup retry.");
        }

        if (profileCoordinator != null && itemUseAdapter != null)
        {
            profileCoordinator.ProfileChanged -= itemUseAdapter.ResetPending;
        }

        itemUseAdapter?.Dispose();
        itemUseAdapter = null;
        if (profileCoordinator != null && economyAdapter != null)
            profileCoordinator.ProfileChanged -= economyAdapter.ResetBaselines;
        if (profileCoordinator != null && economyActivationForProfileChange != null)
            profileCoordinator.ProfileChanged -= economyActivationForProfileChange;
        economyActivationForProfileChange = null;
        economyAdapter?.Dispose();
        economyAdapter = null;
        if (healingAttributionAdapter != null && profileCoordinator != null)
        {
            healingAttributionAdapter.CapabilityChanged -= profileCoordinator.SetHealingCapability;
        }

        healingAttributionAdapter?.Dispose();
        healingAttributionAdapter = null;
        statisticsPanel = null;

        var retainedProfileCoordinator = profileCoordinator;
        var coordinatorCleanupGate = new CleanupCompletionGate(
            3,
            () => retainedProfileCoordinator?.Dispose());
        if (craftingAdapter.TryCleanupOwned(coordinatorCleanupGate.Signal))
        {
            if (retainedProfileCoordinator != null && ownedCraftingAdapter != null)
            {
                retainedProfileCoordinator.CraftingProfileChangeStarted -= ownedCraftingAdapter.BeginProfileChange;
                retainedProfileCoordinator.CraftingProfileChangeCompleted -= ownedCraftingAdapter.CompleteProfileChange;
            }
            coordinatorCleanupGate.Signal();
        }
        else
        {
            Debug.LogWarning(
                $"{LogPrefix} crafting adapter and profile coordinator retained for a later cleanup retry.");
        }
        if (worldTimeAdapter.TryCleanupOwned(coordinatorCleanupGate.Signal))
        {
            coordinatorCleanupGate.Signal();
        }
        else
        {
            Debug.LogWarning(
                $"{LogPrefix} world-time adapter and profile coordinator retained for a later cleanup retry.");
        }
        if (runLifecycleAdapter.TryCleanupOwned(coordinatorCleanupGate.Signal))
        {
            coordinatorCleanupGate.Signal();
        }
        else
        {
            Debug.LogWarning(
                $"{LogPrefix} run-lifecycle adapter and profile coordinator retained for a later cleanup retry.");
        }
        profileCoordinator = null;
        NativeHotPathDiagnostics.WriteSummary(message => Debug.Log($"{LogPrefix} {message}"));
        initialized = false;
    }

    private bool DrainPendingProfileTransitions(string boundary)
    {
        try
        {
            if (profileCoordinator?.DrainPendingProfileTransitions() != false) return true;
            Debug.LogWarning(
                $"{LogPrefix} queued profile transitions remain pending during {boundary}; "
                + "world-time and crafting cleanup will retain staged data for retry.");
            return false;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            return false;
        }
    }

    private void FlushPendingEconomy(string boundary)
    {
        try
        {
            if (economyAdapter?.FlushPendingForBoundary() == false)
                Debug.LogError($"{LogPrefix} economy boundary flush remains pending during {boundary}.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"{LogPrefix} economy boundary flush failed during {boundary}.");
        }
    }

    private void FlushPendingWorldTime(string boundary)
    {
        try
        {
            if (worldTimeAdapter.OwnedValue?.FlushPending() == false)
                Debug.LogError($"{LogPrefix} world-time aggregate flush remains pending during {boundary}.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"{LogPrefix} world-time aggregate flush failed during {boundary}.");
        }
    }

    private void FlushPendingCrafting(string boundary)
    {
        try
        {
            if (craftingAdapter.OwnedValue?.FlushPending() == false)
                Debug.LogError($"{LogPrefix} crafting aggregate or profile-handoff flush remains pending during {boundary}.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"{LogPrefix} crafting aggregate flush failed during {boundary}.");
        }
    }

    private void FinalizeCraftingForTerminalShutdown()
    {
        try
        {
            if (craftingAdapter.OwnedValue?.TryCleanupForTerminalShutdown() == false)
            {
                Debug.LogError(
                    $"{LogPrefix} terminal crafting cleanup remains pending during application quit; "
                    + "a proven completion or aggregate publication has not finished.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            Debug.LogError($"{LogPrefix} terminal crafting cleanup failed during application quit.");
        }
    }

}
