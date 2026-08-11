# Ultimate Duckov Statistics v0.4.0 — pre-release draft

This draft describes M4 weapons and ammunition. The first v0.4.0 artifact completed automated, package, deployment, gameplay, and draft-PR CI gates, but later review found merge-blocking native-contract, event-identity, crash-recovery, persisted-data, and documentation defects. That artifact is superseded. A corrected artifact must complete the replacement validation gates before release. Do not publish, tag, merge, mark ready, or upload to Steam Workshop without an explicit later request.

## Included in v0.4.0

- Everything released in v0.1.0 through v0.3.0: consumables, healing attribution, run lifecycle, records, maps, movement, persistence, and integrity handling.
- A normalized `ShotRecorded` contract with save-generation, run, map, gameplay, integrity, version, stable weapon, and stable ammunition context captured at event time.
- Three deliberately separate capability-gated metrics: accepted firing actions, loaded ammunition units consumed by firing, and native projectiles/pellets created. Duckov 2.3.30 currently proves only firing actions through its public event; the other two remain unavailable.
- Lifetime, per-map, per-run, per-weapon, and per-ammunition aggregates with stable IDs and fallback display names for unknown or modded content.
- Exact-main-duck filtering: base, loading, pause, no-active-run, pets, companions, NPCs, and unrelated projectile sources do not enter player weapon totals.
- A unique per-callback event-ID source and bounded run-level event-ID deduplication; reload-equivalent post-shot ammunition values and infinite-ammunition weapons cannot collapse legitimate callbacks.
- Every accepted firing action immediately flushes the aggregate active-run checkpoint, while a failed flush remains dirty for retry. No write occurs per projectile or pellet.
- Nested persisted combat state is normalized before cloning; negative counters are rejected or repaired at persistence boundaries, and non-negative additions saturate at `long.MaxValue` instead of wrapping.
- A process-lifetime weapon subscription owner that blocks replacement activation until failed cleanup succeeds and makes retained post-disposal callbacks inert.
- Schema-4 migration that preserves M1-M3 data and initializes M4 history empty without reconstruction. Interrupted active-run checkpoints retain already-recorded M4 aggregates exactly once.
- Combat UI with explicit metric semantics, capability states, lifetime totals, weapon/ammunition tables, and per-run context. Overview includes the lifetime firing-action total.
- JSON plus ten CSV files. New flattened files are `combat_totals.csv`, `weapon_totals.csv`, and `ammunition_totals.csv`.

## Native compatibility boundary

M4 uses the verified public static `ItemAgent_Gun.OnMainCharacterShootEvent` on Duckov `2.3.30`. The event proves one accepted main-character firing callback and supplies the firing gun for event-time identity. It does not prove ammunition or projectile outcomes: `ItemSetting_Gun.UseABullet` can return without decrementing when no valid loaded item exists, and `ShootOneBullet` can return before projectile acquisition while the later firing event still occurs.

Reloads, magazine transfers, and inventory movement do not emit this event. Dry-fire trigger attempts also do not emit it. Trigger attempts, actual ammunition consumption, and completed projectile creation are unavailable rather than fabricated from post-shot ammunition or configured `ShotCount`. M4 adds no Harmony patches and never modifies weapon, ammunition, projectile, timing, argument, return, or game state.

## Compatibility

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- HarmonyLib `2.4.1.0` only for the existing M2 healing attribution
- Windows, single-player only

## Validation status

- Focused M4 acceptance: passed.
- Corrective local Release suite: 172 tests pass with the complete M0-M3 regression suite and new reload-equivalent identity, unavailable-outcome, crash-checkpoint, nested-normalization, negative-counter, and overflow regressions.
- Native Duckov/Harmony contract probe: passed against Duckov `2.3.30`, Steam build `24013657`, Unity `2022.3.62f2`, and HarmonyLib `2.4.1.0`. The corrected probe requires only the public firing event and stable identity properties; it no longer treats private firing loops or loaded-ammunition methods as proof of outcomes.
- Native Release build: 0 warnings and 0 errors.
- M4 change-set formatting and analyzer verification: passed. The repository-wide formatting command continues to report two pre-existing whitespace blocks in untouched legacy test files.
- Corrected committed-head package construction, transactional deployment, and focused manual recovery acceptance passed. Two reload-equivalent TT-33 callbacks remained distinct, survived a forced `taskkill /F` as one interrupted run with exactly two firing actions, and kept ammunition/projectile outcomes unavailable across UI, profile, JSON, and CSV. Final remote CI remains pending; the PR stays draft.

## Known limitations

- M4 records accepted native firing callbacks, not trigger attempts. Dry fire is unavailable and not counted.
- Actual loaded-ammunition consumption and completed projectile creation are unavailable on the public Duckov `2.3.30` contract. The normalized schema retains those independent fields so a future proven native outcome hook can enable them without redefining firing actions.
- Weapon/ammunition statistics begin with v0.4.0; schema-3 migration does not reconstruct historical firing.
- F8 remains unavailable during raids; inspect Combat after leaving the raid.
- Damage, accuracy, hits, kills, deaths, melee, critical/headshot attribution, equipment duration/loadouts, and later-milestone systems remain intentionally out of scope.
