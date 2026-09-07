# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) records proven single-player gameplay events in local per-save profiles. It never writes Duckov save files and has no telemetry or online account.

M17 is the completed feature baseline. M18 prepares `1.0.0-rc.1` by removing pre-v1 compatibility paths, impossible metrics and unused runtime code, hardening persistence, and qualifying the ordinary Release artifact. Required native qualification remains recorded in [M18 acceptance](docs/M18_ACCEPTANCE.md); this is not a production-readiness claim. Current delivery state is authoritative on [GitHub](https://github.com/bamboechop/ultimate-duckov-statistics).

## Install and use

See [INSTALL.md](INSTALL.md) for installation, activation, data locations, export/reset and troubleshooting. The verified baseline is Duckov 2.3.30 / Steam build 24013657 / Unity 2022.3.62f2 on Windows, with the separately installed [HarmonyLib dependency](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839) at 2.4.1.0. Game, Unity and Harmony DLLs are never bundled.

Outside raids, open Statistics from the main menu, base pause menu or configurable F8. The retained native shell contains Overview, Runs, Records, Combat, Equipment, Economy, Crafting, Item Use and Diagnostics. Long localization text uses measured layout, clipping and scrolling. Missing required native objects can prevent construction; appearance differences do not reject the shell.

## Statistics and evidence

- Runs, physical movement, teleport/transition-excluded distance, ordered maps and repeated visits use native lifecycle boundaries. Active raid time excludes loading and pause. Records retain eligibility and integrity evidence.
- Accepted firing actions preserve proven weapon/ammunition pairing. Combat uses exact actual damage, actor ownership, player kills, observed world deaths, headshots and completed player projectiles. Delayed effects keep proven application-time source identity; unknown source evidence stays unknown while independently proven outcomes remain available.
- Equipment records observed loadout, attachment, native-slot and nested-slot state and active-raid duration. Proven empty, missing and partially observed state remain distinct. Container counting uses native map/key identity.
- Item activation, consumption and attributed healing remain separate. Known, unknown and modded items retain stable identities and supported effect classifications.
- Money and physical Cash flows are separate from current holdings. Hydration, internal transfers and unproven source attribution do not invent economic activity. A constant-size activation/sequence cursor protects economy replay.
- Crafting records proven successful output delivery, actions, quantities, recipe/batch identity, proven consumed item resources and total currency charge. World time records forward native clock movement and independently proven sleep completion.

The verified baseline does not prove rejected trigger attempts, ammunition units consumed or projectiles created per firing callback, tote-effect activation, secured/lost acquired Cash, crafting workstation/run/map context, multiple-output recipes, or a Money/Cash split of crafting charges. Those metrics have no dormant adapter or permanently disabled export surface. Missing Harmony, foreign patch conflicts, later native drift and partial live evidence can still degrade otherwise supported metrics without erasing independent siblings.

The [native contract documents](docs/M17_NATIVE_CONTRACTS.md) and [M18 removal inventory](docs/M18_REMOVAL_INVENTORY.md) explain these boundaries. Unknown, Unavailable, Partial, repaired evidence and supported zero are never interchangeable.

## Local data and current format

The format identity is `uds-profile-v1`, with data below `%USERPROFILE%/AppData/LocalLow/TeamSoda/Duckov/UltimateDuckovStatistics/v1/`. The candidate starts fresh without importing or deleting pre-v1 data. Clean installation and current-format reinstallation are supported; no schema-by-schema `0.x` reader ships. Incompatible/future profiles are preserved intact.

Current-format validation, atomic primary/backup/temporary recovery, interrupted-run/session recovery, generation isolation, deferred replay watermarks and reset/export safety remain required. A failed durability boundary retains pending data with bounded retry and diagnostics. Reset archives only the UDS generation and defaults to Cancel. Export captures an immutable generation snapshot as local JSON and CSV.

Save fingerprints and native pre-save evidence prevent unrelated save generations from being combined. Playing while UDS is inactive can make continuity unprovable; the prior generation is then archived. See [local-data and privacy details](docs/LOCAL_DATA.md).

Every `0.x` GitHub download was a development artifact for voluntary testing. Supported upgrade guarantees begin with the first version explicitly declared as distributed through a supported channel. The RC does not silently establish that publication baseline.

## Build and qualify

Install .NET 8 and set `DUCKOV_PATH` to the local game root. Run:

```powershell
./scripts/build.ps1 -DuckovPath $env:DUCKOV_PATH
```

This runs Debug/Release tests, the installed-native probe, warning-free native build, exact-five-file packaging and artifact audits. The isolated shell suite source-links real panel/shell code and tests access, overflow, input/focus, listener and private-material ownership through boundary doubles. It cannot establish native rendering or GPU resource behavior.

`verify-reproducibility.ps1` builds an immutable commit under two checkout roots and compares both DLLs, both portable PDBs and deterministic ZIP bytes. `audit-artifacts.ps1 -OrdinaryRelease` checks builder-path leakage and absence of diagnostic IL call sites. `deploy.ps1` requires a closed game, retains a verified previous UDS package outside the native loader scan root and reads back exact deployed hashes.

Follow [PERFORMANCE.md](PERFORMANCE.md), the [M18 capture matrix](docs/M18_CAPTURE_MATRIX.json), [validation evidence](TESTING.md) and [release procedure](docs/RELEASE_PROCESS.md). Synthetic stress tests support diagnosis; ordinary Release D versus Harmony-only B gameplay captures determine native performance acceptance. New metrics and UI redesign are outside M18.
