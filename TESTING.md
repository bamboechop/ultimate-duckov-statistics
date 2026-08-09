# M0/M1 validation protocol

This file is the authoritative, reproducible validation protocol for the consumable-usage MVP. Record evidence and timestamps in the checkpoint tables. Never edit, delete, or restore a Duckov save file while running these checks. A timestamped copy of the user-selected progressed save and its existing backups is created before gameplay testing; the source files remain untouched.

## Fixed baseline

| Property | Required value | Evidence |
| --- | --- | --- |
| Duckov version | `2.3.30` | `<Duckov>\Info.ini` |
| Steam build | `24013657` | `steamapps\appmanifest_3167020.acf` |
| Unity version | `2022.3.62f2` | contract probe / Unity metadata |
| Native loader | `Duckov.Modding.ModBehaviour` | contract probe |
| Lifecycle | `OnAfterSetup`, `OnBeforeDeactivate` | contract probe |
| Item-use hook | `ItemStatsSystem.UsageUtilities.OnItemUsedStaticEvent : Action<Item>` | contract probe |

## Safety invariants

- `DUCKOV_PATH` points to the game root; source projects never copy game references locally.
- No Duckov assembly and no `0Harmony.dll` may exist in Git, `artifacts/package`, or the ZIP.
- Deployment writes only `<Duckov>\Duckov_Data\Mods\UltimateDuckovStatistics\` and requires approval.
- UDS writes only its own directory below `Application.persistentDataPath`; it never writes Duckov's `Saves` directory.
- Save backups are copies to a separate timestamped acceptance-backup directory. No backup is deleted or restored by this protocol.

## Checkpoint 1 — repository bootstrap

```powershell
dotnet --info
git status --short --branch
dotnet restore .\UltimateDuckovStatistics.sln
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
```

Pass criteria: .NET 8 SDK is selected; branch is `feat/consumable-mvp`; restore succeeds; core tests pass; `.idea`, build outputs, local game references, and UDS runtime data are untracked.

| Check | Status | Evidence |
| --- | --- | --- |
| SDK and branch | Passed 2026-08-09 | .NET SDK `8.0.423`; `feat/consumable-mvp` |
| Restore | Passed 2026-08-09 | All four solution projects restored from pinned `NuGet.Config` source |
| Core tests | Passed 2026-08-09 | 1 passed, 0 failed (bootstrap suite) |
| Repository content audit | Passed 2026-08-09 | `.idea/` ignored; build/runtime outputs ignored; no game/Harmony binaries tracked |

## Checkpoint 2 — loader proof

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet run --project .\tools\DuckovContractProbe\DuckovContractProbe.csproj -c Release --no-restore -- $env:DUCKOV_PATH
dotnet build .\src\UltimateDuckovStatistics\UltimateDuckovStatistics.csproj -c Release --no-restore
.\scripts\package.ps1 -DuckovPath $env:DUCKOV_PATH
.\scripts\verify-package.ps1 -PackagePath .\artifacts\package\UltimateDuckovStatistics
```

After explicit approval, deploy with:

```powershell
.\scripts\deploy.ps1 -DuckovPath $env:DUCKOV_PATH
```

User smoke test:

1. Launch Duckov and enable **Ultimate Duckov Statistics** in the mod manager.
2. Confirm the mod loads; disable it once; enable it again.
3. Exit Duckov normally.

Codex evidence inspection:

- `%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\Player.log` contains exactly one setup per activation, a clean deactivation for disable, and no exception from `UltimateDuckovStatistics`.
- Package/deployed folder contains `info.ini`, `UltimateDuckovStatistics.dll`, `UltimateDuckovStatistics.Core.dll`, `INSTALL.md`, and no forbidden DLL.

| Check | Status | Evidence |
| --- | --- | --- |
| Native contract probe | Passed 2026-08-09 | Game `2.3.30`, build `24013657`, Unity `2022.3.62f2`; required metadata contracts present |
| Local mod build | Passed 2026-08-09 | Release loader and core assemblies; 0 warnings, 0 errors |
| Package audit | Passed 2026-08-09 | Five required files; no Duckov, Unity, framework, or Harmony DLLs |
| Approved deployment | Passed 2026-08-09 | Package copied only to `Duckov_Data\Mods\UltimateDuckovStatistics`; all deployed SHA-256 hashes match source |
| Load/disable/re-enable smoke test | Passed with accepted workaround 2026-08-09 | C1 no-touch test proves the flag persists but automatic activation does not run with zero Workshop subscriptions; user explicitly accepted per-launch activation for v0.1.0 |

### Authoritative enabled-state persistence test

Marker: `LOADER-PERSISTENCE-20260809-C1`.

Precondition evidence (read-only): both `Saves\Global.json` and `Global.json.bac` contained `ModActive_UltimateDuckovStatistics = false` with SHA-256 `07c4e2743afdd71a1a2a5ed886ba894772ce395ab4c4f4b2940f0c3039005fa7` before deployment. UDS did not modify either file.

First launch — establish enabled state:

