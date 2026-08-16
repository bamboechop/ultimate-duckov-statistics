# Ultimate Duckov Statistics v0.9.0 — Economy candidate

M9 adds exact, separately capability-gated Money and physical-Cash statistics without changing the accepted M1-M8.1 semantics. This remains an unpublished draft candidate. The earlier 2,048-event economy-identity implementation and the later sequence-zero deployment are superseded as final candidates. The latest review correction has passed the complete local build/package pipeline and awaits fresh exact-hash deployment approval and cold readback.

## Included in v0.9.0

- Typed schema-9 `CurrencyFlowRecorded` events with stable event/save/run/segment/map identity, positive amount, explicit direction, currency, source, context, integrity, adapter provenance, producer activation, and monotonic positive producer sequence. Directly recording aggregates use one constant-size closed-through replay cursor; a newly registered activation is valid at sequence zero before its first event. Exact Money/Cash capture therefore has no fixed event-count stop. Completed-run aggregation is guarded by run identity and exact totals rather than transaction IDs.
- Separate Money and Cash gross inflow/outflow, derived net flow, source/context breakdown, lifetime, run, segment, starting-map, and route-map aggregation.
- Public `EconomyManager.OnMoneyChanged` exact deltas. Completed matching StockShop sales and Money quest rewards are semantic; purchases, crafting, fees, conversion, and unmatched changes remain `UnknownAdjustment`.
- Physical Cash item `451` owned-total observation across storage, main inventory, and pet inventory, with runtime-identity deduplication, dirty-delta drain before full-scene hydration suspension, load/carried-in baseline exclusion, internal-movement/stack-change neutrality, and bounded drop/re-pickup identity plus last-owned-amount exclusion. Production `AddAndMerge` may consume the picked item into a compatible stack; the retained exact amount keeps that re-pickup neutral. Exact-main external world pickups may prove acquisition; the tested corpse/container transfer path does not emit that callback and therefore remains an exact `UnknownAdjustment`, not invented loot attribution.
- Truthful terminal policy: acquired raid Cash is retained, while secured/lost is deliberately unavailable and the final disposition becomes unresolved because public Duckov 2.3.30 evidence cannot prove fungible acquired units across terminal inventory mutation.
- Independent economy capabilities, Diagnostics rows, Economy tab, compact Overview/Runs evidence, complete JSON, and `economy_totals.csv`, `economy_sources.csv`, `economy_contexts.csv`, and `cash_raid_outcomes.csv`.
- Schema-8 migration that preserves all prior statistics and marks pre-M9 economy unavailable, current-schema root validation and backup selection, exact-once deferred lifetime recovery including saturation-only checkpoint degradation, stale-checkpoint protection, saturating counters, composition repair, and independent profile/run publication.
- Event-driven and bounded performance design: no per-frame wallet/inventory scan, no per-event durable profile serialization, constant-size economy replay metadata, bounded pending evidence, coalesced generic Cash scans, and unchanged firing/combat adapter paths.
- Truthful arithmetic limits: an unrepresentable next flow is rejected, the prior exact total is retained, only the affected currency becomes unavailable, and the state survives deferred persistence/recovery. Unsafe negative counters, overlapping raid outcomes, duplicate legacy identities, and malformed replay cursors lose atomic primary selection to an intact backup or temporary candidate; repairable source/context keys still normalize explicitly to unknown. Legacy schema-9 identity lists remain recovery-only evidence and compact only after no surviving checkpoint can replay them; old saturation is preserved as explicit incomplete-history provenance while new capture resumes safely.

## Candidate status

The first scalable-idempotency correction passed the complete 605-test Release suite, 100,000-event stress, installed-game probe, warning-free builds, exact package/extraction audit, and approved byte-exact deployment as implementation commit `ea49808897c018bfbe437859475acb2c6240fb37` / ZIP SHA-256 `2ef0899013319b91bd8771b18e805fa780c82300726f7ba34a495d4a90b4b1b7`. Its cold base-only activation exposed that a newly registered activation at closed-through sequence zero was incorrectly normalized as malformed during the first deferred clone, clearing the activation and marking repair before any transaction. Totals and prior evidence remained exact, and normal shutdown was clean, but that package and deployment are superseded. Implementation commit `0b223e45bbafba8cbbe7f5d2213cd09588b5b870` corrected sequence zero and passed its 606/606 suite, approved deployment, exact readback, and minimal cold persistence check. A later review found three remaining exactness defects and therefore superseded that artifact as the final candidate: merged dropped-Cash re-pickups could be fabricated as acquisitions, a dirty Cash delta could be discarded at level initialization, and an overflow-only newer checkpoint could lose lifetime capability degradation. Correction commit `20fa8acdc873e331515d9155ed838b96cb820b7a` fixes all three with production-order regressions. Debug and Release pass 609/609; the installed Duckov 2.3.30 contract probe, frame-time analyzer, formatter, source-safety check, warning-free Core/native builds, package verifier, and independent extraction pass. The replacement ZIP is 253,758 bytes at SHA-256 `6157fa88e242686f10b9c6a3db63de2b10c7d51cbec807cb2d1f74fbc221cb15`; its 102-byte sidecar is SHA-256 `9278770b9b403b8b1e914de08664dfb00148be7ba5cbc98db401fe53532c3365`. The extracted five-file payload at `artifacts/audit-v090-findings-20fa8ac-20260816T1907266215299Z` matches the package directory byte-for-byte; Core/native hashes are `32de76329eef7b349b4b64dded86880037b30d45fdd65dae65d24737a837a24c` / `15dcc811b8c2c0719070373137cad26d241d8d2c82c8a3dc625fe4f538674c46`, and both report product version `0.9.0+20fa8acdc873e331515d9155ed838b96cb820b7a`. This replacement is not deployed; fresh approval and cold readback remain required. The paragraph below records the still-earlier 2,048-hard-stop candidate and is retained only as historical evidence.

