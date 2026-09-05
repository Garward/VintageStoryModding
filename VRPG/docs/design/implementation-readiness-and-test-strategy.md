# Implementation Readiness and Test Strategy

Status: Living coverage audit and recommended implementation order  
Index: [VRPG design index](README.md)  
Master: [Complete game design](complete-game-design.md)

## 1. Purpose

This document answers two questions:

1. Is a VRPG area defined well enough to implement without inventing its design inside the code?
2. What evidence must exist before that implementation becomes a trusted foundation for later systems?

The goal is not to settle every balance number before prototyping. It is to distinguish deliberate experiments from unspecified architecture and to prevent hidden Survival assumptions from becoming permanent dependencies.

## 2. Readiness scale

| Grade | Meaning |
|---|---|
| **A — Ethos** | The player value and non-goals are clear |
| **B — Contract** | Ownership, data boundaries, authority, configuration, and failure behavior are specified |
| **C — Strategy** | Implementation sequence, bridge or patch points, instrumentation, and tests are specified |
| **D — Proven** | A representative prototype passes automated, multiplayer, performance, and player-use tests |

A system may enter exploratory prototyping at A. It should not become a dependency for several other systems until B and C are documented. Nothing is treated as architectural bedrock until D.

## 3. Current coverage audit

| Area | Current strength | Missing before direct implementation | Current grade |
|---|---|---|---|
| Vision and agency | Clear pillars, non-goals, decision timescales | Periodic playtest rubric and decision-density telemetry | B |
| Survival conversion | Clear gate plus an initial character-stat and Vanilla-class ownership audit | Complete remaining system groups and exact Harmony/API interception map | B |
| Skills and statuses | Existing fun projectile foundation; transformations, propagation, setup/payoff, aftermath direction | Unified cast state machine, effect transaction order, interruption, prediction, stacking, immunity, and rollback contracts | B |
| Combat movement | Responsive burst/kiting contract | Numeric player profiles, camera assumptions, collision/ledge prototype, latency behavior | B |
| Enemy AI | Layered compositional design and initial behavior vocabulary | Concrete interfaces, scheduler budgets, Vanilla AI replacement boundary, debug overlay | B |
| Combat spaces | Flatness, anti-cheese, lanes, formation and temporary-terrain requirements | Exact validation metrics and representative arena corpus | B |
| Custom worldgen | Protected-world topology, mutation classes, half-step routes, generation units | Macro algorithm choice, chunk pipeline, palette system, site placer, migration tool | B |
| Expeditions | Goals, room roles, branching, run resources and extraction ethos | Full lifecycle/state machine, persistence, disconnect/rejoin, seed ownership, director and reset rules | A |
| Inventory/storage | Detailed hybrid aggregate model, UI contract, migration principles | Concrete schema, Vanilla inventory bridge, concurrency prototype, benchmark | B |
| Loot/itemization | Reward layers, targeting and transformative uniques | Item power budget, affix grammar, drop generation, binding/trade, salvage and bad-luck rules | A |
| Crafting/economy | Low-chore direction and housing transformation loop | Recipe transaction model, sinks/sources, pricing, vendor behavior, exploit constraints | A |
| Housing/building | Clear realm, permissions, configuration and player flow | Backend choice, allocation lifecycle, supported-block audit, first progression loop | B |
| Character progression | Classes, talents, experimentation and bounded growth direction | Level curve, respec policy, equipment requirements, class switching, account/character scope | A |
| Rebirth | Purpose, retained/reset categories and choice direction | Exact eligibility, cap, migration transaction, multiplayer ownership and second-life content algorithm | A |
| Regions/factions/NPCs | Elemental identity and world-state intent | Reputation, dialogue, NPC service, settlement, contract and narrative delivery specifications | A |
| Multiplayer | Cooperative principles and server authority appear throughout | Scaling, loot ownership, party state, late join, reconnect, host failure, permissions and abuse policy | A |
| Player information | HUD and several screen contracts exist | Onboarding, settings, accessibility matrix, controller flow, map, housing and expedition end-to-end prototype | B |
| Save/migration/recovery | Individual specs mention revisions, snapshots and idempotence | One global persistence architecture, schema versioning, backup, repair, downgrade and partial-failure policy | A |
| Content authoring | Data-first definitions and validators are a pillar | Schema lifecycle, override rules, dependency reporting, author preview tools and compatibility guarantees | B |
| Mod compatibility | Strict total-conversion tiers and a Home-content boundary are defined | Manifest schema, adapter registry, conflict detector and missing-block prototype | B |
| Realm runtime | Internal ownership, selective Manifold derivation, logical instances, lifecycle, recovery and tests are specified | Freeze API/schema, implement selected components and prove parity | C |
| Observability/performance | Several subsystem budgets and debug fields are named | Whole-game budgets, profiler counters, soak scenarios, warning thresholds and benchmark runner | A |
| Audio/visual/readability | Strong combat FX foundation and defeat-response direction | Audio grammar, effect priority, photosensitivity options, corpse/aftermath budget and large-fight readability tests | B |

