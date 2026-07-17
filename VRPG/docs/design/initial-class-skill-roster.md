# Initial Class Skill Roster

Status: working draft for the first six VRPG classes.

The implementation-facing active/passive contracts, numeric seeds, boss and
mob ratings, and acceptance tests live in the
[first six class skill specification](first-six-class-skill-spec.md). This file
remains the higher-level roster, talent-combo catalog, and gamechanger coverage
plan. Where a seed mechanic differs, the detailed specification is canonical.

This roster follows the [writing style guide](../writing/style-guide.md), the
[lore index](../writing/lore/), and the dual-class rules in the
[high-level design](high-level-design.md). Names are short and mechanically
legible. Rust, Corrosion, and Corruption retain their distinct meanings.

## Roster Rules

- **A class is incomplete unless a player can main it in at least two, and
  preferably three, mechanically distinct ways.** Different styles must change
  the repeated combat loop, not merely exchange one damage stat for another.
- These are **intended styles, not subclasses, specializations, or exclusive
  lanes**. They are examples used to prove the kit has depth. Players may mix
  their pieces, ignore them, or discover an unconventional loop the roster did
  not predict.
- Every style states its setup, repeated action, payoff, reset or failure
  state, survival plan, and intended player appeal.
- Skill count is an output of satisfying those styles, not a fixed content
  budget. The current estimate is roughly five actives and nine class passives
  per class, but a class receives another skill whenever one style cannot yet
  stand on its own.
- A character chooses two classes and normally selects four frequent actions
  from both kits. Long-duration summons, wards, and stances may use the
  extended bar without becoming mandatory rotational buttons.
- Every class offers a primary action, setup or control, payoff, and
  defensive or utility action. These roles may overlap when that supports the
  class loop.
- Skill ranks provide modest numerical growth. Unlocking another interaction
  should change a build more than adding another rank.
- Class passives live in the Skills page. The global talent tree supplies
  generic routes, stats, defenses, and gamechangers rather than duplicating
  class kits.
- Class passives are eight-rank point sinks, not a checklist the player is
  expected to complete. A finished two-class build should maximize only a few
  defining passives, partially invest in some support passives, and leave
  several untouched. Rank 1 grants access to an interaction; sustained
  investment supplies its real power.
- At least one passive per class should create a bridge to broad tags or
  states other classes can use. No class should require one specific partner.
- Skills and ordinary class passives should not contain hidden checks for a
  named style. They react to explicit tags, resources, statuses, positions, or
  combat events so unexpected combinations work naturally.
- Do not add arbitrary mutual exclusions to protect an intended style. Reserve
  exclusions and major tradeoffs for clearly labeled gamechangers whose purpose
  is to change the rules of a build.
- A passive should not exist only to grant a small generic percentage. Raw
  stat filler belongs on the talent tree or gear.
- Every class has a shared defensive tool, and every style explains how it
  stays alive when played as the main class. Range alone is not a full defense
  plan. Healing alone is not a full defense plan. Avoidance, mitigation,
  control, recovery, and escape should reinforce one another.
- Delivery variants are specializations, not automatically separate skills.
  For example, Cinder Bombardment should be a ground-targeting specialization
  of Cinder rather than a second nearly identical active.
- Skills are weapon-agnostic. The listed weapon identifies a favored affix
  family, not a requirement or effectiveness multiplier. Weapon affix weighting
  should make thematic combinations common without preventing rare off-pattern
  rolls or item modification from supporting unconventional builds.

## Class Name Review

| Class | Verdict | Reason |
| --- | --- | --- |
| Smith | Keep | Plain trade name. Immediately implies hammer, armor, and repair. |
| Trapper | Keep | Plain occupation and exact mechanical promise. |
| Pilferer | Keep | Existing Vintage Story trait name. Stronger and more grounded than a generic rogue label. |
| Warden | Keep | Plain protective role. Its kit must protect against ordinary combat as well as temporal danger. |
| Corroder | Keep | Unusual but mechanically exact: this class causes Corrosion through Rust damage. |
| Handler | Keep | Plain Clockmaker-adjacent role. It describes command without generic summoner language. |

## Style Completeness Gate

A style is ready for implementation only when all six questions have concrete
answers:

1. What does the player do first when a fight starts?
2. What action do they repeat while the build is working?
3. What visible state are they building toward?
4. What is the satisfying payoff?
5. What forces them to reposition, rebuild, or change decisions?
6. How do they survive a boss that cannot simply be killed or permanently
   controlled before it acts?

The second class should improve or bend these loops. It must not be required to
repair a missing combat loop or missing defense.

Passing this gate proves that several coherent builds are visible. It does not
define the complete set of valid builds. Balance testing should preserve an
unconventional build when it uses real mechanical interactions and remains
within the power budget; unfamiliarity alone is not a reason to remove it.

## Smith

**Promise:** Stay in melee, stagger enemies, and turn protection into force.

**Stat lean / favored affixes:** Strength; hammers favor Stagger, guard, and
close-hit support. **Damage:** Physical.

### Build Styles

#### Breaker

For players who enjoy setup and a heavy payoff. Open with Hammer Blow, build
visible Stagger, then use Fracture at the right threshold. Consuming Stagger
opens a short Physical vulnerability and starts the next setup faster. Bosses
retain the damage payoff but gain control resistance so they cannot be locked.
The Breaker survives through interrupted attacks, brief armor gained on a
successful break, and deliberate spacing between Fractures.

#### Guard

For players who enjoy timing and counterplay. Reinforce before pressure, Brace
through a telegraphed hit, then answer with a counter and Hammer Blow. A missed
Brace produces no counter and leaves a recovery gap. The Guard survives through
prevented damage, armor, and correct timing rather than passive health inflation.

#### Bruiser

