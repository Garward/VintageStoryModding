# Building VK Machines: JSON First, C# Only When Needed

The recommended workflow for basic VK machines is now data driven. If your
machine consumes item stacks, spends rotation work, and produces item stacks,
you should start with the generic JSON processor:

- 1 blocktype JSON
- 1 shape JSON
- 1 or more `vkrecipe/process/<machineCode>/` recipe JSON files
- optional lang entries
- no C#

Use C# only when the machine has behavior the generic processor cannot express:
custom world interaction, special recipe matching, fluids, heat, entities,
non-item outputs, or unusual state.

## Prerequisites

- Vintage Story 1.22.2+
- Vintage Kinematics installed
- A model file, for example `assets/mymod/shapes/block/handlelathe.json`

## 1. Pure JSON Processor

This example builds a small machine that turns one stick into one hand crank.
It is a 1x2 multiblock: the bottom/controller block contains the shaft, and
the top block is decorative geometry. Add `KineticPiston` or
`KineticAnimationDriver` if that top geometry should move.

`assets/mymod/blocktypes/handlelathe.json`:

```json
{
  "code": "handlelathe",
  "class": "BlockKineticJsonProcessor",
  "entityClass": "KineticJsonProcessor",
  "variantgroups": [
    { "code": "side", "states": [ "n", "e", "s", "w" ] }
  ],
  "attributes": {
    "kineticShaftControllerCell": true,
    "vkProcessor": {
      "machineCode": "handlelathe",
      "titleLangCode": "mymod:handlelathe-title",
      "title": "Handle Lathe",
      "inputSlots": 1,
      "outputSlots": 1,
      "ioScope": "multiblock",
      "io": [
        { "type": "input", "face": "left", "slots": "inputs", "cell": { "x": 0, "y": 0, "z": 0 } },
        { "type": "input", "face": "up", "slots": "inputs", "cell": { "x": 0, "y": 1, "z": 0 } },
        { "type": "output", "face": "right", "slots": "outputs", "cell": { "x": 0, "y": 0, "z": 0 } },
        { "type": "output", "face": "down", "slots": "outputs", "cell": { "x": 0, "y": 0, "z": 0 } }
      ]
    }
  },
  "behaviorsByType": {
    "*": [
      { "name": "Multiblock", "properties": { "sizex": 1, "sizey": 2, "sizez": 1, "cposition": { "x": 0, "y": 0, "z": 0 } } }
    ]
  },
  "entityBehaviorsByType": {
    "*-n": [
      { "name": "Kinetic", "properties": { "role": "Shaft", "stressImpact": 8, "axis": "Z" } },
      { "name": "KineticMultiblock" },
      { "name": "KineticWorker", "properties": { "workPerCycle": 80, "minRPM": 4, "autoReset": true, "tickInterval": 0.25 } },
      { "name": "KineticAnimator", "properties": { "rotators": [ { "element": "Shaft", "axis": "Z", "ratio": 1 } ] } }
    ],
    "*-s": [
      { "name": "Kinetic", "properties": { "role": "Shaft", "stressImpact": 8, "axis": "Z" } },
      { "name": "KineticMultiblock" },
      { "name": "KineticWorker", "properties": { "workPerCycle": 80, "minRPM": 4, "autoReset": true, "tickInterval": 0.25 } },
      { "name": "KineticAnimator", "properties": { "rotators": [ { "element": "Shaft", "axis": "Z", "ratio": 1 } ] } }
    ],
    "*-e": [
      { "name": "Kinetic", "properties": { "role": "Shaft", "stressImpact": 8, "axis": "X" } },
      { "name": "KineticMultiblock" },
      { "name": "KineticWorker", "properties": { "workPerCycle": 80, "minRPM": 4, "autoReset": true, "tickInterval": 0.25 } },
      { "name": "KineticAnimator", "properties": { "rotators": [ { "element": "Shaft", "axis": "Z", "ratio": 1 } ] } }
    ],
    "*-w": [
      { "name": "Kinetic", "properties": { "role": "Shaft", "stressImpact": 8, "axis": "X" } },
      { "name": "KineticMultiblock" },
      { "name": "KineticWorker", "properties": { "workPerCycle": 80, "minRPM": 4, "autoReset": true, "tickInterval": 0.25 } },
      { "name": "KineticAnimator", "properties": { "rotators": [ { "element": "Shaft", "axis": "Z", "ratio": 1 } ] } }
    ]
  },
  "creativeinventory": { "general": [ "*-n" ] },
  "blockmaterial": "Wood",
  "drawtype": "json",
  "textures": {
    "wood": { "base": "game:block/wood/planks/generic" },
    "metal": { "base": "game:block/metal/sheet/iron" }
  },
  "shape": { "base": "mymod:block/handlelathe" },
  "shapeByType": {
    "*-n": { "base": "mymod:block/handlelathe", "rotateY": 0 },
    "*-e": { "base": "mymod:block/handlelathe", "rotateY": 90 },
    "*-s": { "base": "mymod:block/handlelathe", "rotateY": 180 },
    "*-w": { "base": "mymod:block/handlelathe", "rotateY": 270 }
  },
  "collisionbox": { "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 2, "z2": 1 },
  "selectionbox": { "x1": 0, "y1": 0, "z1": 0, "x2": 1, "y2": 2, "z2": 1 }
}
```

