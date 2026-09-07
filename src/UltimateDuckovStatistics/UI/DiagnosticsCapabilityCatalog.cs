namespace UltimateDuckovStatistics.UI;

internal sealed class DiagnosticsCapabilityDescriptor
{
    public string Id { get; }
    public string Group { get; }
    public string EnglishName { get; }
    public string TextKey => "ui.diag_cap_" + Id;
    public bool BaselineLimitation { get; }
    public DiagnosticsCapabilityDescriptor(string id, string group, string name, bool baselineLimitation = false)
    { Id = id; Group = group; EnglishName = name; BaselineLimitation = baselineLimitation; }
}

internal static class DiagnosticsCapabilityCatalog
{
    // Exact shipped IDs, grouped by user-facing metric family. Deliberately unsupported
    // baseline metrics stay visible without declaring otherwise healthy capture broken.
    public static IReadOnlyList<DiagnosticsCapabilityDescriptor> All { get; } = new DiagnosticsCapabilityDescriptor[]
    {
        new("native-item-use", "items", "Successful raid item uses"),
        new("native-healing-attribution", "items", "Observed HP restored"),
        new("throwable-releases", "items", "Throwable releases / uses"),
        new("native-run-lifecycle", "runs", "Run outcomes and active duration"),
        new("native-main-duck-movement", "runs", "Physical distance"),
        new("native-map-identity", "runs", "Starting-map identity"),
        new("native-multi-map-route", "runs", "Routes and segments"),
        new("native-firing-actions", "combat", "Firing actions"),
        new("native-weapon-identity", "combat", "Fired weapon identity"),
        new("native-ammunition-identity", "combat", "Fired ammunition identity"),
        new("native-weapon-ammunition-pairing", "combat", "Weapon and ammunition pairs"),
        new("native-trigger-attempts", "combat", "Trigger attempts", true),
        new("native-ammunition-consumption", "combat", "Ammunition consumption", true),
        new("native-projectile-count", "combat", "Projectile count from firing callback", true),
        new("native-damage-dealt", "combat", "Damage dealt"),
        new("native-damage-received", "combat", "Damage received"),
        new("native-ranged-hits", "combat", "Ranged hits"),
        new("native-projectile-accuracy", "combat", "Projectile accuracy"),
        new("native-melee-swings", "combat", "Melee swings"),
        new("native-melee-hits", "combat", "Melee hits"),
        new("native-enemies-killed", "combat", "Legacy enemy-kill metric", true),
        new("native-player-deaths", "combat", "Player deaths"),
        new("native-combat-ownership", "combat", "Damage ownership"),
        new("native-enemy-identity", "combat", "Enemy identity"),
        new("native-enemy-family", "combat", "Observed enemy families"),
        new("native-damage-cause", "combat", "Damage causes"),
        new("native-damage-weapon-identity", "combat", "Damage weapon identity"),
        new("native-damage-ammunition-identity", "combat", "Damage ammunition identity"),
        new("native-damage-over-time", "combat", "Damage over time"),
        new("native-headshots", "combat", "Headshots"),
        new("native-headshot-final-blows", "combat", "Headshot final blows"),
        new("native-throwable-player-final-blows", "combat", "Throwable kills"),
        new("native-proven-player-final-blows", "combat", "Kills by you"),
        new("native-observed-world-deaths", "combat", "Observed world deaths"),
        new("native-equipment-slots", "equipment", "Equipped slots and loadouts"),
        new("native-selected-weapon", "equipment", "Selected weapon time"),
        new("native-weapon-attachments", "equipment", "Attachment metadata"),
        new("native-direct-totems", "equipment", "Directly equipped totems"),
        new("native-tote-contents", "equipment", "Totems in tote bags"),
        new("native-tote-totem-activation", "equipment", "Tote-bag effect activation", true),
        new("native-character-equipment-slot-state", "equipment", "Occupied and empty character slots"),
        new("native-equipped-item-nested-slot-state", "equipment", "Occupied and empty nested slots"),
        new("native-container-loot-access", "containers", "Unique containers looted"),
        new("native-economy-money-flow", "economy", "Money flow"),
        new("native-economy-money-source", "economy", "Money sources"),
        new("native-economy-money-context", "economy", "Money contexts"),
        new("native-economy-cash-flow", "economy", "Cash flow"),
        new("native-economy-cash-acquisition", "economy", "Proven raid Cash acquisition"),
        new("native-economy-cash-context", "economy", "Cash contexts"),
        new("native-economy-cash-terminal", "economy", "Run Cash outcomes", true),
        new("native-economy-route", "economy", "Run and route flow attribution"),
        new("native-economy-holdings-current-money", "economy", "Current Money holding"),
        new("native-economy-holdings-current-cash", "economy", "Current Cash holding"),
        new("native-economy-holdings-liquid-wealth", "economy", "Liquid wealth"),
        new("native-crafting-completion-actions", "crafting", "Successful crafting times"),
        new("native-crafting-produced-quantity", "crafting", "Produced units"),
        new("native-crafting-output-identity", "crafting", "Crafted item identity"),
        new("native-crafting-recipe-identity", "crafting", "Recorded recipe identity"),
        new("native-crafting-batch-metadata", "crafting", "Recorded output batches"),
        new("native-crafting-multiple-output-recipes", "crafting", "Multiple-output recipes", true),
        new("native-crafting-workstation-identity", "crafting", "Workstation identity", true),
        new("native-crafting-context-attribution", "crafting", "Crafting run and map attribution", true),
        new("native-crafting-item-resource-identity", "crafting", "Exact crafting resources used"),
        new("native-crafting-output-resource-association", "crafting", "Output and resource relationships"),
        new("native-crafting-currency-charge", "crafting", "Recorded currency charge"),
        new("native-crafting-currency-money-cash-split", "crafting", "Crafting Money / Cash split", true),
        new("native-world-time-calendar-days", "world", "Calendar days advanced"),
        new("native-world-time-observed-elapsed", "world", "Observed game time"),
        new("native-world-time-completed-sleep", "world", "Completed sleep sessions"),
        new("native-world-time-sleep-advanced", "world", "Time advanced through sleep"),
        new("native-save-lifecycle", "storage", "Profile and save lifecycle"),
    };
    public static IReadOnlyList<string> GroupOrder { get; } = new[]
    { "items", "runs", "combat", "equipment", "containers", "economy", "crafting", "world", "menu", "storage", "other" };
}
