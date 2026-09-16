# Encounter history: diagnostic build and historical qualification

Encounter history now runs in ordinary builds; see [production integration](COMBAT_ENCOUNTER_PRODUCTION.md). This page documents the optional diagnostic package and earlier prototype evidence. F6 recording and F5 preview remain diagnostic-only and are not needed by Runs. Historical package hashes and test totals below identify their original builds, not the current production package.

## Build isolation

Build with PowerShell:

```powershell
./scripts/build-encounter-diagnostics.ps1 -DuckovPath 'E:/SteamLibrary/steamapps/common/Escape from Duckov'
```

The build requires the installed native assemblies and was developed against Duckov 2.3.30. The script enables `UDSEncounterDiagnostics=true` and performance diagnostics, uses a unique directory under `artifacts/encounter-history/diagnostics/`, verifies the 13-file package, audits builder-path leakage, and writes an inventory with source and package SHA-256 hashes. `artifacts/encounter-history/diagnostics-latest.json` identifies the latest package. Its in-game mod-list name includes **ENCOUNTER DIAGNOSTICS**. The script does not deploy or overwrite the ordinary package.

Only raw diagnostic recording, the saved-capture preview/reader and their controls use `UDS_ENCOUNTER_DIAGNOSTICS`. Automatic native observers, trusted hook composition, storage barriers and the retained UI are identical to ordinary builds. The ordinary package audit rejects diagnostic types. Do not publish diagnostic packages or recordings as mod content.

## Recording

New active runs are captured automatically; F6 is not required for the Runs UI. **F6** starts an additional diagnostic JSONL recording and shows its overlay; **F6** again stops that diagnostic recording. Completion is reported in `Player.log`. Each capture gets a unique folder; a second recording does not overwrite the first. A failure or overflow is reported in the overlay and `Player.log`. F9/F10 retain the existing performance-diagnostic controls; they do not toggle encounter recording.

Evidence lives under the game's persistent-data root:

```text
UltimateDuckovStatistics/encounter-prototype/
  captures/<UTC timestamp and unique ID>/observations.jsonl
  maps/maps-cache/<calibration hash>.png
  maps/maps-cache/<calibration hash>.json
```

The shared map cache survives capture and game restarts. Capture files are JSON Lines for inspection, not a supported statistics export or restore format. No Duckov save is read or rewritten by the encounter observers.

## Saved-capture map preview

After stopping F6 recording and waiting for **Capture saved**, open the ordinary UDS panel, then press **F5**. The prototype opens an evidence viewer over that panel and borrows its input/cursor ownership. F5 or CLOSE returns to UDS; Escape or the configured panel hotkey closes the owning panel. F5 takes preview priority when it is also the configured panel hotkey. F6 remains reserved for recording in this opt-in build. The preview closes when recording starts, the owning panel closes, or its profile/modal state no longer permits previewing.

The viewer loads saved captures on a bounded worker, normally skipping the newest cache-only restart recording. NEWER/OLDER selects among the latest twenty capture folders; the arrows select a recorded run/map group. The recorded image/calibration loads from the shared cache without loading a gameplay scene. The header identifies which run/map evidence is shown; this is not an import into the active profile.

- The overview reveals the route over five unscaled seconds, weighted by walking length with brief dotted teleport steps. Stationary intervals do not stall it. Markers start hidden and become visible by recorded chronology; events without a matching observed interval wait until the end.
- A marker selects and scrolls to its list entry; a list entry focuses the available participant positions. Closing the entry returns to the completed overview. There is no manual zoom or pan. REPLAY ROUTE restarts the reveal.
- Player-death markers use the player's fatal position; kill markers use the enemy's. Missing positions do not borrow another participant's position. Focus can show the one known participant, but a connecting line requires both in the same recorded map. Distance is explicitly the horizontal map distance.
- Missing/failed artwork shows a themed fallback and the actual cache status. The encounter list remains usable. The preview displays source/weapon/ammo evidence, but does not present the raw diagnostic HP mutations or unfinished loot evidence as finalized totals.
- Drawing is a temporary IMGUI qualification surface, not the finished retained Runs tab. A route exceeding 4,096 edges is not drawn in this prototype; its encounter markers remain available. Files remain bounded to 128 MiB, 100,000 records and a 1 MiB line. Invalid/missing records split continuity and report partial evidence. The list draws only visible rows; source coordinates remain lossless.
- Route strokes are rasterized into one software-clipped, straight-alpha texture, at most 1,024 pixels per axis, and drawn in the same local pane/frame as the artwork. This replaces per-stroke GUI rotations. Reveal updates are quantized to twenty uploads per second; completed/focused layers are cached, and the owned texture is released on close or resize. Marker reveal uses the displayed route progress.

