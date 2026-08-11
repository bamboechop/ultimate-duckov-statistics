# Installation and compatibility

## Supported baseline for v0.4.0

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- Windows, single player
- HarmonyLib Workshop item `3589088839`, version `2.4.1.0` or a newer build retaining the verified reflection API

The UDS package must not contain Duckov assemblies or `0Harmony.dll`. HarmonyLib is supplied and loaded separately by its Workshop item.

## Required HarmonyLib Workshop item

Subscribe to [HarmonyLib for Duckov](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) before installing UDS. UDS v0.4.0 requires HarmonyLib for M2 healing attribution, but M3 run lifecycle/movement and M4 weapons/ammunition use public native Duckov APIs and add no Harmony patches. UDS uses reflection for healing, so consumable-use, run, and weapon tracking can still load while Diagnostics reports healing as `DisabledIncompatible` if Harmony is missing, too old, its API is incompatible, any foreign prefix, postfix, transpiler, or finalizer touches a required method, or a required UDS callback disappears. UDS validates the exact healing patch set at activation, periodically, and at attribution callback boundaries. If Duckov activates UDS before the Workshop loader, UDS retries safely after Harmony appears and updates Diagnostics. If Harmony unpatch cleanup fails, UDS keeps attribution detached, retains and retries that cleanup, and prevents a same-process reactivation from colliding with leftover callbacks.

After every cold launch and before selecting a save:

1. Open **Mods**.
2. Confirm the HarmonyLib Workshop item is installed and active.
3. If the left UDS indicator is unchecked, click it exactly once.
4. Confirm the UDS check mark appears, then return to the main menu.
5. Outside a raid, open UDS Diagnostics and confirm `native-healing-attribution: Supported` before relying on healing totals.

UDS stores read-only SHA-256 and `SaveTime` observations of the selected Duckov save in UDS's own external profile; it never writes the save. While active, Duckov's public pre-save event lets UDS persist a short-lived expected-save marker in that external profile. This preserves continuity when Duckov completes a normal save and then crashes before UDS can observe the new file, or when the player selects that same slot again in the same Duckov process. Re-selection closes only the prior UDS session checkpoint before identity comparison; a later verified observation or clean application shutdown consumes or clears the marker. The expected Duckov save step itself must fall within 30 seconds of the pre-save event.

If Duckov is played or a slot is reused while UDS is inactive, no expected-save marker is available. The next active launch intentionally archives the prior UDS profile whenever continuity cannot be proven instead of merging possibly unrelated statistics. Always activate UDS before selecting a save if you want one continuous statistics generation.

## Install

1. Close Escape From Duckov.
2. Install or update the required HarmonyLib Workshop item linked above.
3. For an upgrade, remove only the old `<Duckov>\Duckov_Data\Mods\UltimateDuckovStatistics\` folder; UDS statistics are stored elsewhere and remain untouched.
4. Extract the new `UltimateDuckovStatistics` folder into `<Duckov>\Duckov_Data\Mods\`.
5. Start Duckov, accept its mod agreement if prompted, enable **Ultimate Duckov Statistics**, and restart if Duckov requests it.
6. Follow the activation and Diagnostics checks above.
7. From the main menu or base, press F8 to open the UDS panel.

UDS data and exports are written outside the game saves under `%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\`.

## Uninstall

Close Duckov and remove only `<Duckov>\Duckov_Data\Mods\UltimateDuckovStatistics\`. Existing UDS statistics remain outside the game directory unless the user removes them separately.

## M3 data and exports

- A run starts only after native raid initialization when the alive main duck has player control; base and loading activity do not start runs.
- Run outcomes are Extracted, Died, or Interrupted. Active duration excludes pause/loading; wall-clock duration is diagnostic.
- Overview, Runs, Records, Combat, Items, and Diagnostics are enabled. Combat separates firing actions, loaded ammunition units consumed, and projectiles/pellets, with per-weapon, per-ammunition, per-run, and per-map aggregates. On Duckov 2.3.30 the public event proves firing actions and event-time identities; actual ammunition consumption and projectile creation display as unavailable.
- Each Runs entry shows integrity and whether it is eligible for Records, including the exclusion reason. Records show shortest/longest extraction and death active times overall and per map.
- Physical movement and teleport/excluded displacement are stored separately. If movement or map compatibility is unavailable, the panel and Diagnostics show that state explicitly.
- Exports contain `statistics.json`, `overview.csv`, `groups.csv`, `items.csv`, `runs.csv`, `run_totals.csv`, `map_totals.csv`, `records.csv`, `combat_totals.csv`, `weapon_totals.csv`, and `ammunition_totals.csv`.

## Known v0.4.0 limitations

- Statistics begin at installation; no history is reconstructed.
- Only successful main-duck item uses in raids count.
- Healing totals start with v0.2.0; v0.1.0 usage history migrates with zero historical healing because past HP restoration cannot be reconstructed reliably. Run and movement history starts with v0.3.0. Weapon and ammunition history starts with v0.4.0 and is not reconstructed during schema-4 migration.
- The verified public firing event proves accepted firing callbacks, not trigger attempts. Reloads and dry-fire attempts are not counted. `UseABullet` can skip consumption and `ShootOneBullet` can return before projectile initialization, so the public callback cannot prove actual ammunition units consumed or projectiles created; both submetrics remain unavailable instead of being inferred from cached ammunition or configured `ShotCount`.
- Only healing applied through the verified item/effect paths is attributed. Unrelated regeneration, pets/companions, base use, cancelled uses, failed uses, and overheal are excluded.
- Duration records exclude Interrupted runs and runs tagged for cheats/custom difficulty or gameplay-altering mods. The required `HarmonyLoadMod` infrastructure by itself does not disqualify a run.
- UDS itself remains a local GitHub package; only the HarmonyLib dependency is installed through Steam Workshop.