Important fields:

- `class: BlockKineticJsonProcessor` and `entityClass: KineticJsonProcessor`
  opt into the generic processor. This is the no-C# path.
- `vkProcessor.machineCode` links this block to recipes under
  `assets/<modid>/vkrecipe/process/<machineCode>/`.
- `inputSlots` and `outputSlots` control the visible/active GUI buffers.
  Internally the generic processor reserves slots `0..63` for inputs and
  `64..127` for outputs.
- `storageStyle` controls player interaction with those buffers. Omit it or
  use `slots` for the normal GUI. Use `crate` for crate-style world
  interaction: shift-right-click inserts into inputs, right-click takes from
  outputs, and ctrl does the bulk version. You can also set
  `inputStorageStyle` or `outputStorageStyle` separately.
- `KineticWorker.workPerCycle` is the recipe cost in RPM-seconds.
- `KineticAnimator` and `KineticPiston` can be added directly in JSON for
  moving parts.
- `vkProcessor.io` declares automation faces directly. `left` and `right`
  rotate with the machine placement; `cell` is an exact controller-relative
  block offset in the base/model orientation, also rotated with the machine.
  In this example, the top input is on the decorative block one cell above
  the controller.

## 2. Recipe JSON

`assets/mymod/vkrecipe/process/handlelathe/stick-to-handcrank.json`:

```json
{
  "machine": "handlelathe",
  "ingredient": {
    "type": "item",
    "code": "game:stick",
    "quantity": 1
  },
  "outputs": [
    {
      "type": "block",
      "code": "vintagekinematics:handcrank-y",
      "quantity": 1
    }
  ]
}
```

Drop more files in the same folder to add recipes. No code changes needed.
The folder name is used as the machine code if the recipe omits `"machine"`,
but declaring it explicitly makes templates easier to read.

## 3. Template Files Shipped With VK

VK ships an editable template under:

```text
assets/vintagekinematics/templates/jsonprocessor/
```

Copy that folder into your own mod, rename `exampleprocessor` to your machine
code, replace the shape, and add recipes under your own
`vkrecipe/process/<machineCode>/` folder.

The in-game creative-only block `JSON Machine Template` is the same workflow
packaged as a visible example. It converts sticks into hand cranks and shows
shaft animation, piston animation, multiblock IO, GUI slots, and output
pushing.

## 4. JSON IO

Prefer explicit `vkProcessor.io` entries for new generic JSON processor
machines:

```json
"io": [
  { "type": "input", "face": "left", "slots": "inputs", "cell": { "x": 0, "y": 0, "z": 0 } },
  { "type": "input", "face": "up", "slots": "inputs", "cell": { "x": 0, "y": 1, "z": 0 } },
  { "type": "output", "face": "right", "slots": "outputs", "cell": { "x": 0, "y": 0, "z": 0 } },
  { "type": "output", "face": "down", "slots": "outputs", "cell": { "x": 0, "y": 0, "z": 0 } }
]
```

