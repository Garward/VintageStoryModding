using System;
using Vintagestory.API.Common;

namespace VRPG.Config;

public sealed class VRPGConfig
{
    public VRPGModuleConfig Modules { get; set; } = new VRPGModuleConfig();
}

public sealed class VRPGModuleConfig
{
    public RpgModuleConfig Rpg { get; set; } = new RpgModuleConfig();
    public DungeonModuleConfig Dungeons { get; set; } = new DungeonModuleConfig();
}

public sealed class RpgModuleConfig
{
    public bool Enabled { get; set; } = true;
    public bool EnableDataCommands { get; set; } = true;
    public int BaseTalentPointsPerLevel { get; set; } = 1;
    public int BaseStatPointsPerLevel { get; set; } = 3;
    public float CombatStatLockSeconds { get; set; } = 8f;
    public float EvasiveStepCooldownSeconds { get; set; } = 6f;
    public float EvasiveStepInvulnerabilitySeconds { get; set; } = 0.38f;
    public float EvasiveStepImpulse { get; set; } = 0.28f;
    public RpgHudConfig Hud { get; set; } = new RpgHudConfig();
    public RpgSkillHotbarConfig SkillHotbar { get; set; } = new RpgSkillHotbarConfig();
    public RpgEntityHudConfig EntityHud { get; set; } = new RpgEntityHudConfig();
    public RpgResourceConfig Resources { get; set; } = new RpgResourceConfig();
    public VRPG.Client.Visuals.CombatVisualsConfig CombatVisuals { get; set; } = new VRPG.Client.Visuals.CombatVisualsConfig();
}

public sealed class RpgSkillHotbarConfig
{
    public bool Enabled { get; set; } = true;
    public bool Locked { get; set; } = true;
    public int SlotCount { get; set; } = 4;
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int SlotSize { get; set; } = 54;
    public int Gap { get; set; } = 7;
}

public sealed class RpgHudConfig
{
    public bool Enabled { get; set; } = true;
    public bool HideVanillaStatbar { get; set; } = true;
    public string Anchor { get; set; } = "left-bottom";
    public int X { get; set; } = 14;
    public int Y { get; set; } = 112;
    public int Width { get; set; } = 350;
    public int BarHeight { get; set; } = 22;
    public int Gap { get; set; } = 7;
    public bool ShowExperience { get; set; } = true;
    public bool ShowMagicShieldWhenEmpty { get; set; } = false;
    public bool ShowBloodWhenUnavailable { get; set; } = false;
}

public sealed class RpgResourceConfig
{
    public float BaseMaxHealth { get; set; } = 100f;
    public float BaseMaxMana { get; set; } = 100f;
    public float BaseMaxBlood { get; set; } = 100f;
    public float BaseMaxMagicShield { get; set; } = 0f;
    public float BloodMaxHealthRatio { get; set; } = 1f;
    public float BloodRegenHealthRatio { get; set; } = 1f;
    public long BaseExperienceToNextLevel { get; set; } = 1000;
    public ResourceRegenConfig Regen { get; set; } = new ResourceRegenConfig();
    public ResourceLevelGrowthConfig LevelGrowth { get; set; } = new ResourceLevelGrowthConfig();
    public AttributeResourceScalingConfig AttributeScaling { get; set; } = new AttributeResourceScalingConfig();
}

public sealed class ResourceRegenConfig
{
    public float BaseHealthRegenPerSecond { get; set; } = 1f;
    public float BaseManaRegenPerSecond { get; set; } = 1f;
    public float BaseMagicShieldRegenPerSecond { get; set; } = 1f;
}

public sealed class ResourceLevelGrowthConfig
{
    public float MaxHealthPerLevel { get; set; } = 0f;
    public float MaxManaPerLevel { get; set; } = 0f;
    public float MaxBloodPerLevel { get; set; } = 0f;
    public float MaxMagicShieldPerLevel { get; set; } = 0f;
    public float HealthRegenPerLevel { get; set; } = 0f;
    public float ManaRegenPerLevel { get; set; } = 0f;
    public float MagicShieldRegenPerLevel { get; set; } = 0f;
}

