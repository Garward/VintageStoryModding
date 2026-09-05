# Combat HUD Total-Replacement Design

Status: Adopted target; implementation staged behind a capability switch  
Parents: [Complete game design](complete-game-design.md) · [UI visual language](ui-visual-language-and-hub.md)  
Related: [Inventory, loot, and storage](inventory-loot-and-storage.md) · [Survival ownership audit](survival-system-ownership-audit.md)  
Index: [VRPG design index](README.md)

## 1. Purpose

VRPG replaces Survival's ordinary play HUD as one coherent combat interface. It does not place independently themed RPG widgets beside the Vanilla hotbar and stat bars.

The HUD has one job: let the player read the next combat decision without looking away from enemies, telegraphs, or the crosshair. Build explanations, exact progression details, inventory management, and diagnostics remain in the player menu or logs.

The reference silhouette is a broad framed health display joined to a familiar central hotbar. VRPG keeps that readability but uses the established oxidized-iron, fitted-plate, dull-copper, and restrained-heat language rather than ornate fantasy stone.

## 2. Screen contract

### Screen: Combat HUD

- **Player goal:** Read immediate combat state and activate the intended item or skill.
- **Primary decision:** Attack, evade, spend a resource, use a quick item, trigger a prepared payoff, or disengage.
- **Entry points:** Always present during ordinary play; reduced or absent in spectator, cinematic, loading, and configured screenshot states.
- **Required information:** Current health, relevant build resources, equipped skill state, resolved bindings, selected quick item, actionable cooldowns and charges, critical personal statuses, current target, boss phase, channel progress, and immediate encounter warnings.
- **Primary controls:** Existing remappable hotbar and skill actions. The HUD displays resolved mappings and never invents physical-key ownership.
- **Secondary controls:** Mouse-wheel selection, offhand/alternate-use state, inspect tooltip, and an explicit HUD-layout mode outside combat.
- **Persistent information:** Health, available combat slots, selected slot, and any resource the current loadout can spend.
- **Explicitly excluded:** Hunger, satiety, temporal stability, body temperature, nutrition, backpack capacity, raw XP totals during combat, full status descriptions, complete objective prose, and developer identifiers.
- **Empty state:** A resource or slot family with no gameplay meaning is absent; the remaining console closes the gap rather than showing dead instruments.
- **Disabled state:** The slot remains recognizable and gives a concise local reason through selection or inspect input.
- **Pending state:** Server-ordered actions show one restrained engaged/heat state and reject repeated input where ordering matters.
- **Error state:** The affected slot releases and briefly shows a reason such as `Not enough mana`, `No target`, or `Out of range`.
- **Success feedback:** Resource movement, cooldown motion, charge consumption, and a brief local mechanical response; routine success does not use chat.
- **Authority:** Server state is canonical. Local press feedback is immediate and reconciles without rewinding unrelated HUD state.
- **Expected volume:** Up to ten unified combat-action positions, two common resources, one narrow XP track, a bounded status row, one selected-target frame, and up to the encounter-defined boss frames.
- **Combat-visible:** Yes. Center aim space and ground-telegraph space have priority over ornament.

## 3. Target composition

```text
                                  TOP ENCOUNTER BAND
                 +------------------------------------------------+
                 | boss mark  NAME / PHASE              level     |
                 |========= health / ward / break state ==========|
                 +------------------------------------------------+
                       only while encounter ownership is active

                            WORLD AND AIM SPACE
                 selected-target frame stays close to its target
                  telegraphs and threats are never covered by HUD

                                  STATUS BAND
                         [status] [status] [status]
                         bounded, prioritized, inspectable

                               BOTTOM COMBAT CONSOLE
       +----------------+ +----------------------------+ +----------------+
       | HEALTH         | | familiar combat action bar | | BUILD RESOURCE |
       | 6575 / 6575    | | [1][2][3][4][5]...[10]     | | 3945 / 3945     |
       |===============>| | selected slot locks/warms  | |<===============|
       +----------------+ +----------------------------+ +----------------+
                    +-- item, skill and consumable action state --+
                    `----------- thin level / XP groove -----------'
