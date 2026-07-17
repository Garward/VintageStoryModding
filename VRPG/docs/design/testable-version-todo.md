# Testable Version Live TODO

Last reconciled with code and design: **2026-07-17**

This is the living implementation checklist for getting VRPG into players' hands. The initial list supplied during development is a set of immediate observations, **not** the scope boundary or a claim that those are the only remaining tasks. This checklist is audit-driven from the design documents, executable code, data assets, UI, persistence, networking, and playtest requirements, and it must expand whenever implementation or testing exposes another dependency.

Update an item to complete only after its acceptance check has been exercised in game. Add newly discovered work here before allowing it to disappear into chat history. Do not mark the build testable merely because the most visible UI tasks are complete.

## Status Key

- `[x]` — implemented and verified at its current scope.
- `[ ]` — remaining work.
- **Partial** — useful implementation exists, but it does not yet satisfy the testable-version requirement.
- **Decision** — a product choice is required before implementation should be finalized.

## Two Delivery Gates

The high-level design describes a complete gameplay vertical slice, but the current immediate goal is smaller. Keeping these gates separate prevents loot, rifts, and crafting from blocking useful combat and build testing.

### Gate A — Internal Build Playground

This is the next testable version. Admin bootstrap commands are acceptable. A fresh player must be able to configure a build, read it, operate it in combat, and reconnect without losing it.

The build playground is ready when a tester can:

- Open a clean Hub and understand stats, talents, classes, skills, and options without redundant panels.
- Choose a talent-tree starting foundation and spend/refund test points through the UI.
- Browse readable class and skill presentations with recognizable icons.
- Assign every learned active skill to a real four-slot VRPG hotbar from the Skills page.
- Move and lock the hotbar, rebind each slot independently, and see the current bindings on the bar.
- Fight RPG-eligible enemies using raycast, projectile, circle, forward-arc, line/thrust, and precise single-target skills, including one sequence and held channel, without Energy appearing anywhere.
- See cooldown, cost, insufficient-resource, hit, experience, and level feedback without chat or particle spam.
- Spend stats and talents that measurably change executable combat or defense outcomes.
- Save, disconnect, reconnect, and recover the same build, loadout, options, and hotbar configuration.
- Complete a short two-player dedicated-server smoke test without desync, duplicate rewards, cross-player state, or crashes.

Gate A does **not** require final class acquisition, final balance, randomized loot, salvage, crafting, a boss, or a complete rift. Those belong to Gate B.

### Gate B — One-Hour Gameplay Vertical Slice

This is the larger loop defined in the high-level design:

```text
explore / craft
        ↓
acquire and prepare a Rift Chart
        ↓
enter a curated rift encounter
        ↓
fight a staged objective and boss
        ↓
receive, compare, salvage, and refine loot
        ↓
make a clear next-build decision
        ↓
choose to run another rift
```

Gate B remains tracked below, but it must not expand Gate A unless playtesting proves a missing system prevents meaningful combat/build evaluation.

## Verified Baseline

