# Post-M18 batch four: opacity and base-distance proposal

Base: `68623d4f32190b7c73f1368b10d3ee3f8b73cb09`, fetched from `origin/main` on 2026-09-10 and verified as the [PR #22](https://github.com/bamboechop/ultimate-duckov-statistics/pull/22) merge. Branch: `codex/rc2-opacity-base-distance`. This is an ordinary local test build, not an rc.2 publication. The original checkout's modified `PLAN.md` and untracked firing diagnostic are outside this batch.

## Implemented: shared background opacity

`RetainedDimmerPolicy.VisualAlpha` in `src/UltimateDuckovStatistics/UI/StatisticsPanelProjection.cs` changes from 0.75 to 0.85. `RetainedStatisticsShell.TryCreate` applies it to the full-canvas black root `Image`, which also blocks raycasts. Child graphic opacity is independent of that image: no `CanvasGroup`, material, input, access, resource-ownership or cleanup change is involved.

The rendering path is `RetainedNativeStatisticsPanel.RequestOpen` -> native canvas resolution -> the same `RetainedStatisticsShell.TryCreate` for main-menu, base-pause-menu and permitted hotkey access. All ten tabs remain in the existing order, with About immediately before Diagnostics. Raid access restrictions remain as before.

`CreateHeaderBackground` adds a black 0.50-alpha `ProceduralImage`. `CreateOverviewPanel` uses the shared black 0.50-alpha panel style, also used by the other retained tab views; individual cards and rows can add further layers. For flat interiors over the underlying game image, alpha compositing gives:

| Area | Before | After |
| --- | ---: | ---: |
| Root dimmer only | 75% | 85% |
| Dimmer plus one 50% header/panel | 87.5% | 92.5% |
| Dimmer plus two 50% layers | 93.75% | 96.25% |

Formula: `1 - (1 - dimmerAlpha) * product(1 - childAlpha)`. These are background attenuation values, not text alpha or a measured screenshot result; rounded/antialiased edges vary. Existing button, selection, hover, tooltip, icon and text styles are unchanged. An unused `RetainedHeaderPolicy.EffectiveOpacity = 0.75` constant and its literal-only assertion were removed because they did not describe this rendered composition. The existing dimmer contract test now checks matching styling, mismatched styling and raycast rejection against the policy rather than freezing a color literal. No new visual-literal test was added. Blur is cancelled; no blur work or dependencies are part of this batch.

User-controlled visual check:

1. Open from the main menu and from the base via pause-menu and hotkey access. Assess the initial 85% dimmer target over bright and dark scenery.
2. Visit all ten tabs; check text, icons, controls, selected/hover states, scrolling and tooltips. About must immediately precede Diagnostics.
3. Close with Back/Escape, reopen, and verify background clicks remain blocked while open and normal input returns after closing. Existing raid access rejection remains in effect. Visual acceptance is still required; no game was launched by the agent.

## Investigated, not implemented: base distance

**Verdict: feasible.** Reuse the lifecycle adapter's cadence, native subscriptions and main-character binding, but route base observations to a separate accumulator and a profile-level recorded total. Do not enable `RunLifecycleTracker` in the base or reuse its run/segment totals. There is no native historical base odometer in the inspected movement path; the proposal records future observations only.

### Evidence and current production path

Installed metadata and method bodies were inspected locally from Duckov **2.3.30**, Steam build **24013657**, Unity **2022.3.62f2**. The normal contract probe passed. `TeamSoda.Duckov.Core.dll` SHA-256: `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`. Decompiled files remain in ignored local artifacts and are neither committed nor packaged.

| Contract / production location | Finding and implication |
| --- | --- |
| `NativeRunLifecycleAdapter.Tick`, `TryStartRun`, `SampleMainDuck`, `OnMainCharacterSetPosition` | Tick samples only an active, unsuspended tracker. Start requires `NativeRaidContext.IsRaidMap()` and native control readiness; the position callback also requires `IsActive`. Thus the base is explicitly excluded, although the main character is already bound there. |
| `RunLifecycleTracker.ObserveMovement`, `CreateCheckpoint`, terminal completion; `MovementAccumulator.Observe` | Physical distance belongs to the active run and segment. Observation uses 3D position, monotonic elapsed time and speed plausibility. Run checkpoints normally fall due every five seconds, with separate combat scheduling. |
| `LevelManager.IsBaseLevel`, `IsRaidMap`, `LevelConfig` | Authoritative serialized level classification. Use positive base evidence; neither a missing raid nor a scene-name substring proves base context. Conflicting flags should be unknown. |
| `LevelManager.InitLevel`, `HandleRaidInitialization` | `LevelInited` becomes true before final `SetPosition(startPos)`. `OnAfterLevelInitialized` follows that placement, then `AfterInit` becomes true. Base initialization can call `RaidUtilities.NotifyEnd`; `CurrentRaid` is not an exclusive base/raid classifier. |
| `SceneLoader.IsSceneLoading`, loading events; `MultiSceneCore.IsLoading`, subscene events | `onFinishedLoadingScene` precedes the wait for level initialization. Existing loading guards and placement boundaries must be preserved; do not start collecting just because this callback fires. |
| `LevelManager.MainCharacter`, `CharacterMainControl.IsMainCharacter`, `Health.IsDead`, `OnSetPositionEvent` | Bind the exact live main duck, never companions/NPCs/camera. `CharacterMainControl.SetPosition(Vector3)` calls `Movement.ForceSetPosition`, then the instance event. Replacement and death need baseline reset. |
| `CharacterWalkSpeed`, `CharacterRunSpeed`, `DashSpeed`; `Movement.UpdateMovement` | Existing speed bound takes the maximum of these native properties. Native movement includes velocity, gravity and forced movement; input intent alone does not define displacement. |
| `GameManager.Paused`, `PauseMenu.Show/Hide`, `TimeScaleManager.Update` | Paused means the pause menu is shown; native time scale is set to zero for pause or camera mode. This is distinct from merely opening a view. |
| `RetainedNativeStatisticsPanel.CaptureFocusAndCursor`, `InputManager.DisableInput/ActiveInput/InputActived` | UDS blocks input through a live input-source object; it does not set time scale. Input cooldown also lasts two frames after removal. Existing movement can settle while input is blocked. |
| `Duckov.UI.SleepView.Sleep`, `View.OnOpen/OnClose` | Sleep calls `GameClock.Step(seconds)` behind a black screen, waits in unscaled time, then raises `OnAfterSleep`. A view disables input; this clock jump is not elapsed physical motion. |
| `ModBehaviour.Update`; `NativeProfileCoordinator` transition, flush and export barriers | Lifecycle tick already runs while UDS is closed. Slot changes, identity changes and reset rotate/bind generations; `ProfileChanging` interrupts the run. Persistence/transition failures have retry barriers. New base state must participate before the repository changes. |
| `StatisticsPanelProjectionFactory`, profile-summary distance case; `RunStatisticsViewModelFactory.Create` | Overview's current “Total distance travelled” reads `RunTotals.PhysicalDistance`, i.e. completed/recovered recorded runs. It does not merge the active checkpoint into that total. |
| `StatisticsExporter`, `ProfileExportWriter`, `ProfileRepository.CaptureExportSnapshot` | JSON and run/map/route CSVs contain run-derived distances. Export captures a detached profile after durability barriers. Base values require their own projection/export fields; no change to run/route CSV meanings. |

### Counting and lifecycle recommendation

Define the metric as **recorded sampled physical displacement of the live main duck in a positively identified base**, in meters. Include walking, running, dashes, slopes/vertical motion and plausible physical drift/knockback. It is a 3D sampled path, not steps, movement-key time or an exact animation/physics odometer. Chords across curved motion and the existing 0.02 m jitter filter can undercount.

Use one cadence decision (0.2 s, no catch-up loop) and mutually exclusive `Base`, `Raid`, `Suspended/Unknown` routing. Keep raid tracker, segments, records, run IDs and completed-run reducers untouched. Give base its own `MovementAccumulator`; count only `Physical` results into its durable aggregate.

- Enter base only with a bound current generation, no pending profile transition, a live main character, positive base classification, `LevelInited` and `AfterInit`, and all scene/level/multiscene loading guards clear. The extra `AfterInit` check prevents late initial placement from becoming movement. Late mod activation can use those state flags without waiting for an event it already missed.
- Reset the positional baseline on context, level host, subscene, character or generation change; on death/destruction, missing/invalid position or speed; and before/after loading and pause. The first valid observation establishes a baseline with zero increment. Do not bridge two actors or contexts, and do not sample the retiring raid and base from the same cadence opportunity.
- Handle `OnSetPositionEvent` even when no raid is active: validate the exact actor, discard/rebaseline the base discontinuity, and never count the callback's relocation as physical movement. Scene placement, respawn and scripted repositioning through that contract are excluded. Existing route placement bookkeeping remains raid-only.
- Reject implausible jumps and gaps longer than two seconds using existing accumulator limits (`speed * elapsed * 1.75 + 0.35 m`). The accumulator labels some inferred jumps and resume/gap discontinuities `Teleport`; this is **not proof of an explicit teleport**. Base should neither publish nor add those values to a “proven teleport” total.
- Pause/loading/camera freeze produces no increments; clear baselines across the suspension. Do not gate established base sampling on `InputActived` or UDS visibility: input blocking does not prove zero velocity. With a hotkey-opened UDS or another ordinary view, count actual plausible movement while the world runs; stationary samples add nothing. For pause-menu UDS, the native pause guard excludes the interval.
- Do not derive movement from `GameClock` or sleep duration. Sleep's clock jump contributes zero by itself. Actual physical displacement during the unpaused view/black-screen interval follows the same rules. `OnAfterSleep` may conservatively clear the baseline; no new sleep Harmony patch is needed for distance. Missing evidence loses that interval, never invents meters.

A script/mod that directly writes a transform or a lower-level movement position, bypassing `CharacterMainControl.SetPosition`, can make a small plausible reposition indistinguishable from locomotion at 5 Hz. The inspected public event is not a universal interception guarantee. Large jumps are filtered, but strict separation of every possible scripted movement would require a larger, separately qualified native-hook scope. Recommend the observational definition above, with this limitation documented.

On base exit, main-menu return, profile transition, explicit export, disable and normal shutdown, drain pending base increments into the **old** generation and require the existing durability barrier to succeed before rebinding. Stage generation-tagged batches so a retry cannot apply them twice; retain failed data and back off. Never let `ProfileChanged` alone decide that a still-live old character belongs to the incoming slot: a slot-load transition must wait for the new level's readiness/placement; a same-native-scene user reset can establish a fresh baseline after the reset barrier completes. This needs an explicit handoff state analogous to the existing world-time/crafting boundaries.

On abrupt termination, restore only the latest valid durable base aggregate from the profile; do not replay an in-memory position or turn it into a recovered raid. Reuse the interrupted-session signal to mark a known capture gap. Recommend five-second dirty-only durability while moving, so under healthy storage the loss window is roughly five seconds plus in-flight write latency (not a hard bound during failures). A mod-disabled interval and history before collection are never reconstructed.

### Durable representation, compatibility and presentation

Smallest recommended payload: an optional profile-level `BaseMovement` aggregate containing `RecordedMeters`, nullable `CollectionStartedUtc`, and `HasKnownGaps`; current adapter availability/provenance belongs in the existing capability snapshot. No coordinate history, run, segment, per-map base table or second journal is needed. Establish collection start at the first trusted baseline, including a stationary one, not at software installation or profile creation. Absence means not recorded, never historical zero. A “no known gaps” bit cannot prove capture while UDS was disabled; labels always qualify the value as recorded.

Keep all numeric values finite/nonnegative and timestamps UTC, with start presence consistent with collection state. Validate invalid aggregates during candidate selection so a valid backup/temporary file can recover them; do not silently normalize corruption to an apparently exact zero. Include the aggregate in repository persistence snapshots/clones, detached export snapshots, generation creation/reset and capability publication. No sample baseline is persisted across processes.

Current storage is `FormatId=uds-profile-v1`, schema **18**, directory `v1`. `AtomicJsonStore` selects primary, backup, then temporary with semantic validation; `ProfileFormat` preserves incompatible candidates for explicit archival rather than selecting an older compatible backup over them. `ProfileRepository.Open` archives unsupported schemas intact and creates a new generation.

**Compatibility finding:** a temporary, in-memory experiment using the actual current `ProfileDocument` and `DataContractJsonSerializer` added a synthetic `Statistics.BaseMovement.RecordedMeters=123.45`, deserialized and reserialized it. The unknown member disappeared. Thus simply adding a field under schema 18 would let older builds silently erase newly recorded base data on save. Optional fields alone do not make rollback safe.

Recommend a schema marker increment with a narrow, tested admission path for the **actual current v1/schema-18 format** only. It must validate and preserve the existing generation, all metrics, retained runs and supported recovery state before updating required root/nested/session/checkpoint schema stamps and adding absent base evidence. Cover primary/backup/temporary candidates and interrupted active-run recovery together; do not merely change `ProductInfo.SchemaVersion`, which would archive the existing profile. Keep an intact pre-change backup and perform writes atomically. Reject unknown/future formats without normalization. This is a concrete current-format extension, not restoration of schema-by-schema 0.x migrations. An older build would archive the newer format intact and start another generation, rather than silently deleting the new field; returning to the newer build would not automatically reselect that archive. Document that rollback limitation. A separately versioned sidecar avoids the old writer but adds cross-file generation/reset/recovery joins; it is not the recommended minimum.

Proposed Overview rows (localizable, with normal wrapping):

| Label | Existing current-format profile | Fresh profile |
| --- | --- | --- |
| Raid distance | Preserve recorded completed/recovered run distance and its capability state | Recorded zero if supported; otherwise unavailable |
| Recorded base distance | “Not recorded” until the first trusted baseline; then `N m — since <date>` | Same baseline rule; `0 m` is valid only after collection starts |
| Total recorded distance | Numeric sum of available recorded raid/base contributions, explicitly partial when base has never been recorded or either source is incomplete | Sum of the recorded contributions; never a lifetime-before-UDS claim |

If a source is currently unavailable but retains a known recorded value, show that value as partial with its unavailable-collection status. If a contribution has no trustworthy numeric evidence, present the combined amount as a partial subtotal with that component named, not an unqualified total. A shared hint states: “Recorded while UDS is active. Earlier base movement is not included.” Known gaps remain visible. Keep raid updates at the existing completed/recovered-run boundary; do not silently add active raids to Overview in this feature.

Add one shared distance projection for Overview and exports. Extend `statistics.json` and `overview.csv` with explicit recorded raid/base/combined meters, base start, current source states and partial-coverage metadata; use null/empty numeric cells for not-recorded evidence. Keep `run_totals.csv`, `runs.csv`, map, segment, route and records exports raid-only. Export must drain base state into the chosen generation before its existing detached snapshot. Package inventory remains five files; export-schema consumers and CSV headers need regression coverage.

### Runtime cost and implementation scope

Reuse `NativeRunLifecycleAdapter.Tick`, native callbacks, `ReferenceSubjectGate` and `MonotonicCadenceGate`; no second polling loop or repeated scene searches. Cache boundary identities from native state/events. Add at most one position read and the existing speed reads per due base sample, up to five samples/second while UDS is closed or open. O(1) arithmetic/state is sufficient; do not allocate per-position logs or collections.

The existing `MovementObservationResult` is a class, so reuse is not allocation-free. An isolated warmed .NET 8 experiment with 100,000 observations measured **4,000,000 allocated bytes**, **40 bytes/sample**, for both stationary and physical movement paths. At 5 Hz that is an estimated 200 bytes/second for results alone. This measures the managed accumulator on .NET 8, not Unity Mono, native property access, full adapter cost or frame time. No in-game overhead measurement was performed, and “negligible overhead” is not established.

Accumulate in memory; update profile projection at a bounded cadence (recommend at most once/second while changing), and request dirty-only durability every five seconds plus lifecycle barriers. The generic `DeferredSnapshotWriter.MarkDirty` is serviced from `ModBehaviour.Update` as soon as submission is permitted: **do not mark it dirty on every sample**, or this becomes frequent whole-profile serialization. A dedicated small cadence gate must schedule the existing writer, retaining the latest aggregate/revision across in-flight writes. Other legitimate profile writes may include the already-applied base total sooner. Background serialization still scales with profile size; snapshot capture and publication have main-thread costs. Five-second cadence permits about 12 movement-driven submissions/minute during sustained movement, with no continuing submissions for a stationary unchanged aggregate. Measure this later on representative profiles; no complete M18 performance campaign is required for this proposal.

Affected components: a small pure base accumulator/aggregation policy, lifecycle adapter routing and disposal, `ModBehaviour` wiring, coordinator handoff/export/flush barriers, repository deferred mutations and snapshot clones, profile format/aggregate validation and the single current-format admission path, capability publication, Overview/localization, shared distance export projection and affected JSON/CSV contracts. No permanent base implementation or schema change is in this batch.

Required implementation regressions: physical/jitter/invalid/speed-gap observations; small explicit teleport exclusion; pause/UDS distinction; late initialization placement; same/different actor and subscene; base -> raid -> base without duplicate meters or synthetic runs; same-slot reopen, slot load and generation reset with delayed readiness; blocked and retried persistence/handoff without duplicate batches; current-format admission preserving every existing field and interrupted recovery; valid backup/temporary fallback; newer-format preservation by old code; unknown versus recorded-zero rendering; retained partial values and identical UI/JSON/CSV meanings; bounded sampling/dirty submissions and snapshot ownership during concurrent saves.

User-controlled gameplay qualification after implementation: known base walks/runs with UDS closed/open, pause and sleep without artificial distance, ordinary base/raid transitions and death return, normal save/reopen across slots, Overview/export agreement and focused frame/allocation observations. Scripted jumps and crash recovery can be automated with synthetic state; do not force gameplay hazards or alter saves for testing.

**Decision before implementation:** authorize the proposed feature scope, particularly sampled 3D physical movement (including plausible drift), qualified recorded totals and the guarded current-format schema extension. Recommended defaults are 0.2-second sampling and five-second dirty-only durability. Exact base calibration, unusual lower-level repositioning and native performance remain qualification boundaries, not blockers to this opacity delivery.

## Validation and delivery

The full ordinary build passed 2,001 main tests and 64 shell tests in **each** of Debug and Release, the installed contract probe, native Debug/Release builds and package/ordinary-IL/privacy checks. Diagnostic shell suites also passed 68 tests per configuration; these source-linked test builds are not the deployed mod. Changed-source formatting/analyzers, whitespace checks and tracked native-binary exclusion passed. The first run's existing 75% assertion was corrected as described above. No test count was added for opacity literals. Prior accepted M18/UI/Combat/About evidence is reused for unchanged behavior; actual visual acceptance remains with the user. Local scratch experiments touched no game, save or UDS-profile data.

Final package/deployment evidence is recorded in the delivery section below. PR and CI status should be read from GitHub rather than treated as durable document state.
