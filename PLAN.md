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
- Every `0.x` build and GitHub pre-release is a development artifact for voluntary testing, not an officially distributed build and not part of a supported installation channel. Manual GitHub downloads do not establish an end-user support obligation.
- Compatibility and persisted-profile migration between `0.x` versions are best-effort development continuity, not supported upgrade guarantees. Existing pre-1.0 migration implementations and exact-migration acceptance tests remain valuable internal hardening, but their presence does not turn a `0.x` carry-forward path into a release blocker.
- Supported upgrade and migration guarantees begin with the first version explicitly declared as officially distributed through a supported channel; that release will define its supported starting baseline. Until then, a finding reachable only by upgrading UDS-owned data from one `0.x` build to another is outside the supported release contract.
- `v0.1.0` is a GitHub pre-release containing the consumable-usage MVP.
- `v0.2.0` is a GitHub pre-release containing M2 healing attribution after its manual acceptance matrix passes.
- `v0.3.0` is the published GitHub pre-release containing M3 run lifecycle, duration records, map aggregation, and movement.
- `v0.4.0` is the published GitHub pre-release containing M4 accepted firing actions and event-time weapon/ammunition identity; unsupported outcome metrics remain unavailable.
- M5 is merged into `main`; its complete manual acceptance matrix passed, and GitHub pre-release `v0.5.0` was published on 2026-08-12.
- `v0.6.0` is the merged M6 equipment-and-totems release baseline.
- `v0.7.0` is the published GitHub pre-release containing M7 unique successful non-corpse container access.
- `v0.8.0` is the published M8 multi-map run/segment-attribution GitHub pre-release. Implementation, automated/package gates, single- and multi-map routes, repeated-map re-entry, later-map death, cross-map activity, abrupt recovery, review hardening, and release asset publication passed; the pre-release was published on 2026-08-13.
- `v0.8.1` product commit `90384352d323e6ea19dfa607c7da18162dbcefcb` passed its performance, gameplay, package, projection, and shutdown gates and PR #9 merged into `main`; it was not published separately and its accepted changes shipped in `v0.9.0`.
- `v0.9.0` is the published GitHub pre-release containing M9 Economy and the accepted M8.1 performance hardening. It was published on 2026-08-18 from merge commit `ba2d01ca345f005de6bb88249592eb7f31c9254a`.
- `v0.10.0` is the published GitHub pre-release containing M10 lossless route-event association. PR #11 merged as `cbabd3eb7760178c939f5ebea50709c42f183cb6`, and the pre-release was published on 2026-08-19.
- `v0.11.0` is the published GitHub pre-release containing M11 combat ownership and observed-death semantics. PR #12 merged as `875f53792b7dab7ac35a27d8957966ecc9e5c2be`, and the pre-release was published on 2026-08-20.
- `v0.12.0` is the published M12 world-time and sleep statistics pre-release. M13 crafted-item statistics merged through PR #14 as `a59ed777ed0f316bd7b7fcbd2b61aeacf8752990`, and [`v0.13.0`](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v0.13.0) is published.
- M14 preserves lossless weapon associations as `v0.14.0`: accepted firing actions by weapon-ammunition pair and equipped duration by weapon-attachment membership. M15 then applies the native UI overhaul to the stable M0-M14 data model as `v0.15.0`.
- M16 is feature-frozen release hardening and the complete v1.0 audit. Its first accepted candidate is published as `v1.0.0-rc.1`; behavior-changing RC corrections increment the RC suffix rather than becoming additional `v0.x` feature releases.
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

