# VRPG Gear Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the permanent server-authored VRPG gear format, deterministic level-one starter sets, authoritative equipment effects, and finalized defensive resolver needed before onboarding can grant real equipment.

**Architecture:** Gear mechanics live in a focused `VRPG.Inventory.Gear` subsystem. Data definitions describe bases, rarities, and affix tiers; one authoring boundary resolves those definitions into immutable instances embedded in the canonical item state's `vrpgGear` tree. A server equipment service reads equipped canonical entries, supplies Weapon Power and defensive/resource modifiers to RPG services, and owns the incoming-damage pipeline, while Vintage Story collectibles remain visual/materialization chassis only.

**Tech Stack:** C# 12, .NET 10, xUnit 2.9, Vintage Story 1.22 server API, Newtonsoft.Json-backed data loading, Vintage Story `TreeAttribute` item state, Harmony damage interception.

**Spec:** `VRPG/docs/superpowers/specs/2026-09-05-vrpg-gear-foundation-design.md`

## Global Constraints

- Required level is the only item scaling level and is permanently stored from 1 through 100; do not add item level, target-level staleness, or gear score.
- Gear instances store schema version, generation-rule version, base code, rarity, exact affix rolls, resolved combat values, provenance, unique rule data, and a separate visual chassis reference.
- Existing instances never reroll or recalculate after a balance-data update.
- Weapon Power uses `10 × 1.035^(required level - 1)`, then Flat, summed Additional, multiplicative More, and rarity scalars Common `1.00`, Rare `1.15`, Unique `1.20` in that order.
- Armor base rating scales at 8% per required level; attack pressure starts at 150 and scales at 8% per attacker level.
- Armor uses `Armor / (Armor + pressure)`, treats 90% as the practical soft ceiling, and clamps at the absolute 99% physical-mitigation hard cap.
- Elemental resistance hard-caps at 75% per element. Good current-level late-game gear must be able to cap every ordinary element while maximizing one primary defense.
- Eligible-hit Evasion hard-caps at 90%; Accuracy lowers effective Evasion. Damage over time, persistent hazards, and explicit unavoidable mechanics do not roll Evasion.
- Evasive Step is automatic, Dexterity-primary, hotbar-free, and triggers only when post-mitigation, post-shield Health damage would otherwise be lethal. Its seeds are 6 seconds cooldown, 0.38 seconds invulnerability, and 0.28 escape impulse.
- The late-game encounter-population damage budget targets approximately 50% physical and 50% elemental output; individual enemies need not deal both.
- Each selectable starter receives one level-one Common weapon plus head, body, legs, hands, and feet. Starter pieces have no affixes, binding, set bonus, or special economic rules.
- This plan must not implement random base/rarity/affix selection, enemy drops, reward routing, the onboarding commit, or any other normal acquisition path.
- Missing mechanical definitions create explicit rejection/recovery; a missing chassis uses a runtime fallback for materialization without rewriting the stored chassis code or deleting the mechanical instance.
- Clients never author seeds, rolls, requirements, resolved stats, Evasion outcomes, or incoming-damage results.

## File Structure

### New production files

- `VRPG/src/Inventory/Gear/GearInstanceModels.cs` — immutable permanent instance, roll, resolved-stat, provenance, and decode-result contracts.
- `VRPG/src/Inventory/Gear/GearItemStateCodec.cs` — versioned `vrpgGear` tree encoding/decoding inside canonical item state.
- `VRPG/src/Data/Definitions/GearBaseDefinition.cs` — data-owned mechanical identity and visual chassis schema.
- `VRPG/src/Data/GearDefinitionValidator.cs` — cross-record base, rarity, affix-tier, role, tag, and chassis validation.
- `VRPG/src/Inventory/Gear/GearScalingMath.cs` — pure Weapon Power, Armor, resistance, and Evasion formulas.
- `VRPG/src/Inventory/Gear/GearInstanceAuthor.cs` — exact request-to-instance boundary shared by starters and future random loot.
- `VRPG/src/Inventory/Gear/GearPrototypeFactory.cs` — canonical `PlayerItemPrototype` creation from a validated instance.
- `VRPG/src/Inventory/Gear/StarterGearSetFactory.cs` — deterministic class starter-set authoring without committing it.
- `VRPG/src/Inventory/Gear/GearEquipmentService.cs` — validated equip gateway and equipped-instance snapshot.
- `VRPG/src/Modules/Rpg/Stats/EquippedGearStatResolver.cs` — aggregate equipped resolved values and modifiers once per player/revision.
- `VRPG/src/Modules/Rpg/Combat/PlayerDefenseMath.cs` — pure ordered defense calculation.
- `VRPG/src/Modules/Rpg/Combat/PlayerDefenseService.cs` — server adapter for Evasion rolls, shield spending, Evasive Step, and final Health damage.
- `VRPG/src/Modules/Rpg/Combat/PlayerWeaponDamageService.cs` — replaces eligible RPG basic attacks with equipped Weapon Power while preserving marked skill hits.
- `VRPG/src/Modules/Rpg/Combat/RpgDamageSourceTagger.cs` — server-only weak tagging that distinguishes authored skill hits from engine basic attacks.
- `VRPG/src/Inventory/Gear/GearReadinessValidator.cs` — persisted gear validation, fallback-chassis repair, and unresolved-item recovery before readiness.

### New test files

- `VRPG/tests/VRPG.Tests/GearItemStateCodecTests.cs`
- `VRPG/tests/VRPG.Tests/GearDefinitionValidatorTests.cs`
- `VRPG/tests/VRPG.Tests/GearScalingMathTests.cs`
- `VRPG/tests/VRPG.Tests/GearInstanceAuthorTests.cs`
- `VRPG/tests/VRPG.Tests/StarterGearSetFactoryTests.cs`
- `VRPG/tests/VRPG.Tests/GearEquipmentServiceTests.cs`
- `VRPG/tests/VRPG.Tests/EquippedGearStatResolverTests.cs`
- `VRPG/tests/VRPG.Tests/PlayerDefenseMathTests.cs`
- `VRPG/tests/VRPG.Tests/PlayerDefenseServiceTests.cs`
- `VRPG/tests/VRPG.Tests/PlayerWeaponDamageServiceTests.cs`
- `VRPG/tests/VRPG.Tests/GearReadinessValidatorTests.cs`

### Modified production/data files

- `VRPG/src/Inventory/ItemStackCodec.cs` — expose safe canonical attribute-state encode/decode helpers.
- `VRPG/src/Inventory/PlayerItemModels.cs`, `IPlayerItemStore.cs`, and `InMemoryPlayerItemStore.cs` — add cloned internal item-detail lookup.
- `VRPG/src/Data/Definitions/GearAffixDefinition.cs` and `GearRarityDefinition.cs` — permanent tier/compatibility metadata and validation inputs.
- `VRPG/src/Data/Definitions/ClassDefinition.cs` and `StarterLoadoutDefinitionValidator.cs` — starter entries reference gear-base codes and require all six roles.
- `VRPG/src/Data/VRPGDataRegistry.cs` and `VRPG/src/Data/Library/GearLibrarySource.cs` — load and describe gear bases.
- `VRPG/src/Modules/Rpg/Stats/SkillDamageResolver.cs` and `VRPG/src/Modules/Rpg/Skills/SkillCastingService.cs` — use equipped Weapon Power and gear modifiers.
- `VRPG/src/Modules/Rpg/Players/RpgResourceCalculator.cs` and `RpgResourceService.cs` — include equipped Health/Magic Shield modifiers and expose shield absorption.
- `VRPG/src/Modules/Rpg/Combat/EvasiveStepService.cs` — separate active i-frame check from post-shield lethal activation.
- `VRPG/src/Patches/EntityDamageScalingPatch.cs`, `VRPG/src/Modules/Rpg/RpgModule.cs`, and `VRPG/src/VRPGModSystem.cs` — wire the authoritative gear/damage services to the player item directory.
- `VRPG/assets/vrpg/vrpg/affixes/*.json`, `rarities/*.json`, `classes/*.json`, and new `gear/*.json` — validated foundation content and complete starter references.
- `VRPG/docs/design/testable-version-todo.md` — mark only proven gear-foundation checklist items complete.

---

### Task 1: Permanent gear instance and canonical state codec

**Files:**
- Create: `VRPG/src/Inventory/Gear/GearInstanceModels.cs`
- Create: `VRPG/src/Inventory/Gear/GearItemStateCodec.cs`
- Modify: `VRPG/src/Inventory/ItemStackCodec.cs`
- Test: `VRPG/tests/VRPG.Tests/GearItemStateCodecTests.cs`

**Interfaces:**
- Produces: `GearInstance`, `GearAffixRoll`, `GearResolvedStats`, `GearProvenance`, `GearInstanceSchema.CurrentVersion`, `GearItemStateCodec.Encode(GearInstance)`, and `GearItemStateCodec.TryDecode(byte[], out GearInstance?, out string)`.
- Consumes: Vintage Story `TreeAttribute`; no data registry or world access.

- [ ] **Step 1: Write failing round-trip, future-version, and malformed-state tests**

