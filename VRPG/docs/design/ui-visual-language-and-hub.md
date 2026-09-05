# UI Visual Language and Hub Contract

Status: Adopted direction; exact textures and theme name remain provisional  
Parent: [Complete game design](complete-game-design.md)  
Prototype: [`vs-ui-workbench`](../../../tmp/Tools/vs-ui-workbench/)  
Related: [Combat HUD total replacement](combat-hud-total-replacement.md)  
Index: [VRPG design index](README.md)

## 1. Direction

VRPG abandons Survival's paper-ledger presentation for a combat-focused interface made from dark oxidized iron, fitted plates, recessed mechanisms and restrained heat. The interface should feel assembled from the same old machinery, rusty gears and recovered technology that exist in the world, without becoming a decorative steampunk dashboard.

The current VRPG Hub establishes a strong structural baseline, but the total-overhaul target folds it into one player menu rather than retaining a separate dashboard:

- One persistent top-level shell.
- Stable Stats, Skills, Talents, Library and Options navigation.
- A dominant task area with contextual detail beside it.
- Compact warm typography against a dark field.
- A talent tree that already suggests a powered mechanism.

The material treatment remains provisional. Layout and interaction contracts should survive texture iteration.

## 2. Visual thesis

```text
VOID / WORLD DIMMING
        |
        v
+------------------------------+
| OXIDIZED IRON CHASSIS        |  silhouette, window ownership, heavy outer edge
|  +------------------------+  |
|  | FITTED METAL PLATE     |  navigation, headings, stable groups
|  |  +------------------+  |  |
|  |  | RECESSED WELL    |  |  |  lists, graphs, meters, scroll regions
|  |  |                  |  |  |
|  |  +------------------+  |  |
|  +------------------------+  |
|       copper control          |  available interaction
|       heated etched line      |  active/selected/pending energy
+------------------------------+
```

Material communicates hierarchy:

- **Iron** means stable structure.
- **Dull copper or brass** means a control can be used.
- **Heat orange** means the player is interacting, something is selected, or energy is actively travelling.
- **Rust and verdigris** mean age and accumulated wear, not state.
- **Class material accents** mean identity, never global navigation or universal success/error state.

The design fails if the entire screen glows orange. Heat must be scarce enough to direct the eye.

## 3. Material layers

### 3.1 Chassis

- Near-black oxidized iron rather than dark red-brown.
- Broad, readable silhouette with slightly heavier outer edges than internal dividers.
- Extremely subtle mottling or hammered texture, capped to a narrow luminance range.
- Rust gathers at corners, seams, fasteners and lower recess edges; it does not wash uniformly across the panel.
- Major windows may use two to four visible fasteners or fitted corner plates. Every row and button should not receive bolts.

### 3.2 Fitted plates

- Page titles, navigation groups and inspector headers sit on slightly raised plates.
- Plate seams and thickness establish grouping before color does.
- Adjacent plates may vary slightly in iron tone without becoming a checkerboard.
- Stamped labels, serial marks and assembly lines are optional low-priority texture. They never compete with player text.

### 3.3 Recessed wells

- Talent canvases, lists, meters and inventory grids sit below the chassis surface.
- Recesses are darker and cooler than the outer frame.
- Their inner edge catches a small amount of dull metal or rust, not a continuous luminous outline.
- Scroll clipping remains visually obvious; content should not appear to float over the frame.

### 3.4 Controls

- Idle controls resemble fitted copper-faced plates or iron plates with copper edges.
- Hover warms a short etched edge or glyph.
- Pressed state shifts the plate inward by approximately one screen pixel and darkens its upper edge.
- Selected state keeps a stable heated groove, notch or illuminated stamp after hover ends.
- Disabled controls remain legible but physically unpowered. The nearby interface explains why they cannot be used.

## 4. Provisional theme tokens

Exact values must be tested in-game because browser CSS and Vintage Story rendering differ. These values establish relative roles:

| Token | Provisional value | Use |
|---|---:|---|
| `void` | `#070808` | Outside and deepest recesses |
| `iron-950` | `#0d1010` | Chassis shadow |
| `iron-900` | `#141817` | Main chassis |
| `iron-800` | `#1c211f` | Raised plate |
| `iron-700` | `#292d29` | Worn edge/high plane |
| `rust-deep` | `#3a190f` | Seam accumulation |
| `rust` | `#74351c` | Local corrosion |
| `copper-dull` | `#9b6a42` | Inactive interactive edge |
| `copper-worn` | `#bd8450` | Hover-ready control |
| `heat` | `#ff9918` | Active selection and live connection |
| `heat-hot` | `#ffc44f` | Focus, commit and brief peak response |
| `text` | `#eadfce` | Primary text |
| `text-muted` | `#aa9d8b` | Supporting text |
| `success` | `#8fcf76` | Success paired with shape/text |
| `danger` | `#df6557` | Error/danger paired with shape/text |
| `verdigris` | `#568477` | Patina and selected identity accents |

