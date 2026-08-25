# ResponsiveVS Existing Mod Adaptation Map

This document compares the two current prototype mods:

- `FastCraftingGrid`
- `ItemSyncFixes`

The goal is to decide what should be adapted into a larger `ResponsiveVS` replacement layer, what should stay as diagnostics, and what should be treated as a band-aid that only compensates for vanilla behavior after the race already happened.

## Short Version

`FastCraftingGrid` contains real architecture worth carrying forward:

- Replace repeated recipe scans with an indexed matcher.
- Cache matching results for identical grid state.
- Measure crafting bursts so one visible lag event can be tied to recipe matching, output generation, packet moves, or excessive caller volume.

`ItemSyncFixes` contains a mix of useful discoveries and transitional fixes:

- The diagnostics are valuable and should be generalized.
- Crafting drag preview proves that delaying client mutation until the gesture is complete is viable.
- Crafting output full-output handling is useful as a correctness rule.
- Stale correction suppression, external-slot pending gates, and delayed broad-content deferrals are mostly symptom control because they happen after vanilla already allowed speculative local mutation.

If these are combined into one drop-in replacement, the main direction should not be "suppress bad packets better." It should be "own the interaction transaction earlier, predict only display state, execute vanilla/modded logic on the server, then reconcile the client from authoritative results."

## FastCraftingGrid

### What It Fixes

`FastCraftingGrid` targets crafting UI stalls caused by expensive recipe lookup. It patches:

- `InventoryCraftingGrid.FindMatchingRecipe`
- `GridRecipe.Matches(IPlayer, IWorldAccessor, ItemSlot[], int)`
- `InventoryCraftingGrid.ActivateSlot` for burst diagnostics only
- `InventoryCraftingGrid.TryMoveItemStack` for burst diagnostics only

The biggest fix is replacing broad per-edit recipe scanning with a pre-expanded recipe index:

1. Build a code-to-recipe index from `world.FastSearchRecipesByIngredient`.
2. Expand broad wildcard/tag ingredients once at world ready.
3. Store recipe buckets as dense `ulong[]` bitsets.
4. Gather candidates by bitwise `AND` across occupied input item-code buckets.
5. Run vanilla `GridRecipe.Matches` only on the reduced candidate set.
6. Generate output with vanilla `GridRecipe.GenerateOutputStack`.

It also caches the last recipe result per crafting grid using a grid fingerprint based on:

- slot index
- collectible id
- stack size
- stack attributes hash

### What Is Solid

The indexed matcher is a real fix. It removes repeated large scans from a hot UI path and preserves vanilla recipe/output behavior after candidate narrowing.

The bitset change is especially important. Earlier candidate-set intersection used arrays and repeated reference scans. That still produced visible 300-700ms freezes in testing even when direct matcher logs looked smaller. The bitset version reduced candidate gathering to fixed word operations and made the tested problem recipes feel instant.

The grid fingerprint cache is also worth keeping. Vanilla and modded callers can ask the same crafting grid to rematch several times during one visible UI change. Reusing the previous match for an unchanged grid state is a normal cache, not a sync workaround.

The burst logger is worth adapting. It captures visible interaction bursts, not only individual method timings. That matters because one user-visible freeze can be caused by many small calls in one frame.

### What Is A Band-Aid Or Needs Narrowing

The global `GridRecipe.Matches` prefilter is useful, but it is broad. It affects callers outside the crafting grid, including handbook conflict-selection code. It should either:

- be scoped to known hot callers,
- be optional,
- or be kept as a correctness-validated prefilter with diagnostics.

The prefilter is still safe in concept because it only rejects impossible matches and lets vanilla handle possible matches. The risk is compatibility with unusual recipes that rely on custom side effects or nonstandard matching assumptions.

The async index build currently reads `world.FastSearchRecipesByIngredient` and `world.Collectibles` inside `Task.Run`. A combined mod should snapshot vanilla references on the main thread, then build derived bitsets in the background from that snapshot.

### What ResponsiveVS Should Adapt

Adapt directly:

- recipe-id assignment in vanilla `world.GridRecipes` order
- item-code recipe buckets
- bitset candidate intersection
- grid fingerprint cache
- vanilla fallback on suspicious empty candidates
- diagnostic burst aggregation

