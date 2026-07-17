# VRPG Class Candidates (Draft)

Twenty candidate classes for the Mine-and-Slash-style class layer described in
`docs/mine-and-slash-stat-conversion.md`. This is a selection pool, not a
final roster. A character is assumed to pick **two** of these, so each entry
lists a primary playstyle axis and weapon so combinations can be judged for
overlap and synergy. No skill names yet, only what each attack does. Voice
and naming follow `docs/writing/style-guide.md`; lore grounding cites
`docs/writing/lore/`.

The listed weapon is an itemization theme, not a skill requirement. Every
class skill works with every equipped weapon; the named family merely favors
related affixes in its normal drop weighting.

Each entry:
- **Bucket** — which naming bucket it draws from (Rust-bleed, provenance,
  trade/material, clockwork/mechanism) or "vanilla-class" if it extends one
  of the six base game classes directly.
- **Axis / Weapon** — STR/DEX/INT lean and weapon family from the stat
  conversion notes.
- **Element** — primary damage type, if any.
- Three attacks, described functionally.
- Passive and/or buff/debuff where relevant.

---

## 1. Cutter
*"Cut deep enough, the pain stops mattering."*

**Bucket:** Rust-bleed (extends the temporal gear self-cut mechanic in
`economy.md`). **Axis / Weapon:** STR, knife. **Element:** Physical/Rust.
Spends its own blood resource for power instead of drawing from an external
pool.

- **First:** A stab that costs a flat chunk of current health and deals
  bonus damage scaled to how much was spent.
- **Second:** A short cutting ritual, the same motion used on a temporal
  gear, that trades a burst of health for a brief spike in damage or
  critical chance.
- **Third:** A finishing strike that hits harder the lower the Cutter's own
  health is.
- **Passive:** Dropping below a health threshold grants a window of
  lifesteal instead of just running out.

## 2. Warden
*"Stand where the bleed is worst. Someone has to."*

**Bucket:** Rust-bleed (protective). **Axis / Weapon:** STR/INT hybrid,
trident. **Element:** Physical, anti-Rust utility.

- **First:** A trident strike that restores a sliver of the Warden's own
  temporal stability on hit.
- **Second:** Plants a ward that pulses stability regeneration to nearby
  allies.
- **Third:** A strike that deals bonus damage specifically against
  Rust-touched enemies (drifters, bowtorns, locusts).
- **Passive:** While the Warden's stability is high, nearby allies take
  reduced damage from Rust-touched enemies.
- **Buff:** An aura that raises party temporal stability regeneration.

## 3. Trapper
*"Set it, back off, let the trap do the killing."*

**Bucket:** creature-grounded (bowtorn). **Axis / Weapon:** DEX, crossbow.
**Element:** Physical, bleed.

- **First:** A crossbow bolt that applies a bleed.
- **Second:** Plants a mechanical trap that roots the first enemy to step
  on it, mirroring a bowtorn's dart in reverse.
- **Third:** Fires while stepping backward, dealing damage and gaining a
  short burst of speed, the same retreat behavior bowtorns show.
- **Passive:** Bonus damage against rooted or snared enemies.

## 4. Handler
*"Bronze or corrupt, it still comes when I tune it right."*

**Bucket:** clockwork/mechanism, extends the Clockmaker's locust-hacking.
**Axis / Weapon:** INT, tuning spear. **Element:** Discharge.

- **First:** A tuning spear strike with a chance to hack a weak mechanical
  enemy, turning it to fight alongside the Handler for a limited time.
- **Second:** Commands an active hacked minion to focus a target.
- **Third:** Overtunes a minion, spending its remaining time to detonate
  it for Discharge damage.
- **Passive:** Can keep more than one hacked minion active at once, once
  unlocked.

## 5. Smith
*"Hold still. This will only take a moment to fix."*

**Bucket:** trade/material. **Axis / Weapon:** STR, hammer. **Element:**
Physical.

- **First:** A heavy hammer swing with a chance to stagger.
- **Second:** A defensive parry that punishes an incoming attack.
- **Third:** Field-repairs its own armor mid-fight, restoring armor value
  and sending out a small shockwave.
- **Passive:** Armor value climbs the longer the Smith stays in
  uninterrupted melee.

## 6. Corroder
*"Everything rusts eventually. I just don't wait."*

**Bucket:** Rust-bleed. **Axis / Weapon:** INT, staff. **Element:** Rust.

