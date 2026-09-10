# Remaining M18 user qualification

This is the remaining action list, not a replay of the completed campaign. Current evidence and candidate identities are in [M18 acceptance](M18_ACCEPTANCE.md) and [item 2 closeout](M18_ITEM2_CLOSEOUT.json). The user accepted the [observed performance deviations](M18_PERFORMANCE_ACCEPTANCE.json). No further CapFrameX captures or attempts to improve the accepted percentiles are scheduled.

The final branch push and prepared draft PR update also await explicit approval. Automatic approval review rejected remote delivery because it requires authorization for the exact final commit and destination, despite the original M18 delivery authorization. All commits, packages and the prepared PR body are saved locally; this step needs no gameplay.

## One native resource session

The prepared diagnostic differs from the ordinary candidate only by opt-in observations. F11 records live Unity resources on request; it does not start a timed capture or change statistics. Ordinary release code contains no resource diagnostic. The purpose is to verify actual deferred destruction of the totem shadow mesh corrected during item 2, and restoration of the surrounding native UI. Managed tests cannot prove Unity's deferred object reclamation.

Use slot 1 at base with Harmony and UDS enabled. Use the existing recorded totems; no equipment changes or raid are required. Keep the same location, camera, language and resolution throughout.

1. Confirm Diagnostics is green. Open Equipment → Totems and hover a totem icon; also visit a recorded run containing totems. Confirm icons, glow and tooltips look normal. Close UDS and wait briefly for the native menu to settle. This is warm-up.
2. With UDS closed, press F11 once for the warm baseline. Open Equipment → Totems and press F11 again so the diagnostic observes the actual mesh owners.
3. Close UDS, wait about two seconds and press F11 for the closed endpoint.
4. Repeat that measured open/close cycle once: same Totems view, F11 while open, close, wait two seconds, F11. Check that the native inventory/map shortcuts and movement work normally after closing UDS.
5. Export once from Diagnostics, close Duckov completely, and report completion plus any changed glow, missing tooltip, stuck input or error. No CapFrameX recording is needed.

Codex then compares observed owned mesh IDs against live post-close meshes, remaining UDS roots/shadows/input blockers, and the global resource counts. Global counts are context, not proof of a UDS leak: native caches and unrelated objects can change. A scan taken with the panel still open or before deferred destruction is not a failed cleanup result. The diagnostic retains bounded integer identities, never Unity object references; it does not force garbage collection or unload assets.

After that session, the already-built ordinary candidate must replace the diagnostic while the game is closed. One ordinary cold activation with green Diagnostics, retained statistics and a final export completes the replacement check. Repeat only affected behavior if a concrete defect is found.

## Scope decisions and publication

- **Controlled multi-target firing remains Not exercised.** The [feasibility record](M18_MULTI_TARGET_FEASIBILITY.json) documents that paired regular ducks occur unpredictably. Accept this coverage limitation explicitly or identify an available repeatable encounter. No farming or random encounter search is scheduled.
- **History coverage is bounded.** UDS has no fixed retained-run maximum. The isolated large-history check qualifies its stated run count through production persistence/projections/export; native UI/export observations use the actual five-run progressed profile. Neither proves unlimited-history native cost. Accept that stated coverage for this release or specify a concrete additional profile available for native observation.
- Merge, tags, GitHub release, Workshop upload, preview approval and subscription installation remain user-controlled. The candidate and release material are prepared before those decisions. Deferred empty-state polish and other PLAN follow-ups are separate from this item 2 work.

## Already qualified; no repeat requested

The [acceptance record](M18_ACCEPTANCE.md) links completed tutorial/regular/multi-map/death/delayed-effect gameplay, paid craft/shop/ATM/sleep, profile/JSON/CSV agreement, save isolation, reset cancellation/confirmation, current-format reinstallation, cold reopen, missing-primary backup recovery, Harmony absence/restoration, language/resolution and shortcut checks.

Current-format corruption, temporary-file recovery, interrupted multi-map runs, terminal write failures, generation transitions and tutorial death/retry are covered through the production repository/coordinator/lifecycle and isolated real filesystem failures. The native callback order was checked against the installed baseline. These cases do not require deliberately corrupting a real profile, replacing a save, force-quitting or replaying a tutorial.

The installed native input asset exposes KeyAndMouse controls and no Gamepad/Joystick bindings. A controller-specific test is not a supported-baseline requirement. Existing native shortcut observations plus direct production modal/focus tests qualify the supported input paths within their stated boundaries. Synthetic foreign-hook drift is exercised through the shipping adapter; no arbitrary conflicting mod needs to be installed.