For players who want a steady, low-complexity melee main. Stay in contact with
Hammer Blow, keep Tempered armor active, and use Fracture as regular area clear
rather than waiting for a perfect break. Leaving melee drops the built state.
The Bruiser survives through sustained armor, bounded recovery while Reinforced,
and a short armored advance that keeps ranged enemies from invalidating the loop.

### Active Skills

1. **Hammer Blow** — The low-cooldown primary. Strike a short area and build
   Stagger. A direct hit against an already Staggered enemy deals more posture
   damage rather than merely scaling raw damage.
2. **Brace** — Guard for a short, readable window. Preventing a hit triggers a
   hammer counter and briefly improves the Smith's armor. Missing the timing
   grants no counter.
3. **Reinforce** — Fasten temporary protection in place. Gain temporary armor
   without repairing item durability. This distinction preserves vanilla
   equipment wear and the crafting economy.
4. **Fracture** — Slam the ground around the Smith. Deal area damage and
   consume Stagger from affected enemies for a stronger break. This is the
   setup payoff and current `Fracture Pulse` should be renamed to it.
5. **Advance** — Take a short armored step toward the aimed enemy and strike.
   This is gap closing and repositioning, not a long invulnerable dash.

### Class Passives

- **Tempered** — Sustained melee contact builds temporary armor. The stacks
  fall away after leaving combat.
- **Heavyhanded** — Hammer Blow creates more Stagger and pushes lesser enemies
  farther.
- **Reprisal** — A successful Brace counter shortens the recovery of Hammer
  Blow and Fracture.
- **Sound Work** — Reinforce lasts longer and grants a smaller share to nearby
  allies.
- **Breaker** — Fracture leaves a short Physical vulnerability after consuming
  enough Stagger. This gives other Physical builds a bridge into the Smith's
  setup.
- **Follow Through** — Consuming Stagger empowers the next Hammer Blow instead
  of adding another independent damage proc.
- **Set Feet** — A successful Brace makes the Smith harder to move or control
  for a short time.
- **Close Work** — Staying close to enemies strengthens Tempered, while leaving
  melee clears it faster.
- **Patchwork** — Reinforce supplies bounded recovery over its duration. It
  never repairs equipment durability.

## Trapper

**Promise:** Choose the ground, stop movement, and punish enemies for crossing it.

**Stat lean / favored affixes:** Dexterity; crossbows favor projectile, Bleed,
and placed-effect support. **Damage:** Physical and Bleed.

### Build Styles

#### Bleeder

For players who enjoy damage over time and target management. Deepen Bleed with
Barbed Shot, preserve distance with Snare, then Cull when the target is prepared
or low. Reapplying blindly wastes duration or stacks, so target switching
matters. The Bleeder survives through range, Parting Shot, Snare, Evasive Step
when Dexterity-primary, and bounded recovery when striking a deeply Bleeding
target. The recovery has an internal cooldown and remains useful against bosses.

#### Engineer

For players who enjoy planning space and chain reactions. Set Snares and Blast
Traps along approach lanes, draw enemies through them, then use one trigger to
accelerate the others. Poor placement costs time and charges. The Engineer
survives through rooted lanes, forced movement, warning from armed traps, and
the ability to recover one misplaced trap when enemies take another route.

#### Skirmisher

For players who enjoy constant motion and direct aim. Alternate direct shots
with Parting Shot, hold a preferred distance band, and use Cull as a precision
payoff rather than a DoT execute. Getting cornered removes the range benefit and
forces a Snare or escape. Survival comes from movement, brief evasion after
Parting Shot, slows, and Evasive Step when Dexterity-primary.

### Active Skills

1. **Barbed Shot** — The low-cooldown primary. Fire a bolt that applies Bleed.
2. **Snare** — Place a visible mechanical trap. The first enemy to cross it is
   Rooted. Bosses are slowed or build control resistance instead of being held
   indefinitely.
3. **Parting Shot** — Fire while stepping backward. The shot gains damage when
   it passes through the Snare area or hits a controlled target.
4. **Cull** — A deliberate finishing shot. Deal more damage for each relevant
   condition on the target, such as Bleeding, Rooted, or low health. Its base
   hit remains usable when solo setup is imperfect.
5. **Blast Trap** — Place a short-lived trap that deals Physical area damage.
   An enemy or another trap reaction may trigger it, enabling a true trap main.

### Class Passives

- **Patient** — An armed trap grows stronger for a bounded time before it is
  triggered.
- **Barbed** — Repeated Barbed Shots deepen Bleed instead of creating redundant
  independent effects.
- **Quick Hands** — Snare gains an additional charge and can be placed without
  halting movement.
- **Deadfall** — A Snare deals a small Physical area hit when it triggers or is
  destroyed.
- **Quarry** — A target controlled by the Trapper is briefly marked as Quarry
  and takes increased damage from allies as well as the Trapper. The value must
  be bounded for bosses.
- **Bloodletting** — Striking a deeply Bleeding target grants bounded recovery
  with an internal cooldown.
- **Linked Traps** — Triggering one trap advances nearby traps without allowing an
  infinite reaction loop.
- **Fleetfooted** — Parting Shot grants a brief evasion window after movement.
- **Long Sight** — Direct hits in a preferred range band improve Cull; maximum
  possible distance is not automatically optimal.

## Pilferer

**Promise:** Create an opening, take an advantage, and leave before retaliation.

**Stat lean / favored affixes:** Dexterity; daggers favor critical, Opening, and
Tempo support. **Damage:** Physical.

The Pilferer should not use generic poison. Persistent Rust deterioration is
Corrosion and belongs primarily to the Corroder. This class is about openers,
critical access, stolen boons, and escape.

### Build Styles

#### Ambusher

For players who enjoy short burst windows. Use Slip Away or approach unnoticed,
Ambush, spend the opener window on Quick Cut, then disengage before attention
settles. Failing to secure a reset leaves weaker sustained combat. Survival
comes from threat breaking, a brief evasive window, and repositioning. Bosses
allow periodic opener windows without permitting permanent stealth.