1. Launch Duckov.
2. Open the mod UI and verify the UDS package version is `0.1.0-loader-c1`.
3. Enable UDS exactly once. Do not disable it again.
4. Wait at the main menu for 10 seconds.
5. Exit Duckov normally.
6. Tell Codex that the first launch is complete. Codex reads `Player.log` and both global settings files and must observe the C1 activation marker plus a persisted `true` flag before the restart test begins.

Phase 1 evidence, 2026-08-09: passed. The UI showed the checked active indicator. `Player.log` recorded one C1 activation followed by `application-quitting active=True` and `destroyed active=True`, with no deactivation or exception. Both `Global.json` and `Global.json.bac` persisted `ModActive_UltimateDuckovStatistics = true`; both hashes were `c906a8648fb406f72f62794650c426ef65d5e55b2350ab264cae17f9f2999b13`.

Second launch — no-touch restart observation:

1. Launch Duckov again.
2. Open the mod UI only to observe the UDS checkbox. Do not click the checkbox, mod row, arrows, or any other mod control.
3. Report whether the checkbox visibly appears checked or unchecked.
4. Return to the main menu without changing any mod setting and wait 15 seconds.
5. Exit Duckov normally.
6. Tell Codex that the second launch is complete. Codex inspects `Player.log`, `Player-prev.log`, and both global settings files.

Phase 2 evidence, 2026-08-09: the untouched UI indicator was unchecked. `Player.log` contained no C1 marker, `Mod Loaded`, or setup failure. Both global files remained unchanged at `ModActive_UltimateDuckovStatistics = true` with SHA-256 `c906a8648fb406f72f62794650c426ef65d5e55b2350ab264cae17f9f2999b13`. Therefore the mod was inactive despite the persisted enabled preference; activation in phase 1 occurred only because the user clicked the control.

Root cause evidence: Duckov `2.3.30` calls `ModManager.ScanAndActivateMods()` at startup only from `SteamWorkshopManager.OnSteamUGCQueryCompleted` (success or failure). The local installation has neither `steamapps\workshop\appworkshop_3167020.acf` nor `steamapps\workshop\content\3167020`, so its Workshop query input is empty. In the observed cold launch the callback never ran, and Duckov never applied its persisted local-mod flag. `ModManagerUI` shows live activation state rather than the stored preference, so the unchecked indicator is accurate rather than stale.

Accepted v0.1.0 workaround: on every cold Duckov launch, open **Mods** before selecting a save. If UDS is unchecked, click its left activation control exactly once, confirm the check mark appears, return to the main menu, and continue. Do not install or require Harmony merely to work around this Duckov loader edge case. Manual acceptance begins only after Codex verifies the current launch's UDS activation marker. The user explicitly accepted this workaround for the GitHub pre-release on 2026-08-09.

Pass/diagnosis rules:

- C1 activation marker in the untouched second launch plus persisted `true`: active and correctly persisted; an unchecked UI is a stale/incorrect checkbox.
- No C1 activation marker and persisted `false`: enabled flag did not persist.
- C1 marker only after a UI action: activation was manual, not automatic.
- Persisted `true`, unchecked indicator, and no C1 marker (observed): Duckov retained the preference but did not execute automatic activation; use the documented per-launch manual activation workaround if explicitly accepted.
- If evidence remains ambiguous, repeat one completely cold third launch without opening or touching the mod UI; wait 15 seconds at the main menu, exit, and inspect again.

## Checkpoint 3 — item-use tracking

Automated tests must prove:

- one successful completion increments once;
- cancelled, interrupted, failed, non-player, and incomplete correlations do not increment;
- repeated setup and scene transitions do not duplicate subscriptions;
- activation count and amount consumed use separate fields;
- base use is diagnostic-only and raid use counts;
- canonical groups do not double-count multi-effect items;
- unknown/modded items retain their stable ID and fallback name.

Run:

```powershell
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore --filter "Category=ItemUse"
```

| Check | Status | Evidence |
| --- | --- | --- |
| Correlation and cancellation | Passed automated 2026-08-09 | Successful pre/success/player chain; cancelled, failed, incomplete, non-player, and duplicate callbacks produce no extra event |
| Raid/base scope | Passed automated 2026-08-09 | Raid completion countable; base completion normalized for diagnostics and rejected by reducer |
| Amount semantics | Passed automated 2026-08-09 | Activation, stack unit, durability delta, destroyed-stack fallback, and item unit tested separately |
| Classification and unknowns | Passed automated 2026-08-09 | Deterministic primary priority, all effect tags, group invariants, stable unknown ID/name |
| Subscription idempotency | Passed automated 2026-08-09 | Setup/deactivation gate and duplicate native callback sequence are idempotent |

## Checkpoint 4 — persistence

Automated tests must prove save-generation isolation, serialization round-trip, migration, corruption recovery, `.bak` fallback, atomic interruption safety, bounded diagnostics, and clean-session checkpoint recovery.

```powershell
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore --filter "Category=Persistence"
```

