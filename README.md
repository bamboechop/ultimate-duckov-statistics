# Ultimate Duckov Statistics

Ultimate Duckov Statistics (UDS) is a local, single-player statistics mod for Escape From Duckov. Version 0.1.0 records successful consumable uses in raids, keeping activation counts separate from stack, charge, or durability consumed.

The mod never modifies Duckov save files. Its data is stored under:

```text
%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\
```

## Status

M0 (bootstrap and loader proof) and M1 (consumable-usage MVP) are under active development on `feat/consumable-mvp`. See [PLAN.md](PLAN.md) for the product contract and [TESTING.md](TESTING.md) for the exact validation protocol.

## Build prerequisites

- Windows
- .NET 8 SDK
- A local Escape From Duckov installation
- `DUCKOV_PATH` set to the game root, for example `E:\SteamLibrary\steamapps\common\Escape from Duckov`

Game assemblies are referenced locally with copy-local disabled. They are never committed, downloaded by CI, or included in release packages. UDS does not bundle Harmony or `0Harmony.dll`.

> **v0.1.0 activation note:** Duckov `2.3.30` does not automatically reactivate persisted local mods on the verified setup with zero Workshop subscriptions. Open **Mods** on every cold launch and check UDS once if its indicator is blank. See [INSTALL.md](INSTALL.md#important-activate-uds-on-every-cold-launch).

UDS fingerprints saves read-only and never modifies them. While active, it records a short-lived pre-save intent in its own external profile from Duckov's public save-collection event, so a normal save completed immediately before a crash or same-process re-selection of the current slot can retain the same UDS generation. If a save changes while UDS is inactive, that proof is unavailable and UDS conservatively archives the prior statistics generation rather than risk merging a reused slot.

## Development commands

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet restore .\UltimateDuckovStatistics.sln
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
dotnet build .\src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj -c Release --no-restore
```

Create the validated installable ZIP and SHA-256 sidecar with:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.1.0
```

See [INSTALL.md](INSTALL.md) for installation and compatibility details.

## License

MIT. Copyright 2026 Ultimate Duckov Statistics contributors.