Each entry supports:

- `type`: `input` or `output`.
- `face`: absolute `north`, `east`, `south`, `west`, `up`, `down`; relative
  `front`, `back`, `left`, `right`; or model-lip aliases
  `inputLipNorth`, `inputLipEast`, `inputLipSouth`, `inputLipWest`.
- `slots`: `inputs`, `outputs`, a single slot string like `64`, a range like
  `64-72`, or an object `{ "first": 64, "last": 72 }`.
- `cell`: exact controller-relative base-orientation offset, for example
  `{ "x": 0, "y": 1, "z": 0 }`.
- `cells`: use `"face"` to map every multiblock cell on the named face,
  `"controller"` for only the controller, or an array of exact offsets.
- `rotateCell`: optional `false` when an exact offset should stay in world
  coordinates. Leave it out for normal rotatable machines.

For broad multiblock face IO, use:

```json
"io": [
  { "type": "input", "face": "left", "slots": "inputs", "cells": "face" },
  { "type": "output", "face": "right", "slots": "outputs", "cells": "face" }
]
```

The older `vkProcessor.ioLayout` shortcut is still supported when `io` is not
present. Available fallback layouts:

- `leftInputRightAndDownOutput`: placement-facing left/top input,
  right/bottom output.
- `sideInputOppositeAndDownOutput`: a named side/top input, opposite/bottom
  output.
- `sideInputOppositeOutput`: a named side input and opposite side output.
- `topInputDownOutput`: top input and bottom output.

For layouts that use a named side, set `vkProcessor.inputFace` to one of:

- `inputLipNorth`, `inputLipEast`, `inputLipSouth`, `inputLipWest` for a
  model-relative side that rotates with the block's `side` variant.
- `north`, `east`, `south`, `west`, `up`, or `down` for fixed world faces.

For explicit `io`, use `cell` or `cells` to decide where each entry is
exposed. `vkProcessor.ioScope` is still used by the legacy `ioLayout` fallback:

- `controller` for single-cell IO.
- `multiblock` when funnels, belts, and output pushing should work against
  the whole claimed multiblock footprint.

Custom C# processors that extend `BEKineticItemProcessorBase<TRecipe>` can use
the same IO schema without being full JSON processors. Put it at top-level
`attributes.vkIo` and call `BuildJsonIOFaceMap()` from `BuildIOFaceMap()`:

```json
"attributes": {
  "vkIo": [
    { "type": "input", "face": "up", "slots": "inputs", "cells": "face" },
    { "type": "input", "face": "left", "slots": "inputs", "cells": "face" },
    { "type": "output", "face": "down", "slots": "outputs", "cells": "face" },
    { "type": "output", "face": "right", "slots": "outputs", "cells": "face" }
  ]
}
```

## 5. JSON-Backed Generators

Generators should put their common kinetic source tuning in JSON:

```json
"entityBehaviorsByType": {
  "*-n": [
    { "name": "Kinetic", "properties": { "role": "Shaft", "stressImpact": -1024, "axis": "X" } },
    { "name": "KineticSource", "properties": { "targetRPM": 16, "alwaysActive": false } }
  ]
},
"attributes": {
  "vkGenerator": {
    "tickMs": 500,
    "windSeconds": 1.25,
    "sourceRefreshThresholdSeconds": 1,
    "waterLitresPerSecond": 0.05
  }
}
```

Use `Kinetic` / `KineticSource` for the universal source facts: shaft role,
axis, SU capacity, target RPM, decay tick, and whether the source is always
active. Use `attributes.vkGenerator` for common timing and resource-rate
knobs. Current built-in examples:

- Coal Motor: `tickMs` and `sourceRefreshThresholdSeconds` are JSON; C# only
  decides whether the fuel slot contains burnable fuel and how long that item
  burns.
- Geothermal Steam Engine: `tickMs`, `windSeconds`, and
  `waterLitresPerSecond` are JSON; C# only checks for tapped geothermal heat
  and consumes adjacent water.
- Hand Crank: `windSeconds` is JSON; C# only handles player/activator
  direction.
