using UnityEngine;
using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Encounters;

// Detached observation contract shared by native encounter capture and its reducer.
internal sealed class EncounterObservationContext
{
    public string GenerationId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string SegmentId { get; set; } = string.Empty;
    public bool Active { get; set; }
    public bool Paused { get; set; }
    public bool Loading { get; set; }
    public double MonotonicSeconds { get; set; }
}

internal interface IEncounterObservationSink
{
    EncounterObservationContext Context { get; }
    // Values queued here must contain detached managed data only, never Unity objects.
    void Record(string eventKind, object payload);
    // Shared numeric identity survives native subscene deactivation, not run/session changes.
    int ActorId(UnityEngine.Object actor);
}

internal interface IEncounterObserver : IDisposable
{
    string Status { get; }
    EncounterCaptureIssue? FailureIssue { get; }
    void Tick(EncounterObservationContext context);
}
