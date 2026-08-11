# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) is a local, single-player statistics mod for Escape From Duckov. Version 0.5.0 retains the released consumable, healing, run, movement, and firing-action statistics and adds actual main-duck damage dealt/received, compatible projectile accuracy, melee swings/hits, kills/deaths, combat ownership, stable enemy/killer identity, broader family/cause breakdowns, player-applied damage-over-time, event-time weapon/ammunition attribution, and independently proven head-targeted hits/final blows.

The mod never modifies Duckov save files. Its data is stored under:

```text
%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\
```

## Status

M0-M4 are merged and published through v0.4.0. M5 is implemented on `feat/combat-attribution` as a v0.5.0 pre-release candidate; automated validation is complete, while user-controlled gameplay, approved deployment readback, packaging from the accepted commit, and the draft PR delivery gates remain pending. See [PLAN.md](PLAN.md) for the product contract and [TESTING.md](TESTING.md) for exact evidence.

## Build prerequisites

- Windows
- .NET 8 SDK
- A local Escape From Duckov installation
- Steam Workshop item [HarmonyLib (2.4.1.0)](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), installed and enabled
- `DUCKOV_PATH` set to the game root, for example `E:\SteamLibrary\steamapps\common\Escape from Duckov`

Game assemblies are referenced locally with copy-local disabled. They are never committed, downloaded by CI, or included in release packages. UDS discovers the separately installed HarmonyLib at runtime. Healing and M5 combat attribution each use distinct, minimal, version-checked patch owners; a missing/incompatible contract or unsafe foreign patch disables the affected metrics. Failed UDS unpatch cleanup retains the exact owner, is retried, and blocks unsafe same-process replacement. UDS never bundles `0Harmony.dll`.

> **Activation check:** Open **Mods** after a cold launch and confirm both HarmonyLib and UDS are active before selecting a save. See [INSTALL.md](INSTALL.md#required-harmonylib-workshop-item).

UDS fingerprints saves read-only and never modifies them. While active, it records a short-lived pre-save intent in its own external profile from Duckov's public save-collection event, so a normal save completed immediately before a crash or same-process re-selection of the current slot can retain the same UDS generation. If a save changes while UDS is inactive, that proof is unavailable and UDS conservatively archives the prior statistics generation rather than risk merging a reused slot.

Runs begin only when the native raid is initialized and the live main duck actually has player control. Active duration excludes pause and loading. Movement is sampled at approximately 5 Hz with the real monotonic sample interval and verified native walk/run/dash speeds; implausible, loading-boundary, explicit-position, and long-gap displacement is retained separately as teleport/excluded distance. Interrupted and integrity-flagged runs stay visible but do not enter default duration records; each Runs row shows its integrity tag and an explicit eligible/excluded Records status with the exclusion reason.

M4 uses the public `ItemAgent_Gun.OnMainCharacterShootEvent` from the verified Duckov build. Each accepted callback receives a unique UDS event ID and proves one firing action plus event-time weapon/ammunition identity. The event occurs after calls that may conditionally skip ammunition consumption or projectile initialization, so loaded-ammunition and projectile counts are explicitly unavailable rather than inferred from cached ammunition or configured `ShotCount`. Reloads, magazine transfers, inventory movement, base activity, loading, pause, non-main-duck actors, and dry fire do not create firing-action records.

M5 measures `Health.Hurt` before/after HP and therefore excludes rejected damage and overkill. A reliable ranged hit is one completed exact-main-duck projectile that caused positive actual enemy HP loss; penetration or repeated damage from that projectile cannot inflate the numerator. Accuracy uses those hits over completed player projectiles, not M4 firing actions. Melee swings come from the accepted native attack action and melee hits are deduplicated per damage scope. Ownership is exact main duck, the built-in pet/master chain, environmental (`fromCharacter == null`), or unknown. Tick/update effect scopes independently prove damage-over-time. Generic effect damage is not mislabeled as DoT. Critical hits never imply headshots: M5 records only native head-targeted projectiles observed independently at projectile initialization, and tracks their fatal subset separately.

The in-game panel enables Overview, Runs, Records, Combat, Items, and Diagnostics. One export action writes `statistics.json` plus eleven flattened CSV files under the current UDS generation. `combat_attribution.csv` contains lifetime/map/run totals and enemy, killer, family, cause, weapon, ammunition, and ownership breakdowns with capability states.

## Development commands

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet restore .\UltimateDuckovStatistics.sln
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
dotnet build .\src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj -c Release --no-restore
```

Create the validated installable ZIP and SHA-256 sidecar with:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.5.0
```

See [INSTALL.md](INSTALL.md) for installation and compatibility details.

## License

MIT. Copyright 2026 Ultimate Duckov Statistics contributors.