```csharp
[Fact]
public void ExactInstanceRoundTripsWithoutRecalculation()
{
    GearInstance expected = new(
        GearInstanceSchema.CurrentVersion,
        GearInstanceSchema.CurrentGenerationRules,
        "vrpg:warden-body",
        80,
        "vrpg:rare",
        PlayerEquipmentRoles.Body,
        "game:armor-body-chain-copper",
        new[]
        {
            new GearAffixRoll("vrpg:stalwart", 4, new[]
            {
                new GearModifierRoll("vrpg:armor", "increased", 7.375f)
            })
        },
        new GearResolvedStats(0, 215f, 40f, 0, 0.05f, 0.25f, 0.25f, 0.25f, 0.25f),
        new GearProvenance("test", "codec", new[] { "roundtrip" }, 42UL),
        "vrpg:bulwark-rule",
        new byte[] { 9, 4, 1 });

    byte[] state = GearItemStateCodec.Encode(expected);
    bool decoded = GearItemStateCodec.TryDecode(state, out GearInstance? actual, out string error);

    Assert.True(decoded, error);
    Assert.NotNull(actual);
    Assert.Equal(expected.BaseCode, actual!.BaseCode);
    Assert.Equal(expected.RequiredLevel, actual.RequiredLevel);
    Assert.Equal(expected.Resolved, actual.Resolved);
    Assert.Equal(expected.Affixes, actual.Affixes);
    Assert.Equal(expected.Provenance.Tags, actual.Provenance.Tags);
    Assert.Equal(expected.UniqueRulePayload, actual.UniqueRulePayload);
    Assert.Equal(7.375f, actual!.Affixes[0].Modifiers[0].Value);
}

[Fact]
public void FutureSchemaAndNonFiniteStatsAreRejected()
{
    var root = new TreeAttribute();
    var gear = new TreeAttribute();
    gear.SetInt("schemaVersion", GearInstanceSchema.CurrentVersion + 1);
    root["vrpgGear"] = gear;
    byte[] future = ItemStackCodec.EncodeCanonicalState(root);
    Assert.False(GearItemStateCodec.TryDecode(future, out _, out string futureError));
    Assert.Contains("Unsupported gear schema", futureError);

    GearInstance expectedFixture = new(
        GearInstanceSchema.CurrentVersion,
        GearInstanceSchema.CurrentGenerationRules,
        "vrpg:test-body",
        1,
        "vrpg:common",
        PlayerEquipmentRoles.Body,
        "game:armor-body-chain-copper",
        Array.Empty<GearAffixRoll>(),
        new GearResolvedStats(0, 100, 0, 0, 0, 0, 0, 0, 0),
        new GearProvenance("test", "invalid", Array.Empty<string>(), 0UL),
        "",
        Array.Empty<byte>());
    GearInstance invalid = expectedFixture with
    {
        Resolved = expectedFixture.Resolved with { Armor = float.NaN }
    };
    Assert.Throws<ArgumentException>(() => GearItemStateCodec.Encode(invalid));
}
```

Do not introduce a test-only codec that can produce state the production codec cannot read.

- [ ] **Step 2: Run the codec tests and verify the missing types fail compilation**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~GearItemStateCodecTests`

Expected: FAIL with missing `GearInstance`/`GearItemStateCodec` symbols.

- [ ] **Step 3: Add immutable instance contracts and extract reusable attribute serialization**

```csharp
public static class GearInstanceSchema
{
    public const int CurrentVersion = 1;
    public const int CurrentGenerationRules = 1;
}

public sealed record GearInstance(
    int SchemaVersion,
    int GenerationRuleVersion,
    string BaseCode,
    int RequiredLevel,
    string RarityCode,
    string EquipmentRole,
    string VisualChassisAssetCode,
    GearAffixRoll[] Affixes,
    GearResolvedStats Resolved,
    GearProvenance Provenance,
    string UniqueCode,
    byte[] UniqueRulePayload);

public sealed record GearAffixRoll(string AffixCode, int Tier, GearModifierRoll[] Modifiers);
public sealed record GearModifierRoll(string Stat, string Operation, float Value);

public sealed record GearResolvedStats(
    float WeaponPower,
    float Armor,
    float MaximumHealth,
    float MaximumMagicShield,
    float Evasion,
    float FireResistance,
    float ColdResistance,
    float LightningResistance,
    float RustResistance);

public sealed record GearProvenance(
    string SourceKind,
    string SourceCode,
    string[] Tags,
    ulong SelectionSeed);
```

Move `ItemStackCodec.WriteAttributes` and `ReadAttributes` into public, clone-safe methods named `EncodeCanonicalState(ITreeAttribute)` and `DecodeCanonicalState(byte[])`. Preserve the existing trailing-byte rejection and make existing `Encode`/`Materialize` call those methods.

- [ ] **Step 4: Implement the versioned `vrpgGear` tree codec**

```csharp
public static byte[] Encode(GearInstance instance)
{
    ValidateForWrite(instance);
    var root = new TreeAttribute();
    var gear = new TreeAttribute();
    gear.SetInt("schemaVersion", instance.SchemaVersion);
    gear.SetInt("generationRuleVersion", instance.GenerationRuleVersion);
    gear.SetString("baseCode", instance.BaseCode);
    gear.SetInt("requiredLevel", instance.RequiredLevel);
    gear.SetString("rarityCode", instance.RarityCode);
    gear.SetString("equipmentRole", instance.EquipmentRole);
    gear.SetString("visualChassis", instance.VisualChassisAssetCode);
    WriteResolved(gear, instance.Resolved);
    WriteAffixes(gear, instance.Affixes);
    WriteProvenance(gear, instance.Provenance);
    gear.SetString("uniqueCode", instance.UniqueCode);
    gear.SetBytes("uniqueRulePayload", (byte[])instance.UniqueRulePayload.Clone());
    root["vrpgGear"] = gear;
    return ItemStackCodec.EncodeCanonicalState(root);
}
```

Decode must require the `vrpgGear` subtree, validate level `1..100`, finite non-negative resolved numbers, normalized nonempty codes/role/chassis, unique affix identities, positive tiers, supported operations, and exact array/payload clones. It must reject future schema versions and return an error rather than throwing for persisted malformed input.

- [ ] **Step 5: Run codec and existing item-stack tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearItemStateCodecTests|FullyQualifiedName~ItemStackCodecTests|FullyQualifiedName~ItemStackCodecContentFixtureTests"`

Expected: PASS with zero failures.

- [ ] **Step 6: Commit the codec boundary**

```bash
git add VRPG/src/Inventory/Gear/GearInstanceModels.cs VRPG/src/Inventory/Gear/GearItemStateCodec.cs VRPG/src/Inventory/ItemStackCodec.cs VRPG/tests/VRPG.Tests/GearItemStateCodecTests.cs
git commit -m "Add permanent VRPG gear instance codec"
```

### Task 2: Gear base, rarity, and affix definition validation

**Files:**
- Create: `VRPG/src/Data/Definitions/GearBaseDefinition.cs`
- Create: `VRPG/src/Data/GearDefinitionValidator.cs`
- Modify: `VRPG/src/Data/Definitions/GearAffixDefinition.cs`
- Modify: `VRPG/src/Data/Definitions/GearRarityDefinition.cs`
- Modify: `VRPG/src/Data/VRPGDataRegistry.cs`
- Modify: `VRPG/src/Data/Library/GearLibrarySource.cs`
- Modify: `VRPG/assets/vrpg/vrpg/affixes/heavy_handed.json`
- Modify: `VRPG/assets/vrpg/vrpg/affixes/stalwart.json`
- Test: `VRPG/tests/VRPG.Tests/GearDefinitionValidatorTests.cs`

**Interfaces:**
- Produces: `VRPGDataRegistry.GearBases`, `GearBaseDefinition`, `GearAffixTierDefinition`, and `GearDefinitionValidator.ValidateOrThrow(...)`.
- Consumes: built-in `PlayerEquipmentRoles`, loaded rarities/affixes, and a `Func<string, bool>` chassis lookup.

- [ ] **Step 1: Write failing cross-definition validation tests**

```csharp
[Fact]
public void RejectsUnknownRoleMissingChassisAndOverlappingAffixTiers()
{
    var gear = new GearBaseDefinition
    {
        Code = "vrpg:test-body",
        Name = "Test Body",
        EquipmentRole = "armor.elbow",
        Family = "armor",
        Tags = new[] { "armor" },
        AffixFamilies = new[] { "defense" },
        VisualChassis = "missing:coat",
        LevelOneArmor = 35
    };
    var affix = new GearAffixDefinition
    {
        Code = "vrpg:test-defense",
        Name = "Test Defense",
        Family = "defense",
        RequiredTags = new[] { "armor" },
        MinimumRarityCode = "vrpg:common",
        Tiers = new[]
        {
            new GearAffixTierDefinition { Tier = 1, MinimumRequiredLevel = 1, MaximumRequiredLevel = 20, Modifiers = new[] { new StatModifierDefinition { Stat = "vrpg:armor", Min = 1, Max = 2, Operation = "add" } } },
            new GearAffixTierDefinition { Tier = 2, MinimumRequiredLevel = 20, MaximumRequiredLevel = 40, Modifiers = new[] { new StatModifierDefinition { Stat = "vrpg:armor", Min = 3, Max = 4, Operation = "add" } } }
        }
    };

    var common = new GearRarityDefinition
    {
        Code = "vrpg:common", Name = "Common", MinAffixes = 0, MaxAffixes = 1,
        StatScalar = 1f, WeaponPowerScalar = 1f
    };

    string[] errors = GearDefinitionValidator.Validate(
        new[] { gear }, new[] { common }, new[] { affix }, _ => false);

    Assert.Contains(errors, value => value.Contains("equipment role"));
    Assert.Contains(errors, value => value.Contains("visual chassis"));
    Assert.Contains(errors, value => value.Contains("overlap"));
}
```

Also cover duplicate codes, invalid rarity scalar/cap ranges, affix family mismatch, duplicate/missing exclusion-group values, unsupported operations, nonfinite tier rolls, Unique without authored identity support, and an all-valid fixture.

