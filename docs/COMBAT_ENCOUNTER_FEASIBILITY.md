# Encounter history feasibility — September 15, 2026

## Decision

The approved Runs **Details | Map & Kills** design is technically plausible on the inspected Duckov baseline. Proceed through small native prototypes before committing to the complete UI and capture implementation. Individual encounters, direct weapon/ammunition capture, sampled routes and observed corpse inventory have useful native contracts. Exact fatal-event timing, durable map artwork access and complete item-transfer coverage still require in-game qualification.

Three independent Astra High investigations covered combat, maps/routes and loot. A separate source audit assessed integration with the accepted incremental storage and UI. No production implementation, deployment, game launch or profile mutation was performed. A small synthetic storage experiment was run separately; it is not native performance evidence.

Existing runs require no backfill or reconstruction. Same-format recovery of newly captured data, export/restore correctness, generation isolation and safe shutdown remain requirements. Enemy silhouettes and schematic hit markers remain deferred. The [feature proposal](COMBAT_ENCOUNTER_HISTORY.md) records the approved mockup behavior.

## Evidence baseline

Production source inspected at `a5df1e9a5cfd836ada2ec80b446b60e76220366d`. Relevant native types were decompiled read-only from the installed assemblies, with existing local decompilation used for discovery:

| Assembly | SHA-256 |
|---|---|
| `TeamSoda.Duckov.Core.dll` | `298D5D5885427632D5A94B2F3CE587F8EBC9528EC71E575A475158C326ECAE8F` |
| `ItemStatsSystem.dll` | `A276E15C022F71B2214BD05E1B9B0F2E620C16561DF576FED0B79C2FE4402E60` |

The installed baseline is Duckov 2.3.30. Local detailed reports, type excerpts, map metadata and the synthetic experiment are retained under `artifacts/encounter-history/feasibility/`, in `combat/`, `maps/`, `loot/` and `storage/`. These local research files are not shipping assets. The conclusions below do not require committing native decompiled source.

## Findings by feature

| Feature | Finding | Required qualification or limit |
|---|---|---|
| Individual kills and player deaths | Native actor references and damage/death callbacks exist. | Add run/session-scoped individual identities; current persisted attacker/target keys group types. Deduplicate the player's public death notification and fatal damage observation. |
| Damage exchanged with one enemy | Individual rollups can accumulate outgoing and incoming damage. | Nested damage/healing/death callbacks complicate naive before/after health subtraction; qualify the capture boundary and prevent double counting. |
| Weapon and ammunition | Direct player and NPC projectile launch exposes useful origin evidence; melee/effects have separate source paths. | Snapshot at origin, never infer from held-at-impact equipment. Reflections, destroyed sources and mixed-source buff refreshes can leave ownership, weapon or ammo independently uncertain. |
| Fatal positions | Participant transforms can be sampled around the fatal health transition. | Capture before death cleanup; independently resolve logical scenes. A connecting line means separation of recorded positions, not a bullet trajectory or proven firing distance. |
| Walking route | Existing movement observation runs at approximately 5 Hz. | Reuse it through an independent capture stream, preserving visit and discontinuity boundaries. A sampled path cannot recover turns between samples. |
| Same-map teleports | Native placement can be correlated with a before/after observation. | `OnSetPositionEvent` fires after placement and has no departure field. Capture departure before the call and confirm arrival; render the approved dotted connection without adding it to walked distance. |
| Historical map images | Native scene settings reference usable artwork and conversion metadata. | No general named historical asset lookup was established. A durable first-visit cache or prepared catalog needs a prototype. Some scenes have no artwork. |
| Corpse inventory | An exact dying actor → character item → returned corpse box → local inventory join exists. | Record observed/revealed contents, with unseen and genuinely empty states separate. Death equipment and resulting corpse inventory can differ. |
| Taken-item highlighting | Exact quantities can be captured through item/inventory operations. | No single event covers every path. Partial merges, async splits, equips, swaps, returns, nested ammo and pet transfers need a qualified transaction/provenance layer. |

## Combat capture decisions

Use a numeric individual identity scoped to the current run and capture session; keep the enemy type/name as separate metadata. Repeated subscene visits can deactivate and reactivate the same native actor rather than create a new one. Preserve its identity across those visits, while destruction/new objects and a new capture session receive different identities. Do not join by name, location or a recycled Unity instance integer.

The native `Health.Hurt` sequence subtracts health, invokes health-change logic, may clamp a nonraid character back to life, then invokes instance death handlers, static death handlers, deactivates the object and invokes hurt handlers before the Harmony postfix. Corpse conversion and secondary damage can therefore happen before the current UDS postfix. A targeted prototype must select the smallest reliable observation point for fatal coordinates and chronological sequencing, confirm actual fatality, and isolate nested health changes. A guarded current-health mutation observation is one candidate, not an already validated patch.

