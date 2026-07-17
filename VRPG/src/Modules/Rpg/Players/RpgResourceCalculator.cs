using System;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
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

    public RpgResourceMaximums CalculateMaximums(RpgPlayerState state)
    {
        var totals = new ResourceTotals(
            Math.Max(1f, config.Resources.BaseMaxHealth),
            Math.Max(1f, config.Resources.BaseMaxMana),
            0f,
            Math.Max(0f, config.Resources.BaseMaxMagicShield));

        ApplyMaximumLevelGrowth(totals, state);
        ApplyMaximumAttributeScaling(totals, state);
        ApplyTalentModifiers(totals, state, ResourceTotalKind.Maximum);

        float health = totals.Health.Final();
        float bloodBase = health * Math.Max(0f, config.Resources.BloodMaxHealthRatio);

        return new RpgResourceMaximums(
            health,
            totals.Mana.Final(),
            totals.Blood.FinalFromBase(bloodBase),
            totals.MagicShield.FinalAllowZero());
    }

    public RpgResourceRegeneration CalculateRegeneration(RpgPlayerState state)
    {
        ResourceRegenConfig regen = config.Resources.Regen;
        var totals = new ResourceTotals(
            Math.Max(0f, regen.BaseHealthRegenPerSecond),
            Math.Max(0f, regen.BaseManaRegenPerSecond),
            0f,
            Math.Max(0f, regen.BaseMagicShieldRegenPerSecond));

        ApplyRegenerationLevelGrowth(totals, state);
        ApplyRegenerationAttributeScaling(totals, state);
        ApplyTalentModifiers(totals, state, ResourceTotalKind.Regeneration);

        float health = totals.Health.FinalAllowZero();
        float bloodBase = health * Math.Max(0f, config.Resources.BloodRegenHealthRatio);

        return new RpgResourceRegeneration(
            health,
            totals.Mana.FinalAllowZero(),
            totals.Blood.FinalAllowZeroFromBase(bloodBase),
            totals.MagicShield.FinalAllowZero());
    }

    private void ApplyMaximumLevelGrowth(ResourceTotals totals, RpgPlayerState state)
    {
        int levelSteps = Math.Max(0, state.Level - 1);
        ResourceLevelGrowthConfig growth = config.Resources.LevelGrowth;
        totals.Health.AddFlat(growth.MaxHealthPerLevel * levelSteps);
        totals.Mana.AddFlat(growth.MaxManaPerLevel * levelSteps);
        totals.Blood.AddFlat(growth.MaxBloodPerLevel * levelSteps);
        totals.MagicShield.AddFlat(growth.MaxMagicShieldPerLevel * levelSteps);
    }

    private void ApplyRegenerationLevelGrowth(ResourceTotals totals, RpgPlayerState state)
    {
        int levelSteps = Math.Max(0, state.Level - 1);
        ResourceLevelGrowthConfig growth = config.Resources.LevelGrowth;
        totals.Health.AddFlat(growth.HealthRegenPerLevel * levelSteps);
        totals.Mana.AddFlat(growth.ManaRegenPerLevel * levelSteps);
        totals.MagicShield.AddFlat(growth.MagicShieldRegenPerLevel * levelSteps);
    }

    private void ApplyMaximumAttributeScaling(ResourceTotals totals, RpgPlayerState state)
    {
        AttributeResourceScalingConfig scaling = config.Resources.AttributeScaling;
        int strength = GetStatPoints(state, "vrpg:strength");
        int intelligence = GetStatPoints(state, "vrpg:intelligence");

        totals.Health.AddFlat(strength * scaling.HealthPerStrength);
        totals.Health.AddPercent(strength * scaling.HealthPercentPerStrength);

        totals.Mana.AddFlat(intelligence * scaling.ManaPerIntelligence);
        totals.Mana.AddPercent(intelligence * scaling.ManaPercentPerIntelligence);

        totals.MagicShield.AddFlat(intelligence * scaling.MagicShieldPerIntelligence);
        totals.MagicShield.AddPercent(intelligence * scaling.MagicShieldPercentPerIntelligence);

        totals.Blood.AddFlat(strength * scaling.BloodPerStrength);
        totals.Blood.AddPercent(strength * scaling.BloodPercentPerStrength);
    }

    private void ApplyRegenerationAttributeScaling(ResourceTotals totals, RpgPlayerState state)
    {
        AttributeResourceScalingConfig scaling = config.Resources.AttributeScaling;
        int strength = GetStatPoints(state, "vrpg:strength");
        int intelligence = GetStatPoints(state, "vrpg:intelligence");

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
        if (!TryGetResourceTotal(totals, modifier.Stat, kind, out ResourceTotal? total) || total == null)
        {
            return;
        }

        float value = modifier.Max != 0f || modifier.Min != 0f ? (modifier.Min + modifier.Max) / 2f : 0f;
        total.Add(modifier.Operation, value);
    }

    private int GetStatPoints(RpgPlayerState state, string stat)
    {
        if (state.BaseStats.TryGetValue(stat, out int value))
        {
            return value;
        }

        string shortCode = stat.StartsWith("vrpg:", StringComparison.OrdinalIgnoreCase) ? stat.Substring("vrpg:".Length) : stat;
        return state.BaseStats.TryGetValue(shortCode, out value) ? value : 0;
    }

    private static bool TryGetResourceTotal(ResourceTotals totals, string stat, ResourceTotalKind kind, out ResourceTotal? total)
    {
        string normalized = NormalizeStat(stat);
        switch (kind)
        {
            case ResourceTotalKind.Maximum:
                return TryGetMaximumTotal(totals, normalized, out total);
            case ResourceTotalKind.Regeneration:
                return TryGetRegenerationTotal(totals, normalized, out total);
        }

        total = null;
        return false;
    }

    private static bool TryGetMaximumTotal(ResourceTotals totals, string normalized, out ResourceTotal? total)
    {
        switch (normalized)
        {
            case "maxhealth":
            case "health":
                total = totals.Health;
                return true;
            case "maxmana":
            case "mana":
                total = totals.Mana;
                return true;
            case "maxblood":
            case "blood":
                total = totals.Blood;
                return true;
            case "maxmagicshield":
            case "magicshield":
            case "shield":
                total = totals.MagicShield;
                return true;
            default:
                total = null;
                return false;
        }
    }

    private static bool TryGetRegenerationTotal(ResourceTotals totals, string normalized, out ResourceTotal? total)
    {
        switch (normalized)
        {
            case "healthregen":
            case "healthregeneration":
            case "hpregen":
                total = totals.Health;
                return true;
            case "manaregen":
            case "manaregeneration":
            case "mpregen":
                total = totals.Mana;
                return true;
            case "bloodregen":
            case "bloodregeneration":
                total = totals.Blood;
                return true;
            case "magicshieldregen":
            case "magicshieldregeneration":
            case "shieldregen":
            case "shieldregeneration":
                total = totals.MagicShield;
                return true;
            default:
                total = null;
                return false;
        }
    }

    private static string NormalizeStat(string stat)
    {
        string value = (stat ?? "").Trim().ToLowerInvariant();
        if (value.StartsWith("vrpg:", StringComparison.Ordinal))
        {
            value = value.Substring("vrpg:".Length);
        }

        return value.Replace("_", "").Replace("-", "").Replace(" ", "");
    }

    private sealed class ResourceTotals
    {
        public ResourceTotals(float health, float mana, float blood, float magicShield)
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
        private readonly float baseValue;
        private readonly StatModifierAccumulator modifiers = new StatModifierAccumulator();

        public ResourceTotal(float baseValue)
        {
            this.baseValue = baseValue;
        }

        public void AddFlat(float value)
        {
            modifiers.Add(StatModifierOperations.Add, value);
        }

        public void AddPercent(float value)
        {
            modifiers.Add(StatModifierOperations.Increased, value);
        }

        public void Add(string? operation, float value)
        {
            modifiers.Add(operation, value);
        }

        public float Final()
        {
            return FinalFromBase(baseValue);
        }

        public float FinalAllowZero()
        {
            return FinalAllowZeroFromBase(baseValue);
        }

        public float FinalFromBase(float value)
        {
            return Math.Max(1f, FinalAllowZeroFromBase(value));
        }

        public float FinalAllowZeroFromBase(float value)
        {
            return (float)modifiers.Resolve(value);
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
