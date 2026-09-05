# World Generation and World Mutation

Status: Adopted direction; generator algorithms remain provisional  
Parent: [Complete game design](complete-game-design.md)  
Related: [Combat spaces, movement, and AI](combat-spaces-movement-and-ai.md) · [Player housing and building](player-housing-and-building.md)  
Index: [VRPG design index](README.md)

## 1. Purpose

VRPG needs a custom main world built around combat, exploration, objectives, and persistent outcomes rather than mining access, homesteading terrain, or Survival resource distribution. Because ordinary players cannot reshape this world, generation must guarantee that routes, landmarks, combat spaces, resources, and recovery paths work without digging or permanent block placement.

The immutable-world promise is not “the world never changes.” It means the player changes it through authored actions, skills, and outcomes rather than unrestricted terrain editing.

## 2. Main-world contract

- Custom VRPG generation is enabled by default for new VRPG worlds.
- The main world is protected from lasting player block placement, breaking, chiseling, liquids, fire, and equivalent terrain modification.
- Housing realms are the default location for freeform construction.
- Skills may create authorized temporary combat terrain with explicit limits and cleanup.
- Contextual interactions, encounters, bosses, factions, and world outcomes may cause authorized changes through the server mutation service.
- Generated traversal never assumes the player can mine a shortcut, pillar upward, bridge a gap permanently, drain a cave, or place ordinary light sources.
- Required destinations have legible, validated routes and recovery from foreseeable falls or displacement.
- Progression resources are not hidden behind ordinary block breaking.
- The generator preserves Vintage Story's voxel character and reusable assets without preserving Survival's terrain obligations.

## 3. World topology

Generation begins with a semantic graph before producing voxel terrain:

```text
World seed and rules
        ↓
Region graph and progression connections
        ↓
Landmarks, settlements, routes, expedition entrances, and encounter sites
        ↓
Terrain envelopes and traversal corridors
        ↓
Combat-space and navigation validation
        ↓
Decoration, resource interactions, and ambient population
```

The graph identifies multiple routes between broad goals, optional branches, region borders, faction territory, safe anchors, fast-travel unlocks, and deeper danger pockets. It must not quietly reconstruct a single campaign corridor at world scale.

Terrain generation supports those decisions:

- Primary and secondary routes connect required sites.
- Landmarks provide distant orientation without requiring a minimap.
- Natural barriers communicate boundaries and route choices rather than functioning as mine-through delays.
- Dangerous shortcuts trade risk for time or reward.
- Falls have a route back, bounded recovery, or a clearly telegraphed lethal boundary.
- Caves and interiors have authored entrances and complete traversal; no critical chamber assumes tunneling.
- Region transitions are readable through terrain, architecture, enemies, ambience, and rewards.

### 3.1 Half-step slope surfacing

Walkable outdoor slopes use material-matched bottom slabs or an equivalent authored half-height surface by default. The goal is a smooth half-block grade instead of repeated full-block hops.

- Primary routes target no more than one half-block of rise per horizontal block. Exact limits remain configurable by traversal profile.
- A one-block rise on a required walking route normally receives two half-step transitions or a broader terrace, not one abrupt ledge.
- Combat arenas remain broad level plateaus. Slabs smooth their entrances, edges, and intentional broad grades rather than adding noisy micro-height across the fighting floor.
- Route slabs are backed by solid support, use stable collision, preserve required headroom, and match the region's surface language.
- Bottom slabs are the normal walking surface. Top slabs, isolated half-block bumps, alternating teeth, and decorative slab gaps are rejected on required routes.
- Large natural hills use terraces and switchbacks when a continuous slab grade would become visually repetitive or exceed route limits.
- Scenery that is not classified as walkable may retain rough full-block silhouettes.

The generator should produce a half-step surface-height field before selecting blocks:

1. Solve the route corridor and desired grade.
2. Quantize its walk surface to half-block heights.
3. Place full support blocks and the correct full-block or slab surface.
4. Blend corridor shoulders into surrounding terrain without introducing reachable snag points.
5. Validate continuous clearance and traversal for every required actor profile.

Player and AI navigation must share the same sampled walk-surface height. Slabs cannot be only a visual post-process applied after navigation data is built.

## 4. Combat-space integration

The generator labels combat surfaces separately from traversal scenery.

- Routine encounter sites use the mostly level geometry contract and validated lane widths.
- Two-block-or-higher elevation inside combat areas is an intentional wall or explicit navigation feature.
- Spawn points, orbit lanes, charge lanes, range bands, formation widths, line of sight, and escape routes are validated against assigned encounters.
- Decorative voxel noise cannot create reachable safe shelves or holes inside combat envelopes.
- Transitions into combat provide enough staging space to avoid immediate unavoidable damage.
- Boss spaces declare entry lock, extraction, recovery, spectator, and multiplayer reinforcement behavior.

