# VRPG Design Index

This directory is the authoritative map for VRPG's total-conversion design. The master document defines the whole game; subsystem specifications turn its decisions into contracts that can later be implemented and tested.

## North star

VRPG is a combat-first action RPG in a persistent voxel world. It should continuously present worthwhile opportunities without manufacturing obligations: the player chooses a goal, adapts during an expedition, brings lasting progress home, and can immediately see several interesting things they could do next.

Every inherited Survival system must answer one question:

> What does this add to playing VRPG?

If the answer is unclear or merely “more upkeep,” the system is disabled by default as safely as possible and exposed through an optional server toggle when a reliable compatibility boundary exists.

## Full player-menu end goal

The [unified Inventory, Character, and Hub design](../superpowers/specs/2026-09-06-unified-player-menu-design.md)
consolidates the complete six-page target, five inventory categories, high quantities,
equipment comparison, recovery, character-creation handoff and service integrations.
Its [delivery plan](../superpowers/plans/2026-09-06-unified-player-menu.md) starts from
the current store/gear foundations. Both are proposed for review; the existing
usable-replacement checklist remains the first custody gate.

## Documents

| Document | Use it for |
|---|---|
| [Complete game design](complete-game-design.md) | Vision, pillars, complete loop, progression, regions, expeditions, loot, Rebirth, conversion policy, and overall acceptance criteria |
| [Architecture and ownership map](architecture-ownership-map.md) | Full ASCII target-state map showing engine, VRPG domains, authority, request flows, disabled Survival systems, and project boundaries |
| [Creature ingestion and adaptation](creature-ingestion-and-adaptation.md) | Automatic conversion of compatible creature-mod assets into VRPG-owned actors, plus profiles, safety boundaries, encounter admission, and tests |
| [Reference creature pack audit](reference-creature-pack-audit.md) | Static findings and fixture expectations for Eidolon Unchained, The Monsterpack, and Fantasy Creatures Reborn |
| [UI visual language and Hub contract](ui-visual-language-and-hub.md) | Oxidized-iron “rusty gears” direction, material hierarchy, interaction states, screen contracts, motion, accessibility, and workbench plan |
| [Combat HUD total replacement](combat-hud-total-replacement.md) | Unified health, resource, XP, quick-use, skill, target and boss presentation; Vanilla suppression ownership; input parity; staged implementation and test gates |
| [Native player-menu implementation](native-player-menu-implementation-plan.md) | Clean source boundaries, semantic-dialog ownership seam, inventory-first migration sequence, and native test ladder |
| [Usable player-menu replacement checklist](usable-player-menu-checklist.md) | Dependency-ordered critical path from the current scaffolding to a safe, playable Inventory/Character replacement |
| [Combat spaces, movement, and AI](combat-spaces-movement-and-ai.md) | Arena geometry, anti-cheese rules, burst movement, navigation, enemy tactics, squad behavior, extension interfaces, and performance budgets |
| [Inventory, loot, and storage](inventory-loot-and-storage.md) | Fixed player capacity, typed tabs, high quantities, pickup flow, base storage, performance, and the inventory screen contract |
| [Player housing and building](player-housing-and-building.md) | Housing realm, world-building restrictions, resource processing, NPC trade, permissions, configuration, persistence, and backend options |
| [World generation and world mutation](world-generation-and-world-mutation.md) | Custom main-world topology, immutable traversal, non-mining resources, temporary terrain, authorized world changes, and validation |
| [Implementation readiness and test strategy](implementation-readiness-and-test-strategy.md) | Coverage audit, critical missing specifications, architecture spine, slab strategy, test layers, and vertical implementation gates |
| [Survival-system ownership audit](survival-system-ownership-audit.md) | Concrete Keep/Reframe/Replace/Disable map; begins with character/environment stats and complete Vanilla class neutralization |
| [Character creation and onboarding](character-creation-and-onboarding.md) | Rethemed appearance flow, data-defined starter kits, first skill, protected Welcome guide, idempotency, and release gate |
| [Mod compatibility and addon contract](mod-compatibility-and-addon-contract.md) | Strict total-conversion compatibility tiers, Home content boundary, native addon API expectations, missing-content recovery, and non-goals |
| [Realm runtime architecture](realm-runtime-architecture.md) | Decision to own realms internally, selective Manifold derivation, logical instances, travel, persistence, recovery, and tests |
| [Testable-version TODO](testable-version-todo.md) | Current implementation path and vertical-slice work |

## Adopted decisions at a glance

