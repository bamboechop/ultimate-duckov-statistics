# M16 installed native crafting-resource contract

This document records the M16 contract audited from the installed Escape From Duckov baseline before choosing hooks or persistence semantics. Installed native behavior is authoritative; names and quantities below are evidence from Duckov 2.3.30 rather than assumptions from UI text or historical UDS data.

## Audited baseline

| Component | Installed evidence |
| --- | --- |
| Duckov | 2.3.30, Steam build 24013657 |
| Unity | 2022.3.62f2 |
| `TeamSoda.Duckov.Core.dll` | SHA-256 `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f` |
| `ItemStatsSystem.dll` | SHA-256 `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60` |
| `resources.assets` | SHA-256 `93c4ab6ad71fdb3bf4a331bbb2ac6bc2f7db7b0f12efe60dd43ea019ab2e543d` |
| HarmonyLib | 2.4.1.0, SHA-256 `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6` |

`tools/DuckovContractProbe` verifies the managed members and parses the serialized `CraftingFormulaCollection` in `resources.assets`. The collection contains 269 formulas, 681 item-cost entries, no empty item-cost arrays, no repeated resource ID within an installed formula, and at most six item-cost entries in one formula. Its formula-list payload consumes exactly 29,020 serialized bytes; including the Unity object header and name, the audited object size is 29,068 bytes.

## Native execution order

Decompilation of private `CraftingManager.Craft(CraftingFormula)` proves this order:

1. Read `formula.cost.Enough`; return `null` if false.
2. Construct the singular output `Cost` from `formula.result.id` and `formula.result.amount`.
3. Call `formula.cost.Pay()`; return `null` if false.
4. Await `outputCost.Return(false, true, 1, generatedBuffer)`.
5. Mark each returned item with `FromInfoKey = "UI_Crafting"`, invoke `OnItemCrafted`, and return the buffer.

The adapter correlates `Cost.Return(false, true, 1, generatedBuffer)` with its private craft invocation. Publication requires a completed delivery task and a nonempty buffer exposing only the expected output item type. Formula result quantity remains the request-time amount. Managed references survive full stack merges; their remaining stack quantity is not a substitute for the produced amount. Empty or mismatched buffers count no craft, while later attempts remain observable.

Payment evidence remains provisional until delivery. A rejected or abandoned attempt changes no crafting aggregate or historical coverage flag. A replacement craft can deliver despite missing or failed payment; that output is retained with unknown costs. The [record-by-default audit](RECORD_BY_DEFAULT.md) describes the policy and validation boundary.

## Item-resource semantics

`CraftingFormula.cost.items` is a nullable array of `Cost.ItemEntry { int id; long amount; }`. Native `Cost.IsFree` treats `items == null` exactly like an empty array when `money` is not positive. `EconomyManager.IsEnough(Cost)` enters its item loop only when `items != null`, and `ItemUtilities.ConsumeItems(Cost)` likewise skips removal and succeeds when `items == null`. On the unmodified baseline, a default `Cost` consumes no item resources. UDS still observes the payment rather than assuming a modded call remained free; a successful observed free payment leaves item-resource capability and history complete and emits no resource mutation.

For a non-null array, `EconomyManager.IsEnough(Cost)` compares each declared entry independently with `ItemUtilities.GetItemCount(id)`. `EconomyManager.Pay(Cost)` repeats that check, pays currency, then requires `ItemUtilities.ConsumeItems(cost)` to return true. `ConsumeItems` first finds and durability-sorts the matching item objects and builds one deferred removal closure per entry; only after every entry passes its independent check does it execute the closures. Before removing a slot-bearing item, each closure detaches its children and queues them for return through `SendToPlayer` after all deferred removals execute. A partial-stack closure writes the reduced `Item.StackCount`. A full-stack closure detaches the item and calls `DestroyTree`, which calls `Item.MarkDestroyed()` synchronously but defers Unity object destruction. The method neither excludes an already marked item from a later captured closure nor verifies any closure's residual requested amount before returning `true`.

That behavior matters for modded formulas containing the same resource ID more than once. With one four-unit stack and `X x 3; X x 3`, both independent checks see four, the first closure reduces the stack to one, and the second destroys the remaining one; native payment reports success after removing only four. More subtly, with two consistently durability-ordered three-unit stacks, both checks see six and both closures capture both item references. The first closure detaches and marks the first stack for deferred destruction. The second still sees that captured object as non-null with `StackCount == 3`, marks it again, and leaves the second real stack untouched. Native payment again reports success, but only three distinct units were removed. Pre-removal total stock therefore cannot prove actual combined consumption. A single six-unit stack behaves differently: the first closure reduces it to three and the second destroys the remaining three, so its two mutations do remove six.

