# Ultimate Duckov Statistics v0.7.0 — pre-release candidate

This draft describes M7 unique-container statistics. M0-M6 are merged through v0.6.0. M7 remains an unmerged draft pre-release candidate: do not publish, tag, merge, mark ready, or upload it to Steam Workshop without a later explicit request.

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
- UDS remains a local GitHub package. Only HarmonyLib is supplied through Steam Workshop.

## Acceptance state

Focused M7 regressions, the complete Release suite, native contract probe, warning-free native build, formatting, source safety, package/extraction audit, deployment readback, and the user-controlled gameplay matrix are recorded in `TESTING.md`. This candidate must remain draft until every gate is current and the draft PR points at the exact validated head.
