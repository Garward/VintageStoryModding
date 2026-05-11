# Building a Kinetic Machine: End-to-End Tutorial

This walkthrough builds a "Kinetic Pulper" mod that turns logs into pulp.
Total: 1 block JSON, 1+ recipe JSONs, ~50 lines of C#.

## Prerequisites

- Vintage Story 1.22.2+
- Vintage Kinematics installed (provides `VintageKinematics.Api`)
- A model file (`shapes/block/kineticpulper.json`) with a named element
  `blade` you'll rotate

## 1. Block JSON

`assets/mymod/blockTypes/kineticpulper.json`:

```json
{
  "code": "kineticpulper",
  "class": "BlockGeneric",
  "entityClass": "KineticPulper",
  "shape": { "base": "block/mymod/kineticpulper" },
  "behaviors": [
    { "name": "Kinetic",         "properties": { "role": "Custom", "stressImpact": 8, "tier": "iron" }},
    { "name": "KineticWorker",   "properties": { "workPerCycle": 200, "minRPM": 16 }},
    { "name": "KineticAnimator", "properties": { "rotators": [{ "element": "blade", "axis": "Y", "ratio": 1.0 }] }},
    { "name": "KineticSound",    "properties": { "sound": "mymod:pulper-loop", "minRPM": 16, "pitchScalesWithRPM": true }}
  ]
}
```

What each behavior does:

- **Kinetic**: joins the kinetic network, accounts for stress, drives the
  per-block tooltip. `tier: iron` means this block contributes 256 to the
  network's MaxRPM average.
- **KineticWorker**: accumulates progress while the network is running,
  fires `OnWorkCompleted` when it hits 200 RPM·seconds.
- **KineticAnimator**: rotates the `blade` shape element on the client.
- **KineticSound**: plays a looping sound; deduplicated against any other
  pulpers in the same network.

## 2. Recipe JSON

`assets/mymod/recipes/pulper/log-to-pulp.json`:

```json
{
  "ingredients": [{ "type": "item", "code": "game:log-*", "quantity": 1 }],
  "outputs":     [{ "type": "item", "code": "mymod:pulp", "quantity": 4 }],
  "workCycles": 1
}
```

Drop more files in the same folder to add more recipes. No code changes
needed.

## 3. The BlockEntity

`src/BEKineticPulper.cs`:

```csharp
using Vintagestory.API.Common;
using VintageKinematics.Api;

public class BEKineticPulper : BlockEntity
{
    private InventoryGeneric inventory;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        inventory = new InventoryGeneric(1, "pulper", api);

        var worker = GetBehavior<BEBehaviorKineticWorker>();
        if (worker != null) worker.OnWorkCompleted += OnPulpCycle;
    }

    private void OnPulpCycle(KineticWorkCompletedArgs args)
    {
        var input = inventory[0]?.Itemstack;
        var recipe = MyModRecipeRegistry.MatchPulper(input);
        if (recipe == null)
        {
            GetBehavior<BEBehaviorKineticWorker>().ResetProgress();
            return;
        }

        // Consume input, produce output
        inventory[0].TakeOut(1);
        inventory[0].MarkDirty();
        // ... emit output stack to your output slot or world drop ...
    }
}
```

Register the BE class in your mod's `ModSystem.Start`:

```csharp
api.RegisterBlockEntityClass("KineticPulper", typeof(BEKineticPulper));
```

## 4. Driving keyframe animations from RPM

If your model has a keyframe animation (the kind exported by VS Model
Creator with bones / joints), add `KineticAnimationDriver` to wire its
playback speed to network RPM:

```json
{ "name": "KineticAnimationDriver", "properties": { "animations": [
  { "code": "running", "animation": "running", "minRPM": 4, "speedAt32RPM": 1.0, "speedScalesWithRPM": true }
]}}
```

- `minRPM`: below this, the animation stops.
- `speedAt32RPM`: `AnimationSpeed` when the network is at 32 RPM.
- `speedScalesWithRPM`: if `false`, speed stays fixed at `speedAt32RPM`
  whenever RPM ≥ minRPM (useful for "either on or off" animations).

This behavior coexists with `KineticAnimator`. Partition by element: a shape
element jointed to a keyframe animation is owned by the animation system; a
non-jointed element can be procedurally rotated by `KineticAnimator` or
translated by `KineticPiston`. Don't drive the same element from both.

## 5. Pistons (linear oscillating or extending elements)

For elements that should reciprocate (hammer, pump, saw) or extend in one
direction with the network's rotation (lift, gate, drill), use
`KineticPiston`:

```json
{ "name": "KineticPiston", "properties": { "pistons": [
  { "element": "hammer", "axis": "Y", "mode": "oscillate",   "travel": 6,  "ratio": 1.0, "waveform": "sine" },
  { "element": "lift",   "axis": "Y", "mode": "directional", "travel": 16, "ratio": 0.5 }
]}}
```

Modes:

- `oscillate`: sinusoidal or triangular reciprocation. Stateless. `ratio`
  is cycles per shaft revolution. Network rotation direction doesn't matter.
- `directional`: extends on positive RPM, retracts on negative, clamps at
  `[0, travel]`. `ratio` is voxels of travel per shaft revolution. Position
  persists across save/load. Set `invert: true` to flip which sign extends.

`travel`, `ratio` for directional mode, and the returned offset vector are
all in 1/16ths (the same unit shape JSON uses).

In your BE, retrieve the current offset and apply it to the element when
you build your mesh:

```csharp
var piston = GetBehavior<BEBehaviorKineticPiston>();
Vec3f offset = piston?.GetOffsetFor("hammer") ?? new Vec3f();
// translate the "hammer" element by `offset` in your mesh assembly
```

## 5b. Wire it all up: the OnTesselation override

`KineticAnimator` and `KineticPiston` render their managed elements through
their own client-side `IRenderer`. To prevent the block's default
tesselation from drawing those same elements *again* (which would cause
visible overlap and z-fighting), override `OnTesselation` on your BE and
ask the splitter to tesselate the body minus the managed elements:

```csharp
public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tess)
{
    string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
    var body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tess, excluded);
    if (body != null) mesher.AddMeshData(body);
    return true;
}
```

That's the entire glue. The splitter walks your behaviors, collects the
union of element names from `KineticAnimator` and `KineticPiston`, and
returns a static-body mesh with those elements removed. The renderers
handle the moving parts.

If you also need to render custom static decorations (item-on-pedestal
style), tesselate them and call `mesher.AddMeshData` on each before
returning.

## 5c. Multiblocks (footprint larger than 1×1×1)

If your machine occupies more than one block (the Kinetic Sieve is a 1×1×3
drum, for example), use vanilla's `Multiblock` block behavior to declare the
footprint and let VK do the rest.

`assets/mymod/blockTypes/kineticsieve.json` (excerpt):

```json
"variantgroups": [
  { "code": "side", "states": ["n", "e", "s", "w"] }
],
"behaviorsByType": {
  "*-n": [{ "name": "Multiblock", "properties": { "sizex": 1, "sizey": 1, "sizez": 3, "cposition": { "x": 0, "y": 0, "z": 0 } }}],
  "*-s": [{ "name": "Multiblock", "properties": { "sizex": 1, "sizey": 1, "sizez": 3, "cposition": { "x": 0, "y": 0, "z": 2 } }}],
  "*-e": [{ "name": "Multiblock", "properties": { "sizex": 3, "sizey": 1, "sizez": 1, "cposition": { "x": 2, "y": 0, "z": 0 } }}],
  "*-w": [{ "name": "Multiblock", "properties": { "sizex": 3, "sizey": 1, "sizez": 1, "cposition": { "x": 0, "y": 0, "z": 0 } }}]
}
```

How the pieces fit together:

- `sizex / sizey / sizez`: footprint in blocks.
- `cposition`: the offset within that footprint where the controller cell
  (your real block, with the BE) lives. The other cells get auto-filled
  with vanilla `BlockMultiblock` placeholders that have no BE.
- One entry per facing variant: when the block rotates, the footprint and
  the controller's position within it both rotate, so each `*-n / *-e /
  *-s / *-w` needs its own `cposition`.

What VK handles automatically:

- **Kinetic adjacency**: shafts touching any filler cell join the same
  network as the controller. `WorldNodeProvider` resolves filler positions
  back to their controller before walking the graph.
- **Funnel pulls / pushes**: a funnel pointed at a filler cell sees the
  controller's inventory, not nothing. `BEFunnel` uses
  `MultiblockHelper.GetMultiblockAwareBE` for both source and target
  lookups.

What you handle in your own code:

When your BE looks at neighbors (for inventory probes, ignition checks,
heat sources, anything that asks "what's in that block"), don't call
`world.BlockAccessor.GetBlockEntity(pos)` directly if the neighbor might be
another mod's multiblock filler. Use the helper instead:

```csharp
using VintageKinematics.Api;

