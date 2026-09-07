using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

internal sealed class EquipmentProjectionBinding
{
    private readonly ProfileDocument profile;
    private readonly EquipmentStatisticsViewModel equipment;
    private readonly EquipmentCompositionEvidence composition;
    private readonly object definitions, typedStates, activeDefinitions, emptySlots, runRows, lifetimeLoadouts;
    private readonly IReadOnlyList<EquipmentDurationAggregate> recurring, states, sets;
    private readonly IReadOnlyList<RunSummary> recent;
    private readonly RunStatisticsViewModel runs;
    private readonly string generation;
    public EquipmentProjectionBinding(StatisticsPanelProjection p)
    {
        profile = p.Profile; equipment = p.Equipment; composition = equipment.Lifetime.Composition; recurring = p.RecurringLoadouts;
        states = p.TotemStates; sets = p.TotemSets; recent = p.RecentEquipmentRuns; runs = p.Runs; generation = profile.GenerationId;
        definitions = composition.Loadouts; typedStates = composition.TotemStates; activeDefinitions = composition.ActiveTotemSets;
        emptySlots = composition.EmptyDirectSlots; runRows = runs.Runs; lifetimeLoadouts = equipment.Lifetime.Loadouts;
    }
    public bool Matches(StatisticsPanelProjection p, string current) => generation == current && profile.GenerationId == current
        && profile.Statistics.SaveGenerationId == current && ReferenceEquals(profile, p.Profile) && ReferenceEquals(equipment, p.Equipment)
        && ReferenceEquals(equipment.Lifetime, profile.Statistics.RunTotals.EquipmentStatistics) && ReferenceEquals(composition, equipment.Lifetime.Composition)
        && ReferenceEquals(lifetimeLoadouts, equipment.Lifetime.Loadouts)
        && ReferenceEquals(recurring, p.RecurringLoadouts) && ReferenceEquals(states, p.TotemStates) && ReferenceEquals(sets, p.TotemSets)
        && ReferenceEquals(recent, p.RecentEquipmentRuns) && ReferenceEquals(runs, p.Runs) && ReferenceEquals(runRows, runs.Runs)
        && ReferenceEquals(definitions, composition.Loadouts) && ReferenceEquals(typedStates, composition.TotemStates)
        && ReferenceEquals(activeDefinitions, composition.ActiveTotemSets) && ReferenceEquals(emptySlots, composition.EmptyDirectSlots);
}

