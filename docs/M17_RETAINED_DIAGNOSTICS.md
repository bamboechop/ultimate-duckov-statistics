# M17 retained Diagnostics and profile operations

Diagnostics is mounted in the shipped retained shell after Item Use. It presents the current generation's recorded capability inventory, save receipt and recovery result, native menu observations, and bounded diagnostic entries. Opening, layout, scrolling and refresh do not read or write profile files. The controller polls operation completion on the main thread even when the panel is closed.

Recent issues group repeated severity/category/guidance into at most 12 entries, showing the latest timestamp and report count from the newest 50 log records. All left-side accordions except Technical details initially stay collapsed. Clipboard results appear in a local tooltip above the panel. See [Diagnostics and base-access corrections](M17_DIAGNOSTICS_ACCESS_CORRECTIONS.md) for evidence and verification boundaries.

## Accepted states and production sources

| Reference section/state | Production source and implemented behavior |
| --- | --- |
| Data & settings | Exact profile/manifest/statistics generation; last successful `ProfileSaveReceipt` for that generation; coordinator data directory; persisted configurable keyboard shortcut. Last-saved time never comes from the wall clock or a requested write. |
| Recent issues | Current failed capability groups and recent warning/error entries, with consequence and recovery guidance. Ordinary informational lifecycle entries stay in the technical log. Menu fallback and export/reset/clipboard failures have separate user-facing descriptions. |
| Technical details, including scrolled reference | Native game/mod versions and current format versions; typed primary/backup/temporary open results, repair/arithmetic evidence and interrupted-run/session recovery; actual capability limitations; newest 50 entries with All/Warnings/Errors filters. |
| Tracking systems | Exact current capability IDs, including successful throwable releases. Groups expose independent metrics and nested adapter ID, version, state and detail. Missing or conflicting supported-contract evidence cannot become Working. Baseline-impossible metrics are omitted and documented as limitations; supported siblings retain independent states. |
| Tracking error | A failed supported metric makes its affected system Error and the tracking banner Error. Independently working rows remain usable. Experimental supported tracking is Limited. |
| Native menu fallback | An unavailable or unverified menu entry makes Menu access Limited. The configured outside-raids hotkey remains available, and access failure alone leaves the tracking banner healthy. |
| Reset confirmation | A shell-owned blocking overlay describes archive/new-generation semantics and explicitly preserves Duckov saves. Cancel receives initial focus; keyboard traversal remains within the modal; Escape cancels; obscured controls become non-interactable. |
| Reset success/failure | Success follows exact-token completion of the production transition and a different committed generation. A safely rolled-back failure leaves the previous UDS generation active and settles as Failure. An unresolved persistence/handoff boundary remains Pending and single-flight. |
| Export success/failure | The existing JSON/CSV writer runs on a fully detached generation snapshot. Success follows completion of every export write; only verified clipboard readback produces the copied acknowledgment. Clipboard failure retains a visible, copyable export directory. Failed exports report failure without claiming tracking stopped. |

The default desktop composition has equal-width independent columns. Narrow viewports stack Data & settings/issues/technical details above tracking health, with bounded inner and outer scrolling. Panels reuse native typography/materials, button animation and feedback, measured wrapping, rounded clipping and the existing Runs overflow cues. Expanded accordions keep a translucent black group background and 10px separation. No visual acceptance assertion runs in the panel-opening path.

## Operation boundary

Mouse and keyboard use one `PanelOperationController` and the existing `PanelOperationGate`. Starting either export or reset immediately excludes duplicate or overlapping submissions. Reset confirmation is tied to the generation displayed when it opened and closes if that generation changes. A queued operation rechecks generation before invoking the service. Closing the UI does not launch another operation or discard an in-flight result; disposal cancels only actions not yet invoked and observes detached export faults without callbacks into destroyed Unity objects.

`BeginExportCurrent` drains the existing persistence barrier, captures the export snapshot on the owning thread, and writes it in the background. This export-specific snapshot deeply detaches mutable runs, totals and nested collections; the deferred persistence snapshot's intentional structural sharing is not used across concurrent gameplay/export work.

`ResetCurrent` reuses the production archive rotation and transition queue. Immutable `NativeUserResetAttempt` evidence identifies the requested generation, transition token, Pending/Success/Failure outcome, completed generation and failure detail. A precommit failure whose rollback is proven cannot remain queued and silently reset the profile when storage later recovers. Failed rollback recovery and postcommit handoff work retain the transition boundary until safe settlement; the UI does not claim that either succeeded early. No real user profile is reset by the automated verification.

## Verification boundary

`RetainedDiagnosticsTests` checks the actual published capability inventory, independent group health, fallback access, exact generation/save receipts, recovery, actionable issues, bounded filters, selection and responsive scroll policies. `PanelOperationControllerTests` exercises duplicate input, cancel/default confirmation, changed generations, asynchronous success/failure, clipboard outcomes, reset token matching, pending settlement and disposal. Export/reset storage tests use isolated temporary fixtures.

Native compilation verifies installed type/member compatibility. Screenshot fidelity, sounds, native input ordering, cursor restoration and live repeated-open/close behavior remain user-controlled acceptance in [M17_MANUAL_VALIDATION.md](M17_MANUAL_VALIDATION.md). Passing automated checks does not certify those runtime observations.