- [x] JSON-authored skills support instant raycast area, projectile area, and caster-centered circle delivery.
- [ ] **Partial — implemented, awaiting multiplayer/in-game acceptance:** JSON-authored melee supports server-owned forward arc, forward line, and precise single-target shapes with explicit reach, collision-size tolerance, solid-block obstruction, and no dependency on vanilla weapon range.
- [ ] **Partial — implemented, awaiting multiplayer/in-game acceptance:** timing supports independent multi-hit sequences and held channels with server ticks, prorated per-second costs, finite duration, release/death/resource/other-skill cancellation, and cooldown-on-channel-end.
- [x] Skill level damage/cost formulas, resource checks, cooldowns, models, particles, and colors execute server-side.
- [x] Admin commands can grant, remove, equip, inspect, and cast skills for testing.
- [x] Four persisted server-side skill loadout slots exist and cast through rebindable Vintage Story hotkeys.
- [x] Player level, XP, resources, stat points, talent points, learned skills, loadout, and notification options persist by player UID.
- [x] The Hub has focused Stats, Skills, Talents, Library, and scalable category-based Options pages.
- [x] Stat allocation is server-authoritative and blocked while the player is actively in combat.
- [x] Dexterity-primary Reflex and Evasive Step provide an early answer to single large hits.
- [x] Cooldown and insufficient-resource notifications are configurable and rate-limited.
- [x] Talent graph rendering supports selection, pan, zoom, links, node states, and detail inspection.
- [x] Release builds package and copy directly into the Vintage Story Mods folder.
- [x] A project-native UI authoring guide now defines screen contracts, workbench and in-game visual gates, shared patterns, rejection conditions, and a review scorecard.
- [ ] **Partial — implemented, awaiting in-game acceptance:** the combat-visuals framework (server event broadcast/degradation budgets, ground-area telegraphs, status-effect overlay/auras, floating combat text, player-state HUD, Hub visual options, and `/vrpg vfx event|status|area|empower|stress` admin test commands) satisfies "see cooldown, cost, insufficient-resource, hit, experience, and level feedback without chat or particle spam"; the in-game/dedicated-server acceptance pass has not yet been run.
- [ ] **Partial — implemented, awaiting in-game acceptance:** Hub talent allocation/refund is server-authoritative, enforces starter/connectivity/point/combat rules, and returns visible failure feedback.
- [ ] **Partial — implemented, awaiting in-game acceptance:** talent hover and selected-node views lead with large resolved effect values, demote cost/state/instructions, suppress empty scaffold descriptions, and keep connection counts plus ordinary-node sector taxonomy in the admin editor.
- [ ] **Partial — implemented, awaiting in-game acceptance:** Skills is data-backed and shows icon tokens, learned rank, resolved prototype values, and real four-slot loadout controls. Normal skill learning/ranking remains future work; admin grants remain the playground path.
- [ ] **Partial — implemented, awaiting in-game acceptance:** the combat HUD is driven by the persisted server loadout and synchronized cooldowns, shows current remapped bindings/rank/cost/resource state, and has a locally persisted drag/lock mode.
- [ ] **Partial:** creature levels and XP exist, but RPG eligibility currently infers from generic entity properties instead of the explicit hostile registry required by design.
- [ ] **Partial:** talent modifiers affect a subset of resource calculations; offensive modifiers and several displayed build promises do not yet affect skill damage.
- [ ] **Partial:** the rift-chart fallback can spawn timed overworld waves, but the Manifold doorway, objectives, boss, rewards, and failure loop are not complete.

## P0 — Remove Energy Completely

Energy has been cut from VRPG. Dexterity's identity is evasion, critical access, speed, and Reflex/Evasive Step—not ownership of a third universal resource bar.

**Implementation status:** the active C#, packets, JSON definitions, documentation, Hub, HUD, and workbench contain no executable Energy system. Static search, JSON parsing, and compilation pass; clean-save and old-save in-game acceptance remain required before these boxes become verified.

- [ ] Update the high-level resource rules so Energy is no longer an active or future core resource.
- [ ] Remove `max_energy` and `energy_regen` stat records and remove Energy from resource stat-family data.
- [ ] Remove Energy gains from Dexterity descriptions and conversion data.
- [ ] Change `Fracture Pulse` away from Energy. For the playground, use `none` and let cooldown control it until a real class-specific mechanic exists.
- [ ] Remove `energy` from skill validation and authoring documentation.
- [ ] Remove Energy fields and calculations from config, resource totals, regeneration, player runtime state, commands, and debug output.
- [ ] Remove Energy from resource packets without reusing its old protobuf member numbers.
- [ ] Remove Energy from the external resource HUD, Hub character summary, recovery fields, tooltips, sample data, and UI workbench.
- [ ] Treat obsolete Energy fields in existing JSON player saves/configs as ignored migration input; loading an old test save must not fail.
- [ ] Search active source, assets, config, UI, and current documentation for `energy`; any remaining use must be an explicit migration note or unrelated English prose.
- [ ] Verify Mana, Magic Shield, Blood, health, and their regeneration still reconcile correctly after the resource model shrinks.

**Done when:** a clean and an existing player save both load, all three prototype skills can cast, and no Energy value, bar, cost, stat, command option, tooltip, or data definition is visible or executable.

## P0 — Make Displayed Build Choices Executable

The playground is not useful if allocation changes labels without changing play.

- [ ] Create one resolved player-stat pipeline used by resources, defenses, skill damage, tooltips, and later gear.
- [ ] Apply Strength, Dexterity, and Intelligence effects from the actual allocated-stat state rather than duplicating hand-written UI claims.
- [ ] **Partial:** allocated talent modifiers now feed the authoritative skill-damage resolver for total, skill, damage-type, elemental, spell, attack, projectile, area, and skill-tag damage; the Hub displays the same resolved value used on hit. Critical, speed, armor, evasion, status effects, gear, and several conditional families remain.
- [ ] Apply the resolved offensive values to server-authoritative skill damage.
- [ ] Implement or explicitly suppress every stat displayed in the compact Hub; never present a modifier as active when runtime ignores it.
- [ ] Generate current and next-rank skill values from the runtime resolver so the Skills page cannot drift from combat.
- [ ] Add a small admin/debug breakdown for one resolved skill hit to make balance errors inspectable.
- [ ] Define a repeatable dummy-target check showing that a stat point, talent, and skill rank each change the expected result.

