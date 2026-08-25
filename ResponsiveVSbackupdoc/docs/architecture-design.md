# ResponsiveVS Architecture Design

Status: design contract before runtime implementation.

ResponsiveVS is intended to replace fragile Vintage Story inventory interaction prediction with an explicit transaction model. The core rule is simple: client input may create a visual preview, but only the server mutates real inventory state.

This document is the implementation guide for the first real version of ResponsiveVS. If code conflicts with this design, the design wins until deliberately revised.

## Architecture Goals

- Inventory and crafting correctness must not vary with FPS, video settings, vsync, focus state, GUI frame pacing, or `deltaTime`.
- The client must not chain real inventory actions from speculative local state.
- The server remains authoritative for all item movement, crafting output, and external storage mutation.
- The UI should still feel responsive by rendering preview and pending state immediately.
- Unknown or risky modded inventories fall back to vanilla unless explicitly enabled in config.
- Existing vanilla/modded inventory semantics should be reused wherever possible; ResponsiveVS owns timing and synchronization, not every item rule.

## Non-Goals For The First Architecture

- Do not rewrite every inventory, slot, recipe, block entity, or item rule.
- Do not make the client authoritative.
- Do not rely on packet suppression windows as the main correctness model.
- Do not start with world interaction replacement. Block/entity/held-use interactions are researched later after inventory is stable.
- Do not force ownership of arbitrary modded inventories by default.

## Existing Failure Model

Vanilla slot UI is optimistic:

1. `GuiElementItemSlotGridBase.SlotClick` receives input.
2. The client calls `inventory.ActivateSlot`, `TryMoveItemStack`, `TryFlipItemStack`, or drag redistribution locally.
3. Local inventory slots mutate immediately.
4. A vanilla packet is sent to the server.
5. The server later repeats the action against authoritative state.
6. Dirty slot, double slot, or full contents packets eventually correct the client.

The weak points:

- `ActivateSlot` has no transaction id and no explicit server ack.
- Client state can run multiple clicks ahead before the first server correction.
- Click-drag mutates every crossed slot during the drag.
- Left drag pauses inventory updates, allowing queued corrections to go stale.
- Broad `InventoryContents` and block entity tree syncs can race local predictions.
- FPS/focus stalls can change how many input/step events are processed before reconciliation.

ResponsiveVS should stop creating fragile local state instead of hiding corrections afterward.

## System Overview

ResponsiveVS has five subsystems:

1. **Input Interceptor**
   - Hooks GUI inventory input before vanilla slot mutation.
   - Converts the user action into an `InventoryTransactionRequest`.
   - Chooses owned path or vanilla fallback.

2. **Client Preview Store**
   - Stores transaction-scoped visual deltas.
   - Renders pending target slots and mouse cursor state without changing real inventory slots.
   - Blocks or queues conflicting input while relevant slots are pending.

3. **Transaction Network**
   - Uses a custom mod channel named `responsivevs`.
   - Client sends requests to server.
   - Server returns results with authoritative slot deltas.

4. **Server Executor**
   - Resolves inventories from the server player's inventory manager.
   - Executes equivalent vanilla/modded inventory methods on server state.
   - Captures authoritative changed slots and returns them to the client.

5. **Reconciler And Diagnostics**
   - Applies accepted deltas to the client.
   - Clears previews on ack/reject/timeout.
   - Logs transaction timing, ownership decisions, fallback reasons, and mismatches.

## Transaction Lifecycle

Every owned interaction follows this lifecycle:

1. Client input arrives at a slot grid.
2. ResponsiveVS checks if the inventory and operation are ownable.
3. If not ownable, vanilla handles the input and diagnostics record the fallback reason.
4. If ownable, ResponsiveVS creates a transaction id.
5. ResponsiveVS computes preview deltas for target and mouse cursor slots.
6. ResponsiveVS stores preview state and sends a request packet.
7. The server validates the request against authoritative state.
8. The server executes the operation or rejects it.
9. The server sends a result packet with authoritative deltas or reject reason.
10. The client applies the result, clears matching preview state, and logs timing.

No real client inventory slot should be mutated by the owned path before step 10.

## Network Channel

Register one TCP mod channel on both sides:

```text
responsivevs
```

Register message types in identical order on client and server:

1. `InventoryTransactionRequest`
2. `InventoryTransactionResult`
3. `InventorySnapshotRequest`
4. `InventorySnapshotResult`
5. `ResponsiveDiagToggle`

UDP is not used for inventory. Inventory transactions require reliable ordering.

## Message Contracts

### `InventoryTransactionRequest`

Fields:

- `long TransactionId`
- `string PlayerUid`
- `string TargetInventoryId`
- `int TargetSlot`
- `string SourceInventoryId`
- `int SourceSlot`
- `ResponsiveInventoryOp Operation`
- `ResponsiveMouseButton MouseButton`
- `int Modifiers`
- `int WheelDir`
- `int RequestedQuantity`
- `long TargetLastChanged`
- `long SourceLastChanged`
- `string TargetStackBefore`
- `string SourceStackBefore`
- `string MouseStackBefore`
- `int[] DragSlots`
- `int[] DragSlotOrder`
- `long ClientCreatedMs`

Rules:

- `PlayerUid` is diagnostic only. Server uses the connected player identity, not this field, for authority.
- `SourceInventoryId` is normally the mouse cursor inventory for activate-style clicks.
- `DragSlots` is empty except for drag transactions.
- Stack fingerprints are diagnostics and mismatch detection hints, not authority.

### `InventoryTransactionResult`

Fields:

- `long TransactionId`
- `ResponsiveTransactionStatus Status`
- `string RejectReason`
- `SlotDelta[] Deltas`
- `InventoryRevision[] Revisions`
- `long ServerStartedMs`
- `long ServerFinishedMs`

Rules:

- Accepted results must include every slot the server knows changed.
- Rejected results should include authoritative deltas for the involved source, target, and mouse slots when possible.
- Client clears preview for the transaction on any terminal result.

### `SlotDelta`

Fields:

- `string InventoryId`
- `int Slot`
- `Packet_ItemStack Stack`
- `string StackFingerprint`

Rules:

- `Stack == null` or `ItemClass == -1` means empty.
- Deltas are applied directly to the local inventory slot if the inventory is still open/available.
- If the target inventory is gone, the delta is logged and dropped.

### `InventoryRevision`

Fields:

- `string InventoryId`
- `long LastChanged`

Rules:

- Revisions mirror vanilla `lastChangedSinceServerStart`.
- They are not enough by themselves to solve sync; they are used for diagnostics, stale request detection, and resync decisions.

### `InventorySnapshotRequest`

Fields:

- `long RequestId`
- `string InventoryId`
- `string Reason`

Use this when the client detects missing inventory, timeout, impossible delta application, or explicit diagnostics request.

### `InventorySnapshotResult`

Fields:

- `long RequestId`
- `string InventoryId`
- `SlotDelta[] Deltas`
- `long LastChanged`
- `string Reason`

This is the custom equivalent of a targeted full inventory contents correction.

## Enums

### `ResponsiveInventoryOp`

Values:

- `ActivateLeft`
- `ActivateRight`
- `ActivateWheel`
- `ShiftClick`
- `MoveStack`
- `FlipStacks`
- `HotbarSwap`
- `DragRight`
- `DragLeft`
- `CraftOutputTake`
- `CraftOutputShiftMany`
- `CraftOutputMove`

### `ResponsiveTransactionStatus`

Values:

- `Accepted`
- `Rejected`
- `FallbackRequired`
- `ServerError`
- `InventoryMissing`
- `SlotMissing`
- `Conflict`

### `ResponsiveMouseButton`

Values:

- `Left`
- `Right`
- `Wheel`
- `None`

## Ownership Policy

ResponsiveVS classifies every interaction as one of:

- `Owned`
- `FallbackVanilla`
- `BlockedPending`
- `QueuedPending`
- `RejectedLocal`

Default owned inventories:

- player hotbar
- player backpack
- player mouse cursor inventory
- player crafting grid
- vanilla chest-like inventories exposed through the player's open inventory manager
- vanilla quern and standard block entity inventories that use normal `InventoryBase`/`InventoryGeneric` behavior

Default fallback inventories:

- creative inventory
- ground inventory
- character/equipment slots until separately audited
- unknown modded inventory classes
- inventories whose slots reject normal `CanTake`, `CanHold`, or `CanTakeFrom` checks during preview validation

Config opt-in inventories:

- specific inventory class names
- specific inventory id prefixes
- specific mod ids if a reliable mapping is available

The first implementation should prefer too many fallbacks over unsafe ownership. A fallback must log why it happened when diagnostics are enabled.

## Conflict Policy

The client keeps pending transactions keyed by touched inventory slots.

Rules:

- A slot with a pending owned transaction cannot be mutated by another owned transaction until the first result arrives or times out.
- Same-slot input is locally blocked by default.
- Multi-slot operations reserve all touched slots plus the mouse cursor slot.
- Drag operations reserve all previewed slots once the transaction is sent.
- Unrelated slots may be owned in parallel only after the first implementation proves ordering is safe; default v1 behavior should serialize all inventory transactions per player.

V1 default:

```text
one pending inventory transaction per player client
```

This is conservative but stable. Parallelism can be added later with slot-level reservations.

## Timeout Policy

Default pending timeout:

```text
1500 ms
```

On timeout:

1. Clear preview state.
2. Request snapshots for all inventories touched by the timed-out transaction.
3. Log timeout with transaction details.
4. Allow new input only after snapshot result or a second timeout of 1500 ms.

Timeout does not mean the client invents final state.

## Client Preview Model

Preview state is stored separately from real inventory slots:

- transaction id
- created time
- operation
- target/source/mouse slot keys
- preview stack for each slot
- display status: pending, accepted, rejected, timed out

Rendering rules:

- Slot grids render preview stack over the real slot while pending.
- Mouse cursor rendering must also consult preview state.
- Pending slots should have a subtle overlay so diagnostic testers can see owned behavior.
- Preview stack counts are never used as source of truth for later transactions.

Preview computation:

- For simple clicks, compute the expected visual result using cloned stacks and vanilla-like merge rules.
- For drag-right, preview one item per valid slot until source preview stack is exhausted.
- For drag-left, preview even distribution with remainder assigned to later slots to match existing user expectation.
- If preview computation cannot safely model the slot, use fallback vanilla for that operation.

The preview may be wrong. That is acceptable because the server result overwrites it.

## Server Execution Model

The server executor receives a request and resolves inventories using the connected player's inventory manager.

Resolution order:

1. Resolve target inventory by `TargetInventoryId`.
2. Resolve source inventory by `SourceInventoryId` when provided.
3. Resolve mouse inventory as `"mouse-" + player.PlayerUID` for activate-style clicks.
4. Validate slot indices.
5. Validate inventory ownership/access through existing open inventory state where possible.

Execution rule:

- Use vanilla/modded methods to perform actual mutation whenever possible.
- Capture changed slots before and after execution.
- Return authoritative deltas from server state.

For activate-style clicks:

- Build `ItemStackMoveOperation` on the server.
- Set `ActingPlayer`.
- Call `targetInv.ActivateSlot(targetSlot, sourceSlot, ref op)`.
- Ignore the returned vanilla packet except for diagnostics.

For move/flip:

- Prefer `TryMoveItemStack` and `TryFlipItemStack` against server inventories.
- If vanilla stale checks are needed, perform them before execution and reject with snapshot instead of mutating.

For shift-click:

- Execute server-side `ActivateSlot` with shift modifier.
- Capture all dirty slots across player inventory, target inventory, and mouse inventory after execution.

For drag:

- Do not replay client frame-by-frame drag.
- Server receives the final slot list and executes deterministic per-slot moves in that order.
- For drag-left, server computes requested quantity per slot from server source stack at execution time.
- For drag-right, server places one per valid slot while source stack remains.

## Crafting Output Rules

Crafting output is the first high-risk special case.

Rules:

- Ingredient consumption happens only after the server confirms the output transfer.
- Full recipe output must fit before consumption.
- Partial transfer from output slot is rejected or converted into a full-output-only move.
- `ItemSlotCraftingOutput` leftover state is reset or regenerated after every owned output transaction.
- Malformed recipe output exceptions are caught, logged, and returned as `Rejected` or `ServerError`; they must not disconnect the player.

