# Dungeon MVP Plan

> Implementation status — 2026-07-19: data contracts, fail-loud internal
> validation, connector/footprint rotation geometry, immutable transformed
> layouts, and deterministic connector-frontier backtracking are implemented.
> The shipped four-piece Granite Halls metadata and encounter pools are covered
> by asset parsing plus multi-seed generation tests. Schematic placement,
> engine-owned asset/entity validation, Manifold hosting, encounter execution,
> progress, and authoring commands remain.

## Purpose

The dungeon MVP exists to make the RPG systems testable inside a repeatable
combat activity. It should prove that a small library of authored rooms can
produce readable, replayable rifts without requiring final loot, boss, quest,
or chart-progression systems.

The target experience is:

1. An admin creates a seeded rift instance.
2. A solo player or small party enters a finite custom dimension.
3. The layout reuses three or four room schematics across branches and loops.
4. Entering authored room zones starts staged encounters at authored spawn
   anchors.
5. The party shares room discovery and completion progress.
6. Completing at least 80% of the eligible dungeon opens the boss room.
7. Killing the boss completes the rift and exposes an exit.
8. Leaving the instance cleans up its runtime state and dimension.

## Manifold API Audit and Dependency Decision

### What Manifold 0.4.2 provides

Manifold owns the engine-facing dimension work that would otherwise have to be
implemented and maintained by VRPG:

- Allocation and recycling of Vintage Story dimension IDs.
- Runtime creation of ephemeral dimensions.
- Bounded chunk-column pre-generation before transit.
- Safe player and entity transit.
- Post-generation, pre-teleport `PlayerArriving` events.
- Per-dimension fixed spawn and travel policies.
- Dimension-aware relighting after runtime schematic placement.
- Reconnect and void-rescue behavior.
- Evacuation and forced teardown.
- Automatic ephemeral cleanup when the last occupant transits out.
- Cleanup events for companion runtime state.

Manifold does not provide room graphs, schematic placement, encounters,
exploration, objectives, boss gates, or rewards. Those remain VRPG systems.

### Decision

Do not recreate Manifold inside VRPG. The duplicate implementation would need
to own dimension allocation, transit ordering, chunk creation, reconnect
safety, lighting, persistence, and cleanup before it could place a single room.
That work does not improve VRPG's gameplay or content-production speed.

Manifold 0.4.2 or newer is a required dependency of the custom-dimension
dungeon host, but not of the RPG core or pure dungeon generator.

The preferred packaging boundary is:

- `vrpg`: RPG systems, dungeon definitions, layout generator, encounter state,
  authoring, and the overworld breach fallback. It does not reference Manifold.
- `vrpgdungeons`: a small companion code mod that depends on `vrpg` and
  `manifold >= 0.4.2` and implements the dimension-host interface with the
  strongly typed Manifold API.

This preserves the earlier self-contained-core decision without retaining the
current reflection proxy as production architecture. During the MVP, both
packages should be built and copied together so the extra package does not slow
iteration.

If packaging is intentionally deferred, making Manifold a temporary hard
dependency of the single VRPG package is acceptable for local development, but
the room and encounter runtime must still depend only on the host interface.

## Vintage Story Geometry and Rendering Findings

Vintage Story uses 32-block chunks on every axis. Terrain is tessellated and
sent by chunk. A schematic may cross chunk boundaries, but every destination
column must already exist before a multi-chunk paste.

The vanilla `BlockSchematic` format already supports:

- Capturing a selected cuboid from the server world.
- JSON serialization and deserialization.
- Block, decor, block-entity, and entity data.
- Rotation in 90-degree increments, including rotatable block entities.
- Placement through normal or bulk block accessors.

### Room envelope

Use a theme-level logical cell of 32 by 32 horizontal blocks for the first
release.

- Standard piece: `32 wide × 16 high × 32 long`.
- Long piece: `32 wide × 16 high × 64 long`.
- A rotated long piece occupies `64 × 16 × 32`.
- Floor Y is aligned to a vertical chunk boundary, initially Y=32.
- Authored content stays below Y=48 so a standard room occupies only one
  vertical chunk band.
- Each generated dungeon includes at least one chunk of non-playable margin.

One horizontal room cell per chunk is a production rule, not an engine
requirement. It gives VRPG:

- Constant-time mapping from player position to room placement.
- Predictable pre-generation bounds.
- Simple overlap tests using integer cell coordinates.
- Natural chunk culling and bounded client meshes.
- Clean placement of a later two-cell hallway.
- An exact in-game export envelope that is easy to validate.

