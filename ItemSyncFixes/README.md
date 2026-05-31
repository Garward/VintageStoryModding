# Item Sync Fixes

Item Sync Fixes is my attempt to fix specific Vintage Story inventory desync symptoms, including the temporary fake/ghost item behavior that can show up while rapidly interacting with inventories. It is intentionally narrow: it does not try to rewrite inventory networking or make the client authoritative.

The targeted symptoms are:

1. Crafting output ghost/fake items caused by partial shift-crafts and output-slot moves.
2. Rapid clicking or dragging through crafting-grid and cursor interactions can make items feel like they move late, flicker backward, briefly duplicate client-side, or appear as fake/ghost items until the next normal click forces reconciliation.
3. Moving stacks in hotbar/backpack slots can receive exact delayed server self-confirmations after the client has already predicted the same final state, causing visible snapback/flicker.
4. Right-click place-one and left-click stack placement into normal or external inventories can flicker while the client waits for the server to catch up.

## What it fixes

### 1. Crafting output ghost/fake items

Crafting output slots have special behavior for shift-click crafting and output moves. Partial output transfers can leave the client and server disagreeing about whether the output was fully crafted, partially moved, or should be resynced.

This mod makes crafting output moves all-or-resync for the targeted paths:

- shift-clicking crafting output;
- moving crafting output into another slot;
- output-slot flip/number-key style moves.

For multi-craft output moves, it only accepts full recipe-output transfers. If the full output does not fit, it marks the output and sink slots dirty so the server can resync them instead of leaving a leftover ghost state.

### 2. Stale queued client corrections

Vintage Story already pauses single-slot inventory updates during some drag interactions. When the pause ends, vanilla replays every queued server correction in order. During fast crafting-grid or mouse-slot interactions, many of those queued updates are stale intermediate states.

This mod keeps only the latest queued correction per slot before the vanilla flush applies them. The client still ends on the latest server state; it just skips stale intermediate corrections that can no longer be correct.

Mouse cursor slot corrections are handled more aggressively: if a server mouse-slot update arrives while the mouse inventory is paused, the mod applies that correction immediately and drops older queued mouse-slot corrections. This targets temporary client-only duplicate/fake-item visuals where the cursor and a grid slot both appear to hold the same stack until the next normal click.

As of the current 1.0.2 test pass, mouse cursor corrections are also treated as authoritative instead of being prediction-suppressed. The server cursor state wins because the latest repros showed the client sending later moves from a cursor stack the server no longer had.

### 3. Exact delayed self-confirmation echoes

After the client predicts a hotbar or backpack inventory action and the server accepts the same resulting slot state, the server can still send an exact delayed confirmation back to that same client. This was the original fake-item mitigation attempt: avoid applying delayed self-confirmations that can make the client appear to bounce through old slot states.

This mod suppresses only exact matching self-confirmation updates for `hotbar-*` and `backpack-*` slots. It does not suppress mismatched updates.

### 4. External storage click confirmation gate

External storage clicks are still client-predicted in vanilla. Rapid repeated clicks can chain local predictions before the server has confirmed the previous chest, quern, or machine slot mutation. That creates temporary fake items even when the server state is correct.

The current test pass adds a short client-side confirmation gate after a predicted click changes an external storage slot. While that gate is pending, further slot-grid clicks are ignored until the matching server slot update or full contents packet arrives, or until a short timeout expires. This is intentionally conservative: correctness comes before perfectly smooth rapid clicking while the root vanilla race is still being narrowed down.

The next refinement gives pending external slots a tiny defer window for broad `InventoryContents` packets. Direct `InventoryUpdate` and `InventoryDoubleUpdate` packets still apply immediately. If a direct slot confirmation arrives during the defer window, the broad contents correction for that pending slot is dropped; if no confirmation arrives, the deferred contents correction applies. This should reduce the last short snapback flicker without returning to fake cursor/item state.

The latest diagnostic repro showed a separate machine/quern path: after a local click predicted a slot change, the slot could fall back before the direct server `InventoryUpdate` arrived, with no inventory packet logged between those states. That points at block-entity tree sync applying inventory `FromTreeAttributes` from a `MarkDirty(true)` redraw/state packet. The current pass preserves pending external slots during client-side `InventoryBase.SlotsFromTreeAttributes`, so machine state updates can still apply without rolling back the actively clicked slot. This covers `InventoryGeneric`, vanilla `InventoryQuern`, and other vanilla storage inventories that use the shared slot tree restore helper.

