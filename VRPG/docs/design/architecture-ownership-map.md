# VRPG High-Level Architecture and Ownership Map

Status: Authoritative target-state ownership map  
Scope: End-state total conversion; current code is transitional  
Index: [VRPG design index](README.md)

## 1. How to read this map

```text
+----------------------+  authoritative owner or bounded subsystem
| SYSTEM               |
+----------------------+

------ request ------->  asks an owner to validate or mutate state
<----- event/view -----  immutable result, event, or presentation model

[ENGINE]                 Vintage Story execution substrate
[VRPG]                   VRPG owns gameplay truth
[ADAPTER]                narrow bridge to Vintage Story or approved content
[CLIENT]                 presentation/input only; never final authority
[DISABLED]               blocked in the default VRPG profile
[TRANSITIONAL]           exists today but is not the target architecture
```

Ownership means one subsystem is the authoritative writer for a category of state. Other systems request changes or consume immutable views and events. They do not edit another owner's records directly.

## 2. Complete ownership picture

```text
                                      VRPG TOTAL CONVERSION
                                                |
        +---------------------------------------+---------------------------------------+
        |                                                                               |
        v                                                                               v
+-------------------------------+                                      +--------------------------------+
| VINTAGE STORY ENGINE [ENGINE] |                                      | SURVIVAL CONTENT SUBSTRATE     |
|-------------------------------|                                      | [ENGINE/ADAPTER]               |
| game loop and mod lifecycle   |                                      |--------------------------------|
| chunks and block storage      |                                      | selected models and shapes     |
| entities and physics          |                                      | textures, sounds, animations   |
| rendering, audio, input       |                                      | approved blocks for Home       |
| networking transport          |                                      | appearance/voice machinery     |
| GUI primitives and assets     |                                      | selected weather presentation  |
| savegame byte storage         |                                      | bounded block-entity mechanics |
+---------------+---------------+                                      +----------------+---------------+
                |                                                                       |
                | engine adapters only                                                  | audited adapters only
                +-----------------------------------+-----------------------------------+
                                                    |
                                                    v
                     +-----------------------------------------------------------+
                     | VRPG BOOTSTRAP AND COMPOSITION ROOT [VRPG]                |
                     |-----------------------------------------------------------|
                     | loads vrpg-default preset and world-creation choices      |
                     | resolves server overrides and supported addons            |
                     | validates dependencies, conflicts, and save compatibility |
                     | creates one immutable VrpgRuntimeProfile                  |
                     | publishes one owner for every major capability            |
                     +------------------------------+----------------------------+
                                                    |
                              +---------------------+---------------------+
                              |                                           |
                              v                                           v
             +----------------------------------+        +----------------------------------+
             | DEFINITION REGISTRY [VRPG]       |        | CAPABILITY REGISTRY [VRPG]       |
             |----------------------------------|        |----------------------------------|
             | data definitions and schemas     |        | inventory owner                  |
             | namespace and override rules     |        | character-progression owner      |
             | tags and explicit references     |        | worldgen and mutation owner      |
             | content coverage validation      |        | realm and housing availability   |
             | registered new behavior verbs    |        | combat/death/crafting rules       |
             +----------------+-----------------+        | addon and compatibility status   |
                              |                          +----------------+-----------------+
                              | immutable definitions                     |
                              +---------------------+---------------------+
                                                    |
                                                    v
+---------------------------------------------------------------------------------------------+
| CREATURE INGESTION [ADAPTER -> VRPG]                                                        |
|---------------------------------------------------------------------------------------------|
| discovers compatible foreign creature assets | generates synthetic VRPG-owned actor types  |
| normalizes body/animation descriptors         | assigns VRPG stats, AI, abilities and tags  |
| validates and quarantines unsafe candidates   | never imports foreign AI, drops or spawning |
+-----------------------------------------------+---------------------------------------------+
                                                    |
                                                    v
 +---------------------------------------------------------------------------------------------+
 | CROSS-CUTTING VRPG SERVICES                                                                 |
 |---------------------------------------------------------------------------------------------|
 | +-------------------------+ +----------------------+ +----------------------+                |
 | | Persistence Coordinator | | Stable Identity     | | Deterministic Random |                |
 | |-------------------------| |----------------------| | and Game Clock       |                |
 | | schema versions         | | players, realms     | |----------------------|                |
 | | transactions/revisions  | | sites, entries      | | named random streams |                |
 | | backup and migration    | | expeditions, items  | | simulation time      |                |
 | | recovery records        | | tombstones          | | replayable seeds     |                |
 | +-------------------------+ +----------------------+ +----------------------+                |
 |                                                                                             |
 | +-------------------------+ +----------------------+ +----------------------+                |
 | | Permission Service      | | Telemetry/Debug      | | Network Authority    |                |
 | |-------------------------| |----------------------| |----------------------|                |
 | | player/group roles      | | budgets and metrics  | | request identity     |                |
 | | ownership and access    | | traces and overlays  | | validation/revisions |                |
 | | admin/recovery scope    | | audit history        | | immutable responses  |                |
 | +-------------------------+ +----------------------+ +----------------------+                |
 +-----------------------------------------------+---------------------------------------------+
                                                 |
                +--------------------------------+--------------------------------+
                |                                |                                |
                v                                v                                v
 +------------------------------+ +------------------------------+ +------------------------------+
 | REALM RUNTIME [VRPG]         | | MAIN-WORLD DOMAIN [VRPG]     | | PLAYER DOMAIN [VRPG]         |
 |------------------------------| |------------------------------| |------------------------------|
 | logical RealmId registry     | | region/progression graph     | | VRPG character identity      |
 | physical dimension allocator | | custom terrain generation    | | class and life state         |
 | plot/instance allocation     | | slab-smoothed routes         | | stats/resources              |
 | preparation and streaming    | | landmarks and sites          | | talents and loadouts         |
 | transit and safe anchors     | | non-mining resource sites    | | equipment state              |
 | lifecycle and quarantine     | | semantic world outcomes      | | Rebirth history              |
 | isolation and cleanup        | | protected terrain            | | account/world unlock refs    |
 +---------------+--------------+ +---------------+--------------+ +---------------+--------------+
                 |                                |                                |
                 | where                          | initial state                   | actor state
                 +----------------------+---------+----------------------+---------+
                                        |                                |
                                        v                                v
                         +------------------------------+ +------------------------------+
                         | WORLD MUTATION [VRPG]        | | INVENTORY/STORAGE [VRPG]     |
                         |------------------------------| |------------------------------|
                         | sole mutation authorization  | | exact unique item custody    |
                         | protected/contextual classes | | aggregate long quantities    |
                         | persistent outcome apply     | | typed tabs and capacity      |
                         | temporary-terrain journal    | | reservations/transactions    |
                         | rollback and exact cleanup   | | overflow and recovery        |
                         | build-access delegation      | | query, paging, persistence   |
                         +---------------+--------------+ +---------------+--------------+
                                         |                                |
                     +-------------------+-------------------+            |
                     |                                       |            |
                     v                                       v            v
      +------------------------------+        +------------------------------+   +-------------------------+
      | MAIN-WORLD INTERACTIONS      |        | HOUSING DOMAIN [VRPG]        |   | ITEM/REWARD DOMAIN      |
      | [VRPG]                       |        |------------------------------|   | [VRPG]                  |
      |------------------------------|        | Home allocation and owners   |   |-------------------------|
      | doors, levers, salvage       |        | housing plots and bounds     |   | item generation         |
      | resource interactions        |        | build permissions            |   | affixes/rarity/uniques  |
      | objectives and world sites   |        | build catalog and refunds    |   | reward-pool selection   |
      | repaired/captured outcomes   |        | stations and storage links   |   | loot ownership          |
      +------------------------------+        | visitors and shared homes    |   | salvage valuation       |
                                              +---------------+--------------+   +------------+------------+
                                                              |                               |
                                                              v                               v
                                              +--------------------------------------------------+
                                              | ECONOMY/TRANSFORMATION [VRPG]                    |
                                              |--------------------------------------------------|
                                              | recipes and material transformations             |
                                              | crafting reservations and output                 |
                                              | NPC demand, pricing, buying and selling           |
                                              | sources, sinks, inflation and duplicate controls |
                                              +------------------------+-------------------------+
                                                                       |
                                                                       v
 +---------------------------------------------------------------------------------------------+
 | ACTIVITY AND COMBAT PLANE [VRPG]                                                            |
 |---------------------------------------------------------------------------------------------|
 |                                                                                             |
 | +-------------------------+       requests encounter       +------------------------------+ |
 | | Activity/Expedition     |------------------------------->| Encounter Director           | |
 | |-------------------------|                                |------------------------------| |
 | | planning and objective  |<-------- state/events ---------| roles, waves and objectives  | |
 | | seed and instance       |                                | spawn and threat budgets     | |
 | | rooms and branches      |                                | squad composition            | |
 | | run resources/checkpoint|                                | completion and reward intent | |
 | | extraction/failure      |                                +--------------+---------------+ |
 | | reconnect and cleanup   |                                               |                 |
 | +------------+------------+                                               | spawn/commands  |
 |              |                                                            v                 |
 |              | realm request                               +------------------------------+ |
 |              +-------------------------------------------->| ENEMY AI/NAVIGATION [VRPG]   | |
 |                                                           |------------------------------| |
 |                                                           | perception and memory        | |
 |                                                           | intent and role behavior     | |
 |                                                           | squads and formation claims | |
 |                                                           | local steering and paths     | |
 |                                                           | stuck/inaccessible recovery  | |
 |                                                           +--------------+---------------+ |
 |                                                                          | ability requests |
 |                                                                          v                  |
 | +-------------------------+      cast/attack requests      +------------------------------+ |
 | | PLAYER INPUT/MOVEMENT   |------------------------------->| COMBAT SIMULATION [VRPG]     | |
 | |-------------------------|                                |------------------------------| |
 | | aim and target preview  |<------ results/reconcile ------| costs and cast state         | |
 | | walk and burst requests |                                | targeting and hit resolution | |
 | | loadout commands        |                                | damage/armor/healing         | |
 | +-------------------------+                                | status/stack/propagation     | |
 |                                                            | crowd control and movement   | |
 | +-------------------------+      effect definitions        | defeat and reward triggers   | |
 | | SKILLS/EFFECTS [VRPG]   |------------------------------->| authoritative operation      | |
 | |-------------------------|                                +---------------+--------------+ |
 | | reusable skill grammar  |                                                |                |
 | | projectiles/areas/mines |                                                | mutation req   |
 | | delayed payoff/detonate |                                                v                |
 | | spreadable statuses     |                                +------------------------------+ |
 | | temporary terrain       |------------------------------->| TEMPORARY TERRAIN [VRPG]    | |
 | +-------------------------+                                |------------------------------| |
 |                                                            | placement envelope           | |
 |                                                            | health/duration/collision    | |
 |                                                            | local nav invalidation       | |
 |                                                            | destruction/exact cleanup   | |
 |                                                            +------------------------------+ |
 +---------------------------------------------------------------------------------------------+
                                                 |
                                      authoritative events/views
                                                 |
                                                 v
 +---------------------------------------------------------------------------------------------+
 | CLIENT PRESENTATION [CLIENT]                                                               |
 |---------------------------------------------------------------------------------------------|
 | combat HUD and targeting previews  | skills/loadouts/talents | inventory/equipment          |
 | enemy bars, statuses and telegraphs | expedition planner/map  | Home/build mode               |
 | item comparison and rewards         | Rebirth confirmation    | settings/accessibility        |
 | combat text, particles, ragdolls    | audio and aftermath      | actionable errors/recovery    |
 |---------------------------------------------------------------------------------------------|
 | Sends intentional requests. Predicts only approved local movement/presentation.             |
 | Never declares hits, items, progression, world mutations, realm travel, or transactions.    |
 +---------------------------------------------------------------------------------------------+
```

