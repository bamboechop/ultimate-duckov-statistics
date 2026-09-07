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
