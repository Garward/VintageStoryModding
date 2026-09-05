# Mod Compatibility and Addon Contract

Status: Adopted compatibility scope; exact manifest format remains provisional  
Parent: [Complete game design](complete-game-design.md)  
Index: [VRPG design index](README.md)

## 1. Position

VRPG is a total conversion that happens to use Vintage Story as its engine and asset substrate. General compatibility with Survival gameplay mods is not a design goal.

A mod that assumes Vanilla world generation, mining, hunger, backpacks, classes, combat, crafting, recipes, creature spawning, block mutation, or progression will usually be incompatible by definition. VRPG should report that honestly instead of accumulating fragile patches that weaken its own rules.

Compatibility work is justified when it supports:

- A VRPG-native addon using documented definitions and services.
- Automatic ingestion of compatible creature presentation assets into VRPG-owned actors.
- Safe decorative or functional content inside the player Home.
- A narrow engine, administration, accessibility, or presentation tool that does not claim gameplay ownership.
- Lossless preservation of content when a previously supported addon is temporarily missing.

## 2. Compatibility tiers

| Tier | Scope | Support promise |
|---|---|---|
| **VRPG native** | Addons built against VRPG definitions, capabilities, events and services | Supported within declared API versions and validation rules |
| **Creature content** | Compatible models, textures, animations, sounds and identity imported into a synthetic VRPG-owned actor | Automatically adapted when validation passes; source gameplay is not preserved |
| **Home content** | Allowlisted blocks, decorations, furniture and bounded stations used only in authorized housing | Supported only for audited interactions and housing persistence |
| **Passive utility** | Selected client presentation, server administration or engine utilities | Case-by-case; must not mutate VRPG-owned gameplay state |
| **Foreign gameplay** | Survival progression, worldgen, classes, inventory, combat, crafting, mobs or needs | Unsupported by default; rejected or allowed at the operator's risk |
| **Invasive/unknown** | Arbitrary Harmony patches, direct block writes, entity rewrites or save mutation | Cannot be safely sandboxed; no compatibility guarantee |

“Loads without crashing” is not compatibility. A supported combination must preserve VRPG authority, progression, persistence, multiplayer truth and player-facing rules.

## 3. Strict default

The `vrpg-default` preset uses strict compatibility.

- Known conflicting mods produce a startup error naming the conflicting owner and required action.
- Unknown gameplay mods produce a prominent server warning and mark the world unsupported unless explicitly allowed by the operator.
- Foreign world generators, Survival classes, player needs, inventory replacements, combat overhauls, recipe progression and creature-spawn systems are not merged automatically. Compatible creature assets may be ingested through the dedicated VRPG actor pipeline without retaining the source spawn system.
- The runtime profile publishes one owner for each major capability.
- The server records the effective mod set and compatibility decisions with the save.
- Joining clients must satisfy the server's required gameplay and content set before entering the world.

A permissive mode may exist for experimentation, but it removes compatibility guarantees and must not silently become the default because one unknown mod was detected.

## 4. Main-world boundary

Foreign content does not enter the protected main world merely because its assets loaded.

- No foreign worldgen, deposits, structures, crops, ore, loot, recipes, spawns or traders are eligible unless adapted through a VRPG definition.
- Foreign blocks are not placeable in the main world under protected mutation.
- Foreign weapons, armor, food and tools do not gain progression value until a VRPG storage, combat, equipment or consumable policy explicitly admits them.
- Compatible foreign creature assets may be converted into synthetic VRPG-owned actors. VRPG supplies their archetype, role, scaling, loot, AI and spawning before encounter selection; see [Creature ingestion and adaptation](creature-ingestion-and-adaptation.md).
- Direct code from another mod can still call low-level engine mutation APIs. VRPG cannot prove that arbitrary code is safe; such a mod remains unsupported even if ordinary player placement is blocked.

## 5. Home-content boundary

The player Home is the primary compatibility surface for reusable building content.

A Home-content adapter declares:

- Allowed block, item and entity codes or tags.
- Whether content is decorative, storage, seating, lighting, door, station, mechanical or another explicit role.
- Placement, removal, refund and ownership rules.
- Required block-entity ticking and unload behavior.
- Inventory/storage bridges and quantity limits.
- Recipes or material conversions expressed through VRPG transactions.
- Network and permission behavior.
- Migration behavior when the addon is removed or updated.

Default Home support is conservative:

- Decorative blocks with ordinary persistence are the easiest category.
- Containers use VRPG-approved storage bridges and permissions.
- Stations need an explicit service adapter; a Survival recipe GUI is not admitted automatically.
- Mechanical networks, animals, crops, liquids, fire, automation and time-based production require dedicated audits.
- A Home block cannot grant main-world mutation, Survival progression, unrestricted item creation, or remote combat advantage merely because it functions in Survival.