## 3. Realm and space ownership

```text
                         +------------------------------+
                         | REALM RUNTIME                |
                         | owns: where and lifecycle    |
                         +---------------+--------------+
                                         |
                  +----------------------+----------------------+----------------------+
                  |                      |                      |                      |
                  v                      v                      v                      v
       +----------------------+ +----------------------+ +----------------------+ +----------------------+
       | MAIN REALM           | | HOUSING REALM        | | EXPEDITION REALMS    | | RECOVERY/TEST REALMS |
       |----------------------| |----------------------| |----------------------| |----------------------|
       | physical dim 0       | | persistent plots     | | pooled instances     | | known-safe minimal   |
       | custom worldgen      | | building allowed     | | encounter lifecycle  | | deterministic tests  |
       | protected terrain    | | owner/visitor rules  | | temporary terrain    | | fault recovery       |
       +----------+-----------+ +----------+-----------+ +----------+-----------+ +----------+-----------+
                  |                        |                        |                        |
                  v                        v                        v                        v
       Main World Domain       Housing Domain             Activity/Encounter       Recovery/Admin
       owns generated meaning  owns player construction   owns run meaning         owns evacuation plan

       World Mutation is the shared gate for every lasting or temporary block change.
       Realm Runtime does not decide whether a player may build or whether a skill may make a wall.
       It resolves the realm and lifecycle; World Mutation asks the relevant domain policy.
```

