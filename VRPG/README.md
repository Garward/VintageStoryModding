# VRPG

The playable data-driven skill slice is documented in [docs/skill-authoring-guide.md](docs/skill-authoring-guide.md). It includes the JSON contract, projectile modeling conventions, validation rules, admin acquisition commands, and in-game test workflow.

All menus, HUD elements, tooltips, and combat feedback must follow the design and verification gates in [docs/ui-authoring-guide.md](docs/ui-authoring-guide.md).

The current path to an internally testable build and the later one-hour gameplay vertical slice are tracked in [docs/design/testable-version-todo.md](docs/design/testable-version-todo.md).

Creature health and outgoing skill-damage curves can be swept across every level
and provisional rarity with the [damage scaling tool](docs/damage-scaling-tool.md).

VRPG is a data-first Vintage Story RPG mod scaffold with separately configurable RPG and dungeon modules.

The default active talent tree is the statless 426-node six-sector authoring scaffold. The old nine-node tree remains only as a fallback asset set and is automatically replaced when detected as the persisted `vrpg:default` fixture.

The server-global active tree is persisted in `ModConfig/vrpg-active-talent-tree.json`. Player allocations are stored separately in `ModConfig/vrpg-playerdata.json`. Pan, zoom, hover, and selected-node state are client-local UI state and are never serialized into either file.

The design intentionally uses Mine-and-Slash only as conceptual inspiration because no license was present in the local reference checkout. The implementation here is original C# code and VS JSON assets.

## Modules

- `rpg`: loads stats, stat families, gear rarities, affixes, talent nodes, and library entries from JSON assets. Includes persisted player RPG state plus commands for sheets and talent allocation.
- `dungeons`: optionally integrates with Manifold. If Manifold is absent or unhealthy, the module logs one clean disabled message and the rest of VRPG still loads.

## Data Paths

Content is loaded from asset folders so packs and addons can extend it:

- `assets/*/vrpg/stats/`
- `assets/*/vrpg/statfamilies/`
- `assets/*/vrpg/rarities/`
- `assets/*/vrpg/affixes/`
- `assets/*/vrpg/talents/`
- `assets/*/vrpg/talenttemplates/`
- `assets/*/vrpg/classes/`
- `assets/*/vrpg/skills/`
- `assets/*/vrpg/library/`
- `assets/*/vrpg/dungeons/`

## Commands

- `V` opens the VRPG hub by default. Rebind `Open VRPG Hub` in the normal Vintage Story controls menu.
- `/vrpg status`
- `/vrpg hub`
- `/vrpg reload`
- `/vrpg stats`
- `/vrpg statfamilies`
- `/vrpg talents`
- Hub → Talents shows `ADMIN · EDIT TREE` only when the server reports that the player has `controlserver`; the server checks the privilege again when opening the editor. `/vrpg talenteditor` opens the same controlserver-only draft editor directly. Saved authored trees are distinct from built-in reset templates: admins can open a saved tree, reset its private draft from a template, Save and activate it, Save As New, or delete a non-active saved tree. Destructive draft replacement and deletion require confirmation. Tree and node names can be set or replaced; a blank node name clears it. An amount of `0` removes that stat/operation modifier.
- `/vrpg library list`
- `/vrpg library search <query>`
- `/vrpg library categories`
- `/vrpg sheet`
- `/vrpg talent take <code>` (direct test command; the Hub normally plans changes)
- `/vrpg talent reset` (admin playground reset)
- `/vrpg grantpoints [talents] [stats] [respec]`
- `/vrpg skill list`
- `/vrpg skill grant <code> [level]` (admin)
- `/vrpg skill grantall [level]` (admin)
- `/vrpg skill grantto <player> <code> [level]` (admin)
- `/vrpg skill grantallto <player> [level]` (admin)
- `/vrpg skill equip <slot 1-8> <code|clear>`
- `/vrpg skill loadout`
- `/vrpg skill cast <slot 1-8>`

`/vrpgrpg` remains an alias for compatibility, but `/vrpg` is the canonical command root and `/help vrpg` lists the complete command tree.
- `Alt+1` through `Alt+8` cast the eight persisted skill-loadout slots by default. The Hub controls whether four through eight are visible.
- The Skills page assigns learned skills to the visible persisted slots. Each slot displays its current remapped Vintage Story binding, and hiding a slot does not clear it.
- Evasive Step is an automatic DEX-primary lethal-hit passive; it has no hotkey and never consumes a loadout slot.
- Hub → Options → Combat Hotbar unlocks the HUD bar for dragging and locks it against accidental movement. Layout is stored in the local client config.
- Hub → Talents uses plan-and-commit editing. Before a route is chosen, all six free starting nodes are highlighted. Left-click queues an affordable connected node, right-click queues an allocated-node refund when the remaining path stays connected and a respec point is available, and Apply commits the complete plan atomically. Blue nodes are queued allocations and red nodes are queued refunds.
- Starting routes have large double-ring sockets and route names. Ordinary Tier 1/2 nodes have no authored names by default; their stats are their identity and the inspector uses the generic `Stat Node` label. Selecting a locked node updates only the inspector and never changes gameplay highlighting.
- `/vrpgdungeon status`

## Config

Vintage Story creates `ModConfig/vrpg.json` on first load. `Modules.Rpg.Enabled` and `Modules.Dungeons.Enabled` can be changed independently.

## Iterative build

```bash
dotnet build VRPG/VRPG.csproj -c Release
```

The project-local build configuration copies the compiled DLL, PDB, mod metadata, and assets into `~/.config/VintagestoryData/Mods/VRPG`, matching VintageKinematics's quick-test workflow.