#### Thief

For players who enjoy adapting to the enemy. Pilfer a useful boon or a fallback
armor or speed value, then fight according to what was taken. Precise hits
extend the stolen advantage. An invalid target still supplies a smaller fallback
so the build never collapses. Survival comes from stolen defenses, reduced
enemy offense, and Slip Away when the wrong advantage was taken.

#### Duelist

For players who enjoy fast sustained melee and reactive defense. Build Tempo by
alternating Quick Cut and Flurry, spend it on a critical window, and avoid
breaking rhythm through misses or retreat. Survival comes from brief dodge or
parry windows earned by accurate hits, modest recovery on confirmed criticals,
and Slip Away as an emergency reset. It cannot face-tank through lifesteal.

### Active Skills

1. **Quick Cut** — The low-cooldown primary. A fast close strike with increased
   critical chance against an impaired or unaware target.
2. **Ambush** — Close a short distance and strike. It counts as an opener
   against a full-health, controlled, or recently unengaged target, avoiding a
   brittle requirement for perfect back-facing detection.
3. **Pilfer** — Strip one removable boon from the target. If no boon exists,
   steal a bounded amount of armor or speed instead so the skill remains useful
   in ordinary solo combat.
4. **Slip Away** — Break target attention, step away, and gain a brief evasive
   window. The next eligible attack counts as an opener. This is not the
   Dexterity-primary Evasive Step passive and must have a distinct presentation.
5. **Flurry** — Deliver a short sequence of close strikes. Accurate hits build
   Tempo; missing or striking empty space ends the sequence early.

### Class Passives

- **Furtive** — Enemies notice the Pilferer less readily outside active combat.
- **Opening** — Openers impose a short, explicit defense loss rather than only
  gaining a hidden damage multiplier.
- **Light Fingers** — Pilfered boons last longer and Pilfer recovers faster when
  it successfully removes one.
- **Opportunist** — Critical hits against controlled, Bleeding, Corroded, or
  Corrupted targets gain a bounded benefit. This is the main broad class bridge.
- **Clean Getaway** — Defeating a target shortly after an opener partially
  recovers Slip Away.
- **Second Cut** — Alternating Quick Cut and Flurry builds Tempo faster than
  repeating only one of them.
- **Borrowed Guard** — Defensive properties taken by Pilfer are stronger on the
  Pilferer but remain bounded.
- **Nerve** — Spending Tempo grants a brief reactive defense window rather than
  only more damage.
- **No Witnesses** — Finishing an opener-marked enemy preserves part of the
  opener state for the next nearby target.

## Warden

**Promise:** Hold a safe patch of ground and make nearby allies harder to dislodge.

**Stat lean / favored affixes:** Strength and Intelligence; tridents favor
reach, Ward, and protection support. **Damage:** Physical.

Temporal stability is part of the Warden's flavor and utility, but cannot be
its only value. Every Warden action must remain useful in a stable overworld
fight and in a rift that does not use ordinary stability rules.

### Build Styles

#### Sentinel

For players who enjoy holding chosen ground. Plant Ward, use Drive Back to keep
enemies near its boundary, and Thrust through the lane they form. Moving away
abandons accumulated protection and forces a new setup. The Sentinel survives
through Ward mitigation, control resistance, and displacement rather than high
clear speed.

#### Guardian

For players who enjoy active party protection. Step In when an ally is
pressured, Stand Fast to take attention, and rotate Ward as the group moves. In
solo play Step In targets a location and shields the Warden. Survival comes
from capped Shared Burden, personal guard, and recovery for damage actually
intercepted. It cannot redirect lethal damage indefinitely.

#### Spearhead

For players who want the Warden's safest solo damage style. Use Thrust as a line
primary, Drive Back to align enemies, then advance behind a moved or recast
Ward. Rust-touched enemies add stability and defensive returns, but ordinary
enemies still satisfy the loop. Survival comes from reach, knockback, Ward, and
Magic Shield recovery after clean multi-target thrusts.

### Active Skills

1. **Thrust** — The low-cooldown primary. Drive the trident through a narrow
   line. Hitting a Rust-touched creature grants a small defensive benefit, not
   an entirely separate damage budget.
2. **Ward** — Plant a visible area that reduces incoming damage and restores
   temporal stability. Solo Wardens receive the same protection.
3. **Drive Back** — Sweep nearby enemies away from the Warden. Enemies that
   strike the Ward boundary are briefly slowed.
4. **Stand Fast** — Gain a short personal guard and draw nearby enemies'
   attention. In solo play it remains a defensive cooldown; in a group it
   creates a tanking window.
5. **Step In** — Move to an aimed point or endangered ally and grant a short
   shield to both. With no ally target it remains a personal repositioning tool.

### Class Passives

- **Anchor** — While inside a Ward, resist displacement and recover from control
  effects faster.
- **Shelter** — Ward covers a larger area and gives allies a bounded share of
  the Warden's protection.
- **Watchful** — Hitting an enemy that recently harmed an ally grants a stronger
  defensive return from Thrust.
- **Steady** — High temporal stability improves recovery. In activities without
  ordinary stability, standing in a Ward satisfies the condition.
- **Shared Burden** — A small portion of nearby ally damage is redirected to the
  Warden, subject to a per-hit cap and never able to bypass death protection.
- **Boundary** — Enemies pushed across a Ward edge are slowed, with a per-target
  cooldown.
- **Relief** — Damage safely intercepted through Stand Fast or Step In grants
  bounded recovery.
- **Long Reach** — Clean multi-target Thrusts restore part of Magic Shield.
- **Moving Ward** — Recasting Ward can move the existing area at reduced
  duration instead of always beginning from nothing.

## Corroder