Targeted source/cache analysis found zero opaque cached pixels outside Ground Zero's native sprite triangle footprint. Its orientation and sampled colors agreed closely with the source texture. A proposed triangle-mask rewrite was therefore discarded: it would not correct the visible source-art border. The cache remains recipe v1. Native `MapSprite` material appearance and in-game landmark alignment still require visual qualification; the viewer labels this limitation rather than claiming a verified rendering fix.

Producers enqueue detached managed payloads. Serialization and file writes run on one worker. The queue holds at most 4,096 rows; it reports drops instead of blocking gameplay. A capture stops after 60 minutes, 100,000 accepted rows, or a 128 MiB evidence-file budget. The footer reports writer loss/budget status. A fully drained file does **not** prove complete gameplay coverage: individual probes and records carry their own unavailable/partial states. These raw observations allocate more than the planned production records and must not be treated as the final storage/performance design.

## What this prototype proves or leaves open

| Area | Collected evidence | Still to qualify or implement |
|---|---|---|
| Combat | Shared individual actor IDs, direct health transactions, candidate positions before fatal cleanup, confirmed deaths, projectile launch weapon/ammo and observed effect provenance | Real callback timing, nested damage/death ordering, source uncertainty, ordinary aggregate coexistence |
| Path | Exact float samples at approximately 5 Hz, visit/context boundaries, separate sampling gaps, guarded same-map SetPosition departure/arrival | Landmark alignment, reliable transition endpoints, sampled coverage, rendering and frame cost |
| Artwork | Detached native calibration, one bounded async GPU readback/PNG encoder, shared persisted image/hash manifest and restart decode | Visual orientation/color/alpha and calibration against at least three landmarks; packed/cropped sprites are explicitly unsupported in this first probe |
| Loot | Exact actor/source-item/corpse/local-inventory joins, inspection-aware top-level observations, directed Combine/Detach/AddAt/Plug evidence; second-stage split completion and reconciled directed quantities | Native qualification of split/return/nested operation reconciliation, exact taken/returned totals and nested inventory coverage |

The probe does not expose hidden item types or quantities. Full observed corpse inventory with proven taken-item highlighting remains the intended feature. A Split invocation alone is not reported as a proven completed transfer. Raw `loot.operation` rows remain non-additive; second-stage `loot.transfer` rows report separately reconciled directed quantity flow. Gross taken and returned quantities do not claim identity for interchangeable units or current possession. Projectile/pellet counts are not labeled ammunition rounds.

Native `InteractableLootbox.OnStopLoot` ends inspection and can fire while `LootView` remains open. The second stage distinguishes inspection stop from actual `LootView.OnClose`/`OnDisable`; content observations remain attached to the displayed, linked corpse until that view closes. Pending split evidence is fenced by capture/run/generation lifetime and does not survive a world drop as proven direct corpse-to-player ownership.

Split completion uses a single-await native `UniTask<Item>` wrapper, following the existing crafting observer pattern. The original task is consumed once; its result is observed before returning it to the caller, and native failures are rethrown while observer failures are contained. It adds a continuation for pending tasks and begins consumption when installed; scheduling/abandoned-task behavior therefore requires native testing. The attempted passive forwarding-source approach was rejected because the installed Unity/Mono `IUniTaskSource` inheritance is incompatible with this project's target reference surface. No test stubs, runtime reference replacement or unsupported helper executable were introduced to hide that mismatch.

## First targeted gameplay pass

Do these during ordinary play where convenient; do not manufacture rare cases or repeat a long raid solely for this first pass.

1. Start F6 recording before entering a raid. Keep the same recording across a map transition. Walk a short recognizable route and note three landmarks. Use a same-map teleport if one is naturally available; pause/resume once as a separate gap case.
2. Damage and kill two enemies of the same type separately. Let an enemy hit you if this happens naturally. Include a weapon switch and a shotgun/projectile case if convenient. Note the relevant weapon/ammo rather than relying on later held equipment.
3. Open a killed enemy's inventory, let inspection finish, take an item and part of a stack, then return part if convenient. A direct equip, quick loot, pet transfer or nested ammunition interaction is useful additional evidence. No requirement to cover every operation in one pass.
4. Return/extract normally, or retain a naturally occurring player death. Verify ordinary UDS combat/run diagnostics still work. Stop F6 and wait for the saved status. Record any hitch with the existing performance controls.
5. Restart the game. Start and stop a new capture from the main menu or base; the cache should validate/decode an existing image without first revisiting its source scene. Wait for capture saved, then close the game.

Inspect both recordings and Player.log before the next implementation step. Confirm distinct actor joins, one confirmed death per actor, event-time endpoints, teleport-versus-gap evidence, observed loot operations and the cache validation result. Inspect cached PNGs visually; successful decode alone does not establish correct orientation or map alignment. No-map scenes must retain encounter evidence; the themed missing-image UI is part of the later retained view.

## Targeted split and return qualification

The corrected preview build also contains the second-stage loot observer. The two saved first-pass captures do not qualify it: the raid has one `loot.split-request` with `RequestOnlyAsyncResultAndDestinationUnobserved`, but no split-completion or reconciled transfer rows. Reopening that capture cannot create missing native evidence.

