# Combat Visuals Framework Design

Date: 2026-07-16
Status: approved design, pre-implementation

## Goal

Give the VRPG framework a visual vocabulary that can express every mechanic in
the [initial class skill roster](../../design/initial-class-skill-roster.md)
without per-skill client code. The framework must stay readable under spam
(a party casting four-plus skills each into a crowd), and the player must be
able to understand what is happening: which states are on which enemies, which
areas are dangerous or owned, which windows are open, and what their payoff
actions accomplished.

This design covers framework capability, not per-skill art. A skill or status
authored in JSON gets working visuals from data alone; bespoke polish can be
layered later.

## Decisions Already Made

- **Fidelity tier:** particles plus custom flat renderers (mesh rings/discs,
  Cairo HUD overlays). No shaders, animated decals, or glow meshes yet.
- **Combat text:** merged damage numbers plus distinct event words, both
  toggleable in Hub Options.
- **Enemy status:** nameplate icon rows plus per-status particle auras, with a
  dedicated buildup mini-bar for threshold states such as Stagger.
- **Player state:** buff/debuff row docked to the resource bars, hotbar slot
  glow for currently empowered skills, and a crosshair-adjacent pulse for
  sub-2-second reactive windows.
- **Spam policy:** prioritized degradation that protects the player's own
  gameplay first; the policy is user-configurable (own-first default, uniform
  alternative, master intensity slider).
- **Architecture:** Approach A — the server never draws; it publishes state
  and events, and the client owns all rendering, budgets, and options.

## Architecture

Server combat systems publish facts through three channels, chosen by the
lifetime of the fact. A single client-side `VisualDirector` is the only
consumer of all three; it owns budgets, degradation, and options, and
dispatches to five renderers. Renderers never decide what to skip on their
own.

```text
SkillCastingService ─┐                       ┌─> Skill FX (particles)
StatusEffectTracker ─┤   1 status sync       ├─> Ground telegraphs (mesh)
GroundAreaService  ──┼─> 2 area registry ──> VisualDirector ─> Entity status overlay
future combat code ──┘   3 visual events     ├─> Combat text
                                             └─> Player-state HUD
```

### Channel 1: Entity status sync (continuous, seconds to minutes)

`StatusEffectTracker` writes a compact tree into each affected entity's
WatchedAttributes under `vrpgStatus`. Per effect: code, stacks, magnitude
(for buildup states such as Stagger, 0–100), and an **absolute end time**
rather than a countdown so nothing re-syncs per tick. Writes happen only on
apply, refresh, deepen, or consume.

WatchedAttributes is the engine-native path: it auto-syncs to every client
tracking the entity, and a player who arrives mid-fight sees existing states
for free. Enemies, other players, and Handler constructs are covered
identically because constructs are ordinary entities.

### Channel 2: Ground area registry (persistent placed things)

A new server `GroundAreaService` owns area records:

| Field | Meaning |
| --- | --- |
| `id` | Server-assigned unique id. |
| `ownerUid` | Placing player, for ownership-aware rendering. |
| `styleCode` | Resolves to a visual definition client-side. |
| `shape` | `disc` or `ring`. |
| `center` / `followEntityId` | Fixed position, or an entity the area tracks. |
| `radius` | Blocks. |
| `state` | `armed`, `triggered`, `active`, `expiring`. |
| `expiresAtMs` | Absolute expiry; client animates toward it. |

Sync uses two packets — `GroundAreaUpsert` and `GroundAreaRemove` — broadcast
to players in range, plus a snapshot on join or approach. The client mirrors
the records in a `GroundAreaStore`.

This channel expresses: Ward and its boundary, Snare and Blast Trap (armed
versus triggered), burning ground from Draft, Fracture's brief slam area, and
the Final Turn pre-burst warning (an area following an Overtuned construct).

### Channel 3: Combat visual events (moments, under a second)

One generic broadcast packet:

