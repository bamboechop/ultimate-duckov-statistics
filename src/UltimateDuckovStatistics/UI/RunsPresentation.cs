using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

// Only strings, identities, enums and read-only collections cross into the retained view.
internal sealed class RunsPresentation
{
    public string GenerationId { get; }
    public IReadOnlyList<RunDetailPresentation> Runs { get; }
    public RunsPresentation(string generationId, IEnumerable<RunDetailPresentation> runs)
    {
        GenerationId = generationId;
        Runs = Array.AsReadOnly(runs.ToArray());
    }
}

internal class RunDetailPresentation
{
    public string Id { get; }
    public string Title { get; }
    public string Metadata { get; }
    public virtual string Integrity { get; }
    public RetainedRunBadgeState Outcome { get; }
    public virtual IReadOnlyList<KeyValuePair<string, string>> Summary { get; }
    public virtual string RouteSummary { get; }
    public virtual IReadOnlyList<KeyValuePair<string, string>> Segments { get; }
    public virtual string EquipmentState { get; }
    public virtual TerminalLoadoutState TerminalState { get; }
    public virtual IReadOnlyList<RunSlotPresentation> Slots { get; }
    public virtual string Ranged { get; }
    public virtual string Melee { get; }
    public virtual string ValueNotice { get; }
    public virtual RunDetailPresentation Resolve() => this;
    public RunDetailPresentation(string id, string title, string metadata, string integrity,
        RetainedRunBadgeState outcome, IEnumerable<KeyValuePair<string, string>> summary,
        string routeSummary, IEnumerable<KeyValuePair<string, string>> segments,
        string equipmentState, TerminalLoadoutState terminalState, IEnumerable<RunSlotPresentation> slots, string ranged, string melee, string valueNotice)
    {
        Id = id; Title = title; Metadata = metadata; Integrity = integrity; Outcome = outcome;
        Summary = Array.AsReadOnly(summary.ToArray()); RouteSummary = routeSummary;
        Segments = Array.AsReadOnly(segments.ToArray()); EquipmentState = equipmentState;
        TerminalState = terminalState;
        Slots = Array.AsReadOnly(slots.ToArray()); Ranged = ranged; Melee = melee; ValueNotice = valueNotice;
    }
}

internal sealed class DeferredRunDetailPresentation : RunDetailPresentation
{
    private readonly Lazy<RunDetailPresentation> detail;
    public DeferredRunDetailPresentation(string id, string title, string metadata, RetainedRunBadgeState outcome, Func<RunDetailPresentation> load)
        : base(id, title, metadata, "", outcome, Array.Empty<KeyValuePair<string, string>>(), "", Array.Empty<KeyValuePair<string, string>>(), "", default, Array.Empty<RunSlotPresentation>(), "", "", "")
    { detail = new Lazy<RunDetailPresentation>(load); }
    public override RunDetailPresentation Resolve() => detail.Value;
    public override string Integrity => detail.Value.Integrity;
    public override IReadOnlyList<KeyValuePair<string, string>> Summary => detail.Value.Summary;
    public override string RouteSummary => detail.Value.RouteSummary;
    public override IReadOnlyList<KeyValuePair<string, string>> Segments => detail.Value.Segments;
    public override string EquipmentState => detail.Value.EquipmentState;
    public override TerminalLoadoutState TerminalState => detail.Value.TerminalState;
    public override IReadOnlyList<RunSlotPresentation> Slots => detail.Value.Slots;
    public override string Ranged => detail.Value.Ranged;
    public override string Melee => detail.Value.Melee;
    public override string ValueNotice => detail.Value.ValueNotice;
}

internal sealed class RunEquipmentEvidence
{
    public EquipmentSlotState State { get; }
    public string ItemId { get; }
    public string ItemName { get; }
    public string SlotName { get; }
    public RunEquipmentEvidence(EquipmentSlotState state, string itemId, string itemName, string slotName)
    { State = state; ItemId = itemId; ItemName = itemName; SlotName = slotName; }
}

