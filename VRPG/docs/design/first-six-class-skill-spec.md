# First Six Class Skill Specification

Status: implementation-facing design draft. Numeric values are first balance
seeds. Mechanical contracts, tags, status ownership, boss conversion, and
survival responsibilities are intended requirements.

This specification expands the class concepts in the
[initial roster](initial-class-skill-roster.md). Intended styles are coverage
tests, not selectable subclasses. Players may mix every compatible active,
passive, talent, fitting, and second-class interaction.

## Shared Contracts

### Content shape

- Each initial class has five active skills and nine class passives.
- Active skills have ten ranks. Rank 1 supplies the complete mechanic. Ranks 2
  through 10 normally add about `4%` of the skill's rank-1 damage or effect per
  rank, for roughly `36%` total rank growth. Ranks do not quietly remove setup,
  costs, cooldowns, or positioning requirements.
- Class passives have eight ranks. Every rank costs one skill point and every
  listed per-rank value is cumulative. Rank 1 introduces the interaction, but
  the passive should become build-defining only after sustained investment.
  Ranks 4 and 8 may add a disclosed breakpoint when pure numerical scaling
  cannot carry the identity on its own.
- A passive may not be a one-point wonder whose complete value arrives at rank
  1. Mechanic-changing passives still scale their magnitude, duration, cap, or
  reliability through all eight ranks. Cheap access is acceptable; cheap
  mastery is not.
- The nine passives represent `72` possible points per class and `144` across a
  two-class character before active-skill ranks. The eventual level budget must
  force omission: a finished character is expected to maximize a few defining
  passives, partially invest in support passives, and leave several untouched.
- Every percentage damage value below is **Weapon Damage effectiveness** unless
  the text explicitly names maximum Health, maximum Magic Shield, or another
  non-weapon base. `100% Weapon Damage` uses the equipped weapon's complete
  resolved VRPG Weapon Power before skill-specific damage stats. This applies
  equally to attacks, spells, projectiles, ailments, and constructs.
- Active-skill rank growth modifies effectiveness rather than supplying a
  disconnected flat damage ladder. The default seed is `4% of rank-1
  effectiveness per rank`: an `85%` rank-1 skill reaches `115.6%` at rank 10.
  Multi-hit and ailment components list their own effectiveness and use the
  same rule unless explicitly fixed.
- Active skills are weapon-agnostic. Any equipped weapon may use any learned
  skill with its complete authored delivery and effect; weapons do not impose
  hidden effectiveness penalties and class labels never become weapon locks.
- Weapon identity comes from itemization. Affix pools and weights may favor
  mechanically related stats—for example, hammers finding Stagger support more
  often or daggers finding opener and critical support—while rare off-pattern
  rolls and later modification preserve unconventional builds.

### Delivery and timing contract

Close skills do not inherit Vintage Story's held-item reach. Their server-
authored delivery is one of `melee_arc`, `melee_line`, `melee_single`, or the
existing caster `circle`, with explicit range, width/angle, vertical tolerance,
terrain obstruction, and target count. Current roster mappings include:

| Active | Delivery |
| --- | --- |
| Hammer Blow; Brace counter; Drive Back | Forward arc |
| Advance impact; Thrust | Forward line |
| Quick Cut; Ambush; each Flurry strike | Precise single target |
| Fracture | Caster circle |

Flurry is the reference sequence: four distinct `32%` hits over `0.8s`, not
one `128%` hit. Each hit rolls and feeds criticals, ailments, leech, and on-hit
effects independently. The UI may show `128%` complete-sequence effectiveness
as secondary information but may not hide the four-hit contract.

The shared runtime also supports held channels. A channel repeats its delivery
at an authored interval until input release, resource failure, death, another
skill activation, or maximum duration; cooldown begins when it ends. The
playground **Grinding Sweep** is the mechanical reference: a caster-centered
spin ticking for `45% Physical` every `0.25s` for at most four seconds. It is a
runtime test skill, not a sixth approved Smith launch active. Before the class
roster freezes, a channel must either replace an existing active, arise from a
support transformation, or belong to later class content so the five-active
content budget remains honest.

### Weapon Power contract

Every weapon used by VRPG has a persistent minimum level requirement and a
VRPG Weapon Power value. Required level determines the permanent baseline; it
does not compare itself with the player or target after the item is created.
An old weapon is never debuffed. It becomes ineffective because newer weapons
and higher-level enemies have grown beyond its unchanged number.

The initial shared formula is:

```text
level base = 10 × 1.035 ^ (minimum level requirement - 1)

affixed power =
    (level base + Flat Weapon Damage)
    × (1 + summed Additional Weapon Damage)
    × product(1 + More Weapon Damage)

final Weapon Power = affixed power × weapon rarity power scalar

skill base hit =
    final Weapon Power × skill Weapon Damage effectiveness / 100
```

Skill Flat, Additional, and More damage layers resolve after the skill base
hit. Required level is the dominant source, affixes are the main source of item
quality within a level band, and the rarity power scalar is last and bounded.
Initial scalar seeds are Common `1.00`, Rare `1.15`, and Unique `1.20`; rarity
also controls affix access, but it must not let an old item span several level
bands by itself.

At the `3.5%` growth seed, a level-80 baseline is about `50.3%` of a level-100
baseline. This ratio is not a staleness penalty and is not immutable. The
balance requirement is that, near endgame, using an otherwise comparable
weapon twenty levels below the encounter moves the build outside its acceptable
clear-time target. A remarkably rolled item may bridge one band; it should not
comfortably bridge two.

A level-matched weapon is only the maintained damage foundation, not a complete
build. Ordinary creature health deliberately outpaces the current-level weapon
baseline as progression rises. The initial health tiers produce approximately
`1×` build pressure at level 1, `3.6×` near level 40, `6×` near level 60,
`9×` near level 80, and `13.7×` at level 100 before rarity. Skill ranks,
offensive stats, passives, talents, affixes, critical investment, ailments, and
setup/payoff interactions must close that widening gap.

The explicit hard-failure test is a level-100 character with a plain level-100
weapon, default critical stats, and no skills or talents. That loadout must not
be able to complete a representative level-100 fight. If pure Weapon Power can
eventually win through ordinary attacks or an uninvested rank-1 damage action,
the creature curve, combat pressure, or encounter gate is undertuned. A current
weapon prevents obsolescence; buildcraft earns viability.

Native Vintage Story attack power and VRPG Weapon Power remain separate:

