# Combat encounter history proposal

Status: feature proposal from the September 12, 2026 playtest discussion. No feed capture, persistence, or UI has been implemented. Initial native map-contract findings are recorded below; full feasibility remains pending. The preceding performance and storage follow-ups were completed on September 13; this proposal still requires its own scope and qualification. This is a future feature release, not a reason to change the published 1.0.0 tag.

Decision on September 15, 2026: the user deferred enemy silhouettes with hit markers until after release. Finish release preparation first; the artwork idea and investigation below are retained for a later decision, without committing to implementation. The encounter feed and map do not depend on illustrations.

## Intended experience

A chronological kill feed within each recorded run, with expandable enemy encounters:

- When and on which map the enemy died, which enemy it was, and which weapon or effect received the player's kill credit.
- A map view of the encounter's fatal event: mark the player's and enemy's positions when the enemy dies, or the player's death location and the responsible enemy's position when known. Let the selected feed entry highlight its markers on the game's map artwork.
- Damage exchanged with that individual enemy, including the damage it dealt to the player before its death. Do not substitute totals for all enemies of the same type.
- The enemy's weapon or weapons, where the native evidence identifies them. Distinguish a weapon observed dealing damage from one merely equipped when the enemy died.
- Loot taken from that enemy, if the player looted it.
- A deferred, optional enemy illustration with separate head/body hit markers. Headshot evidence can place a marker in the head region; other hits can use a schematic body region. This must not imply measured impact coordinates without native evidence for those positions. See the preserved one-off rendering proposal below.

The feed should retain unresolved weapon/effect identities and uncertain ownership explicitly. Kill credit, weapon identity, and enemy identity are separate facts; one being unavailable must not erase the others. Old runs contain aggregates rather than these encounter-level joins, so their feeds cannot be reconstructed from existing totals.

Placement direction: integrate the feed and selected encounter's map into the existing Runs tab; do not add a new top-level tab. The user is still considering the exact layout. A possible flow is select a run, open its kill feed, then select an encounter to view its map and details. Treat this as a layout candidate, not a finalized design.

## Suggested first scope

Investigate and implement the chronological player kill entries and per-enemy damage exchange first. Link multiple hits, delayed effects, and death to one enemy instance across the encounter; keep enemies sharing a display name separate. Keep player kills separate from companion, other-NPC, environmental, and unresolved deaths.

Next, investigate enemy weapon identity and actual looted items. A first loot view should show items and quantities proven transferred from that specific corpse to the player. Inventory deltas alone do not prove this relationship. Handle partial-stack takes, transfers back, and repeat visits explicitly.

Whether to retain the corpse's entire available inventory and highlight taken items is still undecided. That option would require a separate inventory-at-observation snapshot and rules for generated, modified, removed, or unobserved loot. Do not silently equate an unlooted corpse with an empty one.

The illustration is deferred until after release and a separate user decision to resume it. If resumed, prove capture semantics and the rendering approach first. A body marker based only on headshot/non-headshot classification is schematic, not a hit-location replay.

## Deferred enemy silhouettes — one-off model rendering

The user proposed generating the artwork once during development from Duckov's bundled 3D models, then shipping small, static silhouette images with UDS. Players would not run an enemy-model renderer. This avoids manually creating an illustration for every enemy while preserving the different body shapes.

A single generic duck silhouette was rejected: enemies include wolves, dogs, spider-bots, arcade machines, snowmen, turrets, ghosts and ducks with substantially different armor. The earlier generated Scavenger X-ray concept remains a visual experiment, not an approved artwork collection or a commitment to generate every enemy separately.

### Proposed workflow if resumed

1. Inventory distinct enemy appearances and reuse a silhouette for variants that genuinely share the same visible shape. Select representative equipment deliberately where armor changes the outline. A static illustration represents an enemy type; it does not reproduce every individual's randomly generated equipment.
2. Render the actual models in a consistent neutral pose with a development-only capture tool. Prefer front views for duck-like enemies and side or three-quarter views for animals or machines whose bodies would be obscured from the front. Framing and camera angle need to be chosen per shape rather than forced onto every enemy.
3. Convert the renders into transparent silhouettes with a consistent UDS treatment. A muted fill and cyan outline, with restrained internal shading where needed for readability, are design candidates rather than an accepted final style. Automate cropping, margins and output sizing where possible.
4. Store a mapping from enemy appearance to image, orientation and suitable schematic hit-marker regions. Headshot/non-headshot evidence must not invent precise impact positions or impose duck anatomy on other enemies. Keep markers separate from the base artwork.
5. Ship only the final images and mapping with the mod; the capture tool and raw model data stay out of the runtime package. Load artwork as needed and reuse it through a bounded cache. Preserve a useful textual encounter when an enemy has no matching illustration.

The tool should be reusable when a later game version introduces or changes an enemy. This is one-off generation for a supported set of appearances, not a guarantee that the artwork will never need maintenance.

### Findings already established

The September 15 installed-asset inspection found no ready-made per-enemy portrait or full-body illustration collection. `CharacterRandomPreset` exposes `CharacterModel` and `FacePreset`; `GetCharacterIcon()` returns shared elite, PMC, boss, merchant or pet badges, or no icon. Model appearance code exposes equipment sockets and face-preset application, which are useful starting points for a capture prototype, but do not establish that a complete, isolated renderer works.

