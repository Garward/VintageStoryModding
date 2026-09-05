# Creature Ingestion and Adaptation

Status: Adopted compatibility exception; exact asset-discovery schema remains provisional  
Parent: [Complete game design](complete-game-design.md)  
Related: [Combat spaces, movement, and AI](combat-spaces-movement-and-ai.md) · [Mod compatibility and addon contract](mod-compatibility-and-addon-contract.md)  
Fixtures: [Reference creature pack audit](reference-creature-pack-audit.md)  
Index: [VRPG design index](README.md)

## 1. Goal

Creature packs are a supported expansion surface even though broad Survival-mod compatibility is not. A server operator should be able to install an ordinary creature-content mod and receive usable VRPG enemies without hand-authoring AI code or translating Survival statistics one creature at a time.

Automatic ingestion is a safe baseline, not a promise to reproduce the foreign mod's original gameplay. VRPG reuses compatible presentation assets and identity while owning every combat rule.

The intended result is:

- Content authors can contribute creatures with models, textures, animations and sounds.
- VRPG automatically produces a valid, conservative combat actor when enough assets are available.
- Data profiles can turn that baseline into a distinctive enemy without C# or Harmony patches.
- New code is needed only for genuinely new reusable behavior or presentation machinery.
- A creature that cannot be adapted is excluded with a precise report; it never enters combat half-converted.

## 2. Ownership boundary

```text
FOREIGN CREATURE PACKAGE                         VRPG RUNTIME
------------------------                         ------------
entity code / variant identity ----------------> source identity registry
shape, texture, animation, sound ---------------> presentation descriptor
collision and locomotion hints -----------------> normalized body/navigation profile
optional VRPG adaptation data ------------------> archetype/role/ability overrides

foreign AI tasks -------------------------------> ignored
foreign health/damage/armor --------------------> ignored
foreign drops and harvesting -------------------> ignored
foreign natural spawning -----------------------> blocked
foreign hostility/domestication ----------------> ignored unless explicitly mapped
foreign progression and configuration ----------> ignored

                                                   |
                                                   v
                                      +----------------------------+
                                      | VRPG ACTOR FACTORY         |
                                      |----------------------------|
                                      | VRPG-owned entity runtime  |
                                      | VRPG stats and resources   |
                                      | VRPG AI and navigation     |
                                      | VRPG abilities and combat  |
                                      | VRPG defeat and rewards    |
                                      | VRPG encounter eligibility |
                                      +----------------------------+
```

VRPG should prefer a synthetic or shadow entity type in its own namespace rather than directly spawning the foreign gameplay class. The generated actor references compatible foreign presentation assets but uses a VRPG-controlled entity implementation and behavior stack. This prevents an inherited `OnGameTick`, AI task, damage handler, reproduction system or other custom code from becoming a second gameplay owner.

If a creature depends on a custom renderer, custom entity subclass or proprietary runtime asset transformation, it requires an explicit code adapter and is not automatically admitted.

### 2.1 Engine feasibility

The inspected Vintage Story API supports this architecture rather than requiring per-creature Harmony patches:

- Loaded creature definitions are exposed as `EntityProperties` through `IWorldAccessor.EntityTypes`.
- `EntityProperties.Clone()` copies the body, client presentation, sounds and sided property descriptors.
- Entity implementation classes and entity behavior classes are separately registered.
- Sided behaviors are constructed from `BehaviorsAsJsonObj`, so a cloned descriptor can receive an intentionally rebuilt VRPG behavior set rather than source tasks.
- `ICoreAPI.RegisterEntityClass(...)` and `IClassRegistryAPI.CreateEntity(EntityProperties)` provide the registration and construction seams needed for a generated type.

The prototype still needs to establish the correct asset-loading phase, stable numeric ID assignment, server/client registry equality and remapping behavior across source-pack changes. Those are implementation gates, not reasons to fall back to patching every foreign creature.

## 3. Admission tiers

| Result | Meaning | Combat eligibility |
|---|---|---|
| **Auto-adapted** | Standard assets and body information produced a complete conservative actor | Eligible after validation |
| **Profiled** | A data file selected roles, abilities, tags, animation bindings and reward families | Eligible after validation |
| **Native** | A VRPG addon supplies definitions or registered reusable verbs | Eligible within declared API version |
| **Presentation-only** | Assets can be loaded, but no safe locomotion/attack representation exists | Bestiary/preview only; never spawned |
| **Rejected** | Missing assets, unsafe runtime dependency, invalid geometry or conflicting identity | Not loaded into VRPG registries |

Installing a creature mod does not automatically admit its other items, recipes, world generation, blocks or systems. Creature ingestion is capability-scoped.

