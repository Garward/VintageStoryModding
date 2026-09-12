# Player Menu Visual, Routing, and Naming Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the current native Inventory replacement into the cohesive hotbar-themed six-page VRPG player menu, with centered controls, real Hub/Library behavior, and localized player-facing item names.

**Architecture:** Keep `GuiDialogVrpgPlayerMenu` as the only ordinary player-menu dialog. Add a connection-owned `HubPageClientService` to hold existing Hub, talent, Library, and Options state independently of dialog lifetime; render each page through focused `PlayerMenuShell` partials. Centralize Cairo styling in shared shell primitives and resolve display names at the client presentation boundary without changing canonical item identity.

**Tech Stack:** C# 12 / .NET 8, Vintage Story 1.22 client API, Cairo cached composition, ProtoBuf network packets, xUnit, Harmony-backed menu replacement.

**Spec:** `VRPG/docs/superpowers/specs/2026-09-12-player-menu-visual-routing-design.md`

## Global Constraints

- The approved visual target is `VRPG/docs/design/assets/player-menu-inventory-visual-target.png`; it governs theme and hierarchy, not gameplay capabilities.
- The one shell exposes `Inventory · Stats · Skills · Talents · Library · Options` in that order.
- Every standard button and top-level tab centers its label horizontally and vertically using measured font extents.
- Real data or an honest loading/error/empty state is required on every page; never invent a functional control.
- Raw identifiers and asset paths stay out of ordinary browse/inspect UI.
- Existing mutations remain server-backed and preserve their present request/response semantics.
- The privileged talent editor remains separate; ordinary Hub and Library dialogs are retired only after parity tests pass.
- Inventory and Character semantic bindings remain the replacement entry points and continue their existing close behavior.
- Preserve unrelated user files and do not stage animation workbench content or audit backups.

---

### Task 1: Shared hotbar-derived shell primitives and centered controls

**Files:**
- Create: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuDrawing.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuTheme.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuLayout.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.Party.cs`
- Test: `VRPG/tests/VRPG.Tests/PlayerMenuDrawingTests.cs`
- Test: `VRPG/tests/VRPG.Tests/PlayerMenuLayoutTests.cs`
- Test: `VRPG/tests/VRPG.Tests/InventoryCompositionCaptureTests.cs`

**Interfaces:**
- Produces: `PlayerMenuDrawing.CenteredOrigin(MenuRect bounds, MenuTextMetrics metrics, double pressOffset) -> (double X, double Y)`.
- Produces: shared `DrawChassis`, `DrawPlate`, `DrawWell`, `DrawButton`, `DrawSectionHeading`, and `DrawBodyText` methods used by every later page partial.
- Consumes: existing `MenuRect`, `MenuControlState`, `FittedButton`, `PlayerMenuTheme`, and Cairo `Context`.

- [x] **Step 1: Write failing geometry and composition tests**

Add a pure text-metrics record and assertions that account for Cairo bearings rather than assuming the baseline is the font size:

```csharp
[Theory]
[InlineData(10, 20, 100, 40, 30, 12, 2, -9, 45, 47.5)]
public void CenteredOriginUsesMeasuredInkBounds(
    double x, double y, double width, double height,
    double inkWidth, double inkHeight, double xBearing, double yBearing,
    double expectedX, double expectedY)
{
    var origin = PlayerMenuDrawing.CenteredOrigin(
        new MenuRect(x, y, width, height),
        new MenuTextMetrics(inkWidth, inkHeight, xBearing, yBearing), 0);
    Assert.Equal(expectedX, origin.X, 3);
    Assert.Equal(expectedY, origin.Y, 3);
}
```

Extend layout tests to assert a dedicated header/title/navigation/body/footer rhythm at 1120×680, 800×520, 620×520, and 400×350. Extend composition capture assertions so all six page controls exist in production Inventory mode and their label origins fall inside their hit regions.

- [x] **Step 2: Run focused tests and verify the new expectations fail**

Run:

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~PlayerMenuDrawingTests|FullyQualifiedName~PlayerMenuLayoutTests|FullyQualifiedName~InventoryCompositionCaptureTests"
```

