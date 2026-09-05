# VRPG Complete Game Design

Status: End-goal design direction  
Scope: Total-conversion playstyle, combat, expeditions, progression, loot, world structure, rebirth, interface, and extensibility  
Implementation baseline: VRPG 0.3.0 on Vintage Story 1.22.x

Design map: [VRPG design index](README.md)  
Ownership map: [VRPG high-level architecture and ownership](architecture-ownership-map.md)  
Focused specifications: [Combat spaces, movement, and AI](combat-spaces-movement-and-ai.md) · [Inventory, loot, and storage](inventory-loot-and-storage.md)
Creature expansion: [Creature ingestion and adaptation](creature-ingestion-and-adaptation.md)
Interface direction: [UI visual language and Hub contract](ui-visual-language-and-hub.md)
World specifications: [World generation and world mutation](world-generation-and-world-mutation.md) · [Player housing and building](player-housing-and-building.md)
Implementation map: [Implementation readiness and test strategy](implementation-readiness-and-test-strategy.md)
Conversion map: [Survival-system ownership audit](survival-system-ownership-audit.md)
Compatibility boundary: [Mod compatibility and addon contract](mod-compatibility-and-addon-contract.md)
Realm architecture: [Realm runtime architecture](realm-runtime-architecture.md)

## 1. Vision

VRPG is a combat-driven action RPG built inside Vintage Story's persistent voxel world. Players choose what to pursue, prepare a build for that purpose, enter dangerous expeditions, make consequential decisions during the run, and return with rewards that change both their character and the world.

The game combines:

- The build expression and item interactions of action RPGs.
- The immediate choices and run variation of roguelites.
- The mobility, readable powers, and loadout identity of Warframe-like combat.
- The discovery, bosses, construction, and nonlinear world progression of Terraria-like sandboxes.
- The exploration and encounter uncertainty of dungeon crawlers.
- A Dungeons & Dragons Online-inspired Rebirth system that turns a completed character into a new level-one life with a small permanent legacy.

VRPG must not become a linear campaign followed by indefinitely repeated corridors with larger numbers. Its defining promise is agency at every timescale.

### Player promise

> Choose your objective. Build for it. Change the expedition while you are inside it. Bring something lasting home.

### Genre description

VRPG is a persistent-world action RPG with roguelite expeditions. It is not a survival RPG addon. Vintage Story's Survival implementation remains an internal substrate for useful assets and mechanics, but VRPG supplies the player-facing rules and progression.

## 2. Design pillars

### 2.1 Combat is the primary activity

Most preparation exists to create new combat possibilities. Travel, gathering, crafting, settlement development, and exploration should lead toward a fight, alter how a fight can be approached, or create a meaningful strategic choice.

Combat must reward:

- Aiming and movement.
- Positioning and use of terrain.
- Target priority.
- Timing and resource management.
- Setup and payoff combinations.
- Knowing when to commit, retreat, or change the objective.

### 2.2 Agency exists during play, not only in the build screen

A deep passive tree cannot compensate for a game whose only repeated action is clearing every enemy between an entrance and a boss.

VRPG distributes decisions across four timescales:

| Timescale | Decisions |
|---|---|
| Seconds | Position, target priority, interrupt, burst, resource use, commit or disengage |
| Minutes | Route, optional objective, temporary power, hazard, escalating risk, extraction |
| Session | Expedition purpose, target rewards, loadout, faction, region, difficulty modifiers |
| Long term | Specialization, Rebirth legacy, alliances, settlement services, regional state, boss paths |

### 2.3 Randomness presents choices

Procedural generation creates situations. It must not make the important decisions for the player.

A useful random result asks, "Which option fits what I am trying to do?" A poor random result says, "The game did or did not give me permission to progress."

### 2.4 Repetition is directed by the player

Repeated content is acceptable when the player controls the purpose, risk, and variation. Players should be able to pursue a reward family, faction, elemental region, boss, room type, or world outcome instead of repeatedly running the single most efficient activity.

### 2.5 The world remembers

Vintage Story's persistent, editable world is a central advantage. Cleared strongholds, rescued specialists, constructed portals, captured sites, faction victories, and unlocked shortcuts should remain visible.

Persistent changes create opportunities. They must not create recurring upkeep chores.

### 2.6 Builds change verbs

The most valuable character choices change what the player does:

- A projectile returns, splits, sticks, or becomes remotely detonated.
- Blocking stores force for a counterattack.
- A dodge marks enemies passed through.
- A status can be consumed for a different payoff.
- Health, mana, blood, or shield becomes an offensive resource.
- A summon can be commanded, repositioned, or sacrificed.

Small numerical bonuses support these transformations but do not replace them.

### 2.7 Expansion is data-first

New regions, objectives, encounters, enemies, rooms, skills, statuses, affixes, rewards, and bosses should enter through validated definitions and stable tags whenever their behavior fits an existing rule.

Code remains appropriate for genuinely new verbs. Content authors should not need Harmony patches to add ordinary content.

## 3. Explicit non-goals

VRPG does not aim to provide:

- Mandatory hunger, food preservation, body-temperature management, or seasonal preparation.
- Long metallurgy chains as a prerequisite for participating in combat.
- Equipment durability that exists primarily to make players repeat replacement work.
- Mandatory settlement upkeep or attacks that punish players for logging out.
- A single fixed campaign that every life must replay in order.
- Infinite vertical scaling that makes most of the world irrelevant.
- Hundreds of passive-tree nodes whose purpose is pathing through small damage increases.
- Hard elemental immunities that invalidate a character at the entrance to a region.
- An auction-house economy as the assumed solution to progression.
- Procedural rooms presented as meaningful variety when their decisions are identical.

## 4. Existing foundation

The following systems or content already exist in VRPG and should be evolved instead of replaced without cause.

### 4.1 Combat and skills

The skill runtime already supports a useful range of physical interactions:

- Authored melee arcs, lines, and close single-target sequences.
- Self-centered areas and aimed raycast areas.
- Straight projectiles that collide with creatures or terrain.
- Ground-impact projectiles.
- Ballistic throws with an arc preview and release timing.
- Targeted falling objects.
- Channeled and repeated attacks.
- Charges, cooldowns, resource costs, and server-authoritative casting.
- Ground areas, impact effects, particles, camera response, and combat text.

`Junk Toss`, `Scrap Bomb`, and `Skyfall Anvil` are especially important references. Their physical travel, aim preview, model presence, release, and impact make them enjoyable before rewards are considered. This is the quality bar for future skills: the action itself must be satisfying.

`Hammer Blow` building Stagger and `Fracture` consuming it for Vulnerable demonstrate the desired setup/payoff structure.

### 4.2 Character vocabulary

Seven class identities currently provide thematic starting points:

- Smith: hammer pressure, Stagger, armor, and staying power.
- Trapper: ranged control, crossbows, traps, and Bleed.
- Pilferer: fast close combat, Poison, openers, and escape.
- Warden: tridents, wards, stability, and protection.
- Corroder: Rust, Fire, corrosion, and temporary summons.
- Handler: constructs, discharge effects, Cold, and command.
- Rust Scrapper: salvaged machinery, thrown junk, and unstable charges.

