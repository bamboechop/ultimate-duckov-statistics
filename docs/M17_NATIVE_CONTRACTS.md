# M17 installed native UI contracts

This document records the M17 UI contract audited from the installed Escape From Duckov baseline before native menu, localization, focus, icon, and feedback integration was implemented. It is version-specific evidence, not a promise that a later Duckov build retains the same hierarchy or members.

## Audited baseline

- Escape From Duckov `2.3.30`, Steam build `24013657`
- Unity `2022.3.62f2`
- HarmonyLib `2.4.1.0`
- `TeamSoda.Duckov.Core.dll` SHA-256 `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`
- `ItemStatsSystem.dll` SHA-256 `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60`
- `SodaLocalization.dll` SHA-256 `f3af2174321f1193e8eea169b4d6050c5819efc927cf3ad42cde19aa54758c7c`
- `resources.assets` SHA-256 `93c4ab6ad71fdb3bf4a331bbb2ac6bc2f7db7b0f12efe60dd43ea019ab2e543d`

The executable contract probe also verifies the separately installed Harmony assembly and all earlier M0-M16 native contracts. M17 adds no Harmony target and no persisted statistics member; the profile remains schema 16.

## Menu lifecycle and access boundary

`MainMenu.OnMainMenuAwake` and `MainMenu.OnMainMenuDestroy` are public static `Action` fields. `PauseMenu.onPauseMenuOn` and `PauseMenu.onPauseMenuOff` are public static events; `PauseMenu.Instance` and `PauseMenu.Shown` expose the current instance and visibility. `LevelManager.Instance.IsBaseLevel` and `LevelManager.Instance.IsRaidMap` are the installed location-state evidence.

UDS subscribes once during panel initialization and unsubscribes on disposal. The installed `MainMenu` component is a lifecycle marker rather than a guaranteed ancestor of the native menu canvas, so main-menu attachment searches the roots of that marker's loaded scene and is retried for an already-created menu and on every later menu-awake signal. Pause attachment remains scoped to the pause-menu hierarchy and is permitted only while the installed level manager proves base. Closing or destroying the source menu closes the one shared panel instance. Raid access remains rejected independently at the panel boundary, so a stale or unexpected menu callback cannot expose the panel during gameplay.

The installed menus do not expose a stable public mod-entry registration API. UDS therefore uses a narrow hierarchy integration:

1. Find a native `UnityEngine.UI.Button` carrying the exact installed localization key `MainMenu_Settings`, `MainMenu_MODs`, or `UI_Menu_Options`.
2. Refuse an ambiguous pause-menu tie.
3. If an exact key is absent, accept only a bounded Settings, Options, or Mods hierarchy-name score.
4. Clone the proven live native button so its actual menu layout, raycast geometry, and interaction styling are retained; remove inherited non-presentation behaviours, replace its click event after activation, replace its icon and label, and place it beside the anchor.

The injected button disables and removes every inherited non-presentation `MonoBehaviour`, then replaces the cloned button's click event after the clone's activation callbacks have run. This prevents the source Settings/Options action from being reattached during activation while preserving Unity graphics, layout, localization, masking, mesh effects, and the primary `Button` transition. The clone remains a runtime instance of the user's installed asset; UDS does not redistribute it. The generated statistics icon is a private runtime texture. Attachment alone is reported as unverified/limited until the replacement click callback is actually observed. Failure is fail-open for tracking and fail-closed for that access path: UDS records an actionable warning, leaves F8 available outside raids, and does not guess another insertion point.

## Native controls, focus, and feedback

`GameplayDataSettings.UIPrefabs.Button` and `.ScrollRect` are public native prefab references. `GameplayDataSettings.UIStyle.TemplateTextUGUI` exposes Duckov's installed TextMeshPro font and material through a public template, and `CanvasScalerController` proves that the installed screen-space UI responds to resolution changes through its `CanvasScaler`. `GameplayDataSettings.UIStyle.FallbackItemIcon` is the installed generic icon. `GameManager.EventSystem` exposes the active Unity event system. `Duckov.UI.NotificationText.Push(string)` is the installed transient-feedback path.

