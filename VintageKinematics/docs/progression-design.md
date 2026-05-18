# Vintage Kinematics Progression Design

This document is for internal design planning. The handbook should explain the
current playable mod; this file can hold larger progression targets, risky
systems, and features that need prototypes before being promised in-game.

## Core Direction

Vintage Kinematics should make kinetic power feel like leverage. Early tiers
turn player effort into better throughput. Later tiers should give the player
reasons to build factories because the world becomes easier to traverse, shape,
paint, and reorganize.

The mod should not only add single-purpose machines. Specific machines are good
for clear progression, but the late-game goal is a toybox: routes, moving
platforms, bulk world tools, player transport, and contraptions that let players
invent their own uses.

## Tier Goals

### T1: Manual Kinetics

T1 should be about turning short bursts of player action into useful mechanical
work.

- Hand crank: direct, bursty, cheap power.
- Primitive sieve: throughput reward, not automation. It processes whole blocks
  instead of hand-panning tiny portions.
- Treadwheel and counterweight drive: stronger crank-era power for players who
  want larger manual setups.
- Flywheel: stores manual bursts and smooths intermittent hand power.

Flywheels make especially strong sense here. They give manual power a real
machine feel without making early-game automation free.

Flywheel design notes:

- Stores rotational energy while the attached network is overproducing.
- Releases stored energy when input stops or dips below demand.
- Smooths hand-crank pulses so small machines can complete work without the
  player holding the crank perfectly.
- Should leak energy over time so it is a buffer, not a battery.
- Should have a visible spin speed and a tooltip for stored energy / remaining
  run time.
- Early flywheels should be low capacity and mostly help one or two machines.
- Later flywheel variants can scale up with iron/steel mass, bearings, or alloy
  parts.
- Heavy flywheel target: a 1x3x3 multiblock with metal outer bands/sides and a
  forge-press-parts recipe, positioned as the sustained workshop upgrade over
  the small wooden T1 flywheel. Initial balance target is 10x rated SU and 5x
  stored output duration: 10240 SU at 16 RPM for up to 900 stored seconds.

Balance target:

- T1 flywheel should make manual setups feel better, but should not run a whole
  unattended workshop for long.
- It should reward gearing and timing. Spinning it up should take effort.
- It should interact naturally with stress: heavy consumers drain it faster.

### T2: Forge And Sustained Workshop

T2 should convert the vanilla metal climb into larger machine capability.

- Bronze crusher opens stronger material processing.
- Forge press turns hot metal into machine parts.
- Kinetic bellows improve bulk heating and let powered forge setups feel like a
  meaningful step past normal firepit behavior.
- Coal motor turns a built workshop into sustained fuel power.
- Belts, funnels, and output pushing should make machine chains legible and
  practical without requiring vanilla chute tricks.

T2 should feel close to complete when the player has sustained power, better
bulk processing, and enough logistics to build a real workshop.

### T3: Factory As A Toybox

T3 should answer: why would a player keep building after vanilla progression is
functionally complete?

Targets:

- Make long-distance movement less punishing.
- Make bulk building and terrain work faster.
- Give factories outputs that are fun even when raw progression is finished.
- Add systems with combinatorial uses rather than only more fixed recipes.

Candidate features:

- Rail carts and cargo wagons.
- Cable cars between bases, cliffs, mines, and climate zones.
- Kinetic launcher / catapult for long-distance player travel.
- Gantry builders, block placers, and block breakers.
- Road rollers, terrain smoothers, or bulk painter tools.
- Mobile drill or quarry carriages.
- Limited contraptions that can move platforms, cargo, seats, and tool heads.

### T4: High-Cost World Leverage

T4 can be where steel-plus materials and high-end parts justify absurd but
useful toys.

- Large flywheels or kinetic accumulators.
- Long-range launch systems.
- Heavier cargo rail.
- Wheeled contraptions with strict mass and stress limits.
- Larger quarry / builder contraptions.
- Climate-scale transport infrastructure.

The theme should stay mechanical. High-tier does not mean magic; it means more
mass, better bearings, stronger frames, tighter tolerances, and more stress
budget.

## Contraptions Feasibility

A Create-like contraption system is realistic only if moving structures are
represented as entities while in motion. Moving real blocks through the world
every tick would be too fragile: lighting updates, chunks, claims, block
entities, inventories, multiblocks, collisions, and network sync would all fight
it.

Vintage Story gives useful pieces:

- Rideable/mountable entities.
- Boat/elevator-style moving entity patterns.
- Multi-box entity collision helpers.
- Block schematic-style snapshot data for blocks and block entities.
- Existing block entity tree serialization.

Vintage Kinematics already has one major constraint:

