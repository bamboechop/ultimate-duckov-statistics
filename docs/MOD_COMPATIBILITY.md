# Optional mod compatibility

Current source adds optional integrations for First Person Camera (Workshop `3785095584`), Become Veteran (`3736801011`) and Storage Search Bar (`3592349195`). These changes are not in the published v1.2.0 archive. No external mod DLL is bundled or required. The initial two-run native smoke test passed on 2026-09-23; broader native cases remain listed below.

Enable Harmony and the optional mods before UDS, and restart Duckov after changing the enabled mod set. Runtime patch-set changes still invalidate affected recording; changing mod load order does not reconstruct missing history.

## Behavior

| Mod | Recording behavior |
| --- | --- |
| First Person Camera | Vanilla launch-time head evidence is retained when FPC is absent or first-person mode is off. With the verified FPC damage prefix active, UDS snapshots that prefix's target-specific head-hit result before native damage. Generic critical hits do not become headshots. Aggregate and encounter records share the same evidence and deduplication. Grenade velocity/camera-shake changes preserve native source attribution. |
| Become Veteran | Native item healing and native healing buffs retain their existing attribution. Each proven BV delayed-healing queue entry retains the original item-use correlation; actual applied HP is measured for its immediate heal and later ticks. A replacement entry replaces its source binding. Natural regeneration, pre-existing unobserved queues and canceled uses do not borrow another item's attribution. Resetting UDS observation clears only its bindings, never the mod's queue. |
| Storage Search Bar | The inspected loot-window close prefix clears warehouse search UI without moving items. It can coexist with UDS corpse observation and item-transfer hooks. |

`CharacterBuffManager.AddBuff` now has an independent UDS owner. A rejected healing/BV hook cannot disable combat buff actor/weapon observation. Healing still depends on trusted shared buff observation for complete native delayed-healing attribution: losing that shared observer correctly degrades healing as well as the combat metrics that depend on it.

The shared patch inspector accepts only the inspected callback, target, category, owner and ordering for these builds. Missing/duplicate UDS callbacks, unknown foreign patches and later patch-state drift remain failures. Build identity combines the loaded module ID and a cached SHA-256 check of the installed DLL, rather than trusting a mod name or reused version number. Changed builds require reinspection; compatibility is not promised for every mod sharing the same owner ID. FPC-specific contract loss marks headshot evidence unavailable/incomplete while independently observable combat continues where its own hooks remain trusted.

Discovery and binary hashing occur during initialization/patch inspection. FPC's hit path uses a cached mode delegate. BV uses cached queue metadata and existing patch-state tokens; it does not poll HP, enumerate patch metadata or hash DLLs for each healing tick. Nothing adds persistence writes to these callbacks beyond existing event collection.

## Panel pause and camera input

Base F8 access now opens Duckov's native `PauseMenu` before showing UDS. Duckov derives `GameManager.Paused` from that menu, and the inspected FPC `IsUiBlocking()` checks the same state before processing mouse rotation. UDS continues to block ordinary native gameplay input and release the cursor. No separate camera patch or direct time-scale override is installed.

UDS owns only a pause it started: closing statistics resumes that pause, while an already-open pause menu stays open. Failed panel activation, profile changes and disposal release UDS-owned pause/input state. External pause closure or replacement closes statistics without reopening a pause or closing the replacement. Main-menu access does not start a base pause.

Shell tests cover these ownership and failure paths; the native contract probe checks the public pause methods. After deployment on 2026-09-23, the user confirmed the base FPC pause test worked: F8 pauses gameplay and camera movement, F8/Escape closing resumes a UDS-owned pause, and opening from an existing pause leaves that pause open when UDS closes. Main-menu access and reopening after a scene transition remain broader native regression checks.

## Inspected build identities

The installed DLLs were statically inspected on 2026-09-23 against Duckov 2.3.30 and HarmonyLib 2.4.1.0. FPC uses assembly version 1.0.0.0 across differing releases, so that version alone is not a compatibility check.

