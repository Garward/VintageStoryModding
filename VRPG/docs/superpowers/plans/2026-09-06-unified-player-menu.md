# Unified Player Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (when delegated execution is authorized) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver the complete native Inventory/Character replacement merged with the VRPG Hub, preserving exact high-count item custody and existing progression behavior.

**Architecture:** A single native composition host contains six independently testable pages. Connection services own request lifetimes; server inventory, gear, progression and service authorities own transactions. Semantic dialog registration and durable custody activate through a recoverable handshake.

**Tech Stack:** Existing C# 12 / .NET 10 project, Vintage Story native GUI/Cairo, protobuf-net packets, xUnit, current JSON world-save archives; existing browser workbench for prototype fixtures only.

**Spec:** [Unified player-menu design](../specs/2026-09-06-unified-player-menu-design.md)

**Status:** Approved; implementation in progress on `main`. Tasks 1 and 2 are implemented and reviewed through `b1ca60f`. The isolated Task 4 shell and clarity fixes are source-reviewed through `1a7d7ca`, with focused menu regressions 45/45, final Back/resize coverage 23/23, and corrected compact/narrow browser captures. Native verification requires user sign-in and remains unperformed, so Task 4's native acceptance gate is still open. Task 4 precedes Task 3 as allowed by the dependency graph; engine bridges remain pending. No native replacement or runtime readiness is claimed. Existing code must be rechecked before each task; extend working foundations rather than recreate them.

## Global constraints

- User handoff rule (2026-09-08): do not say "ready to test" until the user can join a VRPG server with no ordinary access to the Vanilla Inventory or Character menu; both configured actions must use the real server-backed replacement. Keep joined-server user confirmation pending until observed. Report verified checklist progress in the conversation as work proceeds. Isolated previews and source tests are not this acceptance gate; native authentication does not block independent code tasks.

- One shell with Inventory, Stats, Skills, Talents, Library, Options.
- Full fixed inventory capacity from character creation; no backpack slot or bag progression requirement.
- Gear, Materials, Consumables, Keys & Quest, Miscellaneous are views over one authority, not five independent bags.
- Keep checked signed 64-bit quantities end to end. Never use floating-point quantities.
- Server-owned transactions, lossless migration, recovery, and bounded engine stack bridges precede default replacement.
- Semantic Inventory and Character actions retain the player's mappings. Either action closes an already-open unified menu.
- Equipment is a disclosure inside Gear. The normal layout has a browser and an inspector, with broad margins.
- No gear score; decision-changing affixes, conditions and transformations remain visible in comparisons.
- Use `BoundedItemStackRenderer`, inset clipping, localized player text and resolved input bindings.
- Preserve unknown content and extension data; never silently downgrade an unsupported archive schema.
- The proposed capacity seed is 120 unique entries and 512 ordinary aggregate entries, with `long.MaxValue` quantity ceiling. Ledgers/protected quest records bypass ordinary capacity.
- Proposed network bounds: default 64 / maximum 128 summaries; maximum 128 search characters; 200 ms debounce; visible rows plus two overscan rows.
- Proposed layout targets: 1,120 × 680 logical pixels comfortable; 800 × 520 compact; narrower layouts switch between browse and inspector.
- Documentation changes do not authorize activation, change balance defaults, or establish missing economy/Rebirth mechanics.

## Delivery boundaries and dependency order

**A — First usable overwrite:** Tasks 1–8. Real inventory, equipment, quantities, use/bind/drop/recover, relevant Home bridges, onboarding handoff and reversible activation. Existing Hub remains available for unmigrated progression pages. Do not call this the full menu replacement.

**B — Complete Hub merge:** Tasks 9–12 plus Task 15's corresponding release checks. Six native pages, shared navigation and reference links, retained progression/settings behavior, no separate ordinary Hub/Library dialog.

**C — Complete service integration:** Tasks 13–14 plus Task 15's service checks. Storage, crafting withdrawal, salvage, saved loadouts, pickup opportunities and applicable lifecycle transactions. The shell may ship before these authorities; the full end goal remains open until each intended service is delivered or explicitly removed from scope.

Dependency graph:

```text
1 storage policy -> 2 bounded query/detail protocol -> 4 native shell -> 5 browse
                 -> 3 bridge/transaction journal ----> 6 real actions
2 + 5 + 6 -> 7 onboarding handoff -> 8 experimental activation [A]
4 + 6 -> 9 Stats -> 10 Skills/Talents -> 11 Library -> 12 Options/retirement [B]
3 + 6 -> 13 storage/crafting/salvage -> 14 Home/loadouts/lifecycle [C]
8 + 12 + 13 + 14 -> 15 complete release matrix
```

The sequence is conservative, not a reason to start duplicate owners. Workbench fixtures and shell development may proceed before activation against isolated test data. Never attach a writable canonical test page to the same live items still owned by Vanilla.

## Execution and test discipline

All paths below are relative to `VRPG/`, except explicitly named repository-root workbench paths. Create paths are proposed source boundaries, not existing APIs. Preserve the existing `src/Client/PlayerMenu/` enums/navigation and the inventory service signatures.

For each task:

