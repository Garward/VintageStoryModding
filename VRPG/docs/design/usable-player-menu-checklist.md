# Player-Menu Acceptance Checklist

Status: **First usable Adventure menu acceptance checklist**
Updated: 2026-09-12
Execution order: [Playable vertical-slice checklist](testable-version-todo.md)
Design: [Unified player-menu design](../superpowers/specs/2026-09-06-unified-player-menu-design.md)

This document defines the narrow first usable Adventure-menu handoff: VRPG takes
ownership of the semantic `inventorydialog` and `characterdialog` actions together
and routes them to Inventory/Equipment. It does not restore a broad menu roadmap
or gate combat, expeditions, or reward delivery on later pages.

The source/test handoff is complete. A preview, isolated client, or hidden Vanilla
widget does not count as replacement-ready: the joined-client and explicit-player
confirmation checks below remain open.

## Foundations already complete

- [x] Canonical aggregate and unique-item records.
- [x] Fixed large capacity and typed category policies.
- [x] Revisioned server-authoritative store and bounded queries.
- [x] Versioned persistence, migration, recovery, and item-stack codec.
- [x] Starter gear creation, equipment state, comparison data, and server-backed
  menu protocol foundations.
- [x] Production Inventory/Equipment route and paired semantic-owner swap, with
  all-or-nothing restore before Adventure activation.

Completed foundations remain part of the product. Their historical test counts and
commit hashes stay in git rather than in this checklist.

## First-pass boundary

- [x] Inventory and Character are the only first-pass semantic actions.
- [x] Inventory opens browsing; Character opens Inventory with Equipment expanded.
- [x] Canonical-custody failure is fail-closed in Adventure; Vanilla is not a live
  fallback after ownership has transferred.
- [ ] The joined-client verification loop is repeatable.

## Server-backed page acceptance

- [ ] Inventory shows real aggregate and unique records with bounded paging,
  searching, filtering, categories, quantities, and capacity summaries.
- [ ] Equipment shows authoritative equipped state, comparisons, requirements,
  equip/unequip results, and useful errors.
- [ ] Every mutating request is authenticated, validated on the server, revisioned,
  idempotent where retry is possible, and followed by an authoritative response.
- [ ] Loading, empty, stale, unavailable, unsupported-item, overflow, conflict, and
  recovery states tell the player what happened and what to do next.
- [ ] Mouse, keyboard, controller, focus, scaling, resize, and localization behavior
  pass live checks at the supported UI scales.
- [ ] Close, Back, death, disconnect, reconnect, and server restart cannot duplicate
  or lose an item.

## Character action in the first pass

- [ ] The Character semantic action opens the same server-backed Inventory page
  with Equipment expanded, without a separate Character page.
- [ ] The Equipment disclosure shows the selected item, equipped state,
  requirements, mechanical effects, comparison, and Equip/Unequip result needed
  for this first pass.

Appearance, retained Character facts, Stats, Skills, Talents, Library, Options,
progression, starter selection, and Welcome integration are later player-page
work. They do not gate this Inventory/Equipment handoff.

## Semantic-dialog ownership transaction

- [x] Discover the live owners of `inventorydialog` and `characterdialog` after
  normal dialog registration; do not assume fixed list positions or physical keys.
- [x] Require exactly one expected owner for each semantic action before mutation.
- [x] Capture enough state to restore both owners if installation fails.
- [x] Install both replacements as one operation. Never leave one Vanilla action
  replaced while the other remains unexpectedly owned.
- [x] Route Inventory to the Inventory page and Character to the chosen character
  or Equipment context without changing user key bindings.
- [x] Repeating either action while the replacement is open closes it consistently.
- [x] An unexpected owner, missing capability, protocol mismatch, or failed page
  dependency aborts the transaction and leaves the prior owners intact.
- [x] Shutdown and configuration changes restore or retain ownership according to
  the tested custody policy without discarding pending state.

## Joined-client release test

- [ ] Join a dedicated VRPG server with the supported client build.
- [ ] Open both semantic actions through default and remapped bindings.
- [ ] Confirm neither ordinary route opens the Vanilla Inventory or Character UI.
- [ ] Exercise inventory browsing, equipment changes, the Equipment disclosure,
  recovery, resize, death, reconnect, save, and server restart.
- [ ] Confirm any startup or runtime failure follows the declared fallback policy
  and leaves items recoverable.
- [ ] Obtain explicit player confirmation before calling dialog replacement ready.

## Work that does not belong in this gate

- Party management and party-only loot.
- Loot piles, salvage previews, favorite/junk workflows, and random affix breadth.
- Housing, worldgen, realm ownership, creature ingestion, or expedition systems.
- Completing all six planned pages when fewer pages satisfy observed use.

Those features require their own playable gates. They cannot become prerequisites
for semantic-dialog replacement merely because they may eventually appear in the
same shell.