**Promise:** Build Corrosion, move it through a group, then turn the buildup into a burst.

**Stat lean / favored affixes:** Intelligence; staves favor Rust, Fire, ailment,
and Magic Shield support. **Damage:** Rust with Fire payoff.

### Build Styles

#### Corrosion

For players who enjoy damage over time and managing many targets. Apply
Corrosion with Rust Lance, deepen it on durable targets, use Spill to seed a
group, and Collapse only when immediate damage is worth losing the buildup.
Premature Collapse or poor spreading restarts the ramp. Survival comes from
slowing afflicted enemies, Magic Shield gained at a bounded Corrosion threshold,
and Shell as an emergency conversion of nearby buildup into defense.

#### Burn

For players who enjoy area denial and spreading fire. Specialize Cinder into a
ground-targeted burst, ignite prepared areas, and refresh or spread Burn with
new impacts. Corrosion may act as fuel but is not required; Cinder can be the
main skill. Enemies leaving the area force new placement. Survival comes from
range, burning zones that discourage approach, reduced enemy damage while
Burning, and Shell based on active ailments rather than Corrosion alone.

#### Reaction

For players who enjoy alternating buttons, aiming, and critical bursts. Rust
Lance prepares a target, Cinder crits against that state, and a Fire hit opens a
short window for the next Rust crit. Alternating elements builds a visible
Reaction state; repeating one element loses the payoff. The payoff is a compact
burst, not another long DoT. Survival comes from Magic Shield on successful
Reactions, ranged movement, and Shell. This is the highest-action Corroder style.

### Active Skills

1. **Rust Lance** — The low-cooldown primary. An instant aimed Rust hit that
   applies Corrosion in a small area.
2. **Spill** — Move part of a target's Corrosion to nearby enemies. The source
   retains enough stacks that spreading is not a punishment in single-target
   combat.
3. **Cinder** — Launch a Fire projectile that bursts on a creature or surface.
   It gains a clear payoff against Corroded enemies. The current Cinder Orb
   becomes this skill; ground targeting becomes a specialization rather than
   the separate Cinder Bombardment active.
4. **Collapse** — Consume Corrosion on a target for immediate Rust damage. Area
   damage is based on the consumed buildup, while bosses retain a capped
   single-target payoff.
5. **Shell** — Draw strength from nearby Corroded or Burning enemies to gain
   temporary Magic Shield. It consumes or suppresses a bounded portion of that
   buildup, forcing a choice between offense and survival.

### Class Passives

- **Pitting** — Corrosion can build additional stacks on the same target.
- **Lingering** — Corrosion lasts longer and loses stacks less abruptly.
- **Flaking** — A Corroded enemy that dies spreads part of its buildup nearby.
- **Kindling** — Cinder can Burn and gains a stronger interaction with
  Corrosion. It does not turn Corrosion itself into Fire damage.
- **Raw Exposure** — Rust damage rises as temporal stability falls. In rifts,
  an explicit activity exposure value replaces ordinary world stability so
  the passive is never dead or uncontrolled server-wide.
- **Fuel** — Cinder can sustain Burn without requiring Corrosion, while
  Corrosion still improves its initial ignite.
- **Draft** — Cinder becomes ground-targeted and leaves a short burning area.
- **Flashpoint** — A critical Fire hit against a Rust-prepared target starts the
  Reaction window.
- **Backlash** — Completing the alternating Rust and Fire sequence
  restores part of Magic Shield and releases a compact burst.

## Handler

**Promise:** Set a tuned construct loose, direct it precisely, and push it past safe limits.

**Stat lean / favored affixes:** Intelligence; tuning spears favor Discharge,
command, and minion support. **Damage:** Discharge with Cold control.

The class must work without a hackable enemy already present. Its baseline
summon is a prepared Bronze Locust construct. Hacking a suitable wild machine
can be an additional interaction later, not a prerequisite for having a class.

### Build Styles

#### Pack

For players who enjoy broad summoner management. Maintain several weaker
constructs, mark targets with Tuning Strike, and Recall the pack before hazards
or target swaps. Split attention and area damage threaten the loop. Survival
comes from construct interception, Chill around a recalled pack, and the Recall
shield, with hard limits preventing permanent body blocking.

#### Overtuner

For players who enjoy a fast summon-and-sacrifice cycle. Set Loose, Overtune a
construct past safe limits, direct its short damage window, then Scrap or allow
Final Turn to burst. Poor timing wastes construct life and leaves Set Loose
unavailable. Survival comes from shield and repair fragments released when an
Overtuned construct ends nearby, while Recall abandons the explosion to preserve
the summon.

#### Keeper

For players who prefer one durable companion. Maintain one heavily tuned
construct, alternate Tuning Strike with its response attacks, and Recall it out
of danger for repair. Losing it creates a meaningful weak interval. Survival
comes from a capped portion of damage intercepted by the construct, reliable
Chill control, and a stronger Recall shield while only one construct is active.

### Active Skills

1. **Tuning Strike** — The low-cooldown primary. Strike or send a short pulse at
   the aimed target. Active constructs immediately focus it and add a bounded
   response hit.
2. **Set Loose** — Deploy a prepared Bronze Locust for a long duration. Recasting
   replaces the oldest construct unless a passive raises the limit.
3. **Overtune** — Make constructs attack and move faster while steadily losing
   health or remaining duration. The risk is visible before activation.
4. **Recall** — Pull constructs back to the Handler, repair part of their health,
   and grant the Handler a brief shield. This supplies repositioning and solo
   defense without adding another damage button.
5. **Scrap** — End one aimed construct and turn its remaining parts into a
   Discharge burst. Healthy constructs have poor damage efficiency; an
   Overtuned construct near failure gives the intended payoff.

### Class Passives

- **Spare Parts** — A destroyed construct repairs the remaining constructs and
  shortens Set Loose recovery.