1. Add its named failing tests and fixture cases before changing production behavior.
2. Run the focused filter and inspect a failure for the intended behavior. Compilation failure is acceptable only for a newly introduced contract; an unrelated environment failure is not a red test.
3. Implement the minimum vertical slice, wire it to its actual authority, then rerun the focused tests.
4. Run the full suite once at that task's integration boundary; inspect native/game behavior where specified. Record unperformed checks as unperformed.
5. Review the scoped diff and commit only that task's files with no attribution footer. Update the active checklist only for gates supported by fresh evidence.

Commands, run from repository root (normal Debug build avoids release deployment):

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj -p:DisableSharedDeploy=true -p:PackageOnRelease=false --filter FullyQualifiedName~PlayerItemContractTests
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj -p:DisableSharedDeploy=true -p:PackageOnRelease=false
git diff --check
```

Each task names a focused class/filter to substitute in the first command. Use the configured local engine references. A missing dependency is a blocker to test execution, not permission to report success. Do not install/deploy to the user's running game as a side effect of a documentation or pure-test check.

## Task 1: Close capacity, classification and protection gaps

**Files:** Modify `src/Inventory/PlayerItemContract.cs`, `PlayerItemModels.cs`, `InMemoryPlayerItemStore.cs`, `InMemoryPlayerItemStore.Operations.cs`, `InMemoryPlayerItemStore.Persistence.cs`; create `src/Inventory/PlayerItemPolicy.cs`; extend `src/Inventory/Persistence/PlayerItemPersistenceModels.cs` and serializer only for new durable fields. Tests: extend `PlayerItemContractTests.cs`, `TransactionalPlayerItemStoreTests.cs`, `PlayerItemPersistenceTests.cs`.

**Consumes:** Current `PlayerItemCapacity`, canonical payload, `IPlayerItemStore`, `PlayerItemStorageKind` and flags. **Produces:** one shared classification/capacity rule for insert, recover and restore; persisted protection source; effective capacity summary. System protection is distinct from user protection. Addon categories have registered stable definitions and Miscellaneous fallback.

- [x] Add a red test for an ordinary aggregate limit of one: one material fills it, a distinct ordinary material is refused, a Ledger and protected quest record still insert and survive restore. Protecting an ordinary material must not exempt it from capacity.
- [x] Add tests preventing `SetFlags` from clearing system protection or junk-marking quest records; retain user-unprotect behavior for ordinary items. Version/migrate new protection metadata, preserving unknown fields.
- [x] Add the test below using existing APIs, and run the focused contract/transaction filters. It captures the present capacity gap without inventing a GUI dependency.

```csharp
[Fact]
public void QuestRecordsDoNotConsumeOrdinaryAggregateCapacity()
{
    var store = new InMemoryPlayerItemStore(
        capacity: new PlayerItemCapacity(1, 1, long.MaxValue));
    var material = new PlayerItemPrototype(PlayerItemStorageKind.Aggregate,
        "game:stone", PlayerItemCategories.Materials, "Stone", new byte[] { 1 });
    Assert.True(store.Insert("material", material, 10000, store.Revision).Success);
    var quest = new PlayerItemPrototype(PlayerItemStorageKind.Protected,
        "vrpg:testkey", PlayerItemCategories.KeysAndQuest, "Test key", new byte[] { 2 });
    Assert.True(store.Insert("quest", quest, 1, store.Revision).Success);
}
```

- [x] Centralize policy evaluation and apply it to live mutations and archive validation. Freeze attributes for unsafe time-dependent content in recovery or require an audited evolution adapter; do not normalize temperature/perish state to force merging.
- [x] Test missing/renamed addon category fallback, hash collisions, checked quantity overflow, equipped unique capacity, ordinary consumable shared capacity and lossless migration from the old archive.
- [x] Run the three focused suites and full suite; commit `feat: align inventory capacity and protection policies`.

Evidence: 75 focused / 721 full-suite tests passed at `f828913`; the reviewed legacy-reservation migration fix `d29e396` passed 92 covering tests. Schema 0/1 pending quest reservations retain their identities and quantities in explicit recovery until rollback. `FrozenInRecovery` is a trusted classification contract, not automatic engine detection; Task 3 must wire unsafe-content classification before live activation. Addon registry snapshots must also be supplied when restoring addon-aware stores.

**Gate:** A full ordinary inventory cannot lose a protected reward or currency; it also cannot be enlarged by toggling Protect.

## Task 2: Add bounded query, detail, comparison and action contracts

**Files:** Create `src/Network/PlayerItemQueryPackets.cs`, `PlayerItemActionPackets.cs`, `PlayerItemDetailPackets.cs`; modify `src/Network/VRPGNetwork.cs`; create `src/Inventory/Queries/PlayerItemQueryService.cs`, `PlayerItemDetailService.cs`, `src/Inventory/Gear/GearComparisonService.cs`, `src/Inventory/Networking/PlayerItemNetworkService.cs`, `src/Client/PlayerMenu/Inventory/PlayerItemClientService.cs`; extend `src/Inventory/PlayerItemModels.cs` and indexes as needed. Tests: create `PlayerItemProtocolTests.cs`, `PlayerItemQueryServiceTests.cs`, `GearComparisonServiceTests.cs`, `PlayerItemClientServiceTests.cs`.

**Consumes:** Existing `Query`, `TryGet`, `Equipment`, `Recovery`, `GearEquipmentService.TryEquip` and resolved gear/runtime stats. **Produces:** versioned bounded display protocol and a connection-owned client cache. New packet fields are defined here, then used by later tasks:

```text
QueryRequest: ProtocolVersion, ConnectionGeneration, QueryId,
  Category (empty = all types), Location, Search, Filters, Sort, Offset, Limit
