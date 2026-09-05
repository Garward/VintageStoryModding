# Combat Spaces, Movement, and AI

Status: Adopted direction; implementation details remain provisional  
Parent: [Complete game design](complete-game-design.md)  
Index: [VRPG design index](README.md)

## 1. Purpose

VRPG's combat should feel authored even when encounters are assembled from reusable parts. Players need room to aim, kite, group enemies, prepare traps, and read telegraphs. Enemies need spaces they can understand reliably enough to pressure the player without gaining unfair movement privileges.

The geometry, movement model, and AI are therefore one design problem. A spectacular enemy behavior is not useful if ordinary terrain makes it fail, and a movement system is not interesting if every encounter is solved by standing on a two-block ledge.

## 2. Combat-space contract

Routine VRPG combat areas are mostly level. This does not mean visually empty planes.

- Traversable combat floors normally differ by zero or one block between adjacent playable surfaces.
- A rise of two blocks or more is rare. It must be a true boundary, or use an explicit ramp, stair, jump link, lift, or authored traversal rule understood by both players and AI.
- Decorative verticality may surround the arena without becoming reachable combat geometry.
- Pillars, walls, wide cover, hazard fields, and low obstacles create decisions without relying on fragile voxel parkour.
- Every required route supports the encounter's largest intended actor and formation.
- Doors, bridges, and lanes must not accidentally collapse an encounter into a single-body traffic jam unless that choke point is the authored decision.
- An arena must contain enough clear area for its assigned tactics. An orbiting enemy needs an orbit lane; a charger needs a telegraphed charge lane; a kiting player needs at least one recoverable escape route.

The generator should validate configurable metrics rather than infer quality from appearance:

| Metric | Why it matters |
|---|---|
| Traversable height delta | Prevents incidental safe ledges and path disagreement |
| Minimum lane and doorway width | Supports large actors, dodges, and groups |
| Head clearance | Prevents collision and camera traps |
| Connected floor area | Detects islands and unreachable spawn pockets |
| Orbit clearance | Ensures lateral enemies can execute their role |
| Charge distance and stopping room | Makes committed attacks usable and fair |
| Escape-route count | Prevents accidental cages and one-exit body blocking |
| Spawn line of sight | Avoids unavoidable opening damage |
| Hazard-free recovery area | Preserves deliberate movement decisions |

Exact thresholds belong in `CombatSpaceDefinition` data and can vary by room tag, actor scale, and encounter pattern.

## 3. Verticality policy

Height remains useful when it creates a legible rule.

Allowed exceptional uses include boss platforms with explicit access, walls that truly divide routes, balconies designed for ranged units, drops with defined return paths, and traversal-focused rooms whose enemies have matching navigation behaviors.

Every exceptional vertical feature declares:

- Which actor profiles may use it.
- Whether attacks can cross it.
- How an actor recovers after displacement.
- What happens if the player reaches an unintended side.
- Whether the feature is active combat space or scenery.

Routine procedural rooms should not use two-block crenellations, narrow pits, stacked rubble, or reachable decoration as difficulty. Rare vertical set pieces require their own validation profile and encounter test.

## 4. Terrain and anti-cheese policy

Kiting, using cover, funneling enemies, and preparing terrain-aware abilities are intended. Reaching a spot where enemies cease functioning is not.

The response to inaccessible players is layered:

1. Local steering attempts a valid approach or formation slot.
2. Navigation requests a new route without recomputing every actor every tick.
3. The enemy changes to a role-appropriate response: ranged pressure, area denial, guarding the objective, coordinated repositioning, or disengagement.
4. Recovery handles genuinely stuck actors through a visible, rule-consistent reposition. Teleporting directly onto the player is a last resort and should not masquerade as an attack.

Encounter boundaries must be clear and consistent. Ordinary building and block breaking are disabled in the main world and expeditions. Authorized skills may create temporary terrain only inside a validated mutable envelope and cannot produce a permanent zero-risk firing shelf.

Anti-cheese logic must not erase clever play. A trap corridor or line-of-sight break that costs time, space, or resources is a solution; an unreachable voxel seam with no enemy answer is a geometry defect.

### 4.1 Temporary terrain as a combat verb

Skill-created walls, cover, cages, bridges, traps, and elemental blocks are intended tactical tools. Enemies evaluate them through the same steering, navigation, hazard, and recovery layers used for authored terrain.

- A construct declares whether an enemy should route around, attack, vault, ignore, exploit, or wait it out.
- Formation and squad claims are released when the construct invalidates them.
- Straight charges resolve collision with temporary walls explicitly rather than clipping through or stopping without feedback.
- Range and orbit behaviors choose a fallback when a new wall breaks line of sight or their lane.
- Local navigation invalidation is bounded by the construct's affected area and lifetime.
- Encounter rules prevent sealing every route or trapping an immune actor indefinitely.