```

This is one visual assembly, not three draggable boxes. Health and the current build resource terminate into the hotbar chassis so the bottom silhouette reads as a single instrument.

### 3.1 Resource plates

- Health is always present outside modes that intentionally hide the combat HUD.
- Mana, blood, rage, or another class resource occupies the right plate only when the equipped build uses it.
- A secondary shield is a distinct inset layer attached to health, not another permanent full-width bar when its maximum is zero.
- Exact current and maximum values are available by default because large RPG values and threshold builds make the numbers decision-relevant. A compact display option may show current value only.
- Damage prediction, delayed loss, recovery lockout, or incoming healing may use a bounded ghost segment when the server exposes truthful data.
- Low-health warning uses fill, shape, and a restrained pulse; it does not depend on red alone or flash the entire screen.

### 3.2 Unified combat action bar

- Preserve the familiar ten-position Vanilla silhouette and semantic slot actions.
- The row is a combat loadout and quick-use surface, not the player's physical storage boundary. It replaces the separate Vanilla item bar and current floating VRPG skill bar.
- Every assignment declares an action kind: `SelectHeldItem`, `ActivateSkill`, `UseConsumable`, or another validated extension kind. Presentation uses an icon/frame cue and interaction follows that kind; it is never inferred from a physical key.
- Item and consumable assignments reference VRPG-owned inventory entries through the inventory bridge. Binding one does not move its canonical stack into a backpack or hotbar container.
- A selected held-item position uses a fitted raised plate and persistent notch. An instant skill or consumable uses a press/engage response and does not falsely remain selected. Hover and keyboard focus remain visually distinct from both.
- Show the player's resolved input mapping, not `1` through `0` as hard-coded labels.
- Quantity sits in one stable corner. Durability, ammunition, or charges share one reserved lower-edge channel and never overlap quantity.
- Selected-item name and exceptional use mode appear briefly above the console. Full interaction help remains available through the configured help path.
- Offhand appears as a small joined plate only when the current ruleset enables it.

### 3.3 Skill assignments

- Skill assignments occupy ordinary positions in the unified action bar; they do not create a second permanent row or separately themed HUD.
- Each assigned position shows identity, resolved binding, cooldown, resource insufficiency, charges, and channel/held state when applicable.
- Cooldown uses a moving shutter or radial mask plus remaining time near completion. Availability never depends only on color.
- Prepared-payoff skills conduct restrained heat as their condition approaches. For example, a detonation plate may show `7 / 10` while the selected target carries seven valid stacks.
- Mine or deployable skills show available charges and world-active count separately when both change the next decision.
- A skill unavailable because of target, range, resource, weapon, or state exposes the shortest actionable reason on deliberate inspect; it does not keep permanent prose on screen.

### 3.4 Context profiles and Home building

The action bar supports named profiles without changing the player's physical bindings:

```text
Outside Home                              Inside Home
+----------------------+                  +----------------------+
| ADVENTURE profile    | -- enter Home ->| HOME profile         |
| skills/items/actions |                  | utilities/items      |
+----------------------+                  +----------+-----------+
        ^                                             |
        | exact restore                         enter Build Mode
        |                                             v
        |                                  +----------------------+
        `----------- leave Home -----------| BUILD palette        |
                                           | placed materials     |
                                           +----------------------+
```

- Realm context determines which profiles are legal; it does not alter physical mappings.
- Entering Home changes to the saved Home profile because ordinary Home is non-hostile. A short `HOME` plate/latch transition makes the change explicit.
- Build Mode is a deliberate state entered through a visible control or its resolved semantic action. Merely looking at a block or carrying material never silently changes the bar.
- The Build palette contains references to canonical inventory entries, not copied stacks. Selecting a position chooses a placement material; placement consumes the canonical quantity through the housing transaction bridge.
- Ten palette positions are favorites, not the complete block catalog. The Inventory Build view provides search, categories, quantities, palette assignment, and selection of unpinned materials.
- Pick-block may assign or select an owned material according to a configurable palette policy. It cannot create items or bypass discovery, inventory, or Home permissions.
- Leaving Build Mode restores the exact Home profile. Leaving Home restores the exact Adventure profile, including selection and page state.
- Health and combat resources compress while safely building; build material name, remaining quantity, orientation, and placement validity take their place. Critical health or an unexpected hostile state immediately restores combat-critical presentation.
- Failed placement reports one local reason beside the active material, such as `Outside your Home`, `Space is blocked`, or `None left`.

### 3.5 XP

- XP is a narrow amber/copper groove along the console's underside, with a compact level stamp.
- Exact XP appears after a gain, on inspect, or in Stats. It is not persistent combat text.
- Level-up feedback may heat the full groove once and mechanically settle; it must respect reduced-motion settings.

### 3.6 Player statuses

