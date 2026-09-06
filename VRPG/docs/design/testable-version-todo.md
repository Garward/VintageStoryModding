# Testable Version TODO

Status: Active implementation checklist
Updated: 2026-09-05
Parents: [Combat HUD replacement](combat-hud-total-replacement.md) · [Native player-menu implementation](native-player-menu-implementation-plan.md) · [Inventory, loot, and storage](inventory-loot-and-storage.md) · [Gear foundation](../superpowers/specs/2026-09-05-vrpg-gear-foundation-design.md)
Index: [VRPG design index](README.md)

This checklist turns the adopted interaction and player-menu contracts into small testable slices. Detailed behavior remains authoritative in the parent specifications; this file records order, dependencies, completion evidence, and the next safe playable boundary.

## Status key

- `[x]` implemented and covered by the current automated or manual evidence named here.
- `[ ]` not complete. A partial prototype does not count as complete.
- A gate must pass before the dependent ownership switch defaults on.

## 0. Boot-to-overhaul handoff milestone

This is the next project boundary. It is intentionally narrower than a playable
combat release and stricter than a UI mockup.

The milestone is complete when a clean VRPG world can load into a protected
development staging state, finish the real appearance/starter/Welcome flow, and
open one fully themed player menu. The configured Inventory action opens its
Inventory page; the configured Character action opens the same page with
Equipment expanded. Both pages use real saved server state and completely replace
the two Vanilla dialog owners without changing the player's bindings.

At that point the project is ready to begin live spawn allocation, main-world
generation, population/spawn mechanics, encounter implementation, and broader
combat work. Those later systems must consume the player, item, menu, and
onboarding authorities established here rather than forcing another replacement.

### 0.1 Hard blockers by ownership boundary

The ordered slices in section 4 define current execution order. These boundaries
describe dependencies and release gates, but their letter order does not override
an explicitly identified immediate coding target.

#### A. Stable game startup and authority

- [ ] A clean client and dedicated server start with the supported Vintage Story
  build, load all required assets, and report one coherent VRPG runtime profile.
- [ ] Startup validation distinguishes fatal ownership failures from unavailable
  future content. A fatal inventory/menu failure keeps both Vanilla dialogs and
  item custody intact; it never leaves a mixed or half-replaced state.
- [ ] Finish and live-test VRPG ownership of class, trait, walk-speed, jump-height,
  hunger, nutrition, and body-temperature behavior needed to remove the Vanilla
  Character screen truthfully.
- [ ] Pass first join, respawn, disconnect/reconnect, save/load, and server-restart
  smoke tests without restoring a disabled Survival mechanic or stale modifier.

#### B. One safe item and equipment owner

Gear foundation integration evidence (2026-09-05): 649 Release tests pass,
including archive readiness/recovery, exact fallback preservation, shared
equipment stats, damage ordering, and delegate cleanup. A disposable dedicated
server loaded all 30 starter bases, inspected all five six-piece starter sets
without grants, restarted with archive readiness intact, and kept gear unavailable
when item persistence was disabled. Connected-player rendering/combat acceptance
is still incomplete: the isolated client requires a valid game login, and
automatic Evasive Step still awaits authoritative selected-class pairs from the
later onboarding transaction. The gear, defense, starter appearance, and
equipment-effect claims below remain unchecked until those live checks pass.

- [x] Canonical records, authoritative store transactions, persistence, exact
  `ItemStack` codec, recovery, and lossless migration foundations are implemented.
- [ ] Implement the VRPG gear-instance schema, gear-base definitions, validators,
  deterministic generator boundary, required-level Weapon Power, level-scaled
  Armor, and the Health/Magic Shield/Armor/Evasion/Elemental Resistance equipment
  effects defined by the gear-foundation specification.
- [ ] Implement and balance the finalized defensive targets: 75% hard-capped
  elemental resistances attainable across all elements on good late-game gear;
  rating-based Armor with a 90% practical soft ceiling and 99% hard cap; and
  eligible-hit Evasion with a 90% hard cap, accuracy counters, and explicit
  ineligible damage categories.