- **First:** A staff bolt that applies stacking Rust corrosion.
- **Second:** A pulse that spreads existing Rust stacks from one target to
  nearby enemies.
- **Third:** Detonates all Rust stacks on a target for burst damage.
- **Passive:** The Corroder's own temporal stability slowly drains to
  boost Rust damage dealt.

## 7. Kindler
*"Rusted metal wants to burn. I only need to ask it right."*

**Bucket:** Rust-bleed. **Axis / Weapon:** STR/INT hybrid, sword.
**Element:** Fire.

- **First:** Strikes the weapon against the target to spark an ignite.
- **Second:** Ignites the ground around the Kindler in a short radius.
- **Third:** Throws a molten shard for burst fire damage.
- **Passive:** Fire damage dealt increases as the target's remaining
  health drops.

## 8. Frostbitten
*"The cold doesn't rush. Neither do I."*

**Bucket:** Rust-bleed. **Axis / Weapon:** INT, staff. **Element:** Cold.

- **First:** A cold bolt that slows on hit.
- **Second:** Freezes a small area, briefly halting anything caught in it.
- **Third:** A shatter strike that deals bonus damage to frozen or slowed
  enemies.
- **Passive:** Standing still builds a stack that empowers the next cold
  attack.

## 9. Sparker
*"You feel it before you hear it."*

**Bucket:** Rust-bleed. **Axis / Weapon:** DEX/INT hybrid, staff.
**Element:** Discharge.

- **First:** A bolt that arcs between nearby enemies.
- **Second:** Charges the Sparker, so the next few attacks discharge extra
  damage.
- **Third:** An overload strike that costs the Sparker a small amount of
  health to vent a large single-target burst.
- **Passive:** Getting hit builds charge, usable as a retaliation burst.

## 10. Provider
*"Eventually your child will die, and you'll find yourself providing for
someone else's."*

**Bucket:** vanilla-class (Hunter). **Axis / Weapon:** DEX, bow.
**Element:** Physical.

- **First:** An aimed shot that deals more damage the farther the target.
- **Second:** A rapid volley of several weaker shots.
- **Third:** A called shot that applies bleed and lowers the target's
  accuracy.
- **Passive:** A stacking damage bonus for time spent at range without
  being hit.

## 11. Butcher
*"Meat is meat. Waste none of it."*

**Bucket:** creature-grounded (mundane wildlife). **Axis / Weapon:** STR,
axe. **Element:** Physical, bleed.

- **First:** A cleave that applies bleed.
- **Second:** An execute that deals bonus true damage to low-health
  targets.
- **Third:** A hamstring strike that slows the target and increases bleed
  damage it takes.
- **Passive:** Killing a bleeding enemy grants a brief damage buff.

## 12. Pilferer
*"Half of surviving is knowing what to take, and when."*

**Bucket:** vanilla-class (Malefactor); reuses the canon trait name.
**Axis / Weapon:** DEX, dagger. **Element:** Physical, Rust (toxin).

- **First:** A poison stab that applies a damage-over-time.
- **Second:** A bonus-damage strike from behind or out of stealth.
- **Third:** A hit that steals a tick of the target's active buff or
  resource.
- **Passive:** Harder to detect while not attacking.

## 13. Revenant
*"I got up again. I don't know why either."*

**Bucket:** provenance (seraphim). **Axis / Weapon:** INT/STR hybrid,
sword. **Element:** Bleed.

- **First:** A light strike that heals the Revenant for a share of the
  damage dealt.
- **Second:** A brief channel that converts temporal stability into a
  shield.
- **Third:** Once per fight, if a killing blow would land, the Revenant
  survives it at a sliver of health instead, at high resource cost.
- **Passive:** Reduced penalty on death, echoing the seraphim's own
  ability to come back.

## 14. Peddler
*"Buy low here, sell high there. The gears don't care where they came
from."*

**Bucket:** clockwork/mechanism (economy). **Axis / Weapon:** STR/INT
hybrid, thrown gears. **Element:** Physical, Discharge.

- **First:** Throws rusty gears as a cheap, spammable ranged hit.
- **Second:** Throws a temporal gear for a burst of damage that also
  restores a little of the Peddler's own stability.
- **Third:** A haggling strike that temporarily lowers the target's
  defenses.
- **Passive:** Bonus tender dropped from kills.

