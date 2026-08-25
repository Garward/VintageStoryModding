# Client Mutation Coverage Map

Status: source audit for future ResponsiveVS ownership work.

Source snapshot: Vintage Story 1.22.3 decompiled/source tree under `VanillaSource/`.

Goal: list every client-side item mutation surface found in vanilla that can create optimistic local state before the server confirms it. ResponsiveVS should eventually either own these paths, explicitly fall back to vanilla, or prove they are authoritative server updates and leave them alone.

Current ResponsiveVS coverage is intentionally narrow: simple GUI slot clicks routed through `GuiElementItemSlotGridBase.SlotClick` can be converted into preview-only behavior. Everything below that is marked as still needing coverage unless noted.

## Source Search Commands

Useful source searches:

```bash
rg -n "ActivateSlot\(|TryTransferTo\(|TryTransferAway\(|TryMoveItemStack\(|TryFlipItemStack\(|TryFlipItems\(|DropMouseSlotItems|DropHotbarSlotItems|DropItem\(|MouseItemSlot|GetActivateSlotPacket|GetFlipSlotsPacket" VanillaSource -g '*.cs'
rg -n "OnHeldUseStart|OnHeldUseStep|OnHeldUseStop|OnBlockInteractStart|OnBlockInteractStep|OnBlockInteractStop|TakeOut\(|TryPutInto\(|Itemstack\s*=|StackSize\s*=" VanillaSource/vssurvivalmod -g '*.cs'
```

These commands are broad on purpose. Many item/block callbacks mutate slots directly and have to be judged by source context.

## Authoritative Update Path

These are real slot mutations, but they are not the bad prediction path. They are the server correction path and must remain authoritative.

| Source | Mutation | ResponsiveVS rule |
| --- | --- | --- |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryNetworkUtil.cs:432` | `UpdateSlotStack()` assigns `slot.Itemstack = newStack` from incoming inventory packets. | Keep authoritative. Clear any matching preview/pending state when this arrives. |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryNetworkUtil.cs:24` | `PlayerInventoryNetworkUtil.UpdateFromPacket()` updates own player inventories. | Keep authoritative. This must win over preview state. |
| `decompiled/VintagestoryLib/Vintagestory.Common/CraftingInventoryNetworkUtil.cs` | Full crafting-grid content updates. | Keep authoritative. Must clear craft preview/output leftovers. |

## Core Inventory Mutation Primitives

These are the low-level mutation methods most GUI paths eventually call. They should be treated as unsafe on the client unless ResponsiveVS is running them against cloned slots for preview.

| Source | Mutation | Callers / risk | Coverage plan |
| --- | --- | --- | --- |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryBase.cs:444` | `ActivateSlot()` builds a packet first, then mutates locally through shift transfer or `ItemSlot.ActivateSlot()`. | Main slot-click path. Packet has no transaction id and the local mutation can run ahead. | Covered only when routed through current `SlotClick` patch. Needs a lower guard for direct mod calls. |
| `decompiled/VintagestoryLib/Vintagestory.Common/ItemSlot.cs:285` | `ItemSlot.ActivateSlot()` dispatches left/right/middle/wheel behavior. | Mutates mouse/target stacks with `TakeOut`, `TryPutInto`, merge, and flip semantics. | Use real vanilla code only on cloned slots for preview or on server execution. Do not reimplement by hand. |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryBase.cs:403` | `TryFlipItems()` mutates via `targetSlot.TryFlipWith(itemSlot)` before returning a flip packet. | Number-key swaps, offhand swaps, hotbar/hovered-slot swaps. | Add preview-only flip ownership or hard vanilla fallback per gesture. |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryBase.cs:531` | `TryFlipItemStack()` handles server/common flip packet execution. | Mostly server packet handling, but shared API shape matters for mods. | Server-owned execution only; client calls should be intercepted before mutation. |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryBase.cs:552` | `TryMoveItemStack()` mutates `slots[0].TryPutInto(slots[1], ref op)`. | Common move helper used by inventory/network/crafting flows. | Treat as unsafe on client unless running inside an explicit preview clone transaction. |
| `decompiled/VintagestoryLib/Vintagestory.Common/PlayerInventoryManager.cs:190` | `TryTransferAway()` shift-transfers out of a source slot and mutates many sink slots while building packets. | Shift-click, creative transfer, mod helpers, auto-fill style flows. | Needs owned shift-transfer transaction with full changed-slot delta capture. |
| `decompiled/VintagestoryLib/Vintagestory.Common/PlayerInventoryManager.cs:386` | `TryTransferTo()` mutates source/target with `sourceSlot.TryPutInto(targetSlot, ref op)`. | Drag redistribution and some mod UI helpers. | Needs clone-preview plus server transaction, or vanilla fallback when inventory is not ownable. |
| `decompiled/VintagestoryLib/Vintagestory.Client.NoObf/ClientPlayerInventoryManager.cs:75` | `DropItem()` calls `OnHeldDropped()`, may stop held-use, then `slot.TryPutInto(ground[0], ref op)` before sending move packet. | Q/drop key and mouse-slot drops. Can mutate hotbar/mouse before server ack. | Needs owned drop transaction or rollback-on-server-update guard. |