These are presently closer to authored identities than complete class implementations. The end goal preserves their themes while ensuring each class has a distinct combat engine.

### 4.3 Progression and build systems

VRPG already has:

- Character level and experience.
- Strength, Dexterity, and Intelligence foundations.
- Stat and talent points.
- A data-driven talent graph and in-game tree authoring tools.
- Up to eight persisted skill slots, with four shown by default.
- Skill ranks.
- Health, Mana, Blood, and Magic Shield.
- Health, resource, and evasive-step scaling.
- Creature levels, rarity, health, damage, and experience scaling.
- Gear rarity and affix definition scaffolds.

The 426-node talent template is valuable for testing layout, storage, and authoring volume. It is not automatically the intended player-facing tree. A shipped large tree must earn its size through transformative clusters and meaningful route choices.

### 4.4 Damage and status vocabulary

Current damage identities are Physical, Fire, Cold, Lightning, and Rust.

Current status vocabulary includes Bleed, Burn, Chill, Corrosion, Freeze, Guarded, Poison, Regeneration, Shatter, Shock, Stagger, Stun, and Vulnerable.

This is enough vocabulary for the first complete combat ecosystem. More damage types should be added only when they create a new combat pattern rather than another color of damage.

### 4.5 Dungeon foundation

VRPG already has tagged dungeon themes, rooms, connectors, zones, anchors, encounters, waves, spawn constraints, layout generation, validation, and a transitional optional Manifold integration. The Granite Halls provide a starter theme with start, combat, junction, and boss rooms. End-state VRPG owns its focused realm runtime instead of requiring Manifold.

The next step is to add decisions and persistent run state to this foundation rather than only increasing room count.

## 5. The complete player loop

### 5.1 Discover

Players discover opportunities through exploration, settlements, faction contacts, recovered intelligence, crafted expedition devices, bosses, and world events.

An opportunity communicates:

- What can be accomplished.
- The region and expected threats.
- Reward families likely to appear.
- Known special rules.
- What may change if it succeeds or fails.

Unknown details may remain discoverable, but information required for a fair choice must not be hidden.

### 5.2 Choose intent

The player chooses a primary expedition objective. Examples include:

- Hunt a named enemy.
- Recover a particular category of equipment.
- Rescue a specialist.
- Capture or destroy a stronghold.
- Steal research from a faction.
- Stabilize or exploit a rift.
- Find a room, shrine, catalyst, or lost technique.
- Change control of a region.
- Pursue a Rebirth trial.

Intent changes generation weights, encounters, rewards, and possible outcomes. It is not merely quest text attached to the same extermination run.

### 5.3 Prepare

Players select:

- A class or current life identity.
- Equipped active skills.
- Skill transformations and supports.
- Equipment.
- A small number of expedition tools or consumable choices.
- Optional difficulty and reward modifiers.

Preparation should take as long as the player wants, but routine entry should be quick. Saved loadouts and clear comparisons prevent planning depth from becoming clerical work.

### 5.4 Enter and adapt

During the expedition, the player encounters route choices, optional rooms, temporary transformations, incomplete information, and threats created by earlier decisions.

Players may adjust limited parts of their approach inside an expedition, but cannot freely rewrite the entire build during combat. Temporary powers should create adaptation without making preparation meaningless.

### 5.5 Press deeper or extract

Rewards can be banked at explicit safe points. Continuing deeper increases both reward quality and the chance of losing unbanked rewards.

Extraction is a strategic decision, not a punishment for needing to stop playing. Ordinary expeditions advertise their expected duration and provide predictable exit opportunities.

### 5.6 Return and change something

Every successful expedition should advance at least one chosen goal:

- Improve or transform a build.
- Unlock access.
- Change a region.
- Develop a settlement service.
- Reveal a boss path.
- Progress a faction relationship.
- Prepare a future Rebirth.

The player should rarely return with nothing but generic experience and vendor trash.

## 6. World structure

New VRPG worlds use a custom generator designed around protected terrain, nonlinear regions, validated routes, combat spaces, and non-mining sources of progression materials. See [World generation and world mutation](world-generation-and-world-mutation.md) for the generation pipeline, temporary terrain, mutation classes, configuration, and acceptance tests.

### 6.1 Regions and zones

The world is divided into overlapping region identities rather than a single linear level sequence. A region definition includes:

- Environment and visual language.
- Native factions.
- Elemental identity.
- Combat grammar.
- Encounter and objective pools.
- Native reward families.
- Bosses and world-state outcomes.
- Recommended threat range rather than an absolute level requirement.

Regions may contain open-world sites, authored landmarks, procedural interiors, rifts, roaming threats, settlements, and bosses.

### 6.2 Nonlinear access

Major progression gates should usually accept a subset of available accomplishments. For example, completing any two of four regional arcs may awaken the next world threat. Which arcs were completed changes that threat and the services available afterward.

A player should be able to reach the same broad progression stage through different combinations of regions, bosses, factions, and objectives.

### 6.3 Threat ranges

Level affects danger, but it should not produce absolute invalidation. Regions have:

- A baseline threat.
- Optional deeper pockets.
- Escalation states.
- High-risk objectives.
- Boss-specific challenge variants.

Old regions remain relevant through new objectives, altered faction states, targeted rewards, Rebirth lives, and optional difficulty modifiers.

## 7. Elemental regions and bounded rewards

Elemental regions are not just scenery or resistance checks. Each element changes enemy behavior, environmental pressure, status interactions, and available loot.

### 7.1 Combat identities

Initial identities are:

| Identity | Combat grammar | Typical rewards |
|---|---|---|
| Fire | Area denial, ignition, escalating pressure, volatile enemies | Burn tools, explosions, heat conversion, Fire protection |
| Cold | Slowing fields, formation control, brittle windows, Freeze and Shatter | Control, Shatter payoffs, chill movement tools, Cold protection |
| Lightning | Bursts, chains, machinery, target switching, unstable tempo | Discharge, chaining, speed, resource cycling, Lightning protection |
| Rust | Armor decay, attrition, constructs, corruption, stacking pressure | Corrosion, Poison, salvage, construct interactions, Rust protection |
| Physical | Posture, impact, displacement, Bleed, armor contests | Stagger, blocking, weapon techniques, Bleed, general defense |

### 7.2 Reward composition

Every region draws rewards from several layers:

- **Universal:** basic progression resources and broadly usable equipment.
- **Native:** skills, transformations, affixes, materials, and uniques tied to the region's identity.
- **Objective:** rewards selected by the expedition's purpose.
- **Faction or boss:** specific rewards tied to an opponent or outcome.
- **Bridge:** uncommon rewards that help adapt toward another region or strategy.

Exact weights are configurable by content pack. The default should strongly favor regional identity without making a bad random streak halt basic progression.

### 7.3 Specialization creates opportunity cost

A character specialized against Fire can clear Fire regions more safely or select more dangerous Fire modifiers. That character gains efficient access to Fire-region rewards, not every reward in the game.

