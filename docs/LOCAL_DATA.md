# Local data and privacy

UDS performs no telemetry, network reporting, account login or remote synchronization. Runtime data stays below Duckov's `Application.persistentDataPath`, in `UltimateDuckovStatistics/v1/`. The packaged mod never writes Duckov saves.

Per-save data includes the UDS generation, native save slot, read-only save fingerprint/SaveTime evidence, aggregate statistics, compact run summaries, capability state, bounded diagnostics, session/active-run checkpoints, atomic backups and exports. Replaced generations are archived read-only. Run history and user-created exports persist; diagnostic logs and correlation caches are bounded. Removing the mod package does not remove these files.

The first v1-format launch starts fresh beside old development data. There is no supported import of `0.x` profiles. Current-format reinstallation preserves the current data. Invalid current files use validated primary/backup/temporary recovery; incompatible formats are archived intact. Do not remove validators, checkpoints or temporary files to force a damaged primary to load.

Diagnostics exports a frozen generation snapshot as JSON and CSV. Exports contain gameplay identities, timestamps and statistics and may contain mod/item names supplied by native content. Diagnostics can contain local paths and error details; the UI abbreviates paths where appropriate. Inspect exported files and logs before sharing them. No sharing happens automatically.

Reset requires a deliberate confirmation, starts with Cancel focused, archives the current UDS generation and starts a new one. It does not change the Duckov save. A durability failure can leave the request pending; retained data and bounded retries take priority over pretending that reset succeeded. Clipboard failure does not turn a successfully written export into a failed export.

Build artifacts use normalized `/_/uds/` source identities. Post-build/package audits scan PE/PDB metadata and UTF-8/UTF-16 payloads for builder identities and absolute machine paths. PDBs stay outside the five-file install package. Source paths, raw performance sidecars and local qualification logs can contain machine-specific information and remain local unless explicitly shared.
