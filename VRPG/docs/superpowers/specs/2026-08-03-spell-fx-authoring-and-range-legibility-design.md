# Spell FX Authoring and Range Legibility — Design

Date: 2026-08-03
Status: Approved for planning

## Problem

Two goals drive this work:

1. **Authoring.** Each spell should be easy to customise from data, so a new spell's
   look is a JSON edit rather than a C# change.
2. **Feel and legibility.** An impact should be satisfying enough that a player casts
   the spell to watch it, and its silhouette alone should tell nearby players the
   general range of the ability.

A third problem motivated the investigation: describing in-game visual problems to a
model is unreliable, because screenshots destroy the runtime values that explain what
went wrong. That turned out to be a symptom, not the root cause — see Finding 1.

## Findings

Evidence gathered from `src/Client/Visuals/ProceduralImpactFx.cs`,
`src/Client/Visuals/ParticleEffectGeometry.cs`, `src/Client/Visuals/VisualBudget.cs`,
`src/Data/Definitions/SkillDefinition.cs`, and
`assets/vrpg/vrpg/skills/skyfall_anvil.json`.

### 1. The authoring surface cannot express per-spell identity

`SkillImpactVisualDefinition` exposes four quantity floats over four layers whose
character is hardcoded in C#:

| Layer | Colour | Location |
| --- | --- | --- |
| sparks | fixed yellow `ToRgba(255, 255, 201, 82)` | `ProceduralImpactFx.cs:187` |
| embers | fixed orange `ToRgba(255, 255, 116, 28)` | `ProceduralImpactFx.cs:243` |
| fire flash | cloned vanilla `ExplosionFireParticles` | `ProceduralImpactFx.cs:224` |
| debris | ground block colour via `ColorByBlock` | `ProceduralImpactFx.cs:125` |

Layer lifetimes are constants (`0.58 / 0.68 / 0.42 / 0.24 / 0.38`). The authored
`skill.Color` reaches exactly one consumer — the shockwave ring
(`ProceduralImpactFx.cs:58`).

Consequence: every spell produces the same orange rock explosion with a differently
tinted ring. Authors can only vary *how much* of a fixed effect appears, never *what*
it is. This is the root cause of both "spells don't feel distinct" and "the code looks
technically fine" — the defect is an absent data axis, not a broken code path.

### 2. No layer marks the actual area of effect

`ParticleEffectGeometry.RadialSpeed` returns `radius * coverage / lifetime * speedScale`.
The lifetime term cancels against the particle's real lifetime, so:

```
extent = radius x coverage x expansionSpeedScale x particleDurationScale
```

For `skyfall_anvil` (radius 2.8m, `expansionSpeedScale` 1.35, `particleDurationScale`
0.9, product 1.215):

| Layer | coverage | extent | vs. AoE |
| --- | --- | --- | --- |
| dust | 0.75 | 2.55m | 91% |
| debris, sparks | 0.65 | 2.21m | 79% |
| embers | 0.46 | 1.57m | 56% |
| fire flash | 0.35 | 1.19m | 42% |
| shockwave | `ShockwaveRadiusScale` 1.2 | 3.36m | 120% |

Particle layers systematically undershoot the hitbox by a per-layer-varying amount,
while the only element that draws a visible edge — the shockwave ring — draws it 20%
outside the real boundary. Nothing in the effect corresponds to 2.8m.

### 3. `expansionSpeedScale` and `particleDurationScale` are range knobs disguised as taste knobs

Both multiply extent directly. An author tuning "how long the dust hangs" silently
changes what the effect claims about its range. They are presented as aesthetic dials
and are not.

### 4. Lifetime compensation hardcodes a calendar-speed assumption

`NormalSpeedLifetimeCompensation = 0.2f` (`ProceduralImpactFx.cs:19`) compensates for
`ParticlePoolQuads` multiplying provider lifetimes by `5 / sqrt(calendarSpeed / 60)`.
The constant is only correct at `calendarSpeed == 60`. On any other world, every
lifetime shifts, and with it every extent. Range legibility varies per world.

### 5. Budget starvation can erase an effect entirely

