# M12 installed native contracts

This audit is the implementation basis for M12 world-time and sleep statistics. It applies to the locally installed Escape from Duckov build inspected on 2026-08-20.

## Verified installation

- Duckov version: `2.3.30`
- Steam build: `24013657`
- Unity: `2022.3.62f2`
- `TeamSoda.Duckov.Core.dll` SHA-256: `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`
- `ItemStatsSystem.dll` SHA-256: `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60`
- HarmonyLib `2.4.1.0` SHA-256: `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6`

The repository contract probe verifies the public/static clock members, the exact internal sleep advancement signature, the sleep completion callback field, and the native day constant against these installed assemblies.

## World clock

`GameClock` owns `long days` and `double secondsOfDay`. Its private `SecondsPerDay` constant is exactly `86,300.0`, not 86,400. Ordinary `Update` calls `StepTime(Time.deltaTime * clockTimeScale)`; the public serialized field has a code initializer of 60, but UDS neither assumes nor multiplies by that value. `StepTime` adds the actual runtime delta, repeatedly advances `days` while `secondsOfDay > 86,300`, and then invokes public static `GameClock.OnGameClockStep` on every update rather than once per in-game minute. UDS coalesces those exact coordinate deltas into the current in-memory profile at a bounded one-second publication cadence, so consecutive UI snapshots need not differ by exactly 60 seconds when their publication windows are not aligned or Duckov pauses/scales `Time.deltaTime`.

`GameClock.Load` hydrates `days`, `secondsOfDay`, and real-time-played state from the active Duckov save and invokes the same `OnGameClockStep` callback. UDS therefore establishes or replaces a generation-local baseline without counting the first observation. It never treats the callback alone as elapsed time.

Public `GameClock.Day` and `GameClock.TimeOfDay` expose the two clock components. UDS computes an observed coordinate as `Day * 86,300 seconds + TimeOfDay.Ticks`, with checked `Int64` arithmetic. `GameClock.Now` is not used for deltas because it composes `TimeOfDay + TimeSpan.FromDays(Day)`, whose 86,400-second day would introduce a false 100-second discontinuity at each native midnight.

Public `StepTimeTil(TimeSpan)` always requests a forward delta. When the target is later on the current day, it requests `target - TimeOfDay`. Otherwise it requests `target + TimeSpan.FromDays(1) - TimeOfDay`. That second calculation uses .NET's 86,400-second day even though `StepTime` wraps Duckov's clock after 86,300 seconds, so a next-day request can land 100 native seconds later than its nominal target. UDS never substitutes the requested target: it counts the actual checked native coordinate reported after the step. Installed callers are:

- `LevelManager.OnNewBoot`, which advances to 07:00;
- `SetTimeOnEnterBaseFirstTime.Start`, which performs the one-time base clock initialization;
- `TimeOfDayController.Start`, when a level config forces a time;
- `TimeOfDayConfig.InvokeDebug`, the developer time/weather action.

These proven forward changes contribute to total observed in-game elapsed time and to calendar-day advancement when they cross native days. They are not sleep. No installed production path sets the `days` or `secondsOfDay` fields backward. A backward or invalid observation therefore fails closed, preserves prior accepted totals, re-baselines, and disables only current clock-derived capture with a visible diagnostic.

## Sleep lifecycle

`Duckov.UI.SleepView` exposes no sleep-start or cancellation event. Opening and closing the view before confirmation changes no clock state and emits no completion. Confirmation calls private async `Sleep(float minuts)`. The installed sequence is:

1. reject an overlapping call when the private `sleeping` flag is already true;
2. set `sleeping = true` and convert the selected minutes to seconds;
3. await the black-screen transition;
4. call internal static `GameClock.Step(float seconds)`;
5. await 0.5 real-time seconds;
6. invoke public static `Action SleepView.OnAfterSleep`;
7. close the view when still active, hide the black screen, and clear `sleeping`.

The complete installed assembly has one caller of `GameClock.Step(float)`: `SleepView.Sleep`. UDS applies one narrowly scoped prefix/postfix patch to that exact `void GameClock.Step(System.Single)` method. The prefix captures the active UDS generation, requested tick duration, and checked native clock coordinate. The postfix accepts a pending sleep candidate only when the actual native coordinate change matches the exact argument within the two-tick rounding boundary. The later `OnAfterSleep` callback consumes that candidate once and only then increments the completed-session count and sleep-advanced time.

Ordinary clock progression continues during the 0.5-second delay after the sleep jump. It contributes to total observed time through `OnGameClockStep` but cannot inflate sleep-advanced time because the sleep candidate was fixed at the exact `GameClock.Step` return boundary.

There is no native sleep-cancel or failure callback after the clock mutation. A view closed before confirmation has no candidate; an interrupted async operation, teardown, save-generation change, missing patch, mismatched delta, completion without a candidate, duplicate completion, or overlapping candidate adds no completed session and no sleep time. Pending candidates are not persisted, so a process interruption after the clock changed but before native completion retains the observed clock advancement without fabricating a completed sleep.

## Load, save, slot, and lifecycle boundaries

`GameClock.Awake` establishes its singleton, subscribes its private save writer to `SavesSystem.OnCollectSaveData`, and performs the native load. `SavesSystem.OnSetFile`, `OnSaveDeleted`, `OnCollectSaveData`, and `LevelManager.OnNewGameReport` remain the established UDS profile-generation boundaries.

M12 accumulates high-frequency clock callbacks in memory and publishes one checked aggregate mutation into the current profile at a bounded one-second cadence. Publication does not itself dirty the growing durable profile snapshot. A separate 30-second monotonic cadence requests the existing single-flight snapshot writer, limiting ordinary M12-induced persistence to at most 120 full snapshots per hour. An abrupt crash can lose progression observed during at most approximately 30 seconds of real process time: roughly 30 in-game minutes at Duckov's ordinary 60x world-clock rate, or more if a non-sleep fast-forward occurs inside that window. The cadence uses process-monotonic `Stopwatch` time, so a Windows wall-clock rewind cannot extend that real-time window. A proven completed sleep requests durability immediately because one event may advance many in-game hours. Export, native save collection, save-generation transitions, application quit, deactivation, and clean disposal also synchronously flush pending publication and request durability before their existing writer barriers. Profile changes clear any uncompleted sleep candidate; the next clock observation establishes a new baseline without assigning prior observations to the replacement generation. M12 never creates run, route, map, or segment attribution because the sleep UI is a base interaction and no additional useful raid identity is exposed by this contract.

Setup uses the existing idempotent static-subscription owner and a dedicated Harmony owner ID. The exact patch set and shared-state stamp are checked before use and incrementally rechecked at runtime. Foreign or missing patches disable only both sleep metrics; the public clock event can continue supporting calendar and observed-time metrics independently. Cleanup first requires the final retained aggregate mutation to be accepted, detaches the bridge, removes both static subscriptions, and retries patch removal through the established process-lifetime cleanup owner. A shared run/world-time completion gate retains the profile coordinator until both cleanup owners succeed, so a failed world-time retry cannot lose its persistence dependency.
