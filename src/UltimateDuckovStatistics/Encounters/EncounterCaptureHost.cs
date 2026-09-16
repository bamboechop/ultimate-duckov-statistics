using System.Diagnostics;
using System.Runtime.CompilerServices;
using Duckov.Scenes;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.Encounters;

internal sealed class EncounterCaptureHost : IEncounterObservationSink, IDisposable
{
    private sealed class ActorIdentity
    {
        internal ActorIdentity(int id) => Id = id;
        internal int Id { get; }
    }

    private readonly Func<NativeRunLifecycleAdapter?> runProvider;
    private readonly Func<NativeProfileCoordinator?> profileProvider;
    private readonly Action<string> log;
    private ConditionalWeakTable<UnityEngine.Object, ActorIdentity> actors = new();
    private readonly List<IEncounterObserver> probes = new();
    private EncounterObservationContext context = new();
    private string actorRun = string.Empty;
    private string actorGeneration = string.Empty;
    private int nextActor;
    private bool disposed;
    private readonly EncounterCapturePipeline pipeline = new();
    private double nextBatch;
    private double runSeconds;
    private bool captureFailureReported;

    internal EncounterCaptureHost(Func<NativeRunLifecycleAdapter?> runProvider,
        Func<NativeProfileCoordinator?> profileProvider, Action<string> log)
    {
        this.runProvider = runProvider;
        this.profileProvider = profileProvider;
        this.log = log;
    }

#if UDS_ENCOUNTER_DIAGNOSTICS
    private Diagnostics.EncounterCaptureDiagnostics? diagnostics;
    internal void EnableDiagnostics(Func<bool> panelAllowsPreview, Action<bool> previewState) =>
        diagnostics = new Diagnostics.EncounterCaptureDiagnostics(this, RefreshContext, () => pipeline.Failure,
            panelAllowsPreview, previewState, log);
    internal void DrawDiagnosticStatus() => diagnostics?.DrawStatus();
#endif

    public EncounterObservationContext Context => context;

    internal void Tick()
    {
        if (disposed) return;
        TickCapture();
#if UDS_ENCOUNTER_DIAGNOSTICS
        diagnostics?.Tick();
#endif
    }

    private void TickCapture()
    {
        var contextRefreshed = false;
        try
        {
            RefreshContext();
            contextRefreshed = true;
            if (probes.Count == 0 && pipeline.Failure == null)
            {
                probes.Add(new NativeEncounterCombatObserver(this, log));
                probes.Add(new NativeEncounterLootObserver(this, log));
                probes.Add(new NativeEncounterMapObserver(this, EncounterPaths.MapDirectory, log));
            }
            var startBatch = context.MonotonicSeconds >= nextBatch;
            if (startBatch) nextBatch = context.MonotonicSeconds + 2;
            pipeline.Pump(Publish, start: startBatch);
            context.Active &= pipeline.Failure == null;
            foreach (var probe in probes)
            {
                probe.Tick(context);
                if (context.Active && probe.FailureIssue is { } issue)
                    pipeline.ReportCoverage(context.GenerationId, context.RunId, runSeconds, issue);
            }
        }
        catch (Exception exception)
        {
            // A failed context lookup does not establish which generation owns the
            // current run. ObserveRun records the stop once ownership is known again.
            pipeline.StopCapture(contextRefreshed ? context.GenerationId : "", contextRefreshed ? context.RunId : "", runSeconds, EncounterCaptureIssue.NativeCaptureFailed,
                exception.GetType().Name + ": " + exception.Message);
            context.Active = false;
        }
        if (pipeline.Failure != null && !captureFailureReported)
        {
            captureFailureReported = true;
            log("Encounter capture unavailable: " + pipeline.Failure);
        }
    }

