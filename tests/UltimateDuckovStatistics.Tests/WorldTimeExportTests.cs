using System.Globalization;
using System.Text.Json;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class WorldTimeExportTests
{
    [Fact]
    public void ProfileJsonCsvAndUiFormattingUseTheSameExactWorldTimeTotals()
    {
        var aggregate = new WorldTimeStatisticsAggregate
        {
            CalendarDaysAdvanced = 2,
            ObservedGameTimeTicks = TimeSpan.FromHours(5).Ticks,
            CompletedSleepSessions = 3,
            SleepAdvancedTimeTicks = TimeSpan.FromMinutes(90).Ticks,
            HistoricalUnavailable = true,
            HistoricalProvenance = "pre-M12 unavailable"
        };
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        var profile = new ProfileDocument
        {
            GenerationId = "generation-1",
            Slot = 1,
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = "generation-1",
                WorldTime = aggregate
            },
            Capabilities = WorldTimeNativeContractPolicy.ToRecords(aggregate.Capabilities, "test").ToList()
        };

        var bundle = StatisticsExporter.Create(profile, new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));
        using var json = JsonDocument.Parse(bundle.Json);
        var jsonWorldTime = json.RootElement.GetProperty("WorldTime");
        var csvLines = bundle.WorldTimeCsv.Trim().Split('\n');
        var csvHeaders = csvLines[0].TrimEnd('\r').Split(',');
        var csvValues = csvLines[1].TrimEnd('\r').Split(',');
        var csv = csvHeaders.Zip(csvValues).ToDictionary(pair => pair.First, pair => pair.Second);

        Assert.Equal(2, bundle.Document.WorldTime.CalendarDaysAdvanced);
        Assert.Equal(TimeSpan.FromHours(5).Ticks, bundle.Document.WorldTime.ObservedGameTimeTicks);
        Assert.Equal(2, jsonWorldTime.GetProperty("CalendarDaysAdvanced").GetInt64());
        Assert.Equal(TimeSpan.FromHours(5).Ticks, jsonWorldTime.GetProperty("ObservedGameTimeTicks").GetInt64());
        Assert.Equal(3, jsonWorldTime.GetProperty("CompletedSleepSessions").GetInt64());
        Assert.Equal(TimeSpan.FromMinutes(90).Ticks, jsonWorldTime.GetProperty("SleepAdvancedTimeTicks").GetInt64());
        Assert.True(jsonWorldTime.GetProperty("HistoricalUnavailable").GetBoolean());
        Assert.Equal("pre-M12 unavailable", jsonWorldTime.GetProperty("HistoricalProvenance").GetString());
        var jsonCapabilities = jsonWorldTime.GetProperty("Capabilities");
        Assert.Equal((int)AdapterCapabilityState.Supported,
            jsonCapabilities.GetProperty("CalendarDays").GetProperty("State").GetInt32());
        Assert.Equal((int)AdapterCapabilityState.Supported,
            jsonCapabilities.GetProperty("ObservedElapsed").GetProperty("State").GetInt32());
        Assert.Equal((int)AdapterCapabilityState.Supported,
            jsonCapabilities.GetProperty("CompletedSleepSessions").GetProperty("State").GetInt32());
        Assert.Equal((int)AdapterCapabilityState.Supported,
            jsonCapabilities.GetProperty("SleepAdvancedTime").GetProperty("State").GetInt32());
        Assert.Equal("2", csv["calendar_days_advanced"]);
        Assert.Equal(TimeSpan.FromHours(5).Ticks.ToString(CultureInfo.InvariantCulture), csv["observed_game_time_ticks"]);
        Assert.Equal("18000", csv["observed_game_time_seconds"]);
        Assert.Equal("3", csv["completed_sleep_sessions"]);
        Assert.Equal(TimeSpan.FromMinutes(90).Ticks.ToString(CultureInfo.InvariantCulture), csv["sleep_advanced_time_ticks"]);
        Assert.Equal("5400", csv["sleep_advanced_time_seconds"]);
        Assert.Equal(nameof(AdapterCapabilityState.Supported), csv["calendar_capability"]);
        Assert.Equal(nameof(AdapterCapabilityState.Supported), csv["observed_elapsed_capability"]);
        Assert.Equal(nameof(AdapterCapabilityState.Supported), csv["sleep_sessions_capability"]);
        Assert.Equal(nameof(AdapterCapabilityState.Supported), csv["sleep_time_capability"]);
        Assert.Equal("True", csv["historical_unavailable"]);
        Assert.Equal("pre-M12 unavailable", csv["historical_provenance"]);
        Assert.Equal("05:00:00", UiText.FormatWorldTimeDuration(
            bundle.Document.WorldTime.ObservedGameTimeTicks,
            bundle.Document.WorldTime.Capabilities.ObservedElapsed));
        Assert.Equal("3", UiText.FormatWorldTimeCount(
            bundle.Document.WorldTime.CompletedSleepSessions,
            bundle.Document.WorldTime.Capabilities.CompletedSleepSessions));
        Assert.Equal("2", UiText.FormatWorldTimeCount(
            bundle.Document.WorldTime.CalendarDaysAdvanced,
            bundle.Document.WorldTime.Capabilities.CalendarDays));
        Assert.Equal("01:30:00", UiText.FormatWorldTimeDuration(
            bundle.Document.WorldTime.SleepAdvancedTimeTicks,
            bundle.Document.WorldTime.Capabilities.SleepAdvancedTime));
        var unavailable = WorldTimeNativeContractPolicy.Unavailable("gap");
        Assert.Equal("2 (capture incomplete)", UiText.FormatWorldTimeCount(2, unavailable.CalendarDays));
        Assert.Equal("01:30:00 (capture incomplete)", UiText.FormatWorldTimeDuration(
            TimeSpan.FromMinutes(90).Ticks,
            unavailable.SleepAdvancedTime));
        Assert.Equal("Unsupported", UiText.FormatWorldTimeCount(0, unavailable.CompletedSleepSessions));
    }

    [Fact]
    public void FileExportIncludesDedicatedWorldTimeCsvAlongsideJson()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var profile = new ProfileDocument
        {
            GenerationId = "generation-1",
            Slot = 1,
            Statistics = new ProfileStatistics { SaveGenerationId = "generation-1" }
        };

        var result = ProfileExportWriter.Write(
            profile,
            Path.Combine(temporaryDirectory.Path, "profile.json"),
            new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc));

        Assert.Contains(result.Files, path => string.Equals(Path.GetFileName(path), "world_time.csv", StringComparison.Ordinal));
        Assert.Contains(result.Files, path => string.Equals(Path.GetExtension(path), ".json", StringComparison.Ordinal));
        Assert.All(result.Files, path => Assert.True(File.Exists(path)));
    }
}