- **Pack** — Maintain one additional construct, with a bounded penalty to each
  construct's individual damage.
- **Feedback** — Construct hits build charge. Tuning Strike spends it for a
  small Discharge arc.
- **Cold Frame** — Recalled or idle constructs Chill nearby enemies. The name
  describes a physical part rather than a separate school of magic.
- **Final Turn** — An Overtuned construct releases a visible Discharge burst
  when it expires. Manually replacing a healthy construct does not trigger it.
- **Close Order** — A recalled pack Chills enemies and grants a stronger short
  shield.
- **Useful End** — Scrapping an Overtuned construct returns a bounded portion
  of Set Loose recovery.
- **Keeper** — Maintaining exactly one construct improves its defense and
  response attack while disabling Pack's extra-construct benefit.
- **Hard Case** — A lone construct intercepts a capped portion of damage dealt
  to the Handler and cannot intercept an otherwise lethal hit by itself.

## Talent Combos

Talent combos are planning packages for the global tree. They are not named
recipes, set bonuses, class branches, or requirements shown to the player. A
combo records several ordinary talent families that become more interesting
together and identifies the class styles that prove those families have uses.

The tree should reuse a combo anywhere the same mechanics apply. It should not
create `Corroder Burn Damage`, `Trapper Bleed Damage`, and
`Pilferer Damage to Bleeding` when broad Fire, ailment, damage-over-time, critical, and damage to
afflicted stats express the interactions cleanly.

Talent combo rules:

- A class style works before acquiring its preferred talent combos. Talents
  specialize, accelerate, or bend a complete loop; they do not repair one.
- Nodes refer to explicit stats, skill tags, statuses, positions, and events.
  They never check whether the player belongs to a named intended style.
- One combo may span two nearby specialization pods or a route-border hybrid
  section. It does not need to be one densely connected cluster.
- Tier 1 establishes broad stats. Tier 2 offers a stronger focus or a choice
  such as area versus single target, output versus sustain, or speed versus
  impact.
- Ordinary combo nodes have no downside. Possible gamechangers are authored
  only after ordinary sections work in game, following the talent-tree rules.
- A planned effect may not become a player-facing node until its resolver,
  tooltip breakdown, and server-authoritative combat behavior exist.

### Reusable Combo Catalog

#### TC-01: Armored Pressure

**Ingredients:** Armor, Physical or close-range damage while protected, and
bounded recovery after preventing or enduring a hit.

**Useful to:** Smith Guard and Bruiser, Warden Sentinel and Spearhead, durable
off-meta melee builds.

**Pod choices:** armor versus recovery; close damage versus area damage. Avoid
making maximum armor the only correct path through the section.

#### TC-02: Control and Break

**Ingredients:** Stagger or control buildup, damage against controlled targets,
control duration or boss conversion, and a defensive return when control lands.

**Useful to:** Smith Breaker, Trapper Engineer, Warden Sentinel, Pilferer builds
using Opportunist.

**Pod choices:** faster setup versus stronger payoff; control strength versus
damage against targets that resist full control.

#### TC-03: Held Ground

**Ingredients:** placed-skill area, duration, charges or recovery, damage inside
an owned area, and defense or resource recovery while standing in one.

**Useful to:** Trapper Engineer, Warden Sentinel, Corroder Burn, unconventional
players combining traps, Ward, or ground Cinder.

**Pod choices:** larger area versus longer duration; placement frequency versus
personal defense in the area.

#### TC-04: Projectile Precision

**Ingredients:** projectile damage, useful projectile speed or reach, critical
access, and an area-versus-single-target split.

**Useful to:** all Trapper styles, Corroder Burn and Reaction through Cinder,
Handler variants that deliver Tuning Strike as a projectile.

**Pod choices:** faster projectiles and direct-hit output versus area and impact
coverage. Projectile speed must not silently reduce ground-target usability.

#### TC-05: Evasive Tempo

**Ingredients:** evasion, movement speed, recovery after a deliberate movement
skill, and a short offensive window after repositioning.

**Useful to:** Trapper Skirmisher and Bleeder, Pilferer Ambusher and Duelist,
mobile Smith or Warden hybrids.

**Pod choices:** safer movement versus stronger follow-up. Evasive Step remains
a Dexterity-primary foundation and is not required to enter this combo.

#### TC-06: Ailment Pressure

**Ingredients:** ailment application, duration, damage over time, damage against
afflicted enemies, and bounded defense or recovery against enemies carrying an
ailment applied by the player.

**Useful to:** Trapper Bleeder, Corroder Corrosion and Burn, Pilferer using
Opportunist, future Cold or Discharge ailment builds.

**Pod choices:** application and duration versus damage and sustain. Element-
specific pods may branch from a shared ailment trunk without duplicating the
whole package for each class.

#### TC-07: Critical Rhythm

**Ingredients:** common Additional Critical Chance, rare Flat Critical Chance,
critical damage, precision after a noncritical or alternating hit, and a bounded
defensive or tempo return on confirmed crits.

**Useful to:** Pilferer Ambusher and Duelist, Trapper Skirmisher, Corroder
Reaction, precision-focused builds from any class.

**Pod choices:** frequent smaller crits versus rarer stronger crits; offense
versus the defensive return needed by a sustained crit build.

Critical chance uses `5%` base by default. Additional Critical Chance sums, then
multiplies the base, so `100% Additional` produces `10%` final chance before
Flat or More layers. Flat Critical Chance increases the base first and is much
rarer. Initial content should distribute crit investment broadly enough that a
player assembles it across systems rather than finding one mandatory source:

