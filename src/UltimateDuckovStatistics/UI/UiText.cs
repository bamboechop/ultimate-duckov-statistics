using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

internal static class UiText
{
    internal static string FormatHealth(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("N0", CultureInfo.CurrentCulture);
    static UiText()
    {
        foreach (var entry in RemainingTabsText.English) English[entry.Key] = entry.Value;
        foreach (var entry in DiagnosticsText.English) English[entry.Key] = entry.Value;
        foreach (var entry in DiagnosticsCapabilityCatalog.All) English[entry.TextKey] = entry.EnglishName;
    }

    private static Func<string, string?>? nativeResolver;

    private static readonly Dictionary<string, string> English =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ui.about"] = "About",
            ["ui.about_description"] = "Local, per-save statistics for your raids: routes, combat, equipment, item use and healing, economy, crafting, world time and sleep. Recording starts when Ultimate Duckov Statistics (UDS) is installed.",
            ["ui.about_author"] = "Created by {0}",
            ["ui.about_support"] = "Support the author (optional)",
            ["ui.about_discord"] = "Join Discord",
            ["ui.about_translation"] = "Want to help translate UDS? Contact me on Discord or leave a comment on the Steam Workshop page, and mention the language you could help with.",
            ["ui.about_link_failed"] = "Could not open the link. You can try again; your Statistics panel is still available.",
            ["ui.equipment_unknown_item"] = "Unknown item",
            ["ui.equipment_totem_slot_1"] = "Totem slot 1",
            ["ui.equipment_totem_slot_2"] = "Totem slot 2",
            ["ui.equipment_unknown_slot"] = "Unknown slot",
            ["ui.equipment_used_runs"] = "Used in {0} runs",
            ["ui.equipment_used_one_run"] = "Used in 1 run",
            ["ui.equipment_current_unavailable"] = "Current tracking is unavailable or limited; recorded values are retained.",
            ["ui.equipment_partial"] = "Partial equipment evidence",
            ["ui.equipment_active_time"] = "active time",
            ["ui.equipment_selected_time"] = "selected time",
            ["ui.equipment_selected_by_slot"] = "Selected time by slot",
            ["ui.equipment_most_during_run"] = "Most used during this run",
            ["ui.equipment_total_equipped"] = "total time equipped",
            ["ui.equipment_character_slots"] = "Equipped time by slot",
            ["ui.equipment_equipped_in_slot"] = "{0} equipped in {1}",
            ["ui.equipment_primary_weapon_slot"] = "primary weapon slot",
            ["ui.equipment_secondary_weapon_slot"] = "secondary weapon slot",
            ["ui.equipment_melee_weapon_slot"] = "melee weapon slot",
            ["ui.equipment_nothing"] = "Nothing equipped",
            ["ui.equipment_active_together"] = "active together",
            ["ui.equipment_conflict"] = "Composition unavailable: conflicting observations of this identity.",
            ["ui.equipment_singleton"] = "No other active totem",
            ["ui.equipment_loadout_history"] = "Loadout composition unavailable for earlier history",
            ["ui.equipment_nested_slots"] = "Attachment slots",
            ["ui.equipment_carried"] = "carried time",
            ["ui.equipment_slot_unavailable"] = "Direct slot attribution unavailable",
            ["ui.equipment_activation_provenactive"] = "Proven active",
            ["ui.equipment_activation_proveninactive"] = "Proven inactive",
            ["ui.equipment_activation_unknown"] = "Activation unknown",
            ["ui.equipment_no_observation"] = "No observations recorded",
            ["ui.equipment_no_loadout"] = "No loadout observations recorded",
            ["ui.equipment_most_used"] = "Most-used loadout",
            ["ui.equipment_selected"] = "Selected weapon time",
            ["ui.equipment_recent"] = "Recent run loadouts",
            ["ui.equipment_direct"] = "Directly equipped totems",
            ["ui.equipment_empty_slots"] = "Empty slot time",
            ["ui.equipment_sets"] = "Active equipped totem sets",
            ["ui.equipment_tote"] = "Totems in tote bags",
            ["ui.equipment_tote_unknown"] = "Presence is tracked; effect activation is unknown",
            ["ui.combat_hits"] = "Hits",
            ["ui.combat_weapons_ammunition"] = "Weapons & ammunition",
            ["ui.combat_kills"] = "Kills",
            ["ui.combat_swings"] = "Swings",
            ["ui.combat_partial"] = "Partial",
            ["ui.combat_ranged_hits"] = "Ranged hits",
            ["ui.combat_melee_hits"] = "Melee hits",
            ["ui.combat_weapon_details"] = "Weapon details",
            ["ui.combat_melee_no_ammo"] = "Ammunition: Not applicable to melee",
            ["ui.combat_weapon_type_unavailable"] = "Weapon combat type: Unavailable",
            ["ui.combat_throwable_uses"] = "Throws / uses",
            ["ui.combat_throwable_no_ammo"] = "Ammunition: Not applicable to throwables",
            ["ui.combat_throwable_tracking_unavailable"] = "Throwable tracking: Unavailable",
            ["ui.combat_weapon_attribution_unavailable"] = "Per-weapon combat attribution: Unavailable",
            ["ui.combat_headshot_final_blows"] = "Headshot final blows",
            ["ui.combat_effect"] = "Effect",
            ["ui.combat_environmental"] = "Environmental",
            ["ui.combat_unknown"] = "Unknown",
            ["ui.combat_owner_othernpc"] = "Other NPC",
            ["ui.combat_owner_environmental"] = "Environmental",
            ["ui.combat_owner_unknown"] = "Unknown",
            ["ui.combat_owner_companion"] = "Companion",
            ["ui.combat_world_subtitle"] = "Enemy deaths observed during your runs and not credited to you.",
            ["ui.combat_total_suffix"] = "total",
            ["ui.combat_enemy_world"] = "World deaths not credited to you",
            ["ui.combat_ownership_unavailable"] = "Ownership breakdown unavailable",
            ["ui.combat_no_enemies"] = "No enemy combat recorded so far",
            ["ui.combat_weapon_basis"] = "of this weapon's firing actions",
            ["ui.combat_pair_basis"] = "within observed correlated pairs for this weapon",
            ["ui.combat_all_basis"] = "of all firing actions",
            ["ui.combat_uncorrelated"] = "Uncorrelated firing actions",
            ["ui.combat_no_pairs"] = "No correlated ammunition data",
            ["ui.combat_none"] = "None",
            ["ui.combat_attacker_types"] = "Attacker types",
            ["ui.combat_deadliest"] = "Deadliest attacker",
            ["ui.combat_total"] = "Total",
            ["ui.combat_no_attackers"] = "No incoming damage or deaths recorded so far",
            ["ui.combat_attacker"] = "Attacker",
            ["ui.combat_damage_to_you"] = "Damage dealt to you",
            ["ui.combat_share"] = "Share",
            ["ui.combat_deaths_caused"] = "Deaths caused",
            ["ui.combat_enemy"] = "Enemy",
            ["ui.combat_world_deaths"] = "World deaths",
            ["ui.combat_weapons"] = "Weapons",
            ["ui.combat_firing_footer"] = "A firing action is one accepted weapon firing event. It may not equal ammunition consumed or projectiles created.",
            ["ui.records_overall"] = "Overall",
            ["ui.records_per_map"] = "Per starting map",
            ["ui.records_extraction"] = "Extractions",
            ["ui.records_death"] = "Deaths",
            ["ui.records_no_extraction"] = "No eligible extraction runs recorded so far",
            ["ui.records_no_death"] = "No eligible death runs recorded so far",
            ["ui.records_empty_extraction"] = "No eligible extraction run",
            ["ui.records_empty_death"] = "No eligible death run",
            ["ui.records_shortest_death"] = "Shortest death run",
            ["ui.records_longest_death"] = "Longest death run",
            ["ui.records_time"] = "Time",
            ["ui.records_date"] = "Date",
            ["ui.records_starting_map"] = "Starting map",
            ["ui.records_extracted"] = "Extracted",
            ["ui.records_died"] = "Died",
            ["ui.records_interrupted"] = "Interrupted",
            ["ui.records_teleport"] = "Total teleport distance",
            ["ui.records_inconsistent"] = "Starting-map data is inconsistent; affected values are unavailable.",
            ["ui.records_run_unavailable"] = "View run unavailable: the exact recorded run cannot be verified.",
            ["ui.records_no_maps"] = "No starting-map runs recorded so far",
            ["ui.title"] = "Ultimate Duckov Statistics",
            // Full native-menu name accepted after in-game visual validation.
            ["ui.menu_entry"] = "Ultimate Duckov Statistics",
            ["ui.combat_throwables"] = "Throwables",
            ["ui.shell_placeholder"] = "The retained-mode shell is ready. Statistics view content is restored in visual correction Gate 2.",
            ["ui.shell_unavailable"] = "The statistics panel could not attach to Duckov's current UI. Tracking remains active; use Player.log for details.",
            ["ui.close"] = "Close",
            ["ui.overview"] = "Overview",
            ["ui.profile_summary"] = "Profile Summary",
            ["ui.overview_highlights"] = "Highlights",
            ["ui.overview_latest_run"] = "Latest run",
            ["ui.overview_fastest_extraction"] = "Fastest extraction",
            ["ui.overview_longest_successful_raid"] = "Longest successful raid",
            ["ui.overview_most_used_weapon"] = "Most-used weapon",
            ["ui.overview_most_used_consumable"] = "Most-used consumable",
            ["ui.overview_firing_actions_unit"] = "firing actions",
            ["ui.overview_uses_unit"] = "uses",
            ["ui.overview_total_runs"] = "Total runs",
            ["ui.overview_extraction_rate"] = "Extraction rate",
            ["ui.overview_total_active_raid_time"] = "Total active raid time",
            ["ui.overview_total_distance_travelled"] = "Total distance travelled",
            ["ui.overview_total_recorded_distance"] = "Total recorded distance",
            ["ui.overview_raid_distance"] = "Raid distance",
            ["ui.overview_base_distance"] = "Recorded base distance",
            ["ui.distance_not_recorded"] = "Not recorded",
            ["ui.distance_coverage"] = "Recorded while UDS is active. Earlier base movement is not included. Raid distance includes completed/recovered runs.",
            ["ui.distance_since"] = "Base recorded since {0}",
            ["ui.overview_kills_by_you"] = "Kills by you",
            ["ui.overview_deaths"] = "Deaths",
            ["ui.overview_damage_dealt"] = "Damage dealt",
            ["ui.overview_damage_taken"] = "Damage taken",
            ["ui.overview_hp_restored"] = "HP restored",
            ["ui.overview_unique_containers_opened"] = "Unique containers opened",
            ["ui.overview_economy"] = "Economy",
            ["ui.overview_money_net"] = "Money net:",
            ["ui.overview_cash_net"] = "Cash net:",
            ["ui.items"] = "Items",
            ["ui.item_use"] = "Item Use",
            ["ui.runs"] = "Runs",
            ["ui.runs_run"] = "Run",
            ["ui.runs_empty"] = "No recorded runs yet. Complete a raid to see its recorded history here.",
            ["ui.runs_requested_unavailable"] = "The requested run is unavailable in this profile. Select a run from the history.",
            ["ui.runs_partial"] = "partial; recorded values only",
            ["ui.runs_integrity"] = "Integrity",
            ["ui.runs_eligible"] = "Records eligible",
            ["ui.runs_ineligible"] = "Records ineligible",
            ["ui.runs_containers"] = "Unique containers opened",
            ["ui.runs_cash_net"] = "Cash net",
            ["ui.runs_accuracy"] = "Accuracy",
            ["ui.runs_headshots"] = "Headshots",
            ["ui.runs_hp"] = "HP restored",
            ["ui.runs_final_blows"] = "final blows",
            ["ui.runs_headshot_final_blows"] = "headshot final blows",
            ["ui.runs_classification_partial"] = "Ranged/melee classification incomplete",
            ["ui.runs_empty_slot"] = "Empty",
            ["ui.runs_no_combat_or_containers"] = "No combat or containers",
            ["ui.runs_no_combat"] = "No combat",
            ["ui.runs_no_containers"] = "No containers",
            ["ui.runs_combat_activity"] = "Recorded combat activity",
            ["ui.runs_nested_partial"] = "Additional attachment evidence unavailable",
            ["ui.runs_terminal_complete"] = "Captured terminal equipment",
            ["ui.runs_terminal_partial"] = "Partial terminal equipment; unreadable root or attachment evidence unavailable",
            ["ui.runs_terminal_unavailable"] = "Terminal equipment unavailable",
            ["ui.runs_route"] = "Route",
            ["ui.runs_equipment"] = "Equipment",
            ["ui.runs_combat"] = "Combat",
            ["ui.runs_ranged"] = "Ranged",
            ["ui.runs_melee"] = "Melee",
            ["ui.runs_map"] = "map",
            ["ui.runs_maps"] = "maps",
            ["ui.runs_segment"] = "segment",
            ["ui.runs_segments"] = "segments",
            ["ui.runs_kill"] = "kill",
            ["ui.runs_kills"] = "kills",
            ["ui.runs_hit"] = "hit",
            ["ui.runs_hits"] = "hits",
            ["ui.runs_headshot"] = "headshot",
            ["ui.runs_headshots_plural"] = "headshots",
            ["ui.runs_firing_action"] = "firing action",
            ["ui.runs_firing_actions"] = "firing actions",
            ["ui.runs_swing"] = "swing",
            ["ui.runs_swings"] = "swings",
            ["ui.runs_container"] = "container opened",
            ["ui.runs_containers_plural"] = "containers opened",
            ["ui.records"] = "Records",
            ["ui.combat"] = "Combat",
            ["ui.equipment"] = "Equipment",
            ["ui.economy"] = "Economy",
            ["ui.crafting"] = "Crafting",
            ["ui.diagnostics"] = "Diagnostics",
            ["ui.total_uses"] = "Successful raid uses",
            ["ui.actual_hp"] = "Actual HP restored",
            ["ui.save_slot"] = "Save slot",
            ["ui.generation"] = "UDS generation",
            ["ui.interrupted_sessions"] = "Interrupted sessions recovered",
            ["ui.total_runs"] = "Runs",
            ["ui.extracted_runs"] = "Extracted",
            ["ui.died_runs"] = "Died",
            ["ui.overview_run_badge_unknown"] = "Unknown",
            ["ui.overview_latest_run_unknown_map"] = "Unknown map",
            ["ui.overview_latest_run_active_time"] = "Active time",
            ["ui.overview_latest_run_distance"] = "Distance",
            ["ui.overview_latest_run_containers_opened"] = "Containers opened",
            ["ui.overview_latest_run_view_run"] = "View run",
            ["ui.overview_world_time"] = "World time",
            ["ui.overview_sleep_sessions"] = "Sleep sessions",
            ["ui.overview_sleep_advanced_time"] = "Time advanced through sleeping",
            ["ui.interrupted_runs"] = "Interrupted",
            ["ui.physical_distance"] = "Physical distance",
            ["ui.teleport_distance"] = "Teleport distance",
            ["ui.route_movement"] = "Teleport / transition-excluded",
            ["ui.segment_event_unavailable"] = "Segment M1-M7 attribution unavailable",
            ["ui.segment_event_partial"] = "Earlier segment attribution is incomplete; shown values are exact known events",
            ["ui.segment_event_capture_supported"] = "Schema-10 current capture remains supported",
            ["ui.firing_actions"] = "Firing actions",
            ["ui.containers_looted"] = "Unique containers opened",
            ["ui.container_history_unavailable"] = "earlier history unavailable",
            ["ui.repaired_unavailable"] = "repaired data; unavailable",
            ["ui.weapon"] = "Weapon",
            ["ui.ammunition"] = "Ammunition",
            ["ui.no_combat"] = "No accepted firing actions recorded for this save generation.",
            ["ui.metric_contract"] = "A firing action is one accepted native firing callback. Dry-fire attempts, actual loaded-ammunition consumption, and completed projectile creation are unavailable on this game contract.",
            ["ui.damage_contract"] = "Damage is measured from actual Health.Hurt HP loss. Accuracy is unique player projectiles that damaged an enemy divided by completed player projectiles; critical hits alone never prove headshots.",
            ["ui.damage_dealt"] = "Main-duck damage dealt",
            ["ui.damage_received"] = "Main-duck damage received",
            ["ui.accuracy"] = "Ranged accuracy",
            ["ui.overall_accuracy"] = "Overall accuracy",
            ["ui.melee_accuracy"] = "Melee accuracy",
            ["ui.combat_weapon_accuracy"] = "Accuracy",
            ["ui.combat_weapon_accuracy_basis"] = "Accuracy: this weapon's player-credited ranged hits per firing action by you, across all ammunition. Multiple projectiles or controlled characters can exceed 100%.",
            ["ui.combat_other_player_kills"] = "Other kills by you",
            ["ui.combat_effect_kills"] = "Effects / DoT kills",
            ["ui.combat_effect_kills_tooltip"] = "Effects / damage-over-time kills: enemies killed by damage from an effect credited to you, including ongoing damage from status effects. Direct weapon and throwable kills are counted separately.",
            ["ui.combat_environmental_kills"] = "Environmental kills",
            ["ui.combat_unknown_kills"] = "Unclassified kills",
            ["ui.melee"] = "Melee swings / hits",
            ["ui.kills_by_you"] = "Kills by you",
            ["ui.observed_world_deaths"] = "Observed world deaths",
            ["ui.ownership"] = "Observed-death ownership",
            ["ui.deaths"] = "Player deaths",
            ["ui.headshots"] = "Headshots / final blows",
            ["ui.enemies"] = "Enemies",
            ["ui.summary"] = "Summary",
            ["ui.weapons_ammo"] = "Weapons & Ammo",
            ["ui.incoming_damage"] = "Incoming Damage",
            ["ui.killers"] = "Killers",
            ["ui.integrity"] = "Integrity",
            ["ui.record_status"] = "Records",
            ["ui.record_eligible"] = "Eligible",
            ["ui.record_excluded"] = "Excluded",
            ["ui.reason_interrupted"] = "interrupted run",
            ["ui.reason_lifecycle"] = "lifecycle unsupported",
            ["ui.reason_other"] = "not eligible",
            ["ui.no_runs"] = "No completed runs recorded for this save generation.",
            ["ui.no_records"] = "No eligible extraction or death duration records recorded.",
            ["ui.outcome"] = "Outcome",
            ["ui.map"] = "Map",
            ["ui.route"] = "Route",
            ["ui.show_segments"] = "Show segments",
            ["ui.hide_segments"] = "Hide segments",
            ["ui.active_time"] = "Active time",
            ["ui.wall_time"] = "Wall-clock diagnostic",
            ["ui.shortest"] = "Shortest",
            ["ui.longest"] = "Longest",
            ["ui.extraction_records"] = "Extraction records",
            ["ui.death_records"] = "Death records",
            ["ui.per_map"] = "Per-map totals and records",
            ["ui.per_starting_map"] = "Starting-map complete-run totals and records",
            ["ui.unsupported"] = "Unsupported",
            ["ui.em_dash"] = "—",
            ["ui.capture_incomplete"] = "capture incomplete",
            ["ui.group_totals"] = "Canonical groups",
            ["ui.no_items"] = "No successful raid item uses recorded for this save generation.",
            ["ui.item_name"] = "Item",
            ["ui.effects"] = "Effects",
            ["ui.recent_runs"] = "Recent runs",
            ["ui.group"] = "Group",
            ["ui.activations"] = "Activations",
            ["ui.amount"] = "Amount consumed",
            ["ui.capabilities"] = "Adapter capabilities",
            ["ui.diagnostic_log"] = "Recent bounded diagnostics",
            ["ui.data_settings"] = "Data & settings",
            ["ui.recent_issues"] = "Recent issues",
            ["ui.no_recent_issues"] = "No recent warnings or errors.",
            ["ui.technical_details"] = "Technical details",
            ["ui.tracking_health"] = "Tracking-system health",
            ["ui.working"] = "Working",
            ["ui.limited"] = "Limited",
            ["ui.error"] = "Error",
            ["ui.health_legend"] = "Working: current supported capture is active. Limited: one or more dimensions are unavailable but supported siblings continue. Error: a current failure may prevent affected capture; inspect the issue and Player.log.",
            ["ui.menu_access"] = "Menu access",
            ["ui.main_menu_entry"] = "Main-menu entry",
            ["ui.base_pause_entry"] = "Base pause-menu entry",
            ["ui.hotkey_fallback"] = "Configured hotkey fallback",
            ["ui.not_observed"] = "Not observed this lifecycle",
            ["ui.attached_unverified"] = "Attached; activation not yet observed",
            ["ui.menu_access_limited"] = "Statistics tracking remains healthy. Use the configured hotkey outside raids until every native entry is activated and inspect Recent issues for unavailable paths.",
            ["ui.runtime_issues"] = "Runtime issues",
            ["ui.issue_guidance"] = "If the issue persists, inspect Player.log. Unaffected tracking continues unless its health group reports an error.",
            ["ui.data_path"] = "Data path",
            ["ui.export"] = "Export JSON + CSV",
            ["ui.reset"] = "Reset this UDS profile",
            ["ui.reset_warning"] = "Reset archives the current UDS generation read-only and starts at zero. It cannot be undone from within UDS. Duckov saves are not changed.",
            ["ui.confirm_reset"] = "Confirm reset",
            ["ui.cancel"] = "Cancel",
            ["ui.hotkey"] = "Panel hotkey",
            ["ui.apply"] = "Apply",
            ["ui.previous"] = "Previous",
            ["ui.next"] = "Next",
            ["ui.page"] = "Page",
            ["ui.more_above"] = "More above",
            ["ui.more_below"] = "More below",
            ["ui.hotkey_invalid"] = "Unknown Unity key name; hotkey was not changed.",
            ["ui.hotkey_saved"] = "Panel hotkey saved.",
            ["ui.raid_unavailable"] = "Statistics are available outside raids.",
            ["ui.profile_unavailable"] = "Statistics are unavailable until the active UDS save generation is known exactly.",
            ["ui.operation_busy"] = "Another statistics operation is already in progress.",
            ["ui.export_complete"] = "Export complete",
            ["ui.export_failed"] = "Export failed; see Diagnostics and Player.log.",
            ["ui.reset_complete"] = "UDS profile reset; prior generation archived read-only.",
            ["ui.reset_pending"] = "Reset is still pending safely; completion has not been reported. See Diagnostics and Player.log.",
            ["ui.reset_failed"] = "Reset failed before it was queued; the existing profile remains active and no statistics were removed. Duckov save data was not changed. See Diagnostics and Player.log.",
            ["ui.export_path_copied"] = "Export complete; the folder path was copied to the clipboard.",
            ["ui.integrity_note"] = "Run time, weapon, and combat tracking exclude pause/loading and non-raid contexts. Integrity-flagged and interrupted runs remain visible; only eligible runs enter default duration records.",
            ["ui.equipment_contract"] = "Equipment time uses monotonic active raid time. Direct totem and tote presence are tracked separately; tote activation remains unavailable until gameplay proves it.",
            ["ui.open_hint"] = "Press the configured hotkey outside raids to show or hide this panel.",
            ["ui.economy_contract"] = "Money and physical Cash are independent currencies. Gross inflow is not profit, current balance, or net worth. Unknown adjustments retain exact amount and direction without inventing a reason.",
            ["ui.holdings_contract"] = "Current holdings are direct observations of Duckov Money and top-level owned Cash. They are never reconstructed from currency flows. Liquid wealth is a checked Money + Cash sum only while both observations are current.",
            ["ui.current_holdings"] = "Current economy holdings",
            ["ui.money_holding"] = "Money",
            ["ui.cash_holding"] = "Owned Cash",
            ["ui.liquid_wealth"] = "Liquid wealth",
            ["ui.currency_flows"] = "Currency flows since M9 tracking",
            ["ui.last_observed"] = "Last observed",
            ["ui.current"] = "Current",
            ["ui.unavailable"] = "Unavailable",
            ["ui.pre_m15_unavailable"] = "earlier holdings unavailable; not reconstructed",
            ["ui.gross_inflow"] = "Gross inflow",
            ["ui.gross_outflow"] = "Gross outflow",
            ["ui.net_flow"] = "Net flow",
            ["ui.sources"] = "Sources",
            ["ui.contexts"] = "Contexts",
            ["ui.raid_cash"] = "Raid Cash",
            ["ui.acquired"] = "Acquired",
            ["ui.secured"] = "Secured",
            ["ui.lost"] = "Lost",
            ["ui.unresolved"] = "Unresolved",
            ["ui.pre_m9_unavailable"] = "earlier economy history unavailable",
            ["ui.no_m9_flows"] = "no recorded M9 flow",
            ["ui.scope_capture_partly_unavailable"] = "capture unavailable for part of this scope",
            ["ui.scope_capture_unavailable"] = "capture unavailable for this scope",
            ["ui.current_capture_unavailable"] = "current capture unavailable",
            ["ui.calendar_days_advanced"] = "Calendar days advanced",
            ["ui.observed_world_time"] = "Observed Duckov world-clock advancement",
            ["ui.completed_sleep_sessions"] = "Completed sleep sessions",
            ["ui.sleep_advanced_time"] = "Time advanced through sleep",
            ["ui.pre_m12_unavailable"] = "earlier world-time and sleep history unavailable",
            ["ui.world_time_capture_incomplete"] = "capture incomplete",
            ["ui.world_time_contract"] = "Counts proven forward Duckov world-clock movement, including automatic boot/time-target jumps, exact sleep, and other native fast-forward. It is not real-world play time, active raid time, loading time, or wall-clock time.",
            ["ui.crafting_capture_incomplete"] = "capture incomplete",
            ["ui.crafting_actions"] = "Successful crafting actions",
            ["ui.crafting_quantity"] = "Produced item quantity",
            ["ui.crafting_recipe"] = "Recipe",
            ["ui.crafting_batch"] = "Declared batch",
            ["ui.crafting_resources"] = "Most used crafting resources",
            ["ui.crafting_outputs"] = "Most crafted items",
            ["ui.crafting_resource"] = "Declared resource cost",
            ["ui.crafting_currency"] = "Declared currency charged",
            ["ui.crafting_currency_actions"] = "Currency-charged crafting actions",
            ["ui.no_crafting_resources"] = "No proven crafting-resource consumption recorded for this save generation.",
            ["ui.no_crafting"] = "No proven crafting completions recorded for this save generation.",
            ["ui.pre_m13_unavailable"] = "earlier crafted-item history unavailable",
            ["ui.pre_m16_resources_unavailable"] = "earlier crafting resource-cost history unavailable",
            ["ui.pre_m16_currency_unavailable"] = "earlier crafting currency-cost history unavailable",
            ["ui.crafting_contract"] = "One action is one correlated completion of native output delivery before downstream crafting callbacks. Produced quantity and item/currency costs are immutable formula declarations captured when Craft starts and published only after the preceding native Pay succeeds and delivery completes. Attempts, failed payment, inventory deltas, holdings, historical action counts, current recipe metadata, and Money/Cash split inference are excluded. Totals are save-generation lifetime only; workstation and run/map attribution are unavailable.",
            ["ui.weapon_equipment"] = "Weapons by equipped character slot and nested slot",
            ["ui.armor_and_gear"] = "Armor & gear",
            ["ui.loadouts"] = "Loadouts",
            ["ui.weapons"] = "Weapons",
            ["ui.totems"] = "Totems",
            ["ui.recurring_loadouts"] = "Recurring loadouts (at least two completed runs)",
            ["ui.recent_run_loadouts"] = "Recent run loadouts",
            ["ui.observed_totem_time"] = "Observed totem state time (presence; Unknown is not active effect time)",
            ["ui.proven_active_totem_time"] = "Proven-active totem-set time",
            ["ui.recent_run_economy"] = "Recent run economy",
            ["ui.proven_empty"] = "No"
        };

