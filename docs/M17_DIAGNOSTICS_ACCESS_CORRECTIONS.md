# M17 Diagnostics feedback, grouping and base access

This follow-up addresses the reported copy feedback, repeated Recent issues, initial expansion and F8 failure in base. Deployment was initially deferred, then completed on the user's request as recorded below.

## Diagnostics behavior

- Copy data-folder and export-location actions show a nearby tooltip inside the retained panel for 2.5 seconds of unscaled time. Success requires clipboard write/readback confirmation; failure receives different feedback. No full path is included in the tooltip. The overlay does not intercept input and clears on tab hiding, generation loss or destruction.
- Recent issues remain capped at 12 displayed entries. They are now grouped before that cap, using severity plus the displayed issue category and guidance. Repeated persistence reports therefore form one entry even across adjacent seconds. The entry displays the latest report timestamp and the number of matching reports in the newest 50 log records. Counts describe reports, not proven distinct incidents. The technical log retains each individual record, including different underlying messages grouped under one guidance category.
- All left-side accordions begin collapsed except Technical details itself. This includes every Recent issue and the Recovery, Limitations and Recent bounded diagnostics subsections. Same-generation refresh preserves deliberate expansion; changed/lost generations reset it.

## F8 investigation and correction

The reported unpaused-base toast is corroborated by Player.log: the Hotkey shell failed because `Canvas/MainMenuContainer/Menu/OptionsPanel/Text (TMP)` was missing. The main-menu scene is unloaded during entry into a save. That failure is independent of the previously corrected premature pause-menu button geometry check.

Read-only inspection of the installed resources found the corresponding heading under the live `PauseMenu` component at relative path `OptionsPanel/Text (TMP)`. Serialized TMP object 112960 references the same `ResourceHanRoundedCN-Medium SDF` font and `ResourceHanRoundedCN-Medium Atlas Material Shadow` material used by the main-menu source. The resolver now uses this verified source when the main-menu heading is absent. Resolving relative to `PauseMenu.Instance` avoids dependence on scene-root or prefab-clone names and works while its Options panel is inactive.

Native inspection and captured shell-failure evidence are kept in the ignored `artifacts/m17-diagnostics-access-20260907/` directory. The code compiles against the installed game. Manual verification must still exercise F8 with the base pause menu both closed and open, native-menu activation, close/reopen, and the copy tooltip while paused. No gameplay or profile mutation was performed by the agent.

## Automated verification

The complete Debug and Release suites each passed **1846/1846**, with no failures or skips. The installed-native Release build passed with zero warnings/errors. Added grouping tests cover cross-second persistence reports, the latest timestamp, stable expansion across new reports, unchanged technical records and separation by severity/guidance. Existing expansion tests now verify collapsed left-side defaults and preservation of deliberate changes.

## Combined local deployment, 2026-09-07

Revision `153c84a4252feaafce0c4834dc3921f395b1523b`, including the preceding effect-filter and pause-menu fixes, was built and transactionally deployed after checking Duckov was closed. The release workflow passed 1846/1846 tests, the installed compatibility probe, native/tool builds and five-file package validation. Every installed file and ZIP entry matched its package SHA-256. No publication was performed; in-game acceptance remains user-controlled.

- Archive: `artifacts/release/UltimateDuckovStatistics-v0.17.0.zip`, **661185 bytes**.
- ZIP SHA-256: `920c5f87e192deedef3fa202e22b17fd9b5f77018349832906f20f55a7104324`.
- Native DLL SHA-256: `fd9ac407ebcf88bf9401d83acc46c90772806ba42738e80347bdd35cfb6b03ad`.
- Core DLL SHA-256: `3f17bb18a312fdd9ced4993b1175f6c2ec1f67e5e443383739083d387216f7ba`.
- Destination: `E:\SteamLibrary\steamapps\common\Escape from Duckov\Duckov_Data\Mods\UltimateDuckovStatistics`.

Build/deployment logs and the complete installed hash manifest are retained under the ignored `artifacts/m17-combined-deployment-20260907/` directory.