## Boundaries

- The server-side echo suppression does not touch crafting-grid, mouse-slot, creative inventory, or recipe-output updates.
- The crafting output fix only targets crafting-output movement paths; it does not change normal recipe matching.
- The mod does not make the client authoritative.
- The client still applies the latest server correction for each slot.
- Mouse cursor server corrections are not suppressed by the client prediction suppressor.
- External storage clicks may briefly ignore extra rapid clicks while waiting for the server correction.
- Broad full-inventory contents packets may defer a currently pending external slot for a very short window so direct slot confirmation can win.
- Client-side block-entity tree updates preserve currently pending slots while `InventoryBase.SlotsFromTreeAttributes` restores tree state, because some machines serialize inventory into broader machine-state packets.
- The server echo suppression only applies when the outgoing server stack fingerprint exactly matches a recently accepted client-predicted state.
- This is an attempt to reduce the observed fake/ghost client item symptoms caused by these targeted paths, not a guarantee against every possible fake item, ghost item, or inventory race.

## Current Findings

### Vanilla external-storage click path

External storage and block-entity inventories do not use a direct player-inventory packet path. The normal path for a click into a chest, quern, or machine inventory is:

1. `GuiElementItemSlotGridBase.SlotClick` mutates the client-side slot immediately for responsiveness.
2. The GUI sends an inventory operation packet.
3. For block entities, `GuiDialogBlockEntity.DoSendPacket` wraps that inventory packet in a block-entity packet for the target position.
4. The server block entity receives the packet and forwards normal inventory packets to `Inventory.InvNetworkUtil.HandleClientPacket(...)`.
5. The server applies the move against the authoritative inventory.
6. Dirty inventory slots are sent later by the server inventory tick, roughly every 30 ms, or a broader full inventory contents packet is sent when the server thinks the client operated on stale state.
7. The client applies `InventoryUpdate`, `InventoryDoubleUpdate`, or `InventoryContents` corrections.

This means the client is always predicting ahead of the server. There is no per-click "wait until this exact operation has been accepted" handshake before the next local click can run.

### Most likely remaining vanilla causes

The remaining vanilla-style flicker is most likely one of these packet paths:

- `InventoryContents`: broad resyncs are the strongest snapback signal. They are sent when the server thinks the client `TargetLastChanged` is stale, so repeated full contents packets point at stale or missing `lastChanged` tracking for that inventory.
- `InventoryDoubleUpdate`: right-click place-one touches both the target slot and the mouse cursor slot. If those two sides do not apply together, the client can briefly think the cursor stack or target stack is wrong.
- `InventoryUpdate`: normal delayed dirty-slot confirmation can still visibly replay an older state after the client already predicted a newer one, especially during rapid left-click or right-click placement.
- `PauseInventoryUpdates`: vanilla pauses some single-slot updates during drag gestures, but the first click can happen before the pause is set, and the pause does not naturally cover every full-contents, double-update, or block-entity sync path.

The next useful log split is to reproduce with a vanilla chest or quern and classify each flicker by the incoming packet type: `InventoryContents`, `InventoryDoubleUpdate`, or plain `InventoryUpdate`.

### Kinematics result was an amplifier, not the root cause

VintageKinematics machines were making the same class of issue feel worse because many machine inventories called `MarkDirty(true)` from `SlotModified`. That caused an extra broad block-entity sync/redraw path on every item stack change, on top of the normal inventory dirty-slot sync.

That pattern was changed in Kinematics 1.3.4 to mark the chunk modified for persistence instead of forcing a full block-entity redraw/sync for every inventory-only slot change. This made those machines feel closer to vanilla storage, but it did not remove the remaining vanilla prediction/correction race.

### Important assumptions to watch

- The client assumes its local predicted move is temporarily valid until the server proves otherwise.
- A fake item can be entirely visual/client-side: the server can remain correct while the client cursor or slot still chains follow-up actions from stale predicted state.
- The server assumes a stale `TargetLastChanged` means the client should receive a full inventory correction instead of applying the requested operation.
- Vanilla assumes queued or delayed corrections will eventually converge, even if intermediate visual states flicker.
- Block-entity inventories assume the wrapped block-entity packet path plus later inventory dirty-slot sync is enough to reconcile the GUI.
- Any mod that sends extra `MarkDirty(true)` block-entity syncs during pure inventory moves can amplify the vanilla race.

### Minor vanilla patch review

