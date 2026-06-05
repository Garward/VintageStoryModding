# Item Sync Fixes

Item Sync Fixes targets a few specific Vintage Story inventory desync symptoms. The main goal is to reduce temporary fake or ghost items while keeping the server authoritative.

The mod does not rewrite inventory networking. It narrows in on the paths that have produced reliable symptoms during testing.

## Targeted Symptoms

1. Crafting output can leave fake items after partial shift crafts or output slot moves.
2. Rapid clicking or dragging through the crafting grid can make items move late, flicker back, briefly duplicate on the client, or appear fake until the next normal click reconciles the UI.
3. Hotbar and backpack slots can receive delayed server confirmations after the client already predicted the same final state, causing visible snapback.
4. Right click place one and left click stack placement into normal or external inventories can flicker while the client waits for the server.

## What It Fixes

### Crafting Output

Crafting output slots have special rules for shift crafting, output moves, and slot flips. Partial output transfers can leave the client and server disagreeing about whether the output was fully crafted, partially moved, or should be synced again.

This mod makes the targeted crafting output moves behave as all or resync. For multi craft output moves, it accepts only full recipe output transfers. If the full output does not fit, it marks the output and sink slots dirty so the server can send a clean state instead of leaving a leftover ghost state.

The patched paths are:

1. Shift clicking crafting output.
2. Moving crafting output into another slot.
3. Output slot flip and number key style moves.

### Stale Queued Corrections

Vintage Story pauses some single slot inventory updates during drag interactions. When the pause ends, vanilla replays every queued server correction in order. During fast crafting grid or mouse slot interactions, many queued updates are stale intermediate states.

This mod keeps only the latest queued correction per slot before vanilla applies the flush. The client still ends on the latest server state, but it skips intermediate corrections that can no longer be correct.

Mouse cursor slot corrections are treated as authoritative. If a server mouse slot update arrives while the mouse inventory is paused, the mod applies that correction immediately and drops older queued mouse slot corrections.

### Delayed Self Confirmations

After the client predicts a hotbar or backpack action and the server accepts that same resulting slot state, the server can still send an exact delayed confirmation back to the same client. That can make the client appear to bounce through old states.

This mod suppresses only exact matching self confirmations for hotbar and backpack slots. Mismatched updates still apply.

### External Storage

External storage clicks are still predicted by vanilla. Rapid repeated clicks can chain local predictions before the server confirms the previous chest, quern, or machine slot change.

The current storage pass keeps a short client side pending record after a predicted click changes an external slot. That record protects the recently changed slot from broad block entity tree corrections until a direct server confirmation or timeout resolves it.

Broad `InventoryContents` packets can be deferred briefly for a pending external slot. Direct `InventoryUpdate` and `InventoryDoubleUpdate` packets still apply immediately. If a direct slot confirmation arrives during the defer window, the broad contents correction for that pending slot is dropped. If no direct confirmation arrives, the deferred contents correction applies.

The current pass also preserves pending external slots during client side `InventoryBase.SlotsFromTreeAttributes`. This covers `InventoryGeneric`, vanilla `InventoryQuern`, and other vanilla storage inventories that use the shared slot tree restore helper.

### Crafting Grid Drag Preview

Vanilla mutates real client inventory slots during click drag through the crafting grid, then catches up with server corrections later. In multiplayer this can create visible stack growth or fake items while the drag is still in progress.

This mod changes crafting grid drag into a preview. The client records the selected slots while dragging and applies the real slot clicks on mouse up. Right drag previews one item per slot. Left drag previews vanilla redistribution, including the leftover amount on the final selected slot.

## Boundaries

1. The mod does not make the client authoritative.
2. The client still applies the latest server correction for each slot.
3. Mouse cursor server corrections are not suppressed by the prediction suppressor.
4. The crafting output fix only targets crafting output movement paths. It does not change normal recipe matching.
5. The server echo suppression does not touch crafting grid, mouse slot, creative inventory, or recipe output updates.
6. Broad full inventory contents packets may defer a currently pending external slot for a very short window so direct slot confirmation can win.
7. Client side block entity tree updates preserve currently pending slots while `InventoryBase.SlotsFromTreeAttributes` restores tree state.
8. This is an attempt to reduce observed fake or ghost client item symptoms in these targeted paths, not a guarantee against every inventory race.

## Current Findings

### Vanilla External Storage Path

The normal click path for a chest, quern, or machine inventory is:

1. `GuiElementItemSlotGridBase.SlotClick` mutates the client slot immediately for responsiveness.
2. The GUI sends an inventory operation packet.
3. For block entities, `GuiDialogBlockEntity.DoSendPacket` wraps the inventory packet in a block entity packet for the target position.
4. The server block entity receives the packet and forwards normal inventory packets to `Inventory.InvNetworkUtil.HandleClientPacket(...)`.
5. The server applies the move against the authoritative inventory.
6. Dirty inventory slots are sent later by the server inventory tick, roughly every 30 ms, or a broader full inventory contents packet is sent when the server thinks the client operated on stale state.
7. The client applies `InventoryUpdate`, `InventoryDoubleUpdate`, or `InventoryContents` corrections.

This means the client is always predicting ahead of the server. There is no per click handshake that waits until one exact operation has been accepted before the next local click can run.

### Likely Remaining Vanilla Causes

The remaining vanilla style flicker is most likely one of these packet paths:

