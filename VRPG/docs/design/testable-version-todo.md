# Playable Vertical-Slice Checklist

Status: **Authoritative implementation roadmap**
Updated: 2026-09-12
Design basis: [Complete game design](complete-game-design.md) · [Implementation readiness and test strategy](implementation-readiness-and-test-strategy.md) · [Strategy audit](../audit.md)

This is the only document that sets implementation order. Subsystem designs and
acceptance checklists define contracts and tests, but they do not place themselves
on the critical path. When another document disagrees with this sequence, update
that document or treat its ordering as superseded.

The next milestone is ten playable minutes that prove VRPG's combat premise. The
first usable Adventure profile also has a narrow prerequisite: its existing
Inventory and Character semantic owners must hand off together to the
server-backed VRPG Inventory/Equipment page. That is not a return to an
exhaustive pre-combat UI roadmap or proof of compatibility with every theoretical
item producer. It is the small semantic-owner swap and central Adventure
suppression described in the [first usable player-menu plan](../superpowers/plans/2026-09-12-first-usable-player-menu.md).

The swap is source- and automated-test complete, but it is not replacement-ready:
the supported joined-client check and explicit player confirmation remain open.

## Status rules

- `[x]` means the behavior exists with the evidence required by its gate.
- `[ ]` means incomplete. Source review and unit tests do not stand in for a
  joined-client check when the item explicitly requires one.
- Record durable evidence in commits and test output. Keep this file focused on
  current state, dependencies, and the next decision.
- Do not add a subsystem to the critical path because another unfinished feature
  could eventually use it. Add it when the current playable gate requires it.

## Foundations already available

These foundations should be reused rather than rebuilt:

- [x] Server-authoritative player item store with revisions.
- [x] Aggregated commodities, separate unique gear records, fixed large capacity,
  typed categories, bounded queries, migration, persistence, and codec support.
- [x] Server-side skill casting, damage, status, progression, equipment, and
  resource-domain logic with a fast automated test suite.
- [x] Eleven skill definitions and five starter gear sets suitable for the first
  combat slice.
- [x] Data-first definitions and validation infrastructure.
- [x] Tavern and dungeon-generation prototypes that can remain isolated until a
  later gate consumes them.
- [x] Narrow Inventory/Character semantic-owner swap, central Adventure producer
  suppression, and fail-closed activation coverage. Live client acceptance remains
  open.

These are useful foundations, not proof that their complete player journeys work.

## Gate 0 — repeatable live verification

**Purpose:** stop accumulating integration claims that cannot be checked in the
real client/server path.

- [ ] Establish a login-capable client and dedicated-server test pair using the
  supported Vintage Story build.
- [ ] Write one short smoke script covering server start, client join, character
  load, one command or UI interaction, disconnect, reconnect, save, server restart,
  and clean shutdown.
- [ ] Keep the test world and configuration reproducible without depending on a
  developer's existing personal world.
- [ ] Record where client and server logs are collected and which failures block
  the current gate.
- [ ] Run the smoke script once and record the result. Authentication failure,
  isolated startup, or a menu preview does not complete this item.

**Exit:** a developer can repeat the joined-client smoke loop without rediscovering
setup steps. No later gate can be called complete without this loop.

## Gate 1 — protected combat sandbox

**Player promise:** enter one arena, fight three readable enemy types with the
existing skills and starter gear, gain XP, die or win, and return for another
attempt. The loop should sustain at least ten minutes of useful playtesting.

### 1.1 Combat budget

- [ ] Write a one-page numeric budget for ordinary, elite, and boss time-to-kill at
  levels 1, 20, 50, and 100.
- [ ] Define expected player effective health and incoming damage per second at the
  same checkpoints.
- [ ] Set one total effective-health budget before finalizing individual armor,
  evasion, resistance, shield, or lethal-save caps.
- [ ] Replace automatic Evasive Step with a resource cost, slotted choice, or timed
  player action before using it in balance tests.

### 1.2 Arena and entry

- [ ] Provide one deterministic flat combat arena with clear boundaries, broad
  paths, safe spawn points, and room for ranged and charge behaviors.
- [ ] Add a direct developer entry route that does not depend on custom worldgen,
  the owned realm runtime, expedition planning, or menu replacement.
- [ ] Prevent ordinary permanent block placement and breaking in the arena through
  the simplest supported configuration, asset, or permission boundary.
- [ ] Preserve an emergency exit and cleanup path when combat initialization fails.

### 1.3 Three representative enemies

- [ ] Implement a melee advancer that closes distance and uses a readable attack.
- [ ] Implement a ranged enemy that seeks and maintains a useful firing distance.
- [ ] Implement one pressure enemy using charge, shield, or orbit behavior.
- [ ] Give each enemy a visible intent, recovery window, valid target policy, and
  bounded update cost.
- [ ] Exercise navigation around the arena boundary and one temporary obstruction.
  General-purpose world pathfinding is outside this gate.

### 1.4 Player loop

- [ ] Equip one existing starter set and expose the existing eleven skills through
  a stable test loadout or the current Hub.
- [ ] Connect enemy damage, defeat, XP, player death, respawn, and arena reset into
  one repeatable loop.
- [ ] Open the VRPG inventory from the Hub or an explicit `/vrpg inv` route using
  real server-backed data. In the Adventure profile, use the completed paired
  semantic-owner route; it does not require Stats, Skills, Talents, Library, or
  Options to merge into the first pass.