```text
CombatVisualEventPacket {
  kind          // fixed vocabulary below
  styleCode     // skill or status code; client resolves visuals locally
  sourceEntityId, targetEntityId
  position      // used when no target entity applies
  magnitude     // damage amount, consumed stacks, shield amount
  flags         // crit, threshold, damage type
}
```

`kind` vocabulary: `impact`, `burst`, `ray`, `damage`, `heal`, `shield`,
`break`, `counter`, `consume`, `window_open`, `mark`.

Damage numbers ride this channel per hit. **Merging is a client decision**,
so each player's own settings and budgets apply. The server sends facts, not
presentation.

### Data-driven visual definitions

Packets and synced state carry only codes. The client resolves them from its
own copy of the asset registry (mod assets ship to clients), following the
existing JSON authoring pattern:

- `StatusEffectDefinition` gains a `visual` block: icon token, color, aura
  family (`rustflakes`, `embers`, `drips`, `frost`, `sparks`, `mark`),
  intensity-per-stack, `showStacks`, and an optional `buildup` block
  (show mini-bar, threshold value, flash at threshold).
- `SkillDefinition` keeps `color` and `particles`, now interpreted
  client-side, and gains optional `visual.cast` / `visual.travel` /
  `visual.impact` style overrides.
- An unknown or missing style code falls back to a generic style built from
  the definition's color. New content is never invisible.

### Migration debt

The existing server-side draw paths move to channel 3 and client rendering:
`SpawnBurst`, `SpawnRay`, and `SpawnCircle` in `SkillCastingService`, the
projectile impact/trail particles in `EntityVrpgSkillProjectile`, and the
evasive-step puff in `EvasiveStepService`. This is the entire rework cost of
Approach A and is intentionally paid while the mod is small.

## The Five Renderers

### 1. Skill FX (particles)

Client-side port of the burst/ray/circle spawners and the projectile trail.
Same visual language as today, but spawn quantity is scaled by the director's
budget and every spawn carries a priority class.

### 2. Ground telegraphs (flat geometry)

An `IRenderer` drawing from `GroundAreaStore`: filled translucent disc for
owned areas, crisp edge ring for boundaries. State treatments: `armed` shows
a faint marker (brighter for the owner), `triggered` flashes once, `active`
holds steady, `expiring` pulses. Ward gets a standing boundary ring; Final
Turn renders as a ring that fills as the construct's expiry nears. Flat
geometry stays readable under any particle load — that is why these are
meshes, not particles.

### 3. Entity status overlay

Extends the existing entity nameplate HUD (`HudElementVRPGEntityHealthBars`):

- Icon row under the health bar with stack counts and radial duration wipes.
- Buildup mini-bar (Stagger-likes) with a threshold notch that flashes when
  crossed.
- Shield overlay segment on the health bar for Magic Shield.
- Per-entity looping aura emitters whose family comes from the status visual
  block and whose intensity scales with stacks. Auras exist so the player can
  pick the primed target out of a scrum without reading icons.

### 4. Combat text

Projected into screen space like the health bars. Rules:

- Per target: at most **one merged damage number per damage type**,
  accumulating within a rolling ~0.5 s window and growing slightly as it
  merges, plus **one event word slot**.
- Event words (`STAGGERED`, `BREAK`, `COUNTER`, `REACTION`, crit markers)
  always win the slot over numbers.
- Hard screen cap (~20 live entries); beyond it the lowest-priority entries
  retire early.
- The merge/cap logic lives in a pure class with no engine dependencies so it
  is unit-testable.

### 5. Player-state HUD

- Buff/debuff row with stacks and timers docked to the existing resource
  bars: Tempo, Tempered, stolen boons, Reaction progress, active wards on
  self, and similar.
- Hotbar slot glow when that specific skill is empowered right now, driven by
  a server-set per-slot empowered flag (examples: Fracture at Stagger
  threshold, Cinder during a Reaction window, Ambush with an opener ready).
- Sub-2-second reactive windows additionally fire a small crosshair-adjacent
  pulse so the player's eyes never leave the fight.
