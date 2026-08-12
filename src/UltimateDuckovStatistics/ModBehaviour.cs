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
    private readonly ProcessLifetimeCleanupOwner<NativeRunLifecycleAdapter> runLifecycleAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeWeaponFireAdapter> weaponFireAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeCombatAttributionAdapter> combatAttributionAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeEquipmentAdapter> equipmentAdapter = new();
    private readonly ProcessLifetimeCleanupOwner<NativeContainerAdapter> containerAdapter = new();
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

            profileCoordinator = new NativeProfileCoordinator();
            profileCoordinator.Initialize();
            healingAttributionAdapter = new NativeHealingAttributionAdapter(
                profileCoordinator.HandleHealing,
                message => Debug.Log($"{LogPrefix} {message}"));
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
                () => equipmentAdapter.OwnedValue?.MetricCapabilities ?? new Core.Domain.EquipmentMetricCapabilities(),
                () => containerAdapter.OwnedValue?.MetricCapabilities ?? new Core.Statistics.ContainerMetricCapabilities());
            runLifecycleAdapter.Assign(newRunLifecycleAdapter);
            var newContainerAdapter = new NativeContainerAdapter(
                () => profileCoordinator.CurrentGenerationId,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                () => runLifecycleAdapter.OwnedValue?.IsActive == true,
                value => runLifecycleAdapter.OwnedValue?.RecordContainer(value) == true,
                profileCoordinator.SetContainerCapabilities,
                capabilities => runLifecycleAdapter.OwnedValue?.UpdateContainerCapabilities(capabilities),
                message => Debug.Log($"{LogPrefix} {message}"));
            containerAdapter.Assign(newContainerAdapter);
            newContainerAdapter.Initialize();
            var newEquipmentAdapter = new NativeEquipmentAdapter(
                () => runLifecycleAdapter.OwnedValue?.IsActive == true,
                snapshot => runLifecycleAdapter.OwnedValue?.ObserveEquipment(snapshot) == true,
                () => runLifecycleAdapter.OwnedValue?.InvalidateEquipmentObservation() == true,
                profileCoordinator.SetEquipmentCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"));
            equipmentAdapter.Assign(newEquipmentAdapter);
            newEquipmentAdapter.Initialize();
            var newWeaponFireAdapter = new NativeWeaponFireAdapter(
                () => profileCoordinator.CurrentGenerationId,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                shot => runLifecycleAdapter.OwnedValue?.RecordShot(shot) == true,
                profileCoordinator.SetWeaponCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"),
                newEquipmentAdapter.CaptureAssociation);
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
                    profileCoordinator.SetCombatCapabilities(capabilities);
                    if (newCombatAttributionAdapter != null)
                    {
                        runLifecycleAdapter.OwnedValue?.UpdateCombatCapabilities(
                            newCombatAttributionAdapter.MetricCapabilities);
                    }
                },
                message => Debug.Log($"{LogPrefix} {message}"),
                newEquipmentAdapter.CaptureAssociation);
            combatAttributionAdapter.Assign(newCombatAttributionAdapter);
            newCombatAttributionAdapter.Initialize();
            newRunLifecycleAdapter.SetPlayerDeathObserver(newCombatAttributionAdapter.RecordPlayerDeath);
            newRunLifecycleAdapter.Initialize();
            profileCoordinator.ProfileChanging += newRunLifecycleAdapter.InterruptForProfileTransition;
            itemUseAdapter = new NativeItemUseAdapter(
                () => profileCoordinator.CurrentGenerationId,
                profileCoordinator.HandleItemUse,
                message => Debug.Log($"{LogPrefix} {message}"),
                healingAttributionAdapter,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId);
            profileCoordinator.ProfileChanged += itemUseAdapter.ResetPending;
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
            && containerAdapter.OwnedValue == null)
        {
            return;
        }

        Cleanup();
        Debug.Log($"{LogPrefix} deactivated utc={DateTime.UtcNow:O} instance={GetInstanceID()}");
    }

    private void Update()
    {
        runLifecycleAdapter.OwnedValue?.Tick();
        equipmentAdapter.OwnedValue?.Tick();
        itemUseAdapter?.Tick(DateTime.UtcNow);
        healingAttributionAdapter?.Tick();
        combatAttributionAdapter.OwnedValue?.Tick();
        containerAdapter.OwnedValue?.Tick();
        statisticsPanel?.Tick();
    }

    private void OnGUI()
    {
        statisticsPanel?.Draw();
    }

    private void OnApplicationQuit()
    {
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
        var ownedRunLifecycleAdapter = runLifecycleAdapter.OwnedValue;
        var ownedWeaponFireAdapter = weaponFireAdapter.OwnedValue;
        if (profileCoordinator != null && ownedRunLifecycleAdapter != null)
        {
            profileCoordinator.ProfileChanging -= ownedRunLifecycleAdapter.InterruptForProfileTransition;
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

        if (!runLifecycleAdapter.TryCleanupOwned())
        {
            Debug.LogWarning($"{LogPrefix} run-lifecycle adapter retained for a later cleanup retry.");
        }

        if (profileCoordinator != null && itemUseAdapter != null)
        {
            profileCoordinator.ProfileChanged -= itemUseAdapter.ResetPending;
        }

        itemUseAdapter?.Dispose();
        itemUseAdapter = null;
        if (healingAttributionAdapter != null && profileCoordinator != null)
        {
            healingAttributionAdapter.CapabilityChanged -= profileCoordinator.SetHealingCapability;
        }

        healingAttributionAdapter?.Dispose();
        healingAttributionAdapter = null;
        statisticsPanel = null;
        profileCoordinator?.Dispose();
        profileCoordinator = null;
        initialized = false;
    }
}
