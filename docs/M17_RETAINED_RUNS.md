# Retained native Runs tab

The retained shell owns separate Overview and Runs content. Runs implements the accepted [Runs reference](../mockups/uds-ui-runs.jpg) using existing schema-17 data. It adds no tracking callback, schema member, gameplay object reference, export control, or runtime visual acceptance gate.

## Data and navigation

`RunsPresentationFactory` consumes an exact-generation `StatisticsPanelProjection`. It verifies profile, statistics, expected generation and every run generation, and rejects ambiguous duplicate run IDs. It creates immutable presentation strings and read-only collections, including copied terminal equipment and classified combat values from `RunDataProjection`. The view retains no `ProfileDocument`, `RunSummary`, native character, item, or slot.

History sorts by descending start time, then ordinal Run ID, with deterministic newest-to-oldest display numbers. `RunsSelection` initially selects the newest run, preserves a still-present ID within the same generation, and selects the newest remaining run after an ordinary valid refresh removes the selection. A changed generation starts a fresh selection. Overview navigation carries both the generation and exact card Run ID; a missing or mismatched target shows an explicit unavailable detail state rather than substituting another run. Ordinary selection remains available afterward.

The controller observes profile transitions and rebuilds presentations on completed profile-change notifications or a changed coordinator generation. Invalidation immediately removes old Runs values and hides old Overview content. Refresh replaces Overview content and Runs data inside the existing shell; header, tabs, back control and shell identity remain. No ordinary render reads a profile or rebuilds the data projection.

## Evidence and formatting

The ten summary values use run-scoped lifecycle, movement, combat, container, economy and item-use data. Full Runs timestamps convert stored UTC to local time and use `yyyy-MM-dd - HH:mm:ss`. Accuracy uses ranged hits divided by completed player projectiles; a missing denominator is unavailable. Missing or degraded evidence is qualified, and retained positive counts remain explicitly partial where completeness is lost.

Ranged and melee kills come directly from the classified partition. Effect/environmental kills are not reassigned to either section. Historical or unknown attack classification cannot create exact ranged/melee zeros. A current exact run remains exact alongside historical runs.

Terminal root slots retain the foundation's deterministic captured order. Occupied roots show native icons and captured identity; empty roots have no item icon and explicitly say Empty. Missing metadata uses the existing native fallback, then `?` if unavailable, without dropping the captured identity. Nested evidence is compact text under the root; incomplete capture says additional attachment evidence is unavailable. The persisted native model has only positively observed occupied/empty slot rows: an unreadable remainder is represented by terminal completeness, so the UI reports that remainder at card level and invents no missing slot IDs. No backpack, current equipment, duration aggregate or item-use inference supplies terminal equipment.

## Layout and interaction

Runs uses the existing 2560×1440 reference transform and native TMP font/material with natural horizontal metrics. Content starts 40 reference pixels below the header. Desktop panels have a 1:2 width relationship, a 40-pixel gap, 30-pixel padding and the existing translucent rounded surfaces. Below 1180 viewport pixels, history precedes details in a vertically scrolling page, the summary uses two columns, and Route precedes Equipment and Combat. Desktop summary uses five columns and two rows. Measured text determines row/card heights; longer labels reflow rather than cause construction failure.

History uses measured row offsets and a recycled visible control pool with overscan; 10,000 data rows do not create 10,000 native buttons. History measurement is reused across selection changes. Route has a bounded clipped viewport containing every stored segment, including repeated maps, and a final outcome badge. History, Route, details and the stacked page derive subtle white edge cues from actual content, viewport and offset. Cues update after scroll and reflow, with no bottom cue when content fits. Nested wheel/controller scrolling passes unused motion to the parent at an edge.

Every actionable row and Overview navigation button attaches native `ButtonAnimation`. The installed component supplies pointer hover/click sounds; it has no selection/submit interface, so focus and submit forward those callbacks. A separate native UI color transition supplies hover/press/focus feedback while the selected row retains its orange background. Row focus travels through the virtual history and remains associated with Run ID; recycling cannot silently change a focused action. Focus reveals ancestors as needed. The shared tab row is horizontally clipped/scrollable, with selected or focused tabs revealed without changing native label widths.

Scroll state is retained across tab switches and valid refreshes where extents still permit it. Selecting or explicitly routing to another run resets its detail/route offsets. Row listeners attach once per pooled control; shell disposal removes listeners and releases owned badge assets and the common material. Overflow is never a fatal acceptance condition.

## Qualification boundary

`RetainedRunsTests` exercises ordering, generation checks, exact routing, selection changes, immutable values, outcomes, empty/partial data, terminal/nested evidence, icon fallbacks, classified kills, route order, long histories, measured row wrapping, scroll propagation, overflow edges, focus reveal and pooled listener/resource lifetime. Existing foundation and Overview/shared-shell suites remain separate regressions. These game-independent tests do not execute Unity's renderer or certify native screenshots, audible feedback or live GameObject counts.

Manual qualification must use the complete native Runs protocol in [M17_MANUAL_VALIDATION.md](M17_MANUAL_VALIDATION.md). Visible equipment identities/completeness may make cards taller than the illustrative icon-only mock; details scroll to retain this information. Ranged kills are shown alongside melee kills to make the classification explicit. Native typography differences, generated fallback badge glyphs and translucent surfaces are intentional; no blur or copied game image is shipped.
