# Restore statistics from an export

Current source adds **Restore statistics** to Diagnostics. It accepts the schema-1 UTF-8 `statistics.json` export and the current `statistics.zip` containing exactly that one root entry. This is an explicit replacement of the selected save slot's UDS statistics. It does not merge histories, restore Duckov's save, or resume an interrupted raid.

## In-game workflow

1. Select the intended Duckov save slot, outside a raid, and open UDS > Diagnostics.
2. Select **Restore statistics**. The dialog lists local exports through Previous/Next controls, newest first. It searches the UDS exports directory and its immediate subdirectories, excluding directory links. An export from another slot is rejected.
3. For an export kept elsewhere, use File Explorer's **Copy as path**, then **Use copied file path** in the dialog. Quoted paths are accepted. A ZIP can be selected directly without extraction.
4. Wait for validation. Review the abbreviated source path, slot, export time, number of completed runs and raid distance. Invalid, missing, unsupported or incomplete files cannot be confirmed. Cancel is initially focused. Closing the panel or changing save slot cancels the preview.
5. Choose **Replace UDS statistics** to confirm. Wait for success before interacting with the profile again. A durability boundary may keep the request pending; failure is never reported as success.

File reading and validation run on demand on a background worker. Changing the selected file or cancelling requests cancellation of that preview; its eventual completion cannot revive a dismissed dialog. Confirmation uses the validated detached content, so changing the source file afterward cannot change what is restored. Preparation of the replacement database runs at this explicit profile boundary and can briefly pause the game for a large history. It adds no recurring gameplay work.

## What is restored

The export restores item/group totals, all exported completed runs and routes, records, combat/equipment/container evidence, economy, crafting, world time and recorded base movement. Original numeric precision and recorded partial/unknown/unavailable evidence are retained. Holdings are treated as previously observed until the game supplies a fresh observation. Current native capability reporting is refreshed through the existing profile-change flow.

The destination receives a new UDS generation. Imported runs keep their run IDs and are rebound to that generation, as are holdings observations. Session checkpoints and replay cursors start fresh. Export time provides the statistics creation timestamp because the export does not contain the original profile creation metadata. Exported base-distance partial status is preserved conservatively: the existing export cannot distinguish a historical collection gap from current movement capability loss.

The source must match the destination **save slot**, but can come from an older UDS generation of that slot. The export does not carry enough native save identity to prove it came from the same Duckov character; selecting the correct backup is the user's responsibility. It must contain all runs represented by its run count and consistent run-derived counts, distance totals and records. Existing current-schema metric and association validators also run before replacement. Uncompressed JSON is limited to 1 GiB, including compressed ZIP input; no archive entry is extracted onto disk. Raw `profile.json`, SQLite files and exports from unsupported schemas are not accepted.

The previous generation is archived read-only using the existing reset preparation/promotion/rollback mechanism. This one-time archive is not an automatically maintained recovery database. Failure before promotion preserves the active generation. Existing persistence barriers drain pending work and rebind native tracking after replacement. Export source files and Duckov save files are unchanged.

## If corruption prevents UDS from opening

The in-game flow requires an open UDS profile. It does not add an automatic fallback for a corrupt database. With Duckov closed, preserve the affected `v1/profiles/slot-NN/current` directory **as a whole**, including its database and sidecars, by moving it outside `current`. Start Duckov in that slot to create a fresh UDS profile, then restore a user-kept export through Diagnostics. Do not delete individual WAL, SHM, failure-marker or database files to force a damaged generation to load. Moving or deleting live user data is a user-controlled recovery action.

## Validation

Automated coverage includes populated JSON/ZIP restore, SQLite reopen and continued run/movement recording, holdings-generation rebinding, source-file changes after preview, invalid JSON and archive rejection, wrong-slot/generation rejection, preparation failure in JSON and SQLite storage, native observer handoff, and modal cancellation/focus/localization. A local qualification also read both user-created JSON and ZIP exports, restored each into an isolated temporary SQLite profile and reopened it, verifying their three recorded runs. The source files' SHA-256 hashes were unchanged.

Automated validation on 2026-09-13 passed 2,225 main tests and 91 shell tests in both Debug and Release. The native Release build completed with zero warnings/errors; the installed native contract probe, changed-source whitespace check, 13-file package verification and release IL/privacy audit passed. No new runtime dependency was added. These checks do not replace in-game acceptance.

Deployed for native acceptance on 2026-09-13 with Duckov closed. All 13 installed files matched the validated package by SHA-256. Deployment changed the mod package only; no live UDS statistics or Duckov saves were restored.

The user subsequently reported that the in-game flow appeared to work with a freshly created export and requested committing it after matching the Diagnostics restore button's uppercase style. Both English and German action labels now use uppercase. This native check does not independently demonstrate rollback to older values because the selected export closely matched the current statistics; the automated tests cover replacement of different target values and persistence afterward.