Expected: failure because `PlayerMenuDrawing`, `MenuTextMetrics`, and the six production navigation controls do not exist.

- [x] **Step 3: Implement shared measured drawing primitives**

Create the pure geometry API and keep the Cairo adapter small:

```csharp
public readonly record struct MenuTextMetrics(
    double Width, double Height, double XBearing, double YBearing);

public static class PlayerMenuDrawing
{
    public static (double X, double Y) CenteredOrigin(
        MenuRect bounds, MenuTextMetrics metrics, double pressOffset)
    {
        double x = bounds.X + (bounds.Width - metrics.Width) / 2d - metrics.XBearing;
        double y = bounds.Y + (bounds.Height - metrics.Height) / 2d - metrics.YBearing + pressOffset;
        return (x, y);
    }
}
```

Move the shell's generic fill, stroke, text, and button drawing behind shared methods. Use `Context.TextExtents` for every single-line button label, add layered outer chassis/plate/well rendering, selected attached-tab treatment, focus inset, and restrained fastener/notch details. Keep multiline body text left-aligned. Update theme tokens for scrim, chassis layers, inset shadows, amber edge strengths, and semantic states. Adjust layout so title and navigation no longer collide and all six tabs remain reachable at supported sizes.

- [x] **Step 4: Render the production capture matrix and visually inspect it**

Run the focused tests, then capture at minimum `normal`, `long-name`, `equipment`, `loading`, and `rejected` for 1120×680, 800×520, 620×520, and 400×350 using the existing `VRPG_MENU_CAPTURE_*` harness. Inspect generated PNGs for centered labels, clipping, contrast, selected-tab attachment, and non-terminal panel hierarchy.

- [x] **Step 5: Commit the visual foundation**

```bash
git add VRPG/src/Client/PlayerMenu/Shell VRPG/tests/VRPG.Tests/PlayerMenuDrawingTests.cs VRPG/tests/VRPG.Tests/PlayerMenuLayoutTests.cs VRPG/tests/VRPG.Tests/InventoryCompositionCaptureTests.cs
git commit -m "feat: establish VRPG player menu visual system"
```

---

### Task 2: Resolve localized player-facing item names

**Files:**
- Create: `VRPG/src/Client/PlayerMenu/Inventory/PlayerItemDisplayNameResolver.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs`
- Modify: `VRPG/assets/vrpg/lang/en.json`
- Test: `VRPG/tests/VRPG.Tests/PlayerItemDisplayNameResolverTests.cs`
- Test: `VRPG/tests/VRPG.Tests/InventoryCompositionCaptureTests.cs`

**Interfaces:**
- Produces: `PlayerItemDisplayNameResolver.Resolve(PlayerItemEntryPacket entry, Func<PlayerItemEntryPacket, string?> localizedCollectibleName) -> string`.
- Produces: `PlayerItemDisplayNameResolver.HumanizeAssetPath(string assetCode) -> string`.
- Consumes: `PlayerItemEntryPacket.DisplayName` as an authored name only when it is demonstrably different from the technical asset path; never changes `EntryId` or `AssetCode`.

- [x] **Step 1: Write failing resolver tests**

Cover authored gear, ordinary native localization, technical persisted names, absent localization, unresolved assets, non-ASCII names, and long names:

```csharp
[Fact]
public void NativeLocalizationReplacesPersistedDebugPath()
{
    var entry = Entry(asset: "game:pickaxe-iron", display: "pickaxe-iron");
    Assert.Equal("Iron Pickaxe", resolver.Resolve(entry, _ => "Iron Pickaxe"));
}

[Fact]
public void AuthoredGearNameWinsOverChassisLocalization()
{
    var entry = Entry(asset: "game:sword-iron", display: "Emberwake", category: "gear");
    Assert.Equal("Emberwake", resolver.Resolve(entry, _ => "Iron Sword"));
}

[Fact]
public void MissingLocalizationHumanizesPathWithoutDomain()
{
    var entry = Entry(asset: "addon:ancient_vault-key", display: "ancient_vault-key");
    Assert.Equal("Ancient Vault Key", resolver.Resolve(entry, _ => null));
}
```