Use one short, user-controlled encounter on the deployed prototype:

1. Start F6 recording **before killing the enemy**, so the exact death-to-corpse join is observed. Use a newly killed enemy; an old corpse from before recording does not provide this join.
2. Open its inventory and let inspection finish. Note one ammo/cash stack's item name and initial count, and the item name of one whole item you take.
3. Take part of that stack and return some. Use the ordinary split/transfer controls; do not manufacture a full inventory or failed action for this first check.
4. Close and reopen the same corpse, take another part of the stack, and take the whole item. Confirm the remaining stack count. For example, an initial 20 followed by taking 6, returning 2 and taking 3 should leave 13 on the corpse: 9 gross taken, 2 returned, net outward 7. Other counts are fine if recorded. The later take must retain the same corpse association after reopening.
5. Stop F6 and wait for **Capture saved**. No extraction or long raid is required to preserve the stopped prototype capture; leave the raid through normal gameplay when convenient. Report item names, starting/ending count, quantities moved, and any unexpected behavior or hitch.

Inspect `loot.corpse-join`, inspection/view lifetime, `loot.split-completed`, raw `loot.operation`, additive `loot.transfer`, unresolved/coverage rows and the capture footer. Require split source decrement, returned clone identity and destination ownership before counting; raw operation rows remain non-additive. Compare separate gross taken/returned counters and net outward against the user's observed quantities, including child-operation deduplication. A successful split alone is not a transfer. Hidden entries must remain hidden until observed, and the whole-item take must stay attached to the same enemy/corpse. Keep partial merge into a full inventory, pet/nested ammo, interrupted async completion and other unexercised paths explicitly pending rather than claiming blanket coverage.

## Delivery sequence after qualification

### First native pass: September 15, 2026

The user tested the deployed prototype DLL with SHA-256 `0e0d853e8e941b15962397b04e2e60bc6da432abe4bcf19c2a646f6633587940`, reported no noticeable hitches, and clarified that the apparent fourth kill was an NPC-versus-NPC encounter. The copied recordings and logs are retained locally under `artifacts/encounter-history/qualification/20260915T0306/`; `analyze.py` and `summary.json` preserve reproducible checks. The run record was read through SQLite read-only mode and its decoded payload SHA-256 verified; no profile was modified.

- Ground Zero run `2c3ba8f0f44e40eca8b0f83a6db56b26` lasted 99.34 active seconds. The prototype's three player fatalities matched the saved statistics, which also recorded **one separate NPC-caused death**. The capture retained a wolf and two distinct same-preset Scavs. SR-3M had six firing actions and two final blows; MMG had nine firing actions and one final blow.
- One Scav was damaged with MMG and finished with SR-3M under the same actor ID. All three player fatalities retained player/enemy positions before cleanup. The twelve player damage observations summed to `142.91666793823242`, exactly matching the saved run's damage dealt. No incoming player damage or player death occurred in this pass.
- 492 path samples were recorded. Both user teleports had witnessed endpoints, approximately 210.80 m and 211.68 m apart on the map. A third SetPosition changed only height by 0.366 m after spawn; the tested prototype mislabeled it as a teleport. A subsequent branch correction preserves such raw placements as `path-placement` with no dotted map connection. Five regression cases and the native build passed; this correction was not part of the deployed/tested build.
- The first recording drained 612 records and the restart recording drained seven, both with zero drops and complete writer footers. This is transport integrity, not full feature coverage.
- Ground Zero's 2048-square PNG was persisted at 4,468,349 bytes and decoded successfully from the shared cache after restart without revisiting the scene. Its hash matched. Map masking, orientation/color and landmark alignment still require presentation qualification.
- A corpse retained hidden item states until inspection, then emitted observed items, direct equip/removal, a split request and a one-unit return merge. Exact completion and provenance of the split remain unqualified; these raw observations must not be summed into finalized loot totals yet.

The logs contain no probe failure or dropped-event report. Generic scene-initialization index errors and Unity `GL.End` messages also occur before F6 capture starts; they are not attributed to the probes by this evidence. The user's smoothness report applies to this short pass, not a controlled frame-time benchmark. Cross-map visits, no-art scenes, incoming/player-fatal damage and effect cases remain outside this sample.

### Corrected native preview

The second prototype's native preview failed visual qualification: route strokes escaped the map pane and crossed the header/buttons. The correction replaces rotated IMGUI stroke rectangles with the clipped route layer described above. Seventeen raster tests passed, including the preserved Ground Zero recording with both teleports, direction/orientation, off-pane clipping, dash phase, timeline replay, and overview/focus at several pane sizes. The native build also passed. An offline image rendered from the same C# raster shows the route confined to its expected map region; it is not an in-game acceptance result.

