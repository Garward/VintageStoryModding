# VRPG Skill Authoring and Modeling Guide

This guide covers the first playable, data-driven skill runtime. A skill definition owns delivery, level scaling, resource cost, cooldown, targeting dimensions, on-hit status operations, persistent ground presentation, model selection, particles, and color. C# owns execution and validation; adding another skill within the supported contracts should require JSON and, for a projectile, an optional shape.

## Current runtime contract

Skill definitions live under `assets/<domain>/vrpg/skills/*.json`. VRPG currently supports:

| Delivery | Targeting | Behavior |
| --- | --- | --- |
| `raycast_aoe` | Aim up to `range` blocks | Resolves the first selected block or entity and applies an instant area hit at that point. |
| `projectile_aoe` | Current look direction or aimed surface | Spawns a physical projectile using `projectile.speed`; `projectile.impactMode` determines whether creatures intercept it or it travels through them to the aimed surface. |
| `circle` | Caster-centered | Applies a circular area hit around the caster. `range` may be zero. |
| `melee_arc` | Forward sector | Instantly hits valid creatures in an authored `range` and `melee.arcDegrees`. It never reads vanilla weapon reach. |
| `melee_line` | Forward capsule | Instantly hits creatures along an authored `range` and `melee.width`, ordered from near to far. It never reads vanilla weapon reach. |
| `melee_single` | Crosshair-biased forward capsule | Selects one unobstructed creature using authored `range` and `melee.width`. It never reads vanilla weapon reach. |

All casts are server-authoritative. The client sends only a loadout slot number. The server resolves the equipped skill, learned level, resource state, cooldown, targeting, and damage.

The initial implementation intentionally excludes friendly fire and PvP. Area damage affects living, interactable `EntityAgent` creatures, never the caster or another player. Melee candidates are checked server-side against their collision size, vertical tolerance, and solid-block line of sight.

## Melee geometry

Melee skills are skill attacks, not wrappers around Vintage Story's held-item
interaction. The equipped weapon supplies Weapon Power once that gear pipeline is
wired; the skill definition supplies reach and shape. A dagger skill may therefore
have a deliberate `2.2m` lunge and a trident skill a `5m` thrust regardless of the
vanilla collectible's interaction range.

```json
{
  "delivery": "melee_arc",
  "range": 2.5,
  "maxTargets": 0,
  "melee": {
    "arcDegrees": 70.0,
    "width": 1.2,
    "verticalTolerance": 2.25
  }
}
```

- `melee_arc` uses `arcDegrees`; `70°` is a readable ordinary sweep.
- `melee_line` uses `width` as the complete line width; `1.2m` is a narrow thrust.
- `melee_single` uses `width` as aim forgiveness, then chooses the candidate closest
  to the center line. It always resolves at most one target even if `maxTargets` is
  larger.
- `verticalTolerance` permits slopes and enemies of different sizes without turning
  a horizontal attack into an unlimited vertical column.
- `circle` remains the correct delivery for a point-blank melee shockwave or spin.
- Positive `maxTargets` caps arc/line targets; zero means every valid target in the
  shape.

## Timing, multi-hit, and channels

Every skill owns a `timing` block. The three modes are deliberately distinct:

| Mode | Contract |
| --- | --- |
| `instant` | Exactly one hit. A larger coefficient is still one hit. |
| `sequence` | `hitCount` independent hits separated by `hitIntervalSeconds`. Resource cost and cooldown are committed on activation. |
| `channel` | One immediate hit, then repeated hits every `hitIntervalSeconds` while the hotkey remains held. It ends on release, resource failure, death/disconnect, or `maxDurationSeconds`; cooldown begins when it ends. |

```json
{
  "timing": {
    "mode": "sequence",
    "hitCount": 3,
    "hitIntervalSeconds": 0.18,
    "maxDurationSeconds": 1.0
  }
}
```

A three-hit Dexterity attack authored at `100% Weapon Damage` is three separate
`100%` hits, not one `300%` hit. Each hit independently feeds critical hits,
ailments, leech, and on-hit triggers. This distinction is balance-relevant and must
remain visible in skill tooltips and the damage simulator.

Repeated skills must set `damage.ignoreInvFrames: true`; validation rejects a
sequence or channel that would silently lose authored hits to Vintage Story's
ordinary invulnerability window. The runtime currently re-evaluates aim and shape
on every timed hit. Target-lock and “cancel when the original target is lost” are
separate future behaviors rather than implicit sequence rules.