| Check | Status | Evidence |
| --- | --- | --- |
| Save-generation isolation | Passed automated 2026-08-09 | Separate slots remain isolated; changed save identity archives the old generation read-only and starts at zero |
| Round-trip and migration | Passed automated 2026-08-09 | Schema-0 fixture migrated to schema 1 without changing its generation ID |
| Corruption/backup recovery | Passed automated 2026-08-09 | Corrupt primary recovered from `.bak`; orphaned `.tmp` recovered; two corrupt snapshots archived without overwrite |
| Interrupted write/session safety | Passed automated 2026-08-09 | Atomic replacement and session checkpoint recovery preserve totals; one interruption recorded exactly once; diagnostics remain bounded and raw trace defaults off |

### Review hardening regression gate

These checks are required after the four pre-release review findings concerning schema safety, missed lifecycle events, item reclassification, and stale deployment contents.

```powershell
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore
.\scripts\build.ps1 -DuckovPath $env:DUCKOV_PATH
```

Automated pass criteria:

- A profile or nested statistics document newer than the supported schema is moved byte-for-byte into a read-only archive; UDS creates a separate current schema-1 generation and never saves/downgrades the unsupported object.
- Missing legacy identity/statistics fields are migrated locally before identity checks.
- A same-slot content change is accepted only when an active UDS pre-save intent matches the stored SHA-256, Duckov's persisted `SaveTime` advances, and that time falls within 30 seconds of the public `OnCollectSaveData` observation. The interrupted UDS session and its totals survive that proven save step.
- Same-timestamp deletion-shaped changes with no advanced `SaveTime`, changes after clean shutdown, and changes after an expired pre-save intent all rotate conservatively. A nonzero pre-fingerprint profile also rotates conservatively.
- A transient fingerprint read failure does not erase a previously stored continuity proof.
- A stable item ID keeps its first canonical group, and JSON/item/group CSV totals remain mutually consistent after a conflicting later classification.
- Deployment stages and verifies a clean package, replaces the old UDS directory, removes a simulated stale `0Harmony.dll` and obsolete DLL, verifies the final exact five-file inventory, and leaves no staging/backup directory on the success path.
- A simulated partial failure while deleting the already-replaced backup emits a warning, retains the remaining backup, and leaves the exact verified new deployment installed; rollback is allowed only before deployment commit.

| Check | Status | Evidence |
| --- | --- | --- |
| Profile schema safety | Passed automated 2026-08-09 | Future top-level and nested schemas archived without rewrite; direct save guard rejects downgrade; missing legacy fields normalize before identity checks |
| Save reuse continuity | Passed automated 2026-08-09 | Native pre-save intent plus advanced `SaveTime` preserves an interrupted normal save; unchanged time, clean-close removal, expired intent, uncertain legacy identity, and failed refresh remain conservative |
| Classification/export invariant | Passed automated 2026-08-09 | First canonical group is frozen for a stable item ID; item, group, overall, JSON, and CSV activation totals agree |
| Clean deployment replacement | Passed automated 2026-08-09 | Normal replacement ends with five permitted files; simulated partial backup cleanup failure retains the backup with a warning without replacing or damaging the committed new deployment |
| Full Release suite/build | Passed automated 2026-08-09 | 47 tests; Duckov contract including `OnCollectSaveData` passed; native build 0 warnings/0 errors; package exact-inventory validation passed |

Targeted manual continuity acceptance after approved deployment:

1. Confirm Duckov is closed. Codex records current UDS slot-1/slot-6 generation IDs, profile hashes, and save metadata without modifying any save.
2. Cold-launch Duckov, apply the accepted per-launch activation workaround, and select progressed slot 1. The old two-use pre-fingerprint UDS generation must be archived read-only and a new zero generation must appear. This one-time conservative rotation is expected; the accepted evidence remains in the archive.
3. In slot 1, complete exactly one successful raid consumable use, finish the raid, exit normally, and report the item/group/amount.
4. Codex verifies one count, the stored save SHA-256, clean checkpoint removal, and no UDS error. Cold-launch again, activate UDS before selecting slot 1, confirm the same generation and one count, then exit. Codex verifies exact persistence.
5. Cold-launch, activate UDS, and select disposable slot 6. Confirm a zero generation. Complete exactly one successful raid consumable use, finish, and exit normally. Codex verifies the one-use profile and stored fingerprint.
6. Cold-launch Duckov without activating or touching UDS. Select only disposable slot 6, delete/reuse it through Duckov, start the new slot, and exit normally. Do not touch slot 1 or slot 2.
7. Cold-launch once more, activate UDS before selecting slot 6, open the panel, and confirm a different zero generation; then exit normally.
8. Codex verifies that the inactive reuse produced a fingerprint mismatch, archived the former one-use generation read-only, created a zero generation, left slot 1 unchanged, removed session/temp residue, and emitted no UDS exception.

Targeted review-hardening acceptance evidence, 2026-08-09: passed. The approved staged deployment replaced the real UDS mod directory with exactly the five validated files, every deployed hash matched the package, and no staging/backup directory remained. On progressed slot 1, the pre-fingerprint two-use generation `1c8a71ac760c447a8599ab29440adeb0` was conservatively archived read-only and new generation `1523690077194c07b3d2c960f20843eb` recorded exactly one `Wasserflasche`/`Drink` activation and `50 Durability`. A cold active restart reopened that same generation with `created=False`, `rotated=False`, retained exact totals, refreshed the normal-exit save fingerprint, and left matching primary/backup profiles with zero interruption/session/temp residue.