On September 15, 2026, the user visually accepted the corrected deployed preview ("Looks good!"). The installed DLL SHA-256 was independently rechecked as `82e034d7e29773b32a63393c5992b043ade2e328c2c48e62c97647869343fcc7`. This accepts the route-rendering correction on the tested capture. It does not establish frame-time measurements, cross-map coverage, split/returned loot correctness, or acceptance of the future retained Runs integration.

The user identified the screenshot's apparent overexposure as HDR screenshot capture. No artwork/cache color correction was made.

### Targeted native loot pass: September 15, 2026

The user completed the split/return check on the corrected deployed prototype DLL `82e034d7e29773b32a63393c5992b043ade2e328c2c48e62c97647869343fcc7`. The stopped capture `20260915T1252423615095Z-0e53ed5306be48e283da214ebd08ad03` contains 793 records, zero drops and a complete footer, at 394,109 bytes. Its SHA-256 is `921dbd2df3ff7a649bf4b37409f95ccd09c3dd0c49929bb06f30dd65f0bf7f89`. A verified copy, contemporaneous Player.log snapshot, user-action receipt, reproducible `analyze_loot.py` and `summary.json` are retained locally under `artifacts/encounter-history/qualification/20260915T1252/`. The audit reads those copied files only; it does not open or change profiles or Duckov saves.

The user first inspected the corpse, then closed it without taking anything because another enemy attacked. All three subsequent/initial inventory openings retained actor 2 → corpse 4 → inventory 5. The first opening kept aspirin, cash and ammunition type/quantity fields null until their individual inspections completed. The second opening matched the user's starting inventory exactly.

| Observed item | Initial | Gross taken | Returned | Net outward | Final observed on corpse |
|---|---:|---:|---:|---:|---:|
| TT-33 (type 783) | 1 | 0 | 0 | 0 | 1 |
| Aspirin (type 20) | 3 | 3 | 1 | 2 | 1 |
| Cash (type 451) | 19 | 19 | 0 | 19 | 0 |
| Rust ammunition (S), type 594 | 17 | 0 | 0 | 0 | 17 |

Four additive transfer records matched the reported sequence: cash 19 taken, aspirin 2 taken, aspirin 1 returned, then aspirin 1 taken after closing and reopening. Both split completions proved the source decrement and exact returned clone before the destination operation continued. The returned aspirin occupied a separate one-unit slot; the last take removed that returned stack while the original one-unit aspirin stack remained. Raw Detach/Combine/AddAt rows stayed non-additive and did not duplicate the four transfers. No unresolved transfer, split-coverage failure or lost corpse-join row was emitted, and the loot probe reported active at capture stop.

This qualifies the observed inspection, whole-stack merge, split-to-player, split-return-to-corpse and reopen/re-take paths. It does not claim three unique original aspirin units were kept: net outward was two. Whole equipment transfer/equip in this pass, pet/nested ammunition paths, a partial merge into a full destination, interrupted async completion, broader map coverage and frame-time measurements remain separate checks. This targeted pass preceded the permanent-storage and retained-UI integration described below.

### Integrated test build

The development build now records encounters automatically, stores bounded route chunks and changed combat/loot records, and loads the selected completed run into **Runs → Map & Kills**. The details tab remains available. List and numbered markers select the same encounter; opening frames both positions and a distance connector, closing restores the complete map. There are no manual zoom controls. Teleports are dotted, gaps stay disconnected, and markers follow the five-second route reveal. No-map encounters retain their list/details beneath the satellite fallback. English/German captions refresh with the existing language-switch path.

The initial native check requested a short new run with both tested weapons, a same-map teleport, inspected corpse transfers, retained map/list interaction, language switching, restart and export. Existing runs and F5 diagnostic recordings are not backfilled into this view. Acceptance below distinguishes observed evidence from checks not individually confirmed by the user's general report.

Broader incoming/player-death, pet/nested transfer, missing-map and long-run performance checks remain separate qualification. Enemy silhouettes/hit-marker illustrations remain deferred.

### Integrated native pass: September 15, 2026, 16:32 UTC export

The user reported: "Exported. Overall functionality seems to work great" for native DLL SHA-256 `89d274f27a555785c163cd24c6ac28711e1d2340694f69b16f85a5dec3d72e97`, independently rechecked after the test. The copied export, Player.log, read-only SQLite encounter readback, reproducible audit and summary are retained locally under `artifacts/encounter-history/qualification/20260915T1632/`. Export SHA-256 is `314f32f62db7febc3bae3b6fb7589e9b0121c11608760e58352de103ad24fc0a`. These files are local qualification evidence, not package contents.

The 166.917-second Ground Zero extraction recorded five player kills and one other death. The wolf's final blow was credited to another NPC; it was not added to the player's kills. Eleven damage records sum to exactly the ordinary run's 591.3999977111816 damage dealt and 24.46001434326172 damage received. The sample includes SR-3M, MMG, melee, an attributed effect, and incoming enemy projectile/melee damage. It contains no player death or map transition.