- [ ] Correct Evasive Step to resolve an otherwise lethal hostile hit only after
  mitigation and Magic Shield, then apply displacement and server-owned
  invulnerability. Preserve its Dexterity-primary/no-hotkey contract and cover
  cooldown, follow-up hits, talent modifiers, reconnect, and primary-stat changes.
- [ ] Add complete visually coordinated level-one Common starter sets for every
  selectable starter class. Vintage Story collectibles are visual chassis only;
  starter identity, values, ownership, and persistence are VRPG-owned.
- [ ] Finish engine ingress coverage for pickups and grants, then add bounded
  materialization/reconciliation for operations that still require an engine
  stack. Every failure retains the source item or creates an explicit recovery
  record.
- [ ] Make weapons, armor, equipment, quick-use bindings, and held rendering
  reference canonical entry IDs rather than Vanilla slot positions.
- [ ] Reject mixed ownership and prove that migration, equip, use, split, drop,
  death, reconnect, and profile changes cannot lose, duplicate, or strand items.
- [ ] Expose real store readiness per player only after persistence, migration,
  recovery, and the required ingress/materialization bridges are available.

#### C. Real first-run flow

- [ ] Replace the temporary Vanilla appearance bridge with a rethemed cosmetic
  editor that preserves real appearance and voice state without exposing Vanilla
  classes or traits.
- [x] Data-defined starter previews and the deterministic safe-arrival tavern
  generator are implemented and validated. Live tavern allocation is deferred to
  spawn/realm work after this milestone.
- [ ] Commit starter weapon, armor, one ordinary refundable skill point, and its
  initial skill allocation in one retry-safe server transaction after item-store
  readiness is proven.
- [ ] Replace the preview-only starter screen with truthful Confirm, pending,
  success, and actionable rejection states driven by that transaction.
- [ ] Implement the compact Welcome screen and Library > Getting Started page.
  Closing the guide records one server acknowledgement; it does not itself grant
  rewards or release protection.
- [ ] For this milestone, keep first-run players protected and non-participating in
  an explicit development staging location. Production realm allocation,
  generated spawn transfer, and `SafeReleaseReady` remain the first post-milestone
  spawn-runtime slice.

#### D. Server-backed player-menu protocol

- [ ] Add bounded, versioned inventory query/page, equipment snapshot, mutation,
  and result packets with request IDs and source revisions.
- [ ] Implement equip/unequip first, then the minimum complete replacement actions:
  use or bind, split, favorite/protect, discard/drop, and overflow recovery.
- [ ] Preserve tab, selection, search, scroll, and focus while reconciling pending,
  success, stale, rejected, offline, unresolved, empty, and overflow states.
- [ ] Never send the full store by default or let stale responses replace newer
  state. Bound and rate-limit every server request.

#### E. Complete themed UI foundation

- [ ] Implement one `GuiDialogVrpgPlayerMenu` shell with stable navigation, close
  behavior, focus restoration, session state, and named layout/theme tokens.
- [ ] Finish the reusable visual language: fitted buttons, typed tabs, search,
  context messages, item rows/cards, comparison values, mechanical disclosures,
  focus/selection shapes, pending feedback, and reduced-motion behavior.
- [ ] Build real Inventory and Equipment pages for Gear, Materials, Consumables,
  Keys & Quest, and Miscellaneous. Scroll or virtualize only the growing item
  region; keep selection and primary actions anchored.
- [ ] Absorb Stats, Skills, Talents, Library, and Options into the same shell so the
  project exits this milestone with one coherent player-facing theme. A page may
  omit unfinished future mechanics, but every visible value and control must be
  real and functional.
- [ ] Retheme Appearance, Starter, Welcome, player-menu, and current HUD surfaces
  with the same tokens and interaction language. Do not require finished worldgen
  or combat content merely to populate decorative UI.
- [ ] Provide mouse and keyboard paths now, preserve remapped semantic actions, and
  complete controller focus before declaring the UI foundation finished. Required
  meaning cannot depend only on hover, animation, sound, or color.

#### F. Atomic Inventory/Character replacement

- [ ] Discover the current owners of `inventorydialog` and `characterdialog` only
  after Vanilla registration is complete.
- [ ] Validate the native shell, both entry adapters, protocol, per-player item
  readiness, fallback, and restoration path before changing either owner.