- Native in-game panel available from the main menu and base pause menu through a discoverable `Statistics` entry; this is the primary user-facing access path.
- Configurable F8 hotkey, defaulting to F8, is a secondary shortcut and compatibility fallback rather than the primary discovery path.
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
11. **M9 — Economy (`v0.9.0`, merged and released after complete automated and manual acceptance)**
    - Model account-style Money and physical Cash item type `451` as separate currencies with exact positive magnitudes plus explicit inflow/outflow direction.
    - Use public post-mutation `EconomyManager.OnMoneyChanged`; enrich only exact matching completed StockShop sales and `QuestReward_Money` claims, retaining every other proven change as `UnknownAdjustment`.
    - Use event-driven, runtime-identity-deduplicated owned-Cash totals across storage, main inventory, and pet inventory. Drain any dirty exact delta before suspending reconciliation for full-scene hydration, then establish one non-economic baseline only after level initialization completes. Exclude load baselines, carried-in Cash, overlapping native enumeration, stack split/merge, internal movement, and bounded player-originated drop/re-pickup identity plus last-owned amount from raid acquisition, including when production `AddAndMerge` consumes the picked item into another stack. Corpse/container Cash remains an exact unknown-source flow when the proven world-pickup callback is absent.
    - Keep Cash terminal outcomes independently unavailable on Duckov 2.3.30: fungible main/pet/storage ownership does not prove acquired-unit secured/lost disposition. Preserve acquired and report unresolved instead of fabricating extraction/death loss.
    - Add schema 9, pre-M9 historical unavailability, exact-once deferred lifetime/checkpoint recovery, run/segment/starting-map/route-map aggregation, Economy/Overview/Runs UI, eight diagnostic capability rows, JSON, and four new flattened CSVs while preserving the existing nineteen CSV contracts.
    - Preserve M8.1 single-flight persistence and firing/combat hot paths. Economy does no per-frame balance or inventory scan; an ordinary frame performs only an O(1) clean-state return, while native economy/inventory boundaries coalesce bounded work.
    - Use scalable exact economy idempotency with one activation-scoped monotonic replay cursor per directly recording aggregate. A registered activation persists a valid closed-through sequence zero before its first positive-sequence event. Persisted identity metadata remains constant-size, unique Money/Cash flows continue beyond 2,048 events, stale or duplicate sequences are rejected, and completed-run fan-out relies on run identity plus exact totals rather than transaction IDs. Validate and retain schema-9 candidate `RecentEventIds` through recovery, then compact them only after all replayable checkpoint artifacts are consumed; old saturation becomes explicit incomplete-history evidence without stopping new capture.
    - Final release evidence: PR #10 merged as `ba2d01ca345f005de6bb88249592eb7f31c9254a`; the complete Release suite passes 665/665, the installed-game compatibility contract and exact five-file package/readback pass, and the user-verified Economy UI, 24-file JSON-plus-23-CSV export, and clean shutdown agree. GitHub pre-release [`v0.9.0`](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v0.9.0) was published on 2026-08-18. Its 265,633-byte ZIP is SHA-256 `1b126c9d999343e4cb0c5544cff051ff90f454ab72bd6b0b50a3f1d1c0877803`.
12. **M10 — Lossless route-event association (`v0.10.0`, published pre-release)**
    - Remove the fixed 2,048 source/outcome association-row ceiling without weakening exact M1-M9 run, segment, starting-map, or route-map statistics. Do not silently drop, approximate, merge, or relabel later events in long expeditions.
    - Begin with an evidence audit of every producer, consumer, checkpoint, recovery path, reducer, UI model, JSON field, and CSV projection that uses route associations. Select the representation only after measuring its active-state, persistence, replay, and projection costs; the roadmap does not prescribe an ever-growing raw-event ledger.
    - Bound storage by unresolved correlation state and stable aggregate cardinality where exact compaction is possible, rather than by the count of already resolved gameplay events. Preserve delayed source/outcome joins, repeated-map visits, segment identity, duplicate rejection, crash recovery, and legacy incomplete-history provenance.
    - Add schema-10 migration and explicit capability semantics. Existing saturated histories remain marked incomplete because missing associations cannot be reconstructed; unsaturated history must migrate exactly, and new capture must not become unavailable solely because a run exceeds 2,048 resolved associations.
    - Acceptance includes deterministic runs exceeding 2,048 and at least 100,000 mixed M1-M9 events across three or more segments, with late events still attributed exactly after checkpoint/reopen/interruption recovery. Profile, Runs UI, JSON, and every affected CSV must agree, and the M8.1 performance budget must remain satisfied.
    - The implemented schema-10 representation retains finite schema-9 raw rows as `LegacyRaw` evidence and reduces new accepted rows exactly into checked 64-bit buckets keyed by event family plus source/outcome segment. It preserves repeated visits and delayed cross-segment outcomes, bounds current state by five families and the existing 64-segment route cardinality, and exposes current-capture support separately from irrecoverable historical incompleteness. M9 economy remains on its independent cursor/segment fan-out and never consumes these buckets.
    - Local qualification passes 15/15 focused tests and the complete 679/679 Debug and Release suites, the installed-game/native/build/source/package gates, exact five-file deployment readback, a seven-segment repeated-map route, 24-file JSON-plus-23-CSV projection, and two clean shutdown/reopen cycles. The high-rate matched cells pass; the Med-Kit action-window p99 is a truthfully retained +13.777% technical miss against the +10% target, accepted by explicit product exception because no material frame, cluster, or perceived hitch occurred. PR #11 merged as `cbabd3eb7760178c939f5ebea50709c42f183cb6`; GitHub pre-release [`v0.10.0`](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v0.10.0) was published on 2026-08-19 with a 269,381-byte ZIP at SHA-256 `73a0dc3f9c9a64172815562fa0191c6b444b69dbef206f39eb8e8859fd3d8037`.
