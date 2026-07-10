# VRPG Writing Style Guide

## Purpose

VRPG borrows its systems (stats, rarities, affixes, currency, runewords, gems)
from Mine-and-Slash-style ARPGs as conceptual scaffolding only (see
`docs/mine-and-slash-stat-conversion.md`). The names and flavor text for those
systems are still generic ARPG placeholders today (`Common`/`Rare`/`Unique`,
`Heavy-Handed`, `runewords`, `gems`, `currency`). This guide sets the voice and
rules for replacing that placeholder text with something that reads as native
to Vintage Story, without touching any mechanics, stat values, or JSON schema.

The design rationale behind every rule here is in
`docs/superpowers/specs/2026-07-10-arpg-lore-style-guide-design.md`. The
supporting lore research this guide draws on lives in `docs/writing/lore/`.

## Voice Pillars

Pulled directly from the base game's own class flavor text (full quotes in
`lore/classes.md`):

1. **Short sentences.** Every vanilla class quote ends its thought within one or
   two sentences, the way `"Leave behind your old life. Crawl into the new
   world"` does. No line here runs on.
2. **Second person or first person as the subject, never marketing voice.**
   `"Loose the arrow, kill the beast."` addresses the player directly. Nothing
   in vanilla text describes itself in the third person the way ad copy would
   ("Experience the thrill of the hunt").
3. **Plain, mostly Anglo-Saxon vocabulary.** `gear`, `crawl`, `beast`, `child`,
   `machine`, `hard gaze`, rather than `mechanism`, `optimize`, `commence`,
   `traverse`. If a Latinate word has a shorter, plainer synonym, use the
   plainer one.
4. **Matter-of-fact restraint, even when the content is bleak.**
   `"She is gone, but you remain."` states loss without dramatizing it further.
   The bleakness comes from the plain statement, not from adjectives piled on
   top of it.
5. **No exclamation points, no epic-fantasy bombast.** Nothing in vanilla text
   escalates into "at last, the ancient evil awakens!" register. Keep new text
   at the same volume as the quotes above.

## Legible Before Flavorful

Every renamed term must still communicate its relative function or power at a
glance, the same way `Common`/`Rare`/`Unique` or `fire`/`cold`/`lightning` do
today. A player should be able to guess "this is probably better than that" or
"this is probably the burn one" without opening a tooltip. Flavor must never
obscure that.

This is the tie-breaker any time a lore-accurate name and a generic-but-clear
name are in tension: adjust the lore-accurate name until it reads clearly.
Never fall back to the generic term just because the flavorful one is hard to
parse at a glance.

- **Pass:** `Rust` for the old "chaos" element. It still reads as
  corruption/damage-over-time on sight, while being grounded in the Rust World.
- **Fail:** naming that same element `Verdigris`. That is technically about
  corrosion, but nobody will read it as an elemental damage type without a
  tooltip.

## DO NOT

### No corporate or marketing jargon

**Bad:** "Unlock your build's full potential with seamless synergy across your
endgame content."
**Why:** Nothing in Vintage Story's voice describes itself from the outside.
This is ad copy, not in-world text, and it breaks the fiction that any of this
was written by someone living in the world.
**Good:** "Every point spent here makes the next one cheaper."

### No hyphen spam

**Bad:** `Heavy-Handed`, `Blood-Soaked-Vengeance`.
**Why:** Vanilla trait names (`Focused`, `Resourceful`, `Fleetfooted`,
`Heavyhanded`) are single words or closed compounds, never hyphenated chains.
Hyphens read as generator output, not hand-written text.
**Good:** `Heavyhanded`, the base game's own spelling of the same word.

### No em-dash spam

**Bad:** "The gear turns — faster now — a warning you can't ignore — not
anymore."
**Why:** Every quoted vanilla line uses periods and commas for rhythm. Dashes
are a writer's crutch for pacing that plain sentences do without help.
**Good:** "The gear turns faster now. That is a warning. Do not ignore it."

### No compound-word spam

**Bad:** `Doomfire`, `Stormrender`, `Bloodfang`.
**Why:** These read as fantasy-name-generator output, not native VS
vocabulary. Vanilla never stacks two nouns into an invented word for a name.
**Good:** Use a real word or a short, real phrase instead: `Kindling`,
`Discharge`, `Cracked Gear`.

