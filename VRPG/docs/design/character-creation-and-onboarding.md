# Character Creation and First-Run Onboarding

Status: In progress  
Updated: 2026-09-04  
Parents: [Complete game design](complete-game-design.md) · [Survival-system ownership audit](survival-system-ownership-audit.md) · [Native player menu](native-player-menu-implementation-plan.md)  
Index: [VRPG design index](README.md)

## 1. Decision

VRPG preserves Vintage Story's useful appearance, skin, voice, and preview
machinery. It replaces Vanilla class gameplay with a VRPG-owned starter choice
and follows character creation with a short first-run guide.

The three concepts have separate owners:

| Concept | Owner | Meaning |
|---|---|---|
| Appearance | Cosmetic character service | Body presentation, skin, hair, voice, outfit preview; no combat modifiers |
| Starter class | VRPG onboarding service | Initial weapon, armor, and one preallocated refundable skill point |
| Character build | VRPG progression | Skills, talents, equipment, stats, loadouts, and later Rebirth choices |

The internal Vanilla `commoner` value may remain as an inert compatibility
sentinel. It is never shown as the player's class and grants no traits, stats,
gear, recipes, or dialogue identity.

## 2. First-character flow

```text
server detects no completed VRPG character
  -> create durable onboarding transaction
  -> allocate and validate a private safe arrival space
  -> move player to its arrival anchor
  -> apply fallback protection and suspend gameplay actions
  -> Appearance screen
  -> Starter Class screen
  -> server atomically commits appearance + starter grant
  -> compact Welcome guide
  -> player closes or confirms guide
  -> server records acknowledgement
  -> server confirms a valid safe release state or relocates to one
  -> protection ends and one obvious world objective becomes active
```

Disconnecting or crashing resumes the last incomplete stage. It never repeats a
committed grant and never leaves a player damageable inside an unfinished modal.

## 2.1 Safe first arrival

Spatial isolation is the primary safety mechanism. Popup-based invulnerability is
only a fallback around loading, reconnect, and release transitions.

Preferred destination order:

1. The player's allocated Home, when its realm and arrival anchor are ready.
2. A tiny private onboarding instance containing only the creator context, a safe
   floor, and the first obvious exit or interaction.
3. A protected fallback arrival cell used only when realm allocation is recovering.

Until the owned Home realm runtime passes its allocation and recovery gates, the
small private onboarding instance is the implementation default. This keeps
first-run correctness independent from unfinished housing persistence while
preserving Home as the eventual preferred arrival.

The [safe-arrival tavern implementation](safe-arrival-tavern-implementation.md)
provides deterministic geometry, structural validation, inert block assets and
reservation preparation. Live allocation, isolation enforcement and protected
transfer remain gated on the owned realm runtime; generation readiness alone
does not complete safe arrival or authorize protection release.

Do not place a modal-protected player into an active public combat space. A player
who leaves the guide open must not become an immortal obstruction, occupy an
encounter slot, block navigation, scout threats, or hold shared progression open.

### Arrival-space requirements

- [ ] Generate or reserve the arrival space before presenting character creation.
- [ ] Validate a stable floor, head clearance, bounded non-lethal surroundings,
  realm ownership, and a recoverable arrival anchor.
- [ ] Exclude hostile spawning, encounters, hazards, damage volumes, ordinary
  loot, trading, building, and public traffic from the protected arrival volume.
- [ ] Keep unfinished players non-solid or otherwise outside shared collision and
  AI targeting where isolation cannot be guaranteed.
- [ ] Persist the realm and anchor identity so reconnect resumes safely without
  relying on the client's last position.
- [ ] Provide one deterministic recovery anchor if Home/onboarding generation is
  unavailable or corrupt.
- [ ] Do not release protection merely because a chunk or screen reports loaded.
  The server must validate the actual release location.

## 3. Screen contracts

### Screen: Appearance

- Player goal: make a character they want to look and sound like.
- Primary action: continue to Starter Class.
- Entry points: first character, authorized appearance change, and Rebirth.
- Required information: live character preview, body/skin/hair/voice choices, and
  whether a choice is cosmetic.
- Explicitly excluded: Vanilla traits, stat percentages, class gear, recipes,
  nutrition, and Survival explanations.
- Persistent information: appearance draft until committed or deliberately reset.
- Locked/disabled state: unavailable assets identify the missing source without
  discarding the current draft.
