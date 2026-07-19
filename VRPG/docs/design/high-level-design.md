# VRPG High-Level Design

**Status:** Living draft

**Last updated:** 2026-07-17
**Scope:** Product vision, gameplay pillars, core loop, progression structure, content boundaries, and high-level technical constraints.

This document defines what VRPG is trying to become and the constraints that should survive implementation changes. Detailed balance values, schemas, skill specifications, and implementation plans belong in separate documents derived from this one.

## Product Vision

VRPG offers a new way to play Vintage Story for build crafters and power-fantasy players, with enough systemic variety and replayability to support many distinct characters and approaches.

VRPG is an optional RPG layer rather than a replacement for Vintage Story. It should integrate naturally with the existing world, activities, and fiction while allowing players and servers to engage with as much or as little of the RPG content as they want.

## Design Goals

### Build Crafting With Consequences

Player power should come from assembling a coherent build: class choices, skills, talents, equipment, status interactions, and preparation. A successful build should change how the player approaches combat, not merely raise damage and health numbers.

Meaningful build decisions should arrive at a steady cadence. The initial target is approximately one important decision or newly relevant RPG system every ten levels.

### Earned Power Fantasy

VRPG should allow players to become dramatically more capable, but that power must be earned through understanding and combining systems. The player should eventually feel strong because their build works, not only because their level is higher than the enemy's.

### Replayability Through Distinct Solutions

Different builds should solve combat problems in materially different ways. Replayability should come from mechanical identities, interactions, tradeoffs, class combinations, itemization, and varied rift conditions rather than repeated progression through the same numerical ladder.

### Natural Vintage Story Integration

VRPG should preserve and reinforce the following parts of Vintage Story:

- Exploration.
- Temporal stability and the Rust World.
- Non-combat crafting and material progression.

These systems should supply context, preparation, access, or advantages to RPG play without requiring ordinary players to participate in the entire RPG layer.

### Cooperative First, Broadly Scalable

Small cooperative servers are the primary design target. Solo play and larger public servers should remain supported where performance and encounter design allow it.

Cooperative play should reward playing together without requiring rigid MMO roles or making solo builds nonviable.

### Server Sanity

Most VRPG content must remain optional. A server should be able to host players who actively pursue VRPG progression alongside players who primarily engage with normal Vintage Story.

VRPG should avoid mechanics that continuously impose combat, loot management, world disruption, or progression obligations on uninvolved players.

## Explicit Non-Goals

VRPG must not become a simple numbers-go-up simulator in which the player gains levels solely to defeat enemies whose statistics rise in parallel.

The following outcomes are specifically undesirable:

- Levels being the dominant answer to every combat problem.
- Enemy scaling that merely cancels player progression.
- Builds that differ in displayed numbers but play identically.
- Mandatory engagement with most RPG systems to continue playing ordinary Vintage Story.
- Server-wide combat pressure that repeatedly disrupts uninvolved players.
- Progression that invalidates exploration, temporal stability, or non-combat crafting.

## Core Gameplay Loop

The primary loop is:

```text
Explore and craft
        ↓
Acquire or prepare a Rift Chart
        ↓
Choose a build and expedition preparation
        ↓
Enter a rift or open a controlled breach
        ↓
Complete encounters under explicit risks and modifiers
        ↓
Earn gear, materials, and progression
        ↓
Refine the build and choose the next challenge
```

Combat should provide most direct RPG experience. Non-combat activities should remain relevant through preparation, access, equipment, supplies, or temporary combat advantages rather than becoming equivalent XP farms.

One candidate bridge is for meaningful crafting or preparation to grant a bounded combat benefit. This must be designed so that crafting remains valuable without becoming a compulsory buff-maintenance chore.

### World Pace and Encounter Pace

Vintage Story's world progression is intentionally slow, while VRPG combat is intended to be substantially faster. The mod should not solve this tension by accelerating the entire survival game or by forcing players to wait through vanilla material progression before the RPG becomes enjoyable.

The working direction is to separate the two tempos:

- Exploration, settlement, material acquisition, and preparation retain meaningful Vintage Story pacing.
- Once a player enters an RPG encounter or rift, combat and reward feedback operate at a much faster ARPG pace.
- Early RPG content provides credible choices and rewards before late vanilla armor progression.
- Later crafting and exploration improve access, preparation, and build refinement rather than acting only as time gates.

The first vertical slice must test whether these tempos feel connected rather than like two unrelated games.

## Rift Structure

Rifts are the primary concentrated test of a build. Manifold-backed custom dimensions are the intended main experience because VRPG can control their generation, encounters, and rules without interference from ordinary world generation. Overworld breaches remain a fallback rather than an equal content track.

The core RPG remains self-contained. Manifold integration belongs behind the optional dungeon-module boundary so failure or absence does not disable unrelated progression systems.

The dungeon MVP standardizes its custom-dimension host on Manifold 0.4.2 or newer rather than duplicating dimension allocation, pre-generation, transit, relighting, reconnect safety, and ephemeral cleanup. Room definitions, schematic assembly, layout generation, encounters, exploration, and objectives remain VRPG-owned and depend only on a narrow dungeon-host interface. The preferred release boundary is a `vrpgdungeons` companion package that depends on both `vrpg` and Manifold, preserving a self-contained RPG core while using Manifold through its strongly typed API instead of the current reflection proof of concept. See [Dungeon MVP Plan](dungeon-mvp-plan.md).

Rifts use semi-procedural generation assembled from curated room and corridor pieces. Authored pieces provide encounter quality and readable spaces; procedural assembly supplies route, layout, and replay variation.

Initial theme differences are primarily spatial and tactical:

- Wide-open themes offer movement and visibility but reduce the player's ability to control approach lanes.
- Tight themes make funnels and area control stronger but increase the risk of being surrounded or trapped.

Future themes may add enemy families, hazards, rewards, objectives, or elemental conditions after the spatial foundation works.

Initial objective families are:

- **Elimination:** Clear staged encounters and finish with a boss.
- **Survival:** Endure a timer against massed waves or hordes.
- **Resource objective:** A possible secondary mode built around contested collection rather than general mining.

Rifts should remain primarily combat spaces. Their rooms should not become ordinary mineable resource chambers.

Horde encounters must use readable spawn sources, lanes, portals, nests, room entrances, or other spatial staging. Enemies should not simply appear on top of the player.

All release rifts should be soloable. Additional players increase enemy difficulty and improve loot rather than being required to complete the activity. Exact duration, maximum party size, and group multipliers remain open.

For solo rift failure, the current direction is to partially refund and downgrade the chart or attempt rather than destroy its full value. Group failure uses a different exception that still needs definition; it must avoid creating refund or carry exploits.

How Rift Charts are acquired and upgraded remains unresolved.

The first rift should occur only after the player has obtained credible defensive preparation. Vanilla armor acquisition is currently too slow and grind-heavy to assume this happens naturally, so VRPG will need either:

- An earlier RPG-compatible defensive progression path.
- Rift tiers balanced around pre-metal or partial armor.
- New ways for vanilla crafting and exploration to accelerate combat readiness.

This must not trivialize the value of normal material progression.

## Progression Model

### Progression Ownership

Initial progression is account-bound. A future character system or New Game Plus mode may provide additional replay structures, but neither is required for the first complete version.

Account-bound progression must still be scoped safely for multiplayer and world compatibility. The exact relationship between an account, a server, and a world requires a technical persistence decision.

### Level Bands

Levels are a pacing and unlock structure, not the sole source of power.

Initial milestone targets:

| Level range | Intended state |
|---|---|
| 1-10 | Establish the first meaningful build direction and learn the core RPG interface. |
| 10-30 | Introduce additional RPG systems at a controlled cadence and begin early specialization. |
| 30-40 | A coherent, functioning build should be online. |
| 40-60 | Refine interactions, cover weaknesses, and specialize equipment. |
| 60-100 | Endgame progression, difficult rifts, and deeper build refinement. |

Approximately every ten levels should present a meaningful decision, unlock, or newly relevant layer. A system should not be introduced merely to create another obligation; it must create a new decision or play pattern.

Actual leveling speed will be tuned independently from these level milestones. Balance work should define expected encounters or play time per band after the underlying loop is functional.

### Experience Sources

Combat is the primary source of RPG experience.