- non-RPG combat continues to use the native weapon behavior;
- an RPG-eligible target resolves an ordinary weapon attack as a `100% Weapon
  Damage` VRPG hit so native numbers cannot bypass RPG scaling;
- every class skill uses VRPG Weapon Power and accepts every weapon family;
- an ordinary vanilla weapon without VRPG item data is still usable, but has
  only the explicit level-1 fallback and is therefore not a hidden endgame
  weapon;
- when a vanilla weapon is generated, crafted, or upgraded as VRPG equipment,
  its persistent required level and Weapon Power are authored by that RPG item
  process; native material attack power is not multiplied into Weapon Power a
  second time;
- casting a damaging skill without an equipped weapon fails with a clear
  requirement message rather than inventing a level-scaled unarmed weapon.

Weapon material, family, reach, speed, durability, and native usefulness may
still matter. Family-specific favored affixes create identity without changing
the universal skill coefficient or locking a class to a base type.

### Launch active Weapon Damage ledger

These are rank-1 seeds. A zero means the action is utility-only; secondary hits
are listed separately so their budget cannot be hidden in the primary number.

| Class | Active | Rank-1 Weapon Damage effectiveness |
| --- | --- | ---: |
| Smith | Hammer Blow | 105% Physical |
| Smith | Brace | 0% guard; 135% Physical counter |
| Smith | Reinforce | 0% |
| Smith | Fracture | 145% Physical before Stagger payoff |
| Smith | Advance | 80% Physical |
| Trapper | Barbed Shot | 85% Physical hit; 30% Bleed |
| Trapper | Snare | 0% |
| Trapper | Parting Shot | 90% Physical |
| Trapper | Cull | 150% Physical before conditions |
| Trapper | Blast Trap | 135% Physical |
| Pilferer | Quick Cut | 80% Physical before Tempo |
| Pilferer | Ambush | 140% Physical |
| Pilferer | Pilfer | 0% |
| Pilferer | Slip Away | 0% |
| Pilferer | Flurry | 4 × 32% Physical; 128% complete sequence |
| Warden | Thrust | 105% Physical |
| Warden | Ward | 0% |
| Warden | Drive Back | 75% Physical |
| Warden | Stand Fast | 0% |
| Warden | Step In | 0% |
| Corroder | Rust Lance | 85% Rust hit; 2 × 12% Corrosion |
| Corroder | Spill | 0% direct; one existing Corrosion tick in single-target fallback |
| Corroder | Cinder | 120% Fire hit; 35% Burn |
| Corroder | Collapse | 60% Rust + 22% per consumed stack; 236% maximum primary hit |
| Corroder | Shell | 0% |
| Handler | Tuning Strike | 65% Discharge; 30% response per construct |
| Handler | Set Loose | 0%; Bronze Locust attacks for 45% Physical |
| Handler | Overtune | 0% |
| Handler | Recall | 0% |
| Handler | Scrap | 70–175% Discharge from missing construct Health |

### Tags required by the resolver

The runtime needs stable tags rather than hidden class-style checks:

`hit`, `ailment`, `damaging_ailment`, `attack`, `spell`, `projectile`, `area`,
`placed`, `movement`, `guard`, `setup`, `payoff`, `mark`, `minion`, `command`,
`protection`, `close`, `distant`, `fire`, `rust`, `physical`, `discharge`, and
`cold`, `healing`, `health_healing`, `magic_shield_restoration`, `corruption`,
and `debuff`.

Tags describe what an action actually does. A skill may have several. Talent
and item effects inspect these tags but never inspect an intended style name.

### Status ownership and stacking

- A player owns the Bleed, Burn, Corrosion, Mark, Stagger, trap, Ward, and
  construct states they create. Personal consumption mechanics spend only the
  owner's state unless a skill explicitly says it may consume party state.
- **Bleed:** up to five owned stacks per target, six-second base duration.
  Each stack has its own magnitude but refreshing affects the shortest owned
  duration first.
- **Burn:** one owned Burn per source skill. A stronger Burn replaces a weaker
  one; an equal or stronger application refreshes it. This favors large Fire
  hits rather than rapid stack spam.
- **Corrosion:** up to ten owned stacks per target, eight-second base duration.
  It deals Rust damage over time. New applications refresh only part of the
  oldest stack's duration unless a passive says otherwise.
- **Stagger:** a visible `0–100` meter per target. Ordinary enemies become
  Broken at 100 and lose their current action. Bosses become Broken for a short
  `1.25s` window, clear the meter, then gain eight seconds of rising Stagger
  resistance. Damage payoffs still work during resistance.
- **Rooted:** ordinary enemies cannot translate for the stated duration but may
  still use valid attacks. Bosses instead suffer `35%` movement reduction for
  two seconds and gain Stagger progress. Repeated roots face control resistance.
- **Marked, Quarry, Opening, and Reaction:** explicit setup states, not generic
  aliases for every debuff. Their owners and qualifying follow-ups are visible
  in advanced inspection.

### Critical and barrier seeds

- The first critical model uses `5%` base critical chance and `150%` total hit
  damage on a critical before critical-specific More/Less layers.
- Most critical-chance bonuses use **Additional Critical Chance**. All
  Additional sources sum with one another, then multiply the base. With no
  other source, `100% Additional Critical Chance` turns `5%` into `10%`, not
  `100%` final critical chance.
- A small number of premium gear and gear-upgrade sources grant **Flat Critical
  Chance**. Flat is added to the base before Additional scaling and is therefore
  substantially more valuable. Ordinary class passives and talent-tree nodes
  should not provide it. Flat must be rare, explicit, and displayed in
  percentage points. **More Critical Chance** is a separate multiplicative
  layer reserved for exceptional gamechangers or similarly scarce effects.
- Resolve final hit critical chance as:

  ```text
  final critical chance = clamp(
      (5% base + sum of Flat Critical Chance)
      × (1 + sum of Additional Critical Chance)
      × product of More Critical Chance multipliers,
      0%, critical chance cap
  )
  ```

- Additional Critical Chance must be distributed across class passives,
  ordinary talent pods, general gear, weapon-favored affixes, Fittings, and
  conditional combat bonuses. Flat Critical Chance belongs primarily to gear
  drops and gear-upgrade outcomes. No single ordinary source should carry a
  build from `5%` to a gamechanger-ready chance. Initial target bands are
  roughly `5%` uninvested, `10–15%` incidental, `20–30%` committed, and
  `33–45%` ready to evaluate Critical Commitment. These are simulation targets
  rather than caps.