internal sealed class EquipmentEntry
{
    public string Id { get; }
    public string Name { get; }
    public string ItemId { get; }
    public double Duration { get; }
    public string Caption { get; }
    public string Notice { get; }
    public IReadOnlyList<EquipmentGroup> Groups { get; }
    public IReadOnlyList<RunSlotPresentation> Slots { get; }
    public string RunId { get; }
    public bool Expandable => Groups.Count > 0;
    public EquipmentEntry(string id, string name, string itemId, double duration, string caption = "", string notice = "",
        IEnumerable<EquipmentGroup>? groups = null, IEnumerable<RunSlotPresentation>? slots = null, string runId = "")
    {
        Id = id; Name = name; ItemId = itemId; Duration = duration; Caption = caption; Notice = notice; RunId = runId;
        Groups = Array.AsReadOnly(groups?.ToArray() ?? Array.Empty<EquipmentGroup>());
        Slots = Array.AsReadOnly(slots?.ToArray() ?? Array.Empty<RunSlotPresentation>());
    }
}
internal sealed class EquipmentGroup
{
    public string Name { get; }
    public string Notice { get; }
    public IReadOnlyList<EquipmentEntry> Rows { get; }
    public EquipmentGroup(string name, IEnumerable<EquipmentEntry> rows, string notice = "")
    { Name = name; Notice = notice; Rows = Array.AsReadOnly(rows.ToArray()); }
}
internal sealed class EquipmentPresentation
{
    public string GenerationId { get; }
    public EquipmentEntry? MostUsed { get; }
    public IReadOnlyList<EquipmentEntry> SelectedWeapons { get; }
    public IReadOnlyList<EquipmentEntry> Recent { get; }
    public IReadOnlyList<EquipmentEntry> Weapons { get; }
    public IReadOnlyList<EquipmentGroup> Armor { get; }
    public IReadOnlyList<EquipmentEntry> DirectTotems { get; }
    public IReadOnlyList<EquipmentEntry> EmptySlots { get; }
    public IReadOnlyList<EquipmentEntry> ActiveSets { get; }
    public IReadOnlyList<EquipmentEntry> ToteTotems { get; }
    public IReadOnlyDictionary<string, string> Notices { get; }
    public IReadOnlyCollection<string> ExpansionIds { get; }
    public IReadOnlyDictionary<string, RunSlotPresentation> InspectableSlots { get; }
    public EquipmentPresentation(string generation, EquipmentEntry? mostUsed, IEnumerable<EquipmentEntry> selected, IEnumerable<EquipmentEntry> recent,
        IEnumerable<EquipmentEntry> weapons, IEnumerable<EquipmentGroup> armor, IEnumerable<EquipmentEntry> direct, IEnumerable<EquipmentEntry> empty,
        IEnumerable<EquipmentEntry> sets, IEnumerable<EquipmentEntry> tote, IDictionary<string, string> notices)
    {
        GenerationId = generation; MostUsed = mostUsed; SelectedWeapons = Copy(selected); Recent = Copy(recent); Weapons = Copy(weapons);
        Armor = Array.AsReadOnly(armor.ToArray()); DirectTotems = Copy(direct); EmptySlots = Copy(empty); ActiveSets = Copy(sets); ToteTotems = Copy(tote);
        Notices = new System.Collections.ObjectModel.ReadOnlyDictionary<string, string>(new Dictionary<string, string>(notices));
        ExpansionIds = Array.AsReadOnly(Weapons.Concat(Armor.SelectMany(g => g.Rows)).Concat(DirectTotems).Where(r => r.Expandable).Select(r => r.Id).ToArray());
        var cards = mostUsed == null ? Recent : new[] { mostUsed }.Concat(Recent);
        InspectableSlots = new System.Collections.ObjectModel.ReadOnlyDictionary<string, RunSlotPresentation>(cards
            .SelectMany(card => card.Slots.Where(slot => slot.CanOpenDetails).Select(slot => (Id: InspectionId(card, slot), Slot: slot)))
            .ToDictionary(pair => pair.Id, pair => pair.Slot, StringComparer.Ordinal));
    }
    private static System.Collections.ObjectModel.ReadOnlyCollection<EquipmentEntry> Copy(IEnumerable<EquipmentEntry> entries) => Array.AsReadOnly(entries.ToArray());
    public bool CanRoute(string generation, string id) => generation == GenerationId && Recent.Any(r => r.RunId == id && id.Length > 0);
    public static string InspectionId(EquipmentEntry card, RunSlotPresentation slot) => "inspect:" + card.Id + ":slot:" + slot.SlotId;
}

