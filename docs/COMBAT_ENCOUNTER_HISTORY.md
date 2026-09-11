# Combat encounter history proposal

Status: feature proposal from the September 12, 2026 playtest discussion. No feed capture, persistence, UI, or native feasibility work has been implemented. Address the current freeze investigation and storage efficiency before implementing it. This is a future feature release, not a reason to change the published 1.0.0 tag.

## Intended experience

A chronological kill feed within each recorded run, with expandable enemy encounters:

- When and on which map the enemy died, which enemy it was, and which weapon or effect received the player's kill credit.
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

## Investigation and delivery requirements

- Inspect installed native contracts for individual actor lifetimes, damage source and credited owner, weapon/effect identity, fatal health transitions, headshot evidence, corpse ownership, and exact item transfers. The present aggregates alone do not prove these joins are available.
- Account for delayed ticks after a weapon switch, explosions and chains, NPC-caused effects, despawn, map transitions, and callback duplication. Never substitute the currently held weapon for the source of an earlier effect.
- Establish a compact event schema and storage cost using representative long runs. Measure callback CPU time, allocations, save cost, and retained UI cost before shipping. Avoid full inventory snapshots or serialization on every hit, continuous scene scans, and retained Unity object references in saved records.
- Clarify the desired history retention before imposing any limit. A complete feed must not silently discard events; any future bounded-history option needs visible coverage information while preserving aggregate totals.
- Verify export, reload, same-format recovery, and any supported format upgrade against the chosen schema. Use virtualized history rendering so opening a long feed does not create one live control per event.

The first deliverable is a native feasibility report with exact capture points, unresolved fields, and a measured storage/performance estimate. It should separate the smallest useful feed from optional loot snapshots and artwork.