Exploration, crafting, and other survival activities should support combat progression indirectly. Any direct non-combat XP should be limited and should not make passive industrial production the optimal leveling strategy.

### Sources of Player Power

Creature levels provide almost all of a creature's raw statistical growth. Their level is the pressure curve that requires the player to keep developing a build.

Player levels do not grant enough automatic raw power to keep pace by themselves. Automatic growth is limited primarily to modest base-resource improvements such as health and mana. The larger share of player power must come from deliberately spending earned points and combining stats, talents, skills, equipment, crafting, and preparation.

A player who reaches a high level while leaving points unspent should lose decisively against level-appropriate enemies. This is intentional: gaining a level creates a build decision rather than automatically solving the next difficulty tier.

This rule must not collapse into a single mandatory allocation. Multiple coherent builds should be able to satisfy each progression check through different combinations of offense, defense, control, sustain, mobility, and execution.

The intended distribution is:

- Creature level supplies most enemy health, damage, and other raw scaling.
- Player level supplies modest health and resource baselines plus access to choices.
- Allocated stats and talents provide the player's primary progression response.
- Skills and class combinations determine how that invested power is expressed.
- Gear and crafting provide specialization, refinement, and chase goals.
- Player execution and expedition preparation continue to matter.

Exact per-level values and power budgets remain balance decisions, but this distribution is a fixed design direction.

### Reincarnation and Legacy Progression

Reincarnation is a first-class long-term progression pillar, although its complete implementation may follow the initial level-to-endgame vertical slice. Its purpose is to make the full progression range permanently reusable, encourage new builds, and provide an account-level reason to retain useful equipment from many level bands.

Reincarnation is optional. A player may remain at level cap and continue playing endgame content without resetting.

The initial implementation target is deliberately simple:

```text
Reach level cap
        ↓
Choose to reincarnate
        ↓
Reset level, classes, skills, and allocated points
        ↓
Keep equipment, storage, and account-level unlocks
        ↓
Gain one permanent Legacy Rank
        ↓
Legacy Rank slightly improves future VRPG loot rarity
```

Retained equipment keeps its level and other usage requirements. Reincarnation therefore turns the player's equipment collection into a long-term progression library rather than immediate level-one power.

The initial Legacy Rank reward is account-wide rather than tied to the classes equipped when reincarnating. This prevents accessible class respecialization from being exploited to claim a class-specific permanent bonus without playing that class.

Legacy loot rarity should:

- Shift eligible VRPG item-quality rolls upward slightly rather than increase raw item quantity.
- Apply only to VRPG reward generation.
- Use diminishing returns so repeated lives remain rewarding without allowing permanent bonuses to overwhelm the current build.
- Be visible as a clear account-level statistic.
- Avoid directly multiplying player damage or health.

Future legacy rewards may include bounded improvements to salvage yield, Tender discovery, chart quality, or crafting outcomes. These are extension candidates rather than initial requirements.

Class-specific past-life rewards remain a possible later system. VRPG should record normalized per-life class journey progress before those rewards ship so historical eligibility can be determined from classes actually played rather than the classes equipped at the reset moment.

Journey progress should be based on level-normalized eligible XP earned while a class is equipped. Death-related XP loss should not remove that historical progress. Exact qualification thresholds and whether class rewards are ever enabled remain unresolved.

Because rift loot is primarily shared, the rule for applying different party members' Legacy Ranks to group rewards requires a later multiplayer specification.

## Classes, Skills, and Builds

### Dual-Class Foundation

Each player chooses two VRPG classes. The combination of those classes is the foundation of the player's build identity and should create more possibilities than either class provides alone.

Class pairs should support both obvious synergies and less direct combinations that solve problems in unusual ways. The system should avoid requiring one predetermined partner for any class.

A class is not content-complete merely because it owns a list of skills. A
player who mains one class must be able to pursue at least two, and preferably
three, mechanically distinct combat styles. Each style needs its own repeatable
combat loop, visible setup and payoff, failure or reset condition, intended
player appeal, and planned way to survive bosses and sustained pressure. A
second class may improve or bend that loop, but must not be required to supply
a missing core loop or missing defense.

These intended styles are examples and design tests, not subclasses or
exclusive specialization tracks. Players remain free to mix their components,
combine them across both classes, or discover an unplanned build. Skills should
interact through explicit tags, statuses, resources, positions, and combat
events rather than hidden membership in a named style. An unconventional build
that follows real interactions and stays within the power budget is an intended
outcome of the system.

Vanilla classes remain independent background and lore for now. Their existing effects are generally small and often focused on crafting, so they are not prerequisites, replacements, or major inputs to the VRPG class build. Future integration remains possible where it adds meaningful identity without making a player's original vanilla choice a trap.

### Skill Acquisition and Investment

Players earn skill points that can be spent either to unlock additional class skills or to specialize further into skills they already know.

Active-skill ranks provide modest numerical improvement after rank 1 grants the
complete action. Class passives are different: they are eight-rank,
build-defining point sinks that scale heavily with investment. The player is
not expected to acquire every passive in either chosen class. A finished build
should maximize only a few defining passives, partially invest in selected
support passives, and leave several untouched. Unlocking interacting skills
still creates the broadest mechanical changes, while deep passive investment
decides which of those interactions the build truly specializes in.

All class skills are weapon-agnostic and retain their complete authored effect
with any equipped weapon. Weapon families instead favor related affixes in
their drop pools and weighting. This produces recognizable item identities
without turning a held weapon into a class restriction or blocking unusual
dual-class combinations.

Every damaging skill nevertheless scales from the equipped weapon's resolved
VRPG Weapon Power. “Weapon-agnostic” means no family restriction or hidden
family penalty; it does not mean that spells receive a separate automatic
damage ladder. Attacks, spells, ailments, constructs, and ordinary attacks
against RPG enemies all express their damage as an authored percentage of the
same weapon foundation.

Weapon Power is parallel to native Vintage Story attack power. Native combat
against non-RPG targets remains intact. VRPG-eligible targets use the VRPG
formula so a vanilla attack cannot bypass level scaling, while a normal vanilla
weapon without RPG item data remains a usable level-1 fallback rather than
silently inheriting endgame power.

The permanent weapon calculation order is minimum-level baseline, affixes,
then a bounded rarity power scalar. There is no stale-item debuff and no
comparison with the target's level. Near endgame, an unchanged weapon twenty
levels below the encounter should naturally fall outside the intended
clear-time target because its number is too small. A remarkably rolled item may
bridge one twenty-level band but should not comfortably bridge two. The initial
curve uses `10 × 1.035^(required level - 1)` before affixes, with Common/Rare/
Unique rarity power seeds of `1.00/1.15/1.20`; these numbers remain balance
seeds governed by the clear-time requirement.

Keeping the weapon current is necessary but never sufficient at high level.
Ordinary creature health must outpace current-level base Weapon Power strongly
enough that the missing multiplier has to come from an actual build. The first
health-tier seed grows this pressure from `1×` at level 1 to about `3.6×` near
level 40, `6×` near level 60, `9×` near level 80, and `13.7×` at level 100.
A level-100 character with a plain level-100 weapon, default critical stats,
and no skills or talents completing a representative level-100 fight is a hard
balance failure. The weapon maintains the foundation; skill investment,
passives, talents, affixes, and mechanical interactions make it viable.

The intended progression pattern is:

```text
Choose two classes
        ↓
Unlock an initial active skill
        ↓
Invest modestly in its direct strength
        ↓
Unlock supporting skills and interactions
        ↓
Specialize how the primary skill is delivered, sustained, or converted
        ↓
Refine the complete build through later points, talents, and gear
```

It is valid for a build to center on one low-cooldown primary damage ability. Such a build should use other skills, talents, and equipment to change how that ability works rather than filling the action bar with redundant damage buttons.

This is not a requirement that every build use one-button damage. Rotational, conditional, summoner, support, setup-and-payoff, and long-duration ability builds should remain possible.

### Active Ability Model

VRPG will provide its own action bar and dedicated hotkeys for active abilities. Vanilla combat is not a sufficient extension point for the intended class and skill system, so VRPG combat abilities should be implemented as a coherent layer rather than as fragile modifications to vanilla attack behavior.

Vintage Story's public hotkey API exposes a fixed set of Controls-menu categories and does not support a mod-defined VRPG category. Every registered control therefore uses the `VRPG — …` name prefix so the bindings remain adjacent and searchable within the engine's existing categories. VRPG should not patch the base Controls screen solely to fabricate a category.

