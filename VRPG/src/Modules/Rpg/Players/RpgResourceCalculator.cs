using System;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Inventory.Gear;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Talents;

namespace VRPG.Modules.Rpg.Players;

public sealed class RpgResourceCalculator
{
    private readonly RpgModuleConfig config;
    private readonly TalentTreeCatalog talents;

    public RpgResourceCalculator(RpgModuleConfig config, TalentTreeCatalog talents)
    {
        this.config = config;
        this.talents = talents;
    }

    public RpgResourceMaximums CalculateMaximums(RpgPlayerState state, EquippedGearStats? gear = null)
    {
        gear = gear?.Valid == true ? gear : EquippedGearStats.Empty;
        var totals = new ResourceTotals(
            Math.Max(1d, config.Resources.BaseMaxHealth) + gear.MaximumHealth,
            Math.Max(1f, config.Resources.BaseMaxMana),
            0f,
            Math.Max(0d, config.Resources.BaseMaxMagicShield) + gear.MaximumMagicShield);

        ApplyMaximumLevelGrowth(totals, state);
        ApplyMaximumAttributeScaling(totals, state, gear);
        ApplyTalentModifiers(totals, state, ResourceTotalKind.Maximum);
        foreach (StatModifierDefinition modifier in gear.Modifiers) ApplyModifier(totals, modifier, ResourceTotalKind.Maximum);

        double health = totals.Health.Final();
        double bloodBase = health * Math.Max(0d, config.Resources.BloodMaxHealthRatio);

        return new RpgResourceMaximums(
            StatValueMath.NonNegativeFloat(health, 1f),
            StatValueMath.NonNegativeFloat(totals.Mana.Final(), 1f),
            StatValueMath.NonNegativeFloat(totals.Blood.FinalFromBase(bloodBase), 1f),
            StatValueMath.NonNegativeFloat(totals.MagicShield.FinalAllowZero()));
    }

    public RpgResourceRegeneration CalculateRegeneration(RpgPlayerState state, EquippedGearStats? gear = null)
    {
        gear = gear?.Valid == true ? gear : EquippedGearStats.Empty;
        ResourceRegenConfig regen = config.Resources.Regen;
        var totals = new ResourceTotals(
            Math.Max(0f, regen.BaseHealthRegenPerSecond),
            Math.Max(0f, regen.BaseManaRegenPerSecond),
            0f,
            Math.Max(0f, regen.BaseMagicShieldRegenPerSecond));

        ApplyRegenerationLevelGrowth(totals, state);
        ApplyRegenerationAttributeScaling(totals, state, gear);
        ApplyTalentModifiers(totals, state, ResourceTotalKind.Regeneration);
        foreach (StatModifierDefinition modifier in gear.Modifiers) ApplyModifier(totals, modifier, ResourceTotalKind.Regeneration);

        double health = totals.Health.FinalAllowZero();
        double bloodBase = health * Math.Max(0d, config.Resources.BloodRegenHealthRatio);

        return new RpgResourceRegeneration(
            StatValueMath.NonNegativeFloat(health),
            StatValueMath.NonNegativeFloat(totals.Mana.FinalAllowZero()),
            StatValueMath.NonNegativeFloat(totals.Blood.FinalAllowZeroFromBase(bloodBase)),
            StatValueMath.NonNegativeFloat(totals.MagicShield.FinalAllowZero()));
    }

    private void ApplyMaximumLevelGrowth(ResourceTotals totals, RpgPlayerState state)
    {
        double levelSteps = Math.Max(0d, (double)state.Level - 1);
        ResourceLevelGrowthConfig growth = config.Resources.LevelGrowth;
        totals.Health.AddFlat(growth.MaxHealthPerLevel * levelSteps);
        totals.Mana.AddFlat(growth.MaxManaPerLevel * levelSteps);
        totals.Blood.AddFlat(growth.MaxBloodPerLevel * levelSteps);
        totals.MagicShield.AddFlat(growth.MaxMagicShieldPerLevel * levelSteps);
    }

    private void ApplyRegenerationLevelGrowth(ResourceTotals totals, RpgPlayerState state)
    {
        double levelSteps = Math.Max(0d, (double)state.Level - 1);
        ResourceLevelGrowthConfig growth = config.Resources.LevelGrowth;
        totals.Health.AddFlat(growth.HealthRegenPerLevel * levelSteps);
        totals.Mana.AddFlat(growth.ManaRegenPerLevel * levelSteps);
        totals.MagicShield.AddFlat(growth.MagicShieldRegenPerLevel * levelSteps);
    }

    private void ApplyMaximumAttributeScaling(ResourceTotals totals, RpgPlayerState state, EquippedGearStats gear)
    {
        AttributeResourceScalingConfig scaling = config.Resources.AttributeScaling;
        double strength = CoreAttributeResolver.GetEffectiveValue(state, "strength", gear);
        double intelligence = CoreAttributeResolver.GetEffectiveValue(state, "intelligence", gear);

        totals.Health.AddFlat(strength * scaling.HealthPerStrength);
        totals.Health.AddPercent(strength * scaling.HealthPercentPerStrength);

        totals.Mana.AddFlat(intelligence * scaling.ManaPerIntelligence);
        totals.Mana.AddPercent(intelligence * scaling.ManaPercentPerIntelligence);

        totals.MagicShield.AddFlat(intelligence * scaling.MagicShieldPerIntelligence);
        totals.MagicShield.AddPercent(intelligence * scaling.MagicShieldPercentPerIntelligence);

        totals.Blood.AddFlat(strength * scaling.BloodPerStrength);
        totals.Blood.AddPercent(strength * scaling.BloodPercentPerStrength);
    }

