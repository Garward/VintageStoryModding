# ResponsiveVS Interaction Cycle Map

Research snapshot for a possible ResponsiveVS overhaul mod. The goal is to understand where Vintage Story currently predicts, mutates, sends packets, and reconciles for inventory, crafting, and world interactions.

## Source Index

Vanilla source root:

`/home/garward/Games/Games/VintageStory/vintagestory/VanillaSource/decompiled/`

Primary files:

- `VintagestoryAPI/Vintagestory.API.Client/GuiElementItemSlotGridBase.cs`
- `VintagestoryAPI/Vintagestory.API.Common/InventoryBase.cs`
- `VintagestoryAPI/Vintagestory.API.Common/ItemSlot.cs`
- `VintagestoryLib/Vintagestory.Common/InventoryNetworkUtil.cs`
- `VintagestoryLib/Vintagestory.Common/CraftingInventoryNetworkUtil.cs`
- `VintagestoryLib/Vintagestory.Common/PlayerInventoryNetworkUtil.cs`
- `VintagestoryLib/Vintagestory.Common/InventoryCraftingGrid.cs`
- `VintagestoryLib/Vintagestory.Common/ItemSlotCraftingOutput.cs`
- `VintagestoryLib/Vintagestory.Client.NoObf/ClientMain.cs`
- `VintagestoryLib/Vintagestory.Client.NoObf/SystemMouseInWorldInteractions.cs`
- `VintagestoryLib/Vintagestory.Server/ServerSystemInventory.cs`
- `VintagestoryLib/Vintagestory.Server/ServerSystemBlockSimulation.cs`
- `VintagestoryLib/Vintagestory.Server/ServerSystemEntitySimulation.cs`
- `VintagestoryLib/Vintagestory.Server/ServerMain.cs`

Existing related one-off evidence:

- `ButterflyFix/ButterflyFix/DeltaTimeClampPatch.cs`
- `ButterflyFix/ButterflyFix/GuiFocusPatch.cs`

## Current Model

Vintage Story uses optimistic client mutation for inventory UI. The client changes its local inventory immediately, creates a vanilla packet, and sends it to the server. The server later runs similar inventory logic against authoritative state and sends slot/content updates as correction.

There is no obvious per-click transaction id or explicit server ack tied to a predicted client action. The closest existing guard is `lastChangedSinceServerStart` on inventories. Some packet paths include client-side `TargetLastChanged`, `SourceLastChanged`, or `TargetLastChanged`; the server uses those to decide whether to send full inventory contents or process the command.

This is weaker than a Minecraft-style container revision model because the client can run several local mutations ahead before receiving server updates.

## Inventory Slot Click Flow

Client entry:

`GuiElementItemSlotGridBase.SlotClick(...)`

Typical path:

1. GUI receives mouse/key input.
2. `SlotClick` builds an `ItemStackMoveOperation`.
3. If shift is held:
   - source slot is `inventory[slotId]`
   - `op.RequestedQuantity = sourceSlot.StackSize`
   - calls `inventory.ActivateSlot(slotId, sourceSlot, ref op)`
4. Otherwise:
   - source slot is mouse cursor inventory slot
   - `op.CurrentPriority = DirectMerge`
   - calls `inventory.ActivateSlot(slotId, mouseCursorInv[0], ref op)`
5. `inventory.ActivateSlot` mutates local client inventory immediately.
6. `inventory.ActivateSlot` returns a packet from `InvNetworkUtil.GetActivateSlotPacket`.
7. GUI sends the packet through `SendPacketHandler`.

Core mutation:

`InventoryBase.ActivateSlot(...)`

- creates activate packet before mutation
- shift click calls `InventoryManager.TryTransferAway`
- normal click calls `this[slotId].ActivateSlot(sourceSlot, ref op)`

Slot logic:

`ItemSlot.ActivateSlot(...)`

- left click:
  - empty target pulls from source
  - empty source takes target
  - compatible stacks merge
  - otherwise flip
- right click:
  - empty target takes one from source
  - empty source takes half from target
  - otherwise tries to place one into target
- wheel click:
  - moves one either direction based on wheel dir

Server receive:

`ServerSystemInventory.HandleActivateInventorySlot(...)`

1. Finds target inventory by id.
2. Applies item filter.
3. Calls `InventoryNetworkUtil.HandleClientPacket(...)`.
4. Broadcasts hotbar slot if visible.

