using System.Globalization;
using System.Text;
using System.Text.Json;

namespace FrameTimeAnalyzer;

internal sealed class CaptureMetadata
{
    public string Configuration { get; set; } = "?";
    public string BuildLabel { get; set; } = "production";
    public string Scenario { get; set; } = "unknown";
    public int Run { get; set; }
    public double? ActionStartSeconds { get; set; }
    public double? ActionEndSeconds { get; set; }
    public IReadOnlyList<CaptureWindowMetadata>? Windows { get; set; }
}

internal sealed class CaptureWindowMetadata
{
    public string Name { get; set; } = string.Empty;
    public double StartSeconds { get; set; }
    public double EndSeconds { get; set; }
}

internal sealed class FrameTimeThreshold
{
    public required double Milliseconds { get; init; }
    public required int Count { get; init; }
    public required double Percentage { get; init; }
}

internal sealed class FrameTimeSpike
{
    public required double OffsetSeconds { get; init; }
    public required double FrameTimeMilliseconds { get; init; }
    public required bool DuringAction { get; init; }
    public double? CpuBusyMilliseconds { get; init; }
    public double? GpuBusyMilliseconds { get; init; }
}

internal sealed class MetricDistribution
{
    public required int SampleCount { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double MedianMilliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double P99Milliseconds { get; init; }
    public required double MaximumMilliseconds { get; init; }
}

internal sealed class CaptureWindowSummary
{
    public required string Name { get; init; }
    public required double StartSeconds { get; init; }
    public required double EndSeconds { get; init; }
    public required int FrameCount { get; init; }
    public required double DurationSeconds { get; init; }
    public required double AverageFps { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double MedianMilliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double P99Milliseconds { get; init; }
    public required double MaximumMilliseconds { get; init; }
    public required IReadOnlyList<FrameTimeThreshold> Thresholds { get; init; }
    public required IReadOnlyList<FrameTimeSpike> SpikesOver33Milliseconds { get; init; }
    public required bool HasSpikeCluster { get; init; }
    public MetricDistribution? CpuBusy { get; init; }
    public MetricDistribution? GpuBusy { get; init; }
}

internal sealed class CaptureSummary
{
    public required string Source { get; init; }
    public required string Configuration { get; init; }
    public required string BuildLabel { get; init; }
    public required string Scenario { get; init; }
    public required int Run { get; init; }
    public required string FrameTimeColumn { get; init; }
    public required string DominantSwapChain { get; init; }
    public required int ParsedRows { get; init; }
    public required int FrameCount { get; init; }
    public required int IgnoredNonDominantRows { get; init; }
    public required double DurationSeconds { get; init; }
    public required double AverageFps { get; init; }
    public required double MeanMilliseconds { get; init; }
    public required double MedianMilliseconds { get; init; }
    public required double P95Milliseconds { get; init; }
    public required double P99Milliseconds { get; init; }
    public required double MaximumMilliseconds { get; init; }
    public required IReadOnlyList<FrameTimeThreshold> Thresholds { get; init; }
    public required IReadOnlyList<FrameTimeSpike> SpikesOver33Milliseconds { get; init; }
    public required IReadOnlyList<CaptureWindowSummary> Windows { get; init; }
    public required bool HasSpikeCluster { get; init; }
    public required bool HasActionSpikeCluster { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
    public MetricDistribution? CpuBusy { get; init; }
    public MetricDistribution? GpuBusy { get; init; }
    public int? ActionFrameCount { get; init; }
    public double? ActionDurationSeconds { get; init; }
    public double? ActionAverageFps { get; init; }
    public double? ActionMeanMilliseconds { get; init; }
    public double? ActionMedianMilliseconds { get; init; }
    public double? ActionP95Milliseconds { get; init; }
    public double? ActionP99Milliseconds { get; init; }
    public double? ActionMaximumMilliseconds { get; init; }
    public IReadOnlyList<FrameTimeThreshold>? ActionThresholds { get; init; }
    public MetricDistribution? ActionCpuBusy { get; init; }
    public MetricDistribution? ActionGpuBusy { get; init; }
}

internal sealed class AggregateSummary
{
    public required string Configuration { get; init; }
    public required string BuildLabel { get; init; }
    public required string Scenario { get; init; }
    public required int RunCount { get; init; }
    public required double MedianOfRunMediansMilliseconds { get; init; }
    public required double MedianOfRunP99Milliseconds { get; init; }
    public required double MedianFramesOver16MillisecondsPercentage { get; init; }
    public required int CapturesWithSpikeCluster { get; init; }
    public required int CapturesWithActionSpikeCluster { get; init; }
    public double? MedianOfRunCpuBusyMediansMilliseconds { get; init; }
    public double? MedianOfRunCpuBusyP99Milliseconds { get; init; }
    public double? MedianOfRunGpuBusyMediansMilliseconds { get; init; }
    public double? MedianOfRunGpuBusyP99Milliseconds { get; init; }
    public double? MedianOfRunActionMediansMilliseconds { get; init; }
    public double? MedianOfRunActionP99Milliseconds { get; init; }
    public double? MedianActionFramesOver16MillisecondsPercentage { get; init; }
    public double? MedianOfRunActionCpuBusyMediansMilliseconds { get; init; }
    public double? MedianOfRunActionCpuBusyP99Milliseconds { get; init; }
    public double? MedianOfRunActionGpuBusyMediansMilliseconds { get; init; }
    public double? MedianOfRunActionGpuBusyP99Milliseconds { get; init; }
}

internal sealed class ControlComparison
{
    public required string BaselineConfiguration { get; init; }
    public required string BaselineBuildLabel { get; init; }
    public required string CandidateConfiguration { get; init; }
    public required string CandidateBuildLabel { get; init; }
    public required string Scenario { get; init; }
    public required double MedianOverheadPercentage { get; init; }
    public required double P99OverheadPercentage { get; init; }
    public required double FramesOver16MillisecondsPercentagePointDelta { get; init; }
    public required int BaselineActionSpikeClusterCaptures { get; init; }
    public required int CandidateActionSpikeClusterCaptures { get; init; }
    public required bool HasRepeatableNewActionSpikeCluster { get; init; }
    public double? CpuBusyMedianOverheadPercentage { get; init; }
    public double? CpuBusyP99OverheadPercentage { get; init; }
    public double? GpuBusyMedianOverheadPercentage { get; init; }
    public double? GpuBusyP99OverheadPercentage { get; init; }
    public double? ActionMedianOverheadPercentage { get; init; }
    public double? ActionP99OverheadPercentage { get; init; }
    public double? ActionFramesOver16MillisecondsPercentagePointDelta { get; init; }
    public double? ActionCpuBusyMedianOverheadPercentage { get; init; }
    public double? ActionCpuBusyP99OverheadPercentage { get; init; }
    public double? ActionGpuBusyMedianOverheadPercentage { get; init; }
    public double? ActionGpuBusyP99OverheadPercentage { get; init; }
}

internal sealed class FrameTimeReport
{
    public required IReadOnlyList<CaptureSummary> Captures { get; init; }
    public required IReadOnlyList<AggregateSummary> Aggregates { get; init; }
    public required IReadOnlyList<ControlComparison> Comparisons { get; init; }
}

internal static class FrameTimeCsvAnalyzer
{
    private static readonly double[] ThresholdMilliseconds = [8.33, 16.7, 33.3];
    private static readonly JsonSerializerOptions MetadataJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static CaptureSummary AnalyzeFile(string path)
    {
        var metadataPath = path.EndsWith(".capframex.json", StringComparison.OrdinalIgnoreCase)
            ? path[..^".capframex.json".Length] + ".capture.json"
            : Path.ChangeExtension(path, ".capture.json");
        CaptureMetadata? metadata = null;
        if (File.Exists(metadataPath))
        {
            metadata = JsonSerializer.Deserialize<CaptureMetadata>(
                File.ReadAllText(metadataPath),
                MetadataJsonOptions);
        }

        if (path.EndsWith(".capframex.json", StringComparison.OrdinalIgnoreCase))
            return AnalyzeCapFrameXJson(path, File.ReadAllText(path), metadata);

        return AnalyzeLines(path, File.ReadLines(path), metadata);
    }