A typical build should use no more than approximately four frequently activated abilities. Some builds may additionally maintain long-duration abilities, summons, stances, or buffs. The interface should favor a small number of readable, meaningful actions over a large MMO-style rotation.

The action bar defaults to four frequently activated skill slots and can be extended to eight in Hub → Options → Combat Hotbar. Reducing its visible size hides slots without clearing their assignments. Players can assign learned skills from the Skills page, rebind each slot independently through Vintage Story controls, move the bar to a screen-relative position, and lock it against accidental movement. Long-duration abilities and weapon interactions remain later implementation decisions.

### Primary Attributes and Defensive Foundations

The highest allocated core attribute is the player's Primary Attribute. Players may invest in secondary attributes without penalty while their intended attribute remains highest. When attributes tie, the selected starting affinity wins the tie. The talent tree begins from six mutually exclusive routes in a wide hexagon near the center of the full tree. The starts cover every ordered dominant-secondary pairing of Strength, Dexterity, and Intelligence, so each attribute dominates exactly two routes. Every start grants an equal-budget `+10` dominant and `+5` secondary raw-stat package; its dominant raw stat sets starting affinity. Until those six packages replace the fixture tree, the first allocated core stat records affinity as a temporary fallback.

Primary Attribute state must always be visible in the character interface. Any allocation that would change it must disclose the resulting mechanical change before or alongside the allocation. The interface must identify the primary stat, show whether its foundation is active, and state the exact activation condition on affected abilities.

Dexterity-primary characters gain Reflex and Evasive Step from the beginning of progression. Evasive Step is an always-active passive that automatically prevents an otherwise lethal hostile hit when ready, moves the character away from the attacker, grants a brief invulnerability window, and then enters cooldown. It gives early Dexterity a reliable answer to true one-shots while Dexterity supplies critical access and passive evasion. Reflex and Evasive Step become inactive when Dexterity is no longer primary; merely investing some points in Strength or Intelligence does not disable them while Dexterity remains highest.

Evasive Step has no hotkey and never consumes a combat-hotbar slot. Later passives may add charges, improve recovery, or change its escape behavior, but those upgrades must not be required for an early Dexterity build to function.

Strength, Dexterity, and Intelligence require comparably meaningful defensive foundations within the six-route design. Their exact active or passive rules remain part of the defensive balance specification.

### Critical Chance Layers

Characters have `5%` base critical chance without bonuses. Most bonuses grant
Additional Critical Chance: these sources sum additively and multiply the base,
so `100% Additional` produces `10%` final chance. A few premium gear and
gear-upgrade sources grant Flat Critical Chance, which increases the base before
Additional scaling and is therefore much rarer. Ordinary class passives and
talent nodes do not grant Flat Crit. More Critical Chance is a separate
multiplicative layer reserved for exceptional gamechangers.

Additional Crit should appear throughout the progression ecosystem—class
passives, talent sections, general gear, weapon-favored affixes, Fittings, and
conditional combat effects—while Flat Crit gives gear drops and upgrades a
scarce premium role. A viable critical build is assembled rather than granted
by one mandatory node. The first target bands are approximately `5%`
uninvested, `10–15%` incidental, `20–30%` committed, and `33–45%` ready to
evaluate strong critical gamechangers. The advanced character sheet must show
base, Flat, total Additional, More multipliers, cap, and final chance.

### Late-Game Damage Engines

A current weapon and generic damage bonuses are not a complete late-game
offense. By the upper level bands, every solo damage build must establish at
least one high-frequency damage engine:

- a **critical engine** that reaches dependable critical frequency and turns
  repeated criticals into sufficient critical damage, triggers, resources, or
  setup/payoff acceleration; or
- a **damaging-ailment engine** that applies, stacks, refreshes, spreads,
  magnifies, or consumes Bleed, Burn, Corrosion, or another explicitly damaging
  ailment rapidly enough to provide sustained pressure beyond direct hits.

Hybrid engines are valid. “Spam” means dependable repeated applications during
the combat loop, not visual or input clutter. One enormous lucky critical or
one token ailment application does not satisfy the requirement.

A level-100 build with a current weapon and generic Additional/More damage but
neither meaningful critical frequency nor damaging-ailment throughput must not
be able to overcome representative late-game Health. If it can, the neutral
damage budget or enemy Health curve is undertuned. Critical and ailment access
must therefore appear across classes, the passive tree, gear, Fittings, and
second-class combinations rather than being exclusive to Dexterity or the
Corroder. Dedicated support builds may outsource kill pressure in a group, but
their solo configuration still needs an engine even if its clear time is poor.

Player damage and enemy Health intentionally scale much faster than player
Health. A level-100 boss around `2,000,000 Health` should be ordinary enough not
to be surprising. The initial reference is a 250-base-Health creature receiving
the level-100 curve and a `×20` boss/archetype Health layer, producing
`2,060,800 Health` before creature rarity.

The corresponding offensive stress case uses `500% Weapon Damage`, one use per
second, `100%` final critical chance, and `500%` total critical damage. A plain
level-100 common weapon supplies about `7,534 sustained DPS` before weapon
affixes, generic damage, More multipliers, ailments, or conditional payoffs. A
sixfold generic damage package raises that to roughly `45,205 DPS`, producing
about a 46-second uninterrupted damage time against the 2.06M reference. Boss
movement, immunity windows, adds, and defensive play increase real duration.

These are stress-test inputs, not a mandatory endgame build. Ailment engines
need comparable sustained output through applications, stacks, and consumption.
Balance must inspect sustained damage, burst windows, ramp time, area coverage,
and uptime rather than comparing only one displayed hit.

### Late-Game Defensive Scale

The existing `100 Health` runtime baseline is a naked failure profile, not a
level-100 target. A committed Health build with strong gear can exceed `10,000
maximum Health`. A committed Magic Shield build can exceed `20,000 maximum
Magic Shield` because shield-focused characters generally lack Armor's
mitigation. Strength reaches Health and Armor most efficiently; Dexterity uses
a smaller raw pool with evasion, Reflex, and Evasive Step; Intelligence uses a
larger unarmored Magic Shield pool and shield recovery.

These are achievable committed-build ceilings, not free level rewards or a
promise that every level-100 character has the same pool. Enemy outgoing damage,
percentage recovery, barriers, leech, and UI formatting must all be calibrated
for five-digit resources. Flat values meaningful at level 1 cannot remain the
primary endgame recovery scale.

### Regeneration, Leech, and Active Healing

Passive regeneration is dependable but deliberately conservative. Typical
late-game in-combat Health or Mana regeneration should remain around
`0.25–0.75%` of maximum per second after ordinary investment. Unconditional
regeneration should not normally exceed `2%` of maximum per second without an
explicit class mechanic and a real opportunity cost. Out-of-combat recovery may
be faster because it does not erase encounter pressure. Missing-resource regen,
recovery multipliers, and overlapping auras still obey the resolved in-combat
budget rather than bypassing it under different names.

Health Leech and Mana Leech are separate percentage-of-damage stats. Qualifying
post-mitigation damage adds recovery to the matching shared reservoir:

```text
effective damage = min(post-mitigation damage, target Health before damage)
generated leech = effective damage × total resource leech percentage

leech paid per second = min(
    stored reservoir,
    missing resource,
    maximum resource × resolved leech-rate cap
)
```

Each resource has its own reservoir and a default payout cap of `10% maximum
resource per second`. More damage or a higher leech percentage fills the
reservoir more reliably but does not exceed that cap. Exceptional class
passives may add to their matching cap; ordinary gear does not. The provisional
global safety ceiling is `25%` per second even after such increases.

The cap must be attainable by a committed build rather than decorative:

```text
leech percentage needed to sustain cap =
    (maximum resource × leech-rate cap) / sustained qualifying DPS
```

At `10,000 maximum Health` and the `45,205 DPS` endgame reference, sustaining
the default `1,000 Health/s` cap requires about `2.21% Health Leech`. Initial
item, talent, and class budgets must therefore let a committed build assemble
roughly `2–3%` total Health Leech. Mana pools are smaller, so useful Mana Leech
rolls are correspondingly lower; their budget is tested from the same formula
rather than copied from Health. A passive that raises the maximum leech rate
must also leave a realistic route to enough generation to use that extra cap.

