# First Usable VRPG Player Menu

**Status:** Source/test/build/server-startup evidence complete on 2026-09-12; joined-client and player confirmation pending

**Goal:** Replace Vintage Story's Inventory and Character dialog owners with the existing VRPG-native Inventory/Equipment screen, backed only by authoritative VRPG item state.

## Scope

- The Inventory action opens the VRPG Inventory page.
- The Character action opens the same page with Equipment expanded.
- Both semantic actions are replaced together without changing player bindings.
- Inventory browsing, inspection, Equip, and Unequip use live server state.
- VRPG Adventure suppresses unwanted native inventory producers at their owning systems. Unexpected native ingress fails closed and logs one actionable diagnostic.
- A failed menu/store activation blocks VRPG Adventure entry; it does not expose two competing inventories.

The first pass excludes crafting, storage, trading, drops, loot piles, party UI, remaining Hub pages, Home materialization, and generalized third-party compatibility.

## Screen contract

- **Player goal:** Find owned gear and change equipped items.
- **Entry points:** Existing Inventory and Character semantic actions.
- **Required information:** Item identity, quantity, category, equipped state, requirements, mechanical effects, comparison, and available Equip/Unequip action.
- **Primary control:** Equip or Unequip the selected item.
- **Secondary controls:** Category, search, paging, view mode, and Equipment disclosure.
- **Excluded information:** Native backpack slots, crafting grid, Survival nutrition, raw IDs/revisions, unfinished actions, and future services.
- **States:** Loading, empty, stale, disconnected, unsupported item, pending mutation, success, and actionable rejection.
- **Authority:** Every mutation is authenticated and revision-checked by the server.

## Implementation order

### Task 1 — authoritative Equip/Unequip actions

- [x] Add failing protocol/service tests for successful Equip and Unequip, stale revisions, invalid roles, missing entries, requirements, and replay.
- [x] Dispatch actions through the existing gear equipment service and canonical store transaction.
- [x] Return bounded changed entries and invalidation scopes; never mutate from client claims.
- [x] Keep every other action explicitly unsupported.

### Task 2 — live client Inventory/Equipment runtime

- [x] Create one connection-owned `PlayerItemClientService`, attach it to the existing channel, and retain it across dialog lifetimes.
- [x] Create the production `InventoryPage`, controller, and dialog from that service.
- [x] Pump requests on the client game thread and reset state across world/connection changes.
- [x] Wire Equip/Unequip controls with pending, success, stale, and rejection feedback beside the action.

### Task 3 — atomic semantic dialog replacement

- [x] Discover exactly one live owner each for `inventorydialog` and `characterdialog` after native dialogs register.
- [x] Validate both targets, both entry adapters, client protocol, store readiness, and production dialog before mutation.
- [x] Remove both native owners and install both VRPG entry adapters as one operation.
- [x] Restore both native owners on failed installation or disposal before canonical Adventure activation; never leave a half-replaced pair.
- [x] Route Inventory to browsing and Character to Equipment-expanded browsing; either action toggles the shared dialog closed.

### Task 4 — Adventure producer suppression and fail-closed activation

- [x] Define one VRPG Adventure capability boundary used by menu ownership and native item-producer suppression.
- [x] Disable native backpack/crafting/container/trade/ground-item production exposed in Adventure instead of adding compatibility adapters.
- [x] Keep strict VRPG transactions for rewards, equipment, migration, and later explicitly retained flows.
- [x] Reduce old producer coverage gates to diagnostics for unexpected real ingress.
- [x] Refuse Adventure activation with a concise player-facing error when canonical custody or menu ownership is unavailable.

### Task 5 — verification and documentation

- [x] Pass focused tests after each task and the full suite at the integration boundary.
- [x] Clean Release-build and synchronize the installed development mod.
- [x] Verify disposable dedicated-server startup and clean shutdown without claiming UI acceptance.
- [ ] Verify a supported client joins and exercises both semantic actions; no client joined during this task.
- [ ] Have the user confirm that ordinary Inventory and Character actions never open Vanilla UI before marking replacement ready.
- [x] Update the authoritative roadmap to distinguish this first usable replacement from later page and gameplay breadth.

## Rulings

- The replacement is server/profile scoped, not a physical-key interception.
- Failure after canonical custody is fail-closed; Vanilla inventory is not a live fallback inside VRPG Adventure.
- Removing unwanted Adventure producers is preferred to proving compatibility with every theoretical producer.
- Existing reviewed foundations are reused; no shell, store, gear, protocol, party, or world-item subsystem is rewritten for this pass.