    public static CaptureSummary AnalyzeCapFrameXJson(
        string source,
        string inputJson,
        CaptureMetadata? metadata = null)
    {
        using var document = JsonDocument.Parse(inputJson);
        if (!document.RootElement.TryGetProperty("Runs", out var runs)
            || runs.ValueKind != JsonValueKind.Array
            || runs.GetArrayLength() != 1)
        {
            var count = runs.ValueKind == JsonValueKind.Array ? runs.GetArrayLength() : 0;
            throw new InvalidDataException($"CapFrameX JSON must contain exactly one run: {source}. Found {count}.");
        }

        var run = runs[0];
        if (!run.TryGetProperty("CaptureData", out var captureData)
            || captureData.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"CapFrameX JSON contains no raw CaptureData object: {source}.");

        var frameTimes = ReadRequiredNumberArray(captureData, "MsBetweenPresents", source);
        var timestamps = ReadRequiredNumberArray(captureData, "TimeInSeconds", source);
        var dropped = ReadOptionalBooleanArray(captureData, "Dropped", frameTimes.Length, source);
        var cpuBusy = ReadOptionalNumberArray(captureData, "CpuActive", frameTimes.Length, source);
        var gpuBusy = ReadOptionalNumberArray(captureData, "GpuActive", frameTimes.Length, source);
        if (timestamps.Length != frameTimes.Length)
            throw new InvalidDataException(
                $"CapFrameX JSON raw array length mismatch: {source}. " +
                $"MsBetweenPresents={frameTimes.Length}, TimeInSeconds={timestamps.Length}.");

        var frames = new List<RawFrame>(frameTimes.Length);
        for (var index = 0; index < frameTimes.Length; index++)
        {
            var frameTime = frameTimes[index];
            if (!double.IsFinite(frameTime) || frameTime <= 0 || dropped[index]) continue;
            var timestamp = timestamps[index];
            frames.Add(new RawFrame(
                frameTime,
                double.IsFinite(timestamp) ? timestamp : null,
                cpuBusy?[index],
                gpuBusy?[index]));
        }

        if (frames.Count == 0)
            throw new InvalidDataException($"CapFrameX JSON has no valid displayed frame rows: {source}.");

        return SummarizeFrames(
            source,
            metadata,
            "MsBetweenPresents",
            "capframex-json",
            frames.Count,
            frames,
            0,
            1d,
            []);
    }

