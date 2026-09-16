# Encounter history production integration

The ordinary mod now captures encounter history automatically and exposes **Details | Map & Kills** in Runs. No encounter build flag or recording hotkey is needed. This is unreleased source work; the published GitHub 1.0.0 archive is unchanged. The user's accepted design includes overlapping markers when encounters occur at the same position. Do not spread or cluster them artificially.

## Runtime and diagnostic separation

`Encounters/EncounterCaptureHost.cs` owns native combat, corpse/loot and route observers, the bounded detached observation pipeline, incremental publication and run/profile barriers. Native callbacks and their trusted Harmony patch sets are identical between normal and diagnostic builds. Existing fail-closed coverage, publication retries and independent aggregate tracking remain in place. Shared health/corpse callbacks keep their existing owners; encounter-only observers use dedicated encounter owners.

The retained Runs view, map geometry/artwork and presentation code are ordinary code under `Encounters/` and `UI/`. Map artwork remains in the existing `UltimateDuckovStatistics/encounter-prototype/maps/maps-cache` directory; the historical directory name is retained deliberately so previously recorded maps remain available after restart. There is no data migration, profile reset or map-cache copy in this integration.

Raw JSONL recording, its F6 control, the F5 saved-capture viewer, replay-file reader and diagnostic overlay are under `Encounters/Diagnostics/` and compile only with `UDSEncounterDiagnostics=true`. The normal build does not reserve F5/F6. Performance diagnostics remain independently controlled by `UDSPerformanceDiagnostics`. The ordinary package audit rejects both the obsolete prototype namespace and the optional encounter diagnostics namespace.

Use `scripts/build.ps1` and `scripts/package.ps1` for ordinary builds/packages. `scripts/build-encounter-diagnostics.ps1` creates a separate, clearly labeled diagnostic package with both diagnostic flags enabled; it must not be published as a release. All packages retain the existing 13-file inventory and do not redistribute game assemblies.

## Validation boundary

Source-linked tests exercise the real capture reducer, storage/export/restore, map/feed projection, coverage handling and aggregate adapter callback composition. The aggregate adapter tests substitute a native encounter observer boundary; shell tests also stub the Runs child view. Neither suite simulates Unity rendering or proves native actor/loot discovery. The native build, installed contract probe and exact package audit complement those tests.

After deployment, verify the ordinary build using existing history and a short new raid: automatic capture without F6, existing map loading after restart, linked map/feed selection, retained loot tooltips, language switching and normal frame pacing with the panel closed/open. New encounter records must survive restart and appear in the JSON export. Broader gameplay/performance, native incomplete-recording layout and independent review remain release qualification; do not fault-inject a user's profile to exercise them.

## September 15, 2026 integration checks

The final main suites passed 2,390 tests each in Debug and Release. Ordinary shell suites passed 98 each, including assigning F5/F6 as normal panel shortcuts; the combined diagnostic shell passed 100. One initial Debug allocation assertion failed, then passed in isolation and in the complete final Debug run without a production or test-policy change. The installed Duckov contract probe passed. Ordinary Debug/Release, encounter-only diagnostic and combined diagnostic native builds passed with zero warnings/errors. The ordinary package audit verified capture startup/update and Runs rendering call sites, required encounter types, the absence of diagnostic types/calls, and all 13 package files. A real encounter-only diagnostic assembly was correctly rejected by the ordinary audit.

The ordinary package is `artifacts/encounter-history/production/20260915T2141082095881Z-0b898cafa37f459ab4eb49cd2b9b543e/`. It was deployed with Duckov closed, a verified prior-mod backup retained, and all 13 installed hashes read back. Native DLL SHA-256: `a693b16aa1446cc3ab3d5b5a95347e7efa9d61bbbfd6492720e2c7d1d7b00d14`. Source/package inventory, deployment and validation receipts accompany it. The package comes from the uncommitted encounter branch work and is not a published release. The user subsequently reported that the ordinary build looked good. This is positive smoke-test feedback; independent review and the broader release qualification listed above remain pending.

## September 16, 2026 review corrections

The independent review identified three reachable defects. The corrections are:

- Effect observations now carry the shared combat adapter's resolved ownership into encounter capture. Mixed contributors and unavailable ownership cannot promote a retained native buff actor/weapon into player damage or kill credit. Incoming damage remains recorded with unknown attribution. Unresolved source positions are not presented as proven attacker locations.
- Pause/loading, temporarily missing characters and character replacement retain the current map visit and resume through a route gap. A new visit requires a changed visit identity. A combat observation arriving before the map observer's first update also shares that visit rather than creating a duplicate selector entry.
- Focused encounter maps project player/enemy endpoints independently. A known endpoint remains visible and receives automatic focus when its counterpart is absent or on another map. The connector and distance require both endpoints; closing selection restores the overview.

Regression coverage exercises the real shared effect callback with same/mixed actors, the encounter reducer's damage/fatal credit, native map-observer Tick through pause/loading/character boundaries and the real reducer, and the focused-map projection used by the retained view. Native actor discovery, map-art rendering and Unity pointer/display behavior remain separate from the stubbed native boundaries used in tests. Existing recorded runs are not rewritten to reconstruct evidence that was not captured.

After these corrections, all 2,412 main tests and 98 ordinary shell tests passed in both Debug and Release, including 22 added regression cases. The installed native contract probe, ordinary Release assembly/package audits and 13-file package verification passed. Ordinary Debug/Release and combined encounter/performance diagnostic Debug builds completed with zero warnings/errors. The build log is retained locally at `artifacts/encounter-review-fixes-build.log`; the source inventory is under `artifacts/encounter-review-fixes/`. These checks do not establish in-game acceptance of the corrected build or an independent re-review.
