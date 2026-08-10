using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class RunStatisticsViewModelTests
{
    private static readonly DateTime TestTime = new(2026, 8, 10, 14, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ExpectedRunOrder = { "later", "earlier" };

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "UI")]
    public void UiModelUsesTheSameTotalsRecordsMapsAndSummariesAsPersistence()
    {
        var profile = Profile();
        RunReducer.Apply(profile.Statistics, Run("later", RunOutcome.Extracted, 80, TestTime.AddMinutes(2)));
        RunReducer.Apply(profile.Statistics, Run("earlier", RunOutcome.Died, 120, TestTime));
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = RunStatisticsViewModelFactory.MovementAdapterId,
            State = AdapterCapabilityState.Supported,
            Version = "native-main-duck-movement/2.3.30"
        });

        var model = RunStatisticsViewModelFactory.Create(profile);

        Assert.Equal(profile.Statistics.RunTotals.TotalRuns, model.TotalRuns);
        Assert.Equal(1, model.ExtractedRuns);
        Assert.Equal(1, model.DiedRuns);
        Assert.Equal(0, model.InterruptedRuns);
        Assert.Equal(profile.Statistics.RunTotals.PhysicalDistance, model.PhysicalDistance);
        Assert.Equal(profile.Statistics.RunTotals.TeleportDistance, model.TeleportDistance);
        Assert.True(model.MovementSupported);
        Assert.Equal(ExpectedRunOrder, model.Runs.Select(run => run.RunId));
        Assert.Same(profile.Statistics.RunRecords, model.Records);
        Assert.Equal(profile.Statistics.RunTotals.Maps.Keys.OrderBy(value => value), model.Maps.Select(map => map.MapId));
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "UI")]
    public void UiModelExposesUnsupportedMovementInsteadOfFabricatingSupport()
    {
        var profile = Profile();
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = RunStatisticsViewModelFactory.MovementAdapterId,
            State = AdapterCapabilityState.DisabledIncompatible,
            Version = "native-main-duck-movement/2.3.30",
            Detail = "Required member unavailable."
        });

        Assert.False(RunStatisticsViewModelFactory.Create(profile).MovementSupported);
    }

    private static ProfileDocument Profile() => new()
    {
        GenerationId = "generation-a",
        Slot = 1,
        CreatedUtc = TestTime,
        UpdatedUtc = TestTime,
        Identity = new SaveIdentitySnapshot { Slot = 1 },
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = "generation-a",
            CreatedUtc = TestTime,
            UpdatedUtc = TestTime
        }
    };

    private static RunSummary Run(string id, RunOutcome outcome, double duration, DateTime started) => new()
    {
        RunId = id,
        SaveGenerationId = "generation-a",
        MapId = "duckov:map:warehouse",
        MapDisplayName = "Warehouse",
        MapKnown = true,
        StartedUtc = started,
        EndedUtc = started.AddSeconds(duration),
        ActiveDurationSeconds = duration,
        WallClockDurationSeconds = duration,
        Outcome = outcome,
        PhysicalDistance = duration / 2,
        TeleportDistance = 3,
        IntegrityTags = IntegrityTags.Normal,
        RecordEligible = true,
        LifecycleCapability = AdapterCapabilityState.Supported,
        MovementCapability = AdapterCapabilityState.Supported,
        MapCapability = AdapterCapabilityState.Supported
    };
}
