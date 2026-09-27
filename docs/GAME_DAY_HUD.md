# Game-day HUD

UDS 1.4.0 adds a non-interactive day label under the native top-left weather label: `Day 30` in English and `Tag 30` in German. Other languages use the existing English fallback. The label uses the native text template, weather font/material/color and 24-point size. It follows the native HUD's visibility and layout; it does not change the weather, clock, storm display or compass.

## Source and ownership

The number is the loaded save's zero-based `GameClock.Day + 1`. It is independent of the UDS world-time aggregates, installation date and statistics resets. No profile fields, schema changes, saved-value reads, writes or additional Harmony patches are required.

The inspected Duckov 2.3.30 clock assigns `GameClock.Instance`, loads its saved day during `Awake`, and then emits `OnGameClockStep`. The HUD trusts an already-loaded clock on UDS activation. After a Duckov save selection or deletion it hides the outgoing value until a different live clock emits that event, preventing an old save's day or the native missing-clock fallback zero from becoming a false `Day 1`.

The UDS owner subscribes to native clock, language, scene-loading, level-initialization and save-selection/deletion events. HUD discovery runs only for a bounded ten attempts, at half-second intervals, after activation or a relevant lifecycle change. Once bound, the owner checks its references and day twice per second. Clock callbacks only notice clock replacement; ordinary clock steps do not search the scene or format text. The caption is formatted when its day or language changes, and assigned only if its text changed. Language refresh happens after the native localization event completes, so UDS's overrides are ready regardless of subscription order.

Each native `TimeOfDayDisplay` owns one child label through the UDS owner. Missing HUD anchors, absent/destroyed clocks and invalid day values produce no label. Destroyed or replaced HUDs are rebound. Scene loading freezes the existing label until its native HUD is hidden or destroyed: Duckov publishes the loading-start event before starting its black-screen fade, so removing the label at that event would shift the weather layout while it is still visible. Save changes/deletion and UDS disposal still remove labels immediately. Disabling UDS, quitting or disposing it removes its labels and static subscriptions. Template shadows follow their native object lifetime; UDS does not change shared TrueShadow settings or instantiate private font materials. A label failure is reported once and does not change statistic capabilities.

## Coexistence

Disable the standalone **Show Game Days** mod before checking this feature in game. Keeping both enabled produces duplicate labels: UDS deliberately does not remove the other mod's label, disable its settings or alter its patches. The installed mod was inspected only as a positioning reference; UDS's implementation is independent.

## Validation

`NativeGameDayHudTests` executes the production owner and its native lifecycle boundaries against test-only Unity/TMP/event stubs. It covers existing saved days; `Day 1` / `Tag 1`; English/German switching; midnight and sleep jumps; unavailable/replaced clocks; save selection and deletion; scene transitions; bounded discovery without a HUD; hidden/recreated labels; invalid values; contained construction failure; cleanup; and reactivation. Steady-state tests assert no new text assignments or scene searches.

The installed-assembly contract probe checks the native weather anchor in addition to its existing clock, scene, level, localization and text-template contracts. Automated boundaries do not prove native glyph rendering, layout, shadow appearance or frame-time impact.

Local validation on 2026-09-27 passed: 2,519 core tests and 144 shell tests in both Debug and Release (including 12 HUD cases), native Debug/Release builds with no warnings or errors, the installed contract probe, changed-source formatting, and the ordinary 13-file package audit. These are automated checks. On 2026-09-27, the user accepted the label appearance and then confirmed the corrected base-departure fade and arrival behavior in the deployed build. The correction retains the existing label until its native HUD disappears, preventing the early weather-layout shift. Save switching, live language switching, sleep/midnight and reactivation remain separate, unconfirmed native checks.

Native acceptance checklist:

1. **Accepted:** label appearance and the corrected base-departure fade/arrival behavior. Keep **Show Game Days** disabled when checking UDS alone to avoid duplicate labels.
2. Change between English and German while the HUD exists: the same label must become `Day N` / `Tag N`. Close and reopen any UI that hides the HUD and check it returns once.
3. Sleep across a day and, when convenient, observe midnight. The number should follow the saved game's day within half a second, without requiring a UDS panel open.
4. Transition between base and raid and select another save with a different day. Check that no old-save value flashes during loading and that the new day appears without duplicates. Existing statistics should stay unchanged by merely displaying the HUD.
5. Disable/re-enable UDS through the game's supported mod flow, including its restart requirement where applicable. Check that the label disappears and returns once. After closing the game, inspect `Player.log` for a game-day HUD warning or exception.