- [ ] Close and replace both Vanilla dialogs as one transaction. Retain their
  references and restore both on failure, feature disable, world leave, reconnect,
  or mod disposal.
- [ ] The Inventory action opens Inventory. The Character action opens that same
  menu with Equipment expanded. Invoking either while the menu is open closes it
  consistently, including after the actions are remapped.
- [ ] Remove the Vanilla backpack/paper-doll/class surfaces only while VRPG owns
  item custody and both semantic dialogs. No second programmatic path may expose a
  competing item owner.

#### G. In-game release gate

- [ ] Complete the full flow in a clean single-player world and a dedicated-server
  client: appearance, starter confirmation, Welcome, Inventory, Equipment, save,
  reconnect, restart, and continued item use.
- [ ] Migrate a populated test character, force a failure at every ownership step,
  and prove the result is either complete VRPG ownership or untouched/restored
  Vanilla ownership.
- [ ] Test zero, one, normal, and stress-volume inventories; missing addon content;
  long localized text; large quantities; and stale/concurrent mutations.
- [ ] Visually verify 80%, 100%, 125%, and 150% GUI scale at 16:9, 16:10,
  ultrawide, and the minimum supported logical size. Nothing important clips,
  overlaps, becomes hover-only, or falls below readable size.
- [ ] Verify default, hover, keyboard focus, pressed, selected, disabled, locked,
  pending, success, and rejected states. Every disabled action explains why beside
  the control, and ordinary feedback does not depend on chat.
- [ ] Disable inventory replacement and prove Vanilla Inventory and Character
  return untouched with their existing bindings and usable item custody.

### 0.2 Explicitly after this milestone

The following are important, but they are not blockers for completing the themed
UI and Inventory/Character ownership foundation:

- Live allocation, isolation, transfer, reconnect, and recovery for the generated
  safe-arrival tavern.
- Main-world topology, region generation, landmarks, routes, traversal surfacing,
  and production spawn placement.
- Creature population rules, hostile/interactable adaptation, spawn budgets, AI,
  encounters, objectives, loot tables, and expedition flow.
- Seeded random gear generation, Common/Rare/Unique selection, compatible affix
  rolls, soft party-build weighting, and statistical distribution validation.
  Until that slice is complete and a live loot source is wired, only starter
  gear is normally obtainable; test/admin generation is not a player loot path.
- Expansion and balance of combat skills, movement, targeting, temporary terrain,
  enemies, combat FX, and the post-release tutorial objective.
- Home generation, building, crafting-service integration, storage networks,
  trading, economy, Rebirth, and advanced inventory actions not shown by the
  milestone UI.

Deferred controls stay hidden or clearly unavailable; the UI must not simulate
future mechanics with decorative buttons or invented values.

## 1. Current verified groundwork

- [x] Combat mode suppresses Vanilla block placement, block use, and Survival mining initialization/progression on the client.
- [x] Entity-targeted Vanilla right-click interaction remains reachable while the interim block guards are active.
- [x] Combat entry preserves the prior slot, retains or selects a weapon, and prevents later pickups from becoming the selected combat item.
- [x] Mouse wheel cycles weapon-eligible Vanilla bridge slots and wraps in both directions.
- [x] `PlayerMenuNavigation` routes Inventory, Character, and legacy Hub entry points without physical-key assumptions.
- [x] `PlayerMenuDialogReplacementPlan` requires exactly one owner for both `inventorydialog` and `characterdialog` before replacement can proceed.
- [x] Thin `PlayerMenuEntryDialog` adapters exist and own no inventory state or visual composition.
- [x] Pure navigation and replacement-planning tests pass.
- [x] The server advertises inventory configuration, item-store readiness, and dialog-replacement readiness as separate versioned capabilities; unfinished readiness remains false.
- [x] Interaction request/result packets and a server validation service are registered.
- [x] Native opted-in entities can attach `vrpgInteractable`; the entity-behavior handler publishes one revisioned server activation event.
- [x] The client resolves only the selected opted-in entity, consumes one secondary-action press, and presents compact pending/success/rejection feedback.
- [x] The authoritative item store supports revision-safe mutations, reservations,
  equipment, recovery, exact-state aggregation, unique separation, indexed bounded
  queries, checked `long` quantities, and dual-generation world-save persistence.