Orange does not replace health red, mana blue, status identities, rarity colors or class identity. Those systems retain their own constrained semantic palettes.

## 5. Texture and ornament budget

- Base iron texture varies by no more than roughly 2–4% luminance under text.
- Rust occupies edges and interruptions rather than the center of reading surfaces.
- A major panel receives at most one dominant material motif: bolts, seam, stamped mark or fitted reinforcement.
- Repeated list rows use geometry and spacing first; unique ornament is reserved for selected or exceptional rows.
- Gear silhouettes may appear in corner embossing, loading/progress motion or empty space. Literal gears should not surround every button.
- Noise, scratches and corrosion are baked or cached static layers. They are not procedurally regenerated every frame.
- Text always sits on a quiet backing plane.

## 6. Interaction state matrix

Essential state uses more than color.

| State | Material response | Additional signal |
|---|---|---|
| Default | Dull copper edge on fitted plate | Clear label/icon |
| Hover | Short edge or glyph warms | Local lift/edge change |
| Keyboard focus | Heated corner brackets or complete focus groove | Persistent focus shape |
| Pressed | Plate moves inward; upper highlight collapses | Immediate tactile motion |
| Selected | Stable illuminated etching or locked notch | Marker and retained contrast |
| Disabled | Unpowered dark plate | Reason available beside control |
| Locked | Closed mechanical notch or stamped lock | Requirement in context |
| Pending | Plate remains engaged; energy travels once | Repeat input prevented |
| Success | Mechanism settles into new state | Concise local confirmation |
| Rejected | Plate releases; danger notch appears | Actionable reason beside it |

Hover, selection and focus must not share the exact same treatment. A mouse user should know what will be activated, and a keyboard user should never lose focus against selected state.

## 7. Motion language

Motion explains state change rather than decorating idle screens:

- Plate press and release: approximately 70–120 ms.
- Locking tab or latch: approximately 100–180 ms.
- Etched line conducting heat: approximately 140–260 ms along the affected path.
- Talent allocation: energy travels from an allocated neighbor, reaches the node, then the node locks into its persistent state.
- Pending network action: one restrained repeating mechanical state, not a full-panel pulse.
- Rejection: energy retreats or the plate releases; never flash the whole screen.
- Defeat, loot and rarity presentation may be more expressive, but menu motion stays local.

Reduced-motion mode replaces travel and plate motion with immediate state changes and a brief static emphasis. No required information exists only during an animation.

## 8. Class and build identity

Class identity changes fitted details, not the entire interface.

A class theme may supply:

- One material accent, such as blackened steel, brass, verdigris copper, blued steel or pale alloy.
- One stamped sigil or cutout geometry.
- One connection-energy treatment within strict brightness limits.
- Optional wear pattern localized to class cards, equipped-skill bezels and the matching talent sector.

Stable navigation, errors, success, health, mana and universal availability retain global semantics. Class color appears on no more than a small minority of the visible surface so switching class never makes the application feel like a different game.

## 9. Shared player-menu contract

```text
+--------------------------------------------------------------------------------+
| mark | Inventory | Stats | Skills | Talents | Library | Options | Close              |
|------+-----------+-------+--------+---------+---------+---------+--------------------|
| compact page context                                                        |
|                                                                                |
|                        PRIMARY WORKSPACE                                       |
|                                                     +------------------------+ |
|                                                     | selection / action     | |
|                                                     | details                | |
|                                                     +------------------------+ |
| local status, pending result, or one-line contextual help                      |
+--------------------------------------------------------------------------------+
```

- The shell geometry and close behavior remain stable across every hub page.
- `E` opens this shell on Inventory by default. Inventory, Stats, Skills, Talents, Library and Options are sibling tabs; “VRPG Hub” is no longer a second destination the player must remember.
- The former Survival `C` character/equipment window is absorbed into Inventory. `C` may remain as a configurable direct shortcut to Inventory's Gear view with Equipment expanded, but it never opens a separately owned item container.
- The active page is indicated by a locked plate/etched line plus text contrast, not orange text alone.
- The page title is not repeated when the selected navigation label and local heading already make context obvious.
- Footer text is reserved for current controls, state or actionable feedback. Architecture slogans and registry diagnostics do not belong there.
- Administrative authoring controls appear only for authorized administrators and should use a visibly separate tool mode.