## GUI Slot Grid Paths

These are the direct UI interaction entry points. They should decide at gesture start whether ResponsiveVS owns the action or leaves the whole gesture vanilla.

| Source | Mutation | Risk | Coverage plan |
| --- | --- | --- | --- |
| `vsapi/Client/UI/Elements/Impl/Interactive/Inventory/GuiElementItemSlotGridBase.cs:702` | `SlotMouseWheel()` calls `targetInv.ActivateSlot()` and sends the packet. | Mouse-wheel stack transfer is a separate path from click. | Add wheel ownership or force vanilla fallback. |
| `GuiElementItemSlotGridBase.cs:781` | `OnMouseDownOnElement()` starts drag state from the real `MouseItemSlot`, calls `SlotClick()`, and pauses inventory updates for drag. | The initial drag click is now partially covered by `SlotClick`, but drag state still uses real slot state. | Replace with gesture-level preview state. Do not rely on real mouse slot while pending. |
| `GuiElementItemSlotGridBase.cs:857` | `OnMouseMove()` calls `SlotClick()` repeatedly while dragging. | Vanilla mutates every crossed slot. Current per-click preview helps but does not own drag as one operation. | Build a drag gesture owner that renders distribution preview and commits once on mouse-up. |
| `GuiElementItemSlotGridBase.cs:954` | `RedistributeStacks()` calls `PlayerInventoryManager.TryTransferTo()` while left-dragging. | Direct local transfer helper outside plain `SlotClick`. This is the source of many split/drag prediction problems. | Reimplement only the drag distribution math as a sanctioned exception, then execute server-side. |
| `GuiElementItemSlotGridBase.cs:988` | Shift `SlotClick()` calls `inventory.ActivateSlot(slotId, sourceSlot, ref op)`. | Shift transfer can mutate many inventories before correction. | Simple shift-click is currently intercepted only at the GUI level. Need lower direct-call guard. |
| `GuiElementItemSlotGridBase.cs:996` | Normal `SlotClick()` calls `inventory.ActivateSlot(slotId, mouseCursorInv[0], ref op)`. | Main optimistic local mutation path. | Current preview-only click pass targets this path. |

## Hotbar, Offhand, And Number-Key Paths

These bypass normal mouse-click movement and mutate by flipping slots.

| Source | Mutation | Risk | Coverage plan |
| --- | --- | --- | --- |
| `decompiled/VintagestoryLib/Vintagestory.Client.NoObf/HudHotbar.cs:366` | `KeyFlipHandSlots()` calls `hotbarInv.TryFlipItems(activeHotbar, leftHandItemSlot)`. | Offhand/hotbar flip mutates locally before packet. | Add flip transaction or disable ownership for that key path until supported. |
| `HudHotbar.cs:389` | Number-key move while hovering a slot calls `hotbarInv.TryFlipItems(index, CurrentHoveredSlot)`. | Classic number-key swap route. Anecdotes mention number keys around crafting. | High priority after drag and wheel. |
| `decompiled/VintagestoryLib/Vintagestory.Client.NoObf/SystemMouseInWorldInteractions.cs:545` | Pick-block or similar hotbar selection calls `hotbarInv.TryFlipItems(activeHotbarSlotNumber, flipSlot)`. | World interaction can mutate hotbar via flip outside inventory GUI. | Needs world-interaction/hotbar guard. |
| `SystemMouseInWorldInteractions.cs:532` | Creative pick-block assigns `selectedHotbarSlot.Itemstack = blockStack` and sends create packet. | Creative local create is explicitly client-predicted. | Leave vanilla for creative unless ResponsiveVS later owns creative mode separately. |

