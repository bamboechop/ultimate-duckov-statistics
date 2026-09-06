# M17 user-controlled native UI validation

This matrix qualifies the v0.17.0 UI candidate against the thirty accepted images in [mockups/README.md](../mockups/README.md). Codex may build and inspect a package, but deployment requires separate explicit approval and confirmation that Duckov is closed. The user launches Duckov, selects saves, controls gameplay, opens menus, changes resolution/UI scale, performs reset/export actions, and closes the game. UDS never modifies Duckov saves.

No row below is considered passed from source inspection or deterministic tests alone. Record `Pass`, `Fail`, or `Not exercised`, the exact candidate commit/package hash, resolution, UI scale, language, save generation, and screenshot/log/export evidence.

## Complete retained Records qualification

The [Records implementation](M17_RETAINED_RECORDS.md) consumes existing duration records and starting-map totals. Use ordinary recorded runs and existing profile evidence; do not fabricate or edit saves to create test states. Cases unavailable through normal play remain deterministic-only evidence.

1. Compare the top of Records against both accepted references at 1280×720, 1680×1050, 2560×1440 and approximately 1024×768. Confirm the fixed header, 40px reference content/section gaps, rounded translucent panels, native font proportions and full single-column content. Long localized headings, labels, names and routes must wrap or expand without hiding rows or failing construction.
2. Check extraction Shortest/Longest and death Shortest/Longest against stored references. With one eligible run, both roles may show the same duration and Run ID. With no eligible death records, Deaths must say no eligible death runs have been recorded, even if ineligible deaths are present in totals. Check no extraction records and a wholly empty Overall where available. Compare milliseconds, hour-length durations and local full timestamps.
3. Activate each available View run by mouse and keyboard/controller submit. Verify exact Run ID, tab switch, history reveal and reset detail position. A missing/mismatched record target must retain only trustworthy facts and explain unavailable navigation. Confirm native hover/click sounds and focus feedback. Exact repeated routes retain every segment; single-map routes omit the redundant row. Historical/partial routes never become guessed endpoints.
4. Compare every starting-map card against starting-run totals, including conditional Interrupted and physical/teleport distances. Check per-map extraction and eligible death records, stable unknown/modded identities, and no-eligible versus unavailable wording. Visited-only maps must not acquire a starting-map aggregate. Scroll beyond Warehouse Area through all remaining maps and the final complete record rows to the bottom breathing room.
5. Check fit/top/middle/true-bottom cue states: none/bottom/both/top. Both corners must follow the rounded viewport, with no detached lines or escaped TMP/shadow/fallback glyphs. Confirm the bottom cue remains at the second mock's intermediate position. Compare native wheel, drag, inertia, focus reveal and safe clamping after changing resolution. Down from the tab enters the page; up/down scrolls, right reaches record buttons, and left returns to tabs. Button traversal and submit must stay usable with wrapped content.
6. Switch Records → Runs → Overview → Records and confirm the same-shell Records offset persists. A same-generation refresh preserves/clamps it. A normal profile-generation transition must immediately hide stale values and restart the new generation at the top. Check twenty repeated open/close, refresh and tab-switch cycles for duplicate shells, callbacks, retained objects/materials or Player.log errors. Pool/source tests support these checks but cannot certify native object counts or screenshots.

Records qualification does not authorize deployment, save manipulation, export/reset or gameplay automation.

## Complete retained Runs qualification

Overview refresh checks: open Overview, close the panel, complete another run with a different outcome, reopen on the previously selected tab, then return to Overview. Repeat Died → Extracted → Unknown where normal play supplies those outcomes. The outcome badge and View run button must use current label widths, with no overlap or collapsed background; View run must route to the new exact Run ID. Check both visible-Overview refresh and reopening while Runs is selected. The longest successful raid highlight shows only its ordered start and end maps (one name for a single segment). All four highlight labels and values use native ellipsis within their existing cells; hover exposes the full presentation text through the native tooltip. Exercise long localized map, weapon, consumable and label text. Native visual acceptance requires the game; deterministic tests alone do not qualify it.

The installed TMP implementation initializes screen-space metrics in `TextMeshProUGUI.Awake`; `ForceMeshUpdate(ignoreActiveState: true)` still returns before Awake. Overview measurement temporarily activates its own hierarchy and restores the previous tab visibility in `finally`, so rebuilding while another tab is selected cannot cache the pre-Awake one-tenth-scale badge/button widths. This is layout initialization, not a runtime visual acceptance gate.

