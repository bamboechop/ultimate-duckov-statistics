using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public enum RunLifecycleEventKind
{
    RaidInitialized,
    ControlReady,
    PauseStarted,
    PauseEnded,
    LoadingStarted,
    LoadingEnded,
    MapTransitionStarted,
    DestinationControlReady,
    Extracted,
    Died,
    Interrupted,
    RaidCleared
}

public sealed class RunStartContext
{
    public string SaveGenerationId { get; set; } = string.Empty;

    public string? NativeRaidId { get; set; }

    public MapIdentity Map { get; set; } = new();

    public IntegrityTags IntegrityTags { get; set; }

    public string GameVersion { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public AdapterCapabilityState LifecycleCapability { get; set; }

    public string LifecycleAdapterVersion { get; set; } = string.Empty;

    public AdapterCapabilityState MovementCapability { get; set; }

    public string MovementAdapterVersion { get; set; } = string.Empty;

    public AdapterCapabilityState MapCapability { get; set; }

    public string MapAdapterVersion { get; set; } = string.Empty;

    public WeaponMetricCapabilities WeaponCapabilities { get; set; } = new();

    public CombatMetricCapabilities CombatCapabilities { get; set; } = new();

    public EquipmentMetricCapabilities EquipmentCapabilities { get; set; } = new();

    public ContainerMetricCapabilities ContainerCapabilities { get; set; } = new();

    public EconomyMetricCapabilities EconomyCapabilities { get; set; } = new();

    public RouteMetricCapabilities RouteCapabilities { get; set; } =
        RouteStatisticsReducer.Unavailable("Route capability was not supplied by the native adapter.");
}

public sealed class RunLifecycleEvent
{
    public RunLifecycleEventKind Kind { get; set; }

    public DateTime TimestampUtc { get; set; }

    public double MonotonicSeconds { get; set; }

    public string? NativeRaidId { get; set; }

    public RunStartContext? StartContext { get; set; }

    public MapIdentity? Map { get; set; }
}

public sealed class RunLifecycleTransition
{
    public bool Started { get; internal set; }

    public bool StateChanged { get; internal set; }

    public bool CheckpointRequired { get; internal set; }

    public RunSummary? Completed { get; internal set; }
}

public sealed class RunLifecycleTracker
{
    public const double DefaultCheckpointIntervalSeconds = 5;
    private readonly Func<string> runIdFactory;
    private readonly double checkpointIntervalSeconds;
    private readonly HashSet<SuspensionReason> suspensions = new();
    private readonly MovementAccumulator movement;
    private ActiveState? active;
    private bool raidInitialized;
    private string? nativeRaidId;
    private double lastCheckpointMonotonicSeconds;
    private bool combatCheckpointRequired;
    private long checkpointMutationRevision;

    public RunLifecycleTracker(
        Func<string> runIdFactory,
        double checkpointIntervalSeconds = DefaultCheckpointIntervalSeconds,
        MovementAccumulator? movement = null)
    {
        this.runIdFactory = runIdFactory ?? throw new ArgumentNullException(nameof(runIdFactory));
        if (checkpointIntervalSeconds <= 0
            || double.IsNaN(checkpointIntervalSeconds)
            || double.IsInfinity(checkpointIntervalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointIntervalSeconds));
        }

        this.checkpointIntervalSeconds = checkpointIntervalSeconds;
        this.movement = movement ?? new MovementAccumulator();
    }

    public bool IsActive => active != null;

    public bool IsSuspended => suspensions.Count > 0;

    public string? ActiveRunId => active?.RunId;

    public string? ActiveMapId => active?.CurrentMap.MapId;

    public string? ActiveSegmentId => active?.CurrentSegment?.SegmentId;

    public EventAttributionContext? ActiveEventContext => active == null ? null : new EventAttributionContext
    {
        RunId = active.RunId,
        MapId = active.CurrentMap.MapId,
        SegmentId = active.CurrentSegment?.SegmentId ?? string.Empty,
        RouteSupported = active.CurrentEventCaptureSupported && active.CurrentSegment != null
    };

    public bool CombatCheckpointRequired => active != null && combatCheckpointRequired;

    public bool WillComplete(RunLifecycleEvent lifecycleEvent)
    {
        if (lifecycleEvent == null) throw new ArgumentNullException(nameof(lifecycleEvent));
        if (active == null) return false;
        return lifecycleEvent.Kind switch
        {
            RunLifecycleEventKind.Extracted or RunLifecycleEventKind.Died or RunLifecycleEventKind.Interrupted => true,
            RunLifecycleEventKind.RaidInitialized => !active.TransitionPending
                                                     && !string.IsNullOrWhiteSpace(lifecycleEvent.NativeRaidId)
                                                     && !string.Equals(
                                                         active.LastNativeRaidId,
                                                         lifecycleEvent.NativeRaidId,
                                                         StringComparison.Ordinal),
            _ => false
        };
    }

