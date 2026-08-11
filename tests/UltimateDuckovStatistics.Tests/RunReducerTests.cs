using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class RunReducerTests
{
    private static readonly DateTime Origin = new(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Run")]
    public void AggregatesOutcomesDistancesAndUnknownMapsWithoutDiscardingSummaries()
    {
        var profile = Profile();

        Assert.True(RunReducer.Apply(profile, Summary("extract", RunOutcome.Extracted, 10, "known", true, 12, 3)));
        Assert.True(RunReducer.Apply(profile, Summary("death", RunOutcome.Died, 20, MapIdentity.UnknownId, false, 4, 8)));
        Assert.True(RunReducer.Apply(profile, Summary("interrupt", RunOutcome.Interrupted, 30, "known", true, 2, 1)));
        Assert.False(RunReducer.Apply(profile, Summary("extract", RunOutcome.Extracted, 10, "known", true, 12, 3)));

        Assert.Equal(3, profile.Runs.Count);
        Assert.Equal(3, profile.RunTotals.TotalRuns);
        Assert.Equal(1, profile.RunTotals.Outcomes[nameof(RunOutcome.Extracted)]);
        Assert.Equal(1, profile.RunTotals.Outcomes[nameof(RunOutcome.Died)]);
        Assert.Equal(1, profile.RunTotals.Outcomes[nameof(RunOutcome.Interrupted)]);
        Assert.Equal(18, profile.RunTotals.PhysicalDistance);
        Assert.Equal(12, profile.RunTotals.TeleportDistance);
        Assert.Equal(2, profile.RunTotals.Maps.Count);
        Assert.False(profile.RunTotals.Maps[MapIdentity.UnknownId].IsKnown);
        Assert.Equal(3, profile.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(2, profile.RunTotals.Maps["known"].WeaponStatistics.Totals.FiringActions);
    }

    [Fact]
    [Trait("Category", "Run")]
    public void RecordsAreOverallAndPerMapWithDeterministicEarliestRunTieBreak()
    {
        var profile = Profile();
        var laterTie = Summary("z-run", RunOutcome.Extracted, 10, "map-a", true);
        laterTie.StartedUtc = Origin.AddMinutes(1);
        laterTie.EndedUtc = laterTie.StartedUtc.AddSeconds(10);
        var earlierTie = Summary("a-run", RunOutcome.Extracted, 10, "map-a", true);
        var fastest = Summary("fast", RunOutcome.Extracted, 5, "map-b", true);
        var slowest = Summary("slow", RunOutcome.Extracted, 20, "map-b", true);
        var death = Summary("death", RunOutcome.Died, 7, "map-a", true);

        foreach (var summary in new[] { laterTie, earlierTie, fastest, slowest, death })
        {
            RunReducer.Apply(profile, summary);
        }

        Assert.Equal("fast", profile.RunRecords.Extraction.Shortest!.RunId);
        Assert.Equal("slow", profile.RunRecords.Extraction.Longest!.RunId);
        Assert.Equal("a-run", profile.RunRecords.Maps["map-a"].Extraction.Shortest!.RunId);
        Assert.Equal("a-run", profile.RunRecords.Maps["map-a"].Extraction.Longest!.RunId);
        Assert.Equal("death", profile.RunRecords.Death.Shortest!.RunId);
        Assert.Equal("death", profile.RunRecords.Death.Longest!.RunId);
    }

    [Fact]
    [Trait("Category", "Run")]
    public void InterruptedAndIntegrityFlaggedRunsNeverEnterDurationRecords()
    {
        var profile = Profile();
        var interrupted = Summary("interrupt", RunOutcome.Interrupted, 1, "map", true);
        interrupted.RecordEligible = false;
        var flagged = Summary("flagged", RunOutcome.Extracted, 1, "map", true);
        flagged.RecordEligible = false;
        flagged.IntegrityTags = IntegrityTags.ModdedContent;

        RunReducer.Apply(profile, interrupted);
        RunReducer.Apply(profile, flagged);

        Assert.Equal(2, profile.Runs.Count);
        Assert.Null(profile.RunRecords.Extraction.Shortest);
        Assert.Empty(profile.RunRecords.Maps);
    }

    private static ProfileStatistics Profile() => new()
    {
        SaveGenerationId = "generation-1",
        CreatedUtc = Origin,
        UpdatedUtc = Origin
    };

    private static RunSummary Summary(
        string runId,
        RunOutcome outcome,
        double duration,
        string mapId,
        bool mapKnown,
        double physical = 0,
        double teleport = 0) => new()
        {
            RunId = runId,
            SaveGenerationId = "generation-1",
            MapId = mapId,
            MapDisplayName = mapKnown ? $"Map {mapId}" : MapIdentity.UnknownDisplayName,
            MapKnown = mapKnown,
            StartedUtc = Origin,
            EndedUtc = Origin.AddSeconds(duration),
            ActiveDurationSeconds = duration,
            WallClockDurationSeconds = duration,
            Outcome = outcome,
            PhysicalDistance = physical,
            TeleportDistance = teleport,
            IntegrityTags = IntegrityTags.Normal,
            RecordEligible = outcome != RunOutcome.Interrupted,
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported,
            MapCapability = AdapterCapabilityState.Supported,
            WeaponStatistics = CombatStatistics(runId)
        };

    private static WeaponStatisticsAggregate CombatStatistics(string runId)
    {
        var statistics = new WeaponStatisticsAggregate();
        WeaponStatisticsReducer.Apply(statistics, new ShotRecorded
        {
            EventId = $"shot-{runId}",
            SaveGenerationId = "generation-1",
            RunId = runId,
            MapId = "map",
            GameplayContext = GameplayContext.Raid,
            WeaponId = "weapon",
            WeaponDisplayName = "Weapon",
            AmmunitionId = "ammo",
            AmmunitionDisplayName = "Ammo",
            FiringActionCount = 1,
            AmmunitionUnitsConsumed = 1,
            ProjectileCount = 1,
            Capabilities = new WeaponMetricCapabilities
            {
                FiringActions = Supported(),
                AmmunitionConsumption = Supported(),
                Projectiles = Supported(),
                WeaponIdentity = Supported(),
                AmmunitionIdentity = Supported()
            }
        });
        return statistics;
    }

    private static MetricAvailability Supported() => new()
    {
        State = AdapterCapabilityState.Supported,
        Provenance = "test"
    };
}