Server processing:

`InventoryNetworkUtil.handleActivateInventorySlotPacket(...)`

1. Reads target inventory id, slot id, mouse button, modifiers, priority, and `TargetLastChanged`.
2. If server inventory `lastChangedSinceServerStart < TargetLastChanged`, sends full inventory contents and returns.
3. Resolves server mouse slot from `"mouse-" + playerUID`.
4. Builds server `ItemStackMoveOperation`.
5. Calls `targetInv.ActivateSlot(targetSlotId, sourceSlot, ref op)`.
6. Logs audit if mouse stack changed.

Server correction:

- Slot modifications call `ItemSlot.OnItemSlotModified`.
- That calls `InventoryBase.DidModifyItemSlot`.
- Dirty slots are later sent as inventory updates.
- Client receives:
  - `GeneralPacketHandler.HandleInventoryContents`
  - `GeneralPacketHandler.HandleInventoryUpdate`
  - `GeneralPacketHandler.HandleInventoryDoubleUpdate`
- These call `InventoryNetworkUtil.UpdateFromPacket`.

Client correction:

`InventoryNetworkUtil.UpdateFromPacket(...)`

- full contents updates replace slots
- slot updates replace one slot
- double updates replace two slots
- if `PauseInventoryUpdates` is true, single slot updates are queued until unpaused

Risk points:

- Client mutates before server validation.
- Activate-slot path only compares server `lastChanged` against client `TargetLastChanged`; it does not appear to reject when the client is merely behind unless server side has already marked the inventory newer.
- Multiple speculative client actions can be fired before correction packets arrive.
- `PauseInventoryUpdates` queues updates during drag, which can widen the prediction window.

## Click-Drag Inventory Flow

Client entry:

`GuiElementItemSlotGridBase.OnMouseDownOnElement(...)`

1. Clears drag tracking dictionaries.
2. Detects whether right/left mouse started inside the slot grid while mouse cursor stack is non-empty.
3. Adds first slot to `wasMouseDownOnSlotIndex`.
4. For left-drag, clones the reference stack and records previous slot size.
5. Calls `SlotClick(...)` immediately.
6. Sets `PauseInventoryUpdates` on the target inventory and mouse inventory if this is left-drag.

Client drag move:

`GuiElementItemSlotGridBase.OnMouseMove(...)`

- right-drag:
  - when entering a new slot, calls `SlotClick(..., Right, ...)`
- left-drag:
  - when entering a new compatible slot, records previous slot size
  - calls `SlotClick(..., Left, ...)`
  - tracks added stack sizes
  - when mouse stack reaches zero, calls `RedistributeStacks(...)`

Redistribution:

`GuiElementItemSlotGridBase.RedistributeStacks(...)`

- computes target `stacksPerSlot`
- transfers overfilled amounts between already touched slots using `InventoryManager.TryTransferTo`
- sends returned packets

Risk points:

- Drag is not preview-only in vanilla. It mutates slots as the mouse enters them.
- Every drag-entered slot can produce a packet.
- Left-drag pauses inventory updates while the user drags.
- Redistribution performs extra local transfer mutations and packets after prior speculative mutations.

ResponsiveVS candidate:

- Replace click-drag with preview-first rendering.
- Commit a bounded set of actions on mouse-up.
- Optionally send a custom transaction to server that replays vanilla mutation once with server state.

## Generic Move and Flip Flow

Move packet:

Client sources:

- `PlayerInventoryManager.TryTransferTo`
- `GuiElementItemSlotGridBase.RedistributeStacks`
- other UI helpers

Server:

`ServerSystemInventory.HandleMoveItemstack(...)`

1. Finds source inventory.
2. Calls `InventoryNetworkUtil.HandleClientPacket(...)`.
3. Broadcasts visible hand slots.

`InventoryNetworkUtil.handleMoveItemStackPacket(...)`

1. Checks `SendDirtyInventoryContents` for source and target.
2. Builds `ItemStackMoveOperation`.
3. Resolves source and target slots.
4. Calls `inv.TryMoveItemStack(...)`.
5. If success, audit only.
6. If fail, sends full source and target inventory contents.

Flip packet:

`InventoryNetworkUtil.handleFlipItemstacksPacket(...)`

1. Checks dirty state for both inventories.
2. Calls `inv.TryFlipItemStack(...)`.
3. If success, broadcasts double update to other players.
4. If fail, sends double update back to the player.

