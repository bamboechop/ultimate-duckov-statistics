using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public enum TerminalLoadoutState
{
    [EnumMember] Unavailable = 0,
    [EnumMember] HistoricalUnavailable = 1,
    [EnumMember] Partial = 2,
    [EnumMember] Complete = 3
}

/// <summary>Detached evidence from a single accepted terminal boundary; never a duration snapshot.</summary>
[DataContract]
public sealed class TerminalLoadout
{
    [DataMember(Order = 1)] public TerminalLoadoutState State { get; set; }
    [DataMember(Order = 2)] public string Provenance { get; set; } = "No terminal boundary equipment evidence was captured.";
    [DataMember(Order = 3, EmitDefaultValue = false)] public EquipmentSnapshot? Snapshot { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public RunOutcome? CapturedOutcome { get; set; }

    public static TerminalLoadout Historical() => new()
    {
        State = TerminalLoadoutState.HistoricalUnavailable,
        Provenance = "Pre-schema-17 run: terminal equipment was not recorded and cannot be backfilled."
    };

    public static TerminalLoadout Captured(EquipmentSnapshot snapshot, RunOutcome outcome, string provenance)
    {
        EquipmentStatisticsReducer.ValidateSnapshot(snapshot);
        return new TerminalLoadout
        {
            State = snapshot.CharacterSlotStateComplete && snapshot.NestedSlotStateComplete
                    && snapshot.Items.All(item => item.NestedSlotStateComplete)
                ? TerminalLoadoutState.Complete : TerminalLoadoutState.Partial,
            Provenance = provenance,
            CapturedOutcome = outcome,
            Snapshot = EquipmentStatisticsReducer.CloneSnapshot(snapshot)
        };
    }

    public TerminalLoadout Clone() => new()
    {
        State = State,
        Provenance = Provenance,
        CapturedOutcome = CapturedOutcome,
        Snapshot = Snapshot == null ? null : EquipmentStatisticsReducer.CloneSnapshot(Snapshot)
    };

    public static TerminalLoadout ForOutcome(TerminalLoadout? candidate, RunOutcome outcome) =>
        outcome == RunOutcome.Interrupted ? new TerminalLoadout { Provenance = "Interrupted run has no terminal loadout." }
        : candidate?.Clone() ?? new TerminalLoadout();

    public void Validate(RunOutcome? outcome)
    {
        if (!Enum.IsDefined(typeof(TerminalLoadoutState), State) || string.IsNullOrWhiteSpace(Provenance)
            || CapturedOutcome is RunOutcome.Interrupted
            || CapturedOutcome.HasValue && !Enum.IsDefined(typeof(RunOutcome), CapturedOutcome.Value)
            || outcome.HasValue && CapturedOutcome.HasValue && outcome != CapturedOutcome)
            throw new ArgumentException("Terminal loadout has invalid outcome or provenance.");
        var hasEvidence = State is TerminalLoadoutState.Complete or TerminalLoadoutState.Partial;
        if (hasEvidence != (Snapshot != null) || hasEvidence && !CapturedOutcome.HasValue)
            throw new ArgumentException("Terminal loadout availability conflicts with retained evidence.");
        if (Snapshot == null) return;
        EquipmentStatisticsReducer.ValidateSnapshot(Snapshot);
        if (Snapshot.CharacterSlots.Any(slot => slot.State == EquipmentSlotState.Occupied
                && !Snapshot.Items.Any(item => item.SlotId == slot.SlotId && item.ItemId == slot.ItemId))
            || Snapshot.Items.Any(item => !Snapshot.CharacterSlots.Any(slot => slot.SlotId == item.SlotId
                && slot.State == EquipmentSlotState.Occupied && slot.ItemId == item.ItemId)))
            throw new ArgumentException("Terminal root item evidence conflicts with occupied slots.");
        var complete = Snapshot.CharacterSlotStateComplete && Snapshot.NestedSlotStateComplete
                       && Snapshot.Items.All(item => item.NestedSlotStateComplete);
        if (complete != (State == TerminalLoadoutState.Complete))
            throw new ArgumentException("Terminal loadout completeness conflicts with retained evidence.");
    }
}