| Source | First-pass role |
| --- | --- |
| Class passives | Conditional identity investments reaching roughly `80–160% Additional` at eight ranks. |
| Ordinary talent pods | Several reachable choices totaling roughly `120–200% Additional` when a build commits to the section. |
| Gear affixes | Common tiered Additional bonuses across several compatible slots; weapon families may weight them differently but never own them exclusively. |
| Fittings and refinement | Targetable Additional bonuses that repair a weak crit budget without replacing drops. |
| Flat Critical Chance | Rare premium gear rolls and gear-upgrade outcomes, usually small fractions or low single percentage points; ordinary class passives and talent nodes do not supply it. |
| More Critical Chance | Exceptional gamechanger territory, not an ordinary affix filler. |

The target journey is approximately `5%` without investment, `10–15%` from
incidental sources, `20–30%` for a committed crit package, and `33–45%` before
Critical Commitment should be seriously considered. These ranges must be
simulated with available critical-damage scaling rather than balanced alone.

#### TC-08: Mixed Symptoms

**Ingredients:** broad Rust-bleed damage, bonuses after using a different damage
type or applying a different ailment, and ailment interaction without converting
every symptom into one element.

**Useful to:** Corroder Reaction, Handler builds combining Discharge and Chill,
future multi-element classes and cross-class pairs.

**Pod choices:** alternating-hit tempo versus ailment interaction. Fire, Cold,
Discharge, and Rust keep their identities; this combo does not flatten them into
generic elemental damage.

#### TC-09: Shield Cycle

**Ingredients:** maximum Magic Shield, recharge delay and rate, bounded shield
restoration from supported combat events, and output while shield remains.

**Useful to:** every Corroder style, Warden Guardian and Spearhead, every Handler
style, Intelligence-primary unconventional builds.

**Pod choices:** larger shield versus faster cycling; stable defense versus a
short output window while shielded.

#### TC-10: Protection and Aid

**Ingredients:** guard strength, shield or protection granted to others, support
area, bounded intercepted damage, and recovery after actual protection occurs.

**Useful to:** Warden Sentinel and Guardian, Smith Guard, Handler Pack, any class
pair choosing to support a small group.

**Pod choices:** stronger personal guard versus a smaller shared benefit. Solo
value must remain on every entry path.

#### TC-11: Commanded Bodies

**Ingredients:** minion damage, speed, durability, command cadence, player
defense while a minion is active, and support effects around owned bodies.

**Useful to:** every Handler style, future summon skills, Warden or gear builds
that support owned constructs without becoming Handler-only.

**Pod choices:** several weaker bodies versus one durable body; command response
versus passive minion output. Exact body-count tradeoffs are gamechanger or
class-passive territory, not ordinary generic nodes.

#### TC-12: Spend and Rebuild

**Ingredients:** payoff-skill output, benefit after consuming an explicit combat
state, recovery after a successful spend, and faster rebuilding without removing
the setup requirement.

**Useful to:** Smith Breaker consuming Stagger, Trapper Engineer consuming trap
charges, Pilferer spending Tempo or an opener, Corroder Collapse, Handler Scrap.

**Pod choices:** larger payoff versus faster rebuild; offense versus defense on
the successful spend. Skills need stable `setup` and `payoff` tags before these
nodes can execute generically.

#### TC-13: Sustained Contact

**Ingredients:** attack or cast cadence, damage against a repeatedly engaged
target, close-range output, and bounded recovery that requires continuing to
land hits.

**Useful to:** Smith Bruiser, Pilferer Duelist, Warden Spearhead, Handler Keeper,
unconventional one-button mains.

**Pod choices:** speed versus impact; single-target persistence versus nearby
area coverage. Recovery must use an internal cap and cannot become infinite
face-tanking.

#### TC-14: Resource Engine

**Ingredients:** Mana maximum, Mana recovery, skill cost efficiency, cooldown or
cast cadence, and resource return after a supported successful action.

**Useful to:** Warden Ward builds, every Corroder style, every Handler style,
Mana-using cross-class combinations.

**Pod choices:** larger reserve versus faster recovery; cheaper repeated skills
versus stronger expensive casts. Blood conversion remains an optional later
gamechanger rather than an ordinary node in this combo.

#### TC-15: Openings and Marks

**Ingredients:** mark or debuff duration, damage against explicitly marked or
exposed targets, benefit after engaging a new target, and a bounded defensive
return when an opening is used successfully.

**Useful to:** Pilferer Ambusher and Thief, Trapper Quarry builds, Warden threat
and protection loops, future Corruption users.

**Pod choices:** stronger first contact versus longer target focus; personal
burst versus a smaller party-wide opening. Corruption remains a specific
vulnerability category and is not a synonym for every mark.

### Intended Style Coverage Matrix

This matrix is a coverage audit, not a build guide. Primary packages most
directly reinforce the loop. Secondary packages are natural extensions. Players
may ignore both columns.

