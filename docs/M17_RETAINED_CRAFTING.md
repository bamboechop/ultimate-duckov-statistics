# Retained native Crafting

The Crafting tab implements the two peer accordions in [the accepted Crafting reference](../mockups/uds-ui-crafting.jpg): Most crafted items and Most used crafting resources. The example item names and numbers are not fixtures. This view adds no persisted member, tracking callback, inventory scan, current recipe lookup, or profile I/O.

## Sources and acceptance mapping

| Reference element or state | Production evidence and presentation | Control and automated coverage |
| --- | --- | --- |
| Most crafted items | `Profile.Statistics.Crafting.Outputs`, ordered by successful `CompletionActions`, then ordinal captured name and exact output ID | Output accordion; actions-versus-units and deterministic tie tests |
| Times crafted | One successful correlated native output-delivery completion per action, independent of batch size | Header value uses the localized times unit; produced units never determine output ranking |
| Expanded produced units | The output's recorded `ProducedQuantity` | Separate produced value beside Resources used; independent quantity failure coverage |
| Resources used | Exact recorded `Recipes[*].Resources[*].ConsumedQuantity`, grouped only by exact resource ID | Read-only icon/name/used-quantity rows within the selected card; changed cost and identity-collision tests |
| Most used crafting resources | Canonical lifetime `Crafting.Resources`, ranked by exact consumed quantity with deterministic ties | Independent right accordion and resource ranking tests |
| Used for | The exact reverse of the recorded output/recipe/resource graph | Output names, proven produced units and resource quantity used for each; reciprocal selection/subset tests |
| Expanded and collapsed cards | Presentation expansion IDs preserve the distinction between output and resource identities | Native full-row buttons, chevrons and orange selection; one continuous 50% black surface, followed by a 10px gap |
| Supported empty | No recorded output/resource rows and supported evidence for that family | Separate empty messages; no invented totals or anonymous cards |
| No item resources used | Recorded recipe membership covers the output actions, its resource rows are empty, resource association is supported, and resource history is complete | Expanded free-output state; unproven resource evidence cannot enter this state |
| Unavailable and incomplete | Independent current/restricted metric capabilities, arithmetic bounds, missing relationship evidence and repaired-data state | Supported siblings remain visible; missing quantities do not become zero; no blanket development-history banner |
| Native item imagery | Native adapter captures canonical numeric TypeID strings; the icon lookup alone maps these to `duckov:item:N` | Existing native resolver, colored totem treatment, explicit TypeID 356 empty mark and distinct unknown icon fallback |
| Desktop scrolling | Two equal-width documents under the existing fixed header | Independent clamped native scroll regions and changing top/bottom overflow cues |
| Narrow layout and long text | Both complete documents, in output-first order, with bounded inner viewports and outer scrolling | Native measured wrapping; deterministic width, containment, 10px gap and 10,000-detail visible-range tests |
| Refresh, generation and input | Factory-bound profile/statistics/coordinator generation and exact accordion IDs | Same-generation selection/offset retention, removed-ID pruning, stale callback rejection and generation reset tests |

`NativeCraftingAdapter` captures formula result identity/quantity and declared costs at native Craft invocation. `CraftingResourcePaymentProof` verifies the exact paid resource evidence; `CraftingCompletionBoundary` publishes only correlated completed output delivery. `CraftingStatisticsReducer` supplies the canonical lifetime/output/recipe/resource aggregates. The existing `StatisticsPanelProjectionFactory` composes these from the active profile and independently restricted capabilities; `CraftingProjectionBinding` rejects a replaced publication member or unproven generation. `CraftingPresentationFactory` copies primitive values and read-only collections before retained controls receive them. Rendering never accesses native crafting, inventory or profile services.

Captured names are display text only. Missing names retain their exact stable ID in an unknown/modded label. Equal display names never join rows. Bare canonical numeric TypeIDs are normalized only for icon resolution; `output:<captured ID>` and `resource:<captured ID>` remain independent selection identities. Foreign namespaces and noncanonical numeric strings do not acquire a guessed native ID.