The grade describes documentation and evidence, not importance or code quality. Existing VRPG combat, UI, data loading, dungeon layout, scaling, status, and visual tests provide useful foundations but do not yet prove the total-conversion architecture.

## 4. Critical missing specifications

These are required before broad implementation proceeds.

### 4.1 Total-conversion ownership and patch map

For every Survival system, record:

- Owning assemblies, systems, assets, configs, packets, saves, commands, handbook entries, and HUD surfaces.
- Activation timing and client/server side.
- VRPG disposition and effective feature owner.
- Preferred disable method: configuration, asset replacement, public API, event interception, or Harmony patch.
- Patch target and expected original behavior when Harmony is unavoidable.
- Dependencies and compatibility fallback.
- A boot, interaction, persistence, and multiplayer test.

This map is the prerequisite for safely disabling hunger, backpacks, block mutation, Survival combat and healing, durability chores, tutorials, recipes, traders, spawning, and other inherited loops.

### 4.2 Runtime composition and capability registry

The mod needs one server-owned composition root that resolves preset, world-creation choices, server overrides, addon capabilities, dependencies, and conflicts before gameplay services start.

The resulting immutable `VrpgRuntimeProfile` should answer questions such as:

- Who owns player inventory?
- Can this player mutate this position through this operation?
- Which generator created this world?
- Is housing available, and through which backend?
- Which native addons and Home-content adapters are admitted?
- Which combat, death, crafting, economy, and Rebirth rules are active?

Harmony prefixes, UI, commands, data validators, and addons consult capabilities or services. They do not reread loosely related config booleans and derive different answers.

### 4.3 Global persistence and migration

Define separate versioned stores for world profile, semantic world outcomes, site state, player progression, inventory, housing ownership, homes, expeditions, parties, and Rebirth history. Every multi-store transition needs an idempotent transaction or recovery record.

The design must cover clean shutdown, crash during mutation, disconnect, missing addon content, schema upgrade, rejected downgrade, backup verification, repair tooling, and moving between supported presets.

### 4.4 Combat simulation contract

Lock the authoritative order for input, targeting, cost reservation, cast start, telegraph, movement commitment, hit resolution, damage, status operations, propagation, defeat response, loot, cooldown, interruption, cancellation, and reconciliation.

Without this, every new skill risks defining incompatible rules for latency, simultaneous hits, resource refunds, death, and status timing.

### 4.5 Expedition lifecycle

Specify the state machine from planning through allocation, generation, entry, active rooms, branches, checkpoints, boss state, extraction, failure, cleanup, reconnect, abandonment, and archival. State ownership must be explicit for solo, party, and public servers.

### 4.6 Economy and reward budget

Define material sources, crafting transformations, NPC buyers, salvage, currency sinks, item value, region targeting, duplicate protection, trade permissions, and inflation controls as one economy. Housing processing cannot be balanced independently from drops and vendors.

### 4.7 Onboarding and first-hour contract

Define what the player can do immediately, how they learn movement and skills, when they see Home, how they choose the first objective, what failure costs, and which Survival expectations must be explicitly contradicted. The first hour must demonstrate VRPG's loop without a handbook prerequisite.

The adopted first-run screen, starter-grant, protection, recovery, and information
hierarchy are specified in [Character creation and first-run onboarding](character-creation-and-onboarding.md).
First arrival uses a validated private space as its primary safety boundary.
Guide acknowledgement and protection release are separate server transitions:
release requires both acknowledgement and a valid safe state.

