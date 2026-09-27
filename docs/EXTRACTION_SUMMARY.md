# Results screen summary

The source adds one UDS-owned block to Duckov 2.3.30's existing extraction/death results screen. The native title, damage-source text, XP animation, fade groups, and Continue action remain owned by the game. The block uses the native text template's font/material, equal-width metric columns and English/German labels. Native visual acceptance is still required before release.

## Values and capture boundaries

- **Raid time:** the completed UDS run's active duration, excluding loading and pauses, rather than the accelerated in-game clock.
- **Your kills:** UDS's proven player-final-blow total. Other NPC, pet, and world kills are not counted as player kills. Unavailable capture remains unavailable; retained nonzero incomplete evidence is marked partial.
- **Estimated net value:** terminal holdings estimate minus starting holdings estimate, captured at the first controllable frame of a newly observed native raid and at the extraction callback. A multi-map transition does not reset the baseline. This does not include travel costs charged before that first controllable frame.

The holdings estimate is **native wallet Money + 50% of the durability-adjusted raw value of carried/equipped items and pet inventory**. Each item's base value is multiplied by current/max durability when applicable, rounded down per unit as in native `Item.GetTotalRawValue`, then multiplied by its stack count. Slots, attachments, nested inventories and loaded ammunition are traversed once; the character's own virtual root has no sale value. Physical Cash belongs to that tree and is not added again as an all-owned Cash aggregate. Stash contents are excluded.

This is an estimate of the change in those holdings, not actual sale proceeds or gross loot income. Starting gear is subtracted; consumed ammunition/items and equipment wear reduce it. Vendor prices, sellability, modifiers, quest rewards, and direct stash transfers can make this differ from realized income. Inventory-to-pet transfers and unloading ammunition do not create value. A physical Cash item converted to wallet Money is subject to this same 50%-raw estimate; the UI does not claim exact currency profit.

A valid start observation requires seeing the native new-raid event, not merely enabling UDS in the middle of a raid. A new raid blocked behind a previous run's pending completion does not claim an on-time baseline. Missing/unhydrated inventories, ammunition temporarily detached during reload, invalid native values, duplicate item identities, cyclic item trees, or exceeding the 16,384-item defensive bound make value unavailable. Tutorial/other runs without that fresh native identity have no guessed baseline. No old runs are backfilled.

**Death:** time and kills remain available when recorded. Estimated value is unavailable because this implementation does not prove which equipment/items survive native or modded death-retention rules. It never values the pre-death corpse as extracted possessions.

## Lifetime, storage and performance

The baseline, terminal value and small completed-results snapshot are process-local. They are not persisted or exported and require no schema change. The snapshot contains scalar totals and generation/run/native-raid identities, never a full profile or history. Returning to the results screen does not load run history or open/build the UDS panel.

There are at most two bounded holdings scans per fresh run: start and extraction. No per-frame inventory scans, added Harmony patches, background polling, or extra durable writes are introduced. Existing terminal persistence retries keep the original value boundary rather than revaluing later base inventory. If run completion is delayed, the open block can receive its matching result once the normal lifecycle completes.

New runs, profile transitions, results close and mod disposal clear the snapshot. Profile/native-raid mismatches cannot display a previous result. Missing native UI structure or an exception removes only the added block and never blocks the game's results flow.

## Native layout ownership

Installed Duckov 2.3.30 `resources.assets` contains `ClosureView` (GameObject 30068) with `Content` (GameObject 14885, RectTransform 68773) and `Content/ExpBarContainer` (GameObject 29983). Content already stretches vertically across the viewport; its native VerticalLayoutGroup 114534 centers the combined content. Its serialized preferences are MiddleCenter alignment, left/right padding 16, top/bottom padding 0, spacing 0, child width/height control enabled, child width/height scale enabled and force expansion disabled. No ContentSizeFitter is attached to Content. Its background Image_2 is ignored by layout and also stretches across Content. The native Content fade group references only CanvasGroupFade (alpha/raycast animation); the native open/setup flow does not resize this background. The apparent vertical margins come from centering the child group, rather than top/bottom padding fields. The correction restores these content margins; it does not turn the native full-height background into a compact box.

UDS owns a compact 150-unit block, containing an 84-unit metric row and 52-unit note separated by 8 units. The owned root and row explicitly advertise zero flexible height, and the row does not force vertical expansion. A preferred height alone is insufficient: Unity's default LayoutElement flexible height is -1 (no override), so nested layout-group flexibility otherwise reaches the native parent and consumes the entire viewport's spare height. The native anchors, padding, scale, sizing preferences, title, XP block and Continue container are never rewritten. Adding the summary therefore increases the centered content extent by only its own 150 units.

