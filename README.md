# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) is a local, single-player statistics mod for Escape From Duckov. Version 0.7.0 retains the M1-M6 consumable, healing, run, movement, firing, combat, equipment, loadout, and totem statistics and adds unique successful non-corpse container access per run, map, and lifetime.

The mod never modifies Duckov save files. Its data is stored under:

```text
%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\
```

## Status

M0-M6 are merged through v0.6.0. M7 is the v0.7.0 pre-release candidate on `feat/containers`; automated and manual evidence is tracked in [TESTING.md](TESTING.md). No M7 release, tag, merge, ready-for-review transition, or Workshop upload is implied by this branch.

## Build prerequisites

- Windows
- .NET 8 SDK
- A local Escape From Duckov installation
- Steam Workshop item [HarmonyLib (2.4.1.0)](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), installed and enabled
- `DUCKOV_PATH` set to the game root, for example `E:\SteamLibrary\steamapps\common\Escape from Duckov`

Game assemblies are referenced locally with copy-local disabled. They are never committed, downloaded by CI, or included in release packages. UDS discovers the separately installed HarmonyLib at runtime. Healing, M5 combat attribution, and M7 corpse provenance each use distinct, minimal, version-checked patch owners; a missing/incompatible contract or unsafe foreign patch disables the affected metrics. Failed UDS unpatch cleanup retains the exact owner, is retried, and blocks unsafe same-process replacement. UDS never bundles `0Harmony.dll`.

> **Activation check:** Open **Mods** after a cold launch and confirm both HarmonyLib and UDS are active before selecting a save. See [INSTALL.md](INSTALL.md#required-harmonylib-workshop-item).

UDS fingerprints saves read-only and never modifies them. While active, it records a short-lived pre-save intent in its own external profile from Duckov's public save-collection event, so a normal save completed immediately before a crash or same-process re-selection of the current slot can retain the same UDS generation. If a save changes while UDS is inactive, that proof is unavailable and UDS conservatively archives the prior statistics generation rather than risk merging a reused slot.

Runs begin only when the native raid is initialized and the live main duck actually has player control. Active duration excludes pause and loading. Movement is sampled at approximately 5 Hz with the real monotonic sample interval and verified native walk/run/dash speeds; implausible, loading-boundary, explicit-position, and long-gap displacement is retained separately as teleport/excluded distance. Interrupted and integrity-flagged runs stay visible but do not enter default duration records; each Runs row shows its integrity tag and an explicit eligible/excluded Records status with the exclusion reason.

M4 uses the public `ItemAgent_Gun.OnMainCharacterShootEvent` from the verified Duckov build. Each accepted callback receives a unique UDS event ID and proves one firing action plus event-time weapon/ammunition identity. The event occurs after calls that may conditionally skip ammunition consumption or projectile initialization, so loaded-ammunition and projectile counts are explicitly unavailable rather than inferred from cached ammunition or configured `ShotCount`. Reloads, magazine transfers, inventory movement, base activity, loading, pause, non-main-duck actors, and dry fire do not create firing-action records.

M5 measures `Health.Hurt` before/after HP and therefore excludes rejected damage and overkill. A reliable ranged hit is one completed exact-main-duck projectile that caused positive actual enemy HP loss; penetration or repeated damage from that projectile cannot inflate the numerator. Accuracy uses those hits over completed player projectiles, not M4 firing actions. Critical hits never imply headshots.

M6 observes the exact main duck's public character-slot tree, ordinary inventory, and native slot/hold/inventory callbacks. Persisted identities use stable `Item.TypeID`, slot keys, and deterministic attachment signatures; runtime object IDs and localized names never determine identity. Durations use the same monotonic active-raid clock as M3 and therefore exclude pause/loading. Direct slotted totems with usable durability are proven active by the verified item-effect control flow. A totem plugged into the public `AnyThing` slot of a built-in Tote Bag (`Item.TypeID` 1255) carried in top-level ordinary character inventory is recorded as present with activation `Unknown`: tote activation is not inferred and the capability remains `DisabledIncompatible`. Equipment/combat rows are event-time temporal associations, not proof that an item or totem caused an outcome. Only loadouts observed in at least two completed runs enter lifetime recurring-loadout rankings; every run retains its own summary.

M7 observes public `InteractableLootbox.OnStartLoot`, after the interaction timer and inventory checks have succeeded. It requires the event-time interaction owner to be the exact main duck, reads the native private `GetKey()` contract reflectively for per-run deduplication, and excludes native enemy corpses plus persisted/player tombs using a separate version-checked corpse-provenance patch. Reopening a container in the same run does not increment; the same stable key may count again in another run. Proximity, attempts, locked/cancelled/failed interactions, corpses, base activity, item transfers, and loot value do not count.

The in-game panel enables Overview, Runs, Records, Combat, Equipment, Items, and Diagnostics. One export action writes `statistics.json` plus fifteen flattened CSV files under the current UDS generation. M7 adds `containers.csv` and container columns to run/map total CSVs.

## Development commands

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet restore .\UltimateDuckovStatistics.sln
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
dotnet build .\src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj -c Release --no-restore
```

Create the validated installable ZIP and SHA-256 sidecar with:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.7.0
```

See [INSTALL.md](INSTALL.md) for installation and compatibility details.

## License

MIT. Copyright 2026 Ultimate Duckov Statistics contributors.