All 38 encounter records match the closed SQLite records and export exactly. Four sealed chunks retain 829 raw path points and two witnessed teleports of approximately 210.52 and 212.29 horizontal metres. Six corpse inventory observations and ten transfer totals persisted; an uninspected slot retained no type or quantity. The first corpse's ammunition records include 30 gross taken and one returned, net 29. No new F6 diagnostic capture was made, so this audit cannot independently reconstruct those native transfer operations from the aggregate records alone.

The log reports a clean profile close and no UDS capture/publication/history/rendering failure. Generic index errors occur during native scene initialization; this evidence does not attribute them to UDS. One game activation is logged, so post-restart UI acceptance and individual language-switch checks are not separately established. The user's general functionality acceptance is not a controlled frame-time benchmark.

The export audit found zero encounter headshots versus nine in the ordinary run. Encounter capture had incorrectly used `DamageInfo.crit`; UDS's established classification uses recorded launch-time head targeting with accepted-hit deduplication. The subsequent correction shares that classification through the existing health callback. Old records are not backfilled. Two presentation corrections preserve elapsed hours and distinguish failed history reads from genuinely absent recordings, with a retry on reopening the subtab.

The next native check requested a short new run with head-targeted shots, a map transition/revisit, and reopening after restart. The four-visit export below supplies the next evidence; a long performance run remains a separate check.

The corrected development package was deployed with the game closed on September 15, 2026. All 13 installed file hashes matched the validated package. Native DLL SHA-256: `b689d61eeeaab7e93625272a99da9bdaa8042ee37a4e14e1a8e494c29d04c816`. Deployment receipt and validation details are retained under `artifacts/encounter-history/prototype/20260915T1648303942529Z-727d2665adbd4fb5b5e6eb38e96ba3c3/`. The preceding installed files were backed up; no save or profile was changed during deployment.

### Four-visit native pass: September 15, 2026, 17:04 UTC export

The user completed Ground Zero → Warehouse Area → Cellar → Warehouse Area and supplied a screenshot plus headshot notes for all ten kills. The copied export, Player.log, closed SQLite readback, reproducible audit and summary are retained locally under `artifacts/encounter-history/qualification/20260915T1704/`. Export SHA-256: `752030fafe47c55bcd89621e16cd9cc8018f3c4e7f371b6312fc4f5e945cfa3b`. The 340.369-second extracted run has 45 encounter records, 1,683 route points, ten player kills, 445 damage dealt and 36.7103271484375 damage received. Damage and headshot totals match the ordinary run statistics; all encounter records match the closed SQLite readback.

The recorded headshots/hits in chronological kill order are **4/4, 2/2, 2/2, 2/3, 4/4, 5/5, 3/3, 3/4, 2/3, 2/3**, totaling 29/33. Nine pairs exactly match the user's notes. The eighth was supplied as uncertain `2/3?` and is recorded as 3/4. Misses were separately noted for kills six and seven; these damage aggregates cannot assign missed shots to particular enemies. This validates the corrected headshot path on the tested SR-3M run without claiming all weapon/effect coverage.

Four distinct visits persisted: Ground Zero contains encounters 1–4, first Warehouse visit 5–8, Cellar none, second Warehouse visit 9–10. Cellar has no recorded artwork. The initial UI grouped both Warehouse visits into one selection; the user requested separate visit selections, continuous encounter numbering and a full-run list that selects the appropriate visit when an encounter is opened. The revision changes only presentation and can be checked against these existing records. English and German labels distinguish repeated visits. Missing artwork keeps the full feed usable.

The earlier Man of Light's one-point MMG effect row also receives an explicit effect-damage label; absent ammunition no longer makes it resemble another shot. The effect's specific identity and individual damage timestamps were not preserved, so the presentation does not invent either. The screenshot's apparent overexposure remains an HDR capture effect; no color correction was applied.

The revised presentation passed 2,361 main Release tests, 91 ordinary-shell tests, and a native build with zero warnings/errors. Its seven new regression cases use the sanitized native visit records for separate paths, numbering, bidirectional selection, missing artwork, fatal-visit ownership and mechanism-specific damage labels. These are automated checks, not a substitute for the next visual acceptance. Native player death, broader effects/transfers, restart UI and long-run frame-time qualification remain separate.

Next visual check uses existing runs: compare the two Warehouse visit routes; confirm the list remains 1–10 on Cellar; open encounter 9 from another visit and confirm the second Warehouse visit is selected; close it and check the completed visit overview; use a map marker to reopen the corresponding numbered row. Reopen Man of Light in the earlier run to inspect the effect-damage wording.

