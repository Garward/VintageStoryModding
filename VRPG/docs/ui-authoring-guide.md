# VRPG UI Authoring Guide

This guide defines the quality bar and authoring process for every VRPG menu, HUD element, tooltip, and combat-feedback surface. It exists because a functional first draft is not enough: RPG interfaces carry dense build information, frequent combat state, and long-term progression, so a weak first layout can make otherwise good systems feel confusing or unfinished.

The guide is both a design standard and a release gate. A screen is not complete when it renders. It is complete when a player can understand its purpose, operate every visible control, recover from every important state, and still read the game around it.

## Design Pillars

### 1. Combat clarity before ornament

VRPG is faster than normal Vintage Story combat. HUD elements and effects must communicate targets, threats, cooldowns, costs, hits, and defensive states without covering the crosshair or obscuring the world.

Decoration must support hierarchy and identity. It must never compete with gameplay information.

### 2. Information when it is needed

Show the minimum information required for the current decision, then provide detail through selection, tooltips, comparison, or a deliberate advanced-information input.

- A skill tile identifies the skill, rank, cost, cooldown, state, and hotbar assignment.
- Selecting it reveals its complete behavior and resolved values.
- Holding the advanced-information input reveals formulas, tags, and scaling details.
- Debug counts, network ownership, asset codes, and implementation status do not belong in the player interface.

### 3. One focused job per page

Every page must have one primary player task.

| Surface | Primary task |
|---|---|
| Stats | Understand and allocate core attributes |
| Skills | Learn, inspect, specialize, and equip skills |
| Talents | Plan and allocate a connected passive build |
| Library | Find and understand game concepts |
| Options | Change a known category of preferences |
| Hotbar | Read and activate the current combat loadout |
| Loot comparison | Decide whether to keep, equip, modify, trade, or salvage an item |

If a page cannot state its primary task in one sentence, its scope is not ready for layout work.

### 4. Build identity must be visible

Classes, skills, damage families, resources, and item rarities need recognizable silhouettes, icons, and restrained semantic accents. A build screen should be scannable before the player reads every label.

Color cannot carry meaning alone. Pair it with an icon, shape, text label, border treatment, or pattern.

### 5. The interface must tell the truth

Every visible control must operate on real state. Do not ship decorative hotbars, fake toggles, placeholder allocation buttons, hard-coded binding labels, or values that do not affect gameplay.

Server-authoritative state must still feel responsive. A control may show a pending state immediately, but it must reconcile cleanly with the server response and explain rejection without flickering.

### 6. Design for the system after the next one

Lists, navigation, filters, and options must support realistic future content volume. A layout that works for two skills or two settings but collapses at twenty is a prototype, not a reusable screen.

Do not expose empty future categories merely to prove that the layout could contain them. Test scalability in the workbench with generated data instead.

## The Mandatory Authoring Gate

No new screen should go directly from an idea into Cairo code. Use this sequence:

1. Write the screen contract.
2. Build a workbench version with realistic data.
3. Exercise its complete state matrix.
4. Capture and review the supported workbench sizes.
5. Implement it with shared native components and theme tokens.
6. Capture and compare the in-game result at multiple GUI scales.
7. Correct discrepancies and run interaction tests.

Skipping the workbench is acceptable only for a one-line diagnostic overlay or an emergency bug fix that does not establish a reusable pattern. The native result still requires an in-game review.

## Screen Contract

Create a short contract before arranging panels. Copy this template into the relevant design issue or specification:

```markdown
### Screen: <name>

- Player goal:
- Primary decision or action:
- Entry points:
- Required information:
- Primary control:
- Secondary controls:
- Persistent information:
- Explicitly excluded information:
- Empty state:
- Locked/disabled state:
- Loading/pending state:
- Error/rejection state:
- Success feedback:
- Keyboard/hotkey path:
- Server-authoritative mutations:
- Expected content volume now / later:
- Combat-visible or modal:
```

The contract is failed if “required information” contains data that does not help the stated goal, or if a primary action exists only in chat commands after the screen is implemented.

## Information Architecture

Use this hierarchy for full Hub pages:

1. **Global shell:** VRPG identity, top-level tabs, close behavior.
2. **Page context:** current page and, where necessary, a compact available-points or search summary.
3. **Primary workspace:** the large focused region used for the page's main task.
4. **Detail or inspector region:** selected-object explanation and contextual actions.
5. **Transient feedback:** tooltip, pending state, error, or confirmation near the initiating control.

### Duplication rules