Runs implementation and deterministic evidence are described in [M17_RETAINED_RUNS.md](M17_RETAINED_RUNS.md). This protocol qualifies the new view, using the already qualified foundation; it does not require repeating terminal-hook investigation or manipulating saves.

1. Open through main menu, base pause menu and F8. Compare Overview, header, tabs and back control with their accepted appearance. Confirm one shell, native cursor/focus restoration and repeated close/reopen with no errors.
2. Enter Runs with available history. Verify newest-first order, deterministic numbers, local full timestamps and initial newest selection. Select extracted, died, interrupted/unknown and historical runs. Compare all summary, route, equipment and combat values with the existing recorded evidence.
3. Activate Overview's View run with mouse and keyboard/controller. Verify the exact card Run ID, including after returning from a different selected run. Where a normal profile switch is available, verify old-generation content is unavailable during the transition and a completed refresh displays only the new generation.
4. At desktop widths, verify the selected title, metadata/integrity row and ten-cell summary remain fixed. Independently scroll long history, Route, and Equipment/Combat with mouse wheel and supported controller/keyboard input. Scrolling one lower region must not move the other or the fixed summary. Equipment and Combat should both be visible with no cue when they fit. At fit/top/middle/bottom verify rounded edge cues (none/bottom/both/top), including both curved corners aligned with their background contour, clipping, true bottom reachability, focus reveal and continued outer-page scrolling at narrow nested edges. Scrolling must not silently change a focused row's Run ID. Repeatedly cross top and bottom clipping edges through mixed outcome badges, including fast wheel movement and drag/inertia; no label, shadow or fallback glyph may flash in the container padding, even while rows are recycled. Inspect frame-by-frame where possible, including localized labels and narrow layouts.
5. Compare 1280×720, 1680×1050, 2560×1440 and 1024×768. Narrow layouts must retain history, details, Route, Equipment and Combat in that order. Verify horizontal tab scrolling, long/localized names, measured badge wrapping and all ten summary values without clipped inaccessible text. Confirm the desktop badge, metadata and integrity/eligibility begin on the same row; centered white summary values sit above uppercase gray labels; Route counts and segment detail lines are smaller gray controls; RANGED and MELEE are uppercase. Verify no-combat/no-container phrases only for complete evidence, including a segment with combat but no kills.
6. Verify the compact 2×5 icon grid, 2px gray rounded borders, centered icons and absence of persistent identity paragraphs. Hover and focus cards for native tooltips; activate an occupied item with attachment slots by click or submit to open the bounded evidence panel. Empty roots and items proven to have no attachment slots must not open it; occupied items with incomplete attachment evidence remain inspectable. Verify the fixed top row contains the equipped item icon, item name and close button, without the root slot label. Only captured attachment slots appear in the body. Verify each attachment has its own matching icon in the left column (dash for empty, question mark for missing icons); the right column shows its item name above a smaller uppercase #b1b1b1 slot label. Internal IDs/paths and the normal Captured terminal equipment footer must be absent; incomplete evidence keeps its qualification. Test one through six slots: the overlay should adapt to their height and fit all six without scrolling or overflow cues. Test wrapped localized names and empty nested slots. Exceptional text exceeding the screen must remain accessible through body scrolling while the header stays fixed. Clicking or hovering the equipment background must not open an overlay or tooltip. Scroll long evidence, dismiss with its close control and verify focus returns to the originating card. Check main-menu and pause-menu access separately. Compare Kriegsaxt, SR-3M and MMG native icons. Verify muted-dash empty slots, distinct unavailable cards, occupied/empty attachment dots in captured order, and an ellipsis for incomplete nested evidence. Whole-loadout unavailability must never appear as ten empty slots. Confirm no current equipment or backpack content is substituted. Current exact and historical incomplete kill partitions must coexist without fabricated zeros or subtraction-derived melee kills.
7. Check hover anywhere on each full history row, press, exact Run-ID selection, controller submit, focus visibility and native sounds. Press a row and scroll/recycle or drag before release: it must not activate another visible run. Compare native wheel step size, drag inertia and elastic recovery; wheel animation is not claimed. Move away from a selected history row and verify its orange styling remains. Switch tabs and refresh repeatedly; selection and scroll should remain where practical.
8. Repeat at least twenty open/close and tab-switch cycles; inspect Player.log and, if available through the normal development tools, object/material/listener counts. Verify no duplicate shell, retained inactive growth or cleanup errors. Native screenshot, audible feedback and live object-count acceptance cannot be inferred from deterministic tests.

