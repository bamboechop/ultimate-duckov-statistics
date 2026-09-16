namespace UltimateDuckovStatistics.Core.Encounters;

public sealed class KillDistanceRecord(string runId, string encounterId, double meters)
{
    public string RunId { get; } = runId;
    public string EncounterId { get; } = encounterId;
    public double Meters { get; } = meters;
}

public sealed class KillDistanceHighlights
{
    public KillDistanceRecord? Longest { get; private set; }
    public KillDistanceRecord? Shortest { get; private set; }

    internal static KillDistanceHighlights Read(IEnumerable<EncounterRecord> visits,
        IEnumerable<EncounterRecord> encounters, HashSet<string> eligibleRuns, CancellationToken cancellation)
    {
        var maps = new Dictionary<(string Run, string Visit), string>();
        foreach (var record in visits)
        {
            cancellation.ThrowIfCancellationRequested();
            if (eligibleRuns.Contains(record.RunId) && record.Visit != null)
                maps[(record.RunId, record.Id)] = record.Visit.MapId;
        }
        var result = new KillDistanceHighlights();
        foreach (var record in encounters)
        {
            cancellation.ThrowIfCancellationRequested();
            var detail = record.Encounter;
            if (!eligibleRuns.Contains(record.RunId) || detail?.Outcome != EncounterOutcome.PlayerKill
                || detail.FinalSource?.Credit != EncounterCredit.Player || !detail.EndedSeconds.HasValue
                || !maps.TryGetValue((record.RunId, detail.OutcomeVisitId ?? record.VisitId), out var map)
                || !TryDistance(detail.PlayerPosition, detail.EnemyPosition, map, out var meters)) continue;
            var candidate = new KillDistanceRecord(record.RunId, record.Id, meters);
            if (result.Longest == null || meters > result.Longest.Meters
                || (meters == result.Longest.Meters && Earlier(candidate, result.Longest))) result.Longest = candidate;
            if (result.Shortest == null || meters < result.Shortest.Meters
                || (meters == result.Shortest.Meters && Earlier(candidate, result.Shortest))) result.Shortest = candidate;
        }
        return result;
    }

    // Match Map & Kills: horizontal player-to-victim separation at the fatal boundary,
    // not projectile travel, the attack origin, or vertical separation. Artwork is irrelevant.
    internal static bool TryDistance(EncounterPosition? player, EncounterPosition? enemy, string map, out double meters)
    {
        meters = 0;
        if (string.IsNullOrWhiteSpace(map) || !Valid(player, map) || !Valid(enemy, map)) return false;
        var dx = (double)enemy!.X - player!.X;
        var dz = (double)enemy.Z - player.Z;
        meters = Math.Sqrt(dx * dx + dz * dz);
        return true;
    }

    private static bool Valid(EncounterPosition? position, string map) => position != null
        && Finite(position.X) && Finite(position.Y) && Finite(position.Z)
        && (string.IsNullOrEmpty(position.MapId) || Scene(position.MapId!) == Scene(map));
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static string Scene(string id) => id.StartsWith("duckov:map:", StringComparison.Ordinal) ? id.Substring(11) : id;
    private static bool Earlier(KillDistanceRecord a, KillDistanceRecord b)
    {
        var runOrder = StringComparer.Ordinal.Compare(a.RunId, b.RunId);
        return runOrder < 0 || (runOrder == 0 && StringComparer.Ordinal.Compare(a.EncounterId, b.EncounterId) < 0);
    }
}
