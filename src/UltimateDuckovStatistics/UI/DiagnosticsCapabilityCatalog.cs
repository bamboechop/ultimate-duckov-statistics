namespace UltimateDuckovStatistics.UI;

internal sealed class DiagnosticsCapabilityDescriptor
{
    public string Id { get; }
    public string Group { get; }
    public string EnglishName { get; }
    public bool PartialCoverageIsExpected { get; }
    public bool RequiresHarmony { get; }
    public string TextKey => "ui.diag_cap_" + Id;
    public DiagnosticsCapabilityDescriptor(string id, string group, string name, bool partialCoverageIsExpected = false, bool requiresHarmony = false)
    { Id = id; Group = group; EnglishName = name; PartialCoverageIsExpected = partialCoverageIsExpected; RequiresHarmony = requiresHarmony; }
}

internal static class DiagnosticsCapabilityCatalog
{
    // Exact shipped IDs, grouped by supported native metric family.
    public static IReadOnlyList<DiagnosticsCapabilityDescriptor> All { get; } = new DiagnosticsCapabilityDescriptor[]
    {
        new("native-item-use", "items", "Successful raid item uses"),
        new("native-healing-attribution", "items", "Observed HP restored", requiresHarmony: true),
        new("throwable-releases", "items", "Throwable releases / uses", requiresHarmony: true),
        new("native-run-lifecycle", "runs", "Run outcomes and active duration"),
        new("native-main-duck-movement", "runs", "Physical distance"),
        new("native-map-identity", "runs", "Starting-map identity"),
        new("native-multi-map-route", "runs", "Routes and segments"),
        new("native-firing-actions", "combat", "Firing actions"),
        new("native-weapon-identity", "combat", "Fired weapon identity"),
        new("native-ammunition-identity", "combat", "Fired ammunition identity"),
        new("native-weapon-ammunition-pairing", "combat", "Weapon and ammunition pairs"),
        new("native-damage-dealt", "combat", "Damage dealt", requiresHarmony: true),
        new("native-damage-received", "combat", "Damage received", requiresHarmony: true),
        new("native-ranged-hits", "combat", "Ranged hits", requiresHarmony: true),
        new("native-projectile-accuracy", "combat", "Projectile accuracy", requiresHarmony: true),
        new("native-melee-swings", "combat", "Melee swings"),
        new("native-melee-hits", "combat", "Melee hits", requiresHarmony: true),
        new("native-player-deaths", "combat", "Player deaths"),
        new("native-combat-ownership", "combat", "Damage ownership", requiresHarmony: true),
        new("native-enemy-identity", "combat", "Enemy identity"),
        new("native-enemy-family", "combat", "Observed enemy families", requiresHarmony: true),
        new("native-damage-cause", "combat", "Damage causes"),
        new("native-damage-weapon-identity", "combat", "Damage weapon identity", requiresHarmony: true),
        new("native-damage-ammunition-identity", "combat", "Damage ammunition identity", requiresHarmony: true),
        new("native-damage-over-time", "combat", "Damage over time", requiresHarmony: true),
        new("native-headshots", "combat", "Headshots", requiresHarmony: true),
        new("native-headshot-final-blows", "combat", "Headshot final blows", requiresHarmony: true),
        new("native-throwable-player-final-blows", "combat", "Throwable kills", requiresHarmony: true),
        new("native-grenade-hazard-attribution", "combat", "Spawned grenade damage ownership", requiresHarmony: true),
        new("native-proven-player-final-blows", "combat", "Kills by you", requiresHarmony: true),
        new("native-observed-world-deaths", "combat", "Observed world deaths", requiresHarmony: true),
        new("native-equipment-slots", "equipment", "Equipped slots and loadouts"),
        new("native-selected-weapon", "equipment", "Selected weapon time"),
        new("native-weapon-attachments", "equipment", "Attachment metadata"),
        new("native-direct-totems", "equipment", "Directly equipped totems"),
        new("native-tote-contents", "equipment", "Totems in tote bags"),
        new("native-character-equipment-slot-state", "equipment", "Occupied and empty character slots"),
        new("native-equipped-item-nested-slot-state", "equipment", "Occupied and empty nested slots"),
        new("native-container-loot-access", "containers", "Unique containers looted", requiresHarmony: true),
        new("native-economy-money-flow", "economy", "Money flow"),
        // These native contracts operate normally with partial attribution coverage.
        // Runtime loss of those capabilities is published as DisabledIncompatible.
        new("native-economy-money-source", "economy", "Money sources", partialCoverageIsExpected: true),
        new("native-economy-money-context", "economy", "Money contexts", partialCoverageIsExpected: true),
        new("native-economy-cash-flow", "economy", "Cash flow"),
        new("native-economy-cash-acquisition", "economy", "Proven raid Cash acquisition", partialCoverageIsExpected: true),
        new("native-economy-cash-context", "economy", "Cash contexts"),
        new("native-economy-route", "economy", "Run and route flow attribution"),
        new("native-economy-holdings-current-money", "economy", "Current Money holding"),
        new("native-economy-holdings-current-cash", "economy", "Current Cash holding"),
        new("native-economy-holdings-liquid-wealth", "economy", "Liquid wealth"),
        new("native-crafting-completion-actions", "crafting", "Successful crafting times", requiresHarmony: true),
        new("native-crafting-produced-quantity", "crafting", "Produced units", requiresHarmony: true),
        new("native-crafting-output-identity", "crafting", "Crafted item identity", requiresHarmony: true),
        new("native-crafting-recipe-identity", "crafting", "Recorded recipe identity", requiresHarmony: true),
        new("native-crafting-batch-metadata", "crafting", "Recorded output batches", requiresHarmony: true),
        new("native-crafting-item-resource-identity", "crafting", "Exact crafting resources used", requiresHarmony: true),
        new("native-crafting-output-resource-association", "crafting", "Output and resource relationships", requiresHarmony: true),
        new("native-crafting-currency-charge", "crafting", "Recorded currency charge", requiresHarmony: true),
        new("native-world-time-calendar-days", "world", "Calendar days advanced"),
        new("native-world-time-observed-elapsed", "world", "Observed game time"),
        new("native-world-time-completed-sleep", "world", "Completed sleep sessions", requiresHarmony: true),
        new("native-world-time-sleep-advanced", "world", "Time advanced through sleep", requiresHarmony: true),
        new("native-save-lifecycle", "storage", "Profile and save lifecycle"),
    };
    public static IReadOnlyList<string> GroupOrder { get; } = new[]
    { "items", "runs", "combat", "equipment", "containers", "economy", "crafting", "world", "menu", "storage", "other" };
}