## 4. State ownership table

| State or decision | Authoritative owner | May request/consume | Must not own it |
|---|---|---|---|
| Effective feature set | Bootstrap/runtime profile | Every subsystem | Individual patches and GUIs |
| Definitions and references | Definition registry | Domain services and addons | Runtime entity instances |
| Save versions and recovery transaction | Persistence coordinator | Every stateful owner | UI and individual packets |
| Logical realm identity/address | Realm runtime | Housing, expeditions, world, recovery | Dimension-specific content |
| Realm entry/exit | Realm transition service | Activities, Home UI, recovery | Portals teleporting directly |
| Initial main-world terrain | Main-world generator | Realm generation service | Encounter or housing code |
| Whether terrain may change | World mutation service | Skills, sites, housing, admin | Blocks deciding independently |
| Home ownership/build rights | Housing and permission services | Build UI, storage, approved adapters | Vanilla claims widening access |
| Expedition lifecycle | Activity/expedition service | Planner, encounter, realm, rewards | Individual rooms or enemies |
| Encounter composition | Encounter director | Expedition and region definitions | AI actors spawning arbitrary allies |
| Foreign creature admission | Creature ingestion/definition registry | Encounter, AI, combat, presentation | Source entity class or spawn system |
| Enemy intent/movement request | AI/navigation | Encounter and combat simulation | Client animation |
| Hit, damage, status, death | Combat simulation | Skills, AI, encounters, presentation | Clients, items, projectiles alone |
| Temporary wall state | World mutation journal | Skill/combat/AI/navigation | Ordinary building system |
| Reward selection | Item/reward domain | Encounter/activity/economy | Inventory inventing rewards |
| Item custody and quantity | Inventory/storage | Rewards, economy, housing, Rebirth | UI slot widgets |
| Craft/sale price and transform | Economy/transformation | Housing stations, NPCs, inventory | Blocks consuming items directly |
| Character class/stats/talents | Player domain | Combat, UI, item requirements | Vanilla classes and traits |
| Rebirth orchestration | Rebirth service in player domain | Persistence, inventory, activity, housing | A reset command editing stores directly |
| Faction/world outcome | Region/social domain | Activities, NPCs, world mutation | Dialogue scripts writing blocks |
| Visual/audio response | Client presentation | Authoritative events and definitions | Presentation creating gameplay results |
| Addon eligibility | Compatibility/capability registry | Definition registry and adapters | Addons self-authorizing conflicts |