    public static CaptureSummary AnalyzeLines(
        string source,
        IEnumerable<string> inputLines,
        CaptureMetadata? metadata = null)
    {
        var lines = inputLines
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.TrimStart('\uFEFF'))
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToList();
        if (lines.Count < 2) throw new InvalidDataException($"Capture has no frame rows: {source}");

        var headers = ParseCsvLine(lines[0]);
        var indices = headers
            .Select((name, index) => (Name: name.Trim(), Index: index))
            .GroupBy(value => value.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);
        var frameColumn = FirstPresent(indices, "msBetweenPresents", "CPUFrameTime", "DisplayedTime", "FrameTime")
            ?? throw new InvalidDataException(
                $"Capture does not contain a supported raw frame-time column: {source}. " +
                "Use the repository CapFrameX wrapper with JsonCsv output.");
        var swapChainColumn = FirstPresent(indices, "SwapChainAddress", "SwapChain");
        var timestampColumn = FirstPresent(indices, "CPUStartQPCTimeInMs", "TimeInSeconds", "CPUStartQPC", "CPUStartTime");
        var droppedColumn = FirstPresent(indices, "Dropped");
        var cpuBusyColumn = FirstPresent(indices, "MsCPUBusy");
        var gpuBusyColumn = FirstPresent(indices, "MsGPUBusy");