Disposable slot 6 then recorded one `Kakaomilch` activation (`RemedyDebuffRemoval`, one `Item`) in generation `c38a530939ed4861b8c50c6486d1d46c`; Food, Drink, and Debuff Removal tags explain the deterministic Remedy primary group. During the next cold launch the user did not open Mods, activate UDS, or press F8, and deleted/reused only slot 6 through Duckov. `Player.log` contained no UDS activation/setup marker; the UDS profile stayed byte-identical at SHA-256 `eb7622610bbd2371514f2609ccbc946bb5256e4a454b5df2f5729b2997ae0355`, while the Duckov save changed from fingerprint `dad003146bd1e1eaa93e927dfac539560601ae9d1f6730215d6d29190c12c572`/42,297 bytes to `ce94fb7dd735f5cca9d7f9973a7c43ac7105b0d57751ae4a199ad84e40f57ddc`/40,657 bytes. On the following active launch UDS detected the mismatch, archived the exact one-use generation with all four files read-only, and created zero generation `9ee62a9a702d40ba85d3870721e9b072` bound to the new fingerprint. Slot 1 remained unchanged, all current primary/backup pairs match, no session/temp residue remains, and the final log contains no error or exception. The targeted persistence and deployment gate is accepted.

### Follow-up interrupted-save continuity gate

The later review finding about legitimate save evolution reopens persistence and final-release acceptance. Use only disposable slot 6. Do not touch progressed slot 1 or streaming slot 2. UDS and Codex never edit a Duckov save; the user performs all Duckov actions.

Precondition and deployment:

1. Duckov is closed. Codex records slot-6 save/profile hashes, generation, totals, session residue, `SaveTime`, and the absence of a pending-save observation.
2. Codex rebuilds and validates the exact five-file package. After explicit approval, deployment replaces only `Duckov_Data\Mods\UltimateDuckovStatistics` and Codex verifies its exact inventory and hashes.

Phase A — create a real interrupted normal-save step:

1. Cold-launch Duckov and apply the accepted per-launch activation workaround before selecting slot 6.
2. Select slot 6 and open UDS. Record the displayed generation and starting totals, then close the panel.
3. Enter a raid, complete exactly one successful consumable use, and extract normally.
4. Before launching, open a separate Command Prompt or Windows Terminal. After the base finishes loading, do not open UDS, export, change slots, or exit through Duckov. In that separate terminal run `taskkill /F /IM Duckov.exe`. The `/F` is required so Unity cannot run its normal quit/deactivation callbacks. Slot 6 is disposable for this intentional interruption.
5. Report the item, expected group, amount, starting total, ending action total if observed before extraction, and the exact `taskkill` result.
6. Codex verifies that the UDS profile and session checkpoint remain valid, the one use is present, the pending-save observation matches the prior SHA-256/`SaveTime`, Duckov's current `SaveTime` advanced within the 30-second intent window, and the current save fingerprint differs. No save is restored or edited.

Phase B — recover without false rotation:

1. Cold-launch Duckov, activate UDS before selecting slot 6, then select slot 6 and open UDS.
2. Confirm the exact same generation, the one-use total, and `Interrupted sessions recovered: 1`. Exit Duckov normally.
3. Codex verifies `created=False`, `rotated=False`, `interrupted=True`, exact item/group/amount retention, cleared pending-save state, clean session removal after exit, and no UDS error.

Phase C — prove inactive reuse remains conservative:

1. Cold-launch Duckov without opening Mods, activating UDS, or pressing F8.
2. Select only disposable slot 6, delete/reuse it through Duckov, start the new slot, and exit normally. Do not touch slots 1 or 2.
3. Codex verifies from `Player.log` that UDS was inactive and that its slot-6 profile remained byte-identical while the Duckov save changed.

Phase D — detect the reuse:

1. Cold-launch Duckov, activate UDS before selecting slot 6, select slot 6, and open UDS.
2. Confirm a different zero generation with zero uses and zero interrupted sessions, then exit normally.
3. Codex verifies a read-only archive of the exact recovered one-use generation, the new zero profile, no cross-slot change, clean checkpoints, and no UDS error.

| Follow-up check | Status | Evidence |
| --- | --- | --- |
| Native save-intent interruption | Passed manual 2026-08-09 | Forced `taskkill /F` retained generation `9ee62a9a702d40ba85d3870721e9b072`, two uses, a valid session checkpoint, and the exact pending pre-save identity; current Duckov SHA-256/`SaveTime` advanced inside the intent window |
| Same-generation recovery | Passed manual 2026-08-09 | Active restart reopened `9ee62a9a702d40ba85d3870721e9b072` with `created=False`, `rotated=False`, `interrupted=True`, exact two-use totals, and one recovered interruption; normal exit cleared pending/session state |
| Inactive reuse remains isolated | Passed manual 2026-08-09 | Inactive launch left slot-6 UDS profile byte-identical while Duckov save changed; next active launch archived the exact two-use/one-interruption generation read-only and created zero generation `badb76d6cbb14b44915c2ddaf26ba166`; slot 1 remained byte-identical |