The complete placement, mutation, and cleanup contract is defined in [World generation and world mutation](world-generation-and-world-mutation.md#5-temporary-combat-terrain).

## 5. Player movement contract

Movement is responsive at baseline and bursty under pressure.

- Acceleration, stopping, and direction changes are forgiving enough for precise aiming and grouping.
- Dash, dodge, leap, sprint burst, or kit-specific movement provides short reposition windows.
- Bursts consume charges, a regenerating resource, cooldown time, or a tactical condition. They are frequent enough to feel like movement verbs, not emergency buttons saved forever.
- Enemy attacks expose commitment and recovery windows that movement can exploit.
- Collision, slopes, and one-block steps should not unpredictably cancel a movement skill.
- Limited input buffering and edge forgiveness prevent tiny timing or voxel errors from causing disproportionate failure.
- Crowd control may constrain movement, but routine enemies should not repeatedly remove player control.

Careful kiting is expected. Infinite low-attention circle strafing is not. Enemies answer it through mixed roles, converging angles, hazard placement, formation pressure, and attacks that claim space. They should not answer it by universally outrunning the player or rotating perfectly during a committed swing.

`MovementProfileDefinition` should describe acceleration, braking, turn response, burst rules, collision policy, and any air or slope assistance independently from class skills.

## 6. Enemy movement principles

Enemies obey readable physical rules.

- Locomotion speed, turn rate, attack tracking, commitment, and recovery are distinct values.
- Large turns and abrupt velocity changes take visible time unless they are the enemy's explicit supernatural verb.
- A charge selects its line, telegraphs, commits, and then resolves impact or recovery. It does not continuously home like ordinary pursuit.
- Orbiting preserves a range band and side direction but yields when the lane is blocked.
- Ranged enemies value line of sight and a range band; they do not endlessly retreat through the entire map.
- Shield enemies claim space and screen allies without forming an impassable, perfectly synchronized wall.
- Enemies understand active hazards well enough for their identity: cautious actors avoid them, reckless actors accept them, and specialized actors may exploit them.

## 7. AI architecture

Combat AI is server-authoritative and compositional. Content should combine shared capabilities rather than subclass a monolithic creature brain.

```text
Perception and combat memory
        ↓
Role, utility, and intent selection
        ↓
Tactical behavior (orbit, screen, charge, maintain range...)
        ↓
Local steering + long-route navigation
        ↓
Locomotion and ability requests
        ↓
Execution, recovery, and anti-stuck
```

The layers have narrow responsibilities:

- **Perception** records visible threats, last known locations, hazards, allies, objectives, and recent damage. It does not choose animation or paths.
- **Intent selection** scores eligible tactics from the enemy definition and current context. It applies hysteresis so actors do not change their minds every frame.
- **Tactical behavior** requests a destination, facing, range band, formation slot, or ability. It does not move the entity directly.
- **Steering and navigation** reconcile those requests with collision, neighbors, and reachable space.
- **Ability execution** validates cost and target, performs telegraphs, commits, and reports success, interruption, or failure.
- **Recovery** detects lack of progress, invalid targets, broken formations, and inaccessible positions.

Long-route navigation and local combat steering must remain separate. Route queries are cached, shared where practical, invalidated by relevant world changes, and scheduled under a per-tick budget. Local steering updates more often but examines a bounded neighborhood. No encounter performs a full world scan or recalculates every path every tick.

## 8. Reusable tactical behaviors

The initial behavior library should be small, legible, and broadly composable:

| Behavior | Contract |
|---|---|
| Advance | Close toward a reachable pressure position |
| Hold range | Stay inside a minimum/ideal/maximum distance band |
| Orbit | Move laterally around a target while preserving a range band and chosen side |
| Strafe | Shift within a firing lane without completing a full orbit |
| Screen | Occupy a formation slot between a protected ally and threat |
| Straight charge | Telegraph a line, commit, collide or miss, then recover |
| Flank | Claim a side or rear approach not already saturated by allies |
| Retreat and reset | Break contact to a bounded point, then re-enter; never kite forever |
| Guard | Deny access to an ally, object, portal, or zone |
| Investigate | Move to last-known evidence without omniscient tracking |
| Evade hazard | Leave a dangerous shape according to the actor's hazard tolerance |
| Pressure objective | Attack or occupy the encounter objective when player avoidance stalls combat |

Specific enemies gain identity by combining these with abilities and conditions. A fire lancer might orbit until a lane opens, charge through ignited targets, and then retreat during recovery. The orbit, charge, condition, and damage rules remain reusable data-backed pieces.

## 9. Squads and encounter coordination

Some encounters use a lightweight squad coordinator. It assigns claims rather than issuing frame-by-frame commands:

- Formation or approach slots around the player.
- A limited number of simultaneous melee attackers.
- Screen positions for shield enemies.
- Protected firing lanes and range bands for ranged enemies.
- Flank ownership so every actor does not choose the same side.
- Shared escalation when the player remains unreachable or disengaged.

This supports a shield line advancing while ranged enemies fire from behind it. The ranged actor still owns aiming and range control; the shield actor owns screening; the coordinator prevents them from competing for the same physical space.

Coordination must leave gaps, recovery windows, and imperfect reactions. It should produce readable teamwork rather than a single hive mind.

## 10. Extension contract

Provisional interfaces and definitions:

- `ICombatBehavior`: eligibility, score, enter, update, exit, and failure reason; outputs tactical requests instead of mutating movement directly.
- `IPerceptionSource`: contributes bounded, server-authoritative observations.
- `INavigationQuery`: requests reachable positions, paths, and formation slots without exposing one pathfinder implementation.
- `ICombatCoordinator`: offers encounter-level claims and releases them safely.

Foreign creature content enters before these runtime interfaces. The ingestion pipeline converts compatible models, animations, sounds and body hints into a synthetic VRPG actor, then assigns the same tactics, navigation and ability contracts as native enemies. Source AI tasks never become `ICombatBehavior` implementations automatically. See [Creature ingestion and adaptation](creature-ingestion-and-adaptation.md).
- `CombatBehaviorDefinition`: behavior code, parameters, conditions, weights, cooldown, tags, and implementation code.
- `EnemyTacticsDefinition`: available behaviors, ability bindings, role tags, range preferences, commitment, and fallback order.
- `SquadPatternDefinition`: role counts, slot patterns, attack concurrency, and escalation rules.
- `CombatSpaceDefinition`: room metrics, actor profiles, navigation links, boundaries, and validation tolerances.
- `MovementProfileDefinition`: player or actor locomotion response and burst policy.
- `TemporaryTerrainDefinition`: construct shape, collision, duration, health, placement, navigation response, limits, and cleanup.

Unknown behavior codes, invalid range bands, impossible formation requirements, and missing fallbacks fail validation with the exact asset and field. Addons can register genuinely new behavior implementations while ordinary variants remain data-only.

## 11. Readability and player information

Players should learn behavior from the world before needing a handbook entry.

- Charges show direction and commitment early enough to evade.
- Shield screening uses pose, spacing, and impact response that explain its protection.
- Ranged units communicate target acquisition and release timing.
- Orbit direction and range preference are visible through movement, not hidden bonuses.
- Navigation recovery has a recognizable tell and never resembles an untelegraphed attack.
- Major hazards remain readable beneath propagation effects, ragdolls, and loot.

The HUD should expose only exceptional information such as a boss phase, locked objective, or severe control state. Ordinary tactics belong in animation, audio, silhouettes, and ground indicators.

## 12. Performance and multiplayer constraints

- The server selects intents, validates movement abilities, resolves attacks, and owns navigation truth.
- Clients interpolate motion and may predict their own burst movement, but cannot declare hits or valid destinations.
- Perception, utility selection, route queries, and squad reassignment run at different cadences.
- Actor updates are staggered and budgeted; priority increases near players and active hazards.
- Formation slots and path corridors are shared where safe, not independently rediscovered by every actor.
- Debug tooling records current intent, requested range, path age, stuck state, squad claim, and reason for the last transition.
- Performance tests measure server frame cost at target encounter sizes, not only isolated actors.

## 13. Validation and acceptance tests

An initial combat slice is acceptable when:

- Generated routine rooms contain no unintended reachable two-block safe ledges.
- A player can kite deliberately using baseline movement and bursts, but cannot defeat the test encounter through indefinite unopposed backpedaling.
- A straight charger commits to a readable line and handles walls and misses predictably.
- An orbiting actor preserves its range when space permits and selects a fallback when blocked.
- Shield and ranged enemies occupy complementary positions without deadlocking doors.
- A temporary obstacle or inaccessible player position produces a role-appropriate response within a bounded time.
- Twenty or more active enemies do not request full new paths on the same tick.
- A custom behavior can be registered without replacing perception, locomotion, or ability execution.
- A temporary wall changes routing locally, receives readable enemy counterplay, and restores its exact prior state without a full encounter path rebuild.
- Server and client agree on committed attacks, burst movement, and defeat outcomes under simulated latency.

## 14. Open decisions

- Whether players may place or break blocks in active expedition rooms.
- Exact floor-delta, lane-width, orbit-radius, and charge-length defaults.
- Whether the base movement burst is universal, class-selected, or both.
- How much air control and ledge assistance remains compatible with Vintage Story physics.
- Which navigation representation best fits mutable voxel rooms and the encounter budget.
- Whether squad coordination is per room, per spawn group, or a mix of both.