1. `InventoryContents`: broad resyncs are the strongest snapback signal. Repeated full contents packets point at stale or missing `lastChanged` tracking for that inventory.
2. `InventoryDoubleUpdate`: right click place one touches both the target slot and the mouse cursor slot. If those two sides do not apply together, the client can briefly think the cursor stack or target stack is wrong.
3. `InventoryUpdate`: normal delayed dirty slot confirmation can still visibly replay an older state after the client already predicted a newer one.
4. `PauseInventoryUpdates`: vanilla pauses some single slot updates during drag gestures, but the first click can happen before the pause is set, and the pause does not cover every contents, double update, or block entity sync path.

### Kinematics Result

VintageKinematics machines amplified the same class of issue because many machine inventories called `MarkDirty(true)` from `SlotModified`. That caused an extra broad block entity sync and redraw path on every stack change, on top of the normal inventory dirty slot sync.

That pattern was changed in Kinematics 1.3.4 to mark the chunk modified for persistence instead of forcing a full block entity sync for every inventory only slot change. This made those machines feel closer to vanilla storage, but it did not remove the remaining vanilla prediction and correction race.

### Assumptions To Watch

1. The client assumes its local predicted move is temporarily valid until the server proves otherwise.
2. A fake item can be entirely visual on the client. The server can remain correct while the client cursor or slot still chains later actions from stale predicted state.
3. The server assumes a stale `TargetLastChanged` means the client should receive a full inventory correction instead of applying the requested operation.
4. Vanilla assumes queued or delayed corrections will eventually converge, even if intermediate visual states flicker.
5. Block entity inventories assume the wrapped packet path plus later inventory dirty slot sync is enough to reconcile the GUI.
6. Any mod that sends extra `MarkDirty(true)` block entity syncs during pure inventory moves can amplify the vanilla race.

### Minor Vanilla Patch Review

A later 1.22.x minor patch changelog did not list a direct fix for this desync class. The closest adjacent fixes were arrow key inventory crashes, shelf placement with partially full jugs, and emptying liquid containers onto the ground.

After refreshing the decompiled source, these sync path observations still appeared unchanged:

1. `GuiElementItemSlotGridBase.OnMouseDownOnElement` still calls `SlotClick(...)` before setting `PauseInventoryUpdates`.
2. `InventoryNetworkUtil` still sends full `InventoryContents` when `lastChangedSinceServerStart > lastChangedClient`, and activate slot packets still rely on `TargetLastChanged`.
3. `GeneralPacketHandler.HandleInventoryDoubleUpdate` still appears to fetch `invId1` for the second inventory lookup when `invId1 != invId2`. If the decompile is accurate, this remains suspicious for cursor plus target updates.

## Install Notes

Install this mod on both the client and the server. The public package is marked as required on both sides so servers using it should make clients install it too.

Client install enables queued correction coalescing and crafting grid drag preview.

Server install enables hotbar and backpack exact self echo suppression.

The two pieces are technically independent, but the published package is intentionally not optional because one side gives only half of the intended behavior.

## Test Notes

In testing, the client coalescer dropped large numbers of stale queued crafting grid corrections while preserving the latest server state. The separate probe build reported no inventory packet pollution and no UI to packet inventory mismatch during successful test runs.

The storage focused pass intentionally removed the experimental broad `lastChangedSinceServerStart` sequence patch. That path was too invasive and did not match the clarified symptom: fake client items rather than real server duplication.

Observed probe examples:

1. 247 queued paused updates observed, 156 stale queued updates dropped in one crafting grid stress test.
2. 0 outgoing creative inventory actions during the successful test run.
3. 0 UI to packet inventory mismatches during the successful test run.

## Diagnostic Logging

The diagnostic classifier is off by default.

Enable it on the client:

```text
.isfdiag on
```

Enable it on the server:

```text
/isfdiag on
```

Disable it with:

```text
.isfdiag off
/isfdiag off
```

The diagnostic log prefix is:

```text
[ISFDiag]
```

The classifier records:

1. Client slot clicks, including target inventory, slot, mouse button, modifiers, target stack before and after, and mouse stack before and after.
2. Incoming client `InventoryUpdate`.
3. Incoming client `InventoryDoubleUpdate`.
4. Incoming client `InventoryContents`, with detail for recently clicked or mismatching slots and a summary for the whole contents packet.
5. Server received activate, move, and flip inventory packets, including `TargetLastChanged` vs server `lastChangedSinceServerStart`.
6. Server outgoing inventory update, double update, and full contents packets where visible through `ServerMain.SendPacket`.

Recommended repro matrix:

1. Vanilla chest: left click a full stack into a slot 10 to 20 times.
2. Vanilla chest: right click place one into a slot 10 to 20 times.
3. Vanilla quern: repeat both tests.
4. Player inventory only: repeat both tests.
5. Crafting grid: drag a stack through recipe slots quickly, then release.

The goal is to classify each visible flicker by the packet that caused the correction:

1. `InventoryContents` means chase stale `TargetLastChanged` or broad full resyncs.
2. `InventoryDoubleUpdate` means chase cursor plus target atomic application.
3. `InventoryUpdate` means chase delayed stale dirty slot confirmation.
4. No inventory packet at flicker time means look for block entity tree sync or rendering and UI only causes.

## Short ModDB Description

Item Sync Fixes is my attempt to fix specific inventory desync symptoms in Vintage Story, including temporary fake or ghost client items.

It targets crafting output ghosts, stale queued client corrections during rapid crafting grid and cursor interaction, delayed hotbar and backpack self confirmations from the server, external storage flicker, and crafting grid drag prediction.

It does this by making targeted crafting output moves all or resync, coalescing queued client corrections to the latest correction per slot, applying mouse slot corrections authoritatively, preserving pending external slots across shared inventory tree restores, suppressing only exact matching hotbar and backpack echoes on the server, and changing crafting grid click drag into a preview that commits on mouse up.

It does not make the client authoritative.