QueryResult: ConnectionGeneration, QueryId, StoreRevision, Total,
  Entries[<=128], CapacitySummary, RecoveryCount
DetailRequest: ConnectionGeneration, QueryId, EntryId, RoleCode
DetailResult: ConnectionGeneration, QueryId, EntryId, RoleCode,
  StoreRevision, BuildRevision, Effects[], Comparison[], Actions[]
ActionRequest: RequestId, Action, ExpectedStoreRevision, ExpectedBuildRevision,
  EntryId, RoleCode, DestinationId, Quantity (Int64), PreviewToken
ActionResult: RequestId, Success, RejectionCode, StoreRevision, BuildRevision,
  ChangedEntries[bounded], InvalidatedScopes[]
```

Packet implementation uses established protobuf conventions with stable field numbers; never renumber deployed members. Server derives player UID from the connection. Bind candidate/role/build context to comparison results; raw client combat values are not accepted.

- [x] Write failing serialization tests for `9_007_199_254_740_993` and `long.MaxValue`; add request-limit, bad enum, malformed search, invalid identity, replay/conflicting-request and unauthorized-player cases.
- [x] Write query tests for all-category search, typed filters, deterministic ties, zero/maximum volume and recovery paging. Extend the current query/index layer rather than fetch all entries and filter client-side.
- [x] Implement page/detail services with server limits and localizable reason codes; resolve comparisons using actual gear effects and selected equipment role. Include conditions, gained/lost transformations, level requirement and reference level for mitigation.
- [x] Test equal inventory revision but changed build revision invalidates comparison; return exact differences with units and no gear score. A stale selection or old query generation cannot replace newer client state.
- [x] Implement client debounce, cancellation/generation checks, bounded cache eviction and mutation lifetime independent of menu lifetime. On reconnect, discard old-generation responses and refresh before writes.
- [x] Add rate/payload limits before expensive allocation; record query time and bytes. Send bounded invalidations/pages, not the full item store or `OpenHubPacket` after every pickup.
- [x] Run the four new focused suites and full suite; commit `feat: add bounded inventory menu protocol`.

Evidence: `c67ee1e` passed 773 full-suite tests; `9c62db0` corrected the approved 200 ms debounce with focused evidence; reviewed fix `b1ca60f` passed 59 protocol/query/comparison/client tests. Empty-role detail requests now discover the canonical equipment role, and affix units distinguish percentage points and regeneration per second. Transformation arrays exist but remain empty because no active transformation authority currently exists; new mechanics are not invented here. Production currently registers message types only. Tasks 6/8 must attach real mutation/lifecycle handlers, invalidate runtime content changes, and establish transport-level frame limits before activation; typed post-deserialization bounds are not transport limits. An early detail-admission rejection correlation issue remains deferred for pending-state integration.

**Gate:** A test client can inspect 10,000 units as one entry, query equipment and meaningful comparisons, and reject out-of-order data without native GUI involvement.

## Task 3: Finish engine custody and bounded materialization

2026-09-09 progress: prerequisite slice 3a is implemented and reviewed through
`8cc953c`: flush-guaranteed world-bound archives, coherent receipt/canonical
generations, durable claims, conservative loaded-token pickup and exact retry
identity. Full suite 843 before review fixes; final covering 62. The engine audit
showed StoreData is buffered and ordinary spawn events occur after publication.
Fresh-source slice 3b1 is reviewed at `15e9360`: identity precedes initialization
and publication, survives native serialization, and refuses changed sources.
Focused 100/full 899 tests pass; isolated 1.22.7 startup accepted the hook and
reached ticking, then saved/stopped cleanly. Joined transfer checks remain open.
Task 3 remains open for native outbound adapters, producer guards and remaining
lifecycle integration. Read-only browse Task 5 may proceed from Tasks 2 and 4
without exposing live Vanilla-owned items. The audit
in `docs/design/player-item-flow-audit.md` records coverage and paired-backup rules.

**Files:** Extend `src/Inventory/EngineItemIngressService.cs`, `src/Patches/EngineItemFlowPatch.cs`, `src/Inventory/ItemStackCodec.cs`; create `src/Inventory/Bridges/PlayerItemBridgeCoordinator.cs`, `PlayerItemBridgeJournal.cs`, `PlayerItemBridgeModels.cs`, `EngineItemBridgeAdapter.cs`; extend persistence models/serializer and startup wiring in `src/VRPGModSystem.cs`. Tests: extend `EngineItemIngressServiceTests.cs`, `ItemStackCodecTests.cs`, `PlayerItemPersistenceTests.cs`; create `PlayerItemBridgeTests.cs`.

**Consumes:** `Reserve`, `Reservation`, `CommitReservation`, `RollbackReservation`, exact codec and durable archive. **Produces:** receipt-based engine operations for pickup, held item, use, drop, external transfer and Home placement. A receipt contains operation ID, player/store identity, amounts, source/destination, reservation, stage and actual external result.

```text
New -> durable Prepared reservation -> external effect receipt
    -> durable canonical commit -> Completed
