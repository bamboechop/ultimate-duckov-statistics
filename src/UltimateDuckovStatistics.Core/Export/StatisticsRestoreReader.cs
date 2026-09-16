using System.IO.Compression;
using System.Runtime.Serialization.Json;
using System.Xml;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Export;

/// <summary>A validated, detached export. Confirmation never rereads a mutable source file.</summary>
public sealed class StatisticsRestorePreview
{
    private readonly ProfileDocument profile;
    internal StatisticsRestorePreview(string path, DateTime exportedUtc, ProfileDocument profile)
    { SourcePath = path; ExportedUtc = exportedUtc; this.profile = profile; }
    public string SourcePath { get; }
    public DateTime ExportedUtc { get; }
    public int Slot => profile.Slot;
    public string SourceGenerationId => profile.GenerationId;
    public long RunCount => profile.Statistics.RunTotals.TotalRuns;
    public double RaidMeters => profile.Statistics.RunTotals.PhysicalDistance;
    internal IList<Encounters.EncounterRecord>? CreateEncounterHistory() => profile.EncounterHistory?
        .Select(record => ProfileRecordCodec.Decode<Encounters.EncounterRecord>(new ProfileRecordCodec().Encode(record))).ToList();

    internal ProfileStatistics CreateStatistics(string generationId, DateTime now)
    {
        // A restore is an explicit, one-time operation. Clone once so a failed
        // replacement can be retried without changing the validated preview.
        var statistics = ProfileRecordCodec.Decode<ProfileStatistics>(new ProfileRecordCodec().Encode(profile.Statistics));
        statistics.SaveGenerationId = generationId;
        statistics.UpdatedUtc = now;
        foreach (var run in statistics.Runs) run.SaveGenerationId = generationId;
        statistics.Holdings.SaveGenerationId = generationId;
        foreach (var observation in new[] { statistics.Holdings.Money, statistics.Holdings.Cash })
            if (observation.SaveGenerationId.Length > 0) observation.SaveGenerationId = generationId;
        return statistics;
    }
}

public static class StatisticsRestoreReader
{
    // Applies to uncompressed JSON as well as ZIP entries. No archive is extracted.
    public const long MaximumJsonBytes = 1024L * 1024 * 1024;

    public static StatisticsRestorePreview Read(string path, int destinationSlot, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Select a JSON or ZIP export.", nameof(path));
        path = Path.GetFullPath(path.Trim().Trim('"'));
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        StatisticsExportDocument document;
        if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var archive = new ZipArchive(file, ZipArchiveMode.Read);
            if (archive.Entries.Count != 1 || archive.Entries[0].FullName != "statistics.json")
                throw new InvalidDataException("The ZIP must contain only statistics.json at its root.");
            var entry = archive.Entries[0];
            ValidateSize(entry.Length);
            using var json = entry.Open();
            document = ReadDocument(json, cancellationToken);
        }
        else if (string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
        {
            ValidateSize(file.Length);
            document = ReadDocument(file, cancellationToken);
        }
        else throw new InvalidDataException("Select a .json or .zip statistics export.");
        return new StatisticsRestorePreview(path, document.ExportedUtc, Convert(document, destinationSlot, cancellationToken));
    }

    private static void ValidateSize(long size)
    {
        if (size <= 0 || size > MaximumJsonBytes) throw new InvalidDataException("Export JSON must be nonempty and at most 1 GiB.");
    }