Risk points:

- Move/flip paths have stronger stale-client checks than activate-slot path because they call `SendDirtyInventoryContents`.
- Activate-slot is used for many common actions, including normal slot clicks and crafting output clicks.

## Crafting Grid Flow

Inventory:

`InventoryCraftingGrid`

Slots:

- input slots: `0..8`
- output slot: `9`
- output slot type: `ItemSlotCraftingOutput`

Input modification:

`InventoryCraftingGrid.OnItemSlotModified(...)`

- if not currently crafting and slot is not output:
  - drops hot inputs if needed
  - calls `FindMatchingRecipe`

Recipe lookup:

`InventoryCraftingGrid.FindMatchingRecipe()`

1. Clears `MatchingRecipe`.
2. Clears output slot.
3. For every occupied input slot:
   - scans `World.FastSearchRecipesByIngredient`
   - calls `SatisfiesAsIngredient(inputStack, checkStackSize: false)`
   - collects enabled `GridRecipe` candidates
4. Checks shaped recipes first with `GridRecipe.Matches`.
5. Checks shapeless recipes after.
6. On match:
   - `MatchingRecipe = recipe`
   - `recipe.GenerateOutputStack(slots, outputSlot)`
   - marks output slot dirty

Output click:

`InventoryCraftingGrid.ActivateSlot(...)`

If clicked slot is output:

1. `BeginCraft()`
2. `base.ActivateSlot(...)`
3. special shift-click output handling:
   - client clears output if still non-empty
   - server drops output stack if still non-empty
4. `EndCraft()`
5. `EndCraft()` calls `FindMatchingRecipe()`

Output slot:

`ItemSlotCraftingOutput.TryPutInto(...)`

- empty output returns zero
- requested quantity becomes output stack size
- handles leftover output state
- if shift:
  - `CraftMany`
- else:
  - `CraftSingle`

`CraftSingle(...)`

1. moves output into sink via `TryPutIntoNoEvent`
2. if full output moved, calls `inv.ConsumeIngredients(sinkSlot)`
3. marks sink/output modified

`CraftMany(...)`

Loops:

1. clones previous output
2. tries to move current output into sink
3. if only partial move, sets leftover state and breaks
4. consumes ingredients
5. if current recipe still craftable, restores previous output stack and continues

Ingredient consumption:

`InventoryCraftingGrid.ConsumeIngredients(...)`

- if recipe/output exist:
  - lets output collectible consume crafting ingredients first
  - otherwise `MatchingRecipe.ConsumeInput(...)`
  - marks all crafting grid slots dirty

Crafting network behavior:

`CraftingInventoryNetworkUtil.UpdateFromPacket(InventoryContents)`

- replaces crafting slots from full contents
- calls `FindMatchingRecipe()`

Risk points:

- Output slot logic has leftover state (`hasLeftOvers`, `prevStack`) that can get stale if client prediction diverges.
- Client and server both run recipe matching/output generation.
- Modded recipes can throw during `GenerateOutputStack`.
- Repeated output clicks can run several client-side craft cycles before server correction.
- `FindMatchingRecipe` is both performance-sensitive and correctness-sensitive.

ResponsiveVS candidate:

- Own crafting output clicks first.
- Add transaction id and input/output signatures.
- On client, render immediate predicted result but avoid allowing unlimited output click loops.
- On server, execute vanilla `InventoryCraftingGrid.ActivateSlot` and return ack/correction.
- Clear or reset leftover output state on mismatch.

## World Block Placement and Block Interaction

Client frame driver:

`SystemMouseInWorldInteractions`

Constructor registers:

- `OnGameTick` every 20 ms
- renderers for opaque/OIT/ortho
- `OnFinalizeFrame` at render stage `Done`

`OnFinalizeFrame(float dt)`

1. If not spectator:
   - if mouse grabbed or world-interact-anyway:
     - calls `UpdatePicking(dt)`
2. Stores previous mouse button states.

This means world interaction polling is render-frame driven.

Picking:

`UpdatePicking(dt)`

1. `UpdateCurrentSelection()`
2. if no hand use active:
   - no block selected: `HandleMouseInteractionsNoBlockSelected(dt)`
   - block selected: `HandleMouseInteractionsBlockSelected(dt)`

Repeat gate:

Both no-block and block-selected paths check:

`(InWorldEllapsedMs - lastbuildMilliseconds) / 1000f >= BuildRepeatDelay(game)`

`BuildRepeatDelay(game)` returns `0.25f`.

Right-click block selected:

`HandleMouseInteractionsBlockSelected`

1. triggers `InWorldRightMouseDown`
2. tries block interaction
3. tries held item interaction
4. tries block placement via `OnBlockBuild`
5. triggers placement failure messages if `failureCode` set

Block placement client:

`OnBlockBuild(...)`

1. computes placement position and `DidOffset`
2. calls `game.OnPlayerTryPlace(blockSelection, ref failureCode)`
3. on success plays sound and sets hand build animation flag

`ClientMain.OnPlayerTryPlace(...)`

1. validates world position
2. calls `tryAccess(BuildOrBreak)`
3. checks active hotbar stack is a block
4. checks lava placement rule
5. calls `itemstack.Block.TryPlaceBlock(...)`
6. on success:
   - sends `ClientPackets.BlockInteraction(blockSelection, mode: 1)`
   - triggers local block changed
   - triggers neighbor update

Server block placement:

`ServerSystemBlockSimulation.HandleBlockPlaceOrBreak(...)`

1. reconstructs `BlockSelection`
2. rejects spectator
3. tests land-claim build/break access
4. handles decor break mode
5. captures old block id
6. calls `TryModifyBlockInWorld(...)`
7. if fail, reverts block interactions around target position
8. triggers did-place/did-break events

`TryModifyBlockInWorld(...)`

- validates pick range
- validates active hotbar stack
- validates block id/class
- validates replaceability
- validates player collision
- calls `newBlock.TryPlaceBlock(server, player, hotbarSlot.Itemstack, blockSel, ref failureCode)`

Risk points:

- Client locally places before server validates.
- The path is render-frame polled but wall-clock gated.
- Higher FPS can hit the 250 ms gate closer to exact timing.
- Low FPS/tab-out can produce huge `dt` in adjacent held/block paths.
- Server correction is block-state resend around the affected position, not a transaction ack.

## Held Item and Block Use Flow

Client start:

`SystemMouseInWorldInteractions.TryBeginUseActiveSlotItem(...)`

1. calls `slot.Itemstack.Collectible.OnHeldUseStart(...)`
2. if handled:
   - sets `EntityControls.HandUse`
   - resets `UsingCount`
   - sets `UsingBeginMS`
   - sends hand interaction packet:
     - `EnumHandInteractNw.StartHeldItemUse`

Client step/stop/cancel:

`HandleHandInteraction(dt)`

- computes `secondsPassed` from `ElapsedMilliseconds - UsingBeginMS`
- for block interactions:
  - calls block `OnBlockInteractStep`
  - increments `UsingCount`
  - accumulates `stepPacketAccum += dt`
  - sends `StepBlockUse` when accumulator exceeds `0.15`
- for held item interactions:
  - calls item `OnHeldUseStep`
  - increments `UsingCount`
- on stop/cancel:
  - calls corresponding stop/cancel methods
  - sends hand interaction packet with `UsingCount`

Server packet entry:

`ServerSystemInventory.HandleHandInteraction(...)`

- if packet enum is block interaction (`EnumHandInteractNw >= 4`), forwards to `ServerSystemBlockSimulation.HandleBlockInteract`
- otherwise handles held item use

Server held item:

`ServerSystemInventory.HandleHandInteraction(...)`

1. resolves slot from hotbar/backpack/inventory id
2. rebuilds block/entity selection
3. validates range
4. stores current using selections
5. triggers mod hand-interaction event
6. for `StartHeldItemUse`:
   - calls `OnHeldUseStart`
   - sets server `HandUse`, `UsingBeginMS`, `UsingCount`
7. for `CancelHeldItemUse` / `StopHeldItemUse`:
   - loops while server `UsingCount < packet.UsingCount`
   - calls `callOnUsing(...)`
   - caps loop at 5000
   - calls cancel or stop

Server block interaction:

`ServerSystemBlockSimulation.HandleBlockInteract(...)`

1. rejects spectator/non-right-click/invalid use type
2. rebuilds block selection
3. validates range
4. checks land-claim use access
5. computes seconds passed from server elapsed time
6. for `StartBlockUse`:
   - calls `block.OnBlockInteractStart`
   - sets server `HandUse`, `UsingBeginMS`, `UsingCount`