- Treadwheel: `tickMs` and `windSeconds` are JSON; C# only checks mounted
  player movement and direction.
- Counterweight Drive: `maxChargeSeconds`, `windSecondsPerSecond`, and
  `clickWindSeconds` are JSON; C# only tracks winding and release state.
- Flywheels: charge/discharge stress, output RPM, storage time, burst cap,
  leak rate, efficiency, discharge refresh, idle spin decay, and `tickMs`
  live under `attributes.flywheel`; C# only handles charge/discharge state.
- Placed Backpack Flywheel: `tickMs`, `chargeStress`, `chargeEfficiency`, and
  `maxOutputRPM` are JSON; C# only bridges world kinetic charge into the item
  stack's stored charge.

## 6. When You Need C#

If the generic JSON processor is not enough, do not start from an empty
`BlockEntity`. Extend the same primitives instead:

```csharp
public class BEWeirdProcessor : BEKineticJsonProcessor
{
    // Override only the behavior that is genuinely custom.
}
```

For a custom recipe type or custom output behavior, extend the lower-level
base:

```csharp
public class BEWeirdProcessor : BEKineticItemProcessorBase<MyRecipe>
{
    protected override IOFaceMap BuildIOFaceMap() { /* reuse MachineIoLayouts */ }
    protected override MyRecipe FindRecipe(ItemStack input) { /* registry lookup */ }
    protected override IEnumerable<ItemStack> GetOutputs(MyRecipe recipe) { /* outputs */ }
    protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi) { /* GUI */ }
}
```

`BEKineticItemProcessorBase<TRecipe>` already handles inventory save/load,
claim-safe inventory packets, worker subscription, active input/output
ranges, GUI packet flow, output storage, output pushing, drop fallback, and
animated mesh splitting. Custom machines should add features on top of that
instead of reimplementing the same boilerplate.

### Adding Forge Press operations

Downstream mods can also add Kinetic Forge Press operations with JSON only. Put
files under `assets/<modid>/vkrecipe/forgepress/`. Each distinct
`operationCode` becomes one entry in the Forge Press dropdown; multiple
recipes may share the same operation and differ by ingredient.

```json
{
  "ingredient": { "type": "item", "code": "game:ingot-*", "quantity": 2 },
  "operationCode": "plate",
  "operationName": "Plate",
  "allowedVariants": [ "tinbronze", "bismuthbronze", "blackbronze", "iron" ],
  "outputs": [
    { "type": "item", "code": "game:metalplate-*", "quantity": 1 }
  ],
  "requiredTemperature": 1100,
  "pressTicks": 4
}
```

Fields:

- `operationCode`: stable machine-readable dropdown value. Reuse it to
  group several ingredient variants under one visible operation.
- `operationName`: visible dropdown text.
- `ingredient`: input stack; wildcard captures can be reused in wildcard
  output codes.
- `allowedVariants`: optional whitelist for the captured wildcard value.
- `outputs`: item/block outputs produced by one completed operation.
- `requiredTemperature`: chamber temperature required before work can run.
- `pressTicks`: intended recipe cost in press cycles.

## 7. Driving keyframe animations from RPM

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
non-jointed element can be procedurally rotated by `KineticAnimator`,
translated by `KineticPiston`, stretched by `KineticStretch`, or solved as a
pleat strip by `KineticLinkedPleat`. Don't drive the same element from more
than one movement behavior.

## 8. Pistons (linear oscillating or extending elements)

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

## 9. Wire it all up: the OnTesselation override

`KineticAnimator`, `KineticPiston`, `KineticStretch`, and
`KineticLinkedPleat` render their managed elements through their own
client-side `IRenderer`. To prevent the block's default
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
union of element names from all VK movement behaviors, and returns a
static-body mesh with those elements removed. The renderers handle the
moving parts.

If you also need to render custom static decorations (item-on-pedestal
style), tesselate them and call `mesher.AddMeshData` on each before
returning.

## 10. Linked pleats (accordion / bellows folds)

For accordion-like geometry where a row of flat strips should stay snapped
between a fixed bottom edge and a moving top edge, use `KineticLinkedPleat`.
This is a client-side renderer for shape elements; it does not affect the
kinetic graph or server logic.