- VRPG is a distinct playstyle, not an RPG layer placed on top of Survival's chores.
- Combat is the primary activity; other systems create combat choices or persistent consequences.
- Routine combat spaces use mostly level floors. Two-block-or-higher changes are exceptional and must be an intentional wall or a validated traversal feature.
- Player movement is responsive and uses forgiving bursts. Kiting is intended play, but should require reading threats and spending windows or resources.
- Enemy AI is compositional. Shared perception, steering, navigation, abilities, and recovery support replaceable tactical behaviors such as orbiting, charging, screening, and maintaining range.
- The player begins with a fixed full inventory. Backpacks are not required and there is no backpack equipment slot.
- The hotbar is a combat loadout, not the only storage the player can use directly.
- Inventory is presented through typed tabs. Commodity quantities can be aggregated into high-count indexed entries rather than simulated as thousands of slots.
- Major total-conversion replacements are configurable and default enabled in the VRPG preset. Disabling one restores a defined fallback and triggers dependency validation rather than leaving a hybrid system by accident.
- Vanilla class and trait mechanics are disabled under VRPG ownership. Appearance
  customization is preserved and rethemed; the former class choice becomes a
  VRPG starter-kit decision rather than a permanent Vanilla class.
- Broad Survival-mod compatibility is not a goal. Supported expansion comes from VRPG-native addons, compatible creature ingestion, and narrowly audited Home content.
- Creature packs are a deliberate compatibility exception: compatible presentation assets can be automatically converted into synthetic VRPG actors, while VRPG replaces their AI, stats, combat, spawning and rewards.
- Manifold is transitional, not an end-state requirement. VRPG owns a focused realm runtime derived where useful from Manifold's MIT-licensed implementation and tests.
- Storage and loot handling minimize sorting, hauling, deposit trips, and repeated menu work.
- Player building is confined to an authorized Home by default. Survival's building assets and interactions become a secondary creative and processing layer instead of mutable combat terrain.
- New VRPG worlds use custom generation designed for protected terrain. Required travel, combat, and resources never assume mining or permanent block placement.
- Walkable slopes use supported, material-matched half-step surfaces by default; arena cores remain broad level plateaus.
- Skills may create temporary walls and terrain as bounded combat verbs, with placement validation, AI responses, and exact cleanup.
- The game exposes immediate, chosen, and long-term opportunities without daily chores, expiration pressure, or red-dot overload.

## Quick lookup