The focused layout regression uses the installed UnityEngine.UI layout-property priority and vertical negotiation rules with the serialized native preferences. It reproduces the previous viewport-filling summary before the correction and checks measured block bounds, footer adjacency, symmetric native margins, native child dimensions/preferences, and restoration after close at several viewport heights. It runs against the production-created hierarchy but is a narrow managed layout model, not the Unity engine or a glyph renderer; rendered English/German extraction/death acceptance remains required.

## Recording failures and acceptance

The user accepted the corrected extraction layout on September 27, 2026. That gameplay test then exposed a route-publication failure: the native JSON reader changed a recorded timestamp during a read, so immutable-prefix validation rejected the next route update. This blocked run terminalization and left the extraction snapshot unavailable; the Runs list still showed the previous completed raid. FPC initialization in that session reported all 11 combat hooks available.

The route failure was reproduced with the installed Mono reader and the captured 145-point payload: its short-decimal parsing changed the bits of timestamp `29.235182`, so the next immutable-prefix comparison correctly rejected it. The native writer now emits finite doubles with G17 precision and floats with G9 precision, always with an exponent, to bypass that parser path and retain the same original bits across Mono and .NET. Signed zero, nullable values, decimal scale and integral values retain their existing contracts. Source-backed encounter caches use the same configured codec instead of silently re-encoding through the default serializer. Strict prefix validation remains unchanged, including rejection of a one-bit timestamp or coordinate mutation.

An isolated harness compiled the production writer against the installed game assemblies and ran it with Duckov's Mono runtime. It reproduced the old failure, accepted the next route sample after the correction, and preserved the original bits of 10,002 finite doubles and 9,970 finite floats, including extrema and seeded arbitrary bit patterns. The same emitted bytes were checked against Mono's saved original bit patterns using .NET 8; nullable and null/nonfinite reader behavior was checked separately. No running game or live profile was modified by this qualification.

The numeric JSON schema and payload-hash checks are unchanged. Previously stored payloads remain readable; new capture hosts use fresh session/visit/chunk identities and cannot append to a prior process's route chunks. The fix does not reconstruct a failed session's unsaved tail or invent the missing result. A fresh raid is required for native acceptance.

Diagnostics must distinguish healthy capture contracts from successful publication. A rejected encounter record keeps current profile persistence in an error state until that exact generation/run/kind/record is accepted; another successful record or snapshot cannot clear it. Storage errors now also affect the overall banner. Normal deferred writes remain pending without claiming a failure, and old incomplete raids do not imply that today's combat hooks are unavailable. Regression coverage exercises actual repository rejection, an unrelated successful write, exact retry, repeated throttled errors and retained-panel refresh.

Rendered German/death layout and a fresh successful raid's time, kills and value remain native acceptance checks after the recording correction.

## Other mods and native qualification

UDS does not disable or alter other mods. Disable **Match Total & Duration & Stash Value** and **Show kills on extract** for the unified-layout smoke test, or their own independent overlays will remain visible. A full replacement results screen supplied by another mod is outside this native `ClosureView` integration.

Automated tests cover scalar snapshot isolation, profile/run mismatches, fresh versus mid-raid activation, deferred death completion, terminal durability retries, missing/invalid inventories, checked valuation, loaded-ammo movement, physical Cash/pet ownership, UI creation failure and owned cleanup. These do not prove rendered layout, native glyph sizing or gameplay smoothness.

Manual acceptance:

1. Start a new raid with known gear/loaded ammo; pause briefly and cross a map boundary if convenient.
2. Make a few player kills; acquire known loot, use ammunition/healing, and optionally move loot into pet inventory or unload ammunition.
3. Extract. Compare time/kills with UDS Runs and check the estimate's sign/scale against the stated formula. Capture the results block in English/German at the normal display resolution.
4. Verify the title/XP animation/Continue still work, and a second run shows only its own values.
5. On a death run, verify time/kills and the explicit unavailable retained-value note.
6. Optionally change profile or deactivate/reactivate UDS between runs; a mid-raid activation must not manufacture a profit baseline.

A diagnostic performance recording can qualify the native start/extraction callbacks after other agents' builds and tests have stopped. No frame-time conclusion is inferred from automated test duration.