Each chain solves shared joints between `bottom` and `top`. The top anchor
is offset by `topTravelY * progress`, where `progress` follows the selected
`waveform`, `ratio`, `phaseOffset`, and `invert` settings. Every listed
element is rendered as one strip between two adjacent joints.

```json
{ "name": "KineticLinkedPleat", "properties": { "chains": [
  {
    "elements": [
      "LeftFoldLowerA", "LeftFoldLowerB",
      "LeftFoldMiddleA", "LeftFoldMiddleB"
    ],
    "plane": "xy",
    "waveform": "sine",
    "ratio": 1,
    "bottom": { "x": 1.9, "y": 5.0, "z": 8.5 },
    "top":    { "x": 3.05, "y": 9.0, "z": 8.5 },
    "topTravelY": -2.45,
    "xA": 1.9,
    "xB": 3.05,
    "startAtA": false
  },
  {
    "elements": [
      "LeftCornerCapLower", "LeftCornerCapMiddle",
      "LeftCornerCapUpper"
    ],
    "translateOnly": true,
    "translateTOffset": 0.125,
    "translateTStep": 0.25,
    "plane": "zy",
    "waveform": "sine",
    "ratio": 1,
    "bottom": { "x": 2.95, "y": 5.0, "z": 5.95 },
    "top":    { "x": 2.95, "y": 9.0, "z": 5.05 },
    "topTravelY": -2.45,
    "zA": 5.95,
    "zB": 5.05,
    "startAtA": true
  }
]}}
```

Chain fields:

- `elements`: shape element names in bottom-to-top order.
- `plane`: `xy` alternates joints along X and rotates strips around Z;
  `zy` / `yz` alternates joints along Z and rotates strips around X.
- `bottom`, `top`: anchor positions in shape JSON units, not block units.
  For bellows-like parts, read these from the model dimensions if possible
  so model edits do not leave folds floating.
- `topTravelY`: top-anchor travel in shape JSON units. Negative values move
  the top anchor downward during compression.
- `xA`, `xB` or `zA`, `zB`: the alternating joint coordinates for the chain.
- `startAtA`: controls whether joint 0 starts at A or B.
- `waveform`: `sine` or `triangle`.
- `ratio`, `phaseOffset`, `invert`: same timing semantics as
  `KineticPiston`.

Normally, each element rotates and scales along its fold axis so its far
edge reaches the next solved joint. Use `translateOnly: true` for small
filler pieces such as bellows corner caps: the element keeps its baked JSON
rotation and only follows the chain's Y motion. With translate-only chains,
`translateTOffset` and `translateTStep` let a shorter element list ride
specific positions on a longer conceptual chain. For example, four corner
caps centered on an eight-fold chain use `translateTOffset: 0.125` and
`translateTStep: 0.25`, placing them at `1/8`, `3/8`, `5/8`, and `7/8` of
the moving height.

## 11. Multiblocks (footprint larger than 1x1x1)

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

### Multiblock lessons from Treadwheel / Counterweight Drive

For future large machines, decide these four things separately before
modeling:

- **Visual shape origin:** where the model is authored in shape-space.
- **Claim footprint:** the `sizex / sizey / sizez` cells the block occupies.
- **Controller cell:** the `cposition` cell that owns the BE and placement.
- **Kinetic input cell:** the exact cell that should connect to shafts.

Rules that came out of the Treadwheel and Counterweight Drive pass:

- Directional multiblocks should use cardinal `side: [n, e, s, w]`
  variants, not broad `axis: [x, z]` variants, when the entry face,
  shaft face, exit point, mount point, or model silhouette matters.
- Use one base shape plus `shapeByType.rotateY`. Do not add per-direction
  models unless the variant is genuinely different or flipped, not merely
  rotated.
- Keep placement grounded by leaving `cposition.y` on the placement layer.
  If the shaft is one or two blocks above the controller, use
  `kineticShaftControllerOffset`, e.g. `{ "x": 0, "y": 2, "z": 0 }`.
  Moving `cposition.y` upward makes placement claim space below the clicked
  block and can cause false "not enough space" errors.