Leech is combat sustain, not instant healing. Damage over time contributes its
actual ticks. Overkill, self-damage, reflected damage, excluded creatures, and
friendly targets do not inflate it. Minion damage leeches to the owner only
when an explicit minion-leech stat says so. Reservoirs stop paying outside
combat and clear after a short grace period without qualifying damage, so they
cannot be banked between encounters. Leech is displayed separately from passive
regeneration because regeneration modifiers do not secretly scale it.

Active healing skills derive their base restoration from the equipped weapon:

```text
healing base = Weapon Power × healing effectiveness / 100

final healing =
    (healing base + Flat Healing)
    × (1 + summed Additional Healing)
    × product of More Healing
    × target Healing Received
```

Damage modifiers do not scale healing merely because both begin from Weapon
Power. Healing does not critically strike unless an explicit rule enables it.
Active healing is not subject to the leech-rate cap, but every heal has an
authored coefficient, cooldown, resource cost, targeting rule, and opportunity
cost. Overhealing is discarded. Area healing must pay for target count through
lower effectiveness, splitting, cooldown, or resource pressure.

Health healing and Magic Shield restoration are distinct payloads. A skill
restores only the resource it explicitly names; a generic `healing` label never
silently restores both. They use separate effectiveness values, tags, outgoing
modifiers, received modifiers, combat events, and advanced-tooltip breakdowns.
A skill may author both components, but each is listed and balanced separately.
Health Leech never restores Magic Shield, and Mana Leech never restores either.

### Class- and Skill-Specific Resources

Resources belong to particular classes, skills, or build mechanics. Every build is not expected to use every resource, and irrelevant resource bars should not be presented as universal obligations.

Energy is not part of the VRPG resource model. Dexterity's identity comes from evasion, critical access, speed, and Reflex/Evasive Step rather than ownership of a universal resource pool. Mana may support particular classes or skills, while most of its maximum and recovery should come from items, talents, class passives, or other deliberate build choices rather than Intelligence automatically owning all mana scaling. Blood is a game-changing talent mechanic that converts mana costs into blood costs, changing both the risk and sustain model of the affected build.

Resource conversions should be build-defining tradeoffs rather than universally superior upgrades. A conversion must change preparation, recovery, risk, or skill interactions enough to justify its talent cost.

### Respecialization

Players may change classes and rebuild their point allocations. Respecialization should be highly accessible but not completely free.

The intended friction is enough to make a player pause, prepare, or spend a modest resource, while still encouraging experimentation and rebuilding. Respecialization must not require repeating the entire leveling process or impose a grind large enough to make players follow external build guides out of fear of mistakes.

The exact currency, crafting step, cooldown, location requirement, and distinction between class and point respecialization remain open.

### Stat Presentation

The default stat presentation should remain compact and readable. Players should see the information needed to compare choices and understand their build without confronting the entire calculation model at once.

Advanced details should be available through progressive disclosure, such as holding Shift on a tooltip. Expanded information may include calculation layers, source breakdowns, scaling tags, caps, conversion order, and conditional modifiers.

The compact and expanded views must be generated from the same resolved stat data so that advanced tooltips explain actual behavior rather than duplicate hand-written formulas.

## Combat and Enemies

### Combat Pace

VRPG combat should move substantially toward an ARPG pace rather than preserve Vintage Story's deliberate vanilla combat cadence. Combat should be responsive, ability-led, and rewarding enough that players choose to engage instead of routinely bypassing enemies.

The faster pace must remain readable. Enemy telegraphs, status feedback, spawn staging, hit confirmation, resource state, and major ability outcomes should be understandable without filling the screen with effects or requiring the player to parse every underlying calculation.

VRPG should build a coherent combat layer around its own skills and action bar while retaining useful Vintage Story fundamentals such as spatial positioning, terrain, preparation, and weapon identity.

### Cooldown Philosophy

Cooldowns are an accepted part of skill pacing, but most should be short:

- Primary combat abilities will usually have cooldowns in the one-to-five-second range.
- Almost all routinely used abilities should remain below ten seconds.
- Longer-duration buffs, summons, stances, or exceptional high-impact abilities may use different timing where their uptime or effect justifies it.

Cooldowns should not be the only control on skill use. Resources, positioning, targeting, setup-and-payoff conditions, and build interactions should determine when an ability is most effective. A low-cooldown primary skill should feel available and responsive without becoming mechanically empty.

Melee abilities use server-authored skill shapes rather than Vintage Story's
native held-item reach. The initial reusable shapes are a forward arc, a narrow
forward line/thrust, a crosshair-biased single-target strike, and a caster-
centered circle. Each skill states its own range, width or arc, vertical
tolerance, target cap, and solid-block obstruction behavior. Weapon family may
shape affixes and animation identity, but swapping the held vanilla item cannot
silently shorten or extend an RPG skill.

Intentional multi-hit actions remain discrete hits. A three-hit action at
`100% Weapon Damage` is three independently resolved `100%` hits rather than a
single `300%` hit; criticals, ailments, leech, and on-hit triggers therefore
resolve per hit. Timed sequences commit their activation cost and cooldown once
and then execute their authored hit cadence.

Channeled skills begin on hotkey press and repeat on a server-owned cadence
until release, resource failure, death/disconnect, another skill activation, or
an authored maximum duration. Per-second costs are prorated by tick duration.
Channel cooldown begins when the channel ends so tapping and sustaining the
same action do not receive contradictory cooldown behavior. Every channel has
a finite safety duration; packet loss cannot leave an action running forever.
Movement penalties, turn limits, and damage interruption are explicit per-skill
behaviors when introduced, never implicit properties of the word “channel.”

Failed ability feedback must remain useful at ARPG input rates. Cooldown and insufficient-resource chat warnings are independently configurable in Hub → Options and enabled by default. While enabled, warnings are always rate-limited: rapid repeats collapse into one reminder that points to the relevant option, followed by a short quiet period. Other actionable skill failures remain visible but use the same baseline rate limit so an invalid binding cannot flood chat.

### Rust Status Terminology

Rust, Corrosion, and Corruption are separate combat concepts:

- **Rust** is the elemental damage type.
- **Corrosion** is the Rust-derived, poison-like ailment and ongoing
  deterioration state.
- **Corruption** is VRPG's replacement for the generic ARPG curse category. A
  Corruption imposes a specific vulnerability that another part of the build can
  exploit; it is neither a damage type nor a synonym for Corrosion.

This separation supports setup-and-payoff builds without adding another ailment
solely to carry familiar genre terminology. Corruption skills should expose a
target to a named follow-up such as an element, ailment, defense interaction, or
class mechanic. Their compact UI text must state that follow-up directly.

One player may own one Corruption on a target by default; applying another
replaces that player's previous Corruption. A target may carry at most two
different player-owned Corruptions in a party. The strongest duplicate applies,
and additional duplicates refresh rather than stack. Bosses receive `50%` of
ordinary Corruption magnitude unless a skill defines a stricter conversion.
Corruption does not deal damage by category membership and generic “more damage
taken from everything” is not an ordinary Corruption payload. Each skill names
the specific element, ailment, defense, resource, or follow-up it exposes.

### RPG-Eligible Enemies

Ordinary wildlife and passive creatures are excluded from RPG level scaling and RPG experience rewards by default. Farming, breeding, or slaughtering passive animals must not become an optimal progression path.

Enemy eligibility should be explicit through a registry, tag, or content rule rather than inferred from merely having health or being an entity agent. This allows hostile vanilla creatures, rift creatures, bosses, and addon enemies to opt in while protecting ordinary world ecology and NPC-like entities.

### Enemy Rarity and Affixes

Enemy rarity initially provides readable difficulty, raw-stat variation, affix capacity, and improved rewards. The rarity system should make an uncommon enemy worth noticing and choosing how to approach.

Behavior-changing affixes are a stretch goal. When introduced, they should use reusable, telegraphed behavior components rather than bespoke invisible exceptions. Examples may include altered movement, spawned hazards, ally support, reactive defenses, or changed attack patterns.

Purely numerical rarity modifiers should remain bounded so rare enemies do not become ordinary enemies with excessive health. Reward growth must justify the additional danger and time.

### PvP Scope

