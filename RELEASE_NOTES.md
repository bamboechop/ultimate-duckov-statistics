# Ultimate Duckov Statistics v0.8.1 — performance-hardening candidate

M8.1 retains every M1-M8 statistic, attribution, capability, persistence, recovery, and export contract while hardening five measured CPU hot paths. Controlled raw-frame captures show Harmony alone is indistinguishable from the clean game, but production v0.8.0 fails the matched Harmony-only ceiling at idle p99 and during a 30-round Electrified MP7 empty-space firing window. A later three-map soak also exposes a separate synchronous lifetime-profile persistence hitch on consumable completion. Both regressions are CPU-side rather than GPU-side.

## Included in v0.8.1

- Event-driven immutable equipment snapshots for firing/projectile/health associations instead of rebuilding, sorting, traversing, and hashing the full attachment/totem tree for each request. A one-second reconciliation watchdog remains, and unchanged snapshots are not republished.
- Full activation-time Harmony ownership validation bound to the exact shared-state token by a capture-before/validate/recheck sequence, followed by allocation-free identity-stamp checks for every healing, combat, and container patch point. Runtime checks distribute bounded dictionary lookups across the existing two-second integrity window, while healing callbacks check the same stamp immediately; any patch/unpatch replacement conservatively disables the dependent capability without runtime metadata deserialization or background reflection.
- Once-per-Unity-frame projectile context reconciliation with exact capture/outcome-time run checks, direct-reference ambient scope unwinding, and reuse of the application-time equipment association already carried by a combat scope.
- Single-flight deferred active-run validation, JSON serialization, durable flush, and atomic replacement. The exact immutable checkpoint is still cloned on the main thread at the existing one-second dirty/five-second periodic cadence; transitions, completion, profile changes, export, shutdown, and retry synchronously drain the pending write.
- A mutation revision guard so completion of an older snapshot cannot clear newer dirty state, plus deterministic background failure/retry and non-consuming-wait coverage.
- Frame-coalesced immutable lifetime-profile snapshots for item-use and healing mutations, with single-flight background serialization and replacement, checkpoint-before-profile ordering, boundary drains, bounded retry, and an exact persisted recovery watermark for profile/checkpoint deltas after abrupt interruption.
- Route-independent equipment continuity: losing safe map/segment context republishes one overall-only snapshot and preserves overall equipment durations and event-time associations while only route-dependent evidence degrades.
- A frozen CapFrameX/PresentMon protocol, independent raw-frame analyzer, and compile-time diagnostic counters whose call sites are absent from ordinary production builds.

## Candidate status

The complete Release suite passes 507/507. The installed Duckov 2.3.30 / Steam 24013657 / Unity 2022.3.62f2 / Harmony 2.4.1 contract probe, frame-time analyzer build, warning-free native Release build, exact-five-file package validation, changed-source formatting, route-independent equipment regressions, and ordinary-build zero-diagnostic-call proof pass. Earlier candidates identified and corrected the measured equipment, Harmony inspection, projectile reconciliation, checkpoint, lifetime-profile, and segment-cache costs. The final review correction preserves overall equipment tracking when route attribution becomes unavailable. Product commit `90384352d323e6ea19dfa607c7da18162dbcefcb` is the frozen build measured by the final campaign; its Core/native SHA-256 values are `8dd6b6b5891273a9f7807cd9239373c7ca261ee9bb7badf2b4b6ee53f18231a9` / `82a36bdbd41c584d051d637c38d89c186a238cf33c009fff0f4193f0b5b35096`.

The exact final build now has three accepted Harmony-only B and three accepted production-D captures in every required reproducible cell: idle, MP7 empty and single-enemy, MF empty and single-enemy, Mosin, and Med-Kit. Whole-capture median/p99 overhead is -0.007%/+2.454%, +0.313%/+2.066%, -1.267%/-2.334%, +0.063%/+1.748%, -8.087%/-7.315%, +0.007%/+1.617%, and -0.129%/+3.721%, respectively. Action-window median/p99 overhead is +1.355%/+4.526%, -3.902%/+4.068%, -0.654%/+1.339%, -13.645%/-12.420%, +0.036%/+3.916%, and -0.142%/+3.245%. Every cell meets the tighter 5% median/10% p99 engineering target, no action-correlated cluster repeats, and all accepted actions persist exactly. Six invalid attempts remain archived with objective reasons. The unsafe multi-target cell remains explicitly unavailable rather than silently omitted.

