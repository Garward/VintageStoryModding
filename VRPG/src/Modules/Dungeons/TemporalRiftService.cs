using System;
using System.Collections.Generic;
using VRPG.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Dungeons;

public sealed class TemporalRiftService
{
    private readonly ICoreServerAPI api;
    private readonly DungeonModuleConfig config;
    private readonly bool manifoldRegistered;
    private readonly List<ActiveBreach> breaches = new List<ActiveBreach>();
    private readonly Random random = new Random();
    private long tickListenerId;

    public TemporalRiftService(ICoreServerAPI api, DungeonModuleConfig config, bool manifoldRegistered)
    {
        this.api = api;
        this.config = config;
        this.manifoldRegistered = manifoldRegistered;
    }

    public static TemporalRiftService? Current { get; private set; }

    public void Start()
    {
        Current = this;
        tickListenerId = api.Event.RegisterGameTickListener(OnTick, 1000);
    }

    public void Stop()
    {
        if (Current == this)
        {
            Current = null;
        }

        if (tickListenerId != 0)
        {
            api.Event.UnregisterGameTickListener(tickListenerId);
            tickListenerId = 0;
        }

        breaches.Clear();
    }

    public bool TryOpenRiftChart(EntityAgent byEntity, BlockSelection blockSel, ItemSlot slot, out string message)
    {
        message = "";
        if (!config.RiftCharts.Enabled)
        {
            message = "Rift Charts are inert in this world.";
            return false;
        }

        IServerPlayer? player = PlayerFor(byEntity);
        if (player == null || blockSel == null)
        {
            message = "The chart needs a marked place in the world.";
            return false;
        }

        BlockPos anchorBlock = AnchorBlock(blockSel);
        if (!api.World.Claims.TryAccess(player, anchorBlock, EnumBlockAccessFlags.BuildOrBreak))
        {
            message = "The chart refuses to cut into a claimed place.";
            return false;
        }

        if (breaches.Count >= Math.Max(1, config.RiftCharts.MaxActiveBreaches))
        {
            message = "Too many temporal cracks are already open.";
            return false;
        }

        int level = LevelFor(slot);
        if (manifoldRegistered && !config.RiftCharts.UseBreachFallback)
        {
            message = "The chart locks onto " + config.DisplayName + ". The doorway handoff is not enabled on this chart.";
            return false;
        }

        Vec3d anchor = new Vec3d(anchorBlock.X + 0.5, anchorBlock.Y + 0.1, anchorBlock.Z + 0.5);
        breaches.Add(new ActiveBreach(anchorBlock, anchor, level, player.PlayerUID, player.PlayerName));
        EmitCrack(anchor);
        ConsumeChart(player, slot);
        message = manifoldRegistered
            ? "The chart opens a controlled crack while the rift machinery warms."
            : "The chart tears open a temporal crack.";
        return true;
    }

    private void OnTick(float dt)
    {
        for (int i = breaches.Count - 1; i >= 0; i--)
        {
            ActiveBreach breach = breaches[i];
            breach.ElapsedSeconds++;
            EmitCrack(breach.Anchor);

            if (breach.ElapsedSeconds == 1 || breach.ElapsedSeconds >= breach.NextWaveSecond)
            {
                SpawnWave(breach);
                breach.WavesSpawned++;
                breach.NextWaveSecond = breach.ElapsedSeconds + Math.Max(1, config.RiftCharts.SecondsBetweenWaves);
            }

            if (breach.WavesSpawned >= Math.Max(1, config.RiftCharts.Waves))
            {
                NotifyPlayer(breach.OwnerUid, "The temporal crack folds shut.");
                breaches.RemoveAt(i);
            }
            else
            {
                breaches[i] = breach;
            }
        }
    }

    private void SpawnWave(ActiveBreach breach)
    {
        int count = Math.Max(1, config.RiftCharts.MobsPerWave);
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (TrySpawnCreature(breach, i))
            {
                spawned++;
            }
        }

