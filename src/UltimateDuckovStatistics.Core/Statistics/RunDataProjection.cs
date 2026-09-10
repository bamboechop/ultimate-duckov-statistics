using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public sealed class TerminalNestedSlot
{
    public string Path { get; }
    public string SlotKey { get; }
    public string DisplayName { get; }
    public EquipmentSlotState State { get; }
    public string ItemId { get; }
    public string ItemDisplayName { get; }

    public TerminalNestedSlot(string path, string slotKey, string displayName, EquipmentSlotState state, string itemId, string itemDisplayName)
    {
        Path = path;
        SlotKey = slotKey;
        DisplayName = displayName;
        State = state;
        ItemId = itemId;
        ItemDisplayName = itemDisplayName;
    }
}

public sealed class TerminalRootSlot
{
    public string SlotId { get; }
    public string DisplayName { get; }
    public EquipmentSlotState State { get; }
    public string ItemId { get; }
    public string ItemDisplayName { get; }
    public EquipmentItemKind ItemKind { get; }
    public bool NestedComplete { get; }
    public IReadOnlyList<TerminalNestedSlot> NestedSlots { get; }

    public TerminalRootSlot(string slotId, string displayName, EquipmentSlotState state, string itemId, string itemDisplayName, EquipmentItemKind itemKind, bool nestedComplete, IReadOnlyList<TerminalNestedSlot> nestedSlots)
    {
        SlotId = slotId;
        DisplayName = displayName;
        State = state;
        ItemId = itemId;
        ItemDisplayName = itemDisplayName;
        ItemKind = itemKind;
        NestedComplete = nestedComplete;
        NestedSlots = nestedSlots;
    }
}

public sealed class RunDataProjection
{
    public TerminalLoadoutState TerminalState { get; }
    public string TerminalProvenance { get; }
    public bool RootSlotsComplete { get; }
    public bool NestedSlotsComplete { get; }
    public IReadOnlyList<TerminalRootSlot> TerminalSlots { get; }
    public long KillsByYou { get; }
    public long RangedKills { get; }
    public long MeleeKills { get; }
    public long ThrowableKills { get; }
    public long EffectKills { get; }
    public long EnvironmentalKills { get; }
    public long UnknownKills { get; }
    public bool ClassificationComplete { get; }
    public bool RangedMeleeExact { get; }
    public string KillClassificationProvenance { get; }
    public CombatMetricCapabilities CombatCapabilities { get; }

    public RunDataProjection(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        var terminal = run.TerminalLoadout ?? new TerminalLoadout();
        TerminalState = terminal.State;
        TerminalProvenance = terminal.Provenance;
        RootSlotsComplete = terminal.Snapshot?.CharacterSlotStateComplete == true;
        NestedSlotsComplete = terminal.Snapshot?.NestedSlotStateComplete == true;
        TerminalSlots = Array.AsReadOnly(terminal.Snapshot?.CharacterSlots.Select(slot =>
        {
            var item = terminal.Snapshot.Items.SingleOrDefault(value => value.SlotId == slot.SlotId);
            return new TerminalRootSlot(slot.SlotId, slot.SlotDisplayName, slot.State, slot.ItemId,
                slot.ItemDisplayName, slot.ItemKind, slot.State == EquipmentSlotState.Empty || item?.NestedSlotStateComplete == true,
                Array.AsReadOnly(item?.NestedSlots.Select(nested => new TerminalNestedSlot(nested.Path,
                    nested.SlotKey, nested.SlotDisplayName, nested.State, nested.ItemId, nested.ItemDisplayName)).ToArray()
                    ?? Array.Empty<TerminalNestedSlot>()));
        }).ToArray() ?? Array.Empty<TerminalRootSlot>());
        KillsByYou = run.CombatStatistics.Totals.KillsByYou;
        var kills = run.CombatStatistics.Totals.PlayerKills ?? new PlayerKillPartition { Unknown = KillsByYou };
        ThrowableKills = kills.Throwables;
        RangedKills = kills.Ranged; MeleeKills = kills.Melee; EffectKills = kills.Effect;
        EnvironmentalKills = kills.Environmental; UnknownKills = kills.Unknown;
        ClassificationComplete = run.CombatStatistics.Totals.PlayerKills != null && kills.ClassificationComplete;
        KillClassificationProvenance = run.CombatStatistics.Totals.PlayerKills == null
            ? "Player-kill partition evidence is unavailable." : kills.Provenance;
        CombatCapabilities = CombatStatisticsReducer.CloneCapabilities(run.CombatStatistics.Capabilities);
        RangedMeleeExact = ClassificationComplete && CombatCapabilities.KillsByYou.State == AdapterCapabilityState.Supported;
    }
}
