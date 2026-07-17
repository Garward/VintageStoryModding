using System;
using System.Linq;
using VRPG.Data;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Scaling;

public sealed class RpgScalingService
{
    private const string HealthTreeName = "health";
    private readonly ICoreServerAPI api;
    private readonly VRPGDataRegistry data;
    private readonly RpgPlayerStore playerStore;
    private readonly Random random = new Random();

    public RpgScalingService(ICoreServerAPI api, VRPGDataRegistry data, RpgPlayerStore playerStore)
    {
        this.api = api;
        this.data = data;
        this.playerStore = playerStore;
    }

    public ScalingDefinition Scaling => data.Scaling.Get("vrpg:default") ?? data.Scaling.All.FirstOrDefault() ?? new ScalingDefinition();

    public int MaxPlayerLevel => Math.Max(1, Scaling.MaxPlayerLevel);
    public int MaxCreatureLevel => Math.Max(1, Scaling.MaxCreatureLevel);

    public long ExperienceToNextLevel(int level)
    {
        ScalingDefinition scaling = Scaling;
        int clampedLevel = Math.Clamp(level, 1, Math.Max(1, scaling.MaxPlayerLevel));
        PlayerLevelScalingDefinition player = scaling.PlayerLevels;
        double growth = Math.Pow(Math.Max(1.0, player.ExperienceGrowth), clampedLevel - 1);
        double value = Math.Max(1, player.BaseExperienceToNextLevel) * growth + Math.Max(0, player.ExperienceLinearPerLevel) * (clampedLevel - 1);
        return Math.Max(1, (long)Math.Round(value));
    }

    public long CreatureExperience(int level, CreatureRarityScalingDefinition rarity)
    {
        return ScalingMath.CreatureExperience(Scaling, level, rarity);
    }

    public float CreatureHealthMultiplier(int level, CreatureRarityScalingDefinition rarity)
    {
        return ScalingMath.CreatureHealthMultiplier(Scaling, level, rarity);
    }

    public float CreatureDamageMultiplier(int level, CreatureRarityScalingDefinition rarity)
    {
        return ScalingMath.CreatureDamageMultiplier(Scaling, level, rarity);
    }

    public float WeaponBaseDamage(int level, float baseDamage = 0f, float rarityMultiplier = 1f)
    {
        return ScalingMath.WeaponBaseDamage(Scaling, level, baseDamage, rarityMultiplier);
    }

    public CreatureScalingResult AssignCreature(Entity entity)
    {
        int existingLevel = entity.WatchedAttributes.GetInt("vrpgLevel", 0);
        string existingRarity = entity.WatchedAttributes.GetString("vrpgRarity", "");
        CreatureRarityScalingDefinition rarity = FindRarity(existingRarity) ?? RollRarity(existingLevel > 0 ? existingLevel : 1);
        int level = existingLevel > 0 ? existingLevel : CalculateCreatureLevel(entity);
        rarity = FindRarity(existingRarity) ?? RollRarity(level);

        ApplyCreatureAttributes(entity, level, rarity);
        ApplyCreatureHealth(entity, level, rarity);

        return new CreatureScalingResult(level, rarity.Code, rarity.Name, rarity.AffixSlots);
    }

    public int CalculateCreatureLevel(Entity entity)
    {
        ScalingDefinition scaling = Scaling;
        CreatureLevelScalingDefinition levels = scaling.CreatureLevels;

        int distanceLevel = DistanceLevel(entity, levels);
        int depthLevel = DepthLevel(entity, levels);
        bool rift = IsRift(entity);
        int areaLevel = Math.Max(1, distanceLevel + depthLevel + (rift ? Math.Max(0, levels.RiftBaseLevel - 1) : 0));
        areaLevel = Math.Clamp(areaLevel, 1, MaxCreatureLevel);

        if (!levels.OpenWorldNearbyPlayerLevelCap || (rift && !levels.RiftUsesNearbyPlayerCap))
        {
            return areaLevel;
        }

        int playerLevel = HighestNearbyPlayerLevel(entity, Math.Max(1, levels.NearbyPlayerRadius));
        if (playerLevel <= 0)
        {
            return areaLevel;
        }

        int allowed = IsUnderground(entity, levels) ? levels.UndergroundAllowedOverlevel : levels.SurfaceAllowedOverlevel;
        if (rift)
        {
            allowed = levels.RiftAllowedOverlevel;
        }

        return Math.Clamp(Math.Min(areaLevel, playerLevel + Math.Max(0, allowed)), 1, MaxCreatureLevel);
    }

