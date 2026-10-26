# Record by default

UDS records a modded personal playthrough. Foreign Harmony patches are diagnostic context, not evidence that recording is impossible. Recording still depends on installed UDS callbacks, available native contracts, and the evidence required by each event. Observing a health delta does not establish a weapon, headshot, or player ownership by itself.

## Decisions

- Separate UDS callback integrity from foreign patch presence. Validate exact required UDS callbacks, including duplicate and unexpected UDS callbacks, independently of unfamiliar owners and patch categories.
- Keep the existing strict policy for adapters until their event paths have been audited. Introduce an explicit observation policy per patcher; do not change all callers through a global default.
- Revalidate changed patch-state tokens. A foreign patch addition alone does not invalidate observations. Missing UDS callbacks suspend dependent metrics; restoring the callbacks can resume new observations. Cleared correlations and historical gaps are not reconstructed.
- Preserve binary verification for integrations that interpret private mod state, such as FPC headshot mode and Become Veteran delayed healing. Native recording must not depend on recognizing every unrelated mod.
- Preserve save generation, raid, player identity, deduplication, finite-value checks, actual observed deltas, and scope cleanup. Unknown ownership stays unknown; unknown equipment does not become the currently held gun.
- Do not rewrite historical profiles, expand surrogate repairs, commit, publish, or install this broader policy as part of the initial implementation batch.

## Gate inventory

| Surface | Current mechanism and dependency | Implementation batch |
| --- | --- | --- |
| Shared Harmony inspection | `HarmonyPatchSetInspector` validates owned callbacks and formerly rejects all unverified foreign callbacks. `ReflectiveHarmonyPatcher` caches a shared-state token. | Add explicit `Observe` policy, informational foreign-owner summary, and conditional token revalidation. Retain strict default for other callers. |
| Combat | Twelve native hooks, independent public melee/death events, incremental stamp scheduler. Hooks produce HP deltas, projectile completion, source scopes, grenade/barrel provenance, and encounter observations. | Opt into observation policy. Preserve existing per-event ownership and health-delta checks. Separate harmless patch additions from actual callback loss; restore only future recording. |
| Shared buffs | `CharacterBuffManager.AddBuff` feeds combat ownership and healing correlation. Previously any token change invalidated the shared boundary permanently. | Opt into observation policy. Preserve ownership conflict handling; clear bindings on real callback loss, then recover new observations when restored. |
| Healing | Health/item/buff hooks and a specialized Become Veteran integration. | Observation policy enabled. Native item credit requires measured HP increase and successful use correlation. Unverified BV internals limit private queued attribution and report partial coverage while native item observation remains active. |
| Containers and encounter loot | Successful native access, corpse provenance, and reconciled directed inventory quantities provide independent evidence. | Observation policy enabled. Foreign-patch coexistence and real callback-loss tests retain corpse exclusion and actual transfers. |
| Crafting and payments | Correlated craft/delivery tasks and before/after player-owned items plus Money across a unique successful payment. | Observation policy enabled. Changed prices and inputs use measured payment loss. Failed, missing, nested, or unreadable payment retains delivered output with explicit unknown costs. Empty or wrong-item delivery buffers produce no invented craft. |
| Throwables | Successful player skill release plus native release evidence. | Observation policy enabled. Preserve successful release, skipped-original rejection, item-use lifetime, exception propagation, and exactly-once completion. Validated stamps replace repeated full metadata inspection. |
| World time and sleep | Clock observation has independent data; completed sleep uses `GameClock.Step` plus sleep completion. | Observation policy enabled. Source-linked adapter tests cover foreign patches, paired actual advancement, duplicate completion, and an unpaired completion producing no invented time/session. Contradictory advancement still cannot become sleep time. |
| Run lifecycle, movement, equipment, weapon firing and economy | Public events, native members, ownership and lifecycle checks remain separate from the shared Harmony gate. | Inventory indirect dependencies and broad capability invalidation; do not remove missing-contract or event-identity checks merely because they resemble mod guards. |
| Records | Modded content is already eligible under the preceding release fixes. | Keep tags and existing cheat/interrupted/unknown-integrity distinctions; no automatic historical requalification. |
| Diagnostics and persistence | Current capability state, historical aggregate coverage, and per-event attribution represent different facts. | Keep foreign presence informational. Audit that diagnostics and exports distinguish current operation from historical missing coverage; use existing formats where they can represent the distinction. |

