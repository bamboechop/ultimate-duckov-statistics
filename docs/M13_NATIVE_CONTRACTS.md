# M13 installed-native crafting contracts

This audit fixes the M13 implementation boundary to the locally installed Escape From Duckov `2.3.30` / Steam build `24013657`. It is a versioned compatibility contract, not a claim about other game builds. The automated probe verifies the named metadata contracts against the installed assemblies before packaging.

## Verified request and completion path

`CraftView.CraftTask` prevents a second request from that UI instance while its `crafting` flag is set, calls public `CraftingManager.Craft(string)`, and awaits the returned task. The public overload resolves the recipe and delegates to private `CraftingManager.Craft(CraftingFormula)`.

The private method has this ordered behavior:

1. Return `null` when `formula.cost.Enough` is false.
2. Construct one output `Cost` from the singular `formula.result.id` and `formula.result.amount`.
3. Return `null` when `formula.cost.Pay()` fails.
4. Await `Cost.Return(false, true, 1, generatedBuffer)`.
5. Let `Cost.Return` instantiate output stack chunks and deliver each to the player character inventory or, when that cannot accept it, `PlayerStorage.Push`, whose buffer is persisted by Duckov.
6. Invoke `CraftingManager.OnItemCrafted(formula, item)` only for generated references that still compare non-null.
7. Return the generated buffer.

UDS patches only the private `Craft(CraftingFormula)` return value. Its postfix replaces the returned `UniTask<List<Item>>` with an awaiting wrapper, captures the formula evidence once, and publishes one action only when the original task returns non-null. The completion timestamp and current UDS save generation are read after the awaited native delivery path has finished. A null result or exception abandons the in-flight token and publishes nothing.

## Why `OnItemCrafted` is not the counter boundary

The public static callback is downstream of delivery, but it is invoked once per surviving generated stack chunk rather than once per crafting action. `Cost.Return` may split a declared amount by maximum stack size. Inventory merging can also destroy the temporary incoming `Item`, causing Unity null comparison to suppress that callback for a successfully delivered chunk. Counting callbacks would therefore confuse actions with chunks and could undercount merged outputs. Inventory and storage change events are likewise movement/hydration surfaces and cannot prove crafting.

The private task result supplies the missing action boundary: one invocation represents one resolved formula request, `null` represents native rejection/failure on this path, and non-null is returned only after the output return/delivery operation completes. Declared `result.amount` remains the quantity; it is never replaced by callback count, stack count, current inventory, ingredient cost, price, or currency movement.

## Proven metadata and unavailable dimensions

| Dimension | M13 state | Evidence |
|---|---|---|
| Successful completion action | `Supported` | One non-null completion of private `Craft(CraftingFormula)` after awaited output delivery |
| Produced quantity | `Supported` | Positive singular `CraftingFormula.result.amount` |
| Stable output identity | `Supported` | Integer `CraftingFormula.result.id`, persisted as invariant text |
| Display name | Enrichment only | `ItemAssetsCollection.GetMetaData(int)` supports built-in and dynamic/modded entries; fallback is `Unknown item <id>` |
| Recipe identity | `Supported` | `CraftingFormula.id` captured from the exact invocation |
| Batch metadata | `Supported` | Declared quantity retained as a batch-size-to-action distribution |
| Multiple-output recipes | `DisabledIncompatible` | Installed `CraftingFormula` has one `ItemEntry result`, not an output collection |
| Workstation identity | `DisabledIncompatible` | `InteractCrafter` opens the shared view with a tag predicate but does not propagate a stable workstation identity into `Craft` |
| Run/map/context attribution | `DisabledIncompatible` | The completion call exposes no reliable run, map, or crafter context; M13 remains save-generation lifetime scope |
| Ingredients, cost, value, or profit | Not recorded | These cannot be derived truthfully from an output completion |

Unknown and modded integer item identities and arbitrary recipe IDs are retained. Metadata lookup failure does not drop or reclassify the completion.