The returned-child path adds another distinction between a stack decrease and consumption. Installed `Item.Combine(Item incoming)` increases the destination stack first and then decreases the incoming stack by the same transferred quantity. If the duplicate-closure case above destroys only the first `X x 3` stack and that slot-bearing stack carried a detachable same-ID `X x 6` child, `SendToPlayer` can partially merge three child units into the surviving `X x 3` stack. The child decrease is not consumption: the equal destination increase keeps all six child units owned. Counting only positive decreases plus the first destruction would falsely prove six even though ownership ended for only the destroyed three.

`ConsumeItems(Cost)` invokes `ItemUtilities.OnPlayerItemOperation` after executing its removal closures and returning detached children, but before `EconomyManager.Pay(Cost)` returns. Every enabled `CraftView_ListEntry` subscribes `Refresh` to that event, and `Refresh` evaluates `Formula.cost.Enough`, causing a second, post-consumption `EconomyManager.IsEnough` pass while the matched UDS payment scope is still active. The installed `Pay` body nevertheless begins with its own direct `IsEnough` call before currency or item mutation. For each repeated resource, UDS therefore accepts only the first number of same-ID `GetItemCount` results required by the declared entry count and seals that pre-payment affordability evidence; later UI, Cash-display, or other event-driven queries inside the still-active payment scope cannot replace it. Mutation/destruction proof remains mandatory and independently rejects native under- or over-consumption.

UDS now patches only the craft, delivery and payment methods for this adapter. Foreign patch presence does not disable observation. A unique successful synchronous payment captures Money and player-owned item quantities before and after the call, including slots and nested inventories. The snapshot traversal deduplicates instance identities, excludes destroyed items, and keeps primitive values only. This captures actual consumption for ordinary and repeated requirements, including native underconsumption and changed mod costs. Returning or merging a previously owned attachment does not create a cost.

Every item type with positive net ownership loss receives one consumption action and its measured quantity. Physical Cash type 451 contributes to total currency unless explicitly used as an item resource. When Cash may fund both an item requirement and a money charge and its quantity changes, allocation is unknown rather than counted twice. Missing, nested, repeated, failed, unreadable or overflowing observations leave the affected cost evidence unavailable while delivered output remains usable. An observed successful payment with no net loss proves a free craft; a missing callback does not.

Stable item identity is the invariant native integer ID; display metadata is enrichment only. Lifetime quantities and output/recipe/resource associations remain checked Int64 aggregates. Reverse rankings are derived from the canonical aggregate. No historical consumption is reconstructed from current recipes, prices or craft counts.

## Currency semantics

`Cost.money` is a signed 64-bit declared total. The installed `EconomyManager.Pay(Cost)` requires `IsEnough`, calls the private currency `Pay(cost.money, accountAvailable, cashAvailable)`, then consumes item costs. Private currency payment uses Money first and physical Cash item type 451 for the remainder; it emits only the total `OnMoneyPaid(amount)`. UDS measures the net Money plus physical Cash loss during the correlated successful payment rather than substituting the declared price.

The installed asset audit found 14 formulas with nonzero currency cost:

`CraftingFormula.id` belongs to the formula collection; it is not an item TypeID. Numeric formula IDs can therefore collide with unrelated item TypeIDs. For the three Advanced Workbench validation candidates, current item metadata and localization resolve formula `1026` to output item `131` (Cup / Tasse), formula `1028` to output item `21` (Bleach / Bleichmittel), and formula `1029` to output item `52` (Toilet Cleaner / Klo-Reiniger). Item `764` is Polyethylene Sheet / Polyethylen-Folie.

| Formula | Output item | Tags | Money | Item costs |
| --- | ---: | --- | ---: | --- |
| `1026` | 131 | WorkBenchAdvanced | 150 | 764 x 4 |
| `1028` | 21 | WorkBenchAdvanced | 150 | 764 x 6 |
| `1029` | 52 | WorkBenchAdvanced | 150 | 764 x 6 |
| `4001` | 1290 | WorkBenchAdvanced; Printer | 2,400 | 1230 x 3; 1165 x 100; 309 x 4; 394 x 4 |
| `4002` | 1291 | WorkBenchAdvanced; Printer | 1,400 | 662 x 3; 663 x 1 |
| `4003` | 1292 | WorkBenchAdvanced; Printer | 2,400 | 662 x 4; 663 x 1 |
| `4004` | 1293 | WorkBenchAdvanced; Printer | 1,500 | 662 x 2; 663 x 1 |
| `4005` | 1294 | WorkBenchAdvanced; Printer | 2,300 | 662 x 3; 663 x 2 |
| `4006` | 1295 | WorkBenchAdvanced; Printer | 2,500 | 662 x 4; 663 x 2 |
| `4007` | 1296 | WorkBenchAdvanced; Printer | 1,400 | 662 x 5; 663 x 1; 362 x 2 |
| `4008` | 1297 | WorkBenchAdvanced; Printer | 1,400 | 662 x 4; 663 x 3 |
| `4009` | 1305 | WorkBenchAdvanced; Printer | 1,000 | 662 x 4 |
| `4010` | 1306 | WorkBenchAdvanced; Printer | 2,500 | 662 x 3; 663 x 2 |
| `4011` | 1369 | WorkBenchAdvanced; Printer | 2,500 | 662 x 1; 663 x 3 |

