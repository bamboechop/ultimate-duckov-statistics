# M15 user-controlled gameplay validation

This procedure validates the installed M15 candidate without giving Codex control of Duckov gameplay or saves. The player performs every game launch, save selection, inventory move, ATM action, return to menu, export, and shutdown. UDS only reads native state and writes its own external profile/export files.

Use one progressed test save whose Money and physical Cash can safely be observed and changed. Do not use a save whose loss or conversion would matter. Record the selected Duckov slot and the values shown before every action.

## 1. Cold activation and base hydration

1. Start Duckov normally.
2. Open Mods before selecting a save and confirm HarmonyLib and Ultimate Duckov Statistics are both enabled.
3. Select the chosen test save and wait until the base is fully interactive.
4. Press F8 and open Overview, Economy, and Diagnostics.
5. Record current Money, current Cash, and liquid wealth. Confirm all three M15 capability rows are `Supported`.
6. Compare Money with Duckov's own account/ATM display. Count physical Cash using Duckov's normal inventory/storage/pet UI. The UDS Cash value must equal the top-level owned total; Cash placed only inside a nested item slot is outside the installed native ownership helper and must not be added.
7. Confirm liquid wealth equals the checked arithmetic sum `Money + Cash`. A genuine zero must display `0 (current)`; missing evidence must display `Unavailable`, never zero.

Expected result: after complete hydration, Money and Cash are `Current` for the selected UDS generation. No new M9 flow is needed for a non-zero holding to appear.

## 2. Internal Cash movement

1. Move or split a Cash stack between the main inventory, PlayerStorage, and pet inventory without creating, consuming, dropping, depositing, or withdrawing Cash.
2. Reopen or refresh the Economy panel after the move settles.

Expected result: total current Cash and liquid wealth remain unchanged. M9 must not fabricate an inflow/outflow solely for the internal move. Diagnostics may retain the same observation timestamp because an unchanged current total does not create another holdings persistence mutation.

## 3. Representative Cash ownership change and unit check

Use Duckov's ATM for the smallest safe non-zero amount available, and record the amount before confirming the action.

- Deposit Cash: current Cash decreases by exactly the amount, Money increases by exactly the same amount, and liquid wealth remains unchanged; or
- Withdraw Money: current Money decreases by exactly the amount, Cash increases by exactly the same amount, and liquid wealth remains unchanged.

Expected result: both changed components become `Current`; liquid wealth remains their exact checked sum. The M9 flow section remains separately labelled and must not be presented as the holding or as profit.

## 4. Main-menu freshness

1. Return to the main menu using Duckov's normal controls.
2. Open UDS with F8 if Duckov makes the panel available there.
3. Record the three holdings states and values.

Expected result: Cash is `LastObserved` or `Unavailable` while the authoritative owned inventory roots do not exist. Money may be `Current` if the hydrated manager remains authoritative, or truthfully `LastObserved`; no component may expose another slot or generation as current. Liquid wealth is unavailable unless both components are current.

## 5. Clean restart and same-save rehydration

1. Exit Duckov normally and wait for the process to close.
2. Start Duckov again, confirm both mods are enabled, and select the same save.
3. Before and after base hydration, inspect Overview, Economy, and Diagnostics.

Expected result: persisted matching-generation evidence begins only as `LastObserved`. After the same save's native Money and all three Cash roots are authoritative, the values return to `Current` and match the post-change values from step 3. No startup callback creates an M9 flow.

## 6. Projection, export, and shutdown agreement

1. In base, export from the UDS panel.
2. Record the export directory and close Duckov normally.
3. Provide the observed values/states and export directory to the implementation task for read-only verification.

Expected result: Overview, Economy, Diagnostics, the schema-15 profile, `statistics.json`, and `economy_holdings.csv` agree on generation, state, value, timestamp/freshness, capabilities, and conditional liquid wealth. Unavailable CSV values are blank. The export contains exactly 31 files: `statistics.json` plus 30 CSVs. Shutdown leaves no `.tmp`, deploying, or previous-deployment residue and no UDS error in `Player.log`.

## Result record

Record these fields for each step:

- Duckov slot and visible UDS generation ID;
- Money state/value;
- Cash state/value;
- liquid-wealth state/value;
- ATM direction and amount, if used;
- M15 capability states;
- export directory;
- clean-shutdown result and any UDS warning/error.

Do not modify Duckov save files or UDS profile JSON by hand during this procedure.

## 2026-08-29 qualification result

Result: **PASS for M15 current economy holdings.** The player performed every launch, slot selection, ATM action, inventory transfer, menu transition, export, and shutdown. Codex performed only read-only inspection of screenshots, the external UDS profile/export files, and `Player.log`.

