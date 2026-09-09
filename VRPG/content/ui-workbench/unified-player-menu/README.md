# Player-menu gallery evidence

This directory owns the browser preview source independently of the ignored temporary workbench. It contains isolated examples, not a live inventory client. `fixture.js` keeps quantities as decimal strings and the renderer formats them with `BigInt`. Category codes match `PlayerItemCategories`; capacity seeds are 120 unique entries and 512 ordinary aggregates.

The working preview is `tmp/Tools/vs-ui-workbench/index.html?screen=inventory&style=forged`. Its `scripts/player-menu-fixture.js`, `scripts/render-inventory.js`, `styles/player-menu-inventory.css` and `scripts/player-menu-layout-checks.js` match this directory's `fixture.js`, `render-inventory.js`, `inventory.css` and `layout-checks.js`. The index loads the fixture and checks before the renderer. When restoring the workbench, copy these four sources to those paths and retain that script order. The renderer is deliberately part of the existing workbench, not a standalone website.

Native preview build (from repository root):

```sh
dotnet build VRPG/VRPG.csproj -p:DisableSharedDeploy=true -p:DeployToVS=false -p:PackageOnRelease=false --no-restore
```

Launch Vintage Story 1.22.7 with `VRPG_PLAYER_MENU_GALLERY=1` in its environment and an isolated test data path. Put a `VRPG` directory or symlink inside that profile's `Mods` directory, pointing to the absolute `VRPG/bin/Debug` output containing `modinfo.json`, DLL and assets. The engine enumerates child mods of a search root: pointing `--addModPath` directly at `bin/Debug` does not load this loose mod. Load a world, then use `.vrpgmenugallery` in client chat. Without the environment opt-in, the gallery system is not loaded and its command is not registered. The command does not replace either Vanilla dialog and does not attach a network channel or own an item container.

Native gallery controls: select a top page or typed category, search, select an entry, expand Equipment in Gear, and scroll independently in the browser and inspector. Short windows replace the typed strip with a cycling category selector so a complete item row stays reachable. Volume cycles normal/maximum/zero/one examples, capped to the category's capacity. Next state cycles the component state matrix; Long labels stresses wrapping; Motion switches normal/reduced press and disclosure feedback. The narrow layout alternates browse and inspect, with Back returning to browse. Character entry opens the Equipment pane. Tab/Enter reaches shell controls and the native search field; Up/Down, Page Up/Down and Home/End navigate all browser rows while keeping focus visible. Page Up/Down scrolls the inspector when it holds focus. Both resolved Inventory and Character bindings close the open shell when no text field is typing.

Native inspection status on 2026-09-06: **unperformed**. The coordinator launched the real engine with an isolated profile but session validation returned `nosession / Bad`; a loaded world could not be reached without user sign-in. Browser captures are prototype evidence only and must not be labeled native screenshots. No default deployment, packaging or production readiness is claimed.

The six `browser-*.jpg` captures cover desktop 100%, narrow materials 100/150%, narrow no-match search 100%, and compact materials 80/125%. Narrow 100% inspection/Back, exact quantity containment, and unchanged capacity during search were checked. Review fix round 1 replaced the compact 125% and narrow 150% captures after correcting their category clipping/horizontal scrolling and icon/label overlap. The coordinator measured compact browser client/scroll widths 1087/1087 and all five category buttons 204/204; narrow browser widths 742/742, icon-to-label gaps 12px, and quantities inside their rows. These are targeted prototype checks, not a full native scale-matrix pass.

For a repeatable DOM geometry check in a normal page context, run `window.VRPG_MENU_LAYOUT_AUDIT()` after fonts/styles load. It returns failures and measurements without changing the UI. Isolated browser evaluation may not expose page globals; in that case measure the same selectors directly or evaluate the script in that context. The coordinator used direct DOM measurements for the recorded checks.

## Read-only inventory inspection (Task 5)

Use the same isolated `Mods/VRPG` route with `VRPG_PLAYER_MENU_TEST_STORE=1`, then `.vrpgmenuinspect` after joining a test world. This opens the existing native menu against a new in-memory store through the real bounded query and detail services. It does not import Vanilla items, attach a live network channel, write world saves, or submit item actions. Its connection service permanently denies mutation submission. The component gallery remains separately opt-in. Neither route activates Vanilla-menu replacement; production custody and joined-client acceptance remain gated.