A 32-block envelope does not require every room to be a wide-open 30-block
square. Interior walls, pillars, ramps, funnels, and inaccessible decorative
space can produce much smaller combat silhouettes inside it.

### Boundaries and doors

Rooms are authored closed by default. A connector describes an aperture in an
outer wall. When two connectors are joined, the assembler carves the combined
aperture through both boundary walls. Unused connectors remain sealed without
requiring a special cap schematic.

The initial standard connector is:

- Centered on a cell side.
- Six blocks wide.
- Five blocks high above the room floor.
- Two blocks deep across the adjoining boundary walls.
- Socket type `vrpg:standard`.

Later themes may define narrower, taller, locked, vertical, or specialized
socket types. The generator must never join different socket types unless a
data-defined adapter explicitly allows it.

## Runtime Architecture

The implementation is divided into pure planning, Vintage Story content
placement, and Manifold hosting.

```text
Room/theme/encounter JSON
          │
          v
 Definition validation
          │
          v
 Seeded layout generator ──> immutable DungeonLayout
          │
          v
 DungeonSession runtime ───> encounters / progress / boss gate
          │
          v
 IDungeonInstanceHost
          │
          v
 Manifold host companion ──> ephemeral dimension / transit / relight / cleanup
```

### Required boundaries

`DungeonLayout` is an immutable, serializable result containing:

- Seed and theme code.
- Grid origin and complete occupied-cell map.
- Room placements with room code, rotation, and footprint.
- Joined connector pairs.
- Start position, boss gate, and boss room.
- World-space transformed zones and anchors.
- Total exploration/completion weight.

`IDungeonInstanceHost` is the only API allowed to mention dimension lifecycle.
Its contract needs operations equivalent to:

- Create an instance from an immutable layout and construction callback.
- Notify VRPG after destination columns exist but before the first teleport.
- Teleport one player or a party into the fixed start position.
- Return a player to the overworld's last-visited position.
- Relight the pasted dungeon bounds.
- Destroy or force-destroy an instance.
- Notify VRPG when the hosted dimension is destroyed.

The layout generator, validator, progress tracker, and encounter state machine
must run in unit tests without Vintage Story or Manifold.

## Data Contracts

### Theme definition

A dungeon theme selects compatible rooms and establishes shared geometry and
generation budgets.

```json
{
  "code": "vrpg:granite_halls",
  "name": "Rust-Worn Granite Halls",
  "cellSize": 32,
  "roomHeight": 16,
  "floorY": 32,
  "standardDoorWidth": 6,
  "standardDoorHeight": 5,
  "targetPlacements": { "min": 9, "max": 12 },
  "minimumBossDistance": 5,
  "loopChance": 0.25,
  "bossUnlockPercent": 0.8,
  "roomThemes": ["vrpg:granite_halls"],
  "encounterPools": ["vrpg:granite_halls_normal"],
  "bossEncounter": "vrpg:granite_halls_boss"
}
```

The exact field names may change during implementation, but every value above
must remain data-authored and validated.

### Room piece definition

Room metadata is separate from the vanilla schematic JSON. A content author can
edit either file without writing C#.

```json
{
  "code": "vrpg:granite_crossroads",
  "name": "Granite Crossroads",
  "schematic": "vrpg:schematics/dungeons/granite/crossroads.json",
  "themes": ["vrpg:granite_halls"],
  "roles": ["normal", "junction"],
  "weight": 100,
  "footprint": { "widthCells": 1, "lengthCells": 1 },
  "allowedRotations": [0, 90, 180, 270],
  "completionWeight": 1,
  "countsForBossUnlock": true,
  "connectors": [
    {
      "id": "north",
      "side": "north",
      "socket": "vrpg:standard",
      "offset": 16,
      "floorOffset": 1,
      "width": 6,
      "height": 5,
      "allowRoomCodes": []
    }
  ],
  "zones": [
    {
      "id": "main-discovery",
      "kind": "discovery",
      "min": [2, 1, 2],
      "max": [29, 12, 29]
    },
    {
      "id": "main-fight",
      "kind": "encounter",
      "min": [6, 1, 6],
      "max": [25, 10, 25],
      "encounterPool": "vrpg:granite_halls_normal",
      "requiredForCompletion": true
    }
  ],
  "anchors": [
    {
      "id": "north-lane-a",
      "kind": "mob-spawn",
      "position": [16, 1, 5],
      "facing": "south",
      "tags": ["north-lane", "melee"]
    }
  ]
}
```

Connector restrictions use two layers:

1. Socket compatibility handles the common case.
2. Optional `allowRoomCodes`, `denyRoomCodes`, or connector-specific lists
   handle authored exceptions.

