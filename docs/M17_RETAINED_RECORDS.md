# Retained native Records tab

Records implements both accepted [top](../mockups/uds-ui-records.jpg) and [scrolled](../mockups/uds-ui-records-scrolled.jpg) references as one complete document beneath the shared fixed header. The scrolled reference is an intermediate position, not the end of the page. This implementation adds no schema member, record category, gameplay callback, or IMGUI path.

## Sources and evidence

`RecordsPresentationFactory` consumes the existing `StatisticsPanelProjection`. Profile, statistics and expected generations must agree; compact run IDs must be unique and belong to that generation. Runs, record collections and starting-map aggregates must be the objects composed from that same profile publication. Ambiguity returns an unavailable presentation. The factory uses indexed lookups and copies only strings, exact route IDs, and read-only card/row collections. The retained view owns no profile, mutable run/record/map aggregate, or native gameplay object. Rendering does not read a profile or rebuild a projection.

| Card role | Persisted source |
| --- | --- |
| Fastest extraction | `RunDurationRecords.Extraction.Shortest` |
| Longest successful raid | `RunDurationRecords.Extraction.Longest` |
| Shortest death run | `RunDurationRecords.Death.Shortest` |
| Longest death run | `RunDurationRecords.Death.Longest` |

The UI does not rank visible history to replace these references. One eligible run can occupy both roles. Empty extraction and Deaths cards explicitly say that no eligible runs have been recorded; this does not imply no extraction or death ever occurred. Both empty categories remain in Overall when there are no records. A missing half of an existing pair is unavailable, not zero. A present reference contradicting the resolved run's recorded eligibility, integrity or outcome cannot become an actionable duration record.

Time uses stored active seconds and the existing millisecond/hour formatter. Dates use the record's stored UTC start, converted to local time with `yyyy-MM-dd - HH:mm:ss`. Starting map comes from the verified run's starting-map evidence, never automatically from the record's older/general map observation or the ending map. Unknown/modded identities retain stable IDs in their fallback names.

Exact routes use every stored segment in order with ` - ` separators, including repeated maps. A proven single segment omits Route. Missing, historical, repaired or incomplete route evidence explicitly remains unavailable/partial; the view never synthesizes a route from endpoints. If the exact run is missing or its timestamp/duration disagrees, independently stored duration/date facts remain visible, but starting-map/route enrichment and View run are unavailable. Generation ambiguity hides the document immediately.

## Starting maps and complete continuation

Per starting map joins `RunStatisticsViewModel.Maps` to `RunDurationRecords.Maps` by stable Map ID. It never substitutes `RunAggregateTotals.RouteMaps`. Known display names sort ordinally with stable IDs breaking ties; unknown identities follow deterministically. Every aggregate receives a complete card without pagination or a top-N limit. Orphan record-map entries remain visible with unavailable aggregate totals; a mismatched record-map key makes affected records unavailable rather than associating them by display name.

Cards show stored Runs, Extracted and Died counts and Interrupted when nonzero. They do not derive the total from two outcomes. Physical and teleport distances describe complete stored runs grouped by their starting map. Current movement support and complete same-map run history are required for exact values, including exact zero. Positive recorded values with incomplete evidence are qualified partial; missing zero evidence is unavailable. Ordinary totals may include record-ineligible runs.

Each card retains both extraction record values and the applicable death record values. Missing eligible records use explicit no-eligible-run wording; inconsistent record composition uses Unavailable. The document continues beyond Warehouse Area through every final map row, then retains 30 reference pixels of bottom breathing room. There is no third major section.

## Retained controls and layout

The shared scroll configuration explicitly clamps movement to the document bounds, while keeping native sensitivity, inertia and deceleration. Wheel, drag and inertial scrolling must not expose space beyond the first or last content edge.

`RecordsView` reuses the Overview View run factory, native `ButtonAnimation`, keyboard/controller feedback forwarding and the shell's exact generation/Run-ID route into Runs. The Runs route resets detail positions and reveals/selects the requested history row. Unresolvable targets have a muted explanation and no actionable button.

The formerly Runs-private `ScrollRegion` now belongs to the shared shell and remains the same implementation for both tabs. Records copies `RunsNativeScrollConfiguration`, uses `RunsScrollRect`, `OverflowCuePolicy`, `RunsOverflowEdge` and focus reveal. Its one rounded, clipped viewport shows no cue when fitting, bottom only at the top, both in the middle, and top only at the real bottom. TMP fallback submeshes are included in stencil clipping. Native wheel/drag inertia comes from the installed prefab configuration; no separate wheel animation is claimed.

Down from the Records tab enters the page. Up/down scrolls the page; right enters its first actionable record button. Buttons support up/down traversal, native submit, left back to tabs, and right back to page scrolling. At the page top, up returns to tabs. Selecting a button reveals it. Scroll survives tab switches and same-generation refresh/invalidation within the shell, clamps after reflow, and resets for a different generation. A closed/destroyed shell releases its state and controls.

The reference transform preserves native TMP widths and spacing. Major panels use 50% black, 20-pixel radii and 30-pixel padding; inner cards/rows use the existing dark surface and 10-pixel radii. The document starts 40 reference pixels below the header and separates major sections by 40 pixels. Measured headings and buttons wrap in order. Separate label/value controls wrap or stack at narrow widths; no section or row is omitted. No visual dimension assertion controls construction or availability.

Cards and listeners are created once and rebound during refresh. Removed cards are released; the pool never retains a previous larger profile's inactive map cards. Rows have a bounded set of semantic fields. The view borrows the shell material and owns no new material. Disposal removes listeners and destroys only its owned controls under the shell root.

## Verification boundary

`RetainedRecordsTests` covers presentation sources, production reducer/projection composition, exact routing, absent/inconsistent records, repeated/partial routes, map joins/order/continuation, movement availability, detached data, responsive row policies, overflow endpoints, generation scroll state and repeated pool refresh/disposal. Source composition tests check the Unity-only shared controls, native feedback and cleanup wiring; they do not pretend to execute Unity layout, audio or native object lifetimes.

Run focused Records tests, the complete Debug and Release suites, the native build, installed contract probe, package verifier, formatting and diff checks. Live screenshots, scrolling feel, focus/submit/audio, clipping and object/material counts still require the user-controlled [M17 manual protocol](M17_MANUAL_VALIDATION.md). Deployment and gameplay remain separate user actions.
