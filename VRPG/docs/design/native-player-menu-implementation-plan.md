# Native Player Menu Implementation Plan

Status: Implementation plan; navigation contract started  
Parents: [UI visual language](ui-visual-language-and-hub.md) · [Inventory, loot, and storage](inventory-loot-and-storage.md)  
Index: [VRPG design index](README.md)

For the complete end-state menu beyond the first usable overwrite, see the
[unified player-menu design](../superpowers/specs/2026-09-06-unified-player-menu-design.md)
and [delivery plan](../superpowers/plans/2026-09-06-unified-player-menu.md).
These proposed documents preserve this file's activation gates.

## 1. Goal and safety boundary

Replace the separate Vanilla inventory, Vanilla character window, VRPG Hub and VRPG Library with one native player menu:

```text
Inventory menu action --> Inventory                 (E by default)
Character menu action --> Inventory / Equipment     (C by default)
legacy Hub action -----> Stats during migration

Inventory | Stats | Skills | Talents | Library | Options
```

The overwrite must remain disabled until the Inventory page can display and safely mutate real server-owned items. A visual placeholder is not sufficient: replacing the Inventory action's dialog owner too early would hide working Vanilla custody behind controls that cannot preserve, equip or move items.

The browser workbench remains the interaction and density reference. It is not a runtime data source.

## 2. Verified engine seam

The supported game source currently establishes:

- `HotkeyManager` defines the semantic actions `inventorydialog` and `characterdialog`; their default mappings happen to be `E` and `C`, but players may remap them.
- `GuiDialogInventory.ToggleKeyCombinationCode` returns `inventorydialog`.
- `GuiDialogCharacter.ToggleKeyCombinationCode` returns `characterdialog`.
- `GuiDialog.OnBlockTexturesLoaded` connects each registered dialog to the semantic action named by `ToggleKeyCombinationCode`.
- `IGuiAPI.LoadedGuis` exposes the registered dialog list, allowing VRPG to identify the existing menu owners by semantic action rather than physical key.

Preferred implementation: replace the two registered Vanilla dialog owners. Remove `GuiDialogInventory` and `GuiDialogCharacter` from the loaded-dialog chain, then register two tiny entry-dialog adapters for the same semantic actions. Both adapters route into one `GuiDialogVrpgPlayerMenu`; the Inventory action opens Inventory and the Character action opens Inventory with Equipment expanded. The player's mappings remain untouched.

Do not register new physical keys, rewrite current mappings, or Harmony-patch the global input dispatcher. When `InventoryReplacement.Enabled` is false, VRPG leaves both Vanilla dialogs registered and unchanged.

## 3. Ownership

```text
PlayerMenuController
  owns open/close, active page, focus restoration, session view state
  |
  +-- PlayerMenuDialogReplacementCoordinator
  |     swaps dialog owners for existing menu actions only
  |
  +-- GuiDialogVrpgPlayerMenu
  |     owns native dialog lifetime and composition host only
  |
  +-- PlayerMenuShell
        owns shared tabs, margins, close button and page viewport
        |
        +-- InventoryPage  --> IPlayerItemStoreClient
        +-- StatsPage      --> CharacterViewModel
        +-- SkillsPage     --> SkillViewModel
        +-- TalentsPage    --> TalentViewModel
        +-- LibraryPage    --> LibraryViewModel
        +-- OptionsPage    --> OptionsViewModel
```

The shell never owns item quantities, equipment, stat allocation, skill loadouts or talent allocation. Pages render immutable view models and emit named intents. Client services translate intents into revision-aware server requests.

## 4. Readable source layout

The existing `GuiElementVrpgHub.cs` is approximately 2,551 lines and `VRPGModSystem.cs` is approximately 1,243 lines. They are useful prototypes but should not become the unified menu architecture.

