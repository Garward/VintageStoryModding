# KineticPiston — Mod Author's Guide

`KineticPiston` translates named shape elements along a single axis, driven by
the kinetic network's RPM. It is the system behind the crusher head, the plate
piston, and any "moving part that goes back and forth or pushes outward."

This document covers the JSON properties, the motion modes, runtime offset
queries, and the gotchas you need to know to model and wire pistons correctly.

## TL;DR

```json
{ "name": "KineticPiston", "properties": { "pistons": [
  { "element": "head", "axis": "Y", "mode": "oscillate", "waveform": "sine",
    "travel": 14, "ratio": 1, "invert": true }
] } }
```

The behavior reads `pistons[]` from its properties. Each entry binds **one
shape element** to a movement profile. Elements not listed here are static —
listing an element in this array is what makes it movable.

## Properties

| Field         | Type   | Default     | Meaning |
|---------------|--------|-------------|---------|
| `element`     | string | (required)  | Shape element name to translate. Must exist in the block's shape JSON. |
| `axis`        | enum   | `Y`         | `X`, `Y`, or `Z`. Direction of motion in block-local space. |
| `mode`        | enum   | `oscillate` | `oscillate`, `directional`, or `sourceTimed` (see below). |
| `waveform`    | enum   | `sine`      | `sine` or `triangle`. Oscillate mode only. |
| `travel`      | float  | `6`         | Distance in 1/16ths (voxels). Same unit as shape `from`/`to`. |
| `ratio`       | float  | `1`         | Speed multiplier. Negative reverses direction. Semantics differ per mode. |
| `phaseOffset` | float  | `0`         | Radians. Shifts where in the cycle this piston starts. Useful when several pistons share a network and you want them out of phase. |
| `invert`      | bool   | `false`     | Negates the offset. For oscillate, flips the wave below zero. For directional, swaps which sign extends. |

All distances are voxels (1/16 of a block), matching the units in shape JSON.

## Mode: `oscillate`

Sinusoidal or triangular reciprocation. Stateless — driven entirely by elapsed
time and current network RPM. Network rotation direction is ignored
(`abs(rpm)` is what feeds the wave), so the piston behaves the same whether
the network is spinning forward or backward.

- `ratio` is **cycles per shaft revolution.** `ratio: 1` means one full
  up-down cycle per shaft turn.
- `travel` is the **peak-to-trough swing.** With `invert: false`, sine
  oscillates in `[0, travel]` (rest = 0, max = +travel). With `invert: true`
  it oscillates in `[-travel, 0]` (rest = 0, max = -travel) — useful for
  pistons that should push *down* or *out* from their rest pose.
- The wave's **maximum extension** is reached at phase π (half a cycle).

### Choosing a rest pose

The element's `from`/`to` in the shape JSON is its **rest position**. The
piston offset is added on top. Author your shape with the element at the
position where it should sit when stopped.

For the crusher head, the head element rests near the top of the block (`y =
8..12`). With `axis: Y`, `travel: 14`, `invert: true`, the head's offset
sweeps from 0 down to -14 and back, so the head's actual y-range during
animation is `(8..12) + [0, -14]` = down to `(-6..-2)` at the bottom of the
swing.

## Mode: `directional`

One-way travel that persists across save/load. The piston advances on
positive RPM, retracts on negative RPM, and clamps at `[0, travel]`. Position
is saved in the BlockEntity tree as `pistonPos{i}` and ticks on both server
and client so authoritative state stays in sync.

- `ratio` is **voxels of travel per shaft revolution.** `ratio: 0.5` means
  the piston extends half a voxel per turn of the shaft.
- `invert: true` swaps the relationship — the piston extends on negative RPM
  instead of positive.
- Use this for lifts, gates, drills, or any "extend then hold" motion where
  the position should survive unloading the chunk.

## Children move with their parent

Shape elements with `children` (the standard VS shape feature) inherit the
parent's translation. If you put a `plateRod` child inside a `plate` element
that's listed as a piston, the rod moves with the plate automatically. You
do **not** list child elements separately in the `pistons` array — only the
top-level moving element.

This is the cleanest way to keep a piston's "visible driver" attached to its
plate (see the no-floating-kinetics rule): make the rod a child of the plate.

## Reading the live offset in your BlockEntity

If your BlockEntity needs to know where a piston is *right now* — for
hitboxes, particle spawns, sound triggers, recipe gating — fetch it from the
behavior:

```csharp
var piston = GetBehavior<BEBehaviorKineticPiston>();
Vec3f offset = piston?.GetOffsetFor("head") ?? new Vec3f();
// offset is in 1/16ths along the configured axis; other components are 0
```

For oscillating pistons there is also a phase query, useful for firing
once-per-cycle effects (impact sounds, screen shake, recipe ticks):

```csharp
float phase = piston.GetCurrentPhaseFor("head"); // radians, 0..2π
// Maximum extension is at phase π. Compare across ticks to detect crossing.
```

`GetCurrentPhaseFor` is server-callable, so you can drive authoritative
gameplay (recipe progress, block damage) from the same waveform the client
renders.

### Subscribing to phase landmarks

For the common "fire once when the piston bottoms out / returns to rest" use
case, register a callback instead of polling the phase each tick. The
behavior tracks `lastPhase` per element internally and invokes your callback
on forward crossings of the threshold:

```csharp
public override void Initialize(ICoreAPI api)
{
    base.Initialize(api);
    var piston = GetBehavior<BEBehaviorKineticPiston>();
    piston?.OnPhaseCross("head", MathF.PI, OnBottomOut);
}

private void OnBottomOut()
{
    if (Api.Side == EnumAppSide.Client) SpawnImpactDust();
    else AdvanceCraftingTick();
}
```

Useful landmarks:

| Phase    | Meaning                                             |
|----------|-----------------------------------------------------|
| `0`      | rest position (start of stroke / end of return)     |
| `π/2`    | crossing rest going outward — max outward velocity  |
| `π`      | max extension (bottom-out for `invert: true` Y)     |
| `3π/2`   | crossing rest going inward — max inward velocity    |

The handler runs on whichever side this BlockEntity is on. If you need
different behavior per side (visuals on client, inventory on server),
gate inside the callback with `Api.Side`. Subscriptions live as long as the
BlockEntity — no manual unsubscribe.

The detector is wrap-aware (handles phase rolling through 2π) but assumes
you don't advance the wave by more than 2π per tick — which would require
~1200 RPM at `ratio: 1`, well above any realistic kinetic network.

## Wire it into the mesh splitter

The piston's renderer draws the moving element on the client. To stop the
block's static tesselation from drawing the same element a second time
underneath, override `OnTesselation` on your BlockEntity and use the splitter
to exclude managed elements:

```csharp
public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tess)
{
    string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
    var body = KineticMeshSplitter.TesselateBodyExcluding(
        Api as ICoreClientAPI, Block, tess, excluded);
    if (body != null) mesher.AddMeshData(body);
    return true;
}
```

`CollectManagedElements` walks every behavior on this BE and unions the
element names from `KineticAnimator` and `KineticPiston`. You don't have to
list them yourself.

## Gotchas

- **Element name typos are silent.** A piston pointing at an element that
  doesn't exist in the shape will tesselate to an empty mesh and render
  nothing. If a piston "doesn't move," check the spelling against the shape
  JSON first.
- **Geometry can extend outside `[0, 16]`.** A piston that descends below
  `y = 0` (like the crusher head at peak) will visually clip into the block
  below. If you want it to interact with the block below, you have to model
  the neighbor block to accommodate it (the crusher basin's walls top out at
  `y = 12` so the head's bottom voxel lands inside the basin's bowl).
- **`oscillate` ignores network direction; `directional` doesn't.** If you
  want a piston that reverses when the player swaps the gearbox direction,
  you need `directional` mode. Oscillate just runs whenever there's any RPM.
- **`sourceTimed` follows a `KineticSource` release.** It maps the current
  source release duration to `[0, travel]`, so a visual part can make one
  one-way trip over the whole stored burst regardless of how many seconds
  were released.
- **Rest pose is the shape JSON, not zero.** Don't model the element at
  `y = 0` and rely on `invert` to "lift it up to its rest pose" — that's not
  how the offset works. Model it at rest position; the offset is added on top.
- **Per-axis offset only.** A single piston entry moves along one axis. If
  you need diagonal motion, use two piston entries on the same element — but
  note that `GetOffsetFor` returns the offset for the *first* matching entry,
  so you'll need custom logic if you depend on the live offset value.
- **No floating kinetics.** Every visibly-animated part should have a
  visible physical driver — a rod, shaft, or stub the player can see pushing
  it. A floating plate that moves on its own breaks the "real machine"
  illusion the rest of the mod is going for.

## Worked example: the crusher head

The crusher's head plate slams down once per shaft revolution. Its config:

```json
{ "name": "KineticPiston", "properties": { "pistons": [
    { "element": "head", "axis": "Y", "mode": "oscillate", "waveform": "sine",
      "travel": 14, "ratio": 1, "invert": true }
] } }
```

What the numbers mean:
- `axis: Y` + `invert: true` — sine oscillates in `[-14, 0]`, so the head
  pushes downward from rest.
- `ratio: 1` — one full smash per shaft revolution. Faster network = faster
  smashing, automatically.
- `travel: 14` — the head descends 14 voxels at peak, putting its bottom
  face at crusher-local `y = -6` (6 voxels into the basin block below).
- The static `centerpole` element (modeled separately at `y = -6..13`) is
  the visible guide column the head slides along — the "physical driver"
  that satisfies the no-floating rule.

Because `oscillate` is stateless, the head doesn't accumulate position
across save/load — it picks back up wherever the wave happens to be when the
chunk loads.

## Worked example: the plate piston (directional-style usage)

The plate piston is configured as oscillate (the plate goes up and down
forever while powered), but the `plateRod` element inside it shows the
"children inherit translation" pattern:

```json
{ "name": "plate", "from": [0, 14, 0], "to": [16, 16, 16],
  "faces": { ... },
  "children": [
    { "name": "plateRod", "from": [6, 2, 6], "to": [10, 14, 10],
      "faces": { ... }
    }
  ]
}
```

The `plate` is listed in the `pistons` array; `plateRod` is not. As the
plate translates upward, the rod translates with it — its top stays attached
to the plate, its bottom stays hidden inside the casing at rest and pulls
into view as the plate rises.