Persist individual damage contributions grouped by known source and direction; a raw hit replay is unnecessary for the current mock. Keep the fatal weapon separate from all weapons contributing earlier damage. A projectile/pellet hit count does not establish ammunition rounds fired at one enemy, and misses generally cannot be attributed to a specific enemy. Replace mock phrases such as “with 4 bullets” with the actual captured metric when implementation semantics are established.

Buff refreshes can retain the first native owner/weapon while accepting more contributors. Same-player credit can remain known while the weapon or ammo becomes ambiguous. A reflected projectile can change responsible actor while retaining original gun/ammo lineage. Preserve those distinctions instead of supplying a plausible current weapon. Unknown source detail must not erase a known kill or known damage total.

## Maps, visits and teleports

Capture resolved map calibration and its artwork identity during a visit, separately from ordered visit/segment records. Native actor GameObjects can be moved into the main scene while belonging logically to a subscene; raw `gameObject.scene` is not sufficient attribution. Preserve scene identity and height independently for each participant. Do not apply outdoor calibration to an unrelated interior or silently project two different maps onto one line.

Map inspection found 16 settings components, 29 entries covering 28 unique scene IDs, and 26 non-null sprite references including one combined sprite. These include duplicate/test entries and are not a supported playable-map count. J-Lab Facility and the Hidden Warehouse cellar explicitly have null artwork with hidden/no-signal flags; Ground Zero's cave also has no sprite. StormZone B0–B4 have distinct scene artwork. Textual encounters remain usable when map imagery is absent.

Subsequent user decision: replace absent artwork with in-character reconnaissance/satellite flavor text inside the map container, while retaining normal list selection and individual kill/death details. Do not require an image or map transform to expand an encounter. Distinguish this intended no-art presentation from temporary loading or an actual failed cache read.

`PackedMapData` retains sprites and some layout metadata, but omits world centers. Native map UI still depends on live scene settings, camera and level state. It cannot simply be reopened as a historical renderer in the main menu. A UDS-owned renderer should use captured calibration, with automatic overview/encounter framing and the approved bidirectional list/marker selection.

Two concrete image-access options remain:

1. **First-visit shared image cache:** capture the map image once per artwork/calibration revision, then retain it outside per-run payloads. All inspected textures are non-readable, so direct CPU pixel reads are insufficient. A bounded offscreen copy plus supported asynchronous GPU readback and detached encoding is a candidate. Publish complete image/metadata atomically, guard scene/job lifetimes and preserve unavailable/pending states on failure. This needs GPU, memory and frame-time qualification.
2. **Prepared map catalog:** extract and validate full-canvas artwork and a versioned mapping during development. This avoids player-time GPU capture but introduces package size and baseline maintenance. Actual runtime calibration still needs capture/validation. No map pixels were extracted or shipped in this investigation.

Choose between them after a small representative map prototype. Cached/shipped images should be shared across runs. Never load gameplay scenes just to display history, retain all textures indefinitely, or destroy borrowed game resources. A 2048×2048 RGBA32 image alone is 16 MiB before extra buffers/mipmaps; encoded disk size is not texture memory. PNG sizes and actual runtime peaks were not measured. Preserve full sprite-canvas/crop transforms; trimmed texture bounds do not share the same coordinate origin automatically.

Cross-machine restore or deletion of an image cache can leave valid encounter data without artwork. The implementation must define this behavior without silently changing the existing single-JSON ZIP export contract. A prepared catalog or a separately managed image cache may be preferable to embedding image bytes in exports; this decision remains open.

Use explicit path segment kinds: walked movement, witnessed same-map teleport, observation gap, and map/visit transition. Current `MovementDisposition.Teleport` also covers long sample gaps and resume/speed anomalies, so it is not sufficient proof for a dotted teleport. A missing interval stays a gap with a reason. Map transitions and separate visits are not joined just because they share a scene ID.

Start with lossless native coordinate values and compression. Rendering-only simplification can reduce geometry while preserving the stored samples. Any persisted quantization or simplification tolerance is a separate fidelity choice, not authorization supplied by this investigation. Exact fatal/teleport endpoints and existing aggregate movement calculations remain independent.

Dense overlapping markers need deterministic selection without manual zoom: displaced numbered badges with leaders or a local choice list are candidates. The main chronological list remains an exact selection route. Do not move the recorded anchor or silently select an arbitrary hidden encounter.

