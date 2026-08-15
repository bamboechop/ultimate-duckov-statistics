# Installation and compatibility

## Supported baseline for v0.8.1

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- Windows, single player
- HarmonyLib Workshop item `3589088839`, version `2.4.1.0` or a newer build retaining the verified reflection API

The UDS package must not contain Duckov assemblies or `0Harmony.dll`. HarmonyLib is supplied and loaded separately by its Workshop item.

## Required HarmonyLib Workshop item

Subscribe to [HarmonyLib for Duckov](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) before installing UDS. UDS v0.8.1 requires HarmonyLib for M2 healing attribution, the minimal M5 combat scopes, and M7's separate corpse-provenance owner. M8 route boundaries use public native hooks and add no Harmony owner. If Harmony is missing, too old, incompatible, or a required method has an unsafe foreign patch, only the affected Harmony-backed capabilities display `DisabledIncompatible`; proven independent statistics continue.

After every cold launch and before selecting a save:

1. Open **Mods**.
2. Confirm the HarmonyLib Workshop item is installed and active.
3. If the left UDS indicator is unchecked, click it exactly once.
4. Confirm the UDS check mark appears, then return to the main menu.
5. Outside a raid, open UDS Diagnostics and confirm `native-healing-attribution: Supported`, the `native-*` combat capabilities are `Supported`, and `native-container-loot-access: Supported` before relying on those totals.

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

## M3-M8 data and exports

- A run starts only after native raid initialization when the alive main duck has player control; base and loading activity do not start runs.
- Run outcomes are Extracted, Died, or Interrupted. Active duration excludes pause/loading; wall-clock duration is diagnostic.
- Overview, Runs, Records, Combat, Equipment, Items, and Diagnostics are enabled. M4 separates firing actions, loaded ammunition units consumed, and configured projectile outcomes; the public event proves firing actions and event-time identities, while actual loaded-ammunition consumption remains unavailable.
- M5 records actual HP loss, compatible completed-projectile accuracy, melee swings/hits, enemy kills, player deaths, ownership, stable enemy/killer identity, Zombie/unknown family, cause, event-time weapon/ammunition identity, independently proven head-targeted hits, and headshot final blows. Unsupported or inapplicable identities remain visible as unknown.
- Each Runs entry shows a compact route, expandable segment evidence, integrity, and Records eligibility. Records show complete-run shortest/longest extraction and death active times overall and by starting map.
- Physical movement, proven teleport displacement, and transition/loading-excluded displacement are stored separately. If movement, active-map, or route compatibility is unavailable, only dependent metrics are disabled and Diagnostics show why.
- M6 records character-slot and selected-weapon durations, attachment-aware loadouts, direct and tote-carried totem presence, proven-active direct-totem sets, and event-time equipment associations. Tote-carried activation remains unknown and disabled rather than inferred.
- M7 records unique non-corpse containers whose loot access successfully begins for the exact main duck during an active raid. Reopening within one run is deduplicated by native `GetKey()`; the scope resets for the next run.
- M8.1 retains event-time equipment/combat attribution while reading associations from native-event-refreshed immutable snapshots, republishes an unchanged loadout once after every run-segment change, reconciles projectile context once per frame while still checking the exact run identity at capture and outcome time, checks Harmony integrity through allocation-free shared-state identity stamps after full activation validation, and performs durable active-run checkpoint plus lifetime item/healing profile storage as single-flight background writes. Same-frame item/healing mutations coalesce into one immutable profile snapshot only after the active-run checkpoint containing the same accepted mutations is durable; boundary, completion, export, and shutdown paths flush that checkpoint barrier and then wait for pending profile persistence before continuing.
- Exports contain `statistics.json`, the existing fifteen CSVs, and M8's `routes.csv`, `segments.csv`, `segment_events.csv`, and `route_map_totals.csv`. `map_totals.csv` remains starting-map complete-run history; route-map totals are separate.

## Known v0.8.1 limitations

