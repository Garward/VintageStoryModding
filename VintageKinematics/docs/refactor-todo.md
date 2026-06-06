# Refactor Todo

Goal: keep moving VK machines toward reusable public API primitives, with machine-specific code limited to actual unique behavior.

## 1. Filter Dialog Framework

- [x] Add a shared base/helper for filter inventories.
- [x] Move filter inventory init, save/load, client ghost-slot sync, whitelist toggle, fuzzy toggle, open-dialog packet, close packet, claim checks, and dialog disposal into the framework.
- [x] Convert `BETrashcan` to the shared filter dialog framework.
- [x] Convert `BEFunnel` to the shared filter dialog framework.
- [x] Convert `BECopperPump` to the shared filter dialog framework.
- [x] Keep pump-specific liquid-container filter validation as a hook.
- [x] Keep iron funnel pulse mode as a funnel-specific custom action hook.
- [ ] Test: trashcan filter slots save/load and whitelist/blacklist still work.
- [ ] Test: copper and iron funnel filters save/load, sync, and pulse mode still work.
- [ ] Test: pump filter only accepts valid fluid containers and still filters fluid transfer.

## 2. Side-Oriented Placement Framework

- [ ] Add a shared block base/helper for horizontal `side` variant placement.
- [ ] Support player-facing and opposite-player-facing conventions.
- [ ] Support extra fixed variant keys, such as `state=cool`.
- [ ] Support multiblock-aware right-click forwarding to controller block entities.
- [ ] Convert straightforward side-oriented blocks first: sawmill, mixer, extractor, forge press, coal motor, sieves, crusher, retort, bellows, treadwheel, trebuchet, activator, clutch, reverser, json processor.
- [ ] Test: each converted block chooses the same side variant as before.
- [ ] Test: wrench interactions still bypass normal GUI/open behavior.
- [ ] Test: placement preview matches final placement.

## 3. Centered Multiblock Placement

- [ ] Add a reusable centered-footprint placement helper.
- [ ] Support 3x3 controller offset by side variant.
- [ ] Convert kinetic bore placement.
- [ ] Convert geothermal bore placement.
- [ ] Test: placing while facing north/east/south/west centers the footprint on the clicked cell.
- [ ] Test: placement preview and actual placement match.

## 4. Openable Inventory Container Base

- [x] Add a generic openable inventory base for non-recipe machines.
- [x] Move `InventoryGeneric` lifecycle, `LateInitialize`, slot modified chunk mark, dialog title fallback, open-dialog packet, inventory packet handling, claim checks, server packet dialog creation, save/load, and dialog disposal into the base.
- [x] Add an external inventory machine base for passive inventory blocks driven by another BE/behavior.
- [ ] Convert coal motor.
- [x] Convert crusher basin.
- [ ] Convert any remaining simple openable inventory blocks that do not fit the item processor base.
- [ ] Test: inventory interaction packets obey claims.
- [ ] Test: GUIs open, close, sync, and save/load correctly.

## 5. Liquid Processor Framework

- [ ] Add reusable liquid buffer state: stack, litres, capacity, save/load, resolution after load, and tooltip/status helpers.
- [ ] Add reusable liquid container transfer helpers for adjacent containers and bucket-style interactions where applicable.
- [ ] Convert kinetic extractor onto the shared inventory/dialog base plus liquid buffer hooks.
- [ ] Convert kinetic mixer onto the shared inventory/dialog base plus liquid buffer hooks.
- [ ] Keep recipe-specific logic in each machine until a clean JSON recipe shape exists for multi-input/liquid recipes.
- [ ] Test: extractor solid outputs, liquid outputs, and downward draining still work.
- [ ] Test: mixer multi-input mapping, liquid consumption, adjacent liquid source use, and progress reset still work.

## 6. Bore Framework

- [ ] Add a shared bore base for drill depth, halted/retracting/paused state, toggle packet, dialog packet, claim checks, inventory lifecycle, save/load, and dialog disposal.
- [ ] Add reusable deployed-column tracking for placed shaft/pipe visuals.
- [ ] Convert kinetic bore.
- [ ] Convert geothermal bore.
- [ ] Keep kinetic bore mining/drop output logic as its unique hook.
- [ ] Keep geothermal bore pipe/tapped/heat-provider logic as its unique hook.
- [ ] Test: drilling, halting, retracting, pausing, resuming, save/load, and block removal cleanup for both bores.

## 7. Config Dialog Framework

- [ ] Add a small base/helper for non-inventory config dialogs.
- [ ] Move open-dialog packet, state serialization/deserialization, set/action packet routing, client dialog update, and disposal into the framework.
- [ ] Convert trebuchet.
- [ ] Convert flywheel.
- [ ] Test: settings persist and client optimistic updates reconcile with server state.

## 8. IO And Output Push Cleanup

- [ ] Prefer `MachineIoLayouts` or JSON `vkIo` for all machine IO maps.
- [ ] Remove duplicated `InputLipFace`, `LeftOf`, `RightOf`, and output-push loops where existing helpers cover them.
- [x] Convert crusher basin IO to framework layout or JSON IO.
- [ ] Convert extractor IO to framework layout or JSON IO.
- [ ] Convert mixer IO to framework layout or JSON IO.
- [ ] Audit remaining direct `InventoryPusher.TryPush` loops and replace with `MachineOutputHelper.FlushOutputs` where possible.
- [ ] Test: belts, funnels, and barrels can still insert/extract from the intended faces.

## 9. Tesselation Shell Cleanup

- [ ] Add helper/base support for `KineticMeshSplitter.CollectManagedElements` plus optional extra excluded shape elements.
- [ ] Convert coal motor.
- [ ] Convert kinetic bore.
- [ ] Convert geothermal bore.
- [ ] Test: animated and static meshes do not double-render.

## 10. Packet Id Cleanup

- [ ] Remove open-dialog packet constants from machines already using framework defaults unless a custom packet is truly needed.
- [ ] Keep machine-specific action packet ids local to the machine or framework feature that consumes them.
- [ ] Audit all packet handlers so custom ids are separated from vanilla inventory packet ids below `1000`.
- [ ] Test: all affected GUIs open and all buttons still route to the correct block entity.

## 11. API Guide Updates

- [ ] Update the API tutorial after each framework refactor lands.
- [ ] Add examples for the side placement helper.
- [ ] Add examples for JSON IO and exact multiblock cells.
- [ ] Add examples for filter dialog extension hooks if they become public API.
- [ ] Keep the JSON machine template aligned with the new API defaults.