Channel resource drain uses `resource.costMode: "per_second"`. The normal rank-
scaled resource value becomes a per-second rate and each server tick pays only its
time fraction. `costMode: "cast"` remains available for a channel with one upfront
cost. A `none` resource is valid for cooldown/duration-limited prototypes.

The held-input protocol is server authoritative: the client sends only slot press
and release state. The server owns tick cadence, resources, damage, maximum
duration, and cooldown. Losing the release packet cannot create a permanent
channel because every channel has a validated maximum duration.

## On-hit statuses and payoff events

`onHitEffects` run only after the server confirms that a skill damaged a living
target. Status instances are owned by the casting entity: two players may build
or consume their own Corrosion or Stagger on the same enemy without spending one
another's setup. Compact enemy presentation aggregates matching statuses while
the server retains separate ownership.

Supported operations are:

| Operation | Behavior |
| --- | --- |
| `apply` | Apply or refresh an ordinary status such as Burn. |
| `add_stacks` | Add owned stacks up to the status definition's `maxStacks`. |
| `add_buildup` | Add primary/secondary magnitude toward `maximumMagnitude`; reaching the maximum clears the buildup and may apply a result status and event. |
| `consume_buildup` | Remove up to the authored primary/secondary magnitude; a sufficiently large consumption may apply a result status. |

Rust Lance's two owned Corrosion stacks are the smallest example:

```json
"onHitEffects": [
  {
    "statusCode": "vrpg:corrosion",
    "operation": "add_stacks",
    "stacks": 2,
    "durationSeconds": 8.0
  }
]
```

Hammer Blow demonstrates target priority and a confirmed payoff. The nearest,
most centered target selected by the delivery is primary; other targets are
secondary. At 100 Stagger, the buildup clears, Stun is applied, and the client
receives the rare `BREAK` event:

```json
"onHitEffects": [
  {
    "statusCode": "vrpg:stagger",
    "operation": "add_buildup",
    "primaryMagnitude": 18.0,
    "secondaryMagnitude": 9.0,
    "durationSeconds": 6.0,
    "maximumMagnitude": 100.0,
    "triggerEvent": "break",
    "resultStatusCode": "vrpg:stun",
    "resultDurationSeconds": 1.25
  }
]
```

Trigger events are deliberately sparse: `break`, `counter`, `consume`, `mark`,
and `windowopen`. Do not emit one merely because a status refreshed. Event words
are reserved for a confirmed decision or payoff.

`groundArea` adds synchronized persistent presentation after a projectile impact.
Zero radius inherits the skill's ordinary impact radius:

```json
"groundArea": {
  "enabled": true,
  "durationSeconds": 2.25,
  "radius": 3.0
}
```

This contract currently owns visual state and expiry. Burning-area periodic
damage, Corrosion/Burn damage-over-time ticks, Stun AI interruption, and
Vulnerable damage amplification remain combat-mechanics work; do not describe
those numerical effects as executable until their resolvers are wired.

## Definition example

```json
{
  "code": "vrpg:cinder_orb",
  "name": "Cinder Orb",
  "description": "Launch a rust-caged ember that bursts on impact.",
  "classCode": "vrpg:corroder",
  "requiredLevel": 1,
  "maxLevel": 10,
  "delivery": "projectile_aoe",
  "cooldownSeconds": 2.8,
  "range": 28.0,
  "radius": 3.2,
  "maxTargets": 0,
  "model": "vrpg:entity/skill/cinder-orb",
  "color": "#f06a28",
  "tags": ["spell", "fire", "projectile", "area"],
  "damage": {
    "type": "vrpg:fire",
    "base": 16.0,
    "perLevel": 5.0,
    "weaponDamagePercent": 120.0,
    "weaponDamagePerLevelPercent": 4.0,
    "tier": 0,
    "ignoreInvFrames": false
  },
  "resource": {
    "type": "mana",
    "base": 14.0,
    "perLevel": 1.0
  },
  "projectile": {
    "impactMode": "entity",
    "speed": 0.62,
    "lifetimeSeconds": 4.5,
    "verticalOffset": -0.52,
    "horizontalOffset": 0.22,
    "forwardOffset": 0.55,
    "aimConvergenceDistance": 12.0
  },
  "particles": {
    "model": "quad",
    "burstQuantity": 42.0,
    "trailQuantity": 1.5,
    "lifetimeSeconds": 0.7,
    "trailLifetimeSeconds": 0.22,
    "gravity": -0.12,
    "scale": 0.38,
    "velocity": 1.15
  }
}
```