| Class style | Primary talent combos | Secondary talent combos |
| --- | --- | --- |
| Smith Breaker | TC-02 Control and Break; TC-12 Spend and Rebuild | TC-01 Armored Pressure |
| Smith Guard | TC-01 Armored Pressure; TC-10 Protection and Aid | TC-13 Sustained Contact |
| Smith Bruiser | TC-01 Armored Pressure; TC-13 Sustained Contact | TC-05 Evasive Tempo |
| Trapper Bleeder | TC-06 Ailment Pressure; TC-04 Projectile Precision | TC-05 Evasive Tempo |
| Trapper Engineer | TC-03 Held Ground; TC-02 Control and Break | TC-12 Spend and Rebuild |
| Trapper Skirmisher | TC-04 Projectile Precision; TC-05 Evasive Tempo | TC-07 Critical Rhythm |
| Pilferer Ambusher | TC-15 Openings and Marks; TC-07 Critical Rhythm | TC-05 Evasive Tempo |
| Pilferer Thief | TC-15 Openings and Marks; TC-10 Protection and Aid | TC-12 Spend and Rebuild |
| Pilferer Duelist | TC-07 Critical Rhythm; TC-13 Sustained Contact | TC-05 Evasive Tempo |
| Warden Sentinel | TC-03 Held Ground; TC-10 Protection and Aid | TC-01 Armored Pressure; TC-02 Control and Break |
| Warden Guardian | TC-10 Protection and Aid; TC-09 Shield Cycle | TC-14 Resource Engine |
| Warden Spearhead | TC-13 Sustained Contact; TC-09 Shield Cycle | TC-01 Armored Pressure; TC-02 Control and Break |
| Corroder Corrosion | TC-06 Ailment Pressure; TC-12 Spend and Rebuild | TC-09 Shield Cycle; TC-14 Resource Engine |
| Corroder Burn | TC-06 Ailment Pressure; TC-03 Held Ground | TC-04 Projectile Precision; TC-14 Resource Engine |
| Corroder Reaction | TC-07 Critical Rhythm; TC-08 Mixed Symptoms | TC-09 Shield Cycle; TC-12 Spend and Rebuild |
| Handler Pack | TC-11 Commanded Bodies; TC-10 Protection and Aid | TC-09 Shield Cycle; TC-14 Resource Engine |
| Handler Overtuner | TC-11 Commanded Bodies; TC-12 Spend and Rebuild | TC-09 Shield Cycle; TC-14 Resource Engine |
| Handler Keeper | TC-11 Commanded Bodies; TC-13 Sustained Contact | TC-09 Shield Cycle |

### Gamechanger Coverage Targets

These are transformation targets to reserve space and runtime concepts for.
They are not approved nodes and do not override the rule that gamechangers are
authored last, after their ordinary sections and resolver behavior are playable.
The values below are balance seeds, not promises.

Each damage gamechanger is deliberately class- and element-agnostic. Do not
make separate Fire, Rust, Physical, or Discharge copies when one rule can apply
to every eligible hit or ailment.

#### GC-01: Critical Commitment

```text
Critical hits deal 40% more Damage
Non-critical hits deal 30% less Damage
```

This is the primary target for TC-07 Critical Rhythm. It supports Corroder
Reaction, Pilferer Ambusher or Duelist, Trapper Skirmisher, and unconventional
critical builds using any hit damage type. `Critical hits deal` must multiply
the resolved critical hit; it must not ambiguously add 40 points to the base
critical-damage statistic. Damaging ailments do not inherit this multiplier
unless a separate implemented rule allows that ailment to crit.

Its expected-value crossover depends on the final base critical multiplier and
attainable critical chance. With critical chance `c` and base total critical
multiplier `M`, compare:

```text
normal = (1 - c) + cM
gamechanger = 0.70(1 - c) + 1.40cM
```

At the seed `150%` total critical multiplier, `40/30` crosses ordinary expected
hit damage at about `33.3%` final critical chance. That is why Additional Crit
must exist across several progression layers while the default remains only
`5%`; the node should become rational after commitment, not function as an
early automatic damage multiplier.

Do not finalize `40/30` before representative early, functioning, and endgame
critical builds are simulated against that formula.

#### GC-02: Lingering Harm

```text
Damaging ailments deal 35% more Damage
Hits deal 25% less Damage
```

This supports Trapper Bleeder and Corroder Corrosion or Burn without naming any
one ailment. It should apply to Bleed, Burn, and Corrosion through a shared
`damaging_ailment` contract. Chill, Rooted, Stagger, and Corruption are not
damaging ailments merely because they are hostile statuses.

#### GC-03: Clean Blow

```text
Hits deal 30% more Damage
Damaging ailments deal 50% less Damage
```

This is the inverse direct-hit commitment for Smith, Warden Spearhead,
Pilferer, projectile-hit Trapper, and hit-focused elemental builds. It should
not prevent incidental ailments; it makes investing in their damage inefficient
while leaving control or setup utility intact.

#### GC-04: All at Once

```text
Payoff skills deal 40% more Damage
Other skills deal 20% less Damage
```

This supports Smith Fracture, Trapper Cull or trap detonation, Pilferer opener
spends, Corroder Collapse, and Handler Scrap. It requires stable, inspectable
`payoff` skill tags. Setup actions remain useful because their purpose is to
prepare the larger spend, not compete as a second damage source.

#### GC-05: Hold Ground

```text
Owned placed effects have 35% more Effect
You deal 25% less Damage while outside an owned placed effect
```

This supports Trapper Engineer, Warden Sentinel, and ground-Cinder Corroder.
`Effect` must resolve separately for damage, protection, control, and duration
rather than silently multiplying unsupported behavior. Every affected placed
effect needs a visible ownership boundary.

#### GC-06: Within Reach

```text
Close-range hits deal 30% more Damage
Distant hits deal 25% less Damage
```

This supports Smith, Pilferer Duelist, Warden Spearhead, and unconventional
close projectile or spell builds. The distance threshold must be visible in
advanced skill information and measured from authoritative hit positions.

#### GC-07: One Machine

```text
Maximum active minions is 1
Your minion deals 80% more Damage
Your minion has 50% more Defenses
```

This is the extreme Handler Keeper transformation. Ordinary class skills must
already make one construct viable before this node exists. The rule should be
generic to owned minions so later summon content can use it.

#### GC-08: Full Pack

```text
+2 Maximum active minions
Minions deal 25% less Damage
Minions have 20% less Maximum Health
```

This is the opposing breadth transformation for Handler Pack and future summon
builds. One Machine and Full Pack should live in the same broad mechanic region
but may not touch or become transit. Validation should reject allocating both
when their rules are contradictory.

#### GC-09: Take the Blow

```text
Protection granted to others has 50% more Effect
You deal 30% less Damage
```

This supports Warden Guardian, Smith support, and Handler Pack. It requires a
solo fallback on the underlying skills but should not multiply self-protection
at the same rate, or it becomes the best solo defense node as well as the best
support node.

#### GC-10: Blood Price