- Show a persistent value once unless the second copy is needed beside an action that changes it.
- Do not repeat level, point totals, resources, or server-state labels across the header, page body, side panel, and footer.
- The selected top-level tab is sufficient as the page title unless a second heading adds a more specific scope.
- Remove empty panels before inventing filler content for them.
- Prefer one strong workspace over several equally weighted cards.

### Decision-changing information

Any property that could change an equip, crafting, allocation, modification, or salvage decision belongs in the primary comparison or action area. It must not be presented only as a quiet footer, category tag, lore sentence, tooltip, or strip of passive metadata.

- Put an effect beside the object, stat, recipe, or choice it changes.
- Give mechanical effects explicit values and units whenever the runtime has them; `+20% mining yield` is actionable while `Mining Yield Up` is vague.
- Group related effects under a meaningful heading such as `Work Output`, `Defenses`, or `Skill Changes` rather than leaving them as visually unrelated chips.
- Use an icon, value alignment, and positive/negative comparison treatment where those improve scanning, but never rely on color alone.
- Reserve quiet tags and footer metadata for filtering, provenance, classification, and supplementary context that should not determine the immediate choice.
- Do not assume readable text will be noticed. Visual placement and weight teach the player whether information is actionable or safe to ignore.

A mechanically important bonus styled like passive metadata is a hierarchy failure even when its font size and contrast are technically readable.

### Navigation rules

- The Hub's top-level tabs remain stable between pages.
- A page that will have several kinds of settings or content uses a category rail, search, filters, or an inspector—not an indefinitely growing flat list.
- Selection must be visible without relying on hover.
- Returning to a page should preserve useful local context such as selection, filter, scroll position, talent pan/zoom, and option category during the open session.

## VRPG Visual Language

`VrpgGuiTheme` is the native source of truth. The workbench VRPG profile should mirror it. New colors, spacing values, border styles, and typography levels should become named shared tokens before they are repeated across screens.

### Color roles

| Role | Current direction | Use |
|---|---|---|
| Panel | dark red-brown | Main backdrop and large surfaces |
| Panel alternate | warmer brown | Raised or selected sub-surfaces |
| Gold | orange-gold | Primary selection, important headings, actionable emphasis |
| Main text | warm near-white | Names, values, and essential instructions |
| Muted text | desaturated tan | Secondary explanations and metadata |
| Positive | green | Ready, enabled, gained, valid, or improved |
| Error/danger | red | Invalid, unaffordable, health danger, or destructive action |
| Mana/shield | distinct cool hues | Resource identity, always accompanied by a label or icon |

Gold is not ordinary body text. If everything is gold, selection and action lose their signal.

### Spacing

Use a small shared logical-pixel scale: `4, 8, 12, 16, 24, 32`. Use an intermediate value only when the native renderer requires optical correction.

- 4: icon/text correction, compact internal separation.
- 8: tightly related label/value pairs.
- 12–16: control padding and related groups.
- 24: section separation.
- 32: major workspace separation.

Align repeated edges. A two-pixel drift across a stack of rows is more distracting than a slightly less decorative border.

### Typography

Use no more than four clear levels on one page:

- Page or major selection: 18–20 logical px.
- Section heading: 14–16 logical px.
- Primary row/value: 12–14 logical px.
- Helper and metadata: 10–12 logical px.

Helper text is still player-facing text. Do not shrink it to solve a layout problem. Wrap, shorten, scroll, or restructure instead.

Use the ornate/fantasy face for identity and headings. Use the most readable available face for dense values and explanations. Numeric columns should align consistently.

### Surfaces and borders

- Use one dominant panel and only enough raised surfaces to group interaction.
- Avoid turning every datum into an identical bordered card.
- Strong borders indicate selection, focus, rarity, or a meaningful boundary.
- Shadows and glows must remain subtle enough that text and icons retain sharp silhouettes.
- Transparent HUD surfaces need enough backing to survive bright grass, snow, darkness, and particle effects.

### Icons

- Standardize common display sizes such as 24, 32, and 48 logical px.
- A skill or class icon must remain recognizable at its smallest live size.
- Reserve icon corners for compact state badges: rank, lock, assignment, charge, or warning.
- Never place paragraph text inside an icon tile.
- Missing icons use one obvious authored fallback and produce a validation warning; they must not silently disappear.

## Layout and Scaling

The current native Hub chooses a logical size between `920×560` and `1040×620`, based on available screen space. The workbench also exposes compact and narrow stress profiles. Treat these as constraints, not as instructions to shrink the desktop layout uniformly.

Every Hub page must be reviewed at:

- `1040×620` logical size.
- `920×560` logical size.
- 80%, 100%, 125%, and 150% Vintage Story GUI scale where the physical display permits it.
- At least 16:9 and 16:10 displays.
- Long localized-style labels and descriptions, even before localization ships.

Rules:

- The primary action and selected object must remain visible without horizontal scrolling.
- Scroll only the content region that can grow; keep navigation and critical actions anchored.
- Text wraps to explicit bounds. It may ellipsize only when selection or tooltip reveals the complete value.
- Controls do not overlap at minimum size.
- Hit targets remain comfortably clickable after scaling.
- A narrow fallback may stack secondary regions, but it must preserve task order and selection context.
- Never solve overflow by drawing outside a control's bounds.

## Interaction and State

Design every interactive element in these states where applicable:

| State | Required signal |
|---|---|
| Default | Clear affordance and label |
| Hover | Local visual response and tooltip when useful |
| Keyboard focus | Persistent outline or equivalent focus treatment |
| Pressed | Immediate tactile response |
| Selected | Visible without hover |
| Disabled | Reduced emphasis plus a discoverable reason |
| Locked | Requirement shown in context |
| Pending | Input protected from accidental repetition |
| Success | Changed state and concise local confirmation |
| Rejected/error | Reason beside the control; chat is a fallback, not the primary channel |

### Server-authoritative controls

- Send one deliberate mutation request per action.
- Show pending state or temporarily disable repeated activation when ordering matters.
- Keep a request or revision identity where stale responses could overwrite newer input.
- Reconcile from server truth without toggling the control through contradictory intermediate states.
- Preserve the user's selection and scroll context after refresh.
- Do not leave a clickable control visually enabled when combat or another rule makes the action illegal.

### Input

- Display the player's resolved binding, not a hard-coded key name.
- Each hotbar action must be independently rebindable through Vintage Story controls.
- Prefix registered control names with `VRPG —` because Vintage Story exposes only fixed control categories; do not use a brittle base-menu patch to simulate a custom category.
- Important menu operations need a mouse path and a keyboard path.
- Toggle-versus-hold behavior should be configurable when an action is sustained.
- Avoid mouse-only information: selected details or an explicit inspect action must reproduce important hover content.

## RPG Screen Patterns

### Stats

- Show each attribute's name, current value, and legal increase/decrease controls together.
- Show available points once near the allocation workspace.
- Keep full descriptions in tooltips or the selected detail area.
- Break scaling effects into one effect per line; never bury several numeric effects in a paragraph.
- Communicate foundation rules such as Dexterity-primary Reflex beside allocation and before confirmation.
- Disable mutation during combat and state why.

### Skills

- Lead with icon, name, rank, class identity, learned/locked state, and assignment.
- Show resolved damage, cost, cooldown, delivery, tags, and level scaling in the inspector.
- Equip or replace hotbar slots from this page using real loadout state.
- Make learned, available-to-learn, maxed, and unavailable skills visually distinct.
- Skill combinations and specialization choices deserve stronger hierarchy than raw percentage increases.
- Search and filters must work with the expected launch content volume.

### Talents

- Keep graph topology legible before adding node decoration.
- Starting foundations, reachable nodes, allocated paths, selected nodes, and unaffordable nodes need distinct shapes or treatments.
- Links render behind nodes and must not be mistaken for available paths.
- The inspector shows costs, prerequisites, effects, game-changing tradeoffs, and allocation/refund actions.
- In talent tooltips and inspectors, resolved numeric effects appear before cost, state, and interaction instructions. Effects use the strongest body-text hierarchy because they drive the allocation decision.
- Unnamed ordinary nodes may use a quiet generic label, but `Stat Node` must never visually outrank the actual stat bonus. Connection counts and other graph implementation details belong in authoring tools, not the player inspector.
- Foundation/sector names are authoring taxonomy, not automatic player-facing labels. Show a foundation name for a mutually exclusive starting route or when deliberately authored as identity; do not label every ordinary node as belonging to an `Exposure path` or equivalent internal sector.
- Preserve pan, zoom, and selection while the server confirms an allocation.
- Provide a way to locate a selected/search result in a large graph.

### Library

- Optimize for finding an answer: category, search results, then focused article.
- Generated mechanical content and authored explanation need a clear distinction.
- Cross-links should use display names, not asset codes.
- Do not use the Library as a dumping ground for information required at the moment of action.

### Options