- Show only statuses that can change an immediate choice: control loss, lethal damage-over-time, immunity, major vulnerability, short empowerment, or a build-defined trigger.
- Sort by danger/actionability, then expiration. Cap visible icons and collapse lower-priority effects behind an inspectable count.
- Stack count and duration occupy fixed regions. A status definition decides whether either is meaningful.
- Long-duration passive bonuses belong in the player menu unless a build specifically reacts to their live state.

### 3.7 Targets and bosses

- Ordinary selected targets use a compact world-anchored or near-crosshair frame. Nearby unselected creatures do not all receive detailed permanent bars.
- Elites may add one rank mark and one mechanic-relevant layer; their entire stat sheet remains undisclosed.
- A boss or encounter-owned major target uses the top frame: name, known phase, health, relevant ward/break layer, and only the statuses the encounter expects the player to act upon.
- Multiple bosses share a deliberate encounter layout. They are not stacked by whichever entity happened to enter a radius first.
- Boss-frame membership comes from VRPG encounter authority and adapted creature rank, not only Vanilla's `EntityBehaviorBoss`.

## 4. Material and motion

- The chassis is near-black oxidized iron with quiet mottling and heavier outer edges.
- Rust collects at joints, lower recesses, fasteners, and interruptions; it stays out of fill channels and text planes.
- Idle interactive edges use dull copper. Active selection and live skill flow use scarce heat orange.
- Health remains iron-red, mana or a similar magical resource uses constrained blue-green/verdigris, shield uses a pale distinct layer, and XP uses amber. Theme does not erase resource semantics.
- Boss frames may be heavier and wider than the player console but use the same fitted metal grammar.
- Plate engagement is approximately 70–120 ms; state conduction is approximately 140–260 ms. Damage and cooldown animation must remain readable at reduced motion, where travel becomes immediate shape/value change.
- Decorative gears are prohibited unless their motion communicates progress, cooldown, charge, or encounter state.

## 5. Runtime ownership

```text
VrpgHudSystem                         composition root and lifecycle
|
+-- VrpgHudCapability                effective server-approved ownership/config
+-- VrpgHudStateStore                immutable client snapshots and local projections
|   +-- PlayerResourcesView
|   +-- CombatActionBarView
|   +-- ActionProfileView
|   +-- PlayerStatusView
|   +-- TargetView
|   `-- EncounterView
|
+-- VrpgHudInputController           semantic action handlers, selection and inspect
+-- VrpgActionProfileController      Adventure, Home and Build state transitions
+-- VrpgHudSuppressionCoordinator    removes only superseded Vanilla presentation
|
+-- HudElementVrpgCombatConsole      bottom chassis, resources, items, skills and XP
+-- HudElementVrpgTargetLayer        selected target and bounded world markers
+-- HudElementVrpgEncounterFrame     bosses, phases and encounter warnings
+-- HudElementVrpgCombatFeedback     channel, combat text and restrained screen pulse
|
`-- UI/Hud/
    |-- VrpgHudTheme.cs              semantic material and resource tokens
    |-- VrpgHudLayout.cs             pure scale/safe-area layout calculations
    |-- VrpgHudStaticSurfaces.cs     cached chassis and plate textures
    |-- VrpgResourcePlate.cs         frequently changing fills and values
    |-- VrpgActionSlotStrip.cs       item/skill actions, selection and pointer input
    |-- VrpgActionStateOverlay.cs    cooldowns, charges, quantities and readiness
    |-- VrpgStatusStrip.cs           bounded prioritized statuses
    `-- VrpgEncounterBar.cs          reusable boss/phase presentation
