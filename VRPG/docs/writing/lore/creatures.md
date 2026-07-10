# Creatures

Reference roster for naming monster-flavored affixes, library entries, and drop
tables consistently with what the base game already established. Flagged by
whether the creature is Rust-touched (see `cosmology-and-elements.md`) or mundane
wildlife, since that distinction should drive which naming bucket its flavor text
draws from.

## Rust-Touched

**Drifter**: humanoid, hostile, melee and ranged (thrown rock). Spawns in the
dark below light level 7. Escalates through five named tiers as Rust
concentration rises with depth, plus a storm-only variant:

| Tier | Depth band | Relative danger |
|---|---|---|
| Surface | near the top, only within range of a temporal rift | lowest |
| Deep | mid-depth | low-mid |
| Tainted | deeper | mid |
| Corrupt | deeper still | mid-high |
| Nightmare | deepest | highest |
| Double-headed | anywhere, storm-only | spikes regardless of depth |

Drop flax fibers, rusty gears, and temporal gears at rates that climb with tier.
Their tiering is the cleanest existing precedent for "same creature, more Rust".
Use it as the model for any new Rust-scaled enemy family.

**Locust**: land-dwelling, explicitly tagged "mechanical" in the base game,
climbs walls and ceilings, lives in cave-ceiling nests in large groups. Variants:
Bronze (weakest), Corrupt, Corrupt Sawblade (strongest, drops Metal Parts, Metal
Scraps, and Jonas Parts). The Clockmaker class can hack a Bronze or Corrupt
locust with a Tuning Spear to fight alongside the player. Locusts are the
existing bridge between "Rust-touched" and "mechanical/clockwork", useful when
writing flavor for anything that straddles both naming buckets.

**Bowtorn**: hostile, shoots darts, retreats if the player closes distance.
Begins spawning around the player once stability drops below 25%, gaining a tier
for every further 5% lost. A live, mechanical indicator of local Rust
concentration, not just a random spawn.

**Shiver**: hostile, crawls on the ground making unsettling noise. Minimal
documented lore beyond behavior; safe to extend with new flavor text as long as
it fits "wrong, close to the ground, quiet until it isn't."

**Bell**: hostile, rings itself and summons nearby drifters if the player gets
close. Functions as a trap/alarm creature rather than a direct threat.

## Mundane Wildlife

Neutral or passive, no Rust involvement: Bighorn sheep, wild pig, chicken, fox,
raccoon, hare, gazelle, goat, deer, termites (harmless). Hostile but mundane:
wolf, bear (both actively hunt), hyena (wolf-equivalent for dry climates), bees
(non-lethal, despawn after 2 minutes, cannot be killed). These should never be
written with Rust-bleed flavor. They are ordinary hazards of an ordinary
wilderness, and mixing that up undercuts what makes the Rust-touched creatures
distinct.

## Naming Guidance

When an affix, drop, or library entry is themed after a creature: Rust-touched
creatures license Rust/Discharge/Cold-flavored naming (per the element remap);
mundane wildlife should stick to plain, physical-damage or utility framing
(hide, meat, bone, sinew) with no elemental flavor at all.
