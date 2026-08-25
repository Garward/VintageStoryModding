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

Important tradeoff:

- V1 prioritizes correctness over burst throughput. Serializing owned transactions per player means one action can feel instant through preview, but rapid chains of distinct actions are limited by server round trip until a safe preview-chaining design exists.

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

## Runtime Data Hot Path Issue

Vintage Story is heavily data driven. That is a strength for modding, but it also means JSON-derived data can remain live at runtime instead of always being converted into typed fields during load.

Observed source shape:

- Assets are indexed by category and location, then loaded by consumers as needed.
- `JsonObject` wraps Newtonsoft `JToken`; keyed lookups return wrapper objects around live tokens.
- `JsonObject.AsObject<T>()` can stringify the token and deserialize it again.
- `Collectible.Attributes`, `Block.Attributes`, recipe attributes, behavior properties, and many mod extension points remain available as dynamic data after load.
- Item stack attributes are serialized into inventory/network packets, so attribute-heavy stacks can increase slot update payload cost.

Target architecture:

```text
data-driven initialization -> typed or cached runtime data
```

This is not the primary inventory ghost-item correctness issue. The primary correctness issue is still optimistic client mutation plus delayed server correction. However, dynamic runtime data access can amplify the same symptoms by stalling the main thread during crafting, UI refresh, handbook generation, machine ticks, or interaction checks. A stalled client processes input, render, and incoming corrections later, widening the visible prediction/correction window.

ResponsiveVS should treat this as an optional performance and diagnostics track, separate from the inventory transaction ownership model.

Allowed direction:

- Add diagnostics for hot `JsonObject`/attribute conversions and stack attribute packet sizes.
- Cache specific proven-expensive conversions where source data is effectively immutable after asset load.
- Move known hot vanilla/mod paths toward typed cached data when the callsite is understood.
- Keep unknown mod extension data dynamic by default.

Do not attempt:

- global replacement of `JsonObject`
- global caching of all attribute lookups without mutation/version guarantees
- rewriting every `Attributes["x"]["y"].As...()` callsite through Harmony
- changing public JSON/attribute API semantics that mods rely on

## Threading Rule

All live inventory, GUI, pending transaction, preview, and reconciliation state is game-main-thread state.

Rules:

- Client result handlers must marshal to the client main thread before applying deltas, touching preview state, or reading GUI/inventory objects.
- Server request handlers must marshal to the server main thread before validation, execution, delta collection, or snapshot building.
- Render patches run in the render/main-thread context and may only read preview state that is owned by that thread or protected by a main-thread handoff.
- Background work may operate only on immutable snapshots, such as FastCrafting's pre-captured recipe/collectible data.

When thread context is uncertain, use `api.Event.EnqueueMainThreadTask(...)`.

## System Overview

ResponsiveVS has five subsystems:

1. **Input Interceptor**
   - Hooks GUI inventory input before vanilla slot mutation.
   - Guards lower client-side inventory mutation calls that bypass slot-grid GUI code.
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

## Interception Architecture

ResponsiveVS must not rely on `GuiElementItemSlotGridBase.SlotClick` alone. GUI interception is necessary for good preview UX, but direct callers such as auto-fill helpers, recipe tools, or other mods can call inventory methods or send vanilla packets without going through the slot grid.

Use layered interception:

1. **GUI Adapter Layer**
   - Patch `GuiElementItemSlotGridBase.SlotClick`, `OnMouseDownOnElement`, `OnMouseMove`, `OnMouseUp`, and `RenderInteractiveElements`.
   - For an owned operation, skip the vanilla method before it mutates real client inventory.
   - Recreate only the GUI bookkeeping needed for hover, handled mouse state, slot click callbacks, sound feedback, and preview rendering.
   - Do not call vanilla `SlotClick` for owned operations.

2. **Client Mutation Guard Layer**
   - Patch client-side `InventoryBase.ActivateSlot`, `InventoryBase.TryMoveItemStack`, `InventoryBase.TryFlipItemStack`, and `InventoryCraftingGrid.ActivateSlot`.
   - If a direct caller attempts an ownable mutation outside the GUI adapter, convert it into a ResponsiveVS transaction and prevent local mutation.
   - Return a neutral result to the caller: `null` packet for packet-returning methods, `false` for bool move/flip methods, and `op.MovedQuantity = 0` unless the operation is only being observed in diagnostics phase.
   - Mark this transaction as `Origin=DirectClientMutationGuard` in diagnostics.