```text
src/Client/PlayerMenu/
|-- PlayerMenuPage.cs
|-- PlayerMenuEntryPoint.cs
|-- PlayerMenuNavigation.cs
|-- VanillaMenuActions.cs
|-- PlayerMenuDialogReplacementCoordinator.cs
|-- PlayerMenuEntryDialog.cs
|-- PlayerMenuController.cs
|-- GuiDialogVrpgPlayerMenu.cs
|-- Shell/
|   |-- PlayerMenuShell.cs
|   |-- PlayerMenuLayout.cs
|   `-- PlayerMenuTheme.cs
|-- Components/
|   |-- FittedButton.cs
|   |-- MechanicalDisclosure.cs
|   |-- SearchField.cs
|   |-- TypedTabStrip.cs
|   `-- ContextMessage.cs
|-- Inventory/
|   |-- InventoryPage.cs
|   |-- InventoryViewModel.cs
|   |-- InventoryItemSummary.cs
|   |-- InventoryQueryState.cs
|   `-- EquipmentDisclosure.cs
|-- Stats/
|-- Skills/
|-- Talents/
|-- Library/
`-- Options/
```

Rules for keeping it human-readable:

- Page names and state are enums or named records, never unexplained tab integers.
- One file has one responsibility; page renderers should normally remain below roughly 500 lines.
- Layout calculations are pure named functions and receive logical dimensions explicitly.
- Theme values use semantic tokens such as `PanelBackground`, `InteractiveEdge` and `ActiveHeat`, not repeated color literals.
- Input regions are declared beside the component that draws them and use named actions.
- Player text is localized at the page boundary. Domain and network code carry stable codes, not display strings.
- View models contain already-resolved player meaning. Renderers do not discover game rules from raw packets.
- Diagnostics use logging or an explicit authorized debug overlay, never ordinary page fields.
- Repeated page behavior is composed from components rather than inherited through a deep widget hierarchy.

## 5. Navigation state

The first implemented seam is engine-independent:

- `InventoryMenu` opens Inventory.
- `CharacterMenu` opens Inventory with Equipment expanded.
- `LegacyHubMenu` opens Stats while the old Hub action remains available during migration.
- Invoking any of those menu actions closes an already-open unified menu.

`PlayerMenuNavigation` contains this policy without game API dependencies and has unit coverage. The controller will later preserve selected tab, search, scroll and focused entry during a session, while each fresh Inventory-menu open begins on Inventory unless a deliberate direct shortcut was used.

## 6. Native component contract

### Fitted button

States: default, hover, keyboard focus, pressed, selected, disabled with reason, pending, success and rejected. Animation changes presentation only; input and authority never wait for it.

### Mechanical disclosure

Owns collapsed/open state, clipping, measured expanded height, focus movement and reduced motion. Opening heats the edge, releases the plate and reveals content into reserved or newly measured space. It must not render or accept input for clipped children while closed.

### Typed tab strip

Shows only configured categories that are meaningful or required fallbacks. It preserves the selected entry where sensible and requests a bounded first page when the category changes.

### Context message

Appears beside the initiating control and states what is unavailable and what the player can do. It is transient unless the blocked state persists.

## 7. Inventory-first vertical slice

Implement one truthful slice before migrating the other pages:

1. Define immutable inventory summaries, page/query packets, revisions and action results.
2. Implement a read-only client query against a server-owned test store.
3. Render Gear and Materials with paging/virtualization; prove 10,000 identical materials appear as one quantity.
4. Render a selected entry and a collapsed Equipment disclosure using real equipped records.
5. Implement one revision-aware action: equip a selected gear item.
6. Add pending, success, rejected, stale revision, unresolved item, empty, loading and overflow states.
7. Only then enable experimental Inventory/Character dialog replacement for a dedicated test world.
8. Keep Vanilla inventory ownership available behind the configuration switch until migration and recovery tests pass.

Do not globally raise Vanilla stack limits. Materialization into a Vanilla `ItemStack` happens through a bounded bridge only when an engine interaction requires it.

## 8. Dialog replacement installation

`PlayerMenuDialogReplacementCoordinator` should:

1. Subscribe during client startup.
2. Wait until default dialogs are registered, then find owners by `ToggleKeyCombinationCode`, never by `GlKeys.E` or `GlKeys.C`.
3. Close and remove the Vanilla Inventory and Character dialogs from the loaded-dialog chain.
4. Register one thin entry dialog for each existing semantic menu action. Each overrides `Toggle()` to call `PlayerMenuNavigation`; it owns no visual composition or inventory state.
5. Request current page data only when opening, while coalescing repeated requests.
6. Close the unified dialog without changing server state when toggled.
7. Log the replaced dialog types and semantic actions once at debug level.
8. Refuse activation if either expected dialog owner, the inventory feature, network protocol or required store capability is unavailable. Failure before custody migration leaves Vanilla ownership intact; already-migrated players follow the recovery rule below.

The replacement must be transactional: discover and validate both targets first, then swap both. Never remove one Vanilla dialog if the other target or either replacement adapter is unavailable. The coordinator retains the prior dialog references so disposal or failed startup can restore the original registration cleanly. Registration restoration does not reverse item custody: before migration commits, failure leaves Vanilla usable; after commit, canonical custody stays recoverable and locked against competing access until menu repair or journaled reverse migration. Client preparation, durable migration and activation acknowledgement must be coordinated across reconnects; they are not one in-process transaction.

The coordinator has no drawing code and never changes a control mapping. `VRPGModSystem` should create and dispose it, then delegate rather than accumulating more menu ownership logic.

## 9. Testing ladder

### Pure tests on every build

- Entry-point routing and close behavior.
- Page transition table.
- Logical layout at supported dimensions/scales.
- Disclosure measurement and reduced-motion state.
- Query paging, filtering and stable selection.
- Revision reconciliation and stale response rejection.

### Headless/native integration tests

- Default dialogs initialize, then VRPG replaces the registered owners of `inventorydialog` and `characterdialog` only when enabled.
- Default controls open Inventory and Equipment, and remapped controls do exactly the same without VRPG knowing their physical keys.
- Disabling replacement leaves Vanilla dialog registration and mappings unchanged.
- Direct programmatic attempts do not expose duplicate item owners when replacement is active.
- World disconnect/reconnect and mod disposal leave no stale dialog owner and can restore the prior registrations.

### In-game visual matrix

- 80%, 100%, 125% and 150% GUI scale.
- 16:9, 16:10, ultrawide and minimum supported logical size.
- Empty, ordinary and stress-volume categories.
- Keyboard-only, mouse and controller paths.
- Default, selected, disabled, pending, rejected and success states.
- Bright terrain, dark interiors and combat effects behind the modal.

Capture the same states in the workbench and in-game. Differences become explicit issues rather than silently accumulating in the native renderer.

## 10. Migration sequence

```text
Phase 0  navigation + transactional replacement tests     STARTED
Phase 1  shared native shell + component gallery           next
Phase 2  read-only real Inventory/Equipment page
Phase 3  equip action + server revision protocol
Phase 4  opt-in Inventory/Character dialog replacement in test worlds
Phase 5  migrate Stats, then Skills, Talents, Library
Phase 6  migrate Options and retire separate dialogs
Phase 7  default inventory replacement on + migration gate
Phase 8  remove the legacy Hub action after an announced period
```

The old Hub remains operational until its corresponding pages pass the native state and scale matrix. Migration happens page by page, but item custody always has exactly one owner.

## 11. First overwrite release gate

The experimental overwrite is ready only when:

- Inventory and Character menu actions route deterministically with default and remapped controls.
- Every displayed inventory value comes from real server state.
- Equip is revision-safe and explains rejection beside the action.
- Closing or changing tabs cannot lose, duplicate or strand an item.
- No backpack or Vanilla character dialog can expose a competing owner.
- The feature disables cleanly at restart after a validated lossless reverse migration when canonical items exist; otherwise it refuses the ownership change before mutation.
- The native Inventory screen passes the full scale, input and volume matrix.