internal static class EquipmentPresentationFactory
{
    public static EquipmentPresentation? Create(StatisticsPanelProjection p, string generation, Func<string, string>? text = null)
    {
        if (p == null || string.IsNullOrWhiteSpace(generation) || p.EquipmentBinding?.Matches(p, generation) != true
            || p.Runs.Runs.Any(r => r.SaveGenerationId != generation)
            || p.Runs.Runs.Select(r => r.RunId).Distinct(StringComparer.Ordinal).Count() != p.Runs.Runs.Count) return null;
        var routableRuns = new HashSet<RunSummary>(p.Runs.Runs);
        if (p.RecentEquipmentRuns.Any(r => r.SaveGenerationId != generation || !routableRuns.Contains(r))) return null;
        var t = text ?? UiText.Get; var a = p.Equipment.Lifetime; var c = p.Equipment.Capabilities;
        EquipmentCompositionReducer.Validate(a.Composition);
        string Name(string? name, string? id = null) => string.IsNullOrWhiteSpace(name) || name == id ? t("ui.equipment_unknown_item") : name!;
        string SlotName(string? name, string? id = null) => string.IsNullOrWhiteSpace(name) || name == id ? t("ui.equipment_unknown_slot") : name!;
        string DirectName(string id, string? name) => id switch {
            "duckov:slot:Totem1" => t("ui.equipment_totem_slot_1"), "duckov:slot:Totem2" => t("ui.equipment_totem_slot_2"), _ => SlotName(name, id) };
        string Used(long count) => string.Format(CultureInfo.CurrentCulture, t("ui.equipment_used_runs"), count);
        string Notice(MetricAvailability state) => state.State == AdapterCapabilityState.Supported ? "" : t("ui.equipment_current_unavailable");
        var notices = new Dictionary<string, string>(StringComparer.Ordinal) {
            ["loadouts"] = Notice(c.EquipmentSlots), ["selected"] = Notice(c.SelectedWeapon),
            ["weapons"] = Notice(c.CharacterSlotState), ["armor"] = Notice(c.CharacterSlotState),
            ["direct"] = Notice(c.DirectTotems), ["empty"] = Notice(c.CharacterSlotState),
            ["sets"] = Notice(c.DirectTotems), ["tote"] = Notice(c.ToteContents) };
        if (a.WasRepairedFromInvalidState)
            foreach (var key in notices.Keys.ToArray()) notices[key] = t("ui.equipment_partial") + "\n" + notices[key];
        var nestedNotice = Notice(c.NestedSlotState);
        var highestDuration = a.Loadouts.Values.OrderByDescending(r => r.ActiveDurationSeconds).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
        EquipmentEntry? most = highestDuration == null ? null : Loadout(a, highestDuration, "most", "", "",
            highestDuration.RunOccurrences == 1 ? t("ui.equipment_used_one_run") : Used(highestDuration.RunOccurrences));
        var selected = a.SelectedWeapons.Values.Select(r => (Row: r, Split: r.Id.IndexOf('|'))).Where(r => r.Split > 0)
            .GroupBy(r => r.Row.Id.Substring(r.Split + 1), StringComparer.Ordinal).Select(g =>
            {
                var names = a.CharacterSlotStates.Values.Where(r => r.ItemId == g.Key).Select(r => r.ItemDisplayName)
                    .Concat(a.Composition.Loadouts.Values.Where(d => !d.Conflicting).SelectMany(d => d.Items).Where(i => i.ItemId == g.Key).Select(i => i.ItemDisplayName));
                var name = names.Where(n => !string.IsNullOrWhiteSpace(n) && n != g.Key).OrderBy(n => n, StringComparer.Ordinal).FirstOrDefault();
                var slots = g.OrderBy(r => r.Row.Id, StringComparer.Ordinal).Select(r => new EquipmentEntry(r.Row.Id,
                    SlotName(a.Slots.TryGetValue(r.Row.Id.Substring(0, r.Split), out var slot) ? slot.DisplayName : null), "", (double)r.Row.ActiveDurationSeconds));
                return new EquipmentEntry("selected:" + g.Key, Name(name), g.Key, Sum(g.Select(r => r.Row.ActiveDurationSeconds)),
                    t("ui.equipment_selected_time"), groups: new[] { new EquipmentGroup(t("ui.equipment_selected_by_slot"), slots) });
            }).OrderByDescending(r => r.Duration).ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
        var recent = p.RecentEquipmentRuns.OrderByDescending(r => r.EndedUtc).ThenBy(r => r.RunId, StringComparer.Ordinal).Select(run =>
        {
            var row = run.EquipmentStatistics.Loadouts.Values.OrderByDescending(r => r.ActiveDurationSeconds).ThenBy(r => r.Id, StringComparer.Ordinal).FirstOrDefault();
            var segments = run.Segments.OrderBy(s => s.SegmentIndex).ToArray();
            var route = segments.Length == 0 ? Name(run.MapDisplayName) : segments.Length == 1 ? Name(segments[0].MapDisplayName)
                : Name(segments[0].MapDisplayName) + " - " + Name(segments[segments.Length - 1].MapDisplayName);
            var caption = EconomyPresentationFactory.Timestamp(run.StartedUtc, t, runDate: true) + "\n" + t("ui.equipment_most_during_run");
            return row == null ? new EquipmentEntry("run:" + run.RunId, route, "", 0, caption, t("ui.unavailable"), runId: run.RunId)
                : Loadout(run.EquipmentStatistics, row, "run:" + run.RunId, route, run.RunId, caption);
        }).ToArray();
        var weapons = p.Equipment.Weapons.OrderByDescending(w => w.TotalEquippedDurationSeconds).ThenBy(w => w.DisplayName, StringComparer.Ordinal).ThenBy(w => w.WeaponId, StringComparer.Ordinal)
            .Select(w => new EquipmentEntry("weapon:" + w.WeaponId, Name(w.DisplayName, w.WeaponId), w.WeaponId, w.TotalEquippedDurationSeconds, t("ui.equipment_total_equipped"),
                groups: new[] { new EquipmentGroup(t("ui.equipment_character_slots"), w.CharacterSlots.Select(s => new EquipmentEntry(s.SlotId, SlotName(s.SlotDisplayName), "", s.EquippedDurationSeconds))) }
                    .Concat(Nested(w.WeaponId, null)))).ToArray();
        var excluded = new HashSet<string>(a.CharacterSlotStates.Values.Where(r => r.ItemKind is EquipmentItemKind.Weapon or EquipmentItemKind.Totem).Select(r => r.SlotId), StringComparer.Ordinal);
        foreach (var definition in a.Composition.Loadouts.Values.Where(d => !d.Conflicting))
            foreach (var slot in definition.Roots.Where(r => r.IsDirectTotemSlot)) excluded.Add(slot.SlotId);
        foreach (var slot in a.Composition.EmptyDirectSlots.Keys) excluded.Add(slot);
        var armor = p.Equipment.ArmorAndGearSlots.Where(g => !excluded.Contains(g.SlotId)).OrderBy(g => ArmorOrder(g.SlotId)).ThenBy(g => g.SlotId, StringComparer.Ordinal)
            .Select(g => new EquipmentGroup(SlotName(g.SlotDisplayName, g.SlotId), g.Rows.OrderBy(r => r.State).ThenByDescending(r => r.ActiveDurationSeconds)
                .ThenBy(r => r.ItemDisplayName, StringComparer.Ordinal).ThenBy(r => r.ItemId, StringComparer.Ordinal).Select(r =>
                    new EquipmentEntry("gear:" + g.SlotId + "|" + r.ItemId, r.State == EquipmentSlotState.Empty ? t("ui.equipment_nothing") : Name(r.ItemDisplayName, r.ItemId),
                        r.ItemId, (double)r.ActiveDurationSeconds, groups: r.State == EquipmentSlotState.Empty ? null : Nested(r.ItemId, g.SlotId, onlyObserved: true))))).ToArray();
        var direct = Totems(TotemCarryKind.DirectSlot); var tote = Totems(TotemCarryKind.ToteInventory);
        var empty = a.Composition.EmptyDirectSlots.Values.OrderBy(r => r.Id, StringComparer.Ordinal)
            .Select(r => new EquipmentEntry("empty:" + r.Id, DirectName(r.Id, r.DisplayName), "", (double)r.ActiveDurationSeconds)).ToArray();
        var sets = a.TotemSets.Values.OrderByDescending(r => r.ActiveDurationSeconds).ThenBy(r => r.Id, StringComparer.Ordinal).Select(r =>
        {
            a.Composition.ActiveTotemSets.TryGetValue(r.Id, out var d);
            var available = d != null && !d.Conflicting;
            var members = available ? d!.Members.Select(m => new EquipmentEntry("member:" + m.ItemId, Name(m.DisplayName), m.ItemId, 0)).ToArray() : Array.Empty<EquipmentEntry>();
            return new EquipmentEntry("set:" + r.Id, "", "", (double)r.ActiveDurationSeconds, t("ui.equipment_active_together") + " · " + Used(r.RunOccurrences),
                d?.Conflicting == true ? t("ui.equipment_conflict") : available && members.Length == 1 ? t("ui.equipment_singleton") : "",
                groups: new[] { new EquipmentGroup("", members) });
        }).ToArray();
        return new EquipmentPresentation(generation, most, selected, recent, weapons, armor, direct, empty, sets, tote, notices);

        EquipmentEntry Loadout(EquipmentStatisticsAggregate source, EquipmentDurationAggregate row, string id, string name, string runId, string caption)
        {
            source.Composition.Loadouts.TryGetValue(row.Id, out var definition);
            var slots = definition == null || definition.Conflicting ? Array.Empty<RunSlotPresentation>() : definition.Roots.OrderBy(r => IsKnownRoot(r.SlotId) ? 0 : 1).ThenBy(r => r.SlotId, StringComparer.Ordinal).Select(root =>
            {
                var item = definition.Items.SingleOrDefault(i => i.SlotId == root.SlotId);
                return RunsPresentationFactory.PresentSlot(new TerminalRootSlot(root.SlotId, root.SlotDisplayName, root.State, root.ItemId,
                    root.ItemDisplayName, root.ItemKind, root.State == EquipmentSlotState.Empty || item?.NestedSlotStateComplete == true,
                    Array.AsReadOnly(item?.NestedSlots.Select(n => new TerminalNestedSlot(n.Path, n.SlotKey, n.SlotDisplayName, n.State, n.ItemId, n.ItemDisplayName)).ToArray()
                        ?? Array.Empty<TerminalNestedSlot>())), t);
            }).ToArray();
            return new EquipmentEntry(id, name, "", (double)row.ActiveDurationSeconds, caption,
                definition == null ? t("ui.equipment_loadout_history") : definition.Conflicting ? t("ui.equipment_conflict") : definition.NestedComplete ? "" : t("ui.runs_nested_partial"), slots: slots, runId: runId);
        }
        IEnumerable<EquipmentGroup> Nested(string itemId, string? parentSlot, bool onlyObserved = false)
        {
            var groups = a.NestedSlotStates.Values.Where(r => r.ParentItemId == itemId && (parentSlot == null || r.ParentSlotId == parentSlot))
                .GroupBy(r => r.SlotKey, StringComparer.Ordinal).OrderBy(g => NestedOrder(g.Key)).ThenBy(g => g.Key, StringComparer.Ordinal).ToArray();
            if (groups.Length == 0)
            {
                if (!onlyObserved) yield return new EquipmentGroup(t("ui.equipment_nested_slots"), Array.Empty<EquipmentEntry>(), t("ui.unavailable"));
                yield break;
            }
            foreach (var group in groups)
                yield return new EquipmentGroup(SlotName(group.Select(r => r.SlotDisplayName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))),
                    group.GroupBy(r => (r.State, r.ItemId)).Select(g => new EquipmentEntry("nested:" + itemId + ":" + group.Key + ":" + g.Key.ItemId,
                        g.Key.State == EquipmentSlotState.Empty ? t("ui.equipment_nothing") : Name(g.Select(r => r.ItemDisplayName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))),
                        g.Key.ItemId, Sum(g.Select(r => r.ActiveDurationSeconds))))
                        .OrderByDescending(r => r.Duration).ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal),
                    nestedNotice.Length > 0 ? nestedNotice : a.Composition.Loadouts.Values.Any(d => !d.Conflicting && d.Items.Any(i => i.ItemId == itemId && !i.NestedSlotStateComplete)) ? t("ui.runs_nested_partial") : "");
        }
        EquipmentEntry[] Totems(TotemCarryKind carry) => a.Composition.TotemStates.Values.Where(r => r.Totem.CarryKind == carry)
            .GroupBy(r => r.Totem.ItemId, StringComparer.Ordinal).Select(g => new EquipmentEntry((carry == TotemCarryKind.DirectSlot ? "direct:" : "tote:") + g.Key,
                Name(g.Select(r => r.Totem.DisplayName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))), g.Key, Sum(g.Select(r => r.DurationSeconds)),
                t(carry == TotemCarryKind.DirectSlot ? "ui.equipment_total_equipped" : "ui.equipment_carried"),
                groups: carry == TotemCarryKind.ToteInventory ? null : new[] { new EquipmentGroup(t("ui.equipment_character_slots"),
                    g.GroupBy(r => (r.Totem.DirectSlotId, r.Totem.ActivationState)).OrderBy(s => s.Key.DirectSlotId, StringComparer.Ordinal).ThenBy(s => s.Key.ActivationState)
                        .Select(s => new EquipmentEntry("direct-slot:" + g.Key + s.Key.DirectSlotId + s.Key.ActivationState,
                            s.Key.DirectSlotId.Length == 0 ? t("ui.equipment_slot_unavailable") : DirectName(s.Key.DirectSlotId, a.Slots.TryGetValue(s.Key.DirectSlotId, out var slot) ? slot.DisplayName :
                                a.CharacterSlotStates.Values.FirstOrDefault(r => r.SlotId == s.Key.DirectSlotId)?.SlotDisplayName), "", Sum(s.Select(r => r.DurationSeconds)),
                            t("ui.equipment_activation_" + s.Key.ActivationState.ToString().ToLowerInvariant())))) }))
                .OrderByDescending(r => r.Duration).ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Id, StringComparer.Ordinal).ToArray();
    }
    private static double Sum(IEnumerable<decimal> values) => (double)values.Sum();
    internal static double Sum(IEnumerable<double> values) => values.Aggregate(0d, EquipmentCompositionReducer.Add);
    // Native capture and Runs use ordinal known-slot order; additional roots follow in ordinal order.
    internal static bool IsKnownRoot(string id) => id is "duckov:slot:PrimaryWeapon" or "duckov:slot:SecondaryWeapon" or "duckov:slot:MeleeWeapon"
        or "duckov:slot:Helmat" or "duckov:slot:Armor" or "duckov:slot:FaceMask" or "duckov:slot:Headset" or "duckov:slot:Backpack"
        or "duckov:slot:Totem1" or "duckov:slot:Totem2";
    internal static int NestedOrder(string key) => key.ToLowerInvariant() switch { "scope" => 0, "muzzle" => 1, "grip" => 2, "stock" => 3, "tactic" or "tactical" or "tactics" => 4, "magazine" => 5, _ => 6 };
    internal static int ArmorOrder(string key) => key switch { "duckov:slot:Helmat" => 0, "duckov:slot:FaceMask" => 1, "duckov:slot:Armor" => 2, "duckov:slot:Backpack" => 3, "duckov:slot:Headset" => 4, _ => 5 };
}
