#if UDS_ENCOUNTER_DIAGNOSTICS
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace UltimateDuckovStatistics.Encounters.Diagnostics;

internal sealed class EncounterReplayLimits
{
    public long MaximumBytes { get; set; } = 128L * 1024 * 1024;
    public int MaximumRecords { get; set; } = 100000;
    public int MaximumLineCharacters { get; set; } = 1024 * 1024;
    public int MaximumDepth { get; set; } = 32;
}

internal enum EncounterReplayEdgeStyle { Walk, Teleport }
internal enum EncounterReplayFatalKind { PlayerKill, PlayerDeath, OtherPlayerRelatedDeath }

internal readonly struct EncounterReplayVector
{
    public EncounterReplayVector(double x, double y, double z) { X = x; Y = y; Z = z; }
    public double X { get; }
    public double Y { get; }
    public double Z { get; }
}

internal sealed class EncounterReplayPoint
{
    public long Sequence { get; set; }
    public long? ProbeSequence { get; set; }
    public double Time { get; set; }
    public int Visit { get; set; }
    public int? ActorId { get; set; }
    public string SegmentId { get; set; } = string.Empty;
    public EncounterReplayVector Position { get; set; }
}

internal sealed class EncounterReplayEdge
{
    public EncounterReplayPoint From { get; set; } = null!;
    public EncounterReplayPoint To { get; set; } = null!;
    public EncounterReplayEdgeStyle Style { get; set; }
    public int Visit => From.Visit;
    public long Sequence => To.Sequence;
    public double FromTime => From.Time;
    public double ToTime => To.Time;
    public string TimeEvidence { get; set; } = string.Empty;
}

internal sealed class EncounterReplayGap
{
    public long? Sequence { get; set; }
    public double? Time { get; set; }
    public int? Visit { get; set; }
    public string Reason { get; set; } = string.Empty;
}

internal sealed class EncounterReplayActor
{
    public int? Id { get; set; }
    public string PresetKey { get; set; } = string.Empty;
    public string Kind { get; set; } = "unavailable";
    public bool? IsMain { get; set; }
}

internal sealed class EncounterReplaySource
{
    public string Kind { get; set; } = "unknown";
    public string Provenance { get; set; } = string.Empty;
    public EncounterReplayActor Physical { get; set; } = new();
    public EncounterReplayActor Credited { get; set; } = new();
    public EncounterReplayActor NativeDamageActor { get; set; } = new();
    public EncounterReplayActor OriginalPhysical { get; set; } = new();
    public EncounterReplayActor OriginalCredited { get; set; } = new();
    public bool? OriginallyPlayer { get; set; }
    public bool? Delayed { get; set; }
    public int? WeaponId { get; set; }
    public int? LoadedAmmoId { get; set; }
    public int? TargetAmmoId { get; set; }
    public bool? AmmoAgreement { get; set; }
    public int? BuffId { get; set; }
    public string AttributionEvidence { get; set; } = string.Empty;
}

internal sealed class EncounterReplayEndpoint
{
    public EncounterReplayVector Position { get; set; }
    public string LogicalScene { get; set; } = string.Empty;
    public string SceneEvidence { get; set; } = string.Empty;
    public int? PhysicalSceneBuild { get; set; }
    public int? RelatedSceneBuild { get; set; }
    public string Timing { get; set; } = string.Empty;
}

internal sealed class EncounterReplayDamage
{
    public long Sequence { get; set; }
    public double Time { get; set; }
    public long? Transaction { get; set; }
    public EncounterReplayActor Target { get; set; } = new();
    public EncounterReplaySource Source { get; set; } = new();
    public double? NetHpLossAcrossCall { get; set; }
    public double? ProposedHpLossOwnedAssignments { get; set; }
    public bool Nested { get; set; }
    public string Interpretation { get; set; } = "Diagnostic observations; net loss is not proven isolated damage.";
}

