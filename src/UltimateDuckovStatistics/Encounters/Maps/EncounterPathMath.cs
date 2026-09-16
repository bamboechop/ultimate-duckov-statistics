namespace UltimateDuckovStatistics.Encounters;

// Pure sampled-route semantics. These do not mutate or replace the aggregate distance tracker.
internal static class EncounterPathMath
{
    // The overview is an XZ map. Preserve vertical placement evidence, but do not
    // draw a dotted connection for an adjustment between identical map points.
    internal static bool HasMapDisplacement(double fromX, double fromZ, double toX, double toZ)
        => Finite(fromX) && Finite(fromZ) && Finite(toX) && Finite(toZ)
            && (fromX != toX || fromZ != toZ);

    internal static string NativeSceneId(string mapId)
    {
        const string prefix = "duckov:map:";
        return mapId.StartsWith(prefix, StringComparison.Ordinal) ? mapId.Substring(prefix.Length) : mapId;
    }

    internal static string? GapReason(double elapsed, double distance, double maximumSpeed)
    {
        if (!Finite(elapsed) || !Finite(distance) || !Finite(maximumSpeed) || distance < 0 || maximumSpeed <= 0) return "invalid-observation";
        if (elapsed <= 0) return "nonmonotonic-sample";
        if (elapsed > 2) return "sample-gap";
        return distance > maximumSpeed * elapsed * 1.75 + 0.35 ? "unclassified-speed-discontinuity" : null;
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
