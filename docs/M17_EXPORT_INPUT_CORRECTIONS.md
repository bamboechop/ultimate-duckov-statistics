# M17 export and native input corrections

Implementation commit: `8b7d0ea`. Locally packaged and deployed on 2026-09-07.

## Changes

- Equipment CSV durations use invariant decimal formatting. The prior `R` format throws `FormatException` under Duckov's Mono runtime even though the .NET 8 test runner accepts it. All six decimal formatting sites were corrected, including empty slot rows. Floating-point metrics retain their round-trip formatting.
- Diagnostics displays a foreground red/green result banner at the bottom left, with the two-line export messages from the mockups. Clipboard failure uses an orange result. The banner expires using unscaled time, and new shell instances do not replay old completion notices. Export task failures now retain the exception stack in technical diagnostics.
- An open panel owns a dedicated source registered with `InputManager.DisableInput`, the same API used by `Duckov.UI.View.OnOpen`. Close, failed construction, profile changes, and disposal release only UDS's source. The native API retains its input-reactivation cooldown and other menus' blockers. Native cancel events are consumed while the panel handles Escape.
- Pause-menu buttons retain their native root `ButtonAnimation` and safe toggle dependencies, including `SizeDeltaToggle`. Original action behaviours and click listeners remain removed. Activating the pause-menu entry while a hotkey shell is already open recreates it on the pause-menu canvas, instead of leaving it behind that menu.

## Verified evidence

- Installed Duckov 2.3.30 native contracts inspected: `View`, `InputManager`, `UIInputManager`, `PauseMenu`, `ButtonAnimation`, `SizeDeltaToggle`, and the native pause-menu prefab.
- An isolated host using the installed `mono-2.0-bdwgc.dll` reproduced the original decimal-format exception and completed a full synthetic profile export using the corrected core assembly. Equipment and recurring-loadout decimal fractions remained intact. No game process or user profile was used by this probe.
- Debug and Release: 1846/1846 tests passed. Native Release build: zero warnings/errors. Installed compatibility probe passed.
- Five-file package and ZIP validated. Each installed file hash matched its package source and ZIP entry. Duckov was closed during deployment.
- ZIP: `UltimateDuckovStatistics-v0.17.0.zip`, 661944 bytes, SHA-256 `c4172b674723844130f305d1e4da4ca4d40cdcba4f685342ed630df8fdd3215e`.
- Native DLL SHA-256: `661e1ae1c29ae85fa76998425554c3e07f70014366ffc213dd31a950b0398d63`.
- Core DLL SHA-256: `870efcfa8700a5ac20c7ec1822b4fa5ee8780e9a0528a07819846ec4ce049778`.

Local probe sources and logs are retained under ignored `artifacts/m17-export-input/`.

## User-owned runtime checks

Confirm export and its result banner, base F8 input blocking and restoration after close, and pause-menu Statistics hover/click/open behaviour. Automated and isolated-runtime checks do not establish in-game visual or input acceptance.

## Follow-up: Overview refresh flicker

On 2026-09-07 the user confirmed the reported export/input/menu fixes work, then reported periodic flicker of Overview's Latest run button. Commit `ddd9c7b` retains that button and its highlight across live projection refreshes instead of destroying and recreating them. Its presentation and exact generation/run callback still refresh; feedback components are attached once.

Release tests passed 1846/1846, the native build completed without warnings/errors, and the installed compatibility probe and package validation passed. Deployed with Duckov closed; all five installed files matched the package and ZIP hashes. ZIP SHA-256: `1a59d00b81568a533840fec545287677dfe28b5fd9ad4a5f76e86ec6b9219ed6`. Native DLL SHA-256: `86864e50d700313c28b4dc982de3e8542922b67b8f37bae1293ce3ee37184f43`.

The remaining manual check is button stability across several live refreshes, including hover, keyboard focus, and opening the latest run. Local build/deployment logs are under ignored `artifacts/m17-overview-refresh/`.

## Follow-up: matching button highlight outlines

Commit `181a081` replaces the shared highlight's fixed radius of 10 with the button background's own radius. The existing feedback component synchronizes subsequent layout changes without restarting the tint, so pill buttons and less-rounded controls retain matching hover, press, and keyboard-focus outlines.

Packaged and deployed on 2026-09-07 with Duckov closed. Release tests passed 1846/1846; native build, compatibility probe, and package checks passed. All five installed files matched their package and ZIP entries. ZIP SHA-256: `77fa59e1007a9ec8c4071bb1fef0f3d100c6e3df5c8e98b2551deddff3c79212`. Native DLL SHA-256: `21e101d787a73cddbdd86be6e0cd06fa6b424a6dccdf6e524386c196da869627`. Local logs: ignored `artifacts/m17-button-shape/`. In-game outline acceptance remains user-owned.

## Follow-up: backdrop, quick stats, and recent-run layout

Commit `37f76c1` raises the black shell backdrop to 75%, puts Economy metric values above uppercase muted labels, and applies value-first ordering to all Item Use stat cards. Economy and Item Use recent-run headers align their chevron, badge, and map title; View run appears at the bottom right of the expanded body, outside the header's click target.

The installed item 1181 (`Candy`, displayed as Lolli in the user's profile) has native energy +5, water -3, and AddBuff. UDS previously treated negative resource changes as Food/Drink effects. New observations require positive energy/hydration for those tags. Existing historical tags are retained; another use refreshes the lifetime item entry through the existing reducer.

Packaged and deployed on 2026-09-07 with Duckov closed. Focused tests passed 88/88 and full Release tests 1846/1846. Native build had zero warnings/errors; compatibility and package checks passed. All five installed files matched package and ZIP hashes. ZIP SHA-256: `20456033a412de191a0ba44a69991b1f42dcb8b08c489d02fcd61c3b2afd9231`. Native DLL SHA-256: `290d26e533ff0359e7da560f46cf5b989e34723416b07ab2aa50e15a13de4bee`. Local native inspection/build/deployment evidence: ignored `artifacts/m17-layout-polish/`. In-game visual acceptance remains user-owned.