- Treat kinetic connection points as block-cell centers, not model centers.
  Choose the exact input cell first, then put the visible stub and any
  rotator `rotationOrigin` on that cell center (`[8,8,8]` plus 16 units per
  cell offset in the shape). If the visual shaft looks centered across the
  whole multiblock, it is probably wrong for the kinetic graph.
- Prefer normal per-cell `selectionbox` / `collisionbox`
  (`0..1`, `0..1`, `0..1`) with vanilla `Multiblock`. Avoid oversized
  rotated boxes plus `offsetHitboxes`; they drift by orientation and make
  hitbox debugging painful.
- Add `KineticMultiblock` to any machine where only specific cells should
  accept shaft input. Without it, every filler cell falls back to "shaft
  passthrough" behavior.
- Interactions must resolve through `MultiblockHelper.GetMultiblockAwareBE`
  so clicking filler cells works.
- Animated rotating elements need explicit `rotationOrigin`; otherwise they
  fall back to block center and orbit around the model. Visible shaft stubs
  should stop near the block edge so connected shaft meshes do not collide
  badly.
- For visuals tied to source duration, use `sourceTimed` motion:
  `KineticPiston` for moving parts like a falling weight, and
  `KineticStretch` for length-changing parts like a rope.

## 12. Restricting kinetic input to specific cells (`KineticMultiblock`)

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

## 13. Placement previews and non-standard placement

When placement depends on the clicked block, the clicked face, or the
player's yaw, implement `IPlacementPreviewProvider` on the block class.
The client preview renderer calls `TryResolvePlacementPreview` and renders
the exact variant and target position that `TryPlaceBlock` should use.

This is the pattern used by gantry carriages, flywheels, storage blocks,
and several multiblocks:

```csharp
public bool TryResolvePlacementPreview(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    out BlockPos targetPos,
    out Block variant)
{
    targetPos = null;
    variant = null;
    if (blockSel?.Position == null) return false;

    // Resolve clicked support / target position.
    // Pick the final variant with CodeWithVariant or CodeWithVariants.
    variant = world.GetBlock(CodeWithVariant("side", "n")) ?? this;
    targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
    return true;
}
```

Rules:

- `TryResolvePlacementPreview` and `TryPlaceBlock` must agree. If preview
  says the block will attach to a support block, placement must use the
  same target position and variant.
- Return `false` when the selected support is invalid. Then set a normal
  `failureCode` in `TryPlaceBlock`, e.g. `requiregantryshaft`, and add the
  matching lang key `placefailure-requiregantryshaft`.
- Use `PlacementPreview.DefaultTargetPos` for normal "place into adjacent
  cell" behavior. Use custom logic only when the block attaches to a
  specific support, such as a gantry carriage attaching beside or above a
  gantry shaft.
- For multi-variant placement, use `CodeWithVariants(new[] { ... }, new[] { ... })`
  so the block code remains data-driven instead of hard-coded string
  concatenation.

## 14. Kinetic Activator support (`IKineticActivatable`)

The Kinetic Activator normally tries three paths:

1. Target block entity implements `IKineticActivatable`.
2. Target block implements `IKineticActivatable`.
3. Fallback to `Block.Activate(...)` as a block caller.

If your block's real interaction lives in `OnBlockInteractStart`, needs a
player, opens a GUI, or should expose a cleaner automation behavior, add
`IKineticActivatable` instead of relying on the fallback.

```csharp
public class BlockMyToggle : Block, IKineticActivatable
{
    public bool OnKineticActivate(
        IWorldAccessor world,
        BlockPos targetPos,
        BlockFacing activatedFace,
        BlockPos activatorPos,
        float signedRPM)
    {
        if (world.Side != EnumAppSide.Server) return true;

        BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(world, targetPos);
        if (be is not BEMyToggle target) return false;

        target.Toggle();
        return true;
    }
}
```

Guidelines:

- Return `true` only when the activation was accepted.
- Use `MultiblockHelper.GetMultiblockAwareBE` when the target may be a
  multiblock, so activators work against filler cells as well as
  controller cells.