`VisualBudget.QuantityScale` (`VisualBudget.cs:38`) returns `clamp(1 - 2*load, 0, 1)`
for `Cosmetic`, reaching zero at `load == 0.5` (450 of 900 particles/sec). Other
players' abilities can render as nothing, which is the worst possible outcome for an
effect whose job is to communicate danger.

### 6. The particle budget window is tumbling, not sliding

`VisualBudget.RollWindow` (`VisualBudget.cs:61`) hard-resets `spent` to zero once
1000ms elapse rather than sliding. The scale applied to an identical cast therefore
depends on sub-second phase: a cast at 0.95s into a window is starved, the same cast at
1.01s is full strength. This makes effects non-reproducible run to run, which
undermines both tuning and legibility. The class docstring describes it as sliding, so
this is a genuine defect rather than an intentional simplification.

## Goals

- Spell impact layers are authored in data, with colour, timing and shape per layer.
- A shared preset library keeps common authoring to a single line.
- A rim layer marks the true AoE boundary, is locked to the resolved radius, and cannot
  be erased by the particle budget.
- The informative part of an effect lands within 200ms of the gameplay event it
  represents. Later layers are permitted only as decoration.
- Layer timing can be staggered so impacts resolve over time instead of firing as one
  simultaneous pop.
- A structured FX trace records authored vs. resolved values per layer, so tuning
  questions and regressions are answerable without screenshots.

## Non-goals

- Video or frame-by-frame capture. Rejected: it costs hundreds of near-identical
  images and still cannot recover requested-vs-applied quantities, which is the
  information that actually explains a bad-looking effect.
- Locking interior layer extent to the radius. Only the rim is contractual; interiors
  stay freely authorable (subject to the clamp in Pillar B).
- Reworking projectile trail visuals (`SkillParticleDefinition`), auras, ground
  telegraphs, or combat text. Impact FX only.
- Rebalancing any spell's gameplay numbers.

## Design

### Pillar A — Layers as data

Replace the four fixed quantity floats with an authored layer list. Each layer is a
`SkillFxLayerDefinition`:

```jsonc
{
  "role": "debris",             // debris | dust | sparks | fire | rim | custom
  "model": "cube",              // cube | quad
  "color": "$skill",            // "$skill" | "$ground" | "#rrggbbaa"
  "quantity": 48.0,
  "sizeMin": 0.12,
  "sizeMax": 0.30,
  "lifetimeSeconds": 0.58,
  "gravity": 1.35,
  "coverage": 0.65,             // fraction of radius this layer reaches
  "glow": 0,                    // 0-255, maps to VertexFlags
  "delaySeconds": 0.0,          // stagger; see below
  "terrainCollision": true,
  "opacityEvolve": { "fn": "quadratic", "rate": -16.0 },
  "sizeEvolve":    { "fn": "linear",    "rate": -0.14 }
}
```

Colour tokens resolve at spawn: `$skill` to `style.ColorRgba`, `$ground` to the existing
`GetRandomColor` lookup, literals parsed by `SkillDefinitionValidator.TryParseColor`.
This is what gives a frost spell frost-coloured debris without a C# change.

**Presets.** A preset library under `assets/vrpg/vrpg/fx/impact/*.json` holds named
layer lists. A skill references one and overrides fields:

```jsonc
"impactVisual": {
  "enabled": true,
  "preset": "vrpg:stone_slam",
  "overrides": { "sparks": { "color": "#88ccffff" } },
  "shockwave": true,
  "cameraShake": 0.42
}
```

Merge semantics: the preset supplies the layer list; `overrides` is keyed by layer
`role` and merges field-wise over the preset's layer of that role. A skill may instead
supply `layers` directly, which replaces the preset list entirely. Supplying both
`preset` and `layers` is a validation error.

**Timing stagger.** `delaySeconds` schedules a layer relative to impact. Everything
currently spawns at t=0, which reads as a single pop; staggering flash / core / debris /
dust across roughly 0 / 40 / 120 / 300ms makes an impact resolve. Delayed layers are
queued on the existing client tick listener and are budget-checked at *spawn* time, not
at schedule time.

