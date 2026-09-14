# Player-Menu Acceptance Checklist

Status: **Unified Adventure menu implemented; joined-client acceptance pending**
Updated: 2026-09-14
Execution order: [Playable vertical-slice checklist](testable-version-todo.md)
Design: [Unified player-menu design](../superpowers/specs/2026-09-06-unified-player-menu-design.md)

This document defines the first cohesive Adventure-menu handoff. VRPG takes
ownership of the semantic `inventorydialog` and `characterdialog` actions together
and presents Inventory, Stats, Skills, Talents, Library, Options, and the basic
Party permission panel in one player-facing shell.

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

- [x] Inventory, Character, and the VRPG Hub hotkey all route through one shell.
- [x] Inventory opens browsing; Character opens Inventory with Equipment expanded.
- [x] The Hub hotkey opens Stats, and `/vrpg library open` opens Library.
- [x] Ordinary Hub state refreshes do not open or reopen the menu.
- [x] The separate ordinary Hub and Library dialogs and their UI elements are
  removed; the privileged talent editor remains separate.
- [x] Canonical-custody failure is fail-closed in Adventure; Vanilla is not a live
  fallback after ownership has transferred.
- [ ] The joined-client verification loop is repeatable.

Automated evidence on 2026-09-14: 23 focused routing/lifecycle/composition tests
and the full 1,798-test suite passed. The Release build completed with zero warnings
and zero errors, and the development mod was synchronized to the installed Mods
folder. These facts do not replace the joined-client checks below.

Live acceptance was not run from this task environment: no Vintage Story client
was active, no native app surface was available, and the desktop helper could not
connect to the desktop session bus. No login or credential entry was attempted.

## Server-backed page acceptance

- [x] One connection-owned Hub service supplies Stats, Skills, Talents, Library,
  and Options without recreating state for each page.
- [x] Every top-level page tab is a real route in the shared shell; unavailable,
  loading, empty, pending, success, and rejection bodies are covered by composition
  tests where the state applies.
- [x] Ordinary item names prefer the live client localization path, use readable
  fallback names, and reserve raw identifiers for explicit recovery detail.
- [x] Narrow gallery Stats/Skills inspectors accept Page Up and Page Down without
  changing production page behavior.
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

Character facts that are not yet authoritative, starter selection, and Welcome
integration remain later work. The six-page shell is implemented, but its
joined-client usability still belongs to this acceptance gate.

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
- [ ] Open V/Stats, Skills, Talents, Library, Options, and Party from the shared
  shell; confirm navigation, readable names, centered controls, and truthful page
  feedback in the real client.
- [ ] Exercise at least one real mutation or honest rejection on every mutable page.
- [ ] Confirm the client log remains free of texture-atlas, composition, packet,
  and input errors during those transitions.
- [ ] Exercise inventory browsing, equipment changes, the Equipment disclosure,
  recovery, resize, death, reconnect, save, and server restart.
- [ ] Confirm any startup or runtime failure follows the declared fallback policy
  and leaves items recoverable.
- [ ] Obtain explicit player confirmation before calling dialog replacement ready.

## Work that does not belong in this gate

- Party health bars and party-only loot delivery beyond the existing permission panel.
- Loot piles, salvage previews, favorite/junk workflows, and random affix breadth.
- Housing, worldgen, realm ownership, creature ingestion, or expedition systems.
- Later page breadth or visual polish that does not resolve an observed usability issue.

Those features require their own playable gates. They cannot become prerequisites
for semantic-dialog replacement merely because they may eventually appear in the
same shell.
