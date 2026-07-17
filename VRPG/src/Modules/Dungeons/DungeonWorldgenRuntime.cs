using System;
using System.Reflection;
using VRPG.Config;
using VRPG.Data;
using VRPG.Data.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Dungeons;

public sealed class DungeonWorldgenRuntime
{
    private readonly DungeonModuleConfig config;
    private readonly VRPGDataRegistry data;
    private int floorBlockId;
    private int wallBlockId;
    private int accentBlockId;
    private int floorY = 24;
    private int roomHeight = 7;
    private int doorWidth = 6;

    public DungeonWorldgenRuntime(DungeonModuleConfig config, VRPGDataRegistry data)
    {
        this.config = config;
        this.data = data;
    }

    public void OnInitialize(object context)
    {
        var api = GetProperty<ICoreServerAPI>(context, "Api");
        DungeonThemeDefinition? theme = data.DungeonThemes.Get("vrpg:granite_halls") ?? FirstTheme();

        string floor = FirstNonEmpty(config.FloorBlock, theme?.FloorBlock, "game:rock-granite");
        string wall = FirstNonEmpty(config.WallBlock, theme?.WallBlock, "game:rock-andesite");
        string accent = FirstNonEmpty(config.AccentBlock, theme?.AccentBlock, "game:rock-basalt");

        floorY = Math.Max(2, theme?.FloorY ?? 24);
        roomHeight = Math.Clamp(theme?.RoomHeight ?? 7, 4, 20);
        doorWidth = Math.Clamp(theme?.DoorWidth ?? 6, 2, 14);

        floorBlockId = ResolveBlock(api, floor);
        wallBlockId = ResolveBlock(api, wall);
        accentBlockId = ResolveBlock(api, accent);

        api.Logger.Notification(
            "[VRPG/Dungeons] Worldgen ready. floor={0} wall={1} accent={2} y={3} h={4}",
            floor,
            wall,
            accent,
            floorY,
            roomHeight);
    }

    public void GenerateColumn(object context)
    {
        if (floorBlockId == 0 || wallBlockId == 0)
        {
            return;
        }

        int dimensionId = GetProperty<int>(context, "DimensionId");
        int chunkX = GetProperty<int>(context, "ChunkX");
        int chunkZ = GetProperty<int>(context, "ChunkZ");
        var blockAccessor = GetProperty<IBlockAccessor>(context, "BlockAccessor");

        const int chunkSize = 32;
        int baseX = chunkX * chunkSize;
        int baseZ = chunkZ * chunkSize;
        int ceilingY = floorY + roomHeight;
        int center = chunkSize / 2;
        int halfDoor = Math.Max(1, doorWidth / 2);

        for (int lx = 0; lx < chunkSize; lx++)
        {
            for (int lz = 0; lz < chunkSize; lz++)
            {
                int wx = baseX + lx;
                int wz = baseZ + lz;
                bool borderX = lx == 0 || lx == chunkSize - 1;
                bool borderZ = lz == 0 || lz == chunkSize - 1;
                bool northSouthDoor = borderZ && Math.Abs(lx - center) <= halfDoor;
                bool eastWestDoor = borderX && Math.Abs(lz - center) <= halfDoor;
                bool wall = (borderX || borderZ) && !northSouthDoor && !eastWestDoor;
                bool pillar = IsPillar(lx, lz, chunkX, chunkZ);

                blockAccessor.SetBlock(floorBlockId, new BlockPos(wx, floorY, wz, dimensionId));
                blockAccessor.SetBlock(wallBlockId, new BlockPos(wx, ceilingY, wz, dimensionId));

                if (wall || pillar)
                {
                    int blockId = pillar && accentBlockId != 0 ? accentBlockId : wallBlockId;
                    for (int y = floorY + 1; y < ceilingY; y++)
                    {
                        blockAccessor.SetBlock(blockId, new BlockPos(wx, y, wz, dimensionId));
                    }
                }
            }
        }
    }

    private DungeonThemeDefinition? FirstTheme()
    {
        return data.DungeonThemes.All.Count > 0 ? data.DungeonThemes.All[0] : null;
    }

    private static bool IsPillar(int lx, int lz, int chunkX, int chunkZ)
    {
        int roomSeed = unchecked((chunkX * 73428767) ^ (chunkZ * 912931));
        if ((roomSeed & 3) == 0)
        {
            return false;
        }

        return (Math.Abs(lx - 8) <= 1 || Math.Abs(lx - 23) <= 1)
            && (Math.Abs(lz - 8) <= 1 || Math.Abs(lz - 23) <= 1);
    }

    private static int ResolveBlock(ICoreServerAPI api, string code)
    {
        var block = api.World.GetBlock(new AssetLocation(code));
        if (block == null || block.Id == 0)
        {
            api.Logger.Warning("[VRPG/Dungeons] Could not resolve block '{0}'.", code);
            return 0;
        }

        return block.Id;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    private static T GetProperty<T>(object target, string name)
    {
        PropertyInfo? property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property == null)
        {
            throw new MissingMemberException(target.GetType().FullName, name);
        }

        return (T)property.GetValue(target)!;
    }
}