An empty allow-list means unrestricted. If either endpoint has a non-empty
allow-list, both endpoints' restrictions must accept the other room and
connector. Whitelists must not bypass socket compatibility.

### Encounter definition

Encounter definitions are also data. A room references a pool rather than
hard-coding creatures so the same schematic can play differently.

The MVP encounter contract needs:

- Weighted encounter variants.
- One or more timed or clear-gated waves.
- Creature codes and provisional VRPG level/rarity overrides.
- Spawn-anchor tag filters.
- Per-anchor and total concurrent spawn caps.
- Minimum distance from every player.
- Optional line-of-sight avoidance.
- Completion when all required waves have spawned and their tagged mobs die.

Every spawned entity receives watched attributes for dungeon session, room
placement, encounter, wave, and owning party. This is the authoritative link
used for death tracking and cleanup.

## Layout Generation

Use deterministic randomized backtracking over a connector frontier. It is a
small constraint solver, not unconstrained random placement.

### Generation order

1. Resolve the theme and seed.
2. Place exactly one start room at cell `(0, 0)`.
3. Add its compatible connectors to the frontier.
4. Repeatedly select a frontier connector and choose a weighted room,
   rotation, and connector that:
   - matches the socket;
   - passes both endpoints' allow/deny rules;
   - does not overlap occupied cells;
   - stays inside the theme's generation radius;
   - preserves at least one viable frontier when more rooms are required.
5. Prefer placement roles that keep the current branch and junction budgets in
   range.
6. After the minimum ordinary-room count, place one boss room at or beyond the
   configured graph distance.
7. Join adjacent compatible open connectors with the theme's loop chance.
8. Leave every remaining connector sealed.
9. Run graph validation and either accept the layout or retry from the same
   seed with a deterministic attempt index.

### Required topology validation

Every accepted layout must satisfy:

- No occupied-cell overlap.
- Every placed room is reachable from the start.
- Exactly one start room and one boss room.
- The boss room has no path that bypasses its gate.
- The boss is at least the minimum graph distance from the start.
- All connector joins are geometrically opposite and socket-compatible.
- At least one branch or loop exists for layouts large enough to support one.
- At least 80% of completion weight is reachable before opening the boss gate.
- Every required encounter has enough compatible spawn anchors.
- The generated bounds plus one chunk of margin fit Manifold's radius limit.

Generation failure must report the theme, seed, attempt, frontier, and rejected
constraint. It must never silently emit a disconnected or overlapping layout.

### Making three or four schematics replayable

The first useful content set can contain only four schematics:

1. Start/exit room.
2. Multi-connector traversal or junction room.
3. Combat chamber or corridor room.
4. Boss room.

The generator reuses the two ordinary pieces across 9–12 placements. Variation
comes from rotation, sealed versus connected sides, branches, loops, encounter
variants, spawn-anchor choices, and different boss distance. This is enough to
test the system, although a release theme should eventually have more visual
silhouettes.

The automated test suite must generate thousands of valid layouts from this
minimal four-piece pool. If that pool frequently dead-ends, the grammar is too
dependent on content volume.

## Instance Construction with Manifold

Do not paste room schematics from `IWorldgenStrategy.GenerateColumn`. A long
piece crosses columns, while a Manifold worldgen callback is scoped to one
currently generating column. Writing the complete schematic there risks writes
to columns that do not yet exist.

Use this lifecycle instead:

1. Generate the complete immutable layout before dimension creation.
2. Create a unique runtime dimension such as `vrpg:rift-<session>-<seed>`.
3. Mark it `Ephemeral`.
4. Attach a minimal void worldgen strategy.
5. Set a fixed spawn inside the start room.
6. Use bounded generation, with a radius derived from layout bounds plus one
   margin chunk. Do not use streaming for finite MVP dungeons.
7. Use `WithDarkSky` above the room envelope to avoid custom-dimension
   full-bright skylight.
8. Begin party transit.
9. On Manifold's `PlayerArriving`, after all destination columns exist and
   before the first player teleports:
   - atomically transition the session from `Planned` to `Building`;
   - load and rotate each schematic;
   - paste all blocks through one bulk accessor;
   - carve joined connector apertures;
   - place generic boss-gate blocks or the configured gate volume;
   - commit once;
   - call Manifold's dimension-aware `RelightRegion` for the bounded dungeon;
   - mark the session `Ready`.
10. Allow the first and subsequent party members to enter only after `Ready`.

The build guard prevents two nearly simultaneous party transits from pasting
the same layout twice.

The fixed positive origin should remain far from the engine's world corner,
initially chunk `(32, 32)` or block `(1024, 1024)` horizontally.

