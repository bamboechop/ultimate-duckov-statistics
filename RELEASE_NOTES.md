# Ultimate Duckov Statistics v0.3.0 — pre-release draft

This draft describes the planned GitHub pre-release. M3 implementation and its complete manual gameplay matrix have passed; this delivery intentionally stops at a green unmerged draft PR, so do not publish, tag, merge, or mark it ready without an explicit later request.

## Included in v0.3.0

- Everything in v0.1.0 consumable usage and v0.2.0 healing attribution.
- Reliable run start only after native raid initialization and actual main-duck player control.
- Exactly-once Extracted, Died, and Interrupted outcomes across duplicate or reordered native callbacks.
- Monotonic active gameplay duration excluding pause and loading, with wall-clock duration retained diagnostically.
- Stable run IDs, native raid context, stable map identity with explicit unknown fallback, version, capability, and integrity context.
- Indefinitely retained compact run summaries and exactly-once interrupted-checkpoint recovery after abrupt termination.
- Shortest and longest extraction and death active-duration records overall and per map, with deterministic duration/start-time/run-ID tie handling.
- Main-duck-only movement sampled at approximately 5 Hz. Plausibility uses verified native walk/run/dash speed, actual monotonic elapsed time, a 1.75 conservative multiplier, and 0.35 m additive tolerance.
- Separate physical and teleport/excluded distance, including explicit position changes, loading/resume boundaries, implausible movement, and valid displacement after gaps longer than two seconds.
- Overview additions plus enabled Runs and Records tabs with per-map context, explicit unsupported movement state, and visible per-run integrity/record-eligibility reasons.
- Schema-3 atomic persistence and migration preserving M1/M2 generations, activation/amount/group/healing aggregates, capability records, and read-only archives without reconstructing historical runs.
- JSON plus eight-file flattened export set: `statistics.json`, `overview.csv`, `groups.csv`, `items.csv`, `runs.csv`, `run_totals.csv`, `map_totals.csv`, and `records.csv`.

## Native compatibility boundary

M3 uses only verified public Duckov `2.3.30` lifecycle, loading, pause, map, main-character position, and movement-speed APIs. It adds no Harmony patches and performs no global scene/object scan. A capability failure is visible in Diagnostics and disables the affected reporting path rather than fabricating zeroes.

Healing retains the separately installed [HarmonyLib Workshop item 3589088839](https://steamcommunity.com/sharedfiles/filedetails/?id=3589088839), verified as `2.4.1.0`. UDS never bundles `0Harmony.dll`. The infrastructure-only `HarmonyLoadMod` is allowed by the run-integrity policy; cheats/custom difficulty and other active gameplay mods remain tagged and excluded from default duration records.

## Compatibility

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- HarmonyLib `2.4.1.0` for healing attribution
- Windows, single-player only

## Validation status

- Automated Release suite: 140 tests pass, including complete M0-M2 regression coverage and deterministic lifecycle, exact-main-subject movement cadence/map boundaries, records, migration, checkpoint recovery before identity rotation, read-only preservation, isolation, UI/export agreement, cumulative runtime integrity, retained-owner cleanup retry, capability degradation, deployment, and package cases.
- Native Duckov/Harmony contract probe: passed against the versions above, including exact M3 event/property/method/field visibility and signatures for runtime cheat/rule integrity changes.
- Native Release build: 0 warnings and 0 errors.
- Exact five-file install package validation: passed; no Duckov, Unity, framework, or Harmony dependency is bundled.
- Manual M3 deployment/gameplay acceptance: passed on approved, read-only-backed-up slots 1 and 6, covering schema migration, no base run, extraction, death, active-time pause exclusion, stationary and normal movement, genuine teleport/loading separation, map aggregation, exactly-once hard-crash recovery, restart persistence, UI/export agreement, and clean shutdown without M1/M2 regression.
- The final review-hardened archive was built from fix commit `22258f4bcdf7a430f78eb4518f91953cf5120d74`. `UltimateDuckovStatistics-v0.3.0.zip` is 92,145 bytes with SHA-256 `76a28f4c5b8a6ed6a73ef847e8e4d761236536a5c4d3a26019cbaa64a2869982`; its lowercase sidecar matches exactly, independent extraction/package validation passes, and both DLLs report informational version `0.3.0+22258f4bcdf7a430f78eb4518f91953cf5120d74`.
- Draft-PR CI currency remains the final delivery gate and is recorded on the draft PR after the remote state exists.

## Known limitations

- Statistics begin when the corresponding UDS version is installed; historical healing, runs, duration records, and movement are not reconstructed.
- F8 does not open UDS during raids.
- UDS itself is distributed as a local GitHub package, not a Steam Workshop upload.
- Run compatibility is verified for Duckov `2.3.30`; changed native contracts are reported as unsupported until revalidated.
- Valid displacement across pause, loading, explicit teleport, object replacement, or a long sampling gap is deliberately excluded from physical distance and retained as teleport/excluded distance when it can be measured.