To obtain Cold, Lightning, Rust, faction-specific, or boss-specific rewards, the player must do at least one of the following:

- Adapt part of the loadout.
- Accept a slower or riskier expedition.
- Cooperate with another player.
- Trade where the server permits it.
- Use a future Rebirth to pursue a different specialization.
- Find a bridge reward that opens another approach.

This creates genuine economic and build identity without hard-locking solo players.

### 7.4 No hard immunity walls

Specialization should create advantage, not permission to participate. Default rules use resistance, behavior, and status interaction rather than blanket immunity.

A pure Fire build entering a Fire region might face:

- Lower direct damage efficiency.
- Enemies empowered by careless burning.
- Opportunities to convert Burn into explosions or another damage type.
- Objectives that bypass direct combat strength.
- Temporary powers that create an alternate solution.

The player can choose to adapt or accept the disadvantage. The entrance never simply says the build is invalid.

### 7.5 Elemental knowledge

The first visit shows broad warnings. Exploration reveals exact enemy properties, boss mechanics, rare reward families, and environmental interactions. Once learned, this information remains available in the Library and expedition planner.

Knowledge reduces surprise chores on later runs while preserving discovery on the first.

## 8. Expedition design

### 8.1 Expedition contract

An expedition consists of:

- A chosen primary objective.
- A region and threat range.
- One or more known reward families.
- A generated network of rooms or sites.
- A run-state record of alarms, bargains, casualties, corruption, optional victories, and banked rewards.
- At least one predictable extraction opportunity.
- A conclusion that reflects earlier decisions.

### 8.2 Room roles

The room schema should expand beyond start, combat, junction, and boss to include:

- Choice rooms.
- Optional elite encounters.
- Recovery or banking rooms.
- Shrines and temporary-power offers.
- Faction events.
- Traversal challenges.
- Objective rooms.
- Information rooms.
- Puzzle or mechanism rooms.
- Shops and neutral contacts.
- Sacrifice, corruption, or bargain rooms.
- Secret and conditionally revealed rooms.
- Alternate exits.

Every role must change a decision. A room that contains only a differently shaped fight remains a combat room.

### 8.3 Consequential branches

Examples include:

- Destroy a workshop to weaken constructs, or raid it for construct loot and leave it operating.
- Rescue captives now and raise the alarm, or preserve stealth and locate their transfer route.
- Kill one lieutenant, causing another to inherit its power.
- Take a corrupted relic for immediate strength and add a boss phase.
- Open a shortcut by spending an expedition tool, bypassing a reward wing.
- Stabilize a rift for safe extraction, or overcharge it to reach a rare area.

A branch should change at least one later encounter, reward, route, or persistent outcome.

### 8.4 Temporary powers

Roguelite powers provide run-level adaptation. They should frequently modify verbs:

- Junk Toss ricochets and retrieves a charge on a direct hit.
- Scrap Bomb leaves a damaging debris field.
- Hammer Blow pulls small enemies into its arc.
- Thrust creates a ward along its path.
- Cinder Orb can be shot to detonate early.
- Frozen enemies become temporary cover when shattered.

Temporary powers may preview future permanent build options. They should not primarily be small additive damage bonuses.

### 8.5 Run resources

Expeditions may use a small number of comprehensible run resources, such as Resolve, Alarm, Corruption, or Supplies. Each must support a repeated decision.

Do not add parallel meters merely because roguelites have them. A resource with no interesting spend decision should be removed.

## 9. Combat design

### 9.1 The physicality benchmark

Future skills should be evaluated against the existing projectile and targeted-drop skills. A skill succeeds when aiming, timing, travel, collision, impact, sound, and enemy response make it satisfying even in a test arena without rewards.

### 9.2 Encounter decision axes

Substantial encounters should test at least two of:

- Target priority.
- Position and terrain.
- Objective pressure.
- Resource timing.
- Interrupt timing.
- Setup and payoff execution.
- Environmental interaction.
- Risk escalation.

Bosses should test several axes but introduce them progressively.

### 9.3 Enemy roles

Enemies are authored through readable roles:

- Frontline pressure.
- Shield or protection.
- Artillery.
- Summoning.
- Healing or repair.
- Control.
- Pursuit.
- Disruption.
- Objective interaction.
- Commander or aura support.

Interesting encounters come from role combinations and terrain. Ordinary enemies should remain individually understandable.

### 9.4 Counterplay

Every dangerous mechanic needs one or more readable responses:

- Move.
- Interrupt.
- Break line of sight.
- Destroy a source.
- Change target priority.
- Spend a defensive resource.
- Accept a temporary disadvantage to maintain the objective.

Pure unavoidable stat checks are reserved for explicit challenge modifiers and still require clear advance warning.

### 9.5 Status effects

Statuses must create visible decisions. Examples:

- Stagger invites continued pressure before its buildup expires.
- Vulnerable creates a burst window.
- Freeze offers control or a Shatter payoff.
- Corrosion asks whether to keep stacking or consume it.
- Bleed changes the value of enemy movement.
- Shock rewards timing accumulated status pressure.

If a status only produces background damage with no decision or readable consequence, it should be consolidated with another effect.

### 9.6 Propagation, grouping, and delayed payoff

Some statuses can propagate between nearby creatures. Propagation turns enemy movement and density into resources: the player infects or primes one target, shapes the group, then spreads or consumes the effect at the right moment.

This family follows the same `setup -> manipulate -> cash out` pattern as Stagger and prepared traps:

1. Apply a carrier status or build stacks.
2. Group, separate, move, or preserve targets.
3. Trigger a spread, conversion, or detonation.
4. Decide whether to continue propagation or consume the accumulated state.

Examples include:

- Poison jumps to nearby creatures when an infected target dies.
- Corrosion spreads when ten stacks are consumed.
- Burn ignites nearby oil or leaves a short-lived spreading ground patch.
- Shock jumps through grouped targets and consumes stored status pressure.
- Freeze shatters outward, applying Chill without copying the full Freeze.
- A beneficial ward spreads Guarded to nearby allies at reduced duration.

Propagation must not have only one correct enabler. Players can create useful groups through:

- Natural kiting and enemy pursuit.
- Pulls, knockback, fear, taunts, slows, and walls.
- Mines, lures, tripwires, and prepared fields.
- Chokepoints and player-built terrain.
- Summons or constructs that redirect enemies.
- Cooperative positioning.
- Killing or moving a deliberately infected carrier.

The carrier status remains useful against an isolated target, and the payoff retains a single-target result. Grouping improves efficiency or changes behavior; it is not permission to use the kit.

Propagation rules are bounded by radius, target count, generation count, per-target cooldown, and stack or duration falloff. A chain cannot bounce indefinitely between the same creatures. Bosses are not generically immune: nearby adds, boss-created objects, or narrower single-target payoffs let the mechanic remain relevant.

Readable presentation is mandatory:

- A contagious or primed target has a stable marker in addition to its normal status icon.
- The selected target shows relevant stack or threshold progress without requiring combat-text counting.
- A payoff skill visibly enters its ready state when a useful trigger is available.
- Eligible nearby recipients receive a brief, quiet preview while the spread skill is aimed.
- The actual transfer uses a short directional pulse, arc, trail, or ground wave so cause and destination are clear.
- Only locally relevant chains are emphasized; multiplayer and large packs must not fill the screen with connection lines.

### 9.7 Impact, death, and aftermath

Enemies should react to the action that defeated them. Impact and death presentation reinforces skill identity, communicates power, and can leave a final tactical consequence.

Possible defeat responses include:

- Heavy Physical impacts launch, tumble, pin, or collapse an enemy in the force direction.
- Staggered enemies fall differently from enemies killed in a clean strike.
- Frozen enemies remain briefly as brittle statues and can Shatter.
- Fire deaths collapse into embers or leave a short-lived burning patch.
- Lightning deaths arc through nearby conductors or make the remains temporarily conductive.
- Rust and Corrosion cause armor, constructs, or bodies to crumble into salvage and flakes.
- Poison or plague leaves an infected carrier, cloud, or crawling spread source.
- Mines and large explosions produce stronger directional ragdoll impulses than ordinary hits.
- Signature abilities can define unique defeat responses when they deliver the killing blow.

Cosmetic response and gameplay aftermath are separate concerns:

- **Defeat presentation** is client-side animation, ragdoll, particles, sound, decals, model swapping, and camera response. It must degrade gracefully when a creature cannot support a particular presentation.
- **Aftermath** is server-authoritative state such as a hazardous area, spreading status, salvage object, temporary obstruction, summon, or delayed explosion.

One primary defeat response is selected deterministically. The default priority is killing-skill override, explicitly consumed status, dominant qualifying damage identity, then creature fallback. A small bounded number of compatible secondary effects may accompany it. This prevents an enemy carrying several statuses from simultaneously burning, freezing, exploding, spreading plague, dropping a construct, and ragdolling through the room.

Ragdolls and remains use strict lifetime, distance, and count budgets. Important gameplay effects never depend on the client-side resting position of a ragdoll; the server creates a stable area or entity at an authoritative position. Reduced-motion, reduced-effects, and content-presentation settings provide alternate responses without changing gameplay.

Dangerous aftermaths must show ownership, affected area, duration, and activation timing clearly. A corpse that will spread plague or explode cannot look like harmless decoration.

### 9.8 Resources and cooldowns

The default four visible active slots remain a strong starting constraint. Extra slots are optional because more buttons do not necessarily create more decisions.

Class resources should promote a rhythm:

- Mana supports planned expenditure and recovery windows.
- Blood trades safety for output or utility.
- Magic Shield supports timing around damage-free intervals.
- Class-specific resources may be added when they create a distinctive loop.

Cooldowns prevent repetition only when alternative actions remain engaging. Long cooldowns cannot substitute for a complete basic combat kit.

## 10. Classes and builds

### 10.1 Class purpose

A class defines a starting combat engine, not an exclusive list of allowed equipment. It supplies:

- A resource relationship.
- A movement or defensive identity.
- Several signature skill families.
- Early access to a coherent setup/payoff loop.
- The first position in the talent network.

Later progression permits hybridization while preserving opportunity cost.

### 10.2 Three build layers

1. **Combat identity:** class engine, primary resources, and defensive rhythm.
2. **Skill loadout:** active actions available during combat.
3. **Transformations:** talents, supports, equipment, statuses, and uniques that change those actions.

A build should be describable by what it does, not merely by its primary damage number.

### 10.3 Talent design

Talent nodes have four acceptable purposes:

- Enable a new interaction.
- Transform an existing interaction.
- Establish a major specialization with an opportunity cost.
- Support a chosen interaction with necessary numerical strength.

Travel nodes and modest statistical nodes may exist, but they must not dominate allocation.

Large clusters should culminate in mutually exclusive or expensive keystones. The route to a keystone should offer useful alternatives rather than one mathematically mandatory path.

### 10.4 Experimentation

The game supports:

- Several saved loadouts.
- Low-friction respecialization in safe areas.
- A test arena or training construct.
- Actual post-modifier values in comparison screens.
- Temporary trial use of newly discovered skills or transformations.
- Clear confirmation before an irreversible Rebirth or world decision.

Experimentation cost should create commitment at meaningful moments, not punish curiosity.

## 11. Loot and itemization

### 11.1 Loot purpose

Loot provides:

- New combat verbs.
- Transformations of existing verbs.
- Regional adaptation.
- Targeted specialization.
- Preparation for future expeditions or Rebirths.
- Trade and cooperation opportunities.

Raw power remains important, but an item whose only identity is a larger number has a short useful life.

### 11.2 Item readability

An item comparison must immediately show every property that could change the equip decision:

- Required level or life condition.
- Base weapon or defense profile.
- Damage and elemental identity.
- Major affixes.
- Skill transformation.
- Relevant comparison against the equipped item.

Lore, source, exact formula breakdowns, and uncommon interactions remain one deliberate inspection step away.

### 11.3 Rarity

Rarity controls complexity and potential, not guaranteed superiority:

- Common items are legible and useful foundations.
- Rare items combine several build-relevant properties.
- Unique items alter rules or verbs.

A well-matched common or rare item can outperform an irrelevant unique.

### 11.4 Targeting rewards

Players influence loot through region, objective, faction, boss, difficulty modifiers, and expedition choices. Perfect deterministic acquisition is unnecessary, but the player must understand how to improve the odds of obtaining a desired category.

### 11.5 Crafting

Crafting modifies valuable finds rather than replacing combat with industrial chores. Useful crafting actions include:

- Reroll one bounded property.
- Add a regional influence.
- Transfer or preserve a transformation.
- Convert an item for a future Rebirth.
- Trade immediate power for a controlled drawback.
- Salvage unwanted items into targeted progression.

Routine repair and replacement should be automatic, inexpensive, or absent unless durability becomes a deliberate expedition resource.

## 12. Rebirth and Legacy

### 12.1 Purpose

Rebirth lets a completed character begin again at level one with a different appearance, class, and build while retaining a small permanent benefit. It creates long-term goals, makes low-level content useful, and encourages players to experience different combat identities.

Rebirth must not require replaying a fixed campaign or repeatedly performing tutorial chores.

### 12.2 Eligibility

A life becomes eligible after reaching the level cap and completing a capstone challenge. Alternative capstones may exist for combat, exploration, faction, or boss-focused lives, but every capstone must demonstrate mastery rather than demand a long generic grind.

### 12.3 What resets

By default, Rebirth resets character-bound power:

- Level and experience.
- Class or starting combat identity.
- Allocated base stats and talents.
- Skill ranks and level-gated equipment access.
- Current-life quest or challenge progress.

Appearance, including body presentation and gender, may be selected again without mechanical penalty.

### 12.4 What remains

By default, Rebirth retains:

- The persistent world and its changes.
- Shared stash and collected equipment.
- Library discoveries and known mechanics.
- Unlocked expedition categories and world routes.
- Cosmetics, titles, and achievements.
- Settlement development and faction history, except where a server chooses life-specific factions.
- Earned Legacy bonuses.