- The character sheet and advanced tooltip show final chance first, followed by
  base, Flat, total Additional, More multipliers, and cap. These are balance
  seeds, but every tooltip and the Critical Commitment simulation must use the
  same resolved values.
- A damaging ailment does not crit merely because the hit that applied it was a
  critical. An explicit stat or gamechanger is required before ailment criticals
  exist.
- A **barrier** is timed damage absorption separate from maximum Magic Shield.
  It absorbs eligible hit damage before Magic Shield and Health. Reapplying the
  same barrier keeps the stronger remaining value rather than stacking. Barriers
  from different skills share a global cap of `35% maximum Health` until a
  deliberate barrier-cap stat exists.
- Armor, damage reduction, guard reduction, barrier, Magic Shield, and Health
  must have one documented server order before balance approval. The provisional
  order is mitigation, guard reduction, barrier, Magic Shield, then Health.

### Late-game engine requirement

Every solo late-game style must acquire either dependable repeated criticals or
high-throughput damaging ailments. Direct Weapon Damage, skill rank, and generic
damage modifiers establish the floor but are deliberately insufficient against
upper-band Health without one of those engines. Critical builds need frequency,
not merely critical damage on a five-percent chance. Ailment builds need rapid
application, stacking, refresh, spread, magnitude, or consumption rather than a
single incidental Bleed, Burn, or Corrosion.

This requirement is not a class lock. Trapper and Corroder expose obvious
ailment foundations; Pilferer exposes an obvious critical foundation; Smith,
Warden, and Handler must have credible critical or ailment routes through class
passives, generic talents, gear, Fittings, and second-class interactions. A
support-oriented Warden may rely on allies for group damage, but its solo build
still needs one engine and is balanced around slower clear.

Each intended-style audit must identify its expected late-game engine or the
generic/second-class access it is designed to use. A style that reaches endgame
viability through neutral direct-hit scaling alone fails the class audit.

Committed defensive builds operate on a five-digit endgame scale. Strong
Health builds may exceed `10,000` maximum Health. Strong Magic Shield builds may
exceed `20,000` maximum Shield because they generally do not receive Armor's
mitigation. Percentage barriers and recovery remain relative to those pools;
all existing flat recovery seeds require a later level-scaling pass.

Endgame boss testing uses a two-million-Health level-100 reference as an
ordinary stress case, not an exceptional maximum. The initial critical ceiling
case is a `500% Weapon Damage` action used once per second with `100%` final
critical chance and `500%` total critical damage. Every style is compared by
sustained output after ramp and realistic uptime; damaging ailments must reach
a comparable budget through application rate, stack pressure, spread, or
consumption rather than imitating the direct-hit formula.

Health and Mana Leech use separate shared reservoirs and pay no faster than
`10%` of the matching maximum resource per second by default. At `10,000
Health` and `45,205 sustained DPS`, about `2.21% Health Leech` keeps that cap
full; a committed build must be able to assemble roughly `2–3%`. Leech-cap
increases are exceptional class specialization and must include enough access
to generation to matter. Passive regeneration remains far below leech and
active healing during combat.

Active heals list **Healing effectiveness**, which multiplies resolved Weapon
Power but uses healing modifiers instead of damage modifiers. They do not crit
by default and are not constrained by the leech cap. Corruption skills name a
specific vulnerability, replace the caster's previous Corruption, respect the
two-player-Corruption party limit, and apply at half magnitude to bosses unless
the skill is stricter.

Health healing and Magic Shield restoration are separate. A skill carries
`health_healing` or `magic_shield_restoration` for the resource it explicitly
restores; the umbrella `healing` tag is only for shared presentation and
contribution events. Ward and Step In heal Health only. Their barriers do not
restore Magic Shield, and no healing modifier turns them into shield skills.

### Boss and mob rules

- Bosses cannot be permanently Rooted, stagger-locked, threat-locked, or made
  unable to act. Control converts into bounded slows, action interruption,
  Stagger progress, or damage windows.
- A boss without removable boons supplies the documented fallback for Pilfer.
- A boss permits periodic opener and mark windows even when it cannot become
  unaware in ordinary AI terms.
- Area clear may be stronger than single-target damage because target count is
  its reward. It may not also inherit the best boss scaling without an explicit
  opportunity cost.
- Ratings use `1` as deliberately poor, `3` as functional, and `5` as a defining
  strength. A low mobbing rating means slower clear, not inability to finish a
  group encounter. A low boss rating means lower sustained or setup efficiency,
  not a dead skill bar.

### Survival rule

Each intended style identifies at least two cooperating survival layers. A
second class may improve them but is not required to supply them. Recovery is
internally capped, boss control has conversion behavior, and no style assumes it
can kill all threats before receiving a telegraphed attack.

## Smith

**Class promise:** Stay in melee, build Stagger or protection, and turn either
one into force.

**Stat lean:** Strength. **Favored weapon affixes:** hammers favor Stagger,
guard, and close-hit support. **Resource:** no Mana; cooldowns, Stagger,
guard timing, and proximity control cadence. **Overall:** bossing `4/5`, mobbing
`3/5`, survival `5/5`, mobility `2/5`.

### Active skills

| Skill | Seed contract |
| --- | --- |
| **Hammer Blow** | `1.2s` cooldown. Short `70°`, `2.5m` hammer arc. Deals `105% Physical hit damage` and adds `18 Stagger` to the primary target, `9` to others. Against a Broken target it deals `15% more` damage but adds no Stagger until the break ends. Primary, attack, close, area, setup. |
| **Brace** | `6s` cooldown. Guard for `0.55s`, reducing the triggering hit by `70%`. A guarded hit produces a `135% Physical` counter in front and grants `15% increased Armor` for three seconds. If no hit arrives, the cooldown becomes `3s` and no counter occurs. Guard, protection, payoff. |
| **Reinforce** | `8s` cooldown. Gain temporary protection equal to `12% maximum Health` for six seconds and `20% increased Armor` while it remains. This is a VRPG barrier and never repairs equipment durability. Protection. |
| **Fracture** | `5s` cooldown. `4m` caster circle, `145% Physical hit damage`. Consumes up to `60` owned Stagger from each target and deals `1% more` damage per two points consumed. Consuming at least `40` exposes the target to `8% increased Physical damage taken` for four seconds, halved on bosses. Area, payoff. |
| **Advance** | `4s` cooldown. Move up to `4m` toward the aimed point with `50% less damage taken` during movement, then deal an `80% Physical` narrow hit. It cannot cross solid collision or grant invulnerability. Movement, attack, close. |

