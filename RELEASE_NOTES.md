# Ultimate Duckov Statistics v0.1.0 — pre-release draft

This draft describes the planned GitHub pre-release. Do not publish it until the automated suite, both manual save scenarios, package audit, and checksum verification pass.

## Important: activate UDS on every cold launch

Duckov `2.3.30` retains the enabled preference for local mods but, on the verified setup with no Duckov Workshop subscriptions, does not automatically activate them after restart. Before selecting a save on every cold launch:

1. Open **Mods**.
2. If the left UDS indicator is unchecked, click it exactly once.
3. Confirm the check mark appears and return to the main menu.

This is an explicitly accepted v0.1.0 workaround for a Duckov loader edge case. UDS does not add Harmony or an unrelated Workshop dependency to bypass it.

UDS fingerprints the selected save read-only and stores the fingerprint only in its own external profile. Duckov's public pre-save event records a short-lived expected-save marker there, allowing a normal save completed immediately before a crash to retain its UDS generation. The marker is cleared after a later observation or clean shutdown and expires after 30 seconds. If a save changes while UDS is inactive, its next active launch archives the previous UDS generation instead of risking a silent merge with a reused slot. Activate UDS before selecting a save on every launch to preserve continuous statistics.

## Included in v0.1.0

- Successful main-duck consumable activations in raids.
- Separate activation and stack/durability/item-amount totals.
- Deterministic Healing, Food, Drink, Stimulant/Buff, Remedy/Debuff Removal, Special, and Other/Unknown groups.
- Multi-effect tags without group-total double-counting.
- Per-save-generation storage outside Duckov save files.
- Overview, Items, and Diagnostics views outside raids, with configurable F8 access.
- JSON and flattened CSV exports.
- Atomic profile writes, backup recovery, bounded diagnostics, and generation archives.
- Read-only SHA-256 and `SaveTime` continuity checks, plus a native pre-save intent checkpoint that distinguishes interrupted normal saves from slot reuse even when Windows creation timestamps remain unchanged.
- Future-schema profiles are preserved byte-for-byte in read-only archives and are never downgraded or overwritten.
- Exact five-file staged deployment replaces stale mod contents and rejects game, Unity, framework, and Harmony assemblies; failure to clean an already-replaced backup cannot roll back the verified new deployment.

## Compatibility

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- Windows, single-player only

## Validation

- 47 Release tests pass, including profile-schema safety, interrupted native-save continuity, stale-intent rejection, group/export invariants, committed deployment cleanup failure, and capability carryover.
- The native contract probe passes against Duckov `2.3.30`, Steam build `24013657`, and Unity `2022.3.62f2`.
- The progressed slot-1 matrix passed base exclusion, cancellation exclusion, two-group raid use, amount tracking, F8 raid rejection, restart persistence, and JSON/CSV export inspection.
- The fresh/reused slot-6 matrix passed zero isolation, stack-unit tracking, restart persistence, Duckov-driven deletion, read-only archival, new-generation zeroing, and cross-slot isolation.
- The review-hardening continuity gate passed: slot 1 retained its fingerprinted generation across an active restart, UDS remained provably inactive while slot 6 was deleted/reused, and the next active launch archived the old one-use generation read-only and started a fingerprint-matched zero generation without affecting slot 1.
- The follow-up interruption gate passed: a forced termination immediately after Duckov saved retained two slot-6 uses and the UDS session checkpoint; the next launch reopened the same generation, recovered exactly one interruption, and cleared the pending marker on clean exit. A later inactive delete/reuse left UDS byte-identical, and the following active launch archived that exact two-use generation read-only before starting a separate zero generation.
- The validated installable ZIP contains exactly the five documented package files and no Duckov, Unity, framework, or Harmony DLL.
- Final `UltimateDuckovStatistics-v0.1.0.zip`: 45,803 bytes, SHA-256 `7d930422e6e1c7e4b13a3bdd6a1f682e1350edd5448738762807b299eeeec581`. The lowercase sidecar matches exactly.

## Known limitations

- Statistics begin when UDS is installed; historical activity is not reconstructed.
- Base item use is diagnostic-only and does not change totals.
- Actual HP restored is deferred to M2.
- F8 does not open UDS during raids.
- Only Overview, Items, and Diagnostics are enabled.
- No Steam Workshop upload is included in v0.1.0.
