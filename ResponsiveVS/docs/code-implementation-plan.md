# ResponsiveVS Code Implementation Plan

Status: code layout and build plan. This document translates `architecture-design.md` into concrete modules and implementation order.

The architecture document remains the authority for behavior. This document answers where code goes, which subsystem owns each responsibility, and what to build first.

## Project Shape

ResponsiveVS should be a normal Vintage Story C# mod with one assembly and both client/server mod systems.

Top-level layout:

```text
ResponsiveVS/
  ResponsiveVS.csproj
  modinfo.json
  README.md
  scripts/
    package.sh
  docs/
    architecture-design.md
    code-implementation-plan.md
    client-mutation-coverage.md
    interaction-cycle-map.md
    existing-mod-adaptation-map.md
  src/
    ResponsiveVSModSystem.cs
    Config/
    Diagnostics/
    Network/
    Threading/
    Transactions/
    Client/
    Server/
    Inventory/
    Crafting/
    FastCrafting/
    RuntimeData/
    TimingGuards/
    Compatibility/
```

Build/package expectations:

- `modinfo.json` should be universal with `requiredOnClient: true` and `requiredOnServer: false` unless a Phase 0 verification disproves this.
- Rationale: server installs must force clients to have the mod, but client-only installs should remain loaded and harmlessly fall back when a server lacks the `responsivevs` channel.
- `ResponsiveVS.csproj` targets the same framework/style as the existing one-off mods.
- Release builds run `scripts/package.sh` through a `PackageDist` target.
- The package should include the DLL, deps file if produced, `modinfo.json`, and user-facing README. Internal docs can remain repo-only unless intentionally shipped.

## Threading Model

Inventory, GUI, and real slot state are main-thread-only unless a specific phase proves otherwise.

Rules:

- Client GUI interception, preview store mutation, preview rendering, result application, snapshot application, and real inventory slot writes run on the client main thread.
- Server validation, execution, delta collection, and snapshot building run on the server main thread.
- Network message handlers must treat their thread as unspecified. If they need to read/write inventory, GUI, pending state, or preview state, marshal through `api.Event.EnqueueMainThreadTask(...)`.
- Background work may only process immutable snapshots. It must not read `IWorldAccessor`, live inventories, live item slots, GUI state, or block entities.
- FastCrafting may build derived bitsets off-thread only after the world recipe/collectible references it needs were snapshotted on the main thread.

Threading helper:

```text
src/Threading/
  MainThreadDispatcher.cs
  ThreadAssert.cs
```

Responsibilities:

- Provide `RunClientMain`, `RunServerMain`, and `AssertMainThread` helpers.
- Centralize `RuntimeEnv.MainThreadId` checks where available.
- Give every marshaled task a stable profiler code, e.g. `responsivevs-apply-result`.

## Source Modules

### Root

`src/ResponsiveVSModSystem.cs`

- Defines client and server `ModSystem` entrypoints.
- Loads config.
- Registers Harmony patches.
- Registers network channels.
- Starts diagnostics commands.
- Detects incompatible mods before enabling ownership.
- Owns dispose/unpatch cleanup.

Expected classes:

- `ResponsiveVSClientModSystem`
- `ResponsiveVSServerModSystem`
- `ResponsiveVSCore`

### Config

`src/Config/ResponsiveVSConfig.cs`

- Defines config POCOs matching `architecture-design.md`.
- Stores defaults.
- Loads/stores `ModConfig/responsivevs.json`.
- Config changes require restart/reload unless a specific field is explicitly documented as live-reloadable.
- Modded inventory opt-in/out is not live-reloaded in v1.

Expected types:

- `ResponsiveVSConfig`
- `OwnedInventoryConfig`
- `ResponsiveDiagnosticsLevel`
- `ResponsiveVSConfigSystem`

### Network

`src/Network/ResponsiveNetwork.cs`

- Registers the `responsivevs` channel.
- Registers message types in the canonical order.
- Owns handshake state.
- Sends requests/results/snapshots.
- Exposes `IsOwnershipEnabled`.

