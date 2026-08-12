# Ultimate Duckov Statistics v0.5.0 — pre-release draft

This draft describes M5 combat attribution. M0-M4 are already merged and published through v0.4.0. The complete v0.5.0 user-controlled gameplay and approved deployment gates passed, and a subsequent review follow-up corrected seven release-blocking findings. The corrected package, deployment/readback, focused native verification, and clean-shutdown gates also pass. The branch and PR intentionally remain draft and unmerged; do not publish, tag, merge, mark ready, or upload to Steam Workshop without a later explicit request.

## Included in v0.5.0

- Everything released through v0.4.0: consumables, exact attributed healing, run lifecycle and records, map/movement aggregation, accepted firing actions, and event-time weapon/ammunition identity.
- Actual HP loss measured around `Health.Hurt`. Requested damage, rejected callbacks, `finalDamage`, and overkill are not reported as applied damage.
- Main-duck damage dealt and received; compatible completed-projectile ranged hits/accuracy; accepted melee swings and one hit per melee damage scope; enemy kills; and player deaths.
- Exact ownership categories for the main duck, the native built-in pet/master chain, environmental damage, and unknown/unproven actors. Arbitrary player-team actors are not promoted to pets or player ownership.
- Stable target/killer identities from `CharacterRandomPreset.nameKey`, with preset-name and character-name fallback IDs for unknown or modded content. The verified Zombie flag supplies the available broader family; all others remain explicitly unknown.
- Direct, player-applied tick/update damage-over-time, generic effect, explosion, real-damage, and environmental cause categories. Generic effect damage is not mislabeled as DoT.
- Event-time projectile weapon and ammunition snapshots. Delayed/unrelated damage that lacks an ammunition chain retains an unknown ammunition identity.
- Independently proven native head-targeted projectile hits and their fatal subset. Critical hits alone never count as headshots.
- Schema 5 lifetime, per-map, and per-run aggregates plus enemy, killer, family, cause, weapon, ammunition, and ownership breakdowns. M1-M4 data is preserved exactly; historical M5 starts unavailable and empty.
- Bounded event IDs and projectile correlations, saturating non-negative arithmetic, nested repair provenance, exact-once interrupted-run recovery, and one-second coalesced high-frequency combat checkpoints.
- Combat and Runs UI extensions, JSON state, and a new `combat_attribution.csv`; all expose capability state instead of presenting unsupported history as zero.

## Native compatibility boundary

The verified baseline is Escape From Duckov `2.3.30`, Steam build `24013657`, Unity `2022.3.62f2`, and separately installed HarmonyLib `2.4.1.0`.

M5 uses the public main-character death and accepted melee-action callbacks where they are sufficient. Minimal version-checked Harmony scopes are used only where the public callbacks cannot prove the requested semantics:

- `Health.Hurt(DamageInfo)` prefix/postfix: before/after HP and fatal transition.
- `Projectile.Init(ProjectileContext)` postfix: unique projectile, physical source, head-target flag, weapon, and ammunition snapshot.
- `Projectile.Update()` prefix/finalizer: direct and explosion damage correlation.
- `Projectile.Release()` prefix: compatible completed-projectile denominator and one-hit numerator commit.
- `ItemAgent_MeleeWeapon.CheckCollidersInRange(bool)` prefix/finalizer: one melee damage scope across multi-target callbacks.
- `ItemStatsSystem.Effect.Trigger(EffectTriggerEventContext)` prefix/finalizer: exact TickTrigger/UpdateTrigger damage-over-time proof.

Each method is checked for an exact safe patch set at activation and periodically. Combat uses a separate Harmony owner from healing. Callbacks become inert before cleanup, failed unpatch retains the exact owner for retry, and same-process replacement is blocked until cleanup succeeds. No Harmony or Duckov DLL is bundled.

## Exact metric semantics and limitations

- Ranged accuracy = unique completed exact-main-duck projectiles that caused positive actual enemy HP loss / completed exact-main-duck projectiles. Penetration, pellets, explosions, and repeated callbacks cannot count a projectile, headshot, or headshot final blow more than once. It is deliberately not based on M4 firing actions.
- A projectile that has not reached `Projectile.Release` before run termination enters neither numerator nor denominator for that run.
- Headshot = positive enemy HP loss from an exact-player projectile whose native `AimingEnemyHead` flag was independently true at projectile initialization. This is a native head-target claim, not a reconstructed geometric impact point. Unsupported input paths remain uncounted.
- `DamageInfo.crit` is retained only as native context and is never headshot proof.
- Loaded-ammunition units consumed remain unavailable under the M4 public firing contract. M5 can prove event-time ammunition identity for projectile-correlated damage, not consumption.
- Enemy family is exact only for Zombie versus unknown on this verified contract.
- Combat writes are coalesced to approximately one second. An abrupt process or OS failure can lose up to that interval; orderly terminal and shutdown paths flush the current in-memory aggregate.

## Validation status

- Starting baseline: `origin/main` at `261a1e1668536aa1aa77868753add3269a90bd30`.
- Release suite: 216 tests pass, including the complete M1-M4 regression suite and new actual/overkill, relevant-combat filtering, friendly-fire exclusion, ownership, DoT, stable fallback identity, outcome identity retention, projectile lifecycle isolation, hook-specific capability degradation, ranged/melee, event/death-callback deduplication, multi-damage headshot, owner-isolated Harmony cleanup, semantic active-checkpoint backup recovery, migration, nested-root repair, normalization, overflow, pristine-profile/historical capability boundaries, UI/export, and capability-monotonicity coverage.
- Native Release solution build: 0 warnings and 0 errors.
- Expanded native contract probe: passes against the versions and assembly hashes documented in `TESTING.md`.
- `git diff --check`: passes.
- Hardened implementation commit `8029a44357f49d63e96c8ada79f16a9c423e834d` produced a 139,860-byte ZIP at SHA-256 `a31c3915b6c5fd5a95253a4894294c81009a2771bbb7800ea885f9e9a107b911`. Its sidecar, five-file inventory, independent extraction, forbidden-dependency audit, byte comparison, and both `0.5.0+8029a44357f49d63e96c8ada79f16a9c423e834d` DLL product versions pass.
- Draft PR [#5](https://github.com/bamboechop/ultimate-duckov-statistics/pull/5) is open, draft, and unmerged. The initial gameplay-tested artifact is superseded. The independently verified corrected package, approved game-directory redeployment/readback, focused native outcome-identity and relevance verification, clean shutdown, and corrective-head CI pass. No tag or GitHub release exists.