### Tone consistency

**Bad:** A grim, Rust-corrupted weapon sitting next to a tooltip that jokes
about the player's inventory management.
**Why:** Vanilla text stays at one register throughout a given system. Mixing
jokey and grim within the same system reads as two different writers, not one
coherent world.
**Good:** Keep humor, if any, confined to genuinely light systems (cosmetic
flavor, non-combat crafting) and out of anything Rust- or combat-flavored.

## Naming Patterns

Every new name should come from one of these four buckets. Pick the bucket
that matches what the thing actually is in-fiction, not whichever sounds
coolest.

1. **Rust-bleed symptom**: for elements and anything explicitly tied to
   temporal instability. Source: `lore/cosmology-and-elements.md`.
2. **Provenance/pedigree**: for rarity tiers and anything about an item's
   origin (trader-bought, ruin-found, seraph-carried). Source:
   `lore/history-and-factions.md`.
3. **Trade/material**: for affixes and anything a living craftsperson could
   plausibly have made (smithing, tailoring, clockmaking). Source:
   `lore/classes.md`, `lore/history-and-factions.md`.
4. **Clockwork/mechanism**: for socket/gem and rune-equivalent systems, and
   for currency. Source: `lore/economy.md`.

Do not mix buckets within a single name. A "Rust-bleed" element should never
also carry a "trade/material" qualifier, and vice versa. Pick one grounding
per name.

## Before/After Table

Worked renames from the current codebase. These are illustrative for this
pass only; applying them to the actual JSON assets is a separate, future pass.

| Current | Proposed | Bucket | Reason | Legibility check |
|---|---|---|---|---|
| Rarity: `Common` | `Plain` | Provenance | Ordinary make, no history attached | still reads as the base/lowest tier |
| Rarity: `Rare` | `Storied` | Provenance | Bears a documented history or maker's mark | still reads as the middle tier, above "Plain" |
| Rarity: `Unique` | `Relic` | Provenance | Days-of-Old or seraph-attributed artifact | still reads as the top, singular tier |
| Affix: `Heavy-Handed` (`assets/vrpg/vrpg/affixes/heavy_handed.json`) | `Heavyhanded` | Trade/material | Matches the base game's own spelling of the identical trait | unchanged meaning, just the hyphen removed |
| Element: `chaos` | `Rust` | Rust-bleed | Raw, undiluted Rust World exposure | still reads as corruption/DoT damage |
| Element: `lightning` | `Discharge` | Rust-bleed | Overtaxed mechanical/temporal charge venting | still reads as a shock/burst effect |
| Element: `elemental` / `all` | `Bleed` | Rust-bleed | General permeation across every symptom class | still reads as "the one that touches everything" |
| Library category: `gems` | `Fittings` | Clockwork/mechanism | Socketable augment, framed as an engineering part | still reads as "the socket item" |
| Library category: `support_gems` | `Support Fittings` | Clockwork/mechanism | Same bucket, modifies an active skill's Fittings | still reads as "the one that modifies the other one" |
| Library category: `runewords` | `Mechanisms` | Clockwork/mechanism | An assembled multi-part combo bonus, same shape as a runeword | still reads as "the combo bonus you build" |
| Library category: `runes` (individual runeword components) | `Cogs` | Clockwork/mechanism | Individual socketable components that assemble into a Mechanism | still reads as "the small piece that goes into the big one" |
| Library category: `currency` | `Tender` | Clockwork/mechanism | Extends the existing rusty/temporal gear economy rather than inventing new money | still reads as "what you pay with" |

## Index

Consult these when writing anything the table above doesn't already cover:

- `lore/cosmology-and-elements.md`: Rust World, temporal stability, the
  element remap. Go here for anything elemental or Rust-flavored.
- `lore/creatures.md`: drifter tiers, locusts, mundane wildlife. Go here for
  monster-themed affixes, drops, or library entries.
- `lore/history-and-factions.md`: seraphim, traders, ruins. Go here for
  rarity/provenance flavor and NPC-facing naming.
- `lore/classes.md`: the six vanilla classes, quoted directly. Go here first
  whenever you're unsure if a piece of new text sounds right.
- `lore/economy.md`: rusty/temporal gears, trader mechanics. Go here for
  currency, socket/gem, and rune-equivalent naming.