- [x] Lossless legacy-inventory migration and restart-safe administrative recovery
  are implemented but are not activated automatically before dialog ownership is ready.
- [x] The common engine grant boundary has an all-or-nothing canonical ingress
  adapter behind the experimental ownership flag; full live flow coverage remains.
- [x] The deterministic safe-arrival tavern generator, inert block palette,
  reservation preparation, structural/readback validation, and restrictive policy
  tests pass across 1,024 seeds. Live realm allocation and transit remain deferred.

These are scaffolding guarantees, not permission to enable menu replacement.
Vanilla remains the default live item/dialog owner until the atomic replacement
gate passes. Right-click entity interaction also retains its interim Vanilla
fallback until its separate integration gate passes.

## 2. Right-click entity interactions

### 2.1 Data and authority contract

- [x] Add `InteractionDefinition`: stable code, target tags, allowed realm contexts, range, line-of-sight rule, prompt, priority, cooldown, and handler code.
- [ ] Add an entity-side adaptation result that distinguishes `Hostile`, `Interactable`, `HostileInteractable`, and `Ignored`. Creature ingestion must not infer safe interaction merely from a Vanilla `OnInteract` override.
- [x] Add `ICombatInteractionHandler` as a narrow registered verb. Content definitions select a duplicate-safe registered handler; data cannot invoke arbitrary code.
- [x] Add the first server-owned `InteractionPolicy` for feature, request, player, target, context, dimension, range, line of sight, entity opt-in, permission, cooldown, and handler gates. Replace its temporary game-mode context adapter when realm authority lands.
- [ ] Define deterministic priority when an entity can both fight and interact. Primary attack remains attack; secondary interaction selects only an eligible interaction candidate.

### 2.2 Targeting and input

- [x] Implement `CombatInteractionResolver` separately from hostile attack targeting and ground-point skill targeting.
- [x] Resolve only the engine-selected entity using range, selection-box identity, line of sight, entity tags, context, and explicit priority. No loaded-entity scan occurs.
- [x] Implement `CombatInteractionController` on the existing semantic in-world secondary action. It does not register a physical key or replace global input dispatch.
- [ ] Preserve press, hold, release, cancellation, focus loss, dialog opening, death, realm travel, and disconnect behavior.
- [ ] Prevent Adventure blocks and held items from receiving Vanilla block-use intent. Home/Build uses its own contextual profile.
- [x] Keep an exact entity-code allowlist for audited Vanilla fallback while default-denying unknown entity interaction behavior in Adventure.

### 2.3 Request flow

```text
secondary action
  -> client resolver chooses one eligible entity interaction
  -> client shows pending state and sends target + interaction + request id
  -> server resolves the entity again and validates context/range/LOS/state/permission
  -> registered handler commits one authoritative result
  -> client reconciles success or shows a local rejection reason
```

- [x] Define versioned request/result registration with request identity, target entity, interaction code, selection-box identity, success, and stable rejection code.
- [x] Send at most one request per secondary-action press and enforce the definition cooldown again on the server.
- [ ] Revalidate target existence, dimension, distance, line of sight, encounter state, permissions, cooldown, and required item/resource on the server.
- [ ] Ensure a modified client cannot use the interaction path to break/place/use protected terrain, attack arbitrary entities, or invoke unregistered handlers.
- [ ] Make every handler atomic where it changes inventory, rewards, doors, encounter state, travel, housing, or NPC trade.

### 2.4 Player feedback

- [ ] Show one compact prompt only for the selected eligible entity: action and identity are implemented; resolved binding display still needs an engine-supported mapping source.
- [ ] Give keyboard/controller focus or explicit selection the same information as hover/crosshair selection.
- [x] Show pending state beside the prompt and prevent duplicate requests while a press is latched or a request is pending.
- [x] Map protocol failures to concise localized outcomes beside the prompt; handler codes and exceptions remain in logs.
- [ ] Validate the prompt against the crosshair, enemy telegraphs, selected-target frame, supported scales, and bright/dark scenes in game.
- [ ] Rate-limit repeated rejection feedback and avoid routing ordinary interaction results through chat.

### 2.5 Interaction test gate

