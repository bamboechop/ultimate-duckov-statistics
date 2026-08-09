# Ultimate Duckov Statistics v0.2.0 — pre-release draft

This draft describes the planned GitHub pre-release. Do not publish or merge it until the automated suite, manual healing matrix, package audit, and checksum verification pass.

## Required dependency

Install and enable [HarmonyLib Workshop item 3589088839](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839). The verified build is `2.4.1.0`. UDS discovers Harmony at runtime and never bundles `0Harmony.dll`.

If Harmony is missing, too old, its reflection contract changes, a required Duckov method changes, or a foreign transpiler touches one of the three attribution hooks, UDS leaves consumable-use tracking available but disables healing attribution and reports the reason in Diagnostics. Loader order is handled by a bounded retry after the Workshop loader makes Harmony available; late transpiler conflicts are rechecked while active.

## Included in v0.2.0

- Everything in the v0.1.0 consumable-usage MVP.
- Actual HP restored to the main duck, attributed to the successful source item use.
- Immediate and delayed buff/effect healing.
- Exact per-application clamp calculation, excluding nominal overheal.
- Exclusion of base use, failed or cancelled uses, unrelated regeneration, and non-main-player targets.
- Deterministic handling of overlapping buffs, refreshed same-ID buffs, duplicate callbacks, restarts, and expired incomplete correlations.
- Schema-2 migration that preserves every v0.1.0 activation and amount while initializing historical healing to zero.
- Actual healing in Overview, group totals, item rows, JSON, and flattened CSV exports.
- A bounded capability record and diagnostics for Harmony and native contract degradation.

## Native attribution boundary

The approved observer-only Harmony integration patches exactly these Duckov `2.3.30` methods:

- `Health.AddHealth(float)` for the exact clamped HP application.
- `EffectAction.NotifyTriggered(EffectTriggerEventContext)` for delayed effect provenance.
- `CharacterBuffManager.AddBuff(Buff, CharacterMainControl, int)` for buff ownership and refresh provenance.
- Delayed healing buffs are classified as Healing even when the item also changes hydration; pre-release schema-2 profiles are repaired without changing their generation or totals.

The patches do not alter arguments, return values, game state, or Duckov saves. UDS uses public item-use completion as the proof that the source use succeeded before committing buffered immediate healing.

## Compatibility

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- HarmonyLib `2.4.1.0`
- Windows, single-player only

## Validation status

- Automated Release suite: 88 tests passed after the delayed-healing canonical-group repair.
- Native Duckov/Harmony contract probe: passed against the versions above, including exact method visibility/signatures and Harmony reflection members.
- Native build: 0 warnings and 0 errors; the exact five-file package audit passed.
- Deployed repaired candidate: all five SHA-256 hashes match the audited package; no staging or backup residue remains.
- Progressed-save migration preserved the generation and prior usage totals. Gameplay passed exact immediate healing (12 HP), clean delayed healing (30 x 2 HP), partial overheal (0.612381 HP), successful full-health/base use, cancellation, damage interleaving, and unrelated totem regeneration.
- Restart persistence, final JSON/CSV consistency, and normal-shutdown cleanup passed with exact 6-use/132.61238098144531-HP agreement, matching atomic profiles, and no checkpoint or temporary residue.
- Final 62,435-byte folder-rooted ZIP and lowercase SHA-256 sidecar: independently verified; SHA-256 `700ae5372060b6d191a579b633d80cd96e4bf678257b951ac9765c9fd4102e28`.
- Draft PR #2 targets `main`, remains unmerged, and its required `source-safety` and `core` checks are green on the current branch head.

## Known limitations

- Statistics begin when UDS is installed; historical healing is not reconstructed.
- F8 does not open UDS during raids.
- Only Overview, Items, and Diagnostics are enabled.
- UDS itself is distributed as a local GitHub package, not a Steam Workshop upload.
- Healing attribution is conservatively disabled when its exact compatibility boundary cannot be proven.
