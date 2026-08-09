# Installation and compatibility

## Supported baseline for v0.1.0

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- Windows, single player

The package is intentionally self-contained except for Duckov's own assemblies. It must not contain Duckov assemblies or `0Harmony.dll`.

## Important: activate UDS on every cold launch

On the verified Duckov `2.3.30` setup with no Duckov Workshop subscriptions, Duckov persists the UDS enabled preference but does not automatically activate the local mod after restart.

Before selecting a save on every cold launch:

1. Open **Mods**.
2. If the left UDS indicator is unchecked, click it exactly once.
3. Confirm the check mark appears, then return to the main menu.

This v0.1.0 workaround is required for reliable tracking. No Harmony or unrelated Workshop dependency is required.

UDS stores read-only SHA-256 and `SaveTime` observations of the selected Duckov save in UDS's own external profile; it never writes the save. While active, Duckov's public pre-save event lets UDS persist a short-lived expected-save marker in that external profile. This preserves continuity when Duckov completes a normal save and then crashes before UDS can observe the new file, or when the player selects that same slot again in the same Duckov process. Re-selection closes only the prior UDS session checkpoint before identity comparison; a later verified observation or clean application shutdown consumes or clears the marker. The expected Duckov save step itself must fall within 30 seconds of the pre-save event.

If Duckov is played or a slot is reused while UDS is inactive, no expected-save marker is available. The next active launch intentionally archives the prior UDS profile whenever continuity cannot be proven instead of merging possibly unrelated statistics. Always activate UDS before selecting a save if you want one continuous statistics generation.

## Install

1. Close Escape From Duckov.
2. For an upgrade, remove only the old `<Duckov>\Duckov_Data\Mods\UltimateDuckovStatistics\` folder; UDS statistics are stored elsewhere and remain untouched.
3. Extract the new `UltimateDuckovStatistics` folder into `<Duckov>\Duckov_Data\Mods\`.
4. Start Duckov, accept its mod agreement if prompted, enable **Ultimate Duckov Statistics**, and restart if Duckov requests it.
5. Follow the prominent per-launch activation procedure above.
6. From the main menu or base, press F8 to open the UDS panel.

UDS data and exports are written outside the game saves under `%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\`.

## Uninstall

Close Duckov and remove only `<Duckov>\Duckov_Data\Mods\UltimateDuckovStatistics\`. Existing UDS statistics remain outside the game directory unless the user removes them separately.

## Known v0.1.0 limitations

- Statistics begin at installation; no history is reconstructed.
- Only successful main-duck item uses in raids count.
- Healing restored is not tracked until M2.
- Overview, Items, and Diagnostics are the only enabled tabs.
- No Steam Workshop package is published for v0.1.0.
- On the verified zero-Workshop-subscription setup, Duckov does not automatically activate persisted local mods on a cold launch. UDS must be checked once per launch as described above; this is a Duckov loader limitation, not a UDS data-setting failure.
