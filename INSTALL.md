# Install and use Ultimate Duckov Statistics

This `1.0.0-rc.1` package is prepared for voluntary qualification. Publication and production readiness require the remaining gates in the [M18 acceptance record](https://github.com/bamboechop/ultimate-duckov-statistics/blob/codex/m18-release-hardening/docs/M18_ACCEPTANCE.md). The first version explicitly distributed through a supported channel will declare the supported upgrade baseline. Earlier `0.x` GitHub downloads were development artifacts.

## Verified native baseline

- Escape from Duckov 2.3.30, Steam build 24013657, Unity 2022.3.62f2.
- Windows, single player.
- [HarmonyLib Workshop item 3589088839](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), verified at Harmony 2.4.1.0.

Game and Harmony assemblies are supplied separately. The UDS package contains exactly `info.ini`, two UDS DLLs, this guide, and `LICENSE`.

## Installation and tester transition

1. Close Duckov and install the HarmonyLib dependency.
2. Retain a backup of the existing `UltimateDuckovStatistics` mod folder. Replace only that folder under `<Duckov>/Duckov_Data/Mods/` with the extracted package.
3. Launch Duckov. Open Mods, confirm HarmonyLib and UDS are active, and follow any native restart request before selecting a save.
4. Select the desired save. Outside raids, open **Statistics** from the main menu or base pause menu, or use configurable F8.
5. Check Diagnostics for current capability and persistence health. An unavailable integration may leave F8 as the access path.

The v1 format identity is `uds-profile-v1`. Its separate data directory is `%USERPROFILE%/AppData/LocalLow/TeamSoda/Duckov/UltimateDuckovStatistics/v1/`. Starting the candidate creates fresh v1 statistics without importing, resetting, or deleting the older UDS data beside it. Do not copy an old profile into this directory. Incompatible/future identities are preserved intact and a fresh current profile is opened; current-format backup and temporary-file recovery remain supported. Reinstalling the same current-format candidate preserves its statistics.

To uninstall, close Duckov and remove only its UDS mod folder. Statistics remain in the external data directory. The repository deployment tool verifies Duckov is closed, checks every deployed hash, and retains a verified prior-mod backup under repository `artifacts/deployment-backups/` for reversal.

## Statistics and access

All nine retained tabs use the same native shell. It supports localized labels, scrolling, keyboard focus, and responsive layout. Statistics access is deliberately unavailable during raids. Changing the selected save invalidates controls from the previous statistics generation.

Runs begin when the living main duck gains control in a native raid. Loading/base activity is excluded from raid duration and movement. Unknown identity, unavailable capture, partial attribution, repaired evidence, and proven empty observations remain distinct. Disabling a dependency does not invent zeros or erase independently supported sibling metrics.

Accepted firing callbacks, weapon/ammunition identity and completed player projectiles have distinct meanings. Kills by the player are distinct from observed world deaths. Delayed damage uses application-time source/equipment evidence; a missing source remains unknown while a proven destination can retain its damage. Equipment and totem statistics describe observed state. Container deduplication uses native map/key evidence. Economy flows, current holdings, crafting actions/outputs/resources/total charges, item use/consumption/healing, and world time/sleep retain their separate scopes.

The verified baseline does not prove rejected trigger attempts, ammunition units consumed or projectile count per firing callback, tote-effect activations, the secured/lost disposition of fungible acquired Cash, crafting workstation/run/map attribution, multiple-output recipes, or a Money/Cash split of crafting charges. These omitted metrics are documented limitations and are not shipped as permanently disabled adapters. Missing Harmony, foreign patch conflicts, later contract drift, and incomplete live evidence can still disable otherwise supported metrics; Diagnostics identifies those reachable cases.

## Local data, export and reset

UDS has no telemetry or network reporting and never writes Duckov saves. It stores local per-save profiles, bounded diagnostics, checkpoints, recovery files and exports. It reads save fingerprints and native SaveTime evidence to prevent accidental mixing of unrelated generations. A normal native pre-save observation can preserve continuity across an interrupted save; playing a save while UDS is inactive may leave continuity unprovable, in which case the prior statistics generation is archived.

Diagnostics exports an immutable snapshot of the captured generation as JSON and CSV under the v1 data root. Paths remain available when clipboard copying fails. Treat exports as local gameplay data and inspect them before sharing.

Reset requires confirmation, defaults to Cancel, archives the current UDS generation read-only and starts an empty one. It does not reset the game save. A blocked durability boundary keeps the request pending and prevents duplicate submissions; a rolled-back failure retains the original generation. Do not remove backup or temporary files to bypass validation.

## Troubleshooting and release readiness

- Confirm activation before save selection. Check the intended generation and current adapter states in Diagnostics.
- If native menu integration is unavailable, try F8 outside a raid. Long labels or finite content overflow should remain scrollable and must not suppress the shell.
- For persistence trouble, retain the profile, backup, temporary/checkpoint files and diagnostics. Failed persistence/transition work keeps its pending state and retries with bounded backoff; changing slots may remain deferred until that boundary is durable.
- For export, UI or statistics disagreement, retain the candidate version, DLL hashes, ordered reproduction steps and relevant local export. Report through the [repository issues](https://github.com/bamboechop/ultimate-duckov-statistics/issues).
- Do not edit the Duckov save or reset real statistics as a troubleshooting shortcut. Recovery and reset rehearsals belong in isolated fixtures unless the user chooses otherwise.

Workshop upload, dependency declaration, release publication and v1 promotion remain separate approval steps. Consult the repository's [release documentation](https://github.com/bamboechop/ultimate-duckov-statistics) and [M18 acceptance record](https://github.com/bamboechop/ultimate-duckov-statistics/blob/codex/m18-release-hardening/docs/M18_ACCEPTANCE.md) for qualification evidence and unresolved gates.
