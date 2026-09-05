using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Inventory;
using Xunit;

namespace VRPG.Tests;

public sealed class StarterLoadoutDefinitionValidatorTests
{
    [Fact]
    public void AcceptsCompleteDataDefinedStarter() => Assert.Empty(Validate(CompleteStarter()));

    [Fact]
    public void RejectsMissingAssetsForeignSkillAndIncompletePreview()
    {
        ClassDefinition definition = CompleteStarter();
        definition.Starter!.CombatPattern = "";
        definition.Starter.Items = definition.Starter.Items.Take(1).ToArray();
        List<string> errors = Validate(definition, assetExists: _ => false,
            skill: new SkillDefinition { Code = "vrpg:hammer-blow", ClassCode = "vrpg:warden", RequiredLevel = 2 });
        Assert.Contains(errors, error => error.Contains("combatPattern", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("missing role", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("chassis", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("same class", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("level 1", StringComparison.Ordinal));
    }

    [Fact]
    public void RejectsDuplicateRoles()
    {
        ClassDefinition definition = CompleteStarter();
        definition.Starter!.Items[5].EquipmentRole = PlayerEquipmentRoles.Body;
        Assert.Contains(Validate(definition), error => error.Contains("duplicate", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("game:hammer-iron", "gear base")]
    [InlineData("GAME:hammer-iron", "gear base")]
    [InlineData("vrpg:missing", "unknown")]
    public void RejectsRawChassisAndUnknownBases(string code, string expected)
    {
        ClassDefinition definition = CompleteStarter();
        definition.Starter!.Items[0].GearBaseCode = code;
        Assert.Contains(Validate(definition), error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RejectsBaseRoleMismatch()
    {
        GearBaseDefinition[] bases = Bases();
        bases[0].EquipmentRole = PlayerEquipmentRoles.Feet;
        Assert.Contains(Validate(CompleteStarter(), bases), error => error.Contains("role", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("requiredLevel", "2")]
    [InlineData("RequiredLevel", "100")]
    [InlineData("level", "3")]
    [InlineData("rarityCode", "\"vrpg:rare\"")]
    [InlineData("RARITYCODE", "\"vrpg:unique\"")]
    [InlineData("rarity", "\"rare\"")]
    [InlineData("affixes", "[\"vrpg:power\"]")]
    [InlineData("Affixes", "[]")]
    [InlineData("setBonus", "{\"armor\":10}")]
    [InlineData("SetBonuses", "[]")]
    [InlineData("setCode", "\"vrpg:starter\"")]
    public void RejectsAuthoringOverridesAtItemAndLoadoutLevel(string key, string value)
    {
        foreach (bool onItem in new[] { true, false })
        {
            JObject json = JObject.FromObject(CompleteStarter());
            JObject target = onItem ? (JObject)json["Starter"]!["Items"]![0]! : (JObject)json["Starter"]!;
            target[key] = JToken.Parse(value);
            ClassDefinition definition = json.ToObject<ClassDefinition>()!;
            Assert.Contains(Validate(definition), error => error.Contains(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void AllowsUnrelatedForwardCompatibleMetadata()
    {
        ClassDefinition definition = CompleteStarter();
        definition.Starter!.Items[0] = JsonConvert.DeserializeObject<StarterItemDefinition>(
            "{gearBaseCode:'vrpg:test-0',equipmentRole:'weapon.main',editorNote:'future metadata'}")!;
        Assert.Empty(Validate(definition));
    }

    [Fact]
    public void RejectsNullItemsWithoutThrowing()
    {
        ClassDefinition definition = CompleteStarter();
        definition.Starter!.Items[0] = null!;
        Assert.NotEmpty(Validate(definition));
        definition.Starter.Items = null!;
        Assert.NotEmpty(Validate(definition));
    }

    private static List<string> Validate(ClassDefinition definition, GearBaseDefinition[]? bases = null,
        Func<string, bool>? assetExists = null, SkillDefinition? skill = null)
    {
        var errors = new List<string>();
        StarterLoadoutDefinitionValidator.Validate(definition,
            new[] { skill ?? new SkillDefinition { Code = "vrpg:hammer-blow", ClassCode = "vrpg:smith", RequiredLevel = 1 } },
            bases ?? Bases(), assetExists ?? (_ => true), errors);
        return errors;
    }

    private static GearBaseDefinition[] Bases() => new[]
        { "weapon.main", "armor.head", "armor.body", "armor.legs", "armor.hands", "armor.feet" }
        .Select((role, index) => new GearBaseDefinition
        {
            Code = "vrpg:test-" + index, Name = "Test item", EquipmentRole = role, VisualChassis = "game:test-" + index
        }).ToArray();

    private static ClassDefinition CompleteStarter() => new()
    {
        Code = "vrpg:smith", Name = "Smith",
        Starter = new StarterLoadoutDefinition
        {
            CombatPattern = "Break enemy footing with deliberate hammer blows.",
            DefensiveApproach = "Stay close and rely on sturdy armor.",
            PrimaryAttribute = "strength", SkillCode = "vrpg:hammer-blow",
            Items = Bases().Select(gearBase => new StarterItemDefinition
            {
                GearBaseCode = gearBase.Code, EquipmentRole = gearBase.EquipmentRole
            }).ToArray()
        }
    };
}
