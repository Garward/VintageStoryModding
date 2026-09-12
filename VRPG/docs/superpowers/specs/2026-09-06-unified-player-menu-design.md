# Unified Inventory, Character, and VRPG Hub Design

Status: Proposed end-state specification for review; existing adopted contracts remain authoritative
Date: 2026-09-06
Scope: Complete native player menu and its delivery dependencies, beyond the first usable overwrite
Parents: [Inventory, loot, and storage](../../design/inventory-loot-and-storage.md) · [Visual language](../../design/ui-visual-language-and-hub.md) · [Gear foundation](2026-09-05-vrpg-gear-foundation-design.md) · [Onboarding](../../design/character-creation-and-onboarding.md)
Plan: [Delivery plan](../plans/2026-09-06-unified-player-menu.md)

## 1. Outcome

A player finishes character creation, opens Inventory, finds their real starter equipment, and manages items and their build in one coherent native menu. Inventory, Character, VRPG Hub, and Library no longer open competing windows. Ordinary material collection requires no manual stack maintenance. Equipment, skills, talents, and stats explain the choices that change the build.

This document consolidates prior decisions and proposes the remaining interaction details. It does not enable the overwrite, assert that onboarding is complete, or approve new economy/Rebirth rules. The [player-menu acceptance checklist](../../design/usable-player-menu-checklist.md) defines a possible future takeover; the [playable vertical-slice checklist](../../design/testable-version-todo.md) controls current implementation order.

## 2. Existing decisions and observed implementation

Carry forward these adopted requirements:

- One shell with Inventory, Stats, Skills, Talents, Library, Options.
- Full fixed inventory capacity from character creation; no backpack slot or bag progression requirement.
- Gear, Materials, Consumables, Keys & Quest, Miscellaneous are views over one authority, not five independent bags.
- Attribute-exact high-count storage; unique gear remains distinct; currency uses balances and quest-critical records remain protected.
- Equipment is a disclosure inside Gear. The normal layout has a browser and an inspector, with broad margins.
- Semantic Inventory and Character actions retain the player's mappings. Either action closes an already-open unified menu.
- Server-owned transactions, lossless migration, recovery, and bounded engine stack bridges precede default replacement.
- Oxidized iron, fitted plates, recessed content, restrained copper/heat, localized motion and reduced motion.

Repository inspection on 2026-09-06 found:

- `src/Inventory/` already contains canonical records, queries, reservations, flags, equipment references, recovery, persistence, codec, and migration machinery.
- `PlayerItemCapacity.Default` is 120 unique entries, 512 aggregate entries, and `long.MaxValue` per aggregate. Stored and equipped unique records both count. These are current defaults, not evidence of final expedition balance.
- The current aggregate capacity count includes every non-Unique kind, including Ledger and Protected. Exempting protected keys/currencies from ordinary inventory pressure still needs implementation.
- `GearEquipmentService.TryEquip` validates RPG gear before custody changes. The menu must call this domain boundary, not bypass it with direct generic store equip.
- `src/Client/PlayerMenu/` has navigation, capability checks, entry adapters and replacement planning, not the full native controller/pages.
- `GuiElementVrpgHub` still holds multiple page responsibilities; `GuiDialogVRPGHub` closes to open the separate Library.
- The browser inventory prototype uses sample material quantities, a 60-item capacity, and abbreviated category identifiers. Those are fixture artifacts, not server policy.
- Common engine grant ingress exists behind experimental configuration. This is not proof of complete pickup/use/drop/trade/placement coverage.

No game session or tests were run as part of this documentation inspection. Historical checklist test totals are not new verification evidence.

## 3. Approach choice

**Recommended: a native shell with small page controllers and shared components.** Reuse the authoritative store, gear/runtime services, talent graph, rendering helper and current page behavior. Move page responsibilities out of the existing Hub incrementally. This matches the adopted architecture and keeps item authority separate from UI lifetime.

**Alternative: expand the existing Hub element in place.** Fast for one demonstration, but compounds the large renderer's input, layout, packets, and state responsibilities. Reject for the end-state architecture.

**Alternative: embed the browser workbench as the runtime menu.** Useful for visual experiments, but adds a separate input, rendering and transport integration problem. Keep the workbench as a reference and test fixture surface; native code remains the runtime.

## 4. Navigation and session rules

