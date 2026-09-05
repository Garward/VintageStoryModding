# Survival-System Ownership Audit

Status: Living implementation map; first audited surfaces are character statistics and Vanilla classes  
Parent: [Complete game design](complete-game-design.md)  
Strategy: [Implementation readiness and test strategy](implementation-readiness-and-test-strategy.md)  
Index: [VRPG design index](README.md)

## 1. Purpose

This file turns the Survival-system gate into an implementation checklist. Every inherited system receives one owner, disposition, default state, disable or bridge strategy, migration rule, player-facing result, and test set.

The default question remains:

> What does this add to playing VRPG?

Assets and low-level engine capabilities may remain even when their original player loop is disabled.

## 2. Ownership rules

- One system owns each gameplay outcome in the effective runtime profile.
- Hiding a HUD element is not equivalent to disabling its mechanic.
- Setting a rate to zero is acceptable only when no side effects, persistence, tooltips, AI assumptions, or third-party writes can reactivate it.
- Removing an asset is unsafe when Vanilla code assumes at least one record exists; use an inert compatibility record or patch the assumption.
- A disabled system has no ticking behavior, player penalty, reward dependency, misleading UI, tutorial, command, or unexplained persisted state.
- Compatibility toggles operate at coherent ownership boundaries and validate conflicts before the world loads.

## 3. Character and environment panels

The pictured Survival character screen is the first concrete audit surface.

| Visible concept | Disposition | Default VRPG result | Implementation concern |
|---|---|---|---|
| Fruit, Vegetable, Grain, Protein, Dairy | Disable | No nutrition bars or nutrition-derived progression | Disable player nutrition behavior and related tutorials/tooltips; food assets may be repurposed |
| Satiety | Disable | No hunger resource or starvation loop | Neutralize `EntityBehaviorHunger`, consumption side effects, revive hooks, HUD and persistence |
| Body Temperature | Disable | No passive temperature punishment or clothing tax | Disable player temperature behavior/damage and panel; weather visuals may remain |
| Hunger Rate | Disable | Stat does not exist in VRPG presentation or item value | Remove inherited modifiers/tooltips and ensure they do not become dead affix budget |
| Health Points | Replace | VRPG owns maximum health, current health, damage and recovery | Bridge the engine health container where useful; never permit Survival recalculation to overwrite VRPG values |
| Healing Effectiveness | Replace or omit | Show only specific VRPG healing dealt/received rules used by the build | Remove generic Survival stat from the panel and map supported modifiers explicitly |
| Walk Speed | Reframe | VRPG movement profile, movement bonuses and burst rules own the result | Engine movement stat may remain as a low-level output, not an independent progression owner |
| Ranged Accuracy | Replace | Manual aim plus explicitly authored spread/handling rules | Do not inherit a universal Survival percentage unless a VRPG skill references it |
| Ranged Charge Speed | Replace | Tagged cast, draw, channel or charge-speed rules | Avoid one generic modifier silently scaling unrelated abilities |
| Date and time | Conditional keep | Shown only when an objective, encounter or world rule makes it actionable | Atmosphere alone does not justify permanent panel space |
| Outside Temperature | Disable from default UI | Weather may remain visual; show a hazard only when it has a VRPG rule | Never imply body-temperature gameplay when it is disabled |
| Rainfall | Disable from default UI | Communicate only an active weather interaction that changes a decision | Ambient climate belongs in the world, not a permanent stats card |
| Rift Activity | Reframe or disable | Replace with a named VRPG regional threat/world state when used | Remove Survival temporal-event assumptions and generic calm/activity display |
| Character equipment window | Replace | VRPG equipment and typed inventory show build-relevant slots | Appearance may reuse rendering; backpack and Survival slot ownership do not remain |
| Traits tab | Disable | No Vanilla class or trait surface | VRPG class, talents, Legacy and build details live in VRPG interfaces |
| “Commoner” class label | Disable | Show the selected VRPG identity only where useful | Do not expose the inert compatibility class described below |

### 3.1 Runtime character-stat authority

This is a near-term prerequisite for retiring the Vanilla Character dialog. A
hidden Survival value is still a live mechanic unless its mutation and output
paths are neutralized.

#### Adventure and combat profile

- [ ] Prevent `EntityBehaviorHunger` from draining satiety, applying starvation
  damage, changing healing, invoking `ReceiveDamage`, or producing any other
  periodic gameplay result. Its private six-second `SlowTick` listener is a
  separate path from `OnGameTick` and must also be neutralized.
- [ ] Suppress already-scheduled hunger/starvation damage when entering the VRPG
  Adventure profile so a stale Vanilla tick cannot damage the player afterward.
- [ ] Prevent hunger from reaching `EntityPlayer.OnHurt`: zero-satiety
  `ReceiveDamage` currently causes camera shake even when another layer prevents
  the small health change. No starvation shake, hurt response, sound, animation,
  damage event, combat-state change, or proc may occur.
- [ ] Prevent nutrition categories and nutrition-derived health bonuses from
  contributing to maximum health or triggering a later health recalculation.