## Crafting Grid And Crafting Output

Crafting is the highest-risk inventory subtype because output slots are generated state, not just stored state. These paths should not be solved by slot-click preview alone.

| Source | Mutation | Risk | Coverage plan |
| --- | --- | --- | --- |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryCraftingGrid.cs:97` | Output-slot `ActivateSlot()` wraps `BeginCraft()`, `base.ActivateSlot()`, then `EndCraft()`. | Output click can consume ingredients and rebuild output. Any exception or missing delta causes ghost/fake output. | Needs craft-output transaction with forced snapshot on error. |
| `InventoryCraftingGrid.cs:123` | Client shift-output branch sets `outputSlot.Itemstack = null` when leftovers remain. | Explicit client-only output mutation. | Must be covered by craft-output ownership; do not let client clear real output speculatively. |
| `InventoryCraftingGrid.cs:134` | Non-output grid click calls `base.ActivateSlot()` then refreshes recipe. | Normal grid slot movement plus output recompute. | Current simple click preview covers the click only when routed through GUI; recipe/output recompute still needs care. |
| `InventoryCraftingGrid.cs:142` | `OnItemSlotModified()` calls `FindMatchingRecipe()`. | Any grid mutation triggers recipe matching and output mutation. | FastCrafting helps performance; ResponsiveVS still needs correctness ownership. |
| `InventoryCraftingGrid.cs:153` | `TryMoveItemStack()` calls base move, then `FindMatchingRecipe()`. | Direct move into/out of grid can recompute output. | Needs lower helper guard and output snapshot. |
| `InventoryCraftingGrid.cs:166` | `FindMatchingRecipe()` clears `MatchingRecipe` and `outputSlot.Itemstack`, then rebuilds output. | Output slot can vanish/reappear locally based on stale predicted inputs. | Preview should show output as preview, not mutate real output before server state. |
| `InventoryCraftingGrid.cs:247` | `ConsumeIngredients()` mutates input slots after crafting output is taken. | Side-effectful ingredient consumption; also calls collectible crafting hooks. | Server-owned execution; client gets resulting deltas/snapshot. |
| `decompiled/VintagestoryLib/Vintagestory.Common/ItemSlotCraftingOutput.cs:26` | `TryPutInto()` moves crafted output to sink, handles leftovers, may call `CraftMany()`. | Shift-craft-many can touch output, inputs, and destination inventory repeatedly. | Needs complete changed-slot capture, not just target/source slot deltas. |
| `ItemSlotCraftingOutput.cs:76` | `CraftMany()` loops until cannot craft, mutating sink and input slots. | Large burst of mutations from one click. | Require forced snapshot on exception and debug validation against vanilla. |
| `ItemSlotCraftingOutput.cs:122` | `CraftSingle()` consumes ingredients after successful move. | Single craft still has multi-slot side effects. | Same craft transaction path as above. |

## Special Inventory And Slot Overrides

These override normal slot behavior. Preview code that uses plain `DummySlot` can diverge from these classes unless it runs actual cloned slot types or falls back.

| Source | Mutation | Risk | Coverage plan |
| --- | --- | --- | --- |
| `vssurvivalmod/Systems/Trading/InventoryTrader.cs:105` | Trader selling-list click mutates buying cart through `AddToBuyingCart()`. | Does not behave like normal slot movement. | Fallback by default until trader transaction is modeled. |
| `InventoryTrader.cs:115` | Buying-cart click removes stack by `TakeOut()` or `cartSlot.Itemstack = null`. | Local cart state can diverge from server. | Fallback or dedicated trader-cart preview. |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryPlayerCreative.cs:103` | Creative inventory override adds tab index and uses base activation. | Creative stack creation/blackhole semantics differ from survival. | Leave vanilla unless explicitly enabled. |
| `decompiled/VintagestoryLib/Vintagestory.Common/InventoryPlayerBackpacks.cs:149` | Backpack inventory calls base activation, then reloads bag inventory if a bag slot became non-empty. | Side effect reaches container inventory layout. | Own only after bag reload state is included in snapshots. |
| `vssurvivalmod/Systems/Trading/ItemSlotTrade.cs` | Trade slots reject normal taking/holding/flipping and override activation. | Plain-slot preview semantics are wrong. | Dedicated fallback. |
| `vssurvivalmod/Inventory/ItemSlotBarrelInput.cs:18` | Barrel input slot normal activation is followed by `OnItemSlotModified()` that may move stack into liquid slot. | One slot click can mutate another slot. | Needs cloned specialized slots or fallback. |
| `vssurvivalmod/Inventory/ItemSlotLiquidOnly.cs:31` | Liquid-only slot pulls its full stack into source if source can hold liquid. | Nonstandard source/target semantics. | Needs real slot subclass preview or fallback. |
| `decompiled/VintagestoryLib/Vintagestory.Common/ItemSlotOutput.cs` and subclasses | Output slots often reject normal input but allow special transfer out. | Common for machines and crafting. | Treat output slots as ownable only with inventory-specific policy. |