13. **M11 — Combat ownership and observed-death semantics (`v0.11.0`, published pre-release)**
    - Make the primary player statistic explicitly **Kills by you** and include only deaths with proven player ownership. Separately retain clearly labelled observed-world deaths attributed to `Companion`, proven `OtherNpc`, `Environmental`, or `Unknown`; NPC-on-NPC deaths must never inflate player kills.
    - Carry actor evidence through direct projectiles, melee, explosions, buffs, and delayed damage-over-time. Use native owner/master chains where proven; weapon identity never proves actor identity, conflicting or missing evidence remains `Unknown`, and `Environmental` means actorless world damage rather than a generic non-player bucket.
    - Keep ownership, cause, weapon, damage, final-blow, headshot, records, and equipment association internally consistent across lifetime/run/segment/map aggregates, UI, JSON, and CSV. Player-kill records and equipment credit use only proven player final blows while non-player deaths remain available as separate world-event context.
    - Add schema-11 migration with truthful legacy provenance. Historical M5-M10 rows that cannot be reclassified from retained evidence remain legacy/unavailable rather than being silently upgraded.
    - Regression fixtures cover player, companion, NPC-on-NPC, environmental, and unknown fatal events; direct and delayed damage; penetrating headshot/non-headshot victims; repeated maps; checkpoint/reopen/recovery; and actor/weapon conflicts. Export `20260813T1023453040456Z-1523690077194c07b3d2c960f20843eb`, run `e4478f42a21446b5a210f94a8ed5cfae`, is corroborating evidence for the observed NPC-on-NPC case, not a path-dependent test fixture.
    - The schema-11 implementation carries credited, physical, and damage actors through direct/projectile/melee/effect scopes; refreshes reflected projectile ownership at impact; follows only the installed build's exact controlling-character and bounded pet/master/leader chains; and reserves `Environmental` for the exact actorless `ZoneDamage.Damage` scope. The primary and equipment metrics use proven player final blows, while all other fatal enemy transitions remain separately observed and historical ambiguity remains legacy-labelled.
    - Local qualification passes the complete 704/704 Debug and Release suites, installed-game/native/source/package checks, independent five-file extraction and deployment readback, progressed schema-10-to-11 migration, a direct player final blow, two correctly separated Other-NPC deaths including damage-over-time, 24-file JSON/CSV agreement, final UI agreement, and two residue-free clean shutdowns with a cold same-schema reopen. Review follow-up now carries the incoming actor from the exact three-argument `CharacterBuffManager.AddBuff` callback, makes conflicting same-ID buff reapplication monotonically `Unknown`, requires the fatal transition itself to consume the projectile's proven headshot before recording a headshot final blow, and shares the healing-owned buff-callback trust state with combat. If that callback is rejected because any healing patch point conflicts or is later detached, retained native buff actors become `Unknown`, equipment fallback is suppressed, and every incomplete dependent combat capability becomes unavailable while unrelated direct metrics remain available. The same completeness boundary now requires the combat-owned `Effect.Trigger` scope: losing it marks ownership-derived capabilities unavailable, and every health transition without another trusted ownership scope becomes `Unknown` with unknown weapon identity and no player/equipment credit, including `ExplosionAction` damage whose native buff/effect marker remains false. Expanded Runs/Combat UI renders disabled values as Unsupported, and each affected flattened summary/equipment CSV carries the relevant capability state beside any retained partial total. Both retained actor orders, initial rejection, later trust loss, marker-false explosion scope loss, the shared penetrating-projectile scope, checkpoint recovery, and every affected export projection are deterministic regressions. The user reported no hitch. Player-owned delayed damage and Companion final blows are not reliably producible with the user's inventory and native Duckov 2.3.30 respectively; actor-conflict, exact actorless environmental fatality, reflection, and multi-victim penetration remain unsafe or unrealistic manual cases, so installed-native audit plus deterministic production-path tests are the truthful acceptance evidence for those paths.
    - Final release evidence: PR #12 merged as `875f53792b7dab7ac35a27d8957966ecc9e5c2be`; GitHub pre-release [`v0.11.0`](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v0.11.0) was published on 2026-08-20. The merged build reruns and passes 704/704 tests plus the installed-game and exact five-file package gates. Its 273,624-byte ZIP is SHA-256 `17ee7588db34e1424f4513f52af2ece08794a55e370128b52f3d269e689fca85`; the 103-byte checksum sidecar is SHA-256 `413ce31ac998c5200d5b1d463f01a87c54f28f459932fb6ff6e1caeaee094128`.