internal sealed class RunSlotPresentation
{
    public string SlotId { get; }
    public EquipmentSlotState State { get; }
    public string ItemId { get; }
    public string Text { get; }
    public IReadOnlyList<EquipmentSlotState> Attachments { get; }
    public bool NestedComplete { get; }
    public IReadOnlyList<RunEquipmentEvidence> Evidence { get; }
    public bool CanOpenDetails => State == EquipmentSlotState.Occupied && (Attachments.Count > 0 || !NestedComplete);
    public RunSlotPresentation(string slotId, EquipmentSlotState state, string itemId, string text,
        IEnumerable<EquipmentSlotState>? attachments = null, bool nestedComplete = true,
        IEnumerable<RunEquipmentEvidence>? evidence = null)
    {
        SlotId = slotId; State = state; ItemId = itemId; Text = text;
        Attachments = Array.AsReadOnly(attachments?.ToArray() ?? Array.Empty<EquipmentSlotState>());
        NestedComplete = nestedComplete;
        Evidence = Array.AsReadOnly(evidence?.ToArray() ?? new[] { new RunEquipmentEvidence(state, itemId, text, string.Empty) });
    }
}

internal sealed class RunsSelection
{
    private string? retainedGeneration;
    private string? retainedId;
    public RunsPresentation? Snapshot { get; private set; }
    public string? SelectedId { get; private set; }
    public RunDetailPresentation? Selected
    {
        get
        {
            try { return Snapshot?.Runs.FirstOrDefault(run => run.Id == SelectedId)?.Resolve(); }
            catch (Exception exception) when (exception is IOException or InvalidDataException or System.Runtime.Serialization.SerializationException or KeyNotFoundException or ArgumentException or InvalidOperationException)
            { RequestedRunUnavailable = true; DetailFailure = exception; return null; }
        }
    }
    public Exception? DetailFailure { get; private set; }
    public bool RequestedRunUnavailable { get; private set; }

    public bool Refresh(RunsPresentation snapshot, string expectedGeneration)
    {
        if (string.IsNullOrWhiteSpace(expectedGeneration) || snapshot.GenerationId != expectedGeneration)
        { Invalidate(); return false; }
        var sameGeneration = (Snapshot?.GenerationId ?? retainedGeneration) == snapshot.GenerationId;
        var previousId = SelectedId ?? retainedId;
        Snapshot = snapshot;
        SelectedId = sameGeneration && snapshot.Runs.Any(run => run.Id == previousId) ? previousId
            : snapshot.Runs.Count == 0 ? null : snapshot.Runs[0].Id;
        retainedGeneration = null; retainedId = null;
        RequestedRunUnavailable = false;
        DetailFailure = null;
        return true;
    }

    public bool Select(string id)
    {
        if (Snapshot?.Runs.Any(run => run.Id == id) != true) return false;
        SelectedId = id; RequestedRunUnavailable = false; DetailFailure = null; return true;
    }

    public bool Route(string generation, string id)
    {
        if (Snapshot?.GenerationId == generation && Select(id)) return true;
        SelectedId = null; RequestedRunUnavailable = true; return false;
    }

    public void Invalidate()
    {
        if (Snapshot != null) { retainedGeneration = Snapshot.GenerationId; retainedId = SelectedId; }
        Snapshot = null; SelectedId = null; RequestedRunUnavailable = true;
    }
}