- `signedRPM` gives the activator's direction. Use it for directional
  logic such as "positive closes, negative opens." Ignore it for simple
  toggles.
- Keep unsafe or admin-like targets on
  `KineticActivatorTargetBlacklist` in `ModConfig/vintagekinematics.json`.
  The default blacklist blocks command/ticker/conditional block families.

## 15. Gantry contraptions

Gantry contraptions are the current constrained moving-block prototype.
They are intentionally narrower than a full Create-style contraption:

- A `gantryshaft-*` is a powered straight-line track. The whole contiguous
  line is driven by the canonical shaft at the start of the track.
- A `gantrycarriage-*` attaches to a shaft and owns the bound block
  assembly.
- The Mechanical Binder selects two corners, assigns the box to a carriage,
  and the carriage keeps only connected blocks from that selection.
- While moving, the selection becomes an `EntityVKContraption` with custom
  rendering, collision, saved block codes, and saved block-entity trees.
- When stopped, the entity restores to blocks according to the carriage's
  placement mode.

Placement modes:

- `AlwaysPlaceWhenStopped`: restore to blocks after motion stops.
- `OnlyPlaceNearInitialAngle`: future rotating-controller mode; restore
  only near the starting angle.
- `OnlyPlaceWhenAnchorDestroyed`: keep the entity assembled until the
  anchor/controller is destroyed.

Important implementation rules:

- Do not allow floating assemblies. Recompute connected blocks from the
  controller/anchor side of the selection and discard disconnected blocks.
- Save block entity trees when assembling, and restore them when placing
  blocks back into the world. Storage and machine contents otherwise vanish.
- Use claim checks for the controller, selected blocks, and restore
  positions.
- Do not restore the entity to blocks on the same tick it stops. Gantry
  shafts currently use a `250ms` auto-restore settle delay. Without that
  delay, a player standing on the moving entity can lose their support
  surface before physics catches up, causing collision jitter or slipping.
- Multiplayer may need a longer settle window if latency exposes the same
  support-surface swap. Keep the delay named and centralized.

Gantry movement should remain constrained until each step survives
singleplayer, multiplayer, save/load, claims, inventories, block entities,
lighting, and chunk-boundary tests.

### Contraption controller API

New contraption controllers should implement `IContraptionController` so
the Mechanical Binder can assign selections to them:

```csharp
public class BEMyCarriage : BlockEntity, IContraptionController
{
    public bool SetSelectionFromWorldBounds(BlockPos start, BlockPos end, IPlayer byPlayer)
    {
        // Convert world bounds into controller-relative bounds,
        // capture a snapshot, prune disconnected blocks, then store it.
        return true;
    }
}
```

Use `ContraptionApi` for the shared, easy-to-get-wrong pieces:

```csharp
Vec3i min = new Vec3i(start.X - Pos.X, start.Y - Pos.Y, start.Z - Pos.Z);
Vec3i max = new Vec3i(end.X - Pos.X, end.Y - Pos.Y, end.Z - Pos.Z);

ContraptionApi.NormalizeBounds(ref min, ref max);
ContraptionApi.IncludeOffsetInBounds(ref min, ref max, new Vec3i(0, 0, 0)); // controller

ContraptionSnapshot snapshot = ContraptionApi.CaptureSnapshot(
    Api.World,
    Pos,
    min,
    max,
    block => block.Code?.Domain == "mymod" && block.Code.FirstCodePart() == "mytrack");

int removed = ContraptionApi.PruneDisconnected(
    snapshot,
    new Vec3i(0, 0, 0), // controller seed
    new Vec3i(0, 1, 0)); // optional visible anchor seed

if (snapshot.Count == 0) return true;
```

To assemble:

```csharp
if (ContraptionApi.TrySpawnContraption(
    Api,
    Pos,
    snapshot,
    ContraptionPlacementMode.AlwaysPlaceWhenStopped,
    out EntityVKContraption entity))
{
    ContraptionApi.RemoveSnapshotBlocksFromWorld(Api.World, Pos, snapshot);
}
```

Controller responsibilities:

- Store the `ContraptionSnapshot` or equivalent arrays in tree attributes.
- Refresh the controller's own block entity tree in the snapshot before
  spawning so it restores with `linkedEntityId = 0` and `assembled = false`.