The earlier pre-scalability candidate Release suite passed 589/589. The installed Duckov 2.3.30 / Steam 24013657 / Unity 2022.3.62f2 / Harmony 2.4.1 contract probe included the M9 public economy hooks and the level-initialization boundary. Core and native Release builds completed with zero warnings and errors, and the package root validated as exactly five permitted files. The first live matrix proved zero-flow load, unaffordable-purchase exclusion, a Money `Sale +179`, an unknown Money purchase `-1`, corpse-looted Cash `+37`, split/merge neutrality, drop/re-pickup `-37/+37`, JSON plus twenty-three CSVs, and residue-free clean shutdown. It also exposed a Diagnostics full-tab scrolling defect and a false extraction-time Base inflow. Full-tab scrolling passed the second retest, but the lifetime result `Base +81, Raid +7` proved the remaining defect was scene hydration: carried Cash 37 was observed while entering the raid, then returned Cash 44 was observed after extraction. The run itself remained exactly Raid `+7`. Adapter contract `native-economy/2.3.30+public-events-v2` suppresses owned-Cash scans during full-scene hydration and establishes one baseline after `OnAfterLevelInitialized`. Its focused live retest passed: carried Cash 44 produced no flow, newly corpse-looted Cash 24 was the only exact Raid inflow in lifetime and every run/map projection, and clean shutdown left byte-identical profiles with no pending state. A subsequent completion audit found that base/shop flows still bypassed the M8.1 deferred writer and synchronously replaced the profile once per transaction. The source corrected that boundary and proved a base Money mutation leaves the persisted profile byte-identical until the coalesced snapshot writer runs, while active-run watermark recovery remains exact once. The focused live smoke then proved ATM Money `178 → 177` for one Geheime Nachricht, unchanged physical Cash 68, exact UDS Base outflow 1, JSON/twenty-three-CSV agreement, deferred durability through clean shutdown, and no pending or deployment residue. A later source audit also corrected aggregate-to-aggregate overflow so the prior exact currency and Cash-outcome values are retained instead of clamped, while an independently representable currency still merges; three regressions cover those recovery boundaries. Superseded implementation commit `41721ec47944393d10d1ecae279dea1224f8e4fc` produced `UltimateDuckovStatistics-v0.9.0.zip` at 252,244 bytes and SHA-256 `84e2d286e6e2d7f5816d1137ed26cd2bba1fd57c90ccd5ba2bf24e31a877355f`; its historical sidecar, independent extraction, exact five-file deployment/readback, cold activation, and residue-free clean shutdown passed. [Draft PR #10](https://github.com/bamboechop/ultimate-duckov-statistics/pull/10) is open and unmerged; its live final-head CI status is reported on the PR. Progressed-save recovery was not required. No v0.9.0 tag, release, merge, PR-ready transition, or Workshop upload has occurred.

M8.1 PR #9 is merged into `main`, but live tag/release verification found no published v0.8.1 release. The published baseline therefore remains v0.8.0.

---

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

The earlier candidate-8 three-map soak remains supplementary natural-play, route, projection, and elapsed-run evidence; it does not substitute for the final causal matrix. After the final campaign, the user verified Runs, Items, Equipment, clean Diagnostics, JSON, and all nineteen CSVs. Normal shutdown leaves no active/session/checkpoint/temp state, null pending persistence, byte-identical primary/backup profiles, 33 supported plus four deliberately unavailable capability rows, and zero UDS error-like log lines. The performance result is **Target achieved**. The exact 590,272-byte five-file package produces `UltimateDuckovStatistics-v0.8.1.zip` at 221,763 bytes and SHA-256 `2510317d1aca11a19ab658941b513fa630d6b70f2a6d8065c77b57a744cdeb62`; its 102-byte checksum sidecar, independent extraction, deployed-byte readback, cold-start activation, final matrix, export, and shutdown gates pass. [PR #9](https://github.com/bamboechop/ultimate-duckov-statistics/pull/9) later merged into `main`; the v0.8.1 candidate remains untagged, unpublished, and absent from Steam Workshop.

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