3. **Vanilla Packet Guard Layer**
   - Patch or observe client vanilla inventory packet sends only for diagnostics at first.
   - If a vanilla activate/move/flip packet targets a slot with a pending ResponsiveVS transaction, block it, request snapshots for the touched inventories, and log `RejectedLocal:VanillaPacketDuringPending`.
   - If there is no pending ResponsiveVS transaction, allow vanilla packet fallback unless the operation is already owned by the mutation guard.

4. **Ack Application Bypass**
   - When applying authoritative server deltas, set an internal `ApplyingServerResult` guard so local slot assignment does not recursively create a new transaction.

The lower guard is what makes ResponsiveVS a real sync replacement instead of a GUI-only patch. Any client-side mutation path that is not owned or explicitly allowed as vanilla fallback must be visible in diagnostics.

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
5. `ResponsiveHandshakeHello`
6. `ResponsiveHandshakeResult`
7. `ResponsiveDiagToggle`

UDP is not used for inventory. Inventory transactions require reliable ordering.

## Protocol Handshake

ResponsiveVS is required on both sides for owned behavior.

Handshake flow:

1. Client registers the `responsivevs` channel.
2. On join/world-ready, client sends `ResponsiveHandshakeHello` with protocol version, mod version, and supported feature flags.
3. Server replies with `ResponsiveHandshakeResult`.
4. Client enables ownership only after receiving an accepted handshake.

Default behavior:

- RVS client connecting to a vanilla/non-RVS server: disable ownership, show/log a warning, and use vanilla fallback for all interactions.
- Non-RVS client connecting to an RVS server with `RequireClientAndServer=true`: refuse owned sync mode and rely on the mod's required-on-client metadata; if the server can detect missing handshake after join, kick with a clear message.
- Version mismatch with incompatible protocol: disable ownership on the client and reject on a required server.
- Version mismatch with compatible protocol: enable common feature flags only.
- If the channel is not mutually registered, connected, and handshaken, the client must never suppress a vanilla packet.
- If the channel drops or the server restarts mid-session, immediately disable ownership, clear previews, unblock vanilla fallback, and require a fresh handshake before owning another action.

Handshake result fields:

- `bool Accepted`
- `string RejectReason`
- `int ProtocolVersion`
- `string ServerModVersion`
- `string[] EnabledFeatures`
- `bool RequireClientAndServer`

No transaction may be owned before the handshake is accepted.

Packaging note:

- Server-installed ResponsiveVS should advertise `requiredOnClient: true` so clients install the mod.
- Client-installed ResponsiveVS should not require the server via mod metadata in v1, so it can remain loaded and fall back on servers without the `responsivevs` channel.
- Ownership still requires a successful two-sided handshake. A client-only install is allowed to do nothing useful beyond fallback diagnostics/timing-guard features.

Handshake hello fields:

- `int ProtocolVersion`
- `string ClientModVersion`
- `string[] SupportedFeatures`
- `long ClientSessionId`

## Message Contracts

### `InventoryTransactionRequest`

Fields:

- `long TransactionId`
- `long ClientSessionId`
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

- `TransactionId` is allocated by the client, monotonically increasing within a client session.
- `ClientSessionId` is generated randomly on client start or reconnect and scopes transaction ids.
- The server echoes both ids in every result.
- `PlayerUid` is diagnostic only. Server uses the connected player identity, not this field, for authority.
- `SourceInventoryId` is normally the mouse cursor inventory for activate-style clicks.
- `DragSlots` is empty except for drag transactions.
- Stack fingerprints are diagnostics and mismatch detection hints, not authority.

### `InventoryTransactionResult`

Fields:

- `long TransactionId`
- `long ClientSessionId`
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
- Results with an unknown `ClientSessionId` are ignored and trigger a snapshot request for currently open owned inventories.

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

Server revision policy:

- If `TargetLastChanged` or `SourceLastChanged` is older than the server inventory revision, the server should still attempt accept-and-reconcile by default.
- Reject only if the requested operation is unsafe against newer state, such as missing inventory, missing slot, source stack mismatch that makes the action impossible, or a target changed by another actor in a way that would make the client intent destructive or ambiguous.
- On reject due to stale or unsafe state, include authoritative deltas for all touched inventories and set `RejectReason` to a stable code such as `stale-source`, `stale-target`, `source-mismatch`, or `target-conflict`.
- Never mutate first and then reject.

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

