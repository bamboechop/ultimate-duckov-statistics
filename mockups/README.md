# M17 native UI mockups

This directory contains the accepted visual and interaction references for the planned M17 native UI overhaul. The images cover the major panel views plus representative scrolling, empty, partial-history, degraded-capability, failure, confirmation, and access states.

The mockups are design references, not executable specifications or current v0.15 screenshots. Example names, dates, paths, values, statuses, and counts demonstrate hierarchy and wording only. `PLAN.md`, the installed-native contract documents, and the persisted profile/export contracts remain authoritative for metric meaning and availability. Native Duckov runtime behavior takes precedence over literal pixel copying when implementing focus, scaling, controls, or layout.

The JPGs must not be included in the installable mod package or used as a source of redistributed runtime game assets. Duckov names and visuals remain the property of their respective owners.

## Final navigation

Use this exact tab order:

1. Overview
2. Runs
3. Records
4. Combat
5. Equipment
6. Economy
7. Crafting
8. Item Use
9. Diagnostics

`Item Use` replaces the pre-overhaul `Items` tab name. A shared header remains stable while each tab renders and bounds its own content.

## Screen inventory

### Main views

- [Overview](uds-ui-overview.jpg)
- [Runs](uds-ui-runs.jpg)
- [Records](uds-ui-records.jpg)
- [Records, scrolled](uds-ui-records-scrolled.jpg)
- [Combat — Summary](uds-ui-combat-summary.jpg)
- [Combat — Enemies](uds-ui-combat-enemies.jpg)
- [Combat — Weapons and ammunition](uds-ui-combat-weapons.jpg)
- [Combat — Incoming damage](uds-ui-combat-incoming-damage.jpg)
- [Equipment — Loadouts](uds-ui-equipment-loadouts.jpg)
- [Equipment — Weapons](uds-ui-equipment-weapons.jpg)
- [Equipment — Armor and gear](uds-ui-equipment-armor-and-gear.jpg)
- [Equipment — Totems](uds-ui-equipment-totems.jpg)
- [Economy](uds-ui-economy.jpg)
- [Crafting](uds-ui-crafting.jpg)
- [Item Use](uds-ui-item-use.jpg)
- [Diagnostics](uds-ui-diagnostics.jpg)
- [Diagnostics, scrolled](uds-ui-diagnostics-scrolled.jpg)

### Supporting data and operation states

- [Economy with partial history and non-current holdings](uds-ui-economy-partial.jpg)
- [Item Use with no recorded use](uds-ui-item-use-empty.jpg)
- [Diagnostics with a tracking-system error](uds-ui-diagnostics-error.jpg)
- [Diagnostics with unavailable native menu integration and working F8 fallback](uds-ui-diagnostics-fallback.jpg)
- [Reset confirmation](uds-ui-diagnostics-reset.jpg)
- [Reset successful](uds-ui-diagnostics-reset-successful.jpg)
- [Reset failed](uds-ui-diagnostics-reset-failed.jpg)
- [Export successful](uds-ui-diagnostics-export-successful.jpg)
- [Export failed](uds-ui-diagnostics-export-failed.jpg)

### Access states

- [Main-menu entry](uds-ui-main-menu.jpg)
- [Base pause-menu entry](uds-ui-pause.jpg)
- [Raid pause-menu entry disabled](uds-ui-pause-ingame.jpg)
- [In-raid hotkey rejection](uds-ui-ingame.jpg)

The German menu text reflects the available native reference captures. Production strings remain localized resources with English fallback; the mockups do not constrain the runtime language.

## Cross-screen semantic rules

### Availability and evidence

- A proven zero renders as zero. It is not empty, unavailable, or unknown.
- `Empty` requires a readable native slot that was explicitly observed without content.
- Missing, unreadable, historically absent, degraded, or ambiguous evidence renders as `Unavailable` or the appropriate historical/partial state; it never becomes zero or proven empty.
- Unknown/modded identities remain visible with stable fallback names and deterministic icon treatment.
- Example partial, error, and empty states are reusable patterns. M17 does not require a separate full-screen mockup for every tab and every permutation.

### Terminology and units

- User-facing Economy text calls item currency `Cash`; technical documentation may still say physical Cash when distinguishing item type 451 from account-style Money.
- Holdings answer what is owned at a trustworthy observation time. Flows answer recorded changes since UDS tracking began. Neither is profit.
- Use ordinary-language units instead of bare `Actions` or `Consumed` headings. Distinguish item uses, amount used, HP restored, firing actions, equipped duration, successful crafting times, produced units, and resources used.
- Use the display timestamp format `yyyy-MM-dd - HH:mm:ss` with a 24-hour clock wherever the full timestamp is shown.

