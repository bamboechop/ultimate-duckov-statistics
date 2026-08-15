# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) is a local, single-player statistics mod for Escape From Duckov. Version 0.8.1 retains the complete M1-M8 statistics contract while hardening measured equipment, Harmony-integrity, projectile-context, and checkpoint-persistence hot paths.

The mod never modifies Duckov save files. Its data is stored under:

```text
%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\
```

## Status

M0-M8 are published through the [v0.8.0 GitHub pre-release](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v0.8.0). M8.1 preserves every statistic and crash-safety contract while replacing measured equipment, patch-integrity, projectile, checkpoint, and lifetime-profile hot-path costs with bounded cached or deferred work. The final review correction also preserves overall equipment tracking if route context becomes unavailable. Exact product commit `90384352d323e6ea19dfa607c7da18162dbcefcb` passes 507/507 tests and the installed-game/native/package gates. Its final Harmony-only B versus production-D campaign contains three accepted captures in each of seven scenarios and meets the tighter 5% median/10% p99 engineering target everywhere; the largest positive overhead is +0.313% whole median, +3.721% whole p99, +1.355% action median, and +4.526% action p99, with no repeatable new action cluster. The supplementary three-map soak, exact event deltas, Runs/Items/Equipment UI, JSON plus nineteen CSVs, deployed-byte readback, and residue-free byte-identical shutdown also pass. The 221,763-byte exact-candidate ZIP is SHA-256 `2510317d1aca11a19ab658941b513fa630d6b70f2a6d8065c77b57a744cdeb62`. [Draft PR #9](https://github.com/bamboechop/ultimate-duckov-statistics/pull/9) remains open, draft, and unmerged. M9 economy and M10 full UI/release hardening remain future work; no v0.8.1 tag, GitHub release, merge, or Steam Workshop upload has occurred.

## Build prerequisites

- Windows
- .NET 8 SDK
- A local Escape From Duckov installation
- Steam Workshop item [HarmonyLib (2.4.1.0)](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), installed and enabled
- `DUCKOV_PATH` set to the game root, for example `E:\SteamLibrary\steamapps\common\Escape from Duckov`

Game assemblies are referenced locally with copy-local disabled. They are never committed, downloaded by CI, or included in release packages. UDS discovers the separately installed HarmonyLib at runtime. Healing, M5 combat attribution, and M7 corpse provenance each use distinct, minimal, version-checked patch owners; a missing/incompatible contract or unsafe foreign patch disables the affected metrics. Failed UDS unpatch cleanup retains the exact owner, is retried, and blocks unsafe same-process replacement. UDS never bundles `0Harmony.dll`.

> **Activation check:** Open **Mods** after a cold launch and confirm both HarmonyLib and UDS are active before selecting a save. See [INSTALL.md](INSTALL.md#required-harmonylib-workshop-item).

UDS fingerprints saves read-only and never modifies them. While active, it records a short-lived pre-save intent in its own external profile from Duckov's public save-collection event, so a normal save completed immediately before a crash or same-process re-selection of the current slot can retain the same UDS generation. If a save changes while UDS is inactive, that proof is unavailable and UDS conservatively archives the prior statistics generation rather than risk merging a reused slot.

Runs begin only when the native raid is initialized and the live main duck actually has player control. Active duration excludes pause and loading. Movement is sampled at approximately 5 Hz with the real monotonic sample interval and verified native walk/run/dash speeds. Explicit-position, implausible, and long-gap displacement inside an active map is retained as teleport distance; displacement caused solely by loading or a map boundary is retained independently as transition-excluded distance and never inflates physical or teleport totals. Interrupted and integrity-flagged runs stay visible but do not enter default duration records; each Runs row shows its integrity tag and an explicit eligible/excluded Records status with the exclusion reason.

Version 0.8.1 keeps one expedition active across proven full-scene and subscene transitions. It stores starting/ending maps, an ordered stable-ID route, distinct repeated visits, segment duration and movement, and source/outcome segment associations for delayed healing/combat. Loading displacement is separate from physical and proven teleport distance. Complete-run records remain overall and by starting map; route-aware per-map totals are built only from segments. Pre-M8 route history remains explicitly unavailable.

M4 uses the public `ItemAgent_Gun.OnMainCharacterShootEvent` from the verified Duckov build. Each accepted callback receives a unique UDS event ID and proves one firing action plus event-time weapon/ammunition identity. The event occurs after calls that may conditionally skip ammunition consumption or projectile initialization, so loaded-ammunition and projectile counts are explicitly unavailable rather than inferred from cached ammunition or configured `ShotCount`. Reloads, magazine transfers, inventory movement, base activity, loading, pause, non-main-duck actors, and dry fire do not create firing-action records.

M5 measures `Health.Hurt` before/after HP and therefore excludes rejected damage and overkill. A reliable ranged hit is one completed exact-main-duck projectile that caused positive actual enemy HP loss; penetration or repeated damage from that projectile cannot inflate the numerator. Accuracy uses those hits over completed player projectiles, not M4 firing actions. Critical hits never imply headshots.

M6 observes the exact main duck's public character-slot tree, ordinary inventory, and native slot/hold/inventory callbacks. Persisted identities use stable `Item.TypeID`, slot keys, and deterministic attachment signatures; runtime object IDs and localized names never determine identity. Durations use the same monotonic active-raid clock as M3 and therefore exclude pause/loading. Direct slotted totems with usable durability are proven active by the verified item-effect control flow. A totem plugged into the public `AnyThing` slot of a built-in Tote Bag (`Item.TypeID` 1255) carried in top-level ordinary character inventory is recorded as present with activation `Unknown`: tote activation is not inferred and the capability remains `DisabledIncompatible`. Equipment/combat rows are event-time temporal associations, not proof that an item or totem caused an outcome. Only loadouts observed in at least two completed runs enter lifetime recurring-loadout rankings; every run retains its own summary.

M7 observes public `InteractableLootbox.OnStartLoot`, after the interaction timer and inventory checks have succeeded. It requires the event-time interaction owner to be the exact main duck, reads the native private `GetKey()` contract reflectively for per-run deduplication, and excludes native enemy corpses plus persisted/player tombs using a separate version-checked corpse-provenance patch. Reopening a container in the same run does not increment; the same stable key may count again in another run. Proximity, attempts, locked/cancelled/failed interactions, corpses, base activity, item transfers, and loot value do not count.

The in-game panel enables Overview, Runs, Records, Combat, Equipment, Items, and Diagnostics. Runs shows a compact route with expandable segment evidence. One export action writes `statistics.json` plus nineteen flattened CSV files; M8 adds `routes.csv`, `segments.csv`, `segment_events.csv`, and `route_map_totals.csv` without changing the historical starting-map meaning of `map_totals.csv`.

## Development commands

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet restore .\UltimateDuckovStatistics.sln
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
dotnet build .\src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj -c Release --no-restore
```

Create the validated installable ZIP and SHA-256 sidecar with:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.8.1
```

See [INSTALL.md](INSTALL.md) for installation and compatibility details.

## License

MIT. Copyright 2026 Ultimate Duckov Statistics contributors.
