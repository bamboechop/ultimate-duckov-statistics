using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.UI;

internal sealed class ItemUseProjectionBinding
{
    private readonly ProfileDocument profile;
    private readonly ItemUsePanelProjection items;
    private readonly string generation;
    public ItemUseProjectionBinding(StatisticsPanelProjection projection)
    { profile = projection.Profile; items = projection.ItemUse; generation = profile.GenerationId; }
    public bool Matches(StatisticsPanelProjection projection, string current) => current == generation
        && ReferenceEquals(profile, projection.Profile) && ReferenceEquals(items, projection.ItemUse)
        && ReferenceEquals(items.Overall, profile.Statistics.Overall)
        && items.Items.Count == profile.Statistics.Items.Count
        && items.Items.Select(item => item.ItemId).Distinct(StringComparer.Ordinal).Count() == items.Items.Count
        && items.Items.All(item => profile.Statistics.Items.TryGetValue(item.ItemId, out var source)
            && ReferenceEquals(item.Totals, source.Totals) && item.Group == source.Group)
        && items.RecentRuns.All(run => run.SaveGenerationId == current && !string.IsNullOrWhiteSpace(run.RunId)
            && profile.Statistics.Runs.Contains(run))
        && items.RecentRuns.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count() == items.RecentRuns.Count;
}

internal enum ItemUseEvidence { Supported, Partial, Unavailable }
internal sealed class ItemUseValue
{
    public string Text { get; }
    public ItemUseEvidence Evidence { get; }
    public ItemUseValue(string text, ItemUseEvidence evidence) { Text = text; Evidence = evidence; }
}
internal sealed class ItemUseEntry
{
    public string ItemId { get; }
    public string Name { get; }
    public CanonicalItemGroup Group { get; }
    public string GroupName { get; }
    public string Effects { get; }
    public IReadOnlyCollection<ItemEffectTag> EffectTags { get; }
    public long Count { get; }
    public ItemUseValue Uses { get; }
    public ItemUseValue Amount { get; }
    public ItemUseValue Health { get; }
    public ItemUseEntry(string id, string name, CanonicalItemGroup group, string groupName, string effects,
        long count, ItemUseValue uses, ItemUseValue amount, ItemUseValue health, IEnumerable<ItemEffectTag>? effectTags = null)
    { ItemId = id; Name = name; Group = group; GroupName = groupName; Effects = effects;
        Count = count; Uses = uses; Amount = amount; Health = health;
        EffectTags = Array.AsReadOnly(effectTags?.Distinct().ToArray() ?? Array.Empty<ItemEffectTag>()); }
    public bool Matches(CanonicalItemGroup group) => Group == group || EffectTags.Any(tag => group switch
    {
        CanonicalItemGroup.Healing => tag == ItemEffectTag.Healing,
        CanonicalItemGroup.RemedyDebuffRemoval => tag == ItemEffectTag.DebuffRemoval,
        CanonicalItemGroup.Food => tag == ItemEffectTag.Food,
        CanonicalItemGroup.Drink => tag == ItemEffectTag.Drink,
        CanonicalItemGroup.StimulantBuff => tag == ItemEffectTag.Buff,
        CanonicalItemGroup.Special => tag is ItemEffectTag.Special or ItemEffectTag.Throwable,
        _ => false
    });
}
internal sealed class ItemUseGroup
{
    public CanonicalItemGroup Group { get; }
    public string Name { get; }
    public long Count { get; }
    public ItemUseValue Uses { get; }
    public ItemUseGroup(CanonicalItemGroup group, string name, long count, ItemUseValue uses)
    { Group = group; Name = name; Count = count; Uses = uses; }
}
internal sealed class ItemUseRun
{
    public string RunId { get; }
    public string Title { get; }
    public string Caption { get; }
    public RetainedRunBadgeState Outcome { get; }
    public IReadOnlyList<ItemUseEntry> Items { get; }
    public string EmptyText { get; }
    public ItemUseRun(string id, string title, string caption, RetainedRunBadgeState outcome,
        IEnumerable<ItemUseEntry> items, string emptyText)
    { RunId = id; Title = title; Caption = caption; Outcome = outcome;
        Items = Array.AsReadOnly(items.ToArray()); EmptyText = emptyText; }
}
internal sealed class ItemUsePresentation
{
    public string GenerationId { get; }
    public bool Empty { get; }
    public string Notice { get; }
    public ItemUseValue Uses { get; }
    public ItemUseValue DifferentItems { get; }
    public ItemUseValue Health { get; }
    public IReadOnlyList<ItemUseEntry> Items { get; }
    public IReadOnlyList<ItemUseGroup> Groups { get; }
    public IReadOnlyList<ItemUseRun> RecentRuns { get; }
    public IReadOnlyCollection<string> ExpansionIds { get; }
    public ItemUsePresentation(string generation, bool empty, string notice, ItemUseValue uses, ItemUseValue different,
        ItemUseValue health, IEnumerable<ItemUseEntry> items, IEnumerable<ItemUseGroup> groups, IEnumerable<ItemUseRun> runs)
    {
        GenerationId = generation; Empty = empty; Notice = notice; Uses = uses; DifferentItems = different; Health = health;
        Items = Array.AsReadOnly(items.ToArray()); Groups = Array.AsReadOnly(groups.ToArray()); RecentRuns = Array.AsReadOnly(runs.ToArray());
        ExpansionIds = Array.AsReadOnly(Items.Select(item => "item:" + item.ItemId)
            .Concat(RecentRuns.Select(run => "run:" + run.RunId)).ToArray());
    }
    public bool CanRoute(string generation, string id) => generation == GenerationId && RecentRuns.Any(run => run.RunId == id);
}