## Exploration, Completion, and Boss Gate

Track room placements, not unique room definitions and not raw blocks.

Each placement has these states:

```text
Unseen -> Discovered -> EncounterActive -> Completed
```

- Entering a discovery zone marks the placement discovered for the whole
  party.
- A room without a required encounter completes on discovery.
- A room with required encounter zones completes only after all of those
  encounters clear.
- A two-cell room is one placement and contributes its weight once.
- Start, exit, and boss rooms default to `countsForBossUnlock: false`.

This separates map exploration from combat completion and prevents simply
sprinting through every trigger from opening the boss.

Boss progress is:

```text
completed eligible weight / total eligible weight
```

The gate opens when the ratio is at least the theme's configured value,
initially 0.80. The server sends party members a compact tracker containing:

- Theme or rift name.
- Completed weight and total weight.
- Percentage.
- Active encounter or objective.
- Boss-gate locked/unlocked state.

The boss room is physically sealed before unlock. When the threshold is met,
the runtime removes or opens the generic gate volume, updates collision and
lighting, sends a clear event to every party member, and marks the gate
permanently open for that session.

Entering the boss encounter zone starts the boss once. Boss death completes the
session and activates the exit. Rewards and chart mutation may initially be an
admin-visible placeholder event.

## Encounter Staging Rules

Enemy staging must remain authored and readable:

- Spawn only at room anchors selected by encounter data.
- Never spawn inside the trigger zone directly on the entering player.
- Enforce a minimum player distance before choosing an anchor.
- Prefer anchors outside every player's current view when enough anchors exist.
- Allow visible spawns only when paired with a portal, nest, breach, or other
  explicit arrival effect.
- Cap simultaneous living mobs per room and per session.
- Do not activate a room twice.
- Clean up every session-tagged mob when the dimension is destroyed.

For the first MVP, ordinary drifters are sufficient. Creature rarity, affixes,
party scaling, and final rewards can attach later without changing room data.

## In-Game Authoring Workflow

Do not depend on Creative mode's internal WorldEdit implementation. VRPG can use
the public `BlockSchematic` API directly and expose a small admin workflow.

### Minimum commands

```text
/vrpg dungeon author begin <room-code>
/vrpg dungeon author pos1
/vrpg dungeon author pos2
/vrpg dungeon author connector add <id> <side> [socket]
/vrpg dungeon author zone add <id> <discovery|encounter|boss>
/vrpg dungeon author anchor add <id> <mob-spawn|player-start|gate>
/vrpg dungeon author validate
/vrpg dungeon author save
/vrpg dungeon author cancel
```

Positions use either the block under the crosshair or the player's current
block when no block is selected. A later authoring wand or UI can call the same
draft service.

### Save output

`save` writes two files:

- A vanilla `BlockSchematic` JSON containing the selected build.
- A room-piece JSON containing themes, footprint, connectors, zones, anchors,
  weights, and the schematic asset path.

The default export folder is under the server's VRPG data directory. A
development-only configurable export root may point at the repository assets
folder; no absolute development path belongs in runtime code.

The exporter must:

- Require an exact 32×16×32 or supported long-piece selection.
- Normalize the schematic to canonical north orientation.
- Reject players and ordinary entities inside the selection.
- Reject containers or other exploitable block entities unless explicitly
  whitelisted.
- Validate all connector, zone, and anchor positions against the envelope.
- Emit stable, deterministic JSON suitable for source control.
- Never overwrite an existing room without an explicit force flag.

The exported files can be copied into `assets/vrpg/vrpg/rooms/` and
`assets/vrpg/schematics/dungeons/`, edited manually, and loaded like any other
data content. Live reload is a development convenience, not a server gameplay
feature.

## Content and Runtime Enforcement

Invalid dungeon content follows VRPG's existing fail-loud policy. Startup
validation must reject:

- Wrong schematic dimensions.
- Missing schematics, themes, sockets, encounters, or creature codes.
- Connectors away from an external wall.
- Apertures outside the schematic envelope.
- Duplicate codes or local IDs.
- Zones or anchors outside their room.
- Rotations incompatible with a long footprint.
- Required encounters without enough spawn anchors.
- Themes without a start, ordinary expansion candidate, and boss room.
- Theme pools that cannot produce valid layouts in a bounded validation sweep.

Runtime generation errors should abort the chart attempt cleanly, preserve the
overworld party state, and log a reproducible seed. They should not crash the
server after content passed startup validation.

Dungeon dimensions are protected combat spaces. Normal players cannot place or
break blocks. Admin authoring mode is the explicit exception. Interactions with
approved doors, switches, containers, and objective blocks remain data-driven.