PvP is a low-priority feature and should not constrain the initial PvE combat, class, or itemization design. The first complete version must avoid crashes, exploits, and unintended cross-player effects, but competitive balance is not an initial acceptance criterion.

Whether RPG statistics and effects apply fully, partially, or through a separate PvP ruleset remains unresolved.

## Gear, Crafting, and Economy

### Equipment Scope

VRPG uses both enhanced vanilla equipment and purpose-built RPG equipment. Vanilla item families should remain recognizable and useful, while VRPG-native items provide room for systems that cannot fit cleanly into vanilla schemas or interaction models.

The two equipment tracks must share a resolved stat and tooltip model so they do not feel like unrelated games or require players to learn two incompatible comparison systems.

### Loot Volume and Item Lifecycle

VRPG intentionally supports high randomized loot volume. A substantial midgame rift may produce approximately ten to thirty items when its duration and difficulty justify that amount.

High loot volume is acceptable only if the complete item lifecycle is fast:

```text
Drop
  ↓
Collect into VRPG-oriented storage
  ↓
Compare through compact, readable summaries
  ↓
Keep promising bases
  ↓
Bulk salvage unwanted items
  ↓
Use recovered value to modify or pursue the next item
```

Custom backpacks or equivalent storage for VRPG items and an efficient salvage workflow are launch requirements, not optional polish. Loot density must not ship before players can quickly collect, triage, and dispose of it. Loot filtering, automatic routing, salvage rules, and backpack limits require detailed specifications.

### Acquisition and Modification

The strongest equipment comes from a mixture of drops and crafting, with traders and deterministic progression supporting rather than replacing those sources.

The common item journey is:

1. Find an item with a useful base and several desired statistics.
2. Keep the valuable identity of that drop.
3. Modify it in bounded ways so it fits the build more closely.
4. Continue hunting for rarer combinations, stronger bases, or build-defining items.

Crafting should improve and adapt good discoveries rather than make all dropped items interchangeable raw material or guarantee a perfect item cheaply.

Critical itemization uses the same principle. Additional Critical Chance may
appear across ordinary gear and several progression systems, but Flat Critical
Chance is primarily discovered on gear or created and improved through bounded
gear upgrades. Because Flat is multiplied by the build's total Additional Crit,
its roll tier and upgrade access must be treated as premium item power rather
than ordinary filler.

### Required Launch Systems

The following systems are all considered necessary to the intended launch itemization model:

- **Fittings:** Active-skill or skill-bearing socket components.
- **Support Fittings:** Components that change how supported skills behave, scale, or pay costs.
- **Etchings:** Socketed modifier components with distinct acquisition and mechanical identities.
- **Assemblies:** Bonuses activated by arranging compatible etchings in a valid item.
- **Augments:** Permanent or semi-permanent modifications layered onto an item.
- **Tender:** Crafting and modification items used to roll, upgrade, seal, cleanse, or otherwise work on RPG gear.

Their exact boundaries and the relationship between class-unlocked skills and skill-bearing Fittings require a dedicated itemization specification. These systems should be reduced or combined if they cannot each create a distinct decision; merely having six names is not sufficient justification.

### Economy

Rusty and temporal gears remain the likely economic foundation. VRPG enemy rewards and selling unwanted VRPG items to traders should become important sources so that active RPG play can support its own crafting economy.

Balance must account for every vanilla and modded source or sink that affects these gears. VRPG must not accidentally trivialize vanilla trade or temporal-stability decisions by flooding the world with an existing currency.

Player trading is supported and should allow valuable or best-in-slot equipment to be transferred freely. Trading remains a small part of the balance model so solo players and small cooperative servers are not punished for lacking a large market.

## Multiplayer and Rewards

All core release content should be soloable. Cooperative groups gain efficiency, build-combination opportunities, greater challenge, and improved loot rather than access to otherwise mandatory content.

Rift difficulty is determined from a combination of:

- Chart level.
- Party size.
- The highest player level in the party.

The system should prevent a high-level player from silently making content impossible for lower-level companions while also preventing low-level party members from reducing a high-level rift's intended danger.

Full shared XP requires both party membership and qualifying contribution. Contribution rules must credit support, healing, control, tanking, and other useful participation rather than measuring damage alone. Exact thresholds, range, death handling, and late-join rules remain open.

Loot is primarily shared. Larger groups face harder enemies and receive better or more numerous rewards, but group scaling must not make item generation so efficient that solo play becomes irrational.

Dedicated support, control, and tank-oriented builds should remain solo-capable. They may clear substantially slower when alone, but they still need a credible damage plan and must not require a second player for ordinary progression.

## Death and Failure

Death should matter enough to encourage upgrading equipment, improving execution, and reconsidering a weak build. It should not be so punishing that combat-focused play becomes unattractive.

Current direction:

- Equipped items should not drop on death.
- Experience loss is the leading candidate for the general death penalty.
- Rift failure may carry its own bounded cost.
- Punishment must not erase large amounts of progression or encourage players to avoid experimentation.

The exact XP-loss rule, recovery mechanic, rift failure cost, and protections against repeated-death spirals remain unresolved.

## Relationship With Vanilla Combat

VRPG exists partly because vanilla combat provides little incentive for sustained engagement. Many enemies can be avoided, combat rewards are limited, recovery after death is often straightforward, and the underlying combat does not provide enough build expression on its own.

VRPG should address this through:

- Enemies and encounters that are worth choosing to fight.
- Rewards that feed meaningful build decisions.
- Skills and status interactions that create tactical options.
- Preparation and equipment that materially change outcomes.
- Challenges that test a build's mechanics rather than only its statistics.

This does not mean every vanilla creature must become an RPG enemy. Passive wildlife, ordinary survival hazards, and uninvolved players should not automatically be pulled into the progression loop.

## Content Production and Technical Boundaries

### Data-First Authoring

Data-only definitions are a primary development tool even before a third-party addon ecosystem exists. The immediate goal is to make first-party content faster, more consistent, and easier to validate. A stable public addon contract is a later benefit rather than the first reason for the architecture.

Invalid content should fail startup with visible, actionable errors. VRPG should aggregate validation failures where possible and report exact assets, fields, and broken references before refusing to load. Quiet partial loading that later produces null-reference failures is unacceptable.

Live reload is primarily a development convenience. Production servers are not guaranteed to apply arbitrary schema or registry changes safely without restart.

### Dependencies and Compatibility

The VRPG core should remain self-contained. Optional modules may integrate with Manifold or other mods without turning those integrations into dependencies of unrelated core systems.

Combat Overhaul compatibility is desirable, but it is not allowed to dictate or destabilize the core combat architecture. Because Combat Overhaul is large and complex, VRPG may implement its own melee system if that produces a clearer, more maintainable result. The eventual compatibility boundary requires a dedicated investigation.

### Questing

A full quest module is planned as a later expansion. The initial systems should expose events, objectives, rewards, and progression hooks that a quest module can consume without embedding quest logic into combat, item, or dungeon definitions.

### Release Content Scope

Exact first-release counts for classes, active skills, talent nodes, enemy affixes, dungeon themes, bosses, rarities, and unique items remain undecided. Release scope should be set after one complete vertical slice establishes the real production cost of executable content.

## Priority Order

When goals conflict, use this current priority order:

| Priority | Goal |
|---:|---|
| 1 | Combat feel |
| 2 | Build depth |
| 3 | Performance |
| 4 | Balance predictability |
| 5 | Vintage Story integration |
| 6 | Loot excitement |
| 7 | Cooperative play |
| 8 | Content-authoring speed |
| 9 | Addon extensibility |

This ordering does not make lower priorities optional. It identifies which result should win when two goals cannot both be fully satisfied.

## Vertical-Slice Validation

The first playable vertical slice must directly test the tension between Vintage Story's slow world progression and VRPG's faster encounter pacing. Success cannot be judged only by whether combat functions technically.

The test should determine whether a player can:

- Reach meaningful VRPG choices without waiting through an excessive vanilla equipment grind.
- Enter fast combat without the mod feeling disconnected from the surrounding world.
- Recognize how class, skill, and item decisions changed the fight.
- Receive enough loot to feel excitement without being buried in inventory work.
- Salvage and refine that loot into a clear next-build decision.
- Return to exploration and crafting with a reason to pursue another rift.

Exact one-hour success criteria remain open until the vertical-slice starting conditions and progression band are chosen.