Failure before external effect -> rollback
Unknown/partial external result -> ReconcileRequired (no blind replay)
```

- [ ] Write red tests interrupting each transition, retrying each request, and racing two consumes of the final unit. Assert both canonical quantity and world/engine destination totals.
- [ ] Audit all writes to hotbar, offhand, cursor, crafting, equipment and backpack inventories; record each ingress/egress route in `docs/design/player-item-flow-audit.md` with adapter or safe refusal. Include commands, rewards, trade, containers, death and admin grants.
- [ ] Route pickups before Vanilla chooses the active hand. Rejected pickup remains at its source; failed rewards retain a durable claim. Avoid an accepted callback plus a surviving duplicate world item.
- [ ] Materialize only codec-approved bounded stacks; journal actual consumed/returned quantity. Never replay an unknown world drop, placement or use effect solely because a network response was lost.
- [ ] Reconcile on cancel, swap, death, realm change, disconnect and restart. Reserved items cannot be deposited, salvaged or reused through a second engine route.
- [ ] Add corruption/failure tests around journal save and reconcile, run focused bridge/ingress/persistence suites and full suite; commit `feat: reconcile player item engine bridges`.

**Gate:** Every enabled ordinary item flow has one custody authority. Unsupported adapters safely refuse while preserving the source; they do not silently fall back into a second owner.

## Task 4: Build the native shell and component gallery

**Files:** Create `src/Client/PlayerMenu/PlayerMenuController.cs`, `GuiDialogVrpgPlayerMenu.cs`, `PlayerMenuSessionState.cs`; retain `PlayerMenuNavigation.cs`; create `Shell/PlayerMenuShell.cs`, `PlayerMenuLayout.cs`, `PlayerMenuTheme.cs`; create `Components/FittedButton.cs`, `MechanicalDisclosure.cs`, `TypedTabStrip.cs`, `SearchField.cs`, `ContextMessage.cs`, `QuantityEditor.cs`. Tests: extend `PlayerMenuNavigationTests.cs`; create `PlayerMenuSessionStateTests.cs`, `PlayerMenuLayoutTests.cs`, `PlayerMenuComponentStateTests.cs`, `QuantityEditorTests.cs`.

**Consumes:** Existing `PlayerMenuPage`, entry-point policy and Task 2 client cache. **Produces:** one composition host with page viewport and immutable per-page view state. Layout is pure logical geometry; pages emit named intents. Quantity editing is a pure parser/model before native input wiring.

- [ ] Add red navigation tests for category restoration, Character forcing Gear/Equipment, inner Escape, Back from Library, close while a mutation is pending, and world-identity reset.
- [ ] Add quantity cases for zero, negatives, fractions, `long.MaxValue`, overflow, grouped localized input, available/reserved amount changes, Half of one and destination-limited Max. Define parser culture explicitly and never round or clamp a typed invalid integer silently.
- [ ] Implement shell/disclosures/focus with clipped children inert while closed; native fields consume typing before global semantic actions. Separate focus from selection and preserve scroll anchors.
- [ ] Add geometry tests at the spec's targets and below compact width. Assert nonnegative bounds, reachable Close/Back/primary action, no overlap and no horizontal scroll requirement for critical controls.
- [ ] Update repository-root `tmp/Tools/vs-ui-workbench/scripts/render-inventory.js` and its fixture source during prototype work only: use canonical category codes, real capacity seeds and decimal strings for huge counts. Save fixture source and captures in tracked `content/ui-workbench/unified-player-menu/` so the temporary tool is not the only evidence owner.
- [ ] Run focused suites; inspect native gallery states and long labels at 80/100/125/150% scales. Record screenshots with viewport, scale and build. Commit `feat: add native player menu shell`.

**Gate:** Truthful native component states/layout work without replacing either Vanilla menu owner. The gallery does not advertise inventory readiness.

Implementation evidence: `66480d1`, `241017c`, `1a7d7ca`; full suite 810 before final gallery refinements, subsequent focused menu coverage 45/45 and final Back/resize coverage 23/23. Two scoped fix reviews are clean. Compact 125% and narrow 150% prototype defects were corrected and recaptured. The native state/scale/input inspection checkbox stays open: the isolated engine launch reached an expired session, not a loaded world. These source and browser results do not establish native acceptance or authorize live replacement.

## Task 5: Implement all five browse views and recovery inspection

**Files:** Create `src/Client/PlayerMenu/Inventory/InventoryPage.cs`, `InventoryViewModel.cs`, `InventoryQueryState.cs`, `InventorySelectionState.cs`, `InventoryItemGrid.cs`, `InventoryItemRow.cs`, `RecoveryView.cs`; reuse `src/Client/UI/BoundedItemStackRenderer.cs`; add localized keys to `assets/vrpg/lang/en.json`. Tests: create `InventoryViewModelTests.cs`, `InventorySelectionStateTests.cs`, `InventoryVirtualizationTests.cs`; extend `BoundedItemStackRendererTests.cs`.

**Consumes:** Task 2 bounded pages/details and Task 4 shell/components. **Produces:** stable selection and explicit entry/quantity selections across typed tabs; all-types query mode; read-only recovery location view.

- [ ] Write red tests for five stable empty core categories, hidden unused addon categories, all-types results with category labels, unknown fallback and no-results clearing.
- [ ] Render Gear grid/list and material/consumable rows. Quantity overlay formatting must not alter the exact detail/quantity-editor value. Currency balances and protected records use their own meaningful presentation.
- [ ] Preserve selected ID and scroll on refresh; removed selection cannot transfer a queued action to another row. “Select this page” enumerates only that returned page; filter changes preserve explicit selections visibly or allow Clear selection.
- [ ] Show capacity from the server, reserved quantity, favorites/protection and recovery count. Test disabled unresolved records and stale cached content; loading never shows a fabricated zero inventory.
- [ ] Stress 10,000 recovery entries and 10,000,000 identical units; assert bounded pages and visible rows plus two overscan rows. Clip every live item model using the shared renderer.
- [ ] Run focused suites and capture normal/empty/maximum/long-name/recovery layouts; commit `feat: browse typed inventory and recovery`.

**Gate:** All five categories and Recovery are inspectable from real test-store data at scale; no placeholder equips or decorative actions.

## Task 6: Complete equipment and ordinary item actions

**Files:** Create `src/Client/PlayerMenu/Inventory/EquipmentDisclosure.cs`, `ItemInspector.cs`, `InventoryActionController.cs`, `QuickUseBindingView.cs`; create `src/Inventory/PlayerItemActionService.cs`, `src/Inventory/Loadouts/PlayerItemLoadoutService.cs`, `PlayerItemLoadoutModels.cs`; extend `src/Inventory/Gear/GearEquipmentService.cs`, `src/Client/CombatWeaponSelection.cs`, `CombatActionBarMode.cs` and relevant HUD consumers. Tests: create `InventoryActionControllerTests.cs`, `PlayerItemActionServiceTests.cs`, `PlayerItemLoadoutTests.cs`; extend `GearEquipmentServiceTests.cs`, `CombatWeaponSelectionTests.cs`.

**Consumes:** Task 2 action/detail contracts, Task 3 bridge receipts and Task 5 stable selection. **Produces:** real equip/unequip, use, quick-use bind, quantity transfer/drop/discard, mark/protect and Claim recovery intents; canonical item-ID weapon/quick-use bindings.

- [ ] Write red tests for role choice, level/role/two-hand restrictions, replaced-item identity, duplicate equip, unrelated stale build state, reserved/protected items and a full unique inventory unequip.
- [ ] Route gear through `GearEquipmentService.TryEquip`; resolve worn groups from supported role definitions. Keep mechanical affixes visible and optional appearance/lore/roll ranges disclosed.
- [ ] Add exact quantity actions and durable confirmations for permanent discard. A split transfers a requested amount to a concrete destination; it never manufactures identical canonical rows.
- [ ] Bind usable entries without filling Vanilla storage slots; wheel selection follows eligible equipped weapon references. Depletion shows empty/unavailable rather than choosing new loot automatically.
- [ ] Enforce one in-flight conflicting mutation per resource/role and authoritative reconciliation on rejection. Closing a page retains request tracking. Confirmed server result, not optimistic client math, updates build projections.
- [ ] Test recovery Claim at full capacity, unsupported content, consume/realm swap/disconnect and local multi-selection undo. Do not expose service salvage/deposit controls until Task 13.
- [ ] Run focused suites and perform mouse/keyboard real-item loop in an isolated test world; commit `feat: manage equipment and items from the player menu`.

**Gate:** Ordinary item management no longer requires a removed backpack/crafting/character dialog. Task 8 still controls live ownership replacement.

## Task 7: Complete first-character and cosmetic handoff

**Files:** Extend `src/Modules/Rpg/Onboarding/FirstRunOnboardingFlow.cs`, `FirstRunOnboardingState.cs`, `src/Modules/Rpg/RpgModule.Gear.cs`, `src/Client/GuiDialogVRPGStarterPreview.cs`, `src/Network/OnboardingPackets.cs`; create `src/Client/PlayerMenu/Onboarding/PlayerMenuOnboardingRouter.cs`, `AppearanceReturnContext.cs`, `WelcomeView.cs`; reuse `src/Realms/Onboarding/OnboardingInstancePolicy.cs` and tavern preparation. Tests: extend `FirstRunOnboardingFlowTests.cs`, `StarterGearSetFactoryTests.cs`, `RpgGearRuntimeTests.cs`; create `PlayerMenuOnboardingRouterTests.cs`.

**Consumes:** Existing durable first-run stages, starter equipment/skill authority, validated safe-arrival service, Task 6 views. **Produces:** resumable Appearance → starter → Welcome → safe release, with real menu links and a cosmetic-only return route.

- [ ] Recheck current onboarding code against its dedicated spec; implement only missing stages. Test disconnect/retry at grant and acknowledgement; an already-committed starter must never grant again.
- [ ] Verify actual gear and allocated first skill appear after server confirmation. Use the standard gear/item services rather than UI-created starter copies.
- [ ] Keep safe arrival and protection server-owned. No menu close, appearance revisit or late packet may complete onboarding or release protection by itself.
- [ ] Route Welcome to the appropriate menu entry and Getting Started content. Before Task 11, use the existing read-only Library route without removing its working dialog.
- [ ] Verify cosmetics cannot expose Vanilla class mechanics or repeat kit choice. Capture fresh-character/reconnect/revisit flows; run focused suites and full suite; commit `feat: connect character onboarding to the player menu`.

**Gate:** First playable overwrite includes a safe new-character route; character creation's foundations are not mistaken for completed release behavior.

## Task 8: Coordinate custody and semantic-dialog activation

**Files:** Create `src/Client/PlayerMenu/PlayerMenuDialogReplacementCoordinator.cs`, `src/Inventory/Migration/PlayerMenuActivationCoordinator.cs`, `PlayerMenuActivationState.cs`, `src/Network/PlayerMenuActivationPackets.cs`; extend existing entry adapters, capability state, replacement plan, migration coordinator/journal, `src/Network/CombatInteractionPackets.cs` (current capability packet owner), `src/Modules/Rpg/RpgModule.cs`, `src/VRPGModSystem.cs`, `src/Config/VRPGConfig.cs`. Tests: extend `PlayerMenuDialogReplacementPlanTests.cs`, `PlayerMenuCapabilityStateTests.cs`, `PlayerItemMigrationTests.cs`; create `PlayerMenuActivationTests.cs`, `PlayerMenuNativeOwnershipTests.cs`.

**Consumes:** Tasks 1–7 readiness, current durable migration journal and semantic action identifiers. **Produces:** per-player activation state with connection generation, durable custody state and client readiness acknowledgement. Do not equate a config flag with readiness.

```text
VanillaOwned -> client Prepared (both adapters + native page validated)
             -> server MigrationPrepared (journal durable, source intact)
             -> source clear / durable CanonicalOwned
             -> client Active (acknowledged current connection)
