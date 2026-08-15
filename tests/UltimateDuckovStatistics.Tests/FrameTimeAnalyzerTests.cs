using FrameTimeAnalyzer;

namespace UltimateDuckovStatistics.Tests;

public sealed class FrameTimeAnalyzerTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public void RawFramesUseDominantSwapChainAndReportRequestedDistribution()
    {
        var summary = FrameTimeCsvAnalyzer.AnalyzeLines(
            "pilot.csv",
            [
                "Application,SwapChainAddress,Dropped,TimeInSeconds,msBetweenPresents,MsCPUBusy,MsGPUBusy",
                "Duckov.exe,0xA,0,100.000,5,4,3",
                "Duckov.exe,0xA,0,100.005,10,8,6",
                "Duckov.exe,0xA,0,100.015,20,16,12",
                "Duckov.exe,0xA,0,100.035,40,32,24",
                "Duckov.exe,0xB,0,100.040,999,999,999",
                "Duckov.exe,0xA,1,100.045,999,999,999"
            ],
            new CaptureMetadata
            {
                Configuration = "C",
                Scenario = "high-rate-empty",
                Run = 1,
                ActionStartSeconds = 0.01,
                ActionEndSeconds = 0.04
            });

        Assert.Equal(4, summary.FrameCount);
        Assert.Equal(1, summary.IgnoredNonDominantRows);
        Assert.Equal(15, summary.MedianMilliseconds, 6);
        Assert.Equal(40, summary.MaximumMilliseconds, 6);
        Assert.Equal(50, summary.Thresholds.Single(value => value.Milliseconds == 16.7).Percentage, 6);
        Assert.Equal(12, summary.CpuBusy!.MedianMilliseconds, 6);
        Assert.Equal(31.52, summary.CpuBusy.P99Milliseconds, 6);
        Assert.Equal(9, summary.GpuBusy!.MedianMilliseconds, 6);
        var spike = Assert.Single(summary.SpikesOver33Milliseconds);
        Assert.True(spike.DuringAction);
        Assert.Equal(32, spike.CpuBusyMilliseconds!.Value, 6);
        Assert.Equal(24, spike.GpuBusyMilliseconds!.Value, 6);
        Assert.Equal(2, summary.ActionFrameCount);
        Assert.Equal(0.06, summary.ActionDurationSeconds!.Value, 6);
        Assert.Equal(30, summary.ActionMeanMilliseconds!.Value, 6);
        Assert.Equal(30, summary.ActionMedianMilliseconds!.Value, 6);
        Assert.Equal(39.8, summary.ActionP99Milliseconds!.Value, 6);
        Assert.Equal(24, summary.ActionCpuBusy!.MedianMilliseconds, 6);
        Assert.Equal(18, summary.ActionGpuBusy!.MedianMilliseconds, 6);
        Assert.Equal(100, summary.ActionThresholds!.Single(value => value.Milliseconds == 16.7).Percentage, 6);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void SpikeClusterRequiresTwoFramesWithinRollingSecond()
    {
        var clustered = FrameTimeCsvAnalyzer.AnalyzeLines(
            "cluster.csv",
            [
                "SwapChainAddress,TimeInSeconds,msBetweenPresents",
                "0xA,0,40",
                "0xA,0.5,35",
                "0xA,2,40"
            ]);
        var isolated = FrameTimeCsvAnalyzer.AnalyzeLines(
            "isolated.csv",
            [
                "SwapChainAddress,TimeInSeconds,msBetweenPresents",
                "0xA,0,40",
                "0xA,1.1,35"
            ]);

        Assert.True(clustered.HasSpikeCluster);
        Assert.False(isolated.HasSpikeCluster);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void QpcMillisecondOffsetsAreConvertedBeforeActionCorrelation()
    {
        var summary = FrameTimeCsvAnalyzer.AnalyzeLines(
            "qpc.csv",
            [
                "SwapChainAddress,CPUStartQPC,msBetweenPresents",
                "0xA,1000000,10",
                "0xA,1000500,40"
            ],
            new CaptureMetadata { ActionStartSeconds = 0.4, ActionEndSeconds = 0.6 });

        var spike = Assert.Single(summary.SpikesOver33Milliseconds);
        Assert.True(spike.DuringAction);
        Assert.Null(spike.CpuBusyMilliseconds);
        Assert.Null(spike.GpuBusyMilliseconds);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AggregationUsesMedianOfIndependentRunsAndMatchedControl()
    {
        var captures = new[]
        {
            Summary("B", 1, 10, 20, 1), Summary("B", 2, 12, 22, 2), Summary("B", 3, 100, 200, 30),
            Summary("D", 1, 11, 21, 1.1, actionCluster: true),
            Summary("D", 2, 13.2, 24.2, 2.2, actionCluster: true),
            Summary("D", 3, 14, 25, 3),
            Summary("C", 1, 999, 999, 99, "diagnostic")
        };

        var report = FrameTimeCsvAnalyzer.BuildReport(captures, "B");
        var baseline = report.Aggregates.Single(value => value.Configuration == "B");
        var candidate = report.Aggregates.Single(value => value.Configuration == "D");
        var comparison = Assert.Single(report.Comparisons);

        Assert.Equal(12, baseline.MedianOfRunMediansMilliseconds, 6);
        Assert.Equal(13.2, candidate.MedianOfRunMediansMilliseconds, 6);
        Assert.Equal(10, comparison.MedianOverheadPercentage, 6);
        Assert.Equal(10, comparison.P99OverheadPercentage, 6);
        Assert.Equal(0.2, comparison.FramesOver16MillisecondsPercentagePointDelta, 6);
        Assert.Equal(10, comparison.CpuBusyMedianOverheadPercentage!.Value, 6);
        Assert.Equal(10, comparison.CpuBusyP99OverheadPercentage!.Value, 6);
        Assert.Equal(10, comparison.ActionMedianOverheadPercentage!.Value, 6);
        Assert.Equal(10, comparison.ActionP99OverheadPercentage!.Value, 6);
        Assert.Equal(0.2, comparison.ActionFramesOver16MillisecondsPercentagePointDelta!.Value, 6);
        Assert.True(comparison.HasRepeatableNewActionSpikeCluster);
        Assert.Equal(0, comparison.BaselineActionSpikeClusterCaptures);
        Assert.Equal(2, comparison.CandidateActionSpikeClusterCaptures);
        Assert.DoesNotContain(report.Comparisons, value => value.CandidateConfiguration == "C");
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void UnsupportedCsvFailsInsteadOfInventingFrameTimes()
    {
        var exception = Assert.Throws<InvalidDataException>(() => FrameTimeCsvAnalyzer.AnalyzeLines(
            "overlay.csv", ["Application,FPS", "Duckov.exe,120"]));
        Assert.Contains("raw frame-time column", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CapFrameXMetadataPreambleIsSkippedBeforeRawHeader()
    {
        var summary = FrameTimeCsvAnalyzer.AnalyzeLines(
            "capframex.csv",
            [
                "//Ignore=true",
                "\uFEFFApplication,SwapChainAddress,TimeInSeconds,MsBetweenPresents",
                "Duckov.exe,0xA,100.000,5",
                "Duckov.exe,0xA,100.005,10"
            ]);

        Assert.Equal(2, summary.FrameCount);
        Assert.Equal("msBetweenPresents", summary.FrameTimeColumn);
        Assert.Equal(7.5, summary.MedianMilliseconds, 6);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CapFrameXJsonRawArraysUseTheSameDistributionAndMetricRules()
    {
        var summary = FrameTimeCsvAnalyzer.AnalyzeCapFrameXJson(
            "soak.capframex.json",
            """
            {
              "Runs": [{
                "CaptureData": {
                  "TimeInSeconds": [0, 0.005, 0.015, 0.035, 0.075],
                  "MsBetweenPresents": [5, 10, 20, 40, 999],
                  "Dropped": [false, false, false, false, true],
                  "CpuActive": [4, 8, 16, 32, 999],
                  "GpuActive": [3, 6, 12, 24, 999]
                }
              }]
            }
            """,
            new CaptureMetadata
            {
                Configuration = "D",
                Scenario = "full-run-soak",
                Run = 1,
                ActionStartSeconds = 0.01,
                ActionEndSeconds = 0.04,
                Windows =
                [
                    new CaptureWindowMetadata { Name = "early", StartSeconds = 0, EndSeconds = 0.02 },
                    new CaptureWindowMetadata { Name = "late", StartSeconds = 0.02, EndSeconds = 0.08 }
                ]
            });

        Assert.Equal(4, summary.FrameCount);
        Assert.Equal("capframex-json", summary.DominantSwapChain);
        Assert.Equal(15, summary.MedianMilliseconds, 6);
        Assert.Equal(40, summary.MaximumMilliseconds, 6);
        Assert.Equal(12, summary.CpuBusy!.MedianMilliseconds, 6);
        Assert.Equal(9, summary.GpuBusy!.MedianMilliseconds, 6);
        Assert.True(Assert.Single(summary.SpikesOver33Milliseconds).DuringAction);
        Assert.Equal(2, summary.ActionFrameCount);
        Assert.Equal(2, summary.Windows.Count);
        Assert.Equal(10, summary.Windows[0].MedianMilliseconds, 6);
        Assert.Equal(40, summary.Windows[1].MedianMilliseconds, 6);
        Assert.False(summary.Windows[1].HasSpikeCluster);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CapFrameXJsonRejectsMismatchedRawArrays()
    {
        var exception = Assert.Throws<InvalidDataException>(() =>
            FrameTimeCsvAnalyzer.AnalyzeCapFrameXJson(
                "broken.capframex.json",
                """
                {
                  "Runs": [{
                    "CaptureData": {
                      "TimeInSeconds": [0],
                      "MsBetweenPresents": [5, 10]
                    }
                  }]
                }
                """));

        Assert.Contains("length mismatch", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void CapFrameXQpcMillisecondsDriveActionSpikeOffsets()
    {
        var summary = FrameTimeCsvAnalyzer.AnalyzeLines(
            "capframex-qpc.csv",
            [
                "TimeInSeconds,CPUStartQPCTimeInMs,MsBetweenPresents",
                "1107112.1365,0,4.2",
                "1118553.2944,11440.6777,4.4",
                "1119382.8472,11445.1165,829.5528"
            ],
            new CaptureMetadata { ActionStartSeconds = 11, ActionEndSeconds = 12 });

        var spike = Assert.Single(summary.SpikesOver33Milliseconds);
        Assert.Equal(11.4451165, spike.OffsetSeconds, 6);
        Assert.True(spike.DuringAction);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void MillisecondScaledTimeInSecondsIsDetectedWhenQpcColumnIsUnavailable()
    {
        var summary = FrameTimeCsvAnalyzer.AnalyzeLines(
            "capframex-time.csv",
            [
                "TimeInSeconds,MsBetweenPresents",
                "100000,4",
                "100004,4",
                "100044,40"
            ],
            new CaptureMetadata { ActionStartSeconds = 0.04, ActionEndSeconds = 0.05 });

        var spike = Assert.Single(summary.SpikesOver33Milliseconds);
        Assert.Equal(0.044, spike.OffsetSeconds, 6);
        Assert.True(spike.DuringAction);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void AnalyzeFileUsesObservedFractionalSignalOffsetsFromSchemaFiveSidecar()
    {
        using var directory = new TemporaryDirectory();
        var capturePath = Path.Combine(directory.Path, "observed-offsets.csv");
        File.WriteAllLines(
            capturePath,
            [
                "TimeInSeconds,MsBetweenPresents",
                "1000.000,4",
                "1000.150,40",
                "1000.350,50",
                "1000.600,4"
            ]);
        File.WriteAllText(
            Path.ChangeExtension(capturePath, ".capture.json"),
            """
            {
              "SchemaVersion": 5,
              "RequestedActionStartSeconds": 0.1,
              "RequestedActionEndSeconds": 0.2,
              "ActionStartSeconds": 0.304,
              "ActionEndSeconds": 0.573
            }
            """);

        var summary = FrameTimeCsvAnalyzer.AnalyzeFile(capturePath);

        Assert.Equal(1, summary.ActionFrameCount);
        Assert.Equal(50, summary.ActionMaximumMilliseconds!.Value, 6);
        Assert.False(summary.SpikesOver33Milliseconds.Single(value => value.OffsetSeconds < 0.2).DuringAction);
        Assert.True(summary.SpikesOver33Milliseconds.Single(value => value.OffsetSeconds > 0.3).DuringAction);
    }

    private static CaptureSummary Summary(
        string configuration,
        int run,
        double median,
        double p99,
        double over16,
        string buildLabel = "production",
        bool actionCluster = false) => new()
        {
            Source = $"{configuration}-{run}.csv",
            Configuration = configuration,
            BuildLabel = buildLabel,
            Scenario = "same",
            Run = run,
            FrameTimeColumn = "msBetweenPresents",
            DominantSwapChain = "0xA",
            ParsedRows = 1,
            FrameCount = 1,
            IgnoredNonDominantRows = 0,
            DurationSeconds = 1,
            AverageFps = 100,
            MeanMilliseconds = median,
            MedianMilliseconds = median,
            P95Milliseconds = p99,
            P99Milliseconds = p99,
            MaximumMilliseconds = p99,
            Thresholds =
        [
            new FrameTimeThreshold { Milliseconds = 8.33, Count = 1, Percentage = 100 },
            new FrameTimeThreshold { Milliseconds = 16.7, Count = 1, Percentage = over16 },
            new FrameTimeThreshold { Milliseconds = 33.3, Count = 0, Percentage = 0 }
        ],
            CpuBusy = Distribution(median, p99),
            GpuBusy = Distribution(median, p99),
            ActionFrameCount = 1,
            ActionDurationSeconds = median / 1000d,
            ActionAverageFps = 1000d / median,
            ActionMeanMilliseconds = median,
            ActionMedianMilliseconds = median,
            ActionP95Milliseconds = p99,
            ActionP99Milliseconds = p99,
            ActionMaximumMilliseconds = p99,
            ActionThresholds =
        [
            new FrameTimeThreshold { Milliseconds = 8.33, Count = 1, Percentage = 100 },
            new FrameTimeThreshold { Milliseconds = 16.7, Count = 1, Percentage = over16 },
            new FrameTimeThreshold { Milliseconds = 33.3, Count = 0, Percentage = 0 }
        ],
            ActionCpuBusy = Distribution(median, p99),
            ActionGpuBusy = Distribution(median, p99),
            SpikesOver33Milliseconds = [],
            Windows = [],
            HasSpikeCluster = false,
            HasActionSpikeCluster = actionCluster,
            Warnings = []
        };

    private static MetricDistribution Distribution(double median, double p99) => new()
    {
        SampleCount = 1,
        MeanMilliseconds = median,
        MedianMilliseconds = median,
        P95Milliseconds = p99,
        P99Milliseconds = p99,
        MaximumMilliseconds = p99
    };
}