    public static void ConfigureNativeResolver(Func<string, string?>? resolver) => nativeResolver = resolver;

    internal static IReadOnlyDictionary<string, string> EnglishFallbacks => English;

    internal static IReadOnlyDictionary<string, string> GermanFallbacks => GermanText.All;

    public static string Get(string key) => Resolve(key, nativeResolver);

    internal static string Resolve(string key, Func<string, string?>? resolver)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A UI localization key is required.", nameof(key));
        if (resolver != null)
        {
            try
            {
                var localized = resolver(key);
                if (!string.IsNullOrWhiteSpace(localized)
                    && !string.Equals(localized, key, StringComparison.Ordinal))
                {
                    return localized;
                }
            }
            catch
            {
                // A native localization failure must never make the statistics panel unusable.
            }
        }

        return English.TryGetValue(key, out var fallback) ? fallback : key;
    }

    public static string FormatProvenEmpty(string slotDisplayName) =>
        $"{Get("ui.proven_empty")} {(string.IsNullOrWhiteSpace(slotDisplayName) ? "slot item" : slotDisplayName)}";

    public static string FormatMetric(long value, AdapterCapabilityState state) =>
        state == AdapterCapabilityState.DisabledIncompatible
            ? Get("ui.unsupported")
            : value.ToString(CultureInfo.InvariantCulture);

    public static string FormatMetric(double value, AdapterCapabilityState state) =>
        state == AdapterCapabilityState.DisabledIncompatible
            ? Get("ui.unsupported")
            : value.ToString("0.###", CultureInfo.InvariantCulture);

    internal static string FormatContainers(
        ContainerStatisticsAggregate statistics,
        AdapterCapabilityState currentCapability,
        Func<string, string>? text = null)
    {
        if (statistics == null) throw new ArgumentNullException(nameof(statistics));
        var resolve = text ?? Get;
        var value = statistics.UniqueContainersLooted.ToString(CultureInfo.InvariantCulture);
        if (statistics.WasRepairedFromInvalidState)
            return $"{value} ({resolve("ui.repaired_unavailable")})";
        return currentCapability == AdapterCapabilityState.Supported
            ? value
            : $"{value} ({resolve("ui.unsupported")})";
    }

    public static string FormatWorldTimeCount(long value, MetricAvailability availability)
    {
        if (availability.State != AdapterCapabilityState.DisabledIncompatible)
            return value.ToString(CultureInfo.InvariantCulture);
        return value == 0
            ? Get("ui.unsupported")
            : $"{value.ToString(CultureInfo.InvariantCulture)} ({Get("ui.world_time_capture_incomplete")})";
    }

    public static string FormatWorldTimeDuration(long ticks, MetricAvailability availability)
    {
        if (availability.State == AdapterCapabilityState.DisabledIncompatible && ticks == 0)
            return Get("ui.unsupported");
        var duration = TimeSpan.FromTicks(ticks);
        var formatted = duration.TotalDays >= 1
            ? $"{(long)duration.TotalDays}d {duration.Hours:00}:{duration.Minutes:00}:{duration.Seconds:00}"
            : $"{(long)duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        return availability.State == AdapterCapabilityState.DisabledIncompatible
            ? $"{formatted} ({Get("ui.world_time_capture_incomplete")})"
            : formatted;
    }

    public static string FormatRoute(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        if (!HasAvailableSegments(run))
            return "Route unavailable";
        return string.Join(" → ", run.Segments.OrderBy(value => value.SegmentIndex).Select(value => value.MapDisplayName));
    }

    public static bool HasAvailableSegments(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        return run.RouteCapabilities.OrderedRoute.State == AdapterCapabilityState.Supported
               && run.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported
               && run.Segments.Count > 0;
    }

    public static bool HasAvailableEventAttribution(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        return HasAvailableSegments(run)
               && run.RouteCapabilities.EventAttribution.State == AdapterCapabilityState.Supported;
    }

    public static bool HasKnownEventAttribution(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        return HasAvailableEventAttribution(run)
               || (HasAvailableSegments(run) && run.HistoricalEventAttributionIncomplete);
    }
}