### Class passives

1. **Tempered** — Dealing a close hit or guarding damage grants one Tempered
   stack for four seconds, maximum five. Each rank grants `1.5% increased Armor`
   per stack, reaching `12%` per stack at rank 8.
2. **Heavyhanded** — Each rank makes Hammer Blow add `3` additional Stagger to
   its primary target and `1` to secondary targets, reaching `+24/+8` at rank
   8.
3. **Reprisal** — A successful Brace counter shortens Hammer Blow and Fracture
   cooldowns by `0.08s` per rank, reaching `0.64s`. One trigger per Brace.
4. **Sound Work** — Reinforce grants nearby allies `5%` of its barrier per rank,
   reaching `40%` at rank 8. The Smith's own barrier is unchanged.
5. **Breaker** — Consuming at least 40 Stagger adds `1 percentage point` per
   rank to Fracture's Physical exposure, raising it from `8%` to `16%` at rank
   8. The final value is still halved on bosses.
6. **Follow Through** — After Fracture consumes Stagger, the next Hammer Blow
   within four seconds gains `3% increased area` and `3% more damage` per rank,
   reaching `24%` of each at rank 8.
7. **Set Feet** — A successful Brace grants `8% control resistance` per rank
   for three seconds, reaching `64%`. At rank 4 it also prevents one ordinary
   displacement; ranks 5–8 improve the resistance rather than adding more
   prevention charges.
8. **Close Work** — At five Tempered stacks, gain `1% action speed` per rank,
   reaching `8%`. Each rank also adds `0.15s` to the allowed gap between close
   hits before Tempered begins expiring, from two seconds to `3.2s` at rank 8.
9. **Patchwork** — While Reinforce's barrier remains, recover `0.15% maximum Health per second`
   per rank, reaching `1.2%` at rank 8. Recovery stops when
   the barrier breaks.

### Intended style audit

| Style | Repeatable loop | Survival | Bossing | Mobbing |
| --- | --- | --- | ---: | ---: |
| Breaker | Hammer Blow builds Stagger; Fracture spends it; Follow Through starts the next cycle. | Interrupted actions, armor on break, Advance for spacing. | 5 | 3 |
| Guard | Reinforce, read a telegraph, Brace, counter, then pressure during the armor window. | Barrier, timed reduction, armor, Patchwork. | 4 | 2 |
| Bruiser | Maintain Tempered with Hammer Blow, Advance between targets, use Fracture as regular area clear. | Sustained armor, barrier recovery, damage reduction during Advance. | 3 | 4 |

Smith is intentionally stronger against durable enemies than sprawling packs.
Bruiser raises its clear to good, but no Smith style should rival dedicated trap,
Burn, or pack-summon mobbing without investing its second class.

## Trapper

**Class promise:** Prepare terrain, preserve distance, and punish movement or a
carefully maintained target state.

**Stat lean:** Dexterity. **Favored weapon affixes:** crossbows favor projectile,
Bleed, and placed-effect support. **Resource:** two trap charges per trap
skill; charges recover independently. **Overall:** bossing `3/5`, mobbing `5/5`,
survival `4/5`, mobility `4/5`.

### Active skills

| Skill | Seed contract |
| --- | --- |
| **Barbed Shot** | `1.3s` cooldown. Entity-impact projectile. Deals `85% Physical hit damage` and applies one six-second Bleed stack dealing `30%` damage over its duration. Projectile, attack, damaging ailment, setup. |
| **Snare** | Two charges, `7s` charge recovery, `0.6s` arming time, `30s` world duration. The first enemy in `1.8m` is Rooted for `2.5s`; boss conversion uses the shared rule. The device is visible and attackable. Placed, control, setup. |
| **Parting Shot** | `4s` cooldown. Step up to `3.5m` backward and fire toward the crosshair for `90% Physical hit damage`. Deals `20% more` against an owned Rooted, Quarry, or deeply Bleeding target. Movement, projectile, attack. |
| **Cull** | `6s` cooldown. Precise projectile dealing `150% Physical hit damage`, plus `8% more` per owned Bleed stack and `25% more` against Rooted or Quarry targets. The condition bonuses share a `65%` cap. Projectile, payoff. |
| **Blast Trap** | Two charges, `6s` charge recovery, `0.7s` arming time, `20s` duration. Enemy proximity or an owned trap reaction triggers a `3.5m`, `135% Physical` area hit. One Blast Trap may trigger each other owned trap once, preventing loops. Placed, area, payoff. |

### Class passives

1. **Patient** — After remaining armed for two seconds, traps gain `4% increased effect`
   per rank, reaching `32%` at rank 8. The bonus caps at that value
   rather than growing with time.
2. **Barbed** — Barbed Shot's Bleed deals `4% increased damage` per rank,
   reaching `32%`. At rank 4, a direct hit refreshes `0.5s` of the shortest
   owned Bleed; this reaches `1.0s` at rank 8.
3. **Quick Hands** — Trap arming time is reduced by `5%` per rank, reaching
   `40%`. At rank 4, manually recovering an armed, untriggered trap returns one
   charge with an eight-second internal cooldown; ranks 5–8 shorten that
   cooldown by `0.75s` each, reaching five seconds.
4. **Deadfall** — When Snare triggers or is destroyed after arming, it deals
   `12% Physical area damage` per rank in `2.5m`, reaching `96%` at rank 8.
5. **Quarry** — Rooting an enemy marks it Quarry for `3s + 0.25s per rank`.
   Quarry takes `1.25 percentage points increased damage` per rank from all
   players, reaching `10%` for five seconds at rank 8 and half that on bosses.
   One Trapper's strongest Quarry applies.
6. **Bloodletting** — Hitting a target with at least three owned Bleed stacks
   restores `0.25% maximum Health` per rank, reaching `2%`. `2s` internal
   cooldown per Trapper.
7. **Linked Traps** — Triggering a trap advances the charge recovery of the
   other trap skill by `0.125s` per rank, reaching `1s`. A reaction chain
   triggers this once.
8. **Fleetfooted** — After Parting Shot finishes moving, gain `4% evasion` per
   rank for `0.7s + 0.075s per rank`, reaching `32%` for `1.3s` at rank 8.
