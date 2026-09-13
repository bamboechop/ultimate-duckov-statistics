# SQLite columns versus compressed records — 2026-09-13

Decision — 2026-09-13: the user rejected converting all data to dedicated columns and chose to retain the accepted compressed-record implementation in `eaeca35`. Replacing the remaining JSON payloads with dedicated columns did not produce a meaningful storage saving in this comparison. Sharing strings greatly improves the column representation, but it still exceeds the compressed format. Keeping completed runs compressed and converting only mutable records saves one 4 KiB page on the recorded profile and is slightly larger on the large fixture.

This is a disposable prototype comparison, not a production schema implementation or deployment. It makes no change to the mod or live profiles. A future direct column reader could perform differently from this prototype; the storage result alone does not establish that columns are always slower or unsuitable for new queryable features.

## Inputs and representations

The recorded input is the accepted three-run profile captured after the compression acceptance session, containing 1,630 JSON records plus the existing typed crafting data. The larger input is the existing 1,000-run qualification fixture after its subsequent base/crafting updates and synthetic checkpoint workload. It contains 18,774 JSON records, including the checkpoint initially populated with 4,000 distinct firing entries and then updated further. It is not 1,000 independently played raids: its creator repeats the largest recorded run with distinct run IDs and applies the normal reducers. This especially favours reuse of strings; it is a scaling fixture, not a prediction of every player's history.

Each case starts from a SQLite backup of the same committed input state, including WAL contents. Every representation is compacted with VACUUM before the primary size is measured, including the existing compressed baseline. The recorded baseline is therefore 925,696 bytes rather than the 937,984-byte uncompacted primary seen during native acceptance. The large baseline includes the later checkpoint workload, so it is not the earlier 64,114,688-byte post-conversion snapshot.

| Representation | Recorded three-run primary | Change | 1,000-run fixture primary | Change |
| --- | ---: | ---: | ---: | ---: |
| Current compressed records | 925,696 bytes | — | 66,662,400 bytes | — |
| Dedicated columns, ordinary text | 1,724,416 bytes | +86.28% | 292,909,056 bytes | +339.39% |
| Dedicated columns, shared string IDs | 1,019,904 bytes | +10.18% | 79,060,992 bytes | +18.60% |
| Mutable records in columns with shared strings; completed runs compressed | 921,600 bytes | −0.44% | 66,740,224 bytes | +0.12% |

Sizes include tables, indexes, schema pages and remaining free space after compaction. They exclude WAL, SHM, exports and the independent source fixtures. They are measured file sizes, not estimates from summing values.

