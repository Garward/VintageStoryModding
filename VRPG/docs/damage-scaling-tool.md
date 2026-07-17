# Damage Scaling Tool

`VRPG.Balance` is an offline, deterministic sweep for comparing player damage
profiles with every configured creature level and provisional creature rarity.
It reads the same `scaling/default.json`, skill definitions, and pure scaling
formulas used by the mod. Creature rarity does not need to be enabled in normal
gameplay before its proposed health and damage budgets can be tested.

## Quick Start

From the Modding workspace root:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release --
```

The default run evaluates every loaded skill at rank 1 and maximum rank against
levels 1–100, every configured creature rarity, and a creature with 20 unscaled
vanilla health. It compares unchanged Common weapons at the creature's level,
20 levels behind, and 40 levels behind. It writes:

```text
VRPG/balance-reports/damage-scaling.csv
VRPG/balance-reports/damage-scaling.md
```

The CSV contains every level/rarity row. The Markdown file gives milestone TTK
tables for quick inspection. Generated reports are ignored by Git.

## Useful Runs

Follow a skill's Weapon Damage effectiveness and weapon curve across the
creature-level range:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release -- \
  --skill rust_lance \
  --rank matched \
  --base-health 20
```

Compare current-band, one-band-old, and two-band-old weapons. `weapon-lag`
does not debuff an item; it selects the unchanged required-level baseline used
for that row:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release -- \
  --skill rust_lance \
  --rank max \
  --min-level 80 \
  --weapon-lag 0,20,40 \
  --weapon-rarity common,rare,unique
```

Inspect the no-skill offensive floor with the synthetic `basic_attack` profile:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release -- \
  --skill basic_attack \
  --rank 1 \
  --min-level 80 \
  --weapon-lag 0
```

This supplies exactly `100% Weapon Damage`, one action per second, default
critical stats, and no skill-rank growth. The default provisional damage-race
profile also gives the player `100 Health` and exposes them to a base `2`
creature hit at `0.6667` attacks per second after creature-level scaling.

Test whether an excellent old Rare can bridge one band by giving it representative
affixes:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release -- \
  --skill rust_lance \
  --rank max \
  --weapon-lag 20 \
  --weapon-rarity rare \
  --flat-weapon-damage 10 \
  --additional-weapon-damage 50 \
  --more-weapon-damage 10
```

Test a committed critical build against several vanilla health profiles:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release -- \
  --skill rust_lance \
  --rank max \
  --base-health 20,80,250 \
  --additional-damage 250 \
  --more-damage 40 \
  --flat-crit 1 \
  --additional-crit 400 \
  --crit-damage 220
```

Test an arbitrary complete pre-stat hit instead of weapon and skill coefficients:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release -- \
  --skill rust_lance \
  --rank max \
  --hit-damage 500 \
  --casts-per-second 2
```

Use `--help` for the complete option list.

## Rank Modes

- A number uses that fixed skill rank for every creature level.
- `max` uses the skill's authored maximum rank.
- `matched` maps creature levels 1–100 across skill ranks 1–10. This is a
  reference curve, not a claim that skill ranks are automatically granted at
  those player levels.
- Several modes may be comma-separated, such as `--rank 1,5,max,matched`.

## Report Fields

Each CSV row records:

- scenario, skill, skill rank, creature level, rarity, eligibility, and affix
  slots;
- base Health, encounter Health layer, final Health, level/rarity multiplier,
  creature damage multiplier, XP, base build pressure, and encounter-adjusted
  build pressure;
- weapon required level, actual level gap, rarity scalar, level baseline,
  affixed Weapon Power, and skill Weapon Damage effectiveness;
- noncritical hit, final critical chance, critical damage, and expected damage
  per independent hit;
- hits per activation, expected activation damage, activations and hits per
  second, expected DPS, fractional and whole hits to kill, and
  expected time to kill;
- provisional incoming damage per hit and second, player survival time, and
  whether the profile wins the representative damage race.

Rows below a rarity's configured minimum level remain in the default report
with `rarityEligible=false`. Use `--include-ineligible false` to omit them.

## Current Level-100 Enemy Health Seed

At level 100 the current creature formula multiplies the entity's unscaled
base Health by `412.16`. “Average level-100 enemy Health” therefore depends on
which vanilla or authored creature base is being scaled. For the 20-Health
ordinary reference used by default:

| Creature rarity | Level-100 Health |
| --- | ---: |
| Ordinary | 8,243.2 |
| Hardened | 11,128.3 |
| Marked | 15,249.9 |
| Warped | 21,432.3 |
| Named | 32,972.8 |

An 80-base-Health creature starts at `32,972.8` as Ordinary and reaches
`131,891.2` as Named. A 250-base-Health boss profile starts at `103,040`
before boss-specific phases or modifiers. Reports should always name the base
Health profile rather than presenting one of these as the universal average.

A normal level-100 boss stress profile applies a separate `×20` encounter
Health layer to that 250-base creature, producing `2,060,800 Health` before
rarity. Model it together with the high-critical example using:

```bash
dotnet run --project VRPG/tools/VRPG.Balance -c Release -- \
  --skill basic_attack \
  --rank 1 \
  --min-level 100 \
  --max-level 100 \
  --weapon-lag 0 \
  --base-health 250 \
  --encounter-health-multiplier 20 \
  --weapon-effectiveness 500 \
  --additional-crit 1900 \
  --crit-damage 500 \
  --casts-per-second 1 \
  --player-health 10000
