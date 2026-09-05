# Reference Creature Pack Audit

Status: Initial static audit and adopted fixture plan  
Parent: [Creature ingestion and adaptation](creature-ingestion-and-adaptation.md)  
Index: [VRPG design index](README.md)

## 1. Purpose

These three real creature packs form the initial external fixture suite for the ingestion pipeline:

- [Eidolon Unchained](https://mods.vintagestory.at/show/mod/42530), local version 1.9.0.
- [The Monsterpack](https://mods.vintagestory.at/show/mod/55075), local version 1.1.10.
- [Fantasy Creatures Reborn](https://mods.vintagestory.at/show/mod/27638), local version 0.8.3.

The archives currently exist outside the repository. Tests must accept fixture paths through configuration or command-line arguments; they must not hard-code a user's Downloads directory. The archives and extracted copyrighted assets are not committed or redistributed without appropriate permission.

This is a static archive and assembly audit. It does not yet prove that generated actors register, synchronize or fight correctly in a live game.

Pinned inputs for repeatable local tests:

| Archive | SHA-256 |
|---|---|
| `EidolonUnchained-1.9.0v.zip` | `6eae03b3eff362753c2ee4dcc4ea287c247e10d000f2d689a076ee3f7e104888` |
| `monsterpack_1.1.10.zip` | `25a6c5b8c60703f812aba8ae8aa904c2a083e13d455fd292759f38fff9bf5586` |
| `fantasycreaturesreborn_0_8_3.zip` | `b9771542b146c8436840224fee00acf259bcdb637d3f15cb3ab5adb842767a41` |

A harness should fail with “fixture version changed” rather than silently updating a golden result when a hash differs.

## 2. Fixture matrix

| Pack | Packaging | Contents relevant to ingestion | Expected admission |
|---|---|---|---|
| Eidolon Unchained 1.9.0 | Code mod | One large Shape-rendered boss, custom obstacle-breaking task, built-in complex attack tasks, boss music | Assets discoverable; boss profile required; source code disabled/ignored |
| The Monsterpack 1.1.10 | Code mod | Eight standard `EntityAgent` definitions, Shape renderers, ordinary ground monsters, custom pack-leader behavior and spawn-config code | Most actors auto-baseline; leader identity needs profile; code-bearing source remains untrusted |
| Fantasy Creatures Reborn 0.8.3 | Content mod | Fourteen entity definition families with variants, humanoids, slime, treants, land/flying dragons, dialogue and growth relationships | Best initial positive fixture; ordinary ground actors auto-baseline; special cases require profile or deferred capability |

## 3. Eidolon Unchained

### Static findings

- Mod ID and asset domain are `unchained`; the source entity code is `unchained:unchained`.
- The entity uses a standard Shape renderer and ships its own humanoid Eidolon shape.
- The declared class is the game-side `EntityEidolon`, while the archive DLL registers a custom `breakobstacle` AI task.
- Source behavior includes melee, boulder throw, area slam, reinforcement summoning, pursuit, wandering and obstacle breaking.
- The custom obstacle task calls the low-level block accessor to break blocks after a delay.
- The entity has a large 1.5 by 3.5 body, 2,500 source weight, boss presentation and direct Survival harvest drops.

### VRPG decision

This is the advanced boss fixture, not the first automatic success case.

- Reuse the shape, textures, ordinary animation clips, sounds and optionally boss music.
- Ignore source health, weight-as-power, damage, drops, spawning and task priorities.
- Never load `breakobstacle` into a synthetic actor. It conflicts with protected-world mutation ownership.
- Recreate the readable concepts through VRPG operations: committed melee, telegraphed projectile, authored area attack, bounded reinforcement request and an explicit response to temporary terrain.
- Require an authored boss profile before encounter eligibility. A generic melee baseline may be useful in the preview arena but must not ship as the intended Eidolon fight.
- Test discovery of animation clips used directly by task names even when they are absent from the entity's client animation alias list.

### What it validates

- Code-bearing source classification.
- Safe extraction from a creature whose distinctive gameplay must not be inherited.
- Large-body navigation and arena-clearance rejection.
- Boss complexity and threat budgets.
- Protected terrain versus foreign direct block mutation.
- Mapping a recognizable source concept into original VRPG-owned behavior.

## 4. The Monsterpack

### Static findings

- Eight ground creature JSON files use `EntityAgent` and the standard Shape renderer.
- The archive contains custom code despite the mostly data-defined creatures.
- Its mod system registers `packleader`, scans loaded `monsterpack` entity types during `AssetsFinalize`, writes spawn configuration and mutates source spawn conditions.
- `EntityBehaviorPackLeader` periodically scans nearby entities, recruits up to fifteen zombies and directly notifies followers to seek or cancel targets.
- `zombiealpha` uses the custom leader behavior; the remaining inspected actors use ordinary task AI.
- The definitions consistently provide idle, locomotion, attack and death animation intent, although several contain misspelled optional property names.
- Two mushroom entity files contain `//` comments. They are valid Vintage Story content but not strict RFC JSON.
- One filename contains a trailing space before `.json`, demonstrating why filesystem names must not become semantic identity.

### VRPG decision

This pack supplies the first broad automatic ground-actor set.

- Auto-adapt the ordinary ground shapes into conservative melee actors.
- Use the entity code and resolved variant as identity, not the filename.
- Ignore all source spawn conditions and the DLL's spawn configuration.
- Ignore the live `packleader` behavior. A profile may translate `zombiealpha` into VRPG's squad-leader or coordinator role.
- Treat misspelled source fields as absent unless the Vintage Story loader has already resolved them into reliable properties.
- Parse through Vintage Story's asset/deserialization facilities or an equivalent compatible JSON reader; do not require strict JSON syntax.

### What it validates

- Batch discovery and conversion.
- Ordinary Shape-rendered humanoid monsters.
- Code-bearing pack quarantine versus asset eligibility.
- Known-behavior hints without behavior inheritance.
- Variant/identity independence from filenames.
- Lenient Vintage Story JSON handling and incomplete metadata fallbacks.
- Group performance and density limits.

## 5. Fantasy Creatures Reborn

### Static findings

- The archive declares itself as content-only and contains no DLL.
- It has fourteen entity-definition families and numerous resolved variants.
- Ordinary actors use `EntityAgent`, the standard Shape renderer and standard entity behaviors.
- The set includes skeletons, goblins, orks, slimes, treants, ground dragons, flying dragons and a conversational blue dragon.
- Several definitions use `shapeByType`, `texturesByType`, `aitasksByType`, health-by-type and growth relationships.
- Ground dragons declare a three-block step height and large bodies, which violates ordinary VRPG traversal assumptions.
- Flying dragons use Air habitat and lack the ordinary combat/defeat presentation expected of a ground enemy.
- The blue dragon uses conversation, name-tag and stay-near semantics, so assuming every discovered creature is a hostile enemy would be incorrect.
- The mod ID is `fantasycreaturesupdate`, while its asset domain is `fantasycreatures`. Provider identity and asset namespace therefore cannot be treated as the same field.

### VRPG decision

This is the primary safe-ingestion fixture.

- Auto-baseline conventional ground humanoids, slimes and treants after per-variant asset validation.
- Expand type variants deterministically and preserve source asset locations.
- Clamp or reject unsafe body/navigation values rather than copying a three-block step height.
- Require explicit profiles for ground dragons due to size, boss candidacy and multi-attack identity.
- Defer flying dragons until VRPG owns a flight-navigation and anti-cheese contract.
- Classify the blue dragon as non-encounter content until a profile explicitly chooses enemy, ally or NPC semantics.
- Ignore growth, dialogue, drops, equipment, recipes and natural spawn rules unless a separate VRPG domain admits them.

### What it validates

- Content-only positive admission.
- Variant expansion and `ByType` resolution.
- Provider ID versus asset-domain tracking.
- Diverse body plans using a common renderer.
- Non-hostile creature detection.
- Unsupported locomotion quarantine.
- Oversized navigation-property validation.
- Capability-scoped admission of creatures without admitting the pack's backpacks, armor, recipes or economy.

## 6. Source-code trust boundary

```text
CONTENT-ONLY ARCHIVE
        |
        +--> normal asset discovery --> validate --> synthetic VRPG actor

CODE-BEARING ARCHIVE
        |
        +--> assets may be analyzable
        +--> assembly is not sandboxed
        +--> strict support requires an audited allowlist or an asset-only import path
        `--> synthetic actor still never receives source gameplay behaviors
```

Not spawning a source entity does not stop its `ModSystem`, Harmony patches or global event handlers from running when Vintage Story loads the DLL. Therefore:

- Content-only packs are the safe default automatic source.
- A code-bearing creature mod installed normally remains an audited or operator-approved dependency, even if every creature is converted.
- The long-term importer may mount or compile an asset-only local cache without activating the source assembly, provided engine load timing and asset ownership allow it.
- VRPG must not redistribute extracted models, textures, sounds or altered source archives merely to make that workflow convenient.

## 7. Hint extraction

Source gameplay metadata may suggest a profile, but never becomes runtime authority.

| Recognized source hint | Suggested VRPG output | Confidence/action |
|---|---|---|
| Ground habitat + melee animation/task | Conservative melee striker | May auto-admit |
| Projectile/throw task + readable clip | Ranged candidate | Generate preview; profile confirms timings/projectile |
| Flee/evasion task | Skirmisher/evasive tactic candidate | Suggest only |
| Leader/group behavior | Squad-leader role candidate | Suggest only; never execute custom behavior |
| Boss behavior/tag/large body | Boss-capable profile | Profile required |
| Air habitat | Flight capability | Defer until supported |
| Conversable/stay-close/tame/grow | NPC, ally or lifecycle semantics | Do not auto-hostilize |
| Block breaking/direct mutation | Temporary-terrain counter candidate | Never import; explicit VRPG ability required |

All generated suggestions appear in the adaptation report with their evidence and confidence. The server pack may accept, replace or suppress them through data.

## 8. Test sequence

1. **Archive inventory:** discover mod metadata, provider, domains, entity definitions, shapes, textures, sounds and assemblies without executing foreign code.
2. **Static classification:** mark content-only versus code-bearing and list custom entity, renderer, behavior and task names.
3. **Descriptor extraction:** resolve every variant into presentation, body, locomotion and animation descriptors.
4. **Baseline generation:** generate definitions only for candidates meeting the current capability envelope.
5. **Golden report:** compare stable IDs, classifications, warnings and inferred profiles without storing copyrighted source content.
6. **Registry test:** load generated types on server and client and assert matching codes/IDs/fingerprints.
7. **Preview arena:** test locomotion, melee, ranged, obstacles, slopes, temporary terrain, defeat and rewards.
8. **Negative cases:** prove flight, dialogue ambiguity, unsafe code, giant step height and direct mutation are rejected or deferred.
9. **Batch/performance:** spawn representative Monsterpack groups and mixed Fantasy Creatures squads under navigation and network budgets.
10. **Source change/removal:** change a fixture version, preview the compatibility delta and recover active instances without identity substitution.

## 9. Initial expected results

Before live prototyping, the static expected result is:

```text
Fantasy Creatures ordinary ground actors ---> AUTO-BASELINE CANDIDATES
Monsterpack ordinary ground actors --------> AUTO-BASELINE CANDIDATES, CODE SOURCE UNTRUSTED
Monsterpack zombie alpha -------------------> PROFILED LEADER CANDIDATE
Fantasy ground dragons ---------------------> PROFILE REQUIRED
Fantasy flying dragons ---------------------> DEFERRED: FLIGHT UNSUPPORTED
Fantasy blue dragon ------------------------> DEFERRED: ALLY/NPC INTENT AMBIGUOUS
Eidolon ------------------------------------> PROFILED BOSS; CUSTOM BLOCK BREAK REJECTED
```
