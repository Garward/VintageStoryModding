# Refactor Todo

Goal: keep moving VK machines toward reusable public API primitives, with machine-specific code limited to actual unique behavior.

Future platform plan: [`kinetic-storage-plan.md`](kinetic-storage-plan.md) outlines the proposed VK-native indexed warehouse/storage system, including multiblock capacity, automation, and data-loss safety requirements.

## 1. JSON Machine Platform

- [x] Add a public JSON-defined kinetic processor.
- [x] Support JSON recipe loading for generic process machines.
- [x] Support JSON-configured input/output slot counts.
- [x] Support JSON-configured machine IO faces and exact cells.
- [x] Support multiblock JSON IO cells for simple machine templates.
- [x] Support crate-style/range storage behavior for JSON processors.
- [x] Support JSON-configured GUI progress bars.
- [x] Support JSON-configured recipe browser button for generic process machines.
- [x] Support JSON-configured kinetic animation/moving parts through existing behavior primitives.
- [x] Add a creative-only JSON machine template block.
- [x] Add template recipe showing a JSON-only machine converting sticks to hand cranks.
- [x] Update API guide with the JSON machine workflow.
- [x] Keep the shipped JSON processor template aligned with the current recipe-browser/progress/IO API.
- [ ] Keep the release/template files aligned whenever new JSON API options are added.
- [ ] Add more example templates if a second common machine shape emerges.

## 2. Shared GUI And Progress Primitives

- [x] Add reusable Cairo machine progress bar.
- [x] Convert JSON processor progress display.
- [x] Convert sawmill progress display.
- [x] Convert forge press progress display.
- [x] Convert mixer progress display.
- [x] Convert crusher basin progress display.
- [x] Make the generic slot/progress GUI configurable enough for non-JSON-processor machines.
- [x] Convert crusher basin to the shared slot/progress GUI.
- [x] Convert sawmill to the shared slot/progress/recipe-browser dialog.
- [x] Make recipe-browser dropdown act as sorting instead of mode/category filtering.
- [x] Add selected-recipe/state label support to the shared recipe-browser button.
- [ ] Convert remaining simple slot-only GUIs where the shared dialog is a clean fit.
- [x] Split recipe-browser UI into a reusable component for sawmill/forge-press/json-machine-style machines.

## 3. Openable And External Inventory Framework

- [x] Add `BEOpenableInventoryMachineBase` for non-recipe/openable inventory machines.
- [x] Move inventory lifecycle, `LateInitialize`, slot modified chunk marking, title fallback, open-dialog packet flow, inventory packet handling, claim checks, server packet dialog creation, save/load, and dialog disposal into the base.
- [x] Add `BEExternalInventoryMachineBase` for passive inventory blocks driven by another BE/behavior.
- [x] Add `IExternalWorkProgressProvider`.
- [x] Convert crusher basin to the external inventory base.
- [x] Convert crusher basin IO to JSON.
- [x] Convert crusher basin GUI/progress source to JSON.
- [x] Keep crusher-specific piston/vanilla-crushing/tier/effects logic isolated in `BEBehaviorCrusherProcess`.
- [ ] Convert coal motor if it fits the openable inventory base cleanly.
- [ ] Convert any remaining simple openable inventory blocks that do not fit the item processor base.
- [ ] Test: all converted GUIs open, close, sync, and save/load correctly in-game.

## 4. Filter Dialog Framework

- [x] Add a shared base/helper for filter inventories.
- [x] Move filter inventory init, save/load, client ghost-slot sync, whitelist toggle, fuzzy toggle, open-dialog packet, close packet, claim checks, and dialog disposal into the framework.
- [x] Convert `BETrashcan` to the shared filter dialog framework.
- [x] Convert `BEFunnel` to the shared filter dialog framework.
- [x] Convert `BECopperPump` to the shared filter dialog framework.
- [x] Keep pump-specific liquid-container filter validation as a hook.
- [x] Keep iron funnel pulse mode as a funnel-specific custom action hook.
- [x] Add pump fluid filters.
- [x] Add trashcan 6-slot filters.
- [x] Add funnel activator pulse modes.
- [x] Fix filter default behavior so empty filters allow normal machine behavior while populated filters apply whitelist/blacklist/fuzzy rules.
- [ ] Test: trashcan filter slots save/load and whitelist/blacklist still work.
- [ ] Test: copper and iron funnel filters save/load, sync, and pulse mode still work.
- [ ] Test: pump filter only accepts valid fluid containers and still filters fluid transfer.

