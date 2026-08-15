# Ultimate Duckov Statistics v0.8.1 — performance-hardening candidate

M8.1 retains every M1-M8 statistic, attribution, capability, persistence, recovery, and export contract while hardening five measured CPU hot paths. Controlled raw-frame captures show Harmony alone is indistinguishable from the clean game, but production v0.8.0 fails the matched Harmony-only ceiling at idle p99 and during a 30-round Electrified MP7 empty-space firing window. A later three-map soak also exposes a separate synchronous lifetime-profile persistence hitch on consumable completion. Both regressions are CPU-side rather than GPU-side.

## Included in v0.8.1

- Event-driven immutable equipment snapshots for firing/projectile/health associations instead of rebuilding, sorting, traversing, and hashing the full attachment/totem tree for each request. A one-second reconciliation watchdog remains, and unchanged snapshots are not republished.
- Full activation-time Harmony ownership validation bound to the exact shared-state token by a capture-before/validate/recheck sequence, followed by allocation-free identity-stamp checks for every healing, combat, and container patch point. Runtime checks distribute bounded dictionary lookups across the existing two-second integrity window, while healing callbacks check the same stamp immediately; any patch/unpatch replacement conservatively disables the dependent capability without runtime metadata deserialization or background reflection.
- Once-per-Unity-frame projectile context reconciliation with exact capture/outcome-time run checks, direct-reference ambient scope unwinding, and reuse of the application-time equipment association already carried by a combat scope.
- Single-flight deferred active-run validation, JSON serialization, durable flush, and atomic replacement. The exact immutable checkpoint is still cloned on the main thread at the existing one-second dirty/five-second periodic cadence; transitions, completion, profile changes, export, shutdown, and retry synchronously drain the pending write.
- A mutation revision guard so completion of an older snapshot cannot clear newer dirty state, plus deterministic background failure/retry and non-consuming-wait coverage.
- Frame-coalesced immutable lifetime-profile snapshots for item-use and healing mutations, with single-flight background serialization and replacement, checkpoint-before-profile ordering, boundary drains, bounded retry, and an exact persisted recovery watermark for profile/checkpoint deltas after abrupt interruption.
- A frozen CapFrameX/PresentMon protocol, independent raw-frame analyzer, and compile-time diagnostic counters whose call sites are absent from ordinary production builds.

## Candidate status

The complete Release suite passes 505/505. The installed Duckov 2.3.30 / Steam 24013657 / Unity 2022.3.62f2 / Harmony 2.4.1 contract probe, frame-time analyzer build, warning-free native Release build, exact-five-file package validation, changed-source formatting, and ordinary-build zero-diagnostic-call proof pass. The first configuration-D candidate passed idle but failed the matched high-rate empty-space p99 ceiling. Candidates 2-4 removed frame-thread reflection, replaced periodic patch metadata inspection with shared-state identity stamps, bound every stamp to the exact metadata snapshot validated at activation, and passed the firing matrix. The three-map soak then exposed the separate lifetime-profile write path. Candidates 5-7 added frame-coalesced item/healing persistence, exact interrupted-run watermark recovery, profile-lifecycle following, checkpoint-before-profile ordering, delayed completed-run healing drains, strict malformed-aggregate recovery, exact same-frame projectile-boundary checks, and healing-group composition preservation. Candidate 7's matched enabled Med-Kit cell and clean shutdown pass with exact activation/healing persistence and no perceived completion hitch. The final pre-soak audit then found that unchanged equipment snapshots were suppressed across a new segment. Candidate 8 keys that cache by segment plus snapshot identity and republishes the unchanged loadout once after transition. Its immutable commit `fb61cd86c4a31ed786f50e87bb8d6c90bba5e94d` has Core/native SHA-256 values `5506145a45ebc0b7acd98cd73538da4f0e48d80f22d0eddd573084586f313f12` / `cf2e4f331d67865c1710d3a5ba0d977acfe277e184d91e30b3cd0e0781533823`, exact transactional deployment/readback, retained byte-identical UDS/save backups, and no staging residue.