9. **Long Sight** — Direct projectile hits made from `8–18m` add one Precision
   stack for four seconds, maximum three. Each stack grants Cull
   `5% Additional Critical Chance per rank` and Cull consumes them for
   `1% more damage per rank` each. At rank 8, three stacks supply
   `120% Additional Critical Chance`
   and `24% more damage`. Hits outside the band neither add nor remove stacks.

### Intended style audit

| Style | Repeatable loop | Survival | Bossing | Mobbing |
| --- | --- | --- | ---: | ---: |
| Bleeder | Stack Bleed with Barbed Shot, reposition, then Cull at five stacks. | Snare, Parting Shot, Bloodletting, Evasive Step when Dexterity-primary. | 5 | 3 |
| Engineer | Lay Snare and Blast Trap lanes, pull enemies through, use Linked Traps to continue the chain. | Rooted lanes, recoverable placement, Parting Shot, Evasive Step. | 2 | 5 |
| Skirmisher | Hold the `8–18m` band, build Precision with direct hits, spend it through Cull. | Constant movement, Fleetfooted, Snare, Evasive Step. | 4 | 3 |

Engineer is deliberately the strongest initial mobbing style and a weaker boss
choice when a boss moves unpredictably or destroys devices. Bleeder supplies
the class's strongest boss ramp, while Skirmisher rewards aim without needing a
long ailment setup.

## Pilferer

**Class promise:** Create an opening, take an advantage, and leave before the
answer lands.

**Stat lean:** Dexterity. **Favored weapon affixes:** daggers favor critical,
Opening, and Tempo support. **Resource:** Tempo, maximum five, generated
only by accurate close sequences and lost after four seconds without a qualifying
hit. **Overall:** bossing `4/5`, mobbing `3/5`, survival `4/5`, mobility `5/5`.

### Active skills

| Skill | Seed contract |
| --- | --- |
| **Quick Cut** | `1.0s` cooldown. `1.9m` close strike for `80% Physical hit damage`. Gains `60% Additional Critical Chance` against a controlled, Marked, Bleeding, Corroded, Corrupted, or Opening target. A critical Quick Cut consumes up to three Tempo for `8% more damage` per Tempo. Attack, close, payoff. |
| **Ambush** | `5s` cooldown. Move up to `4m` to an aimed enemy and deal `140% Physical hit damage`. Applies Opening for three seconds when the target is full-health, controlled, unaware, or not struck by the Pilferer for six seconds. Bosses allow this qualification once every eight seconds. Movement, attack, setup, mark. |
| **Pilfer** | `7s` cooldown. Remove one removable enemy boon and copy a bounded version for six seconds. With no valid boon, apply **Covetous Weakness**, a six-second Corruption reducing target Armor and action speed by `8%`, and grant the Pilferer the same values. Boss magnitude is halved. Setup, mark, corruption, debuff. |
| **Slip Away** | `8s` cooldown. Step up to `3m` away, clear ordinary threat, and gain `35% evasion` for `1.2s`. The next Ambush or Quick Cut within four seconds may create Opening regardless of awareness, but dealing damage ends the evasion early. Movement, protection, setup. |
| **Flurry** | `3s` cooldown. Four strikes over `0.8s`, each dealing `32% Physical hit damage`. Each accurate strike after the first adds one Tempo; missing or losing the target ends the sequence. Attack, close. |

### Class passives

1. **Furtive** — Outside active combat, hostile detection distance against the
   Pilferer is reduced by `4%` per rank, reaching `32%`. It gives no mid-fight
   invisibility.
2. **Opening** — Attacks against the Pilferer's Opening target ignore `1.5`
   percentage points of Armor per rank for three seconds, reaching `12%` at
   rank 8. Boss value is halved.
3. **Light Fingers** — A successful boon removal extends the copied boon by
   `0.4s` and shortens Pilfer cooldown by `0.15s` per rank, reaching `3.2s` and
   `1.2s` at rank 8.
4. **Opportunist** — Against a controlled, Bleeding, Corroded, Corrupted,
   Quarry, or Opening target, gain `20% Additional Critical Chance` and `3%`
   increased critical damage per rank. At rank 8 this is `160% Additional`
   and `24% increased critical damage`. Critical hits against such a target
   also restore `0.15% maximum Health` per rank, with a `1s` recovery cooldown.
   This is the Pilferer's principal eight-point critical investment.
5. **Clean Getaway** — Defeating a target within four seconds of creating
   Opening recovers `5%` of Slip Away's cooldown per rank, reaching `40%`.
6. **Second Cut** — Alternating Quick Cut and Flurry grants `0.5% action speed`
   per Tempo per rank, reaching `4%` per Tempo at rank 8. Repeating the same
   skill preserves Tempo but grants no new speed stack from this passive.
7. **Borrowed Guard** — Armor, resistance, barrier, or evasion copied by Pilfer
   has `5% increased effect` per rank on the Pilferer, reaching `40%`.
   Offensive stolen values are unchanged.
8. **Nerve** — Spending at least three Tempo through a critical hit grants `4% evasion`
   per rank for one second, reaching `32%`. It may trigger once every
   three seconds.
9. **No Witnesses** — Defeating an Opening target lets the next eligible target
   within `1s + 0.5s per rank` receive Opening from Quick Cut, reaching five
   seconds. At rank 8, one additional successful kill may transfer the state a
   second time; it cannot continue chaining beyond that.

### Intended style audit

| Style | Repeatable loop | Survival | Bossing | Mobbing |
| --- | --- | --- | ---: | ---: |
| Ambusher | Slip Away or approach quietly, Ambush, spend the Opening window, then secure a reset. | Threat break, burst evasion, movement, Clean Getaway. | 3 | 5 |
| Thief | Pilfer a boon or fallback, exploit its weakness, extend the useful stolen defense through precision. | Borrowed defenses, reduced enemy action speed, Slip Away. | 4 | 3 |
| Duelist | Alternate Quick Cut and Flurry, hold Tempo, convert accurate criticals into Nerve windows. | Earned evasion, bounded crit recovery, Slip Away emergency reset. | 5 | 2 |

Pilferer has exceptional target access and boss dueling but deliberately lacks
large native area attacks. Ambusher can move quickly through weak packs via
resets; Duelist is intentionally poor at clearing scattered enemies without a
second-class area tool.

## Warden

**Class promise:** Establish safe ground, control pressure, and make nearby
players harder to dislodge.