## 5. Canonical Drops And Wrench Pickup

- [x] Add shared `CanonicalDrop` block behavior.
- [x] Apply `CanonicalDrop` broadly to rotated VK blocks.
- [x] Fix blocks with `behaviorsByType` so generated variants also receive `CanonicalDrop`.
- [x] Add `CanonicalDropJsonTests` to catch missing per-type canonical drop coverage.
- [x] Make wrench shift-right-click pickup work on normal VK blocks, not only blocks with kinetic behavior.
- [x] Keep belts excluded from generic wrench pickup so belt-chain behavior is preserved.
- [x] Keep technical deployed bore shafts excluded from generic wrench pickup.
- [ ] In-game audit: report/fix any remaining VK block that drops as a rotated variant.
- [ ] In-game audit: report/fix any remaining normal VK block that cannot be picked up with shift-wrench.

## 6. Automation, Claims, And IO

- [x] Barrels accept recipe inputs from belts/funnels.
- [x] Barrels output to funnels.
- [x] Activators can trigger barrel seal.
- [x] Funnel tops count as solid support blocks for physics.
- [x] Funnel canonical drops fixed.
- [x] Automation and inventory packets respect claims for converted systems.
- [x] Contraptions stop/restore when hitting protected claims.
- [x] Crusher basin outputs use shared output/deposit helpers.
- [x] Prefer JSON IO for crusher basin.
- [x] Prefer `MachineIoLayouts` or JSON `vkIo` for simple remaining machine IO maps.
- [x] Remove duplicated `InputLipFace`, `LeftOf`, `RightOf`, and output-push loops where existing helpers cover them.
- [x] Convert extractor IO to framework layout or JSON IO.
- [x] Convert mixer IO to framework layout or JSON IO.
- [x] Audit remaining direct `InventoryPusher.TryPush` loops and replace output-buffer flushes with `MachineOutputHelper.FlushOutputs`.
- [ ] Review complex/custom IO maps, such as forge press and bores, for later framework hooks.
- [ ] Test: belts, funnels, and barrels can still insert/extract from the intended faces.

## 7. Item Processor Base Refactors

- [x] Convert sawmill onto `BEKineticItemProcessorBase`.
- [x] Remove the dedicated sawmill GUI and use the shared processor dialog.
- [x] Convert both sieves onto shared sieve/item processor base.
- [x] Convert charcoal retort onto shared item processor primitives where practical.
- [x] Add reusable crate-style interaction/range inventory behavior.
- [x] Move most charcoal retort IO into JSON.
- [x] Keep charcoal retort bellows/firewood/charcoal-specific logic as custom hooks.
- [x] Convert forge press progress/tooltip work tracking onto shared primitives where practical.
- [ ] Move more forge press boilerplate into framework where it does not obscure unique die/heat/operation behavior.
- [ ] Convert generator boilerplate where practical while keeping generation requirements custom.
- [ ] Continue converting simple 1-input/1-output machines toward the public API primitives.

## 7A. Remaining Machine Framework Upgrades

These are not done until the block class, placement/open behavior, IO mapping, packet/dialog boilerplate, and tests are all using the shared framework where practical. Machine-specific code should be limited to the actual unique mechanic.

- [ ] Kinetic bore
  - [x] Move placement/open behavior to reusable centered-multiblock/framework helpers.
  - [x] Move inventory lifecycle, dialog packets, save/load, and claim checks into a shared bore or machine base.
  - [x] Move output-face/cell routing into JSON IO or a reusable exact-cell multiblock IO helper.
  - [x] Keep mining/drop/descent/retract behavior as kinetic-bore-specific hooks.
  - [ ] Test placement in all four directions, drilling, pause/halt/retract, output automation, save/load, and cleanup on break.