The earlier candidate-8 three-map soak remains supplementary natural-play, route, projection, and elapsed-run evidence; it does not substitute for the final causal matrix. After the final campaign, the user verified Runs, Items, Equipment, clean Diagnostics, JSON, and all nineteen CSVs. Normal shutdown leaves no active/session/checkpoint/temp state, null pending persistence, byte-identical primary/backup profiles, 33 supported plus four deliberately unavailable capability rows, and zero UDS error-like log lines. The performance result is **Target achieved**. The exact 590,272-byte five-file package produces `UltimateDuckovStatistics-v0.8.1.zip` at 221,763 bytes and SHA-256 `2510317d1aca11a19ab658941b513fa630d6b70f2a6d8065c77b57a744cdeb62`; its 102-byte checksum sidecar, independent extraction, deployed-byte readback, cold-start activation, final matrix, export, and shutdown gates pass. [Draft PR #9](https://github.com/bamboechop/ultimate-duckov-statistics/pull/9) is open and remains draft and unmerged; this candidate is not published, tagged, or uploaded to Steam Workshop.

Historical A/B/C controls still establish that Harmony alone was indistinguishable from the clean game and that merged v0.8.0 introduced the diagnosed CPU-side regression. Because those secondary controls measured earlier binaries, they are retained only for diagnosis. The final release decision uses the exact `9038435` production D versus Harmony-only B matrix above. Frozen reports and hashes are recorded in `PERFORMANCE.md` and `TESTING.md`.

---

# Ultimate Duckov Statistics v0.8.0 — published pre-release

M8 keeps a continuous expedition as one run across proven Duckov full-scene and subscene transitions. It adds explicit starting/ending maps, ordered repeated-map-aware segments, separate transition displacement, event-time M1-M7 segment attribution, schema 8, Runs route presentation, and four joinable route CSVs. Automated validation and the single-map, multi-map, repeated-map re-entry, later-map death, cross-map M1-M7, abrupt recovery, UI, export, shutdown, and review-hardening gates pass. M8 is merged into `main`; GitHub pre-release `v0.8.0` was published on 2026-08-13. No Steam Workshop upload has occurred.

The published installable asset is `UltimateDuckovStatistics-v0.8.0.zip` (209,384 bytes, SHA-256 `0e0f8e8cbcd41d6097348627f411e0b28793672ec99db8991c481f8862cf1419`) with its 102-byte checksum sidecar. Active M8.1 does not modify that tag or release.

## Included in v0.8.0

- Public-hook route lifecycle using `SceneLoader`, `MultiSceneCore`, `LevelManager`, exact main-duck `SetPosition`, and restored `InputManager.InputActived` evidence.
- Stable visited-map identity from `MultiSceneCore.ActiveSubSceneID` when present, otherwise the proven full-scene ID; no localized-name identity, object ID, scene scan, or timing guess.
- Segment-local M1-M7 aggregates plus preserved source/outcome association for delayed healing and combat.
- Physical, proven teleport, and transition/loading-excluded movement as separate categories whose overall values compose from segment totals.
- Separate historical starting-map complete-run totals/records and new segment-derived route-map totals.
- Schema-8 migration that preserves prior data and marks route history unavailable without fabricating segments; incomplete current-schema profiles and checkpoints are rejected before atomic selection so an intact backup can win.
- Successful high-rate firing mutations are checkpointed through a bounded one-second scheduler instead of synchronously cloning and durably writing the growing route from every firing callback.
- Accepted item uses publish independently to lifetime and active-run destinations, so a failed lifetime save cannot silently omit the run, segment, association, or route-map contribution.
- `routes.csv`, `segments.csv`, `segment_events.csv`, and `route_map_totals.csv`; existing map scopes are explicitly named `starting_map`.
- Defensive bounds of 64 visits and 2,048 association rows with route-only capability degradation.
- Delayed outcomes with no proven active destination segment preserve overall statistics and truthfully disable only event attribution and route-map totals.

## Acceptance status

The single-map, two-map extraction, seven-segment Nullpunkt → Lagerbereich → Keller → Lagerbereich → Keller → Lagerbereich → Farmstadt expedition, repeated-map re-entry, later-map death, representative cross-map M1-M7 activity, abrupt interruption/recovery, UI, persistence, export, clean shutdown, and deployment-readback gates pass and are recorded in `TESTING.md`. Post-correction controls with the Vektor SMG, Electrified MP7, and MF assault rifle confirm a broader firing-related frame-time problem rather than a weapon-specific Vektor issue. Frame loss occurs while firing into empty space and worsens when hits add combat activity. The earlier per-shot checkpoint defect was real and is corrected, but the live result does not yet prove the remaining UDS contribution; active M8.1 owns a game-only versus UDS-enabled baseline followed by profiling and hardening across the cumulative M1-M8 firing, projectile, combat, attribution, and persistence paths because earlier milestones did not run an equivalent controlled sustained-fire stress case. No safe reproducible delayed cross-segment healing/damage case was identified; the source/outcome implementation is covered by production-path regressions and remains truthfully capability-gated during loading.

---

## Published v0.7.0 history

M7 unique-container statistics were merged and published as the GitHub `v0.7.0` pre-release on 2026-08-12. M0-M7 are therefore published through v0.7.0. No Steam Workshop upload has occurred.

## Included in v0.7.0

- One count per unique normal non-corpse container whose loot access successfully begins for the exact main duck during an active raid.
- Per-run deduplication by the verified private `InteractableLootbox.GetKey()` result. Closing and reopening the same container does not increment; a later run receives a fresh deduplication scope.
- Lifetime, per-map, and compact per-run aggregation with saturating non-negative counters.
- A bounded 4,096-key active-run checkpoint. Hitting the defensive bound disables the run metric rather than evicting identities and risking double-counting.
- Crash/interruption recovery that carries both the accepted total and exact stable-key set into the interrupted run summary.
- Overview and Runs presentation, full schema-7 JSON, container columns in `runs.csv`, `run_totals.csv`, and `map_totals.csv`, plus the flattened `containers.csv`.
- Schema-7 migration preserving M1-M6 and marking all pre-M7 history explicitly unavailable.
- Visible repair provenance for malformed container aggregates and semantic rejection/backup recovery for inconsistent current-schema active-run container checkpoints.

## Native contract and truth boundary

M7 is verified against Duckov 2.3.30:

- Public `InteractableLootbox.OnStartLoot : Action<InteractableLootbox>` is invoked by `StartLoot()` only after the interaction timer has completed and `Inventory` is non-null. Proximity, interaction start, locked/missing requirements, cancellation, and failed `StartLoot` do not reach this event.
- The event-time protected `InteractableBase.interactCharacter` must reference the exact `CharacterMainControl.Main` and report `IsMainCharacter`.
- Private `InteractableLootbox.GetKey() : int` is the canonical position-derived identity already used by Duckov's lootbox inventory cache. UDS invokes only this version-probed method; it never persists a Unity runtime object ID.
- Native enemy death lootboxes are marked through a narrowly owned prefix/finalizer on private `CharacterMainControl.OnDead(DamageInfo)` plus a postfix on `InteractableLootbox.CreateFromItem(...)`. Persisted and player tombs are recognized from Duckov's exact `LootBoxPrefab_Tomb` argument.
- The corpse-provenance patches use their own Harmony owner. Callbacks become inert before exact-owner unpatching; cleanup remains retryable and blocks unsafe replacement.
- Any missing/changed member, unsafe foreign patch, runtime stable-key failure, or patch-set drift marks the capability `DisabledIncompatible` and stops recording approximate numbers.

“Container looted” means successful access to the loot interface. M7 does not claim that an item was removed and does not count item quantity or value.

## Compatibility and migration

- Verified baseline: Duckov `2.3.30`, Steam build `24013657`, Unity `2022.3.62f2`, HarmonyLib `2.4.1.0`.
- Schema-6 and older profiles migrate to schema 7 without reconstructing container history.
- Existing M1-M6 aggregates, run summaries, capabilities, checkpoints, exports, and generation identity remain intact.
- Missing or invalid container roots are repaired with provenance. A current-schema active checkpoint whose unique total and stable-key set disagree is rejected so atomic backup recovery can choose a valid candidate.

## Known limitations

- Statistics begin when their milestone is installed; pre-v0.7.0 container history is unavailable, not a genuine zero.
- The native stable key is position-derived. Unknown or modded non-corpse lootboxes work when they use the verified class and stable-key contract; incompatible implementations remain uncounted and disable the capability if they reach the successful-access event without usable identity.
- Native corpse classification covers installed Duckov enemy death paths, persisted dead bodies, and player tombs. Third-party corpse systems that bypass all verified native death/create contracts are outside the proven capability.
- A sudden process or OS failure can lose up to approximately one second of newly accepted container/combat/equipment state because mutation checkpoints are intentionally coalesced. Orderly shutdown and run completion flush state.
- Version 0.7.0 stores one stable root/starting map per run. Multi-map expeditions are not yet represented as ordered routes, and complete run totals remain grouped under that stored map. Planned M8 adds route segments and event-time per-map attribution without reconstructing historical routes.
- UDS remains a local GitHub package. Only HarmonyLib is supplied through Steam Workshop.

## Acceptance state

Focused M7 regressions, the complete Release suite, native contract probe, warning-free native build, formatting, source safety, package/extraction audit, deployment readback, and the user-controlled gameplay matrix are recorded in `TESTING.md`. PR #7 merged as commit `172125abd59d2398d18b00e8512dc69acbab8f63`; the published pre-release tag points to that merge commit. The validated ZIP and SHA-256 sidecar are attached to the GitHub release.
