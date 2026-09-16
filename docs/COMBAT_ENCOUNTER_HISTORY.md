# Combat encounter history proposal

Scope: feature proposed in the September 12, 2026 playtest discussion and refined through the September 15 mockups and native tests. Automatic capture, incremental persistence and the retained Runs map/feed are now integrated into ordinary builds. See [production integration](COMBAT_ENCOUNTER_PRODUCTION.md) for the diagnostic separation, review corrections and September 16 focused gameplay acceptance, and [prototype evidence](COMBAT_ENCOUNTER_PROTOTYPE.md) for the historical native passes. The published GitHub 1.0.0 tag and archive remain unchanged.

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

Placement decision from the September 15 mockups: keep the run list on the left and add **Details | Map & Kills** within the selected run's right pane. Details retains the run summary, route, equipment and combat. Map & Kills contains a map selector, a map with the recorded walking path and numbered encounter markers, and an expandable chronological encounter list. No new top-level tab is required. Enemy silhouettes remain deferred.

### Approved map interactions

- Selecting a map visit shows its full extent with only that visit's recorded path and encounter markers. The user's September 15 revision after the integrated playtest replaces grouped revisits with separate chronological selections: Ground Zero → Warehouse Area (visit 1) → Cellar → Warehouse Area (visit 2). Repeated visits show the same plain map name in route order; neither paths nor markers from different visits are overlaid.
- The right-hand list always contains the selected run's complete chronological encounter feed, including when a visit with no kills or no artwork is selected. Encounter numbering is continuous across the run and identical on list rows and markers. Selecting a visit does not filter or renumber the list.
- Opening a list entry selects the exact visit where that encounter ended and focuses its positions. Selecting a map marker opens the same globally numbered entry and scrolls it into view. Only one encounter is expanded at a time. An encounter first contacted on an earlier visit belongs to its recorded fatal visit.
- Focus frames both recorded participant positions with padding, with a connecting line and distance when both endpoints belong to the displayed map. Opening another entry changes visit/focus directly. Closing the entry restores that visit's full-map overview; choosing a visit manually clears the expanded encounter while preserving the full list.
- No manual zoom buttons or mouse-wheel zoom. The automatic overview/focused states determine the map transform. Markers intentionally overlap when encounters share a position; the user accepted this behavior and requested no spreading or clustering treatment.
- Walking paths and encounter connecting lines are white with a black outline and drop shadow.
- **Same-map teleports have dotted connections between their departure and arrival points.** These connections represent a teleport, not walked movement. Preserve the exact endpoints when native evidence allows. A sampling gap or uncertain discontinuity must not be silently labeled as a proven teleport; transitions between different maps must not be drawn across one map.

### Missing map artwork

User decision: when a scene has no map image, keep the map container and replace its imagery with short, in-character flavor text, such as a jammed surveillance satellite or unavailable reconnaissance. The encounter list and individual kill/death details remain fully usable to the extent their recorded fields are available. Opening an encounter must not depend on successful map loading. Do not fabricate terrain or disable the whole map's history.

Candidate English copy, not final localization: **SATELLITE LINK LOST** / “No eyes in the sky. Your combat log made it through.” Keep a clear statement that map imagery is unavailable alongside the flavor. This is themed presentation, not a diagnostic claim that satellite jamming caused a cache/read failure. Temporary loading and actual storage errors keep their appropriate states.

### Animated route reveal — approved direction

The user approved drawing the path from map entry to its finish (next-map transition, death, extraction or last recorded point) over about 5–10 seconds, with a refinement: encounter markers must not clutter the map before the path is revealed. They should appear progressively or after the animation completes. The selected direction is progressive reveal, with completion as a fallback for markers lacking a reliable path association. This uses the planned ordered path data without additional gameplay sampling or a second persisted replay stream; exact duration and optional controls remain subject to visual qualification.