The reset/export rows below remain later Diagnostics work and are not acceptance criteria for the Runs-only delivery.

## Preconditions

1. Freeze the candidate commit and verify the exact five-file package plus checksum.
2. After separate deployment approval, transactionally install only that exact package and read back all five installed hashes while Duckov is closed.
3. Start Duckov yourself. In Mods, confirm HarmonyLib and Ultimate Duckov Statistics are active before selecting a save.
4. Use a user-selected save with representative M1-M16 history. Back it up through the user's normal process before any reset exercise. Reset archives UDS-owned statistics only, but its UI result must still be treated as an intentional user action.
5. Keep `Player.log`, the active UDS profile/backup, and a pre-action export available for comparison. Never edit a profile or Duckov save to manufacture a UI state; use deterministic test evidence for otherwise unsafe or unreachable combinations.

## Main-view screenshot matrix

| Accepted reference | Runtime exercise | Required evidence |
| --- | --- | --- |
| `uds-ui-overview.jpg` | Open Overview with representative run, route, item, combat, container, world-time, and crafting data | Hierarchy and units are readable; unsupported siblings stay unavailable; desktop two-column and narrow left-first stack preserve the same facts. |
| `uds-ui-runs.jpg` | Expand representative extracted, dead, interrupted, and integrity-excluded runs | Route, segment, outcome, record eligibility, and metric summaries agree with profile/export; large history remains bounded. |
| `uds-ui-records.jpg`; `uds-ui-records-scrolled.jpg` | Review overall and per-starting-map records, then move through overflow | No loading/pause time enters active-duration records; page/overflow cues change only when more rows exist. |
| `uds-ui-combat-summary.jpg` | Open Combat Summary | Kills by you, observed-world deaths, legacy/unavailable states, damage, projectiles, and headshots retain their M11 semantics. |
| `uds-ui-combat-enemies.jpg` | Review multiple enemy identities including unknown/modded | Stable fallback names remain visible and no family/owner is guessed. |
| `uds-ui-combat-weapons.jpg` | Select at least two weapons with different ammunition pairs | Each expansion shows only that weapon's ammunition, correlated firing-action counts, within-weapon percentages, and explicit uncorrelated actions. |
| `uds-ui-combat-incoming-damage.jpg` | Review multiple killer/cause rows | Incoming damage/death evidence remains separate from player kill credit. |
| `uds-ui-equipment-loadouts.jpg` | Expand recurring and one-off loadout evidence | Recurring ranking requires the existing two-run rule; signatures and duration are not relabelled as selected-weapon time. |
| `uds-ui-equipment-weapons.jpg` | Expand weapons with occupied and proven-empty attachment slots | Total/per-character-slot duration and named nested slots agree with current-schema profile/CSV; unavailable is never rendered empty. |
| `uds-ui-equipment-armor-and-gear.jpg` | Expand occupied and proven-empty native character slots | Occupied, empty, nested, unknown/modded, and unavailable states remain distinct. |
| `uds-ui-equipment-totems.jpg` | Review direct and tote-carried totem evidence | Presence and proven-active direct sets remain distinct; tote activation is not invented. |
| `uds-ui-economy.jpg` | Compare current holdings, flows, and recent runs with Duckov/profile/export | Money and Cash holdings are separate from Money/Cash flow; liquid wealth appears only when both holdings are current. |
| `uds-ui-crafting.jpg` | Expand output-first and resource-first rows | Successful times, produced units, used quantity, recipes, batches, currency, and reciprocal associations agree exactly. |
| `uds-ui-item-use.jpg` | Review items with different group/effect/heal evidence | Item uses, amount used, HP restored, primary group, and effect tags remain independent facts; icons do not determine identity. |
| `uds-ui-diagnostics.jpg`; `uds-ui-diagnostics-scrolled.jpg` | Scroll both desktop columns independently and expand health groups/technical details | Left contains settings, issues, and technical/log details; right contains grouped Working/Limited/Error health; narrow layout stacks left first. |

## Complete retained Combat qualification

Use [M17_RETAINED_COMBAT.md](M17_RETAINED_COMBAT.md) for exact sources and automated/manual boundaries. The following checks qualify all four subpages together on one candidate; they do not require modifying a real profile to manufacture unavailable states.