## Content and System Guardrails

The following rules are accepted design constraints:

1. Every major progression layer must create a decision, interaction, or new play pattern.
2. Player power must not be primarily canceled by matching enemy stat growth.
3. Most content must be opt-in at the player or activity level.
4. Rift enemies must enter encounters through readable spatial staging.
5. Non-combat crafting should support RPG preparation without becoming mandatory upkeep.
6. Exploration and temporal stability must remain meaningful inputs to danger and access.
7. A functioning build should exist well before endgame and continue to gain refinement afterward.
8. Server-wide systems must account for uninvolved players and avoid uncontrolled disruption.
9. Content described by the UI or library must eventually correspond to real, executable gameplay.
10. Lore and terminology must follow the project's existing "legible before flavorful" writing standard.
11. Creature levels may supply most enemy raw power, but player levels must require active point investment to answer that growth.
12. Unspent points should cause a clear power deficit without making one particular allocation mandatory.
13. Every character build is founded on two VRPG classes, and no class should require one predetermined partner.
14. Active-skill ranks should provide modest numerical growth after unlocking the complete action; eight-rank class passives should scale heavily enough to become defining point sinks, while skill combinations provide the largest changes in behavior.
15. A viable build may focus on one primary active skill, but supporting choices must materially change how that skill plays.
16. Frequently activated abilities should remain few and readable rather than forming a large mandatory rotation.
17. Resources must be class- or skill-relevant and should not appear as universal obligations for every build.
18. Respecialization must have modest friction while remaining accessible enough to encourage experimentation.
19. Stats must be compact by default with advanced detail available through progressive disclosure.
20. Combat should target a faster ARPG cadence while preserving readable spatial and effect feedback.
21. Primary abilities should normally use short cooldowns, with resources and interactions providing additional control.
22. Passive wildlife and ordinary animals must be excluded from RPG scaling and experience rewards by default.
23. RPG enemy eligibility must be explicit rather than inferred from the presence of health or an agent type.
24. Enemy rarity must improve rewards and remain more interesting than an excessive health multiplier.
25. Fast encounters and slow world progression must reinforce one another rather than feel like disconnected games.
26. High loot volume must not ship without specialized storage, fast comparison, and efficient bulk salvage.
27. Crafting should adapt valuable drops while preserving the identity and excitement of finding a strong item.
28. Every launch itemization subsystem must create a distinct decision or be combined with another system.
29. VRPG gear income and sinks must be balanced against vanilla trade and temporal-stability uses.
30. All core release activities must be soloable, with groups receiving greater challenge and improved rewards.
31. Multiplayer contribution rules must recognize support, control, healing, and tanking rather than damage alone.
32. Rift layouts must use curated pieces assembled semi-procedurally.
33. Invalid content must fail startup with actionable validation errors instead of loading partially.
34. Every mainable class must support at least two, preferably three, distinct combat loops rather than one finished loop with cosmetic stat variants.
35. Every class style must have a planned survival model that remains functional against bosses; range, healing, or permanent crowd control alone is insufficient.
36. Intended class styles are non-exclusive proof cases; mechanically sound unconventional and cross-style builds must remain supported.
37. Talent-tree content should reuse broad mechanical combo packages across class styles; class-named duplicate nodes and hidden style membership checks are not valid specialization.
38. Damage gamechangers should transform broad hit, ailment, critical, placement, payoff, or targeting rules across eligible damage types instead of being duplicated once per element or class.
39. Core RPG systems must remain self-contained behind optional integration boundaries.
40. Reincarnation must remain optional and preserve retained gear behind its normal level requirements.
41. Rust is a damage type, Corrosion is its poison-like ailment, and Corruption is the curse-like vulnerability category; content and UI must not use these terms interchangeably.
42. Initial Legacy Rank rewards should improve VRPG loot quality with diminishing returns rather than directly multiply combat power.
43. Permanent legacy bonuses must not matter more than the player's current build and point allocation.
44. Any future class-specific legacy reward must use recorded class journey history rather than classes equipped at reincarnation.
45. The passive tree begins from six mutually exclusive raw-stat starts arranged as a wide hexagon near its center; the starts cover all six ordered dominant-secondary STR/DEX/INT pairings, are equal in budget, target roughly twice nearby Tier-2 value, never link directly, and ordinary progression never requires a gamechanger.
46. The passive tree forms a mostly symmetrical six-sector radial spiderweb with one continuous sparse perimeter express ring; paired inner paths and cross-rungs extend into outward lanes with separate perimeter entrances rather than forming a redundant second circle or giant direct road chords. Perimeter-to-perimeter steps are deliberately the longest ordinary links in the tree, and every non-perimeter link must remain shorter. Any ordinary node is reachable from any chosen start, corner-to-corner edge travel is cheaper than dense inner travel, and the 42-node perimeter consists only of small Tier-1 STR/DEX/INT packages so its travel is productive breadth rather than focused specialization. Seven ring nodes belong to each sector. Terminal specialization pods may extend beyond the express ring, which is a travel circuit rather than the tree's hard visual boundary.
47. The full passive tree targets roughly 400 meaningful nodes; a normal complete build has about 100 spendable talent points and late-game progression may reach about 125, limiting characters to roughly one quarter or one third of the tree.
48. Small raw-attribute nodes form readable major roads, junction forks, and optional inner spiderwebs, while focused mechanics occupy recognizable specialization pods branching from several inner, middle, and outer web depths. Each inner gap offers at least two ordinary paths plus cross-rungs that let players change routes; removing any one internal web node must leave an ordinary route between its junctions. Ordinary pods use sparse split/rejoin choices, bounded internal link counts, and at least `1.8D` node spacing rather than compressed stat knots. Dense regions must still expose traceable roads instead of becoming either a straight stat ladder or an undifferentiated mesh.
49. Rule-changing gamechangers are optional one-edge leaves scattered from ordinary interior road or branch nodes, usually the 10th–20th allocation from the matching start when counting both the selected start and the gamechanger. They do not cap ordinary Tier-1/Tier-2 pods, never provide transit, and their attachment nodes remain at least three ordinary graph links apart; spatial separation beyond avoiding contact is not a balance tool because their massive tradeoffs should discourage collecting every gamechanger.
50. Tree geometry is generated deterministically from a checked-in authoring manifest and reusable topology templates, then polished through persistent per-node manual offsets; generated coordinates remain stable, the workbench exports overrides, and runtime positions are never procedurally randomized. Generation rejects unrelated edge crossings, overlapping nodes, and unrelated links entering a node's protected socket radius.
51. Saved authored talent trees and built-in reset templates are separate concepts: templates replace only an admin's private draft, while Save or Save As New creates the server revision and synchronizes players.
52. Multiplayer clients render the active server-authored talent tree from a standalone versioned snapshot; custom world trees do not require distributing generated asset files. Admin edits remain draft-only until Save. A changed Save creates one revision, reconciles allocations, refreshes derived player state, and broadcasts once; an unchanged Save does nothing.
53. Player talent changes use plan-and-commit interaction: starting-route selection is free, all starts are selectable before one is queued, left-click queues connected affordable allocations, right-click queues refunds that preserve final-tree connectivity and consume one respec point per refunded node, and Apply submits the complete plan for atomic server validation. Individual clicks never mutate authoritative progression.
54. Every learned class skill works at its complete authored effect with every equipped weapon. Weapon families establish identity through favored affix pools and weights rather than skill locks or hidden effectiveness penalties, and off-pattern rolls or modification must leave room for unconventional builds.
55. Each class passive has eight ranks and costs one skill point per rank. A two-class character is balanced around maximizing only a few defining passives, partially investing in selected support passives, and leaving several passives untouched rather than completing either class sheet.
56. Default critical chance is 5%. Flat Critical Chance is a rare gear and gear-upgrade property that increases the base; ordinary class passives and talent nodes grant the much more common Additional Critical Chance, whose sources sum before multiplying that base. More Critical Chance is a separate exceptional multiplier. Crit investment must be distributed across class, talent, gear, affix, and Fitting layers rather than concentrated in one mandatory source.
57. Every damaging attack, spell, ailment, and construct action derives its base damage from the equipped weapon's server-authored VRPG Weapon Power using an explicit Weapon Damage effectiveness percentage. Weapon-agnostic skills accept every weapon family; they do not ignore weapon progression.
58. Weapon Power resolves in the order minimum-level baseline, affixes, then bounded rarity power scalar. The initial level curve is compounding at 3.5% per required level and the initial Common/Rare/Unique power scalars are 1.00/1.15/1.20. These are balance seeds; the binding outcome is that a comparable weapon twenty levels below a top-end encounter falls outside the intended clear-time budget without receiving any explicit staleness penalty.
59. Native Vintage Story attack power remains separate from VRPG Weapon Power. Non-RPG targets retain native combat, RPG-eligible targets resolve ordinary attacks through the VRPG 100% Weapon Damage rule, and an unmodified vanilla weapon has an explicit level-1 VRPG fallback rather than inheriting current player or target level.
60. Creature health growth must increasingly outpace the current-level plain-weapon baseline. The initial ordinary-monster build-pressure targets are approximately 1× at level 1, 3.6× near level 40, 6× near level 60, 9× near level 80, and 13.7× at level 100. A level-100 character with a plain level-100 weapon, default critical stats, and no skill or talent investment defeating representative level-100 content is a hard balance failure.
61. Every solo late-game damage build must establish dependable repeated critical or damaging-ailment throughput. A current weapon plus neutral damage scaling with neither engine defeating representative late-game Health is a hard balance failure; critical and ailment access must remain broad enough that this does not prescribe one class pairing.
62. A committed level-100 Health build with strong gear may exceed 10,000 maximum Health, while a committed unarmored Magic Shield build may exceed 20,000 maximum Magic Shield. These are build ceilings rather than automatic level grants, and enemy damage plus recovery systems must be authored for five-digit resource pools.
63. Player damage and enemy Health scale much faster than player Health. Level-100 bosses around two million Health should be common enough to establish an ordinary endgame damage test, with higher rarity, phases, party scaling, and chart modifiers able to exceed it substantially. Boss balance uses sustained engine output and uptime rather than raw single-hit screenshots.
64. Health and Mana Leech convert actual qualifying damage into separate recovery reservoirs. Each pays at no more than 10% of its maximum resource per second by default; exceptional class passives may increase the matching cap, while ordinary gear may only improve reservoir generation. Leech is never instant and cannot be banked between encounters.
65. Passive in-combat regeneration remains conservative: ordinary late-game investment targets roughly 0.25–0.75% maximum resource per second and does not normally exceed 2% without a class mechanic and opportunity cost. Out-of-combat recovery may use a separate faster budget.
66. Active healing skills scale from explicit Weapon Power healing-effectiveness percentages and separate healing modifiers. Damage modifiers do not double as healing modifiers, heals do not crit by default, and authored cooldown/resource/target-count costs—not the leech cap—bound active healing.
67. Corruption is a named specific vulnerability with one owned Corruption per player and at most two different player-owned Corruptions on a party target. Duplicates do not stack and bosses receive half ordinary magnitude by default.
68. The default 10% leech-rate cap must be realistically attainable by a committed endgame build. At the 10k-Health/45,205-DPS reference, approximately 2.21% Health Leech fills the cap, so roughly 2–3% total Health Leech must be obtainable across item, talent, and class sources without becoming a free incidental stat.
69. Health healing and Magic Shield restoration are separate explicit payloads and stat families. A healing skill affects only its named resource unless it authors and displays two independently balanced components; Health Leech, Mana Leech, healing modifiers, and shield-restoration modifiers never cross resources implicitly.
70. RPG melee reach and hit geometry are authored by the skill and resolved server-side. Forward arc, forward line, precise single-target, and caster-circle skills never inherit Vintage Story's native weapon interaction range; solid terrain blocks forward melee hits.
71. Intentional multi-hit skills resolve each authored hit independently for criticals, ailments, leech, on-hit effects, and target validity. They cannot be represented as one summed damage coefficient merely because the tooltip also shows total sequence effectiveness.
72. Channeled skills use held press/release input but server-owned tick timing, cost, damage, maximum duration, and cooldown. Release, resource failure, death/disconnect, another skill activation, or maximum duration ends the channel, and its cooldown starts at that boundary.

