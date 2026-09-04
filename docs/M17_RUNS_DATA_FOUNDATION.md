# M17 Runs data foundation (schema 17)

Product version remains 0.17.0. This prerequisite provides data for the later retained Runs UI. It does not change Overview, retained tab visuals, mockups, or native UI construction.

## Installed native evidence

Inspected Duckov 2.3.30, Steam build 24013657. `TeamSoda.Duckov.Core.dll` SHA-256 is `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`. The public item/slot contracts are documented in [M14_NATIVE_CONTRACTS.md](M14_NATIVE_CONTRACTS.md) and checked by the installed contract probe.

The installed `LevelManager.OnMainCharacterDie(DamageInfo)` sets its death guard, starts `CharacterDieTask`, then raises `LevelManager.OnMainCharacterDead`. `CharacterDieTask` calls `RaidUtilities.NotifyDead()` synchronously on raid maps. `NotifyDead` updates the raid outcome and raises `OnRaidEnd` followed by `OnRaidDead`, before returning to the death task. Only afterward does the task record/drop/destroy equipment, save the native character, and reach its first await (`UniTask.WaitForSeconds`). The installed `AsyncUniTaskVoidMethodBuilder.Start` immediately invokes `MoveNext`; starting this task does not defer its initial body.

Consequently `OnMainCharacterDead` is too late for pre-teardown equipment capture. UDS captures at its existing `OnRaidDead` subscription. `OnRaidEnd` only marks pending death; it does not capture. `OnMainCharacterDead` retains its deduplicated player-death observer. Completion remains deferred until the next update so the fatal `Health.Hurt` post-call can publish damage/death attribution before aggregation. The earlier equipment capture and later completion are separate boundaries.

For extraction, `LevelManager.NotifyEvacuated` sets invincibility, raises `OnEvacuated`, then saves the character and native save data. UDS captures inside the accepted `OnEvacuated` callback before applying its terminal transition.

No new equipment or combat hook is added. Capture reads the current main character and character item afresh through `NativeEquipmentAdapter` and `NativeEquipmentSnapshotBuilder`. It does not use `CaptureAssociation`, the periodic cache, duration aggregates, later inventory, or transition hashes. Capture adds no per-frame work, checkpoint serialization, or disk I/O to combat/equipment callbacks. Existing terminal durability work remains on its established path.

## Terminal equipment contract

`RunSummary.TerminalLoadout` and `ActiveRunCheckpoint.TerminalLoadout` retain an independent detached candidate, so suspending ordinary equipment duration statistics does not erase it. Native character-slot keys, display names, occupied root item IDs/names, proven-empty slots, and ordered nested slot paths use the existing M14 identities. Unknown/modded identities are retained without guessing categories. Ordinary inventory is not substituted for equipped roots.

States are `Complete`, `Partial`, `Unavailable`, and `HistoricalUnavailable`. Complete requires complete root and nested evidence. Partial retains individually readable entries, including proven-empty slots, while unreadable/duplicate/bounded evidence prevents an exact whole-loadout claim. Missing evidence is never an empty loadout. Root/nested completeness and provenance accompany exports and the projection.

The first accepted candidate is deep-copied into active state. Repeated notifications and economy/checkpoint retries reuse that candidate, including a failed capture recorded as unavailable. No later teardown state can overwrite it. Capture errors produce diagnostics and do not block an otherwise valid run. Candidate capture does not publish equipment duration transitions or extend intervals.

The economy pre-terminal observer remains a distinct retryable durability barrier. Once economy is accepted, the refreshed active checkpoint includes the candidate and pending outcome before completed-run persistence. Recovery with a checkpointed pending extracted/dead outcome retains that exact evidence; ordinary interruption recovery discards any terminal candidate. An interrupted run reports unavailable even if equipment was previously observed. A process failure before the deferred terminal checkpoint becomes durable cannot promise terminal outcome or loadout recovery from an older checkpoint.

## Player-kill partition

Every accepted `CombatRecorded.KillsByYou` contributes exactly once according to that same event's `CombatAttackKind`: `Ranged`, `Melee`, `Effect`, `Environmental`, or `Unknown`. Other enum values fail into unknown classification rather than being guessed. This extends the existing proven player-owned fatal-health-transition path; it introduces no second kill detector.

`PlayerKills` accompanies combat totals and each enemy, killer, family, cause, weapon, ammunition, ownership, and equipment-combat association row. It propagates through run/segment checkpoints, completed runs, save-generation lifetime totals, starting maps, and route maps. Buckets plus `HistoricalUnclassified` reconcile exactly to each containing `KillsByYou`. Bucket validation does not assert that kills are bounded by projectiles, hits, swings, or headshots; penetrating projectiles, separate victims, and delayed effects retain their independent event semantics.