Each layer declares `informative: true|false` (default `false`). Informative layers
carry gameplay meaning — where the ability hit, how far it reached — and are bound by
the 200ms contract in Pillar B. Decorative layers are free to trail well past it;
settling dust at 300ms claims nothing and misleads nobody. The `rim` role is always
informative regardless of what the author writes.

### Pillar B — Rim layer, range contract, and the 200ms timing contract

A `rim` role layer is the only element bound to the AoE:

- Its extent is always `resolvedRadius`. `coverage`, `expansionSpeedScale` and
  `particleDurationScale` do not apply to it.
- It is spawned at `VisualPriority.Critical`, which returns scale 1.0 and bypasses the
  budget entirely, so it cannot be starved to nothing.
- It is a dense, brighter band at `r ≈ radius` rather than a uniform falloff. A falloff
  does not read as an edge; a band does.
- Sample count scales with circumference via the existing
  `ParticleEffectGeometry.RingSamples`, so large areas stay legible without unbounded
  particle counts.

**Interior clamp.** Interior layers may reach shorter than the rim but never further.
Effective extent is clamped to `radius`, i.e.
`min(coverage * expansionSpeedScale * particleDurationScale, 1.0) * radius`. Without
this, interiors can wash past the rim and the edge stops reading. The clamp is applied
at resolve time and recorded in the trace when it binds.

**Shockwave.** `ShockwaveRadiusScale` currently defaults to 1.15 and is 1.2 for
`skyfall_anvil`, drawing a ring outside the hitbox. Since the ring is the strongest edge
cue on screen, it is pinned to 1.0 and the knob is removed. Spells wanting a wider
flourish use a non-rim custom layer.

**Calendar speed.** Replace the `NormalSpeedLifetimeCompensation` constant with a
lifetime conversion computed from the world's actual calendar speed, so authored
lifetimes mean real seconds on every world.

**Budget window.** Convert `VisualBudget` to a genuine sliding window (bucketed
accumulation over the trailing 1000ms) so identical casts produce identical scale
regardless of sub-second phase.

#### The 200ms timing contract

200ms is the **ceiling, not the target**. It exists so a laggy server still feels
responsive; it is a failure threshold, not a budget to spend. The design target for
client-controlled delay is zero, and the tooling reports the distribution so drift
toward the ceiling is visible long before anything breaches it. Decorative layers are
unbound.

The budget splits into three parts, only two of which the mod controls:

1. **Client scheduling (controlled, target zero).** From `CombatVisualEventPacket`
   arrival to informative-layer spawn. Informative layers default to `delay == 0` and
   the `rim` role is pinned there. Any informative layer with a non-zero delay is a
   validator warning naming the skill; `delaySeconds > 0.2` is a hard load-time
   rejection. This portion is deterministic and statically checkable, so in practice it
   should contribute nothing and the whole ceiling stays available to absorb parts 2
   and 3.

2. **Carrier synchronisation (controlled, the real risk).** Deliveries like
   `targeted_drop` and projectiles apply damage server-side while the *visual* impact is
   driven by a client-simulated carrier. `skyfall_anvil` drops from `height 9.0` at
   `fallSpeed 1.25` with `gravity 18.0`; any divergence between the server's damage
   moment and the client's landing moment lands squarely in this budget. The existing
   `CombatVisualFlags.SynchronizeToCarrier` is the hook — the contract is that when the
   flag is set, FX fire on carrier landing, and the packet's stamped event time is used
   to detect drift rather than to trigger.

3. **Network transit (not controlled, measured only).** Reported alongside so a
   200ms breach can be attributed rather than guessed at.

**Measurement.** `CombatVisualEventPacket` gains `[ProtoMember(13)] long ServerEventMs`,
stamped from server elapsed time when the gameplay effect resolves. Raw client/server
clock diffs are meaningless across machines, so the client maintains a per-session
offset estimate (minimum observed `clientRecvMs - ServerEventMs` over a rolling window,
which approximates one-way transit plus skew) and reports **drift relative to that
baseline**. Absolute latency is not claimed; *change* in gameplay-to-visual delta is,
and that is what detects a desync.

Because the ceiling is not the target, the trace reports the **distribution** of
gameplay-to-visual delta per skill — median, p95, max — rather than only breaches. A
skill whose p95 has crept from 20ms to 140ms is still passing and is already the thing
worth looking at.

