# Install and use Ultimate Duckov Statistics

This guide accompanies **Ultimate Duckov Statistics 1.0.0**, prepared for its first Steam Workshop release. English and German are included. The first listing and subscription-install verification are pending; check the [project releases](https://github.com/bamboechop/ultimate-duckov-statistics/releases) for publication rather than treating the version number as proof of release. The supported upgrade baseline begins when 1.0.0 is explicitly distributed through the verified Workshop channel. Earlier `0.x` builds and v1 release candidates were voluntary testing artifacts.

## Verified native baseline

- Escape from Duckov 2.3.30, Steam build 24013657, Unity 2022.3.62f2.
- Windows, single player.
- [HarmonyLib Workshop item 3589088839](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), verified at Harmony 2.4.1.0.

Game and Harmony assemblies are supplied separately. The UDS package contains exactly `info.ini`, two UDS DLLs, this guide, and `LICENSE`.

## Local installation and Workshop transition

Use the local package until the official Workshop listing is available. When switching to a Workshop subscription, close Duckov and remove the manually installed UDS mod folder first so only one UDS copy is loaded. Keep the separate statistics directory; changing installation channel does not require a statistics reset. The Workshop item ID will be recorded after the first listing is created and verified.

1. Close Duckov and install the HarmonyLib dependency.
2. Retain a backup of the existing `UltimateDuckovStatistics` mod folder. Replace only that folder under `<Duckov>/Duckov_Data/Mods/` with the extracted package.
3. Launch Duckov. Open Mods, confirm HarmonyLib and UDS are active, and follow any native restart request before selecting a save.
4. Select the desired save. Outside raids, open **Statistics** from the main menu or base pause menu, or use configurable F8.
5. Check Diagnostics for current capability and persistence health. An unavailable integration may leave F8 as the access path.

The first public format identity is `uds-profile-v1`, **schema 1**. Its data directory is `%USERPROFILE%/AppData/LocalLow/TeamSoda/Duckov/UltimateDuckovStatistics/v1/`. Earlier local 1.0.0 preparation builds and release candidates used development schema 18. The final baseline intentionally starts fresh: those profiles are not converted. If present, incompatible profiles are archived intact and a fresh current profile is opened. Older `0.x` data beside this directory is neither imported nor deleted. Do not copy an older-format profile into the current directory. Schema-1 backup/temporary recovery and reinstallation preserve valid current-format statistics. Schema versions track the stored format independently of the mod release number.

To uninstall, close Duckov and remove only its UDS mod folder. Statistics remain in the external data directory. The repository deployment tool verifies Duckov is closed, checks every deployed hash, and retains a verified prior-mod backup under repository `artifacts/deployment-backups/` for reversal.

## Statistics and access

All ten retained tabs use the same native shell. It supports English/German language switching, wrapping, scrolling with overflow indicators, keyboard focus, and responsive layout. Statistics access is deliberately unavailable during raids. Changing the selected save invalidates controls from the previous statistics generation. About precedes Diagnostics and remains available with an empty profile or unavailable measurements whenever the panel can open. It contains the mod description, author attribution, and a translation invitation directing volunteers to Steam Workshop comments, with no external links or browser actions.

Runs begin when the living main duck gains control in a native raid. Loading/base activity is excluded from raid duration and movement. Unknown identity, unavailable capture, partial attribution, repaired evidence, and proven empty observations remain distinct. Disabling a dependency does not invent zeros or erase independently supported sibling metrics.

Overview and exports show total recorded distance, raid distance and recorded base distance. Raid distance uses completed/recovered runs; active checkpoints do not contribute to that lifetime total. Base movement is a separate sampled distance with pause/loading/placement exclusions. It starts at the first valid observation, cannot backfill earlier movement, and reports collection-start and partial coverage. No base activity is added to run, route or record statistics.

Accepted firing callbacks, weapon/ammunition identity and completed player projectiles have distinct meanings. Kills by the player are distinct from observed world deaths. Delayed damage uses application-time source/equipment evidence; a missing source remains unknown while a proven destination can retain its damage. Equipment and totem statistics describe observed state. Container deduplication uses native map/key evidence. Economy flows, current holdings, crafting actions/outputs/resources/total charges, item use/consumption/healing, and world time/sleep retain their separate scopes.

The verified baseline does not prove rejected trigger attempts, ammunition units consumed or projectile count per firing callback, tote-effect activations, the secured/lost disposition of fungible acquired Cash, crafting workstation/run/map attribution, multiple-output recipes, or a Money/Cash split of crafting charges. These omitted metrics are documented limitations and are not shipped as permanently disabled adapters. Missing Harmony, foreign patch conflicts, later contract drift, and incomplete live evidence can still disable otherwise supported metrics; Diagnostics identifies those reachable cases.

## Local data, export and reset

UDS has no telemetry or network reporting and never writes Duckov saves. It stores local per-save profiles, bounded diagnostics, checkpoints, recovery files and exports. It reads save fingerprints and native SaveTime evidence to prevent accidental mixing of unrelated generations. A normal native pre-save observation can preserve continuity across an interrupted save; playing a save while UDS is inactive may leave continuity unprovable, in which case the prior statistics generation is archived.

Diagnostics exports an immutable snapshot of the captured generation as JSON and CSV under the v1 data root. Paths remain available when clipboard copying fails. Treat exports as local gameplay data and inspect them before sharing.

Reset requires confirmation, defaults to Cancel, archives the current UDS generation read-only and starts an empty one. It does not reset the game save. A blocked durability boundary keeps the request pending and prevents duplicate submissions; a rolled-back failure retains the original generation. Do not remove backup or temporary files to bypass validation.

## Troubleshooting

- Confirm activation before save selection. Check the intended generation and current adapter states in Diagnostics.
- If native menu integration is unavailable, try F8 outside a raid. Long labels or finite content overflow should remain scrollable and must not suppress the shell.
- For persistence trouble, retain the profile, backup, temporary/checkpoint files and diagnostics. Failed persistence/transition work keeps its pending state and retries with bounded backoff; changing slots may remain deferred until that boundary is durable.
- For export, UI or statistics disagreement, retain the UDS version shown in Diagnostics, DLL hashes, ordered reproduction steps and relevant local export. Report through the [repository issues](https://github.com/bamboechop/ultimate-duckov-statistics/issues).
- Do not edit the Duckov save or reset real statistics as a troubleshooting shortcut. Recovery and reset rehearsals belong in isolated fixtures unless the user chooses otherwise.

The [release procedure](https://github.com/bamboechop/ultimate-duckov-statistics/blob/main/docs/RELEASE_PROCESS.md) tracks final artifact verification, Workshop setup and publication. The [M18 acceptance record](https://github.com/bamboechop/ultimate-duckov-statistics/blob/main/docs/M18_ACCEPTANCE.md) and [RC2 record](https://github.com/bamboechop/ultimate-duckov-statistics/blob/main/docs/RC2_RELEASE.md) retain the completed native qualification and accepted performance/coverage limits. Version preparation does not repeat or expand that evidence.
