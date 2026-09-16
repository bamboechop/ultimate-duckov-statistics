using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Encounters;

internal sealed class StoredEncounterMap
{
    internal static string FormatEventTime(double seconds) => TimeSpan.FromSeconds(seconds).ToString(
        seconds >= 3600 ? @"h\:mm\:ss\.fff" : @"mm\:ss\.fff", System.Globalization.CultureInfo.InvariantCulture);

    internal string MapId = "";
    internal string VisitId = "";
    internal int MapVisitNumber, MapVisitCount;
    internal EncounterMapCalibration? Calibration;
    internal string ArtworkKey = "";
    internal EncounterRecord[] Events = Array.Empty<EncounterRecord>();
    internal EncounterRecord[] Records = Array.Empty<EncounterRecord>();
    internal EncounterRouteStroke[] Strokes = Array.Empty<EncounterRouteStroke>();
    internal EncounterRevealTimeline Timeline = new(Array.Empty<EncounterRevealSpan>());
    internal bool RouteLimited;

    internal static StoredEncounterMap[] Build(EncounterRecord[] records)
    {
        var visits = records.Where(record => record.Visit != null).OrderBy(record => record.Visit!.StartedSeconds)
            .ThenBy(record => record.Visit!.Ordinal).ThenBy(record => record.Id, StringComparer.Ordinal).ToArray();
        var totals = visits.GroupBy(record => record.Visit!.MapId).ToDictionary(group => group.Key, group => group.Count());
        var counts = new Dictionary<string, int>();
        return visits.Select(visit =>
        {
            var mapId = visit.Visit!.MapId;
            counts.TryGetValue(mapId, out var number);
            var map = BuildMap(mapId, new[] { visit }, records);
            map.VisitId = visit.Id; map.MapVisitNumber = counts[mapId] = number + 1; map.MapVisitCount = totals[mapId];
            return map;
        }).ToArray();
    }

    private static StoredEncounterMap BuildMap(string mapId, EncounterRecord[] visits, EncounterRecord[] records)
    {
        var ids = new HashSet<string>(visits.Select(visit => visit.Id));
        var events = records.Where(record => record.Encounter?.Outcome != null && ids.Contains(record.Encounter.OutcomeVisitId ?? record.VisitId))
            .OrderBy(record => record.Encounter!.EndedSeconds).ToArray();
        var actors = new HashSet<string>(events.Select(record => record.Encounter!.ActorId));
        var relatedIds = new HashSet<string>(records.Where(record => record.Encounter != null && actors.Contains(record.Encounter.ActorId)).Select(record => record.Id));
        var relevant = records.Where(record => ids.Contains(record.VisitId) || relatedIds.Contains(record.Id)
            || record.EncounterId != null && relatedIds.Contains(record.EncounterId)).ToArray();
        var result = new StoredEncounterMap { MapId = mapId, Records = relevant, Events = events };
        var latest = visits.LastOrDefault(record => record.Visit!.Calibration?.ArtworkKey != null)?.Visit!.Calibration;
        if (latest != null)
        {
            result.ArtworkKey = latest.ArtworkKey!;
            result.Calibration = new EncounterMapCalibration { MapId = mapId, Available = true,
                CenterX = latest.CenterX, CenterZ = latest.CenterZ, WorldSize = latest.Size,
                OffsetX = latest.OffsetX, OffsetY = latest.OffsetZ, Combined = latest.Combined,
                CombinedCenterX = latest.CombinedCenterX, CombinedCenterY = latest.CombinedCenterZ,
                CombinedSize = latest.CombinedSize, Hidden = latest.Hidden, NoSignal = latest.NoSignal };
        }
        if (result.Calibration == null) return result;
        // Simplification applies only to this detached display model. Persisted float samples remain exact.
        var chains = new List<List<EncounterRoutePoint>>();
        foreach (var visit in visits)
        {
            var points = relevant.Where(record => record.VisitId == visit.Id && record.Route != null)
                .OrderBy(record => record.Route!.Index).SelectMany(record => record.Route!.Points);
            List<EncounterRoutePoint>? chain = null;
            foreach (var point in points)
            {
                if (chain == null || point.Connection is RouteConnection.Start or RouteConnection.Gap)
                { chain = new List<EncounterRoutePoint>(); chains.Add(chain); }
                if (point.Connection == RouteConnection.Teleport && chain.Count > 0)
                {
                    var from = chain[chain.Count - 1];
                    chains.Add(new List<EncounterRoutePoint> { from, point });
                    chain = new List<EncounterRoutePoint> { point }; chains.Add(chain);
                }
                else chain.Add(point);
            }
        }
        List<List<EncounterRoutePoint>> simplified = chains;
        var tolerance = .35;
        do
        {
            simplified = chains.Select(chain => Simplify(chain, tolerance)).ToList();
            tolerance *= 2;
        } while (simplified.Sum(chain => Math.Max(0, chain.Count - 1)) > EncounterRouteRaster.MaximumEdges && tolerance < 100);
        var strokes = new List<EncounterRouteStroke>(); var spans = new List<EncounterRevealSpan>();
        foreach (var chain in simplified.OrderBy(chain => chain.Count == 0 ? 0 : chain[0].Seconds))
        for (var i = 1; i < chain.Count; i++)
        {
            if (strokes.Count >= EncounterRouteRaster.MaximumEdges) { result.RouteLimited = true; break; }
            var from = chain[i - 1]; var to = chain[i];
            if (!EncounterMapGeometry.TryProject(result.Calibration, from.Position.X, from.Position.Z, out var a)
                || !EncounterMapGeometry.TryProject(result.Calibration, to.Position.X, to.Position.Z, out var b)) continue;
            var teleport = to.Connection == RouteConnection.Teleport;
            strokes.Add(new EncounterRouteStroke(a, b, teleport));
            var dx = (double)to.Position.X - from.Position.X; var dz = (double)to.Position.Z - from.Position.Z;
            spans.Add(new EncounterRevealSpan(from.Seconds, to.Seconds, Math.Sqrt(dx * dx + dz * dz), teleport));
        }
        result.Strokes = strokes.ToArray(); result.Timeline = new EncounterRevealTimeline(spans);
        return result;
    }

    private static List<EncounterRoutePoint> Simplify(List<EncounterRoutePoint> points, double tolerance)
    {
        if (points.Count <= 2) return points;
        var keep = new bool[points.Count]; keep[0] = keep[keep.Length - 1] = true;
        var ranges = new Stack<(int A, int B)>(); ranges.Push((0, points.Count - 1));
        while (ranges.Count > 0)
        {
            var range = ranges.Pop(); var a = points[range.A].Position; var b = points[range.B].Position;
            var dx = (double)b.X - a.X; var dz = (double)b.Z - a.Z; var squared = dx * dx + dz * dz;
            var maximum = tolerance * tolerance; var selected = -1;
            for (var i = range.A + 1; i < range.B; i++)
            {
                var p = points[i].Position;
                var t = squared == 0 ? 0 : Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Z - a.Z) * dz) / squared));
                var x = p.X - (a.X + t * dx); var z = p.Z - (a.Z + t * dz); var distance = x * x + z * z;
                if (distance > maximum) { maximum = distance; selected = i; }
            }
            if (selected < 0) continue;
            keep[selected] = true; ranges.Push((range.A, selected)); ranges.Push((selected, range.B));
        }
        return points.Where((_, i) => keep[i]).ToList();
    }
}