- Inventory action opens Inventory on the last used category during this session; a fresh session begins on Gear. Restore that category's search, filters, sort, selection and scroll.
- Character action opens Inventory > Gear with Equipment expanded, regardless of the last category. Prefer the main-weapon role when nothing was selected.
- Legacy Hub action opens Stats during migration. Library commands/links route to Library within the same shell once migrated.
- Either Inventory or Character action while the shell is open closes it, as already specified. Do not silently change that behavior into page switching.
- Top navigation switches pages in place. A Library link from an item preserves a Back destination with the prior item and view state.
- Escape closes the innermost popup/quantity editor first, then the shell. The visible Close control always closes the shell. Text editing respects the host's input handling; a bound printable key typed into search must not trigger a global toggle.
- Closing sends no implicit mutation and does not cancel a server transaction already submitted. Track that request through the connection service and reconcile on reopen.
- Unsubmitted stat/talent plans persist during the same connection with an explicit Apply/Reset plan action. Revalidate them against fresh progression before Apply; disconnect discards only unsubmitted local plans.
- No client view state crosses player/world identity. Reconnect invalidates old query generations and refreshes server state before mutations resume.
- This is a gameplay-blocking input modal, not a world pause or combat invulnerability. Only onboarding's separate server service owns first-run protection.

## 5. Layout and appearance

Normal layout, schematic only; example numbers are fixtures:

```text
+----------------------------------------------------------------------------+
| Inventory | Stats | Skills | Talents | Library | Options             Close |
+----------------------------------------------------------------------------+
| Gear | Materials | Consumables | Keys & Quest | Miscellaneous               |
| Search items…                     Search all types [ ]   Filters   Sort    |
+-------------------------------------------+--------------------------------+
| 120 / 120 unique items                     | Selected weapon                |
|                                           | Rare · Requires level 20       |
| [bounded item tiles or rows]               |                                |
|                                           | Property    Equipped  Selected |
| One record per exact item state           | Weapon Power     …         …   |
| A material entry can show 12,840           | Relevant effects and tradeoffs |
|                                           |                                |
|                                           | Equip [role]     Favorite      |
|                                           | Equipment >  /  More details > |
+-------------------------------------------+--------------------------------+
| Recovery: 2 items   [Review]               | Feedback beside its action     |
+----------------------------------------------------------------------------+
```

The same chassis hosts all six pages. No persistent paper doll, currency dashboard, Survival nutrition, temperature, wetness, or backpack region. Character name/level/classes belong in Stats and the Equipment header when useful, rather than repeated on every pane.

Proposed layout targets: comfortable at 1,120 × 680 logical pixels; compact at 800 × 520. Above compact width, give approximately 60% to browsing and 40% to inspection. Below it, show browse or inspector in the same viewport with an anchored Back control. Wrap top navigation and use a category selector when the tab strip cannot fit. Shrink visible rows before shrinking type. Native testing decides the supported floor; do not claim these dimensions already pass.

Equipment expands inside the inspector and shares its vertical scrolling; its focused role and action stay reachable. At narrow sizes it replaces the inspector body, with Back to the item. It never introduces a third major pane. A cosmetic preview is opt-in inside Appearance, not a permanent dashboard.

Cache iron textures, use semantic theme tokens, clip growing regions, and retain fixed navigation/action areas. `BoundedItemStackRenderer` is mandatory for every item model, with inset stacked scissor clipping and separate quantity/state overlay layers. Focus and selection have distinct shape cues. Reduced motion changes state immediately.

## 6. Inventory browse contract

**Goal:** find an item and choose what to do with it. **Entry:** Inventory action or a contextual link. **Primary control:** the selected item's action. **Secondary:** category, search, sort, filters, mark and multi-select. **Persistent context:** category, search scope, applicable capacity and recovery access. **Excluded:** raw IDs, revisions, quantity implementation details and unrelated balances.

- Gear uses readable icon tiles by default; a list mode exposes longer names and properties. Materials/Consumables favor rows with icon, name and a reserved quantity column. Both are virtualized views of bounded pages.
- Selection uses a stable entry ID. Sorting is deterministic with an entry-ID tie break. Refresh never selects a different object merely because it occupies the previous row.
- Default search stays in the active category. Search all types is explicit; results show their category. Clearing search restores the prior category context.
- Filters open on demand: item family, rarity, usable at current level, equipped, favorite, protected, junk and data-defined tags. Only supported filters appear.
- Cross-tab marking and bulk selection use explicit entry IDs with quantities. “Select this page” means exactly that page, not unseen matches. End-state “Select all matching” requires a server-bounded selection token and preview; it must never infer the set from only loaded rows.
- Keep five core categories stable even when empty; hide unused optional addon categories. Unknown categories route to Miscellaneous with intact records. Unresolved content remains discoverable in Recovery.
- After a selected entry disappears, retain its removal result briefly and move focus to the nearest surviving row; do not automatically perform any action on the new row.