High-level equipment can remain in the stash but cannot be equipped until its requirement is met or altered.

### 12.5 Legacy rewards

Each completed life grants one small permanent Legacy choice associated with how that life was played. Examples include:

- A tiny capped base-stat increase.
- Slightly faster mastery of a previously completed class.
- One additional early skill choice.
- A small regional resistance.
- A minor increase to a narrowly defined reward family.
- A cosmetic aura, title, or class remembrance.

Power bonuses use category caps and diminishing returns. Infinite Rebirth may remain possible for identity, collection, and challenge, but infinite compulsory power growth must not.

The first several Rebirths should provide the most new possibilities. Later Rebirths increasingly emphasize alternate starting options, cosmetics, records, and self-selected challenges.

### 12.6 New-life acceleration

A reborn character does not repeat the original onboarding path. It receives:

- Skippable tutorials.
- Access to previously unlocked travel and settlements.
- A choice of suitable starting region or contract.
- Accelerated early mastery where appropriate.
- Optional heirloom preparation before Rebirth.
- Low-threat objectives generated from the current world state.

The new life is a new build in the existing world, not a reset of the entire game.

### 12.7 Rebirth decisions

Before confirmation, the Rebirth screen shows:

- Everything that resets.
- Everything retained.
- The selected Legacy reward.
- Equipment that will become unusable.
- The new life choices available immediately afterward.

Rebirth is server-authoritative and requires an explicit confirmation after the complete consequence summary is visible.

## 13. Failure, death, and extraction

### 13.1 Graduated failure

Failure should change the situation instead of only deleting time. Possible outcomes include:

- Keep banked rewards and lose unbanked rewards.
- Fail the primary objective but retain discovered intelligence.
- Retreat after defeating a lieutenant, changing the later boss.
- Allow a faction to reinforce the region.
- Sacrifice a reward to rescue or revive another player.
- Spend an expedition resource on emergency extraction.

### 13.2 Death

Ordinary death should return the player to a safe point with a clear, bounded consequence. Corpse retrieval is not a default requirement because it often converts combat failure into travel repetition.

Optional challenge modifiers may introduce harsher death rules in exchange for known rewards.

### 13.3 Multiplayer recovery

Downed and revival states must preserve agency for both the fallen player and allies. Reviving should involve positioning and risk, not a long uninterruptible chore. Solo play receives an equivalent bounded recovery option.

## 14. Persistent world and player housing

The main world persists, but ordinary players do not reshape its terrain. It changes through selected objectives, faction outcomes, repaired routes, captured sites, revealed entrances, and other authorized semantic world states.

Freeform building is reframed into a persistent player Home: a separate housing realm or equivalent isolated plot where Survival's construction capability supports expression, storage, material processing, NPC trade, and a later optional progression layer. Building is disabled by default in the main world and expeditions to preserve combat geometry, navigation, and authored routes.

Protected terrain also makes temporary construction a meaningful skill family. Authorized skills may create short-lived walls, cover, bridges, cages, traps, or elemental blocks that enemies can navigate around, attack, or otherwise counter. These constructs have strict placement, lifetime, navigation, and cleanup contracts; they never become free permanent building.

Home provides convenience, expression, and selected strategic options without continual feeding, repair, rent, crop tending, invasion, or offline defense. See [Player housing and building](player-housing-and-building.md).

## 15. Survival conversion policy

VRPG retains Vintage Story Survival as a technical substrate while replacing its player-facing game design.

### 15.1 Survival-system gate

Every inherited mechanic, content family, tutorial, interface, world process, and recurring timer must answer:

> What does this add to playing VRPG?

A system earns a default place only when it materially supports at least one of the following:

- Combat decisions or build expression.
- Player-chosen preparation with multiple valid approaches.
- Expedition choices, risk, reward, or recovery.
- Persistent-world expression, discovery, or meaningful consequence.
- Social play that remains useful without requiring a fixed group.
- Clear atmosphere that does not impose disproportionate upkeep.

For each Survival system, record one disposition:

| Disposition | Meaning |
|---|---|
| **Keep** | It already strengthens VRPG and creates little conflicting routine |
| **Reframe** | Its assets or fiction are useful, but its rewards, timing, or player purpose must change |
| **Replace** | VRPG needs the capability but Survival's implementation or assumptions conflict with the game |
| **Disable** | It adds no sufficient value, duplicates another system, or primarily creates chores |

“Vintage Story already has it,” realism by itself, implementation convenience, and fear of unused assets are not sufficient reasons to keep a mechanic enabled. Assets and code are evaluated separately: VRPG may retain food models, creatures, weather visuals, crafting stations, or temporal effects while disabling the Survival loops originally attached to them.

The system review must also identify dependencies, patches or assets that activate it, persisted data, player-facing surfaces, multiplayer authority, and a safe restoration path. If a clean runtime toggle is unsafe, changing the setting may require a restart or migration. If a safe fallback cannot be guaranteed, the option remains unavailable rather than pretending to be configurable.

Each audit entry should use the same compact record: system, current player loop, VRPG value, chore or conflict cost, disposition, default state, configuration lever, dependencies, disable strategy, migration needs, and acceptance test. This becomes the implementation checklist for the later Survival replacement and Harmony boundary map.

### 15.2 Configuration contract

The intended experience is a named **VRPG default** preset in which almost every total-conversion feature is enabled. Major replacements remain independently configurable so servers and modpacks can retain selected Survival systems or stage migrations.

Configuration follows these rules:

- A major feature has one documented ownership switch rather than several patches that must be disabled separately.
- Enabling a replacement disables or redirects the conflicting Vanilla behavior as one transaction.
- Disabling a replacement restores a defined fallback, normally Vanilla or Survival behavior, and removes VRPG UI that would falsely imply ownership.
- Dependencies and conflicts are validated at startup. Invalid combinations fail with the exact settings and suggested correction instead of silently creating a half-active hybrid.
- Settings that change persisted data, inventory ownership, world generation, or player capabilities require a restart and a migration preview.
- Client settings control presentation and accessibility only. The server owns gameplay rules and synchronizes the effective feature set to joining clients.
- Addons query capabilities such as `vrpg:typed-inventory` or `vrpg:survival-hunger` rather than assuming a preset name.
- Compatibility is tested around supported feature boundaries, not every theoretical combination of individual low-level toggles.

Initial major feature switches should cover Survival needs, VRPG combat rules, progression, expeditions, inventory replacement, loot handling, custom world generation at world creation, main-world mutation, housing, crafting replacement, death/recovery, Rebirth, and persistent-world services. Sub-options tune an enabled feature but do not compete for ownership of the same behavior.

Example intent, with exact names deferred until implementation:

```json
{
  "Preset": "vrpg-default",
  "Features": {
    "CombatRules": true,
    "Expeditions": true,
    "InventoryReplacement": true,
    "CustomWorldGeneration": true,
    "Housing": true,
    "VanillaCharacterSystem": false,
    "SurvivalNeeds": false,
    "Rebirth": true
  }
}
```

