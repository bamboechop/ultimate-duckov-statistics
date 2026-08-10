# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) is a local, single-player statistics mod for Escape From Duckov. Version 0.3.0 records successful consumable uses, actual HP restored, run outcomes and active duration, map records, and main-duck physical versus teleport/excluded distance. Activation counts, amount consumed, healing, run time, and movement remain separate metrics.

The mod never modifies Duckov save files. Its data is stored under:

```text
%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\
```

## Status

M0-M2 are released. M3 run lifecycle and movement is implemented and its complete automated, deployment, and gameplay acceptance matrix has passed on `feat/run-lifecycle-movement`. The v0.3.0 release remains intentionally unpublished while delivery stops at a green unmerged draft PR. See [PLAN.md](PLAN.md) for the product contract and [TESTING.md](TESTING.md) for exact evidence.

## Build prerequisites

- Windows
- .NET 8 SDK
- A local Escape From Duckov installation
- Steam Workshop item [HarmonyLib (2.4.1.0)](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), installed and enabled
- `DUCKOV_PATH` set to the game root, for example `E:\SteamLibrary\steamapps\common\Escape from Duckov`

Game assemblies are referenced locally with copy-local disabled. They are never committed, downloaded by CI, or included in release packages. UDS discovers the separately installed HarmonyLib at runtime and disables only healing attribution if its contracts are unavailable, its exact UDS callbacks disappear, or any foreign Harmony patch touches a required attribution method. Failed UDS unpatch cleanup remains pending and is retried; same-process reactivation is blocked until the old callbacks are removed. UDS never bundles `0Harmony.dll`.

> **Activation check:** Open **Mods** after a cold launch and confirm both HarmonyLib and UDS are active before selecting a save. See [INSTALL.md](INSTALL.md#required-harmonylib-workshop-item).

UDS fingerprints saves read-only and never modifies them. While active, it records a short-lived pre-save intent in its own external profile from Duckov's public save-collection event, so a normal save completed immediately before a crash or same-process re-selection of the current slot can retain the same UDS generation. If a save changes while UDS is inactive, that proof is unavailable and UDS conservatively archives the prior statistics generation rather than risk merging a reused slot.

Runs begin only when the native raid is initialized and the live main duck actually has player control. Active duration excludes pause and loading. Movement is sampled at approximately 5 Hz with the real monotonic sample interval and verified native walk/run/dash speeds; implausible, loading-boundary, explicit-position, and long-gap displacement is retained separately as teleport/excluded distance. Interrupted and integrity-flagged runs stay visible but do not enter default duration records.

The in-game panel enables Overview, Runs, Records, Items, and Diagnostics. One export action writes `statistics.json`, the existing item/overview CSVs, plus `runs.csv`, `run_totals.csv`, `map_totals.csv`, and `records.csv` under the current UDS generation.

## Development commands

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet restore .\UltimateDuckovStatistics.sln
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
dotnet build .\src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj -c Release --no-restore
```

Create the validated installable ZIP and SHA-256 sidecar with:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.3.0
```

See [INSTALL.md](INSTALL.md) for installation and compatibility details.

## License

MIT. Copyright 2026 Ultimate Duckov Statistics contributors.