- Prefer an approximately five-second reveal when first selecting a map overview; assess ten seconds in visual qualification if five feels too fast. Show a small leading dot if useful. This is an accelerated route reveal, not a recreation of actual movement speed or stationary time.
- Reveal continuous walked sections in recorded order, weighting progress by displayed path length rather than sample count so stationary samples do not stall it. Preserve separate visits and gaps. Briefly reveal a witnessed teleport's dotted connector and jump to its arrival, without animating solid walking through that interval or allocating most of the duration to a long teleport.
- Begin the overview reveal with encounter markers hidden. Reveal each marker when progress reaches the corresponding encounter's point in that visit's recorded chronology, and keep it visible afterward. Match event time/order to path progress rather than testing proximity to the enemy marker: a ranged kill can lie off the walked path, and revisiting the same location must not reveal a later kill early. Reveal any remaining valid markers at animation completion when path coverage cannot establish a reliable association. Replaying resets marker visibility along with the route.
- Keep list entries available immediately. Selecting a kill interrupts the overview reveal and opens the normal focused view with that encounter's available endpoints. Closing it returns to the completed overview with all available markers, rather than forcing another animation. A small optional replay action could restart the reveal; no manual zoom is introduced.
- Mark actual entry/exit outcomes where recorded. Each repeated visit has its own selected route reveal, never an invented connector to another visit. Incomplete capture ends at the last observed point without implying extraction or another known finish.
- Use a UI animation clock that runs while gameplay is paused. Cancel the animation on map/run/generation changes and panel close. Precompute ordered geometry/progress once, then advance a reveal cutoff; do not deserialize, resample or rebuild the complete path each frame. Measure the rendering approach during the map prototype before claiming its cost.

The user explicitly waived backfilling existing runs. The feature captures new data; no investigation is needed into reconstructing encounter events or walking paths from old aggregates. This is not authorization to delete existing profiles.

The current mockups are maintained locally under `C:/Users/micro/OneDrive/Desktop/uds-design/current-mockup/`: `ultimate-duckov-statistics-ui-runs-details.png`, `ultimate-duckov-statistics-ui-runs-map-and-kills-full-map.png`, and `ultimate-duckov-statistics-ui-runs-map-and-kills-one-kill.png`. Their sample numbers and text are illustrative, not recorded-data contracts.

## Suggested first scope

Investigate and implement the chronological player kill entries and per-enemy damage exchange first. Link multiple hits, delayed effects, and death to one enemy instance across the encounter; keep enemies sharing a display name separate. Keep player kills separate from companion, other-NPC, environmental, and unresolved deaths.

Next, investigate enemy weapon identity and actual looted items. A first loot view should show items and quantities proven transferred from that specific corpse to the player. Inventory deltas alone do not prove this relationship. Handle partial-stack takes, transfers back, and repeat visits explicitly.

The September 15 mockups target an **Enemy Inventory & Loot** section showing observed corpse contents with taken items highlighted. Investigate this full presentation as well as the smaller proven-transfer subset before choosing implementation scope. It requires inventory-at-observation evidence and explicit rules for partial stacks, returns, generated, modified, removed or unobserved loot. Do not silently equate an unlooted corpse with an empty one, or expose contents the player has not inspected.

The accepted loot presentation uses each item's first inspected quantity, combining matching stacks in that opening. Later deposits, withdrawals and reopenings do not change that displayed observation. This is observed inventory, not the amount acquired or proof that every unit originally belonged to the enemy. Highlight an item when recorded transfers out to the player/pet exceed returns. Tooltips contain only the item name; gross Taken/Returned counters remain internal evidence. Transfer-only items without an inspected quantity have no quantity badge.

The illustration is deferred until after release and a separate user decision to resume it. If resumed, prove capture semantics and the rendering approach first. A body marker based only on headshot/non-headshot classification is schematic, not a hit-location replay.

## Deferred Overview highlights — kill distance

Idea recorded on September 15, 2026: add **longest kill distance** and **shortest kill distance** highlights to Overview. The inspiration was the user's reported 35.29 m M700 kill with an 8× scope.

Use recorded player/enemy positions at the killing blow for player-attributed kills, consistent with the distance already shown in encounter details. Missing position evidence must not become a zero-distance record. Confirm the available evidence and aggregation rules when this idea is taken up; it is not implemented or part of the current encounter feature's completion criteria. Finish that feature first.

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