- [ ] Geothermal bore
  - [x] Share the bore placement, inventory/dialog, save/load, and claim framework with kinetic bore.
  - [x] Move placement offset out of bespoke block code.
  - [x] Move deployed-column tracking into a reusable bore helper.
  - [x] Move any routable face logic out of bespoke block code.
  - [x] Keep pipe/tapped/heat-provider behavior as geothermal-bore-specific hooks.
  - [ ] Test placement in all four directions, pipe deployment/retraction, heat output, save/load, and cleanup on break.
- [ ] Primitive and kinetic sieves
  - [x] Make both block classes inherit/use `BlockKineticOpenableMachine` or a shared side-oriented openable subclass.
  - [x] Move side placement and right-click opening out of `BlockPrimitiveSieve` and `BlockKineticSieve`.
  - [x] Move remaining sieve IO maps to JSON IO or a reusable multiblock side/top-input and side/down-output layout.
  - [x] Keep panning yield/effects and kinetic-vs-primitive work behavior as sieve-specific hooks.
  - [ ] Test placement direction, GUI open, input/output automation, panning outputs, particles, progress sync, and save/load.
- [ ] Kinetic mixer
  - [x] Change the custom block class to inherit `BlockKineticOpenableMachine` while retaining `ILiquidSink`.
  - [x] Move side placement and right-click opening out of `BlockKineticMixer`.
  - [x] Move solid input/output IO to JSON `vkIo` or a reusable layout.
  - [ ] Move liquid buffer, adjacent container transfer, pipe sink behavior, and progress/dialog sync into reusable liquid machine hooks where practical.
  - [ ] Keep multi-input/liquid recipe matching as mixer-specific until the JSON recipe shape supports it cleanly.
  - [ ] Test multi-input recipes, liquid insertion from pipes/containers, output automation, recipe browser, progress, and save/load.
- [ ] Kinetic forge press
  - [x] Change the custom block class to inherit/use `BlockKineticOpenableMachine`.
  - [x] Move side placement and right-click opening out of `BlockKineticForgePress`.
  - [x] Move forge press multiblock input/fuel/output face mapping to JSON IO or reusable exact-cell multiblock IO hooks.
  - [x] Move remaining open-dialog/packet/inventory boilerplate into shared processor/dialog primitives where practical.
  - [x] Keep die selection, heat, fuel, refractory lining, bellows heat, and forge-specific recipe behavior as custom hooks.
  - [ ] Test placement, fuel input, die input, hot input, output automation, bellows heat, lining upgrade, selected recipe persistence, progress, and save/load.
- [ ] Crusher head
  - [x] Move side placement out of `BlockCrusher` into the shared side-placement helper.
  - [x] Keep piston animation, basin dependency, crushing behavior, and tier/effect behavior isolated in crusher-specific behavior.
  - [x] Confirm the crusher head does not need openable-machine inheritance because crusher basin owns inventory/UI.
  - [ ] Test placement direction, piston animation, basin crafting, basin output automation, claim checks, and save/load.

## 8. Sawmill And Recipe Browser

- [x] Overhaul sawmill UI to forge-press-style recipe browser.
- [x] Collapse wildcard board/log recipes so variants do not spam the recipe list.
- [x] Fix sawmill button hitboxes/icon alignment.
- [x] Add sawmill progress bar.
- [x] Fix sawmill work tooltip/progress noise.
- [x] Fix sawmill sound path.
- [x] Make sawmill recipe browser use the shared dialog abstraction.
- [x] Show currently selected sawmill output type on the recipe button.
- [x] Move sawmill IO to JSON `vkIo` using standard local west/top input and local east/bottom output.
- [x] Replace misleading `inputLip*` internal defaults with `local*` face aliases while keeping compatibility.
- [ ] Consider recipe-category/search helpers if sawmill recipe count grows much larger.

## 9. Side-Oriented Placement Framework