7. for `StepBlockUse`, `StopBlockUse`, `CancelBlockUse`:
   - loops while server `UsingCount < packet.UsingCount`
   - calls `callOnUsingBlock(...)`
   - `callOnUsingBlock` increments seconds by fixed `0.02f` per step

Risk points:

- Client controls `UsingCount`; server catches up by replaying steps to that count.
- Server has a 5000 step cap for held item stop/cancel, indicating this path can receive unreasonable counts.
- Block use step replay increments `secondsPassed` by fixed 20 ms per step regardless of actual client frame timing.
- Client step packet accumulator uses `dt`, so huge tab-out `dt` or unstable focus can affect packet cadence.

ResponsiveVS candidate:

- Clamp interaction `dt` and suppress interaction updates during refocus cooldown.
- Add a maximum client `UsingCount` lead over last server-acknowledged step.
- Pace start/stop/cancel packets so duplicate transitions cannot stack in a tiny window.
- For block placement, optionally turn local placement into visual preview until server confirms.

## Entity Interaction Flow

Simple entity interaction packet:

`ServerSystemEntitySimulation.HandleEntityInteraction(...)`

1. rejects spectator.
2. finds entity near player.
3. validates attack permissions and range.
4. validates anti-abuse target range.
5. updates current entity selection.
6. triggers `EventManager.TriggerPlayerInteractEntity`.

This is separate from held item use packets that include `EntitySelection`.

ResponsiveVS candidate:

- Keep out of phase 1 unless reports clearly implicate entity interactions.
- Later add pacing/range-safe prediction for attack feedback only.

## Timing Evidence

Existing one-off `ButterflyFix` patched a related class of bugs:

- `Entity.OnGameTick(ref float dt)` clamps invalid/huge dt.
- `EntityPlayer.updateEyeHeight(ref float dt)` clamps dt.
- GUI focus patches debounce focus, suppress updates while unfocused, and enforce minimum bounds.

This supports treating uncapped FPS, tab-out, and large `dt` as a real root-cause family.

Frame-driven areas found in vanilla:

- world interaction polling from `OnFinalizeFrame(float dt)`
- held/block use step packet accumulator uses `dt`
- block breaking uses wall-clock checks in a frame-driven path
- GUI rendering/post-render consumes `deltaTime`

Inventory slot clicks do not directly use `deltaTime`, but they do mutate optimistically per input event and can fire many packets before server correction.

## Proposed ResponsiveVS Scope

This is an early research sketch. The canonical implementation schedule is now `architecture-design.md` phases 0-7.

Phase 0: diagnostics only

- log transaction candidates:
  - inventory id
  - slot id
  - mouse button
  - modifiers
  - source/mouse stack signature
  - target stack signature
  - crafting input signature
  - output signature
  - client timestamp and sequence id
- log server-side result:
  - accepted/rejected
  - touched slots
  - resulting stack signatures
  - elapsed processing time

Phase 1: crafting grid ownership

- click-drag preview first, commit on mouse-up
- output click pacing
- reset stale crafting output leftover state on mismatch
- keep server execution on vanilla `ActivateSlot`

Phase 2: generic inventory transaction layer

- intercept `GuiElementItemSlotGridBase.SlotClick`
- create ResponsiveVS transaction packet
- server validates and invokes vanilla inventory method
- server returns ack/correction
- client reconciles prediction to ack
- fallback to vanilla for denied inventory classes

Phase 3: world interaction timing guard

- clamp huge `dt`
- suppress world interaction updates during focus loss/refocus cooldown
- pace duplicate start/stop/cancel transitions
- optionally preview block placement until server confirm

Phase 4: broader compatibility

- config allowlist/denylist by inventory id/class
- compatibility mode that only paces actions
- diagnostic overlay for owned vs vanilla interactions

## Compatibility Rules

Do not reimplement modded slot, inventory, recipe, or block logic.

ResponsiveVS should prefer:

- intercepting client input before local mutation
- sending richer transaction metadata
- executing vanilla/modded methods on the server
- reconciling client display to server result

Avoid:

- custom crafting recipe matching in the interaction mod
- replacing `ItemSlot` merge rules
- replacing machine inventory logic
- suppressing all prediction globally

The safest invariant:

Client may predict visually, but server remains authoritative and the client cannot run unlimited real mutations ahead of confirmation.