- [ ] Unit-test candidate filtering, tie-breaking, context policy, handler registration, cooldowns, and rejection mapping.
- [ ] Integration-test an NPC, shrine, loot entity, door/mechanism entity, hostile-only creature, hostile-interactable creature, missing addon entity, and despawned target.
- [ ] Test too far, blocked line of sight, wrong realm, wrong encounter phase, missing requirement, concurrent use, duplicate packet, stale request, death, disconnect, and reconnect.
- [ ] Verify left/held primary input never invokes interaction and right-click interaction never begins mining, placement, or an attack fallback.
- [ ] Verify remapped secondary input and keyboard/controller selection without consulting a physical-key constant.
- [ ] Replace the interim Vanilla entity-interaction pass-through only after the native path passes this gate.

## 3. Inventory and Character menu conversion

This work replaces the dialogs that own the `inventorydialog` and
`characterdialog` semantic actions. It never replaces the physical `E` or `C`
keys, adds duplicate hotkeys, or changes a player's mappings. Those keys are only
the default bindings used in examples.

Before either dialog owner changes, VRPG must also own the mechanics that the
Vanilla Character surface currently reports. Removing a panel must never conceal
an active Survival stat system.

### 3.0 Character runtime authority

Status: in progress. Hunger/nutrition/body-temperature behavior is verified in
game. Movement-stat and Vanilla-class ownership are implemented with automated
coverage and await live verification. The generic sequenced movement-effect
runtime is implemented and Evasive Step now uses it; its live verification and
collision-policy consumers remain.

- [x] Stop satiety drain, `EntityBehaviorHunger.OnGameTick`, its separately
  registered private `SlowTick`, starvation `ReceiveDamage`, camera shake/hurt
  feedback, and all hunger-derived effects in Adventure/combat mode.
- [x] Stop nutrition from changing maximum health in Adventure/combat mode.
- [ ] Retain satiety/nutrition only through realm-aware hooks for explicitly
  authored Home bonuses; do not reactivate their Survival tick behavior.
- [ ] Make effective walk speed and jump height VRPG-owned outputs and neutralize
  competing Vanilla class, trait, hunger, and equipment modifier sources.
- [ ] Disable the complete Vanilla class/trait gameplay path, not only its UI.
- [x] Move player RPG levels, learned skills, equipped skills, talents, and
  resources out of global mod configuration and into the current world save.
  A matching player UID in another world now starts with an empty level-one profile.
- [x] Add a persisted first-run state machine for appearance, starter commit,
  Welcome acknowledgement, and safe release. Starter retries are idempotent and
  guide acknowledgement alone cannot complete onboarding.
- [ ] Replace the temporary Vanilla `/charsel` appearance fallback with rethemed
  cosmetic editing. Reopening it must never alter starter choice or repeat rewards.
- [x] Add and validate data-defined VRPG starter classes that preview one level-one
  weapon, starter armor, and a representative level-one skill. The actual item and
  skill-point grant remains the next transaction below.
- [ ] Commit appearance, starter equipment, and starting skill allocation through
  one idempotent server transaction after store readiness validation.
- [ ] Add a compact first-world Welcome guide with Adventure/Home/build guidance;
  move deeper mechanics and lore into Library > Getting Started.
- [ ] Allocate Home or a tiny private onboarding instance as the guaranteed-safe
  first arrival; exclude hostiles, hazards, public collision, and ordinary rewards.
  Deterministic tavern geometry, inert assets, structural validation, preparation,
  and policy contracts are complete; the realm allocator and safe transfer are not.
- [ ] Keep unfinished first-run players server-invulnerable and block attacks,
  skills, pickups, rewards, travel, trading, building, and encounter participation
  until a close action is durably acknowledged and `SafeReleaseReady` is validated.
- [ ] Treat closing the guide and releasing protection as separate transitions.
  Relocate to the persisted safe anchor first if the current state is unsafe.
- [ ] Give the released player one obvious world objective that teaches the ready
  starting skill through its resolved binding.
- [x] Make body temperature inert in Adventure, including simulation, frost
  damage, wetness/freezing state, room and clothing scans, cold animations,
  frost rendering, movement effects, and stale transition state.