- Statistics begin at installation; no history is reconstructed.
- Pre-M8 ending maps, ordered routes, segments, transition displacement, and route-aware per-map attribution are unavailable rather than reconstructed as fake one-segment routes.
- Only successful main-duck item uses in raids count.
- Healing totals start with v0.2.0; run/movement with v0.3.0; weapon identity/firing actions with v0.4.0; M5 combat with v0.5.0; equipment/totem data with v0.6.0; container data with v0.7.0; and route attribution with v0.8.0. Schema-8 migration preserves M1-M7 and marks historical route data unavailable.
- A route stores at most 64 visits and 2,048 event associations. Reaching a bound disables route-dependent attribution without evicting or merging earlier evidence; overall proven M1-M7 totals continue.
- If a delayed healing or combat outcome occurs while loading and no destination segment is yet proven, the overall result remains counted but event attribution and route-aware map totals become unavailable for that run rather than guessing a map.
- “Container looted” means the loot interface began successfully, not that any item was transferred. Counts and values of removed items are outside M7.
- Stable identity is the verified native position-derived `InteractableLootbox.GetKey()` integer. A missing, changed, or throwing identity contract disables container statistics rather than falling back to Unity runtime object IDs.
- The bounded active-run deduplication set stores at most 4,096 stable keys. Reaching that defensive bound disables the affected run metric rather than evicting old keys and risking double counts.
- Tote contents are identified only from the public `AnyThing` slot of built-in Tote Bag `Item.TypeID` 1255 instances carried in the exact main duck's top-level ordinary inventory. Their presence does not prove modifiers or effects are active. Tote activation remains disabled; tote-carried totems are persisted with activation `Unknown` and excluded from active-totem-set duration.
- The verified public firing event proves accepted firing callbacks, not trigger attempts. Reloads and dry-fire attempts are not counted. `UseABullet` can skip consumption and `ShootOneBullet` can return before projectile initialization, so the public callback cannot prove actual ammunition units consumed or projectiles created; both submetrics remain unavailable instead of being inferred from cached ammunition or configured `ShotCount`.
- Only healing applied through the verified item/effect paths is attributed. Unrelated regeneration, pets/companions, base use, cancelled uses, failed uses, and overheal are excluded.
- M5 accuracy is not firing-action accuracy. Its numerator and denominator are completed exact-main-duck projectile instances observed by the verified projectile lifecycle while a run remains active. A projectile still alive when the run terminates enters neither side of that run's ratio.
- Headshot means an independently observed native head-targeted exact-player projectile that causes actual enemy HP loss. It is not a geometric impact-point claim, and mouse/controller paths that do not expose that native flag remain uncounted rather than inferred. `DamageInfo.crit` is never used as headshot evidence.
- Enemy family is currently exact only for the native Zombie flag; other families remain `Unknown family`. Ammunition is exact for projectile-correlated damage and otherwise remains unknown, including delayed effects that preserve only weapon provenance.
- M5 combat, M6 equipment, and M7 container mutations share checkpoint writes coalesced to at most once per second, with a five-second periodic fallback and one-second failed-write retry. Item-use and healing lifetime mutations persist through a separate frame-coalesced profile writer; a strictly validated per-run watermark lets interruption recovery apply any exact, compositionally consistent checkpoint delta absent from its last profile snapshot. The active-run checkpoint is ordered before the corresponding lifetime profile snapshot so an abrupt stop cannot leave the watermark ahead of recoverable run evidence. A sudden process/OS failure can still lose up to approximately one second of accepted callbacks/state time that never reached the active checkpoint; orderly shutdown and terminal completion flush in-memory totals.
- Duration records exclude Interrupted runs and runs tagged for cheats/custom difficulty or gameplay-altering mods. The required `HarmonyLoadMod` infrastructure by itself does not disqualify a run.
- UDS itself remains a local GitHub package; only the HarmonyLib dependency is installed through Steam Workshop.