The metadata inventory covered 1,371 sprites and 4,366 textures across resources, global asset data and all 63 shared-asset files. It was a metadata inspection, not a visual review of every image. All 15 dialogue-actor components found in resources and the 63 level files had null portrait references. The 156 character presets identified include test/dummy entries and variants, not 156 distinct enemy species. Stripped custom type trees prevented full automatic preset deserialization in that inspection.

Local evidence remains under `artifacts/encounter-history/native-artwork-audit/`, including `FINDINGS.md`, image metadata, character names, dialogue portrait references and read-only inspection scripts. The generated Scavenger concept and its prompt are retained as `artifacts/encounter-history/scavenger-xray-v1.png` and `scavenger-xray-v1-prompt.txt`. These are local research artifacts; the conclusions in this document preserve the findings independently of those files.

### Work still unproven

No model-rendering prototype, silhouette collection, identity-to-artwork mapping or hit-marker UI has been implemented. The suggested first experiment is a Scavenger, an armored duck, a wolf, a spider-bot and a snowman. Check correct appearance, useful camera angles, readable output and capture effort before scaling up.

Measure compressed file size, decoded texture memory, image-loading time and cache behavior separately. A 256 × 512 image stored in an uncompressed 32-bit texture uses approximately 0.5 MiB before mipmaps and other overhead; this is arithmetic, not a measured UDS result. Small compressed files do not imply equally small texture allocations. Resolution, format, package layout and cache limits remain implementation decisions to validate with the samples. No package-size or hitch-free loading claim has been established.

## Kill and death locations on maps

Initial read-only inspection of the installed Duckov 2.3.30 assembly on September 12 confirms relevant native contracts:

- `MiniMapSettings.MapEntry` exposes a scene ID, sprite, world size, world center, image offset, and hidden/no-signal flags. Combined-map artwork and scale are also available through `IMiniMapDataProvider`.
- `MiniMapDisplay.TryConvertWorldToMinimap` subtracts the scene's map center, projects world X/Z onto the map plane, and applies the map entry's transforms. `MiniMapDisplayEntry` sizes and positions artwork using the provider's scale and offset; the display also handles rotation and content centering. Raw world X/Z cannot simply be treated as image pixels.
- `PackedMapData` retains sprite references, scale, offsets, scene IDs, and combined-map metadata. `MiniMapView.LoadData` can display that provider. This establishes a display mechanism, not a verified way to retrieve every historical map asset after a restart. World centers are resolved separately through current scene settings.
- Do not rely on the similarly named `MiniMapSettings.TryGetMinimapPosition`: in this installed version it checks the scene entry but returns the original world position without assigning the computed conversion.

Capture the two actors' world positions and their individual scene IDs at the fatal health transition, before death cleanup removes the necessary objects. Retain height so later floor handling remains possible. Bind these positions to the same encounter/event as the credited kill or player death, not a later corpse position or the player's next movement sample. Validate callback timing and object lifetimes before implementing capture.

The map UI can distinguish the player marker, enemy marker, and death marker. The user approved a connecting line and distance between the two points as part of the proposed experience. Show them when both endpoints are known and share the displayed map. They describe the two recorded positions, not a proven bullet trajectory, line of sight, or firing distance. In a delayed effect, the attack origin and the actor positions at death can differ. If an actor is gone or on another map, preserve that limitation and show only endpoints that belong to the selected map. Do not invent a remote attacker location for environmental or unresolved deaths.

Persist coordinates and stable map references, with any necessary calibration stored once per scene/run rather than per event. Borrow native artwork at display time; do not embed map images in each event or profile. Establish an asset retrieval path for base/main-menu viewing and restart, and handle scene layers, interiors, missing/no-signal maps, and later game map changes. Missing artwork should leave the textual event available. Reproduce the verified conversion in UDS-owned presentation without repurposing the game's active map UI.

Reading positions for fatal events should require no continuous location recording. Its actual callback, storage, asset-loading, and rendering costs still belong in the feasibility measurements. The existing incident profile does not contain these per-kill coordinates, so this applies to newly captured events.

## Investigation and delivery requirements

- Inspect installed native contracts for individual actor lifetimes, damage source and credited owner, weapon/effect identity, fatal health transitions, headshot evidence, corpse ownership, and exact item transfers. The present aggregates alone do not prove these joins are available.
- Account for delayed ticks after a weapon switch, explosions and chains, NPC-caused effects, despawn, map transitions, and callback duplication. Never substitute the currently held weapon for the source of an earlier effect.
- Establish a compact event schema and storage cost using representative long runs. Measure callback CPU time, allocations, save cost, and retained UI cost before shipping. Avoid full inventory snapshots or serialization on every hit, continuous scene scans, and retained Unity object references in saved records.
- Clarify the desired history retention before imposing any limit. A complete feed must not silently discard events; any future bounded-history option needs visible coverage information while preserving aggregate totals.
- Verify export, reload, same-format recovery, and any supported format upgrade against the chosen schema. Use virtualized history rendering so opening a long feed does not create one live control per event.

The first deliverable is a native feasibility report with exact capture points, unresolved fields, and a measured storage/performance estimate. It should separate the smallest useful feed from optional loot snapshots and artwork.
