# M18 acceptance record

M18 is release hardening of the completed M17 feature baseline. Starting source: `443fad6a268916a6038c919501a714d1f17da371`. Candidate source and artifact identities will be recorded after qualification. Publication is user-controlled.

| Requirement | Implementation / evidence | Status |
| --- | --- | --- |
| Baseline | Clean checkout and live GitHub main matched the starting SHA; no existing M18 branch or open PR on 2026-09-07 | Pass |
| Native baseline | Installed probe: Duckov 2.3.30, Steam 24013657, Unity 2022.3.62f2, Harmony 2.4.1.0 | Pass |
| Current v1 persistence only | Separate format identity and data namespace; remove predecessor readers while retaining validation, recovery and retry ownership | Not exercised |
| Native adapter and metric removal | Read-only adapter-by-adapter contract and caller audit underway | Not exercised |
| UI runtime cleanup | Audit retained material/font construction and obsolete immediate-mode panel; long-label/overflow access regressions | Not exercised |
| Performance | Existing M8.1 method, 5% median / 10% p99 engineering targets and all spike/ceiling rules; final matrix must precede captures | Not exercised |
| Build privacy and reproducibility | Normalized source paths; PE/PDB/package scans; two-checkout artifact comparison | Not exercised |
| Automated qualification | Debug/Release, formatting/analyzers, native probe/build, source/package audits, isolated recovery/composition fixtures | Not exercised |
| Independent review | Fresh bounded whole-system reviews, then immutable full-range integration review and correction re-review | Not exercised |
| Final package and deployment | Independent ZIP extraction, exact inventory and hashes, UDS-only reversible deployment with Duckov closed | Not exercised |
| User qualification | Clean/current-format install, fresh/progressed saves, access/localization, degradation, interruption/recovery, multi-map/high-history gameplay, export agreement, shutdown | Not exercised |

Initial removal inventory: `ProfileMigrator` mixes predecessor migration with current recovery validation and normalization; `ProfileRepository` repeats predecessor checkpoint branches. Their callers include startup/reopen, checkpoint recovery, deferred persistence and export. Preserve those supported compositions. `LegacyImmediateStatisticsPanel` is compiled beside the retained panel and has no construction caller; verify helper dependencies before deletion. Retained material/font construction still contains aesthetic equality rejection, requiring removal without weakening private-resource ownership. Build scripts currently lack host-path scanning and cross-root reproducibility checks. Native metric and runtime-cost inventories will be finalized from code and installed contracts.

Execution: persistence boundary and removal; impossible metrics and redundant/UI runtime code; measured-cost and artifact hardening; full automated qualification; independent review/corrections; exact candidate deployment and user-controlled qualification. Old captures remain immutable. No gameplay performance claim is established by synthetic tests.
