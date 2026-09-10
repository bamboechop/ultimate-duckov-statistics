using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class BaseMovementUpdate
{
    public string GenerationId { get; set; } = string.Empty;
    public string CaptureId { get; set; } = string.Empty;
    public double CapturedMeters { get; set; }
    public DateTime CollectionStartedUtc { get; set; }
    public bool HasKnownGaps { get; set; }
}

// Session-local cumulative publication makes retries idempotent. Coordinates never enter persistence.
public sealed class BaseMovementCapture
{
    private readonly Func<BaseMovementUpdate, bool> publish;
    private readonly MovementAccumulator movement = new();
    private readonly MonotonicCadenceGate publicationCadence = new(1);
    private string captureId = string.Empty;
    private DateTime? startedUtc;
    private double capturedMeters;
    private bool hasKnownGaps;
    private bool dirty;

    public BaseMovementCapture(Func<BaseMovementUpdate, bool> publish) =>
        this.publish = publish ?? throw new ArgumentNullException(nameof(publish));

    public string GenerationId { get; private set; } = string.Empty;
    public bool HasBaseline => movement.HasBaseline;

    public void Bind(string generationId)
    {
        if (GenerationId == generationId) return;
        if (string.IsNullOrWhiteSpace(generationId)) throw new ArgumentException("A generation is required.", nameof(generationId));
        if (dirty) throw new InvalidOperationException("Base movement must be published before changing generation.");
        GenerationId = generationId;
        captureId = Guid.NewGuid().ToString("N");
        movement.Reset();
        publicationCadence.Reset();
        capturedMeters = 0;
        startedUtc = null;
        hasKnownGaps = false;
    }

    public void Observe(Position3D position, double monotonicSeconds, double speed, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(GenerationId)) throw new InvalidOperationException("Base movement is not bound.");
        if (utcNow.Kind != DateTimeKind.Utc || utcNow == DateTime.MinValue) throw new ArgumentException("UTC observation required.", nameof(utcNow));
        if (!double.IsFinite(speed) || speed <= 0)
        {
            ResetBaseline(knownGap: true);
            return;
        }
        var result = movement.Observe(position, monotonicSeconds, speed);
        if (result.Disposition == MovementDisposition.InvalidIgnored)
        {
            MarkGap();
            return;
        }
        if (!startedUtc.HasValue)
        {
            startedUtc = utcNow;
            dirty = true;
        }
        if (result.Disposition == MovementDisposition.Physical)
        {
            capturedMeters = RouteStatisticsReducer.SaturatingAdd(capturedMeters, result.Distance);
            dirty = true;
        }
        else if (result.Disposition == MovementDisposition.Teleport) MarkGap();
    }

    public void ResetBaseline(bool knownGap = false)
    {
        movement.Reset();
        if (knownGap) MarkGap();
    }

    public bool Publish(double monotonicSeconds, bool force = false)
    {
        if (!dirty || !startedUtc.HasValue) return true;
        if (!force && !publicationCadence.IsDue(monotonicSeconds)) return true;
        publicationCadence.MarkCompleted(monotonicSeconds);
        if (!publish(new BaseMovementUpdate
        {
            GenerationId = GenerationId,
            CaptureId = captureId,
            CapturedMeters = capturedMeters,
            CollectionStartedUtc = startedUtc.Value,
            HasKnownGaps = hasKnownGaps
        })) return false;
        dirty = false;
        return true;
    }

    private void MarkGap()
    {
        if (!startedUtc.HasValue || hasKnownGaps) return;
        hasKnownGaps = true;
        dirty = true;
    }
}