The preset supplies defaults; explicit server overrides win. A configuration report should print the effective owner of each replaced system and any compatibility degradation.

### 15.3 Keep initially

- Low-level block interaction, chunk, and construction APIs used behind VRPG permissions.
- Vanilla inventory and container APIs as compatibility bridges for blocks, recipes, and external mods.
- Terrain, weather, physics, rendering, and general AI infrastructure.
- Useful blocks, decorations, vehicles, and creatures.
- Selected low-level terrain, biome, vegetation, lighting, weather, and chunk-generation components that pass the Survival-system gate.

### 15.4 Rework

- Player health and regeneration.
- Weapons, armor, and damage.
- Creature scaling and spawning.
- Healing and consumables.
- Death and recovery.
- Player inventory, backpack ownership, hotbar access, stack representation, loot pickup, and overflow.
- Character creation, classes, traits, starting gear, class-gated recipes, and character progression.
- Main-world generation, traversal guarantees, resource placement, and mutation authority.
- Player construction access, ownership, and housing-realm lifecycle.
- Traders and economy.
- Recipes and crafting stations.
- Temporal concepts where they become regions, resources, or enemies.

### 15.5 Disable by default in VRPG worlds

- Hunger depletion.
- Nutrition progression.
- Food spoilage pressure.
- Body-temperature punishment.
- Mandatory seasonal preparation.
- Random temporal interruptions unrelated to chosen activities.
- Tool and equipment replacement chores.
- Irrelevant Survival tutorials and handbook paths.
- Vanilla class mechanics, traits, trait stat modifiers, automatic class gear, and
  class recipe gates. The appearance creator is retained and rethemed, while its
  class step is replaced by a VRPG starter-kit choice.
- Ordinary player block placement, breaking, chiseling, liquids, ignition, and similar lasting mutation outside authorized housing.

Configuration and asset patches are preferred. Harmony patches are reserved for code-driven behavior that cannot be safely neutralized through supported data or configuration.

### 15.6 Mod compatibility boundary

VRPG does not promise compatibility with arbitrary Survival gameplay mods. Its custom worldgen, protected terrain, inventory, classes, combat, crafting, economy and progression intentionally replace the assumptions those mods commonly target.

The supported expansion paths are VRPG-native addons, automatic ingestion of compatible creature assets, and narrowly audited Home content. Creature ingestion is a deliberate exception: VRPG may reuse a foreign creature's shape, textures, animations, sounds and identity while generating a synthetic VRPG-owned actor with VRPG AI, stats, abilities, spawning, defeat and rewards. It does not reproduce arbitrary creature code or source balance. Decorative and functional building assets may be admitted inside authorized housing without becoming eligible worldgen, loot, recipes, equipment or main-world blocks. Arbitrary code cannot be safely confined to a dimension if it bypasses VRPG services, so Home support remains explicit and allowlisted rather than inferred from successful loading.

Strict compatibility is the default. Permissive operation may help development and private experimentation, but it carries no balance, persistence or mutation guarantee.

## 16. Multiplayer

VRPG supports solo and cooperative play without requiring a fixed tank-healer-damage composition.

### Cooperative principles

- Every class can survive and contribute alone.
- Support actions improve allies without reducing the user to passive maintenance.
- Encounter scaling considers party size and role coverage without spawning only larger health pools.
- Route and bargain decisions use a visible party-ready or vote process.
- Personal targeted rewards coexist with shared world outcomes.
- Players can revive, protect, set up, and capitalize on one another's statuses.

### Trading

Trading is configurable by server. Default progression must remain functional in solo and closed-group worlds. No core build should assume access to a global market.

Regional loot identity naturally supports trade, but targeted acquisition, adaptation, and Rebirth remain valid alternatives.

## 17. Player-facing information design

Combat surfaces show only information needed for the current decision. Planning surfaces provide complete mechanical detail.

### 17.1 Combat HUD contract

- **Player goal:** Read immediate combat state without losing sight of threats.
- **Required information:** Health, relevant class resources, equipped skills, charges, actionable cooldowns, selected target, important statuses, immediate objective, and critical encounter warnings.
- **Excluded information:** Full affix math, progression explanations, inactive resources, raw diagnostic state, and complete enemy encyclopaedia entries.
- **Combat-visible:** Yes; it must preserve the crosshair, telegraphs, and peripheral threats.
- **Authority:** Server state is authoritative; local input feedback appears immediately and reconciles cleanly.

### 17.2 Expedition planner contract

- **Player goal:** Choose an objective, region, rewards, and optional risk.
- **Primary decision:** Start the selected expedition configuration.
- **Required information:** Objective effect, threat range, known enemy identities, likely reward families, modifiers, extraction rules, and persistent consequences.
- **Excluded information:** Unknown rooms, undiscovered boss mechanics, and developer generation weights.
- **Success feedback:** A compact final contract summary before travel begins.

### 17.3 Route-choice contract

- **Player goal:** Choose the next branch quickly.
- **Required information:** Branch identity, known risk, likely opportunity, cost, and any consequence created by current run state.
- **Excluded information:** Full future layout and exact random tables.
- **Input:** World interaction and an accessible selection alternative; essential information cannot require hover alone.

### 17.4 Item comparison contract

- **Player goal:** Decide whether to equip, keep, modify, salvage, or trade an item.
- **Required information:** Requirements, base profile, major effects, transformations, and comparison values.
- **Excluded information:** Flavor and formula derivations from the primary scan area; both remain available in details.

### 17.5 Rebirth contract

- **Player goal:** Understand and confirm the transition into a new life.
- **Required information:** Reset list, retained list, Legacy reward, affected equipment, and immediate new-life choices.
- **Primary control:** Rebirth confirmation, disabled until a Legacy choice is valid.
- **Rejected state:** Explain exactly which eligibility requirement remains.
- **Authority:** Entirely server-authoritative and safely repeat-protected.

### 17.6 Information layers

- Glance: identity, current state, consequence, and available action.
- Interaction screen: everything needed to complete the current decision.
- Library: exact mechanics, discovered enemy knowledge, formulas, and durable explanations.
- Logs: identifiers, validation details, generation seeds, revisions, and exceptions.

Important information never relies on color, sound, animation, or hover alone.

## 18. Extensibility architecture

### 18.1 Definition families

The target data registry includes:

- `RegionDefinition`
- `FactionDefinition`
- `ObjectiveDefinition`
- `ExpeditionDefinition`
- `RunModifierDefinition`
- `RewardPoolDefinition`
- `ItemBaseDefinition`
- `UniqueItemDefinition`
- `SkillTransformationDefinition`
- `EffectOperationDefinition`
- `StatusPropagationDefinition`
- `DefeatResponseDefinition`
- `CombatSpaceDefinition`
- `MovementProfileDefinition`
- `CombatBehaviorDefinition`
- `EnemyTacticsDefinition`
- `SquadPatternDefinition`
- `InventoryCategoryDefinition`
- `ItemStoragePolicyDefinition`
- `LootPresentationDefinition`
- `WorldGenerationDefinition`
- `RegionLayoutDefinition`
- `RouteDefinition`
- `WorldSiteDefinition`
- `ResourceInteractionDefinition`
- `WorldMutationDefinition`
- `TemporaryTerrainDefinition`
- `HousingDefinition`
- `BuildPermissionDefinition`
- `FeatureDefinition`
- `ClassDefinition`
- `EnemyArchetypeDefinition`
- `EnemyRoleDefinition`
- `EncounterDefinition`
- `DungeonThemeDefinition`
- `DungeonRoomDefinition`
- `BossDefinition`
- `WorldOutcomeDefinition`
- `LegacyDefinition`

