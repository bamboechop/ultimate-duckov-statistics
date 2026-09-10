using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Core.Persistence;

public sealed partial class ProfileRepository
{
    private string baseCaptureGeneration = string.Empty;
    private string baseCaptureId = string.Empty;
    private double baseCapturedMetersApplied;

    public bool RecordBaseMovementDeferred(BaseMovementUpdate update)
    {
        if (update == null) throw new ArgumentNullException(nameof(update));
        var profile = Current;
        if (update.GenerationId != profile.GenerationId || string.IsNullOrWhiteSpace(update.CaptureId))
            throw new ArgumentException("Base movement belongs to another generation or an unknown capture.", nameof(update));
        BaseMovementStatistics.Validate(new BaseMovementStatistics
        {
            RecordedMeters = update.CapturedMeters,
            CollectionStartedUtc = update.CollectionStartedUtc
        });
        var sameCapture = baseCaptureGeneration == update.GenerationId && baseCaptureId == update.CaptureId;
        if (sameCapture && update.CapturedMeters < baseCapturedMetersApplied)
            throw new ArgumentException("Base movement capture moved backwards.", nameof(update));
        var delta = update.CapturedMeters - (sameCapture ? baseCapturedMetersApplied : 0);
        var previous = profile.Statistics.BaseMovement;
        var changed = previous == null || delta > 0 || (update.HasKnownGaps && !previous.HasKnownGaps);
        if (changed)
        {
            var next = new BaseMovementStatistics
            {
                RecordedMeters = RouteStatisticsReducer.SaturatingAdd(previous?.RecordedMeters ?? 0, delta),
                CollectionStartedUtc = previous?.CollectionStartedUtc ?? update.CollectionStartedUtc,
                HasKnownGaps = previous?.HasKnownGaps == true || update.HasKnownGaps
            };
            var updatedUtc = EnsureUtc(utcNow());
            profile.Statistics.BaseMovement = next;
            profile.Revision++;
            profile.UpdatedUtc = updatedUtc;
        }
        baseCaptureGeneration = update.GenerationId;
        baseCaptureId = update.CaptureId;
        baseCapturedMetersApplied = update.CapturedMeters;
        return changed;
    }
}