## Drops And Cursor Cleanup

| Source | Mutation | Risk | Coverage plan |
| --- | --- | --- | --- |
| `decompiled/VintagestoryLib/Vintagestory.Client.NoObf/HudDropItem.cs:43` | Drop HUD calls `DropMouseSlotItems(args.Button == Left)`. | Mouse cursor stack can mutate locally before server ack. | Add owned drop transaction or explicit vanilla route. |
| `decompiled/VintagestoryLib/Vintagestory.Client.NoObf/SystemHotkeys.cs:141` | Hotkey drops one item from active slot. | Direct hotbar drop. | Same drop transaction path. |
| `SystemHotkeys.cs:155` | Hotkey drops full stack from active slot. | Direct hotbar drop. | Same drop transaction path. |
| `decompiled/VintagestoryLib/Vintagestory.Client.NoObf/ClientMain.cs:563` | Disconnect cleanup drops mouse-slot items. | This is cleanup, not normal gameplay prediction, but it mutates cursor state. | Clear previews/pending state on disconnect before vanilla cleanup. |

## World Interaction Callback Mutations

The inventory UI patch does not cover held-use, block-use, or entity-use callbacks. `SystemMouseInWorldInteractions` runs client-side interaction callbacks for responsiveness, and many survival blocks/items directly mutate hotbar or mouse slots in those callbacks.

Important sources:

| Source | Mutation | Risk | Coverage plan |
| --- | --- | --- | --- |
| `decompiled/VintagestoryLib/Vintagestory.Client.NoObf/SystemMouseInWorldInteractions.cs` | Calls held/block/entity interaction callbacks from client input. | Callback code can mutate real hotbar/mouse state before server confirms. FPS/delta-time instability can change how many callbacks run. | Add world-interaction mutation guard after inventory GUI paths are stable. |
| `vssurvivalmod/Block/BlockFirepit.cs` | Uses active hotbar `TryPutInto()` and `TakeOut()` during interaction. | Common block interaction with item consumption/placement. | Snapshot active hotbar/mouse before callback; server result must reconcile. |
| `vssurvivalmod/Block/BlockBloomery.cs` | Takes from active hotbar on interaction. | Item consumption outside inventory UI. | World-interaction guard. |
| `vssurvivalmod/Block/BlockTorchHolder.cs` and `BlockChandelier.cs` | Take held items when placing/removing. | Client can consume held item optimistically. | World-interaction guard. |
| `vssurvivalmod/Item/ItemChisel.cs` | Takes from mouse slot in some interaction flows. | Cursor state can diverge outside GUI. | Include mouse cursor in world-interaction snapshot. |
| `vssurvivalmod/Item/ItemPlantableSeed.cs`, `ItemSnowball.cs`, `ItemRope.cs` | Use `TakeOut()`/stack-size changes during held use. | Held item consumption can run ahead. | World-interaction guard. |
| `vssurvivalmod/Block/BlockCrock.cs` and food/container blocks | Move item stacks between active slot/container state. | Often involves nonstandard container/liquid/item-stack attributes. | Fallback first, then opt-in by proven block class. |

