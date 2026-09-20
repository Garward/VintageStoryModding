# VRPG Player Menu Semantic Visual Pass

## Outcome

Turn the live unified menu from a functional wireframe into the adopted dark-forged RPG interface without changing item custody, packets, mutations, or page routing. The pass covers Inventory, Stats, Skills, and Library first because joined-client screenshots show the largest hierarchy and presentation problems there.

## Visual direction

- Materials: near-black forged chassis, visibly raised iron plates, deeper inset wells, worn-copper structure, heat-orange selection/focus, warm parchment text.
- Typography: use Vintage Story's runtime decorative font for page and section headings and its standard font for controls/body; offline captures use deterministic fallbacks.
- Icons: cached Cairo vectors drawn during composition. No runtime texture uploads. Icons supplement labels and never carry meaning alone.
- Hierarchy: brand/navigation rail, contextual page strip, one dominant workspace, selection details, then local feedback/actions.
- Empty states: centered semantic icon, direct title, one useful next-step line. No giant unstructured voids.
- Truthfulness: no sample items, invented effects, decorative controls, raw identifiers, formulas, or unresolved template tokens.

## Shared icon vocabulary

- Navigation: inventory bag, stats crest, skills blades, talent nodes, library book, options gear.
- Global: back chevron, close, party, search.
- Inventory: gear/armor, materials/ingot, consumables/flask, quest/key, miscellaneous/pouch, filter, sort, capacity, empty bag.
- Stats: strength, dexterity, intelligence, available point, level/experience, primary attribute, evasion.
- Skills: lock, damage, cooldown, range/radius, resource cost, delivery, combat slot.
- Library: book/all, skill, status, gear, dungeon, stat/mechanics, document.

Unknown menu semantics use one generic diamond/document icon, never generated initials. Real item icons remain `BoundedItemStackRenderer` output.

## Task 1 — Shared semantic presentation system

- Extend `VrpgIconPainter` with the menu vocabulary and a non-text fallback.
- Resolve decorative and body font families from the client renderer with deterministic test fallbacks.
- Add reusable section header, list row, key/value/stat chip, empty state, search well, and icon+label control primitives.
- Strengthen selected, focused, disabled, and pending shapes while reserving orange for actionable state.
- Reorder desktop shell hierarchy to navigation/brand then contextual page title and purpose; preserve narrow layouts and real Back/Close behavior.
- Add geometry/ink/state tests and captures at normal and narrow sizes.

## Task 2 — Inventory hierarchy

- Replace the three-row control wall with one category rail and one compact search/filter/sort/location toolbar while retaining every query action.
- Present capacity as structured compact values.
- Hide pagination when the result fits on one page; keep Refresh as a utility action.
- Add honest browse and inspector empty states.
- Make Equipment a fitted disclosure and List/Grid a compact view toggle rather than dominant inspector buttons.
- Preserve real item-stack icons, selection, equipment, comparison, recovery, and server feedback.

## Task 3 — Stats and Skills hierarchy

- Stats: render three attribute cards with semantic icon, aligned value, compact stepper, and local pending/rejection state. Show available points as a badge.
- Stats summary: use only decision-relevant real data—level, classes, XP progress, primary attribute, and Evasive Step state. Do not duplicate Health, Mana, shield, blood, or regeneration HUD values.
- Skills browse: class header plus icon-bearing rows and shaped Learned/Locked/Equipped status.
- Skills inspector: title/state, grouped stat chips, description, muted class context, and anchored slot assignment.
- Preserve all selection, scrolling, pending, rejection, and server-authoritative equip behavior.

## Task 4 — Library player-facing cleanup and hierarchy

- Remove unresolved placeholders such as `{value}` from ordinary names and keep formatted display/search text consistent.
- Stop using conversion formulas as ordinary stat-family summaries; prefer notes and player-relevant application text.
- Omit blank fields, generated tags, and internal source codes from ordinary details.
- Present selected identity/purpose and group remaining fields under a Mechanics section using key/value rows.
- Add semantic category and entry icons while preserving real categories, search, selection, and scrolling.
- Add real stat-family fixtures and assertions excluding braces, assignment expressions, internal sources/tags, and empty field labels.

## Verification and acceptance

- Each task starts with focused failing tests and ends with focused tests, the full suite, Release build, inspected captures, and independent review.
- Captures cover 1120×680, 800×520, 620×520, and 400×350. Normal desktop captures also run at 1.5 GUI scale where the harness supports it.
- Joined-client review must repeat Inventory, Stats, Skills, and Library after deployment and inspect `client-main.log` for atlas/composition/input errors.
- The existing user-confirmation gate stays open until E/C/V and all sibling pages are checked in the joined client.