The prototype uses fixed scalar columns derived from current CLR serialization contracts, with separate ordered child rows for collections. It is not a generic property-name/value table. Singular nested objects are flattened into their owner's columns. Integer values and booleans use INTEGER, ordinary floating values use REAL, and text uses TEXT or an integer reference into a shared string table with a uniqueness index. Dictionary keys also participate in string sharing. Composite `(record, node)` primary keys use WITHOUT ROWID. SQLite's [integer/REAL storage rules](https://www.sqlite.org/datatype3.html) and [WITHOUT ROWID guidance](https://www.sqlite.org/withoutrowid.html) informed these choices.

Decimals use their exact 16-byte .NET representation; storing them with SQLite NUMERIC affinity would not by itself guarantee the required decimal precision. Small presence bitmaps distinguish omitted, null and present fields, including explicitly empty collections. Child ordinals retain list and dictionary order. Negative floating zero is preserved separately. The final prototype reuses the existing record checksum and stores a compact binary table descriptor in the existing envelope; it does not retain the duplicate checksum/envelope table used in the first trial. Converted payloads contain no JSON. The existing typed crafting tables and small JSON history overview index remain common to the alternatives.

The full column conversions produce 7,015 child/root rows in 43 typed tables for the recorded input, and 1,620,356 rows in 44 typed tables for the larger input. Mutable-only conversion uses 3,410 rows in 39 tables and 19,557 rows in 42 tables, respectively. Table identities, scalar fields and types are listed in each local `schema-map.json`.

Compression already removes much of the repeated JSON field names, common text and repeated structure within each record. Columns remove those field names but introduce row headers, relationship keys, order and presence information. Even with shared string IDs, the large fixture's equipment-duration, combat-breakdown, character-slot, equipment-transition and equipped-item tables occupy about 54.7 MB together. Page attribution comes from SQLite's [dbstat virtual table](https://www.sqlite.org/dbstat.html), with per-table/index results retained locally.

## Read, export and write measurements

These are serial desktop measurements using .NET 8.0.31, the shipped SQLite 3.53.4 library and the game's Newtonsoft.Json assembly. They are not Unity frame-time measurements, a UDS-disabled comparison, or complete production repository save/load boundaries.

The column reader reconstructs JSON and passes it through existing UDS domain reconstruction and export code. That preserves a common comparison path but adds compatibility work that a purpose-built typed reader could eliminate. Opening reconstructs the mutable profile and active checkpoint and loads history overviews; it does not decode every completed run. Reader construction and contract setup are included. Detail reads load one complete run. The prototype uses prepared statements but does not implement a fully optimized production query/materialization plan.

| Input / representation | Warm profile open, median | Run detail, median | All 37 export streams |
| --- | ---: | ---: | ---: |
| Three runs / compressed | 53.53 ms | 14.77 ms | 260.77 ms |
| Three runs / plain columns | 157.69 ms | 27.39 ms | 280.86 ms |
| Three runs / shared-string columns | 152.86 ms | 25.42 ms | 274.41 ms |
| Three runs / mutable-only columns | 194.12 ms | 16.09 ms | 262.35 ms |
| 1,000 runs / compressed | 293.02 ms | 18.72 ms | 39.11 s |
| 1,000 runs / plain columns | 513.88 ms | 15.39 ms | 74.51 s |
| 1,000 runs / shared-string columns | 548.72 ms | 19.37 ms | 72.47 s |
| 1,000 runs / mutable-only columns | 553.78 ms | 6.28 ms | 39.30 s |

Open medians use four samples after one first sample; detail medians use eleven after one first sample. Export uses one full pass into SHA-256 sinks rather than writing duplicate export files. It includes production serialization and necessary run loading, but excludes export filesystem I/O and initial mutable-profile reconstruction. The outputs total 3,283,027 bytes for the recorded input and 717,610,369 bytes for the large fixture. Small sample counts, caching, JIT tiering, GC and host activity limit timing comparisons. In particular, mutable-only and compressed detail reads both read the same compressed run representation: the observed 6.28 versus 18.72 ms is not evidence that the hybrid improved run-detail storage.

On the recorded input, warm profile reconstruction allocates about 36.1 MB on the calling thread with compressed records versus 61.2–62.1 MB through the column compatibility path. The large input allocates about 263.9 MB versus 357.0–358.8 MB. Large-fixture export allocations accumulate to about 16.3 GB for compressed/history-compressed variants and 45.1–46.7 GB for full column reconstruction. These are cumulative allocations across the whole export, not simultaneous memory use. Corresponding process CPU time for that export is 40.73 seconds compressed, 72.97 plain columns, 71.58 shared-string columns and 39.95 mutable-only. Short-operation CPU readings are too coarse for useful conclusions; raw samples retain them without claiming sub-millisecond CPU precision.

Fifty transactions increment the existing base-distance record by 0.125 metres each and update the same bounded receipt. Both representations use WAL with FULL synchronization, with automatic checkpointing and checkpoint-on-close disabled for the workload. Preparation and durable transaction time are measured separately; the input base-distance record is already below the current compression threshold. This is a small changed-record storage benchmark, not a replay of crafting, vendor callbacks or the production save coordinator. Crafting already uses typed columns in all alternatives. Active-checkpoint storage and reconstruction are included above, but active gameplay/checkpoint write timing is not measured here.

| Representation | Three-run median transaction | 1,000-run median transaction | WAL after 50 transactions |
| --- | ---: | ---: | ---: |
| Compressed | 0.873 ms | 0.897 ms | 412,032 bytes |
| Plain columns | 0.919 ms | 0.935 ms | 618,032 bytes |
| Shared-string columns | 0.912 ms | 0.941 ms | 618,032 bytes |
| Mutable-only columns | 0.927 ms | 0.939 ms | 618,032 bytes |

SHM adds 32,768 bytes in each case. WAL is measured at the workload boundary before the later validation open/close, not as a claim of permanent file growth. Each column write touches a typed table, record descriptor/checksum and receipt; the compressed case touches the record and receipt. Future field-level updates or a different envelope could change these costs. Median preparation is 0.019–0.027 ms compressed and 0.030–0.048 ms for columns, with about 11.9 KB versus 14.7 KB allocated; median transaction allocations are about 0.7 KB versus 2.8–2.9 KB. No save rewrites completed history. The large fixture's test-only history-update rejection trigger is suspended for representation conversion and restored in the same transaction before any measured writes.

Conversion plus compaction takes about 0.20–0.33 seconds for the recorded column variants. The large fixture takes 1.12 seconds for mutable-only conversion and 20.18–21.17 seconds for full columns. These are prototype conversion measurements, not a qualified migration time or a promised startup cost.

## Correctness and qualification boundary

Every original record in both fixtures was compared with its reconstructed value. Checks retain the declared double/float bit patterns and decimal bits, all integers and strings, omitted/null/empty states, collection ordering and identity. Equivalent JSON escape and floating-number spellings may differ after reconstruction; original JSON byte spelling is not the column contract. All 37 complete production export streams have identical lengths and SHA-256 hashes across all four representations for each fixture.

Separate cases cover Int64 extrema, maximum and finely scaled decimals, decimal scale and negative zero, extreme/subnormal double and float values, floating negative zero, Unicode, true/false, missing/null/empty fields, ordered maps and nullable children. Replacement rollback and reopen retain the original content in both column variants. Final integrity and foreign-key checks pass for every comparison database. Base-distance updates read back correctly.

The disposable format deliberately uses internal version 10007 so the shipping mod cannot open it. Its typed content checksums are a prototype contract; production receipt/history hash integration, schema conversion and crash-boundary qualification have not been implemented. This is sufficient for measuring representation size and verifying exported information, not sufficient to ship a replacement storage engine. No production build, deployment, live profile conversion, native gameplay test or new release is part of this comparison.

## Decision and evidence

The measured storage benefit does not justify another general persistence rewrite: the best hybrid saves only 4,096 bytes on the recorded input and costs 77,824 additional bytes on the large fixture. Fully converted shared-string columns cost 94,208 and 12,398,592 additional bytes, respectively. Ordinary text columns are substantially larger. The user accepted the recommendation to retain the compressed implementation and closed the blanket column-conversion proposal. This decision does not claim a globally minimal possible schema or prohibit typed columns for a separately justified feature.

Dedicated columns may still be appropriate when a concrete feature needs filtering, sorting or updating individual fields without loading the whole record. That should be evaluated against the actual query/workload and a direct typed reader/writer, independently of the broad claim that removing JSON must save space. Since this comparison, [JSON-only ZIP export](JSON_EXPORT.md) and [confirmed JSON/ZIP restore](JSON_RESTORE.md) have been implemented. The 37-stream measurements above describe the export implementation used during the comparison, not the current one-file archive.

Local evidence is under `artifacts/column-comparison/`: `summary.json`, the eight `*-final/report.json` files, per-table page usage, schema maps, and the prototype source under `probe/`. The comparison links against the qualified compressed package in `artifacts/package/`, the pinned SQLite dependencies and the installed game JSON assembly. The source fixtures remain under `artifacts/storage-optimization/native-acceptance/after/` and `artifacts/storage-optimization/candidate-qualified-3/1000/`. These contain local gameplay data and are not release artifacts.

After validation, the large disposable comparison databases and intermediate trial databases were removed, reclaiming 1,019,834,368 bytes. Their paths, sizes and final SHA-256 hashes are retained in `retired-databases.json`. The small four `real-*-final` databases remain available for inspection. Measurements, source fixtures, prototype source and reports were retained; no live profile, original export or pre-existing source fixture was deleted.
