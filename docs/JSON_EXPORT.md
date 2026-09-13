# JSON export

Diagnostics writes one `statistics.json` inside the existing timestamp-and-generation export directory. CSV export has been retired. Existing exports are not changed or removed. Historical milestone and qualification documents describe the CSV files produced by those earlier builds.

The JSON contract is unchanged: schema 1, generation/slot/revision and export time, item totals and groups, completed runs and routes, records, combat and equipment evidence, capabilities, economy/holdings, crafting, world time and raid/base distance. Original numeric precision and unknown, unavailable, partial and repaired-data evidence remain intact. It is a structured statistics export, not an interchangeable SQLite database or profile backup; manual restore/import remains a separate feature.

Export captures a detached generation and revision after the existing durability boundary. The SQLite snapshot remains valid across subsequent saves, profile changes and reset. Run details are projected one at a time; the writer does not materialize the complete run history or a whole-file JSON string. It writes UTF-8 JSON to a temporary file, flushes it to disk and publishes the final filename only after serialization completes. A failed write propagates to the existing failure UI. Panel close and profile changes still prevent stale completion messages.

This change removes CSV formatting and file writes during explicit export. It does not change normal gameplay capture, SQLite persistence, schema versions, or compression.

Automated validation: Debug and Release each passed 2,202 main tests and 90 shell tests. Coverage includes one-file output inventory, byte-equal streamed/materialized JSON, detached revision and generation/reset isolation, current/partial/historical evidence, a 1,000-run export, and failure without overwriting an existing export. The native Release build, installed contract probe, changed-source whitespace check and package/IL audit passed.

Native acceptance on 2026-09-13: the deployed package passed all 13 installed-file hash checks, and the user exported from the game and confirmed that the new export directory contained only `statistics.json`.
