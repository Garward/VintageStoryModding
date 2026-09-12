# Player Menu Visual, Routing, and Naming Design

Status: Approved for implementation
Date: 2026-09-12
Scope: First cohesive player-facing pass of the native VRPG replacement menu
Parents: [Unified player menu](2026-09-06-unified-player-menu-design.md) · [Visual language](../../design/ui-visual-language-and-hub.md) · [Usable player-menu checklist](../../design/usable-player-menu-checklist.md)

## 1. Outcome

The native VRPG player menu stops reading like an engineering terminal and becomes the single coherent home for Inventory, Stats, Skills, Talents, Library, and Options. The combat hotbar is the visual reference: layered charcoal surfaces, thin amber structure, inset wells, compact ornament, and orange reserved for active or actionable state. The menu applies that language at a calmer, more spacious scale.

This pass is not accepted merely because the six tab labels render. Every route must display real VRPG data or an honest loading/error/empty state, existing mutations must remain server-backed, and ordinary item names must be readable player-facing names rather than asset paths or debug identifiers.

## 2. Chosen approach

Extend the existing native `GuiDialogVrpgPlayerMenu` shell with small page adapters and shared presentation primitives. Redirect the existing Hub and Library packet flow into this shell, preserving the established server requests and response semantics while retiring their separate player-facing dialogs.

Rejected alternatives:

- A visual-only Inventory reskin leaves the replacement disconnected.
- New tabs that launch the legacy Hub or Library preserve competing windows and navigation state.
- Embedding the monolithic legacy Hub element inside the new shell duplicates navigation and makes the old renderer the new architecture.

The legacy Hub remains a behavior reference during migration, not a player-visible destination. The privileged talent editor remains a separate authoring tool.

## 3. Shared shell and navigation

The shell always exposes these top-level routes in this order:

`Inventory · Stats · Skills · Talents · Library · Options`

- Top navigation switches pages in place without closing or spawning another player dialog.
- Inventory and Character retain their semantic entry behavior. Inventory enters the current inventory context; Character enters Gear with Equipment expanded.
- Existing Hub entry points route to Stats in the same shell. Existing Library commands and links route to Library in the same shell.
- Back first unwinds an inner disclosure or prior cross-page destination, then closes the shell. Close always closes the shell.
- Page switches preserve relevant per-page state during the connection: inventory query and selection, chosen class/skill, talent viewport and draft plan, Library selection, and Options category.
- A route waiting for its server snapshot shows a composed loading state inside the destination page. It never falls back to a legacy dialog or a blank panel.
- Page failures show a short explanation and a Retry action when retry is meaningful. Technical packet, revision, asset, or exception details stay in logs.

## 4. Visual system

The hotbar replacement establishes the family resemblance, but the menu must not inherit hotbar density.

### 4.1 Chassis

- A wide centered chassis sits over a restrained translucent scrim so the world remains faintly visible without competing with text.
- The outer frame uses charcoal/near-black layers, a thin worn-copper or amber edge, small corner fasteners, and shallow clipped/notched corners.
- Content is divided into fitted plates and recessed wells. Major panes must not appear as one uninterrupted black rectangle.
- Page title, short contextual summary, navigation, body, and anchored actions form distinct layers.
- Orange/heat indicates the current page, focus, hover, a pending action, or the primary available action. It is not used as ambient decoration across every edge.
- Status colors supplement shape and text; color alone never communicates selection, failure, or disabled state.

### 4.2 Controls

- Every standard button and top-level tab centers its label horizontally and vertically using measured font extents, including pressed-state offset.
- Controls share consistent heights, internal padding, borders, hover/focus treatment, disabled contrast, and pressed depth.
- Selected tabs read as attached raised plates, not merely text with a narrow marker.
- Text-only rows, item rows, toggles, steppers, and primary actions use distinct shared primitives rather than pretending every interaction is the same rectangular button.
- Focus remains visible and distinct from hover and selection.
- Reduced motion changes state immediately and removes nonessential interpolation without removing feedback.

### 4.3 Typography and hierarchy

- Page names and section headings are visually distinct from body copy and metadata.
- Button labels are short action phrases. Explanatory copy sits beside or below controls, not inside oversized buttons.
- Long text wraps inside clipped bounds. Truncation uses an ellipsis only where the complete value is available in the inspector or tooltip.
- Quantities, levels, costs, and comparable statistics align consistently for scanning.

## 5. Page contracts for this pass

### Inventory