- [ ] **Step 2: Run the validator tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~GearDefinitionValidatorTests`

Expected: FAIL because the new definition and validator types do not exist.

- [ ] **Step 3: Add data contracts with one responsibility per record**

```csharp
public sealed class GearBaseDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string EquipmentRole { get; set; } = "";
    public string Family { get; set; } = "";
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string[] AffixFamilies { get; set; } = Array.Empty<string>();
    public string VisualChassis { get; set; } = "";
    public float LevelOneWeaponPower { get; set; }
    public float LevelOneArmor { get; set; }
    public StatModifierDefinition[] Implicits { get; set; } = Array.Empty<StatModifierDefinition>();
    public string UniqueCode { get; set; } = "";
}

public sealed class GearAffixTierDefinition
{
    public int Tier { get; set; }
    public int MinimumRequiredLevel { get; set; } = 1;
    public int MaximumRequiredLevel { get; set; } = 100;
    public StatModifierDefinition[] Modifiers { get; set; } = Array.Empty<StatModifierDefinition>();
}
```

Replace the affix's single `SlotTag`/`Modifiers` shape with `Family`, `RequiredTags`, `ExcludedTags`, `MutualExclusionGroup`, `UniquePerItem`, and `Tiers`. Keep rarity eligibility as `MinimumRarityCode`. Update the two existing affix JSON files to tier 1 records using their current values so loading never sees two schemas.

- [ ] **Step 4: Implement validation and registry loading**

```csharp
public VrpgDataStore<GearBaseDefinition> GearBases { get; } =
    new("vrpg/gear/");

// In Load, after all three stores are loaded:
GearDefinitionValidator.ValidateOrThrow(
    GearBases.All,
    Rarities.All,
    Affixes.All,
    code => api.World.GetItem(new AssetLocation(code)) != null
        || api.World.GetBlock(new AssetLocation(code)) != null);
```

Validation must collect every error before throwing, normalize domainless codes to `vrpg:`, require built-in equipment roles, enforce Weapon Power only on weapon roles and Armor only on armor roles, require exactly Common/Rare/Unique seed scalars `1.00/1.15/1.20`, and ensure affix tiers are ordered and non-overlapping across levels 1..100.

- [ ] **Step 5: Update Library output and run definition/library tests**

Add base entries under `gear/bases` showing role, family, chassis, and base profile. Update affix presentation to enumerate tiers rather than removed root modifiers.

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearDefinitionValidatorTests|FullyQualifiedName~Library"`

Expected: PASS with zero failures.

- [ ] **Step 6: Commit the data schema**

```bash
git add VRPG/src/Data/Definitions/GearBaseDefinition.cs VRPG/src/Data/Definitions/GearAffixDefinition.cs VRPG/src/Data/Definitions/GearRarityDefinition.cs VRPG/src/Data/GearDefinitionValidator.cs VRPG/src/Data/VRPGDataRegistry.cs VRPG/src/Data/Library/GearLibrarySource.cs VRPG/assets/vrpg/vrpg/affixes VRPG/tests/VRPG.Tests/GearDefinitionValidatorTests.cs
git commit -m "Define and validate VRPG gear bases"
```

### Task 3: Pure Weapon Power and defensive scaling math

**Files:**
- Create: `VRPG/src/Inventory/Gear/GearScalingMath.cs`
- Test: `VRPG/tests/VRPG.Tests/GearScalingMathTests.cs`
- Modify: `VRPG/src/Modules/Rpg/Scaling/ScalingMath.cs`
- Modify: `VRPG/tests/VRPG.Tests/ScalingMathTests.cs`

**Interfaces:**
- Produces: `WeaponLevelBase`, `ResolveWeaponPower`, `ArmorLevelBase`, `AttackPressure`, `ArmorMitigation`, `EffectiveElementalResistance`, and `EffectiveEvasion`.
- Consumes: plain numeric inputs only.

- [ ] **Step 1: Write failing formula boundary tests**

```csharp
[Theory]
[InlineData(1, 10.0)]
[InlineData(20, 19.23)]
[InlineData(40, 38.25)]
[InlineData(60, 76.12)]
[InlineData(80, 151.46)]
[InlineData(100, 301.37)]
public void WeaponBaseUsesPermanentRequiredLevel(int level, double expected)
{
    Assert.Equal(expected, GearScalingMath.WeaponLevelBase(level), 2);
}

[Theory]
[InlineData(9, 0.90)]
[InlineData(19, 0.95)]
[InlineData(32.333333, 0.97)]
[InlineData(99, 0.99)]
[InlineData(999, 0.99)]
public void ArmorHasNaturalDiminishingReturnsAndHardCap(double pressureMultiple, double expected)
{
    Assert.Equal(expected, GearScalingMath.ArmorMitigation(pressureMultiple * 150, 150), 2);
}

[Fact]
public void ResistanceAndEffectiveEvasionUseIndependentHardCaps()
{
    Assert.Equal(0.75, GearScalingMath.ClampElementalResistance(2.0), 3);
    Assert.Equal(0.50, GearScalingMath.EffectiveElementalResistance(0.75, 0.15, 0.10), 3);
    Assert.Equal(0.70, GearScalingMath.EffectiveEvasion(0.90, 0.20), 3);
    Assert.Equal(0.90, GearScalingMath.EffectiveEvasion(2.0, 0.0), 3);
}
```

Add tests for level clamping, `100 + 20 flat, 50 Additional, 25 More, 1.15 Rare = 258.75`, nonnegative results, level-one Armor `100`, level-one pressure `150`, matched 8% growth staying at 40% mitigation, and nonfinite input rejection.

Add a level-100 authored-budget fixture whose five armor pieces sum to `0.75` Fire, Cold, Lightning, and Rust resistance while its chosen primary defense reaches its cap/target. This proves late-game all-resistance capping is feasible without making that fixture obtainable. Add a second Armor fixture at `9×`, `19×`, `32.333333×`, and `99×` pressure to preserve the diminishing-return tail required by future leech builds; this slice does not implement life leech itself.

- [ ] **Step 2: Run the math tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearScalingMathTests|FullyQualifiedName~ScalingMathTests"`

Expected: FAIL with missing `GearScalingMath`.

- [ ] **Step 3: Implement the pure formulas and delegate legacy Weapon Power helpers**

```csharp
public static double WeaponLevelBase(int requiredLevel, double levelOne = 10.0) =>
    PositiveFinite(levelOne) * Math.Pow(1.035, Math.Clamp(requiredLevel, 1, 100) - 1);

public static double ArmorLevelBase(int requiredLevel, double levelOneRating) =>
    NonNegativeFinite(levelOneRating) * Math.Pow(1.08, Math.Clamp(requiredLevel, 1, 100) - 1);

public static double AttackPressure(int attackerLevel, double levelOnePressure = 150.0) =>
    PositiveFinite(levelOnePressure) * Math.Pow(1.08, Math.Clamp(attackerLevel, 1, 100) - 1);

public static double ArmorMitigation(double armor, double pressure)
{
    double rating = NonNegativeFinite(armor);
    double attackPressure = PositiveFinite(pressure);
    return Math.Min(0.99, rating / (rating + attackPressure));
}

public static double ClampElementalResistance(double value) =>
    Math.Clamp(Finite(value), 0.0, 0.75);

public static double EffectiveElementalResistance(
    double resistance,
    double penetration,
    double exposure) =>
    ClampElementalResistance(
        Finite(resistance) - Math.Max(0.0, Finite(penetration))
                           - Math.Max(0.0, Finite(exposure)));

public static double EffectiveEvasion(double evasion, double accuracy) =>
    Math.Clamp(Finite(evasion) - Math.Max(0.0, Finite(accuracy)), 0.0, 0.90);
```

Make existing `ScalingMath.WeaponLevelBaseDamage` and `ResolveWeaponDamage` delegate to the new methods so the simulator and runtime cannot drift.

- [ ] **Step 4: Run all scaling and balance simulator tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearScalingMathTests|FullyQualifiedName~ScalingMathTests|FullyQualifiedName~DamageScalingSimulatorTests"`

Expected: PASS with zero failures and unchanged build-pressure expectations.

- [ ] **Step 5: Commit the shared math**

```bash
git add VRPG/src/Inventory/Gear/GearScalingMath.cs VRPG/src/Modules/Rpg/Scaling/ScalingMath.cs VRPG/tests/VRPG.Tests/GearScalingMathTests.cs VRPG/tests/VRPG.Tests/ScalingMathTests.cs
git commit -m "Add VRPG gear scaling math"
```

### Task 4: Exact gear authoring and canonical prototypes

**Files:**
- Create: `VRPG/src/Inventory/Gear/GearInstanceAuthor.cs`
- Create: `VRPG/src/Inventory/Gear/GearPrototypeFactory.cs`
- Test: `VRPG/tests/VRPG.Tests/GearInstanceAuthorTests.cs`

**Interfaces:**
- Consumes: `VRPGDataRegistry`, `GearScalingMath`, and explicit `GearAuthoringRequest`/`GearAffixSelection` values.
- Produces: `GearInstanceAuthor.Author(GearAuthoringRequest) -> GearAuthoringResult` and `GearPrototypeFactory.Create(GearInstance, GearBaseDefinition, GearRarityDefinition) -> PlayerItemPrototype`.

- [ ] **Step 1: Write failing authoring-order and rejection tests**

```csharp
[Fact]
public void AuthorResolvesExactRollsOnceInRequiredOrder()
{
    GearAuthoringRequest request = Request(
        baseCode: "vrpg:test-sword",
        level: 40,
        rarity: "vrpg:rare",
        selections: new[]
        {
            new GearAffixSelection("vrpg:flat-power", 2, 0.25f),
            new GearAffixSelection("vrpg:additional-power", 2, 0.50f),
            new GearAffixSelection("vrpg:more-power", 1, 1.00f)
        });

    GearAuthoringResult result = Author().Author(request);

    Assert.True(result.Success, result.Error);
    Assert.Equal(40, result.Instance!.RequiredLevel);
    Assert.Equal("vrpg:rare", result.Instance.RarityCode);
    Assert.Equal(result.Instance.Resolved.WeaponPower,
        Decode(Prototype(result.Instance).ExactState).Resolved.WeaponPower);
}
```