Expected message files:

```text
src/Network/Messages/InventoryTransactionRequest.cs
src/Network/Messages/InventoryTransactionResult.cs
src/Network/Messages/InventorySnapshotRequest.cs
src/Network/Messages/InventorySnapshotResult.cs
src/Network/Messages/ResponsiveHandshakeHello.cs
src/Network/Messages/ResponsiveHandshakeResult.cs
src/Network/Messages/ResponsiveDiagToggle.cs
```

Rules:

- No owned transaction is sent until handshake accepted.
- If handshake/channel fails, ownership is disabled and vanilla packets are never suppressed.
- `ClientSessionId` is regenerated on reconnect.
- `ResponsiveHandshakeHello` and `ResponsiveHandshakeResult` both carry `ProtocolVersion`.
- Message type order cannot change inside a protocol version. If message order or shape changes, bump protocol version.

### Threading

`src/Threading/`

Expected files:

```text
MainThreadDispatcher.cs
ThreadAssert.cs
```

Responsibilities:

- Marshal network handler work onto the correct game main thread.
- Assert main-thread-only code paths during diagnostics.
- Keep inventory, GUI, pending-state, and preview-state access off background threads.

### Transactions

`src/Transactions/`

Owns transaction lifecycle and state machines.

Expected files:

```text
TransactionIds.cs
TransactionCoordinator.cs
PendingTransactionStore.cs
TransactionClassifier.cs
TransactionTypes.cs
SlotKey.cs
StackFingerprint.cs
SlotDeltaBuilder.cs
TransactionLifecycle.cs
```

Responsibilities:

- Allocate client transaction ids.
- Track one pending transaction per player client in v1.
- Reserve touched slot keys including mouse cursor.
- Classify actions as `Owned`, `FallbackVanilla`, `BlockedPending`, or `RejectedLocal`.
- Clear/timeout pending transactions.
- Request snapshots after timeout, impossible result application, or vanilla collision.
- Clear pending state on disconnect, GUI close, death, world unload, and ownership disable.

V1 rule:

- Implement per-player serialization first. Do not implement preview-chaining yet.
- Do not implement a queued pending state in v1. New input while pending is blocked, not queued.

### Diagnostics

`src/Diagnostics/`

Expected files:

```text
ResponsiveDiagnostics.cs
ResponsiveDiagCommand.cs
TransactionLogEntry.cs
PerfCounters.cs
```

Responsibilities:

- Register `.rvsdiag` and `/rvsdiag`.
- Log transaction summaries with `[RVS]`.
- Force at least `Basic` diagnostics in Phase 1 development builds.
- Record ownership classification, fallback reasons, before/after fingerprints, timing, and channel state.
- Record client wait-time histograms, pending-block duration, timeout rate, fallback rate, and actions-per-second during burst tests.
- Keep diagnostic code shared between client and server.

### Client

`src/Client/`

Expected folders:

```text
Client/Input/
Client/Lifecycle/
Client/Preview/
Client/Reconciliation/
Client/Patches/
```

#### Client/Input

Expected files:

```text
SlotGridInputAdapter.cs
DragGestureTracker.cs
GuiSlotResolver.cs
ClientIntentFactory.cs
```

Responsibilities:

- Patch/handle `GuiElementItemSlotGridBase` methods.
- Convert GUI input into transaction requests.
- Preserve GUI bookkeeping needed after skipping vanilla mutation.
- Track drag slot order.
- Route fallback actions to vanilla.
- Decide ownership once at gesture start and store the routing decision for the gesture duration.
- Ensure mutation guards honor a gesture that was explicitly routed to vanilla fallback.

#### Client/Lifecycle

Expected files:

```text
ClientLifecycleHooks.cs
GuiCloseObserver.cs
PlayerDeathObserver.cs
WorldUnloadObserver.cs
```

Responsibilities:

- Clear pending transactions and previews on disconnect, world unload, GUI close, and player death.
- Disable ownership and request fresh handshake/snapshots after reconnect.
- Treat late results from old `ClientSessionId` as stale.