14. **M12 — World-time and sleep statistics (`v0.12.0`, published)**
    - Discover and document the installed build's native clock, day-transition, sleep-start, sleep-complete, sleep-cancel, fast-forward, load, and save-slot contracts before choosing hooks or defining counters.
    - Track proven calendar-day advancement, total observed in-game elapsed time, completed sleep sessions, and total in-game time advanced through sleep. Keep real-world active/play time, normal clock progression, sleep fast-forward, load initialization, and other clock jumps distinct; do not infer sleep merely from a large time delta.
    - Scope lifetime totals to the correct save generation. Sleep is not assigned to a raid unless native evidence proves that Duckov permits and identifies that context. Backward clock changes, manual/system time setting, ambiguous fast-forwards, and unsupported native states degrade only the affected metric and remain visible.
    - Add schema-12 migration, crash-safe persistence, UI, JSON, CSV, and independent capability diagnostics for calendar-day advancement, observed world time, completed sleep sessions, and sleep-advanced time. Pre-M12 time and sleep history remains explicitly unavailable rather than reconstructed from current day or clock values.
    - Acceptance covers ordinary progression and midnight crossing, completed sleep within one day and across midnight, multi-day advancement where supported, cancellation/interruption, non-sleep fast-forward, load initialization, restart, save-slot rotation, generation replacement, clock wrap/backward movement, duplicate lifecycle callbacks, and independent capability degradation. Profile, UI, JSON, and CSV projections must agree.
    - The candidate uses public `GameClock.OnGameClockStep` plus checked `Day * 86,300 seconds + TimeOfDay` coordinates. The first observation per generation and the first callback from every replacement `GameClock.Instance` are baseline-only. Instance replacement is tracked independently of profile events because a normal main-menu/base scene round-trip creates a new clock without `OnSetFile`; rebaselining preserves already-pending exact totals and prevents the replacement's saved coordinate from appearing backward. Slot/deletion transitions capture the prior `GameClock.Instance` at transition start and accept hydration for a changed generation only from a replacement instance; `OnSetFile` buffers captured- and replacement-instance observations separately until the completed repository open proves whether the slot and generation actually changed. After preceding FIFO transitions have committed, a separate completed transition step refreshes the then-current identity and freezes repository slot and generation immediately before the queued open. The following non-transactional open can retry without recomputing that pre-open state. A proven same-slot/same-generation reopen restores the captured clock's checked interval and baseline only if no unresolved predecessor still requires a replacement instance. Automatic backup recovery can synchronously raise an inner and outer `OnSetFile` before the selected slot loads; the newer handoff therefore inherits the older replacement gate and cannot baseline from the prior slot. If a changed target commits before that replacement loads, the captured prior-slot aggregate is then known to be ineligible, is discarded, and cannot block clean shutdown; the prior instance identity remains excluded until hydration. Target-clock observations for actual changes remain staged, and resetting UDS statistics while the replacement is still pending preserves that load gate for the reset generation. New-game rotation snapshots the already-loaded clock synchronously at `OnNewGameReport`, buffers the ensuing `OnNewBoot` callbacks, and transfers that delta only after the matching resumable transition reaches the new generation. Multiple UI selections may queue while an older transition is retrying: starting the newer handoff freezes the older transition's checked aggregate and sleep proof, routes later observations only to the newest clock, and lets FIFO completion transfer each snapshot to its own committed generation. Sleep requires the exact internal `GameClock.Step(float)` prefix/postfix result followed once by public `SleepView.OnAfterSleep`; the installed assembly has no sleep-cancel event and no other caller of that exact step method. A completed sleep during a retrying slot/new-game transition is retained with that handoff's transition ID and transferred only to the matching committed generation, including when commit falls between the patched step and completion callback. Quit and deactivation synchronously drain all profile transitions that can now progress before the terminal world-time flush; adapter cleanup refuses to reset a handoff that still contains uncommitted clock or sleep data and retains the coordinator for a later same-process cleanup retry. The adapter's generation provider captures that exact coordinator instance rather than the nullable behavior field, so retained transition completion can still transfer and persist the matching mutation before successful reactivation. The high-frequency clock path coalesces checked aggregate mutations into the in-memory profile once per monotonic second, schedules the full durable snapshot no more than once per 30 monotonic seconds, requests immediate durability for completed sleep, and flushes before save/profile transitions and shutdown. See [docs/M12_NATIVE_CONTRACTS.md](docs/M12_NATIVE_CONTRACTS.md).
    - Calendar/elapsed capture remains independent from the dedicated sleep Harmony owner. Missing, foreign, or drifting sleep patches disable only completed-sleep and sleep-advanced metrics; invalid/backward clock movement disables only the clock-derived pair. Sleep receives no raid, route, map, or segment attribution because the verified native contract proves only a base UI lifecycle.
    - Final release evidence: PR #13 merged as `6bd39dadf5cd1c49149b98b7d6b0898d62608f67`; GitHub pre-release [`v0.12.0`](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v0.12.0) was published on 2026-08-21. The merged build reruns and passes 754/754 tests plus the installed-game and exact five-file package gates. Its 296,036-byte ZIP is SHA-256 `8a929e09a27fdc872182eaa64ffa9bbe6776d5775fa100e0682b3e4915028c18`; the 103-byte checksum sidecar is SHA-256 `03d20b489b709af2d9b92339d296f081938be0d3b5ee1c424eacb50cb9fb694c`.