- Magic Shield renders as a segment on the player's own health/resource
  display.

## Spam Budget and Degradation

Four priority classes, enforced only by the `VisualDirector`:

| Class | Contents | Degradation |
| --- | --- | --- |
| P0 | Ground telegraphs, threshold flashes, window pulses, current target's status overlay, own buff row | Never degrades. |
| P1 | Own casts/impacts, damage dealt or received, statuses the player applied | Degrades last. |
| P2 | Other players' impacts and combat text, statuses on non-target entities | Degrades second. |
| P3 | Trails, ambient aura density, decorative burst extras | Degrades first. |

Budgets are per channel: live particle count, combat-text entries, and aura
emitters (auras cap at the nearest N entities but always include the current
target). When a budget is exceeded, the director scales down P3 quantities,
then P2, then P1. P0 is untouchable.

The degradation policy is a Hub Options choice: **own-first** (default) or
**uniform**, plus a master intensity slider.

## Hub Options

New "Combat Visuals" category following the existing notification-options
pattern, all client-side:

- Combat text master toggle; damage numbers and event words separately.
- Damage-number merging on/off.
- Status auras on/off.
- Telegraph opacity.
- Degradation policy (own-first / uniform) and master intensity slider.

## Roster Coverage

Every roster mechanic maps onto the framework vocabulary with no per-skill
client code:

| Mechanic | Framework expression |
| --- | --- |
| Smith Stagger / Fracture | Buildup bar + threshold flash + `break` event word + empowered-slot glow. |
| Smith Brace / counter | `window_open` pulse + `counter` event word. |
| Smith Tempered / Reinforce | Buff row stacks and timers. |
| Trapper Snare / Blast Trap | Area records with `armed`/`triggered` states + `Deadfall` burst events. |
| Trapper Bleed depth | Status stacks + drip aura + icon row. |
| Trapper Quarry | `mark` event + mark aura + icon. |
| Pilferer Tempo | Buff row stacks. |
| Pilferer openers / Slip Away | Empowered-slot glow + `window_open` pulse. |
| Pilferer Pilfer | Stolen boon appears on buff row; `consume` event on the target. |
| Warden Ward | Disc + standing boundary ring; `Moving Ward` is an upsert. |
| Warden Step In / Shared Burden | `shield` events + shield overlay segments. |
| Corroder Corrosion | Stacking rustflake aura + icon stacks. |
| Corroder Spill | `ray` events from source to recipients. |
| Corroder Collapse | `consume` event with magnitude-scaled burst. |
| Corroder Reaction | Alternating `window_open` pulses + empowered-slot glow. |
| Handler constructs | Ordinary entities: nameplates, statuses, auras all apply. |
| Handler Overtune / Final Turn | Aura + buildup bar on the construct + entity-following warning ring. |
| Handler Recall / Scrap | `shield` event on the Handler; `consume` + burst on the construct. |

## Error Handling

- Unknown style code → generic fallback style from the definition color.
- Events referencing entities not loaded client-side → dropped silently.
- Area snapshot re-sent on join/approach; stale areas expire client-side by
  absolute time even if a remove packet is lost.
- All option combinations must leave P0 information visible; toggles disable
  presentation channels, never the underlying sync.

## Testing

- Combat-text merging and budget/degradation ordering implemented as pure
  classes with unit tests.
- Admin command `/vrpg vfx` fires synthetic events, statuses, and areas for
  each `kind` and renderer.
- `/vrpg vfx stress <events-per-second>` verifies degradation ordering and
  budget caps in game.
- Acceptance ties into the Gate A checklist item: "See cooldown, cost,
  insufficient-resource, hit, experience, and level feedback without chat or
  particle spam."

## Out of Scope

- Shader effects, animated decals, entity tint/glow overlays (fidelity tier
  above this design).
- Per-skill bespoke art beyond the data-driven style blocks.
- Sound design (a later companion to the same event channel).
- PvP/friendly-fire presentation rules (combat itself excludes them today).