#### Client/Preview

Expected files:

```text
PreviewStore.cs
PreviewRenderer.cs
PreviewCalculator.cs
ThrowawaySlotFactory.cs
MouseCursorPreviewPatch.cs
```

Responsibilities:

- Store preview stacks separately from real inventory slots.
- Render pending slot overlays.
- Patch the mouse cursor render path through `HudMouseTools` / `GuiElementPassiveItemSlot` for the `"mouse"` inventory.
- Compute simple previews by running real slot behavior on throwaway cloned slots.
- Fallback when clone/preview is unsafe.

Do not hand-code general merge behavior.

Source notes:

- Vanilla builds the mouse cursor UI in `Vintagestory.Client.NoObf.HudMouseTools.OnOwnPlayerDataReceived()` by composing a passive item slot named `"mouseSlot"` over `InventoryManager.GetOwnInventory("mouse")[0]`.
- The patch target should be passive-slot source/render resolution, not a broad render-loop patch.

#### Client/Reconciliation

Expected files:

```text
ResultApplier.cs
SnapshotApplier.cs
VanillaCorrectionObserver.cs
OwnershipState.cs
```

Responsibilities:

- Apply `SlotDelta` results with `ApplyingServerResult` guard.
- Clear previews on ack/reject/timeout.
- Keep preview visible over vanilla backing-slot corrections while pending.
- Request snapshots on mismatch or missing inventory.
- Disable ownership on channel loss.
- If a result arrives for a closed/disposed inventory, drop direct application, clear preview, and request snapshots for currently open owned inventories.

#### Client/Patches

Expected patch files:

```text
Patch_GuiElementItemSlotGridBase.cs
Patch_ClientInventoryMutationGuard.cs
Patch_ClientVanillaPacketGuard.cs
Patch_ClientMouseCursorRender.cs
```

Patch rules:

- GUI prefixes skip original only for owned actions.
- Mutation guard prevents direct client mutation for ownable paths.
- Packet guard must never suppress vanilla packets unless ownership is handshaken and the action is in pending state.

### Server

`src/Server/`

Expected folders:

```text
Server/Execution/
Server/Lifecycle/
Server/Validation/
Server/Snapshots/
Server/Patches/
```

#### Server/Validation

Expected files:

```text
ServerInventoryResolver.cs
InventoryAccessValidator.cs
RequestValidator.cs
```

Responsibilities:

- Resolve authority from connected `IServerPlayer`.
- Treat all request fields as untrusted.
- Resolve player, mouse, crafting, and open block entity inventories through server-side player/open-inventory state.
- Reject missing or inaccessible inventories before mutation.
- Validate slot indices and quantities.

V1 rule:

- Do not resolve arbitrary block entity inventories directly from client coordinates or ids.
- External inventories are valid only if `player.InventoryManager.GetInventory(id, out inv)` succeeds and `inv.HasOpened(player)` is true, or the inventory is a known own/player inventory class.
- Vanilla `PlayerInventoryManager.OpenedInventories` is derived from `InventoriesOrdered.Where(inv => inv.HasOpened(player))`; the authoritative open bit is `InventoryBase.openedByPlayerGUIds`, reached through `HasOpened(player)`.

#### Server/Lifecycle

Expected files:

```text
ServerPlayerStateStore.cs
ServerLifecycleHooks.cs
```

Responsibilities:

- Track per-player serialization state.
- Clear pending/server-side player state on disconnect, leave, death where relevant, and world unload.
- Reject or ignore late transactions from stale client sessions.

#### Server/Execution

Expected files:

```text
InventoryTransactionExecutor.cs
ActivateSlotExecutor.cs
MoveFlipExecutor.cs
DragExecutor.cs
CraftingOutputExecutor.cs
ServerDeltaCollector.cs
```

Responsibilities:

- Execute server-authoritative operations through vanilla/modded inventory methods.
- Register transaction-local delta collector before mutation.
- Pre-register source, target, mouse, and drag slots.
- Ignore returned vanilla packets as source of truth.
- Return accepted/rejected results with authoritative deltas.
- On `ServerError` after possible partial mutation, snapshot every touched inventory instead of returning best-effort deltas only.

Execution order:

1. Validate request.
2. Register delta collector.
3. Pre-register known touched slots.
4. Execute operation.
5. Read final stacks for collected slots.
6. Send result.

Source notes:

- Vanilla server activate handling already ignores the `InventoryBase.ActivateSlot()` return packet. `InventoryNetworkUtil.handleActivateInventorySlotPacket()` calls `targetInv.ActivateSlot(...)` for mutation and relies on dirty inventory sync/reverts separately.
- ResponsiveVS executors can ignore returned vanilla packets after calling server-side inventory methods. Returned packets are only client-to-server sync packet builders for vanilla optimistic client paths.
- Client-owned paths must intercept before `GuiElementItemSlotGridBase.SlotClick()` calls `inventory.ActivateSlot(...)`, because `SlotClick()` mutates locally and then invokes `SendPacketHandler` with the returned packet.

#### Server/Snapshots

Expected files:

```text
InventorySnapshotService.cs
SlotDeltaSerializer.cs
```

Responsibilities:

- Build full inventory snapshots for snapshot requests.
- Build targeted snapshots for touched inventories on reject.
- Serialize empty stacks consistently.

#### Server/Patches

Expected files:

```text
Patch_ServerDeltaCollection.cs
Patch_ServerInventoryDiagnostics.cs
```

Responsibilities:

- Hook dirty/slot modified paths only as needed for delta collection and diagnostics.
- Do not globally skip vanilla server inventory handling.
- Phase 1 must include a delta-collector spike in observe-only mode around vanilla execution.
- The spike must compare collected slots against vanilla dirty/full-update behavior for simple, shift, move, and crafting cases.

Delta collector decision:

- Primary hook: Harmony postfix on `InventoryBase.DidModifyItemSlot(ItemSlot slot, ItemStack extractedStack = null)`.
- Fallback hook: Harmony postfix on `InventoryBase.MarkSlotDirty(int slotId)` only for code paths that mark dirty without calling `DidModifyItemSlot`.
- Reason: vanilla item movement funnels normal slot mutation through `ItemSlot.ActivateSlot()`, `TryPutInto()`, `TakeOut()`, and `InventoryBase.ActivateSlot()`, which call `DidModifyItemSlot()`. Crafting has direct `dirtySlots.Add(...)` paths, so Phase 1 must prove whether `MarkSlotDirty` fallback is enough or whether crafting needs a narrow crafting-grid collector.
- Phase 3 is blocked until the Phase 1 observe-only collector proves complete touched-slot capture.

### Inventory

`src/Inventory/`

Shared inventory helper code.

Expected files:

```text
InventoryKind.cs
InventoryOwnershipPolicy.cs
InventoryIdUtil.cs
SlotAccess.cs
ItemStackPacketUtil.cs
```

Responsibilities:

- Classify inventory ids/classes.
- Apply config opt-in/out.
- Convert `ItemStack` to `Packet_ItemStack`.
- Compute stack fingerprints.
- Resolve mouse inventory id consistently.

### Crafting

`src/Crafting/`

Expected files:

```text
CraftingOutputGuard.cs
CraftingGridTransactionPolicy.cs
DragDistribution.cs
```

Responsibilities:

- Keep crafting output fallback until Phase 5.
- Implement full-output fit and craft-many rules in Phase 5.
- Implement drag-left distribution as the documented narrow exception.
- Reset or regenerate output leftover state after owned output transactions.

### FastCrafting

`src/FastCrafting/`

Adapt from `FastCraftingGrid`.

Expected files:

```text
CraftingRecipeIndex.cs
FindMatchingRecipePatch.cs
GridRecipeMatchesPrefilter.cs
CraftingBurstDiagnostics.cs
```

Rules:

- Integrate internally; do not require standalone FastCraftingGrid.
- Build world snapshots on main thread before background bitset build.
- Keep `GridRecipe.Matches` prefilter disabled by default.
- Build and consume the index on both client and server when that side performs crafting-grid matching.
- Use server `SaveGameLoaded` and client `LevelFinalize` as the initial prewarm triggers, matching the proven FastCraftingGrid prototype.
- Invalidate static index state on world unload/dispose.

### RuntimeData

`src/RuntimeData/`

Optional diagnostics and narrowly scoped caching for JSON-derived runtime hot paths.

Expected files:

```text
RuntimeDataDiagnostics.cs
JsonObjectHotPathPatch.cs
StackAttributePacketDiagnosticsPatch.cs
RuntimeDataCache.cs
RuntimeDataStats.cs
```

Rules:

- Diagnostics default off.
- Basic mode may collect aggregate counters and slow-call timing only.
- Trace mode may collect caller stacks, but only when explicitly enabled by config or command because stack capture is expensive.
- Do not globally replace `JsonObject`.
- Do not globally cache all indexed lookups.
- Do not assume modded `JsonObject` or `TreeAttribute` data is immutable unless the source is known asset-level data after load.
- Cache only proven hot conversions or known vanilla paths where invalidation rules are clear.
- Reset all static caches and counters on world unload/dispose.

Initial measurement targets:

- `JsonObject.AsObject<T>()` call counts and elapsed time by target type.
- high-volume `JsonObject.AsArray<T>()` calls when diagnostics are enabled.
- optional trace-only keyed lookup caller stacks for `JsonObject.this[string]`.
- serialized `ItemStack.Attributes` byte length when stack packets are created for inventory updates.
- top offenders reported through diagnostics counters, not normal logs.

Do not make this module part of inventory correctness. Runtime data stalls can widen prediction/correction windows, but the correctness fix remains transaction ownership.

### TimingGuards

`src/TimingGuards/`

Independent track for small FPS/focus/dt fixes.

Expected files:

```text
DeltaTimeClampPatch.cs
GuiFocusTimingPatch.cs
InteractionLeadDiagnostics.cs
```

Rules:

- Keep independent from inventory transaction ownership.
- Ship only narrow, testable clamps/debounces.
- Do not implement full world interaction replacement here.

### Compatibility

`src/Compatibility/`

Expected files:

```text
IncompatibleModDetector.cs
HarmonyIds.cs
PatchPriorityPolicy.cs
BetterHandbookProbe.cs
```

Responsibilities:

- Detect `ItemSyncFixes` and standalone `FastCraftingGrid`.
- Disable ownership with clear logs if hard-incompatible mods are active.
- Centralize Harmony priority/after/before ids.
- Add diagnostics around known direct callers such as BetterHandbook auto-fill.
- Patch or observe `GeneralPacketHandler.HandleInventoryDoubleUpdate` as a compatibility fix for vanilla fallback paths.

Confirmed vanilla compatibility bug:

- `GeneralPacketHandler.HandleInventoryDoubleUpdate()` reads `InventoryId1` and `InventoryId2`, but the second lookup currently calls `inventoryMgr.GetInventory(invId1, out invFound2)` again instead of `invId2`.
- `InventoryNetworkUtil.UpdateFromPacket(Packet_InventoryDoubleUpdate)` is prepared to apply either side depending on the inventory id, so the second lookup bug can skip the second inventory update when the ids differ.
- ResponsiveVS owned transactions should not depend on this path, but fallback and coexistence paths still benefit from a narrow patch.

### Tests

`tests/ResponsiveVS.Tests/`

Keep pure logic engine-free where possible.

Initial test targets:

- `DragDistribution`
- `StackFingerprint`
- `TransactionClassifier`
- `PendingTransactionStore`
- `CraftingRecipeIndex` bitset intersection
- packet/message version constants

Rules:

- Pure modules should not depend on `IWorldAccessor` unless unavoidable.
- If a helper needs Vintage Story types, isolate that dependency behind a small adapter so the core logic remains testable.

