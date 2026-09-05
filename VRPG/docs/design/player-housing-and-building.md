# Player Housing and Building

Status: Adopted gameplay boundary; realm backend and progression details remain provisional  
Parent: [Complete game design](complete-game-design.md)  
Index: [VRPG design index](README.md)

## 1. Purpose

Vintage Story's building is a valuable creative capability, but unrestricted construction conflicts with VRPG's combat-space, navigation, and anti-cheese rules. VRPG therefore moves player construction into a persistent **Home**: a private or shared housing realm designed for building, material processing, storage, display, and later housing progression.

Home is a secondary progression layer. It supports the combat game without becoming the required activity between every expedition.

The initial resource loop is:

```text
Fight and explore
      ↓
Bring home materials and discoveries
      ↓
Build, process, craft, display, or prepare trade goods
      ↓
Use the result in a chosen activity or sell it to an NPC
```

## 2. Adopted building boundary

- Player block placement, breaking, chiseling, liquid placement, ignition, and other lasting terrain modification are disabled by default outside authorized housing space.
- The normal world and expeditions remain authored combat and exploration spaces. Players interact with doors, objectives, loot, switches, and designated contextual objects without gaining general construction access.
- Home enables the supported Survival building capability inside a clear ownership boundary.
- World generation, encounter generation, scripted transformations, administrators, and explicitly authorized abilities use separate permissions from player building.
- Temporary combat constructs may be entities, actual temporary blocks, or designated transient objects governed by skill and mutation rules. They do not grant permanent block modification or housing access.
- A denied action explains the rule in player language: **Building is available at Home.** A return action is offered only when travel is currently legal.

This is the **Reframe** disposition under the Survival-system gate: retain the expressive capability and assets, replace where and why the player uses them.

## 3. Housing-realm abstraction

Gameplay depends on `IPlayerHousingService`, not on one dimension implementation. A home record has a stable ID, owner namespace, access policy, entry and return anchors, build bounds, template/version, progression state, and storage link.

The end-state backend is VRPG's owned realm runtime. The preferred prototype is one persistent housing realm subdivided into widely separated protected logical plots. It preserves dimension isolation without requiring an unbounded engine dimension per player and can support individual, party, or settlement homes. A dedicated physical dimension per home remains an option only after lifecycle, streaming, entity, map, weather, compatibility, and save-migration tests.

Vintage Story already exposes dimension-aware block and entity positions, dimension-change events, and custom-dimension infrastructure. However, parts of the engine may still assume the normal dimension. Players are promised a persistent isolated Home, not one physical dimension arrangement. See [Realm runtime architecture](realm-runtime-architecture.md).

## 4. Entry, exit, and safety

- The player enters Home from a stable hub portal, menu action, or physical access point.
- Entering Home is unavailable during active combat, a committed boss encounter, a downed state, or an expedition that has not reached a legal extraction point.
- Returning exits to a recorded safe anchor, never to an unchecked combat coordinate.
- Travel transfers the player, relevant companions, and allowed carried state atomically. Mounts, projectiles, hostile entities, temporary hazards, and active encounter state do not cross accidentally.
- Disconnecting inside Home returns the player there when safe and supported; recovery logic handles a missing or corrupt home without trapping the character.
- Home has no hostile invasion, offline decay, rent, hunger pressure, or mandatory upkeep by default.
- A validated Home may host first arrival once Home allocation and recovery are
  proven. Until then, character onboarding uses a tiny private instance and does
  not depend on housing persistence being available.

## 5. Material transformation

Enemy and expedition rewards can include materials with several clear uses:

- Craft equipment, consumables, skill components, or expedition supplies.
- Modify or salvage existing equipment.
- Construct housing blocks, furniture, stations, storage, and displays.
- Process materials into trade goods with predictable baseline NPC value.
- Fulfill optional NPC requests for targeted rewards, relationships, cosmetics, or services.

Processing should create decisions rather than waiting chores. A station may ask which output, quality, catalyst, affix family, or buyer the player wants; it should not require babysitting fuel, repeating tiny transfers, or waiting through real-time timers by default.

Common materials flow directly from the player's aggregate inventory or connected Home storage into an authorized recipe. The player does not need to split stacks into a crafting grid or carry ingredients between adjacent stations.

Removal and remodeling use a documented refund policy. Cosmetic experimentation should be generous; progression stations and economic transformations must prevent duplication exploits.

### 5.1 Home Build Mode and material palette

Building does not require restoring the Survival inventory hotbar. The unified combat action bar has a Home context and an explicit Build Mode:

