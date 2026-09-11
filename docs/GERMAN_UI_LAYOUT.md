# German localization and retained overview/tab layout

This follow-up started from the committed localization baseline `a2452850dc7d558906e5b41a41b239cc6b5cb6ac`. It retains native language-change subscriptions, translation keys, placeholders, statistics definitions and coverage explanations. The historical local packages below retained RC version metadata and are identified by their distinct filenames and hashes. These changes are included in the final [1.0.0 scope](../RELEASE_NOTES.md); the [published rc.2](https://github.com/bamboechop/ultimate-duckov-statistics/releases/tag/v1.0.0-rc.2) and its publication receipt are unchanged.

Initial layout implementation source: `7ed195a53a905bbba0dacfea780df6839e50059c`. The Diagnostics cache and confirmation-caption corrections below follow that delivery. Current review and CI are on [PR #24](https://github.com/bamboechop/ultimate-duckov-statistics/pull/24).

## Causes and implementation

- Profile Summary's scroll clip began at the panel's content inset, while the heading has a shared upward optical offset of 19 reference pixels. The clip therefore crossed the heading. Both overview clips now include the panels' existing outer padding, preserving heading alignment and placing the heading rectangle 11 reference pixels below the top clip. Clipping remains enabled.
- Summary columns previously met at the same X coordinate. Highlights used fixed-height, unwrapped ellipsis text. The overview-only reflow measures active TMP labels and values at their final widths, reserves a 24-reference-pixel column gap, grows rows, and moves Latest run and World time down. Below 1100 desktop pixels labels and values stack. The right overview panel scrolls when reflow needs more height. Latest-run map/statistics text also wraps and grows its card. Other tables retain their layout policies.
- The tab viewport ended at the header's right edge, unlike its left inset, and used the complete button height past the underline. It now has symmetric inner gutters and ends at the underline's top. A native stencil `Mask`, alongside the rectangle clip, contains procedural graphics and TMP effects; mask raycast filtering excludes cropped button portions. Native Unity owns and releases stencil materials.
- Installed `UnityEngine.UI.ScrollRect.OnScroll` reverses Y and maps the dominant axis to X for horizontal scrolling. That moved content right for wheel-down. The tab subclass consumes the event once, treats down/right as increasing the content offset, and handles wheel events forwarded from the chevron gutters. It never forwards tab wheel input to body scrolling.
- Layout previously called selection/focus reveal on every forced projection refresh. Layout now clamps the existing manual offset; selection or keyboard focus explicitly reveals a tab. Width measurements are cached until text or viewport/scale changes. Overflow chevrons reveal the next obscured tab fully when it fits, page through oversized tabs, disable at the ends, and disappear with a zero offset when all tabs fit. Scrolling does not select tabs.
- German uses `Rekorde`, `Itemnutzung`, `Gesamte Bewegungsdistanz`, `Meistgenutzte Waffe`, and `Meistgenutztes Item`. English uses `Total movement distance` for the same overview key. Other uses of “recorded” and item-use terminology remain intact.

The native `Mask` raycast/stencil implementation, `ScrollRect.OnScroll`, `ProceduralImage` lifecycle/material implementation, and `ButtonAnimation` were inspected in the installed Duckov assemblies. No global font reduction, per-frame text measurement, new runtime dependency, visual opening gate, or totem/shadow ownership change was introduced.

## Automated evidence

Validated on 2026-09-11:

- `scripts/build.ps1`: Debug and Release each pass 2,020 main tests and 74 ordinary shell tests; installed-native contract probe, native build, analyzer build, package validation and ordinary IL/path audits pass with zero build warnings/errors.
- Diagnostic shell suites pass 78 tests in each configuration.
- Changed-source formatting, analyzer verification and `git diff --check` pass; no forbidden game/Harmony binaries are tracked.
- Production-shell tests cover English/German switching, empty/populated profiles, 2559×1439, 1280×720, 720×480 and resize to 960×540; measured label/value containment and separation; long label/item/map reflow; subsequent section positions; right-panel scroll/focus retention; fitting/overflowing tab strips, chevrons, wheel axes and event consumption, focus reveal, refresh, resize, close/reopen and listener cleanup. Existing modal/input and totem ownership tests continue to pass.
- The committed localization test was reviewed and retained: it verifies translation-key coverage and actual live switching. New behavior checks execute the production shell/reflow/event implementation. The existing English overview-label expectation follows the requested wording change; no layout expectation was weakened.
- `scripts/verify-reproducibility.ps1` reproduced both DLLs, both PDBs and the deterministic ZIP from immutable source `7ed195a53a905bbba0dacfea780df6839e50059c` in two isolated roots with .NET SDK 8.0.425. That ZIP also matches the local deployed package archive below.

Unity/TMP boundary doubles model hierarchy, activation and approximate text measurements. They do not prove native glyph bounds, shader clipping, raycast execution, GPU effects or final visual quality. Native acceptance remains with the user.

## Initial layout package and deployment

`artifacts/localization-layout/UltimateDuckovStatistics-german-ui-local.zip` contains exactly the five permitted package files and was independently extracted, hash-compared and audited. ZIP size: **666,078 bytes**. SHA-256: `e372da88246a580a0c3cfc03527546ffdf9077bbf4cb9978818781f1c09015f3`.

The ordinary package was deployed with Duckov closed. Independent readback matched all five installed files, and no transaction residue remained. The previous mod installation is retained under `artifacts/deployment-backups/157351369b74491da8af5125e8e3afe2/UltimateDuckovStatistics`.

| File | SHA-256 |
| --- | --- |
| UltimateDuckovStatistics.dll | `86196f48a124ed9391a61b847de519b0dc47401ecab7cc8f6851689b3640d35b` |
| UltimateDuckovStatistics.Core.dll | `9a939986527e29f158dfab9e69a4fa80c0d95e0557f7a29a7e18856dc3e06d2f` |
| info.ini | `88d93f8ef77cb6e9512fd17f56966fe832a692a50ba3c1a37e6ae5dba26fa64e` |
| INSTALL.md | `b696673a097043dcbd3ab5b3375c30419f94f1f86887862ccd0b1ef06ca17fc6` |
| LICENSE | `04e8cf95fce9a5f2ced55639328dabaf55698116acc56bfd0a0912246238541c` |

## In-game acceptance

1. At the supplied desktop resolution, open English and German Overview at scroll top: confirm complete headings/accents/shadows, aligned headings, clear column gaps, complete Highlights, and no overlap with Latest run/World time.
2. Check an empty and a populated profile with long item/map names. Scroll both overview panels and repeat at 1280×720 and a supported smaller window; wrapped rows/cards and all later content must remain reachable.
3. On an overflowing strip, check both chevrons and both ends. Wheel down/right reveals later tabs; up/left reveals earlier tabs. Selection stays unchanged. Check that tabs/effects do not enter gutters or cross the underline, and clicking clipped areas cannot activate a hidden tab.
4. Navigate tabs with the keyboard, then manually scroll away from the selected tab and wait for statistics refresh. Focus navigation must reveal its tab, and routine refresh must preserve manual browsing. Wheel over the body must scroll only the body, with base-game input still blocked.
5. Switch language live, resize between fitting/overflowing layouts, and close/reopen. Bounds and disabled controls must update; a fitting strip must hide arrows and clear its offset. Confirm the existing totem shadows and reset/export modal behavior remain visually intact.

## Diagnostics language-cache correction

`NativeStatisticsPanel.RefreshDiagnostics()` cached already-translated strings using profile revision and runtime evidence. A language change dirtied the profile projection but left that separate cache valid. `HandleLanguageChanged()` now invalidates the diagnostics revision as well. The next normal tick rebuilds and delivers the snapshot after all language callbacks have applied translation overrides; the native Diagnostics view accepts it and marks its retained content dirty.

The regression fails on the prior implementation and passes with the fix for both an already-visible Diagnostics tab and one opened after the switch. It checks English → German → English banner/detail/system text, unchanged profile revision, save receipt, diagnostic entries and profile contents, and cache reuse on subsequent unchanged ticks. The remaining native check is to repeat that language sequence in Diagnostics without recording new statistics and confirm the banner updates immediately on the next UI tick.

Correction source: `1e7c33a7a7013ffd45a3392da6f986313523b6c5`. Debug and Release each pass 2,020 main, 76 ordinary shell and 80 diagnostic shell tests. Native probing/build, formatting/analyzers, package/IL/path checks and independent ZIP extraction pass. Two isolated builds reproduce the DLLs, PDBs and ZIP, matching the local correction archive.

The corrected ordinary package was deployed with Duckov closed, all five installed hashes verified and no transaction residue. The prior layout build is backed up under `artifacts/deployment-backups/bbd39214814e45408429eb6a41993c4b/UltimateDuckovStatistics`. This supersedes the initial local deployment above; the published rc.2 release is unchanged.

- Archive: `artifacts/localization-layout/UltimateDuckovStatistics-german-ui-diagnostics-local.zip`, **666,074 bytes**, SHA-256 `dd56d976a22b395d53c0b1ad0f4f59993da137d6de6ff43aa6789972d8b10ae0`.
- Updated `UltimateDuckovStatistics.dll` SHA-256: `604eea1cb2e3a91e6c1f2efc9e06cc1e04c50aca951066b65e56ffab86e9fb2a`. The other four package-file hashes remain those listed above.

## Retained confirmation-caption correction

The modal title/body refreshed in `PanelModal.Sync`, but Cancel and Confirm Reset captions were translated only in the constructor. Their assignments now run beside the title update, before the existing measured button layout. This covers both a dialog opened after a language switch and a dialog already visible during the switch, including the shared hotkey-capture Cancel button.

All four production-shell regressions fail with the old construction-only captions and pass with the correction. They exercise reset/hotkey modes, English → German → English, switching before opening/while visible, 1280×720 and 720×480 layouts, measured caption fit, retained button/focus identity, cancellation, unchanged listeners and untouched profile contents. Existing reset-dispatch and modal-blocking tests remain in the full suite. Native confirmation: switch the existing shell to German, open the reset dialog, confirm `ABBRECHEN` and `UDS-PROFIL ZURÜCKSETZEN`, then cancel; also check the hotkey dialog's Cancel caption and switching back to English.

Correction source: `e08785e73f958a2c9efb938d3a8f2a4e6a9574f4`. Debug and Release each pass 2,020 main, 80 ordinary shell and 84 diagnostic shell tests. Native probing/build, formatting/analyzers, package/IL/path checks and independent extraction pass. Two isolated builds reproduce the DLLs, PDBs and ZIP, matching the local modal-correction archive.

This ordinary package supersedes the Diagnostics-only local deployment. Deployment ran with Duckov closed; all five installed hashes match and no transaction residue remains. The prior build is backed up under `artifacts/deployment-backups/ab8d73cafcfd49e3b06bcf40ec27a46c/UltimateDuckovStatistics`. Published rc.2 remains unchanged.

- Archive: `artifacts/localization-layout/UltimateDuckovStatistics-german-ui-modal-local.zip`, **666,075 bytes**, SHA-256 `62bf7124c2154a1bf3def96bdf47bd17be170f1274451d14ea39736273f94413`.
- Updated `UltimateDuckovStatistics.dll` SHA-256: `e69ec1323bf6028214f7048948d858a23d02bcfc99d12bd9a7b08905fb14f027`. The other four package-file hashes remain those listed above.

## Retained tab-caption correction

Records, Item Use and Combat retained several captions assigned only during construction. Their existing refresh/bind methods now reload those strings from localization keys before the dirty views are measured: Records section headings, empty-map/unavailable messages and pooled View run buttons; Item Use's empty/unavailable messages; and Combat's firing-action explanation/unavailable message. No per-tick measurement or additional layout invalidation is introduced.

Six regressions fail with the old captions and pass with the fix. The shell suite now source-links these three production views and their text-measurement adapter, replacing empty child-view substitutes. Coverage includes English → German → English, visible/hidden tabs, desktop/narrow layouts, caption fit, retained Records buttons/listeners, unchanged profile contents/revision, and no measurement on a subsequent unchanged tick. Unity/TMP assets and scroll physics remain isolated boundaries; glyph ink and native interaction are not automated visual proof.

Correction source: `6ead50cfbca0b524772e3423222cea7cf53b139b`. Debug and Release each pass 2,020 main, 86 ordinary shell and 90 diagnostic shell tests. Native contract probing, warning-free builds, formatting/analyzers, package/IL/path audits and independent extraction pass. Two isolated builds reproduce the DLLs, PDBs and ZIP, matching the local archive.

This package supersedes the modal-only local deployment. Deployment ran with Duckov closed; independent readback matched all five installed files and found no transaction residue. The prior build is backed up under `artifacts/deployment-backups/cb15bbd38b1849d2a0784ead8fa7ec8d/UltimateDuckovStatistics`. Published rc.2 remains unchanged.

- Archive: `artifacts/localization-layout/UltimateDuckovStatistics-german-ui-retained-tabs-local.zip`, **666,128 bytes**, SHA-256 `0f387bbf611312f30ebd63cf8daa65c5c6929faf76aad7c0e341504e8d093bfc`.
- Updated `UltimateDuckovStatistics.dll` SHA-256: `8ac5e74a03c5b89ba8f51a30b51451d62d8718ec9c82b60cbc1cf2f276962e61`. The other four package-file hashes remain those listed above.

Native acceptance: create the shell in English, switch to German, then open Records and check both section headings and View run buttons. Check Item Use's empty message and Combat → Weapons & ammunition's firing-action explanation. Repeat while each view is visible, switch back to English, and check desktop/narrow wrapping and button fit.
