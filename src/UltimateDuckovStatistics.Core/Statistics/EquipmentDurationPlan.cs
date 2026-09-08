using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static partial class EquipmentStatisticsReducer
{
    // Only snapshot-derived metadata is retained. Every advance still looks up
    // current destination rows and preflights each checked decimal addition.
    internal sealed class DurationPlan
    {
        internal EquipmentSnapshot Snapshot { get; }
        internal string SelectedWeaponKey { get; }
        internal string LoadoutDescription { get; }
        internal string TotemSetDescription { get; }
        internal bool HasActiveTotems { get; }
        internal (EquippedItemSnapshot Item, string ItemKey, string WeaponKey)[] Items { get; }
        internal (CharacterEquipmentSlotSnapshot Slot, string ObservationKey, string StateKey)[] Roots { get; }
        internal (EquippedItemSnapshot Parent, NestedEquipmentSlotSnapshot Slot, string ObservationKey, string StateKey)[] Nested { get; }
        internal (string Key, string Description)[] Totems { get; }
        internal EquipmentCompositionReducer.DurationPlan Composition { get; }

        internal DurationPlan(EquipmentSnapshot source)
        {
            Snapshot = Clone(source);
            SelectedWeaponKey = source.SelectedWeaponSlotId + "|" + source.SelectedWeaponId;
            LoadoutDescription = DescribeLoadout(Snapshot);
            HasActiveTotems = Snapshot.Totems.Any(t => t.ActivationState == TotemActivationState.ProvenActive);
            TotemSetDescription = HasActiveTotems ? DescribeActiveTotemSet(Snapshot) : string.Empty;
            Items = Snapshot.Items.Select(item => (item,
                item.SlotId + "|" + item.ItemId + "|" + item.AttachmentSignature,
                item.SlotId + "|" + item.ItemId)).ToArray();
            Roots = Snapshot.CharacterSlots.Select(slot =>
                (slot, CharacterSlotObservationKey(slot.SlotId), CharacterSlotStateKey(slot))).ToArray();
            Nested = Snapshot.Items.SelectMany(parent => parent.NestedSlots.Select(slot =>
                (parent, slot, NestedSlotObservationKey(parent.SlotId, parent.ItemId, slot.Path),
                    NestedSlotStateKey(parent.SlotId, parent.ItemId, slot)))).ToArray();
            Totems = Snapshot.Totems.GroupBy(TotemStateKey, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .SelectMany(group => group.Select((totem, index) =>
                    (group.Key + "|copy:" + (index + 1).ToString(CultureInfo.InvariantCulture), DescribeTotem(totem))))
                .ToArray();
            Composition = new EquipmentCompositionReducer.DurationPlan(Snapshot);
        }

        internal bool Matches(EquipmentSnapshot other)
        {
            // Models also serve mutable recovery/caller boundaries. Compare the
            // owned copy rather than trusting object identity or SnapshotId, so
            // same-ID enrichment and in-place edits cannot reuse stale keys.
            var saved = Snapshot;
            if (saved.SnapshotId != other.SnapshotId || saved.LoadoutId != other.LoadoutId
                || saved.SelectedWeaponId != other.SelectedWeaponId || saved.SelectedWeaponSlotId != other.SelectedWeaponSlotId
                || saved.TotemSetId != other.TotemSetId || saved.CharacterSlotStateComplete != other.CharacterSlotStateComplete
                || saved.NestedSlotStateComplete != other.NestedSlotStateComplete
                || saved.Items.Count != other.Items.Count || saved.CharacterSlots.Count != other.CharacterSlots.Count
                || saved.Totems.Count != other.Totems.Count) return false;
            for (var i = 0; i < saved.Items.Count; i++)
            {
                var left = saved.Items[i];
                var right = other.Items[i];
                if (left.SlotId != right.SlotId || left.SlotDisplayName != right.SlotDisplayName
                    || left.ItemId != right.ItemId || left.ItemDisplayName != right.ItemDisplayName || left.Kind != right.Kind
                    || left.AttachmentSignature != right.AttachmentSignature || left.NestedSlotStateComplete != right.NestedSlotStateComplete
                    || left.NestedSlots.Count != right.NestedSlots.Count) return false;
                for (var j = 0; j < left.NestedSlots.Count; j++)
                {
                    var nestedLeft = left.NestedSlots[j];
                    var nestedRight = right.NestedSlots[j];
                    if (nestedLeft.Path != nestedRight.Path || nestedLeft.SlotKey != nestedRight.SlotKey
                        || nestedLeft.SlotDisplayName != nestedRight.SlotDisplayName || nestedLeft.State != nestedRight.State
                        || nestedLeft.ItemId != nestedRight.ItemId || nestedLeft.ItemDisplayName != nestedRight.ItemDisplayName) return false;
                }
            }
            for (var i = 0; i < saved.CharacterSlots.Count; i++)
            {
                var left = saved.CharacterSlots[i];
                var right = other.CharacterSlots[i];
                if (left.SlotId != right.SlotId || left.SlotDisplayName != right.SlotDisplayName || left.State != right.State
                    || left.ItemId != right.ItemId || left.ItemDisplayName != right.ItemDisplayName || left.ItemKind != right.ItemKind
                    || left.IsDirectTotemSlot != right.IsDirectTotemSlot) return false;
            }
            for (var i = 0; i < saved.Totems.Count; i++)
            {
                var left = saved.Totems[i];
                var right = other.Totems[i];
                if (left.ItemId != right.ItemId || left.DisplayName != right.DisplayName || left.CarryKind != right.CarryKind
                    || left.ContainerId != right.ContainerId || left.ActivationState != right.ActivationState
                    || left.DirectSlotId != (right.DirectSlotId ?? string.Empty)) return false;
            }
            return true;
        }
    }
}