## First implementation batch

The shared inspector exposes an opt-in `HarmonyForeignPatchPolicy.Observe`. Combat and shared buff observation select it. That first batch left other patchers on `RequireVerified`; subsequent conversions are listed in the gate inventory. Foreign owner and callback-category information appears in bounded diagnostic summaries without claiming that arbitrary mod semantics have been verified.

Validated stamps retain the expected UDS callbacks. Unchanged tokens retain the cheap comparison path. A changed token triggers a bounded snapshot-validation-current-token sequence; concurrent changes during inspection are rejected until a stable assessment is possible. Missing or duplicate owned callbacks still fail validation. Logging failure cannot stop observation.

Combat continues its incremental scheduled checks. Callback loss clears dependent correlations and suspends the affected metrics; restoration resumes future observations. Shared buff callback loss clears ownership bindings, and successful revalidation can restore the boundary. Recovery must not upgrade historical coverage or reuse correlations captured before an observation gap.

This batch does not claim to infer arbitrary replacement-method semantics. A foreign callback can modify the world before or after an observer, suppress an original, or replace attribution in ways a metadata snapshot cannot prove. The remaining adapter audits and production event-boundary tests are required before the entire broad policy is release-ready.

## Validation

The core inspector tests cover all four foreign callback categories, missing callbacks, and duplicate owned callbacks. Adapter regressions cover foreign patches before activation and at runtime, actual damage and completed-projectile counting, genuine callback loss, and recovery without resurrecting stale projectile bindings. Existing degradation fixtures now simulate actual UDS callback removal rather than treating a harmless foreign prefix as a failure.

`tools/HarmonyObservationProbe` source-links the production patcher and inspector and injects real Harmony callbacks into isolated fixture methods. It covers changed damage arguments, an unrelated postfix, a pass-through transpiler, a finalizer, skipped original execution, exception propagation, runtime patch additions, callback removal/restoration, and the stable fast path.

Run the isolated probe with `dotnet run --project tools/HarmonyObservationProbe/HarmonyObservationProbe.csproj -c Release`. It pins Harmony 2.4.1 for .NET 8. The installed Unity/Mono binary cannot initialize in this host runtime because its embedded MonoMod code accesses a runtime-internal signature helper. The .NET probe therefore validates real patch execution with the same Harmony version, not Unity-native execution. Native acceptance still requires startup diagnostics and user-controlled gameplay with the installed mod set.

After the first batch, the remaining gates were the adapter audits, diagnostic reconciliation, full qualification and native acceptance. Later batches below complete the implementation and reconciliation. Preserve the working-tree checkpoints under `artifacts/record-by-default/checkpoint/` and `artifacts/record-by-default/crafting-checkpoint/`.

On 2026-10-25, the first batch passed 2,595 main tests and 219 shell tests in each of Debug and Release, the real Harmony observation probe, the installed Duckov contract probe, and package validation. Its build transcript is `artifacts/record-by-default/build.log`. It has not been installed or accepted in native gameplay.

## Second implementation batch

Native health completion now compares final source arguments with the prefix observation. A changed actor or a replaced original cannot retain the enclosing projectile's player credit. Conflicting weapon identity loses gun/ammunition/headshot evidence while measured loss remains available. A public player-death event also rejects conflicting weapon identity. Encounter history uses the same corrected source decision; uncertainty in one event does not itself create a run-wide coverage gap.