Terrain outside combat may be more vertically expressive, but still cannot rely on ordinary player building or digging for required travel.

## 5. Temporary combat terrain

Protecting the main world gives temporary terrain skills a distinct purpose. A player who cannot build a permanent wall at will may use an ability to create short-lived cover, split enemies, prepare a kill zone, interrupt a charge, bridge a tactical gap, or reshape a route.

Reusable construct forms may include:

- Wall, partial wall, or curved barrier.
- Pillar or single-target obstruction.
- Temporary bridge, ramp, or stepping path where a room permits it.
- Cage, ring, funnel, or one-way screen.
- Destructible cover shared by either side.
- Elemental block field that burns, freezes, conducts, spreads, or leaves an aftermath.
- Mine, ward, turret base, or trap structure attached to valid floor.

Every `TemporaryTerrainDefinition` declares shape, placement preview, valid surfaces, range, cost, duration, health, collision, allegiance, stacking rule, per-player and per-encounter limits, navigation effect, destruction response, and cleanup behavior.

Temporary terrain follows these rules:

- Placement is server-authoritative and confined to an encounter-approved mutable envelope.
- It cannot overwrite protected landmarks, objectives, exits, reward objects, or another player's protected state.
- It cannot seal every legal route, trap a boss permanently, create an unreachable firing shelf, or survive as free housing construction.
- Enemies receive a readable response: navigate around, attack it, vault or bypass when explicitly capable, use ranged pressure, or wait out a costly short duration.
- Navigation updates are local and bounded. Creating several blocks must not trigger full path reconstruction for every actor.
- Expiration and destruction restore the exact prior state transactionally, including after a crash or chunk unload.
- A construct that bridges traversal cannot become required for a player who lacks that skill unless the activity explicitly supplies an equivalent temporary power.

Temporary terrain should create multiple solutions. A wall can protect a cast, group a plague chain, redirect a charge, or isolate a shield enemy; it should not be useful only at one scripted socket.

## 6. Exploration and gathering without mining

Removing block breaking means replacing useful gathering decisions, not deleting material acquisition.

Materials may come from enemies and bosses, salvage objects, interactable resource sites, optional rooms and caches, faction rewards, NPC trade, gathering encounters, and world-state changes that expose new interactions.

An interactable resource site has a readable state, action, output family, risk, and refresh rule. Collection is quick unless the interaction itself creates a meaningful encounter choice. The world avoids required Survival deposits, sealed cavities, unstable rubble, or terrain features whose purpose is consuming tool durability.

## 7. Mutation classes

Every mutation declares a class and authority:

| Class | Examples | Rule |
|---|---|---|
| Protected world | Terrain, roads, ordinary landmarks | Players cannot permanently alter it |
| Contextual interaction | Door, lever, destructible encounter object | Only its authored operation is allowed |
| Encounter-temporary | Skill wall, hazard, barricade | Bounded and restored by encounter rules |
| Persistent outcome | Captured fort, repaired bridge, faction site | Applied by an authorized world-state transaction |
| Housing | Player home blocks and stations | Housing ownership and permissions apply |
| Administrative | Recovery, moderation, migration | Privileged, logged, and excluded from rewards |

`IWorldMutationService` is the only VRPG authority that widens protection. It records initiator, reason, mutation class, affected bounds, expected prior state, result, and world-state revision. Low-level patches and adapters consult it instead of inventing independent permissions.

Persistent outcomes prefer semantic records such as `bridge-repaired` or `fort-owner=faction-a`, then apply an idempotent authored transformation. This is safer for recovery, synchronization, regeneration, and migration than unexplained block deltas.

## 8. Generation units and lifecycle

The exact algorithm remains open, but generation uses bounded units with stable identities: world graph, region cell, route segment, landmark, settlement, encounter site, expedition entrance, and decorative terrain chunk.

Each unit records generator version, seed inputs, definition codes, semantic-state dependencies, occupied bounds, and validation result. A new generator version does not silently rewrite explored terrain. New content may populate reserved or undiscovered sites; explicit migrations preview affected regions.

The base world should be reproducible from seed, definitions, generator version, and semantic outcomes. Player inventory and housing remain separate from regenerable terrain.

## 9. Data and extension points

Provisional definitions and services:

- `WorldGenerationDefinition`: version, seed rules, region graph constraints, and global budgets.
- `RegionLayoutDefinition`: terrain identity, adjacency, scale, routes, landmarks, and threat bands.
- `RouteDefinition`: endpoints, width, slope, traversal capabilities, encounters, and shortcuts.
- `WorldSiteDefinition`: role, footprint, placement constraints, state variants, and reward hooks.
- `ResourceInteractionDefinition`: presentation, action, output, risk, refresh, and ownership.
- `TemporaryTerrainDefinition`: shape, placement, lifetime, collision, limits, navigation, and cleanup.
- `WorldMutationDefinition`: class, bounds, expected state, operation, rollback, and persistence.
- `TraversalProfileDefinition`: actor size, step, slope, jump, fall, clearance, and recovery.
- `TraversalSurfaceDefinition`: route class, half-step grade, slab palette, terrace rules, support, clearance, and navigation cost.
- `IWorldMutationService`: authorize, apply, validate, rollback, and report mutations.
- `IWorldGenerationValidator`: validate reachability, traversal, encounter geometry, and content coverage.

Addons can introduce regions, sites, routes, resource interactions, and temporary-terrain variants through validated definitions. New terrain algorithms or mutation operations require registered implementations.

## 10. Configuration and world creation

World generation is selected when a world is created:

- `Vrpg` — default, fully supported custom generator.
- `VanillaCompatibility` — Vanilla terrain with VRPG systems; useful for testing and mixed packs but without ideal traversal or encounter guarantees.

Main-world mutation is a separate ownership mode:

- `Protected` — default; only contextual, temporary, persistent-outcome, and administrative mutations are allowed.
- `Vanilla` — compatibility/sandbox mode; ordinary block permissions apply.

The default preset pairs `Vrpg` generation, `Protected` main-world mutation, and `HousingOnly` construction. Other combinations are validated and labeled by support level. Protected Vanilla terrain may run as a compatibility profile, but can contain inaccessible spaces designed around digging.

Changing the generator on an existing world requires a new world or explicit region migration. Changing mutation ownership requires a restart, compatibility audit, and warning about existing construction.

## 11. Player-facing rules

- Ordinary modification says **The main world cannot be altered. Build at Home.**
- A contextual object shows its real verb, such as **Open**, **Repair**, **Salvage**, or **Disarm**.
- A temporary-terrain skill shows its shape, duration or health, valid placement, and concrete rejection reason before commitment.
- If an interaction is locked, it shows the immediate requirement or a route to learn it.
- The map distinguishes discovered routes, safe anchors, known barriers, and entrances without exposing generator cells or internal IDs.

Repeated denials are rate-limited. Full protection and compatibility explanations belong in the handbook and world settings, not every tooltip.

## 12. Performance and multiplayer

- The server owns generation, site state, protection, temporary terrain, and persistent mutation.
- Generation is deterministic for declared inputs and split into bounded jobs.
- Graph and validation work is cached; chunks do not scan the world to rediscover their semantic role.
- Navigation metadata and combat envelopes are cached with terrain where practical.
- Temporary terrain uses bounded local navigation invalidation, strict object limits, and crash-safe cleanup records.
- Persistent outcome transactions are idempotent and survive interruption.
- Clients receive relevant state variants and deltas, not the entire world graph.
- Multiplayer mutations use expected revisions so competing actions cannot duplicate rewards or corrupt blocks.

## 13. Acceptance tests

- A new default VRPG world uses custom generation and protected mutation.
- All required regional goals are reachable without ordinary breaking or placement.
- Representative routes support player movement, multiplayer spacing, and required actors.
- Primary slopes use continuous supported half-step surfaces with no forced auto-jump, collision snag, or disagreement between player and AI traversal.
- Walking, sprinting, strafing, backpedaling, dashing, projectiles, dropped loot, and representative enemy sizes behave predictably across slab-to-full-block seams.
- Combat sites pass flatness, ledge, orbit, charge, formation, and escape-route validation.
- Every required material family has a non-mining acquisition route.
- Supported placement, breaking, chiseling, liquid, fire, and adapted mod paths cannot alter protected terrain.
- Contextual and persistent outcomes change only authorized bounds and reproduce after save/load.
- Housing building works while the main world remains protected.
- A temporary wall can redirect an encounter, receives valid AI responses, cleans up exactly, and cannot seal the only exit.
- Concurrent outcome or construct requests resolve without duplicated rewards or corrupted terrain.

## 14. Open decisions

- Macro world shape, region scale, and finite versus expanding topology.
- Terrain algorithm and how much Vanilla climate, rock, cave, and vegetation generation remains.
- Fast-travel density and permanent-shortcut rules.
- Resource-site refresh rules.
- Which temporary construct shapes are safe for the first combat slice.
- Tooling for visualizing graphs, traversal, combat envelopes, construct navigation, and mutation ownership.
- Migration strategy after generator definitions change in explored regions.