internal static class RunsPresentationFactory
{
    public static RunsPresentation? Create(StatisticsPanelProjection projection, string expectedGeneration,
        Func<string, string>? text = null, Func<DateTime, DateTime>? toLocal = null)
    {
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(projection.Profile, expectedGeneration)
            || !RunHistory.Matches(projection.Profile.Statistics.Runs, projection.Runs.Runs, expectedGeneration)) return null;
        var resolve = text ?? UiText.Get;
        var local = toLocal ?? (value => value.ToLocalTime());
        if (projection.Runs.Runs is RunHistoryView indexed && indexed.Source is IIndexedRunHistory)
            return new RunsPresentation(expectedGeneration, indexed.Overview.Select((row, index) =>
                CreateHistoryRow(row, indexed.Count - index, resolve, local, projection.Names,
                    () => CreateRun(RunHistory.GetById(indexed.Source, row.RunId), indexed.Count - index, resolve, local, projection.Names))));
        var runs = projection.Runs.Runs.OrderByDescending(run => run.StartedUtc)
            .ThenBy(run => run.RunId, StringComparer.Ordinal).ToArray();
        if (runs.Any(run => run.SaveGenerationId != expectedGeneration || string.IsNullOrWhiteSpace(run.RunId))
            || runs.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count() != runs.Length) return null;
        return new RunsPresentation(expectedGeneration, runs.Select((run, index) =>
            CreateRun(run, runs.Length - index, resolve, toLocal ?? (value => value.ToLocalTime()), projection.Names)));
    }

    private static DeferredRunDetailPresentation CreateHistoryRow(RunOverview row, int number, Func<string, string> t,
        Func<DateTime, DateTime> toLocal, EntityDisplayNames names, Func<RunDetailPresentation> load)
    {
        var maps = row.RouteExact && row.RouteMapsKnown ? Plural(row.DistinctMapCount, "map", t) : t("ui.unavailable");
        var title = Map(row.FirstMapKnown, names.Get(row.FirstMapId, row.FirstMapDisplayName), t);
        if (row.LastMapId != row.FirstMapId) title += " - " + Map(row.LastMapKnown, names.Get(row.LastMapId, row.LastMapDisplayName), t);
        var stamp = t("ui.unavailable");
        if (row.StartedUtc != default)
        {
            try { stamp = toLocal(DateTime.SpecifyKind(row.StartedUtc, DateTimeKind.Utc)).ToString(RunDateStyle.Format, CultureInfo.InvariantCulture); }
            catch { /* Match the established unavailable timestamp behavior. */ }
        }
        return new DeferredRunDetailPresentation(row.RunId, title, $"{t("ui.runs_run")} {number} · {stamp} · {maps}", RetainedRunBadgePresentationFactory.MapOutcome(row.Outcome), load);
    }

    private static RunDetailPresentation CreateRun(RunSummary run, int number,
        Func<string, string> t, Func<DateTime, DateTime> toLocal, EntityDisplayNames names)
    {
        var data = new RunDataProjection(run);
        var combat = run.CombatStatistics;
        var c = combat.Capabilities;
        var v = combat.Totals;
        var broken = combat.WasRepairedFromInvalidState;
        var hasPartialValues = false;
        Func<string, string> Partial = value => { hasPartialValues = true; return value + "*"; };
        string Metric(double value, MetricAvailability capability, bool partial = false) =>
            Format(value, capability.State == AdapterCapabilityState.Supported && !broken && !partial, t, formatPartial: Partial);
        string Count(long value, MetricAvailability capability, bool partial = false) =>
            FormatCount(value, capability.State == AdapterCapabilityState.Supported && !broken && !partial, t, Partial);
        var routeExact = !run.RouteWasRepairedFromInvalidState
            && run.RouteCapabilities.OrderedRoute.State == AdapterCapabilityState.Supported
            && run.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported;
        var mapsExact = routeExact && run.Segments.Count > 0 && run.Segments.All(segment => segment.MapKnown);
        var maps = mapsExact ? Plural(run.Segments.Select(segment => segment.MapId).Distinct(StringComparer.Ordinal).Count(), "map", t) : t("ui.unavailable");
        var routeSummary = Plural(run.Segments.Count, "segment", t) + " · " + maps;
        if (!routeExact) routeSummary = Partial(routeSummary);
        var first = run.Segments.FirstOrDefault();
        var last = run.Segments.LastOrDefault();
        var title = first != null ? Map(first.MapKnown, names.Get(first.MapId, first.MapDisplayName), t)
            : Map(run.StartingMapKnown, names.Get(run.StartingMapId, run.StartingMapDisplayName), t);
        if (last != null && last.MapId != first!.MapId) title += " - " + Map(last.MapKnown, names.Get(last.MapId, last.MapDisplayName), t);
        var stamp = t("ui.unavailable");
        if (run.StartedUtc != default)
        {
            try { stamp = toLocal(DateTime.SpecifyKind(run.StartedUtc, DateTimeKind.Utc)).ToString(RunDateStyle.Format, CultureInfo.InvariantCulture); }
            catch { /* Invalid timestamps remain unavailable. */ }
        }
        var metadata = $"{t("ui.runs_run")} {number} · {stamp} · {maps}";
        var integrity = $"{t("ui.runs_integrity")}: {run.IntegrityTags} · "
            + t(run.RecordEligible ? "ui.runs_eligible" : "ui.runs_ineligible");
        var mapTotalsSupported = run.RouteCapabilities.RouteAwareMapTotals.State == AdapterCapabilityState.Supported;
        var headshots = Count(v.Headshots, c.Headshots) + " (" + Count(v.HeadshotFinalBlows, c.HeadshotFinalBlows) + " " + t("ui.runs_final_blows") + ")";
        string Accuracy(double? ratio, bool empty) => ratio.HasValue ? ratio.Value.ToString("P2", CultureInfo.InvariantCulture)
            : empty ? "—" : t("ui.unavailable");
        var accuracy = Accuracy(CombatAccuracyProjection.Overall(v, c, broken), CombatAccuracyProjection.OverallIsEmpty(v, c, broken));
        var cash = run.Economy.Currencies.TryGetValue(CurrencyKind.Cash.ToString(), out var currency) ? currency.Totals.NetFlow : 0;
        // Run totals are captured independently of source/outcome segment joins.
        var cashExact = !run.Economy.WasRepairedFromInvalidState
            && run.Economy.Capabilities.CashAmountDirection.State == AdapterCapabilityState.Supported;
        var cashText = cashExact ? cash.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture)
            : cash != 0 ? Partial(cash.ToString("+#,0;-#,0;0", CultureInfo.InvariantCulture)) : t("ui.unavailable");
        var summary = new[]
        {
            Pair(t("ui.overview_latest_run_active_time"), Duration(run.ActiveDurationSeconds, run.LifecycleCapability == AdapterCapabilityState.Supported, t)),
            Pair(t("ui.overview_latest_run_distance"), Distance(run.PhysicalDistance, run.MovementCapability == AdapterCapabilityState.Supported, t)),
            Pair(t("ui.overview_kills_by_you"), Count(v.KillsByYou, c.KillsByYou)),
            Pair(t("ui.runs_containers"), Containers(run.ContainerStatistics, false, t, Partial)),
            Pair(t("ui.runs_cash_net"), cashText),
            Pair(t("ui.overview_damage_dealt"), Metric(v.DamageDealt, c.DamageDealt)),
            Pair(t("ui.overview_damage_taken"), Metric(v.DamageReceived, c.DamageReceived)),
            Pair(t("ui.runs_accuracy"), accuracy), Pair(t("ui.runs_headshots"), headshots),
            Pair(t("ui.runs_hp"), Format(run.ItemStatistics.Overall.ActualHealthRestored,
                run.HealingCaptureComplete && !run.ItemStatistics.WasRepairedFromInvalidState, t, health: true, formatPartial: Partial))
        };
        var segments = run.Segments.Select((segment, index) =>
        {
            var exact = routeExact && !segment.WasRepairedFromInvalidState;
            var eventsExact = exact && mapTotalsSupported;
            var kills = FormatCount(segment.CombatStatistics.Totals.KillsByYou, eventsExact
                && !segment.CombatStatistics.WasRepairedFromInvalidState
                && segment.CombatStatistics.Capabilities.KillsByYou.State == AdapterCapabilityState.Supported, t, Partial);
            var firing = FormatCount(segment.WeaponStatistics.Totals.FiringActions, eventsExact
                && !segment.WeaponStatistics.WasRepairedFromInvalidState
                && segment.WeaponStatistics.Capabilities.FiringActions.State == AdapterCapabilityState.Supported, t, Partial);
            var containers = Containers(segment.ContainerStatistics, !eventsExact, t, Partial);
            var facts = Duration(segment.ActiveDurationSeconds, exact && run.LifecycleCapability == AdapterCapabilityState.Supported, t)
                + " · " + Distance(segment.PhysicalDistance, exact && run.MovementCapability == AdapterCapabilityState.Supported, t)
                + " · " + SegmentActivity(segment, eventsExact, kills, firing, containers, t, Partial);
            return Pair($"{index + 1}  {Map(segment.MapKnown, names.Get(segment.MapId, segment.MapDisplayName), t)}", facts);
        }).ToArray(); // Resolve partial markers before constructing their shared explanation.
        var rangedKills = Count(data.RangedKills, c.KillsByYou, !data.RangedMeleeExact);
        var meleeKills = Count(data.MeleeKills, c.KillsByYou, !data.RangedMeleeExact);
        var ranged = Unit(FormatCount(run.WeaponStatistics.Totals.FiringActions,
                !run.WeaponStatistics.WasRepairedFromInvalidState && run.WeaponStatistics.Capabilities.FiringActions.State == AdapterCapabilityState.Supported, t, Partial), "firing_action", t)
            + "\n" + Unit(Count(v.RangedHits, c.RangedHits), "hit", t)
            + "\n" + Unit(Count(v.Headshots, c.Headshots), "headshot", t)
            + "\n" + Count(v.HeadshotFinalBlows, c.HeadshotFinalBlows) + " " + t("ui.runs_headshot_final_blows")
            + "\n" + Unit(rangedKills, "kill", t)
            + "\n" + t("ui.accuracy") + ": " + Accuracy(CombatAccuracyProjection.Ranged(v, c, broken), CombatAccuracyProjection.RangedIsEmpty(v, c, broken));
        var melee = Unit(Count(v.MeleeSwings, c.MeleeSwings), "swing", t)
            + "\n" + Unit(Count(v.MeleeHits, c.MeleeHits), "hit", t) + "\n" + Unit(meleeKills, "kill", t)
            + "\n" + t("ui.melee_accuracy") + ": " + Accuracy(CombatAccuracyProjection.Melee(v, c, broken), CombatAccuracyProjection.MeleeIsEmpty(v, c, broken));
        if (!data.RangedMeleeExact)
        {
            var classification = t("ui.runs_classification_partial");
            ranged += "\n" + classification; melee += "\n" + classification;
        }
        var slots = data.TerminalSlots.Select(slot => PresentSlot(slot, t, names));
        var equipmentState = t("ui.runs_terminal_" + data.TerminalState.ToString().ToLowerInvariant());
        var valueNotice = hasPartialValues ? t("ui.runs_partial_values_notice") : string.Empty;
        if (!mapTotalsSupported)
            valueNotice += (valueNotice.Length > 0 ? "\n" : string.Empty) + t("ui.runs_segment_attribution_notice");
        return new RunDetailPresentation(run.RunId, title, metadata, integrity,
            RetainedRunBadgePresentationFactory.MapOutcome(run.Outcome), summary, routeSummary, segments,
            equipmentState, data.TerminalState, slots, ranged, melee, valueNotice);
    }

    internal static RunSlotPresentation PresentSlot(TerminalRootSlot slot, Func<string, string> t, EntityDisplayNames? names = null)
    {
        names ??= EntityDisplayNames.Recorded;
        string Name(EquipmentSlotState state, string displayName) => state == EquipmentSlotState.Empty
            ? t("ui.runs_empty_slot") : state == EquipmentSlotState.Occupied && !string.IsNullOrWhiteSpace(displayName)
                ? displayName : t("ui.unavailable");
        var evidence = new List<RunEquipmentEvidence>
        { new(slot.State, slot.ItemId, Name(slot.State, names.Get(slot.ItemId, slot.ItemDisplayName)), names.Get(slot.SlotId, slot.DisplayName)) };
        foreach (var child in slot.NestedSlots)
        {
            evidence.Add(new RunEquipmentEvidence(child.State, child.ItemId,
                Name(child.State, names.Get(child.ItemId, child.ItemDisplayName)), NestedSlotName(slot, child, names)));
        }
        var detail = string.Join("\n", evidence.Select(row => row.SlotName + ": " + row.ItemName));
        if (!slot.NestedComplete && slot.State != EquipmentSlotState.Empty) detail += "\n" + t("ui.runs_nested_partial");
        return new RunSlotPresentation(slot.SlotId, slot.State, slot.ItemId, detail,
            slot.NestedSlots.Select(child => child.State), slot.NestedComplete, evidence);
    }

    private static string NestedSlotName(TerminalRootSlot root, TerminalNestedSlot child, EntityDisplayNames names)
    {
        var suffix = child.SlotKey.Length.ToString(CultureInfo.InvariantCulture) + ":" + child.SlotKey + "/";
        if (!child.Path.EndsWith(suffix, StringComparison.Ordinal)) return child.DisplayName;
        var parentPath = child.Path.Substring(0, child.Path.Length - suffix.Length);
        if (parentPath.Length == 0) return names.Slot(root.ItemId, child.SlotKey, child.DisplayName);
        var parents = root.NestedSlots.Where(s => s.Path == parentPath).Take(2).ToArray();
        return parents.Length == 1 && parents[0].State == EquipmentSlotState.Occupied
            ? names.Slot(parents[0].ItemId, child.SlotKey, child.DisplayName) : child.DisplayName;
    }

    private static string SegmentActivity(MapSegmentSummary segment, bool eventsExact, string kills,
        string firing, string containers, Func<string, string> t, Func<string, string> formatPartial)
    {
        var combat = segment.CombatStatistics;
        var totals = combat.Totals;
        var capabilities = combat.Capabilities;
        var firingExact = !segment.WeaponStatistics.WasRepairedFromInvalidState
            && segment.WeaponStatistics.Capabilities.FiringActions.State == AdapterCapabilityState.Supported;
        var combatComplete = eventsExact && firingExact && !combat.WasRepairedFromInvalidState
            && new[] { capabilities.DamageDealt, capabilities.DamageReceived, capabilities.RangedHits,
                capabilities.MeleeSwings, capabilities.MeleeHits, capabilities.KillsByYou,
                capabilities.Headshots, capabilities.HeadshotFinalBlows, capabilities.PlayerDeaths,
                capabilities.ObservedWorldDeaths, capabilities.Accuracy }.All(metric => metric.State == AdapterCapabilityState.Supported);
        var containersComplete = eventsExact
            && !segment.ContainerStatistics.WasRepairedFromInvalidState
            && segment.ContainerStatistics.Capabilities.UniqueContainersLooted.State == AdapterCapabilityState.Supported;
        var noCombat = combatComplete && containersComplete
            && totals.DamageCaused == 0 && totals.DamageDealt == 0 && totals.DamageReceived == 0
            && totals.RangedHits == 0 && totals.MeleeSwings == 0 && totals.MeleeHits == 0
            && totals.KillsByYou == 0 && totals.Headshots == 0 && totals.HeadshotFinalBlows == 0
            && totals.PlayerDeaths == 0 && totals.ObservedWorldDeaths == 0
            && totals.CompletedPlayerProjectiles == 0 && segment.WeaponStatistics.Totals.FiringActions == 0;
        // A formatted exact zero is emitted only after the container evidence gates pass.
        var noContainers = combatComplete && containersComplete && containers == "0";
        if (noCombat && noContainers) return t("ui.runs_no_combat_or_containers");
        var facts = new List<string>();
        if (noCombat) facts.Add(t("ui.runs_no_combat"));
        else
        {
            if (kills != "0") facts.Add(Unit(kills, "kill", t));
            if (firing != "0") facts.Add(Unit(firing, "firing_action", t));
            var combatExact = eventsExact && !combat.WasRepairedFromInvalidState;
            var hasPrimaryActivity = totals.KillsByYou > 0 || segment.WeaponStatistics.Totals.FiringActions > 0;
            if (!hasPrimaryActivity && totals.MeleeSwings > 0) facts.Add(Unit(FormatCount(totals.MeleeSwings,
                combatExact && capabilities.MeleeSwings.State == AdapterCapabilityState.Supported, t, formatPartial), "swing", t));
            if (!hasPrimaryActivity && totals.DamageDealt > 0) facts.Add(t("ui.overview_damage_dealt") + ": " + Format(totals.DamageDealt,
                combatExact && capabilities.DamageDealt.State == AdapterCapabilityState.Supported, t, formatPartial: formatPartial));
            if (!hasPrimaryActivity && totals.DamageReceived > 0) facts.Add(t("ui.overview_damage_taken") + ": " + Format(totals.DamageReceived,
                combatExact && capabilities.DamageReceived.State == AdapterCapabilityState.Supported, t, formatPartial: formatPartial));
            if (facts.Count == 0 && totals.RangedHits > 0) facts.Add(t("ui.runs_ranged") + ": " + Unit(FormatCount(totals.RangedHits,
                combatExact && capabilities.RangedHits.State == AdapterCapabilityState.Supported, t, formatPartial), "hit", t));
            if (facts.Count == 0 && totals.MeleeHits > 0) facts.Add(t("ui.runs_melee") + ": " + Unit(FormatCount(totals.MeleeHits,
                combatExact && capabilities.MeleeHits.State == AdapterCapabilityState.Supported, t, formatPartial), "hit", t));
            if (facts.Count == 0 && combatComplete && (totals.DamageCaused > 0 || totals.CompletedPlayerProjectiles > 0
                || totals.Headshots > 0 || totals.HeadshotFinalBlows > 0 || totals.ObservedWorldDeaths > 0 || totals.PlayerDeaths > 0))
                facts.Add(t("ui.runs_combat_activity"));
            if (facts.Count == 0) facts.Add(t("ui.runs_combat") + ": " + t("ui.unavailable"));
        }
        facts.Add(noContainers ? t("ui.runs_no_containers") : Unit(containers, "container", t));
        return string.Join(" · ", facts);
    }

    private static KeyValuePair<string, string> Pair(string label, string value) => new(label, value);
    private static string Map(bool known, string name, Func<string, string> t) => known && !string.IsNullOrWhiteSpace(name) ? name : t("ui.overview_latest_run_unknown_map");
    private static string Plural(int count, string unit, Func<string, string> t) => Unit(count.ToString(CultureInfo.InvariantCulture), unit, t);
    private static string Unit(string value, string unit, Func<string, string> t) => value + " " + t("ui.runs_" + unit + (value is "1" or "1*" ? "" : unit == "headshot" || unit == "container" ? "s_plural" : "s"));
    private static string Duration(double value, bool exact, Func<string, string> t) => exact && RetainedRunDurationFormatter.TryFormat(value, out var formatted) ? formatted : t("ui.unavailable");
    private static string Distance(double value, bool exact, Func<string, string> t) => exact && value >= 0 && !double.IsInfinity(value)
        ? value >= 1000 ? (value / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " km" : value.ToString("0.##", CultureInfo.InvariantCulture) + " m" : t("ui.unavailable");
    private static string Containers(ContainerStatisticsAggregate value, bool partial, Func<string, string> t, Func<string, string> formatPartial) => FormatCount(value.UniqueContainersLooted,
        !partial && !value.WasRepairedFromInvalidState && value.Capabilities.UniqueContainersLooted.State == AdapterCapabilityState.Supported, t, formatPartial);
    internal static string FormatCount(long value, bool exact, Func<string, string> t, Func<string, string>? formatPartial = null) => value < 0 ? t("ui.unavailable")
        : exact ? value.ToString(CultureInfo.InvariantCulture) : value > 0 ? PartialText(value.ToString(CultureInfo.InvariantCulture), t, formatPartial) : t("ui.unavailable");
    internal static string Format(double value, bool exact, Func<string, string> t, bool health = false, Func<string, string>? formatPartial = null)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0 || !exact && value == 0) return t("ui.unavailable");
        var formatted = health ? UiText.FormatHealth(value) : value.ToString("0.##", CultureInfo.InvariantCulture);
        return exact ? formatted : PartialText(formatted, t, formatPartial);
    }
    private static string PartialText(string value, Func<string, string> t, Func<string, string>? formatPartial) =>
        formatPartial != null ? formatPartial(value) : value + " (" + t("ui.runs_partial") + ")";
}