- [x] Add `BlockKineticOpenableMachine` for generic side placement plus openable right-click forwarding.
- [x] Use generic openable block class for JSON machine template.
- [x] Use generic openable block class for crusher basin.
- [x] Use generic openable block class for sawmill.
- [x] Support multiblock-aware right-click forwarding to controller block entities in the generic openable block.
- [x] Fix shared openable N/S player-facing placement convention for template-style rotations.
- [x] Add a shared side-placement helper/base for non-openable side-oriented blocks.
- [x] Support both player-facing and opposite-player-facing conventions in one helper.
- [x] Support extra fixed variant keys, such as `state=cool`.
- [ ] Convert straightforward side-oriented blocks still using custom placement code: mixer, extractor, forge press, coal motor, sieves, crusher, retort, bellows, treadwheel, trebuchet, activator, clutch, reverser.
- [ ] Test: each converted block chooses the same side variant as before.
- [ ] Test: wrench interactions still bypass normal GUI/open behavior.
- [ ] Test: placement preview matches final placement.

## 9A. Compact Kinetic Behavior JSON

Goal: reduce repeated per-rotation `entityBehaviorsByType` blocks by moving shared values into block `attributes`, such as `vkKinetic`, `vkKineticSource`, `vkKineticWorker`, `vkKineticAnimator`, `vkKineticSound`, `vkKineticPiston`, and `vkKineticStretch`. Keep `entityBehaviorsByType` only where a behavior is truly different per variant.

Conversion rules:

- [ ] Prefer one `entityBehaviors` list with behavior names and shared defaults in `attributes`.
- [ ] Use `axisFromSide` for normal side-variant shaft axes instead of repeating `axis` per side.
- [ ] Use `axisFromSideMode: "perpendicular"` for blocks whose shaft axis is crosswise to their side variant, such as trebuchet.
- [ ] Keep variant-specific animator/piston/stretch settings only when the element or axis really differs.
- [ ] Do not add vanilla `HorizontalOrientable` to VK short `side` variants (`n/e/s/w`); use VK placement helpers instead.
- [ ] After each batch, test placement in all four directions, kinetic connection axis, animation direction, canonical drop, and wrench rotation/pickup where relevant.

Done:

- [x] Kinetic sawmill.
- [x] Flywheel.

Batch 1:

- [x] Reinforced flywheel.
- [x] Kinetic mixer.
- [x] Kinetic extractor.
- [x] Kinetic forge press.
- [x] Kinetic charcoal retort.

Batch 2:

- [x] Kinetic sieve.
- [x] Primitive sieve.
- [x] Kinetic bore.
- [x] Geothermal bore.
- [x] Trebuchet.

Batch 3:

- [x] Coal motor.
- [x] Treadwheel.
- [x] Counterweight drive.
- [x] Geothermal steam engine.
- [x] Creative motor.

Batch 4:

- [x] Kinetic bellows.
- [x] Kinetic igniter.
- [x] Kinetic activator.
- [x] Kinetic clutch.
- [x] Kinetic reverser.

Batch 5:

- [x] Gearbox.
- [x] Plate piston.
- [x] Crusher head.
- [x] Copper pump.
- [x] JSON machine template.

Batch 6:

- [x] Kinetic sensor.
- [x] Backpack flywheel placed.
- [x] Re-audit `assets/vintagekinematics/blocktypes` for any remaining `entityBehaviorsByType` repetition.
- [ ] Update the API tutorial/modeling guide after the compact pattern is stable across multiple block shapes.
- [ ] Add or update tests that catch long-name side variants such as `*-south` on short-code VK blocks.

## 10. Centered Multiblock Placement

- [x] Add a reusable centered-footprint placement helper.
- [x] Support 3x3 controller offset by side variant.
- [x] Convert kinetic bore placement.
- [x] Convert geothermal bore placement.
- [ ] Test: placing while facing north/east/south/west centers the footprint on the clicked cell.
- [ ] Test: placement preview and actual placement match.

## 11. Liquid Processor Framework

