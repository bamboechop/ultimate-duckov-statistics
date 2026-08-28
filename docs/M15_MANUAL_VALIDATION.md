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
