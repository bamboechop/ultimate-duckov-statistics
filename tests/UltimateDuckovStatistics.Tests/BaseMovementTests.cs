using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class BaseMovementTests
{
    private static readonly DateTime Started = new(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void CadencedPublicationRetriesCumulativeDistanceWithoutDuplicatesAndStationaryWrites()
    {
        using var directory = new TemporaryDirectory();
        var repository = Open(directory.Path);
        var calls = 0;
        var reject = false;
        var capture = new BaseMovementCapture(update =>
        {
            calls++;
            repository.RecordBaseMovementDeferred(update);
            return !reject; // An uncertain acknowledgement after applying must also be safe.
        });
        capture.Bind(repository.CurrentGenerationId);
        capture.Observe(new Position3D(0, 0, 0), 0, 12, Started);
        Assert.True(capture.Publish(0));
        Assert.Equal(0, repository.Current.Statistics.BaseMovement!.RecordedMeters);
        reject = true;
        capture.Observe(new Position3D(1, 0, 0), .25, 12, Started);
        capture.Publish(.25);
        Assert.Equal(1, calls);
        Assert.False(capture.Publish(1));
        Assert.Equal(1, repository.Current.Statistics.BaseMovement.RecordedMeters);
        Assert.Throws<InvalidOperationException>(() => capture.Bind("other"));
        reject = false;
        Assert.True(capture.Publish(2));
        Assert.Equal(1, repository.Current.Statistics.BaseMovement.RecordedMeters);
        var revision = repository.Current.Revision;
        for (var i = 0; i < 100; i++)
        {
            capture.Observe(new Position3D(1, 0, 0), 3 + i * .25, 12, Started);
            capture.Publish(3 + i * .25);
        }
        Assert.Equal(3, calls);
        Assert.Equal(revision, repository.Current.Revision);
        Assert.Empty(repository.Current.Statistics.Runs);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.PhysicalDistance);
        repository.CloseClean();
    }

    [Theory]
    [InlineData("explicit")]
    [InlineData("invalid")]
    [InlineData("speed")]
    [InlineData("teleport")]
    [InlineData("gap")]
    public void DiscontinuitiesCannotBecomeBaseDistance(string boundary)
    {
        BaseMovementUpdate? published = null;
        var capture = new BaseMovementCapture(update => { published = update; return true; });
        capture.Bind("generation");
        capture.Observe(new Position3D(0, 0, 0), 0, 12, Started);
        capture.Observe(new Position3D(1, 0, 0), .25, 12, Started);
        double next = .5;
        switch (boundary)
        {
            case "explicit": capture.ResetBaseline(); break;
            case "invalid": capture.Observe(new Position3D(double.NaN, 0, 0), .4, 12, Started); break;
            case "speed": capture.Observe(new Position3D(1, 0, 0), .4, double.NaN, Started); break;
            case "gap": next = 10; break;
        }
        capture.Observe(new Position3D(1000, 0, 0), next, 12, Started);
        capture.Observe(new Position3D(1000, 3, 4), next + .5, 12, Started);
        capture.Publish(next + .5, force: true);
        Assert.Equal(6, published!.CapturedMeters);
        Assert.Equal(boundary != "explicit", published.HasKnownGaps);
        Assert.Equal(Started, published.CollectionStartedUtc);
    }

    [Fact]
    public void MissingAndObservedZeroRemainDistinctAcrossCurrentFormatReopen()
    {
        using var directory = new TemporaryDirectory();
        var repository = Open(directory.Path);
        var generation = repository.CurrentGenerationId;
        Assert.Null(repository.Current.Statistics.BaseMovement);
        repository.CloseClean();
        repository = Open(directory.Path);
        Assert.Equal(generation, repository.CurrentGenerationId);
        Assert.Null(repository.Current.Statistics.BaseMovement);
        repository.RecordBaseMovementDeferred(Update(generation, 0));
        repository.CloseClean();
        repository = Open(directory.Path);
        Assert.Equal(generation, repository.CurrentGenerationId);
        Assert.Equal(0, repository.Current.Statistics.BaseMovement!.RecordedMeters);
        Assert.Equal(Started, repository.Current.Statistics.BaseMovement.CollectionStartedUtc);
        Assert.False(repository.Current.Statistics.BaseMovement.HasKnownGaps);
        repository.CloseClean();
    }

    [Fact]
    public void SnapshotIsDetachedAndCrashRecoveryPreservesDistanceWithGapEvidence()
    {
        using var directory = new TemporaryDirectory();
        var repository = Open(directory.Path);
        repository.RecordBaseMovementDeferred(Update(repository.CurrentGenerationId, 2));
        var snapshot = repository.CapturePersistenceSnapshot();
        repository.RecordBaseMovementDeferred(Update(repository.CurrentGenerationId, 5));
        repository.SaveSnapshot(snapshot);
        var loaded = new AtomicJsonStore<ProfileDocument>().Load(repository.CurrentProfilePath!).Value!;
        Assert.Equal(2, loaded.Statistics.BaseMovement!.RecordedMeters);
        var recovered = Open(directory.Path); // Deliberately leave the session marker open.
        Assert.Equal(2, recovered.Current.Statistics.BaseMovement!.RecordedMeters);
        Assert.True(recovered.Current.Statistics.BaseMovement.HasKnownGaps);
        recovered.CloseClean();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidBaseDistanceRecoversTheValidCurrentFormatBackup(double invalid)
    {
        using var directory = new TemporaryDirectory();
        var repository = Open(directory.Path);
        repository.RecordBaseMovementDeferred(Update(repository.CurrentGenerationId, 12));
        var path = repository.CurrentProfilePath!;
        repository.CloseClean();
        var store = new AtomicJsonStore<ProfileDocument>();
        var profile = store.Load(path).Value!;
        profile.Statistics.BaseMovement!.RecordedMeters = invalid;
        store.Save(path, profile);
        repository = Open(directory.Path);
        Assert.Equal(12, repository.Current.Statistics.BaseMovement!.RecordedMeters);
        repository.CloseClean();
    }

    [Fact]
    public void ForeignGenerationAndBackwardsCaptureAreRejectedWithoutMutation()
    {
        using var directory = new TemporaryDirectory();
        var repository = Open(directory.Path);
        repository.RecordBaseMovementDeferred(Update(repository.CurrentGenerationId, 10));
        var revision = repository.Current.Revision;
        Assert.Throws<ArgumentException>(() => repository.RecordBaseMovementDeferred(Update("foreign", 100)));
        Assert.Throws<ArgumentException>(() => repository.RecordBaseMovementDeferred(Update(repository.CurrentGenerationId, 9)));
        Assert.Equal(10, repository.Current.Statistics.BaseMovement!.RecordedMeters);
        Assert.Equal(revision, repository.Current.Revision);
        repository.CloseClean();
    }

    [Fact]
    public void OverviewAndExportShareRecordedCombinedAndSeparateDistanceAndCoverage()
    {
        using var directory = new TemporaryDirectory();
        var repository = Open(directory.Path);
        var profile = repository.Current;
        profile.Statistics.RunTotals.PhysicalDistance = 25;
        profile.Capabilities.Add(new CapabilityRecord { AdapterId = "native-main-duck-movement", State = AdapterCapabilityState.Supported });
        repository.RecordBaseMovementDeferred(Update(repository.CurrentGenerationId, 4));
        var distance = DistanceStatisticsProjection.Create(profile);
        Assert.Equal(29, distance.CombinedMeters);
        Assert.Equal(25, distance.RaidMeters);
        Assert.Equal(4, distance.BaseMeters);
        Assert.False(distance.CombinedPartial);
        Assert.Equal(Started, distance.BaseCollectionStartedUtc);
        var rows = ProfileSummaryPresentationFactory.Create(new StatisticsPanelProjection { Profile = profile }, key => UiText.EnglishFallbacks[key]);
        Assert.Equal("29.00 m", rows.Single(r => r.Metric == ProfileSummaryMetric.TotalDistanceTravelled).Value);
        Assert.Contains("2026-09-10", rows.Single(r => r.Metric == ProfileSummaryMetric.BaseDistance).Tooltip);
        var export = StatisticsExporter.Create(profile, Started);
        Assert.Equal(distance.CombinedMeters, export.Document.Distance.CombinedMeters);
        Assert.Equal(distance.BaseMeters, export.Document.Distance.BaseMeters);
        Assert.Contains("base_distance_meters", export.OverviewCsv);
        Assert.Contains("total_recorded_distance_meters", export.OverviewCsv);
        profile.Statistics.BaseMovement!.HasKnownGaps = true;
        Assert.True(DistanceStatisticsProjection.Create(profile).CombinedPartial);
        profile.Statistics.BaseMovement = null;
        distance = DistanceStatisticsProjection.Create(profile);
        Assert.Null(distance.BaseMeters);
        Assert.Equal(25, distance.CombinedMeters);
        Assert.True(distance.CombinedPartial);
        repository.CloseClean();
    }

    private static ProfileRepository Open(string path)
    {
        var repository = new ProfileRepository(path, () => Started, () => Guid.NewGuid().ToString("N"));
        repository.Open(new SaveIdentitySnapshot { Slot = 1, GameVersion = "2.3.30" });
        return repository;
    }

    private static BaseMovementUpdate Update(string generation, double meters) => new()
    {
        GenerationId = generation,
        CaptureId = "capture",
        CapturedMeters = meters,
        CollectionStartedUtc = Started
    };
}