The revised development package was deployed with the game closed at 17:22 UTC on September 15, 2026. All 13 installed files were independently read back and matched the package hashes. Native DLL SHA-256: `0083b6d143798d3eceed3644622c3b75b1ca76379eb0933c6ebf34bfa2ab550f`. Deployment and validation receipts are retained under `artifacts/encounter-history/prototype/20260915T1720536793376Z-569e1c06b66649a690bd7d0ba4ff72c5/`. The preceding installation was backed up; deployment did not alter saves or profiles. Visual acceptance of this revision remains pending.

### Main-menu performance correction: September 15, 2026

The user accepted the separate visits, global encounter list and effect labels, then reported approximately 200 FPS without UDS versus 25 FPS with it enabled, including when the UDS panel was closed. The preserved `artifacts/encounter-history/main-menu-performance/Player-before.log` independently records an activation with no panel open: 856 UDS updates averaged 34.0802 ms, including 18.4488 ms in run lifecycle and approximately 3.68–3.69 ms each in holdings, healing and combat. The closed panel averaged 0.0009 ms. No profile or checkpoint writes occurred in that interval. `before-summary.json` records the source hash and per-area arithmetic.

Inspection of the installed Duckov 2.3.30 assembly confirms that `LevelManager.Instance` calls `FindFirstObjectByType<LevelManager>()` whenever the static reference is absent. `LevelInitializing`, `CharacterMainControl.Main` and pet inventory access lead back to that getter. The menu has no initialized level, so recurring observer checks repeatedly search the scene. These call sites and the measured distribution identify a concrete expensive path; the exact historical point at which the lookup cost grew has not been established by matched earlier captures.

`NativeLevelAvailability` checks the verified native static reference before the existing public getters. It reads the reference afresh on each call and respects Unity's destroyed-object semantics, so it does not delay discovery of a new or replaced level. Native initialization assigns the field before `OnLevelBeginInitializing`. If the backing-field contract is unavailable, UDS retains the original public lookup behavior. The correction gates the lookup only: observer cleanup, terminal/checkpoint retries, profile operations and encounter pipeline draining continue. The encounter host uses the same check, and diagnostic builds measure its tick separately as `EncounterCapture`.

Four new boundary tests cover absent-level polling without scene searches, immediate initialization/replacement, destroyed references, and contract recognition. The 95 shell tests and 2,361 main tests passed. An initial main-suite run reported 992 allocated bytes in the unchanged Harmony stamp allocation test; that test passed in isolation and the complete suite passed on repeat without changing its source. Native prototype and ordinary builds passed with zero warnings/errors; installed contracts, package inventory/path checks and ordinary-release diagnostic exclusion passed. These static/automated checks preceded the native result below.

The corrected package was deployed with the game closed at `2026-09-15T17:52:00.2137439Z`. All 13 installed hashes matched; native DLL SHA-256 is `50ae33409539359ddd57409edc951af81b00a69cb01d7857c82eb0ce7925bb48`. Receipts are under `artifacts/encounter-history/prototype/20260915T1750184305556Z-c36e3c6c46e744139666b32aa26f1b92/`. Prior installation files were backed up; no saves or profiles were changed.

The requested native check was a cold launch with UDS enabled and an F9–F10 interval in the main menu with the panel closed, followed by a brief UI open/close and normal game shutdown. The captured interval below covers the closed panel; it does not independently isolate the later UI open/close step. Gameplay observer qualification remains separate.


### Main-menu correction accepted: September 15, 2026, 18:23 UTC activation

The user reported "Seems fixed to me." The installed DLL was independently rechecked as `50ae33409539359ddd57409edc951af81b00a69cb01d7857c82eb0ce7925bb48`. The preserved log and computed summary are under `artifacts/encounter-history/main-menu-performance/20260915T1823/`; Player.log SHA-256 is `cda963d3d30d8cb633e644fe1c209f292e3533db5972a877450b55cadf00834d`.

The F9–F10 interval contains 9,152 updates over 38.037 seconds, averaging 240.608 updates per second. All updates had the UDS panel closed. Mean UDS Update cost fell from the earlier 34.0802 ms to 0.052115 ms, a 99.847% reduction; the largest UDS Update was 11.8494 ms compared with 138.0297 ms previously. The new encounter-capture scope averaged 0.043065 ms and peaked at 11.8403 ms, so occasional capture/verification work remains visible even though the sustained menu regression is resolved. The counters do not identify that peak's exact inner operation or provide a frame-time distribution.

No checkpoint/profile writes, gameplay capture events, UDS failures or exceptions appear in this interval/session. The profile closed cleanly. The user report and timing evidence accept the closed-panel main-menu correction on this build. They do not establish a matched UDS-disabled benchmark, post-open/close timing, or long-raid/gameplay performance. No new code or deployment was needed for this acceptance check.

### Failure handling: September 15, 2026

The automatic capture pipeline now distinguishes a stopped recorder from pending publication. Queue overflow drains accepted observations; a failed reducer task is consumed once, keeps previously published records and completed batches, and drops only its unsafe uncommitted tail. Capture stops for the current host, with durable run-level coverage notices for affected runs, including subsequent runs before restart. A family-specific native probe failure records incomplete coverage while independent capture can continue. Optional diagnostic writer/viewer failures remain separate from automatic capture.