**Done when:** two deliberately different allocations produce measurably different damage, sustain, or defense in game, and the UI values match the server result.

## P0 — Talent Starting Routes

The first test tree should prove starting identity and graph expansion, not attempt to ship the final Path of Exile-scale tree.

**Implementation status:** the generated 426-node geometry is the default runtime tree and exposes six mutually exclusive global starting routes arranged as a wide central hexagon. Expansion must follow the [talent tree authoring rules](talent-tree-authoring-rules.md). The native Hub consumes a reusable graph component backed by a standalone, versioned server snapshot instead of bundling node definitions into each player Hub packet. Saved tree revisions synchronize online clients and recalculate player resources without a restart. The admin editor, saved-tree library, and built-in template reset flow are implemented; manual graph topology editing and export remain.

**Content planning:** the [initial class skill roster](initial-class-skill-roster.md#talent-combos) defines fifteen reusable talent-combo packages and audits their coverage across eighteen intended class styles. These are planning packages rather than exclusive lanes. Exact nodes and values wait for executable stat contracts and budget units.

Twelve reusable [gamechanger coverage targets](initial-class-skill-roster.md#gamechanger-coverage-targets) reserve transformation concepts for critical, ailment, hit, payoff, placed-effect, range, minion, support, Blood, mixed-damage, and prepared-target builds. They are not approved nodes and remain subject to ordinary-section playtests and balance simulation.

The [first six class skill specification](first-six-class-skill-spec.md) now
defines five actives, nine passives with eight ranks each, three intended loops,
survival layers, boss/mob strengths, numeric seeds, cross-class states, and acceptance tests for
Smith, Trapper, Pilferer, Warden, Corroder, and Handler. Implementation remains
blocked on the shared status, critical, barrier, control, minion, and placed-
effect runtime contracts listed there.

- [x] Confirm six mutually exclusive global starts in a wide hexagon near the center of the full tree, covering every ordered STR/DEX/INT dominant-secondary pairing with no direct links between starts.
- [x] Set the full-tree scale target at roughly 400 meaningful nodes, balanced around approximately 100 normal complete-build points and 125 late-game points.
- [x] Add a deterministic layout manifest and generator with persistent manual offsets instead of unconstrained hand-authored coordinates.
- [x] Add workbench pan, zoom, radial guides, node dragging, reset actions, layout export, and browser interaction coverage.
- [x] Generate and visually audit a 426-node capacity scaffold with six route-scale starts, one 42-node express ring, ten-node major roads, six ten-node optional inner spiderwebs, six five-node outward web lanes, and 30 silhouette-coded specialization pods distributed across those webs.
- [x] Remove the redundant near-perimeter circle and giant direct road chords; every outward lane links an inner web to a distinct perimeter entrance, while nearby routing happens through the inner cross-rungs.
- [x] Enforce the express-ring hierarchy: every non-perimeter link must be strictly shorter than the shortest perimeter-to-perimeter step.
- [x] Replace compressed specialization knots with sparse split/rejoin choices, distribute them across inner/middle/outer web depths, cap internal pod links at `N + 2`, and enforce at least `1.8D` node spacing.
- [x] Give every inner spiderweb paired five-node outer/inner paths and three cross-rungs; reject a web if removing any single internal node disconnects its two junctions.
- [x] Reject node overlap, unrelated edge crossings, and unrelated links entering another node's protected socket radius during generation.
- [x] Reserve gamechangers as one-edge leaves 10–20 allocations from their matching start, with at least three ordinary graph links between attachment nodes and ordinary progression continuing independently of them.
- [ ] **Partial — implemented, awaiting in-game acceptance:** extract talent layout, fit, zoom, pan, hit testing, node-state evaluation, edge rendering, and socket rendering into a reusable snapshot-driven client component.
- [ ] **Partial — implemented, awaiting dedicated-server acceptance:** send the active tree as a standalone schema-versioned, content-hashed server snapshot and cache it client-side; Hub updates carry only player-specific allocation state and a tree identity.
- [ ] **Partial — implemented, awaiting destructive edit/reload acceptance:** add one saved-revision synchronization boundary that broadcasts changed trees, refunds removed/disconnected allocations, preserves health percentage, clamps derived resources, and refreshes online players without a restart. Unchanged saves do nothing.
- [ ] **Partial — implemented, awaiting in-game acceptance:** use the generated six-sector scaffold as the default geometry-only active tree with 426 nodes and no stat modifiers, expose it as an editor template, and migrate the persisted nine-node `vrpg:default` fixture automatically while preserving custom authored trees.
- [ ] **Partial:** the server-authored Hub admin flag exposes `ADMIN · EDIT TREE` only to `controlserver` players, with privilege checked again server-side; `/vrpg talenteditor` opens the same editor directly. The top-down editor preserves pan/zoom/admin selection across modifier, tree/node rename, and Save responses; supports a persistent saved-tree library, Save As New, guarded deletion, built-in template reset, tree/node naming, stat category/filter/search, Flat, additive Increased %, multiplicative More %, add/replace/remove modifiers, and set/replace/clear node names. Player selection is isolated client state and resets on a new server tree revision. Manual positioning, link editing, remaining metadata, export, and full validation UI remain.
- [ ] **Partial — implemented, awaiting multiplayer acceptance:** keep all admin edits and template resets in a per-admin isolated draft and synchronize gameplay only when Save or Save As New activates a revision.
- [ ] **Partial:** persist multiple flattened world-authored trees in the saved-tree library. Compact template/overlay export and import remain.
- [ ] Add workbench import for saved/exported mod trees after the in-game editor path is accepted; preserve node codes/manual offsets and report schema or graph validation errors before editing.
- [ ] Finalize the six route identities and their allowed stat families; the six equal `+10` dominant, `+5` secondary ordered stat packages are settled.
- [ ] Add explicit route and starting-node fields instead of treating any starter-marked node as a valid first allocation.
- [ ] Selecting a starting route records it and locks the other five routes for that tree life or full-respec state.
- [ ] Set STR/DEX/INT tie affinity from each selected start's dominant raw stat, then replace the “first allocated stat silently chooses affinity” workaround.
- [ ] Author one coherent first section growing outward from each of the six starts using only executable stat families.
- [ ] Establish raw-attribute major roads, compact forks, and terminal 5–8-node or large 9–12-node specialization pods; each pod must have one mechanical family, Tier-1 entries, Tier-2 depth, and an ordinary road alternative when it reconnects as an optional through cluster.
- [ ] Connect mature route regions through ordinary bridges that bypass the five locked starts and never require a gamechanger.
- [ ] Build a mostly symmetrical six-sector radial lattice with inner, middle, outer, and continuous perimeter bands.
- [ ] Keep each sector near a 67-node planning budget, with no more than roughly 10% asymmetry without explicit review.
- [ ] Ensure every ordinary node is reachable from every selected start while keeping the other five starts locked.
- [ ] Author the 42-node perimeter circuit with seven small Tier-1 STR/DEX/INT nodes per sector; make corner-to-corner edge travel cheaper than dense inner travel and target roughly 61–67 points for the cheapest approach plus the full circuit.
- [ ] Give starting, Tier-1, Tier-2, bridge, and gamechanger nodes visibly distinct sockets or treatments.
- [ ] Add server-authoritative allocate/refund packets and perform point, connection, starter-lock, and combat-lock validation server-side.
- [ ] **Partial — implemented, awaiting in-game acceptance:** replace per-node Allocate with a client-side plan: free starter selection and affordable connected nodes highlight, left-click plans allocation, right-click plans a connectivity-safe refund using persisted respec points, and one Apply packet is validated and committed atomically server-side. The legend mirrors all runtime states: Available, Planned, Allocated, Needs points, Refund plan, Not connected, and the separate Free start silhouette.
- [ ] Add complete inline failure reasons for rejected local refund gestures and server-rejected plans; current server feedback and node states cover the main cases but silent invalid right-clicks need clearer feedback.
- [ ] Apply allocated modifiers through the resolved player-stat pipeline.
- [ ] Add a temporary accessible full-tree reset for playground testing; final respec cost remains a later design decision.
- [ ] Validate graph references, reciprocal links where required, unreachable nodes, duplicate starter groups, and impossible costs at startup.
- [x] Define repeatable geometry, Tier-1 and Tier-2 budgets, section-theme rules, lore constraints, ordinary bridges, and optional-gamechanger requirements.
- [ ] Add route, section, tier (`start`/`tier1`/`tier2`/`bridge`/`gamechanger`), mechanical role, topology role, specialization-pod ownership, budget, starting-node, and gamechanger metadata to the data contract.
- [ ] Validate central hexagon spacing, six-sector symmetry, complete ordered stat-pair coverage, band ownership, Tier-1 and Tier-2 rules, specialization-pod size/family/attachment topology, section stat families, whole-tree reachability, perimeter-cycle budget, unsupported effects, gamechanger depth/separation/articulation, and protected geometry at startup.
- [ ] Add a graph audit reporting total nodes, sector counts, tier counts, reachability, band costs, and perimeter costs at both 100 and 125 points.
- [ ] Replace the nine-node fixture with six starting routes and their first ordinary sections before authoring any gamechanger.

**Done when:** a new tester can choose one of six central routes, see the other starts lock, allocate outward through a themed section, observe a real mechanical result, refund/reset, and reconnect with the same valid tree.

## P1 — Data-Backed Classes and Basic Icons

Class metadata must remain data-backed so new class content does not turn `RpgModule` or the Hub into an authoring bottleneck.

**Implementation status:** class metadata now loads from six JSON records; skill-to-class references, class colors/icons, learned ranks, resolved presentation values, and equipped slots flow through the registry and Hub packets. The current icon set is a code-native prototype glyph set and still needs in-game legibility acceptance.

- [ ] Add a `ClassDefinition` registry under `assets/*/vrpg/classes/` with code, name, description, icon, color/accent, tags, and sort order.
- [ ] Validate that every skill's `classCode` references a loaded class.
- [ ] Add `icon` to skill definitions and validate missing assets with actionable startup errors.
- [ ] Extend Hub class/skill packets with class icon, skill icon, learned rank, maximum rank, required level, resolved cost, cooldown, damage summary, and equipped slots.
- [ ] Create a consistent basic icon set for Smith, Trapper, Pilferer, Warden, Corroder, and Handler.
- [ ] Create recognizable prototype icons for the playground skills; silhouettes must remain readable at 24–48 pixels.
- [ ] Use a shared fallback icon for addon content while logging the missing optional presentation asset.
- [ ] Remove the hard-coded class array after data-backed output matches it.

**Done when:** adding a class or skill presentation requires data/assets rather than editing Hub C#, and every current class is recognizable without reading its name.

## P1 — Replace the Skills List With a Build Screen

**Implementation status:** the approved three-column class/skill/inspector layout is implemented in the workbench and native Cairo Hub. Four real server-authoritative loadout slots replace the decorative strip. Next-rank comparison and in-game sizing/input acceptance remain.

- [ ] Use icon-led class cards or tabs rather than a text-only class column.
- [ ] Present skills in a readable grid/tree with distinct states: locked, available to learn, learned, rankable, max rank, and equipped.
- [ ] Keep one focused detail pane showing description, tags, delivery, current rank, next-rank change, resolved damage, cost, cooldown, and requirements.
- [ ] Show why a skill is locked instead of merely dimming it.
- [ ] Show learned/rank investment separately from player-level requirement.
- [ ] Add search/filter only if the first content set exceeds what the class view can read without scrolling.
- [ ] Remove the decorative fake “Skill Bar” currently drawn in the detail pane.
- [ ] Embed the real loadout slots in the Skills page.
- [ ] Allow a selected learned skill to be assigned to any compatible hotbar slot; allow a slot to be cleared or replaced.
- [ ] Make assignment server-authoritative and reject unknown, unlearned, unavailable, or incompatible skills.
- [ ] Refresh the Hub and combat hotbar immediately after a successful assignment.
- [ ] Keep admin grant/equip commands as test tools, but make normal loadout editing possible without chat commands.

**Done when:** a tester can understand a skill, compare its next rank, and assign/replace/clear it from the Skills page without knowing its data code.

## P1 — Real Editable Combat Hotbar

The bar defaults to four frequently activated ability slots and can expose between four and eight. Hidden slots retain their assignments. Long-duration and stance-specific presentation can follow evidence from class prototypes.

**Implementation status:** a native HUD element now receives a dedicated server loadout packet, predicts synchronized cooldown countdowns, resolves live control bindings, reports rank/cost and insufficient resources, updates after casts/equips, and persists a clamped local position plus lock state. Dialog layering, reconnect, GUI-scale, and multiplayer acceptance remain.

- [ ] Replace the decorative Hub strip with a real HUD element driven by the persisted server loadout.
- [ ] Show skill icon, actual bound key, resource cost, and unavailable state per slot without clutter; rank remains in Skills because it is unnecessary during combat.
- [ ] Synchronize cooldown end times so each slot can render a cooldown sweep/countdown without relying on chat failures.
- [ ] Render insufficient-resource and invalid-skill states clearly without flashing the whole bar.
- [ ] Register one independently rebindable Vintage Story control for every slot and display the resolved binding rather than hard-coded `Alt+1` text.
- [ ] Preserve `Alt+1` through `Alt+8` as initial defaults unless playtesting finds a conflict.
- [ ] Add hotbar edit mode: unlock, drag anywhere on screen, clamp to visible bounds, and lock against accidental movement.
- [ ] Persist layout as a screen-relative anchor so resolution and GUI-scale changes do not strand the bar off-screen.
- [ ] Add scale and horizontal/vertical orientation only if they do not delay basic positioning and locking.
- [ ] Put hotbar edit/lock/reset controls under Hub → Options → Interface.
- [ ] Hide or safely layer the bar when dialogs, chat, inventory, or spectator/death states require it.
- [x] Keep Evasive Step as an always-active DEX-primary lethal-hit passive with no hotkey or hotbar slot.
- [ ] Verify casting, cooldown display, loadout changes, key rebinding, and layout persistence after reconnect and client restart.

**Done when:** each visible slot casts exactly the skill it displays, follows its own controls binding, reports cooldown/resource state, and remains where the player locked it.

## P1 — Hub Redundancy and Readability Cleanup

- [ ] Remove Energy as part of the resource pass before judging final spacing.
- [ ] Show level and unspent point totals once in the persistent footer or once in the active page—not both.
- [ ] Remove generated-data-entry counts and “server-authored state” from character-facing pages; keep diagnostics in admin/debug output.
- [ ] Do not repeat full resource bars in both the external HUD and multiple Hub panels unless the active page needs them for a decision.
- [ ] Show only resources active for the current build: health always, Magic Shield when present, Blood when unlocked, and Mana when a learned/equipped mechanic uses it.
- [ ] Keep stat descriptions in hover/expanded tooltips and keep allocation rows focused on name, current value, primary state, and controls.
- [x] Remove the obsolete Evasive Step binding and resolve combat-slot labels from their registered controls.
- [ ] Remove unused legacy layout methods after the active Stats, Skills, Talents, Library, and Options paths are confirmed.
- [ ] Ensure every page has one clear title, one primary interaction area, and no decorative control that looks functional.
- [ ] Check 920×560 minimum Hub bounds, 1040×620 normal bounds, GUI scales 80–150%, and common 16:9/16:10 resolutions in the UI workbench and game.

**Done when:** every visible field answers a player question or supports an action, and no value/control is repeated without a specific reason.

## P1 — RPG Enemy Eligibility and Combat Test Integrity

The current entity predicate is broader than the accepted design and can pull wildlife into RPG progression.

- [ ] Replace generic `EntityAgent + health` inference with an explicit entity-code/tag/registry eligibility rule.
- [ ] Include intended hostile vanilla drifters and rift enemies in the first rule set.
- [ ] Exclude passive wildlife, livestock, traders, NPC-like entities, players, and unrelated mod entities by default.
- [ ] Ensure excluded entities receive no VRPG level, rarity, scaling, health bar, or RPG XP.
- [ ] Attribute skill kills and XP reliably for raycast, projectile, circle, status, and future minion damage.
- [ ] Replace proximity-only group XP qualification with contribution-aware logic for the later multiplayer gate; for Gate A, document the temporary behavior visibly.
- [ ] Add a debug inspect command that reports why an entity is eligible or excluded.

**Done when:** killing ordinary wildlife never advances VRPG, while configured hostile test enemies consistently level, scale, display, and award XP.

## P2 — Playground Content and Feedback

- [ ] Decide the minimum playable class pair for Gate A. Current recommendation: Smith + Corroder, with temporary Pilferer/Warden grants used to exercise single-target sequence and line delivery until the normal class-acquisition path exists.
- [ ] Give each playground class enough skills to test a primary skill plus support/utility interaction rather than only isolated buttons.
- [ ] Keep the other four class candidates visible only if the UI clearly labels them as prototypes; empty class pages should not appear finished.
- [ ] Add at least one single-target pressure case, one clustered group, and one large telegraphed hit for Evasive Step testing.
- [ ] Add hit confirmation and readable damage feedback without screen-filling particles.
- [ ] Confirm ray, projectile, circle, arc, line, and single-target visuals remain readable in first person, third person, groups, walls, slopes, and low ceilings; reconcile this with the concurrent skill-visual revamp before marking accepted.
- [ ] Verify Hammer Blow and Thrust ranges are unchanged across held vanilla weapons with substantially different native interaction reach.
- [ ] Verify Flurry produces four independent health changes and per-hit hooks, does not lose hits to ordinary invulnerability frames, and handles target movement/death predictably.
- [ ] Verify Grinding Sweep tap/hold/release, four-second safety termination, other-skill cancellation, death/disconnect cleanup, channel cooldown boundary, and per-second resource depletion across a dedicated-server connection.
- [ ] Confirm rapid casting never floods chat and notification toggles persist.
- [ ] Add a short admin playground setup command that grants points, prototype skills, and appropriate test level without manually issuing many commands.

## P2 — Persistence, Networking, and Regression Pass

- [ ] Create a clean-save and old-test-save migration checklist before removing Energy fields.
- [ ] Verify all Hub mutations are server-authoritative and malformed packets cannot grant points, talents, or skills.
- [ ] Verify player A cannot mutate or receive player B's stats, options, talent tree, loadout, or cooldowns.
- [ ] Verify reconnect, death, dimension transfer, and server restart preserve or reset each state intentionally.
- [ ] Test an integrated server and a dedicated server with two players.
- [ ] Test client/server version mismatch failure clearly during rapid development.
- [ ] Run a short combat soak with at least 30 eligible enemies and all delivery/timing types while watching server tick time and client frame time.
- [ ] Keep Release build at zero compiler warnings and ensure auto-deployed contents match the packaged zip.
- [ ] Update README commands, skill authoring guide, modeling guide, and this checklist whenever the executable contract changes.

## Gate A Release Checklist

- [ ] All P0 items complete.
- [ ] Data-backed class/skill icons complete for the playground content.
- [ ] Skills build screen and server-authoritative loadout editing complete.
- [ ] Real movable/lockable/rebindable hotbar complete.
- [ ] Talent starter selection and UI allocation complete.
- [ ] Hub redundancy pass complete.
- [ ] Explicit RPG enemy eligibility complete.
- [ ] Clean-save, old-save, solo, dedicated-server, and two-player checks pass.
- [ ] Known limitations are written in README and do not contradict visible UI promises.
- [ ] A tester unfamiliar with data codes completes the Gate A flow without using commands except the single bootstrap command.

## Gate B — Complete Gameplay Loop Backlog

These items are required for the high-level-design vertical slice, not the immediate build playground.

### Classes and Progression

- [ ] Implement normal first- and second-class selection and enforce the dual-class foundation.
- [ ] Implement skill-point earning, unlock/rank costs, prerequisites, and accessible respec friction.
- [ ] Enforce ten active-skill ranks and eight class-passive ranks; tune the final
      two-class point budget so several passives necessarily remain unpurchased.
- [ ] Keep skill activation weapon-agnostic and move weapon identity into
      favored affix pools, affix weights, and item modification.
- [x] Define the common Weapon Power contract: a compounding persistent
      required-level baseline, then Flat/Additional/More weapon affixes, then a
      bounded rarity scalar; no target-level comparison or stale-item penalty.
- [x] Add rank-resolved Weapon Damage effectiveness to skill data and assign
      rank-1 seeds to the four executable playground skills and all thirty
      planned launch actives.
- [x] Extend the offline damage sweep with weapon required level, 0/20/40-level
      comparison bands, gear rarity power scalars, weapon affix layers, and
      exported Weapon Power/effectiveness fields.
- [ ] Persist required level, rarity, affixes, and resolved Weapon Power on
      actual VRPG weapon stacks and synchronize the authoritative breakdown.
- [ ] Route executable skill damage and ordinary attacks against RPG-eligible
      enemies through Weapon Power; retain native attack power against excluded
      vanilla targets.
- [ ] Give unmodified vanilla weapons the explicit level-1 VRPG fallback and
      add the crafting/drop/upgrade path that authors higher-level VRPG data
      without double-counting native material attack power.
- [ ] Add weapon tooltip rows for required level, final Weapon Power, and the
      advanced level/Flat/Additional/More/rarity breakdown.
- [ ] Establish top-end clear-time bands, then verify a comparable weapon
      twenty levels behind fails the intended band while an exceptional roll
      may bridge one band and a forty-level-old weapon remains noncompetitive.
- [x] Increase provisional creature health tiers so ordinary health-to-current-
      weapon build pressure rises from 1× to about 13.7× by level 100 and export
      that pressure in every balance row.
- [x] Add a provisional incoming damage race to the simulator and make the
      level-100 plain-weapon, default-crit, no-skill/no-talent profile a tested
      failure rather than relying only on a long outgoing TTK.
- [ ] Replace the provisional incoming budget with the shared armor, resistance,
      evasion, Magic Shield, recovery, enemy cadence, encounter timeout/leash,
      and control-uptime contracts as each becomes executable.
- [ ] Define viable committed-build multiplier and TTK bands at levels 20, 40,
      60, 80, and 100; verify the required build pressure increases without
      collapsing every build into the same numerical solution.
- [ ] Add three offensive reference profiles at levels 60, 80, and 100:
      neutral direct hit, committed critical frequency, and committed damaging-
      ailment throughput. Neutral must fail late game while both engines remain
      viable through distinct loops.
- [x] Add explicit encounter Health and skill Weapon Damage effectiveness
      overrides to the balance tool so million-Health bosses and high-
      coefficient endgame skills can be tested without changing live content.
- [ ] Add a standard level-100 boss suite beginning near 2,000,000 Health and
      covering uninterrupted damage, movement downtime, immunity windows, adds,
      ailment ramp, critical burst, party scaling, and higher-rarity/chart
      multipliers.
- [ ] Audit all eighteen intended class styles for reachable critical or
      damaging-ailment throughput through class kit, generic talents, gear,
      Fittings, or second class; do not turn this into a required Dexterity/
      Corroder pairing.
- [ ] Replace the prototype `100 + 2.5 per Strength` endgame resource curve with
      level-, attribute-, talent-, and gear-scaled budgets capable of exceeding
      10,000 Health for committed builds and 20,000 unarmored Magic Shield.
- [ ] Add five-digit resource formatting plus percentage recovery, barrier,
      leech, and enemy-damage tests; small level-1 flat values must not remain
      the dominant endgame sustain source.
- [ ] Add Health and Mana leech generation/reservoir/rate-cap simulation. Verify
      approximately 2–3% total Health Leech can saturate the default 10% cap at
      the 10k-Health/45,205-DPS reference, while incidental leech remains below
      cap and cap-increasing class builds can realistically feed their increase.
- [ ] Add distinct Health-healing and Magic-Shield-restoration payloads, tags,
      modifiers, combat events, tooltips, and tests. Verify a Health-only skill
      cannot restore Shield and a Shield-only skill cannot restore Health.
- [ ] Establish representative player/enemy power budgets and leveling cadence.
- [x] Add an offline damage-scaling sweep that uses runtime level/tier formulas
      and exports every creature level and provisional rarity to CSV.
- [ ] Extend the damage-scaling sweep with armor, resistance, ailments, resource
      uptime, boss phases, party scaling, and affixes as those runtime contracts
      become executable.
- [ ] Define the first-rift armor/readiness bridge without trivializing Vintage Story crafting.

### Rift Activity

- [ ] Decide Rift Chart acquisition, upgrading, modifiers, consumption, and failure downgrade/refund.
- [ ] Complete Manifold entry/exit handoff and safe party transfer.
- [ ] Assemble curated rooms semi-procedurally with readable enemy staging.
- [ ] Implement one elimination objective ending in a boss.
- [ ] Implement completion, extraction, failure, retry, and cleanup rules.
- [ ] Add party-size/highest-level/chart-level difficulty and reward scaling.

### Loot, Salvage, and Crafting

- [ ] Freeze the distinct purposes of Fittings, Support Fittings, Etchings, Assemblies, Augments, and Tender.
- [ ] Assign rare Flat Critical Chance to gear drops and bounded gear-upgrade
      outcomes; keep ordinary class and talent sources on Additional Crit.
- [ ] Implement RPG gear bases, level requirements, rarities, affixes, drops, and bounded modification.
- [ ] Build specialized VRPG storage, fast comparison, loot filtering, and bulk salvage before enabling high drop volume.
- [ ] Produce enough drops in the test rift to create one clear keep/salvage/refine decision.
- [ ] Balance rusty/temporal gear income and sinks against vanilla uses.

### Multiplayer and Failure

- [ ] Implement contribution-aware shared XP for damage, support, healing, control, and tanking.
- [ ] Define primarily shared-loot claim/protection rules.
- [ ] Add bounded death and rift-failure consequences without item drops or repeated-death spirals.
- [ ] Verify every core activity remains soloable and support/control/tank builds remain functional solo at slower clear speeds.

## Open Decisions That Block Specific Work

1. **Resolved topology:** six mutually exclusive global starting routes form a wide hexagon near the center and do not link directly. They cover every ordered dominant-secondary STR/DEX/INT pairing; final mechanical identities remain the next decision.
2. **Resolved for Gate A:** Evasive Step is an always-active DEX-primary lethal-hit passive with no input or hotbar slot.
3. **Resolved for Gate A:** cooldown-only `resource: none` is the temporary replacement for Energy on Fracture Pulse.
4. Which two classes and how many skills per class constitute enough Gate A content to test a real mixed build?
5. **Resolved for Gate A:** hotbar layout is a local client preference; skill assignments and cooldowns remain server-authoritative.

## Recommended Working Order

1. Remove Energy and clean the affected resource/UI contract.
2. Establish the resolved player-stat pipeline so future UI reports real behavior.
3. Add data-backed class/skill presentation fields and icon contracts.
4. Implement the real hotbar state/HUD, cooldown synchronization, bindings, and layout editing.
5. Rebuild Skills around icons, learned/rank state, resolved values, and real slot assignment.
6. Add explicit six-route starters, first themed sections, UI allocation/refund, and modifier execution.
7. Complete Hub redundancy cleanup after the new Skills/Talents/hotbar surfaces settle.
8. Fix RPG enemy eligibility and add the one-command playground setup.
9. Run Gate A persistence, dedicated-server, multiplayer, performance, and usability checks.
10. Freeze Gate B content scope using evidence from the build playground.
