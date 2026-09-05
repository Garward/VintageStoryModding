# Usable Player-Menu Replacement Checklist

Status: Active critical path  
Updated: 2026-09-05  
Parents: [Native player-menu implementation](native-player-menu-implementation-plan.md) · [Inventory, loot, and storage](inventory-loot-and-storage.md)  
Index: [VRPG design index](README.md)

This is the dependency-ordered checklist for replacing the menus owned by the
`inventorydialog` and `characterdialog` actions. It deliberately stops at a
usable first replacement. Migrating Stats, Skills, Talents, Library, and Options
into the same shell is follow-up work for this narrow gate and must not delay safe
item custody. The broader full-theme requirement is part of the
[boot-to-overhaul handoff milestone](testable-version-todo.md#0-boot-to-overhaul-handoff-milestone).

Terminology invariant: VRPG replaces the dialogs registered as owners of the
Inventory and Character semantic actions. It does not replace the physical `E`
or `C` keys, register substitute physical hotkeys, or rewrite player mappings.
`E` and `C` are only Vanilla's defaults; remapped actions must work unchanged.

## Current progress

| Step | Status | Evidence |
|---|---|---|
| 0. Existing groundwork | Complete | Navigation, capability, store, and renderer tests |
| 1. First-release item contract | Complete | Canonical record and classifier tests |
| 2. Authoritative store | Complete | Transaction, equipment, recovery, indexing, idempotency, and concurrency tests |
| 3. Persistence | Complete | Versioned dual-generation world-save archive with recovery tests |
| 4. `ItemStack` codec | Complete | Stable top-level/nested remapping, recovery, bounds, and audited content fixtures |
| 5. Migration and recovery | Complete | Staged source snapshot, restart-safe journal, dry run, rollback, and admin recovery |
| 5A. Character runtime authority | In progress | Hunger/nutrition/temperature verified; class step removed in a fresh-world run; movement-stat ownership and generic movement-effect runtime implemented; live movement/Evasive Step verification remains |
| 6. Engine item flows | In progress | Common all-or-nothing grant ingress implemented behind the experimental ownership flag; live coverage and materialization remain |
| 7. Combat and Home/Build loadouts | Not started | — |
| 8. Inventory network service | Not started | Capability packet only |
| 9. Native menu shell | Not started | Navigation scaffolding only |
| 10. Inventory and Equipment pages | Not started | — |
| 10A. Character creation and onboarding | In progress | Durable state/transition rules, appearance-only Vanilla bridge, validated starter definitions, experimental starter preview, and deterministic tavern preparation verified; grant, Welcome, live arrival, and protection remain |
| 11. Transactional semantic-dialog ownership | Not started | Pure replacement planning only |
| 12. Usable-replacement validation | Not started | — |

Update this table and the detailed checkbox together after each verified slice.

## Definition of usable

The first replacement is usable only when all of these statements are true:

- The configured Inventory action opens the VRPG Inventory page. The configured
  Character action opens that same page with Equipment expanded. Remapped keys
  work without VRPG knowing their physical key codes.
- The server is the sole authority for stored items and equipped items.
- Existing player items migrate without loss, duplication, or silent conversion.
- Pickups enter the VRPG store and never become the active combat hand.
- Players can inspect, equip, unequip, use, split, discard or recover every item
  needed for ordinary play; no required operation is trapped behind a removed
  Vanilla dialog.
- Inventory and equipment survive save/load, death, disconnect, reconnect, and
  server restart.
- Disabling the feature or failing startup validation leaves the untouched
  Vanilla Inventory and Character dialogs available.
- Adventure combat, Home/Build tools, and addon items all have an explicit item
  bridge or a safe rejection/recovery path.
- The hidden Vanilla Character systems do not remain active behind the new UI:
  Adventure has no hunger damage or nutrition health bonus, and VRPG exclusively
  owns class gameplay, walk speed, and jump height.
- A first-world character can customize appearance, choose one VRPG starter kit,
  receive exactly one weapon/armor/starting-skill grant, and dismiss a compact
  protected guide before normal gameplay begins.

## 0. Groundwork already present

- [x] Configuration has separate inventory-enabled and experimental-dialog flags.
- [x] The server sends versioned item-store and dialog-readiness capabilities.
- [x] Navigation maps Inventory, Character, and legacy Hub entry points without
  hard-coded keys.
- [x] Replacement planning requires exactly one owner for both semantic actions.
- [x] Thin replacement entry dialogs exist and own no item state.
- [x] The in-memory store supports exact-state aggregation, unique separation,
  checked `long` quantities, stable entry IDs, revisions, and bounded queries.
- [x] A shared bounded item renderer exists for custom hotbar and future inventory
  item models.

These foundations are not a functioning replacement. Vanilla still owns all
real items and both menu actions.

## 1. Freeze the first-release item contract

- [x] Define canonical record types for aggregate commodities, unique gear,
  consumables, keys/quest records, currency/ledgers, equipped entries, overflow,
  and unresolved addon content.
- [x] Define the required typed categories and a guaranteed Miscellaneous fallback.
- [x] Define which `ItemStack` attributes participate in exact identity and which
  are display-only or transient.
- [x] Define stable equipment roles, including main weapon, alternate weapons,
  offhand where supported, armor, accessories, and cosmetic-only appearance.
- [x] Define capacity separately for unique records and aggregated quantities.
- [x] Define explicit outcomes for unsupported or missing addon assets; never
  discard them because their item class cannot currently resolve.
- [x] Version the canonical schema before persistence begins. Inventory query and
  mutation packet versions remain part of step 8 because those packets do not
  exist yet.

Gate: representative Vanilla and addon stacks can be classified deterministically
without mutating them.

## 2. Complete the authoritative store

- [x] Add equipment, overflow, unresolved records, favorites/protection, and any
  reservation state to `IPlayerItemStore`.
- [x] Add reserve, commit, and rollback transactions for multi-step operations.
- [x] Make mutations idempotent through request IDs so retries cannot duplicate
  items.
- [x] Add atomic equip, unequip, recovery transfer, and confirmed destruction.
  Reservation snapshots plus commit/rollback are the store-side primitive for
  split, use, and drop; engine materialization remains step 6.
- [x] Add maintained location/category indexes, bounded search and explicit stable
  sort orders, with every immutable page tagged by its source revision.
- [x] Define one lock per stable player store; expected revisions serialize
  competing mutations while distinct players remain independent.
- [x] Test hash collisions, embedded-reference identity, `long` overflow, stale
  revisions, duplicate requests,
  partial failures, and concurrent mutations.

Gate: the store can model every first-release inventory operation in memory with
rollback and no Vintage Story GUI dependency.

Verified by 32 focused item-store tests and the complete 222-test suite.

## 3. Add durable per-player persistence

- [x] Store two rotating blobs under `vrpg:player-items:a` and
  `vrpg:player-items:b` in Vintage Story's world save. Each binary slot contains
  its format version, generation, payload length, SHA-256 checksum, and a
  gzip-compressed JSON archive.
- [x] Save schema version, store revision, entries, exact state, equipment,
  overflow, unresolved records, and completed request IDs/recovery metadata.
- [x] Capture only stores whose durable state changed. Write the inactive slot and
  mark captured stores clean only after success, leaving the last valid generation
  untouched. A mutation racing the save remains dirty for the next generation.
- [x] Load from `SaveGameLoaded` before the store can become ready. The public
  capability intentionally remains false until migration and engine-flow gates
  are also complete.
- [x] Preserve unknown archive, player, entry, reservation, and request fields;
  migrate the unversioned development shape explicitly and reject newer schemas.
- [x] Validate slot checksums and complete store invariants. Recover from the older
  valid generation after corruption or an interrupted write, but refuse activation
  when neither generation is trustworthy.
- [x] Test new player, ordinary save/load, restart, crash-shaped incomplete write,
  corrupt data, schema upgrade, and missing addon on reload.

Gate: a stress inventory and its equipment round-trip exactly through a server
restart.

Verified by 11 focused persistence tests and the complete 233-test suite.

## 4. Build the lossless `ItemStack` codec

- [x] Serialize item/block code, stack size, attributes, durability, temperature,
  perish state, contents, quality, and addon-defined attributes without relying
  on display text.
- [x] Recreate bounded Vanilla `ItemStack` instances only when an engine operation
  requires one.
- [x] Preserve unknown addon data byte-for-byte where the engine permits it,
  including stable remapping for recursively nested standard item-stack attributes.
- [x] Detect an unresolved top-level asset and return its intact recoverable payload
  instead of discarding it. Nested-stack recovery remains part of the full codec gate.
- [x] Centralize category and unique-versus-aggregate classification behind a
  registered policy so addons can override the automatic result.
- [x] Round-trip fixture stacks from Vanilla and the selected compatibility mods.
  Current fixtures cover a durable/heated blade, perishable food, a nested Vanilla
  backpack, Fantasy Creatures Reborn armor/material/backpack state, a Monsterpack
  creature item, and an Eidolon Unchained creature item across changed registry IDs.

Gate: encode/decode tests prove semantic and exact-state equality for every fixture.

## 5. Implement migration and recovery before ownership changes

- [x] Enumerate backpack, hotbar, character/equipment, offhand, mouse-held,
  crafting, and relevant transient inventories.
- [x] Produce a dry-run report with source counts, destinations, unresolved items,
  overflow, and blocking errors.
- [x] Reject migration while an item move is paused or a non-player container is
  open, with a direct instruction to finish or close it.
- [x] Save a recoverable pre-migration snapshot and prior VRPG store before clearing
  any source slot.
- [x] Import all sources into one staged store transaction, verify totals and exact
  state, persist it, then clear Vanilla sources as the final commit.
- [x] Record an idempotent migration version and durable `Prepared`, `Committed`,
  `RestorePending`, or `Restored` state so reconnect/restart cannot import the
  same items twice.
- [x] Provide `/vrpgitems scan`, `status`, `resume`, and confirmed `restore`
  administrator paths. Ordinary command output shows state and the next action;
  technical failure details remain in the server log.
- [x] Refuse activation if any item lacks a lossless destination or staged totals
  differ from the exact source identity totals.

Gate: forced failure at every migration step restores either the complete old
state or the complete new state, never a mixture.

Verified by 13 focused migration tests and the complete 246-test suite. Live
migration commit is deliberately not invoked automatically until step 11 can
transfer menu ownership in the same gate.

## 5A. Establish character runtime authority

This gate is specified in the [Survival-system ownership audit](survival-system-ownership-audit.md#31-runtime-character-stat-authority).

Implementation checkpoint: the hunger/nutrition/body-temperature slice is
verified in game. Movement reads for `walkspeed` and `jumpHeightMul` now resolve
from VRPG configuration in Adventure, and Vanilla class selection is normalized
to an inert compatibility class with no gear or trait effects. The reusable
movement-effect runtime and Evasive Step migration are also implemented. Automated
fresh-world testing confirms that only the appearance editor opens, its confirmation
advances the durable onboarding stage, and early spawn scaling no longer faults.
The 273-test suite passes; live movement and Evasive Step verification remains.

- [x] Disable satiety drain, the separate hunger `SlowTick`, starvation
  `ReceiveDamage`, camera shake/hurt feedback, and every hunger-derived combat
  side effect in Adventure/combat mode.
- [x] Disable nutrition-derived maximum-health bonuses and recalculation paths in
  Adventure/combat mode while preserving dormant data behind a realm adapter.
- [ ] Provide data-defined Home-only condition hooks for future bonuses, such as
  an optional maximum-satiety Home XP modifier, without reactivating Survival
  behavior or granting a combat bonus.
- [ ] Make VRPG the sole high-level owner of walk speed and jump height; neutralize
  Vanilla class, trait, hunger, and equipment modifier sources at the boundary.
- [ ] Finish Vanilla class/trait disablement, including prompts, commands, gear,
  recipes, stored modifiers, health effects, and movement effects. Preserve and
  retheme cosmetic appearance editing; replace the class step with the one-time
  VRPG starter-kit flow rather than deleting character creation.
- [x] Make body temperature completely inert in Adventure: no simulation, room or
  clothing scans, wetness/freezing accumulation, frost damage, cold animation,
  frost overlay, movement effect, or stale watched-state feedback. Retain only a
  dormant realm-aware compatibility hook for explicitly authored future content.
- [ ] Add the reusable VRPG movement-effect boundary for skill impulses, launches,
  velocity blending, collision grace, and reconciliation, informed by the
  Vintage Kinematics trebuchet and belt-rider implementations.
- [ ] Pass transition, save/load, respawn, Rebirth, and multiplayer tests without
  delayed Survival ticks or leaked movement modifiers.

Gate: hiding or replacing the Character dialog cannot conceal an active Vanilla
character mechanic. Adventure results come from VRPG authority, with no hunger
or temperature tick or feedback, while Home can consume explicitly exposed
contextual conditions without owning Survival ticks.

Onboarding dependency: before semantic Character-dialog ownership becomes the
default, implement the [character creation and onboarding](character-creation-and-onboarding.md)
contract: cosmetic editor, starter choice, idempotent starter grant, preallocated
refundable skill point, compact Welcome guide, and protected non-participating
first-run state.

## 6. Redirect engine item flows

- [ ] Intercept pickups on the server before Vanilla chooses a hotbar slot.
- [ ] Insert accepted pickups atomically and leave/recreate the world item when the
  store rejects them.
- [ ] Route loot rewards, command grants, crafting/results, trades, containers,
  drops, death handling, and admin item operations through named bridges.
- [ ] Prevent combat-mode pickups from changing the equipped weapon or held model.
- [ ] Materialize only the bounded item/count required for an authorized world or
  Home/Build action, then reconcile consume/cancel/change-slot/disconnect.
- [ ] Define overflow behavior visibly and ensure it remains recoverable.
- [ ] Audit all direct writes to player-owned Vanilla inventories and either replace,
  allow only in Home/Build compatibility mode, or reject them safely.

Gate: every ordinary way of gaining, consuming, moving, or losing an item has an
integration test and exactly one authoritative commit path.

## 7. Convert combat and Home/Build loadouts

- [ ] Make equipped combat weapons reference canonical VRPG entry IDs rather than
  Vanilla slot indexes.
- [ ] Make mouse wheel cycle equipped weapon profiles without exposing storage
  position.
- [ ] Make held-item rendering follow the selected materialized weapon safely.
- [ ] Define consumable/quick-use bindings against canonical entries.
- [ ] Give Home/Build a separate tool palette and block selector backed by the same
  store, including effectively unlimited placement from large aggregate counts
  where rules allow it.
- [ ] Reconcile profile switches atomically on realm travel, death, disconnect, and
  forced game-mode changes.

Gate: Adventure and Home/Build can be entered repeatedly without copying, losing,
or stranding items.

## 8. Add the inventory network service

- [ ] Define query request/page packets with protocol version, store revision,
  category, search, sort, offset, limit, and request ID.
- [ ] Define equipment snapshot and mutation request/result packets.
- [ ] Return immutable display summaries; never send the entire store by default.
- [ ] Coalesce repeated queries and reject stale/out-of-order client responses.
- [ ] Add localized stable rejection codes for offline, stale, missing, reserved,
  protected, unresolved, permission denied, full, and invalid operations.
- [ ] Rate-limit and bound every request server-side.
- [ ] Advertise `ItemStoreReady` only after persistence, migration status, and all
  required bridges are ready for that player.

Gate: a test client can page 10,000 aggregate units as one entry, equip one real
item, reconnect, and observe the same authoritative result.

## 9. Build the native menu shell

- [ ] Implement `PlayerMenuController` for open/close, active page, focus restoration,
  pending requests, and per-session view state.
- [ ] Implement `GuiDialogVrpgPlayerMenu` as the one native composition host.
- [ ] Add semantic layout/theme tokens and scale-aware logical measurements.
- [ ] Add reusable fitted buttons, typed tabs, search, context messages, and
  mechanical disclosures with reduced-motion behavior.
- [ ] Use broad margins and one dominant workspace; do not expose every stat and
  operation simultaneously.
- [ ] Support mouse, keyboard, and controller focus. Do not hide required meaning
  behind hover or color alone.
- [ ] Clip every item model through `BoundedItemStackRenderer`.
- [ ] Keep the legacy Hub separately reachable until its other pages are migrated.

Gate: the empty/loading/error gallery and Inventory layout work at all supported
scales without owning the Inventory or Character semantic actions yet.

## 10. Build the usable Inventory and Equipment pages

- [ ] Render real Gear, Materials, Consumables, Keys & Quest, and Miscellaneous
  pages with bounded paging or virtualization.
- [ ] Preserve useful selection, search, scroll, and focus across refreshes.
- [ ] Show item identity, quantity, relevant comparison, protection/favorite state,
  and why an action is unavailable.
- [ ] Put affixes and technical details behind deliberate disclosure.
- [ ] Implement Equipment as a disclosure within Inventory, using the same store
  revision as the item list.
- [ ] Implement equip and unequip first, then use/bind, split, favorite/protect,
  discard/drop, and overflow recovery.
- [ ] Show pending, success, rejected, stale, offline, unresolved, empty, no-results,
  and overflow states beside the relevant control.
- [ ] Ensure no required item operation depends on the removed crafting grid,
  backpack, character paper doll, or Vanilla class/trait UI.

Gate: a player can complete the ordinary item-management loop entirely in the
new menu while Vanilla dialogs still remain the fallback owner.

## 10A. Build character creation and first-run onboarding

The detailed screen and authority contract is maintained in
[Character creation and first-run onboarding](character-creation-and-onboarding.md).

- [ ] Preserve and retheme real appearance, skin, voice, and character-preview
  behavior without exposing Vanilla traits or class mechanics.
- [x] Persist player RPG progression in the current savegame instead of the global
  mod-config directory; never import another world's learned or equipped skills.
- [x] Persist explicit appearance, starter, Welcome, safe-release, and complete
  stages with retry-safe starter and acknowledgement transitions.
- [ ] Replace the old class decision with validated data-defined VRPG starter kits.
- [ ] Show combat pattern, starting weapon/armor, and first skill without exposing
  the entire progression system at once.
- [ ] Atomically grant and equip starter items plus one ordinary preallocated,
  refundable skill point exactly once.
- [ ] Replace the temporary Vanilla `/charsel` appearance fallback with
  cosmetic-only rethemed editing that can never repeat a starter grant.
- [ ] Present the compact Welcome guide once per first-world character and place
  deeper explanation and lore in Library > Getting Started.
- [ ] Spawn first-arrival players in a validated private Home or onboarding space;
  use modal protection as fallback rather than the only safety mechanism.
- [ ] Keep onboarding players server-invulnerable and unable to attack, cast,
  collect rewards, trade, travel, build, or participate in encounters until the
  guide acknowledgement is durably committed and a safe release state is validated.
- [ ] If the guide closes in an invalid state, relocate to the persisted safe
  anchor before releasing protection; never leave an immortal player obstructing
  public combat or expose them to an attack already in progress.
- [ ] Activate one obvious arrival-space objective after release, using resolved
  bindings to teach the starting skill before deeper systems.
- [ ] Resume safely after cancellation, disconnect, crash, missing content, full
  storage, and rejected commits without duplication or loss.

Gate: first character creation is understandable, loss-safe, non-exploitable,
and entirely free of Vanilla class gameplay while preserving cosmetic choice.

## 11. Install transactional semantic-dialog ownership

- [ ] Implement `PlayerMenuDialogReplacementCoordinator` outside
  `VRPGModSystem`.
- [ ] Wait for default dialog registration and discover exactly one owner each for
  `inventorydialog` and `characterdialog`.
- [ ] Validate config, protocol, per-player store readiness, native dialog, both
  targets, both adapters, and recovery availability before mutation.
- [ ] Close and replace both dialog registrations as one transaction.
- [ ] Route Inventory to Inventory and Character to Inventory with Equipment open.
- [ ] Preserve the existing semantic action registrations and all user mappings;
  do not inspect, intercept, register, or hard-code the physical `E` or `C` keys.
- [ ] Retain original dialog references and restore them on failed installation,
  feature disable, world leave, reconnect, and mod disposal.
- [ ] Guard any verified direct/programmatic path that could reopen a duplicate
  Vanilla owner.
- [ ] Log one concise diagnostic explaining activation or the exact fallback reason.
- [ ] Set `DialogReplacementReady` only when the server and client gates both pass;
  the experimental config flag remains the final opt-in during testing.

Gate: default and remapped semantic actions always open one owner, and induced
startup failure restores both Vanilla dialogs rather than replacing only one.

## 12. Validate the first usable replacement

- [ ] Test empty, single-item, ordinary, and stress inventories; huge quantities;
  hash collisions; long names; and unresolved addon entries.
- [ ] Test 80%, 100%, 125%, and 150% GUI scale at 16:9, 16:10, ultrawide, and the
  minimum supported logical size.
- [ ] Test mouse, keyboard, controller, remapped actions, focus loss, and rapid
  open/close.
- [ ] Test pickup, loot, equip, unequip, consume, split, drop, overflow, death,
  realm travel, save/load, disconnect/reconnect, and server restart.
- [ ] Test two simultaneous mutations and malicious stale/duplicate/oversized
  packets.
- [ ] Test enable, disable, failed startup, rollback, recovery, and re-enable.
- [ ] Confirm Vanilla nutrition, class, trait, backpack, crafting, and Survival
  character assumptions are absent from the replacement surface.
- [ ] Confirm no item is visible through two mutable owners.

Gate: enable `ExperimentalDialogReplacement` in the dedicated test world and run
the complete matrix without loss, duplication, inaccessible items, or mismatched
menu ownership.

## Work after the first usable replacement

These are important, but they are not prerequisites for safely replacing the
Inventory and Character menus:

- Before the broader boot-to-overhaul handoff, migrate Stats, Skills, Talents,
  Library, and Options into the shared shell one page at a time.
- Add richer salvage, crafting, comparison, loadout, and bulk-management tools.
- Finish decorative motion, audio, material variants, class-metal identity, and
  advanced controller polish.
- Retire the legacy Hub action only after every migrated page passes its own gate.
- Make replacement default-on in the VRPG preset only after recovery and
  multiplayer soak testing.

## Immediate implementation order

Steps 1-5 are complete. Finish the usable replacement in this order:

1. Step 5A: establish character runtime authority so removing the Vanilla
   Character surface cannot hide active Survival mechanics.
2. Step 6: redirect pickups and every required engine item flow.
3. Step 7: convert combat weapon selection and Home/Build materialization.
4. Step 8: add the bounded inventory/equipment network service and advertise
   truthful per-player `ItemStoreReady` state.
5. Step 9: build the native shared menu shell without changing dialog ownership.
6. Step 10: finish the real Inventory and Equipment item-management loop while
   Vanilla remains the fallback owner.
7. Step 10A: build the rethemed appearance/starter flow and protected Welcome
   guide on the now-usable item and progression services.
8. Step 11: transactionally swap only the registered `inventorydialog` and
   `characterdialog` dialog owners, then advertise `DialogReplacementReady`.
9. Step 12: run the complete failure, scale, input, persistence, and multiplayer
   validation matrix before making the replacement default-on.

Do not treat flipping either readiness boolean as implementation. The ownership
transaction becomes eligible only when its item-flow, recovery, networking, and
usable-page dependencies pass their gates.
