# Inventory, Loot, and Storage

Status: Adopted direction; implementation details remain provisional  
Parent: [Complete game design](complete-game-design.md)  
Index: [VRPG design index](README.md)

## 1. Purpose

Inventory exists to preserve discoveries and support build decisions. It must not repeatedly stop combat for backpack management, force players to shuffle stacks between boxes, or make a static base mandatory merely because ordinary materials occupy hundreds of slots.

The player-facing promise is: pick up worthwhile things now, understand them quickly, and organize or act on them when you choose.

## 2. Player inventory contract

- Every VRPG character begins with the complete intended player inventory capacity.
- There is no backpack equipment slot and no progression tax for basic carrying capacity.
- The combat hotbar is a loadout and quick-use surface, not the only set of items the player can use.
- The inventory size is static across ordinary progression. Rewards improve builds, storage services, filtering, or automation rather than adding mandatory pocket rows.
- Typed tabs keep unlike objects readable. Default categories are **Gear**, **Materials**, **Consumables**, **Keys & Quest**, and **Miscellaneous**.
- Category rules are data-defined. Items always explain where they went, and a missing or invalid category falls back safely instead of deleting or hiding the item.
- Sorting, search, comparison, mark/favorite, salvage selection, and deposit actions work across tabs.
- Quest-critical objects and currencies cannot be lost because a visible slot grid is full.

Typed tabs are primarily views over storage policies, not five unrelated bags the player must manually balance. Where a category has a capacity, the UI states it plainly and overflow follows a predictable policy.

## 3. Configuration and compatibility

The inventory replacement is enabled by default in the `vrpg-default` preset, but it is a major configurable feature boundary.

`InventoryReplacement.Enabled` is the conceptual ownership switch. When enabled, VRPG owns player capacity, typed tabs, backpack-slot removal, hotbar bridging, overflow, and player-inventory persistence as a coherent system. When disabled, Vanilla player inventory and backpack behavior remain active and VRPG adapts loot delivery through a compatibility bridge.

Sub-options may tune the replacement without dividing ownership:

- Fixed player capacity and category capacities.
- Category definitions and display order.
- Aggregate eligibility and maximum quantities.
- Automatic pickup, loot-feed grouping, and overflow destination.
- Base-storage access scope and bulk-deposit behavior.

Backpack-slot replacement can be exposed as an advanced compatibility override, but disabling it while the inventory replacement remains active must have a defined result. The default result is to show the Vanilla backpack slots as optional equipment-provided bonus space; VRPG content must never require that bonus space. It must not cause both Vanilla and VRPG systems to serialize the same items.

Changing inventory ownership on an existing world requires a restart and a migration workflow:

1. Scan all affected player records and report destination capacity, unsupported items, and unresolved content.
2. Create a recoverable migration snapshot.
3. Transfer every entry transactionally, routing excess to an explicit recovery store.
4. Record the source mode and completed schema version so the migration is idempotent.
5. Refuse activation if lossless transfer cannot be guaranteed.

The implemented scanner treats the hotbar, offhand, mouse-held stack, crafting
inputs, character equipment, backpack items, and backpack contents as one source.
Backpack contents are flattened into independent canonical records while a cloned
bag is cleared before it is encoded; this preserves both the empty bag and every
contained item exactly without importing nested contents twice. Crafting output is
derived state and is not imported as an additional item.

Migration uses a durable journal. `Prepared` means the complete source snapshot,
the prior VRPG store, and the staged replacement are saved while Vanilla still
owns its slots. Only then may the source be cleared. `Committed` records completion;
`RestorePending` and `Restored` make administrative rollback restart-safe. Startup
can finish a prepared clear, recognize an already-empty source, or restore the old
owner after a partial clear. No automatic commit runs until the menu-ownership gate
is ready, so implementing migration cannot strand items behind the unfinished UI.

The server synchronizes the effective inventory capabilities to clients. Addons use those capabilities and `IPlayerItemStore`; they do not infer behavior from whether a backpack slot happens to be visible.

## 4. Item storage classes

Different item identities need different representations:

| Class | Examples | Representation |
|---|---|---|
| Unique entries | Rolled gear, modified tools, authored relics | One exact entry per distinct item state |
| Aggregated commodities | Ore, components, common drops, crafting materials | Attribute-aware key plus a high `long` quantity |
| Consumable stacks | Potions, deployables, ammunition where relevant | High-count entry with direct quick-use binding |
| Ledgers | Currency, reputation tokens, account or life resources | Named balances, not fake item slots |
| Protected records | Quest keys, unlock proofs, discovered recipes | Persistent flags or protected entries |

This hybrid model avoids pretending that ten thousand identical materials are ten thousand slot objects while preserving exact attributes for gear that is genuinely unique.

