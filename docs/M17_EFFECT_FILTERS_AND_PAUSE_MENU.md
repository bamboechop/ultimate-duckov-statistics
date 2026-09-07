# M17 effect filters and base pause-menu correction

This follow-up supersedes the primary-only filter behavior and unresolved pause-menu diagnosis recorded in [the first screenshot corrections](M17_SCREENSHOT_CORRECTIONS.md).

## Item Use

Each filter accepts either the recorded primary group or its corresponding effect tag. Thus a Food-primary item with Food, Drink and Buff effects appears in all three filters. Debuff removal and Buff remain distinct; localized text and item-name substrings do not change matching. Effects are copied into the retained snapshot. Overall and Uses by group totals remain unchanged and count each activation once by primary group. No persisted classification or tracking semantics change.

## Base pause-menu investigation

Read-only inspection of the installed Duckov 2.3.30 / Steam build 24013657 resources and managed contracts found:

- The native `PauseMenu/Menu/Layout/Btn_Options` anchor exists, has localization key `UI_Menu_Options`, and is the unique highest-ranked candidate under the existing policy.
- Button object 113118 targets Image 89272, whose raycast target is enabled. The button GameObject (23951) has a RectTransform with a zero serialized size delta; the native layout groups supply its geometry.
- The previous Player.log explicitly records rejection of the cloned button's raycast/layout check, immediately followed by the misleading generic missing-anchor warning.

The injected button was being destroyed before its first native layout pass because its rectangle was still zero-sized. The production geometry validator has been removed entirely. Injection preserves native layout and requests its next layout rebuild. Missing/ambiguous anchors and attachment exceptions still fail explicitly; generic access warnings no longer misreport every failure as a missing anchor. Diagnostics still requires an observed activation before declaring the entry Working.

Ignored native inspection evidence is in `artifacts/m17-effects-menu-20260907/`. This source correction requires manual verification by opening the pause menu in base, selecting Statistics, and checking close/reopen. No gameplay or live UI interaction was performed by the agent.

## Holdings and capability clarification

Money and Cash become current automatically after the loaded save's EconomyManager and authoritative main/storage/pet inventories are ready. Loading another scene marks the previous observations stale; the main menu lacks the live owned-inventory roots. Inspect Economy through F8 after the base finishes loading. No additional deposit or withdrawal is required, and prior-run transactions do not make unloaded main-menu holdings current.

Limited Money source/context and raid Cash acquisition metrics have working, intentionally incomplete evidence paths. The shared Economy adapter also provides fully supported amount/direction metrics. Run Cash terminal outcomes are a separate baseline-impossible capability; they cannot be inferred from acquisition or net flow. The M18 baseline-impossible cleanup policy applies to that unsupported surface and its exclusive artifacts, not to removal of the functioning shared Economy adapter.

## Automated verification

The complete Debug and Release suites each passed **1844/1844**, with zero failures or skips. The installed-native Release build passed with zero warnings/errors. Filter tests cover every mapped effect, unchanged primary-group totals, localization independence and detached snapshots. The native menu correction is supported by the installed prefab and recorded rejection path; live activation remains a manual check.

No package or deployment was performed for this follow-up, at the user's request while further changes are collected.