Existing skill, status, stat, rarity, affix, room, encounter, dungeon, scaling, class, and Library definitions remain part of this model.

### 18.2 Reusable effect grammar

Skills, statuses, temporary powers, talents, equipment, enemies, and environmental objects should share a small effect grammar. A propagation rule composes five independent parts:

1. **Trigger:** apply, tick, threshold, consume, expire, death, skill hit, or entry into an area.
2. **Source condition:** required status, stacks, tags, ownership, health state, or triggering damage identity.
3. **Target selector:** radius, target count, allegiance, required or forbidden tags, line of sight, and selection priority.
4. **Operation:** apply, copy, move, add, consume, convert, detonate, spawn an area, or invoke another named effect.
5. **Limits:** generation cap, per-target cooldown, stack falloff, duration inheritance, and total propagation budget.

Named rules can be referenced and modified rather than copied into every skill. For example, the same proximity selector and bounded-chain behavior can support Poison contagion, Fire ignition, Lightning arcs, a healing pulse, or a construct command while their triggers and operations remain different.

This grammar is intentionally smaller than a general scripting language. A genuinely new action requires code and then becomes a validated reusable operation.

### 18.3 Composition rules

Content selects compatible content through:

- Namespaced codes for identity.
- Tags for broad compatibility.
- Explicit references for authored relationships.
- Threat and complexity budgets.
- Preconditions.
- Incompatibilities.
- Required and forbidden room roles.
- Reward weights and guarantees.
- Outcome hooks.

Tags enable expansion, but validation rejects silent ambiguity. Important authored sequences use explicit references rather than hoping tags produce a coherent story.

### 18.4 Stable extension points

Code-level APIs should expose events or services for:

- Expedition creation and completion.
- Room discovery and resolution.
- Objective state changes.
- Reward generation and modification.
- Creature creation and defeat.
- Skill cast, hit, and transformation.
- Status application, consumption, and expiration.
- Status threshold, propagation attempt, transfer, and chain completion.
- Defeat response selection, aftermath creation, and aftermath resolution.
- Combat behavior registration, intent transition, navigation failure, and squad-claim changes.
- Inventory insertion, extraction, migration, overflow, and storage revision changes.
- World-site generation, contextual mutation, persistent outcome application, and rollback.
- Temporary-terrain placement, damage, navigation invalidation, expiration, and cleanup.
- Housing allocation, entry, exit, permission, and migration.
- Effective feature resolution and capability publication.
- Rebirth eligibility, preview, confirmation, and completion.
- Persistent world outcomes.

Server authority is explicit for every mutation.

### 18.5 Validation

Startup validation checks:

- Missing codes and namespace collisions.
- Invalid tag combinations.
- Invalid feature dependencies, conflicts, or ambiguous system ownership.
- Inventory categories without a safe fallback or storage policies without a migration route.
- Combat spaces incompatible with their assigned actor, orbit, charge, or formation requirements.
- Required world goals unreachable without protected terrain modification.
- Temporary terrain that can seal every route, overwrite protected state, or lack crash-safe cleanup.
- Housing-required progression without a valid housing or alternate hub service.
- Impossible room graphs.
- Objectives with no compatible encounters or outcomes.
- Reward pools with no eligible entries.
- Regions lacking universal progression rewards.
- Bosses with unreachable prerequisites.
- Rebirth rewards that exceed configured category caps.
- Propagation rules with unbounded generations, missing operations, impossible selectors, or recursive cycles without a budget.
- Defeat responses with conflicting primary presentations, unbounded remains, or gameplay state tied to client-only physics.
- Player-facing choices with no distinct consequence.

Offline reports should show composition coverage and dead content without exposing raw diagnostics to players.

## 19. Balance principles

### 19.1 Bounded vertical growth

Levels and gear provide meaningful strength, but vertical growth is segmented and bounded enough to preserve older content. Optional difficulty modifiers extend challenge without requiring endless base-stat inflation.

The current exponential creature tiers are useful for simulation but should not become the sole endgame structure. Health growth must not outpace the creation of new decisions.

### 19.2 No universal best build

Build value is contextual:

- Region and element.
- Objective.
- Enemy roles.
- Party composition.
- Expedition modifiers.
- Desired rewards.
- Player execution preference.

General-purpose builds remain viable but sacrifice the efficiency or risk ceiling of specialization.

### 19.3 Choice quality tests

A choice is meaningful when:

- The player understands the broad consequences.
- At least two options are situationally desirable.
- The choice changes action, route, risk, reward, or future state.
- One option is not universally dominant.
- The choice occurs early enough to affect play.

### 19.4 Decision-density targets

These are evaluation heuristics, not rigid timers:

- Combat presents a positioning, priority, or timing decision every few seconds.
- An expedition presents a route, risk, or reward decision every several minutes.
- A session advances or changes at least one chosen persistent goal.
- Major build decisions occur often enough to invite experimentation but not so often that identity disappears.

Telemetry should measure abandoned objectives, dominant choices, unused skills, extraction behavior, region participation, build diversity, and Rebirth repetition. It should not optimize only time played.

## 20. Content model for the first complete slice

The first slice proves agency rather than breadth.

### Required scope

- One persistent hub or reclaimed site.
- One complete elemental region: the Rust-Worn Granite Halls.
- Three selectable objectives with materially different generation and outcomes.
- Eight to twelve room definitions across at least six functional roles.
- Three ordinary enemy roles and one support or commander role.
- Two elites or lieutenants.
- One boss with at least two variants created by expedition decisions.
- Four complete combat builds, including Rust Scrapper.
- Twelve to sixteen polished skills, retaining the current projectile work.
- One complete propagation kit with at least three viable ways to create a group and a useful isolated-target payoff.
- Two contrasting defeat responses, including one purely presentational response and one server-authoritative aftermath.
- A small set of transformative temporary powers.
- Regional, objective, boss, and universal reward layers.
- One extraction decision and one graduated failure outcome.
- One persistent world result.
- A minimal Rebirth prototype after the level curve can be completed in testing.

### Example objectives

1. **Hunt the Rift Heart**  
   Direct boss pursuit, boss materials, and escalating elite pressure.

2. **Raid the Salvage Works**  
   More machinery rooms, Scrapper and construct rewards, optional alarm escalation.

3. **Recover the Lost Surveyors**  
   Search and rescue, information rooms, route discovery, and a persistent hub specialist.

The objectives reuse the same regional kit while changing purpose, room weights, encounter composition, reward pools, and outcomes.

## 21. Development path