## 15. Strider
*"Keep moving. Standing still is how the world catches up to you."*

**Bucket:** trade/material (occupational, mobility). **Axis / Weapon:**
DEX, sword. **Element:** Physical.

- **First:** A dash-strike that closes distance and hits.
- **Second:** A shot or swing thrown mid-sprint without breaking stride.
- **Third:** A strike with a damage bonus outdoors, mirroring the
  Hunter's own strength-in-the-wild, weakness-underground split.
- **Passive:** Movement speed increases out of combat and briefly after a
  kill.

## 16. Delver
*"Someone built this. Someone left it. I'm only here for what's left."*

**Bucket:** provenance (ruins). **Axis / Weapon:** DEX/INT hybrid, sword.
**Element:** Physical.

- **First:** A precise strike with bonus critical chance.
- **Second:** An appraiser's mark that increases critical damage the
  Delver deals to the marked target.
- **Third:** A relic-empowered burst attack with a chance to not consume
  its resource cost.
- **Passive:** Increased critical chance on the first hit against any new
  enemy.

## 17. Alarmist
*"Ring it and see who comes."*

**Bucket:** creature-grounded (bell). **Axis / Weapon:** INT, staff.
**Element:** Discharge.

- **First:** A resonant strike that marks the target, drawing nearby
  enemies' aggro onto it.
- **Second:** An area toll that staggers everything caught in its radius.
- **Third:** A controlled ring that pulls weaker enemies toward its
  center.
- **Passive:** Reduced damage taken while surrounded by three or more
  enemies.

## 18. Tainted
*"Depth doesn't scare me anymore. I've been there."*

**Bucket:** Rust-bleed (drifter tiering). **Axis / Weapon:** STR, axe.
**Element:** Rust.

- **First:** A series of strikes that speeds up the more Rust the Tainted
  has taken on.
- **Second:** A howl that lowers nearby enemies' resistance to
  instability effects.
- **Third:** An overextended, unstable hit that deals massive damage and
  costs the Tainted some of their own health.
- **Passive:** Damage increases as the Tainted's own temporal stability
  drops.

## 19. Mender
*"Making a fitting garb, so we may walk with grace once more."*

**Bucket:** vanilla-class (Tailor); reuses the canon trait name.
**Axis / Weapon:** INT/STR hybrid, needle. **Element:** Physical, support.

- **First:** A needle throw that deals minor damage and applies a
  mend-over-time to an ally, including the Mender.
- **Second:** Stitches a temporary damage shield onto an ally.
- **Third:** A binding strike that lowers the target's attack speed.
- **Passive:** Allies near the Mender take slightly reduced cold damage.

## 20. Crawler
*"Wrong, close to the ground, quiet until it isn't."*

**Bucket:** creature-grounded (shiver). **Axis / Weapon:** DEX, dagger.
**Element:** Physical.

- **First:** A low lunge that deals bonus damage if the target hasn't
  acted yet in the fight.
- **Second:** An unsettling sound that makes nearby enemies briefly more
  likely to miss.
- **Third:** A fast flurry of close-range strikes.
- **Passive:** Harder to detect or target from range; bonus damage on
  openers from stealth.

---

## Coverage Notes

- **Roles represented:** melee STR (Cutter, Smith, Butcher, Tainted),
  ranged DEX (Trapper, Provider, Strider, Crawler), INT casters (Corroder,
  Kindler, Frostbitten, Sparker), support/hybrid (Warden, Handler,
  Revenant, Peddler, Alarmist, Mender), stealth/DEX hybrid (Pilferer,
  Delver).
- **Vanilla-class ties:** Provider (Hunter), Pilferer (Malefactor), Mender
  (Tailor) reuse canon trait names directly. Handler extends Clockmaker's
  locust-hacking. Smith, Warden, and Strider are new but sit in the same
  space as Blackguard/Commoner without duplicating them.
- **Elements covered:** Physical (most), Fire (Kindler), Cold
  (Frostbitten), Discharge (Handler, Sparker, Alarmist, Peddler), Rust
  (Cutter, Corroder, Tainted), Bleed (Revenant).
- **No seventh element invented**, per `cosmology-and-elements.md`.
- **Self-harm/blood resource** used deliberately by Cutter and lightly by
  Sparker and Tainted, extending the temporal-gear cut-to-heal mechanic
  from `economy.md` rather than inventing a new blood magic system.