Persist coordinates and stable map references, with any necessary calibration stored once per scene/run rather than per event. Do not embed map images in each event or profile. The September 15 investigation found that borrowing native scene artwork alone does not establish restart/main-menu availability; evaluate a shared first-visit image cache or prepared map catalog as described in the [feasibility report](COMBAT_ENCOUNTER_FEASIBILITY.md). Handle scene layers, interiors, missing/no-signal maps and later game map changes. Missing artwork should leave the textual event available. Reproduce the verified conversion in UDS-owned presentation without repurposing the game's active map UI.

Fatal-event coordinates and the walking path are separate capture streams. The latest mockups add continuous route recording to the earlier fatal-position-only proposal. Reuse suitable movement observations where possible, retain meaningful corners and explicit discontinuities, and keep path simplification separate from aggregate distance calculation. Persist bounded chunks rather than rewriting the whole route on each save. Callback, storage, asset-loading and rendering costs still require feasibility measurements; neither stream can be recovered from existing aggregates.

## Investigation and delivery requirements

- Inspect installed native contracts for individual actor lifetimes, damage source and credited owner, weapon/effect identity, fatal health transitions, headshot evidence, corpse ownership, and exact item transfers. The present aggregates alone do not prove these joins are available.
- Account for delayed ticks after a weapon switch, explosions and chains, NPC-caused effects, despawn, map transitions, and callback duplication. Never substitute the currently held weapon for the source of an earlier effect.
- Establish a compact event schema and storage cost using representative long runs. Measure callback CPU time, allocations, save cost, and retained UI cost before shipping. Avoid full inventory snapshots or serialization on every hit, continuous scene scans, and retained Unity object references in saved records.
- Clarify the desired history retention before imposing any limit. A complete feed must not silently discard events; any future bounded-history option needs visible coverage information while preserving aggregate totals.
- Verify export, reload, same-format recovery, and any supported format upgrade against the chosen schema. Use virtualized history rendering so opening a long feed does not create one live control per event.

The [native feasibility report](COMBAT_ENCOUNTER_FEASIBILITY.md) records exact capture concerns, unresolved fields and a measured synthetic storage estimate. The [prototype qualification](COMBAT_ENCOUNTER_PROTOTYPE.md) records the accepted route-rendering correction and targeted combat/loot evidence; it does not establish blanket native coverage or frame-time acceptance. The [incremental storage stage](COMBAT_ENCOUNTER_STORAGE.md) adds separately addressed records, bounded route chunks and consistent export/restore. The development build connects native capture to that storage and implements the linked retained map/feed UI; the first short integrated pass received positive user feedback and exported data matched closed SQLite records. The next four-visit pass confirmed the headshot correction and separate stored visits. The user accepted the revised behavior with separate visit selections and a globally numbered full-run feed, then accepted the closed-panel main-menu performance correction.

On September 15, the user approved failure handling and required design work before production integration because the initial prototype was too far from the mockups. The subsequent passes retained the accepted white outlined route/connector, automatic focus/overview states, linked map/list selection, separate repeated visits and global encounter numbering. Restart UI, failure-state presentation, localization and broader gameplay performance were deferred to final qualification at that checkpoint; the later acceptance record distinguishes user-tested behavior from automated evidence.

The subsequent design pass addressed the user's six reported mismatches: top-rounded subtabs connected to the rule; repeated map names without visit-count suffixes; circular blue encounter badges, concise outcome titles and smaller right-aligned `#b1b1b1` timestamps; smaller circular map numbers in the mock's `#275576` blue; a 3 px white route/connector with a 2 px black outline on each side and a shadow; and pointed P/E pins with localized tooltips and a smaller distance label in a translucent black box offset from the connector. Expanded entries use compact damage sections, weapon/ammunition icons and bordered loot tiles with kept-item highlighting. Hit counts are labeled as hits, never inferred ammunition consumption; effects retain their explicit label.


On September 15, the user accepted the refined design and name-only loot tooltips and authorized production integration. The earlier design-before-integration boundary is satisfied. Following review corrections, the user accepted the focused ordinary-build gameplay test on September 16. See [production integration](COMBAT_ENCOUNTER_PRODUCTION.md#september-16-2026-reviewed-build-acceptance) for the exact tested source and evidence limits; release packaging and publication remain separate.