The approved 5–10-second route reveal can operate entirely on the planned ordered path/visit data. The [design](COMBAT_ENCOUNTER_HISTORY.md#animated-route-reveal--approved-direction) starts with an approximately five-second overview reveal and distinct dotted teleport transitions. Encounter markers begin hidden and appear as reveal progress reaches their recorded event within the matching visit; markers with no reliable path association appear at completion. Use chronology rather than proximity to an enemy position, which may lie off the walked route. List entries remain immediately selectable; opening an encounter interrupts the reveal and returning shows the completed overview. This adds a renderer/interaction task, not another native recording requirement. Prototype a reveal cutoff over prebuilt geometry, using UI time independent of gameplay pause and cancellation on selection/close. Native rendering cost remains unmeasured; avoid full-path mesh reconstruction or data loading on each animation frame.

## Inventory and taken quantities

The corpse inventory is a local native inventory created from the dying character's item tree. Bonus items can be added before corpse construction, and some equipped items are excluded/destroyed or merged when it is built. Register exact identity at construction, then observe successfully opened/revealed contents and subsequent changes. Do not treat an unopened corpse as empty or reveal hidden item identities solely because native memory exposes them.

Native `AddAndMerge` can merge part of a stack and subsequently return false because no free slot remains. `Item.Split` subtracts first, asynchronously creates the new item, and returns a detached result. A success boolean, inventory delta or ordinary async postfix therefore cannot prove the full movement. The proposed transfer layer needs exact source/destination identity, actual quantity changes, completion-aware split provenance and nested-operation deduplication while preserving native async behavior.

Track first/latest observed quantity and gross taken/put-back quantities separately. For example: observe 20, take 6, return 2, take 3 → latest 13, gross taken 9, put back 2, net outward 7. This does not prove nine unique original enemy units or seven currently held items. Identical units become indistinguishable when the player deposits/mixes stacks; expose that ambiguity rather than inventing per-unit provenance. Player, pet and nested equipment destinations also need explicit meanings.

Keep corpse joins across subscene deactivation, and release live references on actual destruction or run/generation teardown. Player tombs have a separate native cross-run persistence mechanism; an old tomb is not a newly killed enemy. Cross-run tomb loot attribution is not automatically included by the ordinary enemy-corpse join.

## Storage and performance direction

Extend the existing incremental SQLite model; no new database technology or blanket conversion to columns is required. Keep the run summary small and use separately addressed actor/encounter/loot records, a compact list index, and immutable sealed route chunks with a bounded mutable tail. Only changed records enter the existing ordered transaction/acknowledgment boundary. Looting after a kill must not rewrite every encounter or run.

Main-thread snapshot serialization still exists before the SQLite worker commits. Putting the whole growing path inside the active checkpoint would reintroduce work proportional to run duration. Likewise, putting it all inside `RunSummary` would force the current selected-run loader and 24-entry detail cache to decode/retain unnecessary data. Stream/cache the selected map's data and visible encounter detail instead.

An offline Python experiment measured hypothetical lossless JSON/compressed records at 5 Hz, without path simplification, with eight loot rows and two damage-source rows per encounter:

| Synthetic workload | Position samples | Added SQLite storage with 256-point chunks |
|---|---:|---:|
| 23 minutes, 100 encounters | 6,901 | 282,624 bytes (276 KiB) |
| 60 minutes, 300 encounters | 18,001 | 753,664 bytes (736 KiB) |
| 120 minutes, 600 encounters | 36,001 | 1,499,136 bytes (1,464 KiB) |

These are measured results for fabricated data and a candidate representation, not expected sizes for real runs. They exclude existing UDS records, extra actor/index/calibration data, artwork, WAL, export copies and runtime overhead. Python JSON/zlib/SQLite do not measure the production .NET codec or Unity frame time. Exact bytes and values survived round-trip checks. The script used only in-memory databases and produced no large database fixtures. It establishes a useful starting point for lossless chunking, not a native performance guarantee.

Chunk capacity is not save cadence: checkpoint the bounded tail at appropriate existing save boundaries while sealed chunks stay unchanged. Preserve pending writes and ordering under transient failures; never silently truncate accepted history. Export, restart and restore must include the new records consistently with their run and generation. Add no per-hit disk writes, per-point tasks, full inventory snapshots on damage, per-frame route rebuilds or scene scans.

## Recommended next work

1. Build a small combat capture prototype proving distinct actor identity, nested fatal ordering, one player-death row and event-time endpoints. Use two same-type enemies, a weapon switch/projectile case and a death/effect case.
2. Prototype one mapped scene and one same-map teleport. Verify at least three landmarks, exact departure/arrival, dotted line, a separate gap case, shared artwork persistence after restart, and cold/steady CPU/GPU costs. Include a no-art scene fallback. Use this to choose image caching versus a prepared catalog.
3. Prototype the corpse transaction layer against partial merge, async split, equip, return, pet and nested ammo paths. Preserve full observed-inventory scope; if some operations cannot be qualified, expose coverage accurately and make any smaller release scope an explicit decision.
4. Once those foundations pass, implement incremental records and new-format export/restore, then the linked virtualized list/map UI. Measure changed-record saves and selected-map loading before a longer native playtest. Keep the accepted aggregate behavior and performance baseline intact.

Short targeted gameplay checks are appropriate for the first prototypes. A full 23-minute raid is not required to start. This audit itself performed no gameplay qualification. The subsequent [capture prototype](COMBAT_ENCOUNTER_PROTOTYPE.md) implements the first evidence collection step; its native acceptance and the later rendering/transfer work remain separate.
