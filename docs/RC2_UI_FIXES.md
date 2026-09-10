# Post-M18 UI batch

Immutable base: `58591d5a397abd484284291fd95ab92d949f46fb` (current `origin/main` when the isolated batch branch was created). This is one contribution toward rc.2, not a new release or a replacement for the accepted [M18 qualification](M18_ACCEPTANCE.md). Published rc.1 artifacts and the original checkout's unrelated PLAN/diagnostic work are preserved.

## Behavior and baseline findings

- Runs reused its 48-point, top-left run title for the empty detail panel. The empty presentation now uses the Item Use 36-point muted treatment, with centered alignment across the detail panel's padded bounds. Populated titles retain their original style; wrapping and responsive page scrolling remain available.
- Records always laid out the starting-map panel. It is now omitted, including its section gap, when no recorded runs or map evidence exists. Recorded runs and orphan map evidence still expose the section even when tracking is degraded.
- Enemies and Incoming Damage always built sortable headers; Incoming Damage also built an empty total row. The document now omits tables without recorded evidence while preserving empty/unavailable/partial notices. Incoming aggregate damage or death evidence keeps the total row even without identity rows. Enemy empty-state provenance also accounts for damage tracking and unattributed aggregate evidence.
- HP-restored values used different decimal formats across Overview, Runs, and Item Use. They now share whole-number display formatting, `AwayFromZero` midpoints and the current numeric culture. Availability still depends on the original unrounded evidence: a small positive partial value may display `0 (Partial)` and never becomes proven empty. Capture, aggregation, persistence, exports, damage, distances, amounts and other numeric formats are unchanged.
- Export results lived in the operation controller, which outlives the shell. Closing the panel or starting a profile transition now dismisses export-result ownership. The task and single-flight gate remain alive; dismissed completions are observed and logged, but cannot restore result UI, produce a completion toast or overwrite the clipboard. A later export has its own result lifetime. Reset outcomes and the normal data-location controls remain independent.
- Expanded weapon entries now separate their character-slot durations from subsequent attachment groups, including the existing unavailable attachment group.
- Economy recent-run quick-glance labels use separate localization keys without trailing colons. Overview's punctuated labels remain unchanged.

## Totem investigation

The user confirmed that expanding a closed entry can still make shading disappear with rc.1. This is not treated as fixed by M18's earlier resource-cleanup acceptance. Fresh read-only decompilation inspected the installed TrueShadow, ShadowRenderer, ShadowFactory, ShadowSettingSnapshot, Unity Graphic, item metadata, UI quality styling and ButtonAnimation contracts. The static audit has not yet established the failing native path. No speculative lifecycle repair or per-frame polling was added.

Installed contract hashes (SHA-256):

- `LeTai.TrueShadow.dll`: `a0fd277f98ba9cf6d8b169fd6e02171f612fbe7b3d7b7decb753bdbea6896208`.
- `UnityEngine.UI.dll`: `b4003eb15f894de8da32b52a8ff832704c5da899780f1a914e85f859c7f34cda`.

The separate F11 diagnostic build extends the existing explicit resource snapshot with owner/sprite/mesh identity, renderer activation/alpha/culling, texture validity/reference counts, and native dirty flags. This observes state without repairing it or retaining native resources. All additions are inside `UDS_PERFORMANCE_DIAGNOSTICS` and excluded from the ordinary package. M18's private-mesh destruction and pooled-mesh reuse are unchanged. The totem fix and its affected native rendering/resource qualification remain open until before/after captures establish the failure path.

## Validation and remaining manual checks

Focused coverage exercises empty → recorded → different profile → recorded transitions, degraded capability evidence, incoming totals without attacker identities, locale/midpoint/invalid HP boundaries, persisted/reloaded healing and precise JSON/CSV export, and export completion across panel and profile boundaries. Shell tests dispatch real panel close and profile-change handlers. Child-view rendering and Unity GPU/resource behavior are native test boundaries.

Use the repository build/package/ordinary-IL audit, diagnostic ownership suite, and changed-source analyzers. The accepted M18 performance campaign is reused for unaffected behavior; no new frame-time or unlimited-resource claim follows from managed tests.

Remaining user checks:

1. With a fresh profile, inspect Runs centering/wrapping at the usual and narrower window sizes, Records' absent map section, and the Enemies/Incoming Damage empty panels without table headers. No profile reset is required; use an existing suitable profile.
2. Switch between fresh and populated profiles. Confirm recorded rows return, and unavailable/partial tracking remains distinct from proven empty.
3. Compare HP restored on Overview, Runs, Item Use totals/items/recent runs. Verify integer display and retained availability markers; exports should still retain fractions.
4. Export, close/reopen, then export again. Also close while an export is pending and switch profile during a pending export. Old confirmations/path/copy controls must not return; normal data-location controls remain usable and completed files remain available.
5. Expand weapons with recorded and unavailable attachments; inspect section spacing. Check recent-run MONEY NET/CASH NET labels in Economy and existing Overview punctuation.
6. For the separate diagnostic build, capture F11 with visible totem shading, expand a closed entry until the failure occurs, then capture F11 again. Once diagnosed and corrected, recheck expand/collapse, another entry, scrolling/pool reuse, tab switching, refresh and panel reopening, then close and capture resource cleanup. Restore and cold-open the ordinary package afterward.

Only the user launches Duckov, selects saves, performs gameplay and accepts native visuals. No save or UDS profile is edited or reset by this batch.