Adapter coverage now distinguishes a partly available source family from a missing health observer. Proven individual player health outcomes carry supported event evidence so the feed can display confirmed kills; aggregate capability merging still preserves partial or missing historical coverage. Observed enemy deaths do not require resolving their killer.

The observation policy is enabled for native healing, throwables, containers, encounter combat/loot/map observation, and clock/sleep tracking. The encounter setter, inventory-transfer, and map-placement observers were additional gates found during the wider audit. Foreign-patch diagnostic summaries are deduplicated per target and remain informational.

Crafting was left for the third batch because ordinary input and currency rows partly relied on declared-cost assumptions. The completed audit and implementation are described below.

Diagnostics and export regression tests preserve the difference between current partial capability and historical unsupported scopes. Final crafting and diagnostic reconciliation follow below. Native gameplay acceptance remains separate; no saved profile is rewritten by this policy.

On 2026-10-25, this batch passed 2,613 main tests and 219 shell tests in each of Debug and Release, the real Harmony observation probe, installed native contracts, and package validation. The final transcript is `artifacts/record-by-default/second-batch-final-build.log`. At that batch boundary, the installed mod DLL had SHA-256 `373e8a2e95ce4ecbec1ff0179c7c39baab3eaf47cf237b975515f44dcb5c31e2`, matching the user-confirmed blast-icon deployment.

## Crafting and final reconciliation

Crafting now selects observation policy. Its former affordability, stack-setter, and destruction hooks were removed. A uniquely correlated successful payment captures Money and recursively owned item quantities before and after the synchronous call. Snapshot values, rather than recipe prices, determine item resources and total currency charged. Item enumeration deduplicates references and includes nested attachments and inventories, so returning or merging an already owned child cannot create a resource cost. Payment snapshots are bounded at 32,768 item identities and occur twice per payment, without per-frame inventory scans.

A successful payment with no net loss is a proven free craft. Missing, failed, nested, repeated, or unreadable payment observations retain a delivered output with explicit unknown costs. Cash used both as a declared item resource and possible currency cannot be allocated twice; an ambiguous allocation leaves both cost dimensions unknown. Existing publication, save-generation handoff, retry, overflow and recovery boundaries remain intact. Uncertain attempts that never deliver do not degrade saved history.

A completed Return task must expose a nonempty generated buffer with the expected output identity. Native Return retains managed item references even when a stack fully merges and its Unity object is destroyed, so this check uses managed reference identity and the cached TypeID. Empty or mismatched delivery does not count a craft and does not prevent later crafts. Produced units still describe the captured formula result amount; arbitrary rewritten output-generation semantics are not inferred from the remaining stack size.

Crafting presentation now separates current adapter capabilities from lifetime history restrictions. Positive recorded values remain visible with a historical-gap notice; that notice does not claim the current adapter is disabled. Diagnostics uses current capability records; exports retain those records separately from aggregate history flags. Native panel shortcut suppression keeps strict patch verification because it actively changes input behavior rather than observing statistics.

Focused validation covers discounts, free replacements, changed item inputs, Money plus physical Cash, missing/failed/nested/unreadable payment, later recovery, native repeated-resource underconsumption, returned children, output no-ops, merged output objects, cold reopen and export. The final full-build transcript is `artifacts/record-by-default/final-build-verified.log`. Deployment and native gameplay acceptance must be recorded separately after they occur. No historical profile repair is part of this policy change.

## Final qualification and local deployment

On 2026-10-25, the completed implementation passed 2,625 main tests and 219 shell tests in each of Debug and Release, the real Harmony injection probe, installed Duckov contract probe, native Release compilation, ordinary-artifact audits and 13-file package validation. The successful transcript is `artifacts/record-by-default/final-build-verified.log`. An earlier full attempt exposed a native-target record-type compilation issue; the corrected immutable snapshot class passed the complete rerun.