    private static StatisticsExportDocument ReadDocument(Stream input, CancellationToken cancellationToken)
    {
        using var bounded = new BoundedInput(input, cancellationToken);
        using var reader = JsonReaderWriterFactory.CreateJsonReader(bounded, new XmlDictionaryReaderQuotas
        { MaxDepth = 128, MaxStringContentLength = 16 * 1024 * 1024, MaxNameTableCharCount = 16 * 1024 * 1024, MaxArrayLength = int.MaxValue, MaxBytesPerRead = 4096 });
        var serializer = new DataContractJsonSerializer(typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        var document = (StatisticsExportDocument?)serializer.ReadObject(reader)
            ?? throw new InvalidDataException("Export is empty.");
        if (reader.Read()) throw new InvalidDataException("Unexpected content follows the export.");
        // The DCS JSON reader stops at the root and may silently ignore buffered
        // trailing data. Drain the bounded stream so its root-termination guard
        // also checks bytes not requested by the serializer.
        var remaining = new byte[4096];
        while (bounded.Read(remaining, 0, remaining.Length) > 0) { }
        return document;
    }

    private static ProfileDocument Convert(StatisticsExportDocument source, int destinationSlot, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.SchemaVersion != ProductInfo.SchemaVersion) throw new InvalidDataException("Unsupported export schema.");
        if (source.Slot != destinationSlot) throw new InvalidDataException("This export belongs to a different save slot.");
        if (string.IsNullOrWhiteSpace(source.GenerationId) || source.ExportedUtc.Kind != DateTimeKind.Utc
            || source.ExportedUtc == default || source.Revision < 0)
            throw new InvalidDataException("Export identity or timestamp is invalid.");
        if (source.Overall == null || source.Groups == null || source.Items == null || source.RunTotals == null
            || source.Runs == null || source.RunRecords == null || source.Capabilities == null || source.Economy == null
            || source.WorldTime == null || source.Crafting == null || source.Holdings == null || source.Distance == null
            || source.Groups.Any(row => row == null) || source.Items.Any(row => row == null)
            || source.Runs.Any(run => run == null) || source.Capabilities.Any(capability => capability == null))
            throw new InvalidDataException("Export roots are incomplete.");
        var statistics = new ProfileStatistics
        {
            SaveGenerationId = source.GenerationId,
            CreatedUtc = source.ExportedUtc,
            UpdatedUtc = source.ExportedUtc,
            Overall = source.Overall,
            HealingCaptureComplete = source.HealingCaptureComplete,
            RunTotals = source.RunTotals,
            Runs = source.Runs.ToList(),
            RunRecords = source.RunRecords,
            Economy = source.Economy,
            WorldTime = source.WorldTime,
            Crafting = source.Crafting,
            Holdings = new EconomyHoldingsSnapshot
            {
                SaveGenerationId = source.Holdings.SaveGenerationId,
                Money = source.Holdings.Money,
                Cash = source.Holdings.Cash,
                Capabilities = source.Holdings.Capabilities,
                WasRepairedFromInvalidState = source.Holdings.WasRepairedFromInvalidState
            },
            BaseMovement = source.Distance.BaseMeters.HasValue ? new BaseMovementStatistics
            {
                RecordedMeters = source.Distance.BaseMeters.Value,
                CollectionStartedUtc = source.Distance.BaseCollectionStartedUtc ?? default,
                // Old exports do not distinguish a collection gap from current
                // capability loss. Keep partial evidence conservatively.
                HasKnownGaps = source.Distance.BasePartial
            } : null
        };
        foreach (var row in source.Groups)
        {
            _ = ParseEnum<CanonicalItemGroup>(row.Group);
            if (statistics.Groups.ContainsKey(row.Group)) throw new InvalidDataException("Duplicate item group.");
            statistics.Groups.Add(row.Group, row.Totals);
        }
        foreach (var row in source.Items)
        {
            if (string.IsNullOrWhiteSpace(row.ItemId) || statistics.Items.ContainsKey(row.ItemId) || row.EffectTags == null)
                throw new InvalidDataException("Invalid or duplicate item identity.");
            statistics.Items.Add(row.ItemId, new ItemAggregate
            {
                ItemId = row.ItemId,
                DisplayName = row.DisplayName,
                Group = ParseEnum<CanonicalItemGroup>(row.Group),
                EffectTags = row.EffectTags.Select(ParseEnum<ItemEffectTag>).ToList(),
                Totals = row.Totals
            });
        }
        var lifetime = new ItemStatisticsAggregate { Overall = statistics.Overall, Items = statistics.Items, Groups = statistics.Groups };
        ItemStatisticsAggregateReducer.Validate(lifetime);
        if (!ItemStatisticsAggregateReducer.IsCompositionConsistent(lifetime)) throw new InvalidDataException("Item totals are inconsistent.");
        var runIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var run in statistics.Runs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RunReducer.Validate(run);
            if (run.SaveGenerationId != source.GenerationId || !runIds.Add(run.RunId))
                throw new InvalidDataException("Run identity or generation is inconsistent.");
        }
        if (statistics.RunTotals.TotalRuns != statistics.Runs.Count) throw new InvalidDataException("Export is missing recorded runs.");
        try { Encounters.EncounterRecordValidation.ValidateHistory(source.EncounterHistory, runIds); }
        catch (ArgumentException exception) { throw new InvalidDataException("Export encounter history is invalid.", exception); }
        if (source.Distance.BaseCollectionStartedUtc.HasValue != source.Distance.BaseMeters.HasValue)
            throw new InvalidDataException("Base distance evidence is incomplete.");
        var profile = new ProfileDocument
        {
            GenerationId = source.GenerationId,
            Slot = source.Slot,
            Revision = source.Revision,
            CreatedUtc = source.ExportedUtc,
            UpdatedUtc = source.ExportedUtc,
            GenerationReason = "UserRestore",
            Identity = new SaveIdentitySnapshot { Slot = source.Slot },
            Statistics = statistics,
            EncounterHistory = source.EncounterHistory is { Count: > 0 } ? source.EncounterHistory : null,
            Capabilities = source.Capabilities
        };
        var error = ProfileFormat.ValidateRecoveryCandidate(profile);
        if (error != null) throw new InvalidDataException(error);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateRunSummaries(statistics);
        cancellationToken.ThrowIfCancellationRequested();
        EconomyHoldingsReducer.NormalizePersisted(statistics.Holdings, source.GenerationId, downgradeCurrent: true);
        return profile;
    }

    private static void ValidateRunSummaries(ProfileStatistics statistics)
    {
        // Export projects live capability states onto historical metrics. Replay
        // checks the run-derived counts/distances/records without mistaking those
        // projected states for a change to the recorded values.
        var replay = RunReducer.RebuildRunStatistics(statistics.SaveGenerationId, statistics.Runs);
        var actual = statistics.RunTotals;
        var expected = replay.RunTotals;
        CheckCounts(actual.Outcomes, expected.Outcomes);
        CheckDistance(actual.PhysicalDistance, expected.PhysicalDistance);
        CheckDistance(actual.TeleportDistance, expected.TeleportDistance);
        CheckDistance(actual.TransitionExcludedDistance, expected.TransitionExcludedDistance);
        if (actual.Maps.Count != expected.Maps.Count || actual.RouteMaps.Count != expected.RouteMaps.Count)
            throw new InvalidDataException("Map totals do not match the recorded runs.");
        foreach (var pair in actual.Maps)
        {
            if (!expected.Maps.TryGetValue(pair.Key, out var map) || pair.Value.TotalRuns != map.TotalRuns)
                throw new InvalidDataException("Starting-map counts do not match the recorded runs.");
            CheckCounts(pair.Value.Outcomes, map.Outcomes);
            CheckDistance(pair.Value.PhysicalDistance, map.PhysicalDistance);
            CheckDistance(pair.Value.TeleportDistance, map.TeleportDistance);
            ContainerStatisticsReducer.ValidateAggregate(pair.Value.ContainerStatistics);
            ItemStatisticsAggregateReducer.Validate(pair.Value.ItemStatistics);
        }
        foreach (var pair in actual.RouteMaps)
        {
            if (!expected.RouteMaps.TryGetValue(pair.Key, out var map) || pair.Value.RunsVisited != map.RunsVisited
                || pair.Value.SegmentVisits != map.SegmentVisits)
                throw new InvalidDataException("Route-map counts do not match the recorded runs.");
            CheckDistance(pair.Value.PhysicalDistance, map.PhysicalDistance);
            CheckDistance(pair.Value.TeleportDistance, map.TeleportDistance);
            CheckDistance(pair.Value.TransitionExcludedDistance, map.TransitionExcludedDistance);
            CheckDistance(pair.Value.ActiveDurationSeconds, map.ActiveDurationSeconds);
            ContainerStatisticsReducer.ValidateAggregate(pair.Value.ContainerStatistics);
            ItemStatisticsAggregateReducer.Validate(pair.Value.ItemStatistics);
        }
        ContainerStatisticsReducer.ValidateAggregate(actual.ContainerStatistics);
        ItemStatisticsAggregateReducer.Validate(actual.ItemStatistics);
        var codec = new ProfileRecordCodec();
        if (!codec.Encode(statistics.RunRecords).SequenceEqual(codec.Encode(replay.RunRecords)))
            throw new InvalidDataException("Records do not match the recorded runs.");
    }

    private static void CheckDistance(double actual, double expected)
    {
        if (double.IsNaN(actual) || double.IsInfinity(actual) || actual < 0
            || Math.Abs(actual - expected) > 1e-9 * Math.Max(1, expected))
            throw new InvalidDataException("Distance totals do not match the recorded runs.");
    }

    private static void CheckCounts(Dictionary<string, long> actual, Dictionary<string, long> expected)
    {
        if (actual.Count != expected.Count || actual.Any(pair => !expected.TryGetValue(pair.Key, out var value) || value != pair.Value))
            throw new InvalidDataException("Outcome totals do not match the recorded runs.");
    }

    private static T ParseEnum<T>(string value) where T : struct, Enum =>
        Enum.TryParse<T>(value, out var result) && Enum.IsDefined(typeof(T), result)
            ? result : throw new InvalidDataException("Invalid export category: " + value);

    private sealed class BoundedInput(Stream source, CancellationToken cancellationToken) : Stream
    {
        private long bytes;
        private int depth;
        private bool inString, escaped, complete;
        public override int Read(byte[] buffer, int offset, int count)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = source.Read(buffer, offset, (int)Math.Min(count, MaximumJsonBytes - bytes + 1));
            bytes += read;
            if (bytes > MaximumJsonBytes) throw new InvalidDataException("Export JSON exceeds 1 GiB.");
            for (var i = offset; i < offset + read; i++)
            {
                var value = buffer[i];
                if (complete)
                {
                    if (value != ' ' && value != '\t' && value != '\r' && value != '\n')
                        throw new InvalidDataException("Unexpected content follows the export.");
                }
                else if (inString)
                {
                    if (escaped) escaped = false;
                    else if (value == '\\') escaped = true;
                    else if (value == '"') inString = false;
                }
                else if (value == '"') inString = true;
                else if (value == '{' || value == '[') depth++;
                else if (value == '}' || value == ']') complete = --depth == 0;
            }
            return read;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => bytes; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
