# Ultimate Duckov Statistics v0.4.0 — pre-release draft

This draft describes M4 weapons and ammunition. Automated and native-contract validation passes; deployment, user-driven gameplay acceptance, final package evidence, and draft-PR CI remain pending. Do not publish, tag, merge, mark ready, or upload to Steam Workshop without an explicit later request.

## Included in v0.4.0

- Everything released in v0.1.0 through v0.3.0: consumables, healing attribution, run lifecycle, records, maps, movement, persistence, and integrity handling.
- A normalized `ShotRecorded` contract with save-generation, run, map, gameplay, integrity, version, stable weapon, and stable ammunition context captured at event time.
- Three deliberately separate metrics: successful firing actions, loaded ammunition units consumed by firing, and native projectiles/pellets created.
- Lifetime, per-map, per-run, per-weapon, and per-ammunition aggregates with stable IDs and fallback display names for unknown or modded content.
- Exact-main-duck filtering: base, loading, pause, no-active-run, pets, companions, NPCs, and unrelated projectile sources do not enter player weapon totals.
- A bounded firing-correlation cache and bounded run-level event-ID deduplication; no raw shot journal, scene-wide scan, inventory-wide scan, repeated hot-path reflection, or per-projectile disk write.
- A process-lifetime weapon subscription owner that blocks replacement activation until failed cleanup succeeds and makes retained post-disposal callbacks inert.
- Schema-4 migration that preserves M1-M3 data and initializes M4 history empty without reconstruction. Interrupted active-run checkpoints retain already-recorded M4 aggregates exactly once.
- Combat UI with explicit metric semantics, capability states, lifetime totals, weapon/ammunition tables, and per-run context. Overview includes the lifetime firing-action total.
- JSON plus ten CSV files. New flattened files are `combat_totals.csv`, `weapon_totals.csv`, and `ammunition_totals.csv`.

## Native compatibility boundary

M4 uses the verified public static `ItemAgent_Gun.OnMainCharacterShootEvent` on Duckov `2.3.30`. `ItemAgent_Gun.TransToFire` returns before that event on empty ammunition or failed durability, creates one projectile per native `ShotCount`, consumes one loaded ammunition item through `ItemSetting_Gun.UseABullet`, and then emits the main-character firing event once per successful discharge. Semi-automatic, repeated automatic, and burst fire therefore produce one firing action per discharged round; one shotgun discharge remains one firing action and one ammunition unit but may create multiple projectiles.

Reloads, magazine transfers, and inventory movement do not emit this event. Dry-fire trigger attempts also do not emit it, so trigger-pull and dry-fire counts are unavailable rather than fabricated as zero. M4 adds no Harmony patches and never modifies weapon, ammunition, projectile, timing, argument, return, or game state.

## Compatibility

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- HarmonyLib `2.4.1.0` only for the existing M2 healing attribution
- Windows, single-player only

## Validation status

- Focused M4 acceptance: passed.
- Complete Release suite: 163 tests pass with the complete M0-M3 regression suite.
- Native Duckov/Harmony contract probe: passed, including exact public firing event, exact private firing/projectile methods, loaded-ammunition method, stable identity properties, ownership path, visibility, and signatures.
- Native Release build: 0 warnings and 0 errors.
- M4 change-set formatting and analyzer verification: passed. The repository-wide formatting command continues to report two pre-existing whitespace blocks in untouched legacy test files.
- Package construction, independent extraction/checksum audit, deployed hashes, manual gameplay evidence, final remote CI, and final package metadata: pending.

## Known limitations

- M4 records successful firing actions, not trigger attempts. Dry fire is unavailable and not counted.
- Duckov `2.3.30` consumes one loaded ammunition unit per accepted gun discharge. The normalized schema keeps ammunition consumption independent so a future proven native weapon contract can report a different value without redefining firing actions.
- Weapon/ammunition statistics begin with v0.4.0; schema-3 migration does not reconstruct historical firing.
- F8 remains unavailable during raids; inspect Combat after leaving the raid.
- Damage, accuracy, hits, kills, deaths, melee, critical/headshot attribution, equipment duration/loadouts, and later-milestone systems remain intentionally out of scope.