A later 1.22.x minor patch changelog did not list a direct fix for this desync class. The closest adjacent fixes were:

- crash when using arrow keys to navigate an inventory with no slots;
- unable to place a partially-full jug onto a shelf with another liquid container while holding shift;
- API fix for emptying liquid container contents onto the ground.

Those touch inventory-adjacent UI or direct block interaction paths, but they do not appear to change the mouse click prediction/server correction path used by normal GUI storage.

After refreshing the decompiled source, these important sync-path observations still appeared unchanged:

- `GuiElementItemSlotGridBase.OnMouseDownOnElement` still calls `SlotClick(...)` before setting `PauseInventoryUpdates`.
- `InventoryNetworkUtil` still sends full `InventoryContents` when `lastChangedSinceServerStart > lastChangedClient`, and activate-slot packets still rely on `TargetLastChanged`.
- `GeneralPacketHandler.HandleInventoryDoubleUpdate` still appears to fetch `invId1` for the second inventory lookup when `invId1 != invId2`. If the decompile is accurate, this remains suspicious for cursor-plus-target updates.

## Install Notes

Install this mod on both the client and the server. The combined public package is marked as required on both sides so servers using it should make clients install it too.

Client-side install enables stale queued correction coalescing.

Server-side install enables hotbar/backpack exact self-echo suppression.

The two pieces are technically independent, but the published package is intentionally not marked optional because only one side gives only half of the intended behavior.

## Test Notes

In testing, the client-side coalescer dropped large numbers of stale queued crafting-grid corrections while preserving the latest server state. The separate probe build reported no inventory packet pollution and no UI-to-packet inventory mismatch during the successful test runs.

The latest storage-focused pass intentionally removed the experimental broad `lastChangedSinceServerStart` sequence patch. That path was too invasive and did not match the clarified symptom: fake client items rather than real server-side duplication.

Observed probe examples:

- 247 queued paused updates observed, 156 stale queued updates dropped in one crafting-grid stress test.
- 0 outgoing creative inventory actions during the successful test run.
- 0 UI-to-packet inventory mismatches during the successful test run.

## Diagnostic Logging

Version 1.0.2 has a diagnostic classifier for the remaining storage flicker work. It is off by default.

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

- client slot clicks: target inventory/slot, mouse button, modifiers, target stack before/after, mouse stack before/after;
- incoming client `InventoryUpdate`;
- incoming client `InventoryDoubleUpdate`;
- incoming client `InventoryContents`, with per-slot detail for recently clicked or mismatching slots and a summary for the whole contents packet;
- server received activate/move/flip inventory packets, including `TargetLastChanged` vs server `lastChangedSinceServerStart`;
- server outgoing inventory update, double-update, and full-contents packets where visible through `ServerMain.SendPacket`.

Recommended repro matrix:

1. Vanilla chest: left-click a full stack into a slot 10-20 times.
2. Vanilla chest: right-click place-one into a slot 10-20 times.
3. Vanilla quern: repeat both tests.
4. Player inventory only: repeat both tests.

The goal is to classify each visible flicker by the packet that caused the correction:

- `InventoryContents` means chase stale `TargetLastChanged` or broad full resyncs.
- `InventoryDoubleUpdate` means chase cursor-plus-target atomic application.
- `InventoryUpdate` means chase delayed stale dirty-slot confirmation.
- No inventory packet at flicker time means look for block-entity tree sync or rendering/UI-only causes.

## Short ModDB Description

My attempt to fix specific inventory desync symptoms in Vintage Story, including temporary fake/ghost client items:

- crafting output ghost/fake items from partial shift-crafts and output-slot moves;
- stale queued client corrections during rapid crafting-grid/cursor dragging, which can make items flicker, briefly duplicate client-side, or appear fake until the next reconciliation;
- exact delayed hotbar/backpack self-confirmation echoes from the server, which can make stacks snap back through old states after the client already predicted the accepted state;
- right-click place-one and left-click stack placement flicker while normal or external inventories wait for server correction.

It does this by making targeted crafting-output moves all-or-resync, coalescing queued client corrections to the latest correction per slot, applying mouse-slot corrections authoritatively, briefly gating repeated external-storage clicks while waiting for server confirmation, giving broad full-inventory corrections a tiny defer window for pending external slots, preserving pending slots across shared client-side inventory tree restores, and suppressing only exact matching hotbar/backpack self-echoes on the server.

It does not make the client authoritative and does not suppress crafting-grid, mouse-slot, creative, or recipe-output server updates.