## 5. Storage model

The canonical aggregate is conceptually:

```text
ItemKey → collision bucket of StoredEntry
StoredEntry = stable entry id + exact exemplar + long quantity + revision
```

`ItemKey` is an efficient candidate key using item class, code or runtime identity, and normalized attribute identity. Hash equality is never sufficient by itself; insertion and extraction verify exact stack equality inside the collision bucket.

Important rules:

- Quantities use checked arithmetic and explicit capacity policy.
- Unique gear never merges merely because its display name matches.
- Unresolved items survive a missing addon or asset and can resolve again later.
- Each mutation is a server-authoritative transaction with a revision and explicit result.
- Interactions that require a normal Vintage Story `ItemStack` materialize only the bounded amount needed, then reconcile the result transactionally.
- Home Build palettes store stable references to canonical entries. Placement reserves and materializes only the bounded quantity required by the attempted action, then commits actual consumption or rolls back.
- UI pages operate on immutable entry summaries rather than live mutable inventory objects.

### Durable world-save format

Player-item persistence is owned by the server world save, not by client config or
an external absolute path. VRPG rotates between the opaque save-data keys
`vrpg:player-items:a` and `vrpg:player-items:b`. A slot contains a format version,
monotonic generation, payload length, SHA-256 checksum, and gzip-compressed JSON.
The JSON archive has its own schema version independent of both the slot framing
and canonical item schema.

Only dirty player stores are recaptured; unchanged snapshots are reused in the
next archive. A save writes the inactive generation and marks a captured store
clean only if it did not change during the write. Startup chooses the newest
checksummed, fully validated generation. Corrupt or interrupted writes fall back
to the prior slot, while a newer unsupported schema blocks activation to prevent
an older build from silently overwriting data it cannot understand.

The persisted state includes exact item payloads and embedded asset references,
quantities, locations, flags, equipment roles, recovery metadata, active
reservations, and completed idempotency requests. Unknown extension fields are
retained when known records are rewritten. Loading stages and validates every
player before replacing the live directory, so one invalid record cannot expose
a partially loaded authority state.

The design should not globally increase every Vanilla item maximum and assume all crafting, networking, containers, and GUIs remain correct. High player-visible quantities are a VRPG storage feature with controlled bridges to systems that still expect bounded stacks.

## 6. VintageKinematics lessons

The VintageKinematics warehouse is the reference architecture for performant high-count storage. VRPG should retain these proven ideas:

- An indexed internal store instead of thousands of simulated slots.
- Attribute-aware candidate keys plus exact collision-safe comparison.
- One exemplar and a `long` quantity for an aggregated entry.
- Stable entry identifiers, revisions, dirty flags, and compact persistence.
- Cached normalized search fields, server-side filter/sort/page, and bounded result pages.
- Delta updates rather than full inventory synchronization after every mutation.
- A clear data owner that delegates indexing, persistence, querying, and network presentation through narrow interfaces.
- No world scans, fake-slot iteration, or full-list network sends on each tick.

VRPG should not directly couple its core inventory to a particular warehouse block or require VintageKinematics to be installed. During implementation, either extract a dependency-light shared index library or implement the same contracts independently behind a VRPG storage interface. Foreign inventories receive a bridge only when they are part of an explicitly supported native addon or Home-content adapter.

## 7. Field loot flow

Loot should reward the fight without turning its aftermath into janitorial work.

- Nearby eligible commodities can collect automatically or through one deliberate area pickup, according to player preference and server rules.
- Pickup batches merge server-side before producing UI updates.
- A compact loot feed announces notable gear, new unlocks, objective items, and quantity changes; routine material spam collapses into summaries.
- Drops are filterable by category, rarity, tags, build relevance, and ownership. Filtering changes presentation or routing, never silently destroys items unless the player explicitly configures salvage.
- Full unique-entry capacity routes excess to a clearly named recovery cache or base intake when the activity allows it. The player receives one actionable notice.
- Boss and objective rewards use a protected reward flow and cannot vanish under ordinary ground-item lifetime rules.
- Comparison and marking are available after safety returns; they do not require inspecting every item while enemies remain active.

The preferred default is generous pickup and delayed evaluation. Inventory pressure may create build choices around unique gear, but ordinary materials should not force an expedition exit.

### Combat transition and held-item bridge

- Entering Adventure combat atomically routes non-equipped field items into `IPlayerItemStore`; a newly collected item never becomes the active hand merely because Vanilla found a free hotbar slot.
- The combat loadout references canonical inventory entry IDs. Only the selected weapon or bounded quick-use bridge stack is materialized into the engine-held slot, and every consume, swap, cancel, disconnect, and zone transition reconciles transactionally.
- Mouse-wheel input cycles eligible equipped weapons, not storage slots or recently collected objects. If no weapon is available, combat selects a neutral empty hand state and reports that state truthfully on the HUD.
- Leaving combat restores the appropriate contextual profile, not stale transient pickup state. A failed transfer retains every item in an explicit recovery path and refuses the context switch rather than risking loss or duplication.