Adapt from ItemSyncFixes:

- full-output fit check
- craft-many full outputs only
- leftover state reset
- missing output result exception guard

Do not port:

- post-hoc stale packet suppression as crafting correctness logic

## Fast Crafting Integration

FastCraftingGrid becomes the performance subsystem of ResponsiveVS.

Keep:

- bitset recipe index
- grid fingerprint cache
- vanilla fallback on suspicious empty candidates
- burst diagnostics

Change:

- index build snapshots `world.FastSearchRecipesByIngredient`, `world.GridRecipes`, and `world.Collectibles` on the main thread first
- derived bitset build may run in the background after snapshot
- global `GridRecipe.Matches` prefilter is configurable and disabled for unknown unsafe callers if mismatches are detected

Purpose:

- transaction ownership should not feel worse because every server ack or preview refresh triggers expensive crafting scans

## Vanilla Packet Relationship

Owned path:

- Client does not send vanilla activate/move/flip packet for the owned action.
- Client sends ResponsiveVS transaction request.
- Server sends ResponsiveVS transaction result.
- Vanilla dirty-slot packets may still arrive later as background confirmation.

Fallback path:

- Client lets vanilla execute normally.
- Diagnostics record fallback reason.
- Existing vanilla packets remain untouched.

Vanilla correction handling:

- Do not suppress arbitrary vanilla corrections by default.
- If a vanilla correction arrives for a pending owned slot before the result, log it.
- If correction matches pending server result later, it is harmless.
- If correction conflicts after accepted result, request snapshot and log mismatch.

This avoids making time-window suppression part of correctness.

## Diagnostics

Command:

```text
.rvsdiag on
/rvsdiag on
.rvsdiag off
/rvsdiag off
```

Log prefix:

```text
[RVS]
```

Each transaction log should include:

- transaction id
- side
- player
- operation
- ownership classification
- fallback reason if any
- target/source inventory ids
- target/source slots
- client stack before fingerprints
- preview fingerprints
- server before fingerprints
- server after fingerprints
- status
- reject reason
- client wait time
- server execution time
- slot delta count

Diagnostic levels:

- `Off`: only startup and fatal errors
- `Basic`: transaction summaries and fallbacks
- `Verbose`: before/after slot fingerprints and packet correlation
- `Trace`: caller stacks and vanilla packet details

Default:

```json
{
  "Diagnostics": "Off"
}
```

## Configuration

Config file:

```text
ModConfig/responsivevs.json
```

Initial shape:

```json
{
  "Diagnostics": "Off",
  "RequireClientAndServer": true,
  "OwnedInventories": {
    "PlayerHotbar": true,
    "PlayerBackpack": true,
    "PlayerCraftingGrid": true,
    "VanillaBlockInventories": true,
    "UnknownModdedInventories": false
  },
  "OptInInventoryClassNames": [],
  "OptInInventoryIdPrefixes": [],
  "TransactionTimeoutMs": 1500,
  "SerializeTransactionsPerPlayer": true,
  "EnableFastCraftingIndex": true,
  "EnableGridRecipeMatchesPrefilter": false,
  "RenderPendingOverlay": true
}
```

Config rules:

- `RequireClientAndServer` should stay true for public releases.
- `UnknownModdedInventories` should stay false until enough diagnostics prove safety.
- `EnableGridRecipeMatchesPrefilter` defaults false in ResponsiveVS even if FastCraftingGrid used it, because this mod's first job is correctness.

## Implementation Phases

### Phase 0: Documentation And Audit

Deliverables:

- this architecture doc
- current interaction map
- existing mod adaptation map
- source audit list for every GUI input path

No runtime behavior change.

### Phase 1: Transaction Diagnostics

Implement channel, message types, transaction id allocation, ownership classifier, and diagnostics.

Behavior:

- observe/intercept input
- classify owned/fallback
- log what would be sent
- let vanilla continue

Acceptance:

- every tested click/drag/shift/wheel/number-key action logs one clear classification
- no behavior change except logging

### Phase 2: Client Preview Without Server Mutation

Implement preview store and rendering for owned inventory slots.

Behavior:

- still let vanilla handle final mutation for safety in this phase, or use a dry-run mode
- prove preview rendering and pending slot reservation are visually correct

Acceptance:

- preview never mutates real slots in dry-run mode
- pending overlay appears and clears reliably

### Phase 3: Owned Simple Transactions

Own simple activate-style clicks for player inventory and vanilla storage:

- left click
- right click
- wheel one
- basic stack flip

Acceptance:

- rapid repeated clicks cannot create fake client stacks
- server deltas reconcile client without drop/pickup or relog
- FPS cap changes do not change final result

### Phase 4: Owned Shift, Move, Flip, Hotbar

Own higher-risk multi-slot operations:

- shift click
- explicit move stack
- flip stacks
- number-key/hotbar swap

Acceptance:

- all changed slots are returned as deltas
- unstackable items and full inventories reject cleanly
- no real or fake duplication under spam

### Phase 5: Crafting And Drag

Own crafting grid and output behavior:

- drag-left
- drag-right
- output take
- shift craft many
- output move/hotbar swap

Integrate FastCraftingGrid.

Acceptance:

- click-drag is preview-only until release
- crafting output cannot leave ghost leftovers
- known laggy recipes stay responsive

### Phase 6: External Modded Opt-In

Support config opt-in for modded inventories.

Acceptance:

- unknown inventories fall back with clear logs
- opted-in classes can be tested individually
- config can disable ownership for a broken class without removing the mod

### Phase 7: World Interaction Research

Only after inventory is stable:

- map block placement
- map block interact start/step/stop
- map held item use
- map entity interaction
- audit `deltaTime`/focus/FPS-sensitive replay paths

No world interaction replacement should be implemented before this audit.

## Testing Matrix

Required environments:

- singleplayer
- dedicated server localhost
- live dedicated server
- low-latency flat test world
- heavily modded server

FPS/focus scenarios:

- 30 FPS cap
- 60 FPS cap
- uncapped FPS
- vsync on
- vsync off
- alt-tab/focus loss during pending transaction
- high UI scale and normal UI scale

Inventory scenarios:

- left click stack pickup/place
- right click place one
- right click split half
- shift click into player inventory
- shift click into full inventory
- wheel one item
- number-key swap
- rapid same-slot spam
- rapid alternating-slot spam

Crafting scenarios:

- drag-right through crafting grid
- drag-left through crafting grid
- craft one output repeatedly
- shift craft many stackable outputs
- craft unstackable outputs
- output into nearly full inventory
- malformed recipe output from Toolsmith-style case

Storage scenarios:

- player backpack
- hotbar
- vanilla chest
- vanilla quern
- standard block entity inventory
- VintageKinematics machine only after opt-in
- unknown modded inventory fallback

Acceptance criteria:

- no permanent fake item
- no client-only stack growth after spam
- no server-side duplication
- no required relog/drop-pickup to recover
- no visible 300-900ms freeze on known crafting recipe tests
- changing FPS/video settings does not change transaction results

## Remaining Research Before Runtime Code

- Confirm every slot-grid input path: mouse, shift, ctrl, alt, wheel, keyboard navigation, number-key swap.
- Identify where mouse cursor item rendering should read preview state.
- Confirm block entity GUI packet wrapping and inventory id resolution for external storage.
- Verify whether `GeneralPacketHandler.HandleInventoryDoubleUpdate` has a real two-inventory lookup bug.
- Audit BetterHandbook auto-fill and other local mods that call `TryTransferTo`, `SlotClick`, or send vanilla inventory packets directly.
- Decide how to collect all changed slots after a server transaction without relying only on `DirtySlots`.
- Confirm whether any slot types have side effects that make client-side preview simulation unsafe; those must fallback until explicitly supported.

## Implementation Guardrails

- Do not port `ClientPredictionSuppressor` as the core fix.
- Do not port `ClientExternalClickGate` as the core fix.
- Do not use time-window packet suppression for correctness.
- Do not force ownership of unknown modded inventories by default.
- Do not mutate client inventory slots for owned input before server ack.
- Do not optimize away diagnostics until the architecture is proven.

The mod succeeds when the UI is responsive because preview is cheap and immediate, while actual state is stable because it is server-owned and transaction-bound.
