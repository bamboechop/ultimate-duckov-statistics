# Post-M18 batch four: panel opacity and base distance

Based on `68623d4f32190b7c73f1368b10d3ee3f8b73cb09`, the merge of [PR #22](https://github.com/bamboechop/ultimate-duckov-statistics/pull/22). The change and its review/CI live at [PR #23](https://github.com/bamboechop/ultimate-duckov-statistics/pull/23). This is an ordinary local test build, not an rc.2 publication. The original checkout's modified `PLAN.md` and untracked firing diagnostic remain outside this batch.

## Background opacity

The full-canvas dimmer remains at the previously implemented 85%. The header, inner panels, cards and dark row backgrounds now use 75% opacity. Shared policies cover Overview and retained views; local row/card styles in Combat, Equipment, Economy, Crafting, Item Use, Runs and Diagnostics use that same alpha. Colored selections and buttons retain their interaction styling.

Over the game image, the effective flat-interior background opacity is 96.25% for the dimmer plus one 75% panel, and 99.0625% with a second 75% inner layer. Formula: `1 - (1 - dimmerAlpha) * product(1 - childAlpha)`. Rounded edges, text and colors still require visual inspection. Opacity does not change input/access restrictions or canvas ordering.

## Recorded distance

Overview shows **Total recorded distance**, **Raid distance**, and **Recorded base distance**. Combined distance adds the known recorded raid and base contributions. Raid distance remains completed/recovered run distance; active checkpoints are not merged into the lifetime total. Runs, routes, segments, records, map totals and their CSV semantics remain raid-only. Base movement never manufactures runs, segments, record candidates or playtime.

Base distance is sampled 3D displacement of the exact live main duck on a positively identified base. Walking, running, dashing, slopes and plausible physical drift count. The existing movement filter excludes jitter, implausible jumps and long sampling gaps. Chords across curved motion and the 0.02 m jitter filter can undercount; this is a sampled odometer. Explicit native `SetPosition` callbacks reset the baseline even for small jumps. A foreign mod directly changing a transform by a physically plausible amount cannot always be distinguished from movement.

The adapter requires positive base / negative raid flags, completed final placement (`LevelInited` and `AfterInit`), a live main character, a current UDS generation, supported movement collection, and no loading, pause, frozen time or pending profile transition. A slot-selection event quarantines the retiring native level until a different ready level exists. Character replacement, subscene changes, load, pause/resume and explicit placement reset the positional baseline. Merely blocking input with UDS does not suppress genuine movement. Sleep's game-clock jump is not converted into distance.

No backfill is possible. The profile records the UTC time of its first valid base observation. Before that, base shows **Not recorded**, rather than zero. A first stationary observation establishes a known zero. Missing or interrupted collection is reported as partial; combined values carry the same limitation. The three distance rows have coverage tooltips rendered on the owned statistics canvas. The Overview summary scrolls, with keyboard/controller focus and offset retained across projection refreshes, so the extra rows do not hide Economy.

## Current-format persistence and export

`ProfileStatistics.BaseMovement` is an optional current-format aggregate containing `RecordedMeters`, `CollectionStartedUtc` and `HasKnownGaps`. No schema bump, predecessor migration, downgrade guard or older-build compatibility machinery is introduced. The user explicitly rules out older builds writing this data. An absent member remains absent until a valid current observation; existing raid totals retain their meaning.

Sampling uses the existing 0.2 s monotonic cadence with no catch-up loop. Session-local cumulative snapshots publish into memory at most once per second while dirty. Absolute capture totals make an uncertain acknowledgement safe to retry without double addition. Profile writes use the existing detached-snapshot writer at a five-second cadence while dirty; no profile serialization or disk write is added per frame. Stationary observations do not keep dirtying the profile. Native save, export, profile transitions and cleanup force the pending observations through the existing durability boundary. Failed writes retain the aggregate and block generation changes until retry succeeds. Crash recovery retains the last durable value and marks a known collection gap; unflushed movement can be lost on process termination.

The aggregate participates in detached persistence/export snapshots, current-format semantic validation, backup/temporary recovery and generation reset. Negative/non-finite distance or invalid UTC collection metadata rejects a recovery candidate. Coordinates and native actor identities are never persisted.

JSON exposes the shared `Distance` projection. `overview.csv` adds raid/base/combined meters, collection-start UTC, current availability, partial flags and coverage text. Missing numeric observations use empty CSV fields / nullable JSON fields. UI and export read the same projection. Run/route exports keep their prior meanings.

## Installed native contracts

Inspected local Duckov **2.3.30**, Steam build **24013657**, Unity **2022.3.62f2**. Native `TeamSoda.Duckov.Core.dll` SHA-256: `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`. Local decompilation and probes are ignored evidence and are not shipped.

| Contract | Implementation consequence |
| --- | --- |
| `LevelManager.IsBaseLevel`, `IsRaidMap`, `LevelConfig` | Authoritative serialized level classification. Use positive base evidence; neither a missing raid nor a scene-name substring proves base context. Conflicting flags should be unknown. |
| `LevelManager.InitLevel`, `HandleRaidInitialization` | `LevelInited` becomes true before final `SetPosition(startPos)`. `OnAfterLevelInitialized` follows that placement, then `AfterInit` becomes true. Base initialization can call `RaidUtilities.NotifyEnd`; `CurrentRaid` is not an exclusive base/raid classifier. |
| `SceneLoader.IsSceneLoading`, loading events; `MultiSceneCore.IsLoading`, subscene events | `onFinishedLoadingScene` precedes the wait for level initialization. Existing loading guards and placement boundaries must be preserved; do not start collecting just because this callback fires. |
| `LevelManager.MainCharacter`, `CharacterMainControl.IsMainCharacter`, `Health.IsDead`, `OnSetPositionEvent` | Bind the exact live main duck, never companions/NPCs/camera. `CharacterMainControl.SetPosition(Vector3)` calls `Movement.ForceSetPosition`, then the instance event. Replacement and death need baseline reset. |
| `CharacterWalkSpeed`, `CharacterRunSpeed`, `DashSpeed`; `Movement.UpdateMovement` | Existing speed bound takes the maximum of these native properties. Native movement includes velocity, gravity and forced movement; input intent alone does not define displacement. |
| `GameManager.Paused`, `PauseMenu.Show/Hide`, `TimeScaleManager.Update` | Paused means the pause menu is shown; native time scale is set to zero for pause or camera mode. This is distinct from merely opening a view. |
| `RetainedNativeStatisticsPanel.CaptureFocusAndCursor`, `InputManager.DisableInput/ActiveInput/InputActived` | UDS blocks input through a live input-source object; it does not set time scale. Input cooldown also lasts two frames after removal. Existing movement can settle while input is blocked. |
| `Duckov.UI.SleepView.Sleep`, `View.OnOpen/OnClose` | Sleep calls `GameClock.Step(seconds)` behind a black screen, waits in unscaled time, then raises `OnAfterSleep`. A view disables input; this clock jump is not elapsed physical motion. |
| `ModBehaviour.Update`; `NativeProfileCoordinator` transition, flush and export barriers | Lifecycle tick already runs while UDS is closed. Slot changes, identity changes and reset rotate/bind generations; `ProfileChanging` interrupts the run. Persistence/transition failures have retry barriers. New base state must participate before the repository changes. |
| `StatisticsPanelProjectionFactory`, profile-summary distance case; `RunStatisticsViewModelFactory.Create` | The raid-distance source reads `RunTotals.PhysicalDistance`, i.e. completed/recovered recorded runs. It does not merge the active checkpoint into that total. |
| `StatisticsExporter`, `ProfileExportWriter`, `ProfileRepository.CaptureExportSnapshot` | JSON and run/map/route CSVs contain run-derived distances. Export captures a detached profile after durability barriers. Base uses its own projection/export fields; no change to run/route CSV meanings. |


## Validation and delivery

Focused tests execute the production capture, repository, native lifecycle/coordinator and retained shell against controlled boundary stubs. They cover baseline/physical movement, stationary publication, invalid inputs, long gaps, inferred and explicit teleport, pause versus input blocking, final initialization, loading, character replacement, base versus menu/raid, slot quarantine, uncertain acknowledgements, blocked writes and retry, current-format absent versus zero, recovery, snapshot isolation, shared projection/export and summary scrolling/focus.

Full Debug/Release tests, installed contract probe, native builds, ordinary package inventory/extraction/hash audit and two-root reproducibility are recorded with the final immutable package receipt in [RC2_OPACITY_DELIVERY.json](RC2_OPACITY_DELIVERY.json). The receipt distinguishes this implementation from the preceding opacity-only package. Automation is not native gameplay or rendering qualification.

User-controlled acceptance: inspect all tabs at normal and small resolutions over bright base scenes and the pause menu; scroll Overview through Economy and hover distance coverage; walk/run/dash in base, pause/resume, reposition/load/switch saves, make one raid and return to base, export and restart. Confirm combined = recorded raid + recorded base, raid totals stay raid-only, and base resumes from the persisted value. Native controller navigation, tooltip placement, frame time and real Unity rendering remain manual checks. No game launch, gameplay, save selection, merge, tag, rc.2 release or Workshop publication is performed by this batch.