- [ ] Add a reusable skill-movement service based on the useful Kinematics
  trebuchet launch/collision-sync and belt-rider velocity-blending patterns.
- [ ] Verify join, respawn, save/load, Rebirth, realm changes, and movement-effect
  cancellation cannot restore Survival values or leak old modifiers.

### 3.1 Ownership and configuration first

- [ ] Add the coherent `InventoryReplacement.Enabled` capability, default enabled only in the VRPG preset and restart-bound.
- [x] Synchronize the initial effective inventory/menu readiness capability from server to client; clients do not infer ownership from hidden Vanilla widgets.
- [ ] Reject mixed ownership where both Vanilla and VRPG can serialize or mutate the same player items.
- [ ] Define the clean fallback: when replacement is disabled or startup validation fails, Vanilla Inventory and Character dialogs and mappings remain untouched.
- [ ] Add startup diagnostics naming missing capabilities or ambiguous dialog owners without exposing those details in ordinary UI.

### 3.2 Server-owned item store

- [x] Define versioned records for unique entries, aggregate commodities,
  consumables, ledgers, protected records, equipment, overflow, and unresolved
  addon content.
- [x] Complete `IPlayerItemStore`: bounded query, revision-safe insert/extract,
  reservations, equip/unequip, flags, recovery, confirmed destruction, exact
  collision buckets, checked `long` quantities, and stable entry IDs.
- [x] Complete storage durability with dirty versioned dual-generation persistence,
  checksum validation, older-generation recovery, and forward-schema refusal.
- [x] Implement typed category definitions with Gear, Materials, Consumables,
  Keys & Quest, Miscellaneous, and a guaranteed fallback.
- [ ] Complete bounded queries end to end: the indexed store returns capped
  immutable category/search/sort pages, but the versioned paged network protocol
  and client reconciliation remain.
- [x] Add explicit overflow/unresolved locations and recovery operations for failed
  migration, capacity failures, and temporarily missing content.

### 3.3 Migration and Vanilla bridges

- [x] Build a dry-run migration report for every affected player: source items,
  destination policy, unsupported content, overflow, and blocking errors.
- [x] Create a recoverable snapshot before mutation and record an idempotent
  schema/migration version with restart-safe recovery states.
- [x] Transfer inventory, hotbar, backpack contents, equipment, offhand,
  mouse-held stack, and crafting inputs into a staged canonical store transaction.
- [x] Refuse activation before mutation if lossless transfer and explicit recovery
  routing cannot be guaranteed.
- [ ] Implement bounded materialization for engine interactions that require a Vanilla `ItemStack`; reconcile consume/cancel/change-slot/disconnect atomically.
- [ ] Redirect pickups into `IPlayerItemStore` before enabling replacement. A pickup must never become the active combat hand.
  The common engine grant boundary now has an all-or-nothing canonical ingress
  adapter behind the experimental ownership flag. Store rejection preserves the
  source stack and successful insertion occurs before Vanilla can select a slot;
  live pickup/grant coverage and durable reward identities remain before this gate
  can be checked complete.
- [ ] Make combat loadouts reference canonical entry IDs; mouse wheel changes equipped weapon selection, not storage position.
- [ ] Test switching Adventure/Home/Build profiles without copying, losing, or duplicating items.

### 3.4 Native menu shell

- [ ] Implement `PlayerMenuController` for open/close, active page, focus restoration, and session view state.
- [ ] Implement `GuiDialogVrpgPlayerMenu` as a native composition host with one shared shell.
- [ ] Implement named layout/theme tokens and reusable `FittedButton`, `MechanicalDisclosure`, `SearchField`, `TypedTabStrip`, and `ContextMessage` components.
- [ ] Use broad margins, one dominant workspace, and progressive disclosure. Do not reproduce the current Hub's all-at-once information density.
- [ ] Support mouse, keyboard, and controller focus; selection and disabled meaning must not rely on hover or color alone.
- [ ] Add reduced-motion behavior for plate/disclosure animation and ensure animation never delays state or input.
- [ ] Use `BoundedItemStackRenderer` for every custom item slot and clip lists, disclosures, and live item models locally.

### 3.5 Inventory page: truthful first slice

