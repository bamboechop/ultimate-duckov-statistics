# Ultimate Duckov Statistics — Master Implementation Plan

## 1. Project and delivery contract

### Purpose

Build a local, single-player statistics mod for Escape From Duckov that records reliable lifetime, per-run, per-map, per-item, and per-equipment statistics without modifying the game’s save data or noticeably affecting gameplay.

The implementation will grow one event family at a time. Each family must fully exploit its reliable native hook before introducing additional hooks.

### Repository and toolchain

- Public repository: `https://github.com/bamboechop/ultimate-duckov-statistics`
- Local checkout: `C:\Users\micro\projects\ultimate-duckov-statistics`
- Product name: **Ultimate Duckov Statistics**
- License: MIT, copyright “Ultimate Duckov Statistics contributors”
- The user creates the repository under `bamboechop`, initialized with a README.
- Do not use the currently connected `hoeffernigsignteq` GitHub account.
- Development happens on feature branches; the first is `feat/consumable-mvp`.
- A draft PR is opened against `main`; the user performs the merge.
- Install the .NET 8 SDK system-wide after execution approval.
- Mod and core libraries target `netstandard2.1`; tests target `net8.0`.
- Game assemblies are referenced locally through `DUCKOV_PATH` with copy-local disabled. They are never committed, packaged, or downloaded by CI.
- CI builds/tests game-independent code. A complete mod build remains a required local check on a machine with Duckov installed.

### Current compatibility baseline

Reconfirm these before implementation and before each release:

- Duckov version: `2.3.30`
- Steam build: `24013657`
- Unity: `2022.3.62f2`
- Native entry point: `Duckov.Modding.ModBehaviour`
- Lifecycle overrides: `OnAfterSetup` and `OnBeforeDeactivate`

A version change triggers compatibility checks, not an automatic global shutdown.

### Release policy

- The repository is public from the beginning.
- `v0.1.0` is a GitHub pre-release containing the consumable-usage MVP.
- `v0.2.0` is a GitHub pre-release containing M2 healing attribution after its manual acceptance matrix passes.
- `v0.3.0` is the published GitHub pre-release containing M3 run lifecycle, duration records, map aggregation, and movement.
- `v0.4.0` is the published GitHub pre-release containing M4 accepted firing actions and event-time weapon/ammunition identity; unsupported outcome metrics remain unavailable.
- M5 is merged into `main`; its complete manual acceptance matrix passed, and GitHub pre-release `v0.5.0` was published on 2026-08-12.
- `v0.6.0` is the merged M6 equipment-and-totems release baseline.
- `v0.7.0` is the published GitHub pre-release containing M7 unique successful non-corpse container access.
- `v0.8.0` is the published M8 multi-map run/segment-attribution GitHub pre-release. Implementation, automated/package gates, single- and multi-map routes, repeated-map re-entry, later-map death, cross-map activity, abrupt recovery, review hardening, and release asset publication passed; the pre-release was published on 2026-08-13.
- `v0.8.1` product commit `90384352d323e6ea19dfa607c7da18162dbcefcb` passed its performance, gameplay, package, projection, and shutdown gates and PR #9 merged into `main`; live verification found no v0.8.1 tag or GitHub release. `v0.9.0` is the active M9 Economy candidate. M10 full UI/release hardening remains separate.
- No Steam Workshop upload in v0.1.
- Release artifact includes the installable ZIP, SHA-256 checksum, installation instructions, compatibility information, and known limitations.
- No Duckov assemblies or bundled Harmony assembly may appear in the package.
- Publication occurs only after the manual fresh-save and progressed-save acceptance matrix passes.

### Goal execution model

- Keep this plan in `PLAN.md`; put the reproducible manual protocol in `TESTING.md`.
- Use one bounded Codex Goal per event family.
- Recommended implementation model: GPT-5.6 Sol with high reasoning. Use xhigh only for difficult hook discovery or reverse engineering.
- A Goal remains active through build, deployment, user gameplay, evidence inspection, fixes, and retesting.
- A Goal is complete only after its automated and manual acceptance criteria pass.

## 2. Architecture and data contracts

### Solution boundaries

- **Core:** normalized events, reducers, records, classification, persistence, export, migration, and capability models. It must not depend on Unity or Duckov.
- **Mod adapter:** `ModBehaviour` entry point, Duckov event subscriptions, reflection/version probes, Unity UI, run state, and deployment integration.
- **Tests:** pure domain tests, persistence tests, compatibility-contract probes, and package validation.