    public CreatureRarityScalingDefinition FindRarity(string code)
    {
        ScalingDefinition scaling = Scaling;
        for (int i = 0; i < scaling.CreatureRarities.Length; i++)
        {
            CreatureRarityScalingDefinition rarity = scaling.CreatureRarities[i];
            if (string.Equals(rarity.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return rarity;
            }
        }

        return scaling.CreatureRarities.FirstOrDefault() ?? new CreatureRarityScalingDefinition { Code = "ordinary", Name = "Ordinary", Weight = 1 };
    }

    private CreatureRarityScalingDefinition RollRarity(int level)
    {
        CreatureRarityScalingDefinition[] eligible = Scaling.CreatureRarities
            .Where(rarity => level >= Math.Max(1, rarity.MinLevel) && Math.Max(0, rarity.Weight) > 0)
            .ToArray();

        if (eligible.Length == 0)
        {
            return FindRarity("ordinary");
        }

        int total = eligible.Sum(rarity => Math.Max(0, rarity.Weight));
        int roll = random.Next(Math.Max(1, total));
        int cursor = 0;
        for (int i = 0; i < eligible.Length; i++)
        {
            cursor += Math.Max(0, eligible[i].Weight);
            if (roll < cursor)
            {
                return eligible[i];
            }
        }

        return eligible[0];
    }

    private void ApplyCreatureAttributes(Entity entity, int level, CreatureRarityScalingDefinition rarity)
    {
        entity.WatchedAttributes.SetInt("vrpgLevel", Math.Clamp(level, 1, MaxCreatureLevel));
        entity.WatchedAttributes.SetString("vrpgRarity", rarity.Code ?? "ordinary");
        entity.WatchedAttributes.SetString("vrpgRarityName", string.IsNullOrWhiteSpace(rarity.Name) ? rarity.Code : rarity.Name);
        entity.WatchedAttributes.SetInt("vrpgAffixSlots", Math.Max(0, rarity.AffixSlots));
        entity.WatchedAttributes.SetFloat("vrpgDamageMultiplier", CreatureDamageMultiplier(level, rarity));
        entity.WatchedAttributes.MarkPathDirty("vrpgLevel");
        entity.WatchedAttributes.MarkPathDirty("vrpgRarity");
        entity.WatchedAttributes.MarkPathDirty("vrpgRarityName");
        entity.WatchedAttributes.MarkPathDirty("vrpgAffixSlots");
        entity.WatchedAttributes.MarkPathDirty("vrpgDamageMultiplier");
    }

    private void ApplyCreatureHealth(Entity entity, int level, CreatureRarityScalingDefinition rarity)
    {
        ITreeAttribute? healthTree = entity.WatchedAttributes.GetTreeAttribute(HealthTreeName);
        if (healthTree == null)
        {
            return;
        }

        float currentMax = Math.Max(1f, healthTree.GetFloat("maxhealth", healthTree.GetFloat("basemaxhealth", 20f)));
        float baseMax = entity.WatchedAttributes.GetFloat("vrpgBaseMaxHealth", 0f);
        if (baseMax <= 0f)
        {
            baseMax = currentMax;
            entity.WatchedAttributes.SetFloat("vrpgBaseMaxHealth", baseMax);
        }

        float desiredMax = Math.Max(1f, baseMax * CreatureHealthMultiplier(level, rarity));
        float currentHealth = healthTree.GetFloat("currenthealth", currentMax);
        float percent = currentMax > 0f ? Math.Clamp(currentHealth / currentMax, 0f, 1f) : 1f;
        bool firstScale = entity.WatchedAttributes.GetBool("vrpgScaledHealth", false) == false;

        healthTree.SetFloat("basemaxhealth", desiredMax);
        healthTree.SetFloat("maxhealth", desiredMax);
        healthTree.SetFloat("currenthealth", firstScale ? desiredMax : Math.Max(1f, desiredMax * percent));
        entity.WatchedAttributes.SetBool("vrpgScaledHealth", true);
        entity.WatchedAttributes.MarkPathDirty(HealthTreeName);
        entity.WatchedAttributes.MarkPathDirty("vrpgBaseMaxHealth");
        entity.WatchedAttributes.MarkPathDirty("vrpgScaledHealth");
    }

    private int HighestNearbyPlayerLevel(Entity entity, int radius)
    {
        int highest = 0;
        double radiusSq = radius * radius;
        IPlayer[] players = api.World.AllOnlinePlayers;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is not IServerPlayer player || player.Entity == null || !player.Entity.Alive)
            {
                continue;
            }

            if (player.Entity.Pos.Dimension != entity.Pos.Dimension)
            {
                continue;
            }

            if (player.Entity.Pos.XYZ.SquareDistanceTo(entity.Pos.XYZ) > radiusSq)
            {
                continue;
            }

            highest = Math.Max(highest, Math.Max(1, playerStore.GetOrCreate(player).Level));
        }

        return highest;
    }

    private int DistanceLevel(Entity entity, CreatureLevelScalingDefinition levels)
    {
        EntityPos spawn = api.World.DefaultSpawnPosition;
        double dx = entity.Pos.X - spawn.X;
        double dz = entity.Pos.Z - spawn.Z;
        double distance = Math.Sqrt(dx * dx + dz * dz);
        return 1 + (int)Math.Floor(distance / Math.Max(1, levels.SpawnDistanceBlocksPerLevel));
    }

    private static int DepthLevel(Entity entity, CreatureLevelScalingDefinition levels)
    {
        int below = Math.Max(0, Math.Max(1, levels.UndergroundStartsBelowY) - (int)Math.Floor(entity.Pos.Y));
        return below / Math.Max(1, levels.DepthBlocksPerLevel);
    }

    private static bool IsUnderground(Entity entity, CreatureLevelScalingDefinition levels)
    {
        return entity.Pos.Y < Math.Max(1, levels.UndergroundStartsBelowY);
    }

    private static bool IsRift(Entity entity)
    {
        return entity.Pos.Dimension != 0;
    }
}

public readonly struct CreatureScalingResult
{
    public CreatureScalingResult(int level, string rarityCode, string rarityName, int affixSlots)
    {
        Level = level;
        RarityCode = rarityCode;
        RarityName = rarityName;
        AffixSlots = affixSlots;
    }

    public int Level { get; }
    public string RarityCode { get; }
    public string RarityName { get; }
    public int AffixSlots { get; }
}
