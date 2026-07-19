# Kinetic Storage Plan

Goal: add a VK-native bulk storage system that scales to factory-sized item counts without relying on hundreds of normal inventory slots or constant physical-container rescans.

This is a design plan, not a shipped feature spec. The important rule is that storage safety comes before convenience. A storage system that can hold thousands of items must be hard to corrupt, hard to accidentally void, and recoverable when Vintage Story block entities behave badly.

Implementation API and hook research lives in [`kinetic-storage-api-layout.md`](kinetic-storage-api-layout.md).

## Design Direction

The preferred design is a physical multiblock warehouse with an indexed internal item store.

Players should increase capacity by making the structure bigger, not by placing one normal crate per item type. The controller handles item typing internally, so the player worries about total capacity, throughput, and automation access rather than maintaining hundreds of sorted chests.

Core blocks:

- `Kinetic Storage Controller`: owns the network, item index, GUI, save data, and recovery identity.
- `Storage Cell`: adds generic item capacity to the attached warehouse.
- `Import Hatch`: accepts items from belts, funnels, machines, or player input.
- `Export Hatch`: exposes filtered output to belts, funnels, machines, or players.
- `Kinetic Drive Port`: optional throughput upgrade using VK rotation/SU.
- `Indexer` or `Sorting Core`: optional unique-item-type capacity upgrade if type count needs a gameplay cost.

The system should feel physical and VK-like, but internally it should be an indexed storage backend:

```text
ItemKey -> StoredEntry
ItemKey -> optional cached routing/output state
```

It should not act like a giant fake inventory made of thousands of slots.

## Gameplay Model

Storage capacity is generic.

Example progression:

- Controller: enables the warehouse and gives a small base capacity.
- Wood Storage Cell: adds a modest amount of item capacity.
- Reinforced Storage Cell: adds much more item capacity.
- Indexer: increases unique item type capacity if needed.
- Import/Export Hatch: adds automation ports.
- Kinetic Drive Port: increases transfer rate when powered.

Possible stats:

- Total capacity: total item count stored, for example `9800 / 10000`.
- Type capacity: unique item keys stored, if we want a type limit.
- Import rate: items per second accepted through hatches.
- Export rate: items per second extracted through hatches.
- Rebuild speed: how quickly the controller can recover/reindex after structural changes.

The system should avoid cost scaling like "8x more metal means 32x more storage". Raw capacity should mostly come from physical volume and wood/structural materials. Metal, gears, and SU should buy throughput, automation, indexing, and convenience.

## Item Data Model

The controller stores aggregated entries, not individual item objects.

```csharp
public readonly struct ItemKey
{
    public readonly int RuntimeCollectibleId;
    public readonly string Code;
    public readonly int AttributeHash;
}

public sealed class StoredEntry
{
    public ItemStack Exemplar;
    public long Quantity;
    public string CachedSearchText;
}
```

Rules:

- Normal stackable items with identical keys aggregate into one entry.
- Attribute-bearing items use an attribute-aware key.
- The stored exemplar keeps the exact stack data needed to recreate extracted stacks.
- Quantity is a `long`, not an `int`, so large warehouses do not overflow.
- Runtime IDs may be used for fast lookup, but persistent save data should keep item/block codes because numeric IDs are not stable enough to be the only saved identity.

Special cases:

- Tools, named items, damaged items, heated items, and containers with contents should usually store as exact attribute keys.
- Food/perishables need careful handling because freshness changes over time.
- Nested inventories, backpacks, and liquid containers should either be stored as exact stacks or blocked until explicitly supported.
- The first implementation can reject risky item categories rather than pretending they are safe.

## Performance Rules

The warehouse must be index-first.

Do:

- Update indexes on insert, extract, cell link, cell unlink, and manual rebuild.
- Cache display names and search text for the GUI.
- Store one exemplar stack plus quantity per item key.
- Keep dirty flags so saving/network sync only happens when data changes.
- Send paged or filtered GUI data rather than every stored entry every frame.

Do not:

- Scan every linked inventory every tick.
- Build a giant fake slot inventory for all stored items.
- Call full `ItemStack.Satisfies` or attribute tree equality inside large repeated scans.
- Sort by localized item name on every GUI refresh.
- Sync the whole storage contents every time one stack changes.

## Capacity And Structural Safety

Breaking or moving storage cells must never delete stored items.

Before any action removes capacity, the controller must calculate whether the remaining valid multiblock can still hold the current stored quantity.

Example:

```text
Current storage: 9800 / 10000
Player tries to remove a cell worth 512 capacity
Remaining capacity would be 9488
Action is denied because 9800 > 9488
```

This must apply to all removal paths:

- Player block breaking.
- VK wrench pickup.
- Contraption capture or movement.
- Explosions.
- Block replacement.
- Worldgen/repair style block setting if practical.
- Any block entity unload/reload path that detects missing cells.

The rule should be:

```text
If removing this block would make stored quantity exceed remaining capacity, deny removal.
```

If the block removal cannot be denied, the fallback should preserve data on the controller and mark the warehouse as over-capacity and locked, not void items.

Over-capacity locked state:

- No item insertion.
- No normal automation output if that would hide the problem.
- Manual extraction is allowed.
- The tooltip and GUI clearly show the missing capacity problem.
- Adding enough cells clears the locked state.

## Controller Data Ownership

Only the controller should own stored item data.

Storage cells should not store item contents individually. They should store only structural data needed to rejoin the controller, such as controller position or warehouse ID.

This avoids data being scattered across many block entities. If one cell block entity dies, the controller still has the item data.

The controller should save:

- Warehouse ID.
- Stored entries.
- Current capacity.
- Known storage cells.
- Known import/export hatches.
- Optional type capacity.
- Optional throughput upgrades.
- Dirty/rebuild flags.