```

`VrpgHudStateStore` is the only input to presentation. Render elements do not discover game rules by walking registries or mutable inventories during drawing. Domain/network systems publish changed snapshots; local projections may animate toward confirmed values without becoming authoritative.

The bottom console should be one `HudElement`, but it need not be one texture. Static chassis surfaces are cached. Resource fills, cooldown masks, item quantities, and status timers are small dynamic layers updated only when their visible value changes. Avoid allocating a full-frame Cairo surface every render frame.

## 6. Vanilla HUD ownership audit

| Existing owner | Target policy | Behavior that must survive or be replaced |
|---|---|---|
| `HudStatbar` | Suppress all rendering in VRPG HUD ownership | Health transition cues retained through VRPG snapshots; hunger is disabled; oxygen becomes contextual only if water/breath remains relevant |
| `HudHotbar` | Stage 1: initialize then suppress rendering. End state: VRPG owns semantic handlers and the Vanilla element is not registered | Slot selection, mouse wheel, hand-action cancellation, active-slot broadcast, offhand flip, held equip/idle sounds, player animation update, item-use help, tool modes, and remapping refresh |
| `HudBosshealthBars` and `ModSystemBossHealthBars` | Suppress Vanilla frames and prevent dynamic creation once the VRPG encounter frame is active | None as presentation; VRPG independently tracks encounter-authorized bosses and adapted boss ranks |
| Survival tutorial HUD | Disabled by default with the Survival tutorial system | VRPG onboarding owns replacement prompts |
| Temporal-stability gear in `HudHotbar` | Removed | No replacement while temporal stability is disabled |
| Saturation/hunger bar | Removed | No replacement while hunger is disabled |
| Vanilla entity/name information | Replace or restyle for ordinary play | Interaction identity and selected-target readability; no Vanilla class or Survival stat leakage |
| Chat, disconnect, errors, death, discovery and interaction prompts | Retain behavior; progressively skin through shared VRPG notification/prompt components | Communication, recovery, actionable errors, death flow, discoveries and input help |
| Debug, coordinates, profiler and editor overlays | Keep outside the player theme contract and gate by existing debug/admin access | Diagnostic truth and authoring workflows |

Removing `HudHotbar` too early is unsafe. Its `OnBlockTexturesLoaded` currently installs handlers for the semantic `hotbarslot*` actions, and its active-slot path stops held actions, sends interaction cancellation, updates held animations and sounds, displays tool modes, and refreshes interaction help. The first implementation therefore lets it initialize and suppresses only its rendering. The end-state controller takes those behaviors over explicitly before unregistering it.

Likewise, hiding only `GuiElementStatbar` is an interim patch. The ownership switch must suppress the entire obsolete Vanilla stat presentation so oxygen or saturation cannot reappear beside the VRPG console.

## 7. Suppression boundary

Use one effective capability, conceptually `HudReplacement.Enabled`, default-enabled by the VRPG total-conversion preset and synchronized by the server. Every suppression patch checks this capability; independent booleans must not create half-replaced states.

Activation is transactional:

1. Verify that the combat console has valid player-resource and loadout snapshots.
2. Verify that required semantic action handlers have an owner.
3. Open and render the VRPG console successfully.
4. Suppress superseded Vanilla rendering on the next UI boundary.
5. If validation fails, keep the complete Vanilla HUD rather than exposing neither HUD or both.

The compatibility preset leaves Vanilla HUD elements unchanged and permits the existing standalone VRPG widgets only as an explicit development option. The total-conversion preset never mixes those widgets with the unified console.

Suppression should target known types and behaviors, not screen rectangles, guessed composer keys, or physical keys. Startup order is recorded and verified because core HUD elements are registered by `GuiManager`, while Survival boss and tutorial elements are created by mod systems later.

## 8. Input and inventory integration

- Preserve or deliberately migrate semantic actions such as `hotbarslot1`; default physical mappings are not part of HUD logic. The current compatibility slice uses Vanilla's ordinary slot mappings and VRPG's distinct `Alt+1` through `Alt+8` skill defaults; the final unified profile must migrate semantic assignments deliberately rather than infer them from either physical mapping.
- `VrpgHudInputController` owns one semantic action per visible action position, resolves current mappings for labels, and watches mapping changes.
- Quick-use bindings refer to stable VRPG item-entry IDs or data-defined item families. The inventory service materializes only the bounded Vanilla stack required for held-item interaction and reconciles use transactionally.
- A slot selection request may update locally for responsiveness, but the visible quantity and resulting held item reconcile from authoritative inventory/loadout state.
- Existing VRPG skill bindings receive an explicit migration into unified action positions. Migration never silently assigns two actions to one input.
- Adventure, Home, and Build assignments are stored separately. Switching profiles changes action targets, never the input mapping itself.
- Home building uses `IStorageBridge` to reserve a bounded item stack, invoke supported block/item placement behavior, then commit the consumed amount or roll it back. This preserves compatible modded block behavior without making the bridge stack canonical.
- HUD layout editing is a deliberate non-combat mode in Options. The ordinary HUD is locked and does not intercept pointer input outside actual interactive regions.

### Combat pointer ownership

Combat mode owns pointer intent before Vanilla block interaction code can interpret it:

- The primary action is `BasicAttack`. Holding it repeats the equipped weapon's data-defined melee or ranged basic attack at its authored cadence. It never falls through to mining, even when the aim ray crosses terrain.
- `CombatAimResolver` produces separate entity, ground-point, and direction candidates. Skills declare which candidates they accept; Blink may request a reachable ground point without making that block mineable or placeable.
- Ordinary Adventure terrain is not a block interaction target. Client suppression prevents accidental feedback, while server zone authority rejects block break and placement packets so a modified client cannot bypass the rule.
- The secondary interaction action may target explicitly eligible entities such as NPCs, dungeon mechanisms, shrines, portals, and loot objects. Eligibility is data-driven and server-authoritative; combat mode does not globally disable entity interaction.
- World-block use is disabled in Adventure unless a future authored interaction adapter explicitly exposes that block as an encounter object. Home and Build profiles retain their separate placement and block-use paths.
- Attack targeting and interaction targeting are distinct. An interactable entity must not steal an attack aimed at a hostile, and a hostile must not prevent an intentional nearby dungeon interaction without a clear selection rule.

The immediate implementation boundary is three small owners: `CombatBasicAttackController` handles press/hold/release and weapon cadence, `CombatAimResolver` supplies legal aim candidates, and `CombatInteractionController` validates secondary entity interactions. None calls Vanilla mining as a fallback.

## 9. Configuration

Default-enabled total replacement options:

- HUD scale and safe-area inset.
- Compact or exact resource numbers.
- Status-icon limit.
- Combat-text amount and merge rate.
- Selected-target presentation.
- Boss-frame scale.
- Reduced motion, pulse intensity, and screen-edge effect intensity.
- Optional hiding outside combat, while never hiding critical low-health or encounter state unexpectedly.

Advanced compatibility options may restore the complete Vanilla HUD only when VRPG HUD ownership is disabled. Do not expose supported configurations that independently enable both Vanilla and VRPG health/hotbar owners.

## 10. Implementation sequence

### Current playable slice

- `ReplaceHotbarInSurvival` is the temporary context adapter. When enabled, a player whose underlying Vintage Story mode is `Survival` receives the VRPG combat action profile.
- The ordinary `hotbarslot1` through `hotbarslot10` semantic bindings drive the unified bar. Positions 1–8 activate the corresponding equipped VRPG skills; unassigned positions are inert and remain visibly empty.
- Press, release, held targeting, repeat casting, cooldowns, and charges remain connected to the existing server-authoritative skill pipeline.
- Creative, Guest, and Spectator retain the compatibility item profile. This gives Home/build development a safe fallback until realm-context packets own profile selection.
- VRPG play context must remain separate from Vintage Story's closed `EnumGameMode`. Future zone authority replaces the Survival predicate with `Adventure`, `Home`, or `Build`; it does not introduce an unknown engine enum value.
- Combat entry preserves the prior Vanilla selection, selects an existing weapon or a neutral empty slot, and prevents later pickups from becoming the active combat item. The wheel cycles weapon-eligible hotbar entries only and wraps in both directions.
- Client guards currently reject Vanilla block placement and stop Survival block-break initialization/progression in combat. This is an interim safety boundary, not the final aim or server-protection implementation.
- The final primary-action path remains an immediate Phase D task: hold-to-repeat must invoke the equipped weapon's authored melee or ranged basic attack through `CombatBasicAttackController`, never the block-breaking path.

### Immediate input-overhaul checklist

- [x] Keep pickups from replacing the selected combat weapon; use a neutral hand when no weapon is available.
- [x] Route mouse-wheel selection through the combat weapon set and wrap in both directions.
- [x] Reject Vanilla placement/block use and stop Survival mining initialization/progression on the client while the combat profile is active; entity-targeted right-click remains available.
- [ ] Replace Vanilla block hover as a combat target with `CombatAimResolver` candidates and distinct entity/ground feedback.
- [ ] Implement held `BasicAttack` input with data-defined melee/ranged behavior, cadence, cancellation, and server validation.
- [ ] Preserve secondary interaction for eligible dungeon entities through a data-defined, server-authoritative filter.
- [ ] Enforce Adventure placement, break, block-use, and entity-interaction policy on the server.
- [ ] Move combat entry/pickup ownership to `IPlayerItemStore` and materialize only the selected weapon or bounded quick-use stack.

### Phase A — pure contracts and audit

1. Add `HudReplacementCapability`, `VrpgHudViewState`, and pure layout/state reducers.
2. Inventory every Vanilla HUD registration and every behavior currently performed by `HudHotbar`.
3. Add tests for capability resolution, contextual resource visibility, status prioritization, and safe-area layout.

### Phase B — unified visual prototype

1. Build `HudElementVrpgCombatConsole` using current real resource, item-loadout, and skill packets.
2. Bridge the real Vanilla hotbar inventory for item icons and interactions; do not use fake slots.
3. Fold the existing skill, channel, and player-status presentation into the shared layout.
4. Keep the old VRPG resource and skill HUD classes available only behind a development flag.

### Phase C — transactional presentation replacement

1. Add the suppression coordinator and type-specific patches for `HudStatbar`, `HudHotbar`, Vanilla boss frames, and Survival tutorial presentation.
2. Activate suppression only after the unified console passes readiness checks.
3. Test reconnect, respawn, dimension travel, game-mode changes, GUI-scale changes, remapping, and toggling the ownership capability at a restart boundary.

### Phase D — VRPG input and item bridge ownership

1. Add `CombatAimResolver`, separating entity, legal ground-point, and direction aim from ordinary block selection.
2. Add `CombatBasicAttackController`; holding the semantic primary action repeats the equipped weapon's authored melee or ranged attack and has no mining fallback.
3. Add `CombatInteractionController`; preserve server-validated right-click interaction with eligible dungeon entities while Adventure block use remains disabled.
4. Reimplement semantic hotbar selection, wheel selection, hand cancellation, broadcasts, animations, sounds, tool modes, help, and offhand behavior behind small services.
5. Bind applicable action positions to canonical VRPG inventory entries and reconcile materialized held stacks.
6. Stop registering or retain only a behavior-only compatibility form of Vanilla `HudHotbar` after parity tests pass.

### Phase E — encounter presentation

1. Add server-authoritative selected-target and encounter snapshots.
2. Implement ordinary target, elite, multi-boss, phase, ward/break, and disconnected/rejoined states.
3. Disable Vanilla boss tracking in VRPG ownership and test adapted creature-mod bosses.

### Phase F — coherent peripheral surfaces

Restyle actionable interaction prompts, discoveries, player-facing errors, death, and the loot feed with shared HUD tokens. Chat and debug/admin overlays retain their functional layouts unless a later contract justifies replacing them.

## 11. Test and release gate

### Functional matrix

- Exercise held-item, instant-skill, held/channelled-skill, and consumable positions through remapped semantic actions; selection and activation semantics remain unambiguous.
- Enter and leave Home and Build Mode repeatedly; every profile, selected position, palette, and mapping restores exactly without item movement.
- Place, rotate, pick, exhaust, reject, cancel, break, and refund Vanilla and compatible modded blocks through the aggregate inventory bridge without duplication or loss.
- Change slots while holding an attack/use action; cancellation and server state remain correct.
- Flip offhand where enabled; held animation, modifiers, tool mode, idle/equip sounds, and interaction help remain correct.
- Spend and regenerate every enabled resource, cross thresholds, gain shield, lose shield, gain XP, level, die, respawn, reconnect, and change dimension.
- Exercise ready, insufficient-resource, cooldown, charged, channeling, held, rejected, and server-corrected skill states.
- Show zero, one, maximum visible, and overflow player statuses.
- Show ordinary targets, elites, one boss, multiple bosses, phase transition, boss leaving range, boss death, and reconnect during an encounter.
- Verify that no Vanilla health, hunger, oxygen, stability, backpack, hotbar, tutorial, or boss frame leaks into ordinary VRPG play.

### Visual matrix

- Minimum and normal supported resolutions, ultrawide, and every supported GUI scale.
- Bright snow, darkness, rain, fire, dense particles, red/orange terrain, and blue-green environments.
- Empty and full slots, four and eight skills, very large resource values, long localized names, long binding labels, and color-vision simulation.
- Crosshair, target silhouette, ground telegraphs, interaction target, and lower peripheral threats remain unobstructed.

### Performance gate

- No full-screen Cairo surface is created every frame for static HUD decoration.
- Static chassis textures rebuild only on scale, theme, layout, or device reset.
- Textures and event listeners are disposed on world leave and replacement rollback.
- Dynamic updates are dirty-region/state driven and bounded by configured visible entries.
- Profile representative multi-target combat with maximum statuses, combat text, telegraphs, and boss presentation active.

The replacement is ready only when every displayed value is real, every shown binding is resolved from current settings, Vanilla presentation cannot leak through, and failure safely restores a complete usable HUD.