## Supporting Design References

- [Class candidate pool](class-candidates.md)
- [First six class skill specification](first-six-class-skill-spec.md)
- [Damage scaling tool](../damage-scaling-tool.md)
- [Testable version live TODO](testable-version-todo.md)
- [Talent tree authoring rules](talent-tree-authoring-rules.md)
- [UI authoring guide](../ui-authoring-guide.md)
- [Gate A UI screen contracts](ui-screen-contracts.md)
- [Stat conversion and layer notes](../mine-and-slash-stat-conversion.md)
- [Generated library design](../generated-library.md)
- [Writing style guide](../writing/style-guide.md)
- [Lore references](../writing/lore/)

## Decision Register

This register is the working queue for unresolved high-level decisions. Decisions should normally be handled in dependency order. When a decision is resolved, its result should be incorporated into the relevant design section and its status changed here rather than deleting the record.

Statuses:

- **Open:** Requires a product or design decision.
- **Direction set:** The governing direction is known, but a detailed specification or balance pass is still required.
- **Deferred:** Intentionally outside the first complete release or vertical slice.

### Phase A: Vertical-Slice Decisions

These decisions define what must be built to prove the core game.

| ID | Status | Decision to make | Current constraints | Unlocks |
|---|---|---|---|---|
| VS-01 | Open | What starting level, equipment state, session length, and exact player outcomes define the first vertical slice? | It must test fast combat against Vintage Story's slow world pace and produce a meaningful next-build decision within the session. | Concrete vertical-slice scope and acceptance test. |
| VS-02 | Open | At what levels and through what process are the first and second VRPG classes selected? | Every final build uses two classes; the first meaningful build decision should occur by roughly level 10. | Onboarding, class UI, and early progression flow. |
| VS-03 | Direction set | How frequently are skill points awarded and what prerequisites gate unlocks? | Active skills have ten ranks with modest post-unlock growth. Class passives have eight heavily scaling ranks; nine passives create 72 possible points per class and the final two-class budget must leave several unpurchased. Exact point cadence and unlock prerequisites remain open. | Skill-tree schema and initial class-kit budgets. |
| VS-04 | Direction set | What are the initial player-versus-creature power budgets at representative levels? | The current weapon is only a maintenance floor. Ordinary health-to-weapon build pressure reaches about 13.7× at level 100. Late offense requires repeated crit or damaging-ailment throughput. Committed Health and Magic Shield builds may exceed 10k and 20k respectively. Exact viable-build damage, defense, and TTK bands remain open. | Final stat resolver targets, XP pacing, and combat simulation tests. |
| VS-05 | Direction set | What targeting, hit detection, weapon interaction, interruption, and movement rules define VRPG combat? | Weapon interaction and initial melee geometry are resolved: every damaging action uses server-authored Weapon Power; melee uses server-authored arc, line, single, or circle shapes rather than vanilla reach; sequences and held channels use server timing. Per-skill interruption, movement, animation, and advanced target-lock rules remain open. | Remaining combat runtime architecture and playable skill prototypes. |
| VS-06 | Direction set | Which resources belong to the first playable classes, and how are they spent and recovered? | Energy is removed. Resources are class- or skill-specific; irrelevant bars should not appear; Blood is a later mana-cost conversion talent. Exact Mana users and recovery remain open. | Resource runtime, HUD behavior, and skill costs. |
| VS-07 | Open | How does a player become defensively ready for the first rift without waiting through excessive vanilla armor progression? | Early RPG readiness cannot trivialize normal material progression or make crafting mandatory upkeep. | First-rift entry point and world-to-rift pacing bridge. |
| VS-08 | Open | What is the minimum executable content set for the vertical slice? | It should be small enough to finish but large enough to demonstrate class interaction, loot refinement, a boss, and a repeat decision. | Production plan and milestone boundary. |
| VS-09 | Direction set | How do core attributes activate defensive foundations? | The highest allocated stat is Primary; starting affinity breaks ties; Dexterity-primary activates Reflex and Evasive Step from early progression. The tree has six central starting routes covering every ordered dominant-secondary core-stat pairing. Strength and Intelligence still require equivalent executable defensive rules. | Six-route schema, affinity mapping, defensive UI, and early Dexterity viability. |
| VS-10 | Direction set | What are Corruptions allowed to do, and how many can affect one target? | A player owns one at a time; a party target carries at most two distinct player-owned Corruptions; duplicates refresh; bosses receive half magnitude. Exact durations and individual payload budgets remain content decisions. | Status-effect schema, class skill hooks, boss rules, and multiplayer debuff behavior. |