Resource cost and the legacy prototype damage fallback use the explicit formula:

```text
value = base + perLevel * (skillLevel - 1)
```

The approved damage contract uses the two weapon fields instead:

```text
rank effectiveness =
    weaponDamagePercent
    × (1 + weaponDamagePerLevelPercent / 100 × (skillLevel - 1))

skill base hit = resolved equipped Weapon Power × rank effectiveness / 100
```

For example, Cinder begins at `120% Weapon Damage` and reaches `163.2%` at
rank 10 with the default four-percent relative rank growth. `base` and
`perLevel` remain temporarily required by the executable prototype because
real item-stack Weapon Power is not wired into casts yet. Do not use those
legacy numbers to make new balance decisions; the offline balance tool already
uses Weapon Damage by default. Remove the fallback only after weapon required
level, rarity, and affixes are authoritative on equipped stacks.

The runtime clamps skill level to `1..maxLevel`. `maxTargets: 0` means unlimited targets within the area. Positive values cap targets nearest to the area center.

Supported resources are `none`, `mana`, and `blood`. Blood skills fail until blood is unlocked. Supported particle models are `quad` and `cube`. Colors use `#RRGGBB` or `#RRGGBBAA`.

Damage `type` must reference a loaded VRPG damage type. The current engine mappings are:

| VRPG type | Vintage Story damage type |
| --- | --- |
| `physical` | Blunt attack |
| `fire` | Fire |
| `cold` | Frost |
| `lightning` | Electricity |
| `rust` | Acid |

These mappings are an execution detail, not the final ailment system.

Projectile launch offsets are measured from the player's eye position. A negative `verticalOffset` moves the launch down toward chest/hand level, `horizontalOffset` moves it beside the camera, and `forwardOffset` keeps it clear of the body.

`impactMode` supports the following values and defaults to `entity` for older definitions:

| Mode | Flight and collision behavior |
| --- | --- |
| `entity` | A swept server-side collision test lets a living creature intercept the projectile. If nothing is hit, terrain remains the fallback impact. The launch socket converges toward the crosshair at `aimConvergenceDistance`. |
| `ground` | Creature collision is disabled. The camera ray resolves the aimed solid surface up to `range`, and the launch socket fires toward that exact point. The projectile detonates when it reaches the point or collides with intervening terrain. |

Use `entity` for bolts, arrows, or orbs that should reward hitting a moving target. Use `ground` for bombardments, traps, persistent zones, and effects whose placement matters more than interception. Both modes remain physical projectiles with travel time; neither is a disguised instant raycast.

## Projectile models

Projectile shapes live under `assets/<domain>/shapes/`. The skill's `model` omits both the `shapes/` prefix and `.json` suffix:

```text
model: vrpg:entity/skill/cinder-orb
file:  assets/vrpg/shapes/entity/skill/cinder-orb.json
```

Vintage Story shape units use 16 model units per world block. Projectiles should normally be compact and centered close to their origin. For directional projectiles, treat the local X axis as the forward silhouette because the base projectile renderer rotates this axis into its flight direction. Symmetric orbs are orientation-independent.

Model for recognition at combat distance:

- Start with a readable silhouette and a small number of cuboids.
- Keep the visual inside a sensible footprint around its origin. The generic collision box is intentionally conservative and does not follow decorative spikes.
- Avoid coplanar faces and nearly overlapping shells; they cause z-fighting in motion.
- Use explicit, semantic texture keys such as `rust`, `ember`, or `core`.
- Include the asset domain when reusing game textures, for example `game:block/metal/tarnished/rusty-iron`.
- Prefer existing game textures during prototyping. Add custom textures only when they materially improve recognition.
- Remember that `color` controls skill particles and library presentation; it does not tint arbitrary model textures.

The projectile entity reads the model path from synchronized attributes and substitutes the shape client-side. This keeps one generic entity type usable by every projectile skill.

## Particle tuning

Use particles to communicate timing and area, not to hide the battlefield.