        var parsedRows = 0;
        var groups = new Dictionary<string, List<RawFrame>>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Skip(1))
        {
            var fields = ParseCsvLine(line);
            if (!TryRead(fields, indices[frameColumn], out var rawFrameTime)
                || !double.TryParse(rawFrameTime, NumberStyles.Float, CultureInfo.InvariantCulture, out var frameTime)
                || !double.IsFinite(frameTime)
                || frameTime <= 0)
            {
                continue;
            }

            if (droppedColumn != null
                && TryRead(fields, indices[droppedColumn], out var dropped)
                && (string.Equals(dropped, "1", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(dropped, "true", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var swapChain = "single";
            if (swapChainColumn != null && TryRead(fields, indices[swapChainColumn], out var observedSwapChain)
                && !string.IsNullOrWhiteSpace(observedSwapChain))
            {
                swapChain = observedSwapChain.Trim();
            }

            double? timestamp = null;
            if (timestampColumn != null
                && TryRead(fields, indices[timestampColumn], out var rawTimestamp)
                && double.TryParse(rawTimestamp, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedTimestamp)
                && double.IsFinite(parsedTimestamp))
            {
                timestamp = parsedTimestamp;
            }

            if (!groups.TryGetValue(swapChain, out var frames))
            {
                frames = [];
                groups.Add(swapChain, frames);
            }
            frames.Add(new RawFrame(
                frameTime,
                timestamp,
                ReadOptionalMetric(fields, cpuBusyColumn == null ? null : indices[cpuBusyColumn]),
                ReadOptionalMetric(fields, gpuBusyColumn == null ? null : indices[gpuBusyColumn])));
            parsedRows++;
        }

        if (groups.Count == 0) throw new InvalidDataException($"Capture has no valid displayed frame rows: {source}");
        var dominant = groups
            .OrderByDescending(pair => pair.Value.Count)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First();
        var warnings = new List<string>();
        var ignored = parsedRows - dominant.Value.Count;
        if (ignored > 0)
        {
            warnings.Add($"Ignored {ignored} row(s) from non-dominant swap chains.");
            if (ignored >= dominant.Value.Count / 10d)
                warnings.Add("Non-dominant swap-chain rows exceed 10% of the selected frame count; inspect the raw capture.");
        }

        return SummarizeFrames(
            source,
            metadata,
            frameColumn,
            dominant.Key,
            parsedRows,
            dominant.Value,
            ignored,
            TimestampDivisor(dominant.Value, timestampColumn),
            warnings);
    }

    private static CaptureSummary SummarizeFrames(
        string source,
        CaptureMetadata? metadata,
        string frameColumn,
        string dominantSwapChain,
        int parsedRows,
        IReadOnlyList<RawFrame> frames,
        int ignored,
        double timestampDivisor,
        List<string> warnings)
    {
        var normalized = NormalizeOffsets(frames, timestampDivisor);
        var frameTimes = normalized.Select(value => value.FrameTimeMilliseconds).ToArray();
        var sorted = frameTimes.OrderBy(value => value).ToArray();
        var thresholds = ThresholdMilliseconds.Select(threshold =>
        {
            var count = frameTimes.Count(value => value > threshold);
            return new FrameTimeThreshold
            {
                Milliseconds = threshold,
                Count = count,
                Percentage = 100d * count / frameTimes.Length
            };
        }).ToArray();
        var actionStart = metadata?.ActionStartSeconds;
        var actionEnd = metadata?.ActionEndSeconds;
        var spikes = normalized
            .Where(value => value.FrameTimeMilliseconds > 33.3)
            .Select(value => new FrameTimeSpike
            {
                OffsetSeconds = value.OffsetSeconds,
                FrameTimeMilliseconds = value.FrameTimeMilliseconds,
                DuringAction = actionStart.HasValue && actionEnd.HasValue
                               && value.OffsetSeconds >= actionStart.Value
                               && value.OffsetSeconds <= actionEnd.Value,
                CpuBusyMilliseconds = value.CpuBusyMilliseconds,
                GpuBusyMilliseconds = value.GpuBusyMilliseconds
            })
            .ToArray();
        var actionFrames = actionStart.HasValue && actionEnd.HasValue
            ? normalized.Where(value => value.OffsetSeconds >= actionStart.Value && value.OffsetSeconds <= actionEnd.Value).ToArray()
            : [];
        var actionPhase = actionFrames.Length > 0
            ? SummarizePhase(actionFrames.Select(value => value.FrameTimeMilliseconds).ToArray())
            : null;
        var cpuBusy = SummarizeMetric(normalized.Select(value => value.CpuBusyMilliseconds));
        var gpuBusy = SummarizeMetric(normalized.Select(value => value.GpuBusyMilliseconds));
        var actionCpuBusy = SummarizeMetric(actionFrames.Select(value => value.CpuBusyMilliseconds));
        var actionGpuBusy = SummarizeMetric(actionFrames.Select(value => value.GpuBusyMilliseconds));
        var windows = BuildWindows(source, metadata?.Windows, normalized);
        if (actionStart.HasValue && actionEnd.HasValue && actionPhase == null)
            warnings.Add("No dominant-swap-chain frames fell within the configured action window.");

        return new CaptureSummary
        {
            Source = source,
            Configuration = metadata?.Configuration ?? "?",
            BuildLabel = metadata?.BuildLabel ?? "production",
            Scenario = metadata?.Scenario ?? Path.GetFileNameWithoutExtension(source),
            Run = metadata?.Run ?? 0,
            FrameTimeColumn = frameColumn,
            DominantSwapChain = dominantSwapChain,
            ParsedRows = parsedRows,
            FrameCount = frameTimes.Length,
            IgnoredNonDominantRows = ignored,
            DurationSeconds = frameTimes.Sum() / 1000d,
            AverageFps = 1000d / frameTimes.Average(),
            MeanMilliseconds = frameTimes.Average(),
            MedianMilliseconds = Quantile(sorted, 0.5),
            P95Milliseconds = Quantile(sorted, 0.95),
            P99Milliseconds = Quantile(sorted, 0.99),
            MaximumMilliseconds = sorted[^1],
            Thresholds = thresholds,
            SpikesOver33Milliseconds = spikes,
            Windows = windows,
            HasSpikeCluster = HasRollingSpikeCluster(spikes),
            HasActionSpikeCluster = HasRollingSpikeCluster(spikes.Where(value => value.DuringAction).ToArray()),
            Warnings = warnings,
            CpuBusy = cpuBusy,
            GpuBusy = gpuBusy,
            ActionFrameCount = actionPhase?.FrameCount,
            ActionDurationSeconds = actionPhase?.DurationSeconds,
            ActionAverageFps = actionPhase?.AverageFps,
            ActionMeanMilliseconds = actionPhase?.MeanMilliseconds,
            ActionMedianMilliseconds = actionPhase?.MedianMilliseconds,
            ActionP95Milliseconds = actionPhase?.P95Milliseconds,
            ActionP99Milliseconds = actionPhase?.P99Milliseconds,
            ActionMaximumMilliseconds = actionPhase?.MaximumMilliseconds,
            ActionThresholds = actionPhase?.Thresholds,
            ActionCpuBusy = actionCpuBusy,
            ActionGpuBusy = actionGpuBusy
        };
    }

    private static List<CaptureWindowSummary> BuildWindows(
        string source,
        IReadOnlyList<CaptureWindowMetadata>? metadata,
        IReadOnlyList<NormalizedFrame> frames)
    {
        if (metadata == null || metadata.Count == 0) return [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<CaptureWindowSummary>(metadata.Count);
        foreach (var window in metadata)
        {
            var name = window.Name.Trim();
            if (string.IsNullOrWhiteSpace(name)
                || !double.IsFinite(window.StartSeconds)
                || !double.IsFinite(window.EndSeconds)
                || window.StartSeconds < 0
                || window.EndSeconds <= window.StartSeconds
                || !names.Add(name))
            {
                throw new InvalidDataException(
                    $"Capture window metadata is invalid or duplicated in {source}: " +
                    $"'{window.Name}' [{window.StartSeconds}, {window.EndSeconds}].");
            }

            var selected = frames
                .Where(value => value.OffsetSeconds >= window.StartSeconds
                                && value.OffsetSeconds < window.EndSeconds)
                .ToArray();
            if (selected.Length == 0)
                throw new InvalidDataException($"Capture window '{name}' contains no frames: {source}.");
            var phase = SummarizePhase(selected.Select(value => value.FrameTimeMilliseconds).ToArray());
            var spikes = selected
                .Where(value => value.FrameTimeMilliseconds > 33.3)
                .Select(value => new FrameTimeSpike
                {
                    OffsetSeconds = value.OffsetSeconds,
                    FrameTimeMilliseconds = value.FrameTimeMilliseconds,
                    DuringAction = false,
                    CpuBusyMilliseconds = value.CpuBusyMilliseconds,
                    GpuBusyMilliseconds = value.GpuBusyMilliseconds
                })
                .ToArray();
            result.Add(new CaptureWindowSummary
            {
                Name = name,
                StartSeconds = window.StartSeconds,
                EndSeconds = window.EndSeconds,
                FrameCount = phase.FrameCount,
                DurationSeconds = phase.DurationSeconds,
                AverageFps = phase.AverageFps,
                MeanMilliseconds = phase.MeanMilliseconds,
                MedianMilliseconds = phase.MedianMilliseconds,
                P95Milliseconds = phase.P95Milliseconds,
                P99Milliseconds = phase.P99Milliseconds,
                MaximumMilliseconds = phase.MaximumMilliseconds,
                Thresholds = phase.Thresholds,
                SpikesOver33Milliseconds = spikes,
                HasSpikeCluster = HasRollingSpikeCluster(spikes),
                CpuBusy = SummarizeMetric(selected.Select(value => value.CpuBusyMilliseconds)),
                GpuBusy = SummarizeMetric(selected.Select(value => value.GpuBusyMilliseconds))
            });
        }
        return result;
    }

    private static double[] ReadRequiredNumberArray(JsonElement owner, string propertyName, string source)
    {
        if (!owner.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"CapFrameX JSON raw array '{propertyName}' is missing: {source}.");
        return property.EnumerateArray().Select(value => ReadJsonNumber(value, propertyName, source)).ToArray();
    }

    private static double?[]? ReadOptionalNumberArray(
        JsonElement owner,
        string propertyName,
        int expectedLength,
        string source)
    {
        if (!owner.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return null;
        if (property.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"CapFrameX JSON raw value '{propertyName}' is not an array: {source}.");
        if (property.GetArrayLength() != expectedLength)
            throw new InvalidDataException(
                $"CapFrameX JSON raw array length mismatch: {source}. " +
                $"MsBetweenPresents={expectedLength}, {propertyName}={property.GetArrayLength()}.");
        return property.EnumerateArray().Select(value => ReadOptionalJsonNumber(value, propertyName, source)).ToArray();
    }

    private static bool[] ReadOptionalBooleanArray(
        JsonElement owner,
        string propertyName,
        int expectedLength,
        string source)
    {
        if (!owner.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
            return new bool[expectedLength];
        if (property.ValueKind != JsonValueKind.Array || property.GetArrayLength() != expectedLength)
            throw new InvalidDataException(
                $"CapFrameX JSON raw array length mismatch: {source}. " +
                $"MsBetweenPresents={expectedLength}, {propertyName}=" +
                $"{(property.ValueKind == JsonValueKind.Array ? property.GetArrayLength() : 0)}.");
        return property.EnumerateArray().Select(value => value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"CapFrameX JSON '{propertyName}' contains a non-boolean value: {source}.")
        }).ToArray();
    }

    private static double ReadJsonNumber(JsonElement value, string propertyName, string source)
    {
        var parsed = ReadOptionalJsonNumber(value, propertyName, source);
        if (!parsed.HasValue)
            throw new InvalidDataException($"CapFrameX JSON '{propertyName}' contains an unavailable value: {source}.");
        return parsed.Value;
    }

    private static double? ReadOptionalJsonNumber(JsonElement value, string propertyName, string source)
    {
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return double.IsFinite(number) ? number : null;
        if (value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return double.IsFinite(number) ? number : null;
        throw new InvalidDataException($"CapFrameX JSON '{propertyName}' contains a non-numeric value: {source}.");
    }

    public static FrameTimeReport BuildReport(IEnumerable<CaptureSummary> captures, string? baselineConfiguration)
    {
        var captureList = captures
            .OrderBy(value => value.Scenario, StringComparer.Ordinal)
            .ThenBy(value => value.Configuration, StringComparer.Ordinal)
            .ThenBy(value => value.Run)
            .ToArray();
        var aggregates = captureList
            .GroupBy(value => (value.Configuration, value.BuildLabel, value.Scenario))
            .Select(group => new AggregateSummary
            {
                Configuration = group.Key.Configuration,
                BuildLabel = group.Key.BuildLabel,
                Scenario = group.Key.Scenario,
                RunCount = group.Count(),
                MedianOfRunMediansMilliseconds = Median(group.Select(value => value.MedianMilliseconds)),
                MedianOfRunP99Milliseconds = Median(group.Select(value => value.P99Milliseconds)),
                MedianFramesOver16MillisecondsPercentage = Median(group.Select(value =>
                    value.Thresholds.Single(threshold => threshold.Milliseconds == 16.7).Percentage)),
                CapturesWithSpikeCluster = group.Count(value => value.HasSpikeCluster),
                CapturesWithActionSpikeCluster = group.Count(value => value.HasActionSpikeCluster),
                MedianOfRunCpuBusyMediansMilliseconds = MedianNullable(group.Select(value => value.CpuBusy?.MedianMilliseconds)),
                MedianOfRunCpuBusyP99Milliseconds = MedianNullable(group.Select(value => value.CpuBusy?.P99Milliseconds)),
                MedianOfRunGpuBusyMediansMilliseconds = MedianNullable(group.Select(value => value.GpuBusy?.MedianMilliseconds)),
                MedianOfRunGpuBusyP99Milliseconds = MedianNullable(group.Select(value => value.GpuBusy?.P99Milliseconds)),
                MedianOfRunActionMediansMilliseconds = MedianNullable(group.Select(value => value.ActionMedianMilliseconds)),
                MedianOfRunActionP99Milliseconds = MedianNullable(group.Select(value => value.ActionP99Milliseconds)),
                MedianActionFramesOver16MillisecondsPercentage = MedianNullable(group.Select(value =>
                    value.ActionThresholds?.Single(threshold => threshold.Milliseconds == 16.7).Percentage)),
                MedianOfRunActionCpuBusyMediansMilliseconds = MedianNullable(group.Select(value => value.ActionCpuBusy?.MedianMilliseconds)),
                MedianOfRunActionCpuBusyP99Milliseconds = MedianNullable(group.Select(value => value.ActionCpuBusy?.P99Milliseconds)),
                MedianOfRunActionGpuBusyMediansMilliseconds = MedianNullable(group.Select(value => value.ActionGpuBusy?.MedianMilliseconds)),
                MedianOfRunActionGpuBusyP99Milliseconds = MedianNullable(group.Select(value => value.ActionGpuBusy?.P99Milliseconds))
            })
            .OrderBy(value => value.Scenario, StringComparer.Ordinal)
            .ThenBy(value => value.Configuration, StringComparer.Ordinal)
            .ToArray();
        var comparisons = new List<ControlComparison>();
        if (!string.IsNullOrWhiteSpace(baselineConfiguration))
        {
            var productionAggregates = aggregates.Where(value =>
                string.Equals(value.BuildLabel, "production", StringComparison.OrdinalIgnoreCase));
            foreach (var scenario in productionAggregates.GroupBy(value => value.Scenario, StringComparer.Ordinal))
            {
                var baseline = scenario.SingleOrDefault(value =>
                    string.Equals(value.Configuration, baselineConfiguration, StringComparison.OrdinalIgnoreCase));
                if (baseline == null) continue;
                foreach (var candidate in scenario.Where(value => !ReferenceEquals(value, baseline)))
                {
                    comparisons.Add(new ControlComparison
                    {
                        BaselineConfiguration = baseline.Configuration,
                        BaselineBuildLabel = baseline.BuildLabel,
                        CandidateConfiguration = candidate.Configuration,
                        CandidateBuildLabel = candidate.BuildLabel,
                        Scenario = candidate.Scenario,
                        MedianOverheadPercentage = Overhead(candidate.MedianOfRunMediansMilliseconds, baseline.MedianOfRunMediansMilliseconds),
                        P99OverheadPercentage = Overhead(candidate.MedianOfRunP99Milliseconds, baseline.MedianOfRunP99Milliseconds),
                        FramesOver16MillisecondsPercentagePointDelta = candidate.MedianFramesOver16MillisecondsPercentage
                                                                         - baseline.MedianFramesOver16MillisecondsPercentage,
                        BaselineActionSpikeClusterCaptures = baseline.CapturesWithActionSpikeCluster,
                        CandidateActionSpikeClusterCaptures = candidate.CapturesWithActionSpikeCluster,
                        HasRepeatableNewActionSpikeCluster = baseline.CapturesWithActionSpikeCluster == 0
                                                              && candidate.RunCount >= 3
                                                              && candidate.CapturesWithActionSpikeCluster
                                                              >= Math.Ceiling(candidate.RunCount * (2d / 3d)),
                        CpuBusyMedianOverheadPercentage = OverheadNullable(candidate.MedianOfRunCpuBusyMediansMilliseconds, baseline.MedianOfRunCpuBusyMediansMilliseconds),
                        CpuBusyP99OverheadPercentage = OverheadNullable(candidate.MedianOfRunCpuBusyP99Milliseconds, baseline.MedianOfRunCpuBusyP99Milliseconds),
                        GpuBusyMedianOverheadPercentage = OverheadNullable(candidate.MedianOfRunGpuBusyMediansMilliseconds, baseline.MedianOfRunGpuBusyMediansMilliseconds),
                        GpuBusyP99OverheadPercentage = OverheadNullable(candidate.MedianOfRunGpuBusyP99Milliseconds, baseline.MedianOfRunGpuBusyP99Milliseconds),
                        ActionMedianOverheadPercentage = OverheadNullable(candidate.MedianOfRunActionMediansMilliseconds, baseline.MedianOfRunActionMediansMilliseconds),
                        ActionP99OverheadPercentage = OverheadNullable(candidate.MedianOfRunActionP99Milliseconds, baseline.MedianOfRunActionP99Milliseconds),
                        ActionFramesOver16MillisecondsPercentagePointDelta = DifferenceNullable(candidate.MedianActionFramesOver16MillisecondsPercentage, baseline.MedianActionFramesOver16MillisecondsPercentage),
                        ActionCpuBusyMedianOverheadPercentage = OverheadNullable(candidate.MedianOfRunActionCpuBusyMediansMilliseconds, baseline.MedianOfRunActionCpuBusyMediansMilliseconds),
                        ActionCpuBusyP99OverheadPercentage = OverheadNullable(candidate.MedianOfRunActionCpuBusyP99Milliseconds, baseline.MedianOfRunActionCpuBusyP99Milliseconds),
                        ActionGpuBusyMedianOverheadPercentage = OverheadNullable(candidate.MedianOfRunActionGpuBusyMediansMilliseconds, baseline.MedianOfRunActionGpuBusyMediansMilliseconds),
                        ActionGpuBusyP99OverheadPercentage = OverheadNullable(candidate.MedianOfRunActionGpuBusyP99Milliseconds, baseline.MedianOfRunActionGpuBusyP99Milliseconds)
                    });
                }
            }
        }

        return new FrameTimeReport { Captures = captureList, Aggregates = aggregates, Comparisons = comparisons };
    }

    private static List<NormalizedFrame> NormalizeOffsets(
        IReadOnlyList<RawFrame> frames,
        double timestampDivisor)
    {
        var normalized = new List<NormalizedFrame>(frames.Count);
        var firstTimestamp = frames.FirstOrDefault(value => value.Timestamp.HasValue)?.Timestamp;
        var cumulative = 0d;
        foreach (var frame in frames)
        {
            var offset = firstTimestamp.HasValue && frame.Timestamp.HasValue
                ? Math.Max(0, (frame.Timestamp.Value - firstTimestamp.Value)
                              / timestampDivisor)
                : cumulative;
            normalized.Add(new NormalizedFrame(
                frame.FrameTimeMilliseconds,
                offset,
                frame.CpuBusyMilliseconds,
                frame.GpuBusyMilliseconds));
            cumulative += frame.FrameTimeMilliseconds / 1000d;
        }
        return normalized;
    }

    private static double TimestampDivisor(IReadOnlyList<RawFrame> frames, string? timestampColumn)
    {
        if (timestampColumn is "CPUStartQPCTimeInMs" or "CPUStartQPC" or "CPUStartTime") return 1000d;
        if (!string.Equals(timestampColumn, "TimeInSeconds", StringComparison.Ordinal)) return 1d;

        var timestamps = frames.Where(value => value.Timestamp.HasValue).Select(value => value.Timestamp!.Value).ToArray();
        if (timestamps.Length < 2) return 1d;
        var rawSpan = timestamps[^1] - timestamps[0];
        var frameDurationSeconds = frames.Sum(value => value.FrameTimeMilliseconds) / 1000d;
        if (rawSpan <= 0 || frameDurationSeconds <= 0) return 1d;

        var secondsError = Math.Abs(Math.Log(rawSpan / frameDurationSeconds));
        var millisecondsError = Math.Abs(Math.Log((rawSpan / 1000d) / frameDurationSeconds));
        return millisecondsError < secondsError ? 1000d : 1d;
    }

    private static bool HasRollingSpikeCluster(IReadOnlyList<FrameTimeSpike> spikes)
    {
        var left = 0;
        for (var right = 0; right < spikes.Count; right++)
        {
            while (spikes[right].OffsetSeconds - spikes[left].OffsetSeconds > 1d) left++;
            if (right - left + 1 >= 2) return true;
        }
        return false;
    }

    private static PhaseSummary SummarizePhase(IReadOnlyList<double> frameTimes)
    {
        var sorted = frameTimes.OrderBy(value => value).ToArray();
        return new PhaseSummary(
            frameTimes.Count,
            frameTimes.Sum() / 1000d,
            1000d / frameTimes.Average(),
            frameTimes.Average(),
            Quantile(sorted, 0.5),
            Quantile(sorted, 0.95),
            Quantile(sorted, 0.99),
            sorted[^1],
            ThresholdMilliseconds.Select(threshold =>
            {
                var count = frameTimes.Count(value => value > threshold);
                return new FrameTimeThreshold
                {
                    Milliseconds = threshold,
                    Count = count,
                    Percentage = 100d * count / frameTimes.Count
                };
            }).ToArray());
    }

    private static MetricDistribution? SummarizeMetric(IEnumerable<double?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (present.Length == 0) return null;
        var sorted = present.OrderBy(value => value).ToArray();
        return new MetricDistribution
        {
            SampleCount = present.Length,
            MeanMilliseconds = present.Average(),
            MedianMilliseconds = Quantile(sorted, 0.5),
            P95Milliseconds = Quantile(sorted, 0.95),
            P99Milliseconds = Quantile(sorted, 0.99),
            MaximumMilliseconds = sorted[^1]
        };
    }

    private static double Quantile(IReadOnlyList<double> sorted, double probability)
    {
        if (sorted.Count == 0) throw new ArgumentException("At least one value is required.", nameof(sorted));
        if (sorted.Count == 1) return sorted[0];
        var position = (sorted.Count - 1) * probability;
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);
        var fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }

    private static double Median(IEnumerable<double> values) => Quantile(values.OrderBy(value => value).ToArray(), 0.5);
    private static double? MedianNullable(IEnumerable<double?> values)
    {
        var present = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return present.Length == 0 ? null : Median(present);
    }
    private static double Overhead(double candidate, double baseline) => baseline == 0 ? double.NaN : 100d * ((candidate / baseline) - 1d);
    private static double? OverheadNullable(double? candidate, double? baseline) =>
        candidate.HasValue && baseline.HasValue ? Overhead(candidate.Value, baseline.Value) : null;
    private static double? DifferenceNullable(double? candidate, double? baseline) =>
        candidate.HasValue && baseline.HasValue ? candidate.Value - baseline.Value : null;

    private static string? FirstPresent(IReadOnlyDictionary<string, int> indices, params string[] names) =>
        names.FirstOrDefault(indices.ContainsKey);

    private static bool TryRead(IReadOnlyList<string> fields, int index, out string value)
    {
        if (index >= 0 && index < fields.Count)
        {
            value = fields[index];
            return true;
        }
        value = string.Empty;
        return false;
    }

    private static double? ReadOptionalMetric(IReadOnlyList<string> fields, int? index)
    {
        if (!index.HasValue || !TryRead(fields, index.Value, out var raw)
                            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                            || !double.IsFinite(value) || value < 0)
            return null;
        return value;
    }

    private static List<string> ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];
            if (character == '"')
            {
                if (quoted && index + 1 < line.Length && line[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else current.Append(character);
        }
        if (quoted) throw new InvalidDataException("CSV row has an unterminated quoted field.");
        result.Add(current.ToString());
        return result;
    }

    private sealed record RawFrame(
        double FrameTimeMilliseconds,
        double? Timestamp,
        double? CpuBusyMilliseconds,
        double? GpuBusyMilliseconds);
    private sealed record NormalizedFrame(
        double FrameTimeMilliseconds,
        double OffsetSeconds,
        double? CpuBusyMilliseconds,
        double? GpuBusyMilliseconds);
    private sealed record PhaseSummary(
        int FrameCount,
        double DurationSeconds,
        double AverageFps,
        double MeanMilliseconds,
        double MedianMilliseconds,
        double P95Milliseconds,
        double P99Milliseconds,
        double MaximumMilliseconds,
        IReadOnlyList<FrameTimeThreshold> Thresholds);
}