1. Open Summary and compare all four cards, ranged/melee rows, projectile accuracy, current effect/environmental/unknown player-kill buckets and observed-world ownership against the user-selected UDS profile/export. Card values must appear above their uppercase muted labels. Historical-only player-kill content is omitted and does not qualify current ranged/melee values; current capability or repaired-data warnings remain. Check that world deaths never add player credit. The world-death total should be muted and aligned to the visible bottom of its heading, wrapping when needed.
2. Open Enemies. Confirm initial kills-descending order, then click every header and verify its column and direction arrow switch correctly. The header must form one continuous dark band, with smaller labels and no individual dark header cards. The current projection has no enemy ownership breakdown, so data rows must not expand, highlight on hover/click or show a chevron; the sort headers remain interactive. Deterministic supplied-breakdown coverage checks a compact orange button above separate white read-only detail rows on translucent black; only the compact button can collapse it. Unknown/modded names must remain visible. Use deterministic coverage for empty/partial states absent from legitimate history.
3. Open Weapons & ammunition. Change weapon selection and compare overall firing-action shares versus within-selected-weapon pair shares. Check uncorrelated actions, historical pairing, and the explicit no-correlated-data state where legitimately available. Verify native icons or question-mark fallbacks, long names, selected orange treatment, independent column scrolling, and the always-visible firing-action footer.
4. Open Incoming damage. Compare lifetime Total, attacker rows, damage shares, count and deadliest attacker with `DamageReceived`/`PlayerDeaths`. Verify values above card labels, smaller single-line desktop headers on one continuous dark band, and aligned columns. Click each header: a new column starts descending; clicking it again reverses direction; its arrow precedes the label. Total stays pinned, numeric sorting uses full recorded precision, unavailable values stay last, and sort state survives subpage changes and same-generation refresh. Supported zero damage must have no percentage, zero complete deaths may show None, and Unknown must not turn into None. Unavailable evidence must remain explicit.
5. On every actionable row verify hover/press/click sounds and visual feedback. From the main Combat tab, use Down, selector Up/Down, Right into the page, Left back, and Up from the first selector to the main tabs. Scroll read-only Summary/Incoming/Ammunition with controller in both directions. Focus must reveal the target without trapping navigation or jumping back to a recycled row after wheel scrolling.
6. At 2560×1440, 1920×1080 and 1024×768, inspect reading order, every metric/notice/footer, long localized names, nested scrolling and exact bottom reachability. At fit/top/middle/bottom verify none/bottom/both/top cues following rounded corners. Cross clipping edges with TMP fallback glyphs, shadows, item icons and expanded orange rows; no content should escape rounded masks. Narrow layouts stack selector, page and ammunition in order.
7. Switch subpages, select/expand entries, refresh valid same-generation data and repeat open/close/setup/disposal. Confirm independent scroll restoration/clamping, valid identity/focus preservation, no duplicate sound/listener activation and stable live control counts while repeatedly traversing large legitimate lists. Generation loss/replacement must hide old content immediately; exercise unavailable generation safely through deterministic tests if no legitimate runtime transition is available.

Deployment, Duckov launch/control, saves, real-profile reset and export remain separately authorized user actions. Record screenshots/audio/input results and any unexercised condition; automated qualification alone does not mark these checks passed.

## Supporting-state matrix

| Accepted reference | Runtime or safe deterministic exercise | Required evidence |
| --- | --- | --- |
| `uds-ui-economy-partial.jpg` | Observe a legitimate last-observed/partial state across a main-menu or lifecycle transition | One unavailable component does not hide a valid sibling; historical flows are labelled recorded-only. |
| `uds-ui-item-use-empty.jpg` | Use a legitimate fresh/zero UDS generation before raid item use | Supported zero/empty wording appears without claiming unavailable historical facts. |
| `uds-ui-diagnostics-error.jpg` | Use a safely induced UI/export failure or the production-composition test fixture | Recent issue states consequence and guidance; affected group is Error without claiming unrelated tracking stopped. |
| `uds-ui-diagnostics-fallback.jpg` | Qualify a compatible missing-menu-integration condition without altering game files, or retain deterministic coverage if none is safely reachable | F8 remains usable outside raids; menu access is Limited/Warning while statistics tracking remains healthy. |
| `uds-ui-diagnostics-reset.jpg` | Open reset confirmation without confirming | Warning names read-only archive, new empty UDS profile, unchanged Duckov saves, and no in-UDS undo; Cancel has initial focus, Escape cancels, background is blocked. |
| `uds-ui-diagnostics-reset-successful.jpg` | User confirms one intentional UDS reset | One new empty generation opens; previous generation exists read-only; success toast/status is visible; Duckov save hashes are unchanged. |
| `uds-ui-diagnostics-reset-failed.jpg` | Use a safe filesystem/test failure condition only if authorized | Existing profile remains active and no statistics are removed; Recent issue and `Player.log` guidance appear. Do not damage a real profile to create this state. |
| `uds-ui-diagnostics-export-successful.jpg` | Export once | One JSON plus thirty-two CSVs are produced, the folder path is copied, and live UI/profile/export values agree. |
| `uds-ui-diagnostics-export-failed.jpg` | Use a safe filesystem/test failure condition only if authorized | Failure leaves tracking active, records one actionable Recent issue, and directs details to `Player.log`. |