## Implementation Phases By Code

### Phase 0: Scaffold And Audit

Create project skeleton, config, package script, and empty module folders.

Code deliverables:

- `ResponsiveVS.csproj`
- `modinfo.json`
- `scripts/package.sh`
- root client/server mod systems
- config loader
- incompatible mod detector stub
- main-thread dispatcher/assertion helpers
- static state reset hooks for dispose/world unload
- verified `requiredOnClient` / `requiredOnServer` semantics documented in README

No behavior change.

### Phase 1: Transaction Diagnostics

Build the shared channel and diagnostics without owning behavior.

Code deliverables:

- network channel and message classes
- handshake hello/result
- protocol version constants and mismatch handling
- transaction id/session id allocator
- ownership classifier
- `.rvsdiag` and `/rvsdiag`
- GUI and mutation patches in observe-only mode
- vanilla packet observer
- runtime data hot-path counters wired behind diagnostics config
- delta-collector spike in observe-only mode
- lifecycle hooks for disconnect/world unload/player leave where available

Acceptance:

- every tested action logs owned/fallback classification
- no vanilla behavior changes
- no vanilla packet is suppressed
- protocol mismatch disables ownership cleanly
- delta collector proves it can see the expected touched slots before Phase 3
- client wait time, pending block duration, timeout rate, fallback rate, and burst action rate are logged

### Phase 2: Dry-Run Preview

Build preview rendering and reservation without changing final behavior.

Code deliverables:

- preview store
- slot preview renderer
- mouse cursor preview patch
- throwaway slot preview calculator
- pending overlay
- dry-run reservation logic

Acceptance:

- preview never mutates real inventory
- vanilla still completes the action after preview clears
- cursor and slot preview agree visually
- GUI close, disconnect, death, and world unload clear preview state

### Phase 3: Owned Simple Transactions

Own simple activate-style clicks.

Code deliverables:

- client owned send path
- mutation guard active for owned simple clicks
- server request validation
- server activate-slot executor
- delta collector
- result applier
- snapshot request/result fallback

Entry gate:

- Phase 1 delta collector spike has proven complete touched-slot capture for simple click paths.

Acceptance:

- left/right/wheel simple clicks work in hotbar/backpack/vanilla chest/quern
- rapid same-slot spam creates no fake items
- handshake loss falls back to vanilla and never suppresses packet

### Phase 4: Multi-Slot Inventory Operations

Own broader inventory operations.

Code deliverables:

- shift-click executor
- move/flip executor
- hotbar/number-key operation support
- expanded delta collection across player inventory/hotbar/backpack/mouse

Acceptance:

- full inventory, unstackable, and shift-transfer cases return complete deltas
- no accepted transaction has missing changed slots
- mid-execution `ServerError` returns snapshots for touched inventories

### Phase 5: Crafting And Drag

Own crafting grid and output.

Code deliverables:

- drag gesture transaction path
- drag-right server executor
- drag-left distribution helper
- crafting output guard/executor
- FastCrafting integration

Acceptance:

- click-drag is preview-only before release
- craft output consumes ingredients only after accepted server output transfer
- known laggy recipes meet p99 target after FastCrafting integration

### Phase 5B: Runtime Data Hot-Path Diagnostics

This phase may run before or after Phase 5 if logs point at JSON-derived or attribute-derived stalls.

Code deliverables:

- `JsonObject.AsObject<T>()` diagnostics patch
- trace-only `JsonObject` keyed lookup diagnostics
- stack attribute packet-size diagnostics
- command/counter output for top runtime-data offenders
- no behavior-changing cache by default

Acceptance:

- diagnostics can identify repeated dynamic JSON/attribute conversions without changing gameplay
- default config produces no log spam
- trace mode clearly warns when caller-stack capture is enabled
- packet attribute byte sizes are available for inventory update analysis

Optimization entry gate:

- only add `RuntimeDataCache` behavior after diagnostics identify a stable hot path and a clear invalidation rule

### Phase 6: Modded Inventory Opt-In