## 7. High counts and capacity

High count is a storage contract, not a Vanilla global stack-size override. One exact material state with 10,000 or 10,000,000 units remains one entry; unlike attributes remain separate entries. Perishable/temperature/charge-bearing items aggregate only under a declared exact-state and evolution policy. Time-dependent updates run under server authority; an unsafe or unaudited policy preserves separate recoverable states rather than normalizing away attributes.

- Keep checked signed 64-bit quantities end to end. Never use floating-point quantities. Browser fixtures use decimal strings or BigInt for values above JavaScript's safe integer range.
- Show grouped exact counts when they fit. Compact tile labels may abbreviate, but selection/focus always exposes the full integer. Quantity input, action preview, available/reserved amounts and confirmations are always exact.
- Quantity editor has amount, available quantity, and One/Half/Max shortcuts. Half means integer floor and is disabled when that yields zero; Max means currently available quantity, bounded by the action/destination. Reject zero, negative, fractional and out-of-range input without submission.
- A split is a requested transfer quantity into a named destination, quick-use bridge or allowed world drop. Splitting an aggregate into two identical canonical rows serves no purpose and is not an action.
- The proposed first balance seed retains 120 unique and 512 ordinary aggregate entries. Equipped unique items count toward the same 120 so unequipping does not invent extra capacity. Consumables share the ordinary aggregate budget initially; there is no additional consumable cap.
- Show the shared budget honestly, such as “84 / 120 unique items” or “230 / 512 stack types.” Do not label a shared count as “Gear slots” or imply every tab owns 512 types.
- Ledgers and protected quest records are exempt from ordinary entry pressure. They still have schema/payload/checked-arithmetic limits and durable failure handling; there is no promise of infinite machine storage.
- The server supplies effective limits. Configuration/addon category changes require validation and lossless migration where custody or capacity changes. Rebirth does not grant more backpack rows.

The 120/512 seed, personal Recovery policy below, and compact layout thresholds are proposals for review/playtesting. A representative long expedition must not fill ordinary material capacity; increase the seed or improve classification if it does.

## 8. Gear and character equipment

Equipment groups use existing role codes: main weapon, two alternate weapons, offhand, head/body/legs/hands/feet, two accessories, then supported cosmetic roles behind Appearance. A role appears only when supported by the active rules. No invented stat-bearing clothing slot or backpack. Empty roles say “Choose weapon” or the relevant item type; selecting one filters eligible Gear entries without losing the previous browse state.

The selected item shows name, family, rarity, required level, current slot status, Weapon Power or defensive profile, and every decision-changing affix or transformation. Compare against the selected eligible role; items usable in multiple roles require a role selector. Show signed numerical differences with units, plus explicit gained/lost mechanics. No gear score or universal green upgrade verdict.

Resolve the old affix-disclosure ambiguity as follows: hide lore, provenance, roll-range reference and formula detail under More details; never hide effects that change the equip decision. Armor mitigation estimates state their reference enemy level; elemental caps and conditional effects explain their conditions. Do not present Armor, Evasion, Magic Shield and resistances as interchangeable ratings.

Equip submits through gear validation, then updates inventory/equipment/build projections from the authoritative result. During pending, disable repeat equip for that affected role. A replaced item remains the same canonical entry in storage. Level, two-hand/offhand, combat, reservation and role restrictions come from server capabilities, with a local reason. Do not invent a blanket combat restriction in UI code.

Appearance editing reuses the cosmetic service and returns to Equipment. It cannot repeat starter selection/grants, reset progression, or expose inert Vanilla class traits. Saved loadouts reference existing item/skill IDs; previews show missing or restricted entries. Full build-preset transactions remain dependent on the progression contract, not an implied free respec.

## 9. Other inventory actions

**Consumables:** exact available count, real effect, cooldown/requirements, Use and Bind quick-use. Bound entries need no physical Vanilla hotbar slot. Show depleted/unavailable state without silently substituting a different item. Manual replacement or an explicitly configured equivalent-item refill policy is required.

