# VRPG Gear Foundation and Progression Design

Status: Approved design for implementation planning
Date: 2026-09-05
Parents: [Complete game design](../../design/complete-game-design.md) · [Inventory, loot, and storage](../../design/inventory-loot-and-storage.md) · [Character creation and onboarding](../../design/character-creation-and-onboarding.md)

## 1. Purpose and implementation boundary

VRPG needs real RPG equipment before the first-run transaction can grant starter
items. A Vanilla `ItemStack` with a preview name is not a final starter item.
The first implementation pass establishes permanent VRPG gear identity, level
scaling, defensive stats, equipment effects, persistence, and deterministic
starter sets. It deliberately does not make random equipment obtainable yet.

Random gear generation is part of this design because exciting loot is central
to the game. It follows the deterministic gear/runtime proof and precedes live
spawn, encounter, and combat reward wiring. Until that follow-up slice lands,
the only obtainable VRPG gear is the idempotent first-join starter set; test and
administrative generation must not be presented as a normal acquisition route.

## 2. Adopted progression philosophy

Combat stays fast, hectic, and agency-driven for a maintained build. Ordinary
enemies are not made into universal damage sponges. Instead, the minimum build
complexity rises every twenty levels. A player who does not keep up becomes too
slow, fragile, resource-starved, or poorly controlled and is likely overwhelmed.
Fast high-level enemies are a natural stat and build check because they cannot be
kited indefinitely.

The five progression bands are:

1. **Levels 1–20 — Fundamentals.** A current weapon and a basic damaging skill
   are sufficient for ordinary enemies. Crit, ailments, optimized talents, and
   high-quality gear are optional.
2. **Levels 21–40 — First specialization.** A real damage skill, current gear,
   and the beginning of a coherent offensive or defensive plan are expected.
3. **Levels 41–60 — Build formation.** Decent gear plus a partial interaction
   such as crit, an ailment, a combo, sustain, or talent specialization is
   expected.
4. **Levels 61–80 — Synergy check.** Skills, talents, gear, and defenses must
   reinforce one another. Basic attacks alone are dangerously slow.
5. **Levels 81–100 — Complete build check.** A good build and good current gear
   retain the fast ordinary-enemy pace; a neglected build is expected to lose
   the damage and survival race.

For a good build, a level-matched ordinary enemy should usually die in about
two to four seconds. A justified 500–600% Weapon Damage finisher may kill one
outright. One button's coefficient is never the balance unit: multihit timing,
cooldowns, charges, setup, resource cost, and the rest of the rotation determine
effective Weapon Damage per second.

The existing enemy Health and rarity curves remain the baseline. With a
current-level Common weapon, one activation per second, and default critical
stats, the current simulator produces the intended ramp: a 100% action moves
from roughly two seconds at level 1 to roughly twenty-seven seconds at level
100, while a raw 600% action takes roughly four and a half seconds at level 100
before build multipliers. A good level-100 build should sustain approximately
700–1,000% effective Weapon Damage per second against an ordinary target.

## 3. Three primary power pillars

Late-game power above the maintained level baseline comes from three practical
pillars. “One third” describes their importance to the outcome, not three
literal additive 33% modifiers.

- **Skill engine:** skill choice, rotation, setup and payoff, ailments, resource
  use, cooldown coverage, and mechanical synergy.
- **Talent tree:** major specialization, conversion, amplification, and
  build-defining rules.
- **Gear quality:** useful bases, affix relevance, roll quality, rarity,
  defensive specialization, and build-enabling properties.

Keeping a weapon's and armor's required level current is maintenance, not the
whole gear-quality pillar. Deep investment in one pillar cannot fully replace
neglect of the other two. A representative level-100 balance seed is a roughly
400% Weapon Damage-per-second skill engine multiplied by about 1.5 from coherent
talents and about 1.5 from good gear.

The player's two chosen classes affect both offense and defense. Every class
has at least one accessible defensive or sustain tool and may offer deeper
optional defensive synergies. Matching classes can reinforce one defense;
complementary classes can cover weaknesses or transform how another defense
works. Classes may heal Health, restore Magic Shield, temporarily raise Armor
or resistance, grant Evasion or displacement, or reduce pressure through
control. They never exclusively own a defensive layer.

## 4. Gear identity and visual chassis

Every RPG item has a stable VRPG gear-base definition. The base owns:

- stable code and display identity;
- equipment role, weapon or armor family, and compatibility tags;
- base combat profile and implicit properties;
- compatible affix families;
- a separate Vintage Story collectible used as its visual chassis;
- optional emissive parts, glow, smoke, particles, trails, and other rarity or
  unique presentation;
- an optional original shape when no suitable existing silhouette exists.

Existing Vintage Story shapes are the initial visual library. Their native
damage, durability, armor values, identity, and inventory ownership do not
become VRPG combat data. New shapes may be scripted through the configured
modeler fork when the existing library has no suitable alternative.

Changing a chassis or adding an original model never changes the saved item.
If a chassis is missing, the item uses a safe fallback appearance or enters an
explicit recoverable state; it is never deleted.