        NotifyPlayer(breach.OwnerUid, "The crack throws out " + spawned + " rust-touched shape" + (spawned == 1 ? "." : "s."));
    }

    private bool TrySpawnCreature(ActiveBreach breach, int index)
    {
        EntityProperties? type = PickEntityType();
        if (type == null)
        {
            api.Logger.Warning("[VRPG/Dungeons] Rift Chart could not find a configured entity to spawn.");
            return false;
        }

        Entity entity = api.World.ClassRegistry.CreateEntity(type);
        if (entity == null)
        {
            return false;
        }

        Vec3d pos = SpawnPosNear(breach.AnchorBlock, breach.Anchor, index);
        entity.Pos.X = pos.X;
        entity.Pos.Y = pos.Y;
        entity.Pos.Z = pos.Z;
        entity.Pos.Dimension = breach.AnchorBlock.dimension;
        entity.PositionBeforeFalling.Set(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
        entity.WatchedAttributes.SetInt("vrpgLevel", Math.Max(1, breach.Level));
        entity.WatchedAttributes.SetString("vrpgSpawnSource", "rift-chart");
        entity.WatchedAttributes.MarkPathDirty("vrpgLevel");
        entity.WatchedAttributes.MarkPathDirty("vrpgSpawnSource");

        api.World.SpawnEntity(entity);
        return true;
    }

    private EntityProperties? PickEntityType()
    {
        string[] codes = config.RiftCharts.EntityCodes ?? Array.Empty<string>();
        for (int i = 0; i < codes.Length; i++)
        {
            EntityProperties? type = api.World.GetEntityType(new AssetLocation(codes[i]));
            if (type != null)
            {
                return type;
            }
        }

        IList<EntityProperties> types = api.World.EntityTypes;
        for (int i = 0; i < types.Count; i++)
        {
            if (types[i].Code?.Path?.IndexOf("drifter", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return types[i];
            }
        }

        return null;
    }

    private Vec3d SpawnPosNear(BlockPos anchorBlock, Vec3d anchor, int index)
    {
        int radius = Math.Max(2, config.RiftCharts.SpawnRadius);
        double angle = random.NextDouble() * Math.PI * 2 + index;
        double distance = 2 + random.NextDouble() * Math.Max(1, radius - 2);
        double x = anchor.X + Math.Cos(angle) * distance;
        double z = anchor.Z + Math.Sin(angle) * distance;
        int y = api.World.BlockAccessor.GetTerrainMapheightAt(new BlockPos((int)x, 0, (int)z, anchorBlock.dimension));
        return new Vec3d(x, Math.Max(1, y + 1), z);
    }

    private int LevelFor(ItemSlot slot)
    {
        int level = Math.Max(1, config.RiftCharts.BaseLevel);
        if (slot.Itemstack?.Collectible?.Attributes != null)
        {
            level = Math.Max(level, slot.Itemstack.Collectible.Attributes["vrpgLevel"].AsInt(level));
        }

        return level;
    }

    private BlockPos AnchorBlock(BlockSelection blockSel)
    {
        BlockFacing face = blockSel.Face ?? BlockFacing.UP;
        int x = blockSel.Position.X + face.Normali.X;
        int y = blockSel.Position.Y + face.Normali.Y;
        int z = blockSel.Position.Z + face.Normali.Z;
        return new BlockPos(x, y, z, blockSel.Position.dimension);
    }

    private void EmitCrack(Vec3d anchor)
    {
        int color = ColorUtil.ColorFromRgba(143, 58, 255, 190);
        api.World.SpawnParticles(
            18,
            color,
            anchor.AddCopy(-0.45, 0.05, -0.45),
            anchor.AddCopy(0.45, 1.7, 0.45),
            new Vec3f(-0.04f, 0.02f, -0.04f),
            new Vec3f(0.04f, 0.18f, 0.04f),
            1.2f,
            -0.03f,
            0.32f);
    }

    private void ConsumeChart(IServerPlayer player, ItemSlot slot)
    {
        if (player.WorldData.CurrentGameMode == EnumGameMode.Creative)
        {
            return;
        }

        slot.TakeOut(1);
        slot.MarkDirty();
    }

    private IServerPlayer? PlayerFor(EntityAgent entity)
    {
        if (entity is not EntityPlayer playerEntity)
        {
            return null;
        }

        return api.World.PlayerByUid(playerEntity.PlayerUID) as IServerPlayer;
    }

    private void NotifyPlayer(string ownerUid, string message)
    {
        IPlayer player = api.World.PlayerByUid(ownerUid);
        if (player is IServerPlayer serverPlayer)
        {
            serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
        }
    }

    private struct ActiveBreach
    {
        public ActiveBreach(BlockPos anchorBlock, Vec3d anchor, int level, string ownerUid, string ownerName)
        {
            AnchorBlock = anchorBlock;
            Anchor = anchor;
            Level = level;
            OwnerUid = ownerUid;
            OwnerName = ownerName;
            ElapsedSeconds = 0;
            NextWaveSecond = 1;
            WavesSpawned = 0;
        }

        public BlockPos AnchorBlock { get; }
        public Vec3d Anchor { get; }
        public int Level { get; }
        public string OwnerUid { get; }
        public string OwnerName { get; }
        public int ElapsedSeconds { get; set; }
        public int NextWaveSecond { get; set; }
        public int WavesSpawned { get; set; }
    }
}