Add failures for unknown base/rarity/affix, incompatible tags/family/role, tier above required level, duplicate affix, mutual exclusion, too many affixes, Unique base mismatch, invalid `Roll01`, and missing chassis fallback.

In `GearInstanceAuthorTests`, implement the referenced private fixtures explicitly:

```csharp
private static GearAuthoringRequest Request(
    string baseCode,
    int level,
    string rarity,
    GearAffixSelection[] selections) =>
    new(baseCode, level, rarity, selections, "test", "author", Array.Empty<string>(), 42UL);

private static GearInstanceAuthor Author() =>
    new(TestGearDefinitions.Registry());

private static PlayerItemPrototype Prototype(GearInstance instance) =>
    new GearPrototypeFactory(TestGearDefinitions.ChassisExists, "game:gear-rusty").Create(
        instance,
        TestGearDefinitions.RequireBase(instance.BaseCode),
        TestGearDefinitions.RequireRarity(instance.RarityCode));

private static GearInstance Decode(byte[] state)
{
    Assert.True(GearItemStateCodec.TryDecode(state, out GearInstance? gear, out string error), error);
    return gear!;
}
```

Create `TestGearDefinitions` in the same test file with `Registry`, `RequireBase`, `RequireRarity`, and `ChassisExists`; populate exactly the sword, three affixes, and Common/Rare/Unique definitions exercised by these tests. Do not hide production validation in this fixture.

- [ ] **Step 2: Run author tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~GearInstanceAuthorTests`

Expected: FAIL because the authoring boundary is absent.

- [ ] **Step 3: Define an explicit request that contains selection but no trusted resolved values**

```csharp
public sealed record GearAuthoringRequest(
    string BaseCode,
    int RequiredLevel,
    string RarityCode,
    GearAffixSelection[] Affixes,
    string SourceKind,
    string SourceCode,
    string[] SourceTags,
    ulong SelectionSeed);

public sealed record GearAffixSelection(string AffixCode, int Tier, float Roll01);

public sealed record GearAuthoringResult(bool Success, GearInstance? Instance, string Error)
{
    public static GearAuthoringResult Reject(string error) => new(false, null, error);
    public static GearAuthoringResult Accept(GearInstance instance) => new(true, instance, "");
}
```

`SelectionSeed` is stored for provenance and future replay diagnostics. This first pass does not use it to choose bases, rarity, affixes, or acquisition.

- [ ] **Step 4: Implement validation, exact interpolation, and permanent resolution**

For each selected tier, interpolate every authored modifier with `min + (max - min) * Roll01`; store all resulting `GearModifierRoll` values beneath that affix's `GearAffixRoll`, then aggregate base implicits plus affixes. Apply rarity `StatScalar` only to ordinary non-Weapon-Power base/affix values and apply `WeaponPowerScalar` last. Never read player or target level. Copy `request.SelectionSeed` into `GearProvenance.SelectionSeed`.

```csharp
double weaponPower = GearScalingMath.ResolveWeaponPower(
    GearScalingMath.WeaponLevelBase(request.RequiredLevel, baseDefinition.LevelOneWeaponPower),
    flatWeaponPower,
    additionalWeaponPower,
    moreWeaponPower,
    rarity.WeaponPowerScalar);
```

Store every resolved defensive value in `GearResolvedStats`; future balance-data changes must not be consulted when decoding it.

- [ ] **Step 5: Build canonical prototypes from chassis plus encoded mechanics**

```csharp
public PlayerItemPrototype Create(
    GearInstance instance,
    GearBaseDefinition gearBase,
    GearRarityDefinition rarity) => new(
        PlayerItemStorageKind.Unique,
        instance.VisualChassisAssetCode,
        PlayerItemCategories.Gear,
        rarity.Code == "vrpg:common" ? gearBase.Name : $"{rarity.Name} {gearBase.Name}",
        GearItemStateCodec.Encode(instance),
        PlayerItemAssetKind.Item);
```

`GearPrototypeFactory` accepts `Func<string, bool> chassisExists` and a configured fallback asset code. It uses the fallback only for `PlayerItemPrototype.AssetCode` when the stored chassis is unavailable; it must not mutate or re-encode `GearInstance.VisualChassisAssetCode`. Never substitute the mechanical base code into `AssetCode`.

- [ ] **Step 6: Run authoring, codec, and prototype/store insertion tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearInstanceAuthorTests|FullyQualifiedName~GearItemStateCodecTests|FullyQualifiedName~PlayerItemContractTests"`

Expected: PASS with exact stable state bytes for identical requests.

- [ ] **Step 7: Commit the authoring boundary**

```bash
git add VRPG/src/Inventory/Gear/GearInstanceAuthor.cs VRPG/src/Inventory/Gear/GearPrototypeFactory.cs VRPG/tests/VRPG.Tests/GearInstanceAuthorTests.cs
git commit -m "Author deterministic VRPG gear instances"
```

### Task 5: Canonical detail lookup and validated equipment snapshots

**Files:**
- Modify: `VRPG/src/Inventory/PlayerItemModels.cs`
- Modify: `VRPG/src/Inventory/IPlayerItemStore.cs`
- Modify: `VRPG/src/Inventory/InMemoryPlayerItemStore.cs`
- Create: `VRPG/src/Inventory/Gear/GearEquipmentService.cs`
- Test: `VRPG/tests/VRPG.Tests/GearEquipmentServiceTests.cs`
- Modify: `VRPG/tests/VRPG.Tests/TransactionalPlayerItemStoreTests.cs`
- Modify: `VRPG/tests/VRPG.Tests/PlayerItemPersistenceTests.cs`

**Interfaces:**
- Produces: `IPlayerItemStore.TryGet(string, out PlayerItemDetail?)`, `GearEquipmentService.TryEquip(...)`, and `GearEquipmentService.Snapshot(IPlayerItemStore)`.
- Consumes: canonical entry IDs, player level, equipment role, and `GearItemStateCodec`.

- [ ] **Step 1: Write failing immutability, role, level, and restart tests**

```csharp
[Fact]
public void EquipRejectsWrongRoleAndUnmetRequirementWithoutMovingEntry()
{
    IPlayerItemStore store = StoreWithGear(requiredLevel: 20, role: PlayerEquipmentRoles.Head);
    string entryId = store.Query(GearQuery()).Entries.Single().EntryId;
    var service = GearEquipmentServiceForFixtures();

    PlayerItemOperationResult wrongRole = service.TryEquip(
        store, playerLevel: 20, "wrong-role", entryId, PlayerEquipmentRoles.Feet, store.Revision);
    PlayerItemOperationResult underLevel = service.TryEquip(
        store, playerLevel: 19, "under-level", entryId, PlayerEquipmentRoles.Head, store.Revision);

    Assert.Equal(PlayerItemOperationRejection.EquipmentRoleMismatch, wrongRole.Rejection);
    Assert.Equal(PlayerItemOperationRejection.RequirementNotMet, underLevel.Rejection);
    Assert.Empty(store.Equipment().Entries);
}

[Fact]
public void DetailLookupReturnsDeepClones()
{
    IPlayerItemStore store = StoreWithGear(1, PlayerEquipmentRoles.MainWeapon);
    string id = store.Query(GearQuery()).Entries.Single().EntryId;
    Assert.True(store.TryGet(id, out PlayerItemDetail? first));
    first!.Prototype.ExactState[0] ^= 0xff;
    Assert.True(store.TryGet(id, out PlayerItemDetail? second));
    Assert.NotEqual(first.Prototype.ExactState[0], second!.Prototype.ExactState[0]);
}
```

Extend the persistence test so equipped gear decodes to the exact pre-save `GearInstance` after archive restart.

Define three private helpers in `GearEquipmentServiceTests`: `StoreWithGear(int requiredLevel, string role)` creates an `InMemoryPlayerItemStore`, inserts one codec-authored Gear entry with that requirement/role, and returns it; `GearQuery()` returns a stored-location Gear-category query; and `GearEquipmentServiceForFixtures()` returns a service backed by the same valid fixture registry. Separately add production-private `InMemoryPlayerItemStore.DetailClone(StoredEntry)`, which constructs a new `PlayerItemDetail` while cloning `Prototype.ExactState`; `TryGet` calls it under the store lock. The test helpers must use the Task 1 codec, not hand-authored state bytes.

- [ ] **Step 2: Run equipment/store tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearEquipmentServiceTests|FullyQualifiedName~TransactionalPlayerItemStoreTests|FullyQualifiedName~PlayerItemPersistenceTests"`

Expected: FAIL with missing detail lookup and rejection values.

- [ ] **Step 3: Add clone-safe internal detail access**

```csharp
public sealed record PlayerItemDetail(
    string EntryId,
    PlayerItemPrototype Prototype,
    long Quantity,
    PlayerItemLocation Location,
    PlayerItemFlags Flags);

public bool TryGet(string entryId, out PlayerItemDetail? detail)
{
    lock (sync)
    {
        if (!byId.TryGetValue(entryId, out StoredEntry? entry))
        {
            detail = null;
            return false;
        }
        detail = DetailClone(entry);
        return true;
    }
}
```

Add `InvalidGear`, `EquipmentRoleMismatch`, and `RequirementNotMet` to `PlayerItemOperationRejection`. The low-level store remains a generic custody primitive; all server gear equip entry points must call `GearEquipmentService` rather than `store.Equip` directly.

- [ ] **Step 4: Implement the validated gear equip gateway and snapshot**

