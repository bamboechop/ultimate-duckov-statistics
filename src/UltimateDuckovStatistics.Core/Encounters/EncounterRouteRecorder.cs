using System.Globalization;

namespace UltimateDuckovStatistics.Core.Encounters;

/// <summary>One bounded mutable tail. Accepted chunks never need to be rewritten.</summary>
public sealed class EncounterRouteRecorder
{
    private readonly string runId;
    private readonly string visitId;
    private readonly Action<EncounterRecord> publish;
    private readonly List<EncounterRoutePoint> points = new();
    private int index;
    private double lastSeconds = -1;
    private bool dirty;

    public EncounterRouteRecorder(string runId, string visitId, Action<EncounterRecord> publish, int firstChunkIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(visitId) || firstChunkIndex < 0)
            throw new ArgumentException("A route needs its run, visit and nonnegative chunk index.");
        this.runId = runId; this.visitId = visitId; index = firstChunkIndex;
        this.publish = publish ?? throw new ArgumentNullException(nameof(publish));
    }

    public void Append(double seconds, EncounterPosition position, RouteConnection connection)
    {
        if (position == null) throw new ArgumentNullException(nameof(position));
        var point = new EncounterRoutePoint
        {
            Seconds = seconds, Connection = connection,
            Position = new EncounterPosition { X = position.X, Y = position.Y, Z = position.Z, MapId = position.MapId }
        };
        // Validate before publishing or changing the previous tail.
        EncounterRecordValidation.Validate(Create(new List<EncounterRoutePoint> { point }, false));
        if (seconds < lastSeconds) throw new ArgumentException("Route time cannot move backwards.", nameof(seconds));
        if (points.Count == EncounterRouteChunk.MaximumPoints) Flush(seal: true);
        points.Add(point); dirty = true; lastSeconds = seconds;
    }

    public void Flush(bool seal = false)
    {
        if (points.Count == 0 || (!dirty && !seal)) return;
        if (seal && index == int.MaxValue) throw new InvalidOperationException("Route chunk index exhausted.");
        publish(Create(points.Select(point => new EncounterRoutePoint
        {
            Seconds = point.Seconds, Connection = point.Connection,
            Position = new EncounterPosition { X = point.Position.X, Y = point.Position.Y, Z = point.Position.Z, MapId = point.Position.MapId }
        }).ToList(), seal));
        // A failed publication retains this exact tail for a later retry.
        dirty = false;
        if (seal) { points.Clear(); index++; }
    }

    private EncounterRecord Create(List<EncounterRoutePoint> samples, bool seal) => new()
    {
        RunId = runId, VisitId = visitId, Id = visitId + "/" + index.ToString(CultureInfo.InvariantCulture), Kind = EncounterRecordKind.Route,
        Route = new EncounterRouteChunk { Index = index, Sealed = seal, Points = samples }
    };
}