Assert unresolved records show localized `Unknown item` in normal rows while retaining asset code only in Recovery details.

- [x] **Step 2: Run the resolver tests and verify they fail**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~PlayerItemDisplayNameResolverTests
```

Expected: failure because the resolver does not exist and the shell currently renders `entry.DisplayName` directly.

- [x] **Step 3: Implement presentation-only name resolution**

Resolve the native name from the client world registry by reconstructing a one-count `ItemStack` from `AssetKind` and `AssetCode`, then asking the collectible for its localized held-item name. Catch lookup/name failures and return `null` to the pure resolver. Treat a stored value equal to the full asset code, path, or separator-normalized path as technical. Preserve a distinct authored name for unique Gear. Humanize only the path portion by splitting hyphens, underscores, and camel-case boundaries, collapsing whitespace, and title-casing with the current culture. Add `menu-unknown-item` and concise recovery-detail copy to `en.json`.

- [x] **Step 4: Integrate the resolver across rows, inspector, equipment, and search-visible labels**

Replace every normal inventory/equipment `DisplayName` draw with the resolver result. Do not change query identity, sort tie-breaks, action packets, or canonical storage. Add a capture scenario containing `game:pickaxe-iron`, `addon:ancient_vault-key`, authored `Emberwake`, and an unresolved asset; assert raw identifiers are absent from ordinary captured text calls.

- [x] **Step 5: Run focused tests and commit**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~PlayerItemDisplayNameResolverTests|FullyQualifiedName~InventoryCompositionCaptureTests"
git add VRPG/src/Client/PlayerMenu/Inventory/PlayerItemDisplayNameResolver.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs VRPG/assets/vrpg/lang/en.json VRPG/tests/VRPG.Tests/PlayerItemDisplayNameResolverTests.cs VRPG/tests/VRPG.Tests/InventoryCompositionCaptureTests.cs
git commit -m "fix: show player-facing inventory item names"
```

---

### Task 3: Add connection-owned Hub page state and six-page routing

**Files:**
- Create: `VRPG/src/Client/PlayerMenu/Hub/HubPageClientService.cs`
- Create: `VRPG/src/Client/PlayerMenu/Hub/HubPageActions.cs`
- Modify: `VRPG/src/Client/PlayerMenu/PlayerMenuController.cs`
- Modify: `VRPG/src/Client/PlayerMenu/GuiDialogVrpgPlayerMenu.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs`
- Modify: `VRPG/src/VRPGModSystem.cs`
- Test: `VRPG/tests/VRPG.Tests/HubPageClientServiceTests.cs`
- Test: `VRPG/tests/VRPG.Tests/PlayerMenuReplacementCompositionTests.cs`
- Test: `VRPG/tests/VRPG.Tests/PlayerMenuSessionStateTests.cs`

**Interfaces:**
- Produces: `HubPageClientService(Action<OpenHubRequestPacket> requestHub, HubPageActions actions)`, plus `Packet`, `TalentTree`, `Loading`, `ErrorKey`, `Changed`, `RequestSnapshot()`, `Receive(OpenHubPacket)`, `Receive(TalentTreeSnapshotPacket)`, `Receive(TalentProgressPacket)`, `Receive(RpgResourcePacket)`, and `ResetConnection()`.
- Produces: `HubPageActions` record containing the existing stat, option, hotbar, auto-throw, HUD, skill-equip, talent-plan, talent-editor, and config-persist callbacks.
- Modifies: `PlayerMenuController` exposes `HubPageClientService? Hub` and calls `Hub.RequestSnapshot()` when navigating to any non-Inventory page without current state.

- [x] **Step 1: Write failing service lifecycle and route tests**

Assert that snapshots are connection-owned, stale talent trees request refresh, resources update closed-menu state, world reset clears player data, all six tabs exist in production, and navigating to Library does not instantiate another dialog:

```csharp
[Fact]
public void ClosedMenuStillAcceptsResourceUpdates()
{
    var service = new HubPageClientService(_ => requests++ , Actions());
    service.Receive(new OpenHubPacket());
    service.Receive(new RpgResourcePacket { Level = 12 });
    Assert.Equal(12, service.Packet!.Resources!.Level);
}
```