Publication rejection or an exception keeps the exact pending record and its original generation, with a 500 ms retry interval. Run/profile barriers can complete after valid records and failure coverage have drained even when capture remains stopped. They continue to wait on genuine storage failures. The notice survives reopen and export/restore and appears once above the encounter feed; failure before the first visit does not create a fictitious map or imply zero kills. English and German text are provided. Normal captures receive no new notice.

Eight new automated failure cases cover queue exhaustion, failed-worker consumption, prior-data immutability, completed owner batches, rejected/throwing publication with backoff, independent family degradation, terminal completion after storage recovery, durable ZIP restore, and invalid coverage mutations. The complete main Release suite passed 2,369 tests and the ordinary-shell suite passed 95. Prototype and ordinary native builds passed without warnings/errors; installed contract checks, package inventory/path audit and ordinary-release diagnostic exclusion passed. These tests inject failures into detached payloads and temporary repositories; no user profile was fault-injected.

The final package is `artifacts/encounter-history/prototype/20260915T1905205213474Z-abb2233ecfec445f8b75c874284227d3/`. It was deployed with the game closed and all 13 installed file hashes matched. Native DLL SHA-256: `438ab8b168880108fd5a5a2fc1e3e6068b61ca75cc4325bfc3ba70c36f00e181`. Manifest, deployment receipt and validation receipt accompany the package. Automated results do not establish native failure-banner layout or broader gameplay acceptance. The requested smoke check is to restart, inspect a known run's map/feed and open/close UDS without renewed menu slowdown.

The next stage is design alignment with the user's current mockups. The user explicitly requires that work and visual acceptance before production integration. This build remains an opt-in prototype and does not change that release boundary.

The user subsequently confirmed the failure-handling smoke check worked. This accepts ordinary restart/history interaction, not a deliberately induced native failure banner.

### Mockup alignment pass: September 15, 2026

The user's six design corrections are implemented in the retained prototype. Subtabs now have rounded top corners and meet the orange rule. Map selections keep distinct visit identities but show only map names. Feed rows separate the circular number badge, concise outcome title and smaller gray right-aligned elapsed time. The feed starts beside the map selectors and the map uses a square frame. The map's numbered markers are 24 px circles in sampled mock color `#275576`; P/E markers use 28 × 36 px pointed shapes with localized hover/focus tooltips, with enemy color `#F75566`.

Routes and connectors use a 3 px white core and a 2 px black outline on each side, plus the existing shadow. A 16 px distance label sits in a 65%-opaque black box, offset perpendicular to the connector and kept within the pane. Expanded details use separate damage headings, compact weapon/ammunition icon rows and 60 px bordered loot tiles. Gross observed/taken/returned quantities and headshot/final-blow details remain available in tooltips. Effects do not acquire ammunition or bullet-hit claims; projectile hit counts remain hits rather than bullets consumed. Missing icons retain text/tooltips. Retained labels and tooltips refresh with language changes; pooled feed rows are rebound on content/layout/scroll changes instead of measured every frame.

The complete main Release suite passed 2,378 tests; the ordinary shell passed 95. New geometry checks verify exact stroke widths and distance-box separation for eight line orientations/edge placements. Native prototype and ordinary builds passed without warnings/errors, the 13-file prototype inventory/path audit passed, and ordinary-release diagnostic exclusion passed. These are automated/static results; native typography, pointer behavior and visual fidelity await user comparison against the mockups. Production integration remains deferred.

The final design package is `artifacts/encounter-history/prototype/20260915T1936425567218Z-fcb01bf69a69433c8d6e9e92bff1063d/`. It was deployed with the game closed; all 13 installed files matched their package hashes. Native DLL SHA-256: `4620123e06fd2c1c98f428d01e0ba74e183adcd050a444852d8d50421c9ed76f`. The package directory contains the manifest, deployment receipt and validation receipt. Visual testing can use existing captured runs: compare the overview and an expanded encounter, inspect P/E and loot/source tooltips, and switch English/German before production integration.

### Passive map tooltips and weapon icons: September 15, 2026

The user reported improved visual alignment but missing P/E tooltips, unwanted marker hover/click sounds and pressed tint, and undersized weapon icons. P/E now use passive graphics with pointer-only tooltip triggers, without `Button`, `ButtonAnimation` or selection feedback. The encounter view reuses UDS's retained popup above its own scrolling masks instead of Duckov's global tooltip renderer; the latter depends on a separate native display and is unreliable on menu surfaces. The same correction covers encounter damage-source and loot tooltips. Short labels fit their text; long details wrap within the existing width cap. Changing language, hiding the source, scrolling or changing encounters clears stale tooltip ownership.

