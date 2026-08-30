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

M13 already correlates the exact `Cost.Return(false, true, 1, generatedBuffer)` task to its private craft invocation and publishes after that task completes. M16 adds no second success hook. It snapshots the cost on entry to the same invocation and attaches that immutable evidence only when M13's correlated delivery proof completes. Failed `Enough`, failed `Pay`, a null result, cancellation, exception, incomplete delivery, duplicate correlation, or abandoned shutdown work publishes no M16 cost.

## Item-resource semantics

`CraftingFormula.cost.items` is a nullable array of `Cost.ItemEntry { int id; long amount; }`. Native `Cost.IsFree` treats `items == null` exactly like an empty array when `money` is not positive. `EconomyManager.IsEnough(Cost)` enters its item loop only when `items != null`, and `ItemUtilities.ConsumeItems(Cost)` likewise skips removal and succeeds when `items == null`. A default `Cost` is therefore exact evidence of no item-resource consumption, not missing or unreadable cost evidence; a successful free craft leaves item-resource capability and history complete and emits no resource mutation.

For a non-null array, `EconomyManager.IsEnough(Cost)` compares each declared entry independently with `ItemUtilities.GetItemCount(id)`. `EconomyManager.Pay(Cost)` repeats that check, pays currency, then requires `ItemUtilities.ConsumeItems(cost)` to return true. `ConsumeItems` first finds and durability-sorts the matching item objects and builds one deferred removal closure per entry; only after every entry passes its independent check does it execute the closures. A partial-stack closure writes the reduced `Item.StackCount`. A full-stack closure detaches the item and calls `DestroyTree`, which calls `Item.MarkDestroyed()` synchronously but defers Unity object destruction. The method neither excludes an already marked item from a later captured closure nor verifies any closure's residual requested amount before returning `true`.

That behavior matters for modded formulas containing the same resource ID more than once. With one four-unit stack and `X x 3; X x 3`, both independent checks see four, the first closure reduces the stack to one, and the second destroys the remaining one; native payment reports success after removing only four. More subtly, with two consistently durability-ordered three-unit stacks, both checks see six and both closures capture both item references. The first closure detaches and marks the first stack for deferred destruction. The second still sees that captured object as non-null with `StackCount == 3`, marks it again, and leaves the second real stack untouched. Native payment again reports success, but only three distinct units were removed. Pre-removal total stock therefore cannot prove actual combined consumption. A single six-unit stack behaves differently: the first closure reduces it to three and the second destroys the remaining three, so its two distinct mutations do remove six.

UDS patches the already-required `EconomyManager.Pay(Cost, bool, bool)` and `ItemUtilities.GetItemCount(int)` contracts plus the public `Item.StackCount` setter and `Item.MarkDestroyed()` in addition to the M13 craft/delivery pair. For a matched craft with repeated resource IDs, a thread-local payment scope observes Duckov's own affordability results and the exact stack mutations performed inside that native `Pay` call. A positive `StackCount` reduction contributes its checked before/after difference. The first `MarkDestroyed` contributes that stack's current positive quantity; later marks of the same synchronously flagged object contribute nothing. Exact repeated-entry evidence requires every affordability observation, sufficient pre-removal stock, and checked distinct mutation quantity exactly equal to the canonical declaration. These hooks add no inventory query or before/after holdings scan and do not substitute a holding for the declared cost. A repeated physical-Cash item cost combined with `Cost.money > 0` also fails closed because the same Cash holding may fund both surfaces. Missing, insufficient, under- or over-mutated, overlapping-Cash, exceptional, or drifted proof marks only that successful craft's item-resource evidence unavailable; completion, output, and independently proven total currency remain eligible.

UDS therefore records the event-time declared item cost of a successful craft as resource consumption evidence:

- stable identity is the invariant string form of the native integer item ID;
- display metadata is enrichment only, so missing names retain `Unknown item <id>` and never erase the stable identity;
- every resource in one successful craft receives one consumption action and its checked declared quantity;
- null and zero-length item arrays both produce an exact empty resource set without a resource mutation or capability degradation;
- repeated IDs in a formula are canonicalized by checked addition only after matched affordability and distinct stack-mutation observations prove the combined quantity, even though the audited installed collection contains none;
- lifetime resource totals and output/recipe/resource associations are checked `Int64` aggregates;
- reverse resource rankings and breakdowns are derived from the canonical aggregate, not persisted as a duplicate inverse index.