**Stat lean:** Strength and Intelligence. **Favored weapon affixes:** tridents
favor reach, Ward, and protection support. **Resource:** Mana only on Ward and
Step In; weapon actions use cooldowns. **Overall:** bossing `3/5`,
mobbing `4/5`, survival `5/5`, group value `5/5`.

### Active skills

| Skill | Seed contract |
| --- | --- |
| **Thrust** | `1.35s` cooldown. `5m × 1.2m` line for `105% Physical hit damage`, hitting at most five enemies. Hitting a Corroded enemy or three enemies at once restores `2% maximum Magic Shield`, capped once per cast. Attack, close, area. |
| **Ward** | `8s` cooldown, `20 Mana`. Place a `5m` radius Ward for eight seconds. Players inside take `12% reduced damage`, gain `20% control resistance`, recover temporal stability slowly, and receive `25% Weapon Power` as Health restoration each second. Only the strongest allied Ward protection and healing tick apply. Placed, protection, healing, health_healing, area. |
| **Drive Back** | `5s` cooldown. `4m` sweep for `75% Physical hit damage`, pushing ordinary enemies toward the Ward boundary. Bosses gain `20 Stagger` instead of displacement. An enemy crossing the owned Ward edge is slowed `25%` for two seconds. Area, control, setup. |
| **Stand Fast** | `8s` cooldown. For two seconds take `40% less damage`, move `30% slower`, and generate strong threat. Preventing damage stores up to `15% maximum Health` as Guard. When the stance ends, Guard becomes a four-second barrier. Guard, protection. |
| **Step In** | `6s` cooldown, `12 Mana`. Move up to `6m` to an aimed point or ally. Restore Health to the selected ally at `180% Weapon Power` healing effectiveness and grant self and that ally a barrier equal to `8%` of the Warden's maximum Health for four seconds. With no ally, heal self and retain the self barrier. Movement, protection, healing, health_healing. |

### Class passives

1. **Anchor** — Inside an owned Ward, gain `7.5% displacement resistance` and
   `3.75% control-duration reduction` per rank, reaching `60%` and `30%` at
   rank 8.
2. **Shelter** — Ward radius increases `2%` per rank and allies receive an
   additional `0.5 percentage points reduced damage` per rank, reaching `16%`
   radius and `4%` ally reduction. The Warden's personal reduction is unchanged.
3. **Watchful** — Thrusting an enemy within four seconds after it damaged an
   ally restores `0.625% maximum Magic Shield` per rank, reaching `5%`, and
   adds strong threat.
4. **Steady** — At high temporal stability, or while inside an owned Ward, gain
   `3% increased Health and Magic Shield recovery` per rank, reaching `24%`.
5. **Shared Burden** — Redirect `1%` of nearby allies' post-mitigation damage
   per rank to the Warden, reaching `8%`. Each redirected hit is capped at `5%`
   Warden maximum Health and cannot reduce the Warden below one Health.
6. **Boundary** — An enemy crossing an owned Ward boundary loses an additional
   `2% movement speed` and gains `1.5 Stagger` per rank, reaching `16%` and
   `12 Stagger`. `3s` target cooldown.
7. **Relief** — Damage actually prevented by Stand Fast or a barrier granted by
   Step In restores up to `0.25% maximum Health per second` per rank, reaching
   `2%`. Unused barriers grant nothing.
8. **Long Reach** — Thrust gains `0.15m` length per rank, reaching `1.2m`.
   Hitting at least three targets adds `0.375 percentage points` per rank to its
   Magic Shield restoration, raising it from `2%` to `5%` at rank 8.
9. **Moving Ward** — Recasting Ward while one is active moves it to the new
   point with `40% + 5% per rank` of its remaining duration, reaching `80%`.
   It also adds `0.25s` per rank, reaching two seconds, and never creates a
   second Ward.

### Intended style audit

| Style | Repeatable loop | Survival | Bossing | Mobbing |
| --- | --- | --- | ---: | ---: |
| Sentinel | Ward a lane, Drive Back across its edge, Thrust aligned targets, move the Ward when forced. | Ward mitigation, Anchor, boundary control, Steady recovery. | 3 | 5 |
| Guardian | Step In to pressure, Stand Fast through the hit, convert prevented damage into barriers and Relief. | Guard, barriers, capped Shared Burden, recovery from real protection. | 3 solo / 5 group | 3 |
| Spearhead | Advance behind Moving Ward, align packs with Drive Back, repeatedly Thrust through them. | Reach, Ward, knockback/Stagger, Magic Shield returns. | 4 | 4 |

Warden trades clear speed and personal burst for the strongest dependable
protection package. Sentinel handles packs well because enemies interact with a
visible boundary. Guardian's group boss value is excellent, but its solo damage
remains intentionally below dedicated boss styles.

## Corroder

**Class promise:** Build Corrosion or Burn, spread pressure through a pack, or
alternate Rust and Fire for critical reactions.

**Stat lean:** Intelligence. **Favored weapon affixes:** staves favor Rust,
Fire, ailment, and Magic Shield support. **Resource:** Mana. **Overall:** bossing
`4/5`, mobbing `5/5`, survival `3/5`, mobility `2/5`.

### Active skills

| Skill | Seed contract |
| --- | --- |
| **Rust Lance** | `1.4s` cooldown, `8 Mana`. Instant aimed hit at up to `18m`, `1.8m` impact radius, `85% Rust hit damage`, and two Corrosion stacks. Each base stack deals `12% Rust damage` over eight seconds. Spell, area, rust, damaging ailment, setup. |
| **Spill** | `4s` cooldown, `12 Mana`. Select a Corroded target within `16m`. Up to five enemies in `6m` receive half its owned stacks, rounded up and capped at four. The source loses only one stack. Affected enemies receive **Smoldering Fault**, a five-second Corruption causing `12% more` owned Burn damage taken; bosses receive `6%`. Against one target, Spill instead refreshes two oldest stacks and deals one immediate Corrosion tick. Spell, area, setup, corruption, debuff. |
| **Cinder** | `2.2s` cooldown, `14 Mana`. Entity-impact projectile with `3.2m` burst, dealing `120% Fire hit damage` and applying a four-second Burn for `35%` damage. A stronger Burn replaces a weaker one. Spell, projectile, area, fire, damaging ailment. |
| **Collapse** | `6s` cooldown, `18 Mana`. Select a target within `16m`; consume up to eight owned Corrosion stacks. Deal `60% + 22% per stack` Rust hit damage to it and half that result in `3m`. If no stack exists, deal only the base hit and consume the cooldown. Spell, area, rust, payoff. |
| **Shell** | `8s` cooldown, `10 Mana`. Begin with a VRPG barrier equal to `6% maximum Health`. In `12m`, consume at most two owned Corrosion stacks per enemy and shorten owned Burns by at most one second per enemy, up to five enemies. Each consumed stack or second adds `2% maximum Health` to the barrier, capped at `26%`. Protection, payoff. |

