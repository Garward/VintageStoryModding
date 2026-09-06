# Player-menu gallery evidence

This directory owns the browser preview source independently of the ignored temporary workbench. It contains isolated examples, not a live inventory client. `fixture.js` keeps quantities as decimal strings and the renderer formats them with `BigInt`. Category codes match `PlayerItemCategories`; capacity seeds are 120 unique entries and 512 ordinary aggregates.

The working preview is `tmp/Tools/vs-ui-workbench/index.html?screen=inventory&style=forged`. Its `scripts/player-menu-fixture.js`, `scripts/render-inventory.js` and `styles/player-menu-inventory.css` match this directory's `fixture.js`, `render-inventory.js` and `inventory.css`. The index loads the fixture before the renderer. When restoring the workbench, copy these three sources to those paths and retain that script order. The renderer is deliberately part of the existing workbench, not a standalone website.

Native preview build (from repository root):

```sh
dotnet build VRPG/VRPG.csproj -p:DisableSharedDeploy=true -p:DeployToVS=false -p:PackageOnRelease=false --no-restore
```

Launch Vintage Story 1.22.7 with `VRPG_PLAYER_MENU_GALLERY=1` in its environment, an isolated test data path, and `--addModPath` pointing to the absolute `VRPG/bin/Debug` directory. Load a world, then use `.vrpgmenugallery` in client chat. Without the environment opt-in, the gallery system is not loaded and its command is not registered. The command does not replace either Vanilla dialog and does not attach a network channel or own an item container.

Native gallery controls: select a top page or typed category, search, select an entry, expand Equipment in Gear, and scroll independently in the browser and inspector. Volume cycles normal/maximum/zero/one examples, capped to the category's capacity. Next state cycles the component state matrix; Long labels stresses wrapping; Motion switches normal/reduced press and disclosure feedback. The narrow layout alternates browse and inspect, with Back returning to browse. Tab/Enter reaches shell controls and the native search field. Page Up/Down scrolls the inspector. Both resolved Inventory and Character bindings close the open shell when no text field is typing.

Native inspection status on 2026-09-06: **unperformed**. The coordinator launched the real engine with an isolated profile but session validation returned `nosession / Bad`; a loaded world could not be reached without user sign-in. Browser captures are prototype evidence only and must not be labeled native screenshots. No default deployment, packaging or production readiness is claimed.

The six `browser-*.jpg` captures cover desktop 100%, narrow materials 100/150%, narrow no-match search 100%, and compact materials 80/125%. Narrow 100% inspection/Back, exact quantity containment, and unchanged capacity during search were checked. Remaining observed prototype defects after three repair rounds: compact 125% clips the Consumables/Miscellaneous category labels and shows horizontal scrolling; narrow 150% lets icon boxes touch/overlap row labels. These are open visual issues, not passing scale checks.
