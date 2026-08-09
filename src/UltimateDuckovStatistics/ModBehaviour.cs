using System;
using System.Diagnostics.CodeAnalysis;
using Duckov.Modding;
using UltimateDuckovStatistics.Adapters;
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
            profileCoordinator = new NativeProfileCoordinator();
            profileCoordinator.Initialize();
            healingAttributionAdapter = new NativeHealingAttributionAdapter(
                profileCoordinator.HandleHealing,
                message => Debug.Log($"{LogPrefix} {message}"));
            profileCoordinator.SetHealingCapability(healingAttributionAdapter.Initialize());
            healingAttributionAdapter.CapabilityChanged += profileCoordinator.SetHealingCapability;
            itemUseAdapter = new NativeItemUseAdapter(
                () => profileCoordinator.CurrentGenerationId,
                profileCoordinator.HandleItemUse,
                message => Debug.Log($"{LogPrefix} {message}"),
                healingAttributionAdapter);
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
        if (!initialized)
        {
            return;
        }

        Cleanup();
        Debug.Log($"{LogPrefix} deactivated utc={DateTime.UtcNow:O} instance={GetInstanceID()}");
    }

    private void Update()
    {
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
