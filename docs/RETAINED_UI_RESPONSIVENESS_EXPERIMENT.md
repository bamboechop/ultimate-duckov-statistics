# Retained UI responsiveness experiment

This experiment separates opening the UDS shell from preparing and displaying its statistics. It targets the first-open hitch and repeated open/close work. Native responsiveness and visual acceptance must be measured in game; automated lifecycle tests alone do not establish a frame-time improvement.

## Behavior

- The first open builds the header, navigation and loading label. Statistics projection waits until a subsequent frame; the selected view is bound on a later frame so the opening shell has an opportunity to render first.
- Each statistics tab is constructed on first selection. Later selections reuse it. Only the selected tab binds, lays out and ticks; unvisited tabs are not constructed.
- Closing hides the owned hierarchy and releases native input, shortcut, cursor and focus ownership. The cached views remain available for another open on the same native canvas and UDS generation.
- Reopening shows the cached view immediately. Profile revisions and language changes invalidate the data. Changed revisions are coalesced at a maximum of four projection refreshes per second while open; a newly opened panel checks immediately after its opening frame.
- A profile transition closes and disposes the cache. A different native canvas, an externally destroyed hierarchy, or a failed construction/bind also causes rebuilding. No cached data is reused across generations. Application quit releases the panel before Unity destroys the native canvas; disposal also tolerates components that Unity has already destroyed.
- Language changes refresh owned captions before affected layout measurement. Deferred work does not construct or measure hidden views after close.

## Scope and tradeoffs

Unity object construction, layout, native metadata access and the aggregate projection remain on the main thread. Existing safe background readers for encounter history and kill-distance highlights remain in use. This is not a claim that all data preparation is asynchronous or that first-open work can no longer cause a hitch.

Retained views consume memory until their host/generation changes or the mod unloads. Unchanged reopening reuses Overview as well; a changed Overview projection still rebuilds that content subtree. If measurements identify that rebuild or aggregate projection as the remaining bottleneck, they need separate work rather than merely another delay.

The opt-in performance build adds `PanelViewBind` alongside shell creation, opening, projection, layout, close, and open/closed Update timings. It does not change the profile schema or published version and must not be published as a release.

## Automated validation

On September 22, 2026, local Debug and Release validation passed 2,481 main tests and 107 ordinary shell tests per configuration. The combined diagnostic shell variant passed 109 tests in each configuration. Ordinary native Debug/Release and the performance diagnostic Release build completed without warnings or errors; the native contract probe and ordinary package audits passed. The shell checks include staged first paint, lazy tab construction, repeated reopen identity, hidden inactivity, profile/canvas invalidation and localization. The test-build package and receipts are under the ignored `artifacts/retained-ui-responsiveness/20260922-first-trial/` directory. These results do not assert native performance acceptance.

The first native recording exposed disposal after Unity had destroyed a cached Records hierarchy. The correction releases the panel during application quit and guards native access during disposal of already-destroyed view components. Two additional shell cases exercise open and closed caches with a boundary that rejects destroyed-component access. Debug/Release passed 2,481 main and 109 ordinary shell tests each; combined diagnostic shell tests passed 111 each. The native builds, probe and package audits also passed. The follow-up evidence is under `artifacts/retained-ui-responsiveness/slot1-investigation/`. Native shutdown acceptance remains separate.

## Native comparison

1. Launch Duckov freshly, reach the main menu and press F9 before opening UDS. The selected save is already loaded at the main menu.
2. Open UDS once, wait for its values, then close and reopen it several times. Compare first open with repeated opens.
3. Visit every tab, then revisit Runs, Combat and Equipment. Open a run's map and encounter details, close UDS and reopen it.
4. Switch English/German and check both an already visited tab and one first visited after the language change.
5. Close UDS and leave the main menu idle briefly; confirm its normal FPS returns. Press F10 to write the timing summary.
6. Enter the base, start another F9 recording, open/close UDS there, sell or craft a small amount, then reopen and check updated values. Finish with F10 and close the game.

Keep the previous installed package backup for rollback. Compare shell creation counts, first/repeated open maxima, view binding/layout maxima and closed-panel work separately. Overall native rendering and allocations outside the instrumented scopes still require the user's observation or an external frame-time capture.
