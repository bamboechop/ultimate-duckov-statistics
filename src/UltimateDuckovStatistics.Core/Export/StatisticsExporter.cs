using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Export;

[DataContract]
public sealed class StatisticsExportDocument
{
    [DataMember(Order = 21, EmitDefaultValue = false)] public IList<Encounters.EncounterRecord>? EncounterHistory { get; set; }
    [DataMember(IsRequired = true, Order = 17)] public bool HealingCaptureComplete { get; set; }
    [DataMember(IsRequired = true, Order = 18)] public AdapterCapabilityState HealingCaptureState { get; set; }
    [DataMember(IsRequired = true, Order = 19)] public bool HealingEvidenceRepaired { get; set; }
    [DataMember(IsRequired = true, Order = 20)] public DistanceStatisticsProjection Distance { get; set; } = new();

    [DataMember(IsRequired = true, Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(IsRequired = true, Order = 2)]
    public DateTime ExportedUtc { get; set; }

    [DataMember(IsRequired = true, Order = 3)]
    public string GenerationId { get; set; } = string.Empty;

    [DataMember(IsRequired = true, Order = 4)]
    public int Slot { get; set; }

    [DataMember(IsRequired = true, Order = 5)]
    public long Revision { get; set; }

    [DataMember(IsRequired = true, Order = 6)]
    public AggregateTotals Overall { get; set; } = new();

    [DataMember(IsRequired = true, Order = 7)]
    public List<GroupExportRow> Groups { get; set; } = new();

    [DataMember(IsRequired = true, Order = 8)]
    public List<ItemExportRow> Items { get; set; } = new();

    [DataMember(IsRequired = true, Order = 9)]
    public RunAggregateTotals RunTotals { get; set; } = new();

    [DataMember(IsRequired = true, Order = 10)]
    public IList<RunSummary> Runs { get; set; } = new List<RunSummary>();

    [DataMember(IsRequired = true, Order = 11)]
    public RunDurationRecords RunRecords { get; set; } = new();

    [DataMember(IsRequired = true, Order = 12)]
    public List<CapabilityRecord> Capabilities { get; set; } = new();

    [DataMember(IsRequired = true, Order = 13)]
    public EconomyStatisticsAggregate Economy { get; set; } = new();

    [DataMember(IsRequired = true, Order = 14)]
    public WorldTimeStatisticsAggregate WorldTime { get; set; } = new();

    [DataMember(IsRequired = true, Order = 15)]
    public CraftingStatisticsAggregate Crafting { get; set; } = new();

    [DataMember(IsRequired = true, Order = 16)]
    public EconomyHoldingsExport Holdings { get; set; } = new();
}

[DataContract]
public sealed class EconomyHoldingsExport
{
    [DataMember(IsRequired = true, Order = 1)] public string SaveGenerationId { get; set; } = string.Empty;
    [DataMember(IsRequired = true, Order = 2)] public EconomyHoldingObservation Money { get; set; } = new();
    [DataMember(IsRequired = true, Order = 3)] public EconomyHoldingObservation Cash { get; set; } = new();
    [DataMember(IsRequired = true, Order = 4)] public EconomyHoldingObservation LiquidWealth { get; set; } = new();
    [DataMember(IsRequired = true, Order = 5)] public EconomyHoldingsMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(IsRequired = true, Order = 8)] public bool WasRepairedFromInvalidState { get; set; }
}

[DataContract]
public sealed class GroupExportRow
{
    [DataMember(IsRequired = true, Order = 1)]
    public string Group { get; set; } = string.Empty;

    [DataMember(IsRequired = true, Order = 2)]
    public AggregateTotals Totals { get; set; } = new();
}

[DataContract]
public sealed class ItemExportRow
{
    [DataMember(IsRequired = true, Order = 1)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(IsRequired = true, Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(IsRequired = true, Order = 3)]
    public string Group { get; set; } = string.Empty;

    [DataMember(IsRequired = true, Order = 4)]
    public List<string> EffectTags { get; set; } = new();

    [DataMember(IsRequired = true, Order = 5)]
    public AggregateTotals Totals { get; set; } = new();
}

public sealed class StatisticsExportBundle
{
    public StatisticsExportBundle(StatisticsExportDocument document, string json)
    {
        Document = document;
        Json = json;
    }