First Phase-A attempt, 2026-08-09: not accepted as an interruption. Task Manager delivered a graceful close: `Player.log` records `application-quitting`, `destroyed`, native-hook unsubscription, and `Closed generation ... cleanly`; `session.json*` and the pending-save observation were therefore removed. The gameplay result itself is valid and retained in generation `9ee62a9a702d40ba85d3870721e9b072`: one `Kakaomilch` activation, `RemedyDebuffRemoval`, one `Item`, zero interruptions. Repeat Phase A from starting total 1 using the explicit forced `taskkill /F` command above; the expected resulting total is 2.

Forced Phase-A evidence, 2026-08-09: passed. `taskkill /F /IM Duckov.exe` terminated PID `26436` without any `application-quitting`, destruction, unsubscription, or clean-close marker. Generation `9ee62a9a702d40ba85d3870721e9b072` remains schema 1 with exactly two uses: `Kakaomilch`/`RemedyDebuffRemoval`/one `Item` and `Verband`/`Healing`/one `StackUnit`. Valid `session.json` remains for the same generation. The pending observation and stored identity agree on SHA-256 `be4f44e2a14de71940cf608a497b5bd7d8483ae19327406f7ba0f5172ac4af12` and `SaveTime` `2026-08-09T17:55:44.2834042Z`; it was collected at `17:55:44.293Z`. Duckov completed a different 46,685-byte save with SHA-256 `158e9619c0a657911d738ba42404cc43a82ac28460a9cac31fbe346539dcd317` and `SaveTime` `17:55:44.3041416Z`, only 11 ms after the intent. The profile, atomic backup, diagnostics, and session JSON are readable; no UDS exception appears. Repeated native `startIndex` messages are the previously classified Duckov initialization noise.

Phase-B recovery evidence, 2026-08-09: passed. The active restart recovered the interrupted session and opened the same generation `9ee62a9a702d40ba85d3870721e9b072` with `created=False`, `rotated=False`, `interrupted=True`. UI and persistence agree on two successful uses, `Kakaomilch`/`RemedyDebuffRemoval`/one `Item`, `Verband`/`Healing`/one `StackUnit`, and one recovered interruption. Normal exit recorded application quit/destruction, one native-hook unsubscription, and a clean generation close. `PendingSave` is absent, no `session.json*` or temporary residue remains, and matching 1,698-byte primary/backup profiles have SHA-256 `66d1c1144003e3da6d5ecb3b4c4dbebcc19683fe9658e8afe5cf8e9860611748`. No UDS exception appears.

Phase-C inactive-reuse evidence, 2026-08-09: passed. The cold launch contains the sorted local package name but no `Mod Loaded`, `[UDS]`, setup, activation, or deactivation marker. Slot-6 UDS generation `9ee62a9a702d40ba85d3870721e9b072` remained byte-identical at SHA-256 `66d1c1144003e3da6d5ecb3b4c4dbebcc19683fe9658e8afe5cf8e9860611748`, with two uses, one prior interruption, no pending save, and no session residue. Duckov's slot-6 save changed from SHA-256 `158e9619c0a657911d738ba42404cc43a82ac28460a9cac31fbe346539dcd317`/46,685 bytes/`17:55:44.3041416Z` to `28e655dae30abb794c1384662eae8947419cb565f0eb17063ddc172bff16ecc4`/44,028 bytes/`18:03:23.7751385Z`. Progressed slot 1 remained byte-identical at profile SHA-256 `809cfaf20de760d7f535897cc19eac1f5e0203b11547e46b37171217646be71c` and save SHA-256 `6d634d69f147cbb3d1e650329a5f04d3a6f77d9d8ad8ab166c893cfe617c9950`.

Phase-D active-detection evidence, 2026-08-09: passed. Startup detected the inactive replacement, archived generation `9ee62a9a702d40ba85d3870721e9b072` as `SaveIdentityChanged`, and opened new generation `badb76d6cbb14b44915c2ddaf26ba166` with `created=True`, `rotated=True`, and `interrupted=False`. The archive's matching 1,698-byte primary/backup profiles retain SHA-256 `66d1c1144003e3da6d5ecb3b4c4dbebcc19683fe9658e8afe5cf8e9860611748`, exactly two uses and one recovered interruption; both profile files and both diagnostics files are read-only. The current profile has zero activations, amounts, items, groups, interruptions, and pending save state, retains both Supported capabilities, and has matching primary/backup SHA-256 `0739922b9f49b419e44aaaa81a9f2aa49cd270818b004b28204b7996b50fd963`. Normal exit recorded application quit/destruction, one unsubscription, and a clean close. No session, temporary, or repair residue remains. Slot-1 profile SHA-256 remains `809cfaf20de760d7f535897cc19eac1f5e0203b11547e46b37171217646be71c`; no UDS exception appears.

## Checkpoint 5 — UI and exports

Automated tests must prove JSON and CSV item/group/overall totals agree and that package validation rejects every forbidden dependency.

```powershell
dotnet test .\tests\UltimateDuckovStatistics.Tests\UltimateDuckovStatistics.Tests.csproj -c Release --no-restore --filter "Category=Export|Category=Package"
.\scripts\build.ps1 -DuckovPath $env:DUCKOV_PATH
```