Home containment is a cooperation contract, not a security sandbox for arbitrary .NET code. Content that bypasses `IBuildAccessService`, `IPlayerItemStore`, or other owners is unsupported.

## 6. VRPG-native addon contract

A native addon declares:

- Addon ID and semantic version.
- Required VRPG API range.
- Required and optional capabilities.
- Definition namespaces and code registrations.
- New code implementations and their stable identifiers.
- Data migrations and downgrade policy.
- Client assets or code required by joining players.
- Content dependencies, conflicts and load order where unavoidable.

Ordinary content uses data definitions. New verbs register narrow implementations behind interfaces. Addons do not Harmony-patch VRPG internals when a public service or event exists; if an extension point is missing, the API should be extended deliberately.

Override rules are explicit:

- Namespaced codes prevent accidental replacement.
- Patches or replacements identify their target and expected version.
- The registry reports the final provider of every replaced definition.
- Conflicting replacements fail validation instead of depending on filesystem order.

## 7. Missing and removed content

- Unknown item and block records are preserved with original namespace, serialized attributes and owner addon where feasible.
- Unresolved inventory and storage entries remain visible in a recovery/details surface but cannot be equipped, sold, crafted or duplicated.
- Missing housing blocks use a recoverable placeholder or freeze the affected home region; they do not become air without a migration.
- Missing enemy, region or expedition definitions disable new selection and preserve active state for recovery.
- Removing an addon requires a preview of affected players, homes, storage, world sites, loadouts and progression records.
- VRPG never guesses that a similarly named asset from another addon is the same persistent identity.

## 8. Configuration

Conceptual server settings:

```json
{
  "Compatibility": {
    "Mode": "Strict",
    "AllowedGameplayMods": [],
    "CreatureIngestion": "CompatibleAssets",
    "CreaturePackOverrides": [],
    "AllowedHomeContent": [],
    "UnknownModPolicy": "WarnAndMarkUnsupported"
  }
}
```

The exact format should support server-pack supplied allowlists and adapter registrations. Client preferences cannot widen server compatibility or gameplay ownership.

Changing the gameplay mod set, native addons, creature sources/profiles, or Home content on a populated world triggers a compatibility preview and may require backup or migration. Presentation-only utilities may change without world migration when verified safe.

## 9. Player-facing handling

Ordinary players should see actionable outcomes:

- **This world requires additional VRPG content.** followed by the missing addon names.
- **This item belongs to unavailable content. It is preserved but cannot be used.**
- **This block is not approved for Home.**
- **This server is running an unsupported mod combination.** on the server-details/join surface, not repeated during play.

Internal type names, stack traces, raw capability IDs and load-order diagnostics remain in logs and administrator reports.

## 10. Tests

- A VRPG-native data addon loads, validates, participates in registries and survives save/load.
- Two addons attempting the same definition replacement fail with both providers named.
- An allowlisted decorative block can be placed and removed in Home but not in the main world.
- A non-allowlisted block receives a clear denial and cannot enter through bulk build actions.
- A Home station cannot bypass inventory transactions or create duplicate output during concurrent use.
- Removing a content addon preserves its inventory entries and housing blocks through recoverable unresolved records.
- A known conflicting Survival gameplay mod prevents strict startup before world mutation.
- An unknown gameplay mod in permissive mode marks the profile unsupported and appears in the administrator report.
- A direct-mutation test adapter proves why arbitrary foreign code is outside the support boundary.
- Multiplayer join rejects missing required native content before the player receives mutable state.
- An asset-defined ground creature is converted into a synthetic VRPG actor without inheriting source AI, stats, drops or spawning.
- A foreign creature with an unsafe custom runtime dependency is quarantined until an explicit adapter is supplied.
- Updating or removing a source creature pack produces a compatibility preview and recoverable active-instance handling.

## 11. Explicit non-goals

- Universal compatibility with Vintage Story Survival mods.
- Automatically translating arbitrary recipes, classes, traits, worldgen, machines or creature-specific code into VRPG.
- Reproducing a foreign creature's original balance or special mechanics without a VRPG profile or adapter.
- Guaranteeing safety for foreign Harmony patches or direct engine writes.
- Maintaining balance when operators admit unsupported weapons, armor, items or progression.
- Spending core development time on compatibility profiles that compromise the default VRPG experience.

## 12. Open decisions

- Manifest schema and whether it extends `modinfo.json` or uses a VRPG-specific file.
- Detection policy for unknown code mods versus asset-only packs.
- Whether unsupported strict combinations fail startup or disable VRPG world loading while leaving menus available.
- Placeholder and frozen-region strategy for missing Home blocks.
- Minimum API compatibility window and deprecation policy.
- Which administration, map and accessibility utilities receive initial passive-utility certification.