## 5. Critical request flows

### 5.1 Skill cast

```text
Client input
    |
    v
Skill request packet
    |
    v
Combat Simulation
    |-- validate runtime profile, player state, cost, cooldown and target
    |-- reserve resources
    |-- execute cast/effect operations
    |-- request World Mutation if temporary terrain is involved
    |-- resolve damage/status/defeat
    |-- ask Reward Domain for eligible rewards
    |-- ask Inventory to commit awarded custody
    `-- emit immutable results to presentation
```

### 5.2 Enter expedition

```text
Planner request
    |
    v
Activity Service
    |-- validates objective, party, loadout and cost
    |-- creates expedition state and deterministic seed
    v
Realm Runtime
    |-- allocates logical instance
    |-- prepares/generates physical space under budget
    |-- records safe return anchor
    v
Activity + Encounter Director
    |-- populate rooms/objectives/enemies
    |-- validate readiness
    v
Realm Transition
    |-- move eligible party and confirm arrival
    `-- commit active expedition or recover safely
```

### 5.3 Complete encounter and receive loot

```text
Combat Simulation declares encounter-relevant defeats
    |
    v
Encounter Director declares completion
    |
    v
Activity Service resolves branch/run outcome
    |
    v
Reward Domain selects rewards from authored pools
    |
    v
Inventory accepts unique entries and aggregate quantities atomically
    |
    +--> overflow/recovery destination if required
    `--> concise loot and opportunity view for client