```

This reaches `100%` final critical chance from the five-percent base and deals
about `7,534 DPS` with a plain level-100 Common weapon before other damage or
weapon modifiers. Add `--additional-damage 500` to represent a sixfold generic
damage package; uninterrupted TTK falls from roughly 274 seconds to 46 seconds.
This deliberately leaves boss downtime and ailment damage visible as additional
budgets instead of hiding them in the base hit.

## Formula Ownership

Runtime creature scaling and this tool both call `ScalingMath`. Do not copy its
health, damage, tier, rarity, XP, or weapon formulas into the CLI. Any future
change must update the shared formula and tests so reports cannot silently drift
from gameplay.

Critical chance follows the current design contract:

```text
(5% base + Flat Critical Chance)
× (1 + summed Additional Critical Chance)
× More Critical Chance
```

Flat Crit is intended primarily for gear and gear upgrades. Additional Crit is
the common class, talent, attribute, affix, and Fitting layer.

Weapon damage follows the current design contract:

```text
level base = configured base × 1.035 ^ (weapon required level - 1)

Weapon Power =
    (level base + Flat Weapon Damage)
    × (1 + summed Additional Weapon Damage)
    × product of More Weapon Damage
    × rarity power scalar

skill base hit = Weapon Power × rank-resolved skill effectiveness
```

The binding test is not a particular ratio in isolation. At top-end reference
levels, a comparable weapon twenty levels below the encounter must fall outside
the build's acceptable clear-time band without any explicit stale-item penalty.
An exceptional roll may bridge one band, but a weapon forty levels behind
should be plainly noncompetitive.

`creatureBuildPressureMultiplier` divides the creature's health growth by a
plain current-level weapon's growth. It intentionally ignores the simulated
weapon lag, rarity, and affixes so every scenario reports the same underlying
progression demand for that creature. The initial ordinary curve reaches about
`13.7×` at level 100. The provisional damage-race fields make the synthetic
level-100 no-build profile a formal failure under representative sustained
pressure. Later encounter tests must still cover avoidance, kiting, control,
enemy recovery, leashing, and rift time limits; a spreadsheet cannot prove that
players cannot evade every attack in the real game.

## Current Boundary

This tool tests outgoing direct-hit damage, authored Weapon Power layers, and a
simple incoming damage race. Sequence skills multiply the independently
resolved per-hit result by authored hit count. Channel skills assume a complete
hold followed by their authored cooldown, so `hitsPerSecond` includes that duty
cycle; resource depletion and early release are not simulated yet. A
`--casts-per-second` override means activations per second, not individual hits.
Its damage-race inputs are explicit provisional
budgets, not a replacement for the eventual armor and defense resolver. It does
not yet simulate armor, resistance, penetration, ailments, resource downtime,
movement loss, area target count, boss phases, party scaling, weapon source-
level generation, unique rule overrides, avoidance/control uptime, or creature
affixes. Add each only after its runtime contract exists, then share that runtime
math rather than inventing a report-only approximation. Use `--damage-model
legacy` only to compare the old prototype flat-damage fields during migration.
