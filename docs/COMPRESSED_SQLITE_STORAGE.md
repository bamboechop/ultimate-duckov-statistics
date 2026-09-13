# Lossless SQLite record compression

Current source stores larger JSON records as individually compressed SQLite BLOBs using the framework's fast Deflate implementation. Typed crafting rows and the small history overview index retain their existing representation. Records below 256 bytes stay raw; other records use compression only if the eight-byte envelope plus compressed data saves more than 32 bytes. This requires no additional packaged library.

Compression belongs to the existing storage worker. A routine save still writes only changed records; it neither scans nor recompresses completed history. Completed-run detail is decompressed only when requested. Metadata, active checkpoints, aggregate entries, run corrections and detached exports all use the same decoder. Command receipts and record/history checksums cover the original uncompressed bytes. Numbers, precision, identities, ordering, partial/unknown states and all recorded detail are preserved. Exported JSON/CSV retains its existing format.

## Existing databases

The internal SQLite format advances from 6 to 7, independently of public `uds-profile-v1` schema 1 and the mod release number. Format 6 is the actual preceding SQLite implementation; no unsupported `0.x` migration machinery is added.

Before opening a format-6 generation, the storage worker checkpoints its WAL and establishes a DELETE rollback journal with EXTRA synchronization. It verifies each original record checksum and changes only payload bytes, preserving row IDs, logical hashes, generation, revision, receipts and indexes. All conversion writes and the new format number commit in one transaction. An interrupted or failed transaction retains the old representation and can be retried after reopening. The conversion retains one record at a time rather than materializing every run.

After conversion commits, a one-time VACUUM reclaims freed pages. This happens before the profile is available for gameplay, not during routine saves. If compaction cannot complete, SQLite retains a valid converted database with reusable free pages; UDS reports the failed reclamation. Routine operation then resumes WAL/FULL synchronization and existing separate passive maintenance. The first open can take longer and needs temporary disk space for journaling/compaction. Later opens do not repeat the conversion or full compaction.

Older SQLite builds cannot read format 7. Reverting just the mod files is insufficient after conversion; an intentional rollback requires a matching pre-conversion data copy, including its WAL. Deployment itself does not rewrite profiles. Original imported JSON, exports, archived generations and obsolete recovery files are not deleted by this change. The single-database policy remains in effect: WAL is transaction state, not an independent recovery database.

## Qualification

On 2026-09-13, the change passed 2,204 main tests and 90 shell tests in both Debug and Release, native builds with no warnings/errors, and the installed Duckov contract probe. Focused tests cover conversion rollback/reopen retry, damaged headers and lengths, changed-content checksum rejection, exact checkpoint/export reconstruction, and a database trigger that rejects any routine rewrite of completed history.

The prior uncompressed implementation and the compression candidate were run separately against disposable copies, using the pinned SQLite 3.53.4 library and game Newtonsoft.Json assembly on .NET 8.0.31:

| Profile | Primary before | Primary after conversion | Reduction |
| --- | ---: | ---: | ---: |
| Recorded three-run profile | 2,387,968 bytes | 962,560 bytes | 59.7% |
| Existing 1,000-run qualification profile | 482,627,584 bytes | 64,114,688 bytes | 86.7% |

These are primary-file measurements, not total directory sizes. WAL/SHM, exports and preserved older files are additional. After 60 base/crafting updates and a synthetic 4,000-entry active checkpoint with subsequent updates, retained WAL capacity was about 5.2 MB in both implementations; compression does not eliminate transaction-page bookkeeping or promise a proportional WAL reduction.

Both cases retained identical full-profile hashes before and after the workload, identical record keys/ordinals/logical checksums, and identical persisted checkpoints across reopening. The three-run profile's 37 export files matched byte-for-byte against the prior implementation both immediately after conversion and after the workload/reopen. The large checkpoint's producer and persisted dictionaries already differ in enumeration order in the baseline: that comparison sorts object keys while preserving raw number tokens; persisted old/new and reopen comparisons remain byte-exact.

Warm median capture allocations were unchanged. In the three-run case, complete base/crafting durability boundaries changed from 1.57/1.83 ms to 1.74/2.13 ms; active-checkpoint updates changed from 4.30 to 5.05 ms. Total managed allocations per update increased by roughly 34–65 KiB on the worker. The 1,000-run case showed similarly bounded updates without history rewrites. Initial conversion plus open took 0.37 seconds for the recorded profile and 3.82 seconds for the large profile; conversion did not repeat on reopen. These are desktop measurements, not Unity frame-time claims.

Source, logs and copied-profile measurements are retained under local `artifacts/storage-optimization/`. ZIP exports and any cleanup of old user-created exports or import files remain separate work.

## Native acceptance — 2026-09-13

The ordinary compression candidate was deployed with all 13 installed files hash-verified against the qualified package. The user exercised base movement, crafting and selling, exported before and after a full game restart, and reported smooth interaction with only tiny occasional spikes. The first startup and first panel open took longer. This ordinary build supplies no frame-time capture or native timing that assigns those delays; gameplay responsiveness is user acceptance, not a measured comparison against UDS disabled.

Both sessions opened the existing generation and closed cleanly. Both exports contain the expected 37 files. All three completed runs retain their exact original persisted bytes after decompression, and exported run totals, records, item use and healing evidence remain unchanged. The second export retains seven new crafting actions producing nine items, 4,014 Money from sales and 247.74451946123963 metres of additional base movement across the restart. Read-only integrity checks on disposable copies passed for both formats, and all 1,630 stored record checksums passed in the converted database. Its internal format is 7 and its session is closed.

The actual primary file fell from 2,387,968 to 937,984 bytes, a 60.7% reduction after this native workload. The retained WAL is 1,227,792 bytes and SHM is 32,768 bytes; these are additional to the primary. Existing JSON import files and user exports remain present. No new UDS warning/error diagnostics or storage/export failures were recorded. The first session also contains 13 unassigned `startIndex` range-error messages; the same message predates compression in the retained ordinary-build and single-database logs, and this check does not establish its source.

The acceptance report, export hashes, captured logs and disposable database copies are retained under `artifacts/storage-optimization/native-acceptance/`. This completes the scoped ordinary-build compression acceptance; native active-raid checkpoint timing was not measured in this base-only session.