BlockEntity neighbor = MultiblockHelper.GetMultiblockAwareBE(Api.World, pos);
```

This returns the controller BE when `pos` is a filler cell, the BE at
`pos` when there's one directly, and null otherwise. Cheap; safe to call
in hot paths.

Selection box, collision box, light absorption, and break-on-controller
behavior are all handled by the vanilla `Multiblock` behavior. You don't
need to set those per cell.

## 5d. Restricting kinetic input to specific cells (`KineticMultiblock`)

By default, every filler cell of a multiblock is treated as a coaxial
shaft passthrough: a shaft against any face of any cell will join the
network. That's correct for a 1×1×3 drum (the Kinetic Sieve), but wrong
for anything with a visible shaft stub on only one side, like the Kinetic
Bore: you don't want power flowing in through the back of the housing
just because the BFS found a matching axis there.

Add the `KineticMultiblock` block-entity behavior and declare which cells
accept input. Two ways to declare, mixable on the same block:

```json
"entityBehaviorsByType": {
  "*-n": [
    { "name": "Kinetic",         "properties": { "role": "Shaft", "stressImpact": 96, "axis": "Z" } },
    { "name": "KineticAnimator", "properties": { "rotators": [ { "element": "ShaftStub", "axis": "Z", "ratio": 1 } ] } },
    { "name": "KineticMultiblock" }
  ]
},
"attributes": {
  "kineticShaftCells":    [ { "x": 1, "y": 1, "z": 2 } ],
  "kineticShaftElements": [ "ShaftStub" ]
}
```

- `kineticShaftCells` — explicit cell coords in the **unrotated**
  (rotateY=0) claim. Useful when the input cell isn't pinned to a visible
  element (e.g. a hidden coupler on top of the housing).
- `kineticShaftElements` — names of shape elements; the behavior resolves
  each name to the cell containing the element's bounding-box centroid.
  This is the path of least resistance: the input cell stays in lockstep
  with whatever cell the visible socket actually lives in, so moving the
  stub in the model doesn't require touching JSON.

The behavior reads the variant's `shape.rotateY` and the `cposition` from
the vanilla `Multiblock` behavior, rotates the declared cells in place,
and subtracts cposition to produce the controller-relative offsets the
network uses. You only declare cells / elements **once**, in the base
orientation; every facing variant is derived automatically.

Behavior under the hood:

- Cells listed (directly or via element) form coaxial edges with external
  shafts as normal.
- Every other cell of the claim is demoted to `Role.Custom` for
  edge-formation only, so the default coaxial rule refuses to bridge into
  it. Internal connectivity to the controller still works through the
  intra-multiblock free edge in `WorldNodeProvider`.
- The vanilla MP bridge also refuses to bridge into non-shaft cells, so
  vanilla axles can't sneak in through a side face either.

When to skip this:

- Your multiblock is genuinely a 1×N coaxial shaft (sieve drum, conveyor
  drum). The default "every filler is a shaft cell" is what you want.
- Your machine only has one input face anyway because the other faces are
  occupied by inventory / fuel / output blocks in the model.

When to use it:

- Any multiblock with directional input (one stub on one face, blank on
  the others) — the bore, future excavators / drills.
- Multiblocks where the input cell isn't on a face of the controller and
  the default coaxial rule wouldn't form the edge.

## 6. What the API does for free

- Network membership and stress accounting on placement / removal.
- Tooltip lines: status, stress, idle/active source labels.
- Work-cycle accumulator gated on conflict, overstress, idle source.
- Per-frame element rotation on the client, including pivot extraction
  from the shape's `RotationOrigin` and block-rotation handling.
- Per-frame element translation on the client (oscillating or directional
  pistons), with directional positions persisted across save/load.
- Static body tesselation that excludes the managed elements
  automatically; call `KineticMeshSplitter` from your `OnTesselation`.
- RPM-scaled keyframe animation speed via `KineticAnimationDriver`.
- Deduplicated looping sound; no ear-shattering stacking when many
  pulpers are nearby.
- Auto-pause when the network goes conflicted or overstressed; auto-resume
  on recovery.
- Multiblock kinetic-input restriction via the `KineticMultiblock`
  behavior: declare shaft cells in JSON, get variant-rotation and
  cposition math for free (see 5d).

## 7. What's still your responsibility

- Recipe-matching logic (`MyModRecipeRegistry.MatchPulper`).
- Inventory wiring and output drop / slot logic.
- Block model and texture.
- Sound asset (`assets/mymod/sounds/pulper-loop.ogg`).

## Diagnostic commands

`/vk netinfo` lists every kinetic network with node count, source RPM,
stress, MaxRPM, and conflict state.
