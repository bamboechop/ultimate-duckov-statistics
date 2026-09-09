# M18 UI timing investigation

The [declared base UI comparison](M18_BASE_UI_QUALIFICATION.json) fails both the engineering limits and 10%/20% ceiling. [Exploratory windows from the same raw captures](M18_BASE_UI_INVESTIGATION.json) show a median gap before UDS opens: approximately 5.67 ms with UDS enabled versus 4.96 ms in the Harmony-only control. D2/D3 return close to their own starting frame time after closing. The first D sample retains its disclosed late close. These post-hoc slices support attribution only; they do not replace the original comparison, remove spikes or establish a revised pass.

The bounded source review found change-gated full projection rebuilding, eager construction of all tabs on open, synchronous disposal on close, and per-frame layout entry into hidden views with guards before expensive reflow. Native menu discovery is callback-driven; shortcut integrity checks already use shared patch tokens. A running world clock can cause a profile revision and full open-panel refresh, but that occurrence is not established for this paused workload. No method has yet been shown to cause the measured miss.

The [diagnostic package and deployment record](M18_UI_TIMING_DIAGNOSTIC.json) binds the source, independent reviews, tests, privacy/IL audits, extracted inventory and fresh DLL repeat. After explicit closure confirmation, both game-closed deployment tests passed in Debug and Release. Deployment verified the exact five-file diagnostic and all 2,524 then-existing UDS data files byte-identical, with the prior ordinary package preserved under verified rollback hashes. The native attribution interval is complete; ordinary-package restoration awaits fresh user-confirmed closure.

## Completed result

The [preserved native result](M18_UI_TIMING_RESULT.json) contains one reset/summary pair spanning 29.984 seconds and 5,886 UDS Update calls. One open/construction, eight tab changes and one close agree with the requested cycle. The user reported "done, nothing noticed", with no timing deviation reported. These counts do not establish exact action timestamps or displayed frame counts.

| Synchronous scope | Calls | Mean per call | Maximum |
| --- | ---: | ---: | ---: |
| Update with panel closed | 3,055 | 0.005571 ms | 0.1352 ms |
| Update with panel open | 2,831 | 0.062232 ms | 20.9884 ms |
| Open, including construction | 1 | 29.4576 ms | 29.4576 ms |
| Shell layout/ticks | 2,831 | 0.049720 ms | 20.9798 ms |
| Tab selection | 8 | 0.877813 ms | 1.9579 ms |
| Close | 1 | 3.9760 ms | 3.9760 ms |

No projection refresh ran while paused, so the clock-triggered full-refresh hypothesis was not exercised. The interval recorded no checkpoint/profile write, holdings scan or combat activity. Process-wide GC counters increased by 12/12/12; these are not 36 independent pauses or attribution to UDS. No error/exception text appears between reset and summary. Pre-existing native initialization errors remain in the full preserved log.

The diagnostic used a fresh zero-run generation, whereas the original UI acceptance used one retained run. It is not a matched acceptance repeat or progressed-history qualification. Timings include instrumentation and overlap. Native dispatch, deferred Unity canvas rebuilding/rendering/destruction, unmeasured allocations and session variance remain unisolated; small synchronous costs do not establish small total UDS-induced native costs.

The measured closed/open Update work is too small to explain the earlier approximately 0.71 ms pre-opening gap. The single opening/closing observations do not justify a lifecycle rewrite. Apply the declared stopping rule: end this investigation with no ordinary runtime correction selected, restore frozen ordinary `88efddb` after confirmed closure, and retain the original UI ceiling failure. No new baseline series or acceptance exception is inferred.

## One diagnostic interval

The existing opt-in fixed-storage stopwatch counters now distinguish Update and panel Tick by panel state at entry. Separate scopes cover opening projection, complete shell construction, projection refresh, shell refresh, actual visual layout after its cache guard, shell layout/ticks, diagnostics rebuild, tab selection and synchronous closing cleanup. Existing adapter and persistence timings remain available for the pre-opening cost. All added code is behind `UDS_PERFORMANCE_DIAGNOSTICS`; ordinary builds contain none of these calls or the state accessor.

Build with `scripts/build-performance-diagnostic.ps1 -DuckovPath <installed-game-directory>`. Preserve the exact five-file package and manifest, verify ordinary Release IL and obtain independent review before deployment. This package is a measurement tool, not the release candidate. The frozen ordinary `88efddb5eacdab40a40498b62f90fdde14d167af` package remains the restoration target. Duckov must be confirmed closed before reversible UDS-only replacement; do not alter saves or reset profiles.

The diagnostic builder performs a nonincremental build with shared compilation disabled. The first package check encountered cached .NET 8.0.30 compiler-runtime metadata alongside a fresh .NET 8.0.31 rebuild. Both attempts and their PDB evidence are preserved. A fresh toolchain build must reproduce its DLL hashes before this diagnostic is deployed; output-directory comparison is not a substitute for the separate release-candidate two-checkout qualification.

Use slot 1 at the same base position/camera and native pause surface, with the unequipped loadout, display settings and short history disclosed. Prepare UDS with Overview selected, then close it. The user controls this approximate 30-second interval:

1. With native pause open and UDS closed, press F9 once to reset/start the counters. Remain paused for about five seconds.
2. Open UDS through its pause-menu button. Visit all nine tabs with eight Ctrl+Tab presses, roughly one second apart, then scroll Diagnostics.
3. At about 20 seconds close UDS with F8. Remain on native pause for about ten seconds, then press F10 once to write/stop the summary.
4. Confirm focus, actions and any delay or perceived hitch. Agent builds, tests, analysis and other work stay suspended until completion is confirmed.

This is one attribution interval; no new Harmony-only baseline or CapFrameX acceptance series is required. An operator mistake is recorded explicitly. Do not start a repeat without first deciding whether the affected timing remains usable.

## Interpretation and stopping rule

Timings are inclusive synchronous stopwatch `calls,total,max` values. Parent and child totals overlap; do not add them or interpret their subtraction as precise ordinary-release cost. Update/panel state is sampled at method entry, so a transition frame stays in its starting bucket. Pause-button opening runs through native UI dispatch and may fall outside `ModBehaviour.Update`; its independent open scope captures UDS synchronous work there. A tab-selection scope excludes dirty reflow in the following shell Tick, which is recorded separately. Closing measures synchronous disposal after the lifecycle gate; Unity may destroy objects or rebuild/render canvases later outside these scopes. Process-wide GC counts do not identify an allocator.

Read this single interval before selecting a correction. A material measured path and supported behavior must justify any change. If UDS synchronous work is too small to explain the gap, report that limitation and stop; do not infer a method from the remaining difference or automatically launch another diagnostic/baseline loop. Restore the ordinary package after diagnosis. A correction requires review and affected ordinary-runtime qualification; the failed ceiling and all remaining M18 gates stay open until then or an explicit product acceptance decision.