**Materials:** quantity, relevant recipes/uses, deposit/withdraw at an authorized service, and Add to Build palette in a permitted Home context. Placement materializes only the bounded amount the engine needs. An expedition never inherits unlimited Home storage access.

**Keys & Quest:** explain what an item opens or advances and link to its known objective/Library entry. Protected records cannot be junked, dropped, destroyed or bulk-salvaged. Ordinary currency appears as a relevant balance beside the transaction using it, not as item tiles.

**Miscellaneous:** preserve uncategorized usable items with their valid actions. Do not offer use/equip when the item lacks a supported adapter. Show “This item cannot be used here” and a relevant recovery/detail route.

**Marks:** Favorite and Protect are explicit controls. Favorite, protected, equipped, bound, reserved and quest entries are excluded from bulk salvage/deposit by default. Junk never overrides those exclusions. System protection cannot be removed with the player Protect toggle; preserve the protection source server-side. User-protecting an ordinary item never changes its storage kind or exempts it from capacity.

**Bulk actions:** Deposit Materials, Deposit by Rule, Take for Recipe, Salvage selected. A destructive preview contains exact inputs, quantities, exclusions, destination, guaranteed outputs and any declared uncertainty. It is bound to the selected entries, rules, permissions and revisions; changed state invalidates it and requires a new preview. No automatic confirmation after refresh. Undo applies to local selection only, never claims to reverse committed salvage.

**Drop/Discard:** distinct actions where game rules allow them. Drop names the world destination and ownership policy; Discard states the exact permanent loss. Neither may bypass protection or reservations. Multi-entry output and external transfers require durable transaction coordination, not sequential Extract calls with hoped-for rollback.

**Dropped-item access (user decision, 2026-09-09):** Options provides a two-choice
setting for intentionally dropped Adventure items: **Everyone** or **Party only**.
This governs who may pick up the drop, not who owns the player's inventory. It does
not change enemy-generated loot allocation or authorize bypassing item protection.
The drop action shows the chosen audience before submission; the server validates
and saves that access rule with the durable drop transaction. A visual setting
alone is not implementation of pickup permissions.

Implementation defaults proposed to the user: Party only initially, owner-only
when solo, and changing the preference affects future drops rather than existing
ones. User-confirmed membership rule: party access follows current membership;
leaving or being kicked immediately revokes shared access to existing party loot.
Never infer a party grant from a client-supplied list of players. The user also
approved a basic permissions-only party system, with party health bars and combat
features deferred. See the approved [party design](2026-09-09-basic-party-permissions-design.md)
for lifecycle, own-item reclaim and compact panel details. These decisions are not
evidence of a completed runtime contract.

**Loot piles (user decision, 2026-09-09):** consolidate nearby compatible world
drops into bounded loot piles where possible, avoiding an entity per contained
item. A pile is a presentation and pickup grouping, not permission to collapse
distinct gear, affixes, conditions, unknown payloads or item identities. Exact
quantities and per-item access remain authoritative. Party-only and public access
must never broaden through grouping; incompatible access may use separate piles
or separately enforced records within one pile.

Pile creation, adding items, partial pickup and empty-pile removal must reconcile
with durable item ownership across restart and retries, without loss or duplicate
pickup. Bound pile contents and updates, use nearby/chunk-local lookup rather than
scanning every world drop, and verify entity-count reduction under repeated drops.
Grouping changes neither loot rewards nor expiry/ownership policy. Implement this
with the drop/pickup adapter; native stack merging alone does not prove the VRPG
identity, access or durability contract.

## 10. Recovery and storage access

Proposed default: excess unique gear routes to a durable personal Recovery cache where the activity permits it. Show one grouped “2 items sent to Recovery” notice with Review. Recovery is a location filter within Inventory, not a sixth core item category. It exposes reason, item, exact quantity, access rule and Claim action; claims obey normal capacity and reservation checks.

Overflow never auto-equips, auto-salvages, silently expires or becomes an unreported ground drop. Recovery is not ordinary crafting/quick-use supply. Activities that forbid field recovery must state the alternative before entry. If no durable destination is available, refuse the grant or leave the original pickup/reward claim intact. Boss/objective rewards remain durable claims outside ordinary drop expiry.

Missing addon items retain their exact payload. Show “Item unavailable. Its content pack is missing.” Claim/use stays unavailable until resolution succeeds. Technical asset information is one step away for troubleshooting; no raw exception in the main pane.