### Economy

- `Current holdings` contains Money, Cash, and conditional liquid wealth. Money is the current ATM/account balance; Cash is the owned top-level Cash total across the exact supported main inventory, storage, and pet-inventory roots.
- `Money flow` and `Cash flow` remain separate from holdings and retain inflow, outflow, net flow, sources, and contexts.
- Recent-run summaries show Money net and Cash net as the two peer values.
- Proven raid Cash acquisition is subordinate to Raid Cash inflow through `of which proven acquired`; it is not a third peer net value, current Cash, secured Cash, or profit.
- `Current`, `Last observed`, and `Unavailable` remain distinct. One unavailable holding must not hide a valid sibling. Liquid wealth is unavailable unless both Money and Cash are current and comparable.
- Partial history explicitly states that totals include recorded changes only.

### Crafting and Item Use

- `Most crafted items` ranks successful crafting actions, phrased as times crafted. Expansion distinguishes produced quantity from exact resources used.
- `Most used crafting resources` ranks exact consumed resource quantity. Expansion shows the outputs produced with that resource and the resource quantity used for each.
- Plain-language examples include `9 times`, `150 produced`, `18 used`, and `using 18 Metal Plates`; these phrases describe units and do not establish the example values as fixtures.
- Item Use reports successful raid use only. Base use does not enter statistics. Uses, amount used, HP restored, primary group, and effect tags remain independent facts.

### Diagnostics

- The desktop composition uses equal-width, independently scrollable columns. At a narrow viewport, the left column stacks above the right column so Data & settings remains the first content users encounter.
- The left column contains Data & settings, Recent issues, and Technical details. Technical details contains versions, recovery/data integrity, known limitations, and the bounded diagnostic log.
- The right column reports current tracking-system health. Each system accordion exposes user-facing sub-capabilities; native contract details remain nested inside the same system rather than moving into a separate global section.
- `Working` means the supported metrics in that group are currently trustworthy. `Limited` means tracking remains usable but a documented sub-capability or access path is unavailable. `Error` means affected statistics may be incomplete.
- A native menu-integration failure leaves statistics tracking healthy when F8 still works. It produces a limited Menu access group and a warning rather than a global tracking error.
- Recent issues contains actionable warnings and errors with their consequence and recovery guidance. Informational lifecycle entries belong in the diagnostic log.

### Reset and export

- Reset confirmation states that the current UDS generation will be archived read-only and replaced with a new empty UDS profile. Duckov save data is unchanged, and the action cannot be undone from within UDS.
- Successful reset reports that the previous generation was archived. Failed reset reports that no statistics were removed and the existing profile remains active.
- Successful export reports that the export location was copied to the clipboard.
- Failed reset/export creates a Recent issue and directs deeper technical investigation to `Player.log`. It must not imply that unrelated tracking stopped.

### Access and interaction

- The main-menu and base pause-menu entries are the primary access paths. F8 is the configurable shortcut and compatibility fallback. All paths open the same single panel instance.
- During a raid, the pause-menu entry is hidden or disabled. F8 never opens the panel and shows the localized outside-raids response.
- If native menu integration is unavailable, F8 remains active outside raids and Diagnostics reports the limited menu capability.
- If the active UDS generation cannot be proven, access fails closed without displaying another generation or inventing an empty profile. A normal no-selected-slot screen is not assumed unless installed-native evidence proves that state reachable.
- At narrower viewports, multi-column desktop layouts may collapse into one vertically scrolling column instead of compressing their contents. Stack regions in desktop reading order from left to right, with the leftmost/primary region first. Responsive placement must preserve every section, control, value, and availability state rather than omitting or merging information.
- Keep the tab strip on one horizontally scrollable row when all nine tabs do not fit. The selected or keyboard-focused tab remains visible; labels remain readable and do not wrap into multiple navigation rows.
- Expanded rows, filters, selected tabs, scroll position, focus restoration, and hover/pressed/disabled feedback must behave like native controls. The 2560×1440 compositions do not authorize a fixed-size implementation.

## M17 implementation checks

Implementation must verify the mockup intent against representative resolutions and UI scales, including a narrow viewport such as 1024×768; left-first stacked multi-column content; horizontally scrolled tab selection; keyboard and mouse navigation; localization and long-name overflow; independently bounded scrolling; large-history performance; missing/modded icons; repeated setup and open/close cycles; every access path; and exact UI/profile/JSON/CSV agreement. The supporting-state images define representative patterns; deterministic tests should cover additional valid combinations without requiring more static mockups.