### Phase 1: Preserve the fun combat core

- Treat ballistic throwing and targeted impacts as quality benchmarks.
- Establish the default-enabled feature registry and report the effective owner of each replaced Survival system.
- Establish versioned persistence, migration, and recovery harnesses before new systems depend on them.
- Build and validate the first mostly level combat-space profile.
- Generate slab-smoothed half-step approaches whose player and AI traversal agree.
- Prototype burst movement and separate long-route navigation from local combat steering.
- Implement the first composable advance, hold-range, orbit, screen, and straight-charge behaviors.
- Establish protected main-world mutation and prototype one temporary-wall skill with AI counterplay and exact cleanup.
- Finish four coherent combat kits.
- Make setup/payoff statuses readable and mechanically complete.
- Implement bounded status propagation as a reusable effect operation.
- Prototype deterministic elemental and skill-specific defeat responses.
- Establish enemy roles and mixed-role encounter tests.
- Prevent Survival health, regeneration, and HUD behavior from conflicting with VRPG.

### Phase 2: Prove one meaningful expedition

- Generate one custom region slice whose required routes and resources work without mining or permanent building.
- Add objective selection and an expedition contract.
- Add run state, route choices, banking, and extraction.
- Expand Granite Halls by room role rather than raw room count.
- Make at least three choices affect the boss or final reward.

### Phase 3: Establish loot identity

- Replace backpack-dependent player capacity with the fixed typed inventory behind a configurable ownership boundary.
- Implement aggregate commodity storage, bounded Vanilla-stack bridges, paged queries, and lossless migration tests.
- Implement actual item generation from rarity and affix data.
- Add regional, objective, boss, and universal pools.
- Add a small set of uniques that transform skills.
- Add targeted salvage and bounded crafting.

### Phase 4: Connect the persistent world

- Prototype one persistent Home with protected ownership, building, indexed storage access, and a material-processing station.
- Add a reclaimed hub or specialist system.
- Persist regional outcomes and unlocked routes.
- Add the first faction state.
- Ensure none of these systems require recurring upkeep.

### Phase 5: Rebirth

- Implement eligibility and consequence preview.
- Reset character-bound progression while preserving world and account knowledge.
- Add capped Legacy choices.
- Generate alternate low-level objectives for reborn lives.
- Validate that a second life does not replay the same mandatory sequence.

### Phase 6: Expansion tools

- Add the missing definition families and registries.
- Extend validators and offline coverage reports.
- Document the authoring contracts.
- Build admin inspection tools that explain why content was or was not eligible.

### Phase 7: Breadth and endgame

- Add additional elemental and faction regions.
- Add cross-region objectives and bridge rewards.
- Add multi-region bosses shaped by world history.
- Expand optional challenge modifiers and Rebirth goals.
- Preserve multiple viable routes through every progression stage.

## 22. Design risks and safeguards

### Risk: Apparent choice with solved optimal paths

Safeguard: Measure selection rates, use contextual value, create opportunity costs, and favor transformations over additive bonuses.

### Risk: Elemental regions become equipment checks

Safeguard: Use soft counters, bridge rewards, temporary adaptation, alternate objectives, and clear advance information.

### Risk: Rebirth becomes mandatory repetitive grinding

Safeguard: Cap permanent power categories, front-load new possibilities, skip the original campaign, and generate new-life objectives from current world state.

### Risk: Procedural generation creates meaningless corridors

Safeguard: Track room roles, require consequential branches, persist run-state effects, and validate objective-specific composition.

### Risk: Protected world generation traps or misleads players

Safeguard: Validate required traversal without mining or building, replace embedded resources with explicit interactions, provide recovery routes, and make immutable versus contextual objects visually legible.

### Risk: Temporary terrain trivializes encounters or destroys navigation performance

Safeguard: Use encounter placement envelopes, route-preservation checks, strict construct budgets, local navigation invalidation, readable enemy responses, and transactional cleanup.

### Risk: Customization overwhelms combat readability

Safeguard: Four default active slots, strong effect language, bounded status displays, progressive disclosure, and mechanically distinct skill silhouettes.

### Risk: Propagation becomes automatic screen-clearing or visual noise

Safeguard: Bound chains by target and generation budgets, preserve single-target tradeoffs, make grouping require active manipulation, and emphasize only locally relevant transfer feedback.

### Risk: Death reactions become expensive or obscure live hazards

Safeguard: Select one primary response, cap secondary aftermaths and ragdolls, shorten distant remains, separate authoritative effects from visual physics, and give every dangerous aftermath a stable telegraph.

### Risk: Persistent systems become chores

Safeguard: Persistent sites provide options rather than maintenance timers. Absence does not cause decay or loss by default.

### Risk: Extensibility weakens identity

Safeguard: Maintain a small stable vocabulary of combat roles, reward layers, objective types, and outcome rules. Add new primitives deliberately.

### Risk: Configuration creates untestable hybrid modes

Safeguard: Configure major ownership boundaries, ship a strongly supported default preset, validate dependencies, and publish a smaller set of supported compatibility profiles. Do not expose every internal patch as an independent gameplay toggle.

## 23. Open design decisions

The following require prototypes rather than immediate permanent answers:

- Whether classes are fixed for a life or can be hybridized beyond a threshold.
- The final level cap and expected time to first Rebirth.
- How many Legacy power bonuses can be active simultaneously.
- Whether regional resistance belongs primarily on gear, talents, temporary powers, or knowledge progression.
- How much equipment is shared between lives.
- Whether failed objectives alter the world permanently or only temporarily by default.
- Whether open-world combat uses fixed regional threat, nearby-player scaling, selectable world tiers, or a hybrid.
- How settlement and faction state should behave on public multiplayer servers.
- Which Survival crafting stations remain as flavor and which become item-modification interfaces.

Each decision should be judged against the pillars: combat focus, agency, readable consequence, low chore burden, nonlinear progression, and extensibility.

## 24. Acceptance criteria for the end goal

VRPG fulfills this design when:

- Every inherited Survival system has a documented Keep, Reframe, Replace, or Disable disposition and a tested default state.
- A new default world is generated for protected traversal and remains completable without ordinary mining or building.
- Temporary terrain creates useful combat choices without becoming permanent construction, sealing required routes, or destabilizing pathfinding.
- A player can state why they chose an expedition beyond "it was the next level."
- Two runs through the same region can differ in purpose, decisions, boss state, and outcome.
- Combat builds feel different through actions and timing, not only damage color.
- Specializing for one region creates real advantages and real opportunity costs without blocking other content.
- The player can pursue desired reward families through understandable actions.
- A failed expedition can still produce an interesting state change or partial result.
- A completed life can Rebirth into a genuinely different early game without replaying a mandatory campaign.
- The persistent world visibly reflects player choices without demanding upkeep.
- Addons can introduce ordinary content through validated data definitions.
- A server can disable a major replacement and receive its documented fallback without duplicated ownership, lost persisted data, or misleading VRPG UI.
- The combat HUD remains readable while complete planning information stays easy to find.
- Survival systems no longer dictate the player's routine in a VRPG world.