- [ ] Write immutable inventory summary, query-state, equipment-summary, and action-result view models.
- [ ] Render Gear and Materials first with real server pages; prove 10,000 identical materials appear as one responsive quantity.
- [ ] Keep navigation and selected-item actions anchored while only the item region scrolls or virtualizes.
- [ ] Show active typed tab, selected entry, meaningful capacity, quantity, and build-relevant effects/comparison.
- [ ] Keep affixes, full equipment, and technical detail behind fitted disclosure plates; automatically reveal only the comparison relevant to the selected gear.
- [ ] Implement empty, loading, no-results, unresolved, overflow, stale, offline, pending, success, rejected, and permission-denied states.
- [ ] Implement one mutation first: equip selected gear through a revision-aware server request with local pending and rejection feedback.
- [ ] Preserve selected tab, search, scroll, focused entry, and useful selection after refresh or rejection.
- [ ] Add use/bind, favorite/protect/junk, salvage preview, split, deposit, and withdraw only after equip is proven safe.

### 3.6 Character menu absorption

- [ ] Make the semantic Character action open the same Inventory page with Equipment expanded; it must not create another item owner.
- [ ] Present equipped gear by meaningful body/weapon role without showing every empty slot at once.
- [ ] Remove the backpack region entirely while VRPG inventory ownership is active.
- [ ] Remove Vanilla class, trait, nutrition, satiety, body-temperature, hunger-rate, and other Survival-only character assumptions from this surface.
- [ ] Keep appearance identity separate from class/build identity.
- [ ] Show health, mana, level, and build stats only where they change the current equipment decision; full progression detail remains on Stats.
- [ ] Show exact comparison values and units beside the equip choice. Lore, source-addon details, and formula breakdowns remain one deliberate details step away.
- [ ] Ensure equipment state comes from the same server revision as inventory custody so the UI cannot show one item both equipped and stored.

### 3.7 Transactional dialog replacement

- [ ] Implement `PlayerMenuDialogReplacementCoordinator` outside `VRPGModSystem`.
- [ ] Wait for Vanilla dialog registration, then discover owners by `ToggleKeyCombinationCode` values `inventorydialog` and `characterdialog`.
- [ ] Validate both Vanilla owners, both entry adapters, the native dialog, protocol, server store, and replacement capability before changing `LoadedGuis`.
- [ ] Close and replace both dialogs as one transaction; never replace only one.
- [ ] Retain original dialog references and restore them on failure, feature disable, world leave, reconnect, or mod disposal.
- [ ] Do not register new physical keys, rewrite mappings, or patch the global keyboard dispatcher.
- [ ] Prevent direct/programmatic opening of duplicate Vanilla item owners only after confirming a real bypass exists.
- [ ] Keep the legacy Hub operational until each corresponding native page passes its own state and scale gate.

### 3.8 Menu test gate

- [ ] Unit-test navigation, replacement planning, layout, disclosure state, paging, filtering, stable selection, and revision reconciliation.
- [ ] Integration-test default and remapped Inventory/Character actions, duplicate/missing dialog owners, feature-off fallback, replacement rollback, disconnect/reconnect, and mod disposal.
- [ ] Test zero, one, normal, and stress-volume inventories; long localized text; missing addon entries; very large quantities; and hash-collision fixtures.
- [ ] Test 80%, 100%, 125%, and 150% GUI scale at 16:9, 16:10, ultrawide, and minimum supported logical size.
- [ ] Test mouse, keyboard, and controller paths plus default, hover, focus, pressed, selected, disabled, pending, success, and rejected states.
- [ ] Verify no visible control is decorative, no quantity is invented, no binding is hard-coded, and no decision-changing equipment property is hover-only.
- [ ] Verify closing, switching tabs, equipping, pickup, overflow, death, realm travel, and reconnect cannot lose, duplicate, or strand an item.

## 4. Ordered implementation slices to the handoff

### Slice A — close runtime and item-ownership gaps

1. Implement the real VRPG gear schema, base registry, generation boundary,
   Weapon Power, finalized defensive caps and ordering, Evasive Step regression
   coverage, equipment effects, and complete starter sets.
2. Live-verify character/class/movement authority and remove the remaining hidden
   Survival sources required by the Character replacement.
