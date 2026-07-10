# VRPG Writing Style Guide & Lore Index — Design

## Goal

VRPG borrows its systems (stats, rarities, affixes, currency, runewords, gems) from
Mine-and-Slash-style ARPGs as conceptual scaffolding only — see
`docs/mine-and-slash-stat-conversion.md`. The names and flavor text for those systems
are currently generic ARPG placeholders (`Common`/`Rare`/`Unique`, `Heavy-Handed`,
`runewords`, `gems`, `currency`). This pass does not change any mechanics, stat
values, or JSON schema fields. It produces two things:

1. A **style guide** that defines the writing voice for all future VRPG flavor text,
   names, and lore, with a concrete DO-NOT list and a legibility principle.
2. A **lore index** — a small set of reference files summarizing researched Vintage
   Story lore (creatures, cosmology, history/factions, classes, economy) that writers
   consult so names and lore stay consistent with the base game instead of drifting
   into invented fantasy.

Future work (not this pass) will use this guide to actually rename the existing
affixes, rarities, currency, and library categories in `assets/vrpg/vrpg/`.

## Core Principle: Legible Before Flavorful

Every renamed term must still communicate its relative function or power at a glance,
the way `Common`/`Rare`/`Unique` or `fire`/`cold`/`lightning` do now. A player should
be able to guess "this is probably better than that" or "this is probably the burn
one" without reading a tooltip. Flavor must not obscure that legibility. This is the
tie-breaker whenever a lore-accurate name and a generic-but-clear name are in tension:
adjust the lore-accurate name until it is clear, never fall back to the generic term.

This principle is stated once in the style guide and applies to every naming
decision below and in future renaming passes.

## Cosmology Grounding (reference material, not new mechanics)

Vintage Story already establishes the Rust World as the same world in total temporal
decay, bleeding through into the stable world at points of low temporal stability —
this is what drifters, bowtorns, and temporal storms already are (confirmed via
wiki research into Temporal Stability, Drifter, and Locust pages). VRPG's elemental
damage types are documented as named symptom-classes of that same bleed, not a
classical school of magic:

| Generic ARPG term | Grounded meaning | Legible tell |
|---|---|---|
| Physical | Baseline, no bleed involved | unchanged, still reads as "physical" |
| Fire | Rust-corroded metal/mineral turning reactive under destabilization | still reads as "the burn one" |
| Cold | Stasis-bleed: decay outpacing time, freezing matter mid-collapse | still reads as "the freeze/slow one" |
| Lightning → **Discharge** | Overtaxed mechanical/temporal charge venting (the "massive cogs and the Thunderlord" seen at extreme instability) | "Discharge" still reads as a shock/burst effect |
| Chaos → **Rust** | Raw, undiluted Rust World exposure — the source symptom itself, mapped onto vanilla drifter tiering (surface→deep→tainted→corrupt→nightmare) | "Rust" still reads as corruption/DoT-flavored damage |
| Elemental/All (generic multiplier categories) → **Bleed** | General permeation across all symptom classes | "Bleed" still reads as "the one that touches everything" |

Other systems are deliberately **not** forced through this same myth — that would
flatten the setting into "everything is the Rust World." Each gets its own adjacent
VS anchor instead:

- **Rarity tiers**: reframed around provenance/pedigree (seraphim-carried, pre-collapse
  "Days of Old" relics, Rust-touched finds) instead of generic Common/Rare/Unique,
  while keeping an obvious low-to-high ordering.
- **Currency**: extends the existing rusty-gear/temporal-gear economy already in
  vanilla VS rather than inventing new tender.
- **Affixes**: named after trades/materials (smithing, tailoring, clockmaking)
  instead of generic adjectives, and without hyphenated compounds like `Heavy-Handed`.
- **Runewords/gems/support gems**: the biggest reskin targets, grounded in VS's
  clockwork/mechanical-power system (gears, cogs, tuned mechanisms) instead of
  generic socket-magic. Mechanically these remain exactly what they are today —
  only the presentation layer changes.