### 9.1 Calm-default information budget

Each page presents one current decision and one contextual explanation. This is a hierarchy rule, not merely a low-density visual theme.

- At most two major panes are visible during ordinary use: the task surface and its inspector.
- Health, mana and combat state remain on the combat HUD and are not repeated in Stats unless a selected choice directly changes them.
- Derived values, worn equipment, affixes, technical registry details, topology tools and extended filters begin behind fitted disclosure plates.
- A disclosure plate opens like a mechanism: its edge heats, the plate releases, and the contents slide into the newly claimed space. Closing reverses the motion. Reduced-motion mode changes state immediately.
- Pages use broad internal margins and quiet iron between controls. Empty space is an active part of the hierarchy, not a region to fill with counters.
- Large fitted buttons represent meaningful actions. Small terminal-like key/value rows are reserved for true comparisons, not used as the default visual vocabulary.
- Persistent helper copy is removed when a label, disabled reason or contextual response can communicate the same fact locally.
- Admin counts, generation coordinates, source IDs and layout diagnostics never appear in player mode.

## 10. Screen contracts

### 10.1 Stats

- **Player goal:** Understand the current character and spend available stat points.
- **Primary decision:** Which primary stat receives the next point.
- **Required information:** Available points, current values, meaningful next-point effects and primary-affinity consequences.
- **Primary control:** Add/remove a planned point, followed by an explicit commit if changes are transactional.
- **Excluded:** Every derived formula, inactive resource, raw stat ID and unrelated talent count.
- **Locked state:** Each disabled allocation control exposes its local reason, such as no points or combat lock.
- **Authority:** Planning is local; committed allocation is server-authoritative and pending-safe.

The focused structure is allocation plus one inspector. A thin character strip may show identity, level and XP, but it does not repeat live health, mana, talent points, automatic movement state or Library counts. Derived stats begin collapsed. Focusing an attribute puts exact next-point effects and affinity consequences in the inspector.

### 10.2 Skills

- **Player goal:** Understand available skills and assemble the current combat loadout.
- **Primary decision:** Learn, improve, replace or equip a skill.
- **Required information:** Skill identity, learned/locked state, exact cost/cooldown/delivery, meaningful effects, requirements and current loadout.
- **Excluded:** Internal effect-operation IDs, raw tags not used for filtering and complete formula traces.
- **Authority:** Learn/equip commits are server-authoritative; selection and filtering are local.

Class material appears primarily on class selectors, skill bezels and the inspector header. Universal learned/locked/equipped states do not change meaning between classes.

Class selection is one mechanical dropdown or compact selector, not a permanent first column. The ordinary layout is a skill browser plus one inspector containing the selected skill and its loadout action.

### 10.3 Talents

- **Player goal:** Read reachable paths, plan allocations and commit a build change.
- **Primary decision:** Which connected path or major transformation to take.
- **Required information:** Current allocation, available nodes, planned changes, costs, connection state, selected effect and post-commit resources.
- **Primary control:** Apply the visible plan.
- **Excluded:** Generation statistics, topology diagnostics and authoring controls during ordinary play.
- **Authority:** Planning/pan/zoom are local; commit is server-authoritative and revision-safe.

Connections are the best place for the mechanical theme: dormant grooves in iron conduct heat from allocated nodes through planned paths. Available, planned, allocated, refund-planned and disconnected states also require distinct node rims/glyphs. At overview scale the tree should fit the useful viewport rather than become a tiny ornament surrounded by empty space.

Player mode shows points, zoom/fit controls, the tree and the selected talent's effect/cost. Node counts, guide controls, generated coordinates, layout offsets, reset/export tools and topology fields live in an explicitly opened Authoring plate available only to authorized users.

### 10.4 Library

- **Player goal:** Find and understand a mechanic, item family, status, skill or discovered content entry.
- **Primary decision:** Select a category/entry or follow a related concept.
- **Required information:** Player-facing name, concise purpose, mechanical effects, acquisition/use context and related entries.
- **Secondary controls:** Search, category filter and optional broad search scope.
- **Excluded by default:** Namespaced registry IDs, `source: manual`, generated-entry counts and “data-first documentation generated from registries.”
- **Details path:** An explicit technical-details mode may expose source addon and registry code when troubleshooting packs.
- **Authority:** Read-only cached views; discovery restrictions are server-derived.

