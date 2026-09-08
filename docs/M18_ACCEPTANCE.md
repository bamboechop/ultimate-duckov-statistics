# M18 acceptance record

M18 hardens the completed M17 feature baseline. Starting source: `443fad6a268916a6038c919501a714d1f17da371`. Publication is user-controlled. **Native qualification remains open; M18 is not complete and the candidate is not declared production-ready.** Live delivery state belongs on [GitHub](https://github.com/bamboechop/ultimate-duckov-statistics).

| Requirement | Implementation and evidence | Status |
| --- | --- | --- |
| Frozen source/native baseline | Clean starting checkout; installed Duckov 2.3.30 / Steam 24013657 / Unity 2022.3.62f2 / Harmony 2.4.1.0 probe | Pass |
| Current v1 persistence | `uds-profile-v1`, separate `v1` namespace, predecessor readers/fields removed; current semantic validation, atomic recovery, generation/reset/export and pending retry retained | Pass (automated composition); native requalification Not exercised |
| Supported native metrics and removal | [Removal inventory](M18_REMOVAL_INVENTORY.md); impossible firing/tote/crafting/Cash surfaces and exclusive consumers removed | Pass (source/native contract review) |
| UI runtime cleanup | Immediate renderer, appearance rejection, unused formatters/production fixture constants removed; hidden Diagnostics measurement fixed. Five real-panel/shell access and ownership regressions through isolated boundaries | Pass (isolated); native rendering/resources Not exercised |
| Runtime cost/durability | Terminal attribution freeze and bounded terminal/transition retries; live integrity enumeration avoids a per-event list; 144,000 mixed operations with 0/200 prior runs, exact six-segment/three-map persistence/reopen | Pass (managed); native performance Not exercised |
| Performance protocol | [Frozen 14-cell matrix](M18_CAPTURE_MATRIX.json), three B/D repetitions, existing engineering/spike/ceiling rules; campaign source/DLL/raw-data checks | Pass (prepared/tested); native captures Not exercised |
| Artifact privacy/reproducibility | Normalized PE/PDB identities, canonical LF source text, UTF-8/UTF-16/package audits and ordinary IL diagnostic-call rejection. Both isolated roots and the Windows checkout produce identical DLL/PDB/ZIP hashes at the source below | Pass |
| Complete automated qualification | 1,856 main + 8 shell tests in **each** Debug and Release; zero failures/skips. Final changed-source formatting/analyzers, installed probe, zero-warning native Debug/Release builds and source safety | Pass |
| Independent whole-system review | Native/UI, persistence/economy/world-time/crafting and delivery reviews; correction re-reviews below; final complete immutable range `443fad6..4068480` | Pass; no confirmed P1/P2 |
| Package/deployment | 622,371-byte deterministic five-file ZIP; independent extraction; exact installed-file readback while Duckov closed; verified 0.17.0 rollback copy; no deployment residue | Pass |
| Native user gates | [Manual sequence](M18_MANUAL_QUALIFICATION.md): fresh/current-format reinstallation, progressed/second save, gameplay, degradation, recovery, UI/export agreement, performance and clean shutdown | Not exercised |

## Confirmed corrections and independent re-review

- `c24275da06f81e956aa20cf75acc529607552936`: terminal checkpoint failure freezes original raid attribution and retries with bounded backoff; actual lifecycle/coordinator/economy regression and independent correction review passed.
- `41f16874e9c5e0eea94dc245f5a398f90cd0ca51`: delayed effects cannot capture a later loadout as their origin. Missing origin remains unknown, with independently proven outcome retained. Correction review passed.
- `34f1b89` and `a419c756946c6727ab7801c4408b0637623563df`: profile-transition retry/backoff and shared injected clock; exact step/generation and queued-flow tests and correction review passed.
- `6f352290c5f25a2e18b927447dd2fd5aff282bb1`: staging/backup folders moved outside `Mods` after installed loader review showed dot-prefixed folders are still discoverable. Denied-cleanup regression and delivery re-review passed. The same commit passed byte-identical two-root DLL/PDB/ZIP comparison.
- `eaf083bedff494d6800b142ff39f6eee6361875f`: real-shell overflow/access, 25 open/close cycles and 10,000 closed ticks through isolated boundaries passed.
- `9a99b02d266efb39b81b9a24b8094b89584ad58c`: live integrity optimization, exact native dictionary contract and 144,000-event repository/tracker workload. Independent complete correction-range review passed with no confirmed P1/P2. Managed timing must be collected alone.

These reviews inspected production/native composition, including unchanged supported code. The final integration, current-data/runtime and delivery reviewers independently passed the complete or assigned bounded immutable range ending at `4068480cd736642a16e14b0510384c957b3b08e0`. Native user qualification remains separate.

## Final identities and outstanding gates

Qualified binary/package source: [`4068480cd736642a16e14b0510384c957b3b08e0`](https://github.com/bamboechop/ultimate-duckov-statistics/commit/4068480cd736642a16e14b0510384c957b3b08e0), version `1.0.0-rc.1`. Later evidence/documentation commits do not change this binary identity. Exact native hashes, all five package hashes, portable PDB hashes, two-root proof, raw managed timing windows, review scope and local evidence checksums are in [the automated evidence manifest](M18_AUTOMATED_EVIDENCE.json).

| Artifact | SHA-256 |
| --- | --- |
| `UltimateDuckovStatistics-v1.0.0-rc.1.zip` (622,371 bytes) | `aed9c92fd7ffd854671a9ecc98769e0a4451481b8181784f82b28752b8d086e4` |
| `UltimateDuckovStatistics.dll` | `5c61dd42dc9408a3f684d385c1ce34d721f372394f60f4b2133ee7babcd94d3a` |
| `UltimateDuckovStatistics.Core.dll` | `1b8c95ad4489d0615863d1387fe6737c4ad6989410bc9fd36480c916b4c0b073` |

Local candidate: `artifacts/release/UltimateDuckovStatistics-v1.0.0-rc.1.zip`. Frozen campaign: `artifacts/qualification/4068480cd736642a16e14b0510384c957b3b08e0/campaign.json`, SHA-256 `b16dab9d05af892b19909b218cd3190143349877f8e00ded951968f387a3303a`. Its adjacent `controls.json` is a blank copy to fill from the actual user-selected setup. Verified rollback package: `artifacts/deployment-backups/7e680a87e7a94cf59d5b2c9aa78a3a9c/UltimateDuckovStatistics`. No real UDS profile or Duckov save was changed.

The final isolated .NET 8 workload retained exact totals after **144,000 operations per case**, six segments and three maps, with zero and 200 prior runs. Median processing per 6,000 operations was 50.003 / 51.049 ms; the early/middle/late medians were 50.556/50.880/49.323 ms and 130.932/49.409/49.495 ms. These runs show no progressive processing slowdown within this workload. Each synthetic operation allocated approximately 12,752 bytes, including event construction and managed fan-out. Whole-test-process retained heap changed from 21,851,336 to 22,228,720 bytes and from 22,908,136 to 23,727,472 bytes. These are diagnostic observations, not native frame-time or memory acceptance.

Checkpoint plus complete-profile snapshot writes were measured separately: median 16.497 / 113.981 ms, maximum **326.330 / 213.901 ms**, at final profile sizes 25,092 / 3,155,379 bytes. These peaks are retained, not excluded. Serialization cost grows with retained history; the native persistence/high-history gates must still pass. No comparable native before/after measurement or gameplay overhead claim is available.

Next, follow [the first native check](M18_MANUAL_QUALIFICATION.md#first-native-check), then complete current-format/reinstallation, second-save, recovery/degradation, all matched performance cells and the natural high-history soak. Native rendering/resources, gameplay/export agreement, performance and clean shutdown remain **Not exercised**. The [release procedure](RELEASE_PROCESS.md), [prepared Workshop description](WORKSHOP_DESCRIPTION.md) and [local data guide](LOCAL_DATA.md) are ready. Workshop preview selection/subscription installation and merge/tag/release/upload authorization remain outstanding.

## Native qualification follow-up

On save 3, startup Diagnostics marked Menu access Limited solely because the base pause-menu entry was `NotObserved`. The presentation now reserves Limited for an observed unavailable entry. Unobserved and attached-but-unexercised rows retain their precise neutral status; the group remains Working, and the banner does not claim every entry was verified. The correction passed 50 focused Release Diagnostics tests, changed-file formatting/analyzers and a zero-warning installed-native Release build. The corrected five-file package is prepared from immutable source `da1746ba9ab94abb9fcdebb0e30488754ebe2227`, with matching ordinary-checkout and two-root DLL/PDB hashes and identical isolated ZIPs. [Correction evidence](M18_MENU_HEALTH_CORRECTION.json) records the package/campaign identities and test results, including two local deployment tests blocked/excluded because Duckov is running. Replacement deployment and native readback remain Not exercised; the artifact identities above still describe the previously deployed candidate. The replacement is under `artifacts/candidates/da1746ba9ab94abb9fcdebb0e30488754ebe2227`, with its campaign under the matching `artifacts/qualification` directory. Current correction delivery is tracked in [PR #19](https://github.com/bamboechop/ultimate-duckov-statistics/pull/19).

The same save's three Experimental Economy capabilities match the supported native contract: exact Money changes have only partial semantic source/context attribution, and proven external Cash acquisition does not cover corpse/container transfers or ambiguous player-originated split identities. Following the user's startup feedback, Diagnostics now treats this expected partial coverage as Working operational health. Capability state/provenance, unknown attribution, Economy data-coverage notices and exports are unchanged. Actual disabled/missing/conflicting capabilities still report Error, and unexpected Experimental capabilities remain Limited. This prevents documented measurement boundaries from becoming a permanent startup warning without hiding a loss of supported tracking. Replacement qualification and deployment for this follow-up remain pending.