3. Finish engine item ingress, bounded materialization, equipment-entry loadouts,
   and explicit recovery behavior.
4. Make readiness per-player and fail closed into untouched Vanilla ownership.

### Slice B — finish first-run authority

1. Implement the stable, recoverable starter item/equipment/skill transaction.
2. Enable real starter confirmation with pending and rejection feedback.
3. Retheme the real appearance editor and build Welcome plus Getting Started.
4. Keep the player protected in development staging; do not pull live realm
   allocation or generated spawn transfer into this milestone.

### Slice C — prove the server-backed menu before ownership

1. Add bounded query/equipment/mutation protocols and immutable view models.
2. Build the shared themed shell and complete Inventory/Equipment pages.
3. Implement the minimum complete item-management action set and state matrix.
4. Migrate Stats, Skills, Talents, Library, and Options into the same visual and
   navigation foundation while Vanilla still owns the two semantic dialogs.

### Slice D — take semantic-dialog ownership experimentally

1. Run the dry migration and persist the recoverable source snapshot.
2. Validate both old owners, both adapters, the new shell, server capability, and
   rollback path.
3. Replace `inventorydialog` and `characterdialog` together behind the opt-in
   test-world flag without changing bindings.
4. Test default/remapped actions, reconnect, disposal, forced failure, and complete
   restoration.

### Slice E — pass the boot-to-overhaul release gate

1. Complete in-game functional and visual matrices in single-player and on a
   dedicated server.
2. Enable replacement by default only in the validated VRPG preset.
3. Remove duplicate legacy player-menu surfaces after every route exists in the
   shared shell; retain a deliberate appearance-editing route.
4. Freeze the player/menu/item/onboarding interfaces that spawn generation,
   population, encounters, combat, and Home will consume next.

### Post-handoff Slice F — make random gear genuinely obtainable

1. Implement seeded base, required-level, Common/Rare/Unique, affix-tier, and
   exact-roll generation through the gear boundary proven by starter equipment.
2. Add soft weighting from participating classes, skills, attributes, defensive
   specializations, region, element, enemy rarity, bosses, and expedition
   modifiers without guaranteeing upgrades or eliminating off-build drops.
3. Run large deterministic distribution sweeps and the level 1/20/40/60/80/100
   combat bands before enabling any live source.
4. Wire the first explicit encounter reward source and truthful pickup/filter/
   salvage presentation. Do not mark random gear obtainable before this route
   creates, stores, equips, saves, reloads, transfers, and salvages real items.
5. Extend acquisition into spawned enemies, bosses, expeditions, traders, and
   Home salvage only after the first source passes loss, duplication, party,
   reconnect, and server-restart tests.

The native right-click interaction gate remains tracked in section 2, but it is
not on this milestone's critical path. Its interim fallback stays in place until
spawned actors and encounter content exist to exercise it honestly.

## 5. Immediate next coding target

The next hard blocker is the real VRPG gear foundation. Implement permanent gear
identity, level scaling, defensive values, equipment effects, visual-chassis
separation, and deterministic complete starter sets before committing onboarding
rewards. This prevents the atomic starter transaction from freezing preview-only
Vanilla placeholders into the saved player format.

After the gear foundation passes, implement the atomic starter transaction. It
must prove that the saved player domain and canonical item/equipment store can
commit one coherent character result. In parallel design order—but not as a
separate owner—the menu protocol must expose the resulting real Gear and Equipment
state.

After that proof, implement the shell and Inventory/Equipment pages against the
real protocol, absorb the remaining player-menu pages into the shared theme, and
only then perform the two-dialog ownership transaction. Working Vanilla menus
remain available until that final transaction passes its rollback and in-game
scale/input gates.

This milestone is not complete merely because Gear and Materials render or one
equip request succeeds. It requires the complete themed shell, truthful first-run
screens, the minimum usable item-management loop, full Inventory/Character owner
replacement and restoration, and an in-game boot/reconnect/restart pass. It does
not require production spawn generation, tavern allocation, population, or
finished combat content.

Random gear generation is the first post-handoff itemization slice and is not
implicitly provided by the starter generator. VRPG must continue to describe
random equipment as unavailable until a validated live loot source calls the
seeded generator and the resulting item completes the full canonical lifecycle.