Manual UI checks are included in the acceptance scenarios below: Overview, Items, Diagnostics, F8 access from main menu/base, raid rejection message, reset confirmation, and export actions.

| Check | Status | Evidence |
| --- | --- | --- |
| JSON/CSV equivalence | Passed automated 2026-08-09 | One snapshot produces matching overall/group/item totals across JSON and three flattened CSV tables; CSV escaping and generation-scoped atomic file output verified |
| Overview/Items/Diagnostics | Passed smoke 2026-08-09 | Main-menu panel and all three tabs opened; both native adapters displayed `Supported`; no visible or logged error |
| F8 and outside-raid access | Passed smoke 2026-08-09 | F8 opened, closed, and reopened the panel at the main menu; raid rejection remains for gameplay acceptance |
| Package validation | Passed 2026-08-09 | Release package and deployed folder contain exactly the five required v0.1.0 files; DLL hashes match; no Duckov, Unity, framework, or Harmony dependency included |

Smoke artifact inspection, 2026-08-09: `Player.log` contained exactly one profile open, one item-hook subscription, one v0.1.0 activation, normal application quit/destruction, one unsubscription, and one clean generation close. There were no UDS exceptions or errors. Slot 2 generation `035c0b8c40854fe197dd955585877f87` persisted at schema 1 with zero activations/items/groups, zero interrupted sessions, both capabilities supported, raw diagnostics disabled, valid primary/backup JSON, no `session.json*`, and no `.tmp`/`.repair` residue.

The UI produced two complete four-file export sets 21 seconds apart; both matched the zero profile and generation. Inspection found the redundant CSV header `unknown_amount_amount`. This was corrected to `unknown_amount`, covered by an exact regression assertion, rebuilt with all 32 tests passing, repackaged, and redeployed. Deployed hashes match the corrected package (`UltimateDuckovStatistics.Core.dll` SHA-256 `43651ccf0f64a05b589d050a387053c02308027b333364bd47da9f00ba6069b4`; `UltimateDuckovStatistics.dll` SHA-256 `2ff1095431c3bed97a4eccd969ce7dbea87d0323b4e67c3e91abb5d20b7baaac`).

## Checkpoint 6 — manual acceptance

Do not begin until the user identifies the progressed slot and disposable fresh slot. Codex records the current UDS profile state and creates a timestamped copy of the progressed save file plus every existing `.bac*` backup before asking the user to launch Duckov.

For every numbered gameplay action, the user records the exact item display name, expected group, starting/ending stack or durability shown by Duckov, and whether the action completed or was cancelled.

### Progressed save scenario

1. User selects the agreed progressed slot.
2. Open UDS outside a raid. Confirm Overview, Items, and Diagnostics load and all totals start at zero (UDS does not reconstruct history).
3. Successfully use one consumable at base. Record the item and confirm totals remain unchanged; Diagnostics must report an ignored base use.
4. Enter a raid. Start one consumable use and cancel it before completion. Record the item; confirm no count.
5. Successfully use consumable A. Record item name/ID if visible, starting and ending stack/durability, and expected group.
6. Successfully use a different consumable B, preferably from another group. Record the same details.
7. If neither A nor B exposes multiple charges/durability, use one charged or stacked item and record action count versus amount consumed.
8. Press F8 during the raid. Confirm the panel does not open and a brief outside-raids message appears.
9. Extract or otherwise finish the session. Outside the raid, open UDS and record total, group, per-item activation, and amount values.
10. Exit and restart Duckov, reopen the same slot, and confirm those values persist exactly.
11. Export JSON and CSV from UDS. Exit Duckov so Codex can inspect `Player.log`, UDS diagnostics, the profile snapshot, session state, and every export.

Progressed gameplay phase evidence, slot 1, 2026-08-09: passed through step 9. The initial zero profile used generation `1c8a71ac760c447a8599ab29440adeb0`. One completed `Flaschenwasser` use at base left all totals at zero and produced exactly one `IgnoredOutsideRaid` diagnostic. One raid water use was cancelled without consumption and produced no count. The two completed raid actions were `Flaschenwasser` (`Drink`, one activation, `50` durability) and `Med-Kit (S)` (`Healing`, one activation, `11.142044067382813` durability). The resulting persisted totals are two activations and `61.142044067382813` durability, with exactly two item records, two canonical groups, and two recent event IDs. F8 did not open the panel during the raid; extraction and game exit completed normally.

Codex inspection after exit found schema-1 primary and backup profiles with identical SHA-256 `89b86784c97878b7a9e2579d849ac822f28ad3b891a11261ecb5e66899e7acc5`, revision 3, zero interrupted sessions, and the same generation shown before gameplay. Diagnostics contain one profile open, one ignored base use, two counted raid uses, and one clean generation close. No `session.json*`, `.tmp`, or repair residue exists. `Player.log` contains one UDS setup/subscription, the expected ignored/count sequence, normal application quit/destruction, one unsubscription, and a clean generation close, with no UDS exception. Duckov also emitted repeated bare `startIndex` range messages only inside its native level-initialization sequence; they contain no UDS stack frame, gameplay continued normally, and they are recorded as unrelated non-blocking game log noise.

