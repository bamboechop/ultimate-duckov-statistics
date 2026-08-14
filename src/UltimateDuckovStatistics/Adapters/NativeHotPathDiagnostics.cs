using System.Diagnostics;
using System.Threading;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeHotPathCounterSnapshot
{
    public long AcceptedFiringCallbacks { get; set; }
    public long TrackerShotMutations { get; set; }
    public long ProjectileCaptures { get; set; }
    public long ProjectileScopeAttempts { get; set; }
    public long ProjectileScopesPushed { get; set; }
    public long ProjectileCompletions { get; set; }
    public long HealthPrefixes { get; set; }
    public long HealthTransitions { get; set; }
    public long TrackerCombatMutations { get; set; }
    public long EquipmentAssociationRequests { get; set; }
    public long EquipmentSnapshotBuilds { get; set; }
    public long EquipmentChangedPublications { get; set; }
    public long EquipmentUnchangedPublications { get; set; }
    public long HarmonyPatchSetInspections { get; set; }
    public long CheckpointClones { get; set; }
    public long CheckpointStoreAttempts { get; set; }
    public long CheckpointStoreSuccesses { get; set; }
    public long ProfileSnapshotCaptures { get; set; }
    public long ProfileStoreAttempts { get; set; }
    public long ProfileStoreSuccesses { get; set; }
}

internal static class NativeHotPathDiagnostics
{
    private static long acceptedFiringCallbacks;
    private static long trackerShotMutations;
    private static long projectileCaptures;
    private static long projectileScopeAttempts;
    private static long projectileScopesPushed;
    private static long projectileCompletions;
    private static long healthPrefixes;
    private static long healthTransitions;
    private static long trackerCombatMutations;
    private static long equipmentAssociationRequests;
    private static long equipmentSnapshotBuilds;
    private static long equipmentChangedPublications;
    private static long equipmentUnchangedPublications;
    private static long harmonyPatchSetInspections;
    private static long checkpointClones;
    private static long checkpointStoreAttempts;
    private static long checkpointStoreSuccesses;
    private static long profileSnapshotCaptures;
    private static long profileStoreAttempts;
    private static long profileStoreSuccesses;
    private static int summaryWritten;

    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")]
    public static void Reset()
    {
        acceptedFiringCallbacks = 0;
        trackerShotMutations = 0;
        projectileCaptures = 0;
        projectileScopeAttempts = 0;
        projectileScopesPushed = 0;
        projectileCompletions = 0;
        healthPrefixes = 0;
        healthTransitions = 0;
        trackerCombatMutations = 0;
        equipmentAssociationRequests = 0;
        equipmentSnapshotBuilds = 0;
        equipmentChangedPublications = 0;
        equipmentUnchangedPublications = 0;
        harmonyPatchSetInspections = 0;
        checkpointClones = 0;
        checkpointStoreAttempts = 0;
        checkpointStoreSuccesses = 0;
        profileSnapshotCaptures = 0;
        profileStoreAttempts = 0;
        profileStoreSuccesses = 0;
        summaryWritten = 0;
    }

    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountAcceptedFiringCallback() => Interlocked.Increment(ref acceptedFiringCallbacks);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountTrackerShotMutation() => Interlocked.Increment(ref trackerShotMutations);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountProjectileCapture() => Interlocked.Increment(ref projectileCaptures);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountProjectileScopeAttempt() => Interlocked.Increment(ref projectileScopeAttempts);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountProjectileScopePush() => Interlocked.Increment(ref projectileScopesPushed);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountProjectileCompletion() => Interlocked.Increment(ref projectileCompletions);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountHealthPrefix() => Interlocked.Increment(ref healthPrefixes);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountHealthTransition() => Interlocked.Increment(ref healthTransitions);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountTrackerCombatMutation() => Interlocked.Increment(ref trackerCombatMutations);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountEquipmentAssociationRequest() => Interlocked.Increment(ref equipmentAssociationRequests);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountEquipmentSnapshotBuild() => Interlocked.Increment(ref equipmentSnapshotBuilds);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountEquipmentChangedPublication() => Interlocked.Increment(ref equipmentChangedPublications);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountEquipmentUnchangedPublication() => Interlocked.Increment(ref equipmentUnchangedPublications);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountHarmonyPatchSetInspection() => Interlocked.Increment(ref harmonyPatchSetInspections);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountCheckpointClone() => Interlocked.Increment(ref checkpointClones);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountCheckpointStoreAttempt() => Interlocked.Increment(ref checkpointStoreAttempts);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountCheckpointStoreSuccess() => Interlocked.Increment(ref checkpointStoreSuccesses);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountProfileSnapshotCapture() => Interlocked.Increment(ref profileSnapshotCaptures);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountProfileStoreAttempt() => Interlocked.Increment(ref profileStoreAttempts);
    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")] public static void CountProfileStoreSuccess() => Interlocked.Increment(ref profileStoreSuccesses);