internal sealed class EncounterReplayFatal
{
    public long Sequence { get; set; }
    public long FatalSequence { get; set; }
    public long? Transaction { get; set; }
    // Envelope completion time, NOT a conversion of the candidate Stopwatch ticks.
    public double Time { get; set; }
    public long? CandidateStopwatchTicks { get; set; }
    public EncounterReplayFatalKind Kind { get; set; }
    public EncounterReplayActor Target { get; set; } = new();
    public EncounterReplaySource Source { get; set; } = new();
    public EncounterReplayEndpoint? TargetPosition { get; set; }
    public EncounterReplayEndpoint? PlayerPosition { get; set; }
    public EncounterReplayEndpoint? EnemyPosition { get; set; }
    public EncounterReplayEndpoint? SourcePosition { get; set; }
    public double? DiagnosticHpLoss { get; set; }
    public string Timing { get; set; } = "Envelope time is recorded after completion; exact candidate time in seconds unavailable.";
    public string Attribution { get; set; } = string.Empty;
    public List<EncounterReplayDamage> DamageDiagnostics { get; } = new();
}

internal sealed class EncounterReplayCalibration
{
    public string CalibrationId { get; set; } = string.Empty;
    public long Sequence { get; set; }
    public double Time { get; set; }
    public string MetadataJson { get; set; } = string.Empty;
    public string NativeSceneId { get; set; } = string.Empty;
    public string MapGroup { get; set; } = string.Empty;
    public bool Available { get; set; }
    public bool? Combined { get; set; }
    public EncounterReplayVector? WorldCenter { get; set; }
    public EncounterReplayVector? CombinedCenter { get; set; }
    public double? ImageWorldSize { get; set; }
    public double? CombinedSize { get; set; }
    public double[]? Offset { get; set; }
    public double[]? SpriteRect { get; set; }
    public double[]? SpritePivot { get; set; }
    public double? PixelsPerUnit { get; set; }
}

internal sealed class EncounterReplayGroup
{
    public string GenerationId { get; set; } = string.Empty;
    public string RunId { get; set; } = string.Empty;
    public string MapId { get; set; } = string.Empty;
    public string NativeMapId => MapId.StartsWith("duckov:map:", StringComparison.Ordinal) ? MapId.Substring(11) : MapId;
    public double? StartTime { get; set; }
    public double? EndTime { get; set; }
    public List<EncounterReplayPoint> PathPoints { get; } = new();
    public List<EncounterReplayEdge> PathEdges { get; } = new();
    public List<EncounterReplayGap> Gaps { get; } = new();
    public List<EncounterReplayFatal> Fatals { get; } = new();
    public List<EncounterReplayDamage> DamageDiagnostics { get; } = new();
    public List<EncounterReplayCalibration> Calibrations { get; } = new();
}

internal sealed class EncounterReplayCapture
{
    public string SourcePath { get; set; } = string.Empty;
    public List<EncounterReplayGroup> Groups { get; } = new();
    public List<string> Issues { get; } = new();
    public bool IsPartial { get; set; }
    public bool FooterPresent { get; set; }
    public long? DroppedRecords { get; set; }
    public bool LimitReached { get; set; }
    public int RecordsRead { get; set; }
}