## Security And Access Validation

The ResponsiveVS channel is a new trust boundary. Treat every request field as untrusted.

Server requirements:

- Never trust client-provided inventory ids, slot ids, quantities, stack fingerprints, player uid, or operation type.
- Resolve authority from the connected `IServerPlayer`, not the packet.
- Re-derive whether the player can access the target inventory from server-side open inventory state and normal game rules.
- For player inventories, resolve only inventories owned by that player.
- For block entity inventories, require that the inventory is currently open/available to that player through the server's normal GUI/open-inventory path.
- Validate every slot index against the resolved inventory count.
- Clamp or reject negative, zero, or impossible requested quantities.
- Validate `CanTake`, `CanHold`, `CanTakeFrom`, inventory filters, and any normal vanilla/modded slot rules by executing through server-side inventory methods.
- On any failed validation, reject without mutation and return authoritative deltas/snapshots for safely resolved inventories.

Resolution source:

- Use the server player's inventory manager as the primary registry for player, mouse, crafting, and open block entity inventories.
- Do not resolve arbitrary world block entity inventories directly from client-provided coordinates or ids in v1.
- If an inventory cannot be proven open and accessible to that player, fallback or reject; do not guess.

## Conflict Policy

The client keeps pending transactions keyed by touched inventory slots.

Rules:

- A slot with a pending owned transaction cannot be mutated by another owned transaction until the first result arrives or times out.
- Same-slot input is locally blocked by default.
- Multi-slot operations reserve all touched slots plus the mouse cursor slot.
- Drag operations reserve all previewed slots once the transaction is sent.
- If a non-RVS local mutation attempt touches a pending slot, block it through the mutation/packet guard, clear the affected preview, and request snapshots for every inventory touched by the pending transaction.
- If a vanilla server correction arrives for a pending slot, apply it to the real backing slot but keep rendering the ResponsiveVS preview until the transaction result, timeout, or snapshot resolves it.
- Unrelated slots may be owned in parallel only after the first implementation proves ordering is safe; default v1 behavior should serialize all inventory transactions per player.

V1 default:

```text
one pending inventory transaction per player client
```

This is conservative but stable. Parallelism can be added later with slot-level reservations.

V1 does not implement a queued pending state. Input that conflicts with the single pending transaction is blocked and measured, not queued for replay.

This deliberately trades click throughput for correctness in the first implementation. If testers report reduced spam-click throughput, that is expected until slot-level parallelism is proven safe.

## Burst Throughput Tradeoff

Strict server ownership removes fake item rollback problems, but it also removes vanilla's ability to run many real local mutations ahead of the server.

V1 decision:

- Owned inventory transactions are serialized per player.
- A second owned action waits for the first ack/reject/timeout.
- This can feel slower for burst workflows at high RTT, especially rapid chest sorting, repeated shift-clicking, and fast manual crafting.

This is intentional for the first correctness-focused implementation. The UI can preview a single pending action instantly, but it does not promise unlimited spam-click throughput.

Future research:

- Preview-chaining could allow multiple ordered pending actions against preview state.
- That would improve burst throughput but reintroduces a controlled version of "client runs ahead."
- Preview-chaining must not be added until the single-transaction model is proven and has transaction-order reconciliation tests.

Acceptance implication:

- Testing must measure both correctness and perceived burst throughput. Passing ghost-item tests is not enough if normal inventory work feels unusably rate-limited.

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

Disconnect/reconnect policy:

- On disconnect, clear every pending transaction and preview immediately.
- On reconnect, generate a new `ClientSessionId`.
- After handshake, request snapshots for any owned inventories that are still open or recreated by the client.
- Ignore late results from the previous session.

GUI/death/world lifecycle policy:

- On GUI close, clear previews and pending reservations for inventories owned by that GUI. Late results for closed inventories are ignored after requesting snapshots for still-open owned inventories.
- On player death or forced inventory close, clear all client pending transactions and previews.
- On server player leave/death where inventory ownership changes, clear that player's server-side pending/serialization state.
- On world unload or return-to-menu, clear all static stores: pending transactions, handshake state, recipe indexes, diagnostics counters tied to world state, and compatibility probes.

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
- Vanilla `InventoryUpdate`, `InventoryDoubleUpdate`, `InventoryContents`, and block entity tree syncs update the real backing slots even while a preview is visible. They must not erase preview state unless tied to a ResponsiveVS result, timeout, or snapshot.

Preview computation:

- For simple clicks, compute preview by cloning the involved real slots into throwaway slots and running the actual vanilla/modded slot operation against the clones.
- Do not hand-reimplement general `ItemSlot.ActivateSlot` merge rules for preview.
- For drag-right, preview one item per valid slot until source preview stack is exhausted.
- For drag-left, preview even distribution with remainder assigned to later slots to match existing user expectation.
- If preview computation cannot safely model the slot, use fallback vanilla for that operation.

The preview may be wrong. That is acceptable because the server result overwrites it.

Preview clone rules:

- Clone only stack state needed for the preview operation.
- Never attach throwaway slots to the real inventory's dirty-slot or network path.
- If a slot type cannot be safely cloned without side effects, do not own that operation for that slot type.
- Liquids, containers, attribute-sensitive stacks, and custom modded slots must use real cloned slot behavior or fallback; no special hand-coded approximation.

### Mouse Cursor State

The mouse cursor inventory is the highest-risk shared slot and must be included in every owned transaction result.

Rules:

- Every request includes `MouseStackBefore`.
- Every accepted or rejected result includes a `SlotDelta` for `"mouse-" + playerUID` slot `0`.
- If the client mouse cursor fingerprint differs from the server result after applying a delta, request snapshots for the mouse inventory and the target inventory.
- If the client cannot resolve the mouse inventory, disable ownership and fall back to vanilla until the next successful handshake/snapshot cycle.
- Pending transaction reservation always includes the mouse cursor slot, even for operations that appear to target only one inventory slot.

The client never uses preview mouse stack size as source for another transaction.

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
- If target or source inventory cannot be resolved, return `InventoryMissing` and include snapshots for every currently open owned inventory that can be resolved.
- If a slot cannot be resolved, return `SlotMissing` and include a snapshot for that inventory when possible.

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

Drag-left exception:

- Vanilla's even-distribution algorithm lives in GUI code, not in a clean server inventory API.
- ResponsiveVS is allowed to implement a dedicated server-side drag-left distribution helper as a documented exception to the "do not reimplement semantics" rule.
- The helper must be byte-for-byte simple in behavior: use server mouse stack size, ordered valid drag slots, even share per slot, and remainder assigned to later slots to match the preview expectation.
- Each slot placement still uses real server slot/inventory merge checks. Only the distribution quantity calculation is custom.
- If the helper cannot model a slot safely, reject or fallback before mutation.

### Server Delta Capture

Correct delta capture is mandatory. Missing one changed slot is a sync bug.

Preferred v1 strategy:

- Before executing a transaction, register a transaction-local slot modification collector on every inventory that may be touched.
- The collector records inventory id and slot id when `DidModifyItemSlot`, `MarkSlotDirty`, or equivalent dirty-slot paths fire.
- Also pre-register known source, target, mouse, and drag slots so a rejected or no-op transaction can still return authoritative deltas.
- Execute the vanilla/modded operation.
- Build `SlotDelta` results by reading final stack state from the union of recorded and pre-registered slots.

Ordering hazard:

- Do not rely on vanilla dirty-slot collections after a vanilla method has had a chance to package or clear them.
- If a server-side vanilla helper pops or clears dirty state while generating vanilla packets, ResponsiveVS must capture before that point or suppress the vanilla packet-generation side effect inside the owned executor.
- The owned executor should ignore returned vanilla packets and should not use them as the source of slot deltas.

Fallback:

- If the touched-slot set cannot be captured reliably for an operation, reject with snapshots instead of accepting with incomplete deltas.

## Crafting Output Rules

Crafting output is the first high-risk special case.

Preview policy:

- Before Phase 5, craft-output clicks are not owned unless diagnostics mode is only observing. They fall back to vanilla because output slots depend on `MatchingRecipe`, leftover state, and `GenerateOutputStack`.
- In Phase 5, craft-output preview may display the current visible output stack only. It must not recompute recipe output on the client as part of prediction.
- The server result is the first authoritative craft-output mutation.