    private void RefreshContext()
    {
        var run = runProvider();
        var profile = profileProvider();
        var generation = profile?.CurrentGenerationId ?? string.Empty;
        var runId = run?.CurrentRunId ?? string.Empty;
        var next = new EncounterObservationContext
        {
            GenerationId = generation, RunId = runId,
            MapId = run?.CurrentMapId ?? string.Empty,
            SegmentId = run?.CurrentSegmentId ?? string.Empty,
            Active = runId.Length != 0 && pipeline.Failure == null && profile?.HasPendingProfileTransition != true,
            Paused = GameManager.Paused,
            Loading = SceneLoader.IsSceneLoading || (NativeLevelAvailability.MayExist && LevelManager.LevelInitializing)
                || (MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading),
            MonotonicSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency
        };
        var changed = context.RunId != next.RunId || context.GenerationId != next.GenerationId
            || context.MapId != next.MapId || context.SegmentId != next.SegmentId
            || context.Paused != next.Paused || context.Loading != next.Loading;
        if (context.RunId.Length != 0 && (context.RunId != next.RunId || context.GenerationId != next.GenerationId))
            pipeline.Record(context.GenerationId, context.RunId, context.MapId, context.SegmentId, runSeconds, "session-end", new { });
        context = next;
        runSeconds = run?.CurrentRunElapsedSeconds ?? 0;
        if (profile?.HasPendingProfileTransition != true) pipeline.ObserveRun(generation, runId, runSeconds);
        if (actorRun != runId || actorGeneration != generation)
        {
            actorRun = runId; actorGeneration = generation;
            actors = new ConditionalWeakTable<UnityEngine.Object, ActorIdentity>();
            // Sequence IDs are unique throughout the recording even across runs.
        }
        if (changed) Record("context", new { next.Active, next.Paused, next.Loading });
    }

    public void Record(string eventKind, object payload)
    {
        var current = context;
        runSeconds = Math.Max(runSeconds, runProvider()?.CurrentRunElapsedSeconds ?? 0);
        var issue = eventKind switch
        {
            "combat_coverage" => EncounterCaptureIssue.CombatIncomplete,
            "loot.coverage" or "loot.split-coverage" => EncounterCaptureIssue.LootIncomplete,
            "map-probe-error" or "map-coverage" => EncounterCaptureIssue.MapIncomplete,
            _ => (EncounterCaptureIssue?)null
        };
        if (current.Active && issue.HasValue) pipeline.ReportCoverage(current.GenerationId, current.RunId, runSeconds, issue.Value);
        if (current.Active)
            pipeline.Record(current.GenerationId, current.RunId, current.MapId, current.SegmentId,
                runSeconds, eventKind, payload);
#if UDS_ENCOUNTER_DIAGNOSTICS
        diagnostics?.Record(eventKind, payload);
#endif
    }

    private bool Publish(string generation, UltimateDuckovStatistics.Core.Encounters.EncounterRecord record) =>
        profileProvider()?.HandleEncounter(generation, record) == true;

    internal bool Flush()
    {
        if (profileProvider()?.HasPendingProfileTransition == true && context.RunId.Length != 0)
        {
            pipeline.Record(context.GenerationId, context.RunId, context.MapId, context.SegmentId, runSeconds, "session-end", new { });
            context.Active = false; context.RunId = string.Empty;
        }
        return pipeline.Pump(Publish, flush: true);
    }

    public int ActorId(UnityEngine.Object actor)
    {
        if (ReferenceEquals(actor, null)) return 0;
        return actors.GetValue(actor, _ => new ActorIdentity(++nextActor)).Id;
    }

    public void Dispose()
    {
        if (disposed) return;
        if (context.RunId.Length != 0) pipeline.Record(context.GenerationId, context.RunId, context.MapId, context.SegmentId, runSeconds, "session-end", new { });
        Flush();
        context.Active = false;
        foreach (var probe in probes)
        { try { probe.Dispose(); } catch (Exception exception) { log(exception.Message); } }
        probes.Clear();
        disposed = true;
#if UDS_ENCOUNTER_DIAGNOSTICS
        diagnostics?.Dispose(); diagnostics = null;
#endif
    }
}
