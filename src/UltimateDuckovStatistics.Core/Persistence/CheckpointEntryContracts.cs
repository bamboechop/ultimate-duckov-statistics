using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

internal static class CheckpointEntryContracts
{
    internal static void Validate(CheckpointEntryKind kind, string key, object value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Checkpoint entry identity is missing.");
        ProfileFormat.ValidateRecordMembers(value);
        switch (value)
        {
            case long count when count >= 0: return;
            case WeaponAggregate weapon when weapon.WeaponId == key && weapon.Totals.FiringActions >= 0: return;
            case AmmunitionAggregate ammunition when ammunition.AmmunitionId == key && ammunition.Totals.FiringActions >= 0: return;
            case WeaponAmmunitionPairAggregate pair when !string.IsNullOrWhiteSpace(pair.WeaponId) && !string.IsNullOrWhiteSpace(pair.AmmunitionId)
                && !string.IsNullOrWhiteSpace(pair.WeaponDisplayName) && !string.IsNullOrWhiteSpace(pair.AmmunitionDisplayName)
                && key == WeaponStatisticsReducer.PairKey(pair.WeaponId, pair.AmmunitionId) && pair.FiringActions >= 0:
                return;
            case CombatBreakdownAggregate combat when combat.Id == key:
                CombatStatisticsReducer.ValidateChangedBreakdown(combat); return;
            case EquipmentDurationAggregate duration when duration.ActiveDurationSeconds >= 0 && duration.RunOccurrences >= 0:
                if (kind == CheckpointEntryKind.EmptyDirectSlots)
                    EquipmentCompositionReducer.Validate(new EquipmentCompositionEvidence { EmptyDirectSlots = new(StringComparer.Ordinal) { [key] = duration } });
                return;
            case CharacterSlotStateDurationAggregate slot:
                EquipmentStatisticsReducer.ValidateChangedCharacterSlot(slot); return;
            case NestedSlotStateDurationAggregate nested:
                EquipmentStatisticsReducer.ValidateChangedNestedSlot(nested); return;
            case EquipmentCombatAssociationAggregate association:
                EquipmentStatisticsReducer.ValidateChangedAssociation(association); return;
            case LoadoutDefinition definition:
                EquipmentCompositionReducer.Validate(new EquipmentCompositionEvidence { Loadouts = new(StringComparer.Ordinal) { [key] = definition } }); return;
            case ActiveTotemSetDefinition definition:
                EquipmentCompositionReducer.Validate(new EquipmentCompositionEvidence { ActiveTotemSets = new(StringComparer.Ordinal) { [key] = definition } }); return;
            case TotemStateDuration state:
                EquipmentCompositionReducer.Validate(new EquipmentCompositionEvidence { TotemStates = new(StringComparer.Ordinal) { [key] = state } }); return;
            case ItemAggregate item when item.ItemId == key && Enum.IsDefined(typeof(CanonicalItemGroup), item.Group):
                ItemStatisticsAggregateReducer.Validate(new ItemStatisticsAggregate
                { Overall = item.Totals, Items = new(StringComparer.Ordinal) { [key] = item }, Groups = new(StringComparer.Ordinal) { [item.Group.ToString()] = item.Totals } }); return;
            default: throw new ArgumentException("Checkpoint entry has invalid values or an unsupported type.");
        }
    }
}