Progressed restart/export phase evidence, slot 1, 2026-08-09: passed steps 10-11. A cold launch reopened generation `1c8a71ac760c447a8599ab29440adeb0` with `created=False`, `rotated=False`, `recovered=False`, `migrated=False`, and `interrupted=False`. The UI and revision-4 profile retained exactly two activations and `61.142044067382813` durability, split identically between `Flaschenwasser`/`Drink` (`1`, `50`) and `Med-Kit (S)`/`Healing` (`1`, `11.142044067382813`), with zero interrupted sessions. One export action created exactly `statistics.json`, `overview.csv`, `groups.csv`, and `items.csv` under the same generation. JSON and all CSV rows agree exactly; the three CSV files use the corrected `unknown_amount` header. The current log contains one setup, one subscription, one export, normal quit/destruction, one unsubscription, two expected clean profile closes around same-slot selection/shutdown, zero generic errors, and zero UDS errors or exceptions. Primary and backup profiles are valid and identical, and no session/checkpoint, `.tmp`, or repair residue remains. The progressed slot 1 matrix is accepted.

### Fresh disposable save scenario

Preflight evidence, 2026-08-09: disposable slot 6 still has its original 64-byte placeholder save with SHA-256 `b9d4ca7617b1efdb5d93294bb3c6669a94992d65495430bf9b38d236221ce49a`, and no UDS `slot-06` profile exists. Slot 2 is explicitly out of scope because it is the user's streaming save.

1. User selects the agreed fresh slot and starts a new game.
2. Open UDS and confirm a separate zeroed generation with no progressed-slot values.
3. Enter a raid and successfully use at least one consumable. Record item, group, and starting/ending stack or durability.
4. Finish the session, reopen UDS, and confirm exactly that use.
5. Exit Duckov. Codex inspects the profile and records its generation ID.
6. User deletes/reuses only this disposable slot through Duckov, starts it again, and opens UDS.
7. Confirm the new generation is zero. Exit Duckov.
8. Codex confirms the old UDS generation is in the read-only archive, the new generation ID differs, and no progressed-save data leaked.

Fresh gameplay phase evidence, slot 6, 2026-08-09: the selected placeholder opened as zero generation `17f2714455b5462c80d1c8e298ef647b`, with no items, groups, amounts, interrupted sessions, or slot-1 data. Duckov's native new-game event then cleanly archived that zero profile read-only and created generation `2a29a0daa1de4554a4d8cd91a068b1bd`. Exactly one completed raid use persisted: `Verband`, `Healing`, one activation, one `StackUnit`. The final profile and backup are identical, revision 1, with one recent event and zero interrupted sessions; shutdown removed all session/checkpoint and temporary residue. The current generation's stored save creation timestamp matches the now-expanded real save, so its one-use history remains the same generation on restart despite the expected 64-byte-to-full-save length change.

Inspection also found that the new-game generation had an empty persisted capability list: capabilities were applied only to the profile open during initialization and were not carried across later slot changes or rotations. This is acceptance-blocking because Diagnostics must remain accurate for every save generation. `ProfileRepository` now retains the configured runtime capability snapshot, applies it idempotently to loaded profiles, and includes it in every newly created or rotated generation. A regression test covers both slot transition and `DuckovNewGame` rotation. The corrected full build passes 33 tests, the Duckov `2.3.30` contract probe, a warning-free native build, and package validation. Fresh gameplay phase 1 must be repeated only far enough to confirm the existing one-use profile survives restart and both capabilities display as Supported before the disposable-slot reuse phase proceeds.

Corrected-build restart evidence, slot 6, 2026-08-09: passed. Generation `2a29a0daa1de4554a4d8cd91a068b1bd` reopened without creation, rotation, recovery, migration, or interruption and retained exactly `Verband`/`Healing`, one activation, and one `StackUnit`. The save identity refreshed from the placeholder observation to the real 42,641-byte save without rotating because its creation timestamp remained stable. Diagnostics displayed both `native-item-use` and `native-save-lifecycle` as Supported. The persisted primary and backup profiles are identical (SHA-256 `7a568774ed618e042870e5a97614f06774f6914aefef39b9a40eb206aa55adce`), revision 2, and contain both capability records. The log has one setup/subscription, no error, normal quit/destruction, one unsubscription, and clean closes; no session/checkpoint or temporary residue remains. The capability carryover fix is manually accepted. Only Duckov-driven deletion/reuse of slot 6 remains.

Delete/reuse evidence, slot 6, 2026-08-09: passed. With UDS active, the user deleted only slot 6 through Duckov and started it again. The native delete event cleanly closed and archived one-use generation `2a29a0daa1de4554a4d8cd91a068b1bd` under a `DuckovSaveDeleted` archive; its primary and backup profile files retain exactly `Verband`/`Healing`, one activation, one `StackUnit`, both Supported capabilities, and identical SHA-256 `7a568774ed618e042870e5a97614f06774f6914aefef39b9a40eb206aa55adce`. Every archived file is read-only. The new-game event correctly matched the already-rotated generation instead of rotating twice. Current generation `19d5d0f3ec564a3fae8eac6d50061d48` is schema 1, revision 1, has zero activations, amounts, items, groups, event IDs, and interruptions, retains both Supported capabilities, and has identical primary/backup SHA-256 `963662621994fb539a38f736f52b4724d71d0345cd92a3071c2e9443d0e6c89b`. No slot-1 data leaked. The log records normal setup, deletion/archive/new-game matching, quit/destruction, one unsubscription, and a clean close with no UDS exception. No session/checkpoint, `.tmp`, or repair residue remains. Repeated bare Duckov level-initialization messages are the same previously classified non-blocking native game noise. The full fresh/reused slot 6 matrix is accepted.