The ordinary layout is a browse pane plus reading pane. Category selection is a mechanical dropdown above search rather than a permanent third column. “Library” remains the page identity; a second decorative “Details” tab is unnecessary unless it changes the actual view. Empty or zero-count future categories remain hidden.

### 10.5 Inventory and equipment

- **Player goal:** Find an item and decide whether to equip, use, keep, mark, salvage or deposit it.
- **Primary decision:** The action on the selected entry.
- **Required information:** Active type tab, relevant capacity policy, selected entry, quantity and build-relevant effects/comparison.
- **Excluded by default:** Every worn slot, empty slot grids, backpack slots, aggregate implementation details and unrelated currencies.
- **Details path:** Equipment and affixes open as fitted disclosure plates. Selecting equippable gear may automatically open only its relevant comparison.
- **Authority:** Browse/search state is local; custody, quantities, equipment and transformations are server-authoritative and revision-safe.

Inventory opens first on `E`. Default typed tabs are Gear, Materials, Consumables, Keys & Quest and Miscellaneous. Unique gear uses fixed entry capacity; commodities render one indexed entry with a high quantity; protected records and currencies do not pretend to be physical stacks. The full intended capacity exists from character creation and no backpack slot appears when inventory replacement owns the player store.

### 10.6 Options

- **Player goal:** Change understandable client preferences and permitted server/gameplay options.
- **Primary decision:** Change one setting with a clear effect and ownership label when needed.
- **Required information:** Current value, practical consequence, restart/reload requirement and whether the setting is local or server-controlled.
- **Excluded:** Capability IDs, config keys and implementation names.
- **Authority:** Client preferences apply locally; server settings use an authorized pending request.

## 11. Native rendering strategy

The browser workbench expresses layout and art direction; the in-game implementation should use bounded native assets:

- Nine-slice or equivalent cached textures for chassis, plate, recess and control states.
- Separate static decoration from frequently changing values and highlights.
- Reuse fastener, seam, notch and corner assets rather than drawing many unique ornaments.
- Clip lists and talent geometry before rendering.
- Rebuild text and decorative surfaces only when displayed state changes.
- Keep active heat/glow layers small and local.
- Treat class materials as token/asset substitutions, not duplicated screen implementations.

## 12. Workbench plan

The workbench preserves the original treatment as `VRPG Custom (Baseline)` and exposes the first oxidized-iron pass as `VRPG Forged (Experiment)`. Both use the same layout and data so material changes can be judged side by side. The experimental theme uses named conceptual roles and scoped overrides rather than changing the baseline.

The workbench comparison must capture:

- Stats, Skills, Talents, Library and Options.
- Default, hover, keyboard focus, pressed, selected, disabled, locked, pending, success and rejection examples.
- Desktop, compact and narrow viewports.
- 80%, 100%, 125% and 150% UI scales.
- Empty, ordinary and stress-volume content.
- At least three class material accents on the same underlying screen.
- Normal and reduced-motion states.

Only after comparison across these states should the new treatment replace the baseline `style-vrpg` profile.

## 13. Acceptance criteria

- A player identifies the active page and primary action within a few seconds.
- Orange reliably means current interaction or powered state rather than general decoration.
- Panels read as chassis, fitted plate and recess without depending on high-frequency texture.
- Rust and hardware reinforce age without reducing text clarity or increasing visual noise.
- Every interaction state remains understandable without color alone.
- Class identity is visible without recoloring the whole application.
- Talent planning reads as energy travelling through a mechanism, while topology remains legible at useful zoom.
- The Library presents player knowledge first and technical registry metadata only through an explicit details path.
- Narrow layouts preserve navigation, selection details and primary actions without horizontal scrolling.
- Static ornament and mechanical transitions stay inside established rendering and reduced-motion budgets.

## 14. Open art decisions

- Final theme name and whether “rusty gears” remains an internal direction or becomes player-facing language.
- How corroded the default chassis should appear versus repaired/upgraded variants.
- Whether fasteners are literal bolts, rivets, clamps or abstract fitted corners.
- Whether housing/workshop progression can unlock cosmetic interface chassis without affecting readability.
- Exact class material set and its relationship to elements, factions and equipment rarity.
- How much screen translucency remains desirable over bright and effect-heavy combat scenes.