```csharp
public PlayerItemOperationResult TryEquip(
    IPlayerItemStore store,
    int playerLevel,
    string requestId,
    string entryId,
    string roleCode,
    long expectedRevision)
{
    if (!store.TryGet(entryId, out PlayerItemDetail? detail)
        || detail == null
        || !GearItemStateCodec.TryDecode(detail.Prototype.ExactState, out GearInstance? gear, out _))
        return PlayerItemOperationResult.Rejected(store.Revision, requestId,
            PlayerItemOperationRejection.InvalidGear);
    if (!string.Equals(gear!.EquipmentRole, roleCode, StringComparison.Ordinal))
        return PlayerItemOperationResult.Rejected(store.Revision, requestId,
            PlayerItemOperationRejection.EquipmentRoleMismatch);
    if (playerLevel < gear.RequiredLevel)
        return PlayerItemOperationResult.Rejected(store.Revision, requestId,
            PlayerItemOperationRejection.RequirementNotMet);
    return store.Equip(requestId, entryId, roleCode, expectedRevision);
}
```

`Snapshot` must iterate `store.Equipment()`, retrieve each exact entry, decode it, verify the stored role equals both the equipment record and gear instance, and return a failure containing the entry ID instead of silently omitting invalid equipment.

- [ ] **Step 5: Run equipment, transaction, migration, and persistence tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearEquipmentServiceTests|FullyQualifiedName~TransactionalPlayerItemStoreTests|FullyQualifiedName~PlayerItemPersistenceTests|FullyQualifiedName~PlayerItemMigrationTests"`

Expected: PASS with no changed generic custody semantics.

- [ ] **Step 6: Commit the equipment boundary**

```bash
git add VRPG/src/Inventory/PlayerItemModels.cs VRPG/src/Inventory/IPlayerItemStore.cs VRPG/src/Inventory/InMemoryPlayerItemStore.cs VRPG/src/Inventory/Gear/GearEquipmentService.cs VRPG/tests/VRPG.Tests/GearEquipmentServiceTests.cs VRPG/tests/VRPG.Tests/TransactionalPlayerItemStoreTests.cs VRPG/tests/VRPG.Tests/PlayerItemPersistenceTests.cs
git commit -m "Validate canonical VRPG equipment"
```

### Task 6: Equipped stats, Weapon Power, Health, and Magic Shield

**Files:**
- Create: `VRPG/src/Modules/Rpg/Stats/EquippedGearStatResolver.cs`
- Create: `VRPG/tests/VRPG.Tests/EquippedGearStatResolverTests.cs`
- Modify: `VRPG/src/Modules/Rpg/Stats/SkillDamageResolver.cs`
- Create: `VRPG/tests/VRPG.Tests/SkillDamageResolverTests.cs`
- Modify: `VRPG/src/Modules/Rpg/Skills/SkillCastingService.cs`
- Modify: `VRPG/src/Modules/Rpg/Players/RpgResourceCalculator.cs`
- Modify: `VRPG/src/Modules/Rpg/Players/RpgResourceService.cs`
- Create: `VRPG/src/Modules/Rpg/Combat/PlayerWeaponDamageService.cs`
- Create: `VRPG/src/Modules/Rpg/Combat/RpgDamageSourceTagger.cs`
- Modify: `VRPG/src/Patches/EntityDamageScalingPatch.cs`
- Create: `VRPG/tests/VRPG.Tests/RpgResourceCalculatorTests.cs`
- Create: `VRPG/tests/VRPG.Tests/PlayerWeaponDamageServiceTests.cs`

**Interfaces:**
- Consumes: `GearEquipmentSnapshot`, store revision, player UID, and `GearInstance.Resolved`/affixes.
- Produces: `EquippedGearStats`, `EquippedGearStatResolver.Resolve(...)`, `RpgResourceService.AbsorbMagicShield(...)`, Weapon-Power-aware `SkillDamageResolver.Resolve(...)`, and basic-attack-aware `PlayerWeaponDamageService.Resolve(...)`.

- [ ] **Step 1: Write failing aggregation and stale-removal tests**

```csharp
[Fact]
public void ResolverAggregatesEachEquippedEntryOnceAndInvalidatesByRevision()
{
    var resolver = new EquippedGearStatResolver();
    GearEquipmentSnapshot first = Snapshot(
        Gear(weaponPower: 25, armor: 0, health: 5),
        Gear(weaponPower: 0, armor: 40, health: 10));

    EquippedGearStats totals = resolver.Resolve("player", revision: 4, first);
    EquippedGearStats cached = resolver.Resolve("player", revision: 4, first);
    var unequipped = new GearEquipmentSnapshot(5, true, new[] { first.Entries[0] }, "");
    EquippedGearStats afterUnequip = resolver.Resolve("player", revision: 5, unequipped);

    Assert.Equal(25, totals.WeaponPower);
    Assert.Equal(40, totals.Armor);
    Assert.Equal(15, totals.MaximumHealth);
    Assert.Same(totals, cached);
    Assert.Equal(0, afterUnequip.Armor);
}
```

Add tests proving only `weapon.main` supplies Weapon Power, all armor/resource modifiers disappear after unequip, resistance fields remain per element, and corrupted/duplicated equipment fails closed.

- [ ] **Step 2: Run aggregation/resource/damage tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~EquippedGearStatResolverTests|FullyQualifiedName~SkillDamageResolverTests|FullyQualifiedName~RpgResourceCalculatorTests|FullyQualifiedName~PlayerWeaponDamageServiceTests"`

Expected: FAIL with missing equipped-stat inputs.

- [ ] **Step 3: Implement immutable revision-keyed aggregation**

```csharp
public sealed record EquippedGearStats(
    float WeaponPower,
    float Armor,
    float MaximumHealth,
    float MaximumMagicShield,
    float Evasion,
    float FireResistance,
    float ColdResistance,
    float LightningResistance,
    float RustResistance,
    StatModifierDefinition[] Modifiers);
```

Task 5 must also define the snapshot contracts consumed here:

```csharp
public sealed record GearEquipmentEntry(string RoleCode, string EntryId, GearInstance Instance);
public sealed record GearEquipmentSnapshot(
    long Revision,
    bool Valid,
    GearEquipmentEntry[] Entries,
    string Error);
```

In `EquippedGearStatResolverTests`, define `Gear(...)` as a private fixture factory returning a complete valid `GearInstance` with the named resolved values and zeroes for omitted fields. Define `Snapshot(params GearInstance[] instances)` to assign the first instance `weapon.main`, subsequent instances distinct armor roles, and return a valid `GearEquipmentSnapshot`.

Cache by `(playerUid, storeRevision)` and replace rather than mutate results. Cap nothing here; the damage resolver owns defensive caps. Preserve exact resolved values from each instance and feed only non-base affix modifiers into applicable stat accumulators.

- [ ] **Step 4: Make skill damage start from equipped Weapon Power**

Change the signature to:

```csharp
public float Resolve(
    RpgPlayerState state,
    SkillDefinition skill,
    int skillLevel,
    EquippedGearStats gear)
{
    double baseHit = gear.WeaponPower * skill.WeaponDamagePercentAtLevel(skillLevel) / 100.0;
    StatModifierAccumulator modifiers = BuildSkillModifiers(state, skill, gear.Modifiers);
    return (float)Math.Min(float.MaxValue, modifiers.Resolve(baseHit));
}
```

Do not use `SkillDefinition.DamageAtLevel` as the base hit once a valid main weapon exists. Add an explicit level-one `10 Weapon Power` fallback for RPG-eligible players with no valid main weapon; never derive fallback from player or target level. Update both `SkillCastingService` call sites to request the current equipped snapshot once per activation/sequence.

- [ ] **Step 5: Replace eligible basic attacks with equipped Weapon Power**

Add `RpgDamageSourceTagger` using `ConditionalWeakTable<DamageSource, RpgDamageSourceMarker>` with `MarkSkill` and `IsSkill`; the empty marker is transient server metadata and is never serialized. Change both skill-casting call sites to construct the `DamageSource` in a local variable, mark that exact instance as Skill, and then call `ReceiveDamage`.

`PlayerWeaponDamageService.Resolve(Entity target, DamageSource source, float engineDamage)` returns `engineDamage` unless the cause is an RPG-active player and the target is RPG-eligible. It also returns the already-resolved value for tagged Skill damage. For an eligible untagged direct melee or projectile basic attack, return the player's equipped main-hand Weapon Power after applicable attack modifiers, using the explicit `10` fallback when no valid main weapon exists. Add tests for equipped basic attack, fallback, tagged skill non-replacement, non-RPG target preservation, non-player source preservation, and invalid equipment fail-closed behavior.

Add `EntityDamageScalingPatch.PlayerOutgoingDamageResolver` and invoke it before creature source multipliers when the damage source cause is a player. Task 7 adds the separate incoming-player resolver. Clear both delegates during module disposal.

- [ ] **Step 6: Include equipped resource modifiers and shield absorption**

Add `EquippedGearStats gear` to `CalculateMaximums` and `CalculateRegeneration`. Apply `gear.MaximumHealth` and `gear.MaximumMagicShield` as flat base contributions before increased/more equipment/talent modifiers. Add:

```csharp
public float AbsorbMagicShield(IServerPlayer player, float incomingDamage)
{
    RpgPlayerState state = playerStore.GetOrCreate(player);
    float absorbed = Math.Min(Math.Max(0f, incomingDamage), Math.Max(0f, state.MagicShield));
    state.MagicShield -= absorbed;
    if (absorbed > 0f) playerStore.Save();
    return Math.Max(0f, incomingDamage - absorbed);
}
```

Do not send or save when `absorbed == 0`. Clamp current resources against recalculated equipped maxima after equip/unequip and preserve current percentage when maximum Health changes.