    public RunLifecycleTransition Apply(RunLifecycleEvent lifecycleEvent)
    {
        if (lifecycleEvent == null)
        {
            throw new ArgumentNullException(nameof(lifecycleEvent));
        }

        ValidateClock(lifecycleEvent);
        var transition = new RunLifecycleTransition();
        switch (lifecycleEvent.Kind)
        {
            case RunLifecycleEventKind.RaidInitialized:
                if (active != null
                    && !active.TransitionPending
                    && !string.IsNullOrWhiteSpace(lifecycleEvent.NativeRaidId)
                    && !string.Equals(active.LastNativeRaidId, lifecycleEvent.NativeRaidId, StringComparison.Ordinal))
                {
                    transition.Completed = Complete(RunOutcome.Interrupted, lifecycleEvent);
                    transition.CheckpointRequired = transition.Completed != null;
                }
                raidInitialized = true;
                nativeRaidId = lifecycleEvent.NativeRaidId;
                transition.StateChanged = true;
                break;
            case RunLifecycleEventKind.ControlReady:
                if (active == null
                    && raidInitialized
                    && suspensions.Count == 0
                    && lifecycleEvent.StartContext != null)
                {
                    Start(lifecycleEvent.StartContext, lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
                    transition.Started = true;
                    transition.StateChanged = true;
                    transition.CheckpointRequired = true;
                }
                break;
            case RunLifecycleEventKind.PauseStarted:
                SetSuspension(SuspensionReason.Pause, active: true, lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.PauseEnded:
                SetSuspension(SuspensionReason.Pause, active: false, lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.LoadingStarted:
                SetSuspension(SuspensionReason.Loading, active: true, lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.LoadingEnded:
                if (active?.TransitionPending != true)
                {
                    SetSuspension(SuspensionReason.Loading, active: false, lifecycleEvent, transition);
                }
                break;
            case RunLifecycleEventKind.MapTransitionStarted:
                BeginMapTransition(lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.DestinationControlReady:
                ResumeAtDestination(lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.Extracted:
                transition.Completed = Complete(RunOutcome.Extracted, lifecycleEvent);
                transition.StateChanged = transition.Completed != null;
                transition.CheckpointRequired = transition.StateChanged;
                break;
            case RunLifecycleEventKind.Died:
                transition.Completed = Complete(RunOutcome.Died, lifecycleEvent);
                transition.StateChanged = transition.Completed != null;
                transition.CheckpointRequired = transition.StateChanged;
                break;
            case RunLifecycleEventKind.Interrupted:
                transition.Completed = Complete(RunOutcome.Interrupted, lifecycleEvent);
                transition.StateChanged = transition.Completed != null;
                transition.CheckpointRequired = transition.StateChanged;
                break;
            case RunLifecycleEventKind.RaidCleared:
                if (active == null)
                {
                    raidInitialized = false;
                    nativeRaidId = null;
                    transition.StateChanged = true;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifecycleEvent));
        }

        return transition;
    }

    public bool Tick(DateTime timestampUtc, double monotonicSeconds)
    {
        if (active == null)
        {
            return false;
        }

        Advance(timestampUtc, monotonicSeconds);
        return monotonicSeconds - lastCheckpointMonotonicSeconds >= checkpointIntervalSeconds;
    }

    public MovementObservationResult ObserveMovement(
        Position3D position,
        double monotonicSeconds,
        double maximumPlausibleSpeed,
        MovementObservationKind kind = MovementObservationKind.Regular)
    {
        if (active == null)
        {
            return new MovementObservationResult(MovementDisposition.InvalidIgnored, 0, 0);
        }

        var result = movement.Observe(position, monotonicSeconds, maximumPlausibleSpeed, kind);
        var segment = active.CurrentSegment ?? active.TransitionSourceSegment;
        if (segment != null)
        {
            switch (result.Disposition)
            {
                case MovementDisposition.Physical:
                    segment.PhysicalDistance = RouteStatisticsReducer.SaturatingAdd(
                        segment.PhysicalDistance,
                        result.Distance);
                    break;
                case MovementDisposition.Teleport:
                    segment.TeleportDistance = RouteStatisticsReducer.SaturatingAdd(
                        segment.TeleportDistance,
                        result.Distance);
                    break;
                case MovementDisposition.TransitionExcluded:
                    segment.TransitionExcludedDistance = RouteStatisticsReducer.SaturatingAdd(
                        segment.TransitionExcludedDistance,
                        result.Distance);
                    break;
            }
        }
        return result;
    }

    public bool ObserveIntegrity(IntegrityTags integrityTags)
    {
        if (active == null)
        {
            return false;
        }

        var accumulated = RunIntegrityPolicy.Accumulate(active.Context.IntegrityTags, integrityTags);
        if (accumulated == active.Context.IntegrityTags)
        {
            return false;
        }

        active.Context.IntegrityTags = accumulated;
        if (active.CurrentSegment != null)
        {
            active.CurrentSegment.IntegrityTags = RunIntegrityPolicy.Accumulate(
                active.CurrentSegment.IntegrityTags,
                integrityTags);
        }
        return true;
    }

    public bool RecordShot(ShotRecorded shot)
    {
        if (active == null || shot == null)
        {
            return false;
        }

        if (!MatchesCurrentContext(shot.SaveGenerationId, shot.RunId, shot.MapId, shot.SegmentId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(shot.EventId)
            || !active.RecentShotEventIds.Add(shot.EventId))
        {
            return false;
        }

        try
        {
            WeaponStatisticsReducer.Apply(active.WeaponStatistics, shot);
            EquipmentStatisticsReducer.RecordShot(active.EquipmentStatistics, shot);
            if (active.CurrentEventCaptureSupported && active.CurrentSegment != null)
            {
                WeaponStatisticsReducer.Apply(active.CurrentSegment.WeaponStatistics, shot);
                EquipmentStatisticsReducer.RecordShot(active.CurrentSegment.EquipmentStatistics, shot);
                RecordAssociation(shot.EventId, "shot", shot.TimestampUtc, shot.SegmentId, shot.MapId, shot.SegmentId, shot.MapId);
            }
            active.Context.IntegrityTags = RunIntegrityPolicy.Accumulate(
                active.Context.IntegrityTags,
                shot.IntegrityTags);
        }
        catch
        {
            active.RecentShotEventIds.Remove(shot.EventId);
            throw;
        }

        active.RecentShotEventIdOrder.Enqueue(shot.EventId);
        while (active.RecentShotEventIdOrder.Count > 512)
        {
            active.RecentShotEventIds.Remove(active.RecentShotEventIdOrder.Dequeue());
        }

        RequireCombatCheckpoint();
        return true;
    }

    public bool RecordCombat(CombatRecorded value)
    {
        if (active == null
            || value == null
            || !string.Equals(value.SaveGenerationId, active.Context.SaveGenerationId, StringComparison.Ordinal)
            || !string.Equals(value.RunId, active.RunId, StringComparison.Ordinal)
            || (string.IsNullOrWhiteSpace(value.OutcomeSegmentId)
                && !string.Equals(value.MapId, active.CurrentMap.MapId, StringComparison.Ordinal))
            || value.GameplayContext != GameplayContext.Raid
            || string.IsNullOrWhiteSpace(value.EventId)
            || active.RecentCombatEventIds.Contains(value.EventId))
        {
            return false;
        }

        active.RecentCombatEventIds.Add(value.EventId);
        try
        {
            CombatStatisticsReducer.Apply(active.CombatStatistics, value);
            EquipmentStatisticsReducer.RecordCombat(active.EquipmentStatistics, value);
            if (MatchesCurrentAttribution(value.OutcomeMapId ?? value.MapId, value.OutcomeSegmentId))
            {
                var segment = active.CurrentSegment!;
                CombatStatisticsReducer.Apply(segment.CombatStatistics, value);
                EquipmentStatisticsReducer.RecordCombat(segment.EquipmentStatistics, value);
            }
            else if (active.CurrentEventCaptureSupported)
            {
                MarkHistoricalEventAttributionIncomplete(
                    active,
                    "A combat outcome occurred without a proven active destination segment; overall combat remains available.");
            }
            if (active.CurrentEventCaptureSupported)
            {
                RecordAssociation(
                    value.EventId,
                    "combat",
                    value.TimestampUtc,
                    value.SourceSegmentId,
                    value.SourceMapId,
                    value.OutcomeSegmentId,
                    value.OutcomeMapId ?? value.MapId);
            }
            active.Context.IntegrityTags = RunIntegrityPolicy.Accumulate(
                active.Context.IntegrityTags,
                value.IntegrityTags);
        }
        catch
        {
            active.RecentCombatEventIds.Remove(value.EventId);
            throw;
        }

        active.RecentCombatEventIdOrder.Enqueue(value.EventId);
        while (active.RecentCombatEventIdOrder.Count > 2048)
        {
            active.RecentCombatEventIds.Remove(active.RecentCombatEventIdOrder.Dequeue());
        }

        RequireCombatCheckpoint();
        return true;
    }

    public bool RecordContainer(ContainerLooted value)
    {
        if (active == null
            || value == null
            || !string.Equals(value.SaveGenerationId, active.Context.SaveGenerationId, StringComparison.Ordinal)
            || !string.Equals(value.RunId, active.RunId, StringComparison.Ordinal)
            || !MatchesCurrentContext(value.SaveGenerationId, value.RunId, value.MapId, value.SegmentId)
            || value.GameplayContext != GameplayContext.Raid)
        {
            return false;
        }

        var wasSaturated = active.ContainerState.DeduplicationSaturated;
        var accepted = ContainerStatisticsReducer.Record(active.ContainerState, value);
        var saturationChanged = active.ContainerState.DeduplicationSaturated != wasSaturated;
        if (accepted)
        {
            active.Context.IntegrityTags = RunIntegrityPolicy.Accumulate(active.Context.IntegrityTags, value.IntegrityTags);
            if (active.CurrentEventCaptureSupported && active.CurrentSegment != null)
            {
                active.CurrentSegment.ContainerStatistics.UniqueContainersLooted = SaturatingAdd(
                    active.CurrentSegment.ContainerStatistics.UniqueContainersLooted,
                    1);
                RecordAssociation(value.EventId, "container", value.TimestampUtc, value.SegmentId, value.MapId, value.SegmentId, value.MapId);
            }
        }
        if (accepted || saturationChanged) RequireCombatCheckpoint();
        return accepted;
    }

    public bool UpdateCombatCapabilities(CombatMetricCapabilities capabilities)
    {
        if (active == null || capabilities == null) return false;
        CombatStatisticsReducer.RestrictCapabilities(active.CombatStatistics, capabilities);
        if (active.CurrentSegment != null)
            CombatStatisticsReducer.RestrictCapabilities(active.CurrentSegment.CombatStatistics, capabilities);
        RequireCombatCheckpoint();
        return true;
    }

    public bool UpdateContainerCapabilities(ContainerMetricCapabilities capabilities)
    {
        if (active == null || capabilities == null) return false;
        ContainerStatisticsReducer.RestrictCapabilities(active.ContainerState.Statistics, capabilities);
        if (active.CurrentSegment != null)
            ContainerStatisticsReducer.RestrictCapabilities(active.CurrentSegment.ContainerStatistics, capabilities);
        RequireCombatCheckpoint();
        return true;
    }

    public bool ObserveEquipment(EquipmentSnapshot snapshot)
    {
        if (active == null || snapshot == null) return false;
        var changed = EquipmentStatisticsReducer.Observe(active.EquipmentStatistics, snapshot, active.ActiveDurationSeconds);
        if (active.CurrentEventCaptureSupported && active.CurrentSegment != null)
        {
            changed |= EquipmentStatisticsReducer.Observe(
                active.CurrentSegment.EquipmentStatistics,
                snapshot,
                active.CurrentSegment.ActiveDurationSeconds);
        }
        if (changed) RequireCombatCheckpoint();
        return changed;
    }

    public bool ObserveEquipment(
        EquipmentSnapshot snapshot,
        DateTime timestampUtc,
        double monotonicSeconds)
    {
        if (active == null || snapshot == null) return false;
        if (double.IsNaN(monotonicSeconds) || double.IsInfinity(monotonicSeconds) || monotonicSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
        Advance(timestampUtc, monotonicSeconds);
        return ObserveEquipment(snapshot);
    }

    public bool SuspendEquipment(DateTime timestampUtc, double monotonicSeconds)
    {
        if (active == null) return false;
        if (double.IsNaN(monotonicSeconds) || double.IsInfinity(monotonicSeconds) || monotonicSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
        Advance(timestampUtc, monotonicSeconds);
        var changed = EquipmentStatisticsReducer.Suspend(active.EquipmentStatistics, active.ActiveDurationSeconds);
        if (active.CurrentSegment != null)
        {
            changed |= EquipmentStatisticsReducer.Suspend(
                active.CurrentSegment.EquipmentStatistics,
                active.CurrentSegment.ActiveDurationSeconds);
        }
        if (changed) RequireCombatCheckpoint();
        return changed;
    }

    public bool RecordItemUse(ItemUseRecorded value)
    {
        if (active == null || value == null || value.GameplayContext != GameplayContext.Raid
            || !MatchesCurrentContext(value.SaveGenerationId, value.RunId, value.MapId, value.SegmentId))
            return false;
        var changed = ItemStatisticsAggregateReducer.Record(active.ItemStatistics, active.Context.SaveGenerationId, value);
        if (changed && active.CurrentEventCaptureSupported && active.CurrentSegment != null)
        {
            ItemStatisticsAggregateReducer.Record(active.CurrentSegment.ItemStatistics, active.Context.SaveGenerationId, value);
            RecordAssociation(value.EventId, "item-use", value.TimestampUtc, value.SegmentId, value.MapId, value.SegmentId, value.MapId);
        }
        if (changed) RequireCombatCheckpoint();
        return changed;
    }

    public bool RecordCurrencyFlow(CurrencyFlowRecorded value)
    {
        if (active == null || value == null || value.GameplayContext != GameplayContext.Raid
            || !MatchesRunContext(value.SaveGenerationId, value.RunId))
            return false;
        var runChanged = EconomyStatisticsReducer.Record(
            active.Economy,
            active.Context.SaveGenerationId,
            value,
            out var runCapabilityChanged);
        var segmentChanged = false;
        var segmentCapabilityChanged = false;
        if (active.RouteSupported)
        {
            var segment = active.Segments.FirstOrDefault(candidate =>
                string.Equals(candidate.SegmentId, value.SegmentId, StringComparison.Ordinal)
                && string.Equals(candidate.MapId, value.MapId, StringComparison.Ordinal));
            if (segment != null)
            {
                // Economy has its own exact cursor-deduplicated run/segment
                // fan-out. It must not consume the bounded legacy association
                // list shared by item, combat, healing, weapon, and container
                // attribution.
                segmentChanged = EconomyStatisticsReducer.Record(
                    segment.Economy,
                    active.Context.SaveGenerationId,
                    value,
                    out segmentCapabilityChanged);
            }
            else if (runChanged || runCapabilityChanged)
            {
                DisableEconomyRouteAttribution(
                    active,
                    "A currency flow lacked a complete proven segment join; overall economy remains available.");
            }
        }
        else if (runChanged || runCapabilityChanged)
        {
            DisableEconomyRouteAttribution(
                active,
                "Ordered route segments were unavailable; overall economy remains available.");
        }
        if (runChanged || runCapabilityChanged || segmentChanged || segmentCapabilityChanged)
        {
            active.Context.IntegrityTags = RunIntegrityPolicy.Accumulate(active.Context.IntegrityTags, value.IntegrityTags);
            RequireCombatCheckpoint();
        }
        return runChanged || runCapabilityChanged || segmentChanged || segmentCapabilityChanged;
    }

    public bool UpdateEconomyCapabilities(EconomyMetricCapabilities capabilities)
    {
        if (active == null || capabilities == null) return false;
        var updated = EconomyStatisticsReducer.CloneCapabilities(capabilities);
        if (active.Economy.Capabilities.RouteAttribution.State == AdapterCapabilityState.DisabledIncompatible)
            updated.RouteAttribution = CloneAvailability(active.Economy.Capabilities.RouteAttribution);
        EconomyStatisticsReducer.SetCapabilities(active.Economy, updated);
        if (active.CurrentSegment != null)
            EconomyStatisticsReducer.SetCapabilities(active.CurrentSegment.Economy, updated);
        RequireCombatCheckpoint();
        return true;
    }

    private static void DisableEconomyRouteAttribution(ActiveState state, string reason)
    {
        state.Economy.Capabilities.RouteAttribution = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = reason
        };
        foreach (var segment in state.Segments)
            segment.Economy.Capabilities.RouteAttribution = CloneAvailability(state.Economy.Capabilities.RouteAttribution);
    }

    private static void SynchronizeEconomyRouteCapability(ActiveState state)
    {
        if (!state.RouteSupported
            && state.Economy.Capabilities.RouteAttribution.State != AdapterCapabilityState.DisabledIncompatible)
            DisableEconomyRouteAttribution(
                state,
                "Ordered route segments became unavailable; overall economy remains available.");
    }

    private static MetricAvailability CloneAvailability(MetricAvailability value) => new()
    {
        State = value.State,
        Provenance = value.Provenance
    };

    public bool RecordHealing(HealingApplied value)
    {
        if (active == null || value == null || value.GameplayContext != GameplayContext.Raid
            || !MatchesRunContext(value.SaveGenerationId, value.RunId))
            return false;
        var changed = ItemStatisticsAggregateReducer.Record(active.ItemStatistics, active.Context.SaveGenerationId, value);
        if (changed && MatchesCurrentAttribution(value.OutcomeMapId ?? value.MapId, value.OutcomeSegmentId))
        {
            ItemStatisticsAggregateReducer.RecordOutcomeHealing(active.CurrentSegment!.ItemStatistics, active.Context.SaveGenerationId, value);
        }
        else if (changed && active.CurrentEventCaptureSupported)
        {
            MarkHistoricalEventAttributionIncomplete(
                active,
                "A healing outcome occurred without a proven active destination segment; overall healing remains available.");
        }
        if (changed && active.CurrentEventCaptureSupported)
            RecordAssociation(
                value.EventId,
                "healing",
                value.TimestampUtc,
                value.SourceSegmentId,
                value.SourceMapId,
                value.OutcomeSegmentId,
                value.OutcomeMapId ?? value.MapId);
        if (changed) RequireCombatCheckpoint();
        return changed;
    }

    public ActiveRunCheckpoint? CreateCheckpoint(DateTime timestampUtc, double monotonicSeconds)
    {
        if (active == null)
        {
            return null;
        }

        Advance(timestampUtc, monotonicSeconds);
        SynchronizeEconomyRouteCapability(active);
        return new ActiveRunCheckpoint
        {
            RunId = active.RunId,
            SaveGenerationId = active.Context.SaveGenerationId,
            NativeRaidId = active.Context.NativeRaidId,
            MapId = active.Context.Map.MapId,
            MapDisplayName = active.Context.Map.DisplayName,
            MapKnown = active.Context.Map.IsKnown,
            StartedUtc = active.StartedUtc,
            LastObservedUtc = active.LastObservedUtc,
            ActiveDurationSeconds = active.ActiveDurationSeconds,
            PhysicalDistance = movement.PhysicalDistance,
            TeleportDistance = movement.TeleportDistance,
            IntegrityTags = active.Context.IntegrityTags,
            GameVersion = active.Context.GameVersion,
            GameBuild = active.Context.GameBuild,
            LifecycleCapability = active.Context.LifecycleCapability,
            LifecycleAdapterVersion = active.Context.LifecycleAdapterVersion,
            MovementCapability = active.Context.MovementCapability,
            MovementAdapterVersion = active.Context.MovementAdapterVersion,
            MapCapability = active.Context.MapCapability,
            MapAdapterVersion = active.Context.MapAdapterVersion,
            WeaponStatistics = WeaponStatisticsReducer.Clone(active.WeaponStatistics),
            CombatStatistics = CombatStatisticsReducer.Clone(active.CombatStatistics),
            EquipmentStatistics = EquipmentStatisticsReducer.Clone(active.EquipmentStatistics),
            ContainerState = ContainerStatisticsReducer.Clone(active.ContainerState),
            StartingMapId = active.Context.Map.MapId,
            StartingMapDisplayName = active.Context.Map.DisplayName,
            StartingMapKnown = active.Context.Map.IsKnown,
            Segments = active.Segments.Select(RouteStatisticsReducer.CloneSegment).ToList(),
            TransitionExcludedDistance = movement.TransitionExcludedDistance,
            RouteCapabilities = RouteStatisticsReducer.CloneCapabilities(active.RouteCapabilities),
            HistoricalRouteUnavailable = false,
            RouteWasRepairedFromInvalidState = false,
            SegmentEventAssociations = active.EventAssociations.Select(RouteStatisticsReducer.CloneAssociation).ToList(),
            ItemStatistics = ItemStatisticsAggregateReducer.Clone(active.ItemStatistics),
            TransitionPending = active.TransitionPending,
            CurrentSegmentId = active.CurrentSegment?.SegmentId,
            MovementBaseline = movement.CaptureBaseline(),
            Economy = EconomyStatisticsReducer.Clone(active.Economy),
            HistoricalEventAttributionIncomplete = active.HistoricalEventAttributionIncomplete,
            HistoricalEventAttributionProvenance = active.HistoricalEventAttributionProvenance
        };
    }

    public void MarkCheckpointSaved(double monotonicSeconds)
    {
        MarkCheckpointSaved(monotonicSeconds, checkpointMutationRevision);
    }

    public void MarkCheckpointSaved(double monotonicSeconds, long capturedMutationRevision)
    {
        if (!double.IsNaN(monotonicSeconds) && !double.IsInfinity(monotonicSeconds))
        {
            lastCheckpointMonotonicSeconds = monotonicSeconds;
            if (capturedMutationRevision == checkpointMutationRevision) combatCheckpointRequired = false;
        }
    }

    public long CheckpointMutationRevision => checkpointMutationRevision;

    public void DisableMovement()
    {
        if (active != null)
        {
            active.Context.MovementCapability = AdapterCapabilityState.DisabledIncompatible;
        }
    }

    private void Start(RunStartContext context, DateTime timestampUtc, double monotonicSeconds)
    {
        ValidateContext(context);
        var runId = runIdFactory();
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("Run ID factory returned an empty value.");
        }

        context.NativeRaidId ??= nativeRaidId;
        timestampUtc = EnsureUtc(timestampUtc);
        active = new ActiveState(runId, context, timestampUtc, monotonicSeconds);
        suspensions.Clear();
        movement.Reset();
        lastCheckpointMonotonicSeconds = monotonicSeconds;
        combatCheckpointRequired = false;
        checkpointMutationRevision = 0;
    }

    public void DisableRoute(string provenance)
    {
        if (active != null)
        {
            if (active.CurrentSegment != null)
            {
                CloseSegment(active.CurrentSegment, active.LastObservedUtc, MapSegmentExitReason.Interrupted);
            }
            RouteStatisticsReducer.DisableRoute(active.RouteCapabilities, provenance);
            active.CurrentSegment = null;
            active.TransitionSourceSegment = null;
        }
    }

    private void BeginMapTransition(RunLifecycleEvent lifecycleEvent, RunLifecycleTransition transition)
    {
        if (active == null)
        {
            SetSuspension(SuspensionReason.Loading, active: true, lifecycleEvent, transition);
            return;
        }
        if (active.TransitionPending)
        {
            return;
        }

        Advance(lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
        suspensions.Add(SuspensionReason.Loading);
        if (active.CurrentSegment != null)
        {
            EquipmentStatisticsReducer.Suspend(
                active.CurrentSegment.EquipmentStatistics,
                active.CurrentSegment.ActiveDurationSeconds);
            CloseSegment(active.CurrentSegment, lifecycleEvent.TimestampUtc, MapSegmentExitReason.Transition);
            active.TransitionSourceSegment = active.CurrentSegment;
            active.CurrentSegment = null;
        }
        active.TransitionPending = true;
        transition.StateChanged = true;
        transition.CheckpointRequired = true;
    }

    private void ResumeAtDestination(RunLifecycleEvent lifecycleEvent, RunLifecycleTransition transition)
    {
        if (active == null || !active.TransitionPending || lifecycleEvent.Map == null)
        {
            return;
        }
        var map = lifecycleEvent.Map;
        if (string.IsNullOrWhiteSpace(map.MapId) || string.IsNullOrWhiteSpace(map.DisplayName))
        {
            RouteStatisticsReducer.DisableRoute(active.RouteCapabilities, "Destination map identity was incomplete.");
            map = new MapIdentity();
        }

        Advance(lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
        active.CurrentMap = CloneMap(map);
        if (active.RouteSupported)
        {
            var source = active.TransitionSourceSegment;
            if (source != null && string.Equals(source.MapId, map.MapId, StringComparison.Ordinal))
            {
                source.ExitedUtc = null;
                source.ExitReason = MapSegmentExitReason.None;
                active.CurrentSegment = source;
            }
            else if (active.Segments.Count >= RouteStatisticsReducer.MaximumSegmentsPerRun)
            {
                RouteStatisticsReducer.DisableRoute(
                    active.RouteCapabilities,
                    $"The defensive {RouteStatisticsReducer.MaximumSegmentsPerRun}-segment route bound was reached.");
                active.CurrentSegment = null;
            }
            else
            {
                active.CurrentSegment = active.CreateSegment(map, lifecycleEvent.TimestampUtc);
            }
        }
        active.TransitionPending = false;
        active.TransitionSourceSegment = null;
        active.LastNativeRaidId = nativeRaidId;
        suspensions.Remove(SuspensionReason.Loading);
        transition.StateChanged = true;
        transition.CheckpointRequired = true;
    }

    private void SetSuspension(
        SuspensionReason reason,
        bool active,
        RunLifecycleEvent lifecycleEvent,
        RunLifecycleTransition transition)
    {
        var alreadySet = suspensions.Contains(reason);
        if (alreadySet == active)
        {
            return;
        }

        if (this.active != null)
        {
            Advance(lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
        }
        if (active)
        {
            suspensions.Add(reason);
        }
        else
        {
            suspensions.Remove(reason);
        }

        transition.StateChanged = true;
        transition.CheckpointRequired = this.active != null;
    }

    private RunSummary? Complete(RunOutcome outcome, RunLifecycleEvent lifecycleEvent)
    {
        if (active == null)
        {
            return null;
        }

        Advance(lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
        var state = active;
        SynchronizeEconomyRouteCapability(state);
        EquipmentStatisticsReducer.Suspend(state.EquipmentStatistics, state.ActiveDurationSeconds);
        var endedUtc = EnsureUtc(lifecycleEvent.TimestampUtc);
        if (state.CurrentSegment != null)
        {
            EquipmentStatisticsReducer.Suspend(
                state.CurrentSegment.EquipmentStatistics,
                state.CurrentSegment.ActiveDurationSeconds);
            CloseSegment(
                state.CurrentSegment,
                endedUtc,
                outcome switch
                {
                    RunOutcome.Extracted => MapSegmentExitReason.Extracted,
                    RunOutcome.Died => MapSegmentExitReason.Died,
                    _ => MapSegmentExitReason.Interrupted
                });
        }
        else if (state.TransitionSourceSegment != null && outcome == RunOutcome.Interrupted)
        {
            state.TransitionSourceSegment.ExitReason = MapSegmentExitReason.Interrupted;
            state.TransitionSourceSegment.ExitedUtc = endedUtc < state.TransitionSourceSegment.EnteredUtc
                ? state.TransitionSourceSegment.EnteredUtc
                : endedUtc;
        }
        var recordEligible = outcome != RunOutcome.Interrupted
                             && state.Context.IntegrityTags == IntegrityTags.Normal
                             && state.Context.LifecycleCapability == AdapterCapabilityState.Supported;
        EconomyStatisticsReducer.FinalizeCashRaidOutcome(state.Economy, outcome);
        var summary = new RunSummary
        {
            RunId = state.RunId,
            SaveGenerationId = state.Context.SaveGenerationId,
            NativeRaidId = state.Context.NativeRaidId,
            MapId = state.Context.Map.MapId,
            MapDisplayName = state.Context.Map.DisplayName,
            MapKnown = state.Context.Map.IsKnown,
            StartedUtc = state.StartedUtc,
            EndedUtc = endedUtc < state.StartedUtc ? state.StartedUtc : endedUtc,
            ActiveDurationSeconds = state.ActiveDurationSeconds,
            WallClockDurationSeconds = Math.Max(0, (endedUtc - state.StartedUtc).TotalSeconds),
            Outcome = outcome,
            PhysicalDistance = movement.PhysicalDistance,
            TeleportDistance = movement.TeleportDistance,
            IntegrityTags = state.Context.IntegrityTags,
            RecordEligible = recordEligible,
            GameVersion = state.Context.GameVersion,
            GameBuild = state.Context.GameBuild,
            LifecycleCapability = state.Context.LifecycleCapability,
            LifecycleAdapterVersion = state.Context.LifecycleAdapterVersion,
            MovementCapability = state.Context.MovementCapability,
            MovementAdapterVersion = state.Context.MovementAdapterVersion,
            MapCapability = state.Context.MapCapability,
            MapAdapterVersion = state.Context.MapAdapterVersion,
            WeaponStatistics = WeaponStatisticsReducer.Clone(state.WeaponStatistics),
            CombatStatistics = CombatStatisticsReducer.Clone(state.CombatStatistics),
            EquipmentStatistics = EquipmentStatisticsReducer.Clone(state.EquipmentStatistics),
            ContainerStatistics = ContainerStatisticsReducer.Clone(state.ContainerState.Statistics),
            StartingMapId = state.Context.Map.MapId,
            StartingMapDisplayName = state.Context.Map.DisplayName,
            StartingMapKnown = state.Context.Map.IsKnown,
            EndingMapId = state.RouteSupported ? state.Segments.LastOrDefault()?.MapId ?? MapIdentity.UnknownId : MapIdentity.UnknownId,
            EndingMapDisplayName = state.RouteSupported
                ? state.Segments.LastOrDefault()?.MapDisplayName ?? MapIdentity.UnknownDisplayName
                : MapIdentity.UnknownDisplayName,
            EndingMapKnown = state.RouteSupported && state.Segments.LastOrDefault()?.MapKnown == true,
            RouteSignature = state.RouteSupported ? RouteStatisticsReducer.BuildSignature(state.Segments) : string.Empty,
            Segments = state.Segments.Select(RouteStatisticsReducer.CloneSegment).ToList(),
            TransitionExcludedDistance = movement.TransitionExcludedDistance,
            RouteCapabilities = RouteStatisticsReducer.CloneCapabilities(state.RouteCapabilities),
            HistoricalRouteUnavailable = false,
            RouteWasRepairedFromInvalidState = false,
            SegmentEventAssociations = state.EventAssociations.Select(RouteStatisticsReducer.CloneAssociation).ToList(),
            ItemStatistics = ItemStatisticsAggregateReducer.Clone(state.ItemStatistics),
            Economy = EconomyStatisticsReducer.Clone(state.Economy),
            HistoricalEventAttributionIncomplete = state.HistoricalEventAttributionIncomplete,
            HistoricalEventAttributionProvenance = state.HistoricalEventAttributionProvenance
        };

        active = null;
        combatCheckpointRequired = false;
        checkpointMutationRevision = 0;
        movement.Reset();
        raidInitialized = false;
        nativeRaidId = null;
        return summary;
    }

    private void Advance(DateTime timestampUtc, double monotonicSeconds)
    {
        if (active == null || monotonicSeconds <= active.LastMonotonicSeconds)
        {
            return;
        }

        if (suspensions.Count == 0)
        {
            var elapsed = monotonicSeconds - active.LastMonotonicSeconds;
            active.ActiveDurationSeconds = RouteStatisticsReducer.SaturatingAdd(
                active.ActiveDurationSeconds,
                elapsed);
            if (active.CurrentSegment != null)
            {
                active.CurrentSegment.ActiveDurationSeconds = RouteStatisticsReducer.SaturatingAdd(
                    active.CurrentSegment.ActiveDurationSeconds,
                    elapsed);
            }
        }

        EquipmentStatisticsReducer.Advance(active.EquipmentStatistics, active.ActiveDurationSeconds);
        if (active.CurrentSegment != null)
        {
            EquipmentStatisticsReducer.Advance(
                active.CurrentSegment.EquipmentStatistics,
                active.CurrentSegment.ActiveDurationSeconds);
        }

        active.LastMonotonicSeconds = monotonicSeconds;
        active.LastObservedUtc = EnsureUtc(timestampUtc);
    }

    private void RequireCombatCheckpoint()
    {
        combatCheckpointRequired = true;
        if (checkpointMutationRevision < long.MaxValue) checkpointMutationRevision++;
    }

    private static void ValidateClock(RunLifecycleEvent lifecycleEvent)
    {
        if (double.IsNaN(lifecycleEvent.MonotonicSeconds)
            || double.IsInfinity(lifecycleEvent.MonotonicSeconds)
            || lifecycleEvent.MonotonicSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleEvent), "Monotonic time must be finite and non-negative.");
        }

        lifecycleEvent.TimestampUtc = EnsureUtc(lifecycleEvent.TimestampUtc);
    }

    private static void ValidateContext(RunStartContext context)
    {
        if (string.IsNullOrWhiteSpace(context.SaveGenerationId)
            || context.Map == null
            || string.IsNullOrWhiteSpace(context.Map.MapId)
            || string.IsNullOrWhiteSpace(context.Map.DisplayName))
        {
            throw new ArgumentException("Run start context is incomplete.", nameof(context));
        }
    }

    private bool MatchesCurrentContext(
        string saveGenerationId,
        string? runId,
        string? mapId,
        string? segmentId)
    {
        if (active == null
            || !MatchesRunContext(saveGenerationId, runId)
            || !string.Equals(active.CurrentMap.MapId, mapId, StringComparison.Ordinal))
            return false;
        if (!active.CurrentEventCaptureSupported) return true;
        return active.CurrentSegment != null
               && !string.IsNullOrWhiteSpace(segmentId)
               && string.Equals(active.CurrentSegment.SegmentId, segmentId, StringComparison.Ordinal);
    }

    private bool MatchesRunContext(string saveGenerationId, string? runId) =>
        active != null
        && string.Equals(active.Context.SaveGenerationId, saveGenerationId, StringComparison.Ordinal)
        && string.Equals(active.RunId, runId, StringComparison.Ordinal);

    private bool MatchesCurrentAttribution(string? mapId, string? segmentId) =>
        active?.CurrentEventCaptureSupported == true
        && active.CurrentSegment != null
        && string.Equals(active.CurrentMap.MapId, mapId, StringComparison.Ordinal)
        && string.Equals(active.CurrentSegment.SegmentId, segmentId, StringComparison.Ordinal);

    private void RecordAssociation(
        string eventId,
        string eventKind,
        DateTime timestampUtc,
        string? sourceSegmentId,
        string? sourceMapId,
        string? outcomeSegmentId,
        string? outcomeMapId)
    {
        if (active == null || !active.CurrentEventCaptureSupported) return;
        active.SegmentsById.TryGetValue(sourceSegmentId ?? string.Empty, out var source);
        active.SegmentsById.TryGetValue(outcomeSegmentId ?? string.Empty, out var outcome);
        if (source == null
            || outcome == null
            || !string.Equals(source.MapId, sourceMapId, StringComparison.Ordinal)
            || !string.Equals(outcome.MapId, outcomeMapId, StringComparison.Ordinal))
        {
            MarkHistoricalEventAttributionIncomplete(
                active,
                "An event association lacked a complete proven source/outcome segment join; overall statistics remain available.");
            return;
        }
        var key = new EventAssociationKey(eventKind, source.SegmentId, outcome.SegmentId);
        var eventTimestampUtc = EnsureUtc(timestampUtc);
        if (active.EventAssociationsByKey.TryGetValue(key, out var aggregate))
        {
            if (aggregate.Count == long.MaxValue)
            {
                MarkHistoricalEventAttributionIncomplete(
                    active,
                    "An exact route event-association aggregate reached Int64.MaxValue; prior exact evidence was retained.");
                active.RouteCapabilities.CurrentEventAttributionCapture = new MetricAvailability
                {
                    State = AdapterCapabilityState.DisabledIncompatible,
                    Provenance = "Route event-association capture stopped before an unrepresentable count could be stored."
                };
                return;
            }
            aggregate.Count++;
            if (eventTimestampUtc < aggregate.FirstTimestampUtc) aggregate.FirstTimestampUtc = eventTimestampUtc;
            if (eventTimestampUtc > aggregate.LastTimestampUtc) aggregate.LastTimestampUtc = eventTimestampUtc;
            aggregate.TimestampUtc = aggregate.LastTimestampUtc;
            return;
        }
        var association = new SegmentEventAssociation
        {
            EventId = string.Empty,
            EventKind = eventKind,
            TimestampUtc = eventTimestampUtc,
            SourceSegmentId = source.SegmentId,
            SourceMapId = source.MapId,
            OutcomeSegmentId = outcome.SegmentId,
            OutcomeMapId = outcome.MapId,
            Representation = SegmentEventAssociationRepresentation.ExactAggregate,
            Count = 1,
            FirstTimestampUtc = eventTimestampUtc,
            LastTimestampUtc = eventTimestampUtc
        };
        active.EventAssociations.Add(association);
        active.EventAssociationsByKey.Add(key, association);
    }

    private static void MarkHistoricalEventAttributionIncomplete(ActiveState state, string provenance)
    {
        state.HistoricalEventAttributionIncomplete = true;
        if (string.IsNullOrWhiteSpace(state.HistoricalEventAttributionProvenance))
            state.HistoricalEventAttributionProvenance = provenance;
        RouteStatisticsReducer.MarkAttributionIncomplete(state.RouteCapabilities, provenance);
    }

    private static void CloseSegment(MapSegmentSummary segment, DateTime timestampUtc, MapSegmentExitReason reason)
    {
        var exitedUtc = EnsureUtc(timestampUtc);
        segment.ExitedUtc = exitedUtc < segment.EnteredUtc ? segment.EnteredUtc : exitedUtc;
        segment.ExitReason = reason;
    }

    private static MapIdentity CloneMap(MapIdentity map) => new()
    {
        MapId = map.MapId,
        DisplayName = map.DisplayName,
        IsKnown = map.IsKnown
    };

    private static long SaturatingAdd(long left, long right) =>
        left > long.MaxValue - right ? long.MaxValue : left + right;

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private enum SuspensionReason
    {
        Pause,
        Loading
    }

    private readonly struct EventAssociationKey : IEquatable<EventAssociationKey>
    {
        public EventAssociationKey(string eventKind, string sourceSegmentId, string outcomeSegmentId)
        {
            EventKind = eventKind;
            SourceSegmentId = sourceSegmentId;
            OutcomeSegmentId = outcomeSegmentId;
        }

        private string EventKind { get; }

        private string SourceSegmentId { get; }

        private string OutcomeSegmentId { get; }

        public bool Equals(EventAssociationKey other) =>
            string.Equals(EventKind, other.EventKind, StringComparison.Ordinal)
            && string.Equals(SourceSegmentId, other.SourceSegmentId, StringComparison.Ordinal)
            && string.Equals(OutcomeSegmentId, other.OutcomeSegmentId, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is EventAssociationKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = StringComparer.Ordinal.GetHashCode(EventKind);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(SourceSegmentId);
                return (hash * 397) ^ StringComparer.Ordinal.GetHashCode(OutcomeSegmentId);
            }
        }
    }

    private sealed class ActiveState
    {
        public ActiveState(string runId, RunStartContext context, DateTime startedUtc, double startedMonotonicSeconds)
        {
            RunId = runId;
            Context = context;
            StartedUtc = startedUtc;
            LastObservedUtc = startedUtc;
            LastMonotonicSeconds = startedMonotonicSeconds;
            WeaponStatistics.Capabilities = WeaponStatisticsReducer.CloneCapabilities(context.WeaponCapabilities);
            CombatStatistics.Capabilities = CombatStatisticsReducer.CloneCapabilities(context.CombatCapabilities);
            EquipmentStatistics.Capabilities = EquipmentStatisticsReducer.CloneCapabilities(context.EquipmentCapabilities);
            ContainerState.Statistics.Capabilities = ContainerStatisticsReducer.CloneCapabilities(context.ContainerCapabilities);
            Economy.Capabilities = EconomyStatisticsReducer.CloneCapabilities(context.EconomyCapabilities);
            RouteCapabilities = RouteStatisticsReducer.CloneCapabilities(context.RouteCapabilities);
            CurrentMap = CloneMap(context.Map);
            LastNativeRaidId = context.NativeRaidId;
            if (RouteSupported)
            {
                CurrentSegment = CreateSegment(context.Map, startedUtc);
            }
            else
            {
                DisableEconomyRouteAttribution(
                    this,
                    "Ordered route segments were unavailable at run start; overall economy remains available.");
            }
        }

        public string RunId { get; }

        public RunStartContext Context { get; }

        public DateTime StartedUtc { get; }

        public DateTime LastObservedUtc { get; set; }

        public double LastMonotonicSeconds { get; set; }

        public double ActiveDurationSeconds { get; set; }

        public string? LastNativeRaidId { get; set; }

        public MapIdentity CurrentMap { get; set; }

        public RouteMetricCapabilities RouteCapabilities { get; }

        public bool RouteSupported =>
            RouteCapabilities.OrderedRoute.State == AdapterCapabilityState.Supported
            && RouteCapabilities.Segments.State == AdapterCapabilityState.Supported;

        public bool CurrentEventCaptureSupported => RouteSupported
            && RouteCapabilities.CurrentEventAttributionCapture.State == AdapterCapabilityState.Supported;

        public bool HistoricalEventAttributionIncomplete { get; set; }

        public string HistoricalEventAttributionProvenance { get; set; } = string.Empty;

        public bool TransitionPending { get; set; }

        public MapSegmentSummary? CurrentSegment { get; set; }

        public MapSegmentSummary? TransitionSourceSegment { get; set; }

        public List<MapSegmentSummary> Segments { get; } = new();

        public Dictionary<string, MapSegmentSummary> SegmentsById { get; } = new(StringComparer.Ordinal);

        public List<SegmentEventAssociation> EventAssociations { get; } = new();

        public Dictionary<EventAssociationKey, SegmentEventAssociation> EventAssociationsByKey { get; } = new();

        public ItemStatisticsAggregate ItemStatistics { get; } = new();

        public WeaponStatisticsAggregate WeaponStatistics { get; } = new();

        public CombatStatisticsAggregate CombatStatistics { get; } = new();

        public EquipmentStatisticsAggregate EquipmentStatistics { get; } = new();

        public ContainerRunCheckpointState ContainerState { get; } = new();

        public EconomyStatisticsAggregate Economy { get; } = new();

        public HashSet<string> RecentShotEventIds { get; } = new(StringComparer.Ordinal);

        public Queue<string> RecentShotEventIdOrder { get; } = new();

        public HashSet<string> RecentCombatEventIds { get; } = new(StringComparer.Ordinal);

        public Queue<string> RecentCombatEventIdOrder { get; } = new();

        public MapSegmentSummary CreateSegment(MapIdentity map, DateTime enteredUtc)
        {
            var segment = new MapSegmentSummary
            {
                SegmentId = $"{RunId}:segment:{Segments.Count}",
                SegmentIndex = Segments.Count,
                MapId = map.MapId,
                MapDisplayName = map.DisplayName,
                MapKnown = map.IsKnown,
                EnteredUtc = EnsureUtc(enteredUtc),
                IntegrityTags = Context.IntegrityTags
            };
            segment.WeaponStatistics.Capabilities = WeaponStatisticsReducer.CloneCapabilities(Context.WeaponCapabilities);
            segment.CombatStatistics.Capabilities = CombatStatisticsReducer.CloneCapabilities(Context.CombatCapabilities);
            segment.EquipmentStatistics.Capabilities = EquipmentStatisticsReducer.CloneCapabilities(Context.EquipmentCapabilities);
            segment.ContainerStatistics.Capabilities = ContainerStatisticsReducer.CloneCapabilities(Context.ContainerCapabilities);
            segment.Economy.Capabilities = EconomyStatisticsReducer.CloneCapabilities(Economy.Capabilities);
            Segments.Add(segment);
            SegmentsById.Add(segment.SegmentId, segment);
            return segment;
        }
    }
}
