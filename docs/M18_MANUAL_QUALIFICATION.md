# M18 manual qualification

Use the candidate source and file identities in [M18 acceptance](M18_ACCEPTANCE.md). Automated isolated recovery/reset tests are complete only when that record says Pass. They do not prove native gameplay, rendering or frame-time acceptance.

## First native check

1. Launch Duckov yourself. Confirm HarmonyLib and exactly one UDS instance are enabled before selecting your chosen save. The new `v1` directory should begin with a fresh UDS generation; old development profiles remain untouched.
2. Outside a raid, open Statistics from the main menu, base pause menu and F8. Visit all nine tabs, change language, resize/change resolution, scroll wide tabs and long content, and verify Back/Escape, keyboard focus and the native pause menu after closing. Repeat open/close and a menu round-trip. In a raid, the unavailable-access message is expected.
3. On the selected save, perform a short ordinary sequence: one successful consumable use and healing where available, firing and an observable enemy outcome, container access, equip/unequip, a paid craft and economy change, and a completed sleep. Keep exact action notes. Do not add unsupported actions just to fill a checklist.
4. Complete an expedition with at least three map visits, including a repeated map if naturally available. For delayed effects, record the source, any loadout swap and the later outcome when such an item is actually available. Extract, inspect Runs and the relevant tabs, then export through Diagnostics.
5. Close the game normally yourself. Codex can read the local logs, generation, checkpoints, profile/backup and exports, compare JSON/CSV/UI against the action notes, and verify clean shutdown and exact deployed hashes.

## Persistence, degradation and ownership

- Cold reopen the same current-format profile and verify exact totals/generation. Reinstalling the same package while closed must preserve those totals. A second user-selected save gets its own generation and cannot borrow data.
- Exercise reset only in an explicitly chosen disposable UDS test generation. Confirmation defaults to Cancel; successful reset archives only UDS data, and export/reset cannot run twice concurrently. Real save deletion/reuse, a forced process termination or real-profile reset requires a separate deliberate user choice. Isolated fixtures cover primary/backup/temporary corruption, interrupted writes, generation handoffs, blocked checkpoint/profile writes and rollback without touching real saves.
- Missing Harmony and foreign conflicts should leave affected capabilities unavailable and supported siblings operable. Test only a reversible user-selected setup, with a cold launch and exact mod list recorded. Do not introduce an arbitrary conflicting mod into controlled performance captures.
- Check repeated native setup/deactivation and scene/profile changes for duplicate subscriptions, UI roots, input blockers and repeated errors. Managed shell tests cover ownership at isolated boundaries; native object/resource behavior remains a separate observation.

## Performance session

The matrix and rules are frozen in [PERFORMANCE.md](../PERFORMANCE.md) and [M18_CAPTURE_MATRIX.json](M18_CAPTURE_MATRIX.json). The prepared `campaign.json` binds the matrix to the reproducible ordinary candidate's DLLs. Each cell needs three valid Harmony-only B captures and three candidate D captures. Confirm the exact retained weapons, equipment, save/location and graphical controls before the first capture; earlier M8.1 inventory notes are not current inventory evidence.

Copy [the controls template](M18_CONTROLS.template.json) to the local campaign directory and fill every material field. Use `capture-m18.ps1 -CampaignPath <campaign.json> -ControlsPath <controls.json> -Configuration B -Scenario idle -Run 1 -DuckovPath $env:DUCKOV_PATH -ValidateOnly` for preflight; omit `-ValidateOnly` only when the user is ready to capture. This does not launch or control Duckov. For non-weapon cells record the actual equipped weapon/ammunition and explicitly state zero expected shots.

After the matched matrix, preserve one untrimmed natural multi-map/high-history soak with UTC start/stop evidence and named early/middle/late windows. Record retained run count, segment/item/loadout cardinalities, profile size and memory/resource observations. Repeat the base UI/export cells on the progressed v1 history. No synthetic profile is injected into real gameplay. If sufficient history or a reproducible target is unavailable, keep that gate Not exercised and state the concrete limitation.

During every controlled capture the agent suspends builds, tests, indexing and other work. Resume analysis only after the user reports capture completion. Analyze approved valid raw inputs with the existing analyzer and `--campaign <campaign.json> --baseline B`; preserve all other attempts with objective invalidation reasons. A changed DLL requires a new frozen campaign and affected requalification.