- Decide movement rules: gantry, piston, rotating bearing, cart, or another
  controller type.
- Decide anchor/track exclusions. For gantries, the shaft is excluded from
  the carried snapshot; the carriage is carried.
- Decide restore timing. For track-like controllers, keep a short settle
  delay before calling `EntityVKContraption.TryRestoreToWorld(...)`.
- Call `EntityVKContraption.MoveBy(dx, dy, dz)` for straight translation
  so carried entities move with the contraption.

## 16. What the API does for free

- Network membership and stress accounting on placement / removal.
- Tooltip lines: status, stress, idle/active source labels.
- Work-cycle accumulator gated on conflict, overstress, idle source.
- Per-frame element rotation on the client, including pivot extraction
  from the shape's `RotationOrigin` and block-rotation handling.
- Per-frame element translation on the client (oscillating or directional
  pistons), with directional positions persisted across save/load.
- Per-frame linked-pleat solving for bellows / accordion-style folds,
  including translate-only filler pieces for corner caps.
- Static body tesselation that excludes managed elements. The generic JSON
  processor and `BEKineticItemProcessorBase<TRecipe>` already call the
  splitter; custom BEs can call `KineticMeshSplitter` from `OnTesselation`.
- RPM-scaled keyframe animation speed via `KineticAnimationDriver`.
- Deduplicated looping sound; no ear-shattering stacking when many machines
  are nearby.
- Auto-pause when the network goes conflicted or overstressed; auto-resume
  on recovery.
- Multiblock kinetic-input restriction via the `KineticMultiblock`
  behavior: declare shaft cells in JSON, get variant-rotation and
  cposition math for free (see section 11).
- Placement previews for blocks that implement `IPlacementPreviewProvider`.
- Kinetic Activator automation hooks via `IKineticActivatable`, with a
  config blacklist before fallback activation.
- Contraption snapshot capture, disconnected-block pruning, entity spawn,
  world block removal, and binder assignment through `IContraptionController`.

## 17. Client-side dialogs: always use `GuiDialogUtil.SafeDispose`

If your block entity opens a `GuiDialogBlockEntity` on the client, **always**
dispose it through `VintageKinematics.Api.GuiDialogUtil.SafeDispose` from
both `OnBlockUnloaded` and `OnBlockRemoved`. Do not roll your own
close-then-dispose sequence.

```csharp
private GuiDialogMyMachine clientDialog;

public override void OnBlockUnloaded()
{
    base.OnBlockUnloaded();
    GuiDialogUtil.SafeDispose(ref clientDialog);
}

public override void OnBlockRemoved()
{
    base.OnBlockRemoved();
    GuiDialogUtil.SafeDispose(ref clientDialog);
}
```

Why this matters: `dialog.TryClose()` **synchronously** fires the
`OnClosed` event. The standard pattern of `dialog.OnClosed += () => clientDialog = null;`
means the field is nulled mid-dispose. The naïve sequence

```csharp
// DON'T DO THIS — NPEs when the player breaks the block with the GUI open
if (clientDialog.IsOpened()) clientDialog.TryClose(); // OnClosed nulls the field
clientDialog.Dispose();                               // NullReferenceException
```

crashes the client whenever a block is broken while its GUI is open.
`SafeDispose` snapshots the reference and nulls the field *before* any
callback can fire, so re-entrant nulling from `OnClosed` is harmless.

The helper takes any `GuiDialogBlockEntity` subclass by `ref`:

```csharp
public static void SafeDispose<T>(ref T dialog) where T : GuiDialogBlockEntity
```

## 18. What's still your responsibility

- For pure JSON machines: blocktype JSON, shape JSON, process recipe JSON,
  textures, and optional lang entries.
- For custom C# machines: the behavior that is genuinely custom, such as
  special recipe matching, fluids, heat, world interaction, or non-item
  outputs.
- Sound assets referenced by JSON, if you add custom sounds.

## Diagnostic commands

`/vk netinfo` lists every kinetic network with node count, source RPM,
stress, MaxRPM, and conflict state.
