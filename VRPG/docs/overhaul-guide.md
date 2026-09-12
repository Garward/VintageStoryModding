# VRPG: the overhaul guide

Choose a goal, put your build to work, and venture somewhere dangerous. Drop an anvil into a crowd, turn a well-timed hit into a burst of damage, or push past the safe exit for one more reward. Come home with something that opens up your next adventure.

VRPG is becoming a **total overhaul of Vintage Story built around combat**, with a persistent world, flexible character builds, and expeditions shaped by your decisions.

**Development snapshot: 12 September 2026.** The combat foundations already exist.
The wider overhaul is still taking shape. The immediate prerequisite for the first
usable Adventure profile is the implemented paired handoff from the ordinary
Inventory and Character actions to server-backed Inventory/Equipment. Its
joined-client and player acceptance checks remain open. After that live check, the
current milestone is a ten-minute combat sandbox with one arena and three readable
enemy roles. The complete six-page menu remains later work. This guide follows the
current development files and recorded checks. Installed and published builds may
differ.

## The adventure taking shape

These are the overhaul's intended features. The status guide below separates existing foundations from future gameplay.

- **Make your hits count.** Aim thrown scrap, call down heavy impacts, and build toward satisfying status combos.
- **Build a fighting style of your own.** Mix active skills, attributes, talents, and equipment around the way you like to fight.
- **Pick your adventure.** Pursue a boss, a reward family, a rescue, or a regional objective instead of following one fixed campaign.
- **Change the run as you go.** Take a bargain, open a different route, bank your rewards, or risk going deeper.
- **Explore regions with character.** Fire, Cold, Lightning, Rust, and Physical threats are intended to change tactics as well as scenery.
- **Spend more time adventuring.** A full starting inventory, clear comparisons, and simpler loot handling aim to cut down sorting and hauling.
- **Bring lasting progress home.** Build a personal Home and unlock services, routes, and changes in the wider world.
- **Adventure alone or together.** Build for solo play or combine your strengths with friends.
- **Begin a new life with a legacy.** Planned Rebirth lets you try a fresh build while keeping your world and a small permanent reward.

## Reading implementation status

- **Core implemented:** the main game logic exists; the complete overhaul version still has work remaining.
- **Partial:** meaningful code or tested components exist, but the full player journey is incomplete.
- **Prototype:** an early version exists to try out the idea.
- **Planned:** described in the design; no complete implementation was identified in this review.

Each entry describes the intended player experience, then names **Built so far** and **Still to come**. A system can have working pieces while its full adventure is still unfinished. Automated checks and menu previews help, but playing through the feature is a separate check. The [playable vertical-slice checklist](design/testable-version-todo.md) sets implementation order; the [player-menu checklist](design/usable-player-menu-checklist.md) defines the current first-pass Inventory/Equipment acceptance boundary.

## Fighting and building your character

### 1. Active skills — Core implemented

**For players:** Throw junk with a visible arc, bombard an area, or bring down Skyfall Anvil. Choose a small set of abilities that feels good to use together.

**Built so far:** The server checks skill use, with support for melee attacks, projectiles, arcing throws, falling attacks, sustained abilities, charges, cooldowns, resource costs, and up to eight saved skill slots. Eleven skill definition files are present in this snapshot.

**Still to come:** Broader skill acquisition through normal adventures, complete class kits, transformations, and balancing within the full gameplay loop. Earning those skills through adventures still needs work.