## Exact reciprocal production

Consumed resource quantities are recorded directly. The reverse graph must also avoid assigning all of a recipe's produced units to a resource that was recorded for only some of its actions. The older generic projection's per-recipe output sum cannot by itself prove that subset.

The retained factory uses the full recorded recipe quantity when that resource's consumption-action count equals the recipe's completion count. If only a subset of actions recorded the resource, one complete recorded batch size can prove the corresponding produced units by exact multiplication. If the recipe has several recorded batch sizes and incomplete resource coverage, the persisted graph does not identify which batch belonged to which resource action. In that case the row retains the corresponding output identity and exact consumed resource quantity, and says Produced quantity unavailable. This is a concrete limit of the existing data contract; no new schema or historical reconstruction is introduced. Arithmetic-unavailable dimensions likewise remain unavailable instead of displaying a saturated approximation as exact.

Current resource tracking failure does not remove independently recorded successful actions or produced units. Produced quantity failure does not remove exact resource units. Existing positive recorded values remain visible with the affected family's notice; unavailable empty evidence never becomes a no-activity claim. Historical flags are preserved in the profile but do not generate the removed earlier-history boilerplate.

## Native layout and lifecycle

The shell's native TMP font/material, existing `RunsHistoryButton` identity binding, `ButtonAnimation`, keyboard/controller submit feedback and `NativeItemIconResolver` are reused. Only accordion headers are interactive; expanded contents are read-only. Headings begin at the panel's actual 30px padding. Desktop columns have equal width with the established 40px reference gap. Selected headers use the established orange, and their combined header/detail card has one 50% black background. Related rows remain together and expanded cards retain a 10px following gap.

Below the existing 1180px viewport threshold, outputs stack above resources. Each inner column has a bounded viewport and forwards scrolling to the outer page at its boundaries. Scrolling uses the shared clamped Runs configuration, rounded stencil clipping and overflow cues. Native text measurement determines the space needed by long localized/modded names and values. No geometry or typography assertion is executed when the panel opens.

Down from the Crafting main tab enters output rows. Up from the first row returns to the tab strip; Left/Right moves between the two regions. Read-only/empty viewports accept controller scrolling. Focus reveals the actual target identity, remains on the same visible pooled control, and moves to its viewport if wheel scrolling removes it from view. Pointer capture is cancelled on scrolling, recycling and refresh. Same-generation updates retain still-present expansion IDs, focus and clamped scroll offsets. Invalidation or a new generation clears them. Disposal releases owned listeners, controls, scroll listeners and icon references; shared typography/material ownership remains with the shell.

## Qualification

The focused `RetainedCraftingTests` Debug batch passed **34/34** on 2026-09-07. Tests exercise real crafting completion/reducer publications through the production projection and retained presentation, as well as deterministic document/selection policies. These checks do not certify gameplay, typography, pointer hit testing, animation/audio or the native rendered appearance. Integrated native build, complete-suite, probe, package and deployment results are recorded with the overall delivery.

Manual acceptance remains pending and user-controlled:

1. Craft an output whose batch contains several units and compare times crafted, produced units and resource units with the profile/export. Expand the corresponding resource and confirm the output relationship and exact used quantity.
2. Compare both columns with the reference at 2560×1440 and 1920×1080; check 1024×768 and representative UI scales for output-first stacking and access to every row. Use long English/German or modded names where available.
3. Scroll columns independently through fit/top/middle/bottom states; expand/collapse cards, check 10px gaps and continuous black backgrounds, and verify top/bottom cues without elastic overscroll.
4. Check pointer, keyboard/controller focus and native feedback; switch tabs, refresh and repeatedly close/reopen. Compare borrowed item icons, colored totems where available, unknown fallback and the explicit TypeID 356 empty mark. Unreachable exceptional states remain covered by isolated tests rather than editing live profiles.