## Access and interaction matrix

| Accepted reference | Runtime exercise | Required evidence |
| --- | --- | --- |
| `uds-ui-main-menu.jpg` | Cold launch, open Mods to activate if required, return to main menu, select Statistics | Exactly one localized entry appears beside the native Mods/Settings area and opens the shared exact-generation panel. Recreate the menu and confirm no duplicate. |
| `uds-ui-pause.jpg` | Pause in base and select Statistics | Exactly one native-styled entry appears near Options/Settings and opens the same panel instance. |
| `uds-ui-pause-ingame.jpg` | Pause during a raid | Statistics is hidden or disabled and cannot open. |
| `uds-ui-ingame.jpg` | Press the configured hotkey during a raid | The panel remains closed and a localized outside-raids response appears. |

Also qualify mouse hover/pressed/disabled feedback; Ctrl+Tab and Ctrl+Shift+Tab navigation; selected-tab visibility after horizontal navigation; Escape close/cancel; focus and cursor restoration; configurable hotkey persistence; repeated setup/deactivation/open/close without duplicate subscriptions, menu entries, or panels; and exact rejection when an active generation cannot be proven.

## Responsive, localization, and stress matrix

Exercise at least 2560×1440 and 1024×768, plus one non-100% UI scale if Duckov exposes it. At 1024×768, verify every desktop multi-column region stacks in left-to-right reading order, all nine tabs remain one readable horizontally scrollable row, the selected keyboard tab stays visible, action controls remain reachable, and scroll/page cues show only where content is available above or below.

Repeat representative views in English and one available non-English Duckov language. Verify long native/modded item and enemy names cannot overlap values or controls. Exercise missing and modded icons. Use deterministic 1,000-row projection coverage plus the largest legitimate live history available; opening, tabbing, expanding, scrolling, exporting, and closing must remain responsive without per-frame profile rebuilding or unbounded layout.

## Evidence to return

- Candidate commit, ZIP length/SHA-256, sidecar SHA-256, and five installed-file hashes
- Duckov, Steam build, Unity, Harmony, resolution, UI scale, and language
- One result row for every reference above, with screenshots for every visual state actually exercised
- Access/open/close/setup counts and relevant `Player.log` excerpts
- Before/after Duckov save hashes for the reset exercise, supplied by the user-approved backup workflow
- UDS generation IDs before/after reset, archived-generation inventory, profile/backup hashes, and residue inventory
- Export ID/path and an exact UI/profile/`statistics.json`/CSV comparison for each changed domain
- Any unexercised state, why it was unsafe or unreachable, and the deterministic evidence retained instead

## Acceptance

M17 manual qualification passes only when every safely reachable row passes on the exact candidate, no duplicate panel or menu entry survives lifecycle repetition, raid and ambiguous-generation access fail closed, reset/export preserve their stated safety boundaries, projection/export agreement is exact, and any unexercised failure state is explicitly retained as deterministic evidence rather than silently called passed. A correction changes the candidate and repeats every affected row. Deployment, gameplay, save selection, reset confirmation, and release publication remain separately authorized user actions.

## Runs data foundation qualification

Before accepting the schema-17 foundation, perform the new extracted-run and died-run protocol in [M17_RUNS_DATA_FOUNDATION.md](M17_RUNS_DATA_FOUNDATION.md). The protocol verifies persisted/projection/export evidence; it does not qualify a retained Runs layout.
