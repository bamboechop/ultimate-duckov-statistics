# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) records proven single-player gameplay events in local per-save profiles. It never writes Duckov save files and has no telemetry or online account.

The source and package metadata target **1.0.0**, the planned first Steam Workshop release. It includes the M18 hardening, four post-M18 feature and UI batches, German localization and layout corrections, and the simplified About translation invitation. See [release notes](RELEASE_NOTES.md) for the final scope and [release preparation](docs/RELEASE_PROCESS.md) for the remaining publication steps. Version metadata alone does not indicate publication: [GitHub releases](https://github.com/bamboechop/ultimate-duckov-statistics/releases) remain authoritative, and the first Workshop listing and subscription-install verification are pending.

## Install and use

English and German are included. UDS follows the game's language, including changes while the panel is open. Tabs provide overflow indicators and scrolling controls; measured wrapping and spacing keep longer translations readable. Translation volunteers can leave their language in a Steam Workshop comment once the listing is available.

See [INSTALL.md](INSTALL.md) for installation, activation, data locations, export/reset and troubleshooting. The verified baseline is Duckov 2.3.30 / Steam build 24013657 / Unity 2022.3.62f2 on Windows, with the separately installed [HarmonyLib dependency](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) at 2.4.1.0. Game, Unity and Harmony DLLs are never bundled.

Outside raids, open Statistics from the main menu, base pause menu or configurable F8. The retained native shell contains Overview, Runs, Records, Combat, Equipment, Economy, Crafting, Item Use, About and Diagnostics. About introduces UDS and its author, and invites translation volunteers to leave a comment on the Steam Workshop page. It contains no external links or browser actions. Long localization text uses measured layout, clipping and scrolling. Missing required native objects can prevent construction; appearance differences do not reject the shell.

## Statistics and evidence

- Runs, physical movement, teleport/transition-excluded distance, ordered maps and repeated visits use native lifecycle boundaries. Active raid time excludes loading and pause. Records retain eligibility and integrity evidence.
- Overview and exports separate recorded base distance from recorded raid distance and show their combined total. Base collection begins with the first valid observation; it cannot reconstruct earlier movement. Collection-start and partial-coverage information describe its limits. Runs, routes and records remain raid-only.
- Accepted firing actions preserve proven weapon/ammunition pairing. Combat uses exact actual damage, actor ownership, player kills, observed world deaths, headshots and completed player projectiles. Delayed effects keep proven application-time source identity; unknown source evidence stays unknown while independently proven outcomes remain available.
- Equipment records observed loadout, attachment, native-slot and nested-slot state and active-raid duration. Proven empty, missing and partially observed state remain distinct. Container counting uses native map/key identity.
- Item activation, consumption and attributed healing remain separate. Known, unknown and modded items retain stable identities and supported effect classifications.
- Money and physical Cash flows are separate from current holdings. Hydration, internal transfers and unproven source attribution do not invent economic activity. A constant-size activation/sequence cursor protects economy replay.
- Crafting records proven successful output delivery, actions, quantities, recipe/batch identity, proven consumed item resources and total currency charge. World time records forward native clock movement and independently proven sleep completion.

The verified baseline does not prove rejected trigger attempts, ammunition units consumed or projectiles created per firing callback, tote-effect activation, secured/lost acquired Cash, crafting workstation/run/map context, multiple-output recipes, or a Money/Cash split of crafting charges. Those metrics have no dormant adapter or permanently disabled export surface. Missing Harmony, foreign patch conflicts, later native drift and partial live evidence can still degrade otherwise supported metrics without erasing independent siblings.

The [native contract documents](docs/M17_NATIVE_CONTRACTS.md) and [M18 removal inventory](docs/M18_REMOVAL_INVENTORY.md) explain these boundaries. Unknown, Unavailable, Partial, repaired evidence and supported zero are never interchangeable.

## Local data and current format

The first public format baseline is `uds-profile-v1`, **schema 1**, with data below `%USERPROFILE%/AppData/LocalLow/TeamSoda/Duckov/UltimateDuckovStatistics/v1/`. This deliberately starts fresh from the schema-18 development/RC profiles; they are incompatible and are not converted. If left on disk, the mod archives them intact and creates fresh statistics. Older `0.x` data is not imported or deleted. Clean installation and valid schema-1 reinstallation are supported; no development-data migration code ships. Incompatible/future profiles remain protected. Schema revisions follow stored-format changes independently of release versions.

Current-format validation, atomic primary/backup/temporary recovery, interrupted-run/session recovery, generation isolation, deferred replay watermarks and reset/export safety remain required. A failed durability boundary retains pending data with bounded retry and diagnostics. Reset archives only the UDS generation and defaults to Cancel. Export captures an immutable generation snapshot as local JSON and CSV.

Save fingerprints and native pre-save evidence prevent unrelated save generations from being combined. Playing while UDS is inactive can make continuity unprovable; the prior generation is then archived. See [local-data and privacy details](docs/LOCAL_DATA.md).

Every `0.x` GitHub download and v1 release candidate was a voluntary testing artifact. The planned supported upgrade baseline is 1.0.0 when it is explicitly published through the verified Steam Workshop channel. A local build or GitHub testing download alone does not establish that baseline.

## Build and qualify

Install .NET 8 and set `DUCKOV_PATH` to the local game root. Run:

```powershell
./scripts/build.ps1 -DuckovPath $env:DUCKOV_PATH
```

This runs Debug/Release tests, the installed-native probe, warning-free native build, exact-five-file packaging and artifact audits. The isolated shell suite source-links real panel/shell code and tests access, overflow, input/focus, listener and private-material ownership through boundary doubles. It cannot establish native rendering or GPU resource behavior.

`verify-reproducibility.ps1` builds an immutable commit under two checkout roots and compares both DLLs, both portable PDBs and deterministic ZIP bytes. `audit-artifacts.ps1 -OrdinaryRelease` checks builder-path leakage and absence of diagnostic IL call sites. `deploy.ps1` requires a closed game, retains a verified previous UDS package outside the native loader scan root and reads back exact deployed hashes.

Follow [PERFORMANCE.md](PERFORMANCE.md), [validation evidence](TESTING.md) and the [release procedure](docs/RELEASE_PROCESS.md). The [M18 acceptance record](docs/M18_ACCEPTANCE.md) and [RC2 record](docs/RC2_RELEASE.md) preserve completed reviews and native qualification, including accepted performance deviations and coverage limits. Synthetic stress tests support diagnosis; recorded ordinary Release versus Harmony-only gameplay captures establish the bounded native performance evidence. Version-only changes require new artifact verification, not a repeated unaffected gameplay campaign.