public sealed class AttributeResourceScalingConfig
{
    public float HealthPerStrength { get; set; } = 2.5f;
    public float HealthPercentPerStrength { get; set; } = 0f;
    public float HealthRegenPerStrength { get; set; } = 0.2f;
    public float HealthRegenPercentPerStrength { get; set; } = 0f;
    public float ManaPerIntelligence { get; set; } = 10f;
    public float ManaPercentPerIntelligence { get; set; } = 0f;
    public float ManaRegenPerIntelligence { get; set; } = 0f;
    public float ManaRegenPercentPerIntelligence { get; set; } = 1f;
    public float MagicShieldPerIntelligence { get; set; } = 0f;
    public float MagicShieldPercentPerIntelligence { get; set; } = 0.2f;
    public float MagicShieldRegenPerIntelligence { get; set; } = 0f;
    public float MagicShieldRegenPercentPerIntelligence { get; set; } = 0f;
    public float BloodPerStrength { get; set; } = 0f;
    public float BloodPercentPerStrength { get; set; } = 0f;
    public float BloodRegenPerStrength { get; set; } = 0f;
    public float BloodRegenPercentPerStrength { get; set; } = 0f;
}

public sealed class RpgEntityHudConfig
{
    public bool Enabled { get; set; } = true;
    public string Mode { get; set; } = "readable";
    public float Radius { get; set; } = 28f;
    public int MaxShown { get; set; } = 10;
    public float Scale { get; set; } = 1f;
    public bool ShowNames { get; set; } = true;
    public bool ShowLevels { get; set; } = true;
    public bool ShowNumbers { get; set; } = true;
    public bool ShowPercent { get; set; } = false;
    public bool ShowOnlyDamaged { get; set; } = false;
    public bool ShowPlayers { get; set; } = false;
    public bool ShowDead { get; set; } = false;
    public float VerticalOffset { get; set; } = 0.45f;
    public int RefreshIntervalMs { get; set; } = 100;
}

public sealed class DungeonModuleConfig
{
    public bool Enabled { get; set; } = true;
    public bool RequireManifold { get; set; } = true;
    public string DimensionCode { get; set; } = "vrpg:temporal_rift";
    public string DisplayName { get; set; } = "Temporal Rift";
    public string EntryItemName { get; set; } = "Rift Chart";
    public int SpawnX { get; set; } = 1024;
    public int SpawnY { get; set; } = 32;
    public int SpawnZ { get; set; } = 1024;
    public int GenerationRadiusChunks { get; set; } = 5;
    public bool Streaming { get; set; } = false;
    public int StreamingRadiusChunks { get; set; } = 5;
    public int StreamingBudgetColumnsPerTick { get; set; } = 8;
    public string FloorBlock { get; set; } = "game:rock-granite";
    public string WallBlock { get; set; } = "game:rock-andesite";
    public string AccentBlock { get; set; } = "game:rock-basalt";
    public RiftChartConfig RiftCharts { get; set; } = new RiftChartConfig();
}

public sealed class RiftChartConfig
{
    public bool Enabled { get; set; } = true;
    public bool UseBreachFallback { get; set; } = true;
    public int BaseLevel { get; set; } = 1;
    public int Waves { get; set; } = 4;
    public int SecondsBetweenWaves { get; set; } = 8;
    public int MobsPerWave { get; set; } = 3;
    public int SpawnRadius { get; set; } = 8;
    public int MaxActiveBreaches { get; set; } = 4;
    public string[] EntityCodes { get; set; } =
    {
        "game:drifter-normal",
        "game:drifter-deep",
        "game:drifter-tainted"
    };
}

public static class VRPGConfigStore
{
    public const string ConfigFileName = "vrpg.json";

    public static VRPGConfig Load(ICoreAPI api)
    {
        VRPGConfig config;
        try
        {
            config = api.LoadModConfig<VRPGConfig>(ConfigFileName) ?? new VRPGConfig();
        }
        catch (Exception ex)
        {
            api.Logger.Warning("[VRPG] Failed to load {0}; using defaults: {1}", ConfigFileName, ex.Message);
            config = new VRPGConfig();
        }

        Normalize(config);
        api.StoreModConfig(config, ConfigFileName);
        return config;
    }

    private static void Normalize(VRPGConfig config)
    {
        config.Modules.Rpg.SkillHotbar.SlotCount = Math.Clamp(config.Modules.Rpg.SkillHotbar.SlotCount, 4, 8);
        RpgHudConfig hud = config.Modules.Rpg.Hud;
        hud.ShowExperience = true;

        RpgEntityHudConfig entityHud = config.Modules.Rpg.EntityHud;
        bool oldPercentOnlyDefault = string.Equals(entityHud.Mode, "compact", StringComparison.OrdinalIgnoreCase)
            && !entityHud.ShowLevels
            && !entityHud.ShowNumbers
            && entityHud.ShowPercent;

        if (oldPercentOnlyDefault)
        {
            entityHud.Mode = "readable";
            entityHud.ShowLevels = true;
            entityHud.ShowNumbers = true;
            entityHud.ShowPercent = false;
        }
    }
}
