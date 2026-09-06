# Player-menu gallery evidence

This directory owns the browser preview source independently of the ignored temporary workbench. It contains isolated examples, not a live inventory client. `fixture.js` keeps quantities as decimal strings and the renderer formats them with `BigInt`. Category codes match `PlayerItemCategories`; capacity seeds are 120 unique entries and 512 ordinary aggregates.

The working preview is `tmp/Tools/vs-ui-workbench/index.html?screen=inventory&style=forged`. Its `scripts/player-menu-fixture.js`, `scripts/render-inventory.js`, `styles/player-menu-inventory.css` and `scripts/player-menu-layout-checks.js` match this directory's `fixture.js`, `render-inventory.js`, `inventory.css` and `layout-checks.js`. The index loads the fixture and checks before the renderer. When restoring the workbench, copy these four sources to those paths and retain that script order. The renderer is deliberately part of the existing workbench, not a standalone website.

Native preview build (from repository root):

```sh
dotnet build VRPG/VRPG.csproj -p:DisableSharedDeploy=true -p:DeployToVS=false -p:PackageOnRelease=false --no-restore
```

Launch Vintage Story 1.22.7 with `VRPG_PLAYER_MENU_GALLERY=1` in its environment, an isolated test data path, and `--addModPath` pointing to the absolute `VRPG/bin/Debug` directory. Load a world, then use `.vrpgmenugallery` in client chat. Without the environment opt-in, the gallery system is not loaded and its command is not registered. The command does not replace either Vanilla dialog and does not attach a network channel or own an item container.

Native gallery controls: select a top page or typed category, search, select an entry, expand Equipment in Gear, and scroll independently in the browser and inspector. Short windows replace the typed strip with a cycling category selector so a complete item row stays reachable. Volume cycles normal/maximum/zero/one examples, capped to the category's capacity. Next state cycles the component state matrix; Long labels stresses wrapping; Motion switches normal/reduced press and disclosure feedback. The narrow layout alternates browse and inspect, with Back returning to browse. Character entry opens the Equipment pane. Tab/Enter reaches shell controls and the native search field; Up/Down, Page Up/Down and Home/End navigate all browser rows while keeping focus visible. Page Up/Down scrolls the inspector when it holds focus. Both resolved Inventory and Character bindings close the open shell when no text field is typing.

Native inspection status on 2026-09-06: **unperformed**. The coordinator launched the real engine with an isolated profile but session validation returned `nosession / Bad`; a loaded world could not be reached without user sign-in. Browser captures are prototype evidence only and must not be labeled native screenshots. No default deployment, packaging or production readiness is claimed.

The six `browser-*.jpg` captures cover desktop 100%, narrow materials 100/150%, narrow no-match search 100%, and compact materials 80/125%. Narrow 100% inspection/Back, exact quantity containment, and unchanged capacity during search were checked. Review fix round 1 replaced the compact 125% and narrow 150% captures after correcting their category clipping/horizontal scrolling and icon/label overlap. The coordinator measured compact browser client/scroll widths 1087/1087 and all five category buttons 204/204; narrow browser widths 742/742, icon-to-label gaps 12px, and quantities inside their rows. These are targeted prototype checks, not a full native scale-matrix pass.

For a repeatable DOM geometry check in a normal page context, run `window.VRPG_MENU_LAYOUT_AUDIT()` after fonts/styles load. It returns failures and measurements without changing the UI. Isolated browser evaluation may not expose page globals; in that case measure the same selectors directly or evaluate the script in that context. The coordinator used direct DOM measurements for the recorded checks.