## 8. Base storage and crafting access

A base should be valuable because it expands planning and expression, not because the player must visit twelve labeled boxes.

- Static storage presents a unified searchable index across connected authorized storage.
- **Deposit Materials**, **Deposit by Rule**, and **Take for Recipe** are single transactions with previews where loss or ambiguity is possible.
- Crafting and upgrade services query authorized storage through an interface instead of moving ingredients into a temporary player grid by hand.
- Favorite, protected, loadout-bound, and quest items are excluded from bulk deposit or salvage by default.
- Hub services may provide remote access within an explicit scope. Expeditions do not grant unlimited remote resupply unless that is the activity's rule.
- Physical storage construction can raise base capacity, unlock routing rules, or create specialized services without shrinking the player's functional field inventory.

Storage ownership, multiplayer permissions, reservation during crafting, and concurrent withdrawals are server-authoritative.

## 9. Inventory screen contract

Primary goal: decide what to equip, keep, use, salvage, or pursue next without manual stack maintenance.

Inventory is the default page of the unified player menu:

- `E` opens Inventory by default, then the same shell switches to Stats, Skills, Talents, Library or Options without closing and reopening unrelated dialogs.
- The Survival character/equipment window is replaced. Equipment is a disclosure within the Gear view, not a separate inventory owner or an always-visible paper-doll dashboard.
- `C` may remain as a configurable direct shortcut to the Gear view with Equipment expanded. Both inputs reach the same screen state and server inventory.
- There is no backpack region when `InventoryReplacement.Enabled` owns capacity.
- The normal composition is two panes: typed browse/search and selected-item context. Filters, equipment, affixes and technical details expand only when requested.
- Broad margins and fitted buttons take priority over dense grids of labeled values. Opening a disclosure uses a short plate-release/slide animation, with an immediate reduced-motion alternative.

Native integration replaces menus, not keys. Vanilla associates `GuiDialogInventory` with the `inventorydialog` action and `GuiDialogCharacter` with the `characterdialog` action through `ToggleKeyCombinationCode`. Their default mappings are `E` and `C`, but VRPG never reads, writes or hard-codes those physical keys. When inventory replacement is enabled, the client transactionally replaces the two registered dialog owners:

1. `inventorydialog` toggles the unified player menu on its Inventory page.
2. `characterdialog` toggles the same menu on Gear with Equipment expanded.
3. Invoking either menu action while the unified menu is open closes it consistently.
4. Remapping either action in Vintage Story controls automatically remaps the replacement because the action identity is unchanged.
5. The old dialogs are removed from the loaded-dialog chain and cannot become a second inventory owner. A narrow guard rejects programmatic opening only if testing finds another path can still expose them.
6. When replacement is disabled, VRPG leaves the Vanilla dialogs and mappings intact.

This boundary needs an integration test after each supported game update because default-dialog registration is engine-owned. It should not require patching the global keyboard dispatcher.

The screen must show:

- Current tab, search/filter state, used capacity where relevant, and any overflow destination.
- Item name, type, rarity, quantity, protected/favorite state, and build-relevant comparison.
- Why an action is unavailable: reserved, bound, in use, insufficient quantity, permission denied, or destination full.
- The result of bulk actions before confirmation when they would destroy or irreversibly transform valuable items.

Core controls:

- Search and switch typed tabs without losing the current useful context.
- Equip or bind a usable item without first placing it in a physical hotbar slot.
- Mark favorite, junk, or protected; filter by those states.
- Compare against the relevant equipped item or saved loadout.
- Deposit, withdraw, split a requested quantity, salvage, and undo a reversible local selection.
- Use controller/keyboard navigation without relying solely on color or hover.

States that require explicit treatment are empty, loading, no search results, partially resolved addon content, offline storage, stale revision, permission denied, overflow, and failed transaction. Large inventories use paging or virtualization and never create one UI element for every stored unit.

### Item-icon rendering contract

- Custom slots use the shared `BoundedItemStackRenderer`; inventory pages must not call `RenderItemstackToGui` directly.
- Nominal item-model size follows Vintage Story's own `unscaledItemSize / unscaledSlotSize` ratio so an item occupies the same visual proportion at every VRPG slot size.
- Every live item render is enclosed by a slot-local stacked scissor rectangle. The renderer's size argument is not a visual bound: unusually authored block and item models may exceed it.
- The clip is inset from the frame so item geometry cannot cover selection borders, adjacent slots, bindings, or nearby text.
- Quantity and state overlays use reserved UI layers rather than enlarging the item model. The same contract applies to the Home/Build palette and combat-console item profile.