- `burstQuantity` controls the impact-ring density. Impact particles are placed around the affected area instead of over its center so targets remain readable.
- `trailQuantity` controls ray segments and projectile trails. Use zero for skills without a trail.
- `lifetimeSeconds` controls impact and perimeter particles and should usually stay below one second for frequent combat skills.
- `trailLifetimeSeconds` independently controls ray and projectile-trail persistence. Instant raycasts should generally use `0.1-0.2` seconds so they read as hitscan flashes rather than traveling attacks.
- `scale` is the individual particle size. Runtime impact markers render slightly below this value to keep the target silhouette clear.
- `velocity` controls burst spread speed.
- `originVerticalOffset`, `originHorizontalOffset`, and `originForwardOffset` place ray visuals at a readable chest/hand socket while targeting still raycasts from the camera.
- Positive `gravity` pulls particles down; negative values make them rise.
- `quad` reads as energy, smoke, or glow. `cube` reads as debris, fragments, or physical force.

Circle skills use `burstQuantity` to choose a 12-40 point perimeter at `radius`. Raycast skills draw nine trail samples between the configured visual socket and just before the resolved impact point. Near samples have shorter lifetimes than distant samples, clearing the line progressively toward the target; the impact ring owns the endpoint. Treat these counts as a shared performance budget when increasing per-sample quantities.

## Validation and failure policy

VRPG validates every skill after assets load and throws one aggregated startup error if definitions are invalid. Validation currently enforces:

- namespaced code and non-empty name;
- supported delivery and resource names;
- valid authored melee arc/width/vertical geometry with a maximum `16m` skill reach;
- timing mode, sequence hit count/interval, channel interval/duration, and repeated-hit invulnerability behavior;
- `resource.costMode` of `cast` or channel-only `per_second`;
- positive level limits, cooldown, radius, and aimed range;
- non-negative legacy damage, Weapon Damage effectiveness/rank growth, and
  resource formulas;
- a known VRPG damage type;
- a valid hex color and particle profile;
- positive projectile speed and lifetime;
- projectile `impactMode` of `entity` or `ground`;
- an existing model asset for projectile skills;
- known on-hit status/result codes, supported operations and events, and
  non-negative stack, buildup, threshold, and duration values;
- bounded persistent ground-area duration and radius;
- conservative hard caps of 32 blocks for radius and 128 blocks for range.

This is intentional: content errors should stop startup with a useful list rather than become delayed null-reference failures during combat.

Before launching the game, validate JSON syntax and compile:

```bash
find VRPG/assets/vrpg/vrpg/skills -name '*.json' -print0 | xargs -0 -n1 jq empty
dotnet build VRPG/VRPG.csproj --no-restore
```

## In-game test workflow

Admin acquisition is temporary scaffolding until class selection and skill-point spending are implemented.

```text
/vrpg skill list
/vrpg skill grantall 3
/vrpg skill equip 1 rust_lance
/vrpg skill equip 2 cinder_orb
/vrpg skill equip 3 cinder_bombardment
/vrpg skill equip 4 fracture_pulse
/vrpg skill equip 5 hammer_blow
/vrpg skill equip 6 flurry
/vrpg skill equip 7 grinding_sweep
/vrpg skill equip 8 thrust
/vrpg skill loadout
```

Cast visible slots with `Alt+1` through `Alt+8` by default, or use `/vrpg skill cast <slot>` while debugging. The Hub can show between four and eight slots without clearing hidden assignments. Admins can grant another online player with:

```text
/vrpg skill grantto <player> <code> [level]
/vrpg skill grantallto <player> [level]
```

Use `/vrpg skill remove <code>` to remove a skill from yourself and clear any slot containing it.

Check each skill in first and third person, against one target and a dense group, beside walls and low ceilings, at minimum and maximum skill level, with insufficient resources, and while rapidly pressing the hotkey during cooldown. An `entity` projectile must hit a creature along the complete swept flight path, hit terrain when it misses, and expire at range. A `ground` projectile must pass through creatures, reach the aimed surface, respect intervening terrain, and detonate at the authored point. Melee must stop at walls, include target collision size, tolerate ordinary slopes, and remain unchanged when the held vanilla weapon has a different native range. Sequence tests must count actual health changes for every hit. Channel tests must cover tap, hold, release, depletion, death, four-second safety termination, and multiplayer latency. Test all delivery types across a multiplayer connection.

For the first status-feedback pass, verify that Rust Lance adds two Corrosion
stacks, Cinder applies Burn, Hammer Blow advances Stagger by 18 on its primary
target and 9 on secondary targets, 100 Stagger emits `BREAK`, and Fracture
consumes at most 60 Stagger while emitting `CONSUMED`. Cinder Bombardment should
leave a synchronized disc that enters its expiring presentation before removal.