- Entering Home swaps the Adventure profile for the player's saved Home profile and visibly labels the transition.
- Entering Build Mode swaps the action positions to a saved palette of up to ten favorite block or placeable-material references.
- The palette is not storage. Every position points to an entry in the canonical VRPG item store and displays its authoritative available quantity.
- The Inventory Build view is the complete searchable material catalog. Players can select any available material there and pin frequently used materials to palette positions.
- Pick-block selects or assigns the targeted material only when the player owns it and policy permits it. It never creates a stack.
- Exiting Build Mode restores the Home profile exactly; leaving Home restores the Adventure profile exactly.
- Health and combat resources may visually compress during safe building, making room for selected material, quantity, orientation, placement validity, and local rejection feedback.

Placement remains server-authoritative:

```text
select palette/material reference
             |
             v
client placement intent + request/revision identity
             |
             v
server validates Home, bounds, permission, target, collision and quantity
             |
             v
reserve bounded material through IStorageBridge
             |
             v
invoke supported block/item placement behavior
       | success                 | rejection/failure
       v                         v
commit consumed amount      rollback reservation
       |                         |
       `------ reply + inventory/world delta ------'
```

The bounded bridge permits compatible Vanilla and modded blocks to execute their placement rules without turning a temporary `ItemStack` into the source of truth. Breaking follows the corresponding authorized removal/refund transaction and routes recovered material directly to canonical storage unless a specific block defines a different safe outcome.

## 6. Housing progression principles

The detailed progression tree is deliberately deferred, but later additions must follow these boundaries:

- Expand expression, convenience, collection, and strategic options before raw mandatory combat power.
- Unlock new station functions, NPC relationships, display systems, templates, architectural sets, storage rules, and social permissions.
- Let combat discoveries create new things to build or process, and let Home expose new combat opportunities.
- Avoid upkeep meters, daily collections, offline production pressure, and optimal rows of duplicated machines.
- Never require a player to rebuild the same essential stations after Rebirth.
- Preserve a functional compact home; larger construction is chosen expression rather than required walking distance.

Possible future layers include room or plot expansion, station specialization, visiting specialists, trophy effects, training spaces, saved build templates, and shared guild or settlement housing. None are adopted until their decisions and chore cost are designed.

## 7. NPC trade role

NPC selling converts selected drops into economic choices rather than automatic vendor trash.

- Materials have understandable baseline uses and values before the player commits them.
- Buyers may prefer categories, regions, processing methods, or finished trade goods.
- Targeted requests offer choices and variety but do not expire on daily timers by default.
- The best buyer may vary, but finding them should not require checking every NPC after every run.
- Bulk sale supports preview, protection rules, favorites, and a concise result.
- Quest, loadout, unique, and protected items never enter bulk sale by default.

Selling is one valid outlet alongside crafting, housing, collection, and future use. The economy must not make every combat drop feel like currency with extra clicks.

## 8. Build access and authority

All lasting world mutations pass through a server-authoritative build-access service. It answers the operation, actor, position, realm, ownership, and current activity state.

The service distinguishes:

- Place and break.
- Chisel or microblock edit.
- Liquid placement and removal.
- Fire and other block transformations.
- Container, station, door, and decoration use.
- Wiring, mechanical networks, and multiblock assembly.
- Temporary skill constructs.
- Administrator, worldgen, recovery, and scripted operations.

Checking only ordinary block placement is insufficient because Survival content modifies terrain through multiple interaction paths. API events, claims, asset configuration, and Harmony patches may all be required, but they must consult the same effective permission rather than implement conflicting rules independently.

Housing bounds and permissions are resolved before Vanilla claims. Vanilla claims may provide an additional restriction; they never widen VRPG's allowed building area.

## 9. Multiplayer ownership

A housing owner namespace may represent a player, party, or settlement. Permissions are role-based and explicit:

- Visit.
- Use doors, seating, and displays.
- Use storage or stations.
- Place and remodel.
- Manage permissions and recovery.

Visitors cannot carry away blocks, storage contents, trophies, or station outputs without the corresponding permission. Concurrent building and storage operations are server-authoritative. Ownership survives player renames and uses stable player or group IDs.

If the owner leaves a server or a group dissolves, a configurable recovery policy archives, transfers, or freezes the home; it never silently deletes it.

## 10. Home and build-mode screen contracts

### Screen: Home

- **Player goal:** travel home or manage access to an existing home.
- **Primary action:** Enter Home or Return.
- **Required information:** home name, owner, current travel availability, destination, and the reason travel is blocked.
- **Secondary controls:** visitors, permissions, recovery, and details.
- **Excluded information:** dimension IDs, plot coordinates, chunk state, and backend health.
- **Pending state:** prevent repeated travel while the server prepares and validates the destination.
- **Error state:** state what is unavailable and offer a safe return or retry; preserve diagnostics in logs.

### Screen: Build mode

- **Player goal:** choose and place, remove, or alter a supported building object.
- **Primary action:** confirm the current preview.
- **Required information:** selected object, quantity available, placement validity, material result, ownership bounds, and concrete failure reason.
- **Secondary controls:** category, search, rotation, variant, replace, copy, undo where safe, and exit.
- **Excluded information:** raw block IDs, permission flags, transaction revisions, and backend coordinates.
- **Success feedback:** immediate world change and updated material quantity.
- **Rejected state:** keep the preview and explain the correction, such as **Outside your build area** or **This block is not available in Home**.

Build mode uses the player's resolved bindings, supports keyboard and mouse paths, and never depends only on color to distinguish valid placement.

## 11. Configuration

Housing is enabled by default in the `vrpg-default` preset. The major construction rule is a mode, not scattered booleans:

- `HousingOnly` — default; player building is allowed only within authorized homes.
- `Everywhere` — compatibility/sandbox mode; ordinary world building is restored where other permissions allow it.
- `Disabled` — player building and housing progression are unavailable.

Sub-options tune plot dimensions, owner types, visitor permissions, material refunds, allowed block or station tags, entry routes, storage integration, and backend preference.

If Housing is disabled while recipes, NPC requests, or progression require a Home-only station, startup validation must provide an alternate hub service or reject the configuration. Switching backend or ownership mode on a populated world requires a restart, a migration preview, and a recoverable snapshot.

## 12. Data and extension points

Provisional definitions and services:

- `IPlayerHousingService`: allocate, resolve, enter, leave, recover, and query capability.
- `IBuildAccessService`: authorize player and scripted world mutations.
- `IHousingBackend`: provision, load, archive, migrate, and resolve realm positions.
- `HousingDefinition`: bounds, template, owner policy, entry rules, storage link, and progression code.
- `BuildPermissionDefinition`: operation, realm, actor, block or item tags, and conditions.
- `HousingStationDefinition`: accepted inputs, transformations, storage scope, and service unlock.
- `NpcDemandDefinition`: accepted goods, pricing policy, reward, discovery, and refresh rule.
- `HousingProgressionDefinition`: costs, prerequisites, unlocks, and persistence scope.

Supported VRPG-native addons and audited Home-content adapters register blocks and stations through tags and capability contracts. General Survival-mod compatibility is not promised. Content must not assume dimension 0 or bypass build access by writing blocks directly on behalf of a player.

## 13. Performance and persistence

- Unoccupied homes do not keep chunks, block entities, mechanical networks, or AI ticking without a specific reason.
- Home allocation and loading are asynchronous and bounded; entry shows a truthful pending state.
- Persistent homes use stable IDs and versioned records independent of transient dimension numbers or plot coordinates.
- Plot separation, claims, entity partitioning, map data, weather, light, and scheduled block ticks are included in backend stress tests.
- Large inventories and stations use the indexed storage contracts rather than scanning nearby containers.
- Backups and migration preserve blocks, block entities, inventories, ownership, and return anchors together.

## 14. Acceptance tests

- A player cannot place, break, chisel, pour, ignite, or otherwise permanently modify ordinary combat terrain through supported Survival interactions.
- The same authorized interactions work inside the owner's Home and respect visitor roles.
- Temporary combat constructs function without granting permanent building access.
- Entering Home cannot escape an active encounter or bypass expedition extraction.
- A player can turn a representative enemy drop into a useful item or trade good without manual stack splitting or container shuffling.
- Home remains safe and unchanged after a long offline period.
- Housing-disabled configuration supplies validated alternative services or fails before world mutation.
- Backend migration either preserves a populated home exactly or refuses before changing ownership.
- Two simultaneously occupied homes do not see, hear, target, load, or mutate one another unless deliberately connected.

## 15. Open decisions

- Physical housing-dimension pool size, plot layout, and isolation distance inside the owned realm runtime.
- Individual homes only at first, or individual plus party ownership.
- Initial plot size, vertical limit, expansion rules, and template.
- Which Survival blocks and block entities are safe and useful in Home.
- Whether build mode wraps ordinary placement or supplies a separate catalog and material ledger.
- Exact processing, crafting, NPC demand, and housing progression systems.
- Whether housing is preserved per character, account, world, or group across Rebirth.