internal static class ItemUsePresentationFactory
{
    public static ItemUsePresentation? Create(StatisticsPanelProjection projection, string generation,
        Func<string, string>? text = null, Func<DateTime, DateTime>? toLocal = null)
    {
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(projection.Profile, generation)
            || projection.ItemUseBinding?.Matches(projection, generation) != true) return null;
        var history = projection.Profile.Statistics.Runs;
        if (history.Any(run => run.SaveGenerationId != generation || string.IsNullOrWhiteSpace(run.RunId))
            || history.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count() != history.Count) return null;
        var runNumbers = history.OrderByDescending(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal)
            .Select((run, index) => (run.RunId, Number: history.Count - index))
            .ToDictionary(run => run.RunId, run => run.Number, StringComparer.Ordinal);
        var t = text ?? UiText.Get; var local = toLocal ?? (utc => utc.ToLocalTime());
        var source = projection.ItemUse;
        bool Supported(string id) => projection.Profile.Capabilities.Count(cap => cap.AdapterId == id) == 1
            && projection.Profile.Capabilities.Single(cap => cap.AdapterId == id).State == AdapterCapabilityState.Supported;
        var usesSupported = Supported("native-item-use"); var throwsSupported = Supported(ThrowableUseObservation.CapabilityId);
        var healthSupported = HealingSupported(projection.Profile.Capabilities); var repaired = source.WasRepairedFromInvalidState;
        var allUsesSupported = usesSupported && throwsSupported;
        var notices = new List<string>();
        if (!usesSupported) notices.Add(t("ui.item_use_tracking_unavailable"));
        if (!throwsSupported) notices.Add(t("ui.item_use_throwable_unavailable"));
        if (!healthSupported) notices.Add(t("ui.item_use_healing_unavailable"));
        if (repaired) notices.Add(t("ui.item_use_repaired"));
        var items = source.Items.Select(item => Entry(item.ItemId, item.DisplayName, item.Group, item.EffectTags, item.Totals,
            usesSupported, throwsSupported, healthSupported, repaired, t)).OrderByDescending(item => item.Count)
            .ThenBy(item => item.Name, StringComparer.Ordinal).ThenBy(item => item.ItemId, StringComparer.Ordinal).ToArray();
        var groups = Enum.GetValues(typeof(CanonicalItemGroup)).Cast<CanonicalItemGroup>().Select(group =>
        {
            var records = source.Groups.Where(value => value.Group == group).ToArray();
            var count = records.Length == 1 ? records[0].Uses : -1;
            // Successful throwable releases belong to Special; losing that adapter must
            // not suppress separately supported Food, Drink, Healing or other groups.
            var supported = usesSupported && (group != CanonicalItemGroup.Special || throwsSupported);
            return new ItemUseGroup(group, GroupName(group, t), count, Count(count, supported, repaired, t));
        }).OrderByDescending(group => group.Count).ThenBy(group => group.Group).ToArray();
        var runs = source.RecentRuns.OrderByDescending(run => run.EndedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal).Select(run =>
        {
            var aggregate = run.ItemStatistics; var incomplete = aggregate.WasRepairedFromInvalidState;
            var runItems = aggregate.Items.Values.Select(item => Entry(item.ItemId, item.DisplayName, item.Group, item.EffectTags,
                    item.Totals, usesSupported, throwsSupported, healthSupported, incomplete, t))
                .OrderByDescending(item => item.Count).ThenBy(item => item.Name, StringComparer.Ordinal)
                .ThenBy(item => item.ItemId, StringComparer.Ordinal).ToArray();
            // Historical absence cannot prove an empty run, but it does not add a
            // development-history notice to otherwise recorded item rows.
            var exactEmpty = !aggregate.HistoricalUnavailable && !incomplete && allUsesSupported;
            var usage = Count(aggregate.Overall.ActivationCount, allUsesSupported, incomplete
                || aggregate.HistoricalUnavailable && aggregate.Overall.ActivationCount == 0, t);
            var health = Number(aggregate.Overall.ActualHealthRestored, healthSupported, incomplete
                || aggregate.HistoricalUnavailable && aggregate.Overall.ActualHealthRestored == 0, t);
            var route = run.Segments.OrderBy(segment => segment.SegmentIndex).ToArray();
            var exactMaps = UiText.HasAvailableSegments(run) && !run.RouteWasRepairedFromInvalidState
                && route.All(segment => segment.MapKnown && !segment.WasRepairedFromInvalidState);
            var mapCount = route.Select(segment => segment.MapId).Distinct(StringComparer.Ordinal).Count();
            var maps = exactMaps ? mapCount.ToString(CultureInfo.InvariantCulture) + " " + t(mapCount == 1 ? "ui.runs_map" : "ui.runs_maps") : t("ui.unavailable");
            var metadata = new List<string> { t("ui.runs_run") + " " + runNumbers[run.RunId].ToString(CultureInfo.InvariantCulture),
                Timestamp(run.StartedUtc, local, t), maps };
            metadata.Add(Format(t(aggregate.Overall.ActivationCount == 1 ? "ui.item_use_use_value" : "ui.item_use_uses_value"), usage.Text));
            metadata.Add(Format(t("ui.item_use_hp_value"), health.Text));
            string Map(bool known, string name) => known && !string.IsNullOrWhiteSpace(name) ? name : t("ui.overview_latest_run_unknown_map");
            var title = route.Length > 0 ? Map(route[0].MapKnown, route[0].MapDisplayName)
                : Map(run.StartingMapKnown || run.MapKnown, run.StartingMapKnown ? run.StartingMapDisplayName : run.MapDisplayName);
            if (route.Length > 1 && route[0].MapId != route[route.Length - 1].MapId)
                title += " - " + Map(route[route.Length - 1].MapKnown, route[route.Length - 1].MapDisplayName);
            return new ItemUseRun(run.RunId, title, string.Join(" · ", metadata), RetainedRunBadgePresentationFactory.MapOutcome(run.Outcome),
                runItems, exactEmpty ? t("ui.item_use_no_run_uses") : t("ui.item_use_run_unavailable"));
        }).ToArray();
        return new ItemUsePresentation(generation, items.Length == 0 && source.Overall.ActivationCount == 0
            && source.Overall.ActualHealthRestored == 0 && allUsesSupported && healthSupported && !repaired,
            string.Join("\n", notices), Count(source.Overall.ActivationCount, allUsesSupported, repaired, t),
            Count(items.LongCount(item => item.Count > 0), allUsesSupported, repaired, t),
            Number(source.Overall.ActualHealthRestored, healthSupported, repaired, t), items, groups, runs);
    }

    private static ItemUseEntry Entry(string id, string name, CanonicalItemGroup group, IEnumerable<ItemEffectTag> effects,
        AggregateTotals totals, bool uses, bool throws, bool health, bool repaired, Func<string, string> t)
    {
        var tags = effects.Distinct().OrderBy(value => value).ToArray();
        var supported = tags.Contains(ItemEffectTag.Throwable) ? throws : uses;
        return new ItemUseEntry(id, StatisticsPanelProjectionFactory.StableDisplayName(name, id), group, GroupName(group, t),
            tags.Length == 0 ? t("ui.unavailable") : string.Join(", ", tags.Select(tag => EffectName(tag, t))),
            totals.ActivationCount, Count(totals.ActivationCount, supported, repaired, t), Amount(totals, supported, repaired, t),
            Number(totals.ActualHealthRestored, health, repaired, t), tags);
    }
    internal static string GroupName(CanonicalItemGroup group, Func<string, string> t) =>
        Enum.IsDefined(typeof(CanonicalItemGroup), group) ? t("ui.item_use_group_" + group.ToString().ToLowerInvariant()) : t("ui.unavailable");
    private static string EffectName(ItemEffectTag effect, Func<string, string> t) => effect switch
    {
        ItemEffectTag.Healing => GroupName(CanonicalItemGroup.Healing, t), ItemEffectTag.Food => GroupName(CanonicalItemGroup.Food, t),
        ItemEffectTag.Drink => GroupName(CanonicalItemGroup.Drink, t), ItemEffectTag.Special => GroupName(CanonicalItemGroup.Special, t),
        ItemEffectTag.Buff => t("ui.item_use_effect_buff"), ItemEffectTag.DebuffRemoval => t("ui.item_use_effect_debuffremoval"),
        ItemEffectTag.Throwable => t("ui.item_use_effect_throwable"), _ => t("ui.unavailable")
    };
    internal static ItemUseValue Amount(AggregateTotals totals, bool supported, bool repaired, Func<string, string> t)
    {
        var values = new List<string>(); var unknown = totals.AmountsByUnit.Count == 0; var positive = false;
        foreach (var amount in totals.AmountsByUnit.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var key = amount.Key switch { nameof(ConsumptionUnit.Item) => amount.Value == 1 ? "ui.item_use_item_unit" : "ui.item_use_items_unit",
                nameof(ConsumptionUnit.StackUnit) => amount.Value == 1 ? "ui.item_use_stack_one_unit" : "ui.item_use_stack_unit",
                nameof(ConsumptionUnit.Durability) => "ui.item_use_durability_unit", _ => "" };
            if (key.Length == 0 || !Finite(amount.Value)) { unknown = true; continue; }
            values.Add(amount.Value.ToString("N3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') + " " + t(key));
            positive |= amount.Value > 0;
        }
        if (unknown) values.Add(t("ui.unavailable"));
        if (values.Count == 0 || (!supported || repaired) && !positive) return Unavailable(t);
        var evidence = unknown || !supported || repaired ? values.Count == 1 && unknown ? ItemUseEvidence.Unavailable : ItemUseEvidence.Partial : ItemUseEvidence.Supported;
        var value = string.Join(", ", values);
        if ((!supported || repaired) && positive) value += " (" + t("ui.item_use_partial") + ")";
        return new ItemUseValue(value, evidence);
    }
    internal static ItemUseValue Count(long n, bool supported, bool repaired, Func<string, string> t) => n < 0 ? Unavailable(t)
        : Value(n.ToString("N0", CultureInfo.InvariantCulture), n > 0, supported, repaired, t);
    internal static bool HealingSupported(IReadOnlyList<CapabilityRecord> capabilities) =>
        capabilities.Count(cap => cap.AdapterId == "native-healing-attribution") == 1
        && capabilities.Single(cap => cap.AdapterId == "native-healing-attribution").State == AdapterCapabilityState.Supported;
    internal static ItemUseValue Number(double n, bool supported, bool repaired, Func<string, string> t, bool fixedPrecision = false) => !Finite(n) ? Unavailable(t)
        : Value(fixedPrecision ? n.ToString("N2", CultureInfo.InvariantCulture)
            : n.ToString("N3", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.'), n > 0, supported, repaired, t);
    private static ItemUseValue Value(string value, bool positive, bool supported, bool repaired, Func<string, string> t) => supported && !repaired
        ? new ItemUseValue(value, ItemUseEvidence.Supported) : positive ? new ItemUseValue(value + " (" + t("ui.item_use_partial") + ")", ItemUseEvidence.Partial) : Unavailable(t);
    private static ItemUseValue Unavailable(Func<string, string> t) => new(t("ui.unavailable"), ItemUseEvidence.Unavailable);
    private static bool Finite(double n) => n >= 0 && !double.IsNaN(n) && !double.IsInfinity(n);
    internal static string Format(string format, string value) => string.Format(CultureInfo.CurrentCulture, format, value);
    private static string Timestamp(DateTime utc, Func<DateTime, DateTime> local, Func<string, string> t)
    {
        if (utc == default) return t("ui.unavailable");
        try { return local(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString(RunDateStyle.Format, CultureInfo.InvariantCulture); }
        catch (ArgumentException) { return t("ui.unavailable"); }
    }
}