internal static class RunsLayoutPolicy
{
    public const float Gap = 40;
    public const float Padding = 30;
    public static bool Stack(float viewportPixels) => viewportPixels < 1180;
    public static float HistoryWidth(float width, bool stacked) => stacked ? width : (width - Gap) / 3;
    public static int SummaryColumns(bool stacked) => stacked ? 2 : 5;
    public static float Reveal(float offset, float viewport, float content, float top, float height) =>
        Math.Clamp(top < offset ? top : top + height > offset + viewport ? top + height - viewport : offset, 0, Math.Max(0, content - viewport));
}

internal static class RunsItemIconPolicy
{
    public static T? Resolve<T>(RunSlotPresentation slot, Func<string, T?> resolve) where T : class
        => Resolve<T>(slot.State, slot.ItemId, resolve);
    public static T? Resolve<T>(RunEquipmentEvidence item, Func<string, T?> resolve) where T : class
        => Resolve<T>(item.State, item.ItemId, resolve);
    private static T? Resolve<T>(EquipmentSlotState state, string itemId, Func<string, T?> resolve) where T : class
    {
        if (state != EquipmentSlotState.Occupied) return null;
        try { return resolve(itemId); }
        catch { return null; } // The renderer retains identity and its deterministic question-mark fallback.
    }
}

