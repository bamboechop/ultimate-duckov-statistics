# M17 final UI comparison — 2026-09-07

## Scope and evidence

Compared all 30 JPG references in [mockups](../mockups/README.md) with the retained shell, nine tab implementations, presentation/layout policies, native menu integration, and operation modal/toast paths. Source baseline: `cff4221`; small corrections are recorded below. This is a mock-to-source audit, informed by the user's supplied screenshots, not a fresh in-game screenshot acceptance run.

Later user decisions take precedence over the original images: 75% black backdrop; value-first quick statistics with uppercase muted labels; View run inside expanded Economy/Item Use cards; private paths with explicit copy feedback; grouped issues; initially closed Diagnostics details except Technical details. Example amounts, dates, names, capabilities and counts in mocks are not fixtures.

## Small — corrected

| ID | Finding | Correction |
| --- | --- | --- |
| S1 | Combat Enemies/Incoming damage numbers and numerical headers were left-aligned; summary/detail metric values also lacked right alignment. | Right-align desktop numerical columns and their headers, plus metric values. Narrow stacked table entries remain left-aligned. |
| S2 | Combat section headings inherited the extra 15-unit row inset despite their enclosing section already supplying padding. | Remove that inner horizontal inset, retaining the recent visible-glyph top alignment and heading/suffix relationship. |
| S3 | Opening a Diagnostics accordion replaced the status color with white, including an Error or Working status. | Preserve Working/Error colors in both states. Limited stays white on the orange expanded header to avoid orange-on-orange text; warning labels retain their existing white treatment. |
| S4 | Item Use's centered empty-state instructions were white, while the empty mock uses muted text. | Use the shared `#b1b1b1` muted color. |
| S5 | Runs and Equipment called native tooltip pointer-exit on every refresh, even for unchanged slot text. | Dismiss/rebind only when the displayed text changes. Hiding a control still invokes the native provider's OnDisable cleanup. |

S1–S4: `ee3d04a1f8f4e0158e1a479a77ccb891083a901f`, with Limited-status contrast correction `7280ee8`. S5: `5276bb2c81da59b221fcb99d1bcc0b32024badf3`. No statistics, saves, exports or capability semantics change.

Source: [Combat rendering](../src/UltimateDuckovStatistics/UI/RetainedCombatView.cs), [Combat layout](../src/UltimateDuckovStatistics/UI/CombatViewPolicy.cs), [Diagnostics rendering](../src/UltimateDuckovStatistics/UI/RetainedDiagnosticsView.cs), [Item Use rendering](../src/UltimateDuckovStatistics/UI/RetainedItemUseView.cs), [Runs rendering](../src/UltimateDuckovStatistics/UI/RetainedRunsView.cs), [Equipment rendering](../src/UltimateDuckovStatistics/UI/RetainedEquipmentView.cs).

## Medium — approved and implemented

### M1. Consistent run dates

Overview uses `dd.MM.yyyy - HH:mm`; Runs, Records, Economy and Item Use use `yyyy-MM-dd - HH:mm:ss`. The mocks themselves vary between ordinary run dates and technical timestamps, so there is no single literal format to copy everywhere. Equipment recent loadouts also display the run's end timestamp, while the other run cards display its start timestamp.

**Implemented after approval:** player-facing run cards and Records use `yyyy-MM-dd - HH:mm` (corrected after the user clarified the date-order preference); recent Equipment loadouts now use run start time. Diagnostics and holding observations retain seconds. Export and persisted timestamps are unchanged.

Source: timestamp formatting in [Overview projection](../src/UltimateDuckovStatistics/UI/StatisticsPanelProjection.cs), [Runs](../src/UltimateDuckovStatistics/UI/RunsPresentation.cs), [Records](../src/UltimateDuckovStatistics/UI/RecordsPresentation.cs), [Economy](../src/UltimateDuckovStatistics/UI/EconomyPresentation.cs), [Item Use](../src/UltimateDuckovStatistics/UI/ItemUsePresentation.cs), [Equipment](../src/UltimateDuckovStatistics/UI/EquipmentPresentation.cs).

### M2. More compact Equipment detail lists

The Weapons, Armor & gear and Totems mocks distinguish small group headings and compact subordinate rows from major section headings and expandable item cards. `EquipmentDocument` currently gives every heading size 40, reserves at least 80 units for icon rows, and shares caption/row spacing across those roles. Active-totem sets are composed from separate item rows rather than the mock's compact combined member block. The result is a looser hierarchy and more scrolling, even after correcting duplicate heading padding.

