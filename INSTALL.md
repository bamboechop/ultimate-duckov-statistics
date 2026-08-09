# Installation and compatibility

## Supported baseline for v0.2.0

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- Windows, single player
- HarmonyLib Workshop item `3589088839`, version `2.4.1.0` or a newer build retaining the verified reflection API

The UDS package must not contain Duckov assemblies or `0Harmony.dll`. HarmonyLib is supplied and loaded separately by its Workshop item.

## Required HarmonyLib Workshop item

Subscribe to [HarmonyLib for Duckov](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) before installing UDS. UDS v0.2.0 requires HarmonyLib for healing attribution, but it uses reflection so consumable-use tracking can still load and Diagnostics will report `DisabledIncompatible` if Harmony is missing, too old, its API is incompatible, or a transpiler conflicts with a required method. If Duckov activates UDS before the Workshop loader, UDS retries safely after Harmony appears and updates Diagnostics.

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

## Known v0.2.0 limitations

- Statistics begin at installation; no history is reconstructed.
- Only successful main-duck item uses in raids count.
- Healing totals start with v0.2.0; v0.1.0 usage history migrates with zero historical healing because past HP restoration cannot be reconstructed reliably.
- Only healing applied through the verified item/effect paths is attributed. Unrelated regeneration, pets/companions, base use, cancelled uses, failed uses, and overheal are excluded.
- Overview, Items, and Diagnostics are the only enabled tabs.
- UDS itself remains a local GitHub package; only the HarmonyLib dependency is installed through Steam Workshop.
