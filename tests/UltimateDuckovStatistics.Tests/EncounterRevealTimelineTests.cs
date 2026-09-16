using UltimateDuckovStatistics.Encounters;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterRevealTimelineTests
{
    [Fact]
    public void StationaryTimeDoesNotStallRevealAndMarkersUseChronology()
    {
        var timeline = new EncounterRevealTimeline(new[] {
            new EncounterRevealSpan(0, 10, 10, false), new EncounterRevealSpan(10, 100, 0, false),
            new EncounterRevealSpan(100, 110, 10, false) });
        Assert.Equal(.5, timeline.MarkerProgress(50));
        Assert.Equal(.75, timeline.MarkerProgress(105));
        Assert.Equal(1, timeline.EdgeFraction(0, .5));
        Assert.Equal(0, timeline.EdgeFraction(2, .5));
    }

    [Fact]
    public void TeleportHasBriefWeightAndMissingTimeWaitsUntilFinish()
    {
        var timeline = new EncounterRevealTimeline(new[] {
            new EncounterRevealSpan(0, 10, 90, false), new EncounterRevealSpan(10, 11, 500, true),
            new EncounterRevealSpan(20, 30, 100, false) });
        Assert.Equal(.45, timeline.MarkerProgress(10));
        Assert.Equal(.5, timeline.MarkerProgress(11));
        Assert.Equal(1, timeline.MarkerProgress(15));
        Assert.Equal(1, timeline.MarkerProgress(40));
        Assert.Equal(.5, timeline.EdgeFraction(1, .475), 8);
    }

    [Fact]
    public void EmptyOrStationaryRouteStillRevealsMarkersAtEnd()
    {
        Assert.Equal(1, new EncounterRevealTimeline(Array.Empty<EncounterRevealSpan>()).MarkerProgress(1));
        var timeline = new EncounterRevealTimeline(new[] { new EncounterRevealSpan(0, 10, 0, false) });
        Assert.Equal(1, timeline.MarkerProgress(5));
        Assert.Equal(1, timeline.EdgeFraction(0, 1));
    }

    [Fact]
    public void OverlappingVisitsCannotBeSilentlyAnimatedAsOneWalk()
    {
        Assert.Throws<ArgumentException>(() => new EncounterRevealTimeline(new[] {
            new EncounterRevealSpan(0, 10, 10, false), new EncounterRevealSpan(8, 20, 20, false) }));
    }
}