- [x] **Step 2: Run focused tests and verify failure**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~HubPageClientServiceTests|FullyQualifiedName~PlayerMenuReplacementCompositionTests|FullyQualifiedName~PlayerMenuSessionStateTests"
```

Expected: failure because Hub state is still owned by `GuiDialogVRPGHub` and production navigation exposes only Inventory.

- [x] **Step 3: Implement the service and controller ownership**

Create a bounded service that normalizes null arrays/options, exposes immutable read access to the latest accepted packets, coalesces duplicate open requests while loading, clears pending state after response, and raises `Changed` once per accepted update. Store selected class, selected skill, Options category, Library query/selection, and talent viewport/draft in dedicated service state rather than shell fields.

Wire `GuiDialogVrpgPlayerMenu` to subscribe to `Hub.Changed`, refresh/recompose as needed, and pass Hub state to `PlayerMenuShell`. Always render `Enum.GetValues<PlayerMenuPage>()`; remove the Inventory-only `availablePages` branch and `page-unavailable` rejection for supported routes.

- [x] **Step 4: Redirect network handlers into the connection service**

In `InitializeClientPlayerMenu`, construct `HubPageClientService` with the existing channel callbacks and client configuration methods. Change handlers as follows:

```csharp
.SetMessageHandler<OpenHubPacket>(packet => clientHubPages?.Receive(packet))
.SetMessageHandler<TalentTreeSnapshotPacket>(packet => ReceivePlayerMenuTalentTree(api, packet))
.SetMessageHandler<TalentProgressPacket>(packet => clientHubPages?.Receive(packet))
.SetMessageHandler<OpenLibraryPacket>(packet => OpenPlayerMenuLibrary(packet))
.SetMessageHandler<RpgResourcePacket>(packet => ReceivePlayerResources(api, packet))
```

The resource handler must continue updating the combat console/HUD after updating Hub page state. World leave and mod disposal reset/dispose the service and unsubscribe events.

- [x] **Step 5: Run focused tests and commit**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~HubPageClientServiceTests|FullyQualifiedName~PlayerMenuReplacementCompositionTests|FullyQualifiedName~PlayerMenuSessionStateTests"
git add VRPG/src/Client/PlayerMenu/Hub VRPG/src/Client/PlayerMenu/PlayerMenuController.cs VRPG/src/Client/PlayerMenu/GuiDialogVrpgPlayerMenu.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs VRPG/src/VRPGModSystem.cs VRPG/tests/VRPG.Tests/HubPageClientServiceTests.cs VRPG/tests/VRPG.Tests/PlayerMenuReplacementCompositionTests.cs VRPG/tests/VRPG.Tests/PlayerMenuSessionStateTests.cs
git commit -m "feat: connect all VRPG player menu routes"
```

---

### Task 4: Implement real Stats and Skills pages

**Files:**
- Create: `VRPG/src/Client/PlayerMenu/Hub/StatsPage.cs`
- Create: `VRPG/src/Client/PlayerMenu/Hub/SkillsPage.cs`
- Create: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.StatsSkills.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs`
- Modify: `VRPG/assets/vrpg/lang/en.json`
- Test: `VRPG/tests/VRPG.Tests/StatsSkillsPageTests.cs`
- Test: `VRPG/tests/VRPG.Tests/HubPageCompositionCaptureTests.cs`

**Interfaces:**
- Produces: `StatsPage.Adjust(string code, int delta)` with one pending mutation at a time and response feedback reconciliation.
- Produces: `SkillsPage.SelectClass(int index)`, `SelectSkill(int index)`, and `EquipSelected(int slot)` using `HubPageActions.EquipSkill`.
- Consumes: normalized `OpenHubPacket.BaseStats`, `Resources`, `Classes`, `Options`, and feedback.

- [x] **Step 1: Write failing state/action tests**

Assert stat-code allowlisting (`strength`, `dexterity`, `intelligence`), pending duplicate suppression, class/skill index clamping after refresh, learned-skill-only assignment, same-slot unequip semantics, and exact callback arguments.

```csharp
[Fact]
public void LearnedSkillUsesOneBasedVisibleSlot()
{
    var page = SkillsFixture(learnedLevel: 1, equippedSlot: 0);
    page.EquipSelected(2);
    Assert.Equal((3, "vrpg:cleave"), fixture.LastEquip);
}
```

- [x] **Step 2: Run focused tests and verify failure**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~StatsSkillsPageTests
```

