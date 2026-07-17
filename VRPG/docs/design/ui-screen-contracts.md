# Gate A UI Screen Contracts

These contracts apply the [UI authoring guide](../ui-authoring-guide.md) to the immediate build-playground surfaces. Values, names, and balance may be provisional. State and interactions may not be fake.

## Skills

- **Player goal:** inspect every loaded skill, understand its current resolved behavior, and assign learned skills to the visible executable slots.
- **Primary decision or action:** choose a learned skill and equip it into a selected slot.
- **Entry points:** Hub → Skills; selecting a hotbar slot may return here later.
- **Required information:** class icon/accent, skill icon, learned/locked state, rank/max rank, required player level, resolved damage, resource cost, cooldown, delivery, radius/range, tags, and equipped slot.
- **Primary control:** 4–8 real loadout slots plus `Equip`/`Clear` actions.
- **Secondary controls:** class filter, skill selection, learned-only filter when content volume requires it.
- **Persistent information:** unspent skill points when the progression system exists; otherwise do not fabricate them.
- **Explicitly excluded information:** decorative skill bars, fictional learned state, fake class ownership, debug asset counts, and hard-coded key labels.
- **Empty state:** explain that no skills are loaded or no skills match the selected class.
- **Locked/disabled state:** distinguish not learned, player-level locked, and unavailable because it belongs to another future class choice.
- **Loading/pending state:** the affected slot shows pending while an equip request is outstanding.
- **Error/rejection state:** show the server reason beside the loadout region without losing selection.
- **Success feedback:** the slot updates from the server-authored loadout and the same skill shows its assignment badge.
- **Keyboard/hotkey path:** resolved bindings for slots 1–8; menu assignment can use slot-selection keys after mouse flow is stable.
- **Server-authoritative mutations:** equip and clear.
- **Expected content volume now / later:** 3 prototype skills / dozens across the launch classes.
- **Combat-visible or modal:** modal Hub page; loadout is mirrored by a combat HUD.

For Gate A, admin commands remain the supported way to grant and rank skills. The UI must represent those real grants and ranks; it must not imply that a final skill-purchase economy already exists.

## Talents

- **Player goal:** choose one of six central starting routes, understand its dominant/secondary core-stat affinity, inspect connected effects, and allocate or refund a working passive path.
- **Primary decision or action:** allocate the selected reachable node.
- **Entry points:** Hub → Talents.
- **Required information:** node icon/tier, name, route and section, allocation state, reachability, point cost, prerequisite links, effects, available points, and the selected starting-route consequence.
- **Primary control:** `Allocate`; `Refund All` remains acceptable for the first pass until safe single-node refund rules exist.
- **Secondary controls:** pan, zoom, center selected, and inspect.
- **Persistent information:** available talent points once, near the action.
- **Explicitly excluded information:** modifiers that do not execute, impossible paths, fake allocation, and raw asset codes.
- **Empty state:** explain that no talent definitions loaded.
- **Locked/disabled state:** show insufficient points, disconnected path, or one of the four mutually exclusive starting routes.
- **Loading/pending state:** selected node action is protected while the server replies.
- **Error/rejection state:** show the server reason in the inspector.
- **Success feedback:** allocated node/path updates from server state without resetting pan, zoom, or selection.
- **Keyboard/hotkey path:** inspect/activate selected node is a later accessibility follow-up; mouse path is required now.
- **Server-authoritative mutations:** allocate and reset/refund.
- **Expected content volume now / later:** six working starting routes with one ordinary section each / a large passive graph.
- **Combat-visible or modal:** modal Hub page; allocation follows the same combat-lock policy as base stats for Gate A.

Every displayed modifier must affect an executable calculation. A talent with a future effect remains out of the registry rather than appearing as flavor-only UI.

## Combat Hotbar

- **Player goal:** read and activate the current 4–8-skill loadout without looking away from combat.
- **Primary decision or action:** cast the skill bound to a slot.
- **Entry points:** always-visible HUD while RPG is enabled.
- **Required information:** skill icon/accent, resolved binding, cooldown remaining, insufficient-resource state, empty slot, and edit/lock state.
- **Primary control:** the normal rebindable skill hotkeys.
- **Secondary controls:** explicit edit mode, drag position, and lock.
- **Persistent information:** only the configured visible slots and state needed during combat.
- **Explicitly excluded information:** skill rank, descriptions, formulas, chat-cadence errors, inactive resources, and decorative controls.
- **Empty state:** an empty slot is visibly empty and points the player to Skills only outside combat.
- **Locked/disabled state:** cooldown and affordability are visually distinct.
- **Loading/pending state:** cast prediction may begin only after a server success response or be reconciled by authoritative cooldown state.
- **Error/rejection state:** local slot feedback; rate-limited chat remains an optional expanded explanation.
- **Success feedback:** immediate cooldown sweep and cast response.
- **Keyboard/hotkey path:** each slot uses its resolved Vintage Story binding.
- **Server-authoritative mutations:** equipped skill codes and cooldown timing. Position and lock are client presentation preferences.
- **Expected content volume now / later:** four active slots by default, expandable to eight; passive foundations never consume one.
- **Combat-visible or modal:** combat-visible and movable only in explicit edit mode.

## Class Presentation

- **Player goal:** understand the identity and available prototype skills of each loaded class.
- **Primary decision or action:** select a class to filter and inspect its skills.
- **Required information:** authored icon token, accent, name, concise role, tags, and actual loaded skills.
- **Explicitly excluded information:** a `Selected Class` or ownership marker until the two-class acquisition flow is implemented.
- **Server-authoritative mutations:** none in this browser scope.
- **Expected content volume now / later:** six candidate classes with partial prototype skill coverage / the complete release roster.

Class records are data-backed even before class acquisition ships. A class with no executable prototype skill must say so directly; the interface must not invent one.