### Phase B: Itemization and Economy Decisions

These decisions turn high loot volume into a sustainable build loop.

| ID | Status | Decision to make | Current constraints | Unlocks |
|---|---|---|---|---|
| IT-01 | Open | What unique decision does each launch system provide: Fittings, Support Fittings, Etchings, Assemblies, Augments, and Tender? | All six are intended for launch, but systems without distinct decisions should be combined rather than retained by name alone. | Itemization architecture and content schemas. |
| IT-02 | Open | How do class-unlocked active skills interact with active-skill or skill-bearing Fittings? | Class combinations are the build foundation; Fittings cannot make classes irrelevant or create a contradictory acquisition model. | Skill/item boundary and socket rules. |
| IT-03 | Direction set | How are bases, rarities, affixes, level requirements, unique items, and drop weighting generated? | Weapon Power order and initial level/rarity curves are fixed as seeds. Exact affix budgets, source-level assignment, unique overrides, armor bases, and drop weighting remain open. | Executable loot generator and full item balance simulator. |
| IT-04 | Open | What collection, specialized storage, comparison, loot-filter, and bulk-salvage workflow supports ten to thirty items per substantial rift? | High loot volume cannot ship before fast triage and disposal exist. | Backpack, inventory, tooltip, and salvage specifications. |
| IT-05 | Open | Which crafting operations may correct an item, and what prevents cheap deterministic perfect gear? | Crafting should refine valuable drops rather than make drops interchangeable. | Tender operations, costs, and crafting UI. |
| IT-06 | Open | How many rusty and temporal gears enter and leave the economy through VRPG combat, salvage, crafting, and traders? | RPG play should sustain its own item economy without trivializing vanilla trade or stability uses. | Drop tables, trader integration, and economic test targets. |
| IT-07 | Direction set | How freely may players trade RPG items? | Valuable and best-in-slot gear should be transferable; the game must remain balanced for solo and small-coop play without a large market. | Binding rules and multiplayer item transfer behavior. |

### Phase C: Rift and Multiplayer Decisions

These decisions define repeatable endgame activities and cooperative scaling.

| ID | Status | Decision to make | Current constraints | Unlocks |
|---|---|---|---|---|
| RF-01 | Open | How are Rift Charts acquired, leveled, modified, traded, and consumed? | Manifold dimensions are primary; overworld breaches are fallback; chart progression must connect exploration and crafting to combat. | Rift access loop and chart item schema. |
| RF-02 | Open | What duration bands, room counts, objective cadence, and maximum party size should rifts support? | All release rifts must be soloable and use curated pieces assembled semi-procedurally. | Generator budgets, encounter pacing, and server limits. |
| RF-03 | Open | What boss structure ends elimination rifts, and what escalation or climax ends survival rifts? | Elimination ends with a boss; horde enemies require readable spatial staging. | Encounter director and completion rules. |
| RF-04 | Open | What exactly happens to charts and accumulated rewards after solo and group failure? | Solo failure should partially refund and downgrade; group behavior needs a separate anti-exploit rule. | Failure flow, extraction rules, and retry economy. |
| RF-05 | Open | Which risks or modifiers distinguish higher rifts beyond enemy statistics? | Rifts must test builds rather than merely repeat enemies with more health and damage. | Modifier pools and endgame variety. |
| MP-01 | Open | How do chart level, party size, and highest player level combine into enemy difficulty and reward scaling? | Groups face harder enemies and receive more loot; lower-level members must not trivialize or be silently crushed by scaling. | Party scaling formula and encounter simulation. |
| MP-02 | Open | What counts as qualifying contribution for shared XP? | Full XP splitting requires party membership and contribution; damage, support, healing, control, and tanking must all count. | Contribution tracker and XP award rules. |
| MP-03 | Open | How is primarily shared loot claimed, distributed, and protected from disputes? | Trading remains allowed and group play must not make solo loot acquisition irrational. | Loot ownership and pickup rules. |
| MP-04 | Open | Whose Legacy Rank affects shared rift loot? | Legacy bonuses are account-level and use diminishing returns; shared loot must not invite party composition exploits. | Reincarnation integration with party rewards. |

### Phase D: Progression and Long-Term Decisions

These decisions govern failure, rebuilding, persistence, and repeated lives.

| ID | Status | Decision to make | Current constraints | Unlocks |
|---|---|---|---|---|
| PR-01 | Open | What XP is lost on ordinary death, what protections prevent repeated-death spirals, and can lost XP be recovered? | Items should not drop; death should provoke build improvement without discouraging experimentation. | Death handler and progression-risk tuning. |
| PR-02 | Open | What exact costs and restrictions distinguish skill respec, stat respec, and class replacement? | Rebuilding should be accessible with modest friction; reincarnation remains a separate commitment. | Respec economy and UI. |
| PR-03 | Open | Is account-bound progression scoped globally, per server, per world, or by a configurable combination? | Reincarnation, Legacy Rank, gear retention, and multiplayer identity all depend on this boundary. | Persistence schema and migration strategy. |
| PR-04 | Open | What persists through reincarnation besides equipment, storage, and general account unlocks? | Retained items keep their normal level requirements. | Reset transaction and persistence contract. |
| PR-05 | Open | What diminishing-return curve converts Legacy Rank into loot rarity, and what practical advantage is acceptable? | It should shift VRPG loot quality, not quantity or direct combat power, and must not outweigh the current build. | Initial reincarnation reward implementation. |
| PR-06 | Deferred | Will class journey progress eventually grant class-specific past-life rewards? | History must be recorded from eligible level-normalized XP before rewards ship; current Legacy Rank is class-agnostic. | Future class legacy specification. |
| PR-07 | Open | Which non-combat activities provide preparation or temporary combat advantages, and how is mandatory upkeep prevented? | Combat supplies most XP; exploration and non-combat crafting must remain valuable. | Crafting-to-combat integration and buff rules. |

### Phase E: Release and Technical Decisions

These decisions establish production scope and compatibility guarantees.

| ID | Status | Decision to make | Current constraints | Unlocks |
|---|---|---|---|---|
| RL-01 | Open | How many classes, active skills, talents, affixes, themes, bosses, rarities, and unique items constitute the first complete release? | Set after the vertical slice reveals the production cost of executable content. | Release roadmap and content budget. |
| RL-02 | Open | What client, server, network, save, and world-generation performance budgets are acceptance requirements? | Performance is priority three; small co-op is primary, but solo and larger servers should remain supported where practical. | Benchmarks, profiling gates, and scalability limits. |
| RL-03 | Open | Which configuration choices are supported without invalidating balance or saves? | Most content should remain optional and server-safe; production live reload is not guaranteed. | Config schema and compatibility policy. |
| RL-04 | Open | Does VRPG ship custom melee, integrate with Combat Overhaul, or support a bounded compatibility mode? | Core combat must remain self-contained and maintainable; Combat Overhaul compatibility is desirable but low authority over VRPG architecture. | Melee implementation plan and compatibility matrix. |
| RL-05 | Deferred | What is the scope and integration contract of the future quest module? | Combat, item, and dungeon systems should expose events and rewards without embedding quest logic. | Quest roadmap. |
| RL-06 | Deferred | What RPG-stat and status-effect rules apply to PvP? | PvP is low priority and should not constrain initial PvE balance. | PvP compatibility and balance policy. |

### Recommended Working Order

Resolve the next decisions in this order unless implementation evidence changes a dependency:

1. `VS-01` — vertical-slice starting state and success criteria.
2. `VS-02` and `VS-03` — class-selection and skill-point progression.
3. `VS-05` and `VS-06` — combat contract and first resources.
4. `VS-04` — initial player/enemy power budgets using the playable combat prototype.
5. `IT-01` and `IT-02` — launch item-system boundaries and their relationship to class skills.
6. `VS-08` — freeze the actual vertical-slice content set.
7. `VS-07`, `RF-01`, and `RF-02` — connect world preparation to the first rift.
8. `IT-03` through `IT-06` — complete the loot, salvage, crafting, and economy loop.
9. Remaining rift, multiplayer, reincarnation, release, and compatibility decisions.