    private void ApplyRegenerationAttributeScaling(ResourceTotals totals, RpgPlayerState state, EquippedGearStats gear)
    {
        AttributeResourceScalingConfig scaling = config.Resources.AttributeScaling;
        double strength = CoreAttributeResolver.GetEffectiveValue(state, "strength", gear);
        double intelligence = CoreAttributeResolver.GetEffectiveValue(state, "intelligence", gear);

        totals.Health.AddFlat(strength * scaling.HealthRegenPerStrength);
        totals.Health.AddPercent(strength * scaling.HealthRegenPercentPerStrength);

        totals.Mana.AddFlat(intelligence * scaling.ManaRegenPerIntelligence);
        totals.Mana.AddPercent(intelligence * scaling.ManaRegenPercentPerIntelligence);

        totals.MagicShield.AddFlat(intelligence * scaling.MagicShieldRegenPerIntelligence);
        totals.MagicShield.AddPercent(intelligence * scaling.MagicShieldRegenPercentPerIntelligence);

        totals.Blood.AddFlat(strength * scaling.BloodRegenPerStrength);
        totals.Blood.AddPercent(strength * scaling.BloodRegenPercentPerStrength);
    }

    private void ApplyTalentModifiers(ResourceTotals totals, RpgPlayerState state, ResourceTotalKind kind)
    {
        for (int i = 0; i < state.Talents.Count; i++)
        {
            TalentNodeDefinition? talent = talents.Get(state.Talents[i]);
            if (talent == null)
            {
                continue;
            }

            for (int modifierIndex = 0; modifierIndex < talent.Modifiers.Length; modifierIndex++)
            {
                ApplyModifier(totals, talent.Modifiers[modifierIndex], kind);
            }
        }
    }

    private static void ApplyModifier(ResourceTotals totals, StatModifierDefinition modifier, ResourceTotalKind kind)
    {
        if (GearModifierContract.IsHealthPercentage(modifier.Stat))
        {
            if (kind == ResourceTotalKind.Maximum && modifier.Operation == GearModifierOperations.Add)
                totals.Health.AddPercent(StatValueMath.ModifierValue(modifier));
            return;
        }
        if (!TryGetResourceTotal(totals, modifier.Stat, kind, out ResourceTotal? total) || total == null)
        {
            return;
        }

        total.Add(modifier.Operation, StatValueMath.ModifierValue(modifier));
    }

    private static bool TryGetResourceTotal(ResourceTotals totals, string stat, ResourceTotalKind kind, out ResourceTotal? total)
    {
        GearResourceStat resource = kind == ResourceTotalKind.Maximum
            ? GearModifierContract.MaximumResource(stat)
            : GearModifierContract.RegenerationResource(stat);
        total = resource switch
        {
            GearResourceStat.Health => totals.Health,
            GearResourceStat.Mana => totals.Mana,
            GearResourceStat.Blood => totals.Blood,
            GearResourceStat.MagicShield => totals.MagicShield,
            _ => null
        };
        return total != null;
    }

    private sealed class ResourceTotals
    {
        public ResourceTotals(double health, double mana, double blood, double magicShield)
        {
            Health = new ResourceTotal(health);
            Mana = new ResourceTotal(mana);
            Blood = new ResourceTotal(blood);
            MagicShield = new ResourceTotal(magicShield);
        }

        public ResourceTotal Health { get; }
        public ResourceTotal Mana { get; }
        public ResourceTotal Blood { get; }
        public ResourceTotal MagicShield { get; }
    }

    private sealed class ResourceTotal
    {
        private readonly double baseValue;
        private readonly StatModifierAccumulator modifiers = new StatModifierAccumulator();

        public ResourceTotal(double baseValue)
        {
            this.baseValue = baseValue;
        }

        public void AddFlat(double value)
        {
            modifiers.Add(StatModifierOperations.Add, value);
        }

        public void AddPercent(double value)
        {
            modifiers.Add(StatModifierOperations.Increased, value);
        }

        public void Add(string? operation, double value)
        {
            modifiers.Add(operation, value);
        }

        public double Final()
        {
            return FinalFromBase(baseValue);
        }

        public double FinalAllowZero()
        {
            return FinalAllowZeroFromBase(baseValue);
        }

        public double FinalFromBase(double value)
        {
            return Math.Max(1f, FinalAllowZeroFromBase(value));
        }

        public double FinalAllowZeroFromBase(double value)
        {
            return modifiers.Resolve(value);
        }
    }

    private enum ResourceTotalKind
    {
        Maximum,
        Regeneration
    }
}

public readonly struct RpgResourceMaximums
{
    public RpgResourceMaximums(float health, float mana, float blood, float magicShield)
    {
        Health = health;
        Mana = mana;
        Blood = blood;
        MagicShield = magicShield;
    }

    public float Health { get; }
    public float Mana { get; }
    public float Blood { get; }
    public float MagicShield { get; }
}

public readonly struct RpgResourceRegeneration
{
    public RpgResourceRegeneration(float health, float mana, float blood, float magicShield)
    {
        Health = health;
        Mana = mana;
        Blood = blood;
        MagicShield = magicShield;
    }

    public float Health { get; }
    public float Mana { get; }
    public float Blood { get; }
    public float MagicShield { get; }
}