- [ ] **Step 7: Run focused stat/resource/skill/basic-attack tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~EquippedGearStatResolverTests|FullyQualifiedName~SkillDamageResolverTests|FullyQualifiedName~RpgResourceCalculatorTests|FullyQualifiedName~PlayerWeaponDamageServiceTests|FullyQualifiedName~ScalingMathTests"`

Expected: PASS and skill tests assert actual Weapon Damage coefficients rather than legacy flat skill base damage.

- [ ] **Step 8: Commit authoritative equipment effects**

```bash
git add VRPG/src/Modules/Rpg/Stats/EquippedGearStatResolver.cs VRPG/src/Modules/Rpg/Stats/SkillDamageResolver.cs VRPG/src/Modules/Rpg/Skills/SkillCastingService.cs VRPG/src/Modules/Rpg/Players/RpgResourceCalculator.cs VRPG/src/Modules/Rpg/Players/RpgResourceService.cs VRPG/src/Modules/Rpg/Combat/PlayerWeaponDamageService.cs VRPG/src/Modules/Rpg/Combat/RpgDamageSourceTagger.cs VRPG/src/Patches/EntityDamageScalingPatch.cs VRPG/tests/VRPG.Tests/EquippedGearStatResolverTests.cs VRPG/tests/VRPG.Tests/SkillDamageResolverTests.cs VRPG/tests/VRPG.Tests/RpgResourceCalculatorTests.cs VRPG/tests/VRPG.Tests/PlayerWeaponDamageServiceTests.cs
git commit -m "Apply equipped VRPG gear stats"
```

### Task 7: Ordered player defense resolution and Evasive Step correction

**Files:**
- Create: `VRPG/src/Modules/Rpg/Combat/PlayerDefenseMath.cs`
- Create: `VRPG/src/Modules/Rpg/Combat/PlayerDefenseService.cs`
- Create: `VRPG/tests/VRPG.Tests/PlayerDefenseMathTests.cs`
- Create: `VRPG/tests/VRPG.Tests/PlayerDefenseServiceTests.cs`
- Modify: `VRPG/src/Modules/Rpg/Combat/EvasiveStepService.cs`
- Modify: `VRPG/src/Patches/EntityDamageScalingPatch.cs`

**Interfaces:**
- Consumes: scaled incoming damage, `DamageSource`, attacker level/Accuracy, target `EquippedGearStats`, current Magic Shield/Health, Evasive Step state, and injected server RNG.
- Produces: `PlayerDefenseMath.Resolve(DefenseResolutionInput) -> DefenseResolution`, `PlayerDefenseService.ResolveDamage(...) -> float`, `EvasiveStepService.TryActivate(...)`, and `EntityDamageScalingPatch.PlayerDamageResolver`.

- [ ] **Step 1: Write failing pure ordering and cap tests**

```csharp
[Fact]
public void PhysicalDamageUsesEvasionThenArmorThenShieldThenHealth()
{
    DefenseResolution result = PlayerDefenseMath.Resolve(new DefenseResolutionInput(
        IncomingDamage: 100,
        Element: DamageElement.Physical,
        EvasionEligible: true,
        EvasionRoll01: 0.95,
        Evasion: 0.90,
        Accuracy: 0,
        Armor: 1350,
        AttackPressure: 150,
        ElementalResistance: 0,
        ResistancePenetration: 0,
        ResistanceExposure: 0,
        OtherReductionMultiplier: 1,
        MagicShield: 4,
        CurrentHealth: 10));

    Assert.False(result.Evaded);
    Assert.Equal(0.90, result.Mitigation, 3);
    Assert.Equal(4, result.ShieldAbsorbed, 3);
    Assert.Equal(6, result.HealthDamage, 3);
    Assert.False(result.WouldBeLethal);
}

[Fact]
public void DotAndPersistentHazardsNeverRollEvasion()
{
    DefenseResolution result = PlayerDefenseMath.Resolve(Input(
        evasionEligible: false, evasion: 0.90, evasionRoll01: 0.01));
    Assert.False(result.Evaded);
}
```

Add cases for 99% Armor cap, 75% each-element cap, resistance penetration/exposure, the other-reduction layer occurring before shield, 90% Evasion cap, Accuracy subtraction, connected zero-Armor physical hit, elemental mitigation without Armor, shield exhaustion, lethal residual equality, and a 50/50 aggregate-damage fixture.

Define `Input(bool evasionEligible, double evasion, double evasionRoll01)` in `PlayerDefenseMathTests` as a complete `DefenseResolutionInput` with `10` incoming Physical damage, zero Accuracy/Armor/shield/penetration/exposure, other-reduction multiplier `1`, pressure `150`, Health `20`, and the three supplied values. The 50/50 fixture must sum a fixed list of physical and elemental source budgets and assert each category contributes half before mitigation; it is an encounter-composition contract, not a rule that every enemy mixes damage types.

- [ ] **Step 2: Run defense tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter FullyQualifiedName~PlayerDefense`

Expected: FAIL because the resolver/service do not exist.

- [ ] **Step 3: Implement a side-effect-free defense result**

```csharp
public sealed record DefenseResolution(
    bool Evaded,
    double EffectiveEvasion,
    double Mitigation,
    float ShieldAbsorbed,
    float HealthDamage,
    bool WouldBeLethal);

public enum DamageElement { Physical, Fire, Cold, Lightning, Rust }

public sealed record DefenseResolutionInput(
    float IncomingDamage,
    DamageElement Element,
    bool EvasionEligible,
    double EvasionRoll01,
    double Evasion,
    double Accuracy,
    double Armor,
    double AttackPressure,
    double ElementalResistance,
    double ResistancePenetration,
    double ResistanceExposure,
    double OtherReductionMultiplier,
    float MagicShield,
    float CurrentHealth);

public static DefenseResolution Resolve(DefenseResolutionInput input)
{
    double effectiveEvasion = input.EvasionEligible
        ? GearScalingMath.EffectiveEvasion(input.Evasion, input.Accuracy)
        : 0.0;
    if (input.EvasionEligible && input.EvasionRoll01 < effectiveEvasion)
        return new(true, effectiveEvasion, 1.0, 0, 0, false);

    double mitigation = input.Element == DamageElement.Physical
        ? GearScalingMath.ArmorMitigation(input.Armor, input.AttackPressure)
        : GearScalingMath.EffectiveElementalResistance(
            input.ElementalResistance,
            input.ResistancePenetration,
            input.ResistanceExposure);
    if (!double.IsFinite(input.OtherReductionMultiplier))
        throw new ArgumentOutOfRangeException(nameof(input.OtherReductionMultiplier));
    double otherReduction = Math.Clamp(input.OtherReductionMultiplier, 0.0, 1.0);
    float afterMitigation = (float)(input.IncomingDamage
        * (1.0 - mitigation)
        * otherReduction);
    float shield = Math.Min(input.MagicShield, afterMitigation);
    float health = Math.Max(0f, afterMitigation - shield);
    return new(false, effectiveEvasion, mitigation, shield, health,
        health + 0.001f >= input.CurrentHealth);
}
```

- [ ] **Step 4: Split Evasive Step's two responsibilities**

Keep `IsInvulnerable(Entity)` as the first pipeline gate. Replace `TryPreventDamage` with:

```csharp
public bool TryActivate(EntityPlayer player, DamageSource source, bool wouldBeLethal)
{
    if (!wouldBeLethal || !IsHostile(source) || !IsDexterityPrimary(player) || IsOnCooldown(player))
        return false;
    ApplyEscape(player, source);
    StartCooldownAndInvulnerability(player);
    return true;
}
```

Implement the referenced private operations in `EvasiveStepService`: `IsHostile` rejects self, friendly, and non-hostile environmental sources; `IsDexterityPrimary` reads the authoritative selected-class pair; `IsOnCooldown` reads the server cooldown clock; `ApplyEscape` computes the away-from-source vector and applies the configured `0.28` impulse; and `StartCooldownAndInvulnerability` applies the `6.0` second cooldown and `0.38` second i-frame window after talent modifiers. Cover each helper through the public service tests; do not expose them as a second combat API.

The method must not inspect raw incoming damage or pre-mitigation Health. Preserve config clamping, Quick Reflex, Untouchable Moment, Slipstream, visuals, and cooldown HUD reporting.

- [ ] **Step 5: Implement the runtime defense adapter with injected RNG**

`PlayerDefenseService.ResolveDamage` must:

1. return zero immediately during active Evasive Step invulnerability unless the source explicitly ignores invulnerability;
2. map direct attack/spell hits to `EvasionEligible=true` and DoT, persistent block hazards, internal/environmental damage, and explicit unavoidable metadata to false;
3. resolve the pure calculation using server RNG, the attacker's level-scaled pressure/Accuracy, matching resistance penetration/exposure, and any explicit other-reduction multiplier from the authoritative combat context;
4. spend exactly `ShieldAbsorbed` from `RpgResourceService`;
5. call `EvasiveStepService.TryActivate` only for the post-shield lethal result;
6. return zero when evaded or Evasive Step activates, otherwise return `HealthDamage` to Vintage Story.

Expose RNG as `Func<double>` in the constructor for deterministic tests; the production delegate is `Random.Shared.NextDouble`.

- [ ] **Step 6: Replace the boolean patch delegate with a final-damage delegate**

```csharp
public static Func<Entity, DamageSource, float, float>? PlayerDamageResolver { get; set; }

// After source level/rarity scaling:
if (target is EntityPlayer && PlayerDamageResolver != null)
    damage = Math.Max(0f, PlayerDamageResolver(target, damageSource, damage));