`HistoricalIncomplete` distinguishes missing old classification from a newly observed `Unknown` attack. `ClassificationComplete` requires no historical gap and no unknown attacks. Ranged/melee values are presentable as exact only when classification is complete and the existing `KillsByYou` capability is Supported. Known retained counts are still exported when incomplete, with provenance and availability. Other combat availability semantics remain unchanged. Nonfatal and non-player outcomes add no player kills. Effects are not reclassified as ranged/melee from source weapons.

Player-kill arithmetic uses checked addition and preflights affected scopes before mutation. An overflowing player-kill event or merge is rejected rather than saturating a total independently of its partition. Other established counter semantics remain unchanged. Current-schema profile and checkpoint selection validates partition reconciliation before normalization or choosing an unsafe primary over a valid backup/temporary candidate.

## Historical continuity and consumer contracts

Schema 16 and earlier migrate through the existing pre-1.0 migrations, then into schema 17. Existing proven player kills become `HistoricalUnclassified`; their attack kind is never inferred from weapons, ammunition, headshots, firing, or aggregate subtraction. Every historical scope is marked incomplete, even if its retained count is zero. Existing durations, transitions, identities, records, combat totals, and prior availability remain intact. Every old run has `HistoricalUnavailable` terminal equipment. None can be backfilled.

Old-plus-new lifetime/map scopes retain their historical qualification. A newly completed run can independently have exact equipment and classification. Older migrations remain as M17 development continuity; their removal is M18 work, per AGENTS.md.

`RunPresentationRow.Data` / `RunDataProjection` expose detached terminal root/nested rows, state/completeness/provenance, all kill buckets, total kills, exactness, and existing combat capabilities. No Unity object or layout policy is constructed.

JSON includes terminal candidates and partitions. `runs.csv`, `run_totals.csv`, `map_totals.csv`, `route_map_totals.csv`, `segments.csv`, `combat_attribution.csv`, and `equipment_combat.csv` carry the kill partition and classification metadata beside the existing total/capability columns. The existing `combat_totals.csv` is a firing-action export and remains unchanged. `terminal_loadouts.csv` emits a state row when no root evidence exists, or root and nested rows with terminal state, provenance, and completeness. An empty-slot row is positive native evidence; absence of rows is not.

## User-controlled qualification

Automated tests establish callback composition, retry/recovery, migration, overflow, and export contracts. They do not establish live gameplay acceptance. On the exact packaged candidate, the user must complete:

1. One new extracted run: note equipped root items, nested attachments, and empty slots immediately before extraction; include a late loadout change. Verify persisted terminal equipment and `terminal_loadouts.csv` match the extraction boundary, with no reconstruction from subsequent base inventory.
2. One new died run: note the equipped tree before a fatal hit. Verify the died summary retains that pre-teardown loadout even when native death removes items; confirm the fatal received damage and player death are included exactly once.
3. In those runs, record known ranged and melee final blows and, when safely reproducible, delayed/effect damage. Compare total kills and same-event buckets in run, lifetime, map, segment, equipment association, JSON, and CSV. Unknown evidence must remain qualified.
4. Restart and reopen the profile/export. Confirm no duplicate runs or durations. If an older development profile is voluntarily used, confirm historical runs remain unavailable and only new runs can show exact terminal evidence. No native save edits or fabricated runtime history are required.

Deployment, game launch/control, native save access, and gameplay are not performed by the data-foundation implementation task.

## Automated qualification (2026-09-05)

The foundation-specific suite passes 28 tests. Complete Debug and Release suites each pass 1,227 tests with no failures or skips. The installed Duckov 2.3.30 contract probe passes; the native adapter, Core, and FrameTimeAnalyzer Release builds complete with zero warnings/errors. Scoped `dotnet format --verify-no-changes`, `git diff --check`, source-binary safety, and the normal five-file package/ZIP verification gates pass. These results qualify the data foundation and local package; the gameplay protocol above remains user-controlled and unqualified.

The subsequent JSON export correction adds the omitted terminal-loadout deep copy to `StatisticsExporter.CloneRun`. Four regression cases complete extracted/died runs with complete/partial native equipment, deserialize the exported JSON, compare all terminal evidence, and verify that mutations to the export document leave the retained root/nested tree intact. All four reproduced the erroneous `Unavailable` export before the correction. After the correction, the foundation suite passes 32 tests and the full Debug and Release suites each pass 1,231 tests with no failures or skips. The native contract, Release builds, scoped formatting, source-binary safety, and package/ZIP checks also pass.