Adapt with changes:

- global `GridRecipe.Matches` prefilter should become a scoped or config-controlled optimization
- async prewarm should snapshot world data on the main thread first
- diagnostics should include caller classification, total burst wall time, and frame-visible delay if possible

Avoid:

- treating recipe optimization as an inventory sync fix. It improves responsiveness, but it does not solve ghost items by itself.

## ItemSyncFixes

### What It Fixes

`ItemSyncFixes` targets client/server inventory desync symptoms. It patches:

- `GuiElementItemSlotGridBase.SlotClick`
- `GuiElementItemSlotGridBase.OnMouseDownOnElement`
- `GuiElementItemSlotGridBase.OnMouseMove`
- `GuiElementItemSlotGridBase.OnMouseUp`
- `GuiElementItemSlotGridBase.RenderInteractiveElements`
- `InventoryNetworkUtil.UpdateFromPacket` for single updates
- `InventoryNetworkUtil.UpdateFromPacket` for double updates
- `InventoryNetworkUtil.UpdateFromPacket` for full contents
- `PlayerInventoryNetworkUtil.UpdateFromPacket`
- `InventoryNetworkUtil.PauseInventoryUpdates`
- `InventoryBase.SlotsFromTreeAttributes`
- `InventoryNetworkUtil.HandleClientPacket`
- `ServerMain.SendPacket` for diagnostics
- `InventoryCraftingGrid.ActivateSlot`
- `ItemSlotCraftingOutput.TryPutInto`
- `ItemSlotCraftingOutput.FlipWith`
- `InventoryCraftingGrid.FindMatchingRecipe`

The mod's main families are:

1. Diagnostics around clicks, incoming corrections, and server packets.
2. Client prediction tracking and stale confirmation suppression.
3. External storage pending-slot protection.
4. Crafting grid drag preview.
5. Crafting output all-or-resync behavior.
6. Toolsmith/malformed recipe crash suppression around missing output results.

### What Is Solid

#### Diagnostics

The `isfdiag` tracing is worth keeping and generalizing. It records:

- click start/end
- target and mouse stack before/after
- incoming `InventoryUpdate`
- incoming `InventoryDoubleUpdate`
- incoming `InventoryContents`
- server receive of activate/move/flip packets
- server outgoing inventory packets
- recent-click correlation

This is the right kind of tooling for ResponsiveVS. The next version should make it transaction-aware instead of slot-only.

#### Crafting Drag Preview

The crafting grid drag preview is the clearest proof that vanilla's "mutate every crossed slot immediately" behavior can be replaced.

Current behavior:

- records slots during drag
- renders a preview stack
- avoids mutating real grid slots while the mouse is still moving
- commits the drag on mouse up
- optionally paces commits across callbacks

This is conceptually correct for ResponsiveVS, but the current implementation still commits by replaying vanilla `SlotClick` calls or direct local transfer calls. That means it reduces the race window but does not fully replace the transaction model.

#### Crafting Output Full-Output Rule

The crafting output guard has a useful invariant:

When moving from a crafting output slot, either the full recipe output fits and ingredients are consumed, or the output/sink slots are dirtied for resync.

That is better than allowing partial output moves to leave `ItemSlotCraftingOutput` leftover state half-valid on one side.

Useful pieces:

- `BeginCraftIfNeeded`
- `EndCraftIfNeeded`
- `FullOutputFits`
- `CraftManyFullOutputsOnly`
- `ResetLeftoverState`
- clearing errored output state on known malformed recipe exceptions

The final combined mod should avoid reflection-heavy direct field access where possible, but the invariants are good.

#### Double Update Handler Fix

The patch around `GeneralPacketHandler.HandleInventoryDoubleUpdate` is worth keeping for vanilla fallback compatibility. The decompiled vanilla path reads both `InventoryId1` and `InventoryId2`, but the second lookup uses `invId1` again, so the second inventory can miss its update when the ids differ. ResponsiveVS owned transactions should not depend on this path, but fallback/coexistence paths still benefit from a narrow fix.

### What Is Mostly A Band-Aid

#### Stale Confirmation Suppression

`ClientPredictionSuppressor` records predicted slot states after vanilla already changed local inventory. Later, it suppresses incoming server packets if they look like delayed confirmations of states the client already passed through.

