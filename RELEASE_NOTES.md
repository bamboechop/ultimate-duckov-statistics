# Ultimate Duckov Statistics v0.6.0 — pre-release draft

This draft describes M6 equipment and totem statistics. M0-M5 are published through the v0.5.0 GitHub pre-release. M6 remains an unmerged draft pre-release candidate: do not publish, tag, merge, mark ready, or upload it to Steam Workshop without a later explicit request.

## Included in v0.6.0

- Monotonic active-raid duration for each equipped item/slot/attachment signature.
- Selected-weapon duration with both canonical weapon identity and exact character-slot identity, including correct distinction between identical weapon types in different slots.
- Deterministic loadout identities built from stable slot keys, `Item.TypeID` values, and bounded recursive attachment metadata. Runtime object IDs and mutable/localized names are display-only.
- Direct slotted totems recorded as proven active when their public durability contract permits activation, or proven inactive when depleted.
- Totems inside the version-checked `Item_ToteBag` inventory recorded as present with activation `Unknown`.
- Active-totem-set duration excludes unknown tote activation. Tote activation remains `DisabledIncompatible` and cannot silently produce active statistics.
- Event-time loadout, selected-weapon/slot, and active-totem-set temporal associations for firing actions and M5 combat outcomes. They do not claim equipment caused an outcome. Projectile completion retains the association captured at projectile initialization.
- Per-run loadout state and bounded 256-transition history. Lifetime recurring-loadout rankings require observation in at least two completed runs.
- Schema 6 migration that preserves M1-M5 exactly and marks historical equipment/totem data unavailable rather than reconstructing it.
- Crash-safe active-run equipment checkpoints using the existing one-second mutation checkpoint coalescing and five-second fallback.
- Equipment panel plus `equipment_totals.csv`, `recurring_loadouts.csv`, and `equipment_combat.csv`; the JSON export carries the complete schema-6 structure.

## Native contract and truth boundary

M6 uses public Duckov 2.3.30 contracts only:

- `CharacterMainControl.CharacterItem.Slots` for direct character-slot contents.
- `CharacterMainControl.OnMainCharacterSlotContentChangedEvent` for equipment changes.
- `CharacterMainControl.CurrentHoldItemAgent` and `OnMainCharacterChangeHoldItemAgentEvent` for selection.
- `Item.onItemTreeChanged`, `Item.Slots`, and `Item.TypeID` for attachment-aware state.
- the exact `Totem` tag for totem identity.
- `Item_ToteBag` plus its attached public `Inventory.Content` for tote presence.

The public item-effect control flow proves direct slotted activation subject to durability. It does not prove tote-contained activation because individual modifiers/effects may opt into inventory activation independently. Therefore no tote buff/effect total is emitted in this candidate.

## Compatibility and migration

- Verified baseline: Duckov `2.3.30`, Steam build `24013657`, Unity `2022.3.62f2`.
- M6 adds no Harmony patch. Existing M2/M5 Harmony requirements and safe degradation remain unchanged.
- Schema-5 and older profiles migrate to schema 6 without reconstructing equipment history. Historical equipment capability provenance explicitly says it predates M6.
- Missing/partial current equipment roots are normalized with repair provenance. Semantically invalid active-run equipment checkpoints are rejected so the atomic backup recovery path can select a valid candidate.

## Known limitations

- Statistics begin when their milestone is installed; no game-save history is reconstructed.
- Tote activation is disabled and unknown. Presence in a tote never implies active modifiers or effects.
- A sudden process or OS failure can lose up to approximately one second of newly accepted equipment/combat state because mutation checkpoints are intentionally coalesced. Orderly shutdown and run completion flush state.
- Recurring loadouts require two completed run occurrences. One-off loadouts remain available in their per-run summaries but are omitted from lifetime ranking output.
- M4 loaded-ammunition consumption and configured projectile creation remain unavailable under the public firing callback; M6 associations do not upgrade those unsupported metrics.
- UDS remains a local GitHub package. Only HarmonyLib is supplied through Steam Workshop.

## Acceptance state

Automated tests, native contract probe, native build, formatting, package/extraction audit, deployment readback, and the manual gameplay matrix must be recorded in `TESTING.md`. This draft is not release-ready until every required gate has current evidence and the draft PR points at the exact validated head.