Cells should save:

- Controller position or warehouse ID.
- Cell tier/capacity contribution.
- Optional structural role.

## Block Entity Failure Recovery

Vintage Story block entities can fail to deserialize, disappear, or be temporarily missing after mod changes. The storage system needs recovery tools from the start.

Recovery rules:

- If a cell loses its block entity but the block remains, the controller should be able to rescan the multiblock and restore the cell link.
- If the controller block entity is missing but the controller block remains, an admin command should attempt to reconstruct the controller from saved data if possible.
- If the controller is gone but cells remain, cells should not contain item data, so there is no partial item state to merge.
- If saved item data cannot be resolved because an item/block was removed by a mod change, the entry should be retained as unresolved data instead of deleted.

Admin commands to consider:

```text
/vk storageinfo
/vk storagerebuild [radius]
/vk storagelock [radius]
/vk storageunlock [radius]
/vk storageexport [radius]
```

Command behavior:

- `storageinfo`: show controller position, capacity, stored quantity, entry count, dirty state, and locked/over-capacity state.
- `storagerebuild`: rescan nearby warehouse blocks and rebuild structural capacity/index metadata without changing stored entries.
- `storagelock`: manually prevent insertion/extraction while investigating.
- `storageunlock`: unlock only if capacity and data checks pass.
- `storageexport`: emergency dump stored entries to item entities or crates near the controller, with confirmation or server-control privilege.

All recovery commands should require server-control/admin privilege.

## Breaking, Pickup, And Drops

Controller break behavior:

- If storage contains items, breaking is denied.
- If storage is empty, it drops the canonical controller item.
- If it has linked cells but no items, breaking may either deny until unlinked or auto-unlink all cells.

Cell break behavior:

- If removing the cell keeps current stored quantity within remaining capacity, allow break and update controller capacity.
- If not, deny break with a clear message.
- Drops should be canonical cell variants.

Wrench pickup:

- Same capacity checks as block breaking.
- No shortcut should bypass the safety logic.

Explosions:

- Prefer blocking destruction when it would void storage.
- If the game API cannot reliably cancel a destruction path, preserve controller data and enter over-capacity locked state.

Contraptions:

- Storage controller and cells should probably be non-movable in the first implementation.
- If movable support is added later, capture must move the entire valid warehouse or refuse capture.
- Partial warehouse movement must not split stored data.

## Automation

Import hatches:

- Accept from belts, funnels, player slots, and machine outputs.
- Apply filters if configured.
- Respect claims.
- Respect capacity and type capacity.
- Return remainder if full.

Export hatches:

- Pull by filter or selected item key.
- Optional exact/fuzzy filter modes can reuse VK filter helpers.
- Output to funnels, belts, machines, or normal inventories through existing output helpers.
- Respect throughput limits.

The controller should expose an API similar to:

```csharp
public interface IVKStorageNetwork
{
    bool TryInsert(ItemStack stack, out ItemStack remainder);
    bool TryExtract(ItemKey key, int quantity, out ItemStack extracted);
    IReadOnlyList<StoredEntry> GetEntries();
    StorageStats GetStats();
    void RebuildStructure();
}
```

Machines, funnels, hatches, and terminals should talk to this interface rather than knowing the backend details.

## GUI Plan

The GUI should be searchable and bulk-focused.

Required display:

- Total stored quantity and capacity.
- Entry count and optional type capacity.
- Current throughput.
- Locked/over-capacity warning if applicable.
- Search field.
- Paged entry list.
- Entry rows with icon, name, quantity, and quick withdrawal controls.

Actions:

- Left click withdraw one stack.
- Shift click withdraw as much as player inventory can hold.
- Right click set export/filter target if using an export hatch UI.
- Optional sort modes: name, quantity, mod/domain, recent.

The GUI should not create thousands of actual GUI slots. Use a recipe-browser-style list component with cached entries.

## Save And Sync Plan

Save compact storage data:

- Controller stores entries as code, item class, serialized exemplar attributes, and quantity.
- Avoid saving a separate full `ItemStack` for every item count.
- Avoid syncing all entries every tick.

Sync:

- Send full entry list only when opening the GUI or requesting refresh.
- Send small delta packets when one entry changes while the GUI is open.
- Batch changes during high-throughput import/export.
- Keep server authoritative.

## First Implementation Scope

Keep the first version intentionally small.

Include:

- Controller.
- Storage cells.
- Manual GUI insert/extract.
- Searchable aggregated item list.
- Capacity checks on controller/cell breaking and wrench pickup.
- Admin `storageinfo` and `storagerebuild`.
- Canonical drops.
- Claim checks.

Defer:

- Remote terminals.
- Moving warehouses on contraptions.
- Complex priority routing.
- Liquid storage.
- Nested inventory support.
- Cross-controller networks.
- Animated crane retrieval as functional logic.

Visual crane arms or moving parts can be added as decorative/feedback animation later. The data model should not depend on animating every item movement.

## Test Checklist

- Insert many stacks of the same item and confirm they aggregate into one entry.
- Insert many different item types and confirm search remains responsive.
- Insert attribute-bearing items and confirm extraction preserves attributes.
- Fill near capacity, then try breaking one cell that would drop capacity below stored quantity.
- Break one cell that is safe to remove and confirm quantity remains unchanged.
- Try wrench pickup on unsafe and safe cells.
- Try explosion damage against cells and controller.
- Reload world with a full warehouse.
- Remove/readd a storage cell block entity through debug conditions and run rebuild.
- Remove an item-providing mod and confirm unresolved stored entries are not silently deleted.
- Open GUI while import/export is active and confirm no full-list packet spam.
- Test claims for insert, extract, break, wrench pickup, and admin commands.