This can make flicker better, but it is not a full solution:

- it relies on stack fingerprints and age windows
- it cannot prove packet causality
- it can only reason per slot
- it does not stop the client from chaining more actions from speculative state
- it is vulnerable to real server corrections that look like old predicted states

ResponsiveVS should replace this with transaction ids/revisions where possible. The useful part is the fingerprinting and recent-action diagnostics, not the suppression policy.

#### External Storage Pending Gate

`ClientExternalClickGate` records a pending external slot and briefly defers broad `InventoryContents` updates for that slot. It also preserves pending slots during `InventoryBase.SlotsFromTreeAttributes`.

This improved visible flicker in external storage, but it is still post-hoc:

- vanilla already locally mutated the slot
- broad tree/full-content syncs are delayed, not prevented by a real transaction boundary
- direct updates still race with local predictions
- the current `HasActivePending` implementation always returns `false`, so it does not actually block repeated clicks before confirmation

For ResponsiveVS, this should become an interaction gate before local mutation:

1. assign a client transaction id
2. show a visual pending state
3. optionally allow local display prediction only
4. block or queue conflicting mutations until ack/reject/timeout
5. reconcile from server state

#### Pause Queue Coalescing

The paused update coalescer keeps only the latest queued update per slot when vanilla unpauses inventory updates.

This is reasonable as a compatibility fallback, but it is still cleaning up a queue after vanilla has allowed stale intermediate states to accumulate. ResponsiveVS should avoid building the stale queue in the first place for owned interactions.

#### Server Right-Click Place-One Replay

`TryHandleRightClickFromMouseStack` intercepts right-click activate packets and replays `targetInv.ActivateSlot` using the server mouse slot as the source.

This may help one right-click path, but it is narrow and duplicates vanilla behavior from the outside. In a replacement system, right-click place-one should be one transaction type handled by the same server-owned operation path as left click, drag, split, shift move, and number-key swap.

#### Malformed Recipe Crash Suppression

Suppressing `Missing or errored output result for recipe` prevents disconnects/crashes from bad mod recipes, but it does not fix the recipe. This should be kept as a defensive compatibility option, not considered part of the sync solution.

### What ResponsiveVS Should Adapt

Adapt directly:

- `isfdiag` style click and packet tracing
- stack fingerprint helper for diagnostics
- recent action correlation
- drag preview UX
- full-output crafting invariant
- known malformed recipe guard as optional compatibility protection
- double-update path verification/fix if confirmed

Adapt with redesign:

- prediction suppression becomes transaction reconciliation
- external pending gate becomes pre-mutation transaction ownership
- pause queue coalescing becomes fallback only
- drag preview commit becomes one operation packet, not many local `SlotClick` replays where possible
- crafting output guards become part of the server authoritative craft transaction

Avoid:

- broad suppression based only on stack equality and time windows as the primary correctness mechanism
- treating block entity tree sync preservation as a core model
- adding more per-slot special cases without transaction ids or revision checks

## Combined Drop-In Replacement Direction

A single replacement mod should combine both prototypes under one interaction model:

1. UI event enters ResponsiveVS before vanilla mutates slots.
2. ResponsiveVS builds a transaction:
   - transaction id
   - player id
   - target inventory id
   - source inventory id when relevant
   - slot ids
   - operation type
   - mouse button/modifiers
   - client-known inventory revisions
   - optional expected source/target stack fingerprints for diagnostics
3. Client renders preview/pending state, not uncontrolled inventory mutation.
4. Server executes the operation through vanilla/modded inventory methods.
5. Server returns an ack/reject plus authoritative slot deltas.
6. Client applies only the result for the newest accepted transaction, or rolls back display preview on reject/timeout.

This is the difference between a patch set and a replacement layer.

## Candidate Transaction Types

Inventory transactions:

- left click slot
- right click slot
- shift click
- number key/hotbar swap
- mouse wheel transfer
- flip stacks
- explicit move stack
- click drag right distribution
- click drag left redistribution
- crafting output take one
- crafting output craft many
- external storage click
- player inventory click
- mouse cursor correction

World interaction transactions:

- block place
- block break start/step/stop
- block interact start/step/stop
- held item use start/step/stop
- entity interact

Crafting-grid transactions should be the first implementation target because they are the most reproducible and already have working prototype parts.

## Superseded Phase Sketch

This section is a historical sketch from the prototype comparison. The canonical implementation schedule is now `architecture-design.md` phases 0-7. Use this section only for rationale about how the prototype pieces map into the final architecture.

## Earlier ResponsiveVS Phase Sketch

### Phase 1: Merge Diagnostics

Create a shared diagnostic layer:

- transaction start/end
- slot before/after
- packet send/receive
- server processing time
- ack/reject reason
- frame-time visible delay if accessible
- recipe match burst stats from FastCraftingGrid

This phase should be low risk and gives test users useful output even for issues the author cannot reproduce.

### Phase 2: Own Crafting Grid Drag

Replace the current ItemSyncFixes drag preview with a cleaner system:

- preview during drag
- one logical transaction on release
- server computes actual slot changes
- client applies server result
- FastCraftingGrid supplies fast recipe refresh after the result

If vanilla packet formats cannot carry one full drag transaction cleanly, a custom mod packet can be used while still invoking vanilla/modded methods on the server.

### Phase 3: Own Crafting Output

Move crafting output into explicit transactions:

- take output
- shift craft many
- output-to-slot move
- hotbar/number-key output swap

Rules:

- full recipe output must fit before ingredient consumption
- ingredients are consumed only on server-confirmed output transfer
- output leftover state is reset or regenerated after each transaction
- malformed recipe output can fail safely without disconnecting the player

### Phase 4: Own External Inventory Clicks

Move chest/quern/machine clicks from optimistic mutation to pending display:

- local preview can be rendered, but real slot state should not be treated as owned until server ack
- repeated conflicting clicks can be queued or rejected locally until ack
- `InventoryContents` and block entity tree sync become normal authoritative state, not something to hide with time windows

This should replace the external pending gate.

### Phase 5: Integrate Fast Crafting

Fold FastCraftingGrid into the same mod:

- keep indexed matcher enabled by default
- keep diagnostics off by default
- make global `GridRecipe.Matches` prefilter configurable
- make index build main-thread snapshot plus background derived build
- expose stats in `/rvsdiag` or similar

This matters because sync fixes can feel worse if every transaction triggers expensive recipe scans.

### Phase 6: World Interaction Sanity

Only after inventory/crafting:

- inspect frame-driven world interaction polling
- clamp or normalize unsafe `dt` cases
- add interaction rate/replay guards
- compare client `UsingCount` and server step replay behavior
- avoid changing block/item/entity mod APIs unless necessary

The previous `ButterflyFix` work supports this class of issue, but inventory/crafting has clearer reproduction and should come first.

## Compatibility Rules

ResponsiveVS should not reimplement recipe or inventory semantics from scratch.

It should:

- own timing and transaction boundaries
- call vanilla/modded `ActivateSlot`, `TryPutInto`, `TryTransferAway`, `GridRecipe.Matches`, and `GenerateOutputStack` where appropriate
- add explicit result/ack handling around those calls
- preserve server authority
- make client preview visual until accepted

It should avoid:

- making the client authoritative
- relying on packet time windows for correctness
- suppressing corrections unless tied to a known transaction id
- hardcoding specific mod inventory classes unless used as temporary diagnostics

## What To Copy First

Best first code to extract/adapt:

1. `FastCraftingGrid` bitset `CraftingRecipeIndex`.
2. `FastCraftingGrid` grid fingerprint cache.
3. `FastCraftingGrid` burst diagnostics.
4. `ItemSyncFixes` diagnostic command and stack fingerprint helpers.
5. `ItemSyncFixes` crafting drag preview rendering and slot collection logic.
6. `ItemSyncFixes` crafting output full-output fit checks.

Code to leave behind or rewrite:

1. `ClientPredictionSuppressor` suppression policy.
2. `ClientExternalClickGate` time-window contents deferral.
3. direct replay of many `SlotClick` calls as drag commit.
4. broad reflection patches that exist only to work around vanilla private state.

The final goal is a mod that makes the UI feel responsive by previewing instantly but mutating authoritatively, instead of mutating optimistically and hiding the corrections afterward.