The menu entry uses the live native button presentation. Visual-correction Gate 1 replaces the visible immediate-mode panel with one retained Unity UI hierarchy attached to a proven active screen-space Duckov canvas. The shell copies presentation assets from the public native button reference, copies scroll motion settings from the public native scroll reference, and assigns the installed TextMeshPro font and material to mod-owned text components. It packages no font, sprite, background image, or other visual asset. A dark near-full-screen frame, prominent title, close/back control, exact nine-tab strip, selected/hover/pressed states, and a bounded placeholder content host are created once per opening and destroyed on close.

The prior immediate-mode view remains source-only as a Gate 2 porting reference; `ModBehaviour` no longer has an `OnGUI` callback and that renderer cannot be presented. Opening captures cursor visibility, cursor-lock state, and the event system's selected object. Closing restores those values if the objects still exist. Reset and export implementations remain in the legacy source for the subsequent body port; Gate 1 deliberately exposes neither operation through its placeholder content host.

## Localization

`SodaCraft.Localizations.LocalizationManager` exposes public static `SetOverrideText`, `RemoveOverrideText`, and `GetPlainText` members. `TextLocalizor.Key` is public. UDS registers every panel fallback under the private `ultimate-duckov-statistics.` namespace, resolves text through Duckov's localization manager, and removes the overrides on disposal. Missing, blank, or unresolved native text returns to the checked-in English fallback. Menu labels use the same key path as panel text.

The contract proves the localization mechanism and exact anchor keys; it does not claim that Duckov supplies third-party translations. English remains complete when no translated override exists.

## Item metadata and icons

`ItemAssetsCollection.GetMetaData(int)` returns metadata whose public `icon` member is a Unity `Sprite`. UDS resolves only numeric stable identities in the canonical `duckov:item:<id>` form. Unknown namespaces, missing metadata, absent sprites, and modded identities use `GameplayDataSettings.UIStyle.FallbackItemIcon`; if even that installed fallback is unavailable, the panel draws a deterministic `?` placeholder.

The cache holds at most 512 stable identity results and clears as a unit at the bound. Display names and sprites never become persisted identity and never alter statistics semantics.

## Projection, scaling, and performance boundary

The UI projection is built only when the active profile generation, statistics generation, and coordinator generation agree exactly. It is cached by generation plus profile revision. A failure to prove that relationship shows the localized unavailable response and never substitutes another profile or an invented zero state.

The panel has one exact navigation sequence: Overview, Runs, Records, Combat, Equipment, Economy, Crafting, Item Use, Diagnostics. Gate 1 stretches the retained shell within physical-pixel margins of the active native canvas. Control dimensions are normalized by that canvas's scale factor, so the tab strip remains readable rather than shrinking to the canvas reference resolution. The tabs stay on one row, horizontally scroll when their physical width exceeds the viewport, and keyboard movement scrolls the selected tab back into view. The retained content host already clips and bounds its region; Gate 2 will reconnect the existing multi-column and bounded-history projection policies when view bodies are ported.

Projection preserves supported zero, proven empty, unavailable, unknown/modded, partial-history, and last-observed states. Economy holdings remain separate from flows; weapon-ammunition percentages use only the selected weapon's correlated actions; reciprocal crafting views derive from the canonical M16 output/recipe/resource associations. No reverse index, UI state, icon, or localized label is persisted.

## Failure and cleanup boundary

Menu/localization/icon/toast/shell-attachment failures are UI diagnostics. They do not disable unrelated adapters, rewrite a capability as zero, or block F8 outside raids. A shell attachment fails closed when no supported active Duckov screen-space canvas or installed font template can be proven. Repeated open requests cannot create a second active shell. Closing first deactivates and then destroys the one UDS-owned hierarchy. Final disposal unsubscribes all four lifecycle signals, destroys only UDS-created buttons, shell objects, and menu-icon runtime objects, removes only UDS localization overrides, clears the native resolver, and restores panel focus/cursor state.

## Reproduce the executable audit

With `DUCKOV_PATH` set to the installed game root:

```powershell
dotnet run --project .\tools\DuckovContractProbe\DuckovContractProbe.csproj -c Release -- $env:DUCKOV_PATH
```

The probe reads managed metadata and installed asset/version fingerprints. It does not launch Duckov, select a save, change gameplay, deploy a mod, or modify a Duckov save.

Runtime hierarchy placement, native input feel, long localized labels, menu recreation, 2560x1440 and 1024x768 layout, and screenshot agreement remain user-controlled checks in [M17_MANUAL_VALIDATION.md](M17_MANUAL_VALIDATION.md).
