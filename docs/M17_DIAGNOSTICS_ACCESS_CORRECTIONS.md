# M17 Diagnostics feedback, grouping and base access

This follow-up addresses the reported copy feedback, repeated Recent issues, initial expansion and F8 failure in base. Deployment remains deferred at the user's request.

## Diagnostics behavior

- Copy data-folder and export-location actions show a nearby tooltip inside the retained panel for 2.5 seconds of unscaled time. Success requires clipboard write/readback confirmation; failure receives different feedback. No full path is included in the tooltip. The overlay does not intercept input and clears on tab hiding, generation loss or destruction.
- Recent issues remain capped at 12 displayed entries. They are now grouped before that cap, using severity plus the displayed issue category and guidance. Repeated persistence reports therefore form one entry even across adjacent seconds. The entry displays the latest report timestamp and the number of matching reports in the newest 50 log records. Counts describe reports, not proven distinct incidents. The technical log retains each individual record, including different underlying messages grouped under one guidance category.
- All left-side accordions begin collapsed except Technical details itself. This includes every Recent issue and the Recovery, Limitations and Recent bounded diagnostics subsections. Same-generation refresh preserves deliberate expansion; changed/lost generations reset it.

## F8 investigation and correction

The reported unpaused-base toast is corroborated by Player.log: the Hotkey shell failed because `Canvas/MainMenuContainer/Menu/OptionsPanel/Text (TMP)` was missing. The main-menu scene is unloaded during entry into a save. That failure is independent of the previously corrected premature pause-menu button geometry check.

Read-only inspection of the installed resources found the corresponding heading under the live `PauseMenu` component at relative path `OptionsPanel/Text (TMP)`. Serialized TMP object 112960 references the same `ResourceHanRoundedCN-Medium SDF` font and `ResourceHanRoundedCN-Medium Atlas Material Shadow` material used by the main-menu source. The resolver now uses this verified source when the main-menu heading is absent. Resolving relative to `PauseMenu.Instance` avoids dependence on scene-root or prefab-clone names and works while its Options panel is inactive.

Native inspection and captured shell-failure evidence are kept in the ignored `artifacts/m17-diagnostics-access-20260907/` directory. The code compiles against the installed game. Manual verification must still exercise F8 with the base pause menu both closed and open, native-menu activation, close/reopen, and the copy tooltip while paused. No gameplay, profile mutation or deployment is performed for this follow-up.

## Automated verification

The complete Debug and Release suites each passed **1846/1846**, with no failures or skips. The installed-native Release build passed with zero warnings/errors. Added grouping tests cover cross-second persistence reports, the latest timestamp, stable expansion across new reports, unchanged technical records and separation by severity/guidance. Existing expansion tests now verify collapsed left-side defaults and preservation of deliberate changes.