- [ ] Deliver any test rewards directly through an explicit VRPG-owned transaction.
  Do not require field loot piles, party permissions, salvage, or random affixes.
- [ ] Show enough HUD feedback to read health, resources, cooldowns, enemy intent,
  damage, XP, death, and reset state.

### 1.5 Gate 1 verification

- [ ] Pass focused domain and integration tests for each new combat behavior.
- [ ] Complete the joined-client smoke loop through entry, combat, XP, death or
  victory, respawn/reset, inventory inspection, reconnect, and server restart.
- [ ] Play for ten minutes and record unclear attacks, navigation failures, dominant
  tactics, downtime, and any item-boundary diagnostics.
- [ ] Confirm that no test requires Vanilla-dialog ownership, party state, owned
  realms, custom worldgen, housing, or creature-pack ingestion.

**Exit:** the project has a small game that can be played and measured. Findings
from this gate determine the next combat, itemization, and progression work.

## Item-custody simplification during Gate 1

The canonical store remains VRPG's item foundation. The schedule no longer waits
for proof that every possible Vanilla or third-party producer is intercepted.

- [ ] Inventory every native producer currently guarded and classify it as:
  removed by the VRPG preset, retained engine chokepoint, explicit VRPG transfer,
  or unsupported compatibility route.
- [ ] Disable or remove containers, crafting grids, trade inventories, ground
  storage, ordinary placement/breaking, and other unwanted producers at their
  owning system boundary.
- [ ] Keep strict transactions for real VRPG rewards, equipment changes, migration,
  persistence, and any pickup route deliberately retained for the slice.
- [ ] Replace broad producer-by-producer activation proof with one diagnostic at
  unavoidable native ingress points. It must identify the actual caller and item
  flow loudly enough to fix during playtesting.
- [ ] Retire a Harmony guard only after its producer is disabled or its retained
  flow has an explicit owner and test.
- [ ] Remove 100-percent native-producer coverage from activation and release
  criteria. Unknown diagnostics discovered in the live loop become concrete bugs.
- [ ] Resolve or explicitly migrate any existing pending bridge receipt before
  simplifying the guard responsible for it.

This work supports the combat slice when it deletes active producers or fixes a
real diagnostic. Hypothetical coverage does not block Gate 1.

## Gate 2 — one complete expedition

Begin only after Gate 1 produces a stable, enjoyable combat loop.

- [ ] Turn the arena loop into a generated route with a clear objective, three
  consequential choices, a boss, extraction, failure, reconnect, and cleanup.
- [ ] Add one status-propagation kit and multiple readable defeat responses.
- [ ] Generate one targeted reward family and deliver it through the canonical
  item store.
- [ ] Add deterministic seed regression plus solo and two-player integration tests.
- [ ] Keep Manifold as the working runtime until the expedition demonstrates a
  concrete transaction, isolation, or recovery requirement it cannot meet.

## Gate 3 — return-home loop

- [ ] Add a persistent Home with bounded building permissions and storage.
- [ ] Add one material transformation with useful, craft, and sell alternatives.
- [ ] Add one NPC buyer and one non-expiring targeted request.
- [ ] Validate inventory crash recovery, migration, overflow, and concurrent
  transactions against rewards earned in real play.
- [ ] Promote the server-backed inventory UI from its test route based on observed
  use. Expand beyond the first Inventory/Equipment semantic-owner handoff only
  when observed play identifies the next necessary page or action.

## Gate 4 — persistent nonlinear progression

- [ ] Add several regional objectives with alternate progression routes and
  persistent outcomes.
- [ ] Ship four first-slice classes with complete starter identities before
  expanding toward seven.
- [ ] Replace the production 426-node talent scaffold with 60–80 meaningful nodes
  and six keystones. Retain the large layout only as an editor stress fixture.
- [ ] Build item power, affix, salvage, crafting, and economy budgets from results
  of the playable gates.
- [ ] Complete the first progression and respec curve.

## Gate 5 — breadth and release hardening

- [ ] Add transactional Rebirth only after the first-life progression loop is
  complete and measured.
- [ ] Expand regions, bosses, factions, housing options, class breadth, creature
  ingestion, and addon-authoring tools from demonstrated needs.
- [ ] Revisit owned realm-runtime work when expeditions or housing strain the
  existing Manifold boundary.
- [ ] Complete long-soak, public-server permissions, recovery, accessibility,
  onboarding, and final menu-ownership release checks.

## Deferred from the current critical path

The following work is intentionally excluded from Gates 0 and 1:

- The complete unified six-page player menu.
- Loot piles, Everyone/Party drop policy, and further party features.
- Random affixes, salvage, crafting, and economy breadth.
- VRPG-owned realm runtime and Manifold parity work.
- Creature-pack ingestion.
- Custom main-world generation, housing, and Rebirth.
- Seven complete classes and a 426-node production talent tree.

These remain valid possible features. They return to the roadmap only when the
current playable gate consumes them or an earlier assumption proves false.

## Immediate next work

1. Complete Gate 0's joined-client verification loop and obtain player confirmation
   that both ordinary semantic actions stay out of the Vanilla UI.
2. Write the combat budget.
3. Build the deterministic arena and three enemy behaviors.
4. Connect the existing skills, starter gear, XP, death, respawn, and first usable
   Inventory/Equipment route into the ten-minute loop.
5. Simplify item custody only along producer paths encountered by that loop.
