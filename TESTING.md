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
- Reopening the already-open slot on the same `ProfileRepository` instance retires the previous session checkpoint without clearing the pre-save intent before identity comparison; the normal evolved save keeps its generation and totals, consumes the intent, starts a fresh session checkpoint, and records no interruption.
- Same-timestamp deletion-shaped changes with no advanced `SaveTime`, changes after clean shutdown, and changes after an expired pre-save intent all rotate conservatively. A nonzero pre-fingerprint profile also rotates conservatively.
- A transient fingerprint read failure does not erase a previously stored continuity proof.
- A stable item ID keeps its first canonical group, and JSON/item/group CSV totals remain mutually consistent after a conflicting later classification.
- Deployment stages and verifies a clean package, replaces the old UDS directory, removes a simulated stale `0Harmony.dll` and obsolete DLL, verifies the final exact five-file inventory, and leaves no staging/backup directory on the success path.
- A simulated partial failure while deleting the already-replaced backup emits a warning, retains the remaining backup, and leaves the exact verified new deployment installed; rollback is allowed only before deployment commit.

| Check | Status | Evidence |
| --- | --- | --- |
| Profile schema safety | Passed automated 2026-08-09 | Future top-level and nested schemas archived without rewrite; direct save guard rejects downgrade; missing legacy fields normalize before identity checks |
| Save reuse continuity | Passed automated 2026-08-09 | Native pre-save intent plus advanced `SaveTime` preserves interrupted and same-instance/same-slot normal saves; unchanged time, clean-close removal, expired intent, uncertain legacy identity, and failed refresh remain conservative |
| Classification/export invariant | Passed automated 2026-08-09 | First canonical group is frozen for a stable item ID; item, group, overall, JSON, and CSV activation totals agree |
| Clean deployment replacement | Passed automated 2026-08-09 | Normal replacement ends with five permitted files; simulated partial backup cleanup failure retains the backup with a warning without replacing or damaging the committed new deployment |
| Full Release suite/build | Passed automated 2026-08-09 | 48 tests; Duckov contract including `OnCollectSaveData` passed; native build 0 warnings/0 errors; package exact-inventory validation passed |

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

### Same-process same-slot re-selection gate

The later review finding about `Open` clearing the pending save proof before comparing an already-open slot reopens persistence and final-release acceptance. Use only disposable slot 6. Do not touch progressed slot 1 or streaming slot 2. Do not exit or restart Duckov between steps 3 and 7.

Precondition and deployment:

1. Duckov is closed. Codex records the current slot-6 generation, totals, profile/backup hashes, archive inventory, session/pending state, and Duckov save SHA-256/`SaveTime` without modifying any save.
2. Codex reruns the 48-test build and contract/package validation. After explicit approval, deployment replaces only `Duckov_Data\Mods\UltimateDuckovStatistics`; Codex verifies the exact five-file inventory and hashes.

Manual phase:

1. Cold-launch Duckov and apply the accepted per-launch activation workaround before selecting any save.
2. Select only slot 6. Outside a raid, open UDS and record the displayed generation, total uses, per-item totals, and interrupted-session count; then close the panel.
3. Enter a raid, complete exactly one successful consumable use, record its display name/group/amount, and extract normally so Duckov performs its normal save.
4. After the base loads, open UDS once and record the same generation plus the incremented total. Close the panel.
5. Without exiting Duckov, return to the main menu using Duckov's UI. Do not open Mods and do not toggle UDS.
6. Select slot 6 again in that same Duckov process. This must fire the same-slot `OnSetFile` path after the completed save.
7. Outside a raid, open UDS. Confirm the generation is identical to steps 2 and 4, every previous total remains, the one new use remains counted exactly once, and the interrupted-session count did not increase. Exit Duckov normally.

Codex postconditions:

- `Player.log` contains the same-process same-slot selection and no UDS exception.
- The selection reports `created=False` and `rotated=False`; diagnostics contain the same-slot session-transition close and no `SaveIdentityChanged` archive.
- The prior session checkpoint was retired before re-open, so no interruption was recovered; normal exit removes the replacement checkpoint.
- The profile retains the same generation and exact totals, adopts the evolved Duckov identity, has no pending-save observation after comparison, and has matching valid primary/backup JSON.
- Archive inventory, slot-1 profile, and slot-2 state remain unchanged; no `.tmp`, `.repair`, or `session.json*` residue remains.

| Same-slot check | Status | Evidence |
| --- | --- | --- |
| Same-instance regression | Passed automated 2026-08-09 | A single repository instance prepares a native save, reopens the same slot with an evolved hash/`SaveTime`, preserves generation and totals, consumes the proof, deletes the prior checkpoint, starts a replacement checkpoint, and records no rotation/interruption |
| Same-process Duckov re-selection | Passed manual 2026-08-09 | After the second Aspirin use/save, same-process slot-6 selection retained generation `badb76d6cbb14b44915c2ddaf26ba166`, two uses/two `StackUnit`, and zero interruptions; diagnostics recorded the dedicated transition close and `created=False`, `rotated=False` |

Same-process re-selection evidence, 2026-08-09: passed. The validated five-file deployment exactly matched the package and left no deployment residue. The accepted run opened slot-6 generation `badb76d6cbb14b44915c2ddaf26ba166` at one prior Aspirin activation, counted one additional raid Aspirin (`Healing`, one `StackUnit`) for a total of two, and then returned to the main menu without ending the Duckov process. At `18:43:17Z`, diagnostics recorded `Closed generation ... for same-slot re-selection` immediately followed by `Save slot selected ... created=False rotated=False`; no interruption was recovered. Normal exit at `18:43:55Z` recorded application quit/destruction, native-hook unsubscription, and a clean close. The revision-18 profile retains exactly two Aspirin activations/two `StackUnit`, zero interruptions, and no pending save. Its identity exactly matches Duckov's 47,626-byte save at SHA-256 `8f1ce8d3da9fde9cbc8d13e90e4f09537e2c49cafd218b28766b83a6ed824ca8` and `SaveTime` binary `5250904996310185719`; matching primary/backup profiles have SHA-256 `d2b27369b8e42726888443c7bedabd9e4dda38c15f56dcf030541634be0787f7`. Archive count remains six, slot-1 profile SHA-256 remains `809cfaf20de760d7f535897cc19eac1f5e0203b11547e46b37171217646be71c`, and no session, pending, `.tmp`, or `.repair` residue exists. No UDS exception appears; repeated bare `startIndex` messages remain the previously classified native Duckov initialization noise.

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

Superseded final follow-up release-candidate evidence, 2026-08-09: after all four interruption/reuse phases passed, `create-release.ps1` reran all 47 Release tests, the Duckov `2.3.30` / Steam `24013657` / Unity `2022.3.62f2` contract probe including `OnCollectSaveData`, a warning-free native build, and exact five-file package validation. `UltimateDuckovStatistics-v0.1.0.zip` was 45,803 bytes with SHA-256 `7d930422e6e1c7e4b13a3bdd6a1f682e1350edd5448738762807b299eeeec581`. The same-instance/same-slot finding reopened persistence, deployment, manual continuity, source/PR currency, and artifact gates; that ZIP and checksum are no longer the release candidate.

Final same-slot release-candidate evidence, 2026-08-09: after the same-instance regression and real Duckov same-process gate passed, `create-release.ps1` reran all 48 Release tests, the Duckov `2.3.30` / Steam `24013657` / Unity `2022.3.62f2` contract probe including `OnCollectSaveData`, a warning-free native build, and exact five-file package validation. `UltimateDuckovStatistics-v0.1.0.zip` is 45,978 bytes with SHA-256 `283373237ad5e40ae0919a6a608141c4e60528b9b99c4076c4feec678aa62534`; its lowercase sidecar matches exactly. Independent extraction contains only folder-rooted `info.ini`, `INSTALL.md`, `LICENSE`, `UltimateDuckovStatistics.Core.dll`, and `UltimateDuckovStatistics.dll`; the extracted package passes validation and every file is byte-identical to the validated source package. No Duckov, Unity, framework, or Harmony assembly is present.