Retains the server-owned inventory browser, inspector, equipment disclosure, actions, Party entry, capacity, filters, sort, paging, and recovery signals. The visual pass introduces a clear browser header, item rows/tiles, inspector hierarchy, and anchored action area. It does not invent unavailable item actions.

### Stats

Uses the existing Hub snapshot to show character identity, level/resources, classes, base-stat allocation, and defensive/offensive summaries already supported by real data. Stat changes use the existing request and refresh path. Pending and rejected adjustments remain visible beside the affected control.

### Skills

Uses existing class and skill packets. Class choice, skill selection, learned/locked state, requirements, details, and skill-slot assignment remain functional. It does not show unimplemented skills as usable.

### Talents

Reuses the existing talent graph, player state, pan/zoom behavior, allocation/refund draft, connectivity validation, point costs, Apply, Reset, and feedback. The player page may expose the privileged editor action only when the server grants it; the editor itself remains separate.

### Library

Moves the existing Library content into the shell. The page provides category/search or equivalent discoverability supported by current entries, selection, readable details, and a useful empty state. It must not close the shell to create another dialog.

### Options

Preserves current notification, action-bar, combat-HUD, and combat-visual settings and their existing save destinations. Categories render within the shell with real controls and concise explanations. Unavailable future controls are omitted rather than disabled as decoration.

## 6. Player-facing item names

Raw identifiers such as `game:pickaxe`, registry paths, normalization keys, and generated internal codes are diagnostic data and do not belong in ordinary item rows or inspectors.

Name resolution follows this priority:

1. A deliberate VRPG-authored instance name, such as a generated gear name, when the record explicitly identifies it as player-facing.
2. The current client's localized collectible name resolved from the item/block asset for ordinary engine items.
3. A localized VRPG definition name for custom semantic records.
4. A humanized asset-path fallback with domain and technical separators removed, used only when content is present but lacks localization.
5. A localized `Unknown item` label for unresolved/recovery records, with the raw identifier available only in recovery details or logs where it helps repair the record.

The display resolver is presentation-only: it must not mutate canonical identity, grouping, sort tie-breaks, transaction requests, or persistence. Search may match both the displayed name and hidden asset code so recovery remains possible, but the result continues to render the player-facing name. Tests cover localized native items, authored gear names, absent localization, unresolved assets, and names containing long or non-ASCII text.

## 7. Data and dialog migration

- Introduce a client-side Hub/page service that owns the latest `OpenHubPacket`, talent snapshot, loading/error state, and existing action delegates independently of any dialog lifetime.
- `GuiDialogVrpgPlayerMenu` composes the selected page adapter from that service and the existing Inventory/Party adapters.
- Opening Stats, Skills, Talents, Library, or Options requests or refreshes the required snapshot through existing channels, then refreshes only the shell.
- Resource and talent updates target the service even when the shell is closed; opening later renders the newest accepted state.
- Retire ordinary creation of `GuiDialogVRPGHub` and `GuiDialogVRPGLibrary` after their routes reach parity. Do not remove behavior until parity tests cover it.
- One input owner handles mouse, keyboard, search focus, Escape, semantic hotkeys, and resize recomposition.

## 8. Acceptance criteria

Automated acceptance requires:

- standard button and navigation text is geometrically centered across supported sizes and GUI scales;
- all six routes are visible and switch within one shell;
- each route renders real packet-backed content or an honest loading/error/empty state;
- existing Stats, Skills, Talents, Library, and Options actions still reach their authoritative callbacks;
- ordinary native items use localized names, VRPG gear keeps authored names, and raw identifiers are absent from normal browse/inspect surfaces;
- narrow layouts preserve readable navigation and reachable primary actions;
- keyboard focus, mouse hit regions, clipping, reduced motion, and reload/reconnect state remain bounded and deterministic;
- no ordinary player path opens the legacy Hub or Library dialogs.

Live acceptance requires joining a world and confirming:

- Inventory and Character open only the VRPG shell;
- all six pages can be reached without another player menu appearing;
- controls are centered and visually consistent with the hotbar family;
- real items show readable names;
- at least one real action on each mutable page succeeds or reports an honest server rejection;
- no Vanilla Inventory, Character, Stats, or Survival panel appears.

The pass is not declared ready for player acceptance until these live checks have been performed. Final acceptance still belongs to the user.

## 9. Deferred work

This pass does not add new progression rules, party combat frames, random loot generation, new skill/talent content, new item art, or spawn/combat mechanics. It exposes the real systems that already exist through the replacement shell and leaves those later gameplay slices explicit in the project checklist.