## 5. Permanent gear instance contract

The server authors a gear instance once. Its canonical payload stores:

- gear schema and generation-rule versions;
- VRPG gear-base code;
- required level from 1 through 100;
- rarity code;
- exact affix identities, tiers, operations, and rolled values;
- resolved base Weapon Power, Armor, and other base defenses;
- source and provenance tags needed for display or future bounded modification;
- visual-chassis reference separately from mechanical identity;
- authored unique identity and rule payload when applicable.

Entry identity and equipment location remain owned by the canonical player item
store. The client receives bounded immutable summaries and never authors or
recalculates combat values. Existing items do not silently reroll when the mod
updates. Balance changes affect newly generated gear unless an explicit,
versioned migration is designed and validated.

One required-level field is both the base-scaling level and affix-tier ceiling.
The first version does not add a hidden item level or player-facing gear score.
The UI presents the properties that change an equip decision: requirement,
Weapon Power or defensive profile, rarity, major affixes, transformations, and
the relevant comparison with equipped gear.

## 6. Weapon Power

The recovered original VRPG contract remains authoritative:

```text
level base = 10 × 1.035 ^ (required level - 1)

affixed power =
    (level base + Flat Weapon Damage)
    × (1 + summed Additional Weapon Damage)
    × product(1 + More Weapon Damage)

final Weapon Power = affixed power × rarity power scalar

skill base hit =
    final Weapon Power × skill Weapon Damage effectiveness / 100
```

Initial rarity power seeds remain Common `1.00`, Rare `1.15`, and Unique
`1.20`. Required level is permanent item data. There is no target-level check or
staleness debuff after generation. An exceptional item may bridge roughly one
twenty-level band; it should not comfortably bridge two.

Native Vintage Story attack power remains separate. Non-RPG targets retain
native combat. RPG-eligible targets use server-authored VRPG Weapon Power for
ordinary attacks and skills. An unmodified Vanilla weapon has an explicit
level-one fallback and never inherits the player's or target's level.

## 7. Defensive gear and specialization

The five defensive axes remain distinct:

- **Health:** pool size, healing, regeneration, and leech.
- **Magic Shield:** a separate pool with damage-free recharge behavior.
- **Armor:** physical mitigation.
- **Evasion:** prevention of eligible attacks.
- **Elemental Resistance:** mitigation of matching elemental damage.

The server resolves defenses in this order:

```text
eligible hit and Evasion check
→ Armor or matching Elemental Resistance
→ other reduction and suppression layers
→ Magic Shield absorption
→ Health damage
```

Armor uses a rating rather than flat subtraction or a permanent percentage:

```text
level armor base = authored level-one rating × 1.08 ^ (required level - 1)
physical mitigation = Armor / (Armor + level-scaled attack pressure)
```

The pressure curve is data-defined and scales with the attacker level. Initial
balance seeds use `100` total level-one Armor across a complete armor-focused
Common set and `150` level-one attack pressure. Both scale at 8% per level, so a
maintained set mitigates 40% of eligible physical damage. Tripling the maintained
set's rating through bases, affixes, talents, and temporary skills reaches about
67% mitigation. The existing 90% hard layer cap is never exceeded. These are
tuning seeds, not a new architecture: balance sweeps may adjust the two base
constants while preserving rating-based mitigation and the maintained/specialist
targets. Around 8% rating growth per level makes comparable level-ten armor
about twice the rating of level-one armor, so the coordinated starter set is
quickly replaceable.

Elemental resistance remains separate and observes the existing 50% ordinary
cap unless an explicit maximum-resistance modifier changes it. Health, Magic
Shield, Armor, Evasion, and resistance compete for base implicits, affixes, and
talent investment. Specialization is created entirely through opportunity cost:
there are no class locks or explicit penalties for hybrid defenses.

Persistent Evasion is initially capped at 60%. Temporary class skills may raise
the cap to 75%; a named guaranteed evade, Reflex, or Evasive Step is a separate
cooldown-governed response rather than a hidden chance above the cap. The
server owns hit eligibility and Evasion outcomes. The recovered late-game
stress seeds remain ceilings rather than automatic grants: a committed Health
build may exceed 10,000 Health and a committed largely unarmored Shield build
may exceed 20,000 Magic Shield. The defensive simulator must validate actual
survival windows before those ceilings become content targets.

## 8. Starter equipment

Every selectable starter class receives:

- one level-one Common VRPG weapon;
- Common head, body, legs, hands, and feet equipment;
- an offhand only when it is central to that starter's combat identity;
- a coordinated class silhouette built from available chassis shapes and
  restrained presentation effects;
- real VRPG stats and ordinary canonical records;
- the starter skill and one ordinary refundable skill point handled by the
  onboarding transaction.

Starter pieces are ordinary gear. They are not bound, economically disabled,
or protected by special permanent rules. Their low required level and Common
quality make them naturally worthless after early progression. Their visual
coordination has no mechanical set bonus, so replacing one piece never breaks a
mandatory effect.