// Only JObject/JToken are read. Never activates CLR types or calls native code.
internal static class EncounterCaptureReplay
{
    public static EncounterReplayCapture Load(string path, EncounterReplayLimits? limits = null)
    {
        var chosen = ValidateLimits(limits);
        var result = new EncounterReplayCapture { SourcePath = path ?? string.Empty };
        try
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > chosen.MaximumBytes)
            {
                result.IsPartial = result.LimitReached = true;
                result.Issues.Add("Capture exceeds byte budget; no records loaded.");
                return result;
            }
            using var reader = new StreamReader(stream, new UTF8Encoding(false, true), true);
            result = Parse(reader, chosen);
            result.SourcePath = path;
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException)
        {
            result.IsPartial = true;
            result.Issues.Add("Capture could not be read: " + exception.GetType().Name);
        }
        return result;
    }

    public static EncounterReplayCapture Parse(TextReader reader, EncounterReplayLimits? limits = null)
    {
        if (reader == null) throw new ArgumentNullException(nameof(reader));
        return new Parser(ValidateLimits(limits)).Read(reader);
    }

    private static EncounterReplayLimits ValidateLimits(EncounterReplayLimits? limits)
    {
        var value = limits ?? new EncounterReplayLimits();
        if (value.MaximumBytes < 1 || value.MaximumBytes > 128L * 1024 * 1024 || value.MaximumRecords < 1
            || value.MaximumRecords > 100000 || value.MaximumLineCharacters < 1 || value.MaximumLineCharacters > 1024 * 1024
            || value.MaximumDepth < 1 || value.MaximumDepth > 64) throw new ArgumentOutOfRangeException(nameof(limits));
        return new EncounterReplayLimits { MaximumBytes = value.MaximumBytes, MaximumRecords = value.MaximumRecords,
            MaximumLineCharacters = value.MaximumLineCharacters, MaximumDepth = value.MaximumDepth };
    }

    private sealed class Parser
    {
        private readonly EncounterReplayLimits limits;
        private readonly EncounterReplayCapture result = new();
        private readonly Dictionary<(string Generation, string Run, string Map), GroupState> groups = new();
        private readonly HashSet<(string Generation, string Run, long Transaction)> nested = new();
        private readonly Dictionary<(string Generation, string Run, long Transaction), EncounterReplayDamage> damage = new();
        private long lastSequence;
        private long bytes;
        private int nonFooterRecords;
        private long? footerWritten;
        private bool? footerComplete;
        private int suppressedIssues;
        private (string Generation, string Run, string Map)? previousGroup;

        public Parser(EncounterReplayLimits limits) => this.limits = limits;

        public EncounterReplayCapture Read(TextReader reader)
        {
            try
            {
                while (true)
                {
                    var line = ReadLine(reader);
                    if (line == null) break;
                    bytes += Encoding.UTF8.GetByteCount(line) + 1L;
                    if (bytes > limits.MaximumBytes) { Budget("byte"); break; }
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (result.RecordsRead >= limits.MaximumRecords) { Budget("record"); break; }
                    result.RecordsRead++;
                    if (result.FooterPresent)
                    {
                        Issue("Record appears after capture footer; trailing data was not replayed.");
                        BreakAll("data-after-footer");
                        break;
                    }
                    try
                    {
                        using var json = new JsonTextReader(new StringReader(line))
                        { MaxDepth = limits.MaximumDepth, DateParseHandling = DateParseHandling.None, FloatParseHandling = FloatParseHandling.Double };
                        var row = JObject.Load(json, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                        if (json.Read()) throw new JsonReaderException("Unexpected trailing JSON token.");
                        if (Text(row["Kind"]) == "capture-footer") ReadFooter(row);
                        else { nonFooterRecords++; ReadRecord(row); }
                    }
                    catch (Exception exception) when (exception is JsonException || exception is FormatException || exception is OverflowException)
                    {
                        Issue("Malformed record at line " + result.RecordsRead.ToString(CultureInfo.InvariantCulture) + ": " + exception.GetType().Name);
                        BreakAll("malformed-record");
                    }
                }
            }
            catch (ReplayLineLimitException) { Budget("line-length"); }
            catch (Exception exception) when (exception is IOException || exception is DecoderFallbackException)
            { Issue("Capture read interrupted: " + exception.GetType().Name); BreakAll("read-interrupted"); }
            if (!result.FooterPresent) Issue("Capture footer missing; recording may be incomplete or still active.");
            else if (footerWritten != nonFooterRecords) Issue("Footer record count does not match the observed records.");
            if (footerComplete != true || result.DroppedRecords != 0 || result.LimitReached) result.IsPartial = true;
            foreach (var state in groups.Values)
            {
                var group = state.Group;
                group.Fatals.Sort((left, right) => left.FatalSequence.CompareTo(right.FatalSequence));
                foreach (var diagnostic in group.DamageDiagnostics)
                    if (diagnostic.Transaction is long transaction)
                        diagnostic.Nested = nested.Contains((group.GenerationId, group.RunId, transaction));
                foreach (var fatal in group.Fatals)
                    if (fatal.Transaction is long transaction && damage.TryGetValue((group.GenerationId, group.RunId, transaction), out var diagnostic))
                        fatal.DamageDiagnostics.Add(diagnostic);
            }
            if (suppressedIssues > 0) result.Issues.Add(suppressedIssues.ToString(CultureInfo.InvariantCulture) + " additional issues suppressed.");
            return result;
        }

        private string? ReadLine(TextReader reader)
        {
            var text = new StringBuilder();
            while (true)
            {
                var value = reader.Read();
                if (value < 0) return text.Length == 0 ? null : text.ToString();
                if (value == '\n') return text.ToString();
                if (text.Length >= limits.MaximumLineCharacters) throw new ReplayLineLimitException();
                text.Append((char)value);
            }
        }

        private void ReadFooter(JObject row)
        {
            result.FooterPresent = true;
            footerWritten = Integer(row["Written"]);
            result.DroppedRecords = Integer(row["Dropped"]);
            footerComplete = Boolean(row["Complete"]);
            var limit = Boolean(row["LimitReached"]);
            result.LimitReached |= limit == true;
            if (footerWritten is null or < 0 || result.DroppedRecords is null or < 0 || footerComplete == null)
                Issue("Footer contains missing or invalid completion/count fields.");
            if (result.DroppedRecords > 0) Issue("Capture footer reports lost records; continuity is partial.");
            if (footerComplete == false || limit == true) Issue("Capture footer reports partial or limited output.");
        }

        private void ReadRecord(JObject row)
        {
            var sequence = Integer(row["Sequence"]);
            var time = Number(row["Time"]);
            var kind = Text(row["Kind"]);
            if (sequence is null or <= 0 || time is null or < 0 || kind.Length == 0)
            { Issue("Record envelope has invalid sequence, time or kind."); BreakAll("invalid-envelope"); return; }
            if (sequence <= lastSequence)
            { Issue("Duplicate or reversed envelope sequence; row omitted."); BreakAll("sequence-reversal"); return; }
            if (lastSequence != 0 && sequence != lastSequence + 1)
            { Issue("Missing envelope sequence(s); paths are split at the loss."); BreakAll("missing-sequence"); }
            lastSequence = sequence.Value;
            var data = row["Data"] as JObject;
            if (kind == "capture-limit" || kind == "capture-failed") Issue("Capture contains a stop-limit or failure event.");
            if (kind == "context" && data != null && (Boolean(data["Active"]) == false || Boolean(data["Paused"]) == true || Boolean(data["Loading"]) == true))
                BreakAll("inactive-paused-or-loading-context");
            if (!Relevant(kind)) return;
            if (data == null) { Issue("Replay record has no object payload."); BreakAll("missing-payload"); return; }
            var generation = Text(row["GenerationId"]);
            var run = Text(row["RunId"]);
            var map = Text(row["MapId"]);
            // The native path probe emits its final discontinuity after lifecycle
            // clears the run. It closes continuity; it is not an actor observation.
            if (kind == "path-gap" && run.Length == 0)
            {
                BreakAll("outside-run-path-gap");
                if (previousGroup is { } priorKey && priorKey.Generation == generation && groups.TryGetValue(priorKey, out var priorState))
                    Gap(priorState, sequence.Value, time.Value, PositiveInt(data["visit"]), "outside-run-context:" + Text(data["reason"], kind));
                return;
            }
            if (string.IsNullOrWhiteSpace(generation) || string.IsNullOrWhiteSpace(run))
            { Issue("Replay record lacks generation/run identity."); BreakAll("missing-run-identity"); return; }
            var key = (generation, run, map);
            if (previousGroup != null && previousGroup.Value != key) BreakAll("map-or-run-boundary");
            previousGroup = key;
            if (!groups.TryGetValue(key, out var state))
            {
                state = new GroupState(new EncounterReplayGroup { GenerationId = generation, RunId = run, MapId = map });
                groups.Add(key, state);
                result.Groups.Add(state.Group);
            }
            state.Group.StartTime = state.Group.StartTime == null ? time : Math.Min(state.Group.StartTime.Value, time.Value);
            state.Group.EndTime = state.Group.EndTime == null ? time : Math.Max(state.Group.EndTime.Value, time.Value);
            var segment = Text(row["SegmentId"]);
            switch (kind)
            {
                case "path-visit":
                    state.Last = null;
                    if (PositiveInt(data["visit"]) == null) Issue("Path visit has no valid visit ID.");
                    break;
                case "path-gap": case "path-discontinuity":
                    Gap(state, sequence.Value, time.Value, PositiveInt(data["visit"]), Text(data["reason"], kind));
                    break;
                case "path-point": ReadPoint(state, data, sequence.Value, time.Value, segment); break;
                case "path-teleport": case "path-placement": ReadPlacement(state, data, sequence.Value, time.Value, segment); break;
                case "map-calibration": ReadCalibration(state, data, sequence.Value, time.Value); break;
                case "combat_fatal": ReadFatal(state, data, sequence.Value, time.Value); break;
                case "combat_hurt_begin":
                    var parent = PositiveLong(data["ParentTransaction"]);
                    var child = PositiveLong(data["Transaction"]);
                    if (parent != null && child != null)
                    { nested.Add((generation, run, parent.Value)); nested.Add((generation, run, child.Value)); }
                    break;
                case "combat_hurt_complete": ReadDamage(state, data, sequence.Value, time.Value); break;
            }
        }

        private static bool Relevant(string kind) => kind is "path-visit" or "path-gap" or "path-discontinuity" or "path-point"
            or "path-teleport" or "path-placement" or "map-calibration" or "combat_fatal" or "combat_hurt_begin" or "combat_hurt_complete";

        private void ReadPoint(GroupState state, JObject data, long sequence, double time, string segment)
        {
            var position = Vector(data["position"]);
            var sampleTime = Number(data["MonotonicSeconds"]);
            var visit = PositiveInt(data["visit"]);
            if (position == null || sampleTime is null or < 0 || sampleTime > time || visit == null || state.Group.MapId.Length == 0
                || (data["MapId"] != null && Text(data["MapId"]) != state.Group.MapId))
            { Issue("Path point lacks finite position, sample time, visit or matching map."); Gap(state, sequence, time, visit, "invalid-path-point"); return; }
            var point = new EncounterReplayPoint
            {
                Sequence = sequence, ProbeSequence = PositiveLong(data["sequence"]), Time = sampleTime.Value,
                Visit = visit.Value, ActorId = PositiveInt(data["actorId"]), SegmentId = segment, Position = position.Value
            };
            if (state.Last is { } prior && prior.Visit == point.Visit && prior.SegmentId == point.SegmentId
                && prior.ActorId != null && prior.ActorId == point.ActorId)
            {
                if (point.Time > prior.Time)
                    AddEdge(state, new EncounterReplayEdge { From = prior, To = point, Style = EncounterReplayEdgeStyle.Walk, TimeEvidence = "two recorded path sample times" });
                else { Issue("Nonmonotonic path sample time; no connecting edge."); Gap(state, sequence, time, visit, "nonmonotonic-sample-time"); }
            }
            state.Group.PathPoints.Add(point);
            state.Group.StartTime = Math.Min(state.Group.StartTime ?? point.Time, point.Time);
            state.Last = point;
        }

        private void ReadPlacement(GroupState state, JObject data, long sequence, double time, string segment)
        {
            state.Last = null; // Never draw a solid sampled edge across any placement.
            var from = Vector(data["from"]);
            var arrival = Vector(data["arrival"]);
            var fromTime = Number(data["Time"]);
            var visit = PositiveInt(data["visit"]);
            if (Boolean(data["provenSameMap"]) != true || from == null || arrival == null || visit == null
                || fromTime is null or < 0 || fromTime > time || state.Group.MapId.Length == 0
                || (data["Map"] != null && Text(data["Map"]) != state.Group.MapId)
                || (data["NativeMap"] != null && data["destinationNativeMap"] != null && Text(data["NativeMap"]) != Text(data["destinationNativeMap"])))
            { Gap(state, sequence, time, visit, "placement-not-proven-with-valid-same-map-endpoints"); return; }
            // Older captures called vertical ground corrections "teleport".
            // XZ equality is evaluated independently of that legacy kind/style.
            if (from.Value.X == arrival.Value.X && from.Value.Z == arrival.Value.Z) return;
            AddEdge(state, new EncounterReplayEdge
            {
                From = new EncounterReplayPoint { Sequence = sequence, ProbeSequence = PositiveLong(data["sequence"]), Time = fromTime.Value, Visit = visit.Value, SegmentId = segment, Position = from.Value },
                To = new EncounterReplayPoint { Sequence = sequence, ProbeSequence = PositiveLong(data["sequence"]), Time = time, Visit = visit.Value, SegmentId = segment, Position = arrival.Value },
                Style = EncounterReplayEdgeStyle.Teleport,
                TimeEvidence = "recorded placement-start context time and completion envelope time; zero walked distance"
            });
        }

        private void AddEdge(GroupState state, EncounterReplayEdge edge)
        {
            var edges = state.Group.PathEdges;
            if (edges.Count > 0 && edge.FromTime < edges[edges.Count - 1].ToTime)
            {
                Issue("Overlapping path observation times; conflicting edge omitted without adjusting recorded endpoints.");
                Gap(state, edge.Sequence, edge.ToTime, edge.Visit, "overlapping-edge-times");
                return;
            }
            state.Group.StartTime = Math.Min(state.Group.StartTime ?? edge.FromTime, edge.FromTime);
            edges.Add(edge);
        }

        private void ReadFatal(GroupState state, JObject data, long sequence, double time)
        {
            var fatalSequence = PositiveLong(data["FatalSequence"]);
            if (fatalSequence == null) { Issue("Confirmed fatal lacks ordering sequence; row omitted."); return; }
            if (!state.FatalSequences.Add(fatalSequence.Value)) { Issue("Duplicate fatal identity within map/run; row omitted."); return; }
            var source = Source(data["Source"] as JObject);
            var target = Actor(data["Target"] as JObject);
            var playerDeath = Text(data["Kind"]) == "player-death";
            var directPlayer = source.Kind is "projectile" or "melee" && source.Delayed != true
                && source.Credited.IsMain == true && source.Credited.Id != null
                && (source.NativeDamageActor.Id == null || source.NativeDamageActor.Id == source.Credited.Id);
            var kind = playerDeath ? EncounterReplayFatalKind.PlayerDeath
                : directPlayer && target.IsMain == false && target.Kind == "character" ? EncounterReplayFatalKind.PlayerKill
                : EncounterReplayFatalKind.OtherPlayerRelatedDeath;
            source.AttributionEvidence = kind == EncounterReplayFatalKind.PlayerKill
                ? "Observed direct source credits player; original launch and physical actor remain separate."
                : playerDeath ? "Player death confirmed; source identity is diagnostic evidence only."
                : "Player-related target death; player kill credit is unresolved or belongs to another actor. OriginallyPlayer and retained buff fields do not prove causality.";
            var candidate = data["Candidate"] as JObject;
            var candidateProven = Boolean(data["PositionsAvailableBeforeCleanup"]) == true && candidate != null
                && PositiveLong(candidate["Sequence"]) == fatalSequence;
            if (candidate != null && !candidateProven) Issue("Fatal candidate identity/availability does not match confirmed fatal; endpoints omitted.");
            var fatal = new EncounterReplayFatal
            {
                Sequence = sequence, FatalSequence = fatalSequence.Value, Transaction = PositiveLong(data["Transaction"]),
                Time = time, Kind = kind, Target = target, Source = source,
                CandidateStopwatchTicks = candidateProven ? PositiveLong(candidate!["StopwatchTicks"]) : null,
                DiagnosticHpLoss = Nonnegative(data["HpLoss"]), Attribution = source.AttributionEvidence
            };
            if (candidateProven)
            {
                var timing = Text(candidate!["Timing"]);
                fatal.TargetPosition = Endpoint(candidate["TargetPosition"] as JObject, timing);
                fatal.PlayerPosition = Endpoint(candidate["PlayerPosition"] as JObject, timing);
                fatal.SourcePosition = Endpoint(candidate["SourcePosition"] as JObject, timing);
                fatal.EnemyPosition = playerDeath ? fatal.SourcePosition : fatal.TargetPosition;
            }
            state.Group.Fatals.Add(fatal);
        }

        private void ReadDamage(GroupState state, JObject data, long sequence, double time)
        {
            var value = new EncounterReplayDamage
            {
                Sequence = sequence, Time = time, Transaction = PositiveLong(data["Transaction"]),
                Target = Actor(data["Target"] as JObject), Source = Source(data["Source"] as JObject),
                NetHpLossAcrossCall = Nonnegative(data["NetHpLossAcrossCall"]),
                ProposedHpLossOwnedAssignments = Nonnegative(data["ProposedHpLossOwnedAssignments"])
            };
            state.Group.DamageDiagnostics.Add(value);
            if (value.Transaction is long transaction)
                damage[(state.Group.GenerationId, state.Group.RunId, transaction)] = value;
        }

        private void ReadCalibration(GroupState state, JObject data, long sequence, double time)
        {
            var id = Text(data["calibrationId"]);
            var metadata = data["metadata"] as JObject;
            if (id.Length != 64 || !id.All(Uri.IsHexDigit) || metadata == null
                || Text(metadata["MapId"]) != state.Group.MapId)
            { Issue("Calibration lacks a safe hash identity or matching plain metadata."); return; }
            if (state.Group.Calibrations.Any(value => value.CalibrationId == id)) return;
            var value = new EncounterReplayCalibration
            {
                CalibrationId = id, Sequence = sequence, Time = time, MetadataJson = metadata.ToString(Formatting.None),
                NativeSceneId = Text(metadata["nativeSceneId"]), MapGroup = Text(metadata["mapGroup"]),
                Combined = Boolean(metadata["combined"]), WorldCenter = Vector(metadata["worldCenter"]),
                CombinedCenter = Vector(metadata["combinedCenter"]), ImageWorldSize = PositiveNumber(metadata["imageWorldSize"]),
                CombinedSize = PositiveNumber(metadata["combinedSize"]), Offset = NumberArray(metadata["offset"], 2),
                SpriteRect = NumberArray(metadata["spriteRect"], 4), SpritePivot = NumberArray(metadata["spritePivot"], 2),
                PixelsPerUnit = PositiveNumber(metadata["pixelsPerUnit"])
            };
            value.Available = Boolean(metadata["available"]) == true && Boolean(metadata["hide"]) == false
                && Boolean(metadata["noSignal"]) == false && value.WorldCenter != null && value.ImageWorldSize != null
                && value.Offset != null && value.Combined != null && (value.Combined != true || (value.CombinedCenter != null && value.CombinedSize != null));
            if (Boolean(metadata["available"]) == true && !value.Available) Issue("Calibration claims availability but required finite fields are absent.");
            state.Group.Calibrations.Add(value);
        }

        private static EncounterReplayActor Actor(JObject? data) => new()
        {
            Id = PositiveInt(data?["Id"]), PresetKey = Text(data?["PresetKey"]),
            Kind = Text(data?["Kind"], "unavailable"), IsMain = Boolean(data?["IsMain"])
        };

        private static EncounterReplaySource Source(JObject? data) => new()
        {
            Kind = Text(data?["Kind"], "unknown"), Provenance = Text(data?["Provenance"]),
            Physical = Actor(data?["Physical"] as JObject), Credited = Actor(data?["Credited"] as JObject),
            NativeDamageActor = Actor(data?["NativeDamageActor"] as JObject),
            OriginalPhysical = Actor(data?["OriginalPhysical"] as JObject), OriginalCredited = Actor(data?["OriginalCredited"] as JObject),
            OriginallyPlayer = Boolean(data?["OriginallyPlayer"]), Delayed = Boolean(data?["Delayed"]),
            WeaponId = PositiveInt(data?["WeaponId"]), LoadedAmmoId = PositiveInt(data?["LoadedAmmoId"]),
            TargetAmmoId = PositiveInt(data?["TargetAmmoId"]), AmmoAgreement = Boolean(data?["AmmoAgreement"]),
            BuffId = PositiveInt(data?["BuffId"])
        };

        private EncounterReplayEndpoint? Endpoint(JObject? data, string timing)
        {
            if (Boolean(data?["Available"]) != true) return null;
            var x = Number(data?["X"]); var y = Number(data?["Y"]); var z = Number(data?["Z"]);
            if (x == null || y == null || z == null) { Issue("Available fatal endpoint has invalid coordinates; endpoint omitted."); return null; }
            return new EncounterReplayEndpoint
            {
                Position = new EncounterReplayVector(x.Value, y.Value, z.Value), LogicalScene = Text(data?["LogicalScene"]),
                SceneEvidence = Text(data?["SceneEvidence"]), PhysicalSceneBuild = NonnegativeInt(data?["PhysicalSceneBuild"]),
                RelatedSceneBuild = NonnegativeInt(data?["RelatedSceneBuild"]), Timing = timing
            };
        }

        private static void Gap(GroupState state, long? sequence, double? time, int? visit, string reason)
        {
            state.Last = null;
            state.Group.Gaps.Add(new EncounterReplayGap { Sequence = sequence, Time = time, Visit = visit, Reason = reason });
        }

        private void BreakAll(string reason)
        {
            foreach (var state in groups.Values)
                if (state.Last != null) Gap(state, null, null, state.Last.Visit, reason);
        }

        private void Budget(string name) { result.LimitReached = true; Issue("Replay stopped at " + name + " budget."); BreakAll("replay-budget"); }
        private void Issue(string text)
        {
            result.IsPartial = true;
            if (result.Issues.Count < 128) result.Issues.Add(text); else suppressedIssues++;
        }

        private sealed class GroupState
        {
            public GroupState(EncounterReplayGroup group) => Group = group;
            public EncounterReplayGroup Group { get; }
            public EncounterReplayPoint? Last { get; set; }
            public HashSet<long> FatalSequences { get; } = new();
        }
    }

    private sealed class ReplayLineLimitException : Exception { }
    private static string Text(JToken? token, string fallback = "") => token?.Type == JTokenType.String ? token.Value<string>() ?? fallback : fallback;
    private static bool? Boolean(JToken? token) => token?.Type == JTokenType.Boolean ? token.Value<bool>() : null;
    private static long? Integer(JToken? token)
    {
        if (token?.Type != JTokenType.Integer) return null;
        try { return token.Value<long>(); } catch (Exception exception) when (exception is OverflowException || exception is InvalidCastException) { return null; }
    }
    private static long? PositiveLong(JToken? token) => Integer(token) is > 0 and var value ? value : null;
    private static int? PositiveInt(JToken? token) => Integer(token) is > 0 and <= int.MaxValue and var value ? (int)value : null;
    private static int? NonnegativeInt(JToken? token) => Integer(token) is >= 0 and <= int.MaxValue and var value ? (int)value : null;
    private static double? Number(JToken? token)
    {
        if (token?.Type is not (JTokenType.Integer or JTokenType.Float)) return null;
        try
        {
            var value = token.Value<double>();
            return double.IsNaN(value) || double.IsInfinity(value) ? null : value;
        }
        catch (Exception exception) when (exception is OverflowException || exception is InvalidCastException) { return null; }
    }
    private static double? Nonnegative(JToken? token) => Number(token) is >= 0 and var value ? value : null;
    private static double? PositiveNumber(JToken? token) => Number(token) is > 0 and var value ? value : null;
    private static double[]? NumberArray(JToken? token, int length)
    {
        if (token is not JArray array || array.Count != length) return null;
        var result = new double[length];
        for (var index = 0; index < length; index++)
        {
            var number = Number(array[index]);
            if (number == null) return null;
            result[index] = number.Value;
        }
        return result;
    }
    private static EncounterReplayVector? Vector(JToken? token)
    {
        var value = NumberArray(token, 3);
        return value == null ? null : new EncounterReplayVector(value[0], value[1], value[2]);
    }
}
#endif