### Class passives

1. **Pitting** — Rust Lance has `5%` chance per rank to add one additional
   Corrosion stack, reaching `40%`. Maximum owned stacks increase by one at
   ranks 2, 4, 6, and 8, reaching fourteen.
2. **Lingering** — Corrosion duration increases `5%` and Rust Lance refreshes
   `0.15s` on the shortest owned stack per rank, reaching `40%` and `1.2s`.
3. **Flaking** — A Corroded enemy that dies spreads one owned stack at rank 1,
   plus another at ranks 3, 5, and 7, reaching four. It reaches one additional
   nearby target per two ranks, up to four targets within five meters. One
   spread per death.
4. **Kindling** — Cinder deals `1% more hit damage per rank` for each Corrosion
   stack on the primary target, capped at five stacks; its Burn gains half that
   bonus. Rank 8 reaches `8% more` per stack. It does not consume Corrosion.
5. **Raw Exposure** — Rust damage increases by up to `2.5%` per rank as temporal
   stability falls, reaching `20%`. Rifts supply a bounded encounter Exposure
   value. This passive never drains stability by itself.
6. **Fuel** — Cinder against a Burning target refreshes `0.15s` of Burn and
   increases its magnitude `2%` per rank, reaching `1.2s` and `16%`. One Fuel
   increase applies per Burn. Corrosion is helpful through Kindling but not
   required.
7. **Draft** — At rank 1, Cinder becomes ground-impact so creatures no longer
   intercept it, and it leaves a `3m` burning area. The area lasts `2s + 0.25s`
   per rank and deals `5% Fire damage per second` per rank, reaching four
   seconds and `40%` per second at rank 8. It remains one Cinder skill and can
   be respecced normally.
8. **Flashpoint** — Fire hits against a target carrying owned Corrosion and Rust
   hits against a target carrying owned Burn gain `10% Additional Critical Chance`
   per rank, reaching `80%`. A qualifying critical opens a Reaction
   window for `2s + 0.25s per rank`, reaching four seconds, asking for a
   critical of the opposite type.
9. **Backlash** — Completing Reaction releases `15%` mixed Rust and Fire area
   damage and restores `1% maximum Magic Shield` per rank, reaching `120%` and
   `8%` at rank 8. A `1.5s` target cooldown prevents rapid multi-hit loops.

### Intended style audit

| Style | Repeatable loop | Survival | Bossing | Mobbing |
| --- | --- | --- | ---: | ---: |
| Corrosion | Rust Lance ramps stacks, Spill seeds packs, Collapse chooses when to cash out. | Shell converts some ramp into shield; afflicted targets are kept at range; Magic Shield investment. | 4 | 5 |
| Burn | Cinder or Draft establishes fire areas, Fuel maintains strong Burns, new impacts move the zone. | Range, area denial, Shell consuming Burn time, Magic Shield. | 2 | 5 |
| Reaction | Alternate Rust Lance and Cinder criticals, complete Reaction, repeat after the target lockout. | Backlash shield return, Shell, ranged positioning. | 5 | 3 |

Corroder is the strongest initial ailment and elemental pack clearer. Burn pays
for that with weak boss efficiency when targets leave areas or overwrite short
Burn windows. Reaction has the best Corroder boss ceiling but needs two damage
types, critical investment, and accurate alternation. Shell is intentionally a
real damage-versus-defense decision rather than free sustain.

## Handler

**Class promise:** Deploy tuned constructs, direct their attacks, and decide
whether to preserve or spend them.

**Stat lean:** Intelligence. **Favored weapon affixes:** tuning spears favor
Discharge, command, and minion support. **Resource:** Mana and active construct
capacity. **Overall:** bossing `4/5`, mobbing `4/5`, survival `4/5`,
setup dependence `5/5`.

### Baseline construct contract

A Bronze Locust construct lasts 40 seconds, has Health equal to `35%` of the
Handler's maximum Health, attacks every `1.25s` for `45% Physical damage`, and
inherits explicit minion stats. The default maximum is one. Constructs are
owned entities, may be targeted by enemies, cannot block narrow passages
permanently, and teleport to the Handler only after path recovery fails for a
bounded time.

### Active skills

| Skill | Seed contract |
| --- | --- |
| **Tuning Strike** | `1.2s` cooldown, no Mana. Spear strike or short `12m` pulse for `65% Discharge hit damage`. It applies an owned Tuning Mark for three seconds. Active constructs focus the marked target and each performs one response hit for `30%` damage, with a `1.2s` response cooldown per construct. Attack, discharge, command, mark. |
| **Set Loose** | `8s` cooldown, `25 Mana`. Deploy one Bronze Locust at the aimed valid point within `6m`. At capacity, replace the oldest construct without triggering death or Scrap effects. Minion, placed. |
| **Overtune** | `6s` cooldown, `15 Mana`. The aimed or nearest owned construct gains `35% action speed`, `25% movement speed`, and `40% increased damage` for six seconds while losing `6% maximum Health` per second. Overtune cannot reduce it below one Health directly. Command, setup. |
| **Recall** | `7s` cooldown, `10 Mana`. Pull constructs to valid points around the Handler, end Overtune, and repair `20% maximum construct Health`. Grant the Handler a four-second barrier equal to `6% maximum Health + 2% per recalled construct`, capped at `14%`. Command, protection. |
| **Scrap** | `4s` cooldown, no Mana. End one aimed construct and burst in `3.5m` for `70% Discharge damage + 1.2% per percent of construct Health missing`, capped at `175%`. A construct above 75% Health has deliberately poor efficiency. Minion, area, discharge, payoff. |

### Class passives

1. **Spare Parts** — When a construct dies to damage, other constructs repair
   `1.5% maximum Health` and Set Loose recovers `0.15s` per rank, reaching `12%`
   and `1.2s`. Replacement at capacity does not count.