**Implemented after approval:** major/group headings and compact subordinate rows have separate measured styles. Active-set members share their parent surface. Native typography, expandable item headers, membership, captions and evidence notices remain intact.

Source: [EquipmentDocument](../src/UltimateDuckovStatistics/UI/EquipmentViewPolicy.cs), [Equipment renderer](../src/UltimateDuckovStatistics/UI/RetainedEquipmentView.cs); compare the four Equipment JPGs.

### M3. Combat weapon/ammunition detail presentation

The mock gives the selected weapon a simple orange selector and displays plain ammunition entries on the right. The implementation additionally renders selected-weapon metric rows and gives ammunition the same dark item-row background used by weapon selectors. The extra metrics are real supported information, so deleting them to match a screenshot would be a product choice.

**Implemented after approval:** ammunition rows are plain; additional metrics live in an initially collapsed weapon-details block on the right. Expansion survives same-generation refresh, remains specific to each weapon, and clears when identity is lost. Firing-action and percentage definitions remain intact.

Source: `CombatDocument.Items`, `CombatLayoutPolicy.HasBackground` in [Combat layout](../src/UltimateDuckovStatistics/UI/CombatViewPolicy.cs); compare [Weapons/ammunition mock](../mockups/uds-ui-combat-weapons.jpg).

### M4. Native-menu naming

Both native entry mocks use “Ultimate Duckov Statistics”; the implementation deliberately has a short “Statistics” translation key. This is a confirmed naming difference, not an access failure. Chart-icon replacement exists but depends on finding a native Image under an icon-named transform; the current installed menu appearance needs visual verification before claiming that part matches.

**Implemented and visually accepted:** both menus use “Ultimate Duckov Statistics”. The single `ui.menu_entry` key can revert to “Statistics”, “UD Statistics” or “UDS” if a future naming change is requested. Installed Duckov 2.3.30 level1 assets confirm the main-menu Settings icon is a direct sprite-bearing Image child named `Image`, separate from ProceduralImage background/hover layers. Icon replacement now targets that native image. The native HorizontalLayoutGroup controls child width from preferred measurements and retains its spacing/font metrics; the user confirmed the full name looks good and works.

Source: `ui.menu_entry` in [UiText](../src/UltimateDuckovStatistics/UI/UiText.cs), `ApplyLocalizedButtonText` and `ApplyStatisticsIcon` in [native integration](../src/UltimateDuckovStatistics/UI/NativeUiIntegration.cs).

## Large — discuss value before work

### L1. Per-enemy world-death ownership expansion

The Enemies mock expands Soldier into Other NPC / Environmental / Unknown world-death counts. Production `CombatPresentationFactory` never supplies `OwnershipBreakdown`, so those rows correctly have no expansion. The renderer supports the shape, but the underlying saved aggregate has separate `Enemies` and `Ownership` dictionaries, not an enemy-by-owner cross-tab. Global ownership totals cannot truthfully be assigned to individual enemies.

Implementing this requires capturing the joint dimension for future observations, persistence/normalization/recovery/export changes and production-path tests, followed by UI composition. Existing totals cannot be backfilled reliably. This is a feature/data-model change, not a missing chevron fix. Recommendation: defer unless that breakdown is useful enough to justify the additional metric family.

Source: [Combat presentation](../src/UltimateDuckovStatistics/UI/CombatPresentation.cs), [Combat aggregate](../src/UltimateDuckovStatistics.Core/Statistics/CombatStatistics.cs), and the existing `EnemySortUsesAllTieBreakersAndExpansionHasNoOwnershipCrossDimension` test in [retained Combat tests](../tests/UltimateDuckovStatistics.Tests/RetainedCombatTests.cs). Reference: [Enemies mock](../mockups/uds-ui-combat-enemies.jpg).

## Coverage and accepted differences