15. **M13 — Crafted-item statistics (`v0.13.0`, published)**
    - Discover the native crafting request, queue, completion, cancellation, output-delivery, inventory, reload, and recipe/workstation contracts before selecting a successful-craft boundary. Count only outputs proven to have been created successfully for the player; attempts, queued-but-incomplete work, cancellations, failures, load hydration, and ordinary inventory movement do not count.
    - Track crafted completion actions and produced item quantity separately by stable output item identity. Retain recipe, workstation, batch, context, and display metadata only where native evidence exposes them reliably; preserve unknown/modded identities instead of dropping them. Do not infer consumed ingredients, recipe cost, or economic value from the output item.
    - Delayed and batch crafting must publish exactly once even when completion, collection, inventory insertion, save, reset, restart, or scene callbacks overlap. A completion proven while a save/profile transition is queued must be retained with that transition and committed only to its target generation, never to the still-open predecessor. Assign crafting to a run/map only if the installed game exposes and permits a proven raid context; otherwise keep it at save-generation lifetime scope.
    - Add schema-13 migration, crash-safe persistence, UI, JSON, CSV, and independent capability diagnostics for crafting completion, produced-item quantity, and any proven recipe/workstation/context metadata. Pre-M13 crafting history remains explicitly unavailable rather than reconstructed from inventory state or unlocked recipes.
    - Acceptance covers single and batched outputs, multiple-output recipes where supported, cancellation/failure, delayed completion and collection, completion before inventory insertion, queue persistence across restart, overlapping callbacks, unknown/modded outputs, duplicate delivery, save-generation isolation, and independent capability degradation. Profile, UI, JSON, and CSV projections must agree.
    - The installed Duckov 2.3.30 contract has no persisted craft queue or separate collection lifecycle. The implemented adapter therefore wraps the private `Craft(CraftingFormula)` task and accepts one action only when it returns non-null after awaited `Cost.Return` delivery. It records the singular declared result amount as quantity, the numeric result ID, formula ID, and declared batch distribution; callback chunk count, inventory deltas, ingredients, and currency are never inferred. In-flight tokens are overlap-safe and consumed once. A null result, exception, or process-ending incomplete task records nothing. Workstation, run/map context, and multiple-output capabilities remain `DisabledIncompatible`; see [docs/M13_NATIVE_CONTRACTS.md](docs/M13_NATIVE_CONTRACTS.md).
    - Ordinary mod deactivation retains an incomplete native craft task and the profile coordinator for continuation and cleanup retry. Application quit instead abandons only still-unproven task tokens, flushes all already-proven aggregates, and lets the three-owner cleanup gate close the profile session normally; late continuations cannot touch the closed profile or fabricate a completion.
    - Schema 13 uses checked lifetime/output/recipe/batch aggregates and the existing deferred atomic profile writer, with explicit export/profile/save-transition/shutdown barriers and no fixed event-history ceiling. Capability publication commits generic adapter records and all lifetime metric-capability projections as one profile snapshot, preventing a partial degradation write from presenting skipped crafting capture as exact. `statistics.json`, `crafting_totals.csv`, `crafting_recipes.csv`, Overview, Crafting, and Diagnostics expose the same current capability and historical-availability semantics. Implementation and review history is retained in [PR #14](https://github.com/bamboechop/ultimate-duckov-statistics/pull/14), with immutable qualification evidence in [TESTING.md](TESTING.md).
    - User-controlled gameplay qualification passed on 2026-08-22. Crafting one Tierfalle, 30 Standard-Muni (S), and another Tierfalle produced exactly 3 actions and quantity 32, with exact output IDs, recipe IDs, and batch distributions agreeing across UI, schema-13 profile/backup, JSON, and both crafting CSVs. The export contained exactly 27 files, shutdown was clean, and no UDS error or transactional residue remained. The optional naturally failed request was not manually exercised; the installed null-return contract and deterministic tests cover that no-count boundary.
16. **M14 — Lossless weapon associations (`v0.14.0`, planned)**
    - Complete two independently capability-gated projections over stable weapon identity: accepted firing actions by shot-time weapon-ammunition pair, and active equipped duration by weapon-attachment membership. They share schema 14 and one delivery milestone but retain separate semantics, diagnostics, deterministic coverage, and manual acceptance.
    - Preserve the native shot-time relationship between the stable weapon identity and stable ammunition identity already carried by each accepted firing callback. Track accepted firing actions by weapon-ammunition pair; do not rename or infer them as rounds fired, loaded-ammunition consumption, inventory loss, or projectiles created. The installed Duckov 2.3.30 contract still leaves actual `AmmunitionUnitsConsumed` unsupported.
    - Add schema-14 checked aggregates for weapon-ammunition pairs at every scope where both independent firing aggregates are already truthful: save-generation lifetime, completed run, starting map, ordered route map, and route segment. Repeated visits to one map remain distinct segments while their map aggregate combines truthfully. Use compact aggregate keys rather than a raw per-shot journal, impose no fixed pair/event ceiling, and preserve crash-safe deferred persistence without synchronous disk I/O on the firing path.
    - Preserve unknown, modded, and partially available identities without guessing compatibility from caliber, weapon metadata, inventory state, or matching aggregate totals. A callback with only one proven identity continues to update that independently supported aggregate but cannot fabricate a pair. Historical pre-M14 weapon and ammunition totals remain exact while historical pairing is explicitly unavailable; do not reconstruct it from separate totals.
    - Expose a weapon-first projection that lets a consumer select one weapon and inspect each ammunition type observed with that weapon, with accepted firing-action count and percentage of that weapon's correlated actions. Keep the reverse ammo-to-weapon projection available to exports even if the temporary pre-overhaul panel presents only the weapon-first view. The full Duckov-native interaction and visual treatment remain M15 work.
    - Keep the M6 exact attachment-set signatures, loadout identities, transition history, recurring-loadout semantics, and durations. They remain the canonical joint identities and must not be replaced by marginal per-attachment totals. The current schema retains only an irreversible signature beside each exact-set interval, not a persisted signature-to-member catalog, so named attachment durations cannot be reconstructed from pre-M14 profiles and remain explicitly historically unavailable.
    - At the existing public item-tree observation boundary, retain structured attachment membership alongside each unchanged exact-set signature using stable weapon `Item.TypeID`, native attachment slot key/path, attachment `Item.TypeID`, and non-canonical display metadata. Track the time that the weapon is slotted while each named attachment is installed; this is equipped duration, not actively held/selected-weapon duration. Use compact checked aggregates or an equivalently lossless set-membership projection rather than an unbounded raw interval journal, and preserve the existing monotonic active-raid clock, pause/loading exclusion, event-driven invalidation, one-second reconciliation, and deferred persistence behavior.
    - Expose a weapon-first equipment projection listing total equipped time as the checked sum of that weapon identity's character-slot durations, the separate duration in each weapon slot, and named attachment duration grouped by Scope, Muzzle, Grip, Stock, Tactics, and Magazine. Native slot keys remain authoritative; preserve unknown or modded slot paths and attachment identities instead of forcing them into a built-in category. Do not show `No scope`, `No grip`, or other empty-slot duration unless the observed native item tree independently proves that exact slot exists and is empty; never infer an empty interval from the difference between weapon and named-attachment totals.
    - Publish the two association families' independent capability and historical-availability states consistently through profile, current UI model, Diagnostics, `statistics.json`, and dedicated CSV output. In a correlation-supported scope, summed weapon-ammunition pair counts by weapon and by ammunition must reconcile with the corresponding independently captured totals after excluding only events explicitly marked uncorrelated because one identity was unavailable. For each weapon and one attachment slot/path, named-attachment durations cannot exceed that weapon's equipped duration in the same scope; different attachment categories are simultaneous dimensions and are not summed together as a total.
    - Deterministic weapon-ammunition coverage includes one weapon with multiple ammunition types, one ammunition type used by multiple weapons, rapid weapon/ammunition switching, unknown and modded identities, identity enrichment, duplicate callbacks, high-rate and greater-than-100,000-action runs, repeated-map routes, checkpoint/restart/interruption recovery, save-slot/new-game/reset transitions, capability degradation, export/UI agreement, and shutdown cleanup. Manual acceptance uses at least one weapon with two ammunition types and confirms the selected-weapon breakdown across UI, JSON, CSV, persisted profile, restart, and clean shutdown.
    - Deterministic weapon-attachment coverage includes attachment replacement/removal, the same named attachment on multiple weapons, the same weapon identity in different character slots, nested and modded slot paths, display-name enrichment, pause/loading exclusion, repeated-map routes, checkpoint/restart/interruption recovery, save-slot/new-game/reset transitions, capability degradation, arithmetic limits, export/UI agreement, and shutdown cleanup. Manual acceptance changes at least one safely available attachment during a run, records approximate active intervals without treating wall-clock observation as exact native evidence, and confirms the resulting weapon, character-slot, exact-set, and named-attachment projections across UI, JSON, CSV, persisted profile, restart, and clean shutdown.
17. **M15 — Native UI overhaul (`v0.15.0`, planned)**
    - Redesign the complete panel against the stable M0-M14 data model. Match Duckov's discoverable font family, sizes, colors, frames, buttons, spacing, scroll behavior, focus/navigation, and interaction feedback without redistributing proprietary game assets.
    - Present Overview, Runs, Records, Combat, Equipment, Economy, Crafting, Items, and Diagnostics as a coherent information hierarchy. Add the filters, sorting, expandable detail, route presentation, ownership labels, unavailable/legacy states, and cross-tab terminology required to understand the data without changing metric semantics. In Combat, selecting a weapon shows only the ammunition types correlated to that weapon, with accepted firing-action counts and within-weapon percentages rather than misleading global ammunition totals. In Equipment, expanding a weapon shows total and per-character-slot equipped duration plus named attachment duration under Scope, Muzzle, Grip, Stock, Tactics, Magazine, or a truthful unknown/modded slot group; it does not relabel equipped duration as selected-weapon time or invent empty-slot history.
    - Show item images beside stable names where the native icon is available, with deterministic missing/modded-icon fallbacks. Treat the user's Photoshop mock-up as a design reference once supplied; verify every implemented control against native runtime behavior rather than copying pixels blindly.
    - Make native menu access the primary discovery path. Add a localized `Statistics` entry near the existing Mods/Settings controls on the main menu and near Settings in the base pause menu, reusing compatible native runtime styling and interaction behavior without redistributing proprietary assets. Both entries and the hotkey must open the same single panel instance and restore focus/navigation cleanly when it closes.
    - A main-menu entry without a valid selected save/profile must never expose another generation or invent an empty profile. Disable it or show a localized prompt to select or start a save. In base, bind the entry only to the active save generation.
    - Keep the configurable F8 binding, defaulting to F8, as a secondary shortcut and compatibility fallback. Prefer a stable public menu-registration path; otherwise use only the existing narrowly scoped, version-checked integration boundary. If menu integration is unavailable or Harmony is absent, UDS must continue through F8 and publish a visible Diagnostics capability/status instead of failing activation.
    - Keep every panel access path unavailable during raids. A raid pause menu must hide or disable the `Statistics` entry, while the configured hotkey retains the brief localized “available outside raids” response and never opens or duplicates the panel.
    - Preserve localization keys and English fallback, and prevent large run/item/equipment histories from creating unbounded layout or per-frame work through measured pagination, virtualization, or equivalent bounded rendering.
    - Acceptance includes screenshot and interaction review at representative resolutions and UI scales, keyboard/mouse navigation, main-menu access with and without a valid selected save, base pause-menu access, raid rejection, missing/incompatible menu-integration fallback, repeated open/close and setup/deactivation without duplicate entries or panels, long-history stress, empty/unavailable/error states, export/reset flows, and exact consistency with the underlying profile. No statistics-schema bump is required unless implementation introduces genuinely persisted data.
18. **M16 — Release hardening and v1.0 RC audit (`v1.0.0-rc.1`, planned)**
    - Freeze feature scope after M15. M16 owns compatibility, recovery, performance, packaging, documentation, installation/upgrade, accessibility/usability polish, and release defects only; new statistics or redesign work moves beyond v1.0.
    - Establish the final v1 persisted format as the only shipping baseline. Inventory and remove every schema-by-schema `0.x` migrator, legacy compatibility branch, compatibility-only serialized member, fixture, exact-migration test, and documentation path whose sole purpose is accepting unsupported M0-M15 data. Do not leave dormant readers or speculative migration infrastructure in the v1 binary for versions that no supported user could have installed.
    - Preserve only persistence behavior reachable for a real v1 user: clean first installation, reinstallation with an existing current-format v1 profile, current-schema validation and normalization, primary/backup/temporary recovery, interrupted-write recovery, save-generation isolation, reset/export behavior, and safe rejection of incompatible or future formats. Future supported-version migrations are added when a supported predecessor actually exists.
    - Treat every pre-v1 UDS profile as unsupported development data rather than an upgrade source. Document a clear non-destructive tester transition to a fresh v1 profile, verify that incompatible pre-v1 data cannot be silently misread as current data, and never modify Duckov save files while cleaning up or replacing UDS-owned state.
    - Run fresh independent reviews across M0-M15 rather than treating prior milestone re-reviews as certification. A reported P1/P2 must include a feasible Duckov production path from native event or user action to observable impact; purely theoretical state-machine interleavings are retained as hardening notes unless runtime reachability is demonstrated.
    - Audit clean installation, current-format v1 reinstallation, explicit pre-v1 incompatibility handling, primary/backup/temporary recovery, interruption and save-generation rotation, long multi-map and greater-than-100,000-event runs, capability degradation, duplicate setup/cleanup, UI/profile/JSON/CSV agreement, and the accepted M8.1 performance matrix on the final binary. Add a source/package audit proving that historical `0.x` migration code and compatibility-only artifacts are absent.
    - Re-run the installed-game compatibility probe, complete Debug/Release suites, source/package audits, independent extraction, exact deployment readback, cold activation, manual fresh/progressed-save matrix, export, and residue-free shutdown. Complete installation, privacy/local-data, limitations, troubleshooting, release automation, and Workshop-readiness documentation; Workshop upload still requires explicit user authorization.
    - Make release outputs privacy-clean and reproducible. Configure deterministic path mapping so PE/portable-PDB metadata and stack-trace source references do not embed the builder's Windows username, repository root, Duckov installation path, or any other absolute host path; retain useful normalized source identities where practical. Add an automated post-build/package scan that fails when either DLL or any packaged file exposes build-machine paths, and record that gate in release evidence.
    - Publish the first fully accepted candidate as `v1.0.0-rc.1`. Any behavior-changing correction creates `rc.2`, `rc.3`, and so on and repeats every affected automated, gameplay, performance, migration, and packaging gate. Promote an accepted RC source to `v1.0.0` only after final version/package/readback checks; repeat manual gameplay only when the promoted source changes behavior.

Each milestone updates the capability matrix and is manually tested before the next begins.

The post-M9 order is intentional: first remove the remaining data-loss boundary, then correct combat meaning, then implement the coupled world-time/sleep lifecycle, then implement the independent crafting lifecycle, then preserve both shot-time weapon-ammunition and equipped-time weapon-attachment associations before redesigning every presentation surface. This leaves M16 with a feature-frozen data model and gives each remaining native-contract family its own schema, review, manual acceptance, and minor pre-release.

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