This matters concretely for multi-hit skills: `skyfall_anvil` has
`hitIntervalSeconds: 0.2`. At the contract boundary, consecutive hits become
indistinguishable — which is the practical justification for 200ms as the limit.

### Pillar C — FX trace

A `/vrpg fx trace on|off` client command writes NDJSON to the mod data directory, one
record per impact:

```jsonc
{
  "t": 128394, "tick": 40213, "ev": "impact",
  "skill": "vrpg:skyfall_anvil", "preset": "vrpg:stone_slam",
  "radius": 2.8,
  "sync": { "serverEventMs": 128330, "clientRecvMs": 128372, "baselineMs": 38,
            "driftMs": 4, "carrierLandMs": 128376, "informativeSpawnMs": 128376,
            "gameplayToVisualMs": 46, "ceilingMs": 200 },
  "budget": { "spent": 612, "perSec": 900, "load": 0.68, "scale": 0.0 },
  "layers": [
    { "role": "debris", "fired": false, "skipReason": "quantityScale==0",
      "reqQty": 48.0, "outQty": 0.0, "color": "#8fa05cbe",
      "coverage": 0.65, "extent": 1.82, "extentClamped": false,
      "lifetime": 0.58, "delay": 0.0, "size": [0.12, 0.30] },
    { "role": "rim", "fired": true, "priority": "Critical",
      "reqQty": 36.0, "outQty": 36.0, "extent": 2.80 }
  ],
  "pos": [512.3, 110.0, -889.7]
}
```

Load-bearing fields:

- `fired` + `skipReason` — turns a silent no-op into a labelled one.
- `reqQty` vs `outQty` — separates "authored too sparse" from "the budget ate it".
- `extent` vs `radius` — makes the range contract a number rather than a judgement.
- `extentClamped` — flags an author whose interior layer wanted to exceed the rim.
- `sync.gameplayToVisualMs` — makes the 200ms contract a number too, and its
  distribution over a session is the early warning that matters more than any single
  breach.

The trace is the iteration loop: after a tuning pass, diff two traces to see exactly
which resolved values changed.

## Components

| File | Change |
| --- | --- |
| `src/Data/Definitions/SkillDefinition.cs` | Add `SkillFxLayerDefinition`, `SkillFxEvolveDefinition`; extend `SkillImpactVisualDefinition` with `Preset`, `Layers`, `Overrides`; remove `ShockwaveRadiusScale` and the four quantity floats |
| `src/Data/Definitions/SkillFxPresetDefinition.cs` | New — preset record |
| `src/Data/VRPGDataRegistry.cs` | Register and load the FX preset asset category |
| `src/Data/SkillDefinitionValidator.cs` | Validate preset references, `preset` xor `layers`, colour tokens, evolve function names, layer roles |
| `src/Client/Visuals/FxLayerResolver.cs` | New — merges preset + overrides, resolves colour tokens, computes and clamps extent |
| `src/Client/Visuals/ProceduralImpactFx.cs` | Rewrite to iterate resolved layers instead of four hardcoded methods; add rim spawning and delayed-layer scheduling |
| `src/Client/Visuals/ParticleEffectGeometry.cs` | Add rim geometry; extent solver used by both spawn and trace |
| `src/Client/Visuals/VisualBudget.cs` | Sliding-window fix |
| `src/Client/Visuals/ImpactShockwaveRenderer.cs` | Pin ring to resolved radius |
| `src/Network/CombatVisualPackets.cs` | Add `ServerEventMs` (`ProtoMember(13)`), stamped when the gameplay effect resolves |
| `src/Client/Visuals/FxSyncTracker.cs` | New — rolling client/server offset baseline, per-skill delta distribution |
| `src/Client/Visuals/FxTrace.cs` | New — NDJSON writer, off by default |
| `src/Modules/Rpg/RpgCommandSystem.cs` | Register `/vrpg fx trace` |
| `assets/vrpg/vrpg/fx/impact/*.json` | New preset library |
| `assets/vrpg/vrpg/skills/*.json` | Migrate to preset references |