The native inspection screen provides the five stable core categories, All types, occupied addon types with bounded catalog paging, stored/equipped/overflow/unavailable locations, filters, sorting, server page navigation, refresh, explicit entry/quantity selection and a Clear selection control. Gear has a grid/list switch in the inspector. Recovery is inspection only. The shortest layout retains one full row; Browse options opens inspection even with no selected item, and scrolling reaches its filters and other controls. Search and quantity use native text fields; both support the native focus path. Select quantity records an exact local selection and does not move or consume anything. Loading and errors never replace unknown inventory with fabricated zeros.

### Screen contract

- Player goal: find an item and inspect its exact quantity, restrictions and available gear comparison.
- Primary control: select an item; secondary controls refine the bounded query or explicitly select quantities.
- Persistent information: five core types, storage location, exact server capacity, recovery count, retained selection count and selections outside the current page.
- Empty/loading/error: truthful empty results, pending query, stale cached rows, correlated rejection and retry/refresh. Received errors stop pending wording; genuinely lost replies time out.
- Disabled: unresolved/protected/reserved items remain inspectable; unavailable quantities cannot be selected.
- Authority: this route is read-only; no equip, discard, recover, transform or economy action is offered.
- Scale: default64/max128 summaries; independent64/max128 occupied-addon pages; 128-character search; 200ms debounce; visible rows plus two overscan rows; 120 unique and512 aggregate ordinary slots; exact signed64-bit quantities through `long.MaxValue`.
- Entry/input: native menu composition and existing Back/page navigation; current resolved Inventory/Character bindings close the menu when not typing. It is modal and not a combat HUD.

### Actual Cairo composition capture

From the repository root, run this explicit utility (it is **not** counted as a passing unit test when omitted):

```sh
VRPG_MENU_CAPTURE_PATH=/tmp/vrpg-menu-capture \
VRPG_MENU_LANG_PATH="$PWD/VRPG/assets/vrpg/lang/en.json" \
dotnet run --project VRPG/tools/VRPG.MenuCapture \
  -p:DisableSharedDeploy=true -p:DeployToVS=false -p:PackageOnRelease=false
```

Optional `VRPG_MENU_CAPTURE_SCENARIO=gear-grid` and `VRPG_MENU_CAPTURE_SIZE=1120x680` limit output. The utility invokes the actual shell's Cairo composition against real store/query/detail data. Cases include empty/normal, full ordinary120/512 capacity, gear grid and comparison, consumables, protected records, currency, long names,10,000 recovery records, loading and a correlated rejection fixture. Quantities include10,000,000 and `long.MaxValue`. Sizes include1120x680,800x520,620x520 and400x350 with scrolled narrow inspection.

The `cairo-composition-*.png` files preserve representative final surfaces. These are **native composition evidence**, not browser mockups and not in-game screenshots. The harness omits GL item models, actual native search/quantity widgets, resolved bindings, user input, GUI-scale integration and a joined client. Item clipping is covered by shared-renderer geometry/source tests; live rendering remains unperformed. No successful joined-client or Vanilla-dialog replacement acceptance is claimed.

### Review correctness fixes

Gear now contains a read-only Equipment disclosure, also opened by Character entry. Its snapshot comes from the same query service with independent default64/max128 equipment paging and the same store revision; it does not switch the stored-item browser to Equipped or grant any mutation authority. Equipment actions and role integration remain Task6 work. Category-catalog and equipment-only paging preserve selected items, explicit quantities, browser/inspector scroll and still-current details.

The shortest inspector puts server capacity and recovery totals at the top, reachable through Browse options even with no selected item. Conditional affix effects include their actual server-provided qualification beside the value. The transformation producer is still absent; displaying future transformation data remains an explicit integration dependency.

`cairo-composition-equipment-shortest.png`, `cairo-composition-effect-condition.png` and `cairo-composition-recovery-shortest.png` record focused correctness cases from review fix round1. Reproduce them with the existing command and respective scenario/size pairs `equipment`/`400x350`, `effect-condition`/`1120x680`, and `recovery`/`400x350`. The condition case includes an inspector-scrolled `-condition.png` output. Deterministic focused tests also invoke the actual production Cairo composition and assert its translated condition/summary output and reachable Equipment control; these tests do not represent game input or GL acceptance.
