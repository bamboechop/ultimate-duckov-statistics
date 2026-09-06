using System.Globalization;
using System.Text;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Export;

public static class EquipmentCompositionCsv
{
    public static string Loadouts(StatisticsExportDocument document) => Write(document, "loadout");
    public static string ActiveSets(StatisticsExportDocument document) => Write(document, "active_set");
    public static string TotemStates(StatisticsExportDocument document) => Write(document, "totem_state");

    private static string Write(StatisticsExportDocument document, string family)
    {
        var output = new StringBuilder("generation_id,scope,scope_id,run_id,kind,definition_id,evidence,slot_id,path,slot_key,slot_name,state,item_id,item_name,item_kind,roots_complete,nested_complete,carry_kind,container_id,activation,copy_ordinal,duration_seconds,run_occurrences\n");
        foreach (var scope in Scopes(document))
        {
            var evidence = scope.Value.Composition;
            EquipmentCompositionReducer.Validate(evidence);
            if (family == "loadout")
            {
                foreach (var id in scope.Value.Loadouts.Keys.Union(evidence.Loadouts.Keys).OrderBy(k => k, StringComparer.Ordinal))
                {
                    evidence.Loadouts.TryGetValue(id, out var d);
                    scope.Value.Loadouts.TryGetValue(id, out var duration);
                    var state = d == null ? "Unavailable" : d.Conflicting ? "Conflict" : "Observed";
                    Row("loadout_definition", id, state, roots: d?.RootsComplete, nested: d?.NestedComplete, duration: duration?.ActiveDurationSeconds, runs: duration?.RunOccurrences);
                    if (d == null || d.Conflicting) continue;
                    foreach (var root in d.Roots.OrderBy(r => r.SlotId, StringComparer.Ordinal))
                    {
                        var item = d.Items.FirstOrDefault(i => i.SlotId == root.SlotId);
                        Row("root", id, state, root.SlotId, slotName: root.SlotDisplayName, state: root.State.ToString(), itemId: root.ItemId,
                            itemName: root.ItemDisplayName, itemKind: root.ItemKind.ToString(), roots: d.RootsComplete, nested: item?.NestedSlotStateComplete);
                        if (item == null) continue;
                        foreach (var slot in item.NestedSlots)
                            Row("nested", id, state, root.SlotId, slot.Path, slot.SlotKey, slot.SlotDisplayName, slot.State.ToString(),
                                slot.ItemId, slot.ItemDisplayName, roots: d.RootsComplete, nested: item.NestedSlotStateComplete);
                    }
                }
            }
            else if (family == "active_set")
            {
                foreach (var id in scope.Value.TotemSets.Keys.Union(evidence.ActiveTotemSets.Keys).OrderBy(k => k, StringComparer.Ordinal))
                {
                    evidence.ActiveTotemSets.TryGetValue(id, out var d);
                    scope.Value.TotemSets.TryGetValue(id, out var duration);
                    Row("active_set_definition", id, d == null ? "Unavailable" : d.Conflicting ? "Conflict" : "Observed",
                        duration: duration?.ActiveDurationSeconds, runs: duration?.RunOccurrences);
                    if (d == null || d.Conflicting) continue;
                    var copy = 0;
                    foreach (var member in d.Members)
                        Row("active_member", id, "Observed", itemId: member.ItemId, itemName: member.DisplayName,
                            carry: member.CarryKind.ToString(), container: member.ContainerId, activation: member.ActivationState.ToString(), copy: ++copy);
                }
            }
            else
            {
                if (evidence.HistoricalUnavailable) Row("historical_state", "", "Unavailable");
                foreach (var pair in evidence.TotemStates.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var row = pair.Value; var t = row.Totem;
                    Row(t.CarryKind == TotemCarryKind.DirectSlot ? "direct_totem_state" : "tote_presence", "", "Observed", t.DirectSlotId,
                        itemId: t.ItemId, itemName: t.DisplayName, carry: t.CarryKind.ToString(), container: t.ContainerId,
                        activation: t.ActivationState.ToString(), copy: row.CopyOrdinal, duration: row.DurationSeconds);
                }
                foreach (var row in evidence.EmptyDirectSlots.Values.OrderBy(r => r.Id, StringComparer.Ordinal))
                    Row("empty_direct_slot", "", "Observed", row.Id, slotName: row.DisplayName, state: "Empty", duration: row.ActiveDurationSeconds);
            }
            void Row(string kind, string id, string proof, string slotId = "", string path = "", string slotKey = "", string slotName = "",
                string state = "", string itemId = "", string itemName = "", string itemKind = "", bool? roots = null, bool? nested = null,
                string carry = "", string container = "", string activation = "", int? copy = null, double? duration = null, long? runs = null)
            {
                output.AppendLine(string.Join(",", new object?[] { document.GenerationId, scope.Kind, scope.Id, scope.Run, kind, id, proof,
                    slotId, path, slotKey, slotName, state, itemId, itemName, itemKind, roots, nested, carry, container, activation, copy, duration, runs }.Select(Csv)));
            }
        }
        return output.ToString();
    }
    private static string Csv(object? value)
    {
        var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    private static IEnumerable<(string Kind, string Id, string Run, EquipmentStatisticsAggregate Value)> Scopes(StatisticsExportDocument d)
    {
        yield return ("lifetime", d.GenerationId, "", d.RunTotals.EquipmentStatistics);
        foreach (var map in d.RunTotals.Maps.Values.OrderBy(m => m.MapId, StringComparer.Ordinal)) yield return ("starting_map", map.MapId, "", map.EquipmentStatistics);
        foreach (var map in d.RunTotals.RouteMaps.Values.OrderBy(m => m.MapId, StringComparer.Ordinal)) yield return ("route_map", map.MapId, "", map.EquipmentStatistics);
        foreach (var run in d.Runs.OrderBy(r => r.RunId, StringComparer.Ordinal))
        {
            yield return ("run", run.RunId, run.RunId, run.EquipmentStatistics);
            foreach (var segment in run.Segments.OrderBy(s => s.SegmentIndex)) yield return ("route_segment", segment.SegmentId, run.RunId, segment.EquipmentStatistics);
        }
    }
}
