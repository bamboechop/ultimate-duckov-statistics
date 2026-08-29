using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class NativeHotPathDiagnosticsTestGroup
{
    public const string CollectionName = "Native hot-path diagnostics";
}

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativeHotPathDiagnosticsTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public void DiagnosticCountersSeparateCallbacksReductionsClonesAndStores()
    {
        NativeHotPathDiagnostics.Reset();
        for (var index = 0; index < 100; index++)
        {
            NativeHotPathDiagnostics.CountAcceptedFiringCallback();
            NativeHotPathDiagnostics.CountTrackerShotMutation();
            NativeHotPathDiagnostics.CountProjectileCapture();
        }
        for (var index = 0; index < 2_500; index++)
        {
            NativeHotPathDiagnostics.CountProjectileScopeAttempt();
            NativeHotPathDiagnostics.CountProjectileScopePush();
        }
        NativeHotPathDiagnostics.CountCheckpointClone();
        NativeHotPathDiagnostics.CountCheckpointStoreAttempt();
        NativeHotPathDiagnostics.CountCheckpointStoreSuccess();
        NativeHotPathDiagnostics.CountProfileSnapshotCapture();
        NativeHotPathDiagnostics.CountProfileStoreAttempt();
        NativeHotPathDiagnostics.CountProfileStoreSuccess();
        NativeHotPathDiagnostics.CountEconomyHoldingsDirtySignal();
        NativeHotPathDiagnostics.CountEconomyHoldingsReadinessCheck();
        NativeHotPathDiagnostics.CountEconomyHoldingsCashScan();
        NativeHotPathDiagnostics.CountEconomyHoldingsPublication();
        for (var index = 0; index < 12; index++)
        {
            NativeHotPathDiagnostics.CountHarmonyPatchSetInspection();
        }

        var snapshot = NativeHotPathDiagnostics.Snapshot();
        Assert.Equal(100, snapshot.AcceptedFiringCallbacks);
        Assert.Equal(100, snapshot.TrackerShotMutations);
        Assert.Equal(100, snapshot.ProjectileCaptures);
        Assert.Equal(2_500, snapshot.ProjectileScopeAttempts);
        Assert.Equal(2_500, snapshot.ProjectileScopesPushed);
        Assert.Equal(12, snapshot.HarmonyPatchSetInspections);
        Assert.Equal(1, snapshot.CheckpointClones);
        Assert.Equal(1, snapshot.CheckpointStoreAttempts);
        Assert.Equal(1, snapshot.CheckpointStoreSuccesses);
        Assert.Equal(1, snapshot.ProfileSnapshotCaptures);
        Assert.Equal(1, snapshot.ProfileStoreAttempts);
        Assert.Equal(1, snapshot.ProfileStoreSuccesses);
        Assert.Equal(1, snapshot.EconomyHoldingsDirtySignals);
        Assert.Equal(1, snapshot.EconomyHoldingsReadinessChecks);
        Assert.Equal(1, snapshot.EconomyHoldingsCashScans);
        Assert.Equal(1, snapshot.EconomyHoldingsPublications);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void SummaryIsOneBoundedDiagnosticInsteadOfPerEventLogging()
    {
        NativeHotPathDiagnostics.Reset();
        NativeHotPathDiagnostics.CountHealthPrefix();
        NativeHotPathDiagnostics.CountHealthTransition();
        NativeHotPathDiagnostics.CountEquipmentAssociationRequest();
        NativeHotPathDiagnostics.CountEquipmentSnapshotBuild();
        NativeHotPathDiagnostics.CountEquipmentUnchangedPublication();
        NativeHotPathDiagnostics.CountHarmonyPatchSetInspection();
        var messages = new List<string>();

        NativeHotPathDiagnostics.WriteSummary(messages.Add);
        NativeHotPathDiagnostics.WriteSummary(messages.Add);

        var message = Assert.Single(messages);
        Assert.Contains("healthPrefixes=1 healthTransitions=1", message, StringComparison.Ordinal);
        Assert.Contains("equipmentAssociationRequests=1 equipmentBuilds=1", message, StringComparison.Ordinal);
        Assert.Contains("equipmentUnchanged=1", message, StringComparison.Ordinal);
        Assert.Contains("harmonyPatchSetInspections=1", message, StringComparison.Ordinal);
        Assert.Contains("M15Holdings", message, StringComparison.Ordinal);
        Assert.Contains("cashScans=0 publications=0", message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void DiagnosticControlResetsAndEmitsOneExactIntervalSummary()
    {
        NativeHotPathDiagnostics.Reset();
        NativeHotPathDiagnostics.CountAcceptedFiringCallback();
        var messages = new List<string>();

        NativeHotPathDiagnostics.HandleControl(resetRequested: true, summaryRequested: false, messages.Add);
        NativeHotPathDiagnostics.CountAcceptedFiringCallback();
        NativeHotPathDiagnostics.CountAcceptedFiringCallback();
        NativeHotPathDiagnostics.HandleControl(resetRequested: false, summaryRequested: true, messages.Add);
        NativeHotPathDiagnostics.HandleControl(resetRequested: false, summaryRequested: true, messages.Add);

        Assert.Equal(2, messages.Count);
        Assert.Equal("M8.1 diagnostic counter interval reset.", messages[0]);
        Assert.Contains("firing=2", messages[1], StringComparison.Ordinal);
    }
}