This category should not be solved with a single broad Harmony patch that blindly reverts every `TakeOut()`. Some callbacks also create legitimate local visual state. The safer design is:

- snapshot active hotbar slot, mouse slot, and touched open inventory slots before the callback
- allow vanilla callback to decide whether the action is valid
- if the action is client-predicted, render preview and prevent permanent real mutation until server acknowledgement
- on server correction, apply authoritative state and discard preview
- when in doubt, fallback to vanilla and log the mutation surface

## Direct Mod Calls And Auto-Fill Paths

Not every mutation comes from vanilla GUI code. Mods can call these public APIs directly:

- `IInventory.ActivateSlot()`
- `PlayerInventoryManager.TryTransferAway()`
- `PlayerInventoryManager.TryTransferTo()`
- `InventoryBase.TryFlipItems()`
- `ItemSlot.TryPutInto()`
- `ItemSlot.TakeOut()`
- `ItemSlot.Itemstack = ...`

ResponsiveVS cannot safely own arbitrary direct slot writes without becoming a full inventory VM. The practical target is:

1. Guard the public vanilla helpers that build/send vanilla packets (`ActivateSlot`, `TryTransferAway`, `TryTransferTo`, `TryFlipItems`, drop paths).
2. For direct `ItemSlot` writes inside mod code, use diagnostics first and only add targeted patches for proven hot/problem callsites.
3. If a mod bypasses packet helpers while an RVS transaction is pending on the same slot, discard preview and request an authoritative snapshot.

## Priority Coverage Plan

1. **P0: Simple slot click** - normal left/right/shift clicks through `GuiElementItemSlotGridBase.SlotClick`. Initial preview-only pass exists.
2. **P1: Wheel and drop actions** - `SlotMouseWheel`, `DropMouseSlotItems`, `DropHotbarSlotItems`, `ClientPlayerInventoryManager.DropItem`.
3. **P2: Drag gesture ownership** - own `OnMouseDown`, `OnMouseMove`, `OnMouseUp`, and `RedistributeStacks` as one gesture. Preview distribution; mutate only on commit/server ack.
4. **P3: Number-key/offhand flips** - `InventoryBase.TryFlipItems` callers in `HudHotbar` and `SystemMouseInWorldInteractions`.
5. **P4: Shift transfer and direct transfer helpers** - `TryTransferAway` and `TryTransferTo` need clone-preview plus complete changed-slot capture.
6. **P5: Crafting output** - output slot, craft-many, leftovers, ingredient consumption, malformed recipe error snapshot.
7. **P6: Special inventories** - trader, creative, backpacks, barrel/liquid slots, output slots, and modded storage subclasses.
8. **P7: World interactions** - held-use/block-use/entity-use callbacks that mutate active hotbar, mouse slot, or block inventories.

## Design Notes

- Preview should call vanilla semantics on cloned slot objects whenever possible. Hand-coded "vanilla-like" merge rules will drift.
- `DummySlot` preview is acceptable for first-pass normal slots, but not for specialized slot subclasses like crafting output, trade, barrel input, liquid-only, or machine output slots.
- Every owned transaction must include the mouse cursor slot in its fingerprint/result, because vanilla treats it as a normal inventory slot.
- On any exception or incomplete delta capture, request/supply a full snapshot of every touched inventory instead of trying to keep partial preview state alive.
- A lower-level mutation guard must not suppress vanilla packets unless handshake/ownership is active. Client-only installs must stay harmless on servers without ResponsiveVS.