Rules:

- Ingredient consumption happens only after the server confirms the output transfer.
- Full recipe output must fit before consumption.
- Partial transfer from output slot is rejected or converted into a full-output-only move.
- `ItemSlotCraftingOutput` leftover state is reset or regenerated after every owned output transaction.
- Malformed recipe output exceptions are caught, logged, and returned as `Rejected` or `ServerError`; they must not disconnect the player.
- Craft-many and shift-click results may include many slot deltas. This is acceptable; correctness is preferred over minimizing packet size.

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

## Runtime Data Diagnostics Integration

Runtime data diagnostics are an optional subsystem for finding JSON-derived and attribute-derived hot paths that can create client or server stalls.

Initial scope:

- Count and time `JsonObject.AsObject<T>()` calls by target type and, in trace mode, by caller.
- Count and time `JsonObject.AsArray<T>()` and high-volume keyed lookups only when diagnostics are explicitly enabled.
- Measure serialized `ItemStack.Attributes` byte length during inventory slot update packet construction.
- Report top callers/stack types periodically or through a command.

Rules:

- Default off.
- Basic diagnostics may collect cheap aggregate counts only.
- Trace diagnostics may collect caller stacks but must warn that it is expensive.
- No global caching is enabled until logs identify a stable hot path.
- Any cache must be invalidated on world reload and must assume modded data can be mutable unless proven otherwise.

Potential later optimizations:

- cache immutable `AsObject<T>()` conversions for known asset-level attributes
- cache common collectible capability flags after `OnLoaded`
- expose warnings for repeated dynamic attribute reads in recipe, handbook, machine, or interaction hot paths

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
- Apply the vanilla correction to the real backing slot but keep the pending preview visible.
- If correction matches pending server result later, it is harmless.
- If correction conflicts after accepted result, request snapshot and log mismatch.

This avoids making time-window suppression part of correctness.

## Harmony Patch Strategy

ResponsiveVS patches hot methods that other prototype mods also patch. It should not try to coexist with the mods it replaces.

Hard incompatibilities:

- `ItemSyncFixes`
- `FastCraftingGrid` as a separate installed mod once its code is integrated

Startup behavior:

- Detect known Harmony ids for subsumed mods.
- Log a clear error and disable ResponsiveVS ownership if a hard-incompatible mod is active.
- Do not partially own inventory while ItemSyncFixes prediction suppression or drag preview is also active.

Patch ordering:

- GUI adapter prefixes must run before vanilla mutation and before compatibility postfixes that assume mutation already happened.
- Mutation guard prefixes must run before client-side inventory mutation.
- Server executor patches must not skip vanilla server methods globally; they should execute owned transactions from ResponsiveVS channel handlers and use patches only for diagnostics or guarded capture.
- FastCraftingGrid integration should be internal to ResponsiveVS so recipe matching patch order is controlled in one assembly.

Known external interaction:

