namespace UltimateDuckovStatistics.Core.Statistics;

public static partial class EquipmentStatisticsReducer
{
    internal static string CheckpointCharacterObservation(CharacterSlotStateDurationAggregate row) => CharacterSlotObservationKey(row.SlotId);
    internal static string CheckpointNestedObservation(NestedSlotStateDurationAggregate row) => NestedSlotObservationKey(row.ParentSlotId, row.ParentItemId, row.Path);
    internal static void ValidateCheckpointSlotFanOut(EquipmentStatisticsAggregate affected) => ValidateSlotStateReconciliation(affected);
    internal static void ValidateChangedCharacterSlot(CharacterSlotStateDurationAggregate row)
    { if (!ValidCharacterSlotState(row)) throw new ArgumentException("Equipment checkpoint contains an invalid character-slot entry."); }
    internal static void ValidateChangedNestedSlot(NestedSlotStateDurationAggregate row)
    { if (!ValidNestedSlotState(row)) throw new ArgumentException("Equipment checkpoint contains an invalid nested-slot entry."); }
    internal static void ValidateChangedAssociation(EquipmentCombatAssociationAggregate row)
    {
        if (row.FiringActions < 0 || row.RangedHits < 0 || row.MeleeHits < 0 || row.KillsByYou < 0 || row.PlayerDeaths < 0
            || !IsFinite(row.DamageDealt) || row.DamageDealt < 0 || !IsFinite(row.DamageReceived) || row.DamageReceived < 0)
            throw new ArgumentException("Equipment checkpoint contains an invalid combat-association entry.");
        row.PlayerKills.Validate(row.KillsByYou);
    }
}
