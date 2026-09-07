# M18 acceptance record

M18 is release hardening of the completed M17 feature baseline. Starting source: `443fad6a268916a6038c919501a714d1f17da371`. Candidate source and artifact identities will be recorded after qualification. Publication is user-controlled.

| Requirement | Implementation / evidence | Status |
| --- | --- | --- |
| Baseline | Clean starting checkout matched the verified source commit; [authoritative repository](https://github.com/bamboechop/ultimate-duckov-statistics) | Pass |
| Native baseline | Installed probe: Duckov 2.3.30, Steam 24013657, Unity 2022.3.62f2, Harmony 2.4.1.0 | Pass |
| Current v1 persistence only | Implemented `uds-profile-v1` in the separate `v1` data namespace; predecessor readers/fields removed through `b4011a0`. Current validation, replay cursor, partial evidence and recovery remain. Final qualification pending | Not exercised |
| Native adapter and metric removal | Installed-contract audit and [removal inventory](M18_REMOVAL_INVENTORY.md); impossible firing/tote/crafting/Cash surfaces removed, supported siblings retained | Not exercised |
| UI runtime cleanup | `e103d9c` deletes the unused immediate panel and appearance rejection; material ownership regressions pass. Shell access/overflow and native qualification remain | Not exercised |
| Performance | Existing M8.1 method, 5% median / 10% p99 engineering targets and all spike/ceiling rules; final matrix must precede captures | Not exercised |
| Build privacy and reproducibility | Normalized source paths; PE/PDB/package scans; two-checkout artifact comparison | Not exercised |
| Automated qualification | Debug/Release, formatting/analyzers, native probe/build, source/package audits, isolated recovery/composition fixtures | Not exercised |
| Independent review | Fresh bounded whole-system reviews, then immutable full-range integration review and correction re-review | Not exercised |
| Final package and deployment | Independent ZIP extraction, exact inventory and hashes, UDS-only reversible deployment with Duckov closed | Not exercised |
| User qualification | Clean/current-format install, fresh/progressed saves, access/localization, degradation, interruption/recovery, multi-map/high-history gameplay, export agreement, shutdown | Not exercised |

Implementation and evidence are tracked in [the removal inventory](M18_REMOVAL_INVENTORY.md). `c24275da06f81e956aa20cf75acc529607552936` fixes terminal-checkpoint retry frequency and freezes pending run attribution; isolated lifecycle/coordinator/economy tests and independent correction review passed. `9811d2c` passed 1,845 Release tests after Cash cleanup. Subsequent current-format removal slices passed their relevant suites; complete final Debug/Release qualification remains outstanding.

Independent review of `d9a90547fec74fdf5b596db0d50a82567c204c81` covered item use/healing, firing/combat/grenades/throwables, equipment, containers and integrity. It confirmed a P2 delayed-effect caller defect after a loadout swap; correction and re-review are required before acceptance. A separate whole-system persistence review is also checking transition retry cadence. These bounded reviews do not certify the final complete M18 range.

Execution: persistence boundary and removal; impossible metrics and redundant/UI runtime code; measured-cost and artifact hardening; full automated qualification; independent review/corrections; exact candidate deployment and user-controlled qualification. Old captures remain immutable. No gameplay performance claim is established by synthetic tests.