| Surface / reference states | Result of source comparison |
| --- | --- |
| Shared shell, header, tabs, back, dimmer | Tab order and native typography path retained; later 75% backdrop and rounded-hover decisions preserved. No further confirmed small mismatch. |
| Overview | Profile summary, highlights, latest run and world time remain mounted. Latest-run button identity survives refresh. M1 covers date consistency. |
| Runs | History, selected-run header, value-first summary, ordered route, terminal slots, combat and equipment evidence are present. S5 addresses tooltip refresh. M1 covers dates. |
| Records + scrolled | Overall and per-starting-map sections, exact View run routing, row bands and no-death/no-map states are present. M1 covers dates. |
| Combat Summary / Enemies / Weapons / Incoming | S1/S2 correct alignment; M3 covers weapon detail styling; L1 covers missing per-enemy ownership evidence. Sorting and truthful unavailable values remain. |
| Equipment Loadouts / Weapons / Armor / Totems | S5 corrects tooltip refresh; M2 covers density/hierarchy. Terminal/most-used/selected equipment meanings stay distinct. |
| Economy + partial | Holdings, separate Money/Cash flows, sources, contexts, subordinate proven acquisition and recent runs are present. Later value ordering and run-button placement preserved. Current availability comes from evidence, not the sample mock. |
| Crafting | Both rankings and expansion directions present; successful crafts, produced quantities and consumed resources stay distinct. Corrected section insets retained. |
| Item Use + empty | Raid scope, filters, three top metrics, four item-detail metrics, groups and recent runs present. S4 corrects empty text. Multi-effect filtering is an accepted later change; group totals remain primary-group counts. |
| Diagnostics normal / scrolled / error / fallback | S3 corrects expanded status color. Private paths, grouped recent issues, log filtering and initially collapsed detail groups are retained. Schema numbers instead of the mock's “Current” are useful technical detail. |
| Reset confirmation / success / failure | Cancel-first modal, protected game save, archive semantics, operation result and failure toast paths present. No real reset performed. |
| Export success / failure | Foreground result toasts plus persistent result/path and copy feedback present. This audit did not export the user's profile. |
| Main menu / base pause / raid pause / in-raid hotkey | Native clone/layout/input lease and outside-raid access policy present. M4 covers naming; user previously confirmed access/input fixes. No game session started for this audit. |

Baseline-impossible Economy capabilities and unprovable tote activation are documented data limitations, not visual bugs to mask. Their M18 cleanup remains governed by the repository instructions. Literal mock values, generic historical Partial labels, replacement fonts and pixel-copying native controls are not proposed fixes.

## Verification boundary

Verified for the completed small corrections on 2026-09-07: all 1,853 Release tests passed; Duckov 2.3.30 compatibility probe passed; native build had zero warnings/errors; exact five-file package/ZIP validation passed. Deployed while Duckov was closed and compared all five files between package, ZIP and installation: every hash matched. This does not establish visual, hover/audio or gameplay acceptance.

- Release ZIP SHA-256: `38e6699b638628bb5a4c05b97331a7685a9d5030efefb8f5038d6439b8013eb3`.
- Native DLL SHA-256: `b01ca7210eb196b5f45ca03c52f49282d36da01d8b892e04274f10a3f9671b3d`.

Remaining in-game checks: native menu icon and long label fit; stable button focus/audio and slot tooltips across multiple refreshes; scroll clipping and heading baselines at the user's resolution; a longer-language/narrow-window pass. These are verification items, not claimed confirmed defects. M1–M4 are implemented following user approval; L1 remains deferred.

### Approved medium-change delivery

M1–M4 passed all 1,855 Release tests, the Duckov 2.3.30 compatibility probe, zero-warning native build and package validation. Deployed with Duckov closed; all five installed files match the package and ZIP by SHA-256. The user subsequently accepted the full native-menu name.

- ZIP SHA-256: `2036335d952d6285c62bc9761ceff912e63e15e28515b7f63076a65265105f26`.
- Native DLL SHA-256: `c98fe14327adae7b324a8bd31ebe30c37028d22b5ff3985ceed7f8a7bf39a829`.

### Follow-up visual corrections

Run dates use YYYY-MM-DD across all tabs; technical timestamps still include seconds. Equipment navigation rows now use the same measured text height plus 24 units of padding (minimum 64) as Combat. Nested attachment and armor/gear rows carry explicit slot evidence into the renderer, restoring the empty glyph for proven empty slots. Native menu chart icons retain their cloned tint, including the main menu pale blue. The full name is accepted.

The additional player-kill-classification section was subsequently removed on request. Combat Summary no longer presents its heading or rows; underlying recorded partitions and total Kills by you remain intact.

Follow-up verification: 1,855 Release tests passed; compatibility probe, zero-warning native build and package checks passed. Deployed with Duckov closed; all five package/ZIP/installed files matched. ZIP SHA-256: 5063CC80A3E139E8A05E7AEEEEEEB72EF87AEF7F038EA6385DA2E245269F06D8. Native DLL SHA-256: F208BB106D7D5B0E943929B526F3B1294ABA133A44703C4C7E50AF0B0856DF32. Updated appearance awaits user confirmation.

Section removal (2a667ea): all 1,855 Release tests, compatibility probe, native build and package checks passed. Deployed with Duckov closed; all five installed hashes matched the package.