The deterministic starter generator uses the same instance-authoring boundary
as future random loot. The first-run service commits the complete set,
equipment references, skill point, and initial allocation in one idempotent
server transaction after the per-player item store is ready.

## 9. Rarity and affixes

The initial gear system has three data-defined rarities:

- **Common:** readable foundations with zero or one affix.
- **Rare:** several build-relevant properties, initially three to five affixes.
- **Unique:** an authored transformation that changes a rule, interaction, or
  combat verb, with bounded supporting properties. Unique is not merely Rare
  with larger numbers.

Definitions remain extensible so an intermediate or special-purpose rarity can
be added without changing saved-item structure. Rarity controls complexity and
potential, not guaranteed superiority. A relevant Common or Rare item may beat
an irrelevant Unique.

Affix selection enforces base tags, slot compatibility, required-level tier,
mutual-exclusion groups, one-of-a-kind rules, and duplicate prevention. Exact
rolls are permanent. Affix and rarity budgets are bounded so they cannot erase
the twenty-level replacement expectation.

## 10. Random gear generation and soft weighting

The random generator is deterministic for a supplied seed and server-owned:

```text
loot context
→ eligible gear bases
→ required level
→ rarity
→ compatible affix budget
→ exact affixes and rolls
→ resolved combat values
→ canonical item
```

The loot context can include source and encounter level, region, element,
enemy rarity, boss identity, optional objectives, expedition modifiers, party
composition, and reward-quality modifiers.

Soft smart weighting raises the probability of bases and affixes relevant to
participating characters' classes, skills, attributes, and defensive
specializations. It never guarantees an upgrade, removes off-build outcomes,
or prevents transferable items from dropping. Valuable and best-in-slot gear
remains tradeable. Party rewards should increase through encounter density,
behavior, rarity opportunities, experience, and loot—not only enemy Health.

The first gear pass implements the generator interface and deterministic
starter path. The second pass implements random selection and statistical
validation. Live acquisition remains disabled until later spawn and encounter
reward systems call the generator through an explicit loot source.

## 11. Home and salvage loop

Home is a casual creative sanctuary, not a fourth mandatory combat pillar or a
wannabe Survival loop. It provides superior building tools, aesthetic
expression, trophies, collections, NPCs, optional preparation, and ways to use
salvage recovered from hostile realms.

The long-term loop is:

```text
prepare at Home
→ enter a hostile region or expedition
→ survive and recover gear and salvage
→ improve and personalize Home
→ target harder expeditions
```

Home development primarily unlocks materials, architecture, decoration,
trophies, services, targeting options, and optional consumables. It does not
require continual feeding, repairs, rent, crop tending, invasion defense, or
routine buff maintenance. Regional preparations and trader consumables provide
minor situational advantages rather than becoming a mandatory fourth share of
character power.

## 12. Failure and recovery

- Generation validates every base, rarity, affix, roll, role, and chassis
  reference before committing an item.
- Starter generation is all-or-nothing with the onboarding transaction; retries
  return the original result rather than duplicating gear.
- Equip and unequip operations reference canonical entry IDs and cannot copy an
  item into two ownership locations.
- A missing definition, unsupported operation, full store, persistence failure,
  or interrupted transaction retains the source and produces an explicit
  rejection or recovery record.
- Missing visual content never destroys mechanical data.
- Clients may preview resolved values but cannot choose a seed, roll an affix,
  bypass a requirement, or submit trusted combat values.

## 13. Verification

The implementation must prove:

- exact gear-instance and equipment round trips through save/load, reconnect,
  server restart, death, and migration;
- deterministic starter sets for every selectable starter class;
- one atomic starter/equipment/skill transaction under retry, interruption,
  insufficient capacity, and persistence failure;
- correct Weapon Power order and representative level 1, 20, 40, 60, 80, and
  100 outputs;
- Armor rating growth, caps, old-item falloff, and interaction with the other
  four defensive axes;
- authoritative equipment effects and removal without stale modifiers;
- affix compatibility, tier ceilings, exclusions, and exact-roll persistence;
- missing base, rarity, affix, chassis, and addon recovery behavior;
- large seeded random-generation sweeps with stable distributions, no invalid
  items, and no weighting rule that turns into a guaranteed upgrade;
- ordinary-enemy two-to-four-second targets for good builds and clear failure
  of neglected late-game builds in the damage and survival race;
- bounded item summaries and comparisons that never require a gear-score proxy.

## 14. Delivery order

1. Add the gear schemas, base definitions, validators, math, and deterministic
   generator boundary.
2. Add the initial visual-chassis catalog and complete level-one starter sets.
3. Persist gear through the canonical store and apply server-authoritative
   equipment effects.
4. Commit starter gear, equipment, skill point, and allocation atomically.
5. Expose the resulting real Gear and Equipment state through the bounded menu
   protocol and finish the themed UI milestone.
6. Implement seeded random base/rarity/affix generation, soft smart weighting,
   statistical tests, and balance sweeps.
7. Only then wire acquisition into spawned enemies, encounters, bosses,
   expeditions, salvage, traders, and Home services.