- Candidate: `v0.15.0` feature head `02a99731f19955c50481c8e70ecf54574c13afa5`; Duckov slot `1`; UDS generation `d78609c23dd341ac9ca4265c7f19e337`.
- Cold base hydration showed Money `98,959 (current)`, owned Cash `0 (current)`, liquid wealth `98,959 (current)`, and all three M15 capabilities `Supported`. Duckov's ATM independently showed account `98,959` and physical Cash `0`.
- Withdrew one Money through the ATM. Duckov and UDS agreed on Money `98,958`, owned Cash `1`, and unchanged liquid wealth `98,959`; the M9 projection remained visibly separate.
- Moved that Cash from main inventory to PlayerStorage and then to PetProxy. Money, Cash, liquid wealth, and the then-current M9 totals remained unchanged across both internal transfers.
- Returning normally to the main menu changed Money and Cash to timestamped `LastObserved` evidence and made liquid wealth unavailable. Profile and backup were byte-identical after clean shutdown, no UDS temporary residue remained, and no UDS warning/error appeared in `Player.log`.
- A clean restart showed Money `98,958` and Cash `1` as `LastObserved` before slot hydration. Loading the same slot restored both components and liquid wealth to `Current` without adding a startup M9 flow; the Cash retained in PetProxy was included.
- Export `20260828T2232404074617Z-d78609c23dd341ac9ca4265c7f19e337` contained exactly 31 files. Schema-15 `statistics.json`, `economy_holdings.csv`, and the live profile agreed on generation, `Current` states, supported capabilities, Money `98,958`, Cash `1`, liquid wealth `98,959`, timestamps, provenance, historical unavailability, and no repair. The export JSON SHA-256 was `c5939fddda9155c9fa8c797fc28c12fb4e20fd4d9cfe52415b03d1a19c481d69`; the holdings CSV SHA-256 was `aadc2410849380b9a122f9dcf2754f3ef5ce25b3ca7c3b9cd8041d3603be4f97`.
- Final shutdown again closed the generation cleanly. Primary and backup were byte-identical at SHA-256 `4284c397b3f51c72233b7b5985eabdb85cd874a1df2059b74142f1fe2d36b916`, with zero UDS temporary residue and no UDS error-like log line.

The main-menu step also exposed a separate M9 defect: old PetProxy teardown occurred after `SceneLoader.onStartedLoadingScene` but before the next `LevelManager.OnLevelBeginInitializing`, so adapter `public-events-v11` observed owned Cash `1 -> 0` as a false Base outflow. M15 itself remained correct and retained Cash `1`. Adapter `public-events-v12` now flushes legitimate pending changes and suspends the M9 Cash baseline at the earlier verified scene-loading event; one production-adapter regression reproduces the exact base-to-menu pet teardown, and a companion proves a legitimate pre-transition Cash delta is still published. The already-completed M15 gameplay sequence does not need to be repeated for that bounded M9 lifecycle correction.

At the test endpoint, the user-controlled save intentionally contained Money `98,958` and one physical Cash in PetProxy.

A later production-composition review found a save-switch ordering defect not exercised by this single-slot live matrix: native `EconomyManager` can load slot B before the UDS `OnSetFile` subscriber begins its queued transition, allowing a transition flusher to read B while slot A's generation is still open. Adapter `authoritative-roots-v3` now enters a holdings-only profile-change gate before that flusher. `NativeMoneyLoadBeforeUdsSaveSwitchCannotCrossContaminatePriorGeneration` initializes the actual production coordinator and holdings adapter after a native-first subscriber, reproduces the prior primary-profile corruption (`111` expected, `222` persisted), and proves that slot A primary/backup retain `111`, slot B is unavailable before its matching `ProfileChanged`, and slot B then persists current `222`. This deterministic save/persistence regression requires no additional user-controlled gameplay or save manipulation.

A subsequent same-schema recovery correction restores the checked-overflow boundary around crafting validation. Its production `AtomicJsonStore<ProfileDocument>.Load` regression proves that an overflowing schema-15 crafting primary is rejected and an intact backup is selected. It does not affect the native holdings contract or require additional user-controlled gameplay or save manipulation.

A later reset correction covers a clean save before its first native `EconomyData` collection. After `OnMoneyChanged` proves current Money, the actual UDS reset transition now carries that trust only for the same exact manager instance into the new UDS generation; the new primary and backup immediately persist the unchanged value as `Current`. Save-slot switching retains its stricter replacement/hydration gate. This deterministic UDS-owned transition does not require additional user-controlled gameplay or Duckov save manipulation.
