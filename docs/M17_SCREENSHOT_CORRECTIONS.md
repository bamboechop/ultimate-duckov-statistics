# M17 screenshot corrections, 2026-09-07

This follow-up addresses the Economy, Crafting, Item Use and Diagnostics screenshots supplied after the [initial remaining-tabs delivery](M17_REMAINING_TABS_DELIVERY.md).

## Changes

- Section headings use visible glyph ink for their top inset and reserved height. Native font, width and horizontal padding are preserved; no visual acceptance gate is added to shell opening.
- Economy and Diagnostics expand indicators rotate the native-supported right chevron. The unsupported downward glyph was confirmed as a missing-glyph warning in the installed Player.log.
- Diagnostics hotkey, action buttons and log filters measure with the same horizontal text insets used by their renderer, including wrapped-label height.
- The four expanded Item Use statistics place their values above their uppercase labels. The three overall summary cards retain their existing order.
- Diagnostics displays `…/UltimateDuckovStatistics` with an adjacent copy button for the actual data directory. Export locations show only their final component. Displayed diagnostic text redacts the current user-profile directory and conventional Windows user-directory paths; clipboard actions keep the original full paths.
- Available native menu entries publish the Working color independently of unavailable siblings. Experimental Economy tracking says Limited rather than incorrectly saying current tracking is unavailable.

## Explanations and unchanged evidence boundaries

The reported Economy screenshot shows Money and Cash as **last observed**, not current. Liquid wealth requires both components to be current; adding the stale values would imply a current total that UDS cannot establish. Existing current/last-observed tests cover this distinction.

Item Use filters match UDS's single canonical primary group. This is a UDS classification derived from native usage behaviors, not Duckov's inventory tags such as Syringe or Medical Supply. The precedence is Healing, Debuff removal, Food, Drink, Buff, Special, Other. All proven effects remain visible separately. `NativeItemClassifier` treats a nonzero food-energy or hydration change as the corresponding effect, including a negative change. Consequently the reported Food/Drink/Buff combination selects Food, without establishing that Duckov itself calls the yellow injector food. This follow-up does not change classification or reinterpret recorded statistics.

The installed Economy adapter intentionally publishes Limited source/context attribution: completed sales and Money rewards have semantic attribution, while several other Money changes remain unknown adjustments. Proven raid Cash acquisition covers only verified pickup evidence and excludes ambiguous transfers/re-pickups. Run Cash outcomes remain unavailable because supported public events cannot prove terminal disposition across fungible main, pet and storage ownership. Exact supported amounts remain usable independently.

The local diagnostic records a base pause-menu injection attempt that failed to find a safe native Settings/Options/Mods anchor. This is an observed access failure, not a statistics failure or merely an unopened-menu state. The outside-raids hotkey remains available. This follow-up corrects status presentation but does not claim to repair or qualify that native menu integration.

## Verification and manual acceptance

Before packaging, the complete Debug suite passed 1836/1836 with no failures or skips, and the installed-native Release build passed with zero warnings/errors. Added checks cover path redaction, wrapped value-first layout, independent menu colors, limited Economy presentation and glyph-ink padding. Native test doubles cover the measurement boundary, not actual Unity rendering.

Manual verification should revisit all reported heading insets and open chevrons; F8 and longer hotkeys; export/reset labels; all three Diagnostics log filters; four expanded item statistics; and path visibility plus full-path copying. Check normal desktop and narrow layouts. Gameplay, live UI/input/audio qualification and real profile reset remain user-controlled.