```

Do not apply player gear defenses to non-player targets in this slice. Clear the delegate during module disposal exactly as the current Evasive Step delegate is cleared.

- [ ] **Step 7: Run the defense, resource, runtime-patch, and Evasive Step tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~PlayerDefense|FullyQualifiedName~RpgResource|FullyQualifiedName~CharacterRuntimeMode"`

Expected: PASS, including nonlethal hits, exact-lethal hits, cooldown, displacement request, i-frame follow-ups, Dexterity loss, Quick Reflex, Untouchable Moment, and low-Armor connected-hit cases.

- [ ] **Step 8: Commit ordered defense resolution**

```bash
git add VRPG/src/Modules/Rpg/Combat/PlayerDefenseMath.cs VRPG/src/Modules/Rpg/Combat/PlayerDefenseService.cs VRPG/src/Modules/Rpg/Combat/EvasiveStepService.cs VRPG/src/Patches/EntityDamageScalingPatch.cs VRPG/tests/VRPG.Tests/PlayerDefenseMathTests.cs VRPG/tests/VRPG.Tests/PlayerDefenseServiceTests.cs
git commit -m "Resolve VRPG player defenses in server order"
```

### Task 8: Complete deterministic starter sets and shared authoring path

**Files:**
- Create: `VRPG/src/Inventory/Gear/StarterGearSetFactory.cs`
- Create: `VRPG/tests/VRPG.Tests/StarterGearSetFactoryTests.cs`
- Modify: `VRPG/src/Data/Definitions/ClassDefinition.cs`
- Modify: `VRPG/src/Data/StarterLoadoutDefinitionValidator.cs`
- Modify: `VRPG/tests/VRPG.Tests/StarterLoadoutDefinitionValidatorTests.cs`
- Create: `VRPG/assets/vrpg/vrpg/gear/*.json`
- Modify: `VRPG/assets/vrpg/vrpg/classes/corroder.json`
- Modify: `VRPG/assets/vrpg/vrpg/classes/pilferer.json`
- Modify: `VRPG/assets/vrpg/vrpg/classes/scrapper.json`
- Modify: `VRPG/assets/vrpg/vrpg/classes/smith.json`
- Modify: `VRPG/assets/vrpg/vrpg/classes/warden.json`

**Interfaces:**
- Consumes: one starter-enabled `ClassDefinition` and `GearInstanceAuthor`.
- Produces: `StarterGearSetFactory.Create(string classCode) -> StarterGearSetResult`, containing exactly six uncommitted `PlayerItemPrototype`/role pairs.

- [ ] **Step 1: Write failing completeness and determinism tests**

```csharp
[Theory]
[InlineData("vrpg:corroder")]
[InlineData("vrpg:pilferer")]
[InlineData("vrpg:scrapper")]
[InlineData("vrpg:smith")]
[InlineData("vrpg:warden")]
public void StarterClassProducesOneWeaponAndFiveArmorPieces(string classCode)
{
    StarterGearSetResult first = Factory().Create(classCode);
    StarterGearSetResult second = Factory().Create(classCode);

    Assert.True(first.Success, first.Error);
    Assert.Equal(6, first.Items.Length);
    string[] expectedRoles = new[]
    {
        PlayerEquipmentRoles.MainWeapon,
        PlayerEquipmentRoles.Head,
        PlayerEquipmentRoles.Body,
        PlayerEquipmentRoles.Legs,
        PlayerEquipmentRoles.Hands,
        PlayerEquipmentRoles.Feet
    }.Order().ToArray();
    Assert.Equal(expectedRoles, first.Items.Select(item => item.RoleCode).Order().ToArray());
    Assert.All(first.Items, item =>
    {
        GearInstance gear = Decode(item.Prototype.ExactState);
        Assert.Equal(1, gear.RequiredLevel);
        Assert.Equal("vrpg:common", gear.RarityCode);
        Assert.Empty(gear.Affixes);
    });
    Assert.Equal(
        first.Items.Select(x => Convert.ToHexString(x.Prototype.ExactState)),
        second.Items.Select(x => Convert.ToHexString(x.Prototype.ExactState)));
}
```

Add validation failures for missing/duplicate roles, starter references to raw `game:` assets, non-Common/level-one requests, set bonuses, and any base whose chassis is missing.

In `StarterGearSetFactoryTests`, define `Factory()` to load the five class fixtures and the Task 8 base files through a real `VRPGDataRegistry`, then construct `GearInstanceAuthor`, `GearPrototypeFactory`, and `StarterGearSetFactory`. Define `Decode(byte[])` exactly like the checked codec helper in Task 4; do not duplicate codec behavior.

- [ ] **Step 2: Run starter tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~StarterGearSetFactoryTests|FullyQualifiedName~StarterLoadoutDefinitionValidatorTests"`

Expected: FAIL because starters still reference raw chassis items and only one armor slot.

- [ ] **Step 3: Change starter definitions from chassis items to gear-base references**

```csharp
public sealed class StarterItemDefinition
{
    public string GearBaseCode { get; set; } = "";
    public string EquipmentRole { get; set; } = "";
}
```

The validator must require exactly `weapon.main`, `armor.head`, `armor.body`, `armor.legs`, `armor.hands`, and `armor.feet`, forbid duplicates, resolve each base, and require that its authored role matches. Starter preview names come from resolved gear bases rather than duplicated class JSON strings.

- [ ] **Step 4: Author the initial class-specific visual bases**

Create one base per starter weapon and armor piece. Every set totals exactly 100 level-one Armor using `head=15`, `body=35`, `legs=25`, `hands=10`, `feet=15`; no starter base has affixes or a set-bonus field. Use these verified Vintage Story chassis families:

| Class | Weapon chassis | Head/body/legs | Hands | Feet |
|---|---|---|---|---|
| Smith | `game:hammer-iron` | `game:armor-*-chain-copper` | `game:clothes-hand-heavy-leather-gloves` | `game:clothes-foot-metalcap-boots` |
| Warden | `game:spear-generic-iron` | `game:armor-*-lamellar-wood` | `game:clothes-hand-forlorn-bracer` | `game:clothes-foot-soldier-boots` |
| Pilferer | `game:knife-generic-iron` | `game:armor-*-sewn-leather` | `game:clothes-hand-malefactor-gloves` | `game:clothes-foot-malefactor-boots` |
| Corroder | `game:club-scrapmace-scrap` | `game:armor-*-tailored-orange-linen` | `game:clothes-hand-cuffsorange` | `game:clothes-foot-alchemist` |
| Scrapper | `game:club-scrap-scrap` | `game:armor-*-lamellar-wood` | `game:clothes-hand-heavy-leather-gloves` | `game:clothes-foot-worn-leather-boots` |

Expand `*` into head/body/legs base files. If the runtime asset registry rejects any listed chassis, select another existing chassis from the same class silhouette and record the substitution in the commit; do not create a model during this foundation slice.

- [ ] **Step 5: Implement starter authoring without store mutation**

Add the explicit result contracts:

```csharp
public sealed record StarterGearItem(string RoleCode, PlayerItemPrototype Prototype);

public sealed record StarterGearSetResult(
    bool Success,
    string ClassCode,
    StarterGearItem[] Items,
    string Error)
{
    public static StarterGearSetResult Reject(string classCode, string error) =>
        new(false, classCode, Array.Empty<StarterGearItem>(), error);
    public static StarterGearSetResult Accept(string classCode, StarterGearItem[] items) =>
        new(true, classCode, items, "");
}
```

```csharp
public StarterGearSetResult Create(string classCode)
{
    if (!data.Classes.TryGet(classCode, out ClassDefinition? definition)
        || definition?.Starter == null)
        return StarterGearSetResult.Reject(classCode, "Unknown or non-starter class.");
    if (!data.Rarities.TryGet("vrpg:common", out GearRarityDefinition? common))
        return StarterGearSetResult.Reject(classCode, "Common rarity is unavailable.");

    StarterGearItem[] items = definition.Starter!.Items.Select(item =>
    {
        if (!data.GearBases.TryGet(item.GearBaseCode, out GearBaseDefinition? gearBase))
            throw new InvalidOperationException($"Validated starter base disappeared: {item.GearBaseCode}");
        GearAuthoringResult authored = author.Author(new GearAuthoringRequest(
            item.GearBaseCode, 1, "vrpg:common", Array.Empty<GearAffixSelection>(),
            "starter", definition.Code, new[] { definition.Code }, StableSeed(definition.Code, item.EquipmentRole)));
        return new StarterGearItem(item.EquipmentRole,
            prototypeFactory.Create(authored.Instance!, gearBase!, common!));
    }).ToArray();
    return StarterGearSetResult.Accept(definition.Code, items);
}
```

Implement `StableSeed(classCode, roleCode)` by hashing the UTF-8 bytes of `classCode + "\n" + roleCode` with SHA-256 and reading the first eight digest bytes as an unsigned big-endian integer. Check every `GearAuthoringResult`; convert a rejection to `StarterGearSetResult.Reject` instead of dereferencing `Instance`. Use a simple loop rather than LINQ if needed to return early.

Do not insert, equip, persist, learn a skill, spend a point, or advance onboarding here. Those actions belong to the next atomic-onboarding plan.

- [ ] **Step 6: Run starter, data, authoring, and codec tests**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~Starter|FullyQualifiedName~GearDefinition|FullyQualifiedName~GearInstanceAuthor|FullyQualifiedName~GearItemStateCodec"`

Expected: PASS for all five selectable starter classes and identical bytes across repeat creation.

- [ ] **Step 7: Commit complete starter content**