internal static class RunsHistoryWindow
{
    public static (int First, int End) Visible(float[] tops, float offset, float viewport)
    {
        var first = Array.BinarySearch(tops, offset);
        if (first < 0) first = Math.Max(0, ~first - 1);
        var end = first;
        while (end < tops.Length && tops[end] < offset + viewport + 160) end++;
        return (first, end);
    }
}

internal static class RunsScrollPolicy
{
    public static bool Forward(float viewport, float content, float offset, float direction) =>
        content <= viewport || (direction < 0 && offset <= .5f)
        || (direction > 0 && offset >= content - viewport - .5f);
}

internal sealed class RunsHistoryRowLayout
{
    public float TitleLeft { get; private set; }
    public float TitleTop { get; private set; }
    public float TitleWidth { get; private set; }
    public float MetadataTop { get; private set; }
    public float Height { get; private set; }
    public static RunsHistoryRowLayout Create(float width, float badgeWidth, float badgeHeight,
        Func<float, float> measureTitleHeight, float metadataHeight)
    {
        var stack = badgeWidth > (width - 40) * .45f;
        var left = stack ? 20 : 30 + badgeWidth;
        var top = stack ? 20 + badgeHeight : 10;
        var titleWidth = Math.Max(1, width - left - 20);
        var metadataTop = Math.Max(10 + badgeHeight, top + measureTitleHeight(titleWidth)) + 4;
        return new RunsHistoryRowLayout
        {
            TitleLeft = left,
            TitleTop = top,
            TitleWidth = titleWidth,
            MetadataTop = metadataTop,
            Height = Math.Max(96, metadataTop + metadataHeight + 12)
        };
    }
}

internal sealed class RunsControlPool<T> : IDisposable where T : IDisposable
{
    private readonly List<T> items = new();
    private readonly Func<T> create;
    private bool disposed;
    public IReadOnlyList<T> Items => items;
    public RunsControlPool(Func<T> create) => this.create = create;
    public void Ensure(int count)
    {
        if (disposed) return;
        while (items.Count < count) items.Add(create());
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var item in items) item.Dispose();
        items.Clear();
    }
}