| Check | Status | Evidence |
| --- | --- | --- |
| Progressed save matrix | Passed 2026-08-09 | Slot 1 zero baseline, base exclusion, cancellation exclusion, two-group raid counts/amounts, F8 rejection, restart persistence, clean shutdown, and four-file export all verified |
| Fresh/reused save matrix | Passed 2026-08-09 | Slot 6 zero isolation, one stack-unit use, capability carryover correction/retest, restart persistence, Duckov-driven delete/reuse, read-only archive, new zero generation, and no cross-slot leak verified |
| Review-hardening continuity matrix | Passed 2026-08-09 | Active slot-1 fingerprint continuity, inactive slot-6 deletion/reuse, subsequent mismatch rotation, exact read-only one-use archive, new zero generation, clean sessions/logs, and slot-1 isolation verified |
| Follow-up interrupted-save continuity | Passed 2026-08-09 | Forced save-step interruption preserved generation/totals and recovered once; clean recovery cleared checkpoints; inactive reuse stayed byte-isolated and then archived the exact old generation read-only before a new zero profile |
| Same-process same-slot continuity | Passed 2026-08-09 | Automated same-instance regression and real Duckov same-process selection both preserved generation/totals, consumed the pending proof, retired the prior checkpoint, and recorded no rotation/interruption |
| Log and artifact inspection | Passed 2026-08-09 | Same-process selection log, diagnostics, current/archive profiles, matching evolved identity, pending/session cleanup, unchanged archive inventory, and slot-1 isolation inspected; no UDS exception or residue |
| Source committed and pushed | Passed 2026-08-09 | Same-slot implementation and acceptance evidence are commit `1cdaa8a13a057f908820ee410a6a48ed3cbbdfcd`, authored and committed by `bamboechop <info@bamboechop.at>`, and pushed to `origin/feat/consumable-mvp`; this final gate record is included in the subsequent evidence commit |
| Draft PR current and unmerged | Passed 2026-08-09 | PR #1 remains open, draft, and unmerged; its final head, 48-test result, same-process acceptance, release checksum, and green CI were verified after the evidence push |
| Installable ZIP and SHA-256 | Passed 2026-08-09 | Independently extracted exact five-file folder-rooted ZIP; 45,978 bytes; SHA-256 `283373237ad5e40ae0919a6a608141c4e60528b9b99c4076c4feec678aa62534`; exact matching sidecar ready |

The M0/M1 Goal remains active until every row above passes. Do not merge the PR and do not publish a GitHub release.

## M2 healing-attribution acceptance — v0.2.0

M2 adds observer-only Harmony patches for exact item healing. Harmony is installed separately through Workshop item `3589088839`; `0Harmony.dll` must remain absent from Git, the five-file UDS package, deployment, and release ZIP.

### Automated and compatibility gate

