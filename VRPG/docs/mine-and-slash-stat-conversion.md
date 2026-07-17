# Mine-and-Slash Stat Conversion Notes

Mine-and-Slash is used as design inspiration only. The local reference checkout does not include a license, so VRPG should not port code or generated assets verbatim.

## Data Shape

The original stat set is best represented as families plus axes:

- Elements: physical, fire, cold, lightning, chaos, elemental, all.
- Weapons: axe, staff, trident, sword, bow, crossbow.
- Play styles: strength/melee, dexterity/ranged, intelligence/magic.
- Resources: health, magic shield, mana, blood. VRPG intentionally omits the source mod's Energy resource.
- Dynamic registries: spell tags, effect tags, ailments, professions, drop categories.

VRPG stores these under `assets/*/vrpg/statfamilies/` so content packs can add or replace the taxonomy without changing C#.

## Core Stat Math

Use this conversion target:

```text
aggregated = (base + flat) * (1 + increased / 100)
if stat uses statMore:
    aggregated *= product(1 + more / 100)
value = clamp(aggregated, min, hardCap)
```

For damage-increase stats, keep the `more` product separate and apply it during damage resolution:

```text
damageMore = product(1 + more / 100)
```

Only flat modifiers should scale with item/area level. Percent and more modifiers should not scale.

## Damage Layers

Recommended order:

1. Flat damage.
2. Damage conversion.
3. Damage as extra element.
4. Additive increased/reduced damage.
5. Damage-over-time multiplier.
6. Critical damage.
7. Double damage.
8. Damage taken as.
9. Armor/physical/elemental mitigation.
10. Damage reduction.
11. Damage suppression.
12. Flat damage reduction.

## Weapon Power

Weapon Power is VRPG's common damage foundation for attacks, spells, ailments,
constructs, and basic attacks against RPG-eligible targets. It is separate from
Vintage Story's native attack power.

```text
levelBase = baseWeaponPower × (1 + growthPerRequiredLevel) ^ (requiredLevel - 1)

weaponPower =
    (levelBase + Flat Weapon Damage)
    × (1 + summed Additional Weapon Damage)
    × product(1 + More Weapon Damage)
    × weaponRarityPowerScalar

skillBaseHit = weaponPower × skillWeaponDamagePercent / 100
```

The initial seeds are base `10`, growth `3.5%`, and Common/Rare/Unique rarity
power scalars `1.00/1.15/1.20`. Required level is permanent item data. Target
level never reduces an existing item. “Weapon lag” in balance reports only
compares an unchanged older number with a newer encounter.

Skill-specific Flat, Additional, and More damage resolve after `skillBaseHit`.
Do not multiply native material attack power into the level baseline: that
would count the weapon base twice. Rarity may govern affix count and roll
quality, but its direct Weapon Power scalar must remain the smallest and final
layer.

Track the progression requirement explicitly:

```text
buildPressure = creatureHealthGrowth / currentLevelPlainWeaponGrowth
```

The initial ordinary-monster curve targets roughly `1×/3.6×/6×/9×/13.7×`
at levels `1/40/60/80/100`. This is the multiplier the rest of the build and
its combat loop must overcome before rarity and encounter mechanics. A plain
level-100 weapon with no skill or talent investment must fail representative
level-100 combat even though the weapon itself is current.

## Core Attributes

- Strength: health flat, health regen flat, armor percent.
- Dexterity: projectile damage, attack speed, critical access, dodge rating, and the primary-Dexterity Reflex/Evasive Step foundation.
- Intelligence: mana flat, mana regen flat, magic shield percent.

The exact multipliers are in `vrpg:core_and_derived` so we can rebalance them for Vintage Story pacing.

## Implementation Rule

Prefer small data records and small focused systems:

- Stat definitions describe display and caps.
- Stat families describe generated stat categories.
- Gear rolls should reference stat family codes.
- Combat hooks should consume resolved stat totals, not know how gear was generated.
- Native player UI should read the same registry the server uses.