2. **Pack** — Rank 1 raises maximum constructs by one and makes constructs deal
   `22% less damage` while more than one is active. Ranks 2–7 reduce that penalty
   by `2 percentage points` each. Rank 8 raises capacity by one again and sets
   the final multi-construct penalty to `8% less damage`. This is ordinary
   breadth; Full Pack remains a stronger talent gamechanger.
3. **Feedback** — Construct hits add one Charge, maximum ten. Tuning Strike
   consumes ten to arc `10% Discharge damage` per rank to up to three nearby
   enemies, reaching `80%`. Rank 4 lowers the threshold to eight Charge; rank 8
   lowers it to six. Charge generation remains once per construct attack event.
4. **Cold Frame** — Idle or freshly Recalled constructs Chill enemies within
   `1.4m + 0.2m per rank` by `3%` per rank, reaching `3m` and `24%`. A construct
   attacking continuously does not emit the aura.
5. **Final Turn** — An Overtuned construct that dies releases `15% Discharge area damage`
   per rank, reaching `120%`. Scrap replaces this burst rather than
   triggering both.
6. **Close Order** — Recall's barrier gains `0.25% maximum Health` per recalled
   construct per rank and its arrival Chill lasts `0.25s` per rank after
   constructs resume, reaching `2%` per construct and two seconds.
7. **Useful End** — Scrapping an Overtuned construct recovers `4%` of Set Loose
   cooldown and restores `0.5% maximum Magic Shield` per rank, reaching `32%`
   and `4%`.
8. **Keeper** — While exactly one construct is active, it gains `4% increased damage`,
   `5% maximum Health`, and `5% increased Tuning Strike response damage`
   per rank, reaching `32%`, `40%`, and `40%`. The bonus turns off naturally
   when another construct is deployed.
9. **Hard Case** — While exactly one construct is active and within eight meters,
   it intercepts `1%` of post-mitigation damage to the Handler per rank, reaching
   `8%`. Each transfer is capped at `5% construct maximum Health` and cannot
   intercept a lethal hit.

### Intended style audit

| Style | Repeatable loop | Survival | Bossing | Mobbing |
| --- | --- | --- | ---: | ---: |
| Pack | Maintain two or three constructs, mark targets with Tuning Strike, build Feedback, Recall before area hazards. | Multiple targets divide pressure, Recall barrier, Close Order Chill. | 3 | 5 |
| Overtuner | Deploy, Overtune near a damage window, direct responses, then choose Scrap versus Recall. | Useful End shield, Recall abort, construct interception of enemy attention. | 4 | 4 |
| Keeper | Maintain one durable construct, alternate Tuning Strike responses, Recall it before failure. | Hard Case interception, strong Recall barrier, Cold Frame control. | 5 | 2 |

Pack is a mobbing style because several bodies and Feedback arcs cover space;
its individual boss damage is taxed. Keeper is the Handler's deliberate boss
specialist and weakest clearer. Overtuner is flexible burst with the highest
execution risk: a bad Scrap creates both a damage loss and an exposed resummon
window.

## Cross-Class Interaction Audit

The six kits expose broad states rather than prescribed pairs:

| State or tag | Producers | Natural users |
| --- | --- | --- |
| Stagger / Broken | Smith, Warden | Smith payoff, Pilferer crit access, Trapper Quarry/control bonuses |
| Bleed | Trapper | Pilferer Opportunist, generic ailment and prepared-target talents |
| Rooted / slowed | Trapper, Warden, Handler Chill | Smith close access, Pilferer crit access, Corroder area placement |
| Opening / Tuning Mark / Quarry | Pilferer, Handler, Trapper | Any direct-hit or payoff skill through generic prepared-target rules |
| Corrosion / Burn | Corroder | Pilferer Opportunist, generic ailment, critical, and prepared-target rules |
| Owned area | Trapper, Warden, Corroder Draft | Held-ground talents and any second-class skill used from that position |
| Magic Shield | Warden, Corroder, Handler | Shield-cycle talents and defensive second-class combinations |
| Payoff tag | Fracture, Cull, Quick Cut Tempo spend, Collapse, Scrap | Generic Spend and Rebuild talents |
| Protection granted | Smith, Warden, Handler | Protection and Aid talents; multiplayer contribution scoring |

No row names a required class pair. An interaction is valid because the status,
tag, or event is explicit and server-authored.

## Production and Validation Order

1. Implement shared hit, critical, ailment ownership, Stagger, Root conversion,
   barriers, marks, placed-effect ownership, and advanced breakdown contracts.
2. Implement one complete loop first: Corroder Corrosion is the best current
   candidate because Rust Lance and Cinder already exercise the skill runtime.
3. Implement one contrasting physical loop: Smith Breaker or Trapper Engineer.
4. Add class passives only after their triggering active events are logged and
   testable by command.
5. Add boss conversion tests before judging any control, opener, trap, or mark
   skill complete.
6. Add multiplayer ownership and contribution tests before exposing shared
   Quarry, Ward, protection, or minion behavior.
7. Balance damage, cooldown, cost, and recovery seeds only after each style can
   complete its loop and survive a representative boss telegraph.
8. Test passive budgets at ranks 1, 4, and 8. Rank 1 must expose the interaction,
   rank 4 must feel materially committed, and rank 8 must compete with another
   passive rather than becoming an automatic purchase.

## Acceptance Tests

For every intended style:

- complete a dense ordinary pack without relying on a second class;
- fight a stationary boss, mobile boss, and add-spawning boss;
- demonstrate the setup and payoff in the combat log and advanced tooltip;
- survive at least two boss telegraph cycles using the documented class tools;
- verify the low-rated activity remains completable but visibly slower;
- combine one active and one passive with an unconventional second-class state;
- confirm removing all preferred talent combos weakens but does not break it;
- confirm rank 1 contains the mechanic and rank 10 does not erase its failure
  state;
- compare each defining passive at ranks 1, 4, and 8 and verify every point
  changes its resolved tooltip and server result;
- build against the final level-cap point budget and confirm maximizing the
  style necessarily leaves several passives across the chosen classes untouched.
- at the level-100 reference, confirm the style's critical or damaging-ailment
  engine supplies necessary sustained throughput and that removing both makes
  the same current-weapon build fail;
- test committed Health above 10,000 and unarmored Magic Shield above 20,000
  without allowing either pool to ignore representative boss telegraphs.
- test sustained damage, ramp, and realistic uptime against an ordinary
  two-million-Health level-100 boss profile; do not approve a style from one
  theoretical maximum hit.
