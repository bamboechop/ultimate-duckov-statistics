# M14 installed-native contract audit

This document records the Duckov 2.3.30 contracts used by M14. It describes evidence available in the installed assemblies; it does not infer behavior from UI labels, inventory differences, caliber metadata, or prior aggregate totals.

## Verified binary baseline

| Component | Verified value |
| --- | --- |
| Duckov | `2.3.30`, Steam build `24013657` |
| Unity | `2022.3.62f2` |
| `TeamSoda.Duckov.Core.dll` | SHA-256 `298d5d5885427632d5a94b2f3ce587f8ebc9528ec71e575a475158c326ecae8f` |
| `ItemStatsSystem.dll` | SHA-256 `a276e15c022f71b2214bd05e1b9b0f2e620c16561df576fed0b79c2fe4402e60` |
| HarmonyLib | `2.4.1.0`, SHA-256 `353daafec180bb8e7bbe4da78f2a7cdc78067392e3a4e79dc8e7af295f2371e6` |

The executable compatibility probe verifies the public firing event, gun identity properties, character equipment callbacks, item-tree notifications, enumerable `SlotCollection`, stable slot metadata, and nullable slot content against this baseline.

## Accepted firing action and simultaneous identities

`ItemAgent_Gun.OnMainCharacterShootEvent : Action<ItemAgent_Gun>` is raised on the accepted main-character firing path. At callback time the supplied gun agent exposes:

- `ItemAgent.Item.TypeID`, the stable firing weapon identity;
- `ItemAgent_Gun.GunItemSetting.TargetBulletID`, the stable configured ammunition identity; and
- `ItemSetting_Gun.CurrentBulletName`, display-name enrichment only.

The two stable IDs are read inside the same callback before UDS publishes its in-memory event. This proves an event-time pair without joining later inventory or equipment observations. A missing identity disables only that event's corresponding identity dimension. The accepted firing action still increments, and a single remaining proven identity still increments its independent aggregate, but no pair is fabricated.

The callback does not prove a rejected trigger attempt, an inventory unit consumed, or a projectile successfully initialized. M14 therefore calls the metric `accepted firing actions`. M4 ammunition-consumption and projectile capabilities remain unavailable on this public boundary.

M14 reuses the existing M4 callback. It adds no new firing hook and performs no synchronous persistence on the callback path.

## Character equipment-slot membership and empty state

`CharacterMainControl.CharacterItem.Slots` is an `ItemStatsSystem.Items.SlotCollection`. The installed collection implements `ICollection<Slot>` and exposes a public enumerator and `Count`. Its backing list retains each defined slot entry independently of occupancy.

Each enumerable `Slot` exposes:

- `Key`, used as the authoritative stable character-slot identity;
- `DisplayName`, used only as mutable enrichment; and
- `Content`, which is either an `Item` or null.

A retained slot with null `Content` is therefore positive evidence that this native slot exists and is empty. An absent slot entry, a null/duplicate/unreadable key, or an enumeration failure is not equivalent evidence. UDS marks the character-slot-state capability unavailable for that incomplete observation and records no invented empty duration for the missing remainder. Other individually retained slots in the same snapshot remain positive evidence and continue to accumulate their exact state duration.

For occupied root slots, `Content.TypeID` is the stable item identity. Unknown and modded slot keys and type IDs remain ordinary identities; built-in item categories affect presentation only.

## Equipped-item nested slot paths

Every equipped root `Item` exposes its own `Slots` collection. M14 walks that collection for all equipped root item kinds, not only guns. Each nested identity contains:

1. the root character-slot key;
2. the root item `TypeID`;
3. the complete ordered path of nested `Slot.Key` components; and
4. either the occupied child `TypeID` or a proven `Empty` state.

Path components use a length-prefixed canonical form, so delimiter characters in unknown or modded keys cannot alias a different path. Concurrent paths are independent dimensions. Their durations must not be summed as if only one nested slot can exist at a time.

Only the equipped root's public nested slot tree qualifies. Ordinary inventory or container contents are excluded unless the installed item tree itself exposes them through that equipped nested path.

The walk has defensive depth and legitimate slot-cardinality bounds. Exceeding a bound or encountering unreadable/duplicate path evidence marks nested-slot state incomplete. It never emits an empty row for the missing remainder. A separately readable equipped root remains usable even when a sibling root degrades the family capability; only its fully retained paths advance.

## Change, hydration, replacement, and reconciliation boundaries

The established M6 adapter already subscribes to:

- `CharacterMainControl.OnMainCharacterSlotContentChangedEvent`;
- `CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent`;
- `CharacterMainControl.OnMainCharacterInventoryChangedEvent`; and
- `CharacterItem.onItemTreeChanged` on the current main-character item.

Native `Slot.Plug` and `Slot.Unplug` notify the master item, invoke slot-content callbacks, and initiate item-tree change notification. Nested changes propagate through the connected item tree. When `CharacterMainControl.Main` or its `CharacterItem` is replaced, UDS detaches from the old tree, clears its cached snapshot, attaches to the replacement, and rebuilds only after the replacement becomes observable.

The adapter also reconciles once per monotonic second. Its cache is scoped to the active run/route segment, so an unchanged item tree is not rebuilt per frame, while the same immutable state is still published once into each new route segment. Loading or invalid main-character evidence suspends the open interval instead of extending stale equipment time.

Because these M6 boundaries already cover root and nested changes, M14 adds no equipment hook.

## Duration semantics

All character-slot and nested-slot durations use the existing monotonic active-raid clock. Pause and loading are excluded.

- Character-slot duration means the time that an existing native root slot was observably occupied by one stable root item or observably empty.
- Nested-slot duration means the time that the identified parent item was equipped in the identified character slot while the identified child path was observably occupied or empty.

Nested duration is not selected-weapon time, ordinary inventory time, or evidence that a child effect was active.

The existing M6 `AttachmentSignature`, exact loadout IDs, transitions, selected-weapon durations, and recurring-loadout semantics remain unchanged. Schema 14 stores structured slot-state aggregates alongside those exact joint identities; it does not replace or decode them.

## Capability and historical boundaries

M14 publishes three independent capabilities:

- `native-weapon-ammunition-pairing`;
- `native-character-equipment-slot-state`; and
- `native-equipped-item-nested-slot-state`.

One capability can degrade without disabling either of the others. Profile, Diagnostics, JSON, CSV, and the temporary UI expose the same state and provenance.

Schema-13 and earlier data contains independent weapon/ammunition totals and irreversible M6 item-tree signatures, but no event-time pair catalog, native character-slot member catalog, or named occupied/proven-empty nested intervals. Migration preserves those prior exact totals and signatures while explicitly marking pair, named-child, and empty-slot history unavailable. It never decodes hashes, subtracts marginal totals, estimates a pair, or fills historical gaps with zero.

## Reconciliation and persistence

Schema-14 pair counters use checked 64-bit arithmetic. A prospective overflow is rejected before independent totals mutate. Pair totals reconcile by weapon and ammunition after explicitly uncorrelated actions are removed.

Root and nested state durations use compact dictionaries bounded by observed stable identity combinations. For each fully observed slot/path interval, exactly one occupied or empty state advances. A state never exceeds its observation duration, and a nested path never exceeds its matching parent-equipped duration. Simultaneous nested paths reconcile independently.

The firing and equipment callbacks mutate only in-memory run/segment aggregates and the existing deferred checkpoint state. Persistence, save-generation handoff, interruption recovery, terminal completion, export, and shutdown retain their existing crash-safe barriers. No raw M14 event journal or fixed event-count ceiling is introduced.