`ProceduralImpactFx` will shrink as its four bespoke spawn methods collapse into one
layer loop. Layer resolution lives in `FxLayerResolver` so it can be unit tested without
a client API, which is what makes the extent and timing contracts checkable in CI rather
than only in-game.

## Error handling

- Unknown preset code: validation error at load, skill's impact visual disabled, logged
  once with the skill code. The skill still casts and deals damage.
- Both `preset` and `layers` present: validation error, `layers` wins, warning logged.
- Unparseable colour: falls back to `$skill`, warning logged once per skill.
- Unknown `role` or evolve `fn`: validation error at load; the layer is dropped.
- Missing rim layer in a preset used by an area skill (`radius > 0`): validator warning,
  since the ability will not communicate its range.
- Informative layer with `delaySeconds > 0`: validator warning naming the skill and
  layer. Above `0.2`, a load-time rejection — the layer is demoted to decorative and the
  skill logs once, so a bad authoring value degrades the effect rather than breaking the
  timing contract silently.
- Missing or zero `ServerEventMs` (older server, mixed versions): sync measurement is
  skipped and `sync` is omitted from the trace record. FX still fire normally; timing
  is simply unmeasured rather than reported as zero drift.
- Trace write failure: disable tracing, log once, never throw into the render path.

## Testing

`tests/VRPG.Tests` additions, none requiring a running client:

1. **Extent contract.** For every skill definition with `radius > 0`, the resolved rim
   extent equals `radius` within tolerance, and no interior layer's extent exceeds it.
   This is the automated answer to "does the silhouette tell the truth about range?"
2. **Preset merge.** Overrides apply field-wise by role; absent fields inherit; `layers`
   replaces the preset list wholesale.
3. **Colour resolution.** `$skill`, `$ground` and literal tokens each resolve to the
   expected ARGB.
4. **Budget.** Sliding window returns identical scale for identical load regardless of
   phase within the second; `Critical` always returns 1.0.
5. **Timing contract (static).** For every skill, all informative layers resolve to
   `delay == 0`, and `rim` is pinned to zero even when authored otherwise. An
   informative layer authored above 0.2s loses its informative status — it still spawns
   at its authored delay, but as decoration, so it no longer counts toward the contract
   and no longer claims to represent the gameplay event.
6. **Sync tracker.** Given a synthetic stream of `(serverEventMs, clientRecvMs)` pairs
   with injected jitter and a constant clock offset, the baseline estimate converges to
   the true one-way floor and reported drift excludes the offset. A step change in
   latency shows up as drift rather than being absorbed into the baseline within the
   same window.
7. **Validation.** Each error case above produces the specified diagnostic and fallback.
8. **Migration parity.** Each migrated skill resolves to layer values matching its
   pre-migration hardcoded equivalents, so the preset library introduces no unintended
   visual change.

## Migration

Existing skills keep their look. Today's four hardcoded layers become the
`vrpg:stone_slam` preset with the current constants as its authored values; each skill's
current `dustQuantity` / `debrisQuantity` / `sparkQuantity` / `fireQuantity` become
per-role quantity overrides. Test 8 pins this. Rim layers are then added to presets as a
deliberate, visible change — the one intended difference from current behaviour, along
with the shockwave ring moving from 1.2x to 1.0x radius.

`expansionSpeedScale` and `particleDurationScale` are retained as interior-only taste
knobs, now subject to the interior clamp, so they can no longer misrepresent range.

## Open risks

- Pinning the shockwave to 1.0x will make existing impacts read slightly smaller. This
  is intended, but every tuned spell will want a look-over afterwards.
- The calendar-speed fix changes effective lifetimes on any world not at speed 60.
  Authored lifetimes become correct, but previously-tuned values were compensating for
  the bug and may need a pass.
- Carrier synchronisation for `targeted_drop` and projectile deliveries is the least
  controlled part of the 200ms budget and the hardest to test offline. The sync tracker
  measures it, but confirming it holds on a genuinely laggy server needs a real session;
  plan for a measurement pass rather than assuming the unit tests settle it.
- `ServerEventMs` changes the packet contract. Clients and servers on mixed VRPG
  versions degrade to unmeasured timing rather than breaking, but a version bump is
  warranted.