- The kinetic network is keyed by `BlockPos`. A moving contraption cannot stay
  inside the normal world network while its parts are no longer at fixed world
  positions.

Conclusion:

- Use entity-backed contraptions.
- Use strict block whitelists.
- Pause normal processing machines while assembled.
- Let contraptions consume stored/assigned power instead of joining the normal
  world kinetic graph while moving.

## Proposed Contraption Model

Add a `Contraption Core` or `Kinetic Anchor` block.

States:

1. `World`: all blocks are normal placed blocks.
2. `Assembled`: the anchor scans connected allowed blocks, validates them, and
   snapshots them.
3. `Moving`: the blocks are represented by an `EntityKineticContraption`.
4. `Placed`: the entity validates the target space and writes blocks back into
   the world.

Snapshot data:

- Relative block positions.
- Block codes or IDs.
- Optional block entity tree data.
- Collision boxes.
- Mass / material cost.
- Module flags: frame, seat, cargo, wheel, rail bogey, tool head, coupler.

Initial block whitelist:

- VK structural frames.
- Planks, beams, metal blocks, and other simple structure blocks.
- Shafts and cogs as visual/mechanical mass.
- Contraption seats.
- Contraption cargo crates.
- Wheels, bogeys, couplers, anchors, and tool heads.

Initial exclusions:

- Arbitrary chests and containers.
- Firepits, bloomeries, cooking blocks, and liquids.
- Chiseled blocks / microblocks.
- Active VK machines.
- Multiblock controllers and fillers.
- Blocks inside protected claims unless explicitly allowed.

## Movement Families

### Rail Carts

Most viable first transport system.

- Entity follows route blocks or rail blocks.
- Can carry player seats and cargo crates.
- Predictable collision and speed.
- Good for long-distance bases, mines, and logistics.

### Cable Cars

Very viable and highly useful in Vintage Story terrain.

- Route-bound entity between stations.
- Strong fit for cliffs, valleys, mines, and climate travel.
- Stationary winches can draw from normal VK networks.

### Gantries

Strong bridge into contraptions.

- Move a platform or tool head on one axis.
- Good for sliding doors, elevators, farms, builders, and quarry arms.
- Easier than freeform vehicles because movement is constrained.

### Launchers / Catapults

High-value and comparatively simple.

- Convert stored kinetic energy into entity velocity.
- Works for player traversal, item launching, or goofy late-game toys.
- Pogo stick / fall survival can become part of the intended travel loop.

### Wheeled Contraptions

Possible, but should come late.

- Entity-based, not real moving blocks.
- Needs custom terrain handling and simplified collision.
- Must have strict mass, size, and stress limits.
- Start with predefined wheel modules and contraption-safe frames before
  allowing arbitrary-looking builds.

## Power Rules For Moving Contraptions

Do not let moving contraptions directly participate in the normal world kinetic
network.

Cleaner rules:

- Stationary anchors draw RPM and stress from the world network.
- Assembly captures a drive budget, stored energy, or route command.
- Moving contraptions run on that budget.
- Normal VK processing machines pause while assembled.
- Special contraption modules may function while moving.
- Later, onboard flywheels can act as the moving energy buffer.

This gives flywheels a second long-term purpose: early they smooth manual
power; later they become the natural energy storage module for moving
contraptions.

## Recommended Implementation Order

1. Flywheel prototype for T1 manual smoothing.
2. Entity platform prototype that can carry one player.
3. Tiny contraption snapshot: 3x3 platform, whitelist only, no active machines.
4. Place-back validation and claim/chunk safety.
5. Rail cart or cable car using the same entity/mounting foundation.
6. Gantry movement for constrained block/tool-head contraptions.
7. Contraption cargo crates and seats.
8. Launcher / catapult using stored kinetic energy.
9. Wheeled contraptions after the route-bound systems are solid.

## Prototype Acceptance Criteria

A contraption prototype is worth keeping only if it can:

- Assemble and disassemble without losing blocks or inventory.
- Survive save/load while assembled or moving.
- Carry a mounted player without desync.
- Refuse invalid destinations instead of deleting blocks.
- Respect claims and unloaded chunks.
- Have simple enough collision to avoid trapping players constantly.
- Avoid joining the normal `BlockPos` kinetic network while moving.

## Design Guardrails

- Route-bound movement should come before freeform vehicles.
- Storage should leak unless it is intentionally expensive late-game tech.
- Whitelists are better than trying to support every vanilla block on day one.
- Moving structures should be allowed to look like machines even when not every
  part is simulated.
- If a contraption feature cannot be explained through mass, stress, gearing,
  storage, or mechanical linkage, it probably does not fit VK.
