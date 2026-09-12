# Audit: VRPG total-overhaul implementation strategy

Date: 2026-09-09. Revised the same day after establishing the intent behind the
item work; see [Item custody](#1-item-custody-the-store-is-right-the-guards-are-not),
which reverses part of the first draft.

Basis: the 28 design documents (10,870 lines), the three roadmaps, the active
checklists, and the actual source tree. Test suite was executed: **1,568 tests,
21s, all passing.** That part is genuinely healthy.

## Where the effort has actually gone

| Area | Lines | Share |
|---|---|---|
| Client / UI | 14,929 | 34% |
| Item custody (Inventory + Patches + Network) | 10,827 | 24% |
| RPG gameplay sim (skills, combat, status, talents, scaling) | ~5,800 | 13% |
| Data + definitions | 3,918 | 9% |
| Dungeons | 1,943 | 4% |
| Parties | 987 | 2% |
| **Worldgen** | **298** (one tavern) | 0.7% |
| **Enemy AI / pathfinding / spawning** | **0** | **0%** |

Content: 11 skills, 7 classes, 5 of 7 starter sets, 1 dungeon, 2 encounters,
2 affixes, 3 gear rarities. Talent tree: 426 nodes, **0 modifiers, 0 keystones,
6 named nodes**.

---

## The central flaw: the critical path is inverted

Three roadmaps disagree, and the one being executed is the one least connected to
the design's own pillars.

- `complete-game-design.md` Phase 1 = "Preserve the fun combat core" → AI behaviors, enemy roles, combat kits, temporary walls.
- `implementation-readiness` Gate 1 = "Protected combat sandbox" → arena, burst movement, five enemy behaviors.
- `testable-version-todo.md` (**the one being executed**) = replace the Inventory and Character dialogs, with worldgen, spawning, AI and encounters explicitly deferred until after.

So the gate blocking a combat-first game is a menu, and clearing it produces zero
gameplay. `overhaul-guide.md` "What comes next" makes this explicit: world
spawning, generation, encounters and combat all queue behind the menu handoff.

The precise problem is **not** that inventory was built early. Inventory friction
is a stated pillar of the overhaul's feel, and building its model early is
defensible (see the item section below). The problem is that the **atomic
`inventorydialog`/`characterdialog` ownership transaction** is gating everything
else, and that transaction is the single hardest, least reversible, lowest-yield
item on the list.

The VRPG store can be used in a combat sandbox today without owning either dialog:
open it from the Hub or `/vrpg inv`, leave Vanilla's menu registered and ignored.
That validates the "inventory should be invisible" thesis against real play, which
is the only way it can be validated. The dialog takeover then becomes a polish step
near the end rather than the gate at the front.

Secondary cost: the inventory *feature surface* keeps churning because loot does
not exist yet. Loot piles, party permissions, salvage preview, favorite/junk, and
Everyone/Party drop rules were all appended to the "usable replacement" checklist
mid-flight. Loot and itemization are graded **A (ethos only)** in the readiness
table. Expect more churn until a real drop exists.

**Reconcile to one roadmap and delete the other two.**

---

## Overengineering

### 1. Item custody: the store is right, the guards are not

*This section was revised. The first draft treated the whole item effort as
overengineering; that was wrong about roughly half of it.*

The intent is player-facing: inventory management is most of what a Survival
session actually consists of, and VRPG wants it to be something the player never
thinks about — on an engine that already has item desync and item-entity
performance problems. That goal justifies more of this work than the first draft
credited.

**What the goal genuinely requires — and is already built:**

- **Aggregated commodities.** `PlayerItemContract.cs:107` sets
  `AggregateQuantityPerEntry: long.MaxValue`. 10,000 iron is one row, not 100
  stacks across 100 slots. Vanilla `ItemStack` cannot express this — stack sizes
  and slot counts *are* the sorting chore. This alone removes most of the friction.
- **Server-authoritative store with revisions.** Inventory desync is the client
  and server disagreeing about slot contents. A revisioned canonical store with
  reconciliation is the correct fix for it.
- **Typed tabs and bounded paged queries.** No slot-Tetris, no hauling trips, and
  the network never ships hundreds of per-player slots.
- **Fixed large capacity, no backpacks.** Design decision, near-zero engineering.

All of that is **complete**: store, aggregation, unique separation, indexed
bounded queries, typed categories, migration, codec. It is load-bearing for the
pillar and it should stay.

**What is consuming the schedule and does not serve that goal:**

The Sept 9 work — roughly 25 commits — is not the store. It is *producer guards*:
refusing native inventory packets, background ticks, block place/break packets,
block-entity packets, entity packets, direct manager grants, deferred death
effects, clear commands, and queued offline wipes. Plus SHA-256 birth tokens,
tombstones for engine-restored sources, fsync + atomic rename + directory fsync
sidecars, dual-generation checksummed archives, pinned engine method bodies, and a
`ReconcileRequired` ledger state that locks reservations pending external
settlement.

None of that makes inventory simpler or fixes desync. It answers a different
question: *can Vanilla still touch these items behind VRPG's back?*

The coverage target is unbounded by construction. `player-item-flow-audit.md`
still has no adapter for signs, labeled chests, ground storage,
containers/shift-click, trade inventories, arbitrary admin commands, held/use/drop,
Home placement, death drops, or VRPG's own `TemporalRiftService.TakeOut(1)`. Every
new engine path, engine update, or mod adds another entry. Nothing in the docs
evaluates a cheaper posture; there is no "alternatives considered" section in any
item document.

**The cheaper posture: delete the producers instead of guarding them.**

Look at that producer list next to `complete-game-design.md` §15.5 and the
ownership map's scope groups 3 and 6. Ordinary block placement and breaking outside
housing, containers, mechanical systems, crafting grids, traders, and ruins are
**already slated for removal**. The guards being written are refusal logic for code
paths that a VRPG world has already decided will be unreachable.

Guarding is the expensive path. Not having the system is the cheap one, and it is
strictly stronger: a chest that cannot be placed cannot desync.

**Swap prove-then-activate for detect-log-fix:**

1. Delete or disable the producers rather than guarding them. That work is on the
   roadmap anyway and is mostly config/asset-level, not Harmony.
2. At the few engine chokepoints that genuinely cannot be removed
   (`TryGiveItemstack`, death drops), keep **one** boundary that **logs loudly**
   instead of nine that refuse provably. In a VRPG world it should never fire. If
   it fires in playtesting, that is a real caller — fix that caller.
3. Drop the 100%-native-producer-coverage release gate. It cannot be finished, and
   it is what currently holds activation hostage.

Same end state — VRPG is the only thing touching player items — reached by
subtraction rather than interception, and it yields a list of *real* callers
instead of hypothetical ones.

### 2. Rewriting a working MIT dependency before its consumers exist

`realm-runtime-architecture.md` adopts owning the realm runtime, derived from
Manifold, to gain "VRPG-specific transaction and recovery behavior" for expeditions
and housing — neither of which exists. Manifold currently works for dungeons.
`src/Realms/` is 106 lines. The doc already records 30 failing upstream tests that
would be inherited as debt. Defer until expeditions actually strain the boundary.

### 3. Party system inserted off the critical path

~15 commits on Sept 9, 987 lines plus UI, transport, and persistence — built so
dropped-item pickup could be scoped to a party. There are no drops, no loot piles,
and no verified multiplayer test loop. A dependency was added to serve a feature
that does not exist.

### 4. Documentation volume and the evidence-in-checklist antipattern

10,870 doc lines against 44k code lines, with the same status duplicated across
`overhaul-guide.md`, `testable-version-todo.md`, `usable-player-menu-checklist.md`,
`player-item-flow-audit.md`, and `README.md`. Sept 9 shipped roughly one
`docs: record…` commit per code commit.

The specific problem is prose evidence embedded in checkboxes:

> "reviewed through `a37ad5e` (source `8cf203b`): full 1,396 tests, then covering 107 after test-only assertion fixes"

Commit hashes and test counts belong in git. This is stale within a week,
unreadable, and it is a per-slice tax on every change. Collapse to one status file;
move evidence to commit messages.

Note also the readiness doc's own rule — a system "should not become a dependency
for several other systems until B and C are documented" — while listing 8 areas at
grade A and queuing 10 *more* design documents, another ~5–10k lines of design
before implementation.

---

## Gaps that will bite

### 1. The verification gate is currently unsatisfiable

The acceptance rule requires joined-server confirmation. The docs record,
repeatedly: "No clients joined", "the isolated client requires a valid game login",
"Authentication blocks native verification only; continue independent
implementation work". Every slice therefore completes to *source-reviewed +
isolated startup*, and unverified integration debt stacks up — precisely what the
strict gate existed to prevent.

**Highest-priority fix in this audit.** Get a login-capable client plus dedicated
server into a repeatable smoke loop before writing more code. Strictness without a
working verification loop produces documentation, not confidence.

### 2. No combat math budget exists anywhere

There is no documented target time-to-kill, player DPS budget, or incoming-damage
budget. Today: weapon damage ≈30× by level 100 (`baseDamage 10`, `+3.5%/level`)
against creature health ≈32× tier multiplier at 91+ plus `+12%/level`. Those
roughly cancel — by coincidence, not by design. Every talent, affix, and defensive
number authored from here is unanchored.

Write one page: TTK for ordinary/elite/boss at levels 1/20/50/100, expected player
eHP, expected incoming DPS. Before more numbers.

### 3. Removal is sequenced ahead of replacement

Hunger, temperature, nutrition, spoilage, durability, crafting, tutorials, traders,
spawning, worldgen, building, and both menus are all being stripped. What remains
of Vintage Story is the renderer, the voxel world, the entity/animation system, and
the asset pipeline. Each removal is pure cost with no player-facing gain until VRPG
content fills the hole — and right now that gap is total: no spawns, no AI, no
worldgen, no loot.

Not an argument for keeping hunger. It is an argument to sequence removal per
system, *behind* its replacement, rather than removing everything up front so the
Character screen becomes removable.

**One exception, and it is now load-bearing:** the removals that delete item
producers — containers, ground storage, crafting grids, trade, ordinary block
placement — should move *up* the schedule, because they are what makes the guard
work unnecessary. Removal is the item plan.

---

## Gameplay design risks

### 1. Defensive layers will break the difficulty curve

The gear spec sets each cap independently: Evasion 90%, Armor 90% soft / 99% hard,
elemental resistance 75%, plus Magic Shield, plus Health, plus Evasive Step as a
lethal-hit save. It further states a maintained build should cap *every* elemental
resistance *while* maximizing one primary defense.

Multiplied out: 0.10 × 0.10 × 0.25 ≈ **400× effective HP** before Magic Shield and
Evasive Step. Enemy damage grows only 5.7× by level 91. The outcome is the classic
ARPG defense spiral: geared players are unkillable, so boss damage is tuned to
one-shot, so anyone slightly under-geared is deleted. This is being locked in
before a single enemy exists to test it against.

Fix the direction of derivation: set a total effective-HP budget first (e.g. "no
build exceeds ~8× baseline eHP at cap"), then derive per-layer caps from that
budget.

### 2. Evasive Step violates two of the design's own pillars

Automatic, no slot, no hotkey, saves an otherwise lethal hit — on every DEX-primary
character. Pillar 2.2 says agency exists *during play*; this is agency-free. Pillar
19.2 says no universal best build; this makes DEX strictly the best defensive
attribute at the attribute layer, before talents exist. Make it a spent resource, a
slotted choice, or a timing window.

### 3. The 426-node talent tree is a trap

420 unnamed "Stat Node"s, zero modifiers, zero keystones. The design says "builds
change verbs" and "favor transformations over additive bonuses" — but the authored
scaffold is 100% additive filler *by construction*. Filling 426 nodes is weeks of
balance work that gets redone the moment real combat exists. The first-slice
content model asks for four builds and 12–16 skills; it never asks for 426 nodes.

Ship 60–80 meaningful nodes with 6 keystones for the first slice. Keep the 426
layout as an editor stress fixture.

### 4. Class breadth outruns depth

Seven classes on three attributes (STR/DEX/INT), 11 skills total, 2 of 7 starter
sets missing. Seven classes cannot feel distinct on 11 shared skills. Cut to 3–4
for the first slice — the content model already says four, including Rust Scrapper.

---

## What is genuinely working — do not disturb it

- **1,568 tests in 21 seconds, all green**, mostly pure-domain and runnable without
  booting the game. Rare in modding; the project's real asset.
- The canonical item store, aggregation, codec, and migration (see the revised item
  section — this is the correct foundation for the inventory pillar).
- Data-first definitions with validators, and the 1,024-seed deterministic tavern
  validation.
- The plan-and-commit talent editor.
- **Honest status discipline.** The docs consistently refuse to overclaim, separate
  source-review from live acceptance, and record failures — the Sept 9 screenshot
  showing Vanilla menus still open was recorded as an acceptance *failure*. Keep
  that instinct; just make it cheaper to maintain.

---

## Recommended re-cut, in order

1. **Fix the verification loop.** Login-capable client + dedicated server + a
   repeatable manual smoke script. Nothing else clears until this exists.
2. **Re-target the next milestone to a vertical playable slice:** one arena, 3
   enemy types with simple AI, the existing 11 skills, existing starter gear, XP,
   death → respawn. Open the VRPG inventory from the Hub or a command — **no dialog
   replacement.** Goal: 10 minutes that can actually be played. This is the
   project's own Gate 1 and its own Phase 1.
3. **Keep the item store; stop guarding producers.** Move the container, crafting,
   trade and block-placement removals up the schedule so the guarded paths cease to
   exist. Replace prove-then-activate with one loud detect-and-log boundary. Drop
   the 100%-coverage activation gate.
4. **Write the combat math budget** (1 page) before authoring another number.
5. **Collapse the four status trackers into one;** evidence goes in commit messages.
6. **Cut the talent tree** to 60–80 nodes and classes to 4 for the first slice.
7. **Defer:** realm runtime ownership, creature ingestion, housing, Rebirth, the
   unified six-page menu, loot piles, and party. None are on the path to something
   playable.

The one-sentence version: the inventory *model* is right and largely done — what is
burning the schedule is proving custody against an engine you have already decided
to delete, behind an acceptance gate whose verification loop does not run, for a
game that still has no enemies in it.

---

## 2026-09-12 clarification: first usable Adventure menu

The audit's vertical-slice correction remains in force: do not restore the old
exhaustive menu, producer-coverage, or documentation roadmap ahead of a playable
combat loop. The first usable Adventure profile has, however, clarified one narrow
prerequisite that the original re-cut overstated as optional: ordinary Inventory
and Character actions must no longer expose a competing Vanilla UI once canonical
VRPG custody is active.

That does **not** require proving compatibility with every theoretical native or
third-party item producer. The implemented boundary is deliberately small:

- swap the two semantic dialog owners together, preserving player key bindings;
- route them to the server-backed Inventory/Equipment page;
- suppress unwanted Adventure producers at their owning systems; and
- fail closed after canonical custody rather than reintroducing Vanilla inventory
  as a live fallback.

Later Stats, Skills, Talents, Library, Options, crafting, storage, drops, loot
piles, party UI, Home bridges, combat breadth, and worldgen stay outside this
first-pass completion claim. Source, automated test, Release-build, deployment,
and disposable-server startup evidence do not replace a joined-client check or
explicit player confirmation; those remain the only path to calling the handoff
replacement-ready.