- [x] **Step 3: Implement page state and renderers**

Extract behavior from the legacy Hub without copying its full renderer. Stats renders a page title, resource/level summary, class identity, base-stat allocation rows, and supported combat/defense summaries. Skills renders class selectors, virtualized skill cards, selected-skill detail, learned/locked/requirement state, and visible hotbar slot assignment. Use shared plates/wells/buttons and localized copy. Keep raw class/skill codes out of ordinary text.

- [x] **Step 4: Add capture fixtures and inspect all supported sizes**

Capture loading, empty, normal, pending, and rejection states at 1120×680, 800×520, 620×520, and 400×350. Verify primary actions remain visible, long descriptions clip/wrap, and controls do not overlap.

- [x] **Step 5: Run focused tests and commit**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~StatsSkillsPageTests|FullyQualifiedName~HubPageCompositionCaptureTests"
git add VRPG/src/Client/PlayerMenu/Hub/StatsPage.cs VRPG/src/Client/PlayerMenu/Hub/SkillsPage.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.StatsSkills.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs VRPG/assets/vrpg/lang/en.json VRPG/tests/VRPG.Tests/StatsSkillsPageTests.cs VRPG/tests/VRPG.Tests/HubPageCompositionCaptureTests.cs
git commit -m "feat: add native Stats and Skills pages"
```

---

### Task 5: Implement the native Talent page with draft-plan parity

**Files:**
- Create: `VRPG/src/Client/PlayerMenu/Hub/TalentPage.cs`
- Create: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.Talents.cs`
- Modify: `VRPG/src/Client/UI/TalentGraphComponent.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs`
- Modify: `VRPG/src/Client/PlayerMenu/GuiDialogVrpgPlayerMenu.cs`
- Modify: `VRPG/assets/vrpg/lang/en.json`
- Test: `VRPG/tests/VRPG.Tests/TalentPageTests.cs`
- Test: `VRPG/tests/VRPG.Tests/HubPageCompositionCaptureTests.cs`

**Interfaces:**
- Produces: `TalentPage.SelectAt`, `Pan`, `ZoomAt`, `QueueAllocation`, `QueueRefund`, `Apply`, `ResetDraft`, `EffectiveTalents`, and `PlannedPoints`.
- Consumes: `TalentGraphComponent` as reusable graph geometry/state, `OpenHubPacket.Talents`, `Resources.UnspentTalentPoints`, respec points, and `TalentTreeSnapshotPacket`.

- [ ] **Step 1: Write failing parity tests around the extracted draft model**

Port the existing legacy behavior into direct tests: exactly one starter root, allocation cost, refund credit, respec limit, connected effective plan, pruning disconnected queued allocations, right-click refund, response success clearing the draft, response rejection retaining it, and snapshot change revalidation.

- [ ] **Step 2: Run focused tests and verify failure**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~TalentPageTests
```

- [ ] **Step 3: Extract reusable graph behavior and implement the page**

Move draft-plan decisions out of `GuiElementVrpgHub` into `TalentPage`; keep reusable graph layout/drawing calculations in `TalentGraphComponent`. Render the graph in a large recessed well with a selected-node inspector, point/respec summary, legend, Apply/Reset actions, pan/zoom input, and honest empty/loading/error states. Preserve server validation as final authority.

- [ ] **Step 4: Route mouse and wheel input through the single dialog owner**

In `PlayerMenuShell`, only capture talent drag when the pointer begins inside the graph well. Convert screen to local logical coordinates once, preserve click-versus-drag threshold, and consume wheel input only over the graph. Keep Escape/back behavior owned by `GuiDialogVrpgPlayerMenu`.

- [ ] **Step 5: Capture, inspect, run tests, and commit**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~TalentPageTests|FullyQualifiedName~HubPageCompositionCaptureTests"
git add VRPG/src/Client/PlayerMenu/Hub/TalentPage.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.Talents.cs VRPG/src/Client/UI/TalentGraphComponent.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs VRPG/src/Client/PlayerMenu/GuiDialogVrpgPlayerMenu.cs VRPG/assets/vrpg/lang/en.json VRPG/tests/VRPG.Tests/TalentPageTests.cs VRPG/tests/VRPG.Tests/HubPageCompositionCaptureTests.cs
git commit -m "feat: add native Talent page"
```