- [ ] Add reusable liquid buffer state: stack, litres, capacity, save/load, resolution after load, and tooltip/status helpers.
- [ ] Add reusable liquid container transfer helpers for adjacent containers and bucket-style interactions where applicable.
- [ ] Convert kinetic extractor onto the shared inventory/dialog base plus liquid buffer hooks.
- [ ] Convert kinetic mixer onto the shared inventory/dialog base plus liquid buffer hooks.
- [ ] Keep recipe-specific logic in each machine until a clean JSON recipe shape exists for multi-input/liquid recipes.
- [ ] Test: extractor solid outputs, liquid outputs, and downward draining still work.
- [ ] Test: mixer multi-input mapping, liquid consumption, adjacent liquid source use, and progress reset still work.

## 12. Bore Framework

- [x] Add a shared bore base for drill depth, halted/retracting/paused state, toggle packet, dialog packet, claim checks, inventory lifecycle, save/load, and dialog disposal.
- [x] Add reusable deployed-column tracking for placed shaft/pipe visuals.
- [x] Convert kinetic bore to the shared bore base.
- [x] Convert geothermal bore to the shared bore base.
- [x] Keep kinetic bore mining/drop output logic as its unique hook.
- [x] Keep geothermal bore pipe/tapped/heat-provider logic as its unique hook.
- [ ] Test: drilling, halting, retracting, pausing, resuming, save/load, and block removal cleanup for both bores.

## 13. Config Dialog Framework

- [ ] Add a small base/helper for non-inventory config dialogs.
- [ ] Move open-dialog packet, state serialization/deserialization, set/action packet routing, client dialog update, and disposal into the framework.
- [ ] Convert trebuchet.
- [ ] Convert flywheel.
- [ ] Test: settings persist and client optimistic updates reconcile with server state.

## 14. Tesselation Shell Cleanup

- [ ] Add helper/base support for `KineticMeshSplitter.CollectManagedElements` plus optional extra excluded shape elements.
- [ ] Convert coal motor.
- [ ] Convert kinetic bore.
- [ ] Convert geothermal bore.
- [ ] Test: animated and static meshes do not double-render.

## 15. Packet Id Cleanup

- [ ] Remove open-dialog packet constants from machines already using framework defaults unless a custom packet is truly needed.
- [ ] Keep machine-specific action packet ids local to the machine or framework feature that consumes them.
- [ ] Audit all packet handlers so custom ids are separated from vanilla inventory packet ids below `1000`.
- [ ] Test: all affected GUIs open and all buttons still route to the correct block entity.

## 16. API Guide Updates

- [x] Document JSON machine template workflow.
- [x] Document JSON IO and exact multiblock cells.
- [x] Document progress bar JSON.
- [x] Document JSON recipe browser option.
- [x] Document `localNorth/East/South/West` model-local IO aliases.
- [x] Document crate-style/range storage behavior.
- [x] Document external inventory machine base and progress-provider pattern.
- [ ] Add examples for the side placement helper once it exists.
- [ ] Add examples for filter dialog extension hooks if they become public API.
- [x] Keep the JSON machine template aligned with the new API defaults.

## 17. Contraption Rideability

- [ ] Improve free-standing passenger carry so players on moving contraption floors are carried immediately, including near horizontal edges.
- [ ] Revisit contraption restore timing so moving entities are positioned before restored blocks can clip or drop them.
- [ ] Add explicit support/contact tracking for entities that were on a contraption during the previous tick, not only entities that still overlap the top surface cleanly this tick.
- [ ] Test vertical gantry elevators without artificial delay between entity movement and block restore.
- [ ] Test horizontal platforms at center, edges, and corners while walking, sneaking, and standing still.
- [ ] Investigate `IMountable`/`IMountableSeat` support on `EntityVKContraption` for guaranteed-safe contraption seats.
- [ ] Add a simple contraption seat block or captured seat marker that becomes a seat on the assembled contraption.
- [ ] Serialize contraption seat ids/local offsets into the contraption snapshot so mounted players reconnect to the moving entity after save/load.
- [ ] Ensure mounted passengers are unmounted or safely moved before contraptions restore back into world blocks.
- [ ] Document free-standing platforms as best-effort and seats as the safe riding method once seat support exists.