- Use a category rail that can expand without compressing the setting rows.
- Each row contains a short label, one-sentence consequence, and a real control.
- Show whether a setting is client-local, player-profile, server-enforced, or unavailable only when that distinction affects the player.
- Changes should either apply immediately or expose an Apply/Cancel transaction consistently; do not mix the models without a reason.
- Restore-default actions must disclose their scope.

### Hotbar and combat HUD

- The hotbar shows the exact equipped skill, resolved binding, cooldown/charge, and unaffordable state.
- Cooldown and resource feedback belongs on or near the affected slot; normal combat cadence must not require chat.
- The bar can be moved and locked. Editing mode must be unmistakable and impossible to enter accidentally during combat.
- Do not show inactive universal resources. Only resources used by the current build occupy persistent HUD space.
- Reserve screen center for aiming, telegraphs, targets, and threats.
- Validate against bright, dark, low-contrast, and particle-heavy scenes.
- Frequent failure feedback uses controlled visual/audio channels and rate limits; exceptional explanations may fall back to chat.

### Items, loot, and comparison

- Rarity is communicated by name and treatment, not color alone.
- Lead with the comparison-changing values; put formulas, roll ranges, and advanced tags behind expanded detail.
- Place output, defense, skill, and utility bonuses within the relevant comparison group; never hide a decision-changing property in the bottom metadata strip.
- Clearly separate base identity, random affixes, crafted modifications, fittings, and temporary state.
- Support rapid keep/equip/salvage decisions at the planned 10–30-item rift volume.
- Comparison must account for the current build without presenting a misleading single “better” score.

## Tooltips and Copy

- A tooltip answers “what is this?” before “how is it calculated?”
- Keep the first view compact: name, function, important values, constraints, and current state.
- Advanced detail may show formulas, tags, sources, ranges, and exact scaling.
- Wrap text to a deliberate width and break numeric effects into separate lines.
- Keep tooltips inside the screen and away from the crosshair when possible.
- Update tooltip position or anchoring when it covers the selected object or action.
- Legends and other semantic overlays placed over a graph use an opaque-enough field to fully suppress underlying edges. Background geometry must not appear to connect, underline, or annotate legend samples.
- Use direct verbs: `Allocate`, `Refund`, `Equip in Slot 2`, `Unlock`, `Salvage`.
- Explain consequences, not implementation: `Unavailable while in combat`, not `request rejected by server state`.
- Follow the project writing guide for capitalization, tone, and terminology.

## Accessibility Baseline

Accessibility is part of the first layout, not a late alternate skin.

- All combat inputs are remappable.
- Essential meaning uses at least two channels among text, icon, shape, color, animation, and audio.
- Text and controls remain readable at supported GUI scales.
- Focus and selection remain visible for keyboard operation.
- Important timed information is not communicated through a very brief animation alone.
- Avoid rapid or full-screen flashes.
- Plan settings for reduced motion, combat-effect density, text scale, HUD scale, and background opacity as their systems mature.
- Critical hover information has a selection, focus, or explicit-inspect alternative.
- Audio cues supplement rather than replace visual state.

## Performance Rules

VRPG prioritizes performance and supports multiplayer combat, so interface cost must scale predictably.

- Rebuild Cairo textures when their displayed state changes, not every frame by default.
- Separate frequently changing layers such as resource fill or cooldown masks from expensive static text and decoration.
- Cache or reuse icons, text measurements, and shared surfaces where the API permits it.
- Clip scrollable and graph content before drawing it.
- Bound talent nodes, loot rows, floating text, combat indicators, and particles rendered at one time.
- Treat visual-effect density as a budget. More players and enemies must not multiply noise without limit.
- Profile representative worst cases, not empty screens.

## Workbench Workflow

The local preview tool is at `tmp/Tools/vs-ui-workbench/` relative to the Modding workspace, or:

```text
/home/garward/Games/Games/VintageStory/Modding/tmp/Tools/vs-ui-workbench/
```

From that directory:

```bash
./launch.sh --screen skills
PORT=8770 STYLE=vrpg SCREEN=talents VIEWPORT=compact ./launch.sh
```

Useful URLs include:

```text
http://127.0.0.1:8770/index.html?style=vrpg&screen=hub&viewport=desktop
http://127.0.0.1:8770/index.html?style=vrpg&screen=skills&viewport=compact
http://127.0.0.1:8770/index.html?style=wireframe&screen=talents&viewport=narrow
```

For each surface:

1. Load realistic VRPG asset data rather than idealized placeholder lengths.
2. Add enough generated records to test launch-scale content.
3. Test no data, one record, normal volume, maximum expected volume, locked content, and error/pending states.
4. Inspect desktop, compact, and narrow profiles.
5. Capture screenshots and inspect them at actual displayed size.
6. Compare the accepted workbench layout to an in-game screenshot, since browser rendering is only an approximation of Cairo/OpenGL.

