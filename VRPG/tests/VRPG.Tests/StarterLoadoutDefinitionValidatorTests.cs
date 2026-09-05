using System;
using System.Collections.Generic;
using VRPG.Data;
using VRPG.Data.Definitions;
using Xunit;

namespace VRPG.Tests;

public sealed class StarterLoadoutDefinitionValidatorTests
{
    [Fact]
    public void AcceptsCompleteDataDefinedStarter()
    {
        ClassDefinition definition = CompleteStarter();
        var errors = new List<string>();

        StarterLoadoutDefinitionValidator.Validate(
            definition,
            new[] { Skill("vrpg:hammer-blow", "vrpg:smith", 1) },
            code => code is "game:hammer-iron" or "game:armor-body-jerkin-leather",
            errors);

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsMissingAssetsForeignSkillAndIncompletePreview()
    {
        ClassDefinition definition = CompleteStarter();
        definition.Starter!.CombatPattern = "";
        definition.Starter.Armor = Array.Empty<StarterItemDefinition>();
        var errors = new List<string>();

        StarterLoadoutDefinitionValidator.Validate(
            definition,
            new[] { Skill("vrpg:hammer-blow", "vrpg:warden", 2) },
            _ => false,
            errors);

        Assert.Contains(errors, error => error.Contains("combatPattern", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("armor item", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("does not exist", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("same class", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("level 1", StringComparison.Ordinal));
    }

    private static ClassDefinition CompleteStarter()
    {
        return new ClassDefinition
        {
            Code = "vrpg:smith",
            Name = "Smith",
            Starter = new StarterLoadoutDefinition
            {
                GrantSchemaVersion = 1,
                CombatPattern = "Break enemy footing with deliberate hammer blows.",
                DefensiveApproach = "Stay close and rely on sturdy armor.",
                PrimaryAttribute = "strength",
                Weapon = new StarterItemDefinition
                {
                    Code = "game:hammer-iron",
                    Name = "Iron hammer",
                    EquipmentRole = "mainweapon"
                },
                Armor = new[]
                {
                    new StarterItemDefinition
                    {
                        Code = "game:armor-body-jerkin-leather",
                        Name = "Leather jerkin",
                        EquipmentRole = "armorbody"
                    }
                },
                SkillCode = "vrpg:hammer-blow"
            }
        };
    }

    private static SkillDefinition Skill(string code, string classCode, int requiredLevel)
    {
        return new SkillDefinition
        {
            Code = code,
            Name = code,
            ClassCode = classCode,
            RequiredLevel = requiredLevel
        };
    }
}
