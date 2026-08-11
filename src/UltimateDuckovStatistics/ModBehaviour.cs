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
                () => weaponFireAdapter.OwnedValue?.MetricCapabilities ?? new Core.Domain.WeaponMetricCapabilities());
            runLifecycleAdapter.Assign(newRunLifecycleAdapter);
            newRunLifecycleAdapter.Initialize();
            profileCoordinator.ProfileChanging += newRunLifecycleAdapter.InterruptForProfileTransition;
            var newWeaponFireAdapter = new NativeWeaponFireAdapter(
                () => profileCoordinator.CurrentGenerationId,
                () => runLifecycleAdapter.OwnedValue?.CurrentRunId,
                () => runLifecycleAdapter.OwnedValue?.CurrentMapId,
                shot => runLifecycleAdapter.OwnedValue?.RecordShot(shot) == true,
                profileCoordinator.SetWeaponCapabilities,
                message => Debug.Log($"{LogPrefix} {message}"));
            weaponFireAdapter.Assign(newWeaponFireAdapter);
            newWeaponFireAdapter.Initialize();
            profileCoordinator.ProfileChanging += newWeaponFireAdapter.ResetSequence;
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
            && weaponFireAdapter.OwnedValue == null)
        {
            return;
        }

        Cleanup();
        Debug.Log($"{LogPrefix} deactivated utc={DateTime.UtcNow:O} instance={GetInstanceID()}");
    }

    private void Update()
    {
        runLifecycleAdapter.OwnedValue?.Tick();
        itemUseAdapter?.Tick(DateTime.UtcNow);
        healingAttributionAdapter?.Tick();
        statisticsPanel?.Tick();
    }

    private void OnGUI()
    {
        statisticsPanel?.Draw();
    }

    private void OnApplicationQuit()
    {
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

        if (profileCoordinator != null && ownedWeaponFireAdapter != null)
        {
            profileCoordinator.ProfileChanging -= ownedWeaponFireAdapter.ResetSequence;
        }

        if (!weaponFireAdapter.TryCleanupOwned())
        {
            Debug.LogWarning($"{LogPrefix} weapon-fire adapter retained for a later cleanup retry.");
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