At an authorized storage service, show a destination selector in the browse toolbar and access scope beside it. The inspector becomes the transfer context; no permanent third storage pane. Offline or revoked access disables transfer while preserving the last view as stale. Server namespaces/permissions are resolved by the storage service; party/settlement ownership is not decided by this menu proposal.

Death, extraction and Rebirth show only the risk/reset contract supplied by their authorities. Extraction commits secured rewards atomically. Rebirth preview explicitly names what resets, remains or becomes legacy; hide the action until that contract and transaction exist.

## 11. Merging the existing Hub pages

Every page uses the same shell, context history, local feedback, focus rules and request lifecycle.

- **Stats:** character name, level/XP, chosen classes, available points, primary-stat allocations and exact next-point consequences. Derived defenses/resources expand on request. Keep live combat bars on the HUD. Empty means no points to spend, with current stats still readable; pending disables Apply; stale state retains and revalidates the local plan.
- **Skills:** compact class selector, searchable browser, selected skill with exact effects, costs, cooldown, delivery and requirements, and real loadout targets. Locked skills show the requirement; empty filters offer Clear filters. Learn/equip uses existing progression authority; never display a fake Learn control for unsupported progression.
- **Talents:** current graph component, points, pan/zoom/fit, selected-node effect/cost, and Apply/Reset plan. Preserve graph viewport across page switches. Distinguish allocated, planned, reachable, refund-planned and disconnected through shape/text as well as color. Rejection leaves an editable revalidated plan. Authoring tools require a separate authorized mode.
- **Library:** browse/search plus reading pane; live links from items, mechanics, skills and Welcome. Back returns to the originating selection. Hide undiscovered content according to server rules and omit empty future categories. Read-only cache may remain readable offline with a stale indication where discovery matters.
- **Options:** group interface/accessibility, HUD/controls, loot preferences, and authorized server options using the existing real settings. Show local/server ownership and restart effects. Item ownership changes are restart/migration operations, not live casual switches. Default/locked/pending/rejected/success states must be truthful.
  Include the accepted dropped-item pickup setting, Everyone / Party only, under
  loot preferences. Its effect is the access rule on a new deliberate world drop;
  it is not a switch between canonical and Vanilla inventory ownership.

New-character handoff is Welcome → normal world play, with a clear Inventory/Equipment route and Library > Getting Started link. Menu reads the actual granted starter gear and allocated skill. Closing the ordinary menu cannot release unfinished onboarding protection; appearance revisits cannot restart onboarding.

Contextual opportunities are limited to at most three real, dismissible leads: immediate item/build possibility, tracked objective, long-term unlock. No daily streak, expiry pressure, redundant badge wall, or fictional future service.

## 12. Common state and input contract

Apply these states to every interactive screen and service:

- Loading: bounded skeleton/“Loading items…”; do not show a false zero balance or enable mutations.
- Empty category: “No materials yet” or equivalent; other tabs remain available.
- No matches: “No items match these filters” with Clear filters.
- Focus/hover/selected: distinct persistent focus and selection; hover is supplemental.
- Locked/disabled: concise requirement beside or through focused inspection of the action.
- Pending: one request indicator near the action; preserve browse/focus and block conflicting repetition.
- Success: actual revised state and short local confirmation, not a full modal celebration.
- Stale: “Your items changed. Review the updated selection.” Refresh affected pages and invalidate destructive previews.
- Disconnected/offline: “Connection lost. Item actions are unavailable.” Cached values are visibly stale; no queued destructive replay.
- Permission lost/full/reserved: explain the affected destination or quantity and next available action.
- Partial content/recovery: preserve records and show the recovery route without pretending they are usable.

Mouse supports selection, explicit buttons, context actions and optional drag shortcuts. Drag is never the only route, owns no detached item, and cancels locally before submission. Keyboard supports visible focus, category/page navigation, search, row selection, quantity entry and explicit activation. Controller uses the same named actions if the host input layer supports it; controller parity remains an unpassed gate until verified. Never hard-code displayed bindings.

## 13. Authority, queries and performance

The shell owns no inventory or progression mutations. Pages consume immutable view models and emit intents. Connection services survive page closure and coordinate request identities; domain services own legality and persistence.

Proposed packet contracts extend the existing store API rather than sending exact payloads to the browser:

- Query: protocol, connection generation, query ID, category/all-types scope, location, text, typed filters, sort, offset/cursor and bounded limit.
- Page: matching query ID/generation, store revision, total matches, bounded summaries, effective capacity and recovery summary.
- Detail/compare: entry ID, candidate role, inventory and build revisions, server-resolved effects, action availability and rejection reason codes.
- Mutation: request ID, action enum, expected relevant revisions, entry/role/destination and exact requested quantities. Player identity comes from the authenticated connection.
- Result: request ID, success/rejection, resulting revisions and bounded changed summaries/invalidation hints. Persist idempotency for committed operations. Do not reuse a request ID with a changed payload.

Proposed initial bounds: 64 summaries per page, hard server maximum 128; 128 characters of search input; 200 ms search debounce; one active query generation per view; virtualize visible rows plus two overscan rows. Exact payloads are requested only through bounded details when needed. Server search/rate limits and per-entry payload limits are enforced before allocation.

Reject late query generations. Revisions across inventory and character progression are independent: comparisons require both, not the assumption that two unrelated counters match. A pending equip invalidates dependent build projections; never show an old defense total as the confirmed result of new gear. Delta invalidation may trigger a bounded page refresh; no full-store broadcast after each pickup.

Cross-store transfers need durable prepare/reserve/commit/recovery records with stable lock ordering. External block/world side effects need their own reconciliation receipt; store idempotency alone cannot guarantee a world drop or placement occurs exactly once. Crash and reconnect tests must inspect both sides.

Instrument query duration, payload bytes, entry count, rows/icons rendered, pickup batches and failures. Stress fixtures cover 10,000 distinct recovery entries and 10,000,000 identical units in one aggregate. Quantity growth must not grow UI element count or serialized unit objects.

## 14. Ownership activation and fallback

Do not weaken the existing overwrite gate to satisfy a visual milestone. Discover both semantic dialog owners and validate all replacement dependencies before removing either. Preserve original registrations for rollback/disposal. No physical key remapping or global keyboard-dispatch patch.

Server custody migration and client dialog registration are a coordinated activation protocol, not one in-process atomic transaction. Prepare the client shell/adapters before migration; acknowledge readiness, commit durable custody, then activate canonical views. On startup refusal before custody changes, keep Vanilla intact. After custody has committed, a client/menu failure must enter a recoverable locked state or complete a journaled reverse migration before Vanilla resumes item access. Restoring dialog references alone does not restore item custody.

Feature-off is a restart plus validated lossless reverse migration for populated stores. Do not materialize a million items into Vanilla slots or silently discard excess. Refuse the switch before mutation if bounded Vanilla capacity plus declared recovery cannot preserve everything. Exactly one canonical owner exists at each stage.

## 15. Completion and review decisions

The full end goal is complete only when the native six-page shell replaces ordinary Hub/Library paths, both semantic actions work with remapping, all five inventory categories expose real actions, character creation hands off correctly, and scale/input/failure/multiplayer checks pass. Supported storage, salvage, loadout and Home services are connected as their authorities ship; unsupported services have no decorative buttons. The six-page shell may ship earlier, but the complete end goal remains open until each intended service integration passes acceptance or is explicitly removed from scope.

Required acceptance journeys:

1. Fresh character → appearance/starter/Welcome → Gear → inspect actual equipped starter → Skills sees actual first skill.
2. Collect 10,000 identical materials → one entry → transfer 37 → restart → exact remaining and destination quantities.
3. Fill unique capacity → next allowed reward enters Recovery → salvage an eligible item through preview → claim reward without duplication.
4. Equip a level-eligible item into a chosen role → authoritative defense/weapon effects update → compare another item → Library → Back preserves selection.
5. Search all types → mark explicit cross-tab entries → bulk preview excludes bound/protected items → stale change rejects the old preview.
6. Home placement and quick-use → cancel/consume/travel/disconnect → exactly reconciled quantities and held-item state.
7. Missing addon, failed migration, interrupted transfer, unsupported protocol and revoked storage permission → intact recoverable records and actionable UI.
8. Stats/Skills/Talents/Options actions retain existing behavior in the shell; no duplicate old dialog owner; no fake controls.

Review choices remain limited and explicit: adopt or tune the 120/512 seed; adopt personal Recovery with activity-defined access; approve compact layout thresholds after prototype review. Base storage namespaces and exact Rebirth reset rules remain owned by their separate subsystem specifications. None is represented as a previously approved numerical design decision.
