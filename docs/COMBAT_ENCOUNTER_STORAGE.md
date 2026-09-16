# Incremental encounter storage

This is the storage layer of [Map & Kills](COMBAT_ENCOUNTER_HISTORY.md). Ordinary builds connect live native observations to this repository API and the retained Runs view, with automatic capture for active runs. The [production integration record](COMBAT_ENCOUNTER_PRODUCTION.md) separates the optional diagnostic tools and remaining gameplay/frame-time qualification. Targeted earlier native passes are documented below.

## Owned records

`ProfileDocument.EncounterHistory` is an optional collection, separate from `RunSummary`, lifetime aggregates and the active-run checkpoint. Each version-1 `EncounterRecord` has a run ID, stable record ID, discriminant and exactly one typed body. Visit-owned records identify their visit; encounter children also identify their encounter. Run-level coverage records have an empty visit ID. It is not arbitrary JSON or a serialized Unity object.

Seven families use the existing compressed SQLite `records` table, with kind 33 and the unique key `(run ID, family, record ID)`:

| Family | Contents | Update scope |
| --- | --- | --- |
| Visit | Map/segment identity, visit ordinal and times, gap flag, individual/combined map calibration and artwork content key | One visit |
| Route | Chunk index, sealing state, at most 256 timestamped raw positions and connection kinds | Current tail; sealed chunks are immutable |
| Encounter | Actor/preset identity, outcome/time, available player/enemy/source positions and final-source attribution | One encounter |
| Damage | Incoming/outgoing direction, source, damage, hits, headshots and gap state | One source aggregate within an encounter |
| Inventory | Observed corpse identity/time, coverage and at most 512 inspected/hidden slot states | One explicitly addressed observation |
| Loot | Corpse/item identity, separate gross player/pet take/return counters and gap state | One item/corpse total |
| Coverage | Capture issue, observation time and whether recording stopped | Immutable notice, once per issue/run/capture session; no invented visit |

`EncounterRouteRecorder` keeps one mutable tail. It publishes before advancing to the next chunk, retains that tail when publication fails, and does not rewrite older chunks. Connections distinguish start, walking, a witnessed teleport and a recording gap. Chunk boundaries do not imply a teleport or gap. Publication owns detached data; storage receipt handling retains pending records until their matching commit succeeds.

Capture and storage failures have separate lifetimes. Queue overflow stops new capture but drains accepted observations. A reducer failure consumes the failed task once, retains successfully completed batches and previously published records, and discards only the uncommitted tail that cannot safely resume. Both record durable incomplete coverage. Native probe degradation records family-specific coverage while independent producers continue; an unexpected host capture failure stops capture for that host. A stopped host marks subsequent runs incomplete until restart. Optional diagnostic-file/viewer failures do not by themselves stop automatic capture.

Publication rejection or an exception retains the exact pending record and original generation, with a 500 ms retry interval. Lifecycle/profile barriers release after valid records and coverage have been accepted; a sticky capture error alone cannot block them forever. A genuine publication/storage outage still keeps the barrier pending. Coverage survives SQLite reopen, export and restore and appears once above the encounter list, including when failure preceded the first visit. It does not change aggregate run metrics or assert that every field was missing.

Raw native float coordinates, including height, round-trip without quantization. Route simplification belongs to presentation. Endpoint map identities can remain unknown or differ; presentation must not pretend a remote/unknown endpoint belongs on the selected map. Combined-map centers/size, offsets and hidden/no-signal flags remain available to reproduce the native projection. Artwork keys are content hashes, never absolute paths; images remain shared presentation assets rather than profile/export payloads.

The live bridge supplies the run tracker's relative active time. A session UUID and run epoch prefix native actor/corpse/observation IDs so restarted counters cannot collide. The original diagnostic stopwatch values remain confined to diagnostic captures and replay fixtures.

Validation rejects mismatched bodies/addresses, missing parents, invalid coordinates, hidden-item disclosure, duplicate observation slots, oversized chunks/snapshots and negative counters. An accepted route prefix cannot change; sealed chunks cannot grow or become mutable again. Actor/visit/corpse ownership and recorded fatal outcomes cannot change under the same record ID. Gross damage/loot counters cannot move backwards. Unknown weapon/ammunition/source identities remain nullable; net loot can be negative and does not claim unique original items or current possession.

## Persistence and reads

`ProfileRepository.RecordEncounterDeferred` validates and encodes only the supplied record, marks its exact journal address and advances the profile revision. Existing ordered transactions, checksums, lossless record compression and commit receipts apply. Failed writes retain their dirty records; acknowledging an older command cannot discard a newer update to the same address. On a suspended profile transition, the retained history reconnects to the reopened storage source.

SQLite opening reads no encounter payloads. The collection fetches requested run/family records on demand, retaining unacknowledged records and a bounded 128-record recent cache. A malformed selected record is a read failure rather than an empty result. Import/recovery validation can inspect the complete collection; routine save validation reads only changed records and their specific parents.

Internal SQLite format **8** adds this record vocabulary. Opening the actual preceding format 7 advances the format marker transactionally without rewriting payloads, generating encounter backfill or running VACUUM. The existing format-6 compression conversion remains supported. The public profile/export schema remains 1, with an optional encounter collection; a profile with no encounter history does not gain invented empty encounters. Automated tests use disposable profiles. The development build advances the installed database format when the user next opens a profile; it does not backfill previous runs.

## Export and restore

The existing detached SQLite export boundary includes encounter history. The ZIP still contains exactly one `statistics.json`. Completed-run encounter records stream through that snapshot; subsequent gameplay writes and closing the source profile cannot alter the export. Active-run encounter records remain in SQLite and are omitted from the statistics export, matching its omission of the active-run checkpoint.