- Input: mouse, keyboard, controller, and existing remapped close/navigation paths.
- Authority: the server validates and persists the final appearance packet.
- Presentation: retheme with VRPG metal panels, broad margins, fitted controls,
  and one dominant preview rather than preserving the paper Survival layout.

### Screen: Starter Class

- Player goal: choose the first combat style they want to try.
- Primary action: confirm one starter class.
- Required information per choice: combat pattern, defensive approach, starting
  weapon, starting armor, and the first skill in plain language.
- Secondary information: class lore and deeper build possibilities open through
  one Details disclosure or the Library.
- Explicitly excluded: complete talent trees, endgame scaling, every compatible
  weapon, dense formulas, and any claim that this choice permanently locks the
  character.
- Selection state: selected without relying on color; show the exact grant beside
  Confirm.
- Pending state: disable repeated confirmation while the server commits.
- Rejection state: preserve the draft and explain the next action beside Confirm.
- Authority: the server resolves the definition, item identities, capacity,
  equipment destinations, and skill point before committing anything.

Suggested compact class-card hierarchy:

```text
COMMAND
Control the fight with deliberate melee pressure.

Starts with
  Weapon: Iron arming sword
  Armor:  Light plated coat
  Skill:  Hammer Blow — stagger one nearby enemy

[Details]                         [Choose Command]
```

Names and balance values are illustrative until backed by real definitions.

### Screen: Welcome to VRPG

- Player goal: understand what to do immediately after creation.
- Primary action: begin playing.
- Entry point: once after the first starter transaction in this world.
- Required information: Home contains building and the remaining Survival-style
  systems; the wider game focuses on combat, expeditions, loot, and character
  development; the player's first active skill is ready.
- Explicitly excluded: lore chronology, exact formulas, every system, advanced
  crafting, Rebirth detail, and a wall of controls.
- Persistent information: none; the full guide remains in Library > Getting Started.
- Modal behavior: shown only inside the safe arrival flow. Fallback protection and
  non-participation remain until acknowledgement plus safe-state validation.
- Close behavior: the close button, Escape, and the primary action all perform the
  same server acknowledgement. Closing the panel does not directly remove
  protection.

First-page copy target:

```text
WELCOME TO VRPG

Build, craft, and use the remaining Survival systems in your Home.
Beyond it, fight through expeditions for loot and new ways to shape your character.
Your first skill is ready now—and you can change it later.

[Open Full Guide]                           [Continue]
```

Lore belongs on an optional second card or in the Library. Do not delay the first
decision with lore required only for context.

The panel does not explain every control or system. After safe release, one
world-space objective teaches the next action—for example, try the starting skill
on a harmless training target and then use the expedition gate. The actual
objective must match the generated arrival space and show resolved input bindings.

## 4. Starter-class data contract

Implementation checkpoint: the shared definition, asset/skill validation, network
preview projection, and focused metal choice screen are implemented behind
`ExperimentalDialogReplacement`. Smith, Pilferer, Warden, Corroder, and Rust
Scrapper currently have validated previews. Handler and Trapper remain hidden from
the selector until each has an authored level-one skill. Confirmation remains
unavailable until the atomic item-store grant below is real.

Each starter class is a data definition, not a hard-coded switch:

```text
code
name + short description + optional lore entry
appearance material/accent identity
starting weapon entry
starting armor entries
starting skill code and level
recommended but non-binding primary attribute
grant schema version
```

- [x] Validate every referenced item and skill during data loading.
- [x] Require exactly one usable level-one weapon and at least one valid skill.
- [ ] Keep starter equipment intentionally modest; it teaches a loop rather than
  bypassing early loot decisions.
- [ ] Treat the starting skill point as an ordinary point that begins allocated.
  Respeccing refunds it normally; it is not a hidden permanent class bonus.
- [ ] Allow addons to register starter definitions through the same validation
  pipeline without executing arbitrary grant code.

## 5. Atomic starter grant

- [ ] Give the transaction a stable identity derived from player, world, character
  generation, and grant schema version.
- [ ] Validate appearance, definition, items, equipment capacity, store readiness,
  and skill state before mutation.
- [ ] Insert the weapon and armor into `IPlayerItemStore`, equip intended items,
  grant one skill point, and allocate it to the starter skill as one recoverable
  server transaction.
- [ ] Record committed starter code and transaction identity before releasing the
  first-run lock. Retries return the committed result without granting again.
