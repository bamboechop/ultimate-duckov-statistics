#if UDS_ENCOUNTER_DIAGNOSTICS
using System.Diagnostics;
using Newtonsoft.Json;
using UnityEngine;

namespace UltimateDuckovStatistics.Encounters.Diagnostics;

// Optional raw capture and preview only. The normal mod owns capture independently.
internal sealed class EncounterCaptureDiagnostics : IDisposable
{
    private readonly IEncounterObservationSink host;
    private readonly Action refreshContext;
    private readonly Func<string?> captureFailure;
    private readonly Func<bool> panelAllowsPreview;
    private readonly Action<string> log;
    private readonly string root;
    private readonly EncounterDiagnosticViewer viewer;
    private readonly List<EncounterDiagnosticWriter> retiring = new();
    private EncounterDiagnosticWriter? writer;
    private long sequence;
    private double startedAt;
    private bool disposed;
    private string status = "F6 starts an encounter diagnostic recording";

    internal EncounterCaptureDiagnostics(IEncounterObservationSink host, Action refreshContext,
        Func<string?> captureFailure, Func<bool> panelAllowsPreview, Action<bool> previewState, Action<string> log)
    {
        this.host = host; this.refreshContext = refreshContext; this.captureFailure = captureFailure;
        this.panelAllowsPreview = panelAllowsPreview; this.log = log;
        root = Path.GetDirectoryName(EncounterPaths.MapDirectory)!;
        viewer = new EncounterDiagnosticViewer(root, previewState, log);
    }

    internal void Tick()
    {
        if (disposed) return;
        var context = host.Context;
        try
        {
            viewer.Tick(panelAllowsPreview() && writer == null && retiring.Count == 0);
            if (Input.GetKeyDown(KeyCode.F6))
            {
                if (writer == null) Start(); else Stop();
            }
            if (writer != null)
            {
                if (context.MonotonicSeconds - startedAt >= 3600 || writer.Accepted >= 100000 || writer.LimitReached)
                {
                    Record("capture-limit", new { Reason = "diagnostic-time-row-or-byte-budget", Partial = true });
                    Stop();
                }
            }
            if (writer != null)
            {
                status = "RECORDING — F6 stops | " + writer.Accepted + " records, " + writer.Dropped + " dropped";
                if (writer.Failure != null)
                {
                    log("Encounter diagnostic capture failed: " + writer.Failure);
                    Stop();
                    status = "Capture failed; see Player.log";
                }
            }
            for (var index = retiring.Count - 1; index >= 0; index--)
            {
                var previous = retiring[index];
                if (!previous.Completion.IsCompleted) continue;
                if (previous.Failure != null) log("Encounter diagnostic writer failed: " + previous.Failure);
                else log("Encounter diagnostic recording flushed: " + previous.Written + " records; dropped=" + previous.Dropped);
                retiring.RemoveAt(index);
                if (writer == null && retiring.Count == 0)
                    status = previous.Failure != null ? "Capture failed; see Player.log"
                        : previous.LimitReached || previous.Dropped != 0 ? "PARTIAL capture saved; see Player.log. F6 starts again"
                        : "Capture saved. F6 starts a new recording";
            }
        }
        catch (Exception exception)
        {
            log("Encounter diagnostic stopped safely: " + exception.GetType().Name + ": " + exception.Message);
            Record("capture-failed", new { Error = exception.GetType().Name, Partial = true });
            Stop();
            status = "Diagnostic capture unavailable; see Player.log";
        }
    }

    private void Start()
    {
        viewer.Close();
        var directory = Path.Combine(root, "captures", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        writer = new EncounterDiagnosticWriter(Path.Combine(directory, "observations.jsonl"), value => JsonConvert.SerializeObject(value));
        sequence = 0;
        refreshContext();
        startedAt = host.Context.MonotonicSeconds;
        host.Record("capture-start", new { Prototype = true, Version = 1, Utc = DateTime.UtcNow, AutomaticProfileImport = false });
        log("Encounter diagnostic recording started: " + directory);
    }

    private void Stop()
    {
        if (writer != null)
        {
            host.Record("capture-stop", new { Utc = DateTime.UtcNow });
            writer.Stop(); retiring.Add(writer); writer = null;
        }
        status = "Recording stopped; F6 starts a new capture";
    }

    internal void Record(string eventKind, object payload)
    {
        var current = host.Context;
        var output = writer;
        if (output == null) return;
        output.TryRecord(new
        {
            Sequence = ++sequence, Kind = eventKind, Frame = Time.frameCount,
            Time = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency,
            current.GenerationId, current.RunId, current.MapId, current.SegmentId,
            Data = payload
        });
    }

    internal void DrawStatus()
    {
        if (disposed) return;
        if (!panelAllowsPreview()) viewer.Close();
        if (viewer.IsOpen) { viewer.Draw(); return; }
        if (writer == null && captureFailure() == null) return;
        var rect = new Rect(Math.Max(5, Screen.width - 520), 40, 510, 80);
        GUI.Box(rect, "UDS ENCOUNTER DIAGNOSTICS\n" + status + "\nOpen UDS, then F5 to inspect saved captures");
    }

    public void Dispose()
    {
        if (disposed) return;
        Stop(); disposed = true;
        viewer.Dispose();
        // Detached writers close their own files without blocking game shutdown.
        foreach (var previous in retiring) previous.Stop();
    }
}
#endif