With Duckov closed:

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
.\scripts\build.ps1 -DuckovPath $env:DUCKOV_PATH
```

The gate must pass the full Release tests, exact Duckov and Harmony contracts, a warning-free native build, and exact five-file package validation. Tests cover immediate and delayed healing, partial and full overheal, damage interleaving, unrelated regeneration, non-player targets, overlapping and refreshed buffs, duplicate applications, cancellation/base exclusion, expiry/restart cleanup, schema-1 primary/backup/temporary migration, capability degradation, export consistency, and rejection of bundled Harmony.

### Progressed-save protection

Before launching Duckov, record the selected progressed save and its existing `.bac*` files, then create a timestamped read-only backup copy without editing or restoring any Duckov save. Record the current UDS generation, schema, revision, totals, primary/backup hashes, and any session or temporary residue.

### Manual gameplay matrix

Use the user-selected progressed save. Record exact item display names, starting/ending HP, and expected actual restoration for each action.

1. Cold-launch Duckov. Confirm HarmonyLib and UDS are active before selecting the save.
2. Outside a raid, open UDS Diagnostics and confirm `native-healing-attribution: Supported` with HarmonyLib `2.4.1.0`.
3. Confirm the v0.1 profile migrated to schema 2 with the same generation, activations, amounts, groups, and items; all pre-v0.2 healing totals must be zero.
4. In a raid, damage the main duck and complete one immediate-healing consumable. Verify the increase equals `min(max HP - starting HP, nominal heal)` and is attributed once to that item.
5. Perform a partial-overheal use and verify only the missing HP is added. Perform a full-health use and verify zero HP is added while its successful activation still counts.
6. Complete one delayed-healing consumable or healing-over-time buff. Allow multiple ticks and verify their exact total is attributed to the source item.
7. During a delayed effect, take damage between healing ticks. Verify each tick's clamp-boundary application counts independently; do not use a before/after encounter snapshot.
8. Observe unrelated regeneration without a consumable source and verify it adds no item healing.
9. Cancel an eligible use and perform an eligible base use; verify neither adds healing or raid activation totals.
10. Exit normally, restart, reactivate/verify both mods, reopen the same save, and confirm generation and all values persist exactly once.
11. Export JSON and CSV, then exit Duckov. Verify overall, group, and item healing totals agree exactly across profile, UI, JSON, and CSV.

After exit, inspect `Player.log`, UDS diagnostics, profile and backup JSON, exports, and temporary/session residue. There must be one setup and cleanup sequence, no UDS exception, no duplicate healing event, no forbidden DLL, and no Duckov save modification by UDS.

### Final release gate

After manual acceptance and source review:

```powershell
.\scripts\create-release.ps1 -DuckovPath $env:DUCKOV_PATH -Version 0.2.0
```

Independently extract the ZIP, validate its exact five-file inventory and hashes, verify the lowercase sidecar, commit and push the feature branch, open or update a draft unmerged PR, and wait for green CI. Do not publish a GitHub release or merge the PR as part of this Goal.

| M2 check | Status | Evidence |
| --- | --- | --- |
| Automated Release suite | Passed after P2 follow-up 2026-08-10 | 105 tests; full M0/M1 regression plus immediate/delayed healing, full/partial overheal, interleaving, unowned same-ID refresh invalidation, exact four-category Harmony patch-set validation, required-callback removal, injected transient/repeated `UnpatchAll` failure recovery, same-process reactivation blocking, production reflective patcher behavior, restart, migration/recovery, delayed-healing group repair, capability, export, and forbidden-package cases |
| Duckov/Harmony contract | Passed after P1 follow-up 2026-08-09 | Duckov 2.3.30, Steam 24013657, Unity 2022.3.62f2, exact healing methods/properties, and Harmony 2.4.1.0 hash plus Prefixes/Postfixes/Transpilers/Finalizers/PatchMethod reflection API verified |
| Five-file package audit | Passed from committed P2 head 2026-08-10 | Exact inventory and no forbidden DLL; both packaged DLLs embed commit `c98b874762a14d3ec4c228df305e7a70719f4689`. Independent ZIP extraction is byte-identical to the package. The earlier gameplay-tested deployment remains unchanged because manual gameplay/deployment was not repeated |
| Progressed-save backup and schema migration | Passed 2026-08-09 | Slot 1 and all 11 `.bac*` files were copied into read-only backup `artifacts/manual-backups/slot-01-20260809T202108Z`; schema 1 migrated in place to schema 2 generation `1523690077194c07b3d2c960f20843eb`, preserving one prior activation/50 Durability and initializing historical healing to zero |
| Immediate/overheal/base/cancel matrix | Passed manual 2026-08-09 | `Med-Kit (S)` restored exactly 12 HP from 48.6 to 60.6 and 0.612381 effective HP from HUD-rounded 64.4 to 65. A full-health raid injector counted one activation/0 HP; the same successful injector at base logged `IgnoredOutsideRaid`; a cancelled Med-Kit correlation expired without counting |
| Delayed/interleaved/unrelated healing matrix | Passed manual 2026-08-09 | Clean `Heilungsinjektor` use produced thirty exact 2-HP ticks. In the interleaved run, HP began at 7.4, damage occurred about 20 seconds into the buff, and final HP was 49.8, yet all thirty effective ticks still attributed exactly 60 HP. `Totem: Heilung III` restored 6 unrelated HP from 50 to 56 with no UDS attribution |
| Restart and export consistency | Passed manual 2026-08-09 | Cold launch reopened the same generation with `created=False`, `rotated=False`, `interrupted=False`, repaired the injector to Healing in place, and preserved the pre-follow-up 4 uses/72.612380981445313 HP. Final profile, UI, JSON, overview CSV, group CSV, and item CSV agree exactly on 6 uses/132.61238098144531 HP |
| Source, draft PR, CI, ZIP, and checksum | Passed after P2 follow-up 2026-08-10 | Fix commit `c98b874` and committed-head ZIP/checksum are verified; draft PR #2 remained open, draft, and unmerged at pushed head `e6f1946`; both duplicate `core` and `source-safety` CI runs passed |

Repaired-baseline restart/export evidence, 2026-08-09: HarmonyLoadMod loaded first and UDS activated with all three capabilities Supported. Slot-1 generation `1523690077194c07b3d2c960f20843eb` reopened without creation, rotation, recovery, or interruption and recorded `migrated=True` for the in-place canonical-group repair. The profile retained exactly four successful raid uses and `72.612380981445313` actual HP: `Wasserflasche` remains Drink (one use, 50 Durability, 0 HP), `Med-Kit (S)` remains Healing (two uses, 26.275794982910156 Durability, `12.612380981445313` HP), and `Heilungsinjektor` is now Healing with Drink/Buff/Healing tags (one StackUnit, 60 HP). Drink contains only the water use; Healing contains all three healing uses and the complete HP total. Export revision 68 reproduces those values exactly in `statistics.json`, `overview.csv`, `groups.csv`, and `items.csv`; overall, group-sum, and item-sum activation/HP invariants all match. No temporary/repair residue or UDS error is present while the expected active `session.json` remains.

Final gameplay evidence, 2026-08-09: the successful full-health raid injector incremented the overall activation count from four to five without any HP event. A deliberately cancelled Med-Kit left one incomplete correlation that expired without counting. `Totem: Heilung III` then restored 6 HP from 50 to 56 without any item attribution. The final injector began at 7.4 HP, received intervening damage about 20 seconds into its duration, and ended at 49.8 HP; UDS independently recorded all thirty 2-HP applications for exactly 60 HP rather than the misleading 42.4-HP net change. The successfully completed base injector separately logged `IgnoredOutsideRaid` and changed neither metric. After extraction, generation `1523690077194c07b3d2c960f20843eb` contains exactly six raid uses and `132.61238098144531` HP: `Wasserflasche`/Drink one use/0 HP, `Med-Kit (S)`/Healing two uses/`12.612380981445313` HP, and `Heilungsinjektor`/Healing three uses/120 HP. Final export revision 117 exactly matches those profile/UI values across JSON and all three CSV tables.

Final shutdown evidence, 2026-08-09: normal application quit and destruction produced exactly one native-hook unsubscription and a clean close of generation `1523690077194c07b3d2c960f20843eb`. Revision-121 primary and backup profiles are byte-identical at SHA-256 `9674d1c148b8c29e3da0835c87dab19c78759e61bf072e901ea227db79330653`, retain all exact final totals and all three Supported capabilities, and contain neither `PendingSave` nor an interruption. No `session.json`, `.tmp`, or `.repair` residue exists anywhere under the UDS data root. The final log contains no UDS failure, error, exception, or disabled capability.

Superseded pre-follow-up artifact evidence, 2026-08-09: `create-release.ps1` reran all 88 Release tests, the Duckov/Harmony contract probe, the warning-free native build, and five-file package validation. `UltimateDuckovStatistics-v0.2.0.zip` was 62,435 bytes with SHA-256 `700ae5372060b6d191a579b633d80cd96e4bf678257b951ac9765c9fd4102e28`; the lowercase sidecar matched an independent recomputation. The two P1 findings about unowned same-ID buff refreshes and incomplete Harmony conflict monitoring reopened source, artifact, PR, and CI currency. That ZIP is no longer the release candidate.

Pre-follow-up delivery evidence, 2026-08-09: draft PR [#2](https://github.com/bamboechop/ultimate-duckov-statistics/pull/2) was open, draft, unmerged, and green at head `2b50ea7e96cfc003ef7610ac5c15501512c7c38b` before the two P1 findings reopened acceptance.

P1 provenance/conflict follow-up evidence, 2026-08-10: an unowned `CharacterBuffManager.AddBuff` refresh now reconciles the reused main-duck buff instance with a null source and removes its prior consumable mapping, while a correlated refresh still transfers ownership. `Health.AddHealth` attribution now commits only the positive HP delta observed inside that synchronous call, so a prefix-suppressed or modified call cannot record the predicted amount. The reflective patcher validates Prefixes, Postfixes, Transpilers, and Finalizers; rejects every foreign owner; requires each exact UDS callback once; and checks at activation, every two seconds, and at the Health/Effect/Buff callback boundaries. Focused tests exercise all four foreign patch categories, missing/replaced/duplicate UDS callbacks, suppressed/modified health application, unowned refresh invalidation, and the production `ReflectiveHarmonyPatcher` compiled against a faithful fake Harmony reflection contract. The full game-independent suite passes 103/103; the expanded real Harmony 2.4.1 metadata probe and native build pass with 0 warnings/errors. Clean committed-head release generation produced a 64,918-byte ZIP with SHA-256 `a68b503cb8d3a67e1232726de00970f00ecdd0beb3e6e6fec7e7033b45a43c03`; its lowercase sidecar matches. Independent extraction has exactly folder-rooted `info.ini`, `INSTALL.md`, `LICENSE`, `UltimateDuckovStatistics.Core.dll`, and `UltimateDuckovStatistics.dll`, passes package validation, and is byte-identical to the source package. Package SHA-256 values are `a9fa0fd8b8662ecf46a9eaec39fe6524d4ea15abfc86ffa568c2a1db4c4b1e2c` (`info.ini`), `497aabfd632be0f3b8d98fdbf8f659e8ef38f3fb54b983c4d7e438b9b1d49976` (`INSTALL.md`), `0f7558f2469ad0901074f6c380ada1ed91861d55adf905267bc70b26cd2e3ccc` (`LICENSE`), `03aca05805b1ccf2eb188ec9de9e7f8bfd2b9df17895901f722bc3bb7aa89fa7` (`UltimateDuckovStatistics.Core.dll`), and `cd6105b459c7e9bd1317fc24a6ae5508ced09540b17e8f375fcc0c881e0698cb` (`UltimateDuckovStatistics.dll`). Both DLLs embed informational version `0.2.0+f775fa23910653c06d651f616d3c91346f2fbb1e`. Per the review request, prior manual gameplay/UI acceptance was not repeated. Draft PR #2 remained open, draft, and unmerged at pushed acceptance head `c03e27e`; both duplicate `core` and `source-safety` CI runs passed.

P2 retryable-cleanup follow-up evidence, 2026-08-10: the reflective patcher marks itself disposed only after `Harmony.UnpatchAll` succeeds and registers a failed cleanup for process-level retry. The native adapter owns the patcher through a retryable lease, retains that exact lease after activation rollback or conflict cleanup failure, retries once per second while alive and on repeated disposal, and leaves final-disposal failures registered so a later same-process activation must clean them before constructing another patcher. Attribution remains detached and disabled throughout. The faithful fake Harmony contract injects both one-shot and repeated `UnpatchAll` failures: tests prove the lease survives the first failure, callbacks are removed by retry, a replacement patcher is refused while cleanup still fails, and reactivation succeeds only after the registered retry removes the leftovers. Focused tests pass 3/3; the full suite passes 105/105; the real Duckov/Harmony contract probe passes; and the native Release build succeeds with 0 warnings/errors. Clean committed-head release generation produced a 65,840-byte ZIP with SHA-256 `6790280f3286570dcb52e9ec3c8826bdeb0188f7a696b3af045e1ea8a0785425`; its lowercase sidecar matches. Independent extraction has exactly folder-rooted `info.ini`, `INSTALL.md`, `LICENSE`, `UltimateDuckovStatistics.Core.dll`, and `UltimateDuckovStatistics.dll`, passes package validation, and is byte-identical to the source package. Package SHA-256 values are `a9fa0fd8b8662ecf46a9eaec39fe6524d4ea15abfc86ffa568c2a1db4c4b1e2c` (`info.ini`), `8d70107e04d5f8522a9d41f20e8893aa51c4ecea05d6cbd6853a8e778ed72d94` (`INSTALL.md`), `0f7558f2469ad0901074f6c380ada1ed91861d55adf905267bc70b26cd2e3ccc` (`LICENSE`), `3c5eb03e57a25f992b5aa34d61f0f5605a916d2f451aafc3cd406a3cf0d24cc9` (`UltimateDuckovStatistics.Core.dll`), and `5f9ec200cf26522be1895f713371ee5ad66401829432d6cae29ba991cb492ea7` (`UltimateDuckovStatistics.dll`). Both DLLs embed informational version `0.2.0+c98b874762a14d3ec4c228df305e7a70719f4689`. Manual gameplay/UI/deployment was not repeated. Draft PR #2 remained open, draft, and unmerged at pushed acceptance head `e6f1946`; both duplicate `core` and `source-safety` CI runs passed.

## M3 run-lifecycle and movement acceptance — v0.3.0

M3 adds run summaries, duration records, and main-duck movement. This section preserves the M0-M2 evidence above and records the new evidence independently. No Duckov save file may be written, edited, restored, or deleted by UDS or by Codex during this protocol.

### Native discovery gate — recorded before broad implementation

Read-only discovery was repeated on 2026-08-10 against the installed game at `E:\SteamLibrary\steamapps\common\Escape from Duckov`. The existing contract probe and ILSpy `9.1.0.7988` inspected managed metadata/decompiled control flow without loading Duckov gameplay or modifying its assemblies.

| Installed component | Verified value | SHA-256 |
| --- | --- | --- |
| Escape From Duckov | `2.3.30`, Steam build `24013657` | `Duckov.exe`: `7706a7047b45ffe7e23f041dc5ff229faccba62fee99b8b66cf20d5b90aa5beb` |
| Unity | `2022.3.62f2` | `globalgamemanagers`: `73e4d8727c17856cc4ae823431a1682b6141da489994bcfec2df2b282f8a3221` |
| Native gameplay assembly | `TeamSoda.Duckov.Core.dll`, 1,806,336 bytes | `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f` |
| Item system assembly | `ItemStatsSystem.dll`, 98,304 bytes | `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60` |
| External Harmony dependency | `HarmonyLib 2.4.1.0`, Workshop item `3589088839` | `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6` |

The M3 evidence chain uses public native events, properties, and instance events only. It requires no additional Harmony patch.

| Evidence | Exact native contract and visibility | Ordering/ownership evidence | M3 use |
| --- | --- | --- | --- |
| Raid initialization | `public static event Action<RaidUtilities.RaidInfo> RaidUtilities.OnNewRaid`; `RaidInfo` exposes `valid`, `ID`, `dead`, `ended`, `raidBeginTime`, `raidEndTime`, and `totalTime` | `LevelManager.HandleRaidInitialization()` calls `RaidUtilities.NewRaid()` only on a raid map whose prior raid is ended. `NewRaid()` persists the new `RaidInfo` before invoking `OnNewRaid`. | Establish raid-initialized evidence and retain the native raid ID diagnostically. It is not by itself a run start. |
| Player control | `public static bool InputManager.InputActived`; `public static CharacterMainControl CharacterMainControl.Main`; `public bool CharacterMainControl.IsMainCharacter`; `public Health CharacterMainControl.Health` | `InputActived` is false without an input manager, while paused, in camera mode, before `LevelManager.LevelInited`, without a live main character, while the main character is dead, or while an input-block cooldown remains. `CharacterMainControl.Main` is exactly `LevelManager.Instance.MainCharacter`. | A run starts only on the first observed false-to-true/control-ready state after raid initialization, on a raid map, for the exact native main duck. Placeholder/spawn/loading/base objects cannot start it. |
| Level readiness | `public static event Action LevelManager.OnLevelInitialized`; `public static event Action LevelManager.OnAfterLevelInitialized`; `public static bool LevelManager.LevelInited` | `InitLevel` creates and assigns the main duck, waits for registered initialization and subscene loading, sets `levelInited = true`, initializes the raid, invokes `OnLevelInitialized`, restores health/spawns exits, waits 0.25 seconds, sets the final position, then invokes `OnAfterLevelInitialized`. | Wake the start gate and replace position references after initialization; final authority remains `InputManager.InputActived`, not scene-name or spawn proxies. |
| Pause/resume | `public static event Action PauseMenu.onPauseMenuOn`; `public static event Action PauseMenu.onPauseMenuOff`; `public static bool GameManager.Paused` | `PauseMenu.Show()` opens the panel before invoking `onPauseMenuOn`; `Hide()` closes it before invoking `onPauseMenuOff`. `GameManager.Paused` reads the actual pause-panel shown state. | Suspend/resume active time and movement idempotently, with the state property used to reconcile duplicate/reordered callbacks. |
| Full-scene loading | `public static event Action<SceneLoadingContext> SceneLoader.onStartedLoadingScene`; `onFinishedLoadingScene`; `onAfterSceneInitialize`; `public static bool SceneLoader.IsSceneLoading` | `SceneLoader.LoadScene` sets `IsSceneLoading = true` immediately before `onStartedLoadingScene`, replaces the scene, sets it false before `onFinishedLoadingScene`, waits for `LevelManager.LevelInited`, then invokes `onAfterSceneInitialize`. | Suspend active time/movement at loading start. A post-load baseline is accepted only after initialization/control readiness. |
| Raid subscene loading | `public bool Duckov.Scenes.MultiSceneCore.IsLoading`; public static `OnSubSceneWillBeUnloaded` and `OnSubSceneLoaded` events | `LoadSubScene` sets `isLoading = true` before the black screen and unload/load work, then sets it false immediately before `OnSubSceneLoaded`. `LoadAndTeleport` subsequently calls `LevelManager.Instance.MainCharacter.SetPosition(...)`. | Poll the direct instance state and consume the public boundary events; never scan the scene. Keep the pre-load position and classify the valid displacement to the post-teleport position as teleport distance, then reset the physical baseline. |
| Extraction completion | `public static event Action<EvacuationInfo> LevelManager.OnEvacuated` | `NotifyEvacuated` makes the main duck invincible, invokes `OnEvacuated`, then performs save collection. The extraction `SceneLoaderProxy` can call `NotifyEvacuated` before closure and again through `SceneLoader.LoadScene(... notifyEvacuation: true)`, so duplicates are proven possible. | Normalize to `Extracted`; the Core state machine finalizes exactly once despite duplicate extraction/loading/raid-end callbacks. |
| Main-duck death | `public static event Action<DamageInfo> LevelManager.OnMainCharacterDead`; `public static event Action<RaidUtilities.RaidInfo> RaidUtilities.OnRaidDead` and `OnRaidEnd` | The listener is attached specifically to `mainCharacter.Health.OnDeadEvent`. `CharacterDieTask` calls `RaidUtilities.NotifyDead()` before `OnMainCharacterDead` is invoked; `NotifyDead` marks `dead=true`, `ended=true`, invokes `OnRaidEnd`, then `OnRaidDead`. Reordered/near-simultaneous callbacks are therefore native behavior. | Normalize all proven death evidence to `Died`; terminal handling is idempotent and never adds M5 killer/combat attribution. |
| Non-terminal/abandoned raid end | `RaidUtilities.OnRaidEnd` with the public `RaidInfo.dead`/`ended` fields | On a base level, `HandleRaidInitialization` calls `NotifyEnd()` if a prior raid is not ended. The same event therefore cannot prove extraction without `OnEvacuated`. | A still-active run reaching a non-death `OnRaidEnd` without prior extraction becomes `Interrupted`, never `Extracted`. |
| Runtime integrity | `public static event Action<bool> Duckov.CheatMode.OnCheatModeStatusChanged`; `public static bool CheatMode.Active`; `public static event Action Duckov.Rules.GameRulesManager.OnRuleChanged`; `public static RuleIndex GameRulesManager.SelectedRuleIndex` | `CheatMode.Active` invokes its status event whenever `Activate()` or `Deactivate()` changes the stored state. The rule setter persists the new selection before invoking `OnRuleChanged`. Both can therefore change after player control starts. | Accumulate integrity for the entire run, checkpoint a changed tag immediately, and re-read at every throttled checkpoint and terminal boundary. Once uncertain or disqualified, a run can never become record-eligible again. |
| Main-duck position | inherited public `Component.transform`; `public event Action<CharacterMainControl, Vector3> CharacterMainControl.OnSetPositionEvent`; `public void SetPosition(Vector3)` | `SetPosition` calls `Movement.ForceSetPosition`, zeroes native movement velocity, then invokes the instance event with the new position. `MultiSceneCore.LoadAndTeleport` uses this exact method after subscene load. | Sample only the retained `LevelManager.Instance.MainCharacter` reference and subscribe only to that instance's explicit-position event. Companions, pets, cameras, and replacement objects are rejected. |
| Known movement speed | `public float CharacterMainControl.CharacterWalkSpeed`; `CharacterRunSpeed`; `DashSpeed`; `public Vector3 Velocity`; `public Movement movementControl` and public `Movement.walkSpeed`/`runSpeed`/`Velocity` | `Movement.UpdateNormalMove` targets native walk/run speed; dash speed is exposed separately. `CharacterMainControl.Update` advances this exact movement only after `LevelManager.LevelInited`. | Each sample supplies the maximum finite positive verified native walk/run/dash speed. Core multiplies it by measured monotonic elapsed time and a conservative tolerance; there is no universal fixed-distance threshold. |
| Stable map identity | `public static string Duckov.Scenes.MultiSceneCore.MainSceneID`; `public static string SceneInfoCollection.GetSceneID(int)`; `public static SceneInfoEntry GetSceneInfo(string)`; public `SceneInfoEntry.ID` and `DisplayName` | Multi-scene raids retain one main-scene ID while active subscenes change. For a single-scene raid, the `LevelManager` object's scene build index resolves through `SceneInfoCollection`. | Store the stable root scene ID and localized display name at run start. If unavailable, retain a stable explicit fallback derived from the observed root scene/build identity, with display name `Unknown map`; never discard the run. |

#### Selected lifecycle and movement semantics

- The adapter listens to the public events above and performs direct property checks from `Update`; it never calls `FindObject*`, scans the scene, or searches globally for a character.
- A raid-init callback, raid scene, or spawned player object alone is insufficient. Core receives `ControlReady` only when the native current object is the exact main duck, alive, level-initialized, not loading, and `InputManager.InputActived` is true.
- Active time uses a monotonic clock supplied by the adapter. UTC timestamps are retained for start/end and diagnostic wall-clock duration only. Pause and loading are independent suspension reasons, so reordered resume callbacks cannot resume while another reason remains active.
- Sampling cadence is 0.2 seconds (approximately 5 Hz), but every threshold uses the actual monotonic elapsed time. A valid delta at or below the native speed envelope plus tolerance is physical distance; a larger valid delta is teleport distance. Invalid coordinates never change either total.
- Stationary displacement at or below the documented jitter epsilon is ignored. A duplicate/non-positive elapsed sample is ignored. A long sampling gap is excluded from physical distance and its valid displacement is classified as teleport distance.
- On pause, the pre-pause baseline is retained; the first valid resumed displacement is excluded from physical distance and classified as teleport distance if nonzero, then becomes the new baseline.
- On full-scene or subscene loading, the pre-load baseline is retained. Explicit `SetPosition` or the first valid post-load/control-ready sample classifies the cross-boundary displacement once as teleport distance and resets the physical baseline. Samples while loading never add physical distance.
- The stable root map ID is rechecked only at the bounded sample boundary. If it changes during an active run, the first valid cross-map displacement is classified once as teleport distance and establishes a new baseline; it is never physical movement.
- A main-character object replacement detaches the old instance event, releases the old reference, and establishes a new baseline on the verified replacement. No cross-object displacement is physical distance.
- Checkpoint persistence is throttled to a five-second monotonic cadence plus every lifecycle boundary, terminal transition, explicit teleport, profile transition, and deactivation. The profile is not written on every 5 Hz sample.
- Integrity is cumulative across the full run. Public cheat/rule change events trigger an immediate checkpoint, and checkpoint/terminal reads provide a fail-safe for other runtime changes. `Normal` can become `Unknown`, cheat/custom, or modded, but a later normal observation cannot clear uncertainty or disqualifying flags.

#### Capability, integrity, and cleanup policy

Duckov versions other than the fully verified `2.3.30` disable the M3 adapter rather than extrapolating compatibility. At the verified version, movement sampling failures disable only `native-main-duck-movement`; map lookup failures disable only `native-map-identity` and retain the run under the explicit unknown fallback; lifecycle subscription failure disables the lifecycle path and its dependent sampler. Every state and detail is persisted and visible in Diagnostics, and unsupported movement is rendered as `Unsupported` rather than zero.

`HarmonyLoadMod` is an infrastructure-only dependency required by M2 and does not itself add the `ModdedContent` integrity tag. Cheats/custom difficulty and any other active mod remain explicitly tagged. Interrupted or non-`Normal` runs retain their summaries and distance but cannot enter the default extraction/death duration records.

The sixteen native M3 event handlers are activated as one idempotent subscription set. A failure midway through setup rolls back every earlier handler in reverse order. Repeated setup is a no-op and repeated cleanup is safe. A failed unsubscribe remains owned by a process-lifetime `ProcessLifetimeCleanupOwner` after its `ModBehaviour` is destroyed, is diagnosed, blocks replacement/reactivation, and is retried until cleanup succeeds. Per-component ownership tokens prevent a duplicate component from cleaning up or sharing another live adapter. Disposal makes every retained native callback inert immediately; cleanup detaches the retained main-duck instance event both before and after static removal attempts and reports success only when all static and instance subscriptions are gone. Deactivation stops sampling, finalizes any active run as Interrupted, and releases its reference. Healing retains its existing external Harmony dependency and retryable cleanup; M3 adds no Harmony patches and bundles no `0Harmony.dll`.

Reproducible discovery commands:

```powershell
$env:DUCKOV_PATH = 'E:\SteamLibrary\steamapps\common\Escape from Duckov'
dotnet run --project .\tools\DuckovContractProbe\DuckovContractProbe.csproj -c Release --no-restore -- $env:DUCKOV_PATH
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t RaidUtilities "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t LevelManager "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t InputManager "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t SceneLoader "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t Duckov.Scenes.MultiSceneCore "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t CharacterMainControl "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t Movement "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t Duckov.CheatMode "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
ilspycmd --disable-updatecheck -r "$env:DUCKOV_PATH\Duckov_Data\Managed" -t Duckov.Rules.GameRulesManager "$env:DUCKOV_PATH\Duckov_Data\Managed\TeamSoda.Duckov.Core.dll"
```

### Automated acceptance status

`scripts/build.ps1 -DuckovPath 'E:\SteamLibrary\steamapps\common\Escape from Duckov'` is the complete local gate. On 2026-08-10 it passed all 140 Release tests, the expanded Duckov/Harmony contract probe, a native Release build with 0 warnings and 0 errors, and the exact five-file package validator.

The deterministic suite covers the full M0-M2 regression set plus: control-gated starts; base/loading/placeholder rejection; reordered and duplicate terminal callbacks; extraction/death/interruption; independent pause/loading active-time exclusion; five-second checkpoint throttling; primary/backup/temporary/unrecoverable active-run recovery; one-time interruption recovery including recovery into an old generation before identity rotation/archive; save-slot/generation isolation; exact-main-subject selection; 0.2-second monotonic sampling cadence; physical/jitter/elapsed-threshold/teleport/loading/resume/map-boundary/invalid/duplicate/long-gap/object-replacement movement; unknown maps; aggregate and per-map totals; deterministic records/ties; cumulative mid-run integrity changes and record exclusion; schema-2 field/capability/archive preservation; UI-model and JSON/CSV/profile agreement; capability/integrity degradation; idempotent subscription setup, retained-owner cleanup retry, and reactivation blocking; exact package inventory; forbidden dependencies; and deployment rollback behavior.

Follow-up review hardening on 2026-08-10 confirmed and repaired three findings: runtime integrity was previously captured only at run start; identity rotation previously archived an unfinished checkpoint before recovery; and `ModBehaviour` previously discarded the adapter that owned failed native unsubscriptions. The focused 16-test gate and complete 140-test Release gate pass. The expanded native probe verifies the exact public cheat/rule events and properties, and the native build remains warning-free. These are deterministic failure-path corrections; the already-passed gameplay matrix below remains the authoritative evidence for unchanged extraction, death, movement, pause, teleport, map, UI/export, and clean normal-shutdown behavior.

The package remains exactly these five installation files: `info.ini`, `INSTALL.md`, `LICENSE`, `UltimateDuckovStatistics.Core.dll`, and `UltimateDuckovStatistics.dll`. A runtime export is generation-scoped and contains exactly eight data files: `statistics.json`, `overview.csv`, `groups.csv`, `items.csv`, `runs.csv`, `run_totals.csv`, `map_totals.csv`, and `records.csv`.

### M3 manual acceptance matrix — passed 2026-08-10

Codex prepares/deploys the validated package, records hashes and pre/post evidence, and inspects logs, profiles, checkpoints, exports, and residue. The user alone launches and controls Duckov. Before any progressed-save test, Codex asks which slot is approved, creates a timestamped read-only backup of that save and all existing backups after approval, and records the Duckov save hashes/metadata plus the complete current UDS generation state. UDS and Codex never edit or restore a Duckov save.

The bounded gameplay gates are:

1. Open the approved existing v0.2 profile and prove schema-3 migration did not change item/healing statistics, capabilities, generation, or archives.
2. Remain at base long enough to prove no run starts.
3. Complete a normal extracted run with movement, including a measured paused interval and a genuine loading/teleport transition.
4. Inspect the resulting log/profile/UI/export evidence for one Extracted run, active-versus-wall-time exclusion, plausible physical distance, separate teleport distance, stable map identity, per-map aggregates, and no item/healing regression.
5. On a separately approved disposable/test save, complete one death-ended run.
6. On that disposable/test save, force-close one active run, restart, and prove exactly one Interrupted summary, no extraction/death record contamination, and no duplicate recovery on a second restart.
7. Restart normally and prove persistence plus clean shutdown with no duplicate setup, lifecycle handler, sampler, active checkpoint, session checkpoint, or temporary residue.
8. Export and prove profile, backup, UI, `statistics.json`, and all seven CSV tables agree exactly while Duckov save hashes remain unchanged by UDS outside user gameplay saves.

Deployment may replace only `Duckov_Data\Mods\UltimateDuckovStatistics` and requires explicit approval. It must not alter Harmony, another mod, a Duckov assembly, or any Duckov save.

#### Approved preflight and deployment evidence — 2026-08-10

The user approved slot 1 for migration/base/extraction/pause/movement/map/export checks, slot 6 for disposable death/interruption checks, the corresponding backups, and replacement of only the UDS mod folder. Duckov was closed throughout preparation.

- Slot 1 preflight: schema 2, generation `1523690077194c07b3d2c960f20843eb`, revision 121, 6 item uses, `76.27579498291016` Durability, 3 StackUnit, `132.6123809814453` actual HP, zero runs, zero interrupted sessions, one archive, no session/active-run/temporary file. Primary and backup profile SHA-256 both `9674d1c148b8c29e3da0835c87dab19c78759e61bf072e901ea227db79330653`.
- Slot 6 preflight: schema 2, generation `badb76d6cbb14b44915c2ddaf26ba166`, revision 21, 2 item uses, 2 StackUnit, 0 HP, zero runs, zero interrupted sessions, six archives, no session/active-run/temporary file. Primary and backup profile SHA-256 both `58d897c7c6adf0323ed338096453ed1f9933dd6a180975712960d76a01c7ea5d`.
- Duckov save preflight: slot-1 primary SHA-256 `e98b51fa339a78279d19baedbe08a5fb9e4562f65bc4f971ac2a7087da4ef9c5`; slot-6 primary `8f1ce8d3da9fde9cbc8d13e90e4f09537e2c49cafd218b28766b83a6ed824ca8`; both `Global.json` copies `ac7075619fbbf3dc7129a8eed016417117d676ed28c168ff2435173e127e6795`.
- Backup root: `C:\Users\micro\AppData\LocalLow\TeamSoda\Duckov\UDS Manual Backups\M3-20260810T1829364895366Z`. It contains exactly 12 slot-1 files and 11 slot-6 files. Every copied SHA-256 matches its source and all 23 copies report the Windows read-only attribute. The first attribute command was rejected and readback correctly showed writable copies; the correct .NET attribute call was then applied and reverified before deployment.
- Deployment: `scripts/deploy.ps1` staged and validated the five-file package, replaced only `E:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Mods\UltimateDuckovStatistics`, validated it again, and left no staging/previous-directory residue. The five deployed SHA-256 values match the candidate exactly: `20653a8a3411c1e7409bae2a541b703af53db5bc3c6e60017cb3a617e71a6353` (`info.ini`), `4d7c634a354fb19578ced3b1e33eedee5e360a40ef30c16c69192be3ed3d0b40` (`INSTALL.md`), `0f7558f2469ad0901074f6c380ada1ed91861d55adf905267bc70b26cd2e3ccc` (`LICENSE`), `c307ccef94f167b6c6a368ed5904264dc9c0e516161a9af02c1dffc30d32d287` (Core DLL), and `9acfbe137d8c6481e9eebb6a72d8aaeec7ec7feb3a62a9bdb3a72fb58c9622be` (native DLL).
- Post-deployment, Duckov remained closed; the slot-1, slot-6, and both Global hashes above were unchanged. External Harmony remained byte-identical at SHA-256 `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6`.

#### Manual gate 1 — schema migration and no base run

Passed 2026-08-10. The user confirmed the expected Overview values and all six capability rows as Supported after remaining at base without entering a raid. Independent persisted/log evidence confirms package/core `0.3.0`, one clean setup and cleanup sequence, 0.2-second sampler configuration, schema 2-to-3 migration in place, the same slot-1 generation, and no UDS exception.

Slot 1 retained exactly 6 uses, `76.27579498291016` Durability, 3 StackUnit, and `132.6123809814453` HP with the same three item rows and Drink/Healing group totals. It has zero runs and zero distance/records, zero interrupted sessions, the same one archive, and no session, active-run, or temporary residue. Primary and backup are byte-identical at revision 130 and SHA-256 `89661f1e435148fcaa0dff1ffacdcd20880b277fa9983445698e2ab6b9b1f741`. All six persisted capabilities are Supported: item use, save lifecycle, healing attribution, run lifecycle, main-duck movement, and map identity. The normal Duckov shutdown rewrote its own slot-1 save to SHA-256 `66144af55f541bac4ea4f1bb323b4db0defc339a95bedacc63207732204dd91e`; the matching primary/`.bac` pair and unchanged Global content are normal game-owned save activity, not a UDS write.

#### Manual gate 2 — extraction, pause, movement, teleport, map, records, and export

Passed 2026-08-10 on slot 1. The user entered Ground Zero, moved normally, used a genuine teleport/loading transition, held a measured 60-second pause, extracted, and reported that the displayed statistics looked accurate. The Overview, Runs, and Records screenshots show exactly one Extracted run, map display `Nullpunkt`, active time `00:01:35.857`, diagnostic wall time `00:02:36.053`, `256.34 m` physical distance, and `423.03 m` teleport/excluded distance. The user could not independently measure the exact path lengths, so this gate establishes correct separation and plausible accumulation rather than centimetre-level real-world calibration.

Independent persisted evidence identifies stable run `d0c08a09067b4e26957c6cf4c188272b`, native raid 151, and known map `duckov:map:Level_GroundZero_Main` (`Nullpunkt`). It started at `2026-08-10T18:38:32.035Z` and ended at `2026-08-10T18:41:08.088Z` with outcome Extracted, `95.8565314` active seconds, `156.0530297` wall-clock seconds, `256.34317197766495` physical metres, and `423.0342062758248` teleport/excluded metres. The `60.1964983`-second wall-minus-active difference agrees with the timed pause. The average physical speed during active time is approximately `2.674 m/s`, consistent with ordinary player movement. Integrity is Normal, the run is record-eligible, and all M3 capabilities are Supported.

The aggregate contains one run total and one known-map total with the same outcome and distances. The run deterministically supplies all four applicable records: overall shortest and longest extraction plus per-map shortest and longest extraction. No death record exists. Item/healing regression checks remain exact at 6 uses, `76.27579498291016` Durability, 3 StackUnit, and `132.6123809814453` HP. The primary and backup profile are byte-identical at revision 147 and SHA-256 `39ccb76c3c99bc2c385b5cb3ec25d57ae51e538659a4a70d7fe6a6a59f30e555`; no session, active-run, or temporary residue remains.

The latest generation-scoped export is `20260810T1841599424866Z-1523690077194c07b3d2c960f20843eb` and contains exactly the documented eight files. `statistics.json` and all seven CSV tables agree exactly with the persisted run, totals, map identity, distances, four record rows, 6 item/group uses, and HP amount. Its revision 144 is expected because the subsequent clean-exit identity writes advanced the profile to revision 147 without changing the statistical aggregates. The log contains one run-start entry, one Extracted finalization, one clean shutdown, and no UDS exception. Duckov's own recurring `Index out of range` log entries predate this package and do not originate from UDS.

#### Manual gate 3A — live stationary movement and repeated pause boundary

Passed 2026-08-10 on the approved, backed-up existing slot 6. Run `d39e1fd3d6314d65b117b881b73df866` started only after control became active on known map `duckov:map:Level_GroundZero_Main` (`Nullpunkt`). After unavoidable spawn settling, the paused baseline checkpoint contained `15.255079600000002` active seconds, `0.36604952812194824` physical metres, and 0 teleport metres. The user resumed and remained stationary for a timed interval, then paused again. The resulting checkpoint contained `51.068365999999983` active seconds and exactly the same two distance values: active time advanced by `35.81328639999998` seconds while physical and teleport distance advanced by exactly zero.

While each pause remained held, successive checkpoint observations advanced diagnostic observation time but left active duration unchanged. This independently proves both stationary-distance suppression and repeatable pause exclusion in the live native adapter. The checkpoint retained the same run ID, save generation `badb76d6cbb14b44915c2ddaf26ba166`, native raid 3, map identity, Normal integrity, and Supported lifecycle/movement/map capabilities throughout.

#### Manual gate 3B — death finalization, normal movement, and death records

Passed 2026-08-10 on slot 6. After the stationary phase, the user moved normally and allowed the main duck to die. The same active checkpoint reached `110.02448164935633` physical metres with zero teleport metres before the native death transition. The log then contains exactly one finalization for run `d39e1fd3d6314d65b117b881b73df866`: outcome Died, `108.126` active seconds, `110.024` physical metres, and 0 teleport metres. No duplicate terminal callback produced another summary.

The persisted summary starts at `2026-08-10T18:49:02.897Z`, ends at `2026-08-10T18:52:26.438Z`, records `108.12557349999999` active seconds and `203.5411703` diagnostic wall-clock seconds, retains known map `duckov:map:Level_GroundZero_Main` (`Nullpunkt`), Normal integrity, record eligibility, game `2.3.30` / build `24013657`, and Supported lifecycle/movement/map capabilities. The `95.4155968`-second wall-minus-active difference includes the two deliberately held pause intervals and confirms that they did not enter the death record duration.

Slot 6 now has exactly one run and one Died outcome, with exact overall and per-map physical/teleport totals. The run supplies overall shortest and longest death plus per-map shortest and longest death; extraction records remain empty. Existing M1/M2 statistics remain exactly 2 Aspirin uses, 2 StackUnit, and 0 actual HP. The active-run checkpoint was removed at the lifecycle boundary, no temporary file exists, and only the expected live `session.json` remains while Duckov is open. Current profile revision 26 and backup revision 25 both contain the same finalized run and statistical aggregates; their identity revisions differ because the game performed its normal post-death save after finalization.

The user's slot-6 Overview, Runs, and Records screenshots agree with that profile exactly. Overview shows generation `badb76d6cbb14b44915c2ddaf26ba166`, 2 successful item uses, 0 HP, 2 StackUnit, zero recovered interrupted sessions, one run split as Extracted 0 / Died 1 / Interrupted 0, `110.02 m` physical distance, 0 teleport/excluded distance, and the unchanged Healing group. Runs shows one Died row for Nullpunkt with `00:01:48.126` active, `00:03:23.541` wall-clock diagnostic, `110.02 m` physical, and 0 teleport/excluded distance. Records shows no extraction records and the same run ID as shortest and longest death overall and per map, with the per-map total matching Overview. Opening and navigating the panel produced no UDS exception or state residue.

#### Manual gate 4A — graceful active-run shutdown and first restart

Passed as additional lifecycle evidence on 2026-08-10, but explicitly not accepted as the required abrupt-process recovery gate. A second slot-6 run, `1ad3bf912ae642288d00568e08c9e8fa`, reached a paused checkpoint with `27.011198400000012` active seconds, `99.7860749074656` physical metres, and 0 teleport metres. Windows Task Manager's End task action delivered Unity `OnApplicationQuit` and `OnDestroy` callbacks instead of terminating without notification. UDS therefore finalized the run during orderly deactivation as Interrupted, unsubscribed the native lifecycle/movement and item-use hooks, stopped the sampler, released the main-duck reference, closed the generation cleanly, and removed both session and active-run checkpoints.

The resulting Interrupted summary retained the exact checkpoint duration and distance, ended at `2026-08-10T18:56:52.445Z`, recorded `77.41225969999999` diagnostic wall-clock seconds, and is explicitly not record-eligible. Totals became two runs: Died 1 and Interrupted 1, `209.81055655682195` physical metres, and 0 teleport metres. All death-record references remained exclusively on Died run `d39e1fd3d6314d65b117b881b73df866`; extraction records remained empty. Primary and backup were byte-identical at revision 32 and SHA-256 `111c75214331a7353518b2e89b3a669b7e5de88e5410fa5d8edbeee3678004fd`, with no checkpoint, session, or temporary residue after exit.

On the first restart, slot 6 opened with `interruptedRun=False` and retained exactly the same two runs, outcome totals, records, and M1/M2 totals. After remaining at base, revision 37 still had Died 1 / Interrupted 1, 2 item uses, and 0 HP; no active-run checkpoint existed and exactly one new live `session.json` existed. This proves persistence and no duplicate recovery for an already finalized graceful interruption. A separate hard termination is still required below to prove recovery from an unfinished checkpoint.

#### Manual gate 4B — abrupt termination, exactly-once startup recovery, and second restart

Passed 2026-08-10 on slot 6. A third run, `11549531ad5a4705b3f03a5fa8407cfa`, reached a stable paused checkpoint on Nullpunkt with `22.737944400000003` active seconds, `98.935645379747143` physical metres, and 0 teleport metres. Duckov PID 39752 was then terminated with `taskkill.exe /F`, which bypassed Unity shutdown: the process disappeared with no `OnApplicationQuit`, `OnDestroy`, terminal-run, hook-unsubscription, or clean-close log entry. The final active-run checkpoint remained at SHA-256 `830abcee5e8b50358c1cee4022f146760a361db3c838107fdfee222459229f34`, its preceding valid backup remained at `fc7e9e03a1c9145f3cf100d0f940a3f48707059f810348614a115b0c6404d136`, the dirty session checkpoint remained, the profile still contained only the prior two runs, and no temporary file existed.

On the next startup, UDS logged one recovered interrupted run and one recovered interrupted session before opening the same generation with `interruptedSession=True interruptedRun=True`. The recovered summary preserved the exact run ID, generation, native raid 4, known map, active duration, physical distance, teleport distance, integrity/capability context, and non-record-eligible status from the checkpoint. Its diagnostic end is the final checkpoint observation at `2026-08-10T19:01:01.327Z`, producing `92.78999999999999` wall-clock seconds. Both active-run files were consumed. Totals advanced by exactly one summary to three runs: Died 1 and Interrupted 2 (the other Interrupted is the separately documented graceful-shutdown run), `308.7462019365691` physical metres, and 0 teleport metres. `InterruptedSessionCount` advanced exactly once to 1. Death records remained exclusively on Died run `d39e1fd3d6314d65b117b881b73df866`; no extraction record appeared; item/healing totals remained 2 uses, 2 StackUnit, and 0 HP.

That recovery instance then exited normally. The log proves lifecycle/movement and item-use unsubscription, sampler stop, main-duck reference release, and clean generation close. Primary and backup became byte-identical at revision 50 and SHA-256 `9d8881e976723d1c0c04aff04c8587687fc0315627787aead65cd3084c797273`, with no active-run, session, or temporary residue. A second restart remained at base and opened with `interruptedSession=False interruptedRun=False`. At revision 54 it still contained exactly three runs, Died 1 / Interrupted 2, recovered-session count 1, the same distances, records, and M1/M2 totals; no active checkpoint existed and only the expected new live session checkpoint was present. This proves the unfinished checkpoint was recovered exactly once and cannot be counted again on repeated restart.

#### Manual gate 5 — final slot-6 UI/profile/JSON/CSV agreement

Passed 2026-08-10. The user's final Overview shows generation `badb76d6cbb14b44915c2ddaf26ba166`, 2 item uses, 0 HP, 2 StackUnit, one recovered interrupted session, three runs split as Extracted 0 / Died 1 / Interrupted 2, `308.75 m` physical distance, 0 teleport/excluded distance, and the unchanged Healing group. Runs shows all three summaries newest-first with the exact persisted rounded values: recovered Interrupted `00:00:22.738` active / `00:01:32.790` wall / `98.94 m`; graceful Interrupted `00:00:27.011` / `00:01:17.412` / `99.79 m`; and Died `00:01:48.126` / `00:03:23.541` / `110.02 m`. Records contains no extraction record and retains only Died run `d39e1fd3d6314d65b117b881b73df866` as shortest and longest death overall and per map. The Nullpunkt aggregate shows three runs, `308.75 m` physical, and 0 teleport/excluded distance.

The final export directory is `20260810T1904570870989Z-badb76d6cbb14b44915c2ddaf26ba166`. It contains exactly the required eight files with these SHA-256 values: `ea67c8b867cd40c38e1d0ce0c39b3206fe2d849d88aba08df6b3abc37546eb7a` (`groups.csv`), `e3644569e9912fddf9c05e59b54a7b2b0c611465f40d25e597647dfd078c9e90` (`items.csv`), `c572b3005e326f54c8c2d920cef42ef44f492258002c95a43e7dfa51d679769b` (`map_totals.csv`), `a1b526e1b6e16d8b9d0da431483ac7ac22812591f4c84d91042b92c7075ccdbf` (`overview.csv`), `89613f6c87837693cf5b59493e8d97812f5d6e14ec189abb750a31f1cd22c21e` (`records.csv`), `a84eff2e297cfe6cab027d333051727060aa441506eec98bca10092d925e9749` (`run_totals.csv`), `c0c69461e7b1aa48ef0559a0dc4e83ec923b36b802037a0f4895bb3c7b9d0d29` (`runs.csv`), and `c76a53dd7bab4800f8ede019c84a7caa218d425fe53794955ea6050d3a9b6557` (`statistics.json`).

An independent structural assertion pass checked all eight filenames, schema/generation/slot identity, every run ID and numeric field, outcome and record-eligibility counts, overall and per-map totals, record scope/type rows, group/item rows, and all M1/M2 totals. It passed with three runs, Died 1 / Interrupted 2, four Died record rows, exact physical total `308.7462019365691`, and zero teleport distance. The export is revision 58; later game save-identity writes advanced the live profile to revision 60 without changing any statistical field, so the snapshot and live state agree semantically while retaining their truthful revision timestamps.

#### Manual gate 6 — final normal shutdown and deployed-package integrity

Passed 2026-08-10. The final normal exit logged `OnApplicationQuit` and `OnDestroy`, unsubscribed lifecycle/movement and item-use hooks exactly once, stopped the sampler, released the main-duck reference, and closed generation `badb76d6cbb14b44915c2ddaf26ba166` cleanly. The final primary and backup profile are byte-identical at revision 63 and SHA-256 `4a0f46317003b6e78c4a06eb472dadf3caa015316afde961971118b0b7e18552`. They retain exactly three runs, Died 1 / Interrupted 2, one recovered interrupted session, `308.7462019365691` physical metres, 0 teleport metres, 2 item uses, 2 StackUnit, and 0 HP. No active-run primary/backup, session checkpoint, or temporary file remains.

The deployed mod folder still contains exactly five files with the same hashes recorded before gameplay: `20653a8a3411c1e7409bae2a541b703af53db5bc3c6e60017cb3a617e71a6353` (`info.ini`), `4d7c634a354fb19578ced3b1e33eedee5e360a40ef30c16c69192be3ed3d0b40` (`INSTALL.md`), `0f7558f2469ad0901074f6c380ada1ed91861d55adf905267bc70b26cd2e3ccc` (`LICENSE`), `c307ccef94f167b6c6a368ed5904264dc9c0e516161a9af02c1dffc30d32d287` (Core DLL), and `9acfbe137d8c6481e9eebb6a72d8aaeec7ec7feb3a62a9bdb3a72fb58c9622be` (native DLL). External Harmony remains separately installed and was not modified or bundled. The complete manual matrix therefore passes without any UDS write to a Duckov save or any modification of Harmony, another mod, or a game assembly.

#### Initial v0.3.0 committed-artifact gate — superseded by follow-up review hardening

Passed 2026-08-10. Implementation commit `423349d1db846df754577838ecba5657f09e1efa` was cleanly rebuilt through `scripts/create-release.ps1`: all 134 Release tests passed, the Duckov `2.3.30` / Steam `24013657` / Unity `2022.3.62f2` contract probe passed, the native build completed with 0 warnings and 0 errors, and package validation passed. The package freshness audit now excludes generated `bin` and `obj` trees from authored-source timestamps, preventing generated compiler files from falsely invalidating an otherwise fresh DLL while retaining source freshness enforcement.

The resulting `UltimateDuckovStatistics-v0.3.0.zip` is 91,220 bytes with SHA-256 `4ae77ddc95f5ebb28a24724d1f9a2e16d1ffbf05f63424c34b8d3b3115315296`. Its UTF-8 sidecar is exactly `4ae77ddc95f5ebb28a24724d1f9a2e16d1ffbf05f63424c34b8d3b3115315296  UltimateDuckovStatistics-v0.3.0.zip`. Independent extraction and `verify-package.ps1` validation confirmed exactly five permitted files, no bundled game/Unity/framework/Harmony dependency, and byte identity with the source package:

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `info.ini` | 292 | `20653a8a3411c1e7409bae2a541b703af53db5bc3c6e60017cb3a617e71a6353` |
| `INSTALL.md` | 5,616 | `4d7c634a354fb19578ced3b1e33eedee5e360a40ef30c16c69192be3ed3d0b40` |
| `LICENSE` | 1,117 | `0f7558f2469ad0901074f6c380ada1ed91861d55adf905267bc70b26cd2e3ccc` |
| `UltimateDuckovStatistics.Core.dll` | 121,344 | `b7f5a4105a3bcbfbbae3f3704edccdca69a239ab9fbf3f7f89ce8f251533c4e2` |
| `UltimateDuckovStatistics.dll` | 87,552 | `f8cb7bbb1909045c7d7cf73cae61fa3772c8bdb09bf0c6e6b2192c688e9e3440` |

Both DLLs report informational version `0.3.0+423349d1db846df754577838ecba5657f09e1efa`, proving that the archive corresponds to the implementation commit above. This evidence remains preserved historically, but the archive is no longer the release candidate after the follow-up review fixes recorded above. A replacement committed-head artifact gate follows after rebuilding from the fix commit.

#### Final review-hardened v0.3.0 committed-artifact gate

Passed 2026-08-10. Fix commit `22258f4bcdf7a430f78eb4518f91953cf5120d74` was rebuilt through `scripts/create-release.ps1`: all 140 Release tests passed, the expanded runtime-integrity Duckov contract probe passed, the native build completed with 0 warnings and 0 errors, and the exact package validator passed.

The replacement `UltimateDuckovStatistics-v0.3.0.zip` is 92,145 bytes with SHA-256 `76a28f4c5b8a6ed6a73ef847e8e4d761236536a5c4d3a26019cbaa64a2869982`. Its UTF-8 sidecar is exactly `76a28f4c5b8a6ed6a73ef847e8e4d761236536a5c4d3a26019cbaa64a2869982  UltimateDuckovStatistics-v0.3.0.zip`. Independent extraction into a fresh directory and `verify-package.ps1` confirmed exactly five permitted files, no bundled Duckov, Unity, framework, or Harmony dependency, and byte identity with the source package:

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| `info.ini` | 292 | `20653a8a3411c1e7409bae2a541b703af53db5bc3c6e60017cb3a617e71a6353` |
| `INSTALL.md` | 5,616 | `4d7c634a354fb19578ced3b1e33eedee5e360a40ef30c16c69192be3ed3d0b40` |
| `LICENSE` | 1,117 | `0f7558f2469ad0901074f6c380ada1ed91861d55adf905267bc70b26cd2e3ccc` |
| `UltimateDuckovStatistics.Core.dll` | 122,368 | `5012cfbbaa81fa84e65b385e9fed8fa68b6386db38bd04dd969500787a075ed5` |
| `UltimateDuckovStatistics.dll` | 89,088 | `3ef112136709bdca6369e711ee3dbca333b7b7e9df7a682f7d37cd47bf38152d` |

Both DLLs report informational version `0.3.0+22258f4bcdf7a430f78eb4518f91953cf5120d74`, proving the replacement archive corresponds to the review-fix commit. As before, the following evidence-only documentation commit does not trigger an artifact rebuild cycle; final CI currency is recorded on draft PR #3 at its remote documentation head.
