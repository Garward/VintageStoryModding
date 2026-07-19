# Kinetic Storage API Layout

This note records the API hooks and file layout for the planned VK indexed warehouse system.

The goal is to make storage a reusable VK platform feature, not a single 4000 line block entity. Machines, funnels, hatches, tools, logic sensors, and downstream mods should talk to public interfaces in `VintageKinematics.Api.Storage`.

## Researched Vintage Story Hooks

Useful hooks:

- `BlockBehavior.OnBlockBroken(..., ref EnumHandling handling)`: can stop normal player break removal. VK already uses this pattern for empty-storage and bore-retracted guards.
- `BlockBehavior.OnBlockExploded(..., ref EnumHandling handling)`: can stop explosion removal with `EnumHandling.PreventDefault`.
- `Block.GetDrops` / `BlockBehavior.GetDrops`: can enforce canonical drops and stored state drops where needed.
- `Block.OnPickBlock`: can preserve state for creative pick/wrench style flows.
- `BlockEntity.ToTreeAttributes` / `FromTreeAttributes`: controller-owned persisted storage data belongs here.
- `BlockEntity.MarkDirty`: should be called only on real storage changes or structural changes, not every frame/tick.
- `IBlockEntityContainer.Inventory`: useful for normal storage compatibility, but the warehouse should not expose thousands of fake inventory slots.
- `IWorldAccessor.Claims.TryAccess`: required for insert, extract, break, wrench pickup, and admin recovery behavior.
- server `ChatCommands`: existing `/vk` command tree can host storage recovery/admin commands.

Important handling detail:

- For player break guards, `PreventSubsequent` is safest because it stops the block's default break path and later behaviors.
- For explosion guards, use `PreventDefault`; `PreventSubsequent` only stops later behaviors and the base explosion code may still remove the block.

## Public API Files

Current scaffold:

```text
src/Api/Storage/
├── IVKStorageNetwork.cs
├── IVKStorageProvider.cs
├── IVKStorageRemovalGuard.cs
├── IVKStorageStructureMember.cs
├── IVKStoragePort.cs
├── ItemKey.cs
├── StorageChangeReason.cs
├── StorageRemovalCheck.cs
├── StorageRemovalKind.cs
├── StorageStats.cs
├── StorageTransferResult.cs
└── VKStorageKeys.cs
```

Related shared behavior:

```text
src/Blocks/BlockBehaviorRequireStorageCapacityOnRemove.cs
```

Registered JSON behavior name:

```json
{ "name": "RequireStorageCapacityOnRemove" }
```

## Responsibility Split

### API Layer

Namespace: `VintageKinematics.Api.Storage`

Owns public interfaces and small value objects only:

- item identity
- stored entry snapshots
- capacity/stats summary
- insert/extract result types
- storage network interface
- structure member and port interfaces
- removal safety interface

This layer should stay dependency-light so machines and downstream mods can use it without pulling in storage block implementation details.

### Controller Block Entity

Future location:

```text
src/BlockEntities/Storage/BEKineticStorageController.cs
```

Responsibilities:

- own persisted stored entries
- implement `IVKStorageNetwork`
- implement `IVKStorageProvider`
- implement `IVKStorageRemovalGuard`
- save/load storage data
- maintain capacity/type/port stats
- validate multiblock structure
- answer GUI/search/export queries
- lock/unlock over-capacity or recovery state

It should delegate to helper classes for indexing, serialization, structure scan, and GUI sync.

### Storage Index

Future location:

```text
src/Storage/KineticStorageIndex.cs
```

Responsibilities:

- `Dictionary<ItemKey, StoredEntry>`
- insert/extract bookkeeping
- search text cache
- type limit checks
- total stored quantity
- unresolved entry handling after mod removal

### Structure Scanner

Future location:

```text
src/Storage/KineticStorageStructure.cs
```

Responsibilities:

- find connected controller/cells/hatches
- calculate capacity
- calculate type capacity
- calculate throughput
- detect orphaned cells
- rebuild after block changes/admin command

### Persistence

Future location:

```text
src/Storage/KineticStoragePersistence.cs
```

Responsibilities:

- compact entry serialization
- unresolved item preservation
- version migration
- controller warehouse ID handling

### GUI Sync

Future location:

```text
src/Storage/KineticStorageSync.cs
src/Gui/KineticStorageDialog.cs
```

Responsibilities:

- send full page/search results when GUI opens
- send deltas while GUI is open
- avoid syncing all entries after every insert/extract
- keep server authoritative

### Automation Ports

Future location:

```text
src/BlockEntities/Storage/BEKineticStorageImportHatch.cs
src/BlockEntities/Storage/BEKineticStorageExportHatch.cs
```

Responsibilities:

- implement `IVKStoragePort`
- resolve controller
- call `IVKStorageNetwork.TryInsert`
- call `IVKStorageNetwork.TryExtract`
- respect claims and filters
- limit throughput

## Core Interface Shape

`IVKStorageNetwork` is the main integration point:

```csharp
StorageTransferResult TryInsert(ItemStack stack, out ItemStack remainder, int maxQuantity = int.MaxValue);
StorageTransferResult TryExtract(ItemKey key, int quantity, out ItemStack extracted);
StorageRemovalCheck CanRemoveStructuralBlock(BlockPos pos, long capacityContribution);
void RebuildStructure(StorageChangeReason reason = StorageChangeReason.ManualRebuild);
```

Machines/funnels/export hatches should only need this interface. They should not know whether storage is backed by a warehouse controller, a single bulk drawer, or a future remote network.

## Safety Behavior

`BlockBehaviorRequireStorageCapacityOnRemove` asks the local block entity or its controller whether removal is safe:

1. Resolve local BE using multiblock-aware lookup.
2. If the BE implements `IVKStorageRemovalGuard`, ask it directly.
3. If the BE implements `IVKStorageStructureMember`, find `ControllerPos` and ask the controller if it implements `IVKStorageRemovalGuard`.
4. Deny player break or explosion if the controller says removing the block would overflow storage.

This lets all future warehouse blocks use the same safety rule from JSON.

## Implementation Rule

Avoid adding warehouse-specific behavior directly to unrelated systems.

Correct direction:

- funnels, belts, hatches, machines call `IVKStorageNetwork`
- controller delegates to index/structure/persistence helpers
- GUI talks through a controller sync layer
- admin commands call controller/recovery helpers

Avoid:

- storage scans inside GUI code
- machine-specific storage exceptions
- huge fake inventories
- per-tick full rebuilds
- item movement logic duplicated in every block entity