### Final inspection and release-candidate checks

Codex, not the user, inspects:

- `Player.log` for setup/deactivation, compatibility, item correlation, persistence, UI/export, and exceptions;
- UDS profile, archive, diagnostics, and export files for exact values from the action record;
- source/package inventories for forbidden binaries;
- `git diff`, commit, pushed branch, draft PR head, CI state, ZIP contents, and SHA-256 checksum.

Final release-candidate command:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.1.0
```

This command reruns the complete Release suite and contract probe, rebuilds and validates the five-file package, creates an installable folder-rooted ZIP, rejects forbidden ZIP entries, and writes a lowercase SHA-256 sidecar.

Superseded pre-review release-candidate evidence, 2026-08-09: the earlier 33-test candidate produced a 42,353-byte ZIP with SHA-256 `6e63b1c2a6d62d1e1e62a51a15dd26a928fdb98b8cda988e8b972bc7576b7363`. Four review findings reopened persistence, classification/export, deployment, and final-release gates. That ZIP and hash are no longer the release candidate. The review-hardened ZIP and sidecar must be regenerated and independently verified only after the targeted manual continuity gate passes.

Superseded review-hardened release-candidate evidence, 2026-08-09: the 43-test candidate produced a 44,085-byte ZIP with SHA-256 `b37a4af0d6e98c1a0197049685e1175bb705606fcc2cc996e27c677b85d330d5`. The interrupted-normal-save finding reopened persistence, deployment cleanup, manual continuity, source/PR currency, and final artifact gates. That ZIP and checksum are not the release candidate. Regenerate them only after the follow-up interrupted-save continuity gate passes.

Final follow-up release-candidate evidence, 2026-08-09: after all four interruption/reuse phases passed, `create-release.ps1` reran all 47 Release tests, the Duckov `2.3.30` / Steam `24013657` / Unity `2022.3.62f2` contract probe including `OnCollectSaveData`, a warning-free native build, and exact five-file package validation. `UltimateDuckovStatistics-v0.1.0.zip` is 45,803 bytes with SHA-256 `7d930422e6e1c7e4b13a3bdd6a1f682e1350edd5448738762807b299eeeec581`; its lowercase sidecar matches exactly. Independent extraction contains only folder-rooted `info.ini`, `INSTALL.md`, `LICENSE`, `UltimateDuckovStatistics.Core.dll`, and `UltimateDuckovStatistics.dll`; the extracted package passes validation and every file is byte-identical to the validated source package. No Duckov, Unity, framework, or Harmony assembly is present.

| Check | Status | Evidence |
| --- | --- | --- |
| Progressed save matrix | Passed 2026-08-09 | Slot 1 zero baseline, base exclusion, cancellation exclusion, two-group raid counts/amounts, F8 rejection, restart persistence, clean shutdown, and four-file export all verified |
| Fresh/reused save matrix | Passed 2026-08-09 | Slot 6 zero isolation, one stack-unit use, capability carryover correction/retest, restart persistence, Duckov-driven delete/reuse, read-only archive, new zero generation, and no cross-slot leak verified |
| Review-hardening continuity matrix | Passed 2026-08-09 | Active slot-1 fingerprint continuity, inactive slot-6 deletion/reuse, subsequent mismatch rotation, exact read-only one-use archive, new zero generation, clean sessions/logs, and slot-1 isolation verified |
| Follow-up interrupted-save continuity | Passed 2026-08-09 | Forced save-step interruption preserved generation/totals and recovered once; clean recovery cleared checkpoints; inactive reuse stayed byte-isolated and then archived the exact old generation read-only before a new zero profile |
| Log and artifact inspection | Passed 2026-08-09 | Original matrices plus forced interruption, recovery, inactive reuse, final archive/current profiles, diagnostics, capabilities, and checkpoint cleanup inspected; no UDS exception or residue |
| Source committed and pushed | Reopened 2026-08-09 | Follow-up save-lineage/deployment changes and final evidence are not yet committed/pushed |
| Draft PR current and unmerged | Reopened 2026-08-09 | PR #1 must be refreshed only after follow-up acceptance; it must remain draft and unmerged |
| Installable ZIP and SHA-256 | Passed 2026-08-09 | Independently extracted exact five-file folder-rooted ZIP; 45,803 bytes; SHA-256 `7d930422e6e1c7e4b13a3bdd6a1f682e1350edd5448738762807b299eeeec581`; exact matching sidecar ready |

The M0/M1 Goal remains active until every row above passes. Do not merge the PR and do not publish a GitHub release.
