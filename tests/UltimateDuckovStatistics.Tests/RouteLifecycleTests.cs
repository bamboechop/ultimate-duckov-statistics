using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class RouteLifecycleTests
{
    private static long economySequence;
    private static readonly DateTime Now = new(2026, 8, 13, 10, 0, 0, DateTimeKind.Utc);
    private static readonly string[] TwoMapIds = ["duckov:map:A", "duckov:map:B"];
    private static readonly string[] ThreeMapIds = ["duckov:map:A", "duckov:map:B", "duckov:map:C"];
    private static readonly string[] FourSegmentRepeatedMapIds =
        ["duckov:map:A", "duckov:map:B", "duckov:map:A", "duckov:map:C"];

    [Fact]
    [Trait("Category", "M8")]
    public void SingleMapRunCreatesOneSupportedClosedSegment()
    {
        var tracker = Start("A");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;

        Assert.Equal("duckov:map:A", summary.StartingMapId);
        Assert.Equal("duckov:map:A", summary.EndingMapId);
        Assert.Equal("duckov:map:A", summary.RouteSignature);
        var segment = Assert.Single(summary.Segments);
        Assert.Equal(MapSegmentExitReason.Extracted, segment.ExitReason);
        Assert.Equal(10, segment.ActiveDurationSeconds);
        Assert.Equal(summary.ActiveDurationSeconds, summary.Segments.Sum(value => value.ActiveDurationSeconds));
        Assert.True(UiText.HasAvailableEventAttribution(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void OnlyNativeStableMapIdentityCanCreateAPersistableMapKey()
    {
        Assert.False(MapIdentity.TryFromNativeStableId(null, "guessed-scene-name", false, out var unavailable));
        Assert.Equal(MapIdentity.UnknownId, unavailable.MapId);
        Assert.Equal(MapIdentity.UnknownDisplayName, unavailable.DisplayName);
        Assert.False(unavailable.IsKnown);

        Assert.True(MapIdentity.TryFromNativeStableId("Level_HiddenWarehouse", "Lagerbereich", true, out var proven));
        Assert.Equal("duckov:map:Level_HiddenWarehouse", proven.MapId);
        Assert.Equal("Lagerbereich", proven.DisplayName);
        Assert.True(proven.IsKnown);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void RepeatedMapRouteRetainsThreeOrderedVisitsAndExcludesLoadingTime()
    {
        var tracker = Start("A");
        Transition(tracker, 3, 5, "B");
        Transition(tracker, 8, 11, "A");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Died, 15)).Completed!;

        Assert.Equal("duckov:map:A>duckov:map:B>duckov:map:A", summary.RouteSignature);
        Assert.Collection(summary.Segments,
            value => { Assert.Equal("duckov:map:A", value.MapId); Assert.Equal(3, value.ActiveDurationSeconds); },
            value => { Assert.Equal("duckov:map:B", value.MapId); Assert.Equal(3, value.ActiveDurationSeconds); },
            value => { Assert.Equal("duckov:map:A", value.MapId); Assert.Equal(4, value.ActiveDurationSeconds); });
        Assert.Equal(10, summary.ActiveDurationSeconds);
        Assert.Equal(MapSegmentExitReason.Died, summary.Segments[^1].ExitReason);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void DistinctTwoAndThreeMapRoutesRetainEveryOrderedVisit()
    {
        var twoMap = Start("A");
        Transition(twoMap, 2, 4, "B");
        var twoMapSummary = twoMap.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        Assert.Equal("duckov:map:A>duckov:map:B", twoMapSummary.RouteSignature);
        Assert.Equal(TwoMapIds, twoMapSummary.Segments.Select(segment => segment.MapId));

        var threeMap = Start("A");
        Transition(threeMap, 2, 4, "B");
        Transition(threeMap, 6, 9, "C");
        var threeMapSummary = threeMap.Apply(Event(RunLifecycleEventKind.Extracted, 12)).Completed!;
        Assert.Equal("duckov:map:A>duckov:map:B>duckov:map:C", threeMapSummary.RouteSignature);
        Assert.Equal(
            ThreeMapIds,
            threeMapSummary.Segments.Select(segment => segment.MapId));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void DuplicateAndSameMapCallbacksDoNotCreateAVisitAndTransitionRaidEvidenceDoesNotInterrupt()
    {
        var tracker = Start("A", nativeRaidId: "1");
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 2));
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 2.1));
        var raidEvidence = tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 2.2, nativeRaidId: "2"));
        Assert.Null(raidEvidence.Completed);
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, 4, map: Map("A")));
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, 4.1, map: Map("A")));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        Assert.Single(summary.Segments);
        Assert.Equal(4, summary.ActiveDurationSeconds);

        var next = Start("A", nativeRaidId: "8");
        var genuine = next.Apply(Event(RunLifecycleEventKind.RaidInitialized, 1, nativeRaidId: "9"));
        Assert.Equal(RunOutcome.Interrupted, genuine.Completed?.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "M8")]
    public void FullSceneAndSubsceneTransitionSignalsProduceOneContinuousTwoMapRun(bool subscenePath)
    {
        var tracker = Start("A", nativeRaidId: "41");
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 2));
        if (subscenePath)
        {
            tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 2.1));
        }
        else
        {
            var raidEvidence = tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 2.1, nativeRaidId: "41"));
            Assert.Null(raidEvidence.Completed);
        }
        tracker.Apply(Event(RunLifecycleEventKind.LoadingEnded, 3.9));
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, 4, map: Map("B")));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 7)).Completed!;

        Assert.Equal("duckov:map:A>duckov:map:B", summary.RouteSignature);
        Assert.Equal(2, summary.Segments.Count);
        Assert.Equal(5, summary.ActiveDurationSeconds);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void TransitionDisplacementIsNeitherPhysicalNorTeleportAndComposesFromSegments()
    {
        var tracker = Start("A");
        tracker.ObserveMovement(new Position3D(0, 0, 0), 0, 10);
        tracker.ObserveMovement(new Position3D(1, 0, 0), 1, 10);
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 2));
        var excluded = tracker.ObserveMovement(new Position3D(101, 0, 0), 3, 10, MovementObservationKind.LoadingBoundary);
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, 5, map: Map("B")));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;

        Assert.Equal(MovementDisposition.TransitionExcluded, excluded.Disposition);
        Assert.Equal(1, summary.PhysicalDistance);
        Assert.Equal(0, summary.TeleportDistance);
        Assert.Equal(100, summary.TransitionExcludedDistance);
        Assert.Equal(summary.TransitionExcludedDistance, summary.Segments.Sum(value => value.TransitionExcludedDistance));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void ValidationRejectsRunDurationThatDoesNotComposeFromSegments()
    {
        var summary = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;
        summary.ActiveDurationSeconds++;

        var exception = Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
        Assert.Contains("segment composition", exception.Message);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "M8")]
    public void ValidationRejectsExplicitRouteEndpointsThatDoNotMatchOrderedSegments(bool corruptStart)
    {
        var tracker = Start("A");
        Transition(tracker, 2, 4, "B");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 7)).Completed!;
        if (corruptStart) summary.StartingMapId = "duckov:map:not-A";
        else summary.EndingMapId = "duckov:map:not-B";

        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void NormalizationMarksOnlyTheSegmentThatRequiredRepairAndIsIdempotent()
    {
        var summary = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;
        var clean = RouteStatisticsReducer.CloneSegment(summary.Segments[0]);
        clean.SegmentIndex = 1;
        clean.SegmentId = "run-1:segment:1";
        summary.Segments[0].ActiveDurationSeconds = double.NaN;
        summary.Segments.Add(clean);

        Assert.True(RouteStatisticsReducer.NormalizePersisted(summary.Segments));
        Assert.True(summary.Segments[0].WasRepairedFromInvalidState);
        Assert.False(summary.Segments[1].WasRepairedFromInvalidState);
        Assert.False(RouteStatisticsReducer.NormalizePersisted(summary.Segments));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void ValidationRejectsDuplicateSegmentIdentityAndUnjoinableEventAttribution()
    {
        var tracker = Start("A");
        Transition(tracker, 2, 4, "B");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        summary.Segments[1].SegmentId = summary.Segments[0].SegmentId;
        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));

        summary.Segments[1].SegmentId = "run-1:segment:1";
        summary.SegmentEventAssociations.Add(new SegmentEventAssociation
        {
            EventId = "broken",
            EventKind = "combat",
            SourceSegmentId = summary.Segments[0].SegmentId,
            SourceMapId = summary.Segments[1].MapId
        });
        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void ValidationRejectsDuplicateEventAssociationIdentity()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordItemUse(Item("duplicate", tracker, "A")));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        summary.SegmentEventAssociations.Add(RouteStatisticsReducer.CloneAssociation(summary.SegmentEventAssociations[0]));

        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void ValidationRejectsEventAssociationWithoutSourceOrOutcome()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordItemUse(Item("association", tracker, "A")));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        summary.SegmentEventAssociations[0].SourceSegmentId = string.Empty;
        summary.SegmentEventAssociations[0].SourceMapId = string.Empty;
        summary.SegmentEventAssociations[0].OutcomeSegmentId = string.Empty;
        summary.SegmentEventAssociations[0].OutcomeMapId = string.Empty;

        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    [Trait("Category", "M8")]
    public void ValidationRejectsOneSidedEventAssociation(bool missingSource)
    {
        var summary = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        var segment = summary.Segments[0];
        summary.SegmentEventAssociations.Add(new SegmentEventAssociation
        {
            EventId = "one-sided",
            EventKind = "combat",
            TimestampUtc = Now.AddSeconds(2),
            SourceSegmentId = missingSource ? string.Empty : segment.SegmentId,
            SourceMapId = missingSource ? MapIdentity.UnknownId : segment.MapId,
            OutcomeSegmentId = missingSource ? segment.SegmentId : string.Empty,
            OutcomeMapId = missingSource ? segment.MapId : MapIdentity.UnknownId
        });

        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void IncompleteLiveAssociationDisablesOnlyAttributionAndKeepsOverallCombat()
    {
        var tracker = Start("A");
        var outcomeSegment = tracker.ActiveSegmentId!;
        Assert.True(tracker.RecordCombat(Combat(
            "combat-incomplete-source",
            tracker,
            string.Empty,
            MapIdentity.UnknownId,
            outcomeSegment,
            "A")));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;

        Assert.Equal(9, summary.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(AdapterCapabilityState.Supported, summary.RouteCapabilities.Segments.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, summary.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, summary.RouteCapabilities.RouteAwareMapTotals.State);
        Assert.Empty(summary.SegmentEventAssociations);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void ValidationRejectsCompletedRunOccurrencesInsideSegmentEquipment()
    {
        var tracker = Start("A");
        Assert.True(tracker.ObserveEquipment(Snapshot(), Now, 0));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        summary.Segments[0].EquipmentStatistics.Loadouts["loadout-a"].RunOccurrences = 1;

        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void RunValidationStillRejectsMalformedRetainedSegmentsWhenRouteIsUnavailable()
    {
        var tracker = Start("A");
        tracker.DisableRoute("injected route failure");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        summary.Segments[0].ItemStatistics.Overall.ActivationCount = -1;

        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void RunValidationRejectsMismatchedStartingMapWhenRouteIsUnavailable()
    {
        var tracker = Start("A");
        tracker.DisableRoute("injected route failure");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        summary.StartingMapId = "duckov:map:not-A";

        Assert.Throws<ArgumentException>(() => RunReducer.Validate(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void EventTimeAndDelayedSourceOutcomeAttributionUseDestinationSegmentWithoutDoubleCounting()
    {
        var tracker = Start("A");
        var sourceSegment = tracker.ActiveSegmentId!;
        Assert.True(tracker.RecordItemUse(Item("use-a", tracker, "A")));
        Transition(tracker, 2, 4, "B");
        var outcomeSegment = tracker.ActiveSegmentId!;
        Assert.True(tracker.RecordShot(Shot("shot-b", tracker)));
        Assert.True(tracker.RecordHealing(Healing("heal-a", tracker, sourceSegment, "A", outcomeSegment, "B")));
        Assert.True(tracker.RecordCombat(Combat("combat-a", tracker, sourceSegment, "A", outcomeSegment, "B")));
        Assert.True(tracker.RecordContainer(Container("container-a", tracker, 42)));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 8)).Completed!;

        Assert.Equal(1, summary.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(7, summary.ItemStatistics.Overall.ActualHealthRestored);
        Assert.Equal(1, summary.Segments[0].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(0, summary.Segments[0].ItemStatistics.Overall.ActualHealthRestored);
        Assert.Equal(7, summary.Segments[1].ItemStatistics.Overall.ActualHealthRestored);
        Assert.Equal(9, summary.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(9, summary.Segments[1].CombatStatistics.Totals.DamageDealt);
        Assert.Equal(1, summary.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(
            summary.WeaponStatistics.Totals.FiringActions,
            summary.Segments.Sum(segment => segment.WeaponStatistics.Totals.FiringActions));
        Assert.Equal(
            summary.CombatStatistics.Totals.DamageDealt,
            summary.Segments.Sum(segment => segment.CombatStatistics.Totals.DamageDealt));
        Assert.Equal(
            summary.ItemStatistics.Overall.ActivationCount,
            summary.Segments.Sum(segment => segment.ItemStatistics.Overall.ActivationCount));
        Assert.Equal(
            summary.ItemStatistics.Overall.ActualHealthRestored,
            summary.Segments.Sum(segment => segment.ItemStatistics.Overall.ActualHealthRestored));
        Assert.Equal(1, summary.Segments[1].ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(
            summary.ContainerStatistics.UniqueContainersLooted,
            summary.Segments.Sum(segment => segment.ContainerStatistics.UniqueContainersLooted));
        Assert.Contains(summary.SegmentEventAssociations, value => value.SourceSegmentId == sourceSegment && value.OutcomeSegmentId == outcomeSegment);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void InterruptedTransitionCheckpointClosesLastVisitAsInterrupted()
    {
        var tracker = Start("A");
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 3));
        var checkpoint = tracker.CreateCheckpoint(Now.AddSeconds(5), 5)!;
        var summary = checkpoint.ToInterruptedSummary();

        Assert.True(checkpoint.TransitionPending);
        Assert.Equal(RunOutcome.Interrupted, summary.Outcome);
        Assert.Equal(MapSegmentExitReason.Interrupted, Assert.Single(summary.Segments).ExitReason);
        Assert.False(summary.RecordEligible);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void InterruptedRecoveryDoesNotPublishPartialRouteWhenCapabilityIsUnavailable()
    {
        var tracker = Start("A");
        Transition(tracker, 2, 4, "B");
        tracker.DisableRoute("injected route failure");
        var checkpoint = tracker.CreateCheckpoint(Now.AddSeconds(5), 5)!;

        var summary = checkpoint.ToInterruptedSummary();

        Assert.Equal(2, summary.Segments.Count);
        Assert.Equal(MapIdentity.UnknownId, summary.EndingMapId);
        Assert.Equal(MapIdentity.UnknownDisplayName, summary.EndingMapDisplayName);
        Assert.False(summary.EndingMapKnown);
        Assert.Empty(summary.RouteSignature);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, summary.RouteCapabilities.OrderedRoute.State);
        Assert.Equal("Route unavailable", UiText.FormatRoute(summary));
        Assert.False(UiText.HasAvailableSegments(summary));
        Assert.False(UiText.HasAvailableEventAttribution(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void InterruptionAfterDestinationResumeClosesTheDestinationSegment()
    {
        var tracker = Start("A");
        Transition(tracker, 2, 5, "B");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Interrupted, 8)).Completed!;

        Assert.Equal(2, summary.Segments.Count);
        Assert.Equal(MapSegmentExitReason.Transition, summary.Segments[0].ExitReason);
        Assert.Equal(MapSegmentExitReason.Interrupted, summary.Segments[1].ExitReason);
        Assert.Equal("duckov:map:B", summary.EndingMapId);
        Assert.False(summary.RecordEligible);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void DelayedOutcomeDuringLoadingPreservesOverallAndDisablesOnlyRouteAttribution()
    {
        var tracker = Start("A");
        var sourceSegment = tracker.ActiveSegmentId!;
        Assert.True(tracker.RecordItemUse(Item("use-a", tracker, "A")));
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 3));
        Assert.True(tracker.RecordHealing(Healing("heal-loading", tracker, sourceSegment, "A", string.Empty, "A")));
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, 5, map: Map("B")));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 7)).Completed!;

        Assert.Equal(7, summary.ItemStatistics.Overall.ActualHealthRestored);
        Assert.Equal(0, summary.Segments.Sum(segment => segment.ItemStatistics.Overall.ActualHealthRestored));
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, summary.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, summary.RouteCapabilities.RouteAwareMapTotals.State);
        Assert.Equal(AdapterCapabilityState.Supported, summary.RouteCapabilities.OrderedRoute.State);
        Assert.Equal(AdapterCapabilityState.Supported, summary.LifecycleCapability);
        Assert.True(UiText.HasAvailableSegments(summary));
        Assert.False(UiText.HasAvailableEventAttribution(summary));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void DefensiveSegmentBoundDisablesOnlyRouteAttribution()
    {
        var tracker = Start("A");
        for (var index = 1; index < RouteStatisticsReducer.MaximumSegmentsPerRun; index++)
            Transition(tracker, index * 2, (index * 2) + 1, index % 2 == 0 ? "A" : "B");
        Transition(tracker, 130, 131, "C");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 132)).Completed!;

        Assert.Equal(RouteStatisticsReducer.MaximumSegmentsPerRun, summary.Segments.Count);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, summary.RouteCapabilities.OrderedRoute.State);
        Assert.Equal(RunOutcome.Extracted, summary.Outcome);
        Assert.Equal(AdapterCapabilityState.Supported, summary.LifecycleCapability);
    }

    [Theory]
    [Trait("Category", "M10")]
    [InlineData(2048)]
    [InlineData(2049)]
    public void ExactAggregateAssociationHasNoEventCountCeiling(int eventCount)
    {
        var tracker = Start("A");
        for (var index = 0; index < eventCount; index++)
        {
            Assert.True(tracker.RecordItemUse(Item($"bounded-{index}", tracker, "A")));
        }
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;

        Assert.Equal(eventCount, summary.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(eventCount, summary.Segments[0].ItemStatistics.Overall.ActivationCount);
        var association = Assert.Single(summary.SegmentEventAssociations);
        Assert.Equal(SegmentEventAssociationRepresentation.ExactAggregate, association.Representation);
        Assert.Equal(eventCount, association.Count);
        Assert.Equal(AdapterCapabilityState.Supported, summary.RouteCapabilities.Segments.State);
        Assert.Equal(AdapterCapabilityState.Supported, summary.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported, summary.RouteCapabilities.RouteAwareMapTotals.State);
        Assert.Equal(AdapterCapabilityState.Supported, summary.RouteCapabilities.CurrentEventAttributionCapture.State);
    }

    [Fact]
    [Trait("Category", "M10")]
    public void OneHundredThousandMixedEventsRemainExactAcrossFourSegmentsAndRepeatedMaps()
    {
        const int eventCount = 100_000;
        var tracker = Start("A");
        for (var index = 0; index < eventCount; index++)
        {
            if (index == 25_000) Transition(tracker, 2, 3, "B");
            if (index == 50_000) Transition(tracker, 4, 5, "A");
            if (index == 75_000) Transition(tracker, 6, 7, "C");
            var map = tracker.ActiveMapId!["duckov:map:".Length..];
            var segment = tracker.ActiveSegmentId!;
            var accepted = (index % 4) switch
            {
                0 => tracker.RecordItemUse(Item($"stress:item:{index}", tracker, map)),
                1 => tracker.RecordShot(Shot($"stress:shot:{index}", tracker)),
                2 => tracker.RecordCombat(Combat($"stress:combat:{index}", tracker, segment, map, segment, map)),
                _ => tracker.RecordHealing(Healing($"stress:healing:{index}", tracker, segment, map, segment, map))
            };
            Assert.True(accepted);
        }

        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 9)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };
        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(eventCount, run.SegmentEventAssociations.Sum(value => value.Count));
        Assert.Equal(16, run.SegmentEventAssociations.Count);
        Assert.All(run.SegmentEventAssociations, value =>
            Assert.Equal(SegmentEventAssociationRepresentation.ExactAggregate, value.Representation));
        Assert.Equal(4, run.Segments.Count);
        Assert.Equal(FourSegmentRepeatedMapIds,
            run.Segments.Select(value => value.MapId));
        Assert.Equal(25_000, run.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(6250, run.Segments[3].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(25_000, run.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(225_000, run.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(175_000, run.ItemStatistics.Overall.ActualHealthRestored);
        Assert.Equal(12_500, profile.RunTotals.RouteMaps["duckov:map:A"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(6250, profile.RunTotals.RouteMaps["duckov:map:B"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(6250, profile.RunTotals.RouteMaps["duckov:map:C"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.EventAttribution.State);
        Assert.False(run.HistoricalEventAttributionIncomplete);
    }

    [Fact]
    [Trait("Category", "M10")]
    [Trait("Category", "Persistence")]
    public void SerializedCheckpointSizeIsBoundedByBreakdownCardinalityNotEventCount()
    {
        static ActiveRunCheckpoint CheckpointAfter(int eventCount)
        {
            var tracker = Start("A");
            for (var index = 0; index < eventCount; index++)
                Assert.True(tracker.RecordItemUse(Item($"size:{index:D8}", tracker, "A")));
            return tracker.CreateCheckpoint(Now.AddSeconds(3), 3)!;
        }

        var tenThousand = CheckpointAfter(10_000);
        var oneHundredThousand = CheckpointAfter(100_000);
        Assert.Equal(10_000, Assert.Single(tenThousand.SegmentEventAssociations).Count);
        Assert.Equal(100_000, Assert.Single(oneHundredThousand.SegmentEventAssociations).Count);
        using var directory = new TemporaryDirectory();
        var smallerPath = Path.Combine(directory.Path, "ten-thousand.json");
        var largerPath = Path.Combine(directory.Path, "one-hundred-thousand.json");
        var store = new AtomicJsonStore<ActiveRunCheckpoint>();
        store.Save(smallerPath, tenThousand);
        store.Save(largerPath, oneHundredThousand);

        var smallerBytes = new FileInfo(smallerPath).Length;
        var largerBytes = new FileInfo(largerPath).Length;
        Assert.InRange(largerBytes - smallerBytes, 0, 256);
        Assert.True(largerBytes < 200_000, $"Aggregate checkpoint unexpectedly grew to {largerBytes} bytes.");
    }

    [Fact]
    [Trait("Category", "M10")]
    [Trait("Category", "Export")]
    public void EveryLateAssociationFamilyReachesRunSegmentStartingMapRouteMapUiJsonAndCsv()
    {
        var tracker = Start("A");
        for (var index = 0; index < 2050; index++)
            Assert.True(tracker.RecordItemUse(Item($"prime:{index}", tracker, "A")));
        var sourceSegment = tracker.ActiveSegmentId!;
        Transition(tracker, 2, 4, "B");
        var outcomeSegment = tracker.ActiveSegmentId!;
        Assert.True(tracker.ObserveEquipment(Snapshot(), Now.AddSeconds(4), 4));
        Assert.True(tracker.RecordShot(Shot("late:shot", tracker)));
        Assert.True(tracker.RecordCombat(Combat("late:combat", tracker, sourceSegment, "A", outcomeSegment, "B")));
        Assert.True(tracker.RecordContainer(Container("late:container", tracker, 8128)));
        Assert.True(tracker.RecordItemUse(Item("late:item", tracker, "B")));
        Assert.True(tracker.RecordHealing(Healing("late:healing", tracker, sourceSegment, "A", outcomeSegment, "B")));
        var activeCheckpoint = tracker.CreateCheckpoint(Now.AddSeconds(7), 7)!;
        Assert.Equal(2055, activeCheckpoint.SegmentEventAssociations.Sum(value => value.Count));
        Assert.Equal(1, activeCheckpoint.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(9, activeCheckpoint.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(1, activeCheckpoint.ContainerState.Statistics.UniqueContainersLooted);
        Assert.Equal(2051, activeCheckpoint.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(7, activeCheckpoint.ItemStatistics.Overall.ActualHealthRestored);
        Assert.Contains("loadout-a", activeCheckpoint.EquipmentStatistics.Loadouts.Keys);
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 8)).Completed!;
        var profile = new ProfileDocument
        {
            GenerationId = "generation-1",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Statistics = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now }
        };
        Assert.True(RunReducer.Apply(profile.Statistics, run));
        var export = StatisticsExporter.Create(profile, Now.AddMinutes(1));

        Assert.Equal(2055, run.SegmentEventAssociations.Sum(value => value.Count));
        Assert.Contains(run.SegmentEventAssociations, value => value.EventKind == "shot" && value.Count == 1);
        Assert.Contains(run.SegmentEventAssociations, value => value.EventKind == "combat"
            && value.SourceSegmentId == sourceSegment && value.OutcomeSegmentId == outcomeSegment && value.Count == 1);
        Assert.Contains(run.SegmentEventAssociations, value => value.EventKind == "container" && value.Count == 1);
        Assert.Contains(run.SegmentEventAssociations, value => value.EventKind == "item-use"
            && value.OutcomeSegmentId == outcomeSegment && value.Count == 1);
        Assert.Contains(run.SegmentEventAssociations, value => value.EventKind == "healing"
            && value.SourceSegmentId == sourceSegment && value.OutcomeSegmentId == outcomeSegment && value.Count == 1);
        Assert.Equal(1, run.Segments[1].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(9, run.Segments[1].CombatStatistics.Totals.DamageDealt);
        Assert.Equal(1, run.Segments[1].ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(1, run.Segments[1].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(7, run.Segments[1].ItemStatistics.Overall.ActualHealthRestored);
        Assert.Contains("loadout-a", run.Segments[1].EquipmentStatistics.Loadouts.Keys);
        Assert.Equal(2051, profile.Statistics.RunTotals.Maps["duckov:map:A"].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(1, profile.Statistics.RunTotals.Maps["duckov:map:A"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(9, profile.Statistics.RunTotals.Maps["duckov:map:A"].CombatStatistics.Totals.DamageDealt);
        Assert.Equal(1, profile.Statistics.RunTotals.Maps["duckov:map:A"].ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(7, profile.Statistics.RunTotals.Maps["duckov:map:A"].ItemStatistics.Overall.ActualHealthRestored);
        Assert.Contains("loadout-a", profile.Statistics.RunTotals.Maps["duckov:map:A"].EquipmentStatistics.Loadouts.Keys);
        Assert.Equal(1, profile.Statistics.RunTotals.RouteMaps["duckov:map:B"].WeaponStatistics.Totals.FiringActions);
        Assert.Equal(9, profile.Statistics.RunTotals.RouteMaps["duckov:map:B"].CombatStatistics.Totals.DamageDealt);
        Assert.Equal(1, profile.Statistics.RunTotals.RouteMaps["duckov:map:B"].ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(1, profile.Statistics.RunTotals.RouteMaps["duckov:map:B"].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(7, profile.Statistics.RunTotals.RouteMaps["duckov:map:B"].ItemStatistics.Overall.ActualHealthRestored);
        Assert.Contains("loadout-a", profile.Statistics.RunTotals.RouteMaps["duckov:map:B"].EquipmentStatistics.Loadouts.Keys);
        Assert.True(UiText.HasAvailableEventAttribution(run));
        Assert.Contains("\"Representation\":1", export.Json);
        Assert.Contains("\"EventKind\":\"shot\"", export.Json);
        Assert.Contains("\"EventKind\":\"combat\"", export.Json);
        Assert.Contains("\"EventKind\":\"container\"", export.Json);
        Assert.Contains("\"EventKind\":\"item-use\"", export.Json);
        Assert.Contains("\"EventKind\":\"healing\"", export.Json);
        Assert.Contains(",2050,ExactAggregate,", export.SegmentEventsCsv);
        Assert.Contains(",combat,", export.SegmentEventsCsv);
        Assert.Contains(",healing,", export.SegmentEventsCsv);
        Assert.Contains(",2055,6", export.RoutesCsv);
        Assert.Contains(",Supported,false", export.SegmentsCsv);
        Assert.Contains("loadout-a", export.EquipmentTotalsCsv);
    }

    [Fact]
    [Trait("Category", "M10")]
    public void DuplicateDeliveryDoesNotIncreaseExactAssociationCount()
    {
        var tracker = Start("A");
        var value = Item("duplicate-m10", tracker, "A");
        Assert.True(tracker.RecordItemUse(value));
        Assert.False(tracker.RecordItemUse(value));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;

        var association = Assert.Single(run.SegmentEventAssociations);
        Assert.Equal(1, association.Count);
        Assert.Equal(1, run.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(1, run.Segments[0].ItemStatistics.Overall.ActivationCount);
    }

    [Fact]
    [Trait("Category", "M10")]
    public void MissingHistoricalJoinKeepsLaterExactCaptureAndPublishesKnownPartialRouteMapTotals()
    {
        var tracker = Start("A");
        var sourceSegment = tracker.ActiveSegmentId!;
        Assert.True(tracker.RecordItemUse(Item("use-a", tracker, "A")));
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 2));
        Assert.True(tracker.RecordHealing(Healing("unresolved-loading", tracker, sourceSegment, "A", string.Empty, "A")));
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, 4, map: Map("B")));
        Assert.True(tracker.RecordItemUse(Item("captured-after-gap", tracker, "B")));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };
        Assert.True(RunReducer.Apply(profile, run));

        Assert.True(run.HistoricalEventAttributionIncomplete);
        Assert.NotEmpty(run.HistoricalEventAttributionProvenance);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.CurrentEventAttributionCapture.State);
        var association = Assert.Single(run.SegmentEventAssociations, value =>
            value.OutcomeSegmentId == run.Segments[1].SegmentId);
        Assert.Equal("item-use", association.EventKind);
        Assert.Equal(1, run.Segments[1].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(1, profile.RunTotals.RouteMaps["duckov:map:B"].ItemStatistics.Overall.ActivationCount);
        Assert.True(profile.RunTotals.RouteMaps["duckov:map:B"].HistoricalUnavailable);
        Assert.True(UiText.HasKnownEventAttribution(run));
        Assert.False(UiText.HasAvailableEventAttribution(run));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void RouteAwareMapsUseSegmentsWhileStartingMapRecordsUseCompleteRun()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordItemUse(Item("use-a", tracker, "A")));
        Transition(tracker, 2, 5, "B");
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 9)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };
        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(1, profile.RunTotals.Maps["duckov:map:A"].TotalRuns);
        Assert.Equal(1, profile.RunTotals.Maps["duckov:map:A"].ItemStatistics.Overall.ActivationCount);
        Assert.False(profile.RunTotals.Maps.ContainsKey("duckov:map:B"));
        Assert.Equal(2, profile.RunTotals.RouteMaps["duckov:map:A"].ActiveDurationSeconds);
        Assert.Equal(4, profile.RunTotals.RouteMaps["duckov:map:B"].ActiveDurationSeconds);
        Assert.True(profile.RunRecords.Maps.ContainsKey("duckov:map:A"));
        Assert.False(profile.RunRecords.Maps.ContainsKey("duckov:map:B"));
        Assert.Equal(6, profile.RunRecords.Maps["duckov:map:A"].Extraction.Shortest!.ActiveDurationSeconds);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void EconomyFlowsComposeAcrossRunSegmentsStartingMapAndRouteMaps()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "money-a", tracker, "A", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 10)));
        Transition(tracker, 2, 5, "B");
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "cash-b", tracker, "B", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 3, acquisition: true)));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 9)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };

        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(10, run.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(3, run.Economy.Currencies["Cash"].Totals.GrossInflow);
        Assert.Equal(10, run.Segments[0].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.False(run.Segments[0].Economy.Currencies.ContainsKey("Cash"));
        Assert.Equal(3, run.Segments[1].Economy.Currencies["Cash"].Totals.GrossInflow);
        Assert.Equal(3, run.Economy.CashRaidOutcomes.Secured);
        Assert.True(run.Economy.CashTerminalDispositionRecorded);
        Assert.Equal(10, profile.RunTotals.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(3, profile.RunTotals.Economy.Currencies["Cash"].Totals.GrossInflow);
        Assert.True(profile.RunTotals.Economy.CashTerminalDispositionRecorded);
        Assert.Equal(10, profile.RunTotals.Maps["duckov:map:A"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(3, profile.RunTotals.Maps["duckov:map:A"].Economy.Currencies["Cash"].Totals.GrossInflow);
        Assert.Equal(10, profile.RunTotals.RouteMaps["duckov:map:A"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(3, profile.RunTotals.RouteMaps["duckov:map:B"].Economy.Currencies["Cash"].Totals.GrossInflow);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void ZeroFlowDegradedRunKeepsCompletedAndMapTotalsDegradedAfterLaterSupportedFlow()
    {
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };
        var degradedTracker = new RunLifecycleTracker(() => "run-degraded-zero");
        degradedTracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "raid-zero"));
        degradedTracker.Apply(Event(
            RunLifecycleEventKind.ControlReady,
            0,
            context: Context("A", "raid-zero")));
        var degradedCapabilities = SupportedEconomyCapabilities();
        degradedCapabilities.CashAmountDirection = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = "runtime Cash scan failed"
        };
        Assert.True(degradedTracker.UpdateEconomyCapabilities(degradedCapabilities));
        var degradedRun = degradedTracker.Apply(Event(RunLifecycleEventKind.Extracted, 1)).Completed!;
        Assert.True(RunReducer.Apply(profile, degradedRun));

        var supportedTracker = new RunLifecycleTracker(() => "run-supported-flow");
        supportedTracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 2, nativeRaidId: "raid-flow"));
        supportedTracker.Apply(Event(
            RunLifecycleEventKind.ControlReady,
            2,
            context: Context("A", "raid-flow")));
        var flow = Currency(
            "later-supported-flow",
            supportedTracker,
            "A",
            CurrencyKind.Cash,
            CurrencyFlowDirection.Inflow,
            9);
        flow.TimestampUtc = Now.AddSeconds(3);
        Assert.True(supportedTracker.RecordCurrencyFlow(flow));
        var supportedRun = supportedTracker.Apply(Event(RunLifecycleEventKind.Extracted, 4)).Completed!;
        Assert.True(RunReducer.Apply(profile, supportedRun));

        Assert.Equal(9, profile.RunTotals.Economy.Currencies["Cash"].Totals.GrossInflow);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            profile.RunTotals.Economy.Capabilities.CashAmountDirection.State);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            profile.RunTotals.Maps["duckov:map:A"].Economy.Capabilities.CashAmountDirection.State);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            profile.RunTotals.RouteMaps["duckov:map:A"].Economy.Capabilities.CashAmountDirection.State);
        RunReducer.ValidateProfileEconomyComposition(profile);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void DeferredCurrencyPublicationRetainsItsEventTimeSegmentAfterTransition()
    {
        var tracker = Start("A");
        var capturedInA = Currency(
            "money-delayed-a",
            tracker,
            "A",
            CurrencyKind.Money,
            CurrencyFlowDirection.Inflow,
            5);
        Transition(tracker, 2, 5, "B");

        Assert.True(tracker.RecordCurrencyFlow(capturedInA));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 7)).Completed!;

        Assert.Equal(5, run.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(5, run.Segments[0].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.False(run.Segments[1].Economy.Currencies.ContainsKey("Money"));
        Assert.Equal(AdapterCapabilityState.Supported, run.Economy.Capabilities.RouteAttribution.State);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void MissingRouteSegmentDegradesOnlyEconomyRouteAttribution()
    {
        var tracker = Start("A");
        for (var index = 1; index < RouteStatisticsReducer.MaximumSegmentsPerRun; index++)
            Transition(tracker, index * 2, (index * 2) + 1, index % 2 == 0 ? "A" : "B");
        Transition(tracker, 130, 131, "C");

        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "money-overall-only",
            tracker,
            "C",
            CurrencyKind.Money,
            CurrencyFlowDirection.Outflow,
            4)));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 132)).Completed!;

        Assert.Equal(4, run.Economy.Currencies["Money"].Totals.GrossOutflow);
        Assert.Equal(0, run.Segments.Sum(segment =>
            segment.Economy.Currencies.TryGetValue("Money", out var value) ? value.Totals.GrossOutflow : 0));
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.Economy.Capabilities.RouteAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.Economy.Capabilities.MoneyAmountDirection.State);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void RunArithmeticSaturationPersistsWithoutLegacyEconomyAssociationsOrDisablingCash()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "money-max", tracker, "A", CurrencyKind.Money, CurrencyFlowDirection.Inflow, long.MaxValue)));
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "money-overflow", tracker, "A", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "cash-after-money-saturation", tracker, "A", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 2)));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 4)).Completed!;

        Assert.Equal(long.MaxValue, run.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(long.MaxValue, run.Segments[0].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.True(run.Economy.MoneyArithmeticSaturated);
        Assert.True(run.Segments[0].Economy.MoneyArithmeticSaturated);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.Economy.Capabilities.CashAmountDirection.State);
        Assert.Equal(2, run.Economy.Currencies["Cash"].Totals.GrossInflow);
        Assert.Empty(run.SegmentEventAssociations);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void RunArithmeticOverflowDoesNotDiscardRepresentableSegmentOrRouteMapEconomy()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "money-max-a", tracker, "A", CurrencyKind.Money, CurrencyFlowDirection.Inflow, long.MaxValue)));
        Transition(tracker, 2, 3, "B");
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "money-out-b", tracker, "B", CurrencyKind.Money, CurrencyFlowDirection.Outflow, 1)));
        Assert.True(tracker.RecordCurrencyFlow(Currency(
            "money-in-b", tracker, "B", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 5)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };

        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(long.MaxValue, run.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(1, run.Economy.Currencies["Money"].Totals.GrossOutflow);
        Assert.True(run.Economy.MoneyArithmeticSaturated);
        Assert.Equal(long.MaxValue, run.Segments[0].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.False(run.Segments[0].Economy.MoneyArithmeticSaturated);
        Assert.Equal(AdapterCapabilityState.Supported, run.Segments[0].Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Equal(1, run.Segments[1].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(1, run.Segments[1].Economy.Currencies["Money"].Totals.GrossOutflow);
        Assert.False(run.Segments[1].Economy.MoneyArithmeticSaturated);
        Assert.Equal(AdapterCapabilityState.Supported, run.Segments[1].Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Empty(run.SegmentEventAssociations);
        Assert.Equal(long.MaxValue, profile.RunTotals.RouteMaps["duckov:map:A"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(1, profile.RunTotals.RouteMaps["duckov:map:B"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(1, profile.RunTotals.RouteMaps["duckov:map:B"].Economy.Currencies["Money"].Totals.GrossOutflow);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void EconomyFlowsDoNotConsumeTheLegacyAssociationBudgetOrDisableLaterItemAttribution()
    {
        var tracker = Start("A");
        for (var index = 0; index < 2500; index++)
            Assert.True(tracker.RecordCurrencyFlow(Currency(
                $"economy-a:{index}", tracker, "A", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));
        Transition(tracker, 2, 3, "B");
        for (var index = 0; index < 2500; index++)
            Assert.True(tracker.RecordCurrencyFlow(Currency(
                $"economy-b:{index}", tracker, "B", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));
        Assert.True(tracker.RecordItemUse(Item("item-after-5000-economy-flows", tracker, "B")));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 5)).Completed!;
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };
        Assert.True(RunReducer.Apply(profile, run));

        Assert.Equal(5000, run.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(AdapterCapabilityState.Supported, run.Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.Economy.Capabilities.RouteAttribution.State);
        Assert.Empty(run.Economy.RecentEventIds);
        Assert.False(run.Economy.DeduplicationSaturated);
        Assert.All(run.Segments, segment =>
        {
            Assert.Equal(2500, segment.Economy.Currencies["Money"].Totals.GrossInflow);
            Assert.Equal(AdapterCapabilityState.Supported, segment.Economy.Capabilities.MoneyAmountDirection.State);
            Assert.Empty(segment.Economy.RecentEventIds);
        });
        var itemAssociation = Assert.Single(run.SegmentEventAssociations);
        Assert.Empty(itemAssociation.EventId);
        Assert.Equal("item-use", itemAssociation.EventKind);
        Assert.Equal(SegmentEventAssociationRepresentation.ExactAggregate, itemAssociation.Representation);
        Assert.Equal(1, itemAssociation.Count);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.RouteAwareMapTotals.State);
        Assert.Equal(1, run.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(1, run.Segments[1].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(5000, profile.RunTotals.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(5000, profile.RunTotals.Maps["duckov:map:A"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(2500, profile.RunTotals.RouteMaps["duckov:map:A"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(2500, profile.RunTotals.RouteMaps["duckov:map:B"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(1, profile.RunTotals.RouteMaps["duckov:map:B"].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(AdapterCapabilityState.Supported,
            profile.RunTotals.RouteMaps["duckov:map:A"].Economy.Capabilities.RouteAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported,
            profile.RunTotals.RouteMaps["duckov:map:B"].Economy.Capabilities.RouteAttribution.State);
        Assert.Empty(profile.RunTotals.Economy.RecentEventIds);
        Assert.Empty(profile.RunTotals.Maps["duckov:map:A"].Economy.RecentEventIds);
        Assert.Empty(profile.RunTotals.RouteMaps["duckov:map:A"].Economy.RecentEventIds);
        Assert.Empty(profile.RunTotals.RouteMaps["duckov:map:B"].Economy.RecentEventIds);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Economy")]
    public void StartingAndRouteMapEconomyAggregatesContinueBeyondTheLegacyIdentityLimit()
    {
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };
        for (var index = 0; index < 2500; index++)
        {
            var tracker = new RunLifecycleTracker(() => $"run-{index}");
            tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, index * 3, nativeRaidId: $"raid-{index}"));
            tracker.Apply(Event(
                RunLifecycleEventKind.ControlReady,
                index * 3,
                context: Context("A", $"raid-{index}")));
            var flow = Currency(
                $"route-map-economy:{index}",
                tracker,
                "A",
                CurrencyKind.Money,
                CurrencyFlowDirection.Inflow,
                1);
            flow.TimestampUtc = Now.AddSeconds(index * 3 + 0.5);
            Assert.True(tracker.RecordCurrencyFlow(flow));
            var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, index * 3 + 1)).Completed!;
            Assert.True(RunReducer.Apply(profile, run));
        }

        Assert.Equal(2500, profile.RunTotals.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(2500, profile.RunTotals.Maps["duckov:map:A"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(2500, profile.RunTotals.RouteMaps["duckov:map:A"].Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(AdapterCapabilityState.Supported,
            profile.RunTotals.RouteMaps["duckov:map:A"].Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Empty(profile.RunTotals.RouteMaps["duckov:map:A"].Economy.RecentEventIds);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void SameContainerKeyOnDifferentMapsCountsOncePerMapAndTwiceOverall()
    {
        var tracker = Start("A");
        Assert.True(tracker.RecordContainer(Container("a", tracker, 42)));
        Assert.False(tracker.RecordContainer(Container("a-duplicate", tracker, 42)));
        Transition(tracker, 2, 4, "B");
        Assert.True(tracker.RecordContainer(Container("b", tracker, 42)));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;

        Assert.Equal(2, run.ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(1, run.Segments[0].ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(1, run.Segments[1].ContainerStatistics.UniqueContainersLooted);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void EquipmentDurationSplitsAtSegmentBoundary()
    {
        var tracker = Start("A");
        var snapshot = Snapshot();
        Assert.True(tracker.ObserveEquipment(snapshot, Now, 0));
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 3));
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, 5, map: Map("B")));
        Assert.True(tracker.ObserveEquipment(snapshot, Now.AddSeconds(5), 5));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 9)).Completed!;

        Assert.Equal(7, run.EquipmentStatistics.Loadouts["loadout-a"].ActiveDurationSeconds);
        Assert.Equal(3, run.Segments[0].EquipmentStatistics.Loadouts["loadout-a"].ActiveDurationSeconds);
        Assert.Equal(4, run.Segments[1].EquipmentStatistics.Loadouts["loadout-a"].ActiveDurationSeconds);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void RouteMapEquipmentOccurrencesCountEligibleRunsRatherThanSegments()
    {
        var completedTracker = Start("A");
        Assert.True(completedTracker.ObserveEquipment(Snapshot(), Now, 0));
        Transition(completedTracker, 3, 5, "B");
        Assert.True(completedTracker.ObserveEquipment(Snapshot(), Now.AddSeconds(5), 5));
        Transition(completedTracker, 7, 9, "A");
        Assert.True(completedTracker.ObserveEquipment(Snapshot(), Now.AddSeconds(9), 9));
        var completed = completedTracker.Apply(Event(RunLifecycleEventKind.Extracted, 12)).Completed!;
        var completedProfile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };

        Assert.True(RunReducer.Apply(completedProfile, completed));
        Assert.Equal(6, completedProfile.RunTotals.RouteMaps["duckov:map:A"].EquipmentStatistics.Loadouts["loadout-a"].ActiveDurationSeconds);
        Assert.Equal(1, completedProfile.RunTotals.RouteMaps["duckov:map:A"].EquipmentStatistics.Loadouts["loadout-a"].RunOccurrences);
        Assert.Equal(1, completedProfile.RunTotals.RouteMaps["duckov:map:B"].EquipmentStatistics.Loadouts["loadout-a"].RunOccurrences);

        var interruptedTracker = Start("A");
        Assert.True(interruptedTracker.ObserveEquipment(Snapshot(), Now, 0));
        Transition(interruptedTracker, 3, 5, "B");
        Assert.True(interruptedTracker.ObserveEquipment(Snapshot(), Now.AddSeconds(5), 5));
        var interrupted = interruptedTracker.Apply(Event(RunLifecycleEventKind.Interrupted, 7)).Completed!;
        var interruptedProfile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };

        Assert.True(RunReducer.Apply(interruptedProfile, interrupted));
        Assert.Equal(0, interruptedProfile.RunTotals.RouteMaps["duckov:map:A"].EquipmentStatistics.Loadouts["loadout-a"].RunOccurrences);
        Assert.Equal(0, interruptedProfile.RunTotals.RouteMaps["duckov:map:B"].EquipmentStatistics.Loadouts["loadout-a"].RunOccurrences);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void RunAndRouteMapMeasurementsSaturateWithoutBecomingInfinite()
    {
        var first = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        SetMaximumMeasurements(first);
        var second = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;
        second.RunId = "run-2";
        second.Segments[0].SegmentId = "run-2:segment:0";
        SetMaximumMeasurements(second);
        var profile = new ProfileStatistics { SaveGenerationId = "generation-1", CreatedUtc = Now, UpdatedUtc = Now };

        Assert.True(RunReducer.Apply(profile, first));
        Assert.True(RunReducer.Apply(profile, second));

        Assert.Equal(double.MaxValue, profile.RunTotals.PhysicalDistance);
        Assert.Equal(double.MaxValue, profile.RunTotals.TeleportDistance);
        Assert.Equal(double.MaxValue, profile.RunTotals.TransitionExcludedDistance);
        Assert.Equal(double.MaxValue, profile.RunTotals.Maps["duckov:map:A"].PhysicalDistance);
        Assert.Equal(double.MaxValue, profile.RunTotals.RouteMaps["duckov:map:A"].ActiveDurationSeconds);
        Assert.Equal(double.MaxValue, profile.RunTotals.RouteMaps["duckov:map:A"].TransitionExcludedDistance);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void SaturatedOverallMeasurementsStillComposeAcrossSeveralSegments()
    {
        var tracker = Start("A");
        Transition(tracker, 2, 4, "B");
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        foreach (var segment in summary.Segments)
        {
            segment.ActiveDurationSeconds = double.MaxValue;
            segment.PhysicalDistance = double.MaxValue;
            segment.TeleportDistance = double.MaxValue;
            segment.TransitionExcludedDistance = double.MaxValue;
        }
        summary.ActiveDurationSeconds = double.MaxValue;
        summary.PhysicalDistance = double.MaxValue;
        summary.TeleportDistance = double.MaxValue;
        summary.TransitionExcludedDistance = double.MaxValue;

        RunReducer.Validate(summary);
        Assert.Equal(double.MaxValue, RouteStatisticsReducer.SaturatingSum(summary.Segments.Select(x => x.ActiveDurationSeconds)));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void CheckpointRetainsMovementBaselineAndCompleteCurrentRouteContext()
    {
        var tracker = Start("A");
        tracker.ObserveMovement(new Position3D(7, 8, 9), 1, 10);
        var checkpoint = tracker.CreateCheckpoint(Now.AddSeconds(1), 1)!;

        Assert.True(checkpoint.MovementBaseline.HasBaseline);
        Assert.Equal(7, checkpoint.MovementBaseline.X);
        Assert.Equal(1, checkpoint.MovementBaseline.MonotonicSeconds);
        Assert.Equal(tracker.ActiveSegmentId, checkpoint.CurrentSegmentId);
        Assert.Single(checkpoint.Segments);
        Assert.Equal(AdapterCapabilityState.Supported, checkpoint.RouteCapabilities.Segments.State);
    }

    [Fact]
    [Trait("Category", "M8")]
    public void SchemaSevenMigrationPreservesLegacyMapAsStartingMapWithoutFabricatingRoute()
    {
        var document = new ProfileDocument
        {
            SchemaVersion = 7,
            GenerationId = "generation-1",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Statistics = new ProfileStatistics
            {
                SchemaVersion = 7,
                SaveGenerationId = "generation-1",
                CreatedUtc = Now,
                UpdatedUtc = Now,
                Runs = new List<RunSummary>
                {
                    new()
                    {
                        SchemaVersion = 7,
                        RunId = "legacy-run",
                        SaveGenerationId = "generation-1",
                        MapId = "duckov:map:A",
                        MapDisplayName = "A",
                        MapKnown = true,
                        StartedUtc = Now,
                        EndedUtc = Now.AddSeconds(10),
                        Outcome = RunOutcome.Extracted
                    }
                }
            }
        };

        Assert.True(ProfileMigrator.Migrate(document));
        var run = Assert.Single(document.Statistics.Runs);
        Assert.Equal(10, document.SchemaVersion);
        Assert.Equal("duckov:map:A", run.StartingMapId);
        Assert.Equal(MapIdentity.UnknownId, run.EndingMapId);
        Assert.Empty(run.Segments);
        Assert.Empty(run.RouteSignature);
        Assert.True(run.HistoricalRouteUnavailable);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.OrderedRoute.State);
        Assert.Equal("Route unavailable (pre-M8)", UiText.FormatRoute(run));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void CurrentSchemaSegmentRepairDisablesRouteAndPersistsIdempotentProvenance()
    {
        var run = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 5)).Completed!;
        run.Segments[0].ItemStatistics.Overall.ActivationCount = -1;
        var document = Document(run);

        Assert.True(ProfileMigrator.Migrate(document));
        Assert.True(run.RouteWasRepairedFromInvalidState);
        Assert.True(run.Segments[0].WasRepairedFromInvalidState);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.OrderedRoute.State);
        Assert.Empty(run.RouteSignature);
        Assert.Equal(MapIdentity.UnknownId, run.EndingMapId);
        Assert.Equal("Route unavailable", UiText.FormatRoute(run));
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void CurrentSchemaInvalidAssociationDisablesOnlyAttributionAndKeepsValidRoute()
    {
        var tracker = Start("A");
        Transition(tracker, 2, 4, "B");
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        run.SegmentEventAssociations.Add(new SegmentEventAssociation
        {
            EventId = "broken",
            EventKind = "combat",
            TimestampUtc = Now.AddSeconds(5),
            SourceSegmentId = run.Segments[0].SegmentId,
            SourceMapId = run.Segments[1].MapId
        });
        var document = Document(run);

        Assert.True(ProfileMigrator.Migrate(document));
        Assert.Empty(run.SegmentEventAssociations);
        Assert.True(run.RouteWasRepairedFromInvalidState);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.OrderedRoute.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.Segments.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.RouteAwareMapTotals.State);
        Assert.Equal("A → B", UiText.FormatRoute(run));
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Fact]
    [Trait("Category", "M10")]
    [Trait("Category", "Persistence")]
    public void UnsaturatedSchemaNineRawAssociationsMigrateWithoutLoss()
    {
        var run = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 5)).Completed!;
        run.SchemaVersion = 9;
        run.SegmentEventAssociations.Add(LegacyAssociation("legacy-one", run.Segments[0], Now.AddSeconds(1)));
        run.RouteCapabilities.CurrentEventAttributionCapture = null!;
        var document = Document(run);
        document.SchemaVersion = 9;
        document.Statistics.SchemaVersion = 9;

        Assert.True(ProfileMigrator.Migrate(document));
        Assert.Equal(10, document.SchemaVersion);
        var association = Assert.Single(run.SegmentEventAssociations);
        Assert.Equal("legacy-one", association.EventId);
        Assert.Equal(SegmentEventAssociationRepresentation.LegacyRaw, association.Representation);
        Assert.Equal(1, association.Count);
        Assert.Equal(association.TimestampUtc, association.FirstTimestampUtc);
        Assert.Equal(association.TimestampUtc, association.LastTimestampUtc);
        Assert.False(run.HistoricalEventAttributionIncomplete);
        Assert.Empty(run.HistoricalEventAttributionProvenance);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.CurrentEventAttributionCapture.State);
        RunReducer.Validate(run);
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Fact]
    [Trait("Category", "M10")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Export")]
    public void SaturatedSchemaNineHistoryKeepsExactRowsAndExplicitIncompleteProvenance()
    {
        var run = Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 5)).Completed!;
        run.SchemaVersion = 9;
        for (var index = 0; index < RouteStatisticsReducer.LegacyMaximumRawEventAssociationsPerRun; index++)
            run.SegmentEventAssociations.Add(LegacyAssociation($"legacy-{index}", run.Segments[0], Now.AddSeconds(1)));
        RouteStatisticsReducer.DisableAttribution(run.RouteCapabilities, "The defensive 2048-event association bound was reached.");
        run.RouteCapabilities.CurrentEventAttributionCapture = null!;
        var document = Document(run);
        document.SchemaVersion = 9;
        document.Statistics.SchemaVersion = 9;

        Assert.True(ProfileMigrator.Migrate(document));
        Assert.Equal(RouteStatisticsReducer.LegacyMaximumRawEventAssociationsPerRun, run.SegmentEventAssociations.Count);
        Assert.All(run.SegmentEventAssociations, association =>
        {
            Assert.Equal(SegmentEventAssociationRepresentation.LegacyRaw, association.Representation);
            Assert.Equal(1, association.Count);
        });
        Assert.True(run.HistoricalEventAttributionIncomplete);
        Assert.Contains("2,048", run.HistoricalEventAttributionProvenance, StringComparison.Ordinal);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.RouteAwareMapTotals.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.CurrentEventAttributionCapture.State);
        RunReducer.Validate(run);

        var export = StatisticsExporter.Create(document, Now.AddMinutes(1));
        Assert.Contains("\"HistoricalEventAttributionIncomplete\":true", export.Json);
        Assert.Contains("LegacyRaw", export.SegmentEventsCsv);
        Assert.Contains(",true,Supported", export.SegmentEventsCsv);
        Assert.Contains(",Supported,", export.RoutesCsv);
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void CurrentSchemaRunSegmentCompositionMismatchDisablesRouteWithoutRewritingOverallTotals()
    {
        var tracker = Start("A");
        Transition(tracker, 2, 4, "B");
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 6)).Completed!;
        run.ActiveDurationSeconds += 1;
        var retainedOverall = run.ActiveDurationSeconds;
        var document = Document(run);

        Assert.True(ProfileMigrator.Migrate(document));
        Assert.Equal(retainedOverall, run.ActiveDurationSeconds);
        Assert.Empty(run.Segments);
        Assert.Empty(run.RouteSignature);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.OrderedRoute.State);
        Assert.True(run.RouteWasRepairedFromInvalidState);
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void CurrentSchemaUnavailableRouteClearsMismatchedRetainedStartingMapIdempotently()
    {
        var tracker = Start("A");
        tracker.DisableRoute("injected route failure");
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 5)).Completed!;
        run.StartingMapId = "duckov:map:not-A";
        var document = Document(run);

        Assert.True(ProfileMigrator.Migrate(document));
        Assert.Empty(run.Segments);
        Assert.Empty(run.SegmentEventAssociations);
        Assert.Empty(run.RouteSignature);
        Assert.Equal(MapIdentity.UnknownId, run.EndingMapId);
        Assert.True(run.RouteWasRepairedFromInvalidState);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.OrderedRoute.State);
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void CurrentSchemaRouteMapRepairIsFiniteUnavailableAndIdempotent()
    {
        var document = Document(Start("A").Apply(Event(RunLifecycleEventKind.Extracted, 5)).Completed!);
        document.Statistics.RunTotals.RouteMaps["broken"] = new RouteAwareMapAggregate
        {
            MapId = string.Empty,
            DisplayName = string.Empty,
            RunsVisited = -1,
            SegmentVisits = -1,
            ActiveDurationSeconds = double.NaN,
            PhysicalDistance = -1,
            TeleportDistance = double.PositiveInfinity,
            TransitionExcludedDistance = -1
        };

        Assert.True(ProfileMigrator.Migrate(document));
        var map = document.Statistics.RunTotals.RouteMaps["broken"];
        Assert.Equal(MapIdentity.UnknownId, map.MapId);
        Assert.Equal(0, map.RunsVisited);
        Assert.Equal(0, map.SegmentVisits);
        Assert.Equal(0, map.ActiveDurationSeconds);
        Assert.Equal(0, map.PhysicalDistance);
        Assert.Equal(0, map.TeleportDistance);
        Assert.Equal(0, map.TransitionExcludedDistance);
        Assert.True(map.HistoricalUnavailable);
        Assert.True(map.WasRepairedFromInvalidState);
        Assert.False(ProfileMigrator.Migrate(document));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void JsonAndFlattenedRouteExportsUseStableJoinKeysAndHistoricalScopes()
    {
        var tracker = Start("A");
        Transition(tracker, 2, 4, "B");
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 7)).Completed!;
        var profile = new ProfileDocument
        {
            GenerationId = "generation-1",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = "generation-1",
                CreatedUtc = Now,
                UpdatedUtc = Now
            }
        };
        RunReducer.Apply(profile.Statistics, run);

        var export = StatisticsExporter.Create(profile, Now.AddMinutes(1));
        Assert.Contains("run-1,duckov:map:A,A,duckov:map:B,B,duckov:map:A>duckov:map:B,2", export.RoutesCsv);
        Assert.Contains("run-1,run-1:segment:0,0,duckov:map:A", export.SegmentsCsv);
        Assert.Contains("starting_map,duckov:map:A", export.MapTotalsCsv);
        Assert.Contains("duckov:map:B,B,true,1,1", export.RouteMapTotalsCsv);
        Assert.Contains("\"RouteSignature\":\"duckov:map:A>duckov:map:B\"", export.Json);
        Assert.Equal("A → B", UiText.FormatRoute(run));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void UnavailableRouteCapabilityPreservesOverallRunWithoutSyntheticSegment()
    {
        var tracker = new RunLifecycleTracker(() => "run-1");
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "1"));
        var context = Context("A", "1");
        context.RouteCapabilities = RouteStatisticsReducer.Unavailable("active map identity contract missing");
        tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 0, context: context));
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 3)).Completed!;

        Assert.Equal(RunOutcome.Extracted, run.Outcome);
        Assert.Empty(run.Segments);
        Assert.Empty(run.RouteSignature);
        Assert.Equal(AdapterCapabilityState.Supported, run.LifecycleCapability);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.Segments.State);
        Assert.Equal("Route unavailable", UiText.FormatRoute(run));
    }

    [Fact]
    [Trait("Category", "M8")]
    public void IncompleteDestinationIdentityDisablesOnlyRouteAndStillResumesOverallRun()
    {
        var tracker = Start("A");
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, 2));
        var resumed = tracker.Apply(Event(
            RunLifecycleEventKind.DestinationControlReady,
            5,
            map: new MapIdentity { MapId = string.Empty, DisplayName = string.Empty }));
        var summary = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 8)).Completed!;

        Assert.True(resumed.StateChanged);
        Assert.False(tracker.IsActive);
        Assert.Equal(5, summary.ActiveDurationSeconds);
        Assert.Equal(MapIdentity.UnknownId, summary.EndingMapId);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, summary.RouteCapabilities.Segments.State);
        Assert.Equal(AdapterCapabilityState.Supported, summary.LifecycleCapability);
    }

    private static RunLifecycleTracker Start(string mapId, string nativeRaidId = "1")
    {
        var tracker = new RunLifecycleTracker(() => "run-1");
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: nativeRaidId));
        tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 0, context: Context(mapId, nativeRaidId)));
        return tracker;
    }

    private static void SetMaximumMeasurements(RunSummary summary)
    {
        summary.ActiveDurationSeconds = double.MaxValue;
        summary.PhysicalDistance = double.MaxValue;
        summary.TeleportDistance = double.MaxValue;
        summary.TransitionExcludedDistance = double.MaxValue;
        summary.Segments[0].ActiveDurationSeconds = double.MaxValue;
        summary.Segments[0].PhysicalDistance = double.MaxValue;
        summary.Segments[0].TeleportDistance = double.MaxValue;
        summary.Segments[0].TransitionExcludedDistance = double.MaxValue;
    }

    private static ProfileDocument Document(RunSummary run) => new()
    {
        GenerationId = "generation-1",
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = "generation-1",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Runs = new List<RunSummary> { run }
        }
    };

    private static void Transition(RunLifecycleTracker tracker, double start, double ready, string mapId)
    {
        tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted, start));
        tracker.Apply(Event(RunLifecycleEventKind.LoadingEnded, ready - 0.1));
        tracker.Apply(Event(RunLifecycleEventKind.DestinationControlReady, ready, map: Map(mapId)));
    }

    private static RunStartContext Context(string mapId, string raidId) => new()
    {
        SaveGenerationId = "generation-1",
        NativeRaidId = raidId,
        Map = Map(mapId),
        IntegrityTags = IntegrityTags.Normal,
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        LifecycleCapability = AdapterCapabilityState.Supported,
        MovementCapability = AdapterCapabilityState.Supported,
        MapCapability = AdapterCapabilityState.Supported,
        CombatCapabilities = CombatNativeContractPolicy.CreateSupportedCapabilities(),
        WeaponCapabilities = WeaponNativeContractPolicy.CreateMetricCapabilities(),
        EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities(),
        ContainerCapabilities = ContainerNativeContractPolicy.Supported(),
        EconomyCapabilities = SupportedEconomyCapabilities(),
        RouteCapabilities = RouteStatisticsReducer.Supported("test")
    };

    private static MapIdentity Map(string id) => new()
    {
        MapId = $"duckov:map:{id}",
        DisplayName = id,
        IsKnown = true
    };

    private static EquipmentSnapshot Snapshot() => new()
    {
        SnapshotId = "snapshot-a",
        LoadoutId = "loadout-a",
        TotemSetId = "totems-none",
        Items = new List<EquippedItemSnapshot>()
    };

    private static RunLifecycleEvent Event(
        RunLifecycleEventKind kind,
        double seconds,
        RunStartContext? context = null,
        string? nativeRaidId = null,
        MapIdentity? map = null) => new()
        {
            Kind = kind,
            TimestampUtc = Now.AddSeconds(seconds),
            MonotonicSeconds = seconds,
            NativeRaidId = nativeRaidId,
            StartContext = context,
            Map = map
        };

    private static ItemUseRecorded Item(string eventId, RunLifecycleTracker tracker, string map) => new()
    {
        EventId = eventId,
        TimestampUtc = Now.AddSeconds(1),
        SaveGenerationId = "generation-1",
        RunId = tracker.ActiveRunId,
        MapId = $"duckov:map:{map}",
        SegmentId = tracker.ActiveSegmentId,
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        AdapterCapability = AdapterCapabilityState.Supported,
        ItemId = "duckov:item:medkit",
        DisplayName = "Medkit",
        Group = CanonicalItemGroup.Healing,
        ActivationCount = 1,
        AmountConsumed = 1,
        ConsumptionUnit = ConsumptionUnit.Item
    };

    private static CurrencyFlowRecorded Currency(
        string eventId,
        RunLifecycleTracker tracker,
        string map,
        CurrencyKind currency,
        CurrencyFlowDirection direction,
        long amount,
        bool acquisition = false) => new()
        {
            EventId = eventId,
            TimestampUtc = Now.AddSeconds(1),
            SaveGenerationId = "generation-1",
            RunId = tracker.ActiveRunId,
            MapId = $"duckov:map:{map}",
            SegmentId = tracker.ActiveSegmentId,
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            Currency = currency,
            Direction = direction,
            Amount = amount,
            Source = acquisition ? CurrencySourceCategory.LootOrPickup : CurrencySourceCategory.UnknownAdjustment,
            ProvenExternalRaidAcquisition = acquisition,
            AdapterVersion = "test",
            ProducerActivationId = "test-route-lifecycle",
            ProducerSequence = Interlocked.Increment(ref economySequence)
        };

    private static EconomyMetricCapabilities SupportedEconomyCapabilities()
    {
        static MetricAvailability Available() => new() { State = AdapterCapabilityState.Supported, Provenance = "test" };
        return new EconomyMetricCapabilities
        {
            MoneyAmountDirection = Available(),
            MoneySourceAttribution = Available(),
            MoneyContextAttribution = Available(),
            CashAmountDirection = Available(),
            CashExternalAcquisition = Available(),
            CashContextAttribution = Available(),
            CashTerminalOutcomes = Available(),
            RouteAttribution = Available()
        };
    }

    private static HealingApplied Healing(
        string eventId,
        RunLifecycleTracker tracker,
        string sourceSegment,
        string sourceMap,
        string outcomeSegment,
        string outcomeMap) => new()
        {
            EventId = eventId,
            ApplicationId = "application-1",
            SourceItemUseEventId = "use-a",
            TimestampUtc = Now.AddSeconds(5),
            SaveGenerationId = "generation-1",
            RunId = tracker.ActiveRunId,
            MapId = $"duckov:map:{sourceMap}",
            SourceSegmentId = sourceSegment,
            SourceMapId = $"duckov:map:{sourceMap}",
            OutcomeSegmentId = outcomeSegment,
            OutcomeMapId = $"duckov:map:{outcomeMap}",
            GameplayContext = GameplayContext.Raid,
            AdapterCapability = AdapterCapabilityState.Supported,
            ItemId = "duckov:item:medkit",
            DisplayName = "Medkit",
            Group = CanonicalItemGroup.Healing,
            ActualHealthRestored = 7
        };

    private static ShotRecorded Shot(string eventId, RunLifecycleTracker tracker) => new()
    {
        EventId = eventId,
        TimestampUtc = Now.AddSeconds(5),
        SaveGenerationId = "generation-1",
        RunId = tracker.ActiveRunId!,
        MapId = tracker.ActiveMapId!,
        SegmentId = tracker.ActiveSegmentId,
        GameplayContext = GameplayContext.Raid,
        WeaponId = "duckov:weapon:test",
        WeaponDisplayName = "Test weapon",
        AmmunitionId = "duckov:ammo:test",
        AmmunitionDisplayName = "Test ammunition",
        FiringActionCount = 1,
        Capabilities = WeaponNativeContractPolicy.CreateMetricCapabilities()
    };

    private static CombatRecorded Combat(
        string eventId,
        RunLifecycleTracker tracker,
        string sourceSegment,
        string sourceMap,
        string outcomeSegment,
        string outcomeMap) => new()
        {
            EventId = eventId,
            TimestampUtc = Now.AddSeconds(6),
            SaveGenerationId = "generation-1",
            RunId = tracker.ActiveRunId!,
            MapId = $"duckov:map:{outcomeMap}",
            SourceSegmentId = sourceSegment,
            SourceMapId = $"duckov:map:{sourceMap}",
            OutcomeSegmentId = outcomeSegment,
            OutcomeMapId = $"duckov:map:{outcomeMap}",
            GameplayContext = GameplayContext.Raid,
            Ownership = CombatOwnership.Player,
            TargetIsEnemy = true,
            ActualDamageToTarget = 9,
            ActualDamageDealt = 9,
            Capabilities = CombatNativeContractPolicy.CreateSupportedCapabilities()
        };

    private static ContainerLooted Container(string eventId, RunLifecycleTracker tracker, int key) => new()
    {
        EventId = eventId,
        TimestampUtc = Now.AddSeconds(7),
        SaveGenerationId = "generation-1",
        RunId = tracker.ActiveRunId!,
        MapId = tracker.ActiveMapId!,
        SegmentId = tracker.ActiveSegmentId,
        GameplayContext = GameplayContext.Raid,
        ContainerKey = key
    };

    private static SegmentEventAssociation LegacyAssociation(
        string eventId,
        MapSegmentSummary segment,
        DateTime timestampUtc) => new()
        {
            EventId = eventId,
            EventKind = "item-use",
            TimestampUtc = timestampUtc,
            SourceSegmentId = segment.SegmentId,
            SourceMapId = segment.MapId,
            OutcomeSegmentId = segment.SegmentId,
            OutcomeMapId = segment.MapId
        };
}