This is not inventory-delta or holdings accounting. UDS never reconstructs a quantity from before/after inventory, a new holdings scan, historical craft counts, current recipe metadata, item value, or price. The repeated-entry guard observes only affordability calls and item mutations Duckov itself performs inside the exact native payment call and uses them to accept or reject the immutable declaration; it never persists those observations or replaces the declaration with an inferred holding. A schema-15 or older crafted total retains its M13 action/output evidence but has explicitly unavailable pre-M16 item-resource history.

## Currency semantics

`Cost.money` is a signed 64-bit declared total. The installed `EconomyManager.Pay(Cost)` requires `IsEnough`, calls the private currency `Pay(cost.money, accountAvailable, cashAvailable)`, then consumes item costs. Private currency payment uses Money first and physical Cash item type 451 for the remainder; it emits only the total `OnMoneyPaid(amount)`. A successful correlated craft delivery proves that the preceding `Pay` returned true for the invocation's captured total.

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

M16 consequently enables exact declared total currency charged and currency-charged action count. It does not expose Money versus Cash consumption. Although the private implementation chooses Money first, the correlated delivery surface contains no exact split evidence, and deriving a split would require prohibited current-holdings inference. The split capability is permanently `DisabledIncompatible` for this baseline. Pre-M16 currency-cost history is independently unavailable and is never reconstructed from M9 flows or M15 holdings.

## Capabilities and failure boundaries

M16 adds independent capabilities for item-resource identity, output/resource association, total currency charge, and Money/Cash split. Invalid, unreadable, or insufficient repeated-entry payment evidence disables the first two without disabling M13 completion/output totals or exact currency. Invalid currency evidence disables only total currency. A later exact event may still be retained as partial history, but the degraded capability never silently returns to complete. Patch drift across the craft, delivery, payment, affordability, stack-count, or destruction-observation methods, version mismatch, or an invalid core delivery contract retains M13's existing fail-closed behavior.

All aggregates use checked arithmetic. On overflow, UDS preserves the prior exact value and disables only the affected resource-action, resource-quantity, currency-action, or currency-amount projection. While both dimensions remain exact, a persisted resource association must contain positive action and quantity values, and currency actions and amount must either both be zero or both be positive at the lifetime, output, and recipe levels. The reducer cannot emit any other exact state. Current-schema candidate validation rejects those impossible pairs before atomic selection so a corrupt primary cannot defeat an intact backup. When an affected arithmetic-unavailable flag is set, the corresponding frozen zero/nonzero mismatch remains intentional and valid rather than being misclassified as corruption. Save-generation handoff, deferred publication, retry, atomic primary/backup/temporary recovery, and terminal-shutdown ownership remain the M13 production boundaries.

## Performance contract

One craft snapshots and canonicalizes only its small `Cost.items` array. The repeated-entry guard consumes Duckov's existing `GetItemCount`, `StackCount`, and `MarkDestroyed` operations and performs no additional inventory scan. Stack/destruction callbacks perform only a thread-local scope check outside a matched repeated-resource payment. In-scope state is bounded by repeated resource IDs and mutated stacks in that one native payment and is released with the craft scope. Pending publications coalesce by output, recipe, proof state, resource, and currency; no raw craft journal or inverse resource index is retained. Ordinary frames perform only the existing constant-time pending and patch-inspection checks. Persistence remains deferred/coalesced and never performs a synchronous write per craft. Cardinality is bounded by distinct output, recipe, and resource identities rather than action volume.

## Reproduce the executable audit

With `DUCKOV_PATH` pointing to the installed game root:

```powershell
dotnet run --project tools/DuckovContractProbe/DuckovContractProbe.csproj -c Release -- $env:DUCKOV_PATH
```

The probe must print the full nonzero-currency list above, or an explicit `none` on a different audited baseline. A structural pass without the serialized formula audit is not sufficient for M16.