Weapon icon frames increased from 30 × 26 to 52 × 44 px with vertically centered text; ammunition icons remain 30 × 26 px. All 96 shell tests passed, including compact localized labels, long-detail width limits and popup cleanup. Prototype and ordinary native builds passed without warnings/errors; package/path and ordinary-release exclusion audits passed. The package under `artifacts/encounter-history/prototype/20260915T1957157998932Z-7b9e8022cb0a43a6a0ebda79ac4d6fd4/` was deployed with Duckov closed and all 13 installed hashes verified. Native DLL SHA-256: `071de9a0d671fa90275e62f77f7594c447c92f4e3ff5af29b565eff44c40ec01`. Native hover behavior and icon sizing await user acceptance; existing captured runs suffice for this check.

The next user refinement makes both weapon and ammunition frames 60 × 52 px, adding 8 px to each weapon-frame dimension. Loot tooltips show the item name alone for untouched items and straightforward looting, including taking a whole stack. Taken/Returned totals appear only when at least one return was recorded, including when the item was later taken again. The maximum-observed line and its unused English/German keys were removed; capture data and tile quantities are unchanged. All 96 shell tests, the native prototype build and the 13-file package/path audit passed. Package `artifacts/encounter-history/prototype/20260915T2031353206544Z-4c000c2c79c9401f80469137542fa889/` was deployed with the game closed and all installed hashes verified; native DLL SHA-256 is `9c57fa9dee6d2ca09b8b3ff66cbb982a7e309f1dcae15858bc29fb8788d059b7`. Visual acceptance remains with the user.

The user corrected the rectangular source-icon frames: both weapon and ammunition icons now use a single 60 px size for a square 60 × 60 px frame. Previously, aspect-ratio preservation limited square sprites to 52 × 52 px inside the 60 × 52 frame. The native build and 13-file package/path audit passed. The corrected package is `artifacts/encounter-history/prototype/20260915T2038423732444Z-795c5f10a3ec43e4a3326dd0bd14f425/`; deployment with the game closed verified all 13 hashes. Native DLL SHA-256: `5aa180219cafb270b16e3c56b0cb4d559b472c7b075c1fc2fcbd417fd9390af6`. This size-only correction needs visual acceptance; no tests were added or rerun.

### Inline combat details: September 15, 2026

The source rows now say `in 1 hit` / `in N hits`, with localized singular/plural headshot counts inline for outgoing projectile damage. Source fragments share a font-metric baseline instead of independently centering their glyph geometry. Weapon/ammunition tooltips and their misleading encounter-wide final-blow label were removed; map-pin and loot tooltips remain. Selected encounter timestamps are white and return to `#b1b1b1` when collapsed. Inspection of `NativeCombatAttributionAdapter.RecordHealthTransition` and `CombatObservationPolicy.ClassifyProjectileTransition` confirms the existing headshot callback only classifies player-owned ranged hits against enemy targets. Incoming damage therefore does not display a headshot count; a stored default zero is not proof of no incoming headshots. Capture semantics and persistence were not changed.

All 28 focused encounter presentation/live-capture tests and 96 shell tests passed. The native prototype build completed with zero warnings/errors and its 13-file package/path audit passed. Package `artifacts/encounter-history/prototype/20260915T2048450174747Z-41eff7b2e0a041cbba0616a5aa5621f2/` was deployed after Duckov closed, with all 13 installed hashes verified. Native DLL SHA-256: `71cc4b3875b328629b6f7be818288b60e0a4d95d61138b8256a10eb35d6b5653`. Native baseline appearance, wrapping and selected timestamp contrast await user acceptance using existing encounters.

### Simplified loot presentation: September 15, 2026

Loot tooltips now contain only the item name. Each item's tile quantity comes from its first inspected opening, summing matching stacks there, rather than the largest quantity across openings. A later player deposit cannot inflate that observation. Transfer-only evidence supplies no inventory quantity. Highlighting still uses positive net transfers to the player/pet; gross transfer counts remain stored, but are no longer displayed. The old Taken/Returned localization keys were removed. This changes presentation only, including existing captured runs.

All 44 focused loot/presentation/live-capture tests and 96 shell tests passed. The three new cases cover depositing 90 into an observed 15 stack, chronological selection and multiple stacks, later inspection, and transfer-only evidence. The native prototype build passed with zero warnings/errors and the 13-file package/path audit passed. Package `artifacts/encounter-history/prototype/20260915T2109424665546Z-2f3324676a87488cbf6773e45549d82d/` was deployed with Duckov closed and all 13 installed hashes verified. Native DLL SHA-256: `3e3b0ca57574d73a24e2703daec8784d7353c8168d2fd77442a934a1427ff6cf`. Visual acceptance remains pending; existing encounters suffice to check the name-only tooltip and retained highlight.


The user subsequently accepted the name-only loot tooltip refinement. Production integration is recorded separately in [COMBAT_ENCOUNTER_PRODUCTION.md](COMBAT_ENCOUNTER_PRODUCTION.md).