The recorded charge is the measured total across Money and physical Cash. The public presentation retains one total rather than introducing a persisted Money/Cash split. Concurrent semantic side effects inside a replaced payment cannot be decomposed from their net quantities.

## Capabilities and failure boundaries

Item-resource identity, output/resource association and total currency charge remain independent capabilities. Payment failure is provisional until delivered output is accepted. Only an accepted delivered mutation with unknown costs sets historical coverage flags. A later exact payment contributes measured values without upgrading old gaps. Current adapter capability reports partial observation separately from the persisted historical restriction, so diagnostics and the panel do not claim that a historical gap disabled all current crafting.

Missing payment callbacks disable cost capture while retaining independent output delivery. Missing craft/delivery callbacks or incompatible native task contracts still suspend the dependent observer. Foreign callbacks alone do not. Capability/profile publication remains deferred to a bounded retry tick or an explicit lifecycle barrier; native request, payment and delivery callbacks perform no full profile save. Save-generation handoff and publication retry remain unchanged.

All aggregates use checked arithmetic. On overflow, UDS preserves the prior exact value and disables only the affected resource-action, resource-quantity, currency-action, or currency-amount projection. Completion actions are the parent cardinality for resource and currency action projections. If completion arithmetic overflows on an uncharged or resource-free craft, the next affected craft disables its dependent action projection before applying it; independently exact resource quantity and currency amount remain eligible and durable. While both currency dimensions remain exact, actions and amount must either both be zero or both be positive at the lifetime, output, and recipe levels. Every charged craft contributes one action and a positive integral amount, so whenever amount arithmetic remains exact the amount must also be at least the retained action count. That lower bound continues to apply after action arithmetic becomes unavailable. A pair with zero actions and positive exact amount is legitimate because action updates may have stopped while amount updates continued. When only currency amount is unavailable, positive actions with zero or lower frozen amount is legitimate while zero exact actions with positive amount is impossible. If both dimensions are unavailable, either frozen shape may remain. Current-schema candidate validation applies that directional state machine before atomic selection so a corrupt primary cannot defeat an intact backup.

Resource quantity and currency are reducer-atomic fan-outs: one proven resource event updates its lifetime total and exact output/recipe/resource association together, while one proven currency event updates lifetime, output, and recipe totals together. Unproven evidence updates none of those levels, and the checked preflight skips the whole affected dimension before any overflow. `ApplyResourceQuantity` creates a lifetime resource row only while applying a positive canonical quantity; a first-event saturation creates no lifetime row, and later saturation preserves the prior positive value. A persisted lifetime resource row is therefore always strictly positive regardless of `ResourceQuantityArithmeticUnavailable`. If a previously unseen resource arrives only after quantity arithmetic is unavailable, `ApplyResourceActions` creates its exact action association but quantity application remains skipped: the association has frozen zero quantity and no lifetime row. Current-schema validation permits that missing key only while quantity arithmetic is unavailable and the resource's summed association quantity is zero; a positive missing total or any zero lifetime row remains invalid. Each exact resource mutation also contributes at least one quantity per consumption action, so `ConsumptionActions <= ConsumedQuantity` while quantity arithmetic is exact. Quantity overflow stops later quantity updates without stopping independently exact action updates, so that pair constraint intentionally relaxes only when `ResourceQuantityArithmeticUnavailable` is set. Capability or history degradation changes the interpretation of retained history but cannot create a legitimate positive subset between fan-outs. Current-schema validation otherwise requires equality across them; this does not change the separately supported recipe-identity and batch-metadata subset semantics. Save-generation handoff, deferred publication, retry, atomic primary/backup/temporary recovery, and terminal-shutdown ownership remain the M13 production boundaries.

## Performance contract

A successful unique payment performs two owned-item traversals and reads Money before and after its synchronous call. Traversal is bounded at 32,768 distinct identities, includes nested ownership, and retains primitive snapshots only. There are no per-frame holdings scans or stack/destruction hooks. Pending publications coalesce by output, recipe, proof state, resource and currency; no raw craft journal or duplicate inverse index is retained. Ordinary frames retain the existing bounded pending-publication and patch-token checks. Uncertain undelivered attempts perform no capability/profile save, and back-to-back delivery cannot flush pending capability work into synchronous profile I/O.

## Reproduce the executable audit

With `DUCKOV_PATH` pointing to the installed game root:

```powershell
dotnet run --project tools/DuckovContractProbe/DuckovContractProbe.csproj -c Release -- $env:DUCKOV_PATH
```

The probe must print the full nonzero-currency list above, or an explicit `none` on a different audited baseline. A structural pass without the serialized formula audit is not sufficient for M16.
