# M17 user-controlled native UI validation

This matrix qualifies the v0.17.0 UI candidate against the thirty accepted images in [mockups/README.md](../mockups/README.md). Codex may build and inspect a package, but deployment requires separate explicit approval and confirmation that Duckov is closed. The user launches Duckov, selects saves, controls gameplay, opens menus, changes resolution/UI scale, performs reset/export actions, and closes the game. UDS never modifies Duckov saves.

No row below is considered passed from source inspection or deterministic tests alone. Record `Pass`, `Fail`, or `Not exercised`, the exact candidate commit/package hash, resolution, UI scale, language, save generation, and screenshot/log/export evidence.

## Visual-correction Gate 1 qualification

Gate 1 qualifies only the reusable retained-mode shell. On its exact candidate, open Statistics through the existing main-menu entry and through the configured hotkey, then verify at both 2560x1440 and 1024x768 that:

1. the dimmer, rounded translucent navy frame, distinct rounded content surface, substantially larger left-aligned title, and native rounded top-left back control form the accepted spaced hierarchy; no edge-hugging generic cyan square or top-right rectangular Close remains;
2. the exact nine unellipsized English labels fit as one left-packed readable row at 2560x1440 above an aligned cyan rail, with light selected text on bright cyan, dark navy inactive tabs, and visible normal, hover, pressed, disabled, selected, and keyboard-focus states;
3. narrow overflow scrolls horizontally, its left/right cues appear only when more tabs exist in that direction, Ctrl+Tab and Ctrl+Shift+Tab keep the selected tab visible, and clicking a tab updates the retained placeholder without opening a second panel;
4. Escape and the close/back control close only UDS, restore focus/cursor state, and a later F8 open creates exactly one clean shell; and
5. Player.log names `OptionsPanel/Text (TMP)` for the title when loaded, the live menu-button template for navigation, and the selected `OptionsPanel/Return`, tab, surface, and rail hierarchy objects; it contains no `ProceduralImage` graphic-rebuild-loop error from either the menu clone or retained shell and shows no legacy immediate-mode window, default `GUI.skin` chrome, content body, reset/export control, or new gameplay/save behavior.

The main-view, supporting-state, reset/export, content scrolling, data-comparison, and stress rows below are Gate 2 work and are not Gate 1 acceptance criteria. They remain the final M17 qualification matrix rather than being silently marked passed by the shell correction.

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
| `uds-ui-equipment-weapons.jpg` | Expand weapons with occupied and proven-empty attachment slots | Total/per-character-slot duration and named nested slots agree with schema-16 profile/CSV; unavailable is never rendered empty. |
| `uds-ui-equipment-armor-and-gear.jpg` | Expand occupied and proven-empty native character slots | Occupied, empty, nested, unknown/modded, and unavailable states remain distinct. |
| `uds-ui-equipment-totems.jpg` | Review direct and tote-carried totem evidence | Presence and proven-active direct sets remain distinct; tote activation is not invented. |
| `uds-ui-economy.jpg` | Compare current holdings, flows, and recent runs with Duckov/profile/export | Money and Cash holdings are separate from Money/Cash flow; liquid wealth appears only when both holdings are current. |
| `uds-ui-crafting.jpg` | Expand output-first and resource-first rows | Successful times, produced units, used quantity, recipes, batches, currency, and reciprocal associations agree exactly. |
| `uds-ui-item-use.jpg` | Review items with different group/effect/heal evidence | Item uses, amount used, HP restored, primary group, and effect tags remain independent facts; icons do not determine identity. |
| `uds-ui-diagnostics.jpg`; `uds-ui-diagnostics-scrolled.jpg` | Scroll both desktop columns independently and expand health groups/technical details | Left contains settings, issues, and technical/log details; right contains grouped Working/Limited/Error health; narrow layout stacks left first. |

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