- [ ] Keep current and maximum health exclusively under the VRPG health service;
  joining, respawning, eating, loading, changing equipment, changing realm, or
  receiving a Vanilla stat notification must not overwrite them.
- [ ] Make the VRPG movement service the sole high-level owner of baseline walk
  speed, acceleration, sprint policy, braking, and temporary movement modifiers.
  The engine `walkspeed` stat and `entity.walkSpeed` may be synchronized outputs,
  but they are not independent sources of truth.
- [ ] Make the VRPG movement service the sole owner of jump height and vertical
  launch policy. Audit and neutralize Vanilla class, trait, hunger, equipment,
  and addon modifier sources that would silently change the effective jump.
- [ ] Complete the Vanilla class/trait neutralization in sections 4 and 5 before
  removing its UI: no selection prompt, class gear, trait modifier, recipe gate,
  class command, or health/movement side effect may remain active.
- [ ] Make `EntityBehaviorBodyTemperature` inert in Adventure. It must not scan
  rooms, weather, wetness, clothing or heat sources; change body temperature;
  accumulate freezing time; deal frost damage; alter movement; start cold
  animations; add frost rendering; or publish `freezingEffectStrength`.
- [ ] On Adventure entry, normalize stale temperature state, clear freezing
  strength and accumulated damaging-freeze time, and stop both cold animations.
  Ambient climate and weather may continue to exist as world data, but they must
  not feed a player body-temperature simulation.

#### Dormant data and Home reuse hooks

- [ ] Preserve satiety and nutrition data only behind a realm-aware compatibility
  adapter; no Survival behavior may tick merely because the values still exist.
- [ ] If body-temperature data is retained for compatibility, keep it dormant
  behind the same realm boundary. Future Home or encounter content must use an
  explicit VRPG condition/hazard definition rather than restarting Vanilla's
  body-temperature behavior implicitly.
- [ ] Expose read-only, explicitly requested Home-domain conditions such as
  `SatietyAtMaximum` or a nutrition-band condition through a generic contextual
  bonus provider. Do not let Home content call `EntityBehaviorHunger` directly.
- [ ] Keep bonuses data-defined and scoped by realm/activity. A possible future
  rule is “maximum satiety grants +5% Home activity XP”; it is an example hook,
  not a default combat bonus or a committed balance value.
- [ ] On Adventure/Home transitions, atomically remove the old profile's modifier
  sources, apply the new profile, and recompute VRPG health and movement once.
  Dormant Survival state must never leak a delayed damage or maximum-health tick.

#### Skill-driven movement effects

- [ ] Add a reusable, server-authoritative movement-effect service for impulses,
  launches, controlled velocity blending, forced movement, collision grace,
  duration, cancellation, and client reconciliation.
- [ ] Use Vintage Kinematics as a proven reference: the trebuchet demonstrates
  authoritative position/motion synchronization and bounded collision grace;
  belt riders demonstrate gradual velocity convergence rather than abrupt motion
  replacement. Extract the patterns behind a VRPG-owned interface instead of
  depending on Kinematics or copying block-specific state.
- [ ] Let skills request named movement effects through that service. Skills must
  not write raw walk speed, jump height, or player motion from unrelated systems.
- [ ] Define stacking and priority between locomotion, dash, leap, knockback,
  pull, launch, root, slow, and realm transition so the final motion has one owner.

#### Character-authority tests

- [ ] At empty satiety, remain in Adventure combat long enough to prove there is
  no hunger drain, starvation damage, healing alteration, camera shake, hurt
  response, combat-state change, proc, or delayed damage tick.
- [ ] At maximum and minimum nutrition, verify identical VRPG maximum health in
  Adventure after join, respawn, eating, save/load, and realm travel.
- [ ] Inject Vanilla class, trait, walk-speed, and jump modifiers and verify they
  cannot change the effective Adventure result; verify VRPG-authored modifiers do.
- [ ] Verify an opted-in Home contextual bonus can read dormant satiety/nutrition
  state without reactivating hunger damage or nutrition maximum-health effects.
- [ ] Test extreme cold, wetness, cold clothing, heat sources, eating hot food,
  save/load, and Adventure/Home transitions. Adventure must show no frost damage,
  cold animation, frost overlay, temperature modifier, or stale freezing state.
- [ ] Test dash, leap, knockback, belt-like conveyance, and trebuchet-like launch
  effects for server/client agreement, collision recovery, cancellation, and no
  permanent movement modifier leakage.

### Panel acceptance tests

- With the VRPG preset, none of the disabled values tick, damage, buff, gate recipes, alter drops, or appear in ordinary UI and tooltips.
- Toggling a supported compatibility owner restores both behavior and its truthful UI rather than only one side.
- VRPG health and movement survive join, respawn, dimension travel, Rebirth, equipment changes and save/load without a Survival recalculation.
- The character screen contains only values that can change an equipment, skill, talent, or immediate activity decision.

## 4. Vanilla class and trait system

Disposition: **Disable**  
Default owner: **VRPG character progression**  
Configuration boundary: `CharacterProgressionOwner = Vrpg | Vanilla`  
Runtime change: not supported; requires restart and migration

Vanilla classes are not a secondary flavor layer. The decompiled `CharacterSystem` owns or affects:

- `config/characterclasses` and `config/traits` loading.
- The `charselection` network channel and packets.
- Initial selection and a default class assignment on player join.
- The `/charsel` command and class-selection UI.
- The Character window's Traits tab.
- `characterClass`, `extraTraits`, `allowcharselonce`, and `createCharacter` state.
- Starting clothing and gear assignment, including clearing equipment slots before grant.
- Entity stats written under the `trait` modifier source.
- Health recalculation after trait application.
- Trait-gated grid recipes and `HasTrait` calls used by content.
- Dialogue variables and the class line in entity information.

Deleting every Vanilla class asset is unsafe because join and selection paths index the first loaded class. The source itself warns addon authors to disable classes rather than remove expected records.

## 5. Vanilla-class disable strategy

### 5.1 Preserve appearance, replace class selection

Character appearance, voice, and outfit customization are useful and remain in a
rethemed VRPG creation flow. The former class-selection step presents data-defined
VRPG starter classes instead: each grants a modest level-one weapon and armor plus
one ordinary skill point preallocated to a representative class skill. The point
can be refunded normally. No Vanilla class choice or trait is submitted.

The complete flow, transaction, screen, and onboarding contracts are defined in
[Character creation and first-run onboarding](character-creation-and-onboarding.md).

### 5.2 Inert compatibility class

Keep or inject one internal neutral class record when required by Vanilla invariants. It has no traits, stat attributes, starting gear, recipe identity, or player-facing translation. It is a compatibility sentinel, not a playable class and never appears as “Commoner.”

### 5.3 Neutralize every mutation path

- Prevent Vanilla class selection from granting gear or applying trait modifiers in VRPG mode.
- Remove existing `trait`-source modifiers and legacy `extraTraits` during migration.
- Normalize the watched Vanilla `characterClass` to the inert sentinel only where downstream code requires a value.
- Replace the class-selection page with the VRPG starter-class step; disable the
  Traits tab and every Vanilla class mutation behind it.
- Preserve appearance and voice packets needed by the replacement creation flow.
- Replace the temporary Vanilla `/charsel` appearance fallback with an
  appearance-only rethemed editor; reopening it must never repeat a starter grant.
- Disable `RequiresTrait` recipe gates or map recipes into VRPG requirements during recipe loading.
- Ensure `HasTrait` returns no Vanilla gameplay traits in VRPG mode.
- Replace or suppress class dialogue placeholders and entity info; VRPG dialogue may query the VRPG class service explicitly.
- Never let Vanilla selection clear VRPG equipment slots.

The safest exact interception points still require a Harmony/API boundary audit. All patches must consult the effective `CharacterProgressionOwner` capability rather than independent config flags.

### 5.4 Migration

On first VRPG ownership of an existing player:

1. Snapshot Vanilla class, traits, trait-source stats, selection moddata and equipment.
2. Remove only Vanilla-owned trait modifiers and flags; do not delete similarly named addon or VRPG state.
3. Preserve appearance, voice and cosmetic equipment through the inventory migration policy.
4. Set the inert compatibility value if required.
5. Initialize or restore VRPG character progression through its own transaction.
6. Record migration version and result so reconnecting cannot grant or clear gear twice.

Re-enabling Vanilla ownership requires an explicit migration or fresh character selection. It does not infer a Vanilla class from a VRPG class name.

### 5.5 Tests

- A new VRPG player receives the rethemed appearance and starter flow, but no
  Vanilla class traits, gear, recipes, or stat modifiers.
- `/charsel`, class-change world settings and packets cannot mutate VRPG starter
  state or repeat rewards; the supported route may edit cosmetics only.
- The Traits tab and Vanilla class label are absent while appearance customization remains usable.
- No stat contains a Vanilla `trait` source after join, respawn, Rebirth or migration.
- Trait-exclusive Vanilla recipes do not remain accidentally locked or unlocked; their VRPG disposition is explicit.
- Dialogue and entity info do not show the inert class.
- A migrated character retains cosmetics and VRPG equipment without duplication or deletion.
- Vanilla ownership works only in its supported compatibility profile and cannot be enabled alongside VRPG class ownership.

## 6. Next audit groups

Audit in dependency order:

1. Player entity behaviors: hunger, health, body temperature, temporal stability, breath, death and spawning.
2. Character systems: classes, traits, appearance, clothing, armor, inventory, backpacks and hotbar.
3. World mutation: block placement/breaking, chiseling, liquids, fire, farming, containers and mechanical systems.
4. Combat: melee, ranged charging, aiming, damage types, armor, healing, knockback, durability and creature AI.
5. World generation: terrain, climate, caves, deposits, ruins, traders, rifts, temporal events, creatures and resource distribution.
6. Items and processing: food, spoilage, cooking, tools, recipes, metallurgy, crafting grids and handbook output.
7. World information: HUD bars, character/environment panels, map, calendar, weather, tutorials and handbook categories.
8. Multiplayer and administration: claims, game modes, privileges, respawn, commands, player data and compatibility negotiation.

Each group should land as a reviewed table plus executable smoke tests before its replacement becomes a dependency for later gates.