| Question | Primary section |
|---|---|
| What owns what across the entire mod? | [Complete ownership picture](architecture-ownership-map.md#2-complete-ownership-picture) and [state ownership table](architecture-ownership-map.md#4-state-ownership-table) |
| How do the major transactions flow? | [Critical request flows](architecture-ownership-map.md#5-critical-request-flows) |
| What does the target source layout look like? | [Suggested target project boundaries](architecture-ownership-map.md#9-suggested-target-project-boundaries) |
| Can creature mods automatically become VRPG enemies? | [Creature ingestion pipeline](creature-ingestion-and-adaptation.md#4-ingestion-pipeline) and [automatic baseline](creature-ingestion-and-adaptation.md#5-automatic-baseline) |
| Which real creature packs are the first test fixtures? | [Reference creature pack audit](reference-creature-pack-audit.md#2-fixture-matrix) |
| What should the replacement UI look and feel like? | [Visual thesis](ui-visual-language-and-hub.md#2-visual-thesis) and [material layers](ui-visual-language-and-hub.md#3-material-layers) |
| What belongs on each Hub screen? | [Screen contracts](ui-visual-language-and-hub.md#10-screen-contracts) |
| How should the workbench theme be iterated? | [Workbench plan](ui-visual-language-and-hub.md#12-workbench-plan) |
| What makes an arena valid? | [Combat-space contract](combat-spaces-movement-and-ai.md#2-combat-space-contract) |
| How do we stop terrain cheese? | [Terrain and anti-cheese policy](combat-spaces-movement-and-ai.md#4-terrain-and-anti-cheese-policy) |
| What should movement feel like? | [Player movement contract](combat-spaces-movement-and-ai.md#5-player-movement-contract) |
| Where do custom mob tactics plug in? | [AI architecture](combat-spaces-movement-and-ai.md#7-ai-architecture) and [extension contract](combat-spaces-movement-and-ai.md#10-extension-contract) |
| How do ranged and shield enemies cooperate? | [Squads and encounter coordination](combat-spaces-movement-and-ai.md#9-squads-and-encounter-coordination) |
| Does VRPG use backpacks? | [Player inventory contract](inventory-loot-and-storage.md#2-player-inventory-contract) |
| Can a server retain backpacks or another Survival system? | [Conversion configuration](complete-game-design.md#152-configuration-contract) and [inventory configuration](inventory-loot-and-storage.md#3-configuration-and-compatibility) |
| How do we decide whether a Survival system remains? | [Survival-system gate](complete-game-design.md#151-survival-system-gate) |
| How do very large stacks work? | [Storage model](inventory-loot-and-storage.md#5-storage-model) |
| What is borrowed from the warehouse design? | [VintageKinematics lessons](inventory-loot-and-storage.md#6-vintagekinematics-lessons) |
| How does loot avoid interrupting combat? | [Field loot flow](inventory-loot-and-storage.md#7-field-loot-flow) |
| How does the UI keep goals visible? | [Continuous opportunity](inventory-loot-and-storage.md#10-continuous-opportunity-without-obligation) |
| Where can players build? | [Adopted building boundary](player-housing-and-building.md#2-adopted-building-boundary) |
| Is Home literally a separate dimension? | [Housing-realm abstraction](player-housing-and-building.md#3-housing-realm-abstraction) |
| How do combat drops feed housing and NPC trade? | [Material transformation](player-housing-and-building.md#5-material-transformation) and [NPC trade role](player-housing-and-building.md#7-npc-trade-role) |
| Why does VRPG need custom worldgen? | [Main-world contract](world-generation-and-world-mutation.md#2-main-world-contract) and [world topology](world-generation-and-world-mutation.md#3-world-topology) |
| Can skills create walls or other terrain? | [Temporary combat terrain](world-generation-and-world-mutation.md#5-temporary-combat-terrain) |
| What may change protected terrain? | [Mutation classes](world-generation-and-world-mutation.md#7-mutation-classes) |
| How are smooth slab slopes generated and tested? | [Half-step slope surfacing](world-generation-and-world-mutation.md#31-half-step-slope-surfacing) and [implementation strategy](implementation-readiness-and-test-strategy.md#6-slab-slope-implementation-strategy) |
| What design and engineering contracts are still missing? | [Current coverage audit](implementation-readiness-and-test-strategy.md#3-current-coverage-audit) and [critical missing specifications](implementation-readiness-and-test-strategy.md#4-critical-missing-specifications) |
| What should we implement first? | [Vertical implementation gates](implementation-readiness-and-test-strategy.md#8-vertical-implementation-gates) |
| What remains before `E` and `C` can be safely replaced? | [Usable player-menu replacement checklist](usable-player-menu-checklist.md) |
| What happens to the Survival character stats shown in the reference? | [Character and environment panels](survival-system-ownership-audit.md#3-character-and-environment-panels) |
| How is the Vanilla class system disabled safely? | [Vanilla class and trait system](survival-system-ownership-audit.md#4-vanilla-class-and-trait-system) and [disable strategy](survival-system-ownership-audit.md#5-vanilla-class-disable-strategy) |
| Which other mods does VRPG support? | [Compatibility tiers](mod-compatibility-and-addon-contract.md#2-compatibility-tiers) and [explicit non-goals](mod-compatibility-and-addon-contract.md#11-explicit-non-goals) |
| How can external building content work at Home? | [Home-content boundary](mod-compatibility-and-addon-contract.md#5-home-content-boundary) |
| Does end-state VRPG require Manifold? | [Realm runtime decision](realm-runtime-architecture.md#1-decision) and [selective derivation plan](realm-runtime-architecture.md#5-selective-derivation-plan) |
| What does Manifold's license allow? | [Licensing and reuse](realm-runtime-architecture.md#2-licensing-and-reuse) |
| How do many homes and expeditions map to dimensions? | [Logical realms versus physical dimensions](realm-runtime-architecture.md#6-logical-realms-versus-physical-dimensions) |

## Document contract

New subsystem specifications should contain:

1. Purpose and player-facing promise.
2. Adopted decisions and explicit non-goals.
3. Runtime ownership and data extension points.
4. Performance and multiplayer constraints.
5. Validation and acceptance tests.
6. Open decisions, clearly separated from adopted decisions.

If a subsystem spec and the master design disagree, resolve the disagreement explicitly in both documents. Do not silently let the more recent file win.

## Planned specifications

- Expedition generation, room grammar, and director.
- Skills, statuses, propagation, and defeat responses.
- Itemization, crafting, salvage, and reward targeting.
- Factions and detailed elemental-region content.
- Character progression, classes, talents, and Rebirth.
- Complete the remaining Survival-system ownership and Harmony boundary groups.
- Runtime profile, persistence, and migration architecture.
- Implement the owned realm runtime and retire the transitional Manifold adapter after parity.
- Combat simulation and network-authority contract.
- Expedition lifecycle and director.
- Loot, crafting, salvage, NPC economy, and trade.
- Character progression and Rebirth transaction.
- Multiplayer scaling, ownership, reconnect, and abuse rules.
- First-hour onboarding, map, settings, and accessibility.
- Content-authoring tools, observability, and release gates.