```

### 5.4 Enter Home and process materials

```text
Home request
    |
    v
Housing Service validates owner/visitor role and travel eligibility
    |
    v
Realm Runtime prepares Home and performs recoverable transit
    |
    v
Build or station request
    |-- Housing validates bounds/permissions/catalog
    |-- Inventory reserves materials
    |-- World Mutation applies building change OR Economy applies transform
    |-- Persistence Coordinator commits/recover records
    `-- client receives updated world/inventory result
```

### 5.5 Rebirth

```text
Player confirms fully previewed Rebirth
    |
    v
Rebirth Service creates transaction plan
    |-- eligibility and Legacy choice
    |-- character reset set
    |-- retained account/world/housing set
    |-- inventory/loadout handling
    |-- alternate new-life start
    v
Persistence Coordinator snapshots and commits owner changes
    |
    +--> success: publish new life and presentation views
    `--> failure: restore or resume from recovery record
```

## 6. Addon and compatibility boundary

```text
                          +---------------------------+
                          | COMPATIBILITY REGISTRY    |
                          +-------------+-------------+
                                        |
                  +---------------------+---------------------+
                  |                     |                     |
                  v                     v                     v
       +---------------------+ +---------------------+ +---------------------+
       | VRPG-NATIVE ADDON   | | CREATURE CONTENT   | | HOME-CONTENT        |
       |---------------------| |---------------------| |---------------------|
       | definitions         | | visual/audio assets | | allowlisted blocks  |
       | registered verbs    | | synthetic actor     | | audited stations    |
       | migrations          | | VRPG rules replace  | | housing-only scope  |
       | declared capability | | profile/validation  | | bounded adapters    |
       +----------+----------+ +----------+----------+ +----------+----------+
                  |                       |                       |
                  v                       v                       v
          validated and owned    auto/profile-adapted        admitted only through
          extension points       and validation-gated        Housing/Inventory APIs

       +---------------------+
       | FOREIGN GAMEPLAY    |
       |---------------------|
       | Survival owners     |
       | arbitrary patches   |
       | direct world writes |
       +----------+----------+
                  |
                  v
            [UNSUPPORTED]
          strict mode blocks
```

Addons add definitions or narrow implementations. They do not acquire authority over inventory, combat, progression, world mutation, realms, persistence, or configuration simply by loading.

## 7. Default-disabled Survival ownership

```text
+---------------------------------------------------------------------------------------------+
| DISABLED OR REPLACED IN vrpg-default                                                       |
|---------------------------------------------------------------------------------------------|
| [DISABLED] hunger, satiety, nutrition categories, starvation and hunger-rate modifiers     |
| [DISABLED] player body-temperature punishment and seasonal preparation requirements        |
| [REPLACED] Vanilla class mechanics -> rethemed appearance + VRPG starter-kit onboarding    |
| [DISABLED] backpack-gated capacity and backpack equipment ownership                         |
| [DISABLED] ordinary mining/resource progression in the protected main world                 |
| [DISABLED] ordinary placement, breaking, chiseling, liquids and fire outside Home           |
| [DISABLED] Survival durability/replacement chores                                           |
| [DISABLED] Survival technology progression as the route into combat                         |
| [DISABLED] Survival healing, armor and weapon rules where VRPG owns the result               |
| [DISABLED] unrelated temporal interruptions, tutorials and handbook paths                   |
| [DISABLED] broad foreign Survival-mod compatibility                                         |
|                                                                                             |
| [RETAINED] engine physics, chunks, entities, rendering, networking, UI primitives and saves |
| [RETAINED] useful art/audio assets and audited Home building capability                     |
| [REFRAMED] weather, temporal themes, food assets, creatures, stations and world structures  |
+---------------------------------------------------------------------------------------------+
```

## 8. Client/server boundary

```text
CLIENT                                                        SERVER
------                                                        ------
input intent ---------------- request id/revision ------------> domain owner
prediction: local movement/preview                             validates all mutations
pending/disabled interaction state <------- result ----------- commits authoritative state
immutable view caches <-------------------- event/delta ------- publishes relevant changes
particles/audio/ragdolls                                      owns damage and dangerous aftermath
search/filter local cached page                               owns inventory query and permissions