| Assembly | Module ID | SHA-256 |
| --- | --- | --- |
| FirstPersonCamera | `916ebb5c-dc53-4c53-ba0d-a4f672364748` | `832be121a23095bc37dba4735b155a3d1e0c8a511eb18d5e992ba91887965cac` |
| BecomeVeteran | `f86b3a0a-3767-44bc-a545-72252bbf26f4` | `a79a3391fe66b2ea0000eb2310e24d512ed77276af0850eb292515c48a8e8b6b` |
| StorageSearchBar | `6d2fdec7-3d42-480f-8fad-c02752e66fc5` | `2887f088a6bd99186b855135b0e72a548fbd6f72a17076f69e28dee3b3e205b0` |

## Qualification

Automated coverage uses the production adapters with boundary doubles. It covers native/FPC mode selection, per-target fatal headshots, projectile deduplication, aggregate/encounter agreement, delayed healing retention/replacement, HP caps, natural-regeneration exclusion, cancellation, reset, shared-buff independence, and rejected patch identity/order. It cannot prove Harmony injection or Unity rendering and frame time in the running game.

The combined compatibility and pause build passed 2,519 main tests and 132 shell tests in each of Debug and Release, the installed Duckov contract probe, warning-free native Release builds and the 13-file package audit. Deployment verified all 13 installed file hashes. The deployed main UDS DLL SHA-256 is `b4834c9c930e87300c23adde8d25d150cf707e2c160a49c3c0ac05d027906bc1`; automated results and the user's gameplay checks are separate evidence.

### Native smoke test, 2026-09-23

The user reported successful gameplay with FPC enabled for the first test run and disabled for the second. Those mode labels come from the user's test description; exports do not persist camera mode. Export `20260923T1627140957030Z-b3b8ff3260f54043a4dc2735e3eb5522` contains these runs as profile runs 2 and 3:

| Test | Player kills | Headshots | Headshot final blows | Damage dealt | Item HP restored |
| --- | ---: | ---: | ---: | ---: | ---: |
| FPC enabled | 4 | 5 | 4 | 58 | 8.354887 from two bandages |
| FPC disabled | 2 | 4 | 1 | 30 | 0; no items used |

Both runs retain all 19 combat capabilities as supported and `HealingCaptureComplete=true`. Encounter damage, incoming damage and headshot sums agree with their run totals; neither run contains encounter coverage failures or encounter/damage gaps. The first run includes three loot records. Player.log confirms successful combat, BV healing and shared buff activation. This verifies the exercised gameplay and export paths, not every delayed-healing edge case or grenade path.

The earlier profile run still has incomplete healing capture, so lifetime healing remains partial. Item Use currently conflates that historical gap with current unavailability in its notice; its wording should distinguish those states.

After the export, Player.log records FPC being loaded again during the same game session, followed by UDS invalidating Health.Hurt and Grenade.Launch patch stamps. This did not affect the exported test runs. Restarting Duckov after changing the enabled mod set remains required.

### Broader native checks

1. Open and close UDS and check ordinary frame time; the smoke test did not include a frame-time capture.
2. Extend the headshot test to a shotgun or multi-contact projectile; the smoke test used a TT-33.
3. With BV split healing enabled and sufficient missing HP, use a medicine and wait for all ticks. Compare the total actually restored with UDS, then test a near-full-HP heal and a second medicine that replaces a pending heal. Natural regeneration must not inflate the medicine's total. Repeat an immediate heal with split healing disabled.
4. Exercise a native healing buff and a grenade/effect kill. Search the warehouse, close it and reopen a corpse; item quantities and loot highlighting should remain correct.
5. Export any additional test run after returning to base. Existing incomplete history remains incomplete; this integration does not backfill earlier missing combat or healing.

Testing optional mods disabled, death or a profile transition with queued healing, and mod-contract failure completes the broader regression matrix. Gameplay, save changes and final acceptance remain user-controlled.
