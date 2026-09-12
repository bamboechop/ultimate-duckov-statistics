# Combat encounter history proposal

Status: feature proposal from the September 12, 2026 playtest discussion. No feed capture, persistence, or UI has been implemented. Initial native map-contract findings are recorded below; full feasibility remains pending. Address the current freeze investigation and storage efficiency before implementing it. This is a future feature release, not a reason to change the published 1.0.0 tag.

## Intended experience

A chronological kill feed within each recorded run, with expandable enemy encounters:

- When and on which map the enemy died, which enemy it was, and which weapon or effect received the player's kill credit.
- A map view of the encounter's fatal event: mark the player's and enemy's positions when the enemy dies, or the player's death location and the responsible enemy's position when known. Let the selected feed entry highlight its markers on the game's map artwork.
- Damage exchanged with that individual enemy, including the damage it dealt to the player before its death. Do not substitute totals for all enemies of the same type.
- The enemy's weapon or weapons, where the native evidence identifies them. Distinguish a weapon observed dealing damage from one merely equipped when the enemy died.
- Loot taken from that enemy, if the player looted it.
- An optional enemy illustration with separate head/body hit markers. Headshot evidence can place a marker in the head region; other hits can use a schematic body region. This must not imply measured impact coordinates without native evidence for those positions.

The feed should retain unresolved weapon/effect identities and uncertain ownership explicitly. Kill credit, weapon identity, and enemy identity are separate facts; one being unavailable must not erase the others. Old runs contain aggregates rather than these encounter-level joins, so their feeds cannot be reconstructed from existing totals.

## Suggested first scope

Investigate and implement the chronological player kill entries and per-enemy damage exchange first. Link multiple hits, delayed effects, and death to one enemy instance across the encounter; keep enemies sharing a display name separate. Keep player kills separate from companion, other-NPC, environmental, and unresolved deaths.

Next, investigate enemy weapon identity and actual looted items. A first loot view should show items and quantities proven transferred from that specific corpse to the player. Inventory deltas alone do not prove this relationship. Handle partial-stack takes, transfers back, and repeat visits explicitly.

Whether to retain the corpse's entire available inventory and highlight taken items is still undecided. That option would require a separate inventory-at-observation snapshot and rules for generated, modified, removed, or unobserved loot. Do not silently equate an unlooted corpse with an empty one.

Treat the illustration as optional presentation after capture semantics are proven. Investigate native enemy artwork access and headshot flags before selecting a rendering approach. A body marker based only on headshot/non-headshot classification is schematic, not a hit-location replay.

## Kill and death locations on maps

Initial read-only inspection of the installed Duckov 2.3.30 assembly on September 12 confirms relevant native contracts:

- `MiniMapSettings.MapEntry` exposes a scene ID, sprite, world size, world center, image offset, and hidden/no-signal flags. Combined-map artwork and scale are also available through `IMiniMapDataProvider`.
- `MiniMapDisplay.TryConvertWorldToMinimap` subtracts the scene's map center, projects world X/Z onto the map plane, and applies the map entry's transforms. `MiniMapDisplayEntry` sizes and positions artwork using the provider's scale and offset; the display also handles rotation and content centering. Raw world X/Z cannot simply be treated as image pixels.
- `PackedMapData` retains sprite references, scale, offsets, scene IDs, and combined-map metadata. `MiniMapView.LoadData` can display that provider. This establishes a display mechanism, not a verified way to retrieve every historical map asset after a restart. World centers are resolved separately through current scene settings.
- Do not rely on the similarly named `MiniMapSettings.TryGetMinimapPosition`: in this installed version it checks the scene entry but returns the original world position without assigning the computed conversion.

Capture the two actors' world positions and their individual scene IDs at the fatal health transition, before death cleanup removes the necessary objects. Retain height so later floor handling remains possible. Bind these positions to the same encounter/event as the credited kill or player death, not a later corpse position or the player's next movement sample. Validate callback timing and object lifetimes before implementing capture.

The map UI can distinguish the player marker, enemy marker, and death marker. An optional connector or distance describes the two recorded positions; it is not a proven bullet trajectory, line of sight, or firing distance. In a delayed effect, the attack origin and the actor positions at death can differ. If an actor is gone or on another map, preserve that limitation and show only endpoints that belong to the selected map. Do not invent a remote attacker location for environmental or unresolved deaths.

Persist coordinates and stable map references, with any necessary calibration stored once per scene/run rather than per event. Borrow native artwork at display time; do not embed map images in each event or profile. Establish an asset retrieval path for base/main-menu viewing and restart, and handle scene layers, interiors, missing/no-signal maps, and later game map changes. Missing artwork should leave the textual event available. Reproduce the verified conversion in UDS-owned presentation without repurposing the game's active map UI.

Reading positions for fatal events should require no continuous location recording. Its actual callback, storage, asset-loading, and rendering costs still belong in the feasibility measurements. The existing incident profile does not contain these per-kill coordinates, so this applies to newly captured events.

## Investigation and delivery requirements

- Inspect installed native contracts for individual actor lifetimes, damage source and credited owner, weapon/effect identity, fatal health transitions, headshot evidence, corpse ownership, and exact item transfers. The present aggregates alone do not prove these joins are available.
- Account for delayed ticks after a weapon switch, explosions and chains, NPC-caused effects, despawn, map transitions, and callback duplication. Never substitute the currently held weapon for the source of an earlier effect.
- Establish a compact event schema and storage cost using representative long runs. Measure callback CPU time, allocations, save cost, and retained UI cost before shipping. Avoid full inventory snapshots or serialization on every hit, continuous scene scans, and retained Unity object references in saved records.
- Clarify the desired history retention before imposing any limit. A complete feed must not silently discard events; any future bounded-history option needs visible coverage information while preserving aggregate totals.
- Verify export, reload, same-format recovery, and any supported format upgrade against the chosen schema. Use virtualized history rendering so opening a long feed does not create one live control per event.

The first deliverable is a native feasibility report with exact capture points, unresolved fields, and a measured storage/performance estimate. It should separate the smallest useful feed from optional loot snapshots and artwork.