## 10. Continuous opportunity without obligation

VRPG should always make worthwhile next actions visible, but it should not use time pressure or chore lists to manufacture retention.

After meaningful loot or a return to safety, the interface can surface at most a few contextual opportunities:

- **Immediate:** a new item enables a comparison, upgrade, skill interaction, or nearby optional room.
- **Chosen:** progress advanced the objective or reward family the player deliberately tracked.
- **Long-term:** a region, specialist, Rebirth goal, boss path, or base service is now closer or newly available.

These are leads, not mandatory notifications. They have no daily reset, login streak, expiring claim, or punishment for ignoring them. Duplicated notices collapse, completed opportunities disappear, and the player controls tracking. The world and loot should create the desire to continue; the UI merely makes real possibilities legible.

## 11. Death, extraction, and Rebirth

- Death penalties must not turn inventory recovery into repeated corpse-running chores.
- Expedition rules declare which carried rewards are secured, at risk, or recoverable before entry.
- Extraction atomically transfers secured aggregate quantities and unique entries to their destinations.
- A failed transfer preserves a recovery record; it never partially drops records through an unreported overflow.
- Rebirth preserves account-level storage and declared legacy collections while resetting only the character-bound inventory named in the Rebirth contract.
- Loadouts survive as references where possible and clearly identify missing or level-restricted entries in a new life.

## 12. Data and extension points

Provisional definitions and interfaces:

- `IPlayerItemStore`: query, insert, extract, reserve, commit, rollback, and revision access.
- `IStorageQueryService`: bounded search, filter, sort, and page over immutable summaries.
- `IStorageBridge`: materializes or accepts bounded Vanilla stacks for crafting, blocks, and external inventories.
- `InventoryCategoryDefinition`: tab, acceptance tags, ordering, capacity policy, and fallback.
- `ItemStoragePolicyDefinition`: unique, aggregate, ledger, protected, maximum quantity, overflow, and bridge rules.
- `LootPresentationDefinition`: pickup importance, feed grouping, beam/label policy, and comparison route.
- `DepositRuleDefinition`: destination, filters, exclusions, priority, and permission requirements.
- `OpportunityDefinition`: trigger, context, player-facing route, deduplication key, and dismissal policy.

Addons may add categories and policies, but removing or renaming a category must migrate existing entries or preserve them through the fallback tab.

## 13. Performance and authority budgets

- The server owns quantities, item identity, reservations, permissions, and transaction results.
- Pickup aggregation is batched per player and short time window.
- Queries are paged and bounded; sort/search use cached normalized fields and immutable snapshots.
- Clients receive initial pages plus revisions and deltas, not the complete store after every change.
- The UI virtualizes visible rows and icons and cancels obsolete searches.
- Persistence writes dirty records in bounded batches and uses a recoverable format with versioned migrations.
- Crafting reserves inputs before output creation so disconnects and concurrent requests cannot duplicate items.
- Metrics record entry count, query latency, serialized bytes, delta size, pickup batch size, and transaction failures.

## 14. Validation and acceptance tests

The first inventory slice is acceptable when:

- A new VRPG character has full fixed inventory access with no equipped backpack.
- Inventory replacement defaults on, defaults off cleanly into Vanilla ownership, and rejects an unsupported mixed-ownership configuration.
- Enabling or disabling replacement on a populated test world migrates items losslessly or refuses before mutation.
- An item can be used or bound from its typed tab without occupying a Vanilla hotbar slot.
- Ten thousand identical commodities appear as one responsive entry and survive save/load exactly.
- Attribute-different gear remains distinct, including deliberate hash-collision test cases.
- Missing addon items remain recoverable records and resolve when content returns.
- Bulk pickup, deposit, crafting withdrawal, extraction, and overflow are atomic under concurrent multiplayer requests.
- Searching and paging a large synthetic store do not send or instantiate the full entry list.
- Ordinary material collection does not force the player to abandon a representative expedition.
- The inventory shows no more than a small bounded set of contextual opportunities and none expire merely because the player logged out.

## 15. Open decisions

- Final category names and whether consumables have a separate unique-entry cap.
- Fixed gear-entry capacity and overflow behavior during expeditions.
- Exact eligibility rules for aggregate commodities in the Home Build catalog; their palette and bounded placement-bridge behavior are defined.
- Default auto-pickup radius and ownership duration in multiplayer.
- Whether base storage is per player, party, settlement, or supports all three namespaces.
- Whether a generic storage-index library should be extracted from VintageKinematics or independently implemented.
- Exactly which inventory portions reset, remain, or become legacy collections on Rebirth.