- [ ] Route unsupported or missing addon items to explicit recovery and keep the
  player in the selection stage with an actionable explanation.
- [ ] Never grant starter items through Vanilla hotbar, backpack, class gear, or
  `CharacterSystem.setCharacterClass`.
- [ ] Reopening appearance customization changes cosmetics only. It cannot change
  starter choice, reset progression, or repeat a grant.
- [ ] Rebirth uses a new authorized character generation and its own transaction;
  it does not replay the first-world Welcome modal by default.

## 6. Protected onboarding state

The server owns `OnboardingStage` and `GuideAcknowledged`. Client visibility is
not proof that protection should begin or end.

- [ ] Enter protection before transfer to the safe arrival space and reject all incoming
  damage before hurt feedback, death, status procs, durability, knockback, or
  combat-state changes.
- [ ] Also block attacks, skill casts, pickups, rewards, trading, travel, building,
  and encounter participation. Invulnerability must not become a farming or
  scouting exploit.
- [ ] Keep the player in a safe spawn/onboarding location where possible; modal
  protection is recovery, not encounter design.
- [ ] Send one acknowledgement request when any valid close path is used. The
  guide may close after durable acknowledgement, but protection remains until the
  release-state transaction succeeds.
- [ ] Define `SafeReleaseReady` on the server: correct realm, loaded and valid
  anchor, stable floor and clearance, no hostile encounter membership, no active
  hazard, no hostile already committed to the player, and no shared collision or
  navigation obstruction left by the onboarding state.
- [ ] If acknowledgement occurs outside a valid release state, relocate to the
  persisted safe anchor, validate again, then release protection. Never expose the
  player at the unsafe position between those steps.
- [ ] On rejection or disconnect, retain protection and resume the same stage or
  the pending safe-release transition.
- [ ] Never use a client timer to release protection. Add an administrator recovery
  path for a genuinely stuck onboarding record.
- [ ] Version the guide acknowledgement separately from the starter grant so a
  future optional guide revision cannot duplicate character rewards.

## 7. Appearance command and later editing

- [ ] Replace the current temporary `/charsel` no-op with an appearance-only route
  or a clearly named VRPG alias. It opens the rethemed cosmetic editor and never
  submits a Vanilla class change.
- [ ] Preserve current appearance, voice, and draft state when opening and canceling.
- [ ] Separate permission/cost policy for later cosmetic changes from first-run
  creation and Rebirth.
- [ ] Keep the Vanilla Character action mapped to VRPG Inventory/Equipment; cosmetic
  editing is deliberate and is not another inventory owner.
- [ ] Remove the Vanilla Traits tab and every player-facing reference to the inert
  compatibility class.

## 8. Implementation order

1. Define onboarding state, starter definitions, validation, and idempotency tests.
2. Replace the temporary Vanilla appearance fallback with a narrow rethemed
   appearance bridge; `/charsel` remains functional until that replacement exists.
3. Build the rethemed Appearance screen using real skin/voice state.
4. Build Starter Class cards with real definitions and a dry-run grant preview.
5. Implement the atomic item/equipment/skill grant after item-store networking and
   engine-flow bridges are ready.
6. Build the compact Welcome modal and Library > Getting Started entry.
7. Implement safe-arrival allocation, validation, recovery anchor, and transfer.
8. Implement server-owned fallback protection, gameplay-action suspension, and
   the acknowledged-plus-safe release transaction.
9. Add one generated-arrival-aware first objective with resolved skill binding.
10. Test first join, indefinitely open guide, cancel, retry, disconnect, crash,
   missing addon, full storage, unsafe close position, active enemy commitment,
   multiplayer obstruction, duplicate acknowledgement, realm-allocation failure,
   recovery-anchor failure, remapped input, multiplayer, later appearance editing,
   and Rebirth.

## 9. Release gate

- A new player can customize appearance, understand each starter choice, receive
  exactly one coherent kit, and use their first skill without reading a manual.
- Closing or reconnecting at every stage causes neither duplication nor lost state.
- No Vanilla trait, class stat, class recipe, or class gear behavior activates.
- The player cannot take damage or influence gameplay while protected.
- A player can leave the guide open without occupying or obstructing public play.
- Protection ends only after both guide acknowledgement and server-validated safe
  release, never as a direct consequence of closing a client window.
- The Welcome screen communicates the next action in a few seconds and the Library
  contains everything intentionally omitted from it.
- Appearance editing remains available without exposing a repeatable reward path.