```text
Mana costs are paid with Blood
Mana cannot be spent by skills
```

This is the already accepted resource-conversion direction. Its gain comes from
using health-linked recovery and ignoring Mana sustain; its loss is direct
survival risk and the inability to use ordinary Mana payment. Exact conversion
rate, minimum-health rules, and lethal-cost behavior require their own decision.

#### GC-11: Answering Damage

```text
After dealing one Damage type, a different Damage type deals 30% more Damage
Repeating the same Damage type deals 15% less Damage
```

This supports Corroder Reaction, Handler Discharge and Cold interactions, and
unconventional mixed-type pairs. Physical is a valid damage type. Ailment ticks
must not continuously rewrite the remembered type unless explicitly designed.

#### GC-12: Prepared Target

```text
You deal 30% more Damage to targets carrying your Mark or setup status
You deal 20% less Damage to other targets
```

This supports Pilferer Ambusher or Thief, Trapper Quarry, Smith Breaker,
Corroder setup/payoff, and Warden threat loops. The qualifying set must be
explicit and cannot mean every debuff. Bosses must permit the setup even when
they resist its control component.

### Gamechanger Coverage Matrix

| Intended style | Natural gamechanger targets |
| --- | --- |
| Smith Breaker | GC-03 Clean Blow; GC-04 All at Once; GC-12 Prepared Target |
| Smith Guard | GC-06 Within Reach; GC-09 Take the Blow |
| Smith Bruiser | GC-03 Clean Blow; GC-06 Within Reach |
| Trapper Bleeder | GC-02 Lingering Harm; GC-12 Prepared Target |
| Trapper Engineer | GC-04 All at Once; GC-05 Hold Ground; GC-12 Prepared Target |
| Trapper Skirmisher | GC-01 Critical Commitment; GC-03 Clean Blow |
| Pilferer Ambusher | GC-01 Critical Commitment; GC-04 All at Once; GC-12 Prepared Target |
| Pilferer Thief | GC-09 Take the Blow; GC-12 Prepared Target |
| Pilferer Duelist | GC-01 Critical Commitment; GC-03 Clean Blow; GC-06 Within Reach |
| Warden Sentinel | GC-05 Hold Ground; GC-09 Take the Blow |
| Warden Guardian | GC-09 Take the Blow; GC-10 Blood Price |
| Warden Spearhead | GC-03 Clean Blow; GC-06 Within Reach; GC-12 Prepared Target |
| Corroder Corrosion | GC-02 Lingering Harm; GC-04 All at Once; GC-10 Blood Price |
| Corroder Burn | GC-02 Lingering Harm; GC-05 Hold Ground; GC-10 Blood Price |
| Corroder Reaction | GC-01 Critical Commitment; GC-10 Blood Price; GC-11 Answering Damage |
| Handler Pack | GC-08 Full Pack; GC-09 Take the Blow; GC-10 Blood Price |
| Handler Overtuner | GC-04 All at Once; GC-07 One Machine; GC-10 Blood Price |
| Handler Keeper | GC-07 One Machine; GC-10 Blood Price; GC-12 Prepared Target |

This matrix shows plausible transformations, not a promise that each style
should take one. A functioning late build may reject all gamechangers, and an
unconventional build may combine targets not listed here when their rules do
not contradict each other.

### Tree Placement Implications

The current scaffold contains thirty specialization pods. Fifteen reusable
combos with roughly two complementary expressions each—often output/tempo and
defense/sustain—fit that capacity cleanly as a first assignment exercise. This
is a useful estimate, not a one-to-one production quota. A combo may need one
large pod, several smaller placements, or only shared nodes in a border section.
Do not author a weak mirror pod to preserve the arithmetic.

- Force should naturally approach TC-01, TC-02, and TC-13.
- Precision should naturally approach TC-04, TC-05, TC-07, and TC-15.
- Exposure should naturally approach TC-06, TC-08, and the damage side of
  TC-03.
- Tuning should naturally approach TC-09, TC-11, and TC-14.
- Command should naturally approach TC-10, TC-11, and the sustain side of
  TC-12.
- Shelter should naturally approach TC-01, TC-03, TC-09, and TC-10.
- Route-border sections should hold the most reusable hybrids. Examples include
  Precision/Exposure for critical ailments, Exposure/Tuning for shielded
  casters, and Force/Shelter for guarded Physical builds.
- No route owns a combo exclusively. Bridges and later sections must make every
  ordinary combo reachable from every selected start without crossing another
  locked start or requiring a gamechanger.

## Recommended Decisions and Accepted Directions

1. **Completeness budget:** use roughly five actives and nine passives as the
   current estimate, but approve each class by its three combat loops and
   survival plans rather than matching an arbitrary count.
2. **Weapon contract — accepted:** every learned skill works at full authored
   effect with every weapon. Weapon families favor related affixes rather than
   granting class-skill bonuses or imposing hard locks.
3. **Resources:** Smith, Trapper, and Pilferer initially use cooldowns and
   setup states rather than Mana. Warden uses Mana only for Ward-like actions.
   Corroder uses Mana. Handler uses Mana for deployment and overtuning while
   direct commands remain cheap or free.
4. **Corroder identity:** retain Fire as a secondary payoff rather than creating
   a separate launch Fire class. Cinder remains recognizable but is subordinate
   to the Rust and Corrosion loop.
5. **Handler availability:** deploy a prepared construct anywhere. Treat hacking
   wild locusts as an optional later interaction.
6. **Status vocabulary:** use Stagger, Bleed, Rooted, Chill, Burn, Corrosion,
   and Corruption only when their executable status contracts exist. Temporary
   design labels must not quietly ship as nonfunctional tooltip promises.
7. **Passive investment — accepted:** class passives have eight ranks and scale
   heavily with investment. The two-class point budget must make maximizing a
   few passives mutually exclusive with acquiring every passive on either
   class sheet.
