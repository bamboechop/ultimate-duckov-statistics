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
- `v0.5.0` is a GitHub pre-release candidate containing M5 actual damage, reliable projectile accuracy, melee, kills/deaths, ownership, identities, causes, damage-over-time, and independently proven head-target attribution after its complete manual acceptance matrix passes.
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
- Maintain shortest and longest extraction and death times overall and per map.
- Record all compact run summaries indefinitely.
- Track main-duck physical movement at approximately 5 Hz.
- Exclude loading and implausible locomotion deltas from traveled distance.
- Classify excluded deltas as teleport distance.
- Derive the plausibility threshold from known movement speed, elapsed sample time, and a conservative tolerance rather than a fixed universal distance.

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
6. **M5 — Damage, kills, deaths, melee, and headshots (`v0.5.0`, initial manual acceptance passed; corrective review follow-up pending final delivery gates)**
    - Actual HP loss from version-checked `Health.Hurt` pre/post state, never requested damage or `DamageInfo.finalDamage`.
    - Compatible ranged accuracy: unique exact-main-duck projectiles that cause positive enemy HP loss divided by exact-main-duck projectiles that reach verified `Projectile.Release` while the run is active.
    - Accepted melee swings from `CA_Attack.OnAttack`; one melee hit per damage scope even for repeated collider callbacks.
    - Enemy kills and player deaths from actual fatal health transitions, with raid death deferred until the richer main-character death evidence and `Health.Hurt` postfix are recorded.
    - Exact player, built-in pet/master-chain, environmental, and unknown ownership; stable preset-key identities with visible unknown/modded fallbacks.
    - Direct, repeated tick/update damage-over-time, generic effect, explosion, real-damage, and environmental causes.
    - Event-time weapon identity for damage and projectile-init ammunition identity where exposed; uncorrelated ammunition remains unknown rather than inferred.
    - Headshots only for independently observed native head-targeted exact-player projectiles. `DamageInfo.crit` is ignored as headshot evidence; headshot final blows are a separate fatal subset.
    - Schema 5, bounded 2,048-event/run deduplication and 2,048-projectile correlation, one-second combat-checkpoint coalescing, lifetime/map/run aggregation and breakdowns, UI, JSON, and `combat_attribution.csv`.
7. **M6 — Equipment and totems**
    - Slot duration, selected weapons, loadouts, combat associations, experimental tote activation.
8. **M7 — Containers**
    - Unique non-corpse container looting.
9. **M8 — Economy**
    - Money/cash flows, sources, and raid cash outcomes.
10. **M9 — Full UI and release hardening**
    - Remaining tabs, filters, compatibility matrix, performance verification, migration tests, documentation, and Workshop-readiness assessment.

Each milestone updates the capability matrix and is manually tested before the next begins.

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