---

### Task 6: Implement native Library and Options pages

**Files:**
- Create: `VRPG/src/Client/PlayerMenu/Hub/LibraryPage.cs`
- Create: `VRPG/src/Client/PlayerMenu/Hub/OptionsPage.cs`
- Create: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.LibraryOptions.cs`
- Modify: `VRPG/src/Client/PlayerMenu/GuiDialogVrpgPlayerMenu.cs`
- Modify: `VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs`
- Modify: `VRPG/assets/vrpg/lang/en.json`
- Test: `VRPG/tests/VRPG.Tests/LibraryOptionsPageTests.cs`
- Test: `VRPG/tests/VRPG.Tests/HubPageCompositionCaptureTests.cs`

**Interfaces:**
- Produces: `LibraryPage.SetQuery`, `SelectCategory`, `SelectEntry`, `VisibleEntries`, and bounded scroll state.
- Produces: `OptionsPage.SelectCategory` plus typed methods for existing notification, action-bar, combat-HUD, and combat-visual actions.
- Consumes: existing `LibraryEntryPacket[]`, `HubOptionsPacket`, `RpgHudConfig`, `CombatVisualsConfig`, and `HubPageActions`.

- [ ] **Step 1: Write failing Library and Options behavior tests**

Assert deterministic category order, case-insensitive search across readable names/summary/tags, selection reconciliation, bounded scroll, honest empty state, exact callback arguments, 4–8 visible skill-slot clamp, auto-throw toggle, resource-HUD sizing bounds, and combat visual cycles.

- [ ] **Step 2: Run focused tests and verify failure**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~LibraryOptionsPageTests
```

- [ ] **Step 3: Implement Library within the shared shell**

Render a category rail or compact selector, native search field, entry list, and details well using existing packet fields. Keep names and summaries prominent; source, tags, and fields appear only in detail. Replace the dialog's Inventory-only text-input condition with a selected-page input descriptor so Inventory and Library can each own one correctly focused native search field.

- [ ] **Step 4: Implement Options within the shared shell**

Render Notifications, Action Bar, Combat HUD, and Combat Visuals as a category selector plus focused settings panel. Use dedicated toggle/stepper rows, concise explanations, and current values. Do not render future whole-console controls until supported.

