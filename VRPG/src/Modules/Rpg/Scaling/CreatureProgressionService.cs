using System;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Players;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;

namespace VRPG.Modules.Rpg.Scaling;

public sealed class CreatureProgressionService
{
    private readonly ICoreServerAPI api;
    private readonly RpgScalingService scaling;
    private readonly RpgResourceService resources;

    public CreatureProgressionService(ICoreServerAPI api, RpgScalingService scaling, RpgResourceService resources)
    {
        this.api = api;
        this.scaling = scaling;
        this.resources = resources;
    }

    public void Start()
    {
        api.Event.OnEntitySpawn += OnEntitySpawnedOrLoaded;
        api.Event.OnEntityLoaded += OnEntitySpawnedOrLoaded;
        api.Event.OnEntityDeath += OnEntityDeath;
    }

    public void Stop()
    {
        api.Event.OnEntitySpawn -= OnEntitySpawnedOrLoaded;
        api.Event.OnEntityLoaded -= OnEntitySpawnedOrLoaded;
        api.Event.OnEntityDeath -= OnEntityDeath;
    }

    private void OnEntitySpawnedOrLoaded(Entity entity)
    {
        if (!CanScale(entity))
        {
            return;
        }

        scaling.AssignCreature(entity);
    }

    private void OnEntityDeath(Entity entity, DamageSource damageSource)
    {
        if (!CanScale(entity))
        {
            return;
        }

        int level = Math.Max(1, entity.WatchedAttributes.GetInt("vrpgLevel", 1));
        string rarityCode = entity.WatchedAttributes.GetString("vrpgRarity", "ordinary");
        CreatureRarityScalingDefinition rarity = scaling.FindRarity(rarityCode);
        long experience = scaling.CreatureExperience(level, rarity);
        AwardExperience(entity, damageSource, experience);
    }

    private void AwardExperience(Entity entity, DamageSource damageSource, long experience)
    {
        if (experience <= 0)
        {
            return;
        }

        Entity? cause = damageSource?.GetCauseEntity();
        IServerPlayer? killer = PlayerFor(cause);
        IServerPlayer[] receivers = NearbyReceivers(entity, killer);
        if (receivers.Length == 0)
        {
            return;
        }

        float groupBonus = GroupBonus(receivers.Length);
        long total = Math.Max(1, (long)Math.Round(experience * groupBonus));
        long each = Math.Max(1, total / receivers.Length);
        for (int i = 0; i < receivers.Length; i++)
        {
            resources.AddExperience(receivers[i], each, scaling.MaxPlayerLevel, scaling.ExperienceToNextLevel);
        }
    }

    private IServerPlayer[] NearbyReceivers(Entity entity, IServerPlayer? killer)
    {
        const double radius = 48.0;
        const double radiusSq = radius * radius;
        IPlayer[] players = api.World.AllOnlinePlayers;
        var receivers = new System.Collections.Generic.List<IServerPlayer>();

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

            if (player.Entity.Pos.XYZ.SquareDistanceTo(entity.Pos.XYZ) <= radiusSq)
            {
                receivers.Add(player);
            }
        }

        if (receivers.Count == 0 && killer != null)
        {
            receivers.Add(killer);
        }

        return receivers.ToArray();
    }

    private IServerPlayer? PlayerFor(Entity? entity)
    {
        if (entity is not EntityPlayer playerEntity)
        {
            return null;
        }

        IPlayer[] players = api.World.AllOnlinePlayers;
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i] is IServerPlayer player && player.Entity?.EntityId == playerEntity.EntityId)
            {
                return player;
            }
        }

        return null;
    }

    private static float GroupBonus(int players)
    {
        switch (Math.Max(1, players))
        {
            case 1: return 1.00f;
            case 2: return 1.20f;
            case 3: return 1.35f;
            case 4: return 1.45f;
            default: return 1.50f;
        }
    }

    private static bool CanScale(Entity entity)
    {
        if (entity == null || entity is EntityPlayer || entity is not EntityAgent || !entity.IsInteractable)
        {
            return false;
        }

        return entity.WatchedAttributes.GetTreeAttribute("health") != null;
    }
}
