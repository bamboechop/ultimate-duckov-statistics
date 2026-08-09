# Ultimate Duckov Statistics v0.1.0 — pre-release draft

This draft describes the planned GitHub pre-release. Do not publish it until the automated suite, both manual save scenarios, package audit, and checksum verification pass.

## Important: activate UDS on every cold launch

Duckov `2.3.30` retains the enabled preference for local mods but, on the verified setup with no Duckov Workshop subscriptions, does not automatically activate them after restart. Before selecting a save on every cold launch:

1. Open **Mods**.
2. If the left UDS indicator is unchecked, click it exactly once.
3. Confirm the check mark appears and return to the main menu.

This is an explicitly accepted v0.1.0 workaround for a Duckov loader edge case. UDS does not add Harmony or an unrelated Workshop dependency to bypass it.

## Included in v0.1.0

- Successful main-duck consumable activations in raids.
- Separate activation and stack/durability/item-amount totals.
- Deterministic Healing, Food, Drink, Stimulant/Buff, Remedy/Debuff Removal, Special, and Other/Unknown groups.
- Multi-effect tags without group-total double-counting.
- Per-save-generation storage outside Duckov save files.
- Overview, Items, and Diagnostics views outside raids, with configurable F8 access.
- JSON and flattened CSV exports.
- Atomic profile writes, backup recovery, bounded diagnostics, and generation archives.

## Compatibility

- Escape From Duckov `2.3.30`
- Steam build `24013657`
- Unity `2022.3.62f2`
- Windows, single-player only

## Validation

- 33 Release tests pass, including slot-transition and generation-rotation capability carryover.
- The native contract probe passes against Duckov `2.3.30`, Steam build `24013657`, and Unity `2022.3.62f2`.
- The progressed slot-1 matrix passed base exclusion, cancellation exclusion, two-group raid use, amount tracking, F8 raid rejection, restart persistence, and JSON/CSV export inspection.
- The fresh/reused slot-6 matrix passed zero isolation, stack-unit tracking, restart persistence, Duckov-driven deletion, read-only archival, new-generation zeroing, and cross-slot isolation.
- The validated installable ZIP contains exactly the five documented package files and no Duckov, Unity, framework, or Harmony DLL.
- `UltimateDuckovStatistics-v0.1.0.zip` SHA-256: `6e63b1c2a6d62d1e1e62a51a15dd26a928fdb98b8cda988e8b972bc7576b7363`.

## Known limitations

- Statistics begin when UDS is installed; historical activity is not reconstructed.
- Base item use is diagnostic-only and does not change totals.
- Actual HP restored is deferred to M2.
- F8 does not open UDS during raids.
- Only Overview, Items, and Diagnostics are enabled.
- No Steam Workshop upload is included in v0.1.0.
