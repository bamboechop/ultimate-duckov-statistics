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

## Totem material-cache correction

The user reproduced the rc.1 failure by expanding the first directly equipped totem: three of four visible shades disappeared. Fresh read-only decompilation inspected the installed TrueShadow, ShadowRenderer, ShadowFactory, ShadowSettingSnapshot, Unity Graphic, item metadata, UI quality styling and ButtonAnimation contracts. Two native before/after F11 captures established the failure path:

1. Expanding the entry pools its old control and creates a new control while the other three icon controls remain retained.
2. UDS created the new TrueShadow and assigned `ShadowAsSibling = false`. The installed component already defaults to false, but its setter unconditionally calls `ShadowRenderer.ClearMaskMaterialCache()`, destroying shared materials without invalidating the other renderers.
3. Before expansion, all four renderers held the same valid stencil material. After expansion, the new renderer held a new valid material, while the three retained renderers' assigned materials were destroyed/null. Their active state, alpha, culling, private sprite meshes and live shadow textures remained valid. Thus the loss was a shared rendering-material invalidation, not premature destruction of the owned meshes.

The fix removes that redundant setter call and relies on the verified native child-rendering default. It adds no polling, hierarchy scan, repair callback, allocation or global cache patch. M18's private-mesh destruction and pooled-mesh reuse remain unchanged. The focused production-boundary test models the installed setter's destructive cache contract, fails with the original assignment, and passes with the correction; it also checks retained/reused mesh identity and final cleanup. The stub does not establish GPU correctness.

Installed contract hashes (SHA-256):

- `LeTai.TrueShadow.dll`: `a0fd277f98ba9cf6d8b169fd6e02171f612fbe7b3d7b7decb753bdbea6896208`.
- `UnityEngine.UI.dll`: `b4003eb15f894de8da32b52a8ff832704c5da899780f1a914e85f859c7f34cda`.

The temporary F11 investigation extension was confined to diagnostic builds and removed from the final source after diagnosis; the original M18 diagnostic remains. The separate corrected diagnostic build is used for affected native rendering/resource qualification. In-game confirmation of the corrected interactions and cleanup is recorded separately from automated checks.

On 2026-09-10 the user completed the requested F11 interaction checks in the corrected diagnostic build and reported that shading did not disappear. Captures at 15:37:17 and 15:37:25 UTC showed all four active shadows retaining the same valid stencil material across expansion, including the newly created row; the three retained rows also preserved their private mesh identities. At 15:37:42 UTC, after panel closure, there were zero UDS roots, input owners or totem owners, and all 11 tracked owner/mesh pairs were observed absent. Tracking remained complete, with zero remaining tracked meshes or destroyed-owner mesh candidates. This is bounded native rendering/cleanup evidence for the exercised session, not a new frame-time or unlimited-resource qualification.

The verified ordinary package built from `e6dde3e7af1dd17e9e05a92c6637cf826dd3a455` passed independent extraction and two-root DLL/PDB/ZIP reproducibility. Its ZIP is 636,592 bytes, SHA-256 `d39e9352d174ac75cd2656d3056c5c0394d4a8913df1b61c21b6c3d642070230`. It was restored to the local mod installation while Duckov was closed, retaining the diagnostic backup and verifying all five installed hashes. Ordinary native DLL SHA-256: `2d2174215dca8bf9ea68cc4e3d96d721932c2b4360a2a7775a32b29cd9dcf73e`. The user subsequently cold-opened Duckov and confirmed the shading works in this ordinary package as well.

## Validation and manual acceptance

Focused coverage exercises empty → recorded → different profile → recorded transitions, degraded capability evidence, incoming totals without attacker identities, locale/midpoint/invalid HP boundaries, persisted/reloaded healing and precise JSON/CSV export, and export completion across panel and profile boundaries. Shell tests dispatch real panel close and profile-change handlers. Child-view rendering and Unity GPU/resource behavior are native test boundaries.

Use the repository build/package/ordinary-IL audit, diagnostic ownership suite, and changed-source analyzers. The accepted M18 performance campaign is reused for unaffected behavior; no new frame-time or unlimited-resource claim follows from managed tests.

The corrected source passed 1,976 main tests and 46 ordinary shell tests in each of Debug and Release, plus 50 diagnostic shell tests per configuration. Native Debug/Release builds, the installed contract probe, changed-source analyzers, package inventory and ordinary IL/privacy audits passed. Follow implementation and CI on [PR #20](https://github.com/bamboechop/ultimate-duckov-statistics/pull/20).

On 2026-09-10, after the diagnostic and ordinary-package totem confirmations above, the user reported that all remaining checks were completed and everything looked good. The nine-item UI batch has no outstanding manual acceptance checks. This acceptance covers the checklist below and the totem checks recorded above; it does not qualify other planned rc.2 batches or authorize publication.

1. Fresh-profile Runs centering/wrapping at the usual and narrower window sizes, absent Records map section, and Enemies/Incoming Damage empty panels without table headers.
2. Fresh/populated profile switching, recorded rows returning, and unavailable/partial tracking remaining distinct from proven empty.
3. Integer HP-restored display across Overview, Runs and Item Use totals/items/recent runs, retained availability markers, and fractional export precision.
4. Export completion across close/reopen and profile changes, including pending exports; dismissed confirmations/path/copy controls stay dismissed, normal data-location controls remain usable and completed files remain available.
5. Expanded weapon attachment spacing, including unavailable attachments, and Economy recent-run MONEY NET/CASH NET labels with existing Overview punctuation preserved.

Only the user launches Duckov, selects saves, performs gameplay and accepts native visuals. No save or UDS profile is edited or reset by this batch.