The qualified candidate was installed with Duckov closed. Independent readback matched all 13 installed files to the package. The main DLL SHA-256 is `86729c58ed98f6181b2a3f8f1dbffc01927d27094224d3162b05b738dbe2d431`. The previous installation is retained under `artifacts/deployment-backups/9010a1fe4d2045dbb42742e795f953e2/UltimateDuckovStatistics/`. All 132 files in the UDS profile tree matched their pre-deployment hashes, including slot 6. Evidence is in `artifacts/record-by-default/deployment.log`, `deployment-readback.json` and `profiles-before-deploy.json`. No commit or publication was made.

Native gameplay acceptance is pending. During the next normal slot-6 session, check current Diagnostics, combat/feed attribution, an ordinary heal or throwable use, container access, and crafting when those activities naturally occur. For crafting, compare actual before/after owned resources and Money plus Cash with the recorded changes, then export after returning to base. A historical gap must remain distinguishable from a current observer failure. Startup diagnostics and the user's gameplay/export are required before claiming native acceptance; the automated .NET Harmony probe does not establish Unity gameplay behavior.
## Slot 4 native combat check

On 2026-10-25, the user completed a slot-4 Ground Zero / Nullpunkt run with one reported player kill. A read-only SQLite backup captured run `f5b36e17d7a145cab135ff97025e9141`; integrity and every stored record checksum passed. The run records a successful extraction, 393.075 seconds active duration, one TT-33 player kill, 14 damage dealt, two headshots including the final blow, and six unique containers. All run combat capabilities are Supported, the kill encounter has no gaps, and HistoricalEventAttributionIncomplete is false. Startup logs confirm all 12 combat hooks active alongside FPC's foreign callbacks and corpse-loot exclusion while ordinary container accesses continue.

The map visit separately records one route discontinuity near 4.9 seconds. No encounter capture-failure record is present; the compact data does not retain its original reason, so this check does not assert uninterrupted map sampling. No item healing or crafting was exercised. Native acceptance is therefore partial: this run exercises combat, extraction, container and corpse-loot observation, while crafting/payment and healing/throwable behavior still need live exercise. The snapshot and asserted receipt are under `artifacts/record-by-default/slot4-native-acceptance/`, with `acceptance-report.json` as the concise result. Live profile data was opened read-only and was not edited.

### Subsequent slot 4 crafting observation

The user crafted while the first native check was being inspected. A second read-only snapshot shows crafting advancing from zero to two completed actions and two produced Metzger-Messer (Butcher's Knives), output item 305, recipe 1001. Recorded payment consumed four Metallplatte (Metal Plates), item 362, with zero currency charged. Output, recipe, batch, resource and association rows agree; all eight current crafting capabilities are Supported, and neither resource nor currency history is marked unavailable. Integrity and stored record checksums passed. Evidence is under `artifacts/record-by-default/slot4-crafting-acceptance/` with a separate `acceptance-report.json`; the earlier combat snapshot was preserved. This validates the exercised item-cost, zero-currency crafting path and persisted consistency. A separately captured live inventory before/after comparison was not available. Positive currency payment, healing and throwables remain unexercised by these observations and can be checked during later normal gameplay.

### Slot 4 bandage healing observation, 2026-10-26

A read-only snapshot of the subsequent extracted Ground Zero run `b7e997c3d3ec4bbe8635aae5898b1fa2` records three Bandage / Verband activations (item 10) and 13.499999046325684 actual HP restored. Lifetime, item and run totals agree. HealingCaptureComplete is true for both profile and run, historical event attribution is complete, and native-healing-attribution is Supported. Startup and capability evidence confirm the Become Veteran immediate/queued integration was active. Integrity and stored-record checksums passed. The snapshot and asserted receipt are retained under `artifacts/record-by-default/slot4-healing-acceptance-20261026/`; live profile data was not edited.

This exercises bandage healing with the installed mod set, without claiming every delayed-healing replacement, interruption or overheal edge case was tested. The user reports no accessible paid recipe in this playthrough, so positive-currency crafting is recorded as not exercised rather than failed. Its automated payment coverage remains separate. Throwables remain pending until naturally available.