Restore validates record bodies, duplicate addresses, visit/encounter ownership and references to exported completed runs before accepting the detached preview. Confirmation copies the validated history into the replacement generation and archives the prior generation through the existing restore transaction. Run/actor IDs stay intact within the new profile owner. A post-promotion reopen failure can retry activation without losing restored history or subsequent accepted updates. JSON/ZIP exports without encounter history remain usable and produce no backfilled records.

## Automated qualification

`EncounterPersistenceTests` exercises the production repository, native JSON writer, portable reader and SQLite backend. Its cases cover exact record round-trips, raw float bits, nullable attribution, inspection privacy, gross take/return semantics, sealed route bounds, retry ownership, changed-record isolation, lazy reads, detached ZIP export/restore, malformed restore rejection and post-promotion activation retry.

The isolation case creates 1,200 encounter records across 200 synthetic run IDs. A SQLite update trigger observes a later loot mutation and requires exactly that loot row plus profile metadata to change. This is evidence of changed-record isolation, not a Unity callback/frame-time measurement or a representative long-raid storage benchmark.

On September 15, 2026, Release verification passed 2,340 main tests and 91 shell tests, including 23 encounter-storage cases. The native prototype build passed with zero warnings/errors. The ordinary build's exclusion audit also passed. These are automated storage/build results, separate from native gameplay acceptance.

The following integrated stage connects these observations through typed reduction and lifecycle barriers to the retained **Details | Map & Kills** surfaces. Native capture timing, no-art maps, broader enemy/source/loot cases and long-run performance still require their own qualification.

## Live capture and retained presentation

The native hooks enqueue detached observations. A single worker reduces them into typed records in batches; the main thread accepts only those changed records into the existing ordered journal. Routine batches run every two seconds. Terminal, native-save, export and profile-transition barriers drain accepted observations before the corresponding durability boundary. Worker results retain their generation identity and rejected publications remain queued.

Each inventory opening stores its initial observed top-level slots. Later inspection may reveal the same native item in that opening; a different item occupying its old slot cannot reveal it. Content changes from taking or returning items do not rewrite the initial quantities. The UI shows the largest quantity observed at one opening and keeps gross taken/returned counts separate. This avoids counting an original stack plus a returned unit as a larger original inventory.

Selected-run history reads use the SQLite run index on a worker. Read-only transactions pin the selected generation while it is read; closing or replacing the UI owner discards late results. Fatal outcomes retain their own map-visit ID if the actor was first encountered elsewhere. A player's death from an already-dead shooter's projectile remains a separate event.

The map and route textures belong to the retained view and are released on disposal. Route simplification happens only in the display model, preserving gaps and teleport boundaries. Rasterization runs on a worker; texture uploads stay on Unity's main thread. The five-second reveal is quantized to at most 20 requests per second, with one in-flight raster job. Markers follow displayed progress. The list reuses visible row controls. Missing cached artwork preserves the full encounter list and uses the themed satellite fallback.

`EncounterLiveCaptureTests` uses compact, anonymized excerpts of the September 15 native captures to verify the bridge at different flush cadences, SQLite reopening, both witnessed map teleports, inspection privacy, and take/return/reopen totals. These checks do not establish visual or frame-time acceptance of the new retained surface.

The first integrated development build passed 2,349 main Release tests and 91 ordinary-shell Release tests on September 15, 2026, including nine live-evidence integration cases. Its native build passed with zero warnings/errors, the installed Duckov contract check passed, and the 13-file package/path audit and ordinary-build exclusion audit passed. The user subsequently reported that overall functionality worked well on the short single-map pass recorded in the [prototype qualification](COMBAT_ENCOUNTER_PROTOTYPE.md). The shell test suite exercises the ordinary shell, not a simulated native encounter renderer.

That pass's 38 encounter records match the closed SQLite database and the exported JSON exactly, including 829 route points, two teleports, five player kills and one other death. Compressed encounter payloads occupy 25,177 bytes, excluding SQLite pages/indexes and ordinary aggregate records. The complete three-run statistics export is 76,974 bytes zipped and 632,765 bytes uncompressed. This is one short-run sample, not a long-run scaling benchmark.

The audit exposed an encounter-only headshot error: zero were recorded where the existing aggregate correctly recorded nine. The correction forwards the aggregate adapter's deduplicated, launch-time head-target classification into the still-owned native Hurt frame before the encounter completion callback. It does not reinterpret native critical hits as headshots or alter old runs. The correction and elapsed-hour formatting passed 2,354 main Release tests, 91 ordinary-shell tests and a native package build with zero warnings/errors. The subsequent four-visit native pass recorded 29 headshots across 33 hits, matching the ordinary aggregate and nine exact user notes; the user's uncertain eighth count was recorded as 3/4 rather than the recalled 2/3.

The four-visit pass retained 45 records and 1,683 route points. Its closed SQLite encounter records exactly match the export; their compressed payloads total 43,744 bytes, excluding database overhead and other statistics. Visit identities and fatal-visit references already distinguish both Warehouse Area visits, so separating those selections requires no data rewrite. The updated presentation keeps a single chronological run feed with global numbering, selects each visit independently, and routes list/marker selections through that exact fatal visit. It labels effect damage explicitly instead of presenting missing ammunition and a damage event as a bullet hit. The new native-data fixture and presentation checks passed with the full 2,361-test main Release suite, 91 shell tests and a zero-warning native package build. The user subsequently accepted this presentation revision, then reported severe main-menu slowdown with UDS closed. The observer lookup correction and its accepted closed-panel menu measurement (34.08 ms down to 0.052 ms mean UDS Update cost) are recorded in the prototype qualification; this does not implicate SQLite writes, which were absent during the sampled idle interval.