## Deliverable 1: `docs/writing/style-guide.md`

Sections:

1. **Purpose** — one paragraph, links back to this design doc.
2. **Voice pillars** — pulled directly from researched vanilla flavor text (class
   descriptions, item lore): second-person address, short declarative/fragment
   sentences, plain Anglo-Saxon-root vocabulary over Latinate/corporate vocabulary,
   matter-of-fact restraint even about horror, no exclamation points, no epic-fantasy
   bombast. Each pillar gets one real VS quote as evidence.
3. **Legible Before Flavorful** — the principle above, stated as a rule with a
   pass/fail example pair.
4. **DO NOT list** — one subsection per rule, each with a bad example, the reason,
   and a good example:
   - No corporate/marketing jargon (`synergy`, `unlock your potential`, `seamless`,
     `power fantasy`, `endgame content`).
   - No hyphen spam (`Heavy-Handed`, `Blood-Soaked-Vengeance`) — prefer a single word
     or an unhyphenated short phrase.
   - No em-dash spam — vanilla VS text uses periods and commas for rhythm, not dashes.
   - No compound-word spam (`Doomfire`, `Stormrender`) — avoid fantasy-name-generator
     mashups; prefer real words or short real phrases.
   - Tone consistency — no jokey tooltip text next to grim item lore in the same
     system.
5. **Naming patterns** — the four grounding buckets from the cosmology section above
   (Rust-bleed symptom, provenance/pedigree, trade/material, clockwork/mechanism),
   with guidance on which bucket to use for which kind of content.
6. **Before/after table** — worked renames pulled from real current asset names:
   - Rarities: `Common` / `Rare` / `Unique` → provenance-based tier names that still
     read low-to-high at a glance.
   - Affix: `Heavy-Handed` (`assets/vrpg/vrpg/affixes/heavy_handed.json`) → a
     trade/material-rooted name without a hyphen.
   - Library category: `runewords` / `gems` / `support_gems` / `currency` → clockwork/
     mechanism-rooted category names.
   - Elements: `chaos` → `Rust`, `lightning` → `Discharge`, `elemental`/`all` →
     `Bleed`.
   Each row: current name, proposed name, one-line reason, confirmation it still
   passes the legibility check.
7. **Index** — one line per lore file below: what it contains and when to open it
   ("go to `creatures.md` for monster-name and affix-flavor grounding", etc.).

## Deliverable 2: `docs/writing/lore/`

- `cosmology-and-elements.md` — Rust World, temporal stability, temporal storms, the
  bleed-through myth, the element remap table (same as style guide's, this is the
  canonical source the style guide's table is copied from).
- `creatures.md` — researched roster (drifter tiers, locust variants, bowtorn,
  shiver, bell, wolf/bear/hyena and other neutral/passive wildlife) flagged
  Rust-touched vs. mundane, for naming monster-flavored affixes and library entries.
- `history-and-factions.md` — the seraphim mystery (blue-skinned amnesiac arrivals,
  no memory of their past), the 9 vanilla trader types, ruins/"Days of Old"
  civilization implications.
- `classes.md` — the six vanilla classes (Commoner, Hunter, Malefactor, Clockmaker,
  Blackguard, Tailor) with their actual flavor text quoted directly, as the primary
  tone reference for writing new flavor text.
- `economy.md` — rusty gears and temporal gears as the existing currency and
  anti-Rust ward, trader stock/delivery mechanics, and a rule that VRPG's currency/
  gems/runewords must extend this existing economy rather than invent a parallel one.

## Non-Goals

- No changes to any `.cs` files, stat math, JSON schema fields, or balance values.
- No actual renaming of existing assets in `assets/vrpg/vrpg/` — the before/after
  table is illustrative and seeds a future dedicated renaming pass.
- No new mechanics (no new elements, rarities, or systems beyond what already exists
  in the codebase today).