The corrected package now has a complete frozen three-run D/B firing matrix. Whole-capture median/p99 overhead is +0.568%/+7.612% for idle, +0.674%/+11.136% for MP7 empty-space firing, -1.259%/+2.065% for MP7 single-enemy firing, +0.839%/+6.792% for MF empty-space firing, -7.514%/-3.775% for MF single-enemy firing, and +0.924%/+7.388% for the slower Mosin control. The corresponding action-window median/p99 overhead is +2.217%/+12.506%, -5.061%/+5.747%, +0.945%/+3.102%, -11.768%/-1.611%, and +1.057%/+8.816%. No accepted cell introduces a repeatable severe cluster. Every cell passes the predeclared 10% median/20% p99 hard ceiling; all except MP7 empty-space firing meet the tighter 5% median/10% p99 engineering target. Exact checkpoints retain every firing action, completed projectile, hit, damage outcome, kill, identity, equipment association, and source/outcome segment. Favorable and unfavorable invalid attempts remain archived but excluded for predeclared gameplay reasons, and the unreproducible multi-target cell remains explicitly unavailable rather than silently disappearing. The supplementary extracted three-map stress has 4.571 ms median/9.040 ms p99 over 139,455 raid frames and improving early/middle/late p99, but correlates all eight Med-Kit completions with 126.12-735.90 ms CPU-side stalls. The matched candidate-7 Med-Kit cell measures 4.432/5.512 ms aggregate whole-record median/p99 and 4.464/5.671 ms in the action window: +5.498%/+9.677% and +5.868%/+11.191% over Harmony-only B, within every hard ceiling and without a repeatable new cluster. Exact completion-window peaks fall to 6.157-17.516 ms, all three uses persist once with their exact 12 HP heal, and none is perceived. Isolated delayed CPU stalls occur in 2/3 B and 2/3 D captures; D is larger, but the shared delayed pattern, lack of a cluster, and absent perception leave causality unresolved. Candidate 8's first natural-play soak is smooth through Nullpunkt and Lagerbereich, retains complete equipment roots in both segments, has completion-aligned maxima below 15 ms, and shows no late-run degradation. Its UI, nineteen CSVs, JSON, profile, diagnostics, and clean-shutdown state agree exactly. The retry completes Nullpunkt → Lagerbereich → Farmstadt with 4.470/10.532 ms raid median/p99 and only +1.059% late-versus-early p99 movement, retains complete UP-45/StG equipment roots and 891 unique associations across all three segments, and persists three Med-Kits plus food and hydration exactly once. The user perceived no UDS-related hitch; the only noticed stall was the game's known map-load hitch after entering Farmstadt. Codex-side repository writes overlapped this natural capture, so isolated raw maxima are disclosed but not treated as causal UDS evidence; the clean matched matrix supplies that gate. The UI and 20-file export then agree exactly with the live profile and backup, including all three segments, 891 unique event IDs, and the complete item/equipment projection. Normal shutdown consumes the pending-save marker, removes session/checkpoint/temp residue, retains the run once, and leaves byte-identical primary/backup profiles with Info-only diagnostics and one teardown sequence. The performance result is **Acceptable for v0.8.1**. Immutable-head rebuild, final deployment readback, and draft-PR delivery remain release gates. This candidate is not published yet.

The secondary controls support the same attribution. Harmony-only B versus clean A is -0.315%/-2.557% whole-capture median/p99 and -1.555%/-5.422% in the MP7 empty-space action window. Corrected D versus A is +0.358%/+8.294% whole and +0.628%/+6.406% in that window. Corrected D versus merged v0.8.0 C is +0.039%/-18.145% whole and -0.491%/-35.045% in the window, with the material-frame rate also improving. None introduces a repeatable cluster. The frozen reports and hashes are recorded in `PERFORMANCE.md` and `TESTING.md`.

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