Expose config opt-in and diagnostics for modded inventories.

Code deliverables:

- class-name and id-prefix opt-in checks
- per-inventory fallback reason logs
- compatibility diagnostics for direct callers

Acceptance:

- unknown modded inventories fallback by default
- opted-in inventories can be disabled without removing mod

### Phase 7: Timing Guards Track

Implement isolated timing guard patches if tests support them.

Code deliverables:

- dt clamp patch
- focus/refocus debounce patch
- interaction lead diagnostics

Acceptance:

- no dependency on inventory ownership
- can be disabled separately if it conflicts

## First Code To Write

Start with these files in order:

1. `ResponsiveVS.csproj`, `modinfo.json`, `scripts/package.sh`
2. `src/ResponsiveVSModSystem.cs`
3. `src/Config/ResponsiveVSConfig.cs`
4. `src/Threading/MainThreadDispatcher.cs`
5. `src/Diagnostics/ResponsiveDiagnostics.cs`
6. `src/Network/ResponsiveNetwork.cs`
7. `src/Network/Messages/*.cs`
8. `src/Transactions/TransactionTypes.cs`
9. `src/Transactions/PendingTransactionStore.cs`
10. `src/Compatibility/IncompatibleModDetector.cs`

Do not start behavior-changing patches until the channel, config, diagnostics, and compatibility detector compile.

## Resolved Code Research Items

These decisions close the previous open research list.

- Mouse cursor render patch point: patch passive item-slot rendering/source resolution for `HudMouseTools`' `"mouseSlot"` composer, which displays `InventoryManager.GetOwnInventory("mouse")[0]`.
- Open external inventory proof: require server-side `GetInventory(id, out inv)` plus `inv.HasOpened(player)` for external/block-entity inventories. Player-owned inventories are handled by known own inventory ids/classes.
- `HandleInventoryDoubleUpdate`: confirmed bug. The second lookup uses `invId1` instead of `invId2`; keep a narrow compatibility patch for vanilla fallback.
- `InventoryBase.ActivateSlot` return values: safe to ignore on the server-owned executor path. Vanilla server activation already ignores them; they exist for client-to-server vanilla packet generation.
- Delta collection hook: use a transaction-local collector around `InventoryBase.DidModifyItemSlot`, with a `MarkSlotDirty` fallback for direct dirty paths. Phase 1 observe-only diagnostics must prove completeness before any owned mutation phase can ship.
- Throwaway preview slots: only use throwaway cloned slots for known-safe vanilla slot classes (`ItemSlot`, `ItemSlotSurvival`, and basic generic inventory slots) and only when all involved slots can be represented without custom constructor/delegate state. Custom slot subclasses, output slots, creative slots, per-player/backpack content slots, and modded slot types fallback unless explicitly supported.
- Network handler threading: TCP custom packet handlers reach the server through `ServerMain.ProcessMain()` / `HandleClientPacket_mainthread()`, and client TCP packets are enqueued through `game.EnqueueMainThreadTask(...)`; UDP custom handlers can run from UDP tick paths. ResponsiveVS still treats all custom channel handlers as unspecified and marshals inventory/GUI work through `api.Event.EnqueueMainThreadTask(...)`.
- Config live reload: no gameplay-affecting config live reload in v1. Diagnostics may be toggled by command; ownership policy, compatibility, and FastCrafting settings require restart or world reload.

## Hard Guardrails

- Never suppress a vanilla packet before accepted handshake.
- Never suppress a vanilla packet if the channel is disconnected, unhandshaken, or protocol-mismatched.
- Never mutate real client inventory in an owned path before server result.
- Never trust client inventory ids, slot ids, or quantities.
- Never use time-window packet suppression as correctness.
- Never ship with `ItemSyncFixes` or standalone `FastCraftingGrid` active alongside ownership.
- Never hand-code generic item merge behavior for preview.
- Never accept a transaction if changed-slot capture is incomplete.
- Never read or write live inventory or GUI state off the game main thread.
- Never leave static state alive across world unload/dispose.