    public static NativeHotPathCounterSnapshot Snapshot() => new()
    {
        AcceptedFiringCallbacks = Interlocked.Read(ref acceptedFiringCallbacks),
        TrackerShotMutations = Interlocked.Read(ref trackerShotMutations),
        ProjectileCaptures = Interlocked.Read(ref projectileCaptures),
        ProjectileScopeAttempts = Interlocked.Read(ref projectileScopeAttempts),
        ProjectileScopesPushed = Interlocked.Read(ref projectileScopesPushed),
        ProjectileCompletions = Interlocked.Read(ref projectileCompletions),
        HealthPrefixes = Interlocked.Read(ref healthPrefixes),
        HealthTransitions = Interlocked.Read(ref healthTransitions),
        TrackerCombatMutations = Interlocked.Read(ref trackerCombatMutations),
        EquipmentAssociationRequests = Interlocked.Read(ref equipmentAssociationRequests),
        EquipmentSnapshotBuilds = Interlocked.Read(ref equipmentSnapshotBuilds),
        EquipmentChangedPublications = Interlocked.Read(ref equipmentChangedPublications),
        EquipmentUnchangedPublications = Interlocked.Read(ref equipmentUnchangedPublications),
        HarmonyPatchSetInspections = Interlocked.Read(ref harmonyPatchSetInspections),
        CheckpointClones = Interlocked.Read(ref checkpointClones),
        CheckpointStoreAttempts = Interlocked.Read(ref checkpointStoreAttempts),
        CheckpointStoreSuccesses = Interlocked.Read(ref checkpointStoreSuccesses),
        ProfileSnapshotCaptures = Interlocked.Read(ref profileSnapshotCaptures),
        ProfileStoreAttempts = Interlocked.Read(ref profileStoreAttempts),
        ProfileStoreSuccesses = Interlocked.Read(ref profileStoreSuccesses)
    };

    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")]
    public static void WriteSummary(Action<string> diagnostic)
    {
        if (Interlocked.Exchange(ref summaryWritten, 1) != 0) return;
        var value = Snapshot();
        diagnostic(
            "M8.1 diagnostic counters "
            + $"firing={value.AcceptedFiringCallbacks} trackerShots={value.TrackerShotMutations} "
            + $"projectileCaptures={value.ProjectileCaptures} projectileScopeAttempts={value.ProjectileScopeAttempts} "
            + $"projectileScopesPushed={value.ProjectileScopesPushed} projectileCompletions={value.ProjectileCompletions} "
            + $"healthPrefixes={value.HealthPrefixes} healthTransitions={value.HealthTransitions} "
            + $"trackerCombat={value.TrackerCombatMutations} equipmentAssociationRequests={value.EquipmentAssociationRequests} "
            + $"equipmentBuilds={value.EquipmentSnapshotBuilds} equipmentChanged={value.EquipmentChangedPublications} "
            + $"equipmentUnchanged={value.EquipmentUnchangedPublications} harmonyPatchSetInspections={value.HarmonyPatchSetInspections} "
            + $"checkpointClones={value.CheckpointClones} "
            + $"checkpointStoreAttempts={value.CheckpointStoreAttempts} checkpointStoreSuccesses={value.CheckpointStoreSuccesses} "
            + $"profileSnapshotCaptures={value.ProfileSnapshotCaptures} "
            + $"profileStoreAttempts={value.ProfileStoreAttempts} profileStoreSuccesses={value.ProfileStoreSuccesses}.");
    }

    [Conditional("UDS_PERFORMANCE_DIAGNOSTICS")]
    public static void HandleControl(bool resetRequested, bool summaryRequested, Action<string> diagnostic)
    {
        if (resetRequested)
        {
            Reset();
            diagnostic("M8.1 diagnostic counter interval reset.");
        }

        if (summaryRequested)
        {
            WriteSummary(diagnostic);
        }
    }
}
