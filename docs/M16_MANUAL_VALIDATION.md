# M16 user-controlled manual validation

This matrix validates M16 against real Duckov 2.3.30 crafting while keeping gameplay and Duckov saves entirely user-controlled. Codex may deploy the candidate only after explicit approval and confirmation that Duckov is closed. Codex must not launch Duckov, select a save, craft, move inventory, save, or shut the game down.

## Preconditions

1. Confirm Duckov is closed before deployment.
2. Deploy the exact validated five-file candidate to `Duckov_Data/Mods/UltimateDuckovStatistics` and independently read back all five hashes.
3. Start Duckov yourself. In **Mods**, confirm HarmonyLib and Ultimate Duckov Statistics are active before selecting a save.
4. Select the test save yourself and record its slot, UDS generation ID, Money, physical Cash, and the current UDS Crafting totals. Do not alter the save solely for the test unless you accept the gameplay effect.
5. Open the crafting UI and take a screenshot or note the output, declared item costs, declared quantity, and declared currency cost for each recipe you will use. Prefer two Advanced Workbench recipes sharing a visible resource. Audited formula IDs `1026`, `1028`, and `1029` all use item ID 764 and cost 150 currency; the UDS Crafting panel will show the formula ID after completion. If those formulas are unavailable in the chosen save, use any two accessible recipes with a clearly shared resource and record that substitution.

## Matrix

Perform each gameplay action yourself, waiting for the crafted output to arrive before inspecting UDS.

| Case | User action | Required UDS result |
| --- | --- | --- |
| Baseline | Inspect Crafting before crafting | Current M13 actions/output quantities, M16 resource totals, currency totals, capability states, generation, and any pre-M16 partial-history notice are visible. Money/Cash split is unavailable. |
| Successful combined cost | Complete one recipe with item and currency cost | Successful actions increase by one; produced quantity increases by the recipe's declared result amount; each captured item cost increases by exactly its event-time declared amount; currency-charged actions increase by one and total currency by the declared charge. |
| Shared resource | Complete a different output recipe using one resource from the first | The shared resource lifetime total increases by the second declared amount. Its derived breakdown retains separate exact output and recipe associations and consumption-action counts. |
| Item-only or free recipe | If accessible, complete a recipe with zero currency | Completion/output and item-cost totals update as declared; currency action and amount do not change. If no such accessible recipe exists, record the case as not exercised rather than fabricating it. |
| Failed attempt | Attempt a craft that Duckov rejects for insufficient cost, without using cheats or modifying saves | No successful action, output quantity, item-resource, association, or currency total changes. Skip if the game UI prevents a safe attempt and record it as not exercised. |
| Export | Use UDS Export after the successful crafts | The live panel, schema-16 profile, `statistics.json`, `crafting_totals.csv`, `crafting_recipes.csv`, `crafting_resources.csv`, and `crafting_resource_associations.csv` agree exactly. The export contains 33 files. |
| Restart | Save and exit through Duckov normally, then cold-start and reopen the same save | M16 totals reopen unchanged under the same UDS generation. No craft is manufactured during hydration. |
| Clean shutdown | Exit Duckov normally | Primary and backup UDS profiles are valid and consistent; no UDS `.tmp`, deploying, or previous-deployment residue remains; `Player.log` contains no new UDS error-like line. |

## Evidence to return

After you finish, report:

- slot and UDS generation ID;
- each chosen output and formula ID;
- event-time item and currency costs noted before each craft;
- before/after live Crafting totals;
- export directory path;
- whether the failed, zero-currency, and restart cases were exercised;
- when Duckov is closed again.

Codex will then inspect only UDS-owned profiles, exports, package/deployment files, and relevant `Player.log` evidence read-only. It will not inspect or modify Duckov saves.

## Acceptance

Manual qualification passes when every exercised successful craft matches its event-time declarations, shared-resource lifetime and association totals agree, failed/incomplete work produces nothing, currency remains separate from item consumption with no Money/Cash split claim, all schema-16 projections agree, restart creates no extra event, and shutdown is clean. Unexercised optional cases remain explicit residual risk.