[Design](design/complete-game-design.md#41-combat-and-skills) · [Casting implementation](../src/Modules/Rpg/Skills/SkillCastingService.cs) · [Skill content](../assets/vrpg/vrpg/skills)

### 2. Status effects and combat combos — Partial

**For players:** Set up a target, create an opening, and make the next hit hurt. Hammer pressure and a follow-up that consumes Stagger provide the current example.

**Built so far:** Status tracking, buildup and consumption, timed effects, ground areas, and combat feedback. Physical, Fire, Cold, Lightning, and Rust provide the damage vocabulary.

**Still to come:** More ways to spread effects and trigger a payoff, grouping tools, and consistent combo presentation across complete enemy and class kits. Contagious Poison and chained status effects remain ideas for future play.

[Design](design/complete-game-design.md#95-status-effects) · [Propagation plans](design/complete-game-design.md#96-propagation-grouping-and-delayed-payoff) · [Status implementation](../src/Modules/Rpg/Skills/SkillStatusEffectService.cs)

### 3. Movement and defenses — Partial

**For players:** Survive through positioning, armor, shields, and well-timed escape opportunities.

**Built so far:** Movement effects, equipment defense calculations, the order in which damage applies, and automatic Evasive Step for a character whose primary attribute is Dexterity. Evasive Step uses no skill slot or hotkey.

**Still to come:** In-game movement and defense checks, final balance, and a reliable connection to your starter choice. Evasive Step still needs to pass those live checks.

[Movement design](design/combat-spaces-movement-and-ai.md) · [Defense specification](superpowers/specs/2026-09-05-vrpg-gear-foundation-design.md) · [Defense code](../src/Modules/Rpg/Combat/PlayerDefenseService.cs) · [Evasive Step code](../src/Modules/Rpg/Combat/EvasiveStepService.cs)

### 4. Levels, attributes, and resources — Core implemented

**For players:** Shape your character with Strength, Dexterity, and Intelligence, and learn the rhythm of Health, Mana, Blood, and Magic Shield.

**Built so far:** Saved character progression, experience, stat points, resource calculations, and creature level, rarity, health, damage, and XP scaling.

**Still to come:** Final progression pacing, complete class resource identities, and balance across actual regions and expeditions. Existing scaling is a foundation for that work.

[Design](design/complete-game-design.md#43-progression-and-build-systems) · [Player state](../src/Modules/Rpg/Players/RpgPlayerState.cs) · [Scaling implementation](../src/Modules/Rpg/Scaling)

### 5. Class identities and starter kits — Partial

**For players:** Start with a clear fighting style, then branch into a build of your own.

**Built so far:** Seven class identities: Smith, Trapper, Pilferer, Warden, Corroder, Handler, and Rust Scrapper. Five six-piece starter gear sets are defined for Smith, Pilferer, Warden, Corroder, and Rust Scrapper.

**Still to come:** Seven fully developed class engines, complete starter coverage, and reliable starting rewards. Trapper and Handler still need equivalent starter gear sets.

[Class design](design/complete-game-design.md#10-classes-and-builds) · [Class definitions](../assets/vrpg/vrpg/classes) · [Starter gear factory](../src/Inventory/Gear/StarterGearSetFactory.cs)

### 6. Talents and build experimentation — Partial

**For players:** Plan a route through your talents, review the changes, and apply the build when you are happy with it.

**Built so far:** Saved talent choices, checks that your chosen path stays connected, planned purchases and refunds, point checks, and an admin tree editor. The current 426-node template supports authoring and layout testing.

**Still to come:** A finished tree with meaningful specializations and abilities that change how you play. The template still needs its actual talent effects. Multiple saved builds, trial skills, and the planned training space also remain future work.

[Design](design/complete-game-design.md#103-talent-design) · [Allocation implementation](../src/Modules/Rpg/Talents/TalentAllocationService.cs) · [Tree editor](../src/Client/GuiDialogVRPGTalentEditor.cs)

### 7. Impact, enemy defeat, and aftermath — Partial

**For players:** Feel the weight of an impact and clearly see when a defeated enemy leaves something dangerous behind.

**Built so far:** Skill impacts, projectiles, particles, ground telegraphs, combat text, and limits that keep effects from overwhelming the screen.

**Still to come:** The full range of enemy death reactions: directional collapses or ragdolls, frozen remains, elemental death variants, and limited, predictable effects left after death. Those death reactions still need their own implementation.

[Design](design/complete-game-design.md#97-impact-death-and-aftermath) · [Visual implementation](../src/Client/Visuals) · [Ground-area implementation](../src/Modules/Rpg/Combat/GroundAreaService.cs)

## Items, menus, and your first session

### 8. Gear and equipment — Partial

**For players:** Find the piece that makes your build click, then check what it changes before trying it on.

**Built so far:** Saved gear details, checked item definitions, repeatable starter creation, level scaling, equipment effects, and comparison tools. The recorded checks on a separate test server loaded 30 starter bases and inspected the five starter sets.

**Still to come:** In-game appearance and combat checks, a complete way to manage equipment, and random gear earned from real loot drops.

[Gear specification](superpowers/specs/2026-09-05-vrpg-gear-foundation-design.md) · [Implementation](../src/Inventory/Gear) · [Recorded evidence](design/testable-version-todo.md)

### 9. Inventory, saving, and recovery — Partial

**For players:** Start with your full carrying capacity, browse clear item categories, and keep your belongings safe across sessions.

**Built so far:** The server can track items, large material quantities, and unique equipment, save them, move older items into the new storage, and handle recovery. Inventory, Recovery, and Equipment previews can display real stored items in a separate test setup.

**Still to come:** Reliable pickup, use, stack splitting, dropping, death handling,
visible held equipment, and broader reconnect/restart journeys. These are later
item-flow breadth, not prerequisites for the narrow Inventory/Equipment semantic
handoff. That first pass is limited to browsing, inspecting, Equip, and Unequip;
its joined-client and player acceptance checks are still open.

[Inventory design](design/inventory-loot-and-storage.md) · [Current acceptance checklist](design/usable-player-menu-checklist.md) · [Store code](../src/Inventory/InMemoryPlayerItemStore.cs) · [Persistence code](../src/Inventory/Persistence)

### 10. Loot pickup, piles, and sharing — Partial

**For players:** Gather rewards with less clutter and choose whether deliberately dropped items are available to everyone or your party.

**Built so far:** Early pickup tracking and safeguards that help prevent items from being lost or handled twice.

**Still to come:** Reliable item transfers, nearby compatible loot piles, pickup feedback, and the Everyone / Party only setting. Party access depends on the planned party service. Both sharing options still need to be connected to gameplay.

[Loot design](design/inventory-loot-and-storage.md#7-field-loot-flow) · [Current scope](design/usable-player-menu-checklist.md#current-progress) · [Ingress code](../src/Inventory/EngineItemIngressService.cs) · [Party proposal](superpowers/specs/2026-09-09-basic-party-permissions-design.md)

### 11. The unified player menu — Partial

**For players:** Manage Inventory, Stats, Skills, Talents, Library, and Options in one consistent place.

**Built so far:** The existing Hub and a production Inventory/Equipment menu route,
with item browsing, Recovery, comparisons, and expandable Equipment details. The
ordinary Inventory and Character semantic owners now hand off together to that
route in Adventure; source/test, Release-build, deployment, and isolated-server
startup evidence exist, while live client acceptance remains open.

**Still to come:** Live joined-client acceptance of the implemented first pass,
all six pages together, and checks at different menu sizes and controls. Stats,
Skills, Talents, Library, Options, appearance, and other player-page integration
are later work; they do not block the first Inventory/Equipment handoff.

[Unified-menu design](superpowers/specs/2026-09-06-unified-player-menu-design.md) · [Acceptance checklist](design/usable-player-menu-checklist.md) · [Menu implementation](../src/Client/PlayerMenu)

### 12. Combat HUD and interactions — Partial

**For players:** Keep your eyes on the fight while reading your resources, abilities, target health, and nearby interactions.

**Built so far:** A unified combat console, resource and XP display, skill slots, entity health bars, interaction prompts, and remappable controls.

**Still to come:** A consistent finished display, reliable links to your stored equipment, and clear boss, objective, and combo information. Controls, accessibility, and display sizes still need live checks.

[HUD design](design/combat-hud-total-replacement.md) · [Console implementation](../src/Client/HudElementVRPGCombatConsole.cs) · [Interaction implementation](../src/Interactions/CombatInteractionService.cs)

### 13. Character creation and a safe welcome — Partial

**For players:** Choose your appearance and starter kit, get a short introduction, and enter the adventure from a safe starting point.

**Built so far:** Saved introduction progress, Vanilla appearance editing with its class step removed, starter previews, and a tavern generator with layout checks.

**Still to come:** An appearance editor matching VRPG, reliable starting rewards, Welcome and Getting Started pages, and a safe arrival you can play through. The tavern still needs to be connected to actual arrivals. Early testing will use a protected development area.

[Onboarding design](design/character-creation-and-onboarding.md) · [Tavern implementation notes](design/safe-arrival-tavern-implementation.md) · [Flow code](../src/Modules/Rpg/Onboarding/FirstRunOnboardingFlow.cs) · [Generator](../src/Worlds/Generation/Onboarding/TavernGenerator.cs)

### 14. Library and discovery — Partial

**For players:** Look up a skill or mechanic when you want the details, and eventually keep the discoveries you make on your travels.

**Built so far:** A searchable Library built from written entries and game definitions for skills, stats, talents, gear, statuses, and dungeons.

**Still to come:** A complete Getting Started guide, earned regional and enemy knowledge, and its connection to expedition planning and persistent discovery.

[Information design](design/complete-game-design.md#176-information-layers) · [Knowledge plans](design/complete-game-design.md#75-elemental-knowledge) · [Library implementation](../src/Data/Library)

## The world and its adventures

### 15. Dungeon generation and rifts — Prototype

**For players:** Step through a rift into a generated dungeon with a recognizable theme and room layout.

**Built so far:** Room layouts and connection rules, generation and layout checks, encounter and wave definitions, Granite Halls content, Rift Charts, and an early connection through Manifold.

**Still to come:** A complete expedition loop and VRPG’s own system for managing adventure spaces, which is planned to replace the Manifold connection.

[Dungeon baseline](design/complete-game-design.md#45-dungeon-foundation) · [Dungeon implementation](../src/Modules/Dungeons) · [Realm design](design/realm-runtime-architecture.md)

### 16. Expedition objectives, branches, and temporary powers — Planned

**For players:** Choose why you are going, then make decisions that change the run: rescue a specialist, raid a workshop, take a dangerous bargain, or open an alternate route.

**Built so far:** The dungeon layout foundation that future expeditions can use.

**Still to come:** Dungeons shaped by your objective, remembered choices, meaningful branches, temporary powers, run resources, reward banking, and the planning screen.

[Expedition design](design/complete-game-design.md#8-expedition-design) · [Planner contract](design/complete-game-design.md#172-expedition-planner-contract)

### 17. Custom world generation and elemental regions — Planned

**For players:** Explore a world built for readable fights, reliable paths, and several worthwhile directions to pursue.

**Built so far:** Plans for world generation and safe routes, plus the separate dungeon and tavern generators.

**Still to come:** The main world generator, connected regions, gentle slopes, reliable paths, resources found through adventuring, elemental regions, and their rewards.

[World-generation design](design/world-generation-and-world-mutation.md) · [Regions](design/complete-game-design.md#6-world-structure) · [Elemental rewards](design/complete-game-design.md#7-elemental-regions-and-bounded-rewards)

### 18. Enemy tactics, encounters, and bosses — Partial

**For players:** Read an enemy's role, break up a dangerous formation, and handle bosses through clear counterplay.

**Built so far:** Creature scaling, dungeon encounter definitions, wave/spawn constraints, and layout validation.

**Still to come:** Enemies that understand their roles and work together, reliable movement around obstacles, world spawning, and designed boss fights.

[Movement and AI design](design/combat-spaces-movement-and-ai.md) · [Combat design](design/complete-game-design.md#9-combat-design) · [Creature scaling](../src/Modules/Rpg/Scaling/CreatureProgressionService.cs) · [Encounter definitions](../assets/vrpg/vrpg/encounters)

### 19. Realms, travel, and safe return — Prototype

**For players:** Travel between adventure spaces and Home with a reliable way back.

**Built so far:** An early dungeon connection through Manifold and preparation rules for starting areas.

**Still to come:** VRPG's own system for creating, saving, visiting, leaving, and safely recovering these spaces. Several future features depend on it.

[Realm architecture](design/realm-runtime-architecture.md) · [Current adapter](../src/Modules/Dungeons/ManifoldOptionalAdapter.cs) · [Onboarding realm code](../src/Realms/Onboarding)

### 20. Protected terrain and temporary combat construction — Planned

**For players:** Fight in dependable spaces, with future skills that briefly create cover, walls, bridges, or traps.

**Built so far:** Some placement restrictions and safeguards around changes to the world and items.

**Still to come:** Complete building rules for adventure areas, temporary skill creations, placement checks, enemy responses, and reliable removal when an effect ends.

[World-mutation design](design/world-generation-and-world-mutation.md) · [Current placement patch](../src/Client/Patches/CombatPlacementPatch.cs)

### 21. A world that remembers — Planned

**For players:** Rescue a specialist, reclaim a site, reveal a shortcut, or change a faction's fortunes—and see that achievement stay in the world.

**Built so far:** The adopted design for persistent outcomes and nonlinear access.

**Still to come:** Remembered world changes, faction relationships, settlements, captured sites, unlocked routes, boss paths, and outcomes connected to expedition results.

[Persistent-world design](design/complete-game-design.md#14-persistent-world-and-player-housing) · [World structure](design/complete-game-design.md#6-world-structure) · [Ownership architecture](design/architecture-ownership-map.md)

### 22. Home, building, and shared storage — Planned

**For players:** Make a place of your own for building, collecting, storage, and useful services, without recurring rent or offline defense chores.

**Built so far:** Detailed housing plans and item storage code that future Home storage can build on.

**Still to come:** Home creation and travel, building permissions, persistent plots, storage interfaces, material processing, and checked decorative or functional addon content. Free building is intended for Home; ordinary adventure terrain stays protected.

[Housing design](design/player-housing-and-building.md) · [Storage design](design/inventory-loot-and-storage.md)

### 23. Crafting, salvage, traders, and targeted rewards — Planned

**For players:** Turn unwanted finds into progress, improve an item you care about, and choose adventures that favor the rewards you want.

**Built so far:** Gear, rarity, and affix definitions provide part of the data foundation.

**Still to come:** Live targeted loot pools, controlled rerolls and item transformations, salvage, Home processing, NPC services, and configurable player trading. The planned economy should support solo progress without requiring a global market.

[Loot and crafting design](design/complete-game-design.md#11-loot-and-itemization) · [Home processing and trade](design/player-housing-and-building.md) · [Trading principles](design/complete-game-design.md#trading)

## Cooperation and long-term play

### 24. Parties and sharing items — Planned

**For players:** Bring a few friends and pass around the spoils. Choose who can pick up the items you drop.

**Built so far:** An approved direction for a basic party system focused on sharing permissions; its detailed proposal is still marked for review.

**Still to come:** A saved party system, invitations, membership controls, a menu panel, and working pickup permissions. Shared loot access is intended to end immediately on leave or removal. Exact limits and rules for reclaiming your own drops are still proposals.

[Party proposal and approval boundary](superpowers/specs/2026-09-09-basic-party-permissions-design.md) · [Delivery checklist](design/usable-player-menu-checklist.md#current-progress)

### 25. Cooperative encounters and recovery — Planned

**For players:** Combine abilities with friends while keeping solo play viable. Help an ally recover without needing a fixed tank/healer/damage lineup.

**Built so far:** The server already keeps track of combat and characters, providing a starting point for cooperative features.

**Still to come:** Encounters adjusted for the group, route decisions, individual rewards tied to your goals, rules for being downed and revived, and cooperative status interactions. These are outside the first version of parties, which focuses on sharing items.

[Cooperative design](design/complete-game-design.md#16-multiplayer) · [Recovery design](design/complete-game-design.md#133-multiplayer-recovery)

### 26. Death, extraction, and risk — Planned

**For players:** Decide when to bank a good run and when to push your luck. Failure should have a clear consequence and leave a reason to try again.

**Built so far:** Item safeguards handle some of the risks when Vanilla processes a death.

**Still to come:** The actual overhaul death rules, safe return, banked versus unbanked rewards, extraction opportunities, and optional harsher challenge modes. The full recovery experience still needs to be built.

[Failure and extraction design](design/complete-game-design.md#13-failure-death-and-extraction) · [Current death guard](../src/Patches/NativeInventoryMutationGuardPatch.Death.cs)

### 27. Rebirth and Legacy — Planned

**For players:** Finish a life, choose a small lasting legacy, and begin at level one with a fresh appearance and fighting style in the world you helped shape.

**Built so far:** The design states what should reset, what should remain, and how repeat lives avoid mandatory tutorial repetition.

**Still to come:** Qualifying challenges, safe handling of everything kept or reset, limited Legacy bonuses, heirloom preparation, a quicker start for later lives, and a clear confirmation screen.

[Rebirth and Legacy design](design/complete-game-design.md#12-rebirth-and-legacy)

## Making the overhaul work as a whole

### 28. Replacing Survival chores — Partial

**For players:** Spend your session chasing adventure. The intended default removes mandatory hunger, temperature, spoilage, and replacement chores that do not serve the RPG.

**Built so far:** Work taking over character rules, recorded checks for hunger, nutrition, and temperature changes, and removal of Vanilla class effects. Movement changes still need live checks.

**Still to come:** The full Survival audit and safe replacement of every affected loop, including spoilage, seasonal pressure, durability, tutorials, crafting, and world behavior. Several planned removals still need work.

[Survival ownership audit](design/survival-system-ownership-audit.md) · [Runtime patch](../src/Patches/SurvivalCharacterRuntimePatch.cs) · [Class patch](../src/Patches/VanillaClassRuntimePatch.cs)

### 29. Creature packs and addons — Partial

**For players:** The long-term aim is more creatures and adventures that follow VRPG's rules, plus selected building content for Home.

**Built so far:** Content loaded from editable definitions, checks for mistakes, and reviews of several example creature packs.

**Still to come:** Turning compatible creature models, animations, and sounds into enemies using VRPG rules; stable tools for addon creators; and checks for Home content. Compatibility will be limited to supported content.

[Addon contract](design/mod-compatibility-and-addon-contract.md) · [Creature ingestion design](design/creature-ingestion-and-adaptation.md) · [Reference audit](design/reference-creature-pack-audit.md) · [Current registry](../src/Data/VRPGDataRegistry.cs)

### 30. Server settings and safe upgrades — Partial

**For players and server owners:** Tune supported features while keeping consistent rules for your character, items, and world.

**Built so far:** RPG and dungeon module settings, HUD options, character settings, controls for enabling the new inventory, saving and recovery code, and substantial automated coverage recorded in the development checklists.

**Still to come:** The complete named overhaul preset, all planned feature switches, checks that settings work together, safe upgrades of saved data, and full in-game checks through joining, playing, reconnecting, and restarting. The example settings in the master design remain a future proposal.

[Configuration design](design/complete-game-design.md#152-configuration-contract) · [Actual settings](../src/Config/VRPGConfig.cs) · [Test strategy](design/implementation-readiness-and-test-strategy.md)

## What comes next

First, establish a repeatable login-capable client and dedicated-server smoke loop.
Use it to verify the completed paired Inventory/Character semantic-owner handoff
to Inventory/Equipment and obtain player confirmation that neither route opens the
Vanilla UI. Then build a deterministic arena with three simple enemy roles and
connect the existing skills, starter gear, XP, death, respawn, HUD feedback, and
server-backed inventory into ten useful minutes of play.

The canonical item store remains. Unwanted Vanilla item producers should be
disabled at their owning systems, while real VRPG rewards and retained transfers
keep explicit authoritative transactions. The narrow dialog handoff is complete in
source but awaits live acceptance; the full six-page integration, loot piles,
party breadth, owned realms, custom world generation, housing, and Rebirth wait
until a playable gate demonstrates their need.

For the only current execution sequence, use the [playable vertical-slice checklist](design/testable-version-todo.md). For the complete destination, use the [master design](design/complete-game-design.md). For individual technical contracts, use the [design index](design/README.md).

## Keeping this guide accurate

This guide records a documentation review on the date above. It was checked against the design documents, current source/assets, and existing acceptance records. No new in-game verification was performed for this guide.

When a subsystem changes, update its Built so far and Still to come descriptions and link the new evidence. Change a status only when the described experience works. Keep proposed details labeled as proposals, and retain a clear distinction between finished code and verified play.