`OnAfterSetup` initializes capability probes, persistence, UI, and subscriptions. `OnBeforeDeactivate` unsubscribes every handler and flushes pending state. Repeated setup/deactivation must never duplicate counters.

### Hook policy

1. Prefer public native events.
2. Saturate each hook family with low-cost metrics that the hook can prove reliably.
3. Do not retain speculative “everything” event streams.
4. Use Harmony only where no stable native route exists and the metric or required UI integration justifies it.
5. Each adapter reports one capability state:
    - `Supported`
    - `Experimental`
    - `DisabledIncompatible`
6. A missing member disables only the affected adapter and produces a visible compatibility warning.
7. Never infer unsupported semantics—for example, never label every critical hit a headshot.

The consumable-use hook does not require Harmony. If the main-menu button has no stable public registration path, introduce one narrowly scoped, version-checked patch. Use the shared [HarmonyLib Workshop dependency](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), currently observed as Workshop item `3589088839`; reverify its version before packaging. UDS must load after it and must not bundle `0Harmony.dll`. If Harmony is absent, the mod continues with F8 and shows that the menu-button integration is unavailable.

### Normalized event model

Every normalized event carries:

- Schema version and unique event ID
- UTC timestamp
- Save-generation ID
- Optional run ID and map ID
- Game version/build
- Gameplay context: base, raid, paused, or unknown
- Integrity tags: normal, cheat/custom difficulty, modded content, or unknown
- Adapter capability/version information
- Stable game IDs as canonical identifiers and display names as metadata

Initial typed events include:

- `ItemUseRecorded`
- `HealingApplied`
- `RunStarted` / `RunEnded`
- `ShotRecorded`
- `DamageRecorded`
- `CharacterKilled` / `PlayerDied`
- `EquipmentStateChanged`
- `TotemStateChanged`
- `ContainerLooted`
- `CurrencyFlowRecorded`

Reducers convert events into `ProfileStatistics`, `RunSummary`, records, item/equipment aggregates, and source breakdowns. Normalized raw events are transient unless diagnostics are enabled.

### Per-save persistence

Store data outside game saves:

`%USERPROFILE%\AppData\LocalLow\TeamSoda\Duckov\UltimateDuckovStatistics\`

Per save generation, retain:

- Profile identity and schema metadata
- Aggregate statistics and records
- Compact run summaries indefinitely
- Active-run checkpoint
- Exports
- Bounded diagnostic logs
- Read-only archives of replaced save generations

Use the Duckov save slot plus stable save metadata and a UDS generation ID. Rotate generations when `OnNewGameReport` indicates a new game or when startup identity checks show that a slot was deleted/reused. If identity cannot be proven, archive and start separately instead of silently merging.

All JSON snapshots use temporary-write plus atomic replacement and `.bak` recovery. An unfinished active-run checkpoint becomes an `Interrupted` run at the next startup. UDS never edits Duckov save files.

Normal mode stores aggregates and summaries, not raw events. Diagnostics mode is off by default and keeps a bounded rolling trace of recent normalized events and logs.

### UI and exports

- Native in-game panel available from the main menu/base.
- Configurable F8 hotkey, defaulting to F8.
- F8 during a raid does not open the panel and displays a brief “available outside raids” message.
- No charts in v0.1; use readable tables and record cards.
- Planned tabs:
    - Overview
    - Runs
    - Combat
    - Items
    - Equipment
    - Economy
    - Records
    - Diagnostics
- v0.1 enables Overview, Items, and Diagnostics. Later tabs become available as their adapters are implemented.
- Reset requires explicit confirmation and archives the current profile before starting a new generation.
- Export both machine-readable JSON and flattened CSV tables.
- English first, with all UI strings stored as localization keys and an English fallback.

## 3. Metric definitions and milestone roadmap

### Consumables and successful item use

Track raid uses only. Base usage may appear in diagnostics but must not affect statistics.

For every successful use, record separately:

- Activation action count
- Durability, charge, stack, or amount consumed
- Stable item ID and display name
- One canonical group
- Zero or more effect tags
- Run, map, and integrity context

Canonical groups:

- Healing
- Food
- Drink
- Stimulant/Buff
- Remedy/Debuff Removal
- Special
- Other/Unknown

An item belongs to exactly one primary group so group totals do not double-count. Multi-effect behavior is represented by tags. Unknown and third-party items remain visible rather than being discarded.

Actual healing records only HP restored to the main duck, including delayed item effects. Exclude nominal overheal, pets, and companions. Attribute healing through the item’s heal application or `Health.AddHealth` correlation; do not rely on a simple before/after snapshot when damage could interleave.

### Runs, records, and movement

- Start a run when player control begins after raid initialization.
- Use active gameplay time; paused time is excluded.
- Store wall-clock duration only diagnostically.
- End states: `Extracted`, `Died`, or `Interrupted`.
- Interrupted runs do not qualify for extraction/death duration records.
- Maintain shortest and longest extraction and death times overall and by starting map.
- Record all compact run summaries indefinitely.
- Track main-duck physical movement at approximately 5 Hz.
- Exclude loading and implausible locomotion deltas from traveled distance.
- Keep proven teleport distance separate from displacement excluded solely because of loading or a map transition.
- Derive the plausibility threshold from known movement speed, elapsed sample time, and a conservative tolerance rather than a fixed universal distance.

### Multi-map routes and segment attribution

- Treat one continuous expedition from initial player control through final extraction, death, or genuine interruption as one run even when it visits multiple maps.
- Store the starting map, ending map, ordered route, and ordered map segments. A repeated visit to the same map creates a new segment instead of collapsing the route.
- Distinguish the stable raid/root-map identity from the active map or subscene identity. Validate Duckov's full-scene, multi-scene, raid-ID, and control-ready ordering before changing run boundaries.
- Close the current segment when a proven map transition begins. Start the next segment only after the destination is initialized and the exact main duck regains control; loading time belongs to neither segment.
- Each segment records map identity, entry/exit time, active duration, physical distance, proven teleport distance, transition/loading-excluded displacement, and exit reason.
- Attribute M1-M7 actions and outcomes to the segment active when they occur. Where a delayed result can outlive its source action, retain both source-segment and outcome-segment identity rather than rewriting the source.
- Calculate route-aware per-map totals from segments and event-time attribution, never by assigning the complete run to every visited map or only its starting map.
- Preserve complete-run extraction/death records overall and by starting map. Retain a stable route signature for filtering/export, but do not create ranked exact-route records until recurring-route presentation has a proven use.
- Show a compact ordered route in Runs, with expandable per-segment details. Export the complete route in JSON and flattened route/segment CSV data.
- Schema-8 migration preserves all M1-M7 data and overall run records. Legacy `MapId` remains the historical root/starting-map observation; ending maps, routes, segments, and route-aware per-map attribution remain explicitly unavailable rather than being reconstructed as a fake single-map route.
- If an active-map identity or transition boundary cannot be proven safely, disable only route/segment attribution while preserving the established overall run lifecycle and totals.

### Combat

- Deaths by exact killer ID/name, broader enemy family, cause, and weapon when available.
- Direct player attacks and player-applied damage-over-time count as player kills.
- Pet, companion, environmental, and unknown kills remain separate.
- Shots record trigger pulls, ammunition units consumed, and projectile/pellet count separately when available.
- Attribute shots by weapon and ammunition type at event time.
- Track damage dealt/received, reliable hits, accuracy, melee swings/hits, kills, and deaths when available from the same hook family.
- Track headshots and headshot final blows only when the game supplies a proven headshot indicator.
- Do not treat `DamageInfo.crit` as a headshot without validation.

### Containers

- Count unique non-corpse containers per run.
- Count only when loot access actually begins successfully, not on proximity or a failed interaction.
- Deduplicate using `InteractableLootbox.GetKey()` and a per-run set.
- Reopening the same container does not increment the statistic.
- Corpses remain excluded even if they use similar interaction components.

### Economy

Track the game’s `Money` and physical `Cash` separately.

For both currencies, record gross inflow, gross outflow, net flow, and source:

- Purchases
- Sales
- Rewards
- Loot/pickups
- Fees/crafting/costs
- Unknown adjustments

Separate base, raid, shop, reward, and unknown contexts. For physical cash acquired during a raid, distinguish acquired, secured on extraction, and lost on death/interruption. Prefer semantic transaction events over inferring reasons from balance differences.

### Equipment and totems

Track raid-only active gameplay duration for:

- Guns
- Melee weapons
- Backpack
- Face slot
- Armor
- Other ordinary character slots exposed by the same reliable slot API
- Totems

For weapons, track slotted time and actively held/selected time separately. Preserve base item identity plus relevant attachment/state metadata in the per-run loadout snapshot. Attribute shots, damage, and kills to the event-time equipment state.

Totems distinguish:

- Directly equipped and active
- Carried in a tote bag and active
- Carried in a tote bag and inactive
- Carried with activation unknown

Never infer tote activation from inventory presence alone. Tote-bag activation begins as an experimental, disabled-by-default capability. Enable it only after buff or effect-state evidence has been manually validated. Record active totem-set durations per run; only recurring sets receive ranked aggregate presentation.

### Milestones

1. **M0 — Bootstrap and loader proof**
    - Repository, solution, native `ModBehaviour`, diagnostics, local references, build, package, deploy, load/unload smoke test.
2. **M1 — Consumable usage MVP (`v0.1.0`)**
    - Successful raid item uses, canonical groups, per-item/group/total tables, amount consumed, persistence, UI, exports.
3. **M2 — Healing attribution (`v0.2.0`)**
    - Actual HP restored, delayed effects, overheal exclusion, item attribution.
4. **M3 — Run lifecycle and movement (`v0.3.0`, implementation and complete manual acceptance passed)**
    - Run summaries, active timers, extraction/death/interruption, records, maps, physical/teleport distance.
5. **M4 — Weapons and ammunition (`v0.4.0`, released)**
    - Proven accepted firing actions and event-time weapon/ammunition breakdowns. Trigger attempts, actual loaded-ammunition consumption, and completed projectile creation remain explicitly unavailable because the public firing callback does not prove those side effects.
6. **M5 — Damage, kills, deaths, melee, and headshots (`v0.5.0`, implementation, manual acceptance, and independent-final-blow deployment passed)**
    - Actual HP loss from version-checked `Health.Hurt` pre/post state, never requested damage or `DamageInfo.finalDamage`.
    - Compatible ranged accuracy: unique exact-main-duck projectiles that cause positive enemy HP loss divided by exact-main-duck projectiles that reach verified `Projectile.Release` while the run is active.
    - Accepted melee swings from `CA_Attack.OnAttack`; one melee hit per damage scope even for repeated collider callbacks.
    - Enemy kills and player deaths from actual fatal health transitions, with raid death deferred until the richer main-character death evidence and `Health.Hurt` postfix are recorded.
    - Exact player, built-in pet/master-chain, environmental, and unknown ownership; stable preset-key identities with visible unknown/modded fallbacks.
    - Direct, repeated tick/update damage-over-time, generic effect, explosion, real-damage, and environmental causes.
    - Event-time weapon identity for damage and projectile-init ammunition identity where exposed; uncorrelated ammunition remains unknown rather than inferred.
    - Headshots only for independently observed native head-targeted exact-player projectiles. `DamageInfo.crit` is ignored as headshot evidence; headshot final blows are a separate fatal subset.
    - Schema 5, bounded 2,048-event/run deduplication and 2,048-projectile correlation, one-second combat-checkpoint coalescing, lifetime/map/run aggregation and breakdowns, UI, JSON, and `combat_attribution.csv`.
7. **M6 — Equipment and totems (`v0.6.0`, merged and released after complete manual acceptance)**
    - Monotonic active-time duration for canonical character-slot items, selected weapon plus exact slot, attachment-aware deterministic loadouts, and active direct-totem sets.
    - Event-time loadout/selection/totem-set association for firing and combat outcomes; bounded 256-transition per-run history and crash-safe checkpoints.
    - Stable identities use slot keys and `Item.TypeID`; runtime objects and localized/display names never determine persisted identity.
    - Tote content presence uses the public `AnyThing` slot of built-in Tote Bag `Item.TypeID` 1255 instances carried in the exact main duck's version-checked ordinary `CharacterItem.Inventory`. Tote activation remains unavailable and disabled until concrete buff/effect evidence and manual validation prove it.
    - Schema 6, lifetime/map/run aggregation, recurring loadout rankings only after two completed run occurrences, Equipment UI, JSON, and three equipment CSVs.
8. **M7 — Containers (`v0.7.0`, merged and released after complete automated and manual acceptance)**
    - Count one successful loot-interface access per stable `InteractableLootbox.GetKey()` in each run.
    - Use public `InteractableLootbox.OnStartLoot`, which occurs only after the interaction timer and inventory checks succeed; proximity, attempts, locks, cancellation, and failed access do not reach the event.
    - Require the event-time `InteractableBase.interactCharacter` to be the exact `CharacterMainControl.Main`.
    - Exclude native enemy corpses and persisted/player tombs through narrowly owned, version-checked death-path provenance patches. The success boundary itself remains the public event.
    - Persist a bounded 4,096-key active-run deduplication set. If that bound or stable-key evidence fails, disable the metric rather than evicting identities or fabricating counts.
    - Schema 7, lifetime/map/run aggregation, Overview/Runs presentation, JSON, and `containers.csv`; historical pre-M7 data remains explicitly unavailable.
9. **M8 — Multi-map runs and route attribution (`v0.8.0`, merged after implementation, gameplay acceptance, and review hardening)**
    - Keep a continuous expedition as one run across proven full-scene or subscene map transitions, with starting map, ending map, ordered route, and repeated-map-aware segments.
    - Attribute active time, physical distance, proven teleport distance, transition/loading exclusions, and M1-M7 statistics to the segment in which each action or outcome occurs.
    - Preserve both source and outcome segment identity for delayed effects where they differ; never rewrite event origin from the current map at completion time.
    - Replace ambiguous whole-run per-map totals with route-aware segment aggregation while retaining complete-run records overall and by starting map.
    - Add route/segment capability reporting, crash-safe active-segment checkpoints, schema-8 migration with explicit historical unavailability, Runs route presentation, JSON, and flattened route/segment CSVs.
    - Proven native semantics: continuous full-scene/subscene transitions preserve the raid identity and do not call `OnNewRaid`; `ActiveSubSceneID` is the visited subscene identity; destination entry waits for initialization, exact main-duck `SetPosition`, and restored input control. A changed raid ID outside a pending transition interrupts the old expedition.
    - Defensive bounds are 64 ordered segments and 2,048 source/outcome association rows. Bound exhaustion disables route-dependent capability without evicting prior evidence or disabling overall M1-M7 totals.
10. **M8.1 — Performance investigation and hot-path hardening (`v0.8.1`, final candidate qualified; PR #9 merged, no tag/release)**
    - Controlled A/B/C/D captures prove that Harmony alone is indistinguishable from the clean game while merged v0.8.0 adds a CPU-side idle and firing p99 regression across the Electrified MP7, MF assault rifle, and Mosin control. The Vektor was only the historical stress example, not a causal claim.
    - Exact product commit `90384352d323e6ea19dfa607c7da18162dbcefcb` completes comparable cold-launch, same-map UDS-disabled and UDS-enabled idle, empty-space firing, and single-enemy firing cells with two automatic weapon classes, a slower cross-class control, and Med-Kit completion. All seven reproducible cells have three accepted B and three accepted D captures, pass the tighter 5% median/10% p99 engineering target, and introduce no repeatable new action cluster. The unsafe multi-target cell remains explicitly unreproducible.
    - The supplementary three-map soak exposed a separate repeatable synchronous Med-Kit completion hitch. The final matched Med-Kit cell records every accepted activation and exact heal; whole median/p99 overhead is -0.129%/+3.721% and action median/p99 overhead is -0.142%/+3.245%. Isolated severe frames appear in both configurations but do not repeat as a completion-aligned cluster and none was perceived.
    - Candidate 8 keys the immutable equipment cache by segment plus loadout identity so an unchanged loadout is republished once after each transition. Its first natural-play soak is smooth through Nullpunkt and Lagerbereich with clean consumable completions, improving late-run p99, exact UI/export agreement, and clean shutdown. The retry reaches Nullpunkt → Lagerbereich → Farmstadt, preserves non-empty segment-local equipment roots and 891 unique event associations, records all five consumable uses exactly once, shows only 1.1% late-versus-early p99 movement, and produces no perceived UDS-related hitch. UI, JSON, all nineteen CSVs, the live profile, backup, diagnostics, and residue-free byte-identical shutdown state agree. This closes the representative full-route gameplay, projection, and shutdown portions of the gate.
    - The route-independent equipment correction treats absent segment context as a stable overall-only observation instead of suspending equipment. Overall durations and event-time associations continue; only route-dependent evidence degrades. Permanent-missing-context and segment-to-missing-context regressions pass.
    - Immutable product commit `90384352d323e6ea19dfa607c7da18162dbcefcb` passes the complete 507-test/native/package pipeline and produces the exact five-file v0.8.1 package. The 221,763-byte ZIP is SHA-256 `2510317d1aca11a19ab658941b513fa630d6b70f2a6d8065c77b57a744cdeb62`; its 102-byte checksum sidecar, independent extraction, transactional deployment, exact deployed-byte readback, cold activation, final native matrix, JSON/nineteen-CSV projection, 33-supported/four-deliberately-unavailable capability set, and residue-free byte-identical shutdown all pass. PR #9 subsequently merged into `main`; v0.8.1 remains untagged and unpublished.
    - Production-boundary counters and native frame evidence identify repeated equipment rebuilds, reflective Harmony inspection, per-projectile-frame context work, active-run durable persistence, and consumable lifetime-profile persistence as distinct CPU costs. Optimize only these evidence-backed paths; do not replace profiling with speculative rewrites.
    - Keep accepted events in memory on their native callback path. Durable persistence, route cloning, export serialization, metadata discovery, reflection, and other potentially blocking work must not scale once per bullet, pellet, target hit, or health tick on the main thread.
    - Preserve M1-M8 counting, source/outcome attribution, bounded caches, one-second crash-safety cadence, failed-write retry, transition/terminal flushes, interruption recovery, capability degradation, and cleanup semantics exactly.
    - Add deterministic structural tests for callback cost drivers, write/clone frequency, failure/retry, mutation races, lifecycle drains, exact interruption recovery, and export projection. Do not use wall-clock microbenchmarks as CI correctness assertions.
    - Add only bounded, opt-in performance diagnostics if ordinary profiling cannot isolate the cost; diagnostics must be disabled by default and must not become the measured regression.
11. **M9 — Economy (`v0.9.0`, immutable candidate accepted; draft PR open)**
    - Model account-style Money and physical Cash item type `451` as separate currencies with exact positive magnitudes plus explicit inflow/outflow direction.
    - Use public post-mutation `EconomyManager.OnMoneyChanged`; enrich only exact matching completed StockShop sales and `QuestReward_Money` claims, retaining every other proven change as `UnknownAdjustment`.
    - Use event-driven, runtime-identity-deduplicated owned-Cash totals across storage, main inventory, and pet inventory. Suspend reconciliation during full-scene hydration and establish one non-economic baseline only after level initialization completes. Exclude load baselines, carried-in Cash, overlapping native enumeration, stack split/merge, internal movement, and bounded player-originated drop/re-pickup identity from raid acquisition. Corpse/container Cash remains an exact unknown-source flow when the proven world-pickup callback is absent.
    - Keep Cash terminal outcomes independently unavailable on Duckov 2.3.30: fungible main/pet/storage ownership does not prove acquired-unit secured/lost disposition. Preserve acquired and report unresolved instead of fabricating extraction/death loss.
    - Add schema 9, pre-M9 historical unavailability, exact-once deferred lifetime/checkpoint recovery, run/segment/starting-map/route-map aggregation, Economy/Overview/Runs UI, eight diagnostic capability rows, JSON, and four new flattened CSVs while preserving the existing nineteen CSV contracts.
    - Preserve M8.1 single-flight persistence and firing/combat hot paths. Economy does no per-frame balance or inventory scan; an ordinary frame performs only an O(1) clean-state return, while native economy/inventory boundaries coalesce bounded work.
    - Use scalable exact economy idempotency with one activation-scoped monotonic replay cursor per directly recording aggregate. A registered activation persists a valid closed-through sequence zero before its first positive-sequence event. Persisted identity metadata remains constant-size, unique Money/Cash flows continue beyond 2,048 events, stale or duplicate sequences are rejected, and completed-run fan-out relies on run identity plus exact totals rather than transaction IDs. Validate and retain schema-9 candidate `RecentEventIds` through recovery, then compact them only after all replayable checkpoint artifacts are consumed; old saturation becomes explicit incomplete-history evidence without stopping new capture.
    - Current candidate evidence: 589/589 Release tests, installed-game contract probe, warning-free Core/native builds, exact-five-file package validation, and protected M8.1 report hash preservation. The first live matrix proved exact Money/Cash deltas, semantic unknown retention, failed-operation exclusion, split/merge neutrality, drop/re-pickup handling, export agreement, and clean shutdown. The second retest passed Diagnostics full-tab scrolling but rejected the Cash fix with false Base hydration inflows `+37` on raid entry and `+44` on return while preserving the exact Raid `+7`. Decompiled lifecycle order identifies partial inventory hydration before `OnAfterLevelInitialized`; adapter v2 gates scans through that boundary. The focused live retest passed with carried Cash 44 excluded and corpse-looted Cash 24 retained as the only exact Raid inflow across lifetime, run, segment, starting-map, and route-map projections, followed by residue-free shutdown. A completion audit additionally proved base/shop economy was synchronously saving once per flow; the source now always marks the M8.1 profile snapshot writer dirty and retains the run watermark only for active-run recovery. A production-linked repository test proves a base Money mutation leaves the profile bytes unchanged until the coalesced snapshot is persisted. The final live smoke passed with ATM Money `178 → 177`, unchanged Cash 68, exact lifetime Base `-1`, export agreement, byte-identical clean-shutdown profiles, and no runtime/deployment residue. The final source audit also made aggregate overflow retain prior exact values atomically per currency instead of clamping. Immutable implementation commit `41721ec47944393d10d1ecae279dea1224f8e4fc` passes the complete pipeline and produces the 252,244-byte ZIP at SHA-256 `84e2d286e6e2d7f5816d1137ed26cd2bba1fd57c90ccd5ba2bf24e31a877355f`; independent extraction, transactional deployment/readback, cold activation, and clean shutdown pass. [Draft PR #10](https://github.com/bamboechop/ultimate-duckov-statistics/pull/10) is open and unmerged; its live final-head CI status is reported on the PR.
12. **M10 — Full UI and release hardening (`v0.10.0`, planned)**
    - Remaining tabs, filters, compatibility matrix, performance verification, migration tests, documentation, and Workshop-readiness assessment.

Each milestone updates the capability matrix and is manually tested before the next begins.

#### Planned M8 acceptance boundary

- Prove a single-map run remains behaviorally identical except for its explicit one-segment route.
- Complete a two-map extraction and, where practical, a three-map expedition; verify ordered route, start/end map, segment boundaries, active time, distance, and M1-M7 attribution.
- Die after a map transition and verify the final segment receives the death while the complete run retains its original start and route.
- Re-enter a previously visited map when the game permits it and verify a new ordered segment is retained instead of merging non-contiguous visits.
- Interrupt and recover a run after at least one completed transition; verify completed/current segments and all aggregates recover exactly once.
- Verify pause/loading time and cross-map position jumps do not inflate active time or physical/proven-teleport distance.
- Perform representative item, healing, firing/combat, equipment, and container actions on different maps and compare profile, Runs UI, JSON, and CSV segment attribution.
- Retain the user-controlled gameplay model: Codex prepares, deploys, and inspects evidence; only the user launches and controls Duckov.

#### M8.1 investigation and acceptance boundary

- The review-hardened M8 controls confirm that the performance problem is tied broadly to firing activity rather than uniquely to the Vektor. The initial Vektor stress case showed approximately 200+ FPS at baseline and 100-150 FPS while firing into empty space; the Electrified MP7 and MF assault rifle also reproduced material drops, and enemy hits worsened frame pacing. Averaged Steam-overlay FPS did not represent the perceived stalls.
- Do not claim that UDS or M8 introduced the regression without comparative evidence. Earlier milestone validation did not include a UDS-disabled control or similarly intense sustained firing across multiple weapons, so the UDS contribution and introduction point are currently unknown.
- Use a frame-time capture tool rather than the averaged FPS overlay. Perform at least three comparable cold-launch captures per configuration and scenario, report median, p95, p99, maximum frame time, and frames above 16.7 and 33.3 ms, and retain the raw capture artifacts outside the repository when they are large or machine-specific.
- Establish the UDS-disabled baseline first, then isolate enabled subsystem combinations or historical builds without weakening the final production configuration. Record exactly which adapters, versions, save generation, route size, and diagnostics state each capture used.
- Freeze the benchmark method and acceptance budget before optimizing. The default budget is that the median of three UDS-enabled captures is no more than 10% worse than disabled at median frame time, no more than 20% worse at p99, and introduces no repeatable new cluster of frames above 33.3 ms. Any different budget requires an explicit recorded product decision and rationale.
- Instrument storage in automated tests and prove high-rate firing/hit bursts do not cause checkpoint serialization, flush, or atomic replacement per event. Exercise the full 2,048-association route bound, successful cadence, failed-write retry, transition, terminal completion, clean shutdown, and abrupt recovery.
- Prove that optimization does not drop, merge, duplicate, defer past the wrong segment, or recapture any firing/combat event. Profile, active checkpoint, recovered run, UI, JSON, and CSV must agree exactly after the benchmark run.
- Verify one setup and one cleanup sequence with no duplicate subscriptions or stale Harmony/native callbacks before accepting performance results.
- Retain the user-controlled gameplay model: Codex prepares comparable builds, deployment, capture instructions, and evidence inspection; only the user launches and controls Duckov.

## 4. First Goal: consumable-usage MVP

### Implementation sequence

1. Confirm game version, assembly paths, native entry point, item-use event signature, raid-state events, and packaging format.
2. Create the public-project scaffold on `feat/consumable-mvp`.
3. Implement the smallest loadable mod with setup/deactivation logging and no subscriptions left behind.
4. Build, deploy, enable, launch, and verify clean loading before adding statistics.
5. Subscribe to `ItemStatsSystem.UsageUtilities.OnItemUsedStaticEvent`.
6. Add only the minimal raid lifecycle state required to ignore base usage.
7. Normalize successful use events and prevent duplicate counting.
8. Capture activation count and actual item amount/charge/durability consumed independently.
9. Classify known items deterministically and retain unknown/modded items under `Other/Unknown`.
10. Persist aggregates per save generation with atomic recovery.
11. Add Overview, Items, and Diagnostics UI with F8 and main-menu/base access.
12. Export JSON and CSV.
13. Add automated tests, package validation, installation instructions, and the manual test checklist.
14. Open/update the draft PR and keep it unmerged until validation passes.

### Automated acceptance

- Successful uses increment once.
- Cancelled, interrupted, and failed uses do not increment.
- Repeated setup or scene changes do not duplicate event subscriptions.
- Activation count and amount consumed remain distinct.
- Base use is ignored; raid use is counted.
- Group totals equal the sum of their items without multi-effect double-counting.
- Unknown/modded items are preserved with stable IDs.
- Profiles remain isolated by save generation.
- Serialization round-trips, schema migration, corruption recovery, and backup fallback work.
- Active-session interruption cannot corrupt the profile.
- JSON and CSV exports represent the same totals.
- Package contains required files and no game or Harmony DLLs.
- Contract tests confirm required native types/events exist in the currently installed assemblies.

### Manual acceptance matrix

Codex deploys builds and reads logs/statistics; the user launches and plays the game.

For a user-selected progressed save:

1. Codex creates a timestamped backup of the selected save and its existing backups.
2. Verify the panel loads and starts with no reconstructed history.
3. Use an item at base and confirm totals remain unchanged.
4. Enter a raid and cancel an item use; confirm no count.
5. Successfully use at least two different consumables, preferably from different groups.
6. Use a charged/stacked item where possible and verify action count versus amount consumed.
7. Extract or otherwise finish the session, reopen the panel, and compare totals to the performed actions.
8. Restart Duckov and verify persistence.
9. Export JSON and CSV; Codex inspects the exact records and diagnostics.

For a user-selected fresh save:

1. Verify a separate zeroed profile.
2. Complete at least one successful raid use.
3. Confirm no data leaked from the progressed save.
4. Reuse/delete only this disposable test slot and verify that the old generation is archived read-only and the new generation starts at zero.

Failure evidence comes from `Player.log`, UDS diagnostics, persisted profile files, exports, and the user’s action checklist. Fixes are rebuilt and redeployed within the same Goal until the matrix passes.

The first Goal is complete only when:

- Automated checks pass.
- The local mod package loads cleanly.
- Both manual save scenarios pass.
- No Duckov save was edited by UDS.
- Source is committed and pushed to the feature branch.
- The draft PR is current.
- The installable ZIP and checksum are ready.

## 5. Testing, safety, and operating assumptions

### Ongoing verification

- Pure reducers use deterministic fake clocks.
- Adapter contract tests inspect local Duckov assemblies without executing gameplay.
- Every event-family Goal includes focused duplicate, attribution, interruption, unknown-content, save-isolation, and persistence tests.
- Manual testing occurs after every family, not only at final release.
- Tests involving progressed saves always begin with a timestamped backup.
- Codex never deletes or restores a save backup without an explicit user request.
- Codex may deploy builds into the game directory after filesystem approval; only the user launches and controls Duckov.
- Performance checks verify bounded caches, throttled writes, low-frequency movement sampling, and no continuous scene-wide scans.

### Defaults and boundaries

- Windows first, with platform-neutral core and path abstractions for later macOS support.
- Single-player only until co-op ownership and attribution can be proven.
- Start tracking at installation; do not reconstruct historical statistics.
- Statistics remain per save; no combined all-save dashboard.
- All data stays local. No telemetry, accounts, or external service.
- Custom difficulty, cheats, and other mods are tracked and tagged rather than rejected.
- Flagged runs are excluded from records by default but can be included through filters.
- Third-party items and enemies are stored generically through stable IDs and fallback names.
- Unsupported or ambiguous metrics appear as unavailable/experimental, never as fabricated zeroes.
- Main-duck stats exclude pets and companions unless explicitly shown in separate categories.
- Existing game and user files are preserved; deployment affects only the mod directory and UDS’s external data directory.