- [ ] **Step 5: Capture, inspect, run tests, and commit**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~LibraryOptionsPageTests|FullyQualifiedName~HubPageCompositionCaptureTests"
git add VRPG/src/Client/PlayerMenu/Hub/LibraryPage.cs VRPG/src/Client/PlayerMenu/Hub/OptionsPage.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.LibraryOptions.cs VRPG/src/Client/PlayerMenu/GuiDialogVrpgPlayerMenu.cs VRPG/src/Client/PlayerMenu/Shell/PlayerMenuShell.cs VRPG/assets/vrpg/lang/en.json VRPG/tests/VRPG.Tests/LibraryOptionsPageTests.cs VRPG/tests/VRPG.Tests/HubPageCompositionCaptureTests.cs
git commit -m "feat: add native Library and Options pages"
```

---

### Task 7: Retire ordinary legacy routes and verify the complete replacement

**Files:**
- Modify: `VRPG/src/VRPGModSystem.cs`
- Modify: `VRPG/src/Client/GuiDialogVRPGHub.cs`
- Modify: `VRPG/src/Client/GuiDialogVRPGLibrary.cs`
- Modify: `VRPG/src/Client/UI/GuiElementVrpgHub.cs`
- Modify: `VRPG/src/Client/UI/GuiElementVrpgLibrary.cs`
- Modify: `VRPG/docs/design/usable-player-menu-checklist.md`
- Modify: `VRPG/docs/design/testable-version-todo.md`
- Test: `VRPG/tests/VRPG.Tests/PlayerMenuReplacementCompositionTests.cs`
- Test: `VRPG/tests/VRPG.Tests/PlayerMenuReplacementLifecycleTests.cs`
- Test: `VRPG/tests/VRPG.Tests/PlayerMenuRouteRetirementTests.cs`

**Interfaces:**
- Produces: one ordinary open path, `OpenPlayerMenu(PlayerMenuEntryPoint entry, PlayerMenuPage? page = null)`.
- Preserves: the separate privileged `GuiDialogVRPGTalentEditor` path.

- [ ] **Step 1: Write failing route-retirement tests**

Use reflection/proxies to assert:

```csharp
[Fact]
public void HubAndLibraryPacketsNeverCreateLegacyPlayerDialogs()
{
    fixture.ReceiveHub(HubPacket());
    fixture.ReceiveLibrary(LibraryPacket());
    Assert.Null(fixture.Field("clientHubDialog"));
    Assert.Null(fixture.Field("clientLibraryDialog"));
    Assert.True(fixture.PlayerMenu.IsOpened());
}
```

Also assert the V hotkey enters Stats, `/vrpg library open` enters Library, E/C close the already-open shell, and the talent editor still opens only for authorized packets.

- [ ] **Step 2: Run route tests and verify failure**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~PlayerMenuRouteRetirementTests|FullyQualifiedName~PlayerMenuReplacementCompositionTests|FullyQualifiedName~PlayerMenuReplacementLifecycleTests"
```

- [ ] **Step 3: Remove ordinary legacy dialog creation**

Replace `OpenHubDialog` and `OpenLibraryDialog` with state receive + shell navigation. Remove `clientHubDialog` and `clientLibraryDialog` ownership and their disposal branches. Delete legacy dialog/element files only after `rg` proves no runtime or test references remain; otherwise retain them as non-instantiated compatibility code and record why. Do not touch the talent editor.

- [ ] **Step 4: Run the complete automated suite and production build**

```bash
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj
dotnet build VRPG/VRPG.csproj -c Release
```

Expected: all tests pass and the Release build has zero warnings introduced by this slice.

- [ ] **Step 5: Install and perform live single-player acceptance**

Install the newly built mod using the repository's established deployment command. Join a test world and capture evidence for E, C, V/Stats, Skills, Talents, Library, Options, Party, readable localized item names, centered controls, and at least one real action or honest rejection on each mutable page. Inspect `client-main.log` after each transition for texture-atlas, composition, packet, or input errors. Do not claim player acceptance; leave the client open if the user is actively testing.

- [ ] **Step 6: Update checklists with exact evidence and remaining blockers**

Mark only automated and directly observed live facts. Keep user-confirmation boxes open until the user joins and confirms no Vanilla Inventory, Character, Stats, or Survival UI appears and the replacement is usable.

- [ ] **Step 7: Commit route retirement and verified evidence**

```bash
git add VRPG/src/VRPGModSystem.cs VRPG/src/Client VRPG/tests/VRPG.Tests/PlayerMenuRouteRetirementTests.cs VRPG/tests/VRPG.Tests/PlayerMenuReplacementCompositionTests.cs VRPG/tests/VRPG.Tests/PlayerMenuReplacementLifecycleTests.cs VRPG/docs/design/usable-player-menu-checklist.md VRPG/docs/design/testable-version-todo.md
git commit -m "feat: complete unified VRPG player menu routing"
```

---

## Final self-review gate

- [ ] Map every section of the specification to at least one completed task above.
- [ ] Search this plan and implementation for unresolved placeholders, fake UI, raw item IDs in normal presentation, and ordinary `new GuiDialogVRPGHub` / `new GuiDialogVRPGLibrary` paths.
- [ ] Confirm all introduced method/property names match their consumers.
- [ ] Confirm every task was reviewed for spec compliance and code quality before the next task began.
- [ ] Confirm the final test/build output and live screenshots are fresh evidence from the final commit.