Client never owns:
hits | drops | item quantities | stats | progression | world changes | travel | economy | Rebirth
```

## 9. Suggested target project boundaries

```text
VRPG/
|-- src/
|   |-- Core/
|   |   |-- Runtime/             preset, capabilities, composition root
|   |   |-- Persistence/         revisions, migrations, backup, recovery
|   |   |-- Data/                definitions, registries, validation
|   |   `-- Observability/       metrics, traces, admin inspection
|   |-- Realms/                  owned dimension/instance runtime
|   |-- Worlds/
|   |   |-- Generation/          graph, terrain, routes, sites, slabs
|   |   `-- Mutation/            protection, outcomes, temporary terrain
|   |-- Combat/
|   |   |-- Simulation/          cast, hit, damage, status, defeat
|   |   |-- Skills/              reusable effect grammar
|   |   |-- Movement/            player and actor movement profiles
|   |   `-- AI/                  perception, tactics, squads, navigation
|   |-- Activities/
|   |   |-- Expeditions/         lifecycle, rooms, choices, extraction
|   |   `-- Encounters/          composition, roles, waves, objectives
|   |-- Characters/              class, stats, talents, loadout, Rebirth
|   |-- Items/
|   |   |-- Inventory/           custody, aggregation, query, overflow
|   |   |-- Rewards/             generation and targeting
|   |   `-- Economy/             crafting, salvage, prices, NPC demand
|   |-- Housing/                 Home, building, permissions, stations
|   |-- Social/                  factions, NPCs, parties, settlements
|   |-- Client/                  HUD, screens, view models, visuals, audio
|   `-- Integration/
|       |-- VintageStory/         engine adapters and Harmony boundaries
|       |-- CreatureContent/       discovery, normalization, synthetic actors
|       `-- HomeContent/          explicitly supported content adapters
|-- assets/                       VRPG data and presentation assets
|-- docs/design/                  contracts and ownership decisions
`-- tests/
    |-- Pure/                     engine-independent domain tests
    |-- Content/                  schemas and coverage
    |-- Generative/               world/expedition seed properties
    |-- Integration/              engine adapter and save tests
    |-- Scenarios/                headless multiplayer/failure tests
    `-- Performance/              budgets and soak tests
```

The exact assemblies may be fewer initially. These are dependency boundaries, not a requirement to create an empty project for every folder.

## 10. Transitional implementation map

```text
CURRENT                                           TARGET
-------                                           ------
VRPGModSystem composition                         explicit composition root/runtime profile
RpgModule owns several unrelated systems          focused domain services
DungeonModule + reflective Manifold adapter       owned VRPG.Realms service
ManifoldWorldgenProxyFactory                       compile-time internal generation contract
RequireManifold config with no enforcement        removed after parity
overworld breach fallback                          explicit activity/realm policy
Vanilla player/character/inventory assumptions    audited single-owner replacements
individual JSON validators                         unified registry and coverage report
ad hoc save files per feature                      coordinated version/migration/recovery
```

Transitional code may remain while a target owner is being proven. It must be labeled, isolated behind an adapter, and removed after parity rather than becoming a second permanent owner.

## 11. Ownership invariants

The architecture is valid only while these remain true:

1. Exactly one effective owner exists for every gameplay capability.
2. Only server domain owners commit gameplay mutations.
3. Presentation never becomes the source of truth.
4. Engine and Survival code are substrates or adapters, not silent competing rule owners.
5. Realm Runtime owns where and lifecycle; domain services own what a space means.
6. World Mutation gates all block changes; Housing and Skills supply the relevant policy.
7. Inventory owns custody; Rewards and Economy request transfers rather than editing slots.
8. Combat Simulation owns hit/damage/status/defeat order; AI and Skills request operations.
9. Persistence coordinates transactions, while each domain owns the meaning and schema of its state.
10. Addons extend through definitions and registered verbs; they do not bypass owners. Compatible creature packages are sources for synthetic VRPG actors, not alternate AI/combat owners.
11. Every cross-owner request has rejection, retry/idempotence, and recovery behavior.
12. Every transitional owner has a named removal gate.