## Queue, cancellation, restart, and overlap findings

The installed `CraftingManager` persists unlocked formula IDs only. It exposes no native crafting queue, queued job identity, cancellation callback, delayed collection state, or persisted completion journal. The observed UI awaits the task directly. Consequently:

- a request that returns `null`, throws, or is still incomplete at process termination records nothing;
- no load-time queue reconstruction or inventory comparison is attempted;
- overlapping invocations from other callers are isolated by distinct in-flight tokens and may complete out of order without crossing formula evidence;
- a token is consumed once, so duplicate completion publication is rejected;
- a fresh process/boundary cannot consume a stale in-flight token;
- profile transitions flush already-completed pending aggregates before rotation, while a task that completes later is assigned to the save generation current at its proven delivery completion.

During mod deactivation, the adapter stops accepting new requests and refuses final cleanup while an already-wrapped task is incomplete. The retained adapter and profile coordinator remain available for that continuation and persistence retry. Patch ownership is exact and versioned; unsafe pre-existing patches or later Harmony patch-state drift disable current crafting capture instead of silently weakening exactly-once semantics.

The completion token remains outstanding while a proven native result is being converted and handed to the aggregate publisher. Cleanup therefore cannot observe a gap between removal from the in-flight request set and publication into retryable pending state. A failure inside UDS after native delivery never replaces the native task result with a statistics exception: current crafting capture is disabled with diagnostic provenance, cleanup state is released safely, and Duckov receives its original successful result.

## Persistence, arithmetic, and performance

Completion publication updates the in-memory schema-13 aggregate and marks the existing coalesced profile snapshot writer dirty. It does not perform a synchronous full-profile serialization per crafted item. Save collection, profile transitions, export, shutdown, and adapter cleanup are durability barriers. Failed aggregate publication remains incrementally coalesced by save generation, output, recipe, and batch for retry; adding another completion touches only its incoming keys instead of rebuilding the backlog. A full retry snapshot is attempted at most once per second during ordinary update processing, while lifecycle/export barriers force an immediate attempt. No event-history list or fixed 2,048-style ceiling exists.

Completion actions and produced quantity use independent checked 64-bit arithmetic. If the next action increment is unrepresentable, the prior exact action total is retained and only action/batch capture becomes unavailable; quantity can continue. Quantity overflow behaves symmetrically without erasing still-exact action totals. Every pending row carries its event-time recipe/batch proof scope separately from the aggregate's current capability state, so a later patch degradation cannot suppress an already-proven retry and mixed full/partial rows cannot borrow evidence from one another. Current-schema recovery validates capability enums and lifetime/output/recipe/batch composition—including the presence of recipe rows whenever recipe identity is supported—before a primary file can defeat an intact backup. Schema-12 migration creates an empty crafting aggregate with explicit pre-M13 historical unavailability and reconstructs nothing.

The native hook performs constant work per craft completion plus dictionary aggregation by legitimate distinct output/recipe/batch cardinality. It performs no per-frame inventory scan, no inventory-wide search, and no synchronous profile write per item. The ordinary periodic adapter work is an O(1) pending check and a two-second incremental Harmony patch-stamp check; an outstanding publication failure may additionally build one retry snapshot per second, never once per frame.

## Installed binary evidence

- `TeamSoda.Duckov.Core.dll` SHA-256: `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f`
- `ItemStatsSystem.dll` SHA-256: `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60`
- HarmonyLib `2.4.1.0` SHA-256: `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6`

The probe requires the exact formula fields, both crafting overloads and their `UniTask<List<Item>>` result shape, `Cost.Return`, inventory/storage delivery methods, the public callback field, and metadata lookup surface. Package validation still permits exactly the two UDS assemblies plus `info.ini`, `INSTALL.md`, and `LICENSE`; no Duckov, UniTask, Unity, framework, or Harmony binary is redistributed.