Pre-commit failure: VanillaOwned with both original dialogs
Post-commit failure: CanonicalOwned + recovery lock until repair,
                     or journaled lossless reverse migration
```

- [ ] Write red tests for missing/duplicate dialog owner, protocol mismatch, no client acknowledgement, disconnect at every transition, partial source clear, duplicate acknowledgement and late old-generation acknowledgement.
- [ ] Prepare both adapters before removing either original registration. Swap using semantic actions; leave physical mappings untouched. Prevent mutation by both stores during the handoff.
- [ ] Derive advertised capability from completed per-player prerequisites. Keep `DialogReplacementReady` false until real native/custody activation is valid; retain recovery access for already-migrated reconnects even when normal activation fails.
- [ ] Prove restoring GUI registrations alone cannot unlock stale Vanilla custody. Reverse migration validates real destination/recovery capacity before any mutation; huge quantities cannot force unbounded slot materialization.
- [ ] Exercise enabled/disabled restart, programmatic old-dialog opens, default/remapped input, text entry, mod disposal and multiplayer clients at different onboarding stages.
- [ ] Run focused/full tests and native activation matrix on disposable worlds; commit `feat: activate inventory menus with recoverable custody handoff`.

**Milestone A:** Mark only the first usable overwrite complete after its entire checklist passes. Keep experimental gating until default-release validation. Leave working legacy Hub/Library paths for unmigrated pages.

## Task 9: Move Stats into the shared shell

**Files:** Create `src/Client/PlayerMenu/Stats/StatsPage.cs`, `StatsViewModel.cs`, `StatAllocationPlan.cs`; extract relevant rendering from `src/Client/UI/GuiElementVrpgHub.cs`; extend `src/Network/StatAllocationRequestPacket.cs` and progression handlers if atomic plans/revisions are absent. Tests: create `StatsPageModelTests.cs`, `StatAllocationPlanTests.cs`; extend `RpgResourceCalculatorTests.cs` where projections need coverage.

**Consumes:** Real character state and existing stat-allocation rules. **Produces:** allocation view plus server-validated plan/result contract, including independent build revision used by Task 2 comparisons.

- [ ] Capture existing allocation behavior in red tests: points, refunds/limits, affinity effects, concurrent changes, unavailable state and stale plan rejection.
- [ ] Implement local planned points with Apply/Reset; add a server atomic batch endpoint if current requests mutate one point immediately. Never simulate an atomic Apply through multiple independent requests.
- [ ] Render current/next-point effects and optional derived details; omit repeated combat bars. Preserve plans across page switches and revalidate after equip/progression changes.
- [ ] Run focused suites, compare real old/new allocation outcomes, and commit `feat: integrate character stats into the player menu`.

## Task 10: Move Skills and Talents with existing authority

**Files:** Create `src/Client/PlayerMenu/Skills/SkillsPage.cs`, `SkillViewModel.cs`, `SkillLoadoutController.cs`, `src/Client/PlayerMenu/Talents/TalentsPage.cs`, `TalentViewModel.cs`, `TalentPlanController.cs`; reuse `src/Client/UI/TalentGraphComponent.cs`; extract page responsibilities from `GuiElementVrpgHub.cs`; adapt `src/Network/HubActionPackets.cs`, `TalentTreePackets.cs` only where contract gaps require it. Tests: create `SkillMenuModelTests.cs`, `TalentPlanControllerTests.cs`; retain `SkillMutationAuthorityTests.cs`, `SkillPacketAuthorityTests.cs` as regression coverage.

**Consumes:** Existing learned/equipped skill and talent services; Task 4 view/focus state and Task 9 build revision. **Produces:** native browser/inspector and tree/inspector with truthful requirements and atomic pending-safe actions.

- [ ] Test exact skill costs, cooldowns, delivery/requirements and loadout targets; locked/empty/unknown-content and stale request cases. Cover a requested skill action while gear readiness is false.
- [ ] Move skill controls without weakening authority or auto-learning a selected skill. Show supported actual learn/improve actions only.
- [ ] Move talent local plan validation and graph rendering; test connected paths, point accounting, refunds, disconnected plans, replay and concurrent progression changes. Reuse the service's real reset/respec rules.
- [ ] Preserve pan/zoom/selection; keep authoring behind explicit authorization and distinct tool mode. No generator metrics in player view.
- [ ] Run focused suites/full suite and stress-tree native clipping/focus; commit Skills and Talents as separately reviewable commits if their extraction diffs are large.

## Task 11: Merge Library and contextual navigation

**Files:** Create `src/Client/PlayerMenu/Library/LibraryPage.cs`, `LibraryViewModel.cs`, `LibraryNavigationContext.cs`; reuse content from `src/Client/UI/GuiElementVrpgLibrary.cs` and `src/Data/Library/`; adapt `src/Client/GuiDialogVRPGLibrary.cs`, `GuiDialogVRPGHub.cs`, `src/Network/OpenLibraryPacket.cs` and command routing. Tests: create `LibraryNavigationTests.cs`, `LibraryViewModelTests.cs`.

**Consumes:** Existing player-facing Library entries/discovery authority and Task 4 navigation history. **Produces:** in-shell search/reading and stable item/skill/Welcome deep links with Back.

- [ ] Test Inventory item → Library → Back preserves entry, role, search and scroll; repeat for skill/talent and Getting Started.
- [ ] Respect discovery restrictions, hide empty future categories, and localize no-results. Technical/source information is optional detail.
- [ ] Route ordinary Library entry points into the existing shell instead of closing/reopening dialogs. Keep compatibility fallback when inventory replacement is disabled.
- [ ] Run focused tests and native reading/keyboard checks; commit `feat: merge Library into the player menu`.

## Task 12: Move Options and retire duplicate ordinary dialogs

**Files:** Create `src/Client/PlayerMenu/Options/OptionsPage.cs`, `OptionsViewModel.cs`, `OptionsActionController.cs`; reuse `src/Network/HubOptionsPacket.cs` and configuration authority; reduce or retire `GuiElementVrpgHub.cs`, `GuiDialogVRPGHub.cs`, `GuiDialogVRPGLibrary.cs` only after all callers migrate; update `src/VRPGModSystem.cs` and command/hotkey routes. Tests: create `OptionsActionControllerTests.cs`, `PlayerMenuRoutingTests.cs`.

**Consumes:** Existing real client/server options, all five prior native pages. **Produces:** sixth page, capability-aware routes, no duplicate normal Hub/Library windows under replacement ownership.

- [ ] Inventory every old Options action and add parity cases for HUD layout/lock, hotbar, notifications and combat visuals. Add local/server authorization, pending rejection and restart-required cases.
- [ ] Implement grouped settings with ownership labels; reduced motion/input help use actual client preferences. Inventory ownership remains a validated restart/migration workflow.
- [ ] Remove obsolete close-and-open dialog calls. Preserve feature-disabled fallback and authorized authoring routes. Legacy Hub action still opens Stats until its separately announced retirement.
- [ ] Run route/option tests, complete six-page state/scale checks, and commit `feat: finish the unified Hub menu migration`.

**Milestone B:** One native shell covers all existing Hub capabilities. Do not call unfinished storage/salvage integrations complete.

## Task 13: Integrate authorized storage, crafting and salvage

**Files:** Create `src/Inventory/Services/ItemServiceAccess.cs`, `ItemTransferService.cs`, `BulkItemPreviewService.cs`, `BulkItemCommitService.cs`, `ItemServiceTransactionJournal.cs`; create `src/Network/PlayerItemServicePackets.cs`, `src/Client/PlayerMenu/Inventory/StorageScopeSelector.cs`, `BulkActionPreview.cs`; extend Task 3 journal/persistence and Task 2 networking. Tests: create `ItemServiceAccessTests.cs`, `BulkItemPreviewTests.cs`, `ItemTransferTransactionTests.cs`.

**Consumes:** External storage/crafting/salvage authorities, Task 3 journal and Task 6 selection. **Produces:** access-scoped destination queries, exact quantity transfers, Deposit Materials/Deposit by Rule/Take for Recipe and confirmed salvage. Economy recipes/yields/ownership are supplied by their own approved services, not invented here.

- [ ] Establish provider contracts for authorized destinations, rules revision, preview inputs/outputs and idempotent external commit/reconcile. Reject unsupported providers before reservation; UI hides unavailable service types.
- [ ] Write red tests for two players withdrawing the last units, revoked access after preview, full output destination, changed salvage rules, duplicate commits and interruption after each durable/external step.
- [ ] Generate server preview tokens bound to explicit selection, quantities, rules/permissions and revisions. Include exclusion reasons for favorite/protected/bound/equipped/reserved/quest entries. Preview input changes invalidate confirmation.
- [ ] Implement exact transfers and atomic recipe input/output accounting through journaled coordination, with stable multi-store lock ordering. Never implement bulk transactions as independent Extract loops.
- [ ] Implement bounded “Select all matching” token evaluation; refuse selections beyond advertised transaction limits with a smaller-scope action. State explicitly when only the visible page is selected.
- [ ] Show scoped storage toolbar, offline/revoked access and exact quantity preview in two panes. Run focused/full tests and two-client service journeys; commit service slices separately with their providers.

**Gate:** No material is lost or duplicated across stores, crafting output or salvage under concurrent/retried/interrupted actions. An unavailable economy provider remains an explicit unfinished integration, not fake success.

## Task 14: Finish Home palettes, saved loadouts and lifecycle presentation

**Files:** Extend `src/Inventory/Loadouts/PlayerItemLoadoutService.cs`, bridge/journal and service access; create `src/Client/PlayerMenu/Inventory/HomeBuildPaletteView.cs`, `SavedLoadoutView.cs`, `src/Client/PlayerMenu/Components/OpportunityStrip.cs`, `src/Inventory/Services/ItemLifecyclePreviewService.cs`. Tests: create `HomeBuildPaletteTests.cs`, `SavedItemLoadoutTests.cs`, `ItemLifecyclePreviewTests.cs`, `OpportunityPresentationTests.cs`.

**Consumes:** Approved Home permissions, expedition risk/extraction policy, progression loadout/Rebirth transactions and item bridges. **Produces:** menu presentation and requests for real context-specific services, with no new respec or death-penalty rule.

- [ ] Test large-count placement, denied plot permission, cancel, realm travel, disconnect and missing palette reference. Apply actual block consumption through Task 3 receipts.
- [ ] Persist palette/loadout entry references; validate missing/level-restricted gear and skills. Preview all changes before atomic apply; retain gear/skill authority and costs. Do not silently substitute unavailable equipment.
- [ ] Bind death/extraction/Rebirth previews to the provider's secured/at-risk/reset/preserved contract and rule revision. If a provider is absent, keep its action absent and mark that service integration incomplete.
- [ ] Implement at most three deduplicated contextual leads backed by real item/objective/unlock state. Dismissed/completed leads disappear; logout cannot expire rewards.
- [ ] Run focused/full tests, Home/Adventure transition loops and actual extraction/rebirth fixtures once their providers exist; commit each provider integration independently.

**Milestone C:** Complete only after each intended provider has end-to-end acceptance evidence. Unresolved storage namespaces, economy yields and Rebirth retention are separate design dependencies, not instructions for the UI worker to guess.

## Task 15: Prove the complete replacement and set release policy

**Files:** Create `tests/VRPG.Tests/UnifiedPlayerMenuJourneyTests.cs`, `docs/testing/unified-player-menu-acceptance.md`; extend prior task tests; update `docs/design/usable-player-menu-checklist.md`, `native-player-menu-implementation-plan.md`, `testable-version-todo.md` only after evidence; change effective preset/experimental release flags only after their gates pass.

**Consumes:** Completed task deliverables, disposable single-player/multiplayer worlds and recorded provider capabilities. **Produces:** release evidence with passed, failed and unperformed cases, plus an explicit decision for first overwrite, full Hub merge and full service completion.

- [ ] Run all eight acceptance journeys in spec §15; assert exact source/destination identities and quantities, not only UI labels. Split quantity journey must finish with 9,963 stored and 37 at the named destination after restart.
- [ ] Run the full .NET suite with deployment disabled. Record build/engine version, test count and failures; no inherited historical totals.
- [ ] Exercise 16:9, 16:10, ultrawide, compact and below-compact layouts at 80/100/125/150% GUI scales. Include long localized-style strings, largest integer, maximum entries, open Equipment and no selected item.
- [ ] Exercise mouse and keyboard paths, all remapped semantic actions, search typing, visible focus, inner Escape, close during pending and reduced motion. Test controller against the actual supported host layer; absence of support is a documented unmet parity requirement, not a passed test.
- [ ] Capture loading/empty/no-results/locked/disabled/pending/success/stale/offline/permission/full/reserved/unresolved/recovery states with bright/dark/effect-heavy world backgrounds.
- [ ] Use two clients to race equip/consume/withdraw, reconnect during migration/use/drop, and revoke access during preview. Inject interrupted save generations and every activation/journal boundary; restore exactly one owner.
- [ ] Measure page bytes, query latency, render rows/icons and UI frame cost on normal and stress data. Quantity changes from 10,000 to 10,000,000 must not change entry/element count; pages never exceed 128. Establish and record latency/frame baselines on GarBox before setting numerical performance gates.
- [ ] Verify disabled-mode and reverse-migration behavior separately, including insufficient Vanilla capacity. Confirm no programmatic duplicate dialog path, hidden Survival authority, starter duplication or gameplay protection leak.
- [ ] Review each service dependency and every unchecked item. Default-on applies only to a release that passes the required custody, native and onboarding gates. Full end-goal completion additionally requires B and C evidence.
- [ ] Save source-linked screenshots/log summaries without credentials, update readiness documents, review the scoped diff and commit `docs: record unified player menu release evidence` with any separately reviewed release-flag change.

## Coverage and review record

Spec §§1–3 and 14 map to Tasks 1–3/8 and the delivery boundaries. Navigation/layout/input (§§4–6/12) map to Tasks 4–5/15. High counts (§7) map to Tasks 1–3/5–6/13. Equipment/actions/recovery (§§8–10) map to Tasks 2–3/6/8/13–14. The Hub/onboarding (§11) maps to Tasks 7/9–12/14. Authority/performance (§13) maps to Tasks 2–3/8/13/15. Every acceptance journey (§15) is a Task 15 release case.

Planning review checks: no readiness flag is treated as implementation; no client or renderer owns quantities; no code path assumes GUI restoration reverses custody; no protected flag creates extra capacity; no speculative service is presented as shipped. Concrete APIs above are proposed contracts to implement/test, not claims that these types already exist.

Remaining review choices: keep or tune 120/512 capacity seed; approve personal Recovery with activity-defined access; validate the compact layout thresholds. Service ownership/recipes/Rebirth rules retain their separate specification gates. The implementation plan is ready for design review, not an instruction to flip replacement on immediately.