Content-only packs are the safe automatic case. If an installed source includes a DLL, VRPG can ignore its creature behaviors but cannot sandbox its mod systems, patches or global hooks. Code-bearing sources therefore require an audited allowlist/operator override or a future asset-only import path that does not activate the foreign assembly.

## 4. Ingestion pipeline

```text
Discover candidates
      |
      v
Classify source safety
      |---- unsafe custom runtime dependency ------> explicit adapter or reject
      v
Extract presentation and body descriptors
      |
      v
Resolve supplied profile, known hints or conservative defaults
      |
      v
Generate normalized VRPG actor definition
      |
      v
Validate assets, animations, navigation, abilities and budgets
      |---- invalid -------------------------------> report and quarantine
      v
Register synthetic actor + encounter tags
      |
      v
Exercise automated spawn/combat/defeat test
      |---- failure -------------------------------> disable actor, keep report
      v
Eligible for explicitly compatible encounter pools
```

The generated definition and its input fingerprint are cached. Cache invalidation occurs when VRPG, the source mod, the compatibility profile, or relevant server balance rules change. The resolved output is recorded with the save so a changed content set can be previewed before world entry.

## 5. Automatic baseline

Automatic adaptation must produce something mechanically valid, not pretend to infer a creature author's intent. The baseline uses a small number of deterministic templates.

### 5.1 Body and locomotion

VRPG may infer:

- Ground, flying, swimming or stationary locomotion from compatible source metadata.
- Body size, collision width/height, eye position and approximate reach from the source entity definition.
- Turn and acceleration limits from the selected locomotion template.
- Whether doors, narrow lanes, low ceilings, slopes or temporary terrain are navigable.
- Presentation scale within configured safety limits.

Source collision boxes are clamped and validated. Visual size may influence navigation footprint, reach and threat cost, but it does not directly grant exponential health or damage.

### 5.2 Statistics

Foreign Survival statistics are not translated numerically. The VRPG actor receives a stat budget from:

1. Encounter difficulty and region band.
2. Selected rank: fodder, standard, elite, champion or boss-capable.
3. Mechanical role and ability budget.
4. Body/navigation costs that materially affect the fight.
5. Explicit profile modifiers within validation bounds.

This keeps a creature pack useful across the whole game and avoids importing arbitrary health scales. Automatic creatures default to standard rank and a conservative baseline budget.

### 5.3 AI and abilities

The default ground creature receives a legible minimal kit:

- Acquire a valid hostile target through VRPG perception.
- Advance using VRPG navigation and local steering.
- Use a telegraphed short-range strike when an attack slot is available.
- Recover after committing.
- Respond to hazards, inaccessible targets, temporary terrain and encounter leash rules.
- Enter VRPG defeat handling and reward resolution.

Flying, swimming and stationary actors use separate safe templates and are admitted only after those locomotion modes have supported navigation and anti-cheese contracts. Unsupported locomotion does not silently fall back to broken ground pursuit.

Animation aliases are discovered from a configurable vocabulary such as idle, walk, run, attack, hurt and die. A profile can bind exact animation codes and event timings. Missing decorative animations may fall back; a missing readable locomotion or attack presentation either uses an intentional generic fallback or prevents combat eligibility.

Known source task and behavior codes may be analyzed as non-authoritative hints. For example, melee, projectile, flee, leader and boss metadata can suggest VRPG roles or abilities. The importer never executes those tasks, copies their raw balance, or assumes an unknown custom code has familiar semantics. Suggestions carry evidence and confidence in the generated report.

### 5.4 Identity and rewards

The source provider mod ID, asset namespace, resolved entity code and variant remain part of the stable adapted identity. Provider and asset namespace are stored separately because real packs do not always use the same value for both. Filenames are discovery inputs, not semantic identity. Display name, model identity and suitable sounds may be retained. VRPG owns faction, elemental tags, role, rank eligibility, reward family, codex data and encounter placement.

Automatic adaptation assigns a generic reward family appropriate to the selected region and threat cost. It never imports foreign drop tables, harvesting rules or item creation implicitly. A profile can target existing VRPG material and reward tags without granting direct inventory writes.

## 6. Data profile

A content pack or server pack can refine an adapted creature without code. Conceptually:

```json
{
  "source": "creaturemod:wolf-ashen",
  "actor": "vrpg-adapted:creaturemod/wolf-ashen",
  "bodyProfile": "ground-quadruped-medium",
  "roles": ["flanker", "pursuer"],
  "tags": ["beast", "fire", "pack"],
  "rankEligibility": ["standard", "elite"],
  "tactics": ["advance", "flank", "retreat-reset"],
  "abilities": ["vrpg:bite-telegraphed", "vrpg:ash-trail"],
  "animations": {
    "locomotion": "run",
    "primaryTelegraph": "bite-ready",
    "primaryCommit": "bite",
    "defeat": "die"
  },
  "rewardTags": ["region/fire", "creature/beast"],
  "threatCost": 3
}
```