### 4.8 Owned realm runtime

Replace the optional reflective Manifold boundary with [VRPG's focused realm architecture](realm-runtime-architecture.md). Allocation, generation, transit, persistence, multiplayer isolation, quarantine, recovery, and cleanup must reach parity before expeditions or housing treat it as bedrock.

## 5. Recommended architecture spine

Implementation should converge on explicit services rather than a larger `RpgModule` or inheritance tree:

```text
VrpgRuntimeProfile / Capability Registry
                    ↓
World generation ─ World mutation ─ Housing/build access
                    ↓
Encounter director ─ Combat simulation ─ AI/navigation
                    ↓
Rewards/economy ─ Inventory/storage ─ Progression/Rebirth
                    ↓
Server presentation events and immutable client views
```

Cross-cutting services provide clocks and deterministic random streams, stable identities, transactions/revisions, persistence/migration, permissions, telemetry, and validation.

Vintage Story adapters sit at the outside: entity and block events, inventory bridges, chunk generation, network messages, GUI, assets, claims, and Harmony patches. Domain services should remain testable without booting the game wherever the engine API is not the subject under test.

## 6. Slab-slope implementation strategy

The world generator should model walk surfaces at half-block resolution before choosing voxel blocks.

Provisional default for primary routes:

- Maximum grade: `0.5` vertical blocks per `1` horizontal block.
- Minimum clear route width: driven by party and actor traversal profiles, not one hard-coded player width.
- Arena cores: level plateaus; use half-step surfaces for broad authored grades and approaches only.
- A full-block elevation change requires at least two horizontal samples or a deliberately tagged stair obstacle.

Suggested components:

- `RouteGradeSolver`: produces continuous corridor heights from graph endpoints and terrain constraints.
- `HalfStepSurfaceQuantizer`: converts heights to integer and half-integer walk levels without sawtooth noise.
- `TraversalSurfacePainter`: selects full blocks, matching bottom slabs, supports, shoulders, and region palette.
- `TraversalRaster`: shared immutable height/clearance/cost data for validators and initial navigation generation.
- `TraversalValidator`: exercises each actor profile and reports the exact disconnected or invalid segment.

The first test fixture should include straight rises, diagonal rises, switchbacks, slab/full seams, wide group slopes, arena entrances, drops, temporary walls on slopes, and transitions across chunk boundaries.

## 7. Testing layers

### 7.1 Pure domain tests

Run without Vintage Story where possible:

- Damage, status, propagation, item generation and resource calculations.
- Utility scores, intent hysteresis, formation claims and deterministic random streams.
- Region graphs, route grades, room graphs, reward coverage and world-state transitions.
- Inventory transactions, collision buckets, migration, pricing and Rebirth transforms.

### 7.2 Definition and content tests

Every build validates schemas, references, tags, feature dependencies, localization keys, reward reachability, encounter compatibility, and forbidden ownership combinations. Coverage tests prove every required progression resource, damage response, region role, and first-hour instruction has at least one eligible source.

### 7.3 Generative world and expedition tests

Run thousands of deterministic seeds through fast validators. Record and minimize failures for:

- Disconnected goals and circular progression locks.
- Routes exceeding half-step grade or clearance.
- Arena ledges, blocked exits, invalid formation or charge lanes.
- Missing reward families or impossible objectives.
- Temporary terrain sealing all routes.
- Excessive generation cost, site overlap, or empty regions.

Persist failing seeds as regression fixtures.

### 7.4 Engine integration tests

Boot a dedicated test world and exercise actual engine boundaries: patches, block collision, slabs, dimension travel, chunk unload/reload, save/load, packets, inventory bridges, claims, temporary blocks, entity despawn, and addon absence.

### 7.5 Multiplayer and failure tests

Test at least solo, two-player party, late join, reconnect, concurrent interaction, high latency, packet duplication/reordering where simulated, disconnect during a transaction, server restart during generation, and ownership changes.

### 7.6 Performance and soak tests

Measure representative populated states rather than empty worlds:

- Mixed-role encounter at target actor count with path invalidation and status propagation.
- Temporary terrain creation/destruction under combat load.
- World generation plus streaming traversal.
- Large aggregate inventory, loot burst, search, crafting, and persistence.
- Multiple occupied and unoccupied homes.
- Long-running world outcomes and repeated expedition cleanup.

Budgets must be recorded for server tick time, allocations, route queries, generation latency, serialized bytes, client frame time, UI rebuilds, save duration, and recovery duration. Exact thresholds are a pre-production decision based on the target server profile.

### 7.7 Player-use tests

Automated validity does not prove agency or feel. Instrument and observe:

- Whether players identify multiple viable goals.
- Decisions per encounter and expedition segment.
- Time spent fighting versus sorting, walking, waiting, or reading menus.
- Frequency and cause of terrain cheese, navigation failures, and unclear hits.
- Whether temporary terrain produces several strategies or one exploit.
- Whether players understand protected terrain and Home without repeated denials.
- Where players stop because they made a satisfying choice versus because the game became repetitive.

## 8. Vertical implementation gates

### Gate 0: Total-conversion shell

- Runtime profile and capability report.
- Owned realm registry, manifest and transition prototype; Manifold remains only as a temporary parity fallback.
- VRPG world creation preset.
- First Survival ownership audit and safe feature disable checks.
- Versioned world/player stores with backup and migration harness.

### Gate 1: Protected combat sandbox

- Protected main-world mutation and Housing-only building mode.
- Flat arena plus slab-smoothed approaches.
- Burst movement and representative projectile skill.
- Advance, range, orbit, shield, and charge enemies.
- Temporary wall with AI response and cleanup.
- Performance/debug overlays for movement, intent, paths, and mutation.

### Gate 2: One complete expedition

- Planner, generated route, three consequential choices, boss, extraction, failure, reconnect and cleanup.
- One propagation kit and multiple defeat responses.
- Targeted reward family and inventory delivery.
- Seed regression and multiplayer integration suite.

### Gate 3: Return-home loop

- Fixed typed inventory and indexed materials.
- Persistent Home, building permissions and storage.
- One material transformation with useful/craft/sell alternatives.
- One NPC buyer and one non-expiring targeted request.
- Complete crash, migration, overflow and concurrent transaction tests.

### Gate 4: Persistent nonlinear progression

- Several regional objectives with alternate progression routes.
- Persistent outcomes and shortcuts.
- Itemization, salvage, crafting and economy budget.
- Classes, respec and first complete progression curve.

### Gate 5: Rebirth and breadth

- Transactional Rebirth with a meaningfully different second life.
- More regions, bosses, factions, housing options and addon authoring tools.
- Long soak, public-server permissions, recovery, accessibility and onboarding release gates.

Do not begin breadth merely because every subsystem has a stub. Advance when the current gate proves the player loop and its failure behavior.

## 9. Immediate design work queue

Recommended next documents, in dependency order:

1. Survival ownership and Harmony boundary map.
2. Runtime profile, capabilities, persistence and migration architecture.
3. Owned realm runtime API/schema, selective Manifold derivation and parity tests.
4. Combat simulation, targeting and network authority contract.
5. Expedition lifecycle, director and reconnect specification.
6. Loot generation, crafting, salvage, NPC economy and trade specification.
7. Character progression, class, equipment and Rebirth transaction specification.
8. Multiplayer party, scaling, ownership and abuse specification.
9. First-hour onboarding, map, settings and accessibility specification.
10. Content-authoring pipeline, debug tools, metrics and release checklist.

## 10. Definition of design-complete

The end-scope design is ready for systematic implementation when:

- Every inherited Survival system has an owner, disposition, disable strategy, configuration boundary and test.
- Every adopted subsystem names server authority, persistence, migration, extension points, player-facing states, performance risks and acceptance tests.
- Cross-system transactions have one documented order and recovery path.
- The default preset and supported compatibility profiles are explicit.
- The first-hour and first-Rebirth loops can be traced without an unspecified mandatory step.
- Every progression resource and reward family has a reachable source and meaningful sink.
- Procedural content has machine-testable validity properties and reproducible failure seeds.
- A vertical implementation order can proceed without using breadth content to conceal missing foundations.