    public StatisticsExportDocument Document { get; }
    public string Json { get; }
}

public static class StatisticsExporter
{
    internal static StatisticsExportDocument CreateDocument(ProfileDocument profile, DateTime exportedUtc, bool streamHistory)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        exportedUtc = EnsureUtc(exportedUtc);
        var runTotals = CloneRunTotals(profile.Statistics.RunTotals);
        runTotals.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
            runTotals.WeaponStatistics,
            profile.Capabilities,
            allowUninitializedFallback: true);
        runTotals.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
            runTotals.CombatStatistics, profile.Capabilities, allowUninitializedFallback: true);
        runTotals.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
            runTotals.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: true);
        ApplyCurrentContainerCapability(runTotals.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: true);
        foreach (var map in runTotals.Maps.Values)
        {
            map.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                map.WeaponStatistics,
                profile.Capabilities,
                allowUninitializedFallback: false);
            map.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                map.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
            map.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
                map.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: false);
            ApplyCurrentContainerCapability(map.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: false);
        }
        foreach (var map in runTotals.RouteMaps.Values)
        {
            map.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                map.WeaponStatistics, profile.Capabilities, allowUninitializedFallback: false);
            map.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                map.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
            map.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
                map.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: false);
            ApplyCurrentContainerCapability(map.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: false);
        }

        RunSummary ProjectRun(RunSummary source)
        {
            var run = CloneRun(source);
            run.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                run.WeaponStatistics,
                profile.Capabilities,
                allowUninitializedFallback: false);
            run.CombatStatistics.Capabilities = ApplyCurrentCombatCapabilityStates(
                run.CombatStatistics, profile.Capabilities, allowUninitializedFallback: false);
            run.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
                run.EquipmentStatistics, profile.Capabilities, allowUninitializedFallback: false);
            ApplyCurrentContainerCapability(run.ContainerStatistics, profile.Capabilities, allowUninitializedFallback: false);
            foreach (var segment in run.Segments)
            {
                segment.WeaponStatistics.Capabilities = ApplyCurrentWeaponCapabilityStates(
                    segment.WeaponStatistics,
                    profile.Capabilities,
                    allowUninitializedFallback: false);
                segment.EquipmentStatistics.Capabilities = ApplyCurrentEquipmentCapabilityStates(
                    segment.EquipmentStatistics,
                    profile.Capabilities,
                    allowUninitializedFallback: false);
            }
            return run;
        }
        IList<RunSummary> runs = streamHistory
            ? new ExportRunHistory(profile.Statistics.Runs, ProjectRun)
            : profile.Statistics.Runs.Select(ProjectRun).ToList();

        var holdingsProjection = EconomyHoldingsReducer.Project(profile.Statistics.Holdings);
        var document = new StatisticsExportDocument
        {
            ExportedUtc = exportedUtc,
            GenerationId = profile.GenerationId,
            Slot = profile.Slot,
            Revision = profile.Revision,
            HealingCaptureComplete = profile.Statistics.HealingCaptureComplete,
            HealingCaptureState = profile.Capabilities.Count(cap => cap.AdapterId == "native-healing-attribution") == 1
                ? profile.Capabilities.Single(cap => cap.AdapterId == "native-healing-attribution").State
                : AdapterCapabilityState.DisabledIncompatible,
            HealingEvidenceRepaired = profile.Statistics.RunTotals.ItemStatistics.WasRepairedFromInvalidState,
            Overall = CloneTotals(profile.Statistics.Overall),
            Groups = profile.Statistics.Groups
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => new GroupExportRow
                {
                    Group = entry.Key,
                    Totals = CloneTotals(entry.Value)
                })
                .ToList(),
            Items = profile.Statistics.Items.Values
                .OrderBy(item => item.ItemId, StringComparer.Ordinal)
                .Select(item => new ItemExportRow
                {
                    ItemId = item.ItemId,
                    DisplayName = item.DisplayName,
                    Group = item.Group.ToString(),
                    EffectTags = item.EffectTags.Select(tag => tag.ToString()).OrderBy(tag => tag, StringComparer.Ordinal).ToList(),
                    Totals = CloneTotals(item.Totals)
                })
                .ToList(),
            Distance = DistanceStatisticsProjection.Create(profile),
            RunTotals = runTotals,
            Runs = runs,
            EncounterHistory = ExportEncounterHistory.Create(profile, streamHistory),
            RunRecords = CloneRunRecords(profile.Statistics.RunRecords),
            Capabilities = profile.Capabilities.Select(CloneCapability).ToList(),
            Economy = EconomyStatisticsReducer.Clone(profile.Statistics.Economy),
            WorldTime = WorldTimeStatisticsReducer.Clone(profile.Statistics.WorldTime),
            Crafting = CraftingStatisticsReducer.Clone(profile.Statistics.Crafting),
            Holdings = new EconomyHoldingsExport
            {
                SaveGenerationId = profile.Statistics.Holdings.SaveGenerationId,
                Money = holdingsProjection.Money,
                Cash = holdingsProjection.Cash,
                LiquidWealth = holdingsProjection.LiquidWealth,
                Capabilities = holdingsProjection.Capabilities,
                WasRepairedFromInvalidState = profile.Statistics.Holdings.WasRepairedFromInvalidState
            }
        };

        return document;
    }

    public static StatisticsExportBundle Create(ProfileDocument profile, DateTime exportedUtc)
    {
        var document = CreateDocument(profile, exportedUtc, streamHistory: false);
        return new StatisticsExportBundle(document, SerializeJson(document));
    }

    // Project one detached run at a time without materializing the full history.
    internal static void WriteJson(ProfileDocument profile, DateTime exportedUtc, Stream output)
    {
        var document = CreateDocument(profile, exportedUtc, streamHistory: true);
        new DataContractJsonSerializer(typeof(StatisticsExportDocument), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true })
            .WriteObject(output, document);
    }

    private static string SerializeJson(StatisticsExportDocument document)
    {
        var serializer = new DataContractJsonSerializer(
            typeof(StatisticsExportDocument),
            new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
        using var stream = new MemoryStream();
        serializer.WriteObject(stream, document);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static AdapterCapabilityState ReadCapabilityState(
        IReadOnlyList<CapabilityRecord>? capabilities,
        string adapterId,
        AdapterCapabilityState fallback) => capabilities?
            .FirstOrDefault(capability => string.Equals(capability.AdapterId, adapterId, StringComparison.Ordinal))
            ?.State ?? fallback;

    private static WeaponMetricCapabilities ApplyCurrentWeaponCapabilityStates(
        WeaponStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var clone = WeaponStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        clone.FiringActions.State = ResolveAvailability(
            aggregate,
            clone.FiringActions,
            ReadCapabilityState(current, WeaponCapabilityIds.FiringActions, clone.FiringActions.State),
            allowUninitializedFallback);
        clone.WeaponIdentity.State = ResolveAvailability(
            aggregate,
            clone.WeaponIdentity,
            ReadCapabilityState(current, WeaponCapabilityIds.WeaponIdentity, clone.WeaponIdentity.State),
            allowUninitializedFallback);
        clone.AmmunitionIdentity.State = ResolveAvailability(
            aggregate,
            clone.AmmunitionIdentity,
            ReadCapabilityState(current, WeaponCapabilityIds.AmmunitionIdentity, clone.AmmunitionIdentity.State),
            allowUninitializedFallback);
        clone.WeaponAmmunitionPairing.State = ResolveAvailability(
            aggregate,
            clone.WeaponAmmunitionPairing,
            ReadCapabilityState(current, WeaponCapabilityIds.WeaponAmmunitionPairing, clone.WeaponAmmunitionPairing.State),
            allowUninitializedFallback);
        return clone;
    }

    private static CombatMetricCapabilities ApplyCurrentCombatCapabilityStates(
        CombatStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var result = CombatStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        Apply(result.DamageDealt, CombatCapabilityIds.DamageDealt);
        Apply(result.DamageReceived, CombatCapabilityIds.DamageReceived);
        Apply(result.RangedHits, CombatCapabilityIds.RangedHits);
        Apply(result.Accuracy, CombatCapabilityIds.Accuracy);
        Apply(result.MeleeSwings, CombatCapabilityIds.MeleeSwings);
        Apply(result.MeleeHits, CombatCapabilityIds.MeleeHits);
        Apply(result.PlayerDeaths, CombatCapabilityIds.PlayerDeaths);
        Apply(result.Ownership, CombatCapabilityIds.Ownership);
        Apply(result.EnemyIdentity, CombatCapabilityIds.EnemyIdentity);
        Apply(result.EnemyFamily, CombatCapabilityIds.EnemyFamily);
        Apply(result.Cause, CombatCapabilityIds.Cause);
        Apply(result.WeaponIdentity, CombatCapabilityIds.WeaponIdentity);
        Apply(result.AmmunitionIdentity, CombatCapabilityIds.AmmunitionIdentity);
        Apply(result.DamageOverTime, CombatCapabilityIds.DamageOverTime);
        Apply(result.Headshots, CombatCapabilityIds.Headshots);
        Apply(result.HeadshotFinalBlows, CombatCapabilityIds.HeadshotFinalBlows);
        Apply(result.ThrowableKills, CombatCapabilityIds.ThrowableKills);
        Apply(result.KillsByYou, CombatCapabilityIds.KillsByYou);
        Apply(result.ObservedWorldDeaths, CombatCapabilityIds.ObservedWorldDeaths);
        return result;

        void Apply(MetricAvailability value, string id)
        {
            var state = ReadCapabilityState(current, id, AdapterCapabilityState.DisabledIncompatible);
            value.State = allowUninitializedFallback
                ? CombatStatisticsReducer.ResolveCurrentAvailability(aggregate, value, state)
                : CombatStatisticsReducer.RestrictAvailability(value, state);
        }
    }

    private static EquipmentMetricCapabilities ApplyCurrentEquipmentCapabilityStates(
        EquipmentStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        var result = EquipmentStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        Apply(result.EquipmentSlots, EquipmentCapabilityIds.EquipmentSlots);
        Apply(result.SelectedWeapon, EquipmentCapabilityIds.SelectedWeapon);
        Apply(result.AttachmentMetadata, EquipmentCapabilityIds.AttachmentMetadata);
        Apply(result.DirectTotems, EquipmentCapabilityIds.DirectTotems);
        Apply(result.ToteContents, EquipmentCapabilityIds.ToteContents);
        Apply(result.CharacterSlotState, EquipmentCapabilityIds.CharacterSlotState);
        Apply(result.NestedSlotState, EquipmentCapabilityIds.NestedSlotState);
        return result;
        void Apply(MetricAvailability value, string id)
        {
            var capability = current.FirstOrDefault(candidate =>
                string.Equals(candidate.AdapterId, id, StringComparison.Ordinal));
            EquipmentStatisticsReducer.ApplyCurrentAvailability(
                aggregate,
                value,
                capability?.State ?? AdapterCapabilityState.DisabledIncompatible,
                capability?.Detail,
                allowUninitializedFallback);
        }
    }

    private static void ApplyCurrentContainerCapability(
        ContainerStatisticsAggregate aggregate,
        IReadOnlyList<CapabilityRecord> current,
        bool allowUninitializedFallback)
    {
        if (!allowUninitializedFallback) return;
        var capability = current.FirstOrDefault(candidate => string.Equals(
            candidate.AdapterId,
            ContainerCapabilityIds.UniqueContainersLooted,
            StringComparison.Ordinal));
        ContainerStatisticsReducer.ApplyCurrentAvailability(
            aggregate,
            capability?.State ?? AdapterCapabilityState.DisabledIncompatible,
            capability?.Detail);
    }

    private static AdapterCapabilityState ResolveAvailability(
        WeaponStatisticsAggregate aggregate,
        MetricAvailability recorded,
        AdapterCapabilityState current,
        bool allowUninitializedFallback) => allowUninitializedFallback
            ? WeaponStatisticsReducer.ResolveCurrentAvailability(aggregate, recorded, current)
            : WeaponStatisticsReducer.RestrictAvailability(recorded, current);

    private static AggregateTotals CloneTotals(AggregateTotals source) => new()
    {
        ActivationCount = source.ActivationCount,
        ActualHealthRestored = source.ActualHealthRestored,
        AmountsByUnit = source.AmountsByUnit.ToDictionary(
            entry => entry.Key,
            entry => entry.Value,
            StringComparer.Ordinal)
    };

    private static RunAggregateTotals CloneRunTotals(RunAggregateTotals source) => new()
    {
        TotalRuns = source.TotalRuns,
        Outcomes = source.Outcomes.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        TransitionExcludedDistance = source.TransitionExcludedDistance,
        RouteAwareHistoryUnavailable = source.RouteAwareHistoryUnavailable,
        ItemStatistics = ItemStatisticsAggregateReducer.Clone(source.ItemStatistics),
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        EquipmentStatistics = EquipmentStatisticsReducer.Clone(source.EquipmentStatistics),
        ContainerStatistics = ContainerStatisticsReducer.Clone(source.ContainerStatistics),
        Economy = EconomyStatisticsReducer.Clone(source.Economy),
        Maps = source.Maps.ToDictionary(
            entry => entry.Key,
            entry => new MapRunAggregate
            {
                MapId = entry.Value.MapId,
                DisplayName = entry.Value.DisplayName,
                IsKnown = entry.Value.IsKnown,
                TotalRuns = entry.Value.TotalRuns,
                Outcomes = entry.Value.Outcomes.ToDictionary(outcome => outcome.Key, outcome => outcome.Value, StringComparer.Ordinal),
                PhysicalDistance = entry.Value.PhysicalDistance,
                TeleportDistance = entry.Value.TeleportDistance,
                WeaponStatistics = WeaponStatisticsReducer.Clone(entry.Value.WeaponStatistics),
                CombatStatistics = CombatStatisticsReducer.Clone(entry.Value.CombatStatistics),
                EquipmentStatistics = EquipmentStatisticsReducer.Clone(entry.Value.EquipmentStatistics),
                ContainerStatistics = ContainerStatisticsReducer.Clone(entry.Value.ContainerStatistics),
                ItemStatistics = ItemStatisticsAggregateReducer.Clone(entry.Value.ItemStatistics),
                Economy = EconomyStatisticsReducer.Clone(entry.Value.Economy)
            },
            StringComparer.Ordinal),
        RouteMaps = source.RouteMaps.ToDictionary(
            entry => entry.Key,
            entry => CloneRouteMap(entry.Value),
            StringComparer.Ordinal)
    };

    private static RunSummary CloneRun(RunSummary source) => new()
    {
        SchemaVersion = source.SchemaVersion,
        RunId = source.RunId,
        SaveGenerationId = source.SaveGenerationId,
        NativeRaidId = source.NativeRaidId,
        StartedUtc = source.StartedUtc,
        EndedUtc = source.EndedUtc,
        ActiveDurationSeconds = source.ActiveDurationSeconds,
        WallClockDurationSeconds = source.WallClockDurationSeconds,
        Outcome = source.Outcome,
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        IntegrityTags = source.IntegrityTags,
        RecordEligible = source.RecordEligible,
        GameVersion = source.GameVersion,
        GameBuild = source.GameBuild,
        LifecycleCapability = source.LifecycleCapability,
        LifecycleAdapterVersion = source.LifecycleAdapterVersion,
        MovementCapability = source.MovementCapability,
        MovementAdapterVersion = source.MovementAdapterVersion,
        MapCapability = source.MapCapability,
        MapAdapterVersion = source.MapAdapterVersion,
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        EquipmentStatistics = EquipmentStatisticsReducer.Clone(source.EquipmentStatistics),
        TerminalLoadout = source.TerminalLoadout.Clone(),
        ContainerStatistics = ContainerStatisticsReducer.Clone(source.ContainerStatistics),
        StartingMapId = source.StartingMapId,
        StartingMapDisplayName = source.StartingMapDisplayName,
        StartingMapKnown = source.StartingMapKnown,
        EndingMapId = source.EndingMapId,
        EndingMapDisplayName = source.EndingMapDisplayName,
        EndingMapKnown = source.EndingMapKnown,
        RouteSignature = source.RouteSignature,
        Segments = source.Segments.Select(RouteStatisticsReducer.CloneSegment).ToList(),
        TransitionExcludedDistance = source.TransitionExcludedDistance,
        RouteCapabilities = RouteStatisticsReducer.CloneCapabilities(source.RouteCapabilities),
        RouteWasRepairedFromInvalidState = source.RouteWasRepairedFromInvalidState,
        SegmentEventAssociations = source.SegmentEventAssociations.Select(RouteStatisticsReducer.CloneAssociation).ToList(),
        HealingCaptureComplete = source.HealingCaptureComplete,
        ItemStatistics = ItemStatisticsAggregateReducer.Clone(source.ItemStatistics),
        Economy = EconomyStatisticsReducer.Clone(source.Economy),
        HistoricalEventAttributionIncomplete = source.HistoricalEventAttributionIncomplete,
        HistoricalEventAttributionProvenance = source.HistoricalEventAttributionProvenance
    };

    private static RouteAwareMapAggregate CloneRouteMap(RouteAwareMapAggregate source) => new()
    {
        MapId = source.MapId,
        DisplayName = source.DisplayName,
        IsKnown = source.IsKnown,
        RunsVisited = source.RunsVisited,
        SegmentVisits = source.SegmentVisits,
        ActiveDurationSeconds = source.ActiveDurationSeconds,
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        TransitionExcludedDistance = source.TransitionExcludedDistance,
        ItemStatistics = ItemStatisticsAggregateReducer.Clone(source.ItemStatistics),
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        EquipmentStatistics = EquipmentStatisticsReducer.Clone(source.EquipmentStatistics),
        ContainerStatistics = ContainerStatisticsReducer.Clone(source.ContainerStatistics),
        Economy = EconomyStatisticsReducer.Clone(source.Economy),
        HistoricalUnavailable = source.HistoricalUnavailable,
        WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
    };

    private static CapabilityRecord CloneCapability(CapabilityRecord source) => new()
    {
        AdapterId = source.AdapterId,
        State = source.State,
        Version = source.Version,
        Detail = source.Detail
    };

    private static RunDurationRecords CloneRunRecords(RunDurationRecords source) => new()
    {
        Extraction = ClonePair(source.Extraction),
        Death = ClonePair(source.Death),
        Maps = source.Maps.ToDictionary(
            entry => entry.Key,
            entry => new MapRunDurationRecords
            {
                MapId = entry.Value.MapId,
                DisplayName = entry.Value.DisplayName,
                Extraction = ClonePair(entry.Value.Extraction),
                Death = ClonePair(entry.Value.Death)
            },
            StringComparer.Ordinal)
    };

    private static DurationRecordPair ClonePair(DurationRecordPair source) => new()
    {
        Shortest = CloneRecord(source.Shortest),
        Longest = CloneRecord(source.Longest)
    };

    private static DurationRecordReference? CloneRecord(DurationRecordReference? source) => source == null
        ? null
        : new DurationRecordReference
        {
            RunId = source.RunId,
            ActiveDurationSeconds = source.ActiveDurationSeconds,
            StartedUtc = source.StartedUtc,
            MapId = source.MapId,
            MapDisplayName = source.MapDisplayName
        };

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