Exact field names remain provisional. Definitions use namespaced IDs, tags, schema versions and registry validation like other VRPG content.

Profiles may compose existing tactics, abilities, status interactions and defeat responses. They cannot bypass Combat Simulation, Inventory, Reward, World Mutation or Encounter ownership. A genuinely new action registers a narrow reusable verb through the native addon API.

## 7. Encounter integration

An adapted actor is not inserted into every spawn table. Registration publishes capabilities and tags; Encounter Director selects it only when all requirements match:

- Supported realm and locomotion mode.
- Arena clearance, floor connectivity and navigation profile.
- Region, faction, element and narrative tags.
- Threat and complexity budget.
- Required animation and telegraph support.
- Party-size and density limits.
- Compatible squad roles and formation footprint.
- Allowed rank and modifier set.

Foreign natural-spawn behavior remains disabled. Main-world population and expedition composition are both VRPG-owned, so installing a large creature pack expands the candidate library without flooding or destabilizing a world.

## 8. Safety and degradation rules

- Never instantiate unknown foreign gameplay code merely to extract assets during live play.
- Never classify an installed DLL as safe merely because the selected synthetic actor omits its entity behaviors.
- Never copy source AI tasks, damage handlers, drops, breeding, hunger, taming or spawn logic into the normalized actor.
- Custom code mods remain capable of patching engine state globally; creature ingestion does not turn arbitrary assemblies into a sandbox.
- Missing source content disables future spawns. Active adapted actors resolve to a safe placeholder or are evacuated according to activity recovery policy.
- A changed source asset or profile cannot silently change persistent identity.
- Failed adaptation of one creature does not prevent unrelated valid creatures from registering unless strict pack policy requires atomic admission.
- Unsupported animations use declared fallbacks. Hit timing never depends on guessing an animation frame differently on each client.
- Client-only ragdoll or death presentation cannot determine authoritative collision, drops or aftermath.

## 9. Authoring and administration workflow

The compatibility report lists every discovered creature with:

- Source and generated actor IDs.
- Admission tier and reason.
- Selected body, locomotion, rank, role and baseline ability templates.
- Animation bindings and fallbacks.
- Encounter tags and exclusions.
- Validation warnings and required profile fields.
- Source/profile fingerprints and cache state.

Development tooling should be able to export the inferred definition as a starter profile. Authors then edit a small data file instead of reverse-engineering the ingestion process. A preview arena spawns the actor against standard movement, obstacle, temporary-wall, status and defeat scenarios.

## 10. Test contract

Each auto-adapted or profiled creature must pass:

1. Asset resolution without substituting an unrelated model or texture.
2. Server spawn and client synchronization.
3. Collision, step, slope and arena-clearance validation.
4. Idle, locomotion, telegraph, attack, hurt and defeat presentation or declared fallback.
5. VRPG stat assignment with no source health/damage leakage.
6. VRPG perception, tactics, navigation, anti-stuck and leash behavior.
7. Server-authoritative hit, status, defeat and aftermath handling.
8. Encounter threat-budget and density enforcement.
9. Reward creation through Reward and Inventory transactions only.
10. Save/load, unload/reload, reconnect and missing-source recovery.
11. Deterministic output for the same inputs and seed.
12. Bounded CPU, navigation, animation and network cost under representative group counts.

Pack-level tests include conflicting IDs, variants, missing assets, custom entity classes, source version changes and removal from a populated server.

The initial real-world fixture suite and expected classifications are defined in [Reference Creature Pack Audit](reference-creature-pack-audit.md). External archives remain path-supplied test inputs and are not vendored into VRPG.

## 11. Initial implementation slice

The first version should intentionally support the common case:

- Standard JSON-defined ground creatures using ordinary Vintage Story shapes, textures and animations.
- One biped and one quadruped body profile.
- Conservative melee-striker and ranged-attacker baselines.
- Explicit animation alias discovery plus profile overrides.
- Synthetic VRPG-owned entity registration.
- Encounter-only spawning in a validation arena.
- Generated reports and starter-profile export.

Flying, swimming, burrowing, multipart actors, mounts, tameable creatures, custom renderers and custom code-controlled bodies follow only after their movement and authority contracts exist.

## 12. Acceptance criteria

- Installing a compatible asset-defined creature pack can create combat-valid VRPG actors without writing C#.
- Every admitted actor uses VRPG AI, stats, combat, spawning, defeat and reward ownership.
- An ordinary creature can be made distinctive through data by composing existing roles, tactics and abilities.
- Foreign gameplay code is never treated as safe merely because its entity renders correctly.
- Unsupported creatures are excluded with actionable reasons instead of spawning in a broken or partially converted state.
- Adding creatures expands encounter variety without bypassing region identity, threat budgets or performance limits.