```bash
git add VRPG/src/Inventory/Gear/StarterGearSetFactory.cs VRPG/src/Data/Definitions/ClassDefinition.cs VRPG/src/Data/StarterLoadoutDefinitionValidator.cs VRPG/assets/vrpg/vrpg/gear VRPG/assets/vrpg/vrpg/classes VRPG/tests/VRPG.Tests/StarterGearSetFactoryTests.cs VRPG/tests/VRPG.Tests/StarterLoadoutDefinitionValidatorTests.cs
git commit -m "Add complete VRPG starter gear sets"
```

### Task 9: Server wiring, recovery validation, and foundation acceptance gate

**Files:**
- Create: `VRPG/src/Inventory/Gear/GearReadinessValidator.cs`
- Create: `VRPG/tests/VRPG.Tests/GearReadinessValidatorTests.cs`
- Modify: `VRPG/src/VRPGModSystem.cs`
- Modify: `VRPG/src/Modules/Rpg/RpgModule.cs`
- Modify: `VRPG/src/Inventory/Persistence/PlayerItemArchiveService.cs`
- Modify: `VRPG/tests/VRPG.Tests/PlayerItemPersistenceTests.cs`
- Modify: `VRPG/tests/VRPG.Tests/DamageScalingSimulatorTests.cs`
- Modify: `VRPG/docs/design/testable-version-todo.md`

**Interfaces:**
- Consumes: ready `PlayerItemStoreDirectory`, `GearEquipmentService`, `EquippedGearStatResolver`, `PlayerDefenseService`, and `StarterGearSetFactory`.
- Produces: `GearReadinessValidator.ValidateAndRecover(...) -> GearReadinessResult`, one fail-closed server composition root, and a verified handoff API for the later onboarding transaction.

- [ ] **Step 1: Write failing composition and invalid-equipped-gear recovery tests**

```csharp
[Fact]
public void InvalidEquippedGearBlocksGearReadinessAndMovesEntryToRecovery()
{
    PlayerItemArchiveService archive = ArchiveWithEquippedMalformedGear();
    GearReadinessResult result = GearReadinessValidator.ValidateAndRecover(
        archive, Definitions(), _ => true, "game:gear-rusty");

    Assert.False(result.Ready);
    Assert.Equal(PlayerItemLocation.Unresolved,
        archive.Directory.ForPlayer("player").Recovery().Entries.Single().Location);
    Assert.Equal(PlayerItemRecoveryReason.UnsupportedContent,
        archive.Directory.ForPlayer("player").Recovery().Entries.Single().Reason);
}
```

Define the result contract in `GearReadinessValidator.cs`:

```csharp
public sealed record GearReadinessResult(bool Ready, int ValidEntries, int RecoveredEntries, string Error);

public static GearReadinessResult ValidateAndRecover(
    PlayerItemArchiveService archive,
    VRPGDataRegistry definitions,
    Func<string, bool> chassisExists,
    string fallbackChassisAssetCode);
```

Add a healthy restart fixture, missing chassis fallback fixture, missing base/rarity/affix rejection fixtures, future gear schema refusal, and a composition test proving RPG startup does not expose gear readiness when item persistence is unavailable.

In `GearReadinessValidatorTests`, implement `ArchiveWithEquippedMalformedGear()` by creating a disposable archive fixture, inserting a unique Gear-category entry whose `ExactState` is malformed, equipping it through the existing low-level fixture setup, and returning the loaded service. Implement `Definitions()` by loading the minimal valid Common/Rare/Unique, base, and affix fixture set. Call `ValidateAndRecover` with a chassis predicate appropriate to each test and the explicit fallback `game:gear-rusty`.

- [ ] **Step 2: Run integration tests and verify they fail**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --filter "FullyQualifiedName~GearReadiness|FullyQualifiedName~PlayerItemPersistence|FullyQualifiedName~DamageScalingSimulator"`

Expected: FAIL with no composed gear services/readiness validator.

- [ ] **Step 3: Pass the item directory into the RPG module only after archive load**

```csharp
rpgModule = new RpgModule(
    config.Modules.Rpg,
    DataRegistry,
    serverChannel!,
    playerItemPersistence?.Service?.Directory,
    () => playerItemPersistence?.IsReady == true);
```

Change `RpgModule` to require the directory for gear features. When it is null or the archive is not ready, keep Vanilla ownership/fallback behavior and report gear readiness false; do not construct an empty parallel store.

- [ ] **Step 4: Compose one server-owned gear path and dispose every delegate**

In `RpgModule.StartServerSide`, construct in dependency order:

```csharp
gearEquipment = new GearEquipmentService(data);
equippedGear = new EquippedGearStatResolver(gearEquipment);
starterGear = new StarterGearSetFactory(data, new GearInstanceAuthor(data));
playerWeaponDamage = new PlayerWeaponDamageService(
    playerStore,
    itemDirectory,
    equippedGear,
    player => CharacterRuntimeMode.IsOwned(
        config.Modules.Rpg.Enabled,
        config.Modules.Rpg.CharacterRuntime.Enabled,
        player.WorldData.CurrentGameMode),
    target => target.WatchedAttributes.GetInt("vrpgLevel", 0) > 0);
playerDefense = new PlayerDefenseService(
    api, playerStore, itemDirectory, equippedGear, resources, evasiveStep, Random.Shared.NextDouble);
EntityDamageScalingPatch.PlayerOutgoingDamageResolver = playerWeaponDamage.Resolve;
EntityDamageScalingPatch.PlayerDamageResolver = playerDefense.ResolveDamage;
```

Pass a `Func<IServerPlayer, EquippedGearStats>` to `SkillCastingService` and `RpgResourceService` so all consumers use the same revision-keyed snapshot. Keep the two eligibility predicates in the composition root: player ownership uses the existing `CharacterRuntimeMode`, and target eligibility requires the marker already assigned by `RpgScalingService`; do not create a second ownership registry. Clear both damage delegates and cached per-player snapshots on disposal/world leave.

- [ ] **Step 5: Validate persisted gear before reporting readiness**

After archive load, inspect stored/equipped Gear entries. When a valid mechanical instance names a missing chassis, verify the configured fallback exists and use it only when materializing the runtime `PlayerItemPrototype`; preserve the original encoded bytes and `GearInstance.VisualChassisAssetCode`. Missing base/rarity/affix, unsupported operation, role mismatch, malformed bytes, future schema, or a missing configured fallback moves the exact entry to `Unresolved` with source `gear-readiness`; equipped references are removed by the existing location transition. Save recovery changes before readiness becomes true.

- [ ] **Step 6: Run the complete automated suite**

Run: `dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj -c Release`

Expected: PASS with zero failed tests. Record the exact pass count in the task handoff; do not reuse the earlier 306-test count.

- [ ] **Step 7: Build the mod against the installed development target**

Run: `dotnet build VRPG/VRPG.csproj -c Release`

Expected: build succeeds with zero errors. Existing warnings must be listed and compared with the pre-task baseline rather than silently declared new or harmless.

- [ ] **Step 8: Run the focused acceptance matrix**

Run these checks in a disposable test world and dedicated-server fixture:

1. Start with item persistence enabled and confirm data validation reports all starter bases.
2. Use a development-only inspection command to author each starter set; do not insert it into a live player store.
3. Insert/equip one test weapon through the validated service and confirm a 100% skill uses its stored Weapon Power.
4. Make an ordinary basic attack with the same weapon and confirm it uses stored Weapon Power; repeat against a non-RPG target and confirm native damage remains unchanged.
5. Equip/unequip armor and verify Health, Magic Shield, Armor, Evasion, and each resistance update without stale modifiers.
6. Restart and confirm exact instance values, roles, and chassis remain unchanged.
7. Exercise physical, fire, cold, lightning, and rust hits; verify penetration/exposure, other-reduction, caps, and shield ordering.
8. At zero Armor and 90% Evasion, force a connected physical hit and verify full physical pressure is visible.
9. Trigger Evasive Step with a post-shield lethal hit, confirm displacement and 0.38-second i-frames, then confirm cooldown allows clustered follow-up pressure to kill.
10. Disable item persistence and confirm gear readiness fails closed without taking Vanilla inventory/menu ownership.
11. Equip the authored level-100 balance fixture and verify all four elemental resistances clamp at 75% while the chosen primary defense reaches its target; verify Armor continues approaching the 99% hard cap above the 90% practical ceiling. Life-leech behavior remains a later combat-system acceptance item.

- [ ] **Step 9: Update only completed TODO claims**

Mark the gear schema/math/starter/equipment-effect checklist items complete only when Steps 6–8 pass. Leave engine-ingress completion, atomic starter transaction, random generation, loot acquisition, menu replacement, and spawn/combat-content items unchecked.

- [ ] **Step 10: Commit integration and evidence**

```bash
git add VRPG/src/VRPGModSystem.cs VRPG/src/Modules/Rpg/RpgModule.cs VRPG/src/Inventory/Gear/GearReadinessValidator.cs VRPG/src/Inventory/Persistence/PlayerItemArchiveService.cs VRPG/tests/VRPG.Tests/GearReadinessValidatorTests.cs VRPG/tests/VRPG.Tests/PlayerItemPersistenceTests.cs VRPG/tests/VRPG.Tests/DamageScalingSimulatorTests.cs VRPG/docs/design/testable-version-todo.md
git commit -m "Wire the VRPG gear foundation"
```

## Follow-up Plans Required After This One

1. **Atomic first-run grant:** reserve capacity, insert six exact starter prototypes, equip their canonical entry IDs, select class/primary attribute, grant and allocate the ordinary refundable skill point, persist once, and make retry return the original committed result.
2. **Random gear generation:** deterministic seeded base/rarity/affix selection, soft build weighting, compatibility/exclusion enforcement, distribution sweeps, and no live player acquisition.
3. **Live loot acquisition:** explicit encounter/drop/reward source calls random generation and proves the complete canonical lifecycle before the UI or documentation describes random gear as obtainable.