## Performance Rules

- Generate the finite layout before dimension creation.
- Use bounded Manifold generation, not streaming.
- Paste once through a bulk accessor and synchronize once.
- Relight once per constructed dungeon or a small bounded set of room regions;
  never relight per placed block.
- Map player positions to room cells with an integer dictionary lookup.
- Check party room transitions at 200–500 ms, not every render frame.
- Tick active encounters only; dormant and completed rooms do no periodic work.
- Enforce room and session mob caps.
- Keep the first vertical slice to approximately 9–12 placements and one
  vertical chunk band.
- Profile first-entry construction, server tick time during hordes, client
  chunk tessellation, cleanup, and repeated instance-ID reuse.

## MVP Scope

### Required

- Strongly typed Manifold 0.4.2 host behind `IDungeonInstanceHost`.
- Ephemeral per-run dimensions and safe return to overworld.
- Room, theme, connector, zone, anchor, and encounter JSON definitions.
- Vanilla schematic load, rotate, bulk paste, and in-game export.
- Deterministic backtracking layout generator.
- Four-piece Granite Halls test pool reused across 9–12 placements.
- At least one branch or loop.
- Encounter triggers and authored spawn anchors.
- Shared party discovery/completion tracker.
- 80% boss-gate rule.
- One placeholder boss and exit.
- Admin create, seed, inspect, reveal, complete, exit, and destroy commands.
- Full instance and mob cleanup.

### Explicitly deferred

- Final Rift Chart acquisition and modification.
- Final loot generation and salvage loop.
- Enemy rarity and affix behavior.
- Survival/horde objective family.
- Puzzles beyond generic gates or switches.
- Vertical connectors and multi-floor layouts.
- Pieces larger than 1×2 cells.
- Final boss mechanics and art.
- Public graphical room editor.

## Test Plan

### Pure tests

- JSON parsing and validation failures.
- Connector/socket compatibility.
- Rotation transforms for footprints, connectors, zones, and anchors.
- Occupancy and overlap checks.
- Graph reachability and boss-gate bypass rejection.
- Completion-weight and 80% threshold math.
- Room and encounter state transitions.
- Deterministic output for a fixed seed.
- Thousands of seeds using only the four-piece MVP pool.

### Engine integration tests

- Create and enter an ephemeral instance.
- Verify all expected columns exist before schematic paste.
- Verify 1×1 and rotated 1×2 pieces align without seams.
- Verify block entities and rotated blocks survive paste where permitted.
- Verify lighting after `WithDarkSky` and `RelightRegion`.
- Trigger a room once and verify mobs use authored anchors.
- Kill tagged mobs and verify shared party completion.
- Reach 80%, open the boss gate, kill the boss, and exit.
- Disconnect and reconnect during a live instance.
- Leave as the final occupant and verify automatic cleanup.
- Force-destroy an occupied test instance and verify safe evacuation.
- Create and destroy repeated instances to catch recycled-dimension stale data.

## Implementation Order

1. Freeze schemas and write validators plus pure test fixtures.
2. Implement rotations and the immutable seeded layout generator.
3. Introduce `IDungeonInstanceHost` and replace the reflection production path.
4. Add the strongly typed Manifold companion host.
5. Add schematic loading, bulk assembly, apertures, dark sky, and relighting.
6. Add admin test-instance commands and safe transit/cleanup.
7. Add room-position tracking and the shared progress HUD.
8. Add data-driven encounter triggers, spawn anchors, and tagged-mob tracking.
9. Add boss gate, placeholder boss, completion, and exit.
10. Add in-game room selection/export and validation commands.
11. Build the four-piece Granite Halls slice and run performance tests.

This order produces a traversable generated dungeon before encounter work, then
adds the minimum gameplay loop needed to test VRPG builds.

## Decisions Still Needed Before Final Content Balance

These do not block the layout and authoring foundation:

1. Whether a normal rift should target 9–12 or a different placement count and
   duration.
2. Whether discovery-only side rooms should contribute the same completion
   weight as combat rooms.
3. Whether active encounters temporarily lock their room exits.
4. Whether death returns the player to the rift start, the overworld, or a
   limited rift checkpoint.
5. Whether disconnected party members reserve an ephemeral instance
   indefinitely or only for a configured grace period.
6. Whether the first boss gate is a generic removable barrier volume or a
   reusable block entity with animation and interaction state.
7. Whether room connector whitelists identify whole room codes, connector IDs,
   or both. The schema above supports both, but the first authoring UI may expose
   only room-code restrictions.
