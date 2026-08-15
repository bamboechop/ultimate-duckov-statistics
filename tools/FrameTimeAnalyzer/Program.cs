using System.Globalization;
using System.Text.Json;
using FrameTimeAnalyzer;

var arguments = args.ToList();
if (arguments.Count == 0 || arguments.Contains("--help", StringComparer.OrdinalIgnoreCase))
{
    Console.WriteLine("Usage: dotnet run --project tools/FrameTimeAnalyzer -c Release -- <capture.csv|capture.capframex.json|directory> [...] [--baseline B] [--output-json path]");
    return;
}

string? baseline = null;
string? outputJson = null;
var inputs = new List<string>();
for (var index = 0; index < arguments.Count; index++)
{
    switch (arguments[index])
    {
        case "--baseline" when index + 1 < arguments.Count:
            baseline = arguments[++index];
            break;
        case "--output-json" when index + 1 < arguments.Count:
            outputJson = arguments[++index];
            break;
        default:
            inputs.Add(arguments[index]);
            break;
    }
}

var files = inputs.SelectMany(input => Directory.Exists(input)
        ? Directory.EnumerateFiles(input, "*.csv", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(input, "*.capframex.json", SearchOption.AllDirectories))
        : File.Exists(input) ? [input] : throw new FileNotFoundException("Capture input was not found.", input))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .GroupBy(CaptureIdentity, StringComparer.OrdinalIgnoreCase)
    .Select(group => group
        .OrderBy(path => path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
        .First())
    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (files.Length == 0) throw new InvalidOperationException("No raw CapFrameX CSV or JSON capture files were found.");

var report = FrameTimeCsvAnalyzer.BuildReport(files.Select(FrameTimeCsvAnalyzer.AnalyzeFile), baseline);
Console.WriteLine("CAPTURE\tCONFIG\tBUILD\tSCENARIO\tRUN\tFRAMES\tSECONDS\tAVG_FPS\tMEAN_MS\tMEDIAN_MS\tP95_MS\tP99_MS\tMAX_MS\tCPU_BUSY_MEDIAN_MS\tCPU_BUSY_P99_MS\tGPU_BUSY_MEDIAN_MS\tGPU_BUSY_P99_MS\tGT8.33_COUNT\tGT8.33_%\tGT16.7_COUNT\tGT16.7_%\tGT33.3_COUNT\tGT33.3_%\tCLUSTER\tACTION_CLUSTER\tACTION_FRAMES\tACTION_SECONDS\tACTION_AVG_FPS\tACTION_MEAN_MS\tACTION_MEDIAN_MS\tACTION_P95_MS\tACTION_P99_MS\tACTION_MAX_MS\tACTION_CPU_BUSY_MEDIAN_MS\tACTION_CPU_BUSY_P99_MS\tACTION_GPU_BUSY_MEDIAN_MS\tACTION_GPU_BUSY_P99_MS\tACTION_GT8.33_COUNT\tACTION_GT8.33_%\tACTION_GT16.7_COUNT\tACTION_GT16.7_%\tACTION_GT33.3_COUNT\tACTION_GT33.3_%");
foreach (var capture in report.Captures)
{
    Console.WriteLine(string.Join('\t',
        Path.GetFileName(capture.Source), capture.Configuration, capture.BuildLabel, capture.Scenario, capture.Run,
        capture.FrameCount, F(capture.DurationSeconds), F(capture.AverageFps), F(capture.MeanMilliseconds), F(capture.MedianMilliseconds),
        F(capture.P95Milliseconds), F(capture.P99Milliseconds), F(capture.MaximumMilliseconds),
        FN(capture.CpuBusy?.MedianMilliseconds), FN(capture.CpuBusy?.P99Milliseconds),
        FN(capture.GpuBusy?.MedianMilliseconds), FN(capture.GpuBusy?.P99Milliseconds),
        ThresholdCount(capture, 8.33), F(Threshold(capture, 8.33)),
        ThresholdCount(capture, 16.7), F(Threshold(capture, 16.7)),
        ThresholdCount(capture, 33.3), F(Threshold(capture, 33.3)),
        capture.HasSpikeCluster ? "yes" : "no", capture.HasActionSpikeCluster ? "yes" : "no",
        capture.ActionFrameCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
        FN(capture.ActionDurationSeconds), FN(capture.ActionAverageFps), FN(capture.ActionMeanMilliseconds),
        FN(capture.ActionMedianMilliseconds),
        FN(capture.ActionP95Milliseconds), FN(capture.ActionP99Milliseconds), FN(capture.ActionMaximumMilliseconds),
        FN(capture.ActionCpuBusy?.MedianMilliseconds), FN(capture.ActionCpuBusy?.P99Milliseconds),
        FN(capture.ActionGpuBusy?.MedianMilliseconds), FN(capture.ActionGpuBusy?.P99Milliseconds),
        ActionThresholdCount(capture, 8.33), FN(ActionThreshold(capture, 8.33)),
        ActionThresholdCount(capture, 16.7), FN(ActionThreshold(capture, 16.7)),
        ActionThresholdCount(capture, 33.3), FN(ActionThreshold(capture, 33.3))));
    foreach (var warning in capture.Warnings) Console.WriteLine($"WARNING\t{Path.GetFileName(capture.Source)}\t{warning}");
    foreach (var spike in capture.SpikesOver33Milliseconds)
        Console.WriteLine(
            $"SPIKE\t{Path.GetFileName(capture.Source)}\t{F(spike.OffsetSeconds)}s\t{F(spike.FrameTimeMilliseconds)}ms" +
            $"\tcpu={FN(spike.CpuBusyMilliseconds)}ms\tgpu={FN(spike.GpuBusyMilliseconds)}ms\taction={spike.DuringAction}");
    foreach (var window in capture.Windows)
        Console.WriteLine(string.Join('\t', "WINDOW", Path.GetFileName(capture.Source), window.Name,
            F(window.StartSeconds), F(window.EndSeconds), window.FrameCount, F(window.DurationSeconds),
            F(window.AverageFps), F(window.MeanMilliseconds), F(window.MedianMilliseconds),
            F(window.P95Milliseconds), F(window.P99Milliseconds), F(window.MaximumMilliseconds),
            FN(window.CpuBusy?.MedianMilliseconds), FN(window.CpuBusy?.P99Milliseconds),
            FN(window.GpuBusy?.MedianMilliseconds), FN(window.GpuBusy?.P99Milliseconds),
            WindowThresholdCount(window, 16.7), F(WindowThreshold(window, 16.7)),
            WindowThresholdCount(window, 33.3), F(WindowThreshold(window, 33.3)),
            window.HasSpikeCluster ? "yes" : "no"));
}

Console.WriteLine("AGGREGATE\tCONFIG\tBUILD\tSCENARIO\tRUNS\tMEDIAN_OF_MEDIANS_MS\tMEDIAN_OF_P99_MS\tMEDIAN_GT16.7_%\tCLUSTER_CAPTURES\tACTION_CLUSTER_CAPTURES\tMEDIAN_CPU_BUSY_MEDIAN_MS\tMEDIAN_CPU_BUSY_P99_MS\tMEDIAN_GPU_BUSY_MEDIAN_MS\tMEDIAN_GPU_BUSY_P99_MS\tMEDIAN_ACTION_MEDIAN_MS\tMEDIAN_ACTION_P99_MS\tMEDIAN_ACTION_GT16.7_%\tMEDIAN_ACTION_CPU_BUSY_MEDIAN_MS\tMEDIAN_ACTION_CPU_BUSY_P99_MS\tMEDIAN_ACTION_GPU_BUSY_MEDIAN_MS\tMEDIAN_ACTION_GPU_BUSY_P99_MS");
foreach (var aggregate in report.Aggregates)
    Console.WriteLine(string.Join('\t', "AGGREGATE", aggregate.Configuration, aggregate.BuildLabel, aggregate.Scenario, aggregate.RunCount,
        F(aggregate.MedianOfRunMediansMilliseconds), F(aggregate.MedianOfRunP99Milliseconds),
        F(aggregate.MedianFramesOver16MillisecondsPercentage), aggregate.CapturesWithSpikeCluster,
        aggregate.CapturesWithActionSpikeCluster,
        FN(aggregate.MedianOfRunCpuBusyMediansMilliseconds), FN(aggregate.MedianOfRunCpuBusyP99Milliseconds),
        FN(aggregate.MedianOfRunGpuBusyMediansMilliseconds), FN(aggregate.MedianOfRunGpuBusyP99Milliseconds),
        FN(aggregate.MedianOfRunActionMediansMilliseconds),
        FN(aggregate.MedianOfRunActionP99Milliseconds), FN(aggregate.MedianActionFramesOver16MillisecondsPercentage),
        FN(aggregate.MedianOfRunActionCpuBusyMediansMilliseconds), FN(aggregate.MedianOfRunActionCpuBusyP99Milliseconds),
        FN(aggregate.MedianOfRunActionGpuBusyMediansMilliseconds), FN(aggregate.MedianOfRunActionGpuBusyP99Milliseconds)));

if (report.Comparisons.Count > 0)
{
    Console.WriteLine("COMPARISON\tBASELINE\tBASELINE_BUILD\tCANDIDATE\tCANDIDATE_BUILD\tSCENARIO\tMEDIAN_OVERHEAD_%\tP99_OVERHEAD_%\tGT16.7_PP_DELTA\tCPU_BUSY_MEDIAN_OVERHEAD_%\tCPU_BUSY_P99_OVERHEAD_%\tGPU_BUSY_MEDIAN_OVERHEAD_%\tGPU_BUSY_P99_OVERHEAD_%\tACTION_MEDIAN_OVERHEAD_%\tACTION_P99_OVERHEAD_%\tACTION_GT16.7_PP_DELTA\tACTION_CPU_BUSY_MEDIAN_OVERHEAD_%\tACTION_CPU_BUSY_P99_OVERHEAD_%\tACTION_GPU_BUSY_MEDIAN_OVERHEAD_%\tACTION_GPU_BUSY_P99_OVERHEAD_%\tBASE_ACTION_CLUSTERS\tCANDIDATE_ACTION_CLUSTERS\tREPEATABLE_NEW_ACTION_CLUSTER");
    foreach (var comparison in report.Comparisons)
        Console.WriteLine(string.Join('\t', "COMPARISON", comparison.BaselineConfiguration, comparison.BaselineBuildLabel,
            comparison.CandidateConfiguration, comparison.CandidateBuildLabel, comparison.Scenario, F(comparison.MedianOverheadPercentage),
            F(comparison.P99OverheadPercentage), F(comparison.FramesOver16MillisecondsPercentagePointDelta),
            FN(comparison.CpuBusyMedianOverheadPercentage), FN(comparison.CpuBusyP99OverheadPercentage),
            FN(comparison.GpuBusyMedianOverheadPercentage), FN(comparison.GpuBusyP99OverheadPercentage),
            FN(comparison.ActionMedianOverheadPercentage), FN(comparison.ActionP99OverheadPercentage),
            FN(comparison.ActionFramesOver16MillisecondsPercentagePointDelta),
            FN(comparison.ActionCpuBusyMedianOverheadPercentage), FN(comparison.ActionCpuBusyP99OverheadPercentage),
            FN(comparison.ActionGpuBusyMedianOverheadPercentage), FN(comparison.ActionGpuBusyP99OverheadPercentage),
            comparison.BaselineActionSpikeClusterCaptures, comparison.CandidateActionSpikeClusterCaptures,
            comparison.HasRepeatableNewActionSpikeCluster ? "yes" : "no"));
}

if (!string.IsNullOrWhiteSpace(outputJson))
{
    var fullPath = Path.GetFullPath(outputJson);
    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
    File.WriteAllText(fullPath, JsonSerializer.Serialize(report));
    Console.WriteLine($"JSON\t{fullPath}");
}

static double Threshold(CaptureSummary capture, double milliseconds) =>
    capture.Thresholds.Single(value => value.Milliseconds == milliseconds).Percentage;
static int ThresholdCount(CaptureSummary capture, double milliseconds) =>
    capture.Thresholds.Single(value => value.Milliseconds == milliseconds).Count;
static double WindowThreshold(CaptureWindowSummary window, double milliseconds) =>
    window.Thresholds.Single(value => value.Milliseconds == milliseconds).Percentage;
static int WindowThresholdCount(CaptureWindowSummary window, double milliseconds) =>
    window.Thresholds.Single(value => value.Milliseconds == milliseconds).Count;
static double? ActionThreshold(CaptureSummary capture, double milliseconds) =>
    capture.ActionThresholds?.Single(value => value.Milliseconds == milliseconds).Percentage;
static string ActionThresholdCount(CaptureSummary capture, double milliseconds) =>
    capture.ActionThresholds == null
        ? string.Empty
        : capture.ActionThresholds.Single(value => value.Milliseconds == milliseconds).Count.ToString(CultureInfo.InvariantCulture);
static string F(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
static string FN(double? value) => value.HasValue ? F(value.Value) : string.Empty;
static string CaptureIdentity(string path) => path.EndsWith(".capframex.json", StringComparison.OrdinalIgnoreCase)
    ? path[..^".capframex.json".Length]
    : Path.ChangeExtension(path, null);