The local visual audit utility can collect screenshots, layout bounds, overflow signals, and browser errors:

```bash
/home/garward/Scripts/Tools/ClawForge/tools/visual_audit.py \
  '{"target":"http://127.0.0.1:8770/index.html?style=vrpg&screen=skills&viewport=desktop"}'
```

Automated output does not approve visual quality. Review the resulting images for hierarchy, readability, density, RPG identity, and gameplay obstruction.

## Review Scorecard

Score each category from 0 to 2. A zero blocks implementation or release regardless of the total.

| Category | 0 — blocked | 1 — needs revision | 2 — ready |
|---|---|---|---|
| Task clarity | Purpose or next action is unclear | Understandable after exploration | Purpose and next action are immediate |
| Hierarchy | Decision-changing information is hidden, styled as metadata, or everything competes equally | Mostly ordered, but an important effect can be skipped | Selection, action, and decision-changing effects read in order |
| Readability | Clipped, tiny, crowded, or low contrast | Readable with friction | Readable at supported sizes and scales |
| Interaction truth | Fake, stale, or unexplained controls | Works with weak state feedback | All states and server outcomes are clear |
| Scalability | Breaks at realistic volume | Scrolls but becomes awkward | Navigation and density hold at target volume |
| RPG expression | Build identity is text-only or generic | Some useful identity | Icons, values, and relationships scan quickly |
| Combat safety | Obscures aiming or threats | Acceptable in easy scenes | Clear in worst-case combat scenes |
| Accessibility | Meaning depends on one channel or mouse hover | Partial alternatives | Multiple channels and input paths work |
| Consistency | Introduces arbitrary rules | Mostly follows shared patterns | Uses and improves the shared system |
| Performance | Unbounded redraw or content cost | Acceptable in normal state | Bounded and tested in representative stress state |

## Automatic Rejection Conditions

A draft returns to the workbench if any of these are true:

- It contains a visible control that does nothing.
- It shows a value that is not real or not connected to gameplay.
- A decision-changing effect appears only in prose, a tooltip, tags, or low-priority footer metadata.
- The primary action exists only as a command.
- Text escapes its control, clips at minimum size, or becomes unreadably small.
- Important information is conveyed only by color or hover.
- It repeats persistent information without a task-specific reason.
- It uses chat for feedback that occurs during normal skill cadence.
- It assumes hard-coded keys.
- It works only with the current tiny data set.
- It obscures the crosshair, target, or common threat area during combat.
- It cannot explain its disabled, locked, pending, or rejected state.
- Its workbench and in-game screenshots have not both been reviewed.

## Definition of Done

A VRPG interface surface is done at its current scope only when:

- Its screen contract is documented.
- Its primary task is obvious to a first-time tester.
- Every visible control changes real state or navigates somewhere real.
- Normal, empty, locked, disabled, pending, error, success, and maximum-volume states have been exercised where applicable.
- It passes the scorecard with no zeroes.
- It has been reviewed in the workbench at default, compact, and stress layouts.
- It has been reviewed in game at more than one GUI scale.
- Resolved hotkeys, server authority, reconnect behavior, and multiplayer ownership have been tested where applicable.
- No new arbitrary visual constants were duplicated instead of added to the shared theme/component layer.
- Newly discovered follow-up work has been added to the live testable-version TODO.

## External Design References

These sources informed the principles above; VRPG's actual rules remain specific to Vintage Story, Cairo rendering, and this mod's combat and progression needs.

- [Riot Games — User Interface Design](https://www.riotgames.com/en/artedu/user-interface-design): audience, intuitive navigation, cohesive presentation, and showing information at the useful moment.
- [Riot Games — VALORANT Shaders and Gameplay Clarity](https://www.riotgames.com/en/news/valorant-shaders-and-gameplay-clarity): preserving silhouettes, depth, performance, and competitive readability when adding visual treatment.
- [Blizzard — Combatting Demons with Accessibility in Diablo IV](https://news.blizzard.com/en-gb/article/23954932/combatting-demons-with-accessibility-in-diablo-iv): remapping, toggle/hold choices, scalable presentation, multiple feedback channels, and quick-glance combat information.
- [Game Accessibility Guidelines — Full List](https://gameaccessibilityguidelines.com/full-list/): a broad checklist for input, presentation, cognition, communication, and difficulty-related accessibility.