- BetterHandbook and other automation mods may call `TryTransferTo`, `SlotClick`, or send vanilla packets directly. The mutation guard and packet guard are responsible for observing or owning those calls; GUI adapter coverage alone is insufficient.

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
  "RuntimeData": {
    "EnableRuntimeDataHotPathPatch": false,
    "EnableAsObjectResultCache": true,
    "EnableStackAttributePacketDiagnostics": true,
    "TraceCallerStacks": false,
    "MaxCachedAsObjectResults": 4096
  },
  "RenderPendingOverlay": true
}
```

Config rules:

- `RequireClientAndServer` should stay true for public releases.
- `UnknownModdedInventories` should stay false until enough diagnostics prove safety.
- `EnableGridRecipeMatchesPrefilter` defaults false in ResponsiveVS even if FastCraftingGrid used it, because this mod's first job is correctness.
- Runtime data diagnostics default off. Caller-stack tracing is explicitly opt-in because stack capture can be more expensive than the issue being measured.

## Implementation Phases

The phases in this document are canonical. Older research notes may use shorter phase lists; treat those as preliminary maps, not the implementation schedule.

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
- diagnostics level is forced to at least `Basic` for development builds of this phase
- `GeneralPacketHandler.HandleInventoryDoubleUpdate` compatibility patch logs when it fixes a two-inventory update

### Phase 2: Client Preview Without Server Mutation

Implement preview store and rendering for owned inventory slots.

Behavior:

- use dry-run mode only
- do not let preview code mutate real inventory slots
- let vanilla continue after the dry-run preview is cleared so behavior remains unchanged
- prove preview rendering and pending slot reservation are visually correct

Acceptance:

- preview never mutates real slots in dry-run mode
- pending overlay appears and clears reliably
- vanilla behavior remains unchanged after the dry-run preview completes

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

### Parallel Track: World Timing Guards

This track is independent from the inventory transaction replacement and may ship earlier if it stays small and isolated.

Allowed scope:

- clamp huge or invalid `deltaTime` values in known unsafe client/entity paths
- debounce GUI/focus refocus transitions that corrupt UI timing
- cap client held-use or interaction step lead over the last known server state if a narrow, testable hook is found

Rules:

- Do not couple these patches to inventory transaction ownership.
- Do not expand this track into full block/entity interaction replacement without a separate design.
- Treat this as a low-risk mitigation path for FPS/focus/dt symptoms while the larger transaction model bakes.

## Testing Matrix

Required environments:

- singleplayer
- dedicated server localhost
- live dedicated server
- low-latency flat test world
- heavily modded server
- simulated RTT/jitter/loss where practical

FPS/focus scenarios:

- 30 FPS cap
- 60 FPS cap
- uncapped FPS
- vsync on
- vsync off
- alt-tab/focus loss during pending transaction
- high UI scale and normal UI scale

Network scenarios:

- localhost dedicated server baseline
- 50 ms RTT
- 150 ms RTT
- jitter bursts of at least 50 ms
- brief packet loss or TCP stall simulation where available
- mid-session channel loss or server restart behavior where practical

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
- rapid distinct-slot burst actions measured for perceived throughput
- chest emptying with repeated shift-clicks at 50 ms and 150 ms RTT

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
- localhost dedicated p99 transaction round trip under 50 ms for simple inventory clicks
- localhost dedicated p99 server execution time under 5 ms for simple inventory clicks
- heavily modded crafting p99 recipe matching under 10 ms after FastCraftingGrid integration
- no vanilla packet is suppressed before a successful ResponsiveVS handshake
- if the ResponsiveVS channel drops mid-session, ownership disables and vanilla fallback resumes
- pending mouse cursor preview and slot preview agree visually during owned transactions
- burst-throughput tests must record actions per second and user-visible blocking time at each RTT

## Remaining Research Before Runtime Code

- Confirm every slot-grid input path: mouse, shift, ctrl, alt, wheel, keyboard navigation, number-key swap.
- Audit BetterHandbook auto-fill and other local mods that call `TryTransferTo`, `SlotClick`, or send vanilla inventory packets directly.
- Prove the `DidModifyItemSlot` plus `MarkSlotDirty` transaction-local delta collector in Phase 1 observe-only mode.
- Expand the safe preview allow-list only after specific slot subclasses are tested.

Resolved research:

- Mouse cursor rendering is `HudMouseTools` composing passive slot `"mouseSlot"` over the `"mouse"` inventory.
- External storage access is proven server-side by `GetInventory(id, out inv)` and `inv.HasOpened(player)`.
- `GeneralPacketHandler.HandleInventoryDoubleUpdate` has a confirmed `invId1`/`invId2` lookup bug.
- Vanilla server activate handling ignores the `InventoryBase.ActivateSlot()` returned packet, so ResponsiveVS server executors can ignore it too.
- TCP custom packet handlers are effectively main-thread on both sides, but ResponsiveVS still marshals custom handler work because UDP/custom paths are not guaranteed.

## Implementation Guardrails

- Do not port `ClientPredictionSuppressor` as the core fix.
- Do not port `ClientExternalClickGate` as the core fix.
- Do not use time-window packet suppression for correctness.
- Do not force ownership of unknown modded inventories by default.
- Do not mutate client inventory slots for owned input before server ack.
- Do not optimize away diagnostics until the architecture is proven.
- Do not suppress a vanilla packet unless ownership is handshaken and the action has been accepted into the ResponsiveVS pending transaction system.
- Do not implement preview-chaining until the serialized transaction model is stable and tested.

The mod succeeds when the UI is responsive because preview is cheap and immediate, while actual state is stable because it is server-owned and transaction-bound.
