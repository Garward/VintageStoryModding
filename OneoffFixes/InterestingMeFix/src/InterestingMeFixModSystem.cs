using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using IME;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace InterestingMeFix
{
    public class InterestingMeFixModSystem : ModSystem
    {
        private const string HarmonyId = "interestingmefix";
        private Harmony harmony;

        public static InterestingMeFixConfig Config { get; private set; } = new InterestingMeFixConfig();

        public override double ExecuteOrder() => 1.1;

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            Config = InterestingMeFixConfig.Load(api);
            api.Logger.Notification(
                "[InterestingMeFix] Config: version={0} restoreOre={1} bypassStone={2} rightClickPickup={3} recoverOnDestroy={4} autoHealBE={5} syncMuckBreak={6} vkBoreMuck={7} sieveParity={8} sieveFallback={9} refinedTiers={10} disableMuckMovement={11} fixEmptySolidLayer={12}",
                Config.ConfigVersion,
                Config.RestoreOreOnMuckSpawnFail,
                Config.BypassStoneMuck,
                Config.RightClickPickup,
                Config.RecoverMuckOnDestroy,
                Config.AutoHealMissingBE,
                Config.SynchronousMuckBreakHandling,
                Config.VintageKinematicsBoreMuckCompatibility,
                Config.SieveVanillaNuggetParity,
                Config.SieveFallbackNuggetsPerOreLayer,
                Config.RefinedMuckVanillaYieldTiers,
                Config.DisableMuckSloughWhenStoneBypassIsEnabled,
                Config.FixEmptyMuckSolidLayerCleanup);

            try
            {
                harmony = new Harmony(HarmonyId);
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                api.Logger.Notification("[InterestingMeFix] Harmony patches applied.");
            }
            catch (Exception ex)
            {
                api.Logger.Error("[InterestingMeFix] Failed to apply Harmony patches: {0}", ex);
            }
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            RegisterCommands(api);
            if (!Config.AutoHealMissingBE) return;

            // Cache muckpile block ids so chunk-scan only inspects relevant indices.
            HashSet<int> muckpileIds = new HashSet<int>();
            foreach (Block b in api.World.Blocks)
            {
                if (b is BlockMuckPile && b.Id != 0) muckpileIds.Add(b.Id);
            }
            int csize = GlobalConstants.ChunkSize;
            api.Logger.Notification(
                "[InterestingMeFix] Chunk-load sweep enabled. Tracking {0} muckpile block id(s), chunk size {1}.",
                muckpileIds.Count, csize);

            api.Event.ChunkColumnLoaded += (Vec2i chunkCoord, IWorldChunk[] chunks) =>
            {
                if (!Config.AutoHealMissingBE || chunks == null) return;
	                int healed = 0;
	                int cleared = 0;
                try
                {
                    for (int ci = 0; ci < chunks.Length; ci++)
                    {
                        IWorldChunk chunk = chunks[ci];
                        IChunkBlocks blocks = chunk?.Data;
                        if (blocks == null) continue;

                        int yBase = ci * csize;
                        int len = blocks.Length;
                        for (int i = 0; i < len; i++)
                        {
                            int blockId = blocks[i];
                            if (!muckpileIds.Contains(blockId)) continue;

                            // VS index layout: index = (y * csize + z) * csize + x
                            int lx = i % csize;
                            int lz = (i / csize) % csize;
                            int ly = i / (csize * csize);
                            BlockPos pos = new BlockPos(
                                chunkCoord.X * csize + lx,
                                yBase + ly,
                                chunkCoord.Y * csize + lz);

	                            if (api.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityMuckPile) continue;
	                            if (TryClearBypassedMissingMuck(api.World, pos))
	                            {
	                                cleared++;
	                                continue;
	                            }
	                            if (MuckPileBEAutoHeal.TryHeal(api.World, pos, out int _)) healed++;
	                        }
	                    }
                }
                catch (Exception ex)
                {
                    api.Logger.Warning("[InterestingMeFix] ChunkColumnLoaded sweep error at ({0},{1}): {2}",
                        chunkCoord.X, chunkCoord.Y, ex.Message);
                }

	                if (healed > 0)
	                {
	                    api.Logger.Notification(
	                        "[InterestingMeFix] Sweep healed {0} bugged muckpile(s) in chunk ({1},{2}).",
	                        healed, chunkCoord.X, chunkCoord.Y);
	                }
	                if (cleared > 0)
	                {
	                    api.Logger.Notification(
	                        "[InterestingMeFix] Sweep cleared {0} bypassed legacy stone muck block(s) in chunk ({1},{2}).",
	                        cleared, chunkCoord.X, chunkCoord.Y);
	                }
	            };
	        }

	        private static bool TryClearBypassedMissingMuck(IWorldAccessor world, BlockPos pos)
	        {
	            if (!Config.BypassStoneMuck) return false;
	            Block block = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
	            if (block is not BlockMuckPile) return false;
	            if (!MuckPileBEAutoHeal.TryBuildCompositionFromVisibleBlock(block, out _, out _, out bool displayOnlyStone)) return false;
	            if (!displayOnlyStone) return false;

	            world.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Solid);
	            world.BlockAccessor.MarkBlockDirty(pos, (IPlayer)null);
	            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
	            return true;
	        }

	        private static void RegisterCommands(ICoreServerAPI api)
	        {
	            api.ChatCommands
	                .Create("imefix")
	                .WithDescription("InterestingMeFix admin utilities")
	                .RequiresPrivilege(Privilege.controlserver)
	                .BeginSubCommand("clearmuck")
	                    .WithDescription("Clear all Interesting Mining muck piles in a radius around you")
	                    .RequiresPlayer()
	                    .WithArgs(api.ChatCommands.Parsers.OptionalInt("radius", 64))
	                    .HandleWith(args =>
	                    {
	                        int radius = GameMath.Clamp((int)args[0], 1, 256);
	                        BlockPos center = args.Caller.Entity.Pos.AsBlockPos;
	                        int cleared = ClearMuckInRadius(api.World, center, radius);
	                        api.Logger.Warning(
	                            "[InterestingMeFix] Admin cleanup cleared {0} muck block(s) around {1} radius {2}.",
	                            cleared, center, radius);
	                        return TextCommandResult.Success(
	                            $"InterestingMeFix cleared {cleared} muck block(s) around {center.X},{center.Y},{center.Z} radius {radius}.");
	                    })
	                .EndSubCommand()
	                .BeginSubCommand("clearmuckat")
	                    .WithDescription("Clear all Interesting Mining muck piles around normal /tp-style world coordinates")
	                    .WithArgs(
	                        api.ChatCommands.Parsers.Int("x"),
	                        api.ChatCommands.Parsers.Int("y"),
	                        api.ChatCommands.Parsers.Int("z"),
	                        api.ChatCommands.Parsers.OptionalInt("radius", 64))
	                    .HandleWith(args =>
	                    {
	                        int x = (int)args[0];
	                        int y = (int)args[1];
	                        int z = (int)args[2];
	                        int radius = GameMath.Clamp((int)args[3], 1, 512);
	                        BlockPos center = ToInternalWorldPos(api.World, x, y, z);
	                        int cleared = ClearMuckInRadius(api.World, center, radius);
	                        api.Logger.Warning(
	                            "[InterestingMeFix] Admin cleanup cleared {0} muck block(s) around world coords {1},{2},{3} internal {4} radius {5}.",
	                            cleared, x, y, z, center, radius);
	                        return TextCommandResult.Success(
	                            $"InterestingMeFix cleared {cleared} muck block(s) around world coords {x},{y},{z} radius {radius}.");
	                    })
	                .EndSubCommand();
	        }

	        private static BlockPos ToInternalWorldPos(IWorldAccessor world, int worldX, int worldY, int worldZ)
	        {
	            int internalX = worldX + world.BlockAccessor.MapSizeX / 2;
	            int internalZ = worldZ + world.BlockAccessor.MapSizeZ / 2;
	            int y = GameMath.Clamp(worldY, 0, world.BlockAccessor.MapSizeY - 1);
	            return new BlockPos(internalX, y, internalZ);
	        }

	        private static int ClearMuckInRadius(IWorldAccessor world, BlockPos center, int radius)
	        {
	            int cleared = 0;
	            int radiusSq = radius * radius;
	            int minY = 0;
	            int maxY = world.BlockAccessor.MapSizeY - 1;

	            BlockPos pos = new BlockPos(center.dimension);
	            for (int dx = -radius; dx <= radius; dx++)
	            {
	                for (int dz = -radius; dz <= radius; dz++)
	                {
	                    if (dx * dx + dz * dz > radiusSq) continue;

	                    pos.X = center.X + dx;
	                    pos.Z = center.Z + dz;
		                    for (int y = minY; y <= maxY; y++)
		                    {
		                        pos.Y = y;
		                        if (world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid) is not BlockMuckPile) continue;

		                        world.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Solid);
		                        world.BlockAccessor.MarkBlockDirty(pos, (IPlayer)null);
		                        cleared++;
		                    }
	                }
	            }

	            world.BlockAccessor.TriggerNeighbourBlockUpdate(center);
	            return cleared;
	        }

	        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            // IME's rock.json patch removes /dropsByType from vanilla rock blocks because muck
            // is meant to replace the chunks. When BypassStoneMuck nulls the muck entry, the
            // behavior bails early and there are no vanilla drops left to fall back on, so the
            // block drops itself. Put the chunk drops back when bypass mode is on.
            if (!Config.BypassStoneMuck || !Config.RestoreStoneChunkDrops) return;
            if (api.Side != EnumAppSide.Server) return;

            int restored = 0;
            int sampled = 0;
            foreach (Block block in api.World.Blocks)
            {
                if (block?.Code == null) continue;
                if (block.Code.Domain != "game") continue;
                if (block.Code.Path == null || !block.Code.Path.StartsWith("rock-", StringComparison.Ordinal)) continue;

                string rock = block.Variant != null && block.Variant.TryGetValue("rock", out var rv) ? rv : null;
                if (string.IsNullOrEmpty(rock)) continue;

                if (sampled < 3)
                {
                    int dropCount = block.Drops?.Length ?? -1;
                    string dropCodes = "(none)";
                    if (block.Drops != null && block.Drops.Length > 0)
                    {
                        var names = new List<string>();
                        foreach (var d in block.Drops) names.Add(d?.Code?.ToString() ?? "?");
                        dropCodes = string.Join(",", names);
                    }
                    api.Logger.Notification("[InterestingMeFix] rock sample: {0} existingDrops={1} codes=[{2}]", block.Code, dropCount, dropCodes);
                    sampled++;
                }

                var drops = new List<BlockDropItemStack>();

                var chunk = new BlockDropItemStack
                {
                    Type = EnumItemClass.Item,
                    Code = new AssetLocation("game", "stone-" + rock),
                    Quantity = NatFloat.create(EnumDistribution.UNIFORM, 2.5f, 0.5f)
                };
                if (chunk.Resolve(api.World, "interestingmefix-restoredrops", block.Code))
                {
                    drops.Add(chunk);
                }

                if (rock == "suevite")
                {
                    var diamond = new BlockDropItemStack
                    {
                        Type = EnumItemClass.Item,
                        Code = new AssetLocation("game", "gem-diamond-rough"),
                        Quantity = NatFloat.create(EnumDistribution.UNIFORM, 0.005f, 0f),
                        Attributes = new JsonObject(Newtonsoft.Json.Linq.JObject.Parse("{\"potential\":\"low\"}"))
                    };
                    if (diamond.Resolve(api.World, "interestingmefix-restoredrops", block.Code))
                    {
                        drops.Add(diamond);
                    }
                }

                if (drops.Count > 0)
                {
                    block.Drops = drops.ToArray();
                    restored++;
                }
            }

            api.Logger.Notification("[InterestingMeFix] Restored chunk drops on {0} rock block(s) (BypassStoneMuck active)", restored);
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            base.Dispose();
        }
	    }

    internal static class MuckMovementBypass
    {
        public static bool HardDisabled =>
            InterestingMeFixModSystem.Config.BypassStoneMuck &&
            InterestingMeFixModSystem.Config.DisableMuckSloughWhenStoneBypassIsEnabled;
    }

    internal static class MuckSolidLayerCleanup
    {
        public static void ClearIfEmpty(BlockEntityMuckPile pile)
        {
            if (!InterestingMeFixModSystem.Config.FixEmptyMuckSolidLayerCleanup) return;
            if (pile == null) return;

            try
            {
                ICoreAPI api = pile.Api;
                if (api == null || api.Side != EnumAppSide.Server || pile.Pos == null) return;
                if (pile.TotalLayers > 0) return;

                ClearSolidMuck(api.World, pile.Pos);
            }
            catch
            {
            }
        }

        public static bool ClearSolidMuck(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null) return false;
            if (world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid) is not BlockMuckPile) return false;

            world.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Solid);
            world.BlockAccessor.MarkBlockDirty(pos, (IPlayer)null);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
            return true;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "EnsureVariantMatchesComposition")]
    internal static class MuckEnsureVariantSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "NormalizeWithPileBelow")]
    internal static class MuckNormalizeSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryTakeLayersForSlough")]
    internal static class MuckSloughExtractSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryExtractRandomLayer")]
    internal static class MuckRandomExtractSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryExtractWeightedLayer")]
    internal static class MuckWeightedExtractSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryExtractLayers")]
    internal static class MuckExtractLayersSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryExtractBestLayer")]
    internal static class MuckBestExtractSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryExtractHeaviestOreForBaffles")]
    internal static class MuckBaffleExtractSolidLayerCleanupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntityMuckPile __instance)
        {
            MuckSolidLayerCleanup.ClearIfEmpty(__instance);
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "QueueColumnSettle")]
    internal static class MuckQueueColumnSettleBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MuckMovementBypass.HardDisabled;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "RunQueuedColumnSettle")]
    internal static class MuckRunQueuedColumnSettleBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MuckMovementBypass.HardDisabled;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryShiftColumnDownOne")]
    internal static class MuckShiftColumnDownBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            if (!MuckMovementBypass.HardDisabled) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "ForceLocalSettle")]
    internal static class MuckForceLocalSettleBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MuckMovementBypass.HardDisabled;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TriggerLocalPileSettle")]
    internal static class MuckTriggerLocalSettleBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MuckMovementBypass.HardDisabled;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "QueueSloughRetry")]
    internal static class MuckQueueSloughRetryBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MuckMovementBypass.HardDisabled;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "RegisterWaterTransportTicker")]
    internal static class MuckRegisterWaterTransportBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MuckMovementBypass.HardDisabled;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "OnWaterTransportTick")]
    internal static class MuckWaterTransportTickBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix()
        {
            return !MuckMovementBypass.HardDisabled;
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TrySloughFromColumn")]
    internal static class StoneMuckSloughBypassPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(BlockEntityMuckPile __instance, ref bool __result)
        {
            if (!InterestingMeFixModSystem.Config.BypassStoneMuck) return true;
            if (MuckMovementBypass.HardDisabled)
            {
                __result = false;
                return false;
            }
            if (__instance == null) return true;

            try
            {
                MuckComposition composition = __instance.Composition;
                if (composition == null || composition.TotalLayers <= 0) return true;
                if (composition.GetTotalOreLayers() > 0) return true;

                ClearMuckColumn(__instance.Api?.World, __instance.Pos);
                __result = false;
                return false;
            }
            catch
            {
                return true;
            }
        }

        internal static bool ClearMuckColumn(IWorldAccessor world, BlockPos startPos)
        {
            if (world == null || startPos == null) return false;

            BlockPos bottom = startPos.Copy();
            while (bottom.Y > 0 && world.BlockAccessor.GetBlock(bottom.DownCopy(1), BlockLayersAccess.Solid) is BlockMuckPile)
            {
                bottom.Down();
            }

            bool cleared = false;
            BlockPos scan = bottom.Copy();
            while (world.BlockAccessor.GetBlock(scan, BlockLayersAccess.Solid) is BlockMuckPile)
            {
                world.BlockAccessor.SetBlock(0, scan, BlockLayersAccess.Solid);
                world.BlockAccessor.MarkBlockDirty(scan, (IPlayer)null);
                cleared = true;
                scan.Up();
                if (scan.Y > bottom.Y + 64) break;
            }

            if (cleared)
            {
                world.BlockAccessor.TriggerNeighbourBlockUpdate(bottom);
            }

            return cleared;
        }
    }

    [HarmonyPatch(typeof(BlockBehaviorDropMuck), nameof(BlockBehaviorDropMuck.OnBlockBroken))]
    internal static class SynchronousDropMuckBreakPatch
    {
        private static readonly FieldInfo StoneLayersField = AccessTools.Field(typeof(BlockBehaviorDropMuck), "_stoneLayers");
        private static readonly FieldInfo OreLayersField = AccessTools.Field(typeof(BlockBehaviorDropMuck), "_oreLayers");
        private static readonly FieldInfo OreCodeField = AccessTools.Field(typeof(BlockBehaviorDropMuck), "_oreCode");
        private static readonly FieldInfo StoneCodeField = AccessTools.Field(typeof(BlockBehaviorDropMuck), "_stoneCode");
        private static readonly FieldInfo SuppressVanillaDropsField = AccessTools.Field(typeof(BlockBehaviorDropMuck), "_suppressVanillaDrops");
        private static readonly MethodInfo ResolveStoneCodeMethod = AccessTools.Method(typeof(BlockBehaviorDropMuck), "ResolveStoneCode");
        private static readonly MethodInfo BuildCompositionMethod = AccessTools.Method(typeof(BlockBehaviorDropMuck), "BuildComposition");
        private static readonly MethodInfo ResolveMuckSpawnMethod = AccessTools.Method(typeof(BlockBehaviorDropMuck), "ResolveMuckSpawn");

        [HarmonyPrefix]
        public static bool Prefix(
            BlockBehaviorDropMuck __instance,
            IWorldAccessor world,
            BlockPos pos,
            IPlayer byPlayer,
            ref EnumHandling handling)
        {
            if (!InterestingMeFixModSystem.Config.SynchronousMuckBreakHandling) return true;
            if (world == null || world.Side != EnumAppSide.Server || pos == null || byPlayer == null) return true;
            if (GetActiveTool(byPlayer) != EnumTool.Pickaxe) return true;
            if (SuppressVanillaDropsField?.GetValue(__instance) is bool suppress && !suppress) return true;

            try
            {
                string blockCode = __instance.block?.Code?.ToString();
                if (string.IsNullOrEmpty(blockCode)) return true;

                MuckComposition composition = BuildCompositionForBehavior(
                    world,
                    __instance,
                    requireDropTableEntry: InterestingMeFixModSystem.Config.BypassStoneMuck);
                if (composition == null || composition.TotalLayers <= 0) return true;

                BlockPos spawnPos = pos.Copy();
                world.BlockAccessor.SetBlock(0, spawnPos);
                world.BlockAccessor.MarkBlockDirty(spawnPos, (IPlayer)null);
                world.BlockAccessor.TriggerNeighbourBlockUpdate(spawnPos);

                ResolveMuckSpawn(world, spawnPos, composition);
                handling = EnumHandling.PreventSubsequent;
                return false;
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] Synchronous muck break failed at {0}: {1}", pos, ex.Message);
                return true;
            }
        }

        private static string ResolveStoneCode(string blockCode)
        {
            if (ResolveStoneCodeMethod == null) return null;
            return ResolveStoneCodeMethod.Invoke(null, new object[] { blockCode }) as string;
        }

        internal static MuckComposition BuildCompositionForBlock(IWorldAccessor world, Block block, bool requireDropTableEntry)
        {
            if (block == null) return null;
            BlockBehaviorDropMuck behavior = block.GetBehavior<BlockBehaviorDropMuck>();
            if (behavior != null)
            {
                return BuildCompositionForBehavior(world, behavior, requireDropTableEntry);
            }

            string blockCode = block.Code?.ToString();
            if (string.IsNullOrWhiteSpace(blockCode)) return null;

            MuckDropEntry tableEntry = MuckDropTable.Instance.GetEntry(blockCode);
            if (requireDropTableEntry && tableEntry == null) return null;
            if (tableEntry == null) return null;

            string stoneCode = tableEntry.StoneCode ?? ResolveStoneCode(blockCode);
            return BuildComposition(world, tableEntry.StoneLayers, tableEntry.OreLayers, tableEntry.OreCode, stoneCode);
        }

        internal static void SpawnMuckComposition(IWorldAccessor world, BlockPos pos, MuckComposition composition)
        {
            if (world == null || pos == null || composition == null || composition.TotalLayers <= 0) return;
            ResolveMuckSpawnMethod?.Invoke(null, new object[] { world, pos, composition });
        }

        private static MuckComposition BuildCompositionForBehavior(IWorldAccessor world, BlockBehaviorDropMuck behavior, bool requireDropTableEntry)
        {
            string blockCode = behavior?.block?.Code?.ToString();
            if (string.IsNullOrWhiteSpace(blockCode)) return null;

            MuckDropEntry tableEntry = MuckDropTable.Instance.GetEntry(blockCode);
            if (requireDropTableEntry && tableEntry == null) return null;

            NatFloat stoneLayers = StoneLayersField?.GetValue(behavior) as NatFloat ?? tableEntry?.StoneLayers;
            NatFloat oreLayers = OreLayersField?.GetValue(behavior) as NatFloat ?? tableEntry?.OreLayers;
            string oreCode = OreCodeField?.GetValue(behavior) as string ?? tableEntry?.OreCode;
            string stoneCode = StoneCodeField?.GetValue(behavior) as string ?? tableEntry?.StoneCode ?? ResolveStoneCode(blockCode);

            if (tableEntry == null && stoneLayers == null && oreLayers == null) return null;
            return BuildComposition(world, stoneLayers, oreLayers, oreCode, stoneCode);
        }

        private static MuckComposition BuildComposition(IWorldAccessor world, NatFloat stoneLayers, NatFloat oreLayers, string oreCode, string stoneCode)
        {
            if (BuildCompositionMethod == null) return null;
            return BuildCompositionMethod.Invoke(null, new object[] { world, stoneLayers, oreLayers, oreCode, stoneCode }) as MuckComposition;
        }

        private static void ResolveMuckSpawn(IWorldAccessor world, BlockPos pos, MuckComposition composition)
        {
            SpawnMuckComposition(world, pos, composition);
        }

        private static EnumTool? GetActiveTool(IPlayer player)
        {
            ItemSlot slot = player?.InventoryManager?.ActiveHotbarSlot;
            return slot?.Itemstack?.Collectible?.Tool;
        }
    }

    [HarmonyPatch]
    internal static class VintageKinematicsBoreMuckCompatibilityPatch
    {
        private const string BoreTypeName = "VintageKinematics.BlockEntities.BEKineticBore";

        private static readonly MethodInfo CompatGetDropsMethod = AccessTools.Method(
            typeof(VintageKinematicsBoreMuckCompatibilityPatch),
            nameof(CompatGetDrops));

        [ThreadStatic]
        private static BoreMuckContext currentContext;

        public static bool Prepare()
        {
            return InterestingMeFixModSystem.Config.VintageKinematicsBoreMuckCompatibility &&
                   AccessTools.TypeByName(BoreTypeName) != null;
        }

        public static MethodBase TargetMethod()
        {
            Type boreType = AccessTools.TypeByName(BoreTypeName);
            return boreType == null ? null : AccessTools.Method(boreType, "StepDescent");
        }

        [HarmonyPrefix]
        public static void Prefix(object __instance)
        {
            currentContext = null;
            if (!InterestingMeFixModSystem.Config.VintageKinematicsBoreMuckCompatibility) return;
            if (__instance is not BlockEntity bore || bore.Api == null || bore.Api.Side != EnumAppSide.Server) return;

            currentContext = new BoreMuckContext(bore.Api.World);
        }

        [HarmonyPostfix]
        public static void Postfix()
        {
            BoreMuckContext context = currentContext;
            currentContext = null;
            context?.SpawnCapturedMuck();
        }

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (CompatGetDropsMethod != null && IsCompatibleGetDropsCall(instruction))
                {
                    yield return new CodeInstruction(OpCodes.Call, CompatGetDropsMethod);
                    continue;
                }

                yield return instruction;
            }
        }

        private static bool IsCompatibleGetDropsCall(CodeInstruction instruction)
        {
            if (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt) return false;
            if (instruction.operand is not MethodInfo method) return false;
            if (!string.Equals(method.Name, nameof(Block.GetDrops), StringComparison.Ordinal)) return false;
            if (method.ReturnType != typeof(ItemStack[])) return false;

            ParameterInfo[] parameters = method.GetParameters();
            return parameters.Length == 4 &&
                   parameters[0].ParameterType == typeof(IWorldAccessor) &&
                   parameters[1].ParameterType == typeof(BlockPos) &&
                   parameters[2].ParameterType == typeof(IPlayer) &&
                   parameters[3].ParameterType == typeof(float);
        }

        public static ItemStack[] CompatGetDrops(Block block, IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier)
        {
            BoreMuckContext context = currentContext;
            if (context != null &&
                block != null &&
                world != null &&
                pos != null &&
                TryCaptureMuckComposition(world, block, pos, context))
            {
                return Array.Empty<ItemStack>();
            }

            return block.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
        }

        private static bool TryCaptureMuckComposition(IWorldAccessor world, Block block, BlockPos pos, BoreMuckContext context)
        {
            try
            {
                MuckComposition composition = SynchronousDropMuckBreakPatch.BuildCompositionForBlock(
                    world,
                    block,
                    requireDropTableEntry: true);
                if (composition == null || composition.TotalLayers <= 0) return false;

                context.Remember(pos, composition);
                return true;
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] VK bore muck capture failed for {0} at {1}: {2}",
                    block.Code, pos, ex.Message);
                return false;
            }
        }

        private sealed class BoreMuckContext
        {
            private readonly IWorldAccessor world;
            private readonly List<CapturedMuck> captured = new List<CapturedMuck>();

            public BoreMuckContext(IWorldAccessor world)
            {
                this.world = world;
            }

            public void Remember(BlockPos pos, MuckComposition composition)
            {
                captured.Add(new CapturedMuck(pos.Copy(), composition.Clone()));
            }

            public void SpawnCapturedMuck()
            {
                if (captured.Count == 0 || world == null) return;

                foreach (CapturedMuck entry in captured)
                {
                    BlockPos target = ResolveSpawnPos(entry.SourcePos);
                    SynchronousDropMuckBreakPatch.SpawnMuckComposition(world, target, entry.Composition);
                }
            }

            private BlockPos ResolveSpawnPos(BlockPos sourcePos)
            {
                if (CanAcceptMuck(sourcePos)) return sourcePos.Copy();

                for (int radius = 1; radius <= 2; radius++)
                {
                    for (int dx = -radius; dx <= radius; dx++)
                    {
                        for (int dz = -radius; dz <= radius; dz++)
                        {
                            if (Math.Abs(dx) != radius && Math.Abs(dz) != radius) continue;

                            BlockPos candidate = sourcePos.AddCopy(dx, 0, dz);
                            if (CanAcceptMuck(candidate)) return candidate;
                        }
                    }
                }

                return sourcePos.Copy();
            }

            private bool CanAcceptMuck(BlockPos pos)
            {
                Block block = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
                return block == null || block.Id == 0 || block is BlockMuckPile;
            }
        }

        private readonly struct CapturedMuck
        {
            public readonly BlockPos SourcePos;
            public readonly MuckComposition Composition;

            public CapturedMuck(BlockPos sourcePos, MuckComposition composition)
            {
                SourcePos = sourcePos;
                Composition = composition;
            }
        }
    }

    [HarmonyPatch(typeof(BlockBehaviorDropMuck), nameof(BlockBehaviorDropMuck.OnBlockBroken))]
    internal static class DropMuckBreakSafetyNet
    {
        // Scan this many positions below the mined pos when checking whether muck actually spawned.
        private const int ColumnScanDepth = 16;

        // Delay (ms) before verifying. The mod's own delayed spawn fires at +1ms; we wait longer to let it complete.
        private const int VerifyDelayMs = 250;

        [HarmonyPostfix]
        public static void Postfix(
            BlockBehaviorDropMuck __instance,
            IWorldAccessor world,
            BlockPos pos,
            IPlayer byPlayer,
            float dropQuantityMultiplier,
            ref EnumHandling handling)
        {
            try
            {
                if (InterestingMeFixModSystem.Config.SynchronousMuckBreakHandling) return;
                if (!InterestingMeFixModSystem.Config.RestoreOreOnMuckSpawnFail) return;
                if (world == null || world.Side != EnumAppSide.Server) return;
                if (pos == null) return;
                if (handling != EnumHandling.PreventSubsequent) return;

                Block originalBlock = __instance.block;
                if (originalBlock == null || originalBlock.Id == 0) return;

                BlockPos checkPos = pos.Copy();
                int originalId = originalBlock.Id;

                world.RegisterCallback((dt) =>
                {
                    try
                    {
                        if (MuckpileExistsInColumn(world, checkPos, ColumnScanDepth)) return;

                        Block currentAtPos = world.BlockAccessor.GetBlock(checkPos);
                        if (currentAtPos == null) return;
                        if (currentAtPos.Id != 0) return;

                        world.BlockAccessor.SetBlock(originalId, checkPos);
                        world.BlockAccessor.MarkBlockDirty(checkPos);
                        world.BlockAccessor.TriggerNeighbourBlockUpdate(checkPos);

                        world.Logger.Notification(
                            "[InterestingMeFix] Restored {0} at {1}: muck spawn failed silently.",
                            originalBlock.Code, checkPos);
                    }
                    catch (Exception ex)
                    {
                        world.Logger.Warning("[InterestingMeFix] Verify callback error: {0}", ex.Message);
                    }
                }, VerifyDelayMs);
            }
            catch (Exception ex)
            {
                world?.Logger?.Warning("[InterestingMeFix] Postfix error: {0}", ex.Message);
            }
        }

        private static bool MuckpileExistsInColumn(IWorldAccessor world, BlockPos top, int depth)
        {
            BlockPos scan = top.Copy();
            for (int i = 0; i <= depth && scan.Y > 0; i++)
            {
                Block b = world.BlockAccessor.GetBlock(scan, BlockLayersAccess.Solid);
                if (b is BlockMuckPile) return true;
                scan.Y--;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(MuckDropTable), nameof(MuckDropTable.GetEntry))]
    internal static class StoneBypassPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ref MuckDropEntry __result)
        {
            if (!InterestingMeFixModSystem.Config.BypassStoneMuck) return;
            if (__result == null) return;
            if (string.IsNullOrWhiteSpace(__result.OreCode))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(BlockBehaviorDropMuck), "BuildComposition")]
    internal static class SieveParityGradeMarkerPatch
    {
        private const float GradeMarkerStep = 0.001f;

        [HarmonyPostfix]
        public static void Postfix(NatFloat oreLayers, string oreCode, MuckComposition __result)
        {
            if (!InterestingMeFixModSystem.Config.SieveVanillaNuggetParity) return;
            if (__result == null || __result.TotalLayers <= 0) return;
            if (!TryGetGradeLayerCount(oreLayers, out int gradeLayers)) return;
            if (!SieveParityUtil.TryResolveNuggetCode(oreCode, out _)) return;

            float marker = GradeMarkerStep * gradeLayers;
            foreach (MuckEntry entry in __result.Entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.OreCode)) continue;
                if (!string.Equals(entry.OreCode, oreCode, StringComparison.OrdinalIgnoreCase)) continue;
                if (!SieveParityUtil.TryResolveNuggetCode(entry.OreCode, out _)) continue;

                entry.Concentration = 25f + marker;
            }
        }

        private static bool TryGetGradeLayerCount(NatFloat oreLayers, out int gradeLayers)
        {
            gradeLayers = 0;
            if (oreLayers == null) return false;

            int rounded = (int)Math.Round(oreLayers.avg, MidpointRounding.AwayFromZero);
            if (rounded < 1 || rounded > 4) return false;
            if (Math.Abs(oreLayers.avg - rounded) > 0.01f) return false;

            gradeLayers = rounded;
            return true;
        }
    }

    [HarmonyPatch(typeof(BlockEntitySieve), "TryResolveOreOutput")]
    internal static class SieveVanillaNuggetParityPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(
            BlockEntitySieve __instance,
            MuckEntry entry,
            ref string outputCode,
            ref int quantity,
            ref bool __result)
        {
            if (!InterestingMeFixModSystem.Config.SieveVanillaNuggetParity) return true;
            if (entry == null || string.IsNullOrWhiteSpace(entry.OreCode)) return true;
            if (!SieveParityUtil.TryResolveNuggetCode(entry.OreCode, out outputCode)) return true;

            double expected = SieveParityUtil.GetExpectedNuggetsPerLayer(entry);
            string carryKey = SieveParityUtil.GetCarryKey(outputCode, entry);
            quantity = SieveParityUtil.ResolveCarryQuantity(__instance, carryKey, expected);
            __result = quantity > 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.ToTreeAttributes))]
    internal static class SieveParityCarrySavePatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntity __instance, ITreeAttribute tree)
        {
            if (__instance is BlockEntitySieve sieve)
            {
                SieveParityCarryStore.ToTreeAttributes(sieve, tree);
            }
        }
    }

    [HarmonyPatch(typeof(BlockEntity), nameof(BlockEntity.FromTreeAttributes))]
    internal static class SieveParityCarryLoadPatch
    {
        [HarmonyPostfix]
        public static void Postfix(BlockEntity __instance, ITreeAttribute tree)
        {
            if (__instance is BlockEntitySieve sieve)
            {
                SieveParityCarryStore.FromTreeAttributes(sieve, tree);
            }
        }
    }

    internal static class SieveParityUtil
    {
        private const float GradeMarkerStep = 0.001f;

        public static bool TryResolveNuggetCode(string oreCode, out string nuggetCode)
        {
            nuggetCode = null;
            if (string.IsNullOrWhiteSpace(oreCode)) return false;

            string normalized = oreCode.ToLowerInvariant();
            if (normalized.Contains("rock-")) return false;

            if (normalized.Contains("nativegold"))
            {
                nuggetCode = "game:nugget-nativegold";
                return true;
            }
            if (normalized.Contains("nativesilver"))
            {
                nuggetCode = "game:nugget-nativesilver";
                return true;
            }
            if (normalized.Contains("nativeplatinum") || normalized.Contains("native-platinum"))
            {
                nuggetCode = "game:nugget-nativeplatinum";
                return true;
            }

            int colon = normalized.IndexOf(':');
            if (colon <= 0 || colon >= normalized.Length - 1) return false;

            string domain = normalized.Substring(0, colon);
            string path = normalized.Substring(colon + 1);
            if (!path.StartsWith("ore-", StringComparison.OrdinalIgnoreCase)) return false;

            string oreName = path.Substring("ore-".Length);
            if (string.IsNullOrWhiteSpace(oreName)) return false;
            if (IsKnownMineralOre(oreName)) return false;

            nuggetCode = domain + ":nugget-" + oreName;
            return true;
        }

        public static string GetCarryKey(string outputCode, MuckEntry entry)
        {
            int gradeLayers = DecodeGradeLayerCount(entry.Concentration);
            string gradePart = gradeLayers > 0 ? gradeLayers.ToString() : "unmarked";
            return (outputCode ?? "unknown") + "|" + gradePart;
        }

        public static double GetExpectedNuggetsPerLayer(MuckEntry entry)
        {
            int gradeLayers = DecodeGradeLayerCount(entry.Concentration);
            if (gradeLayers <= 0)
            {
                return Math.Max(0.0, InterestingMeFixModSystem.Config.SieveFallbackNuggetsPerOreLayer);
            }

            int nuggetsPerVanillaChunk = GetVanillaNuggetsPerOreChunk(entry.OreCode, gradeLayers);
            return nuggetsPerVanillaChunk * 1.25 / gradeLayers;
        }

        public static int ResolveCarryQuantity(BlockEntitySieve sieve, string carryKey, double expected)
        {
            if (expected <= 0) return 0;

            int whole = (int)Math.Floor(expected);
            double fraction = expected - whole;

            if (fraction > 0.000001)
            {
                double carry = SieveParityCarryStore.GetCarry(sieve, carryKey) + fraction;
                if (carry >= 1.0 - 0.000001)
                {
                    whole++;
                    carry -= 1.0;
                }
                SieveParityCarryStore.SetCarry(sieve, carryKey, carry);
            }

            return whole;
        }

        public static int DecodeGradeLayerCount(float concentration)
        {
            int sourceLayers = EstimateSourceLayerCount(concentration);
            float marker = concentration - sourceLayers * 25f;
            if (marker < GradeMarkerStep * 0.5f || marker > GradeMarkerStep * 4.5f * sourceLayers) return 0;

            int gradeLayers = (int)Math.Round(marker / sourceLayers / GradeMarkerStep, MidpointRounding.AwayFromZero);
            return gradeLayers >= 1 && gradeLayers <= 4 ? gradeLayers : 0;
        }

        public static int EstimateSourceLayerCount(float concentration)
        {
            if (concentration <= 0f) return 1;
            return Math.Max(1, (int)Math.Round(concentration / 25f, MidpointRounding.AwayFromZero));
        }

        public static double GetVanillaMetalUnitsPerSourceLayer(MuckEntry entry)
        {
            int gradeLayers = DecodeGradeLayerCount(entry.Concentration);
            if (gradeLayers <= 0)
            {
                return Math.Max(0.0, InterestingMeFixModSystem.Config.RefinedFallbackVanillaUnitsPerOreLayer);
            }

            return GetVanillaNuggetsPerOreChunk(entry.OreCode, gradeLayers) * 5.0 * 1.25 / gradeLayers;
        }

        public static int GetVanillaNuggetsPerOreChunk(string oreCode, int gradeLayers)
        {
            string normalized = oreCode?.ToLowerInvariant() ?? string.Empty;
            int metalUnits;

            if (normalized.Contains("cassiterite") || normalized.Contains("quartz_nativegold"))
            {
                metalUnits = gradeLayers * 5;
            }
            else if (normalized.Contains("quartz_nativesilver"))
            {
                metalUnits = 5 + gradeLayers * 5;
            }
            else if (normalized.Contains("hematite"))
            {
                metalUnits = gradeLayers switch
                {
                    1 => 20,
                    2 => 25,
                    3 => 30,
                    4 => 40,
                    _ => 25
                };
            }
            else
            {
                metalUnits = gradeLayers switch
                {
                    1 => 15,
                    2 => 20,
                    3 => 25,
                    4 => 35,
                    _ => 20
                };
            }

            return Math.Max(1, metalUnits / 5);
        }

        private static bool IsKnownMineralOre(string oreName)
        {
            return oreName.Contains("quartz") ||
                   oreName.Contains("borax") ||
                   oreName.Contains("potash") ||
                   oreName.Contains("sulfur") ||
                   oreName.Contains("sylvite") ||
                   oreName.Contains("anthracite") ||
                   oreName.Contains("bituminouscoal") ||
                   oreName.Contains("lignite") ||
                   oreName.Contains("fluorite") ||
                   oreName.Contains("flourite") ||
                   oreName.Contains("diamond") ||
                   oreName.Contains("emerald");
        }
    }

    [HarmonyPatch(typeof(BlockEntitySmeltPot), nameof(BlockEntitySmeltPot.ProcessCompletedFiring))]
    internal static class RefinedSmeltPotYieldPatch
    {
        private static readonly FieldInfo MuckField = AccessTools.Field(typeof(BlockEntitySmeltPot), "muck");
        private static readonly FieldInfo PendingOutputsField = AccessTools.Field(typeof(BlockEntitySmeltPot), "pendingOutputs");
        private static readonly MethodInfo GetFluxAvailabilityBonusMethod = AccessTools.Method(typeof(BlockEntitySmeltPot), "GetFluxAvailabilityBonus");
        private static readonly MethodInfo ClearAdditivesMethod = AccessTools.Method(typeof(BlockEntitySmeltPot), "ClearAdditives");

        [HarmonyPrefix]
        public static bool Prefix(BlockEntitySmeltPot __instance, int furnaceTemperature, ref bool __result)
        {
            if (!InterestingMeFixModSystem.Config.RefinedMuckVanillaYieldTiers) return true;
            if (MuckField == null || PendingOutputsField == null || GetFluxAvailabilityBonusMethod == null || ClearAdditivesMethod == null) return true;
            if (__instance?.Api == null || __instance.Api.Side != EnumAppSide.Server) return true;

            MuckComposition muck = MuckField.GetValue(__instance) as MuckComposition;
            if (muck == null || muck.TotalLayers <= 0)
            {
                __result = false;
                return false;
            }

            List<ItemStack> pendingOutputs = PendingOutputsField.GetValue(__instance) as List<ItemStack>;
            if (pendingOutputs == null) return true;

            MuckComposition removed = new MuckComposition();
            Dictionary<string, double> unitsByMetal = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            double fluxAvailabilityBonus = (double)GetFluxAvailabilityBonusMethod.Invoke(__instance, new object[] { muck.TotalLayers });
            bool hasProperFlux = fluxAvailabilityBonus >= InterestingMeFixModSystem.Config.RefinedProperFluxMinimumBonus;

            foreach (MuckEntry entry in muck.Entries)
            {
                if (entry == null || entry.Count <= 0) continue;
                if (RefinedYieldUtil.IsStoneMuck(entry.OreCode))
                {
                    removed.Add(entry.OreCode, entry.Count, entry.HostRockCode, entry.ProcessingVariant, entry.Concentration, entry.Availability, entry.Roasted);
                    continue;
                }

                if (!RefinedYieldUtil.TryResolveSmeltPotMetal(entry.OreCode, out string metal)) continue;
                if (furnaceTemperature < RefinedYieldUtil.GetMeltingTemperature(metal)) continue;
                if (__instance.Api.World.GetItem(new AssetLocation("interestingme", "pigmetal-" + metal)) == null) continue;

                double effectiveAvailability = Math.Min(1.0, entry.Availability + fluxAvailabilityBonus);
                double units = RefinedYieldUtil.GetTieredMetalUnits(entry, effectiveAvailability, hasProperFlux);
                int roundedUnits = (int)Math.Round(units, MidpointRounding.AwayFromZero);
                if (roundedUnits <= 0) continue;

                removed.Add(entry.OreCode, entry.Count, entry.HostRockCode, entry.ProcessingVariant, entry.Concentration, entry.Availability, entry.Roasted);
                unitsByMetal.TryGetValue(metal, out double existingUnits);
                unitsByMetal[metal] = existingUnits + units;
            }

            if (removed.TotalLayers <= 0)
            {
                __result = false;
                return false;
            }

            muck.RemoveComposition(removed);
            if (unitsByMetal.Count > 0)
            {
                ClearAdditivesMethod.Invoke(__instance, Array.Empty<object>());
            }

            foreach (KeyValuePair<string, double> pair in unitsByMetal)
            {
                int metalUnits = (int)Math.Round(pair.Value, MidpointRounding.AwayFromZero);
                if (metalUnits <= 0) continue;

                Item item = __instance.Api.World.GetItem(new AssetLocation("interestingme", "pigmetal-" + pair.Key));
                if (item == null) continue;

                ItemStack output = new ItemStack(item, 1);
                output.Attributes.SetString("metal", pair.Key);
                output.Attributes.SetInt("metalUnits", metalUnits);
                pendingOutputs.Add(output);
            }

            __instance.MarkDirty(true, null);
            __instance.Api.World.BlockAccessor.MarkBlockDirty(__instance.Pos, (IPlayer)null);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockEntityHighTempFurnaceDoor), "ProcessBloomeryOutputs")]
    internal static class RefinedBloomeryYieldPatch
    {
        private static readonly FieldInfo LastIronUnitsField = AccessTools.Field(typeof(BlockEntityHighTempFurnaceDoor), "lastIronUnits");

        [HarmonyPrefix]
        public static bool Prefix(BlockEntityHighTempFurnaceDoor __instance, BlockPos[] muckPositions)
        {
            if (!InterestingMeFixModSystem.Config.RefinedMuckVanillaYieldTiers) return true;
            if (__instance?.Api == null || muckPositions == null || muckPositions.Length == 0) return false;

            int ironUnits = RefinedYieldUtil.GetBloomeryIronUnits(__instance.Api.World, muckPositions);
            LastIronUnitsField?.SetValue(__instance, ironUnits);

            BlockPos outputPos = RefinedYieldUtil.FindBloomeryOutputPosition(muckPositions);
            foreach (BlockPos muckPos in muckPositions)
            {
                __instance.Api.World.BlockAccessor.SetBlock(0, muckPos);
                __instance.Api.World.BlockAccessor.MarkBlockDirty(muckPos, (IPlayer)null);
            }

            if (ironUnits <= 0 || outputPos == null) return false;

            Block ironMassBlock = __instance.Api.World.GetBlock(new AssetLocation("interestingme:ironmass"));
            if (ironMassBlock != null)
            {
                __instance.Api.World.BlockAccessor.SetBlock(ironMassBlock.Id, outputPos);
                if (__instance.Api.World.BlockAccessor.GetBlockEntity(outputPos) is BlockEntityIronMass ironMass)
                {
                    ironMass.SetIronUnits(ironUnits);
                }
                __instance.Api.World.BlockAccessor.MarkBlockDirty(outputPos, (IPlayer)null);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(BlockEntityHighTempFurnaceDoor), "UpdateMuckStats")]
    internal static class RefinedBloomeryStatsPatch
    {
        private static readonly FieldInfo LastDetectedTierField = AccessTools.Field(typeof(BlockEntityHighTempFurnaceDoor), "lastDetectedTier");
        private static readonly FieldInfo LastTotalMuckLayersField = AccessTools.Field(typeof(BlockEntityHighTempFurnaceDoor), "lastTotalMuckLayers");
        private static readonly FieldInfo LastRequiredFuelUnitsField = AccessTools.Field(typeof(BlockEntityHighTempFurnaceDoor), "lastRequiredFuelUnits");
        private static readonly FieldInfo LastIronUnitsField = AccessTools.Field(typeof(BlockEntityHighTempFurnaceDoor), "lastIronUnits");

        [HarmonyPrefix]
        public static bool Prefix(BlockEntityHighTempFurnaceDoor __instance, BlockPos[] muckPositions)
        {
            if (!InterestingMeFixModSystem.Config.RefinedMuckVanillaYieldTiers) return true;
            if (__instance?.Api == null) return true;

            int totalLayers = 0;
            if (muckPositions != null)
            {
                foreach (BlockPos muckPos in muckPositions)
                {
                    if (__instance.Api.World.BlockAccessor.GetBlockEntity(muckPos) is BlockEntityMuckPile pile)
                    {
                        totalLayers += pile.TotalLayers;
                    }
                }
            }

            int tier = Math.Max(1, (int)(LastDetectedTierField?.GetValue(__instance) ?? 1));
            LastTotalMuckLayersField?.SetValue(__instance, totalLayers);
            LastRequiredFuelUnitsField?.SetValue(__instance, RefinedYieldUtil.GetBloomeryRequiredFuelUnits(tier, totalLayers));
            LastIronUnitsField?.SetValue(__instance, RefinedYieldUtil.GetBloomeryIronUnits(__instance.Api.World, muckPositions));
            return false;
        }
    }

    internal static class RefinedYieldUtil
    {
        public static double GetTieredMetalUnits(MuckEntry entry, double effectiveAvailability, bool hasProperFlux)
        {
            int sourceLayers = SieveParityUtil.EstimateSourceLayerCount(entry.Concentration) * Math.Max(1, entry.Count);
            double vanillaUnits = SieveParityUtil.GetVanillaMetalUnitsPerSourceLayer(entry) * sourceLayers;
            double stockUnits = Math.Max(0.0, entry.Concentration * effectiveAvailability * entry.Count);
            double tier = GetTierMultiplier(entry, effectiveAvailability, hasProperFlux);
            double floor = vanillaUnits * tier;
            double cap = vanillaUnits * Math.Max(tier, InterestingMeFixModSystem.Config.RefinedOptimizedMultiplier);

            return Math.Min(Math.Max(stockUnits, floor), cap);
        }

        public static int GetBloomeryIronUnits(IWorldAccessor world, BlockPos[] muckPositions)
        {
            if (world == null || muckPositions == null) return 0;

            int ironUnits = 0;
            foreach (BlockPos muckPos in muckPositions)
            {
                if (!(world.BlockAccessor.GetBlockEntity(muckPos) is BlockEntityMuckPile pile)) continue;

                foreach (MuckEntry entry in pile.Composition.Entries)
                {
                    if (entry == null || entry.Count <= 0 || !IsIronOre(entry.OreCode)) continue;
                    double units = GetTieredMetalUnits(entry, entry.Availability, false);
                    ironUnits += Math.Max(0, (int)Math.Round(units, MidpointRounding.AwayFromZero));
                }
            }
            return ironUnits;
        }

        public static BlockPos FindBloomeryOutputPosition(BlockPos[] muckPositions)
        {
            if (muckPositions == null || muckPositions.Length == 0) return null;

            int minY = int.MaxValue;
            double avgX = 0.0;
            double avgZ = 0.0;
            int count = 0;
            foreach (BlockPos pos in muckPositions)
            {
                if (pos == null) continue;
                if (pos.Y < minY) minY = pos.Y;
                avgX += pos.X;
                avgZ += pos.Z;
                count++;
            }
            if (count <= 0) return null;

            avgX /= count;
            avgZ /= count;
            BlockPos best = null;
            double bestDistance = double.MaxValue;
            foreach (BlockPos pos in muckPositions)
            {
                if (pos == null || pos.Y != minY) continue;
                double dx = pos.X - avgX;
                double dz = pos.Z - avgZ;
                double distance = dx * dx + dz * dz;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = pos;
                }
            }

            return best?.Copy();
        }

        public static int GetBloomeryRequiredFuelUnits(int tier, int muckLayers)
        {
            if (muckLayers <= 0) return 0;
            int divisor = tier == 3 ? 6 : tier == 2 ? 4 : 2;
            return (int)Math.Ceiling((double)muckLayers / divisor);
        }

        public static bool IsStoneMuck(string oreCode)
        {
            return !string.IsNullOrWhiteSpace(oreCode) && oreCode.Contains("rock-", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsIronOre(string oreCode)
        {
            if (string.IsNullOrWhiteSpace(oreCode)) return false;
            string normalized = oreCode.ToLowerInvariant();
            return normalized.StartsWith("game:ore-limonite", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("game:ore-hematite", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("game:ore-magnetite", StringComparison.OrdinalIgnoreCase) ||
                   normalized.StartsWith("geoaddons:ore-pyrite", StringComparison.OrdinalIgnoreCase);
        }

        public static bool TryResolveSmeltPotMetal(string oreCode, out string metal)
        {
            metal = null;
            if (string.IsNullOrWhiteSpace(oreCode)) return false;
            string normalized = oreCode.ToLowerInvariant();
            if (normalized.Contains("rock-")) return false;

            if (ContainsOre(normalized, "nativecopper") || ContainsOre(normalized, "malachite") ||
                ContainsOre(normalized, "azurite") || ContainsOre(normalized, "chalcopyrite") ||
                ContainsOre(normalized, "chalcocite") || ContainsOre(normalized, "tetrahedrite"))
            {
                metal = "copper";
                return true;
            }
            if (ContainsOre(normalized, "cassiterite") || ContainsOre(normalized, "franckeite") || ContainsOre(normalized, "teallite"))
            {
                metal = "tin";
                return true;
            }
            if (ContainsOre(normalized, "bismuthinite"))
            {
                metal = "bismuth";
                return true;
            }
            if (ContainsOre(normalized, "sphalerite") || ContainsOre(normalized, "smithsonite") || ContainsOre(normalized, "hemimorphite"))
            {
                metal = "zinc";
                return true;
            }
            if (ContainsOre(normalized, "galena_nativesilver") || ContainsOre(normalized, "quartz_nativesilver") ||
                ContainsOre(normalized, "native-silver") || ContainsOre(normalized, "nativesilver") || ContainsOre(normalized, "freiburgite"))
            {
                metal = "silver";
                return true;
            }
            if (ContainsOre(normalized, "galena") || ContainsOre(normalized, "vanadanite") ||
                ContainsOre(normalized, "wulfenite") || ContainsOre(normalized, "cerussite"))
            {
                metal = "lead";
                return true;
            }
            if (ContainsOre(normalized, "quartz_nativegold") || ContainsOre(normalized, "native-gold") || ContainsOre(normalized, "nativegold"))
            {
                metal = "gold";
                return true;
            }
            if (ContainsOre(normalized, "nativeplatinum") || ContainsOre(normalized, "native-platinum") || ContainsOre(normalized, "sperrylite"))
            {
                metal = "platinum";
                return true;
            }

            return false;
        }

        public static int GetMeltingTemperature(string metal)
        {
            return metal switch
            {
                "bismuth" => 271,
                "lead" => 328,
                "tin" => 232,
                "zinc" => 420,
                "silver" => 962,
                "gold" => 1064,
                "copper" => 1085,
                "platinum" => 1768,
                _ => 1200
            };
        }

        private static double GetTierMultiplier(MuckEntry entry, double effectiveAvailability, bool hasProperFlux)
        {
            InterestingMeFixConfig cfg = InterestingMeFixModSystem.Config;
            bool raw = string.Equals(NormalizeProcessingVariant(entry.ProcessingVariant), "raw", StringComparison.OrdinalIgnoreCase);
            bool concentrated = entry.Concentration >= 25.01f;
            bool optimized = entry.Concentration >= 99.5f && effectiveAvailability >= 0.95;

            if (optimized) return Math.Max(0.0, cfg.RefinedOptimizedMultiplier);
            if (hasProperFlux || entry.Roasted || effectiveAvailability >= 0.6) return Math.Max(0.0, cfg.RefinedProperFluxMultiplier);
            if (!raw || concentrated) return Math.Max(0.0, cfg.RefinedBasicProcessingMultiplier);
            return Math.Max(0.0, cfg.RefinedNoProcessingMultiplier);
        }

        private static bool ContainsOre(string normalizedOreCode, string oreName)
        {
            return normalizedOreCode.Contains(oreName, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeProcessingVariant(string processingVariant)
        {
            if (string.IsNullOrWhiteSpace(processingVariant)) return "raw";
            string normalized = processingVariant.ToLowerInvariant();
            return normalized == "coarse" || normalized == "fine" ? normalized : "raw";
        }
    }

    internal static class SieveParityCarryStore
    {
        private const string AttrKey = "interestingmefixSieveCarry";
        private static readonly Dictionary<string, Dictionary<string, double>> CarryBySieve = new Dictionary<string, Dictionary<string, double>>();

        public static double GetCarry(BlockEntitySieve sieve, string carryKey)
        {
            if (sieve == null || string.IsNullOrWhiteSpace(carryKey)) return 0.0;
            string sieveKey = GetSieveKey(sieve);
            return CarryBySieve.TryGetValue(sieveKey, out var carryByKey) &&
                   carryByKey.TryGetValue(carryKey, out double carry)
                ? carry
                : 0.0;
        }

        public static void SetCarry(BlockEntitySieve sieve, string carryKey, double carry)
        {
            if (sieve == null || string.IsNullOrWhiteSpace(carryKey)) return;
            string sieveKey = GetSieveKey(sieve);

            if (!CarryBySieve.TryGetValue(sieveKey, out var carryByKey))
            {
                carryByKey = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                CarryBySieve[sieveKey] = carryByKey;
            }

            if (carry <= 0.000001)
            {
                carryByKey.Remove(carryKey);
            }
            else
            {
                carryByKey[carryKey] = Math.Round(carry, 6, MidpointRounding.AwayFromZero);
            }

            sieve.MarkDirty(false, null);
        }

        public static void ToTreeAttributes(BlockEntitySieve sieve, ITreeAttribute tree)
        {
            if (sieve == null || tree == null) return;
            string sieveKey = GetSieveKey(sieve);
            tree.RemoveAttribute(AttrKey);

            if (!CarryBySieve.TryGetValue(sieveKey, out var carryByKey) || carryByKey.Count == 0) return;

            ITreeAttribute carryTree = tree.GetOrAddTreeAttribute(AttrKey);
            int index = 0;
            foreach (var kvp in carryByKey)
            {
                if (kvp.Value <= 0.000001) continue;
                ITreeAttribute node = carryTree.GetOrAddTreeAttribute(index.ToString());
                node.SetString("key", kvp.Key);
                node.SetDouble("carry", kvp.Value);
                index++;
            }
        }

        public static void FromTreeAttributes(BlockEntitySieve sieve, ITreeAttribute tree)
        {
            if (sieve == null || tree == null) return;
            string sieveKey = GetSieveKey(sieve);
            CarryBySieve.Remove(sieveKey);

            ITreeAttribute carryTree = tree.GetTreeAttribute(AttrKey);
            if (carryTree == null) return;

            var carryByKey = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            int index = 0;
            while (true)
            {
                ITreeAttribute node = carryTree.GetTreeAttribute(index.ToString());
                if (node == null) break;

                string key = node.GetString("key", null);
                double carry = node.GetDouble("carry", 0.0);
                if (!string.IsNullOrWhiteSpace(key) && carry > 0.000001)
                {
                    carryByKey[key] = Math.Round(carry, 6, MidpointRounding.AwayFromZero);
                }

                index++;
            }

            if (carryByKey.Count > 0)
            {
                CarryBySieve[sieveKey] = carryByKey;
            }
        }

        private static string GetSieveKey(BlockEntitySieve sieve)
        {
            BlockPos pos = sieve.Pos;
            if (pos == null) return "unknown";
            return pos.dimension + ":" + pos.X + ":" + pos.Y + ":" + pos.Z;
        }
    }

    [HarmonyPatch(typeof(BlockMuckPile), nameof(BlockMuckPile.OnBlockInteractStart))]
    internal static class RightClickPickupPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            BlockMuckPile __instance,
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            ref bool __result)
        {
            if (!InterestingMeFixModSystem.Config.RightClickPickup) return;
            if (__result) return; // IME already handled (e.g. bag-deposit while sneaking)
            if (world == null || world.Side != EnumAppSide.Server) return;
            if (byPlayer == null || blockSel?.Position == null) return;
            if (byPlayer.Entity?.Controls?.Sneak == true) return; // Sneak is reserved for IME's bag-deposit

            BlockPos clickPos = blockSel.Position;

            // Walk down to column bottom (last contiguous BlockMuckPile).
            BlockPos bottomPos = clickPos.Copy();
            while (bottomPos.Y > 0 && world.BlockAccessor.GetBlock(bottomPos.DownCopy(1), BlockLayersAccess.Solid) is BlockMuckPile)
            {
                bottomPos.Down(1);
            }

            try
            {
                Item muckItem = world.GetItem(new AssetLocation("interestingme:muck"));
                if (muckItem == null) return;

                // Aggregate composition across the whole column.
                // Some piles spawn in a broken state where upper blocks have a BE and the
                // bottom does not; others have no BE at all on layers above 1. Reconstruct
                // missing layers from the block-code variant so nothing voids.
                MuckComposition aggregate = new MuckComposition();
                int totalLayers = 0;
                int blocksScanned = 0;
                int blocksWithBe = 0;
                int blocksSynthesized = 0;

                BlockPos scanPos = bottomPos.Copy();
                while (world.BlockAccessor.GetBlock(scanPos, BlockLayersAccess.Solid) is BlockMuckPile mp && blocksScanned < 16)
                {
                    blocksScanned++;
                    BlockEntityMuckPile beAt = world.BlockAccessor.GetBlockEntity(scanPos) as BlockEntityMuckPile;
                    if (beAt != null && beAt.Composition != null && beAt.Composition.TotalLayers > 0)
                    {
                        aggregate.Merge(beAt.Composition);
                        totalLayers += beAt.Composition.TotalLayers;
                        blocksWithBe++;
                    }
                    else
                    {
                        if (MuckCompositionRecoveryCache.TryGet(scanPos, out MuckComposition cached))
                        {
                            aggregate.Merge(cached);
                            totalLayers += cached.TotalLayers;
                            blocksSynthesized++;
                        }
                        else if (MuckPileBEAutoHeal.TryBuildCompositionFromVisibleBlock(mp, out MuckComposition visibleComp, out int visibleLayers, out bool displayOnlyStone))
                        {
                            if (displayOnlyStone &&
                                InterestingMeFixModSystem.Config.BypassStoneMuck &&
                                !InterestingMeFixModSystem.Config.RecoverDisplayOnlyStoneMuckWhenStoneBypassIsEnabled)
                            {
                                world.Logger.Warning(
                                    "[InterestingMeFix] Pickup skipped display-only stone muck at {0}; original ore composition is unavailable.",
                                    scanPos);
                            }
                            else
                            {
                                aggregate.Merge(visibleComp);
                                totalLayers += visibleLayers;
                                blocksSynthesized++;
                            }
                        }
                    }
                    scanPos.Up(1);
                }

                if (totalLayers <= 0)
                {
                    world.Logger.Notification("[InterestingMeFix] Pickup at {0}: column had 0 reconstructable layers, bailing.", clickPos);
                    return;
                }

                ItemStack stack = new ItemStack(muckItem, totalLayers);
                MuckComposition.ToItemStack(stack, aggregate, aggregate.GetDominantProcessingVariant());

                bool gave = byPlayer.InventoryManager.TryGiveItemstack(stack, true);
                if (!gave || stack.StackSize > 0)
                {
                    world.SpawnItemEntity(stack, clickPos.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                // Clear every block in the column.
                BlockPos clearPos = bottomPos.Copy();
                int cleared = 0;
                while (world.BlockAccessor.GetBlock(clearPos, BlockLayersAccess.Solid) is BlockMuckPile)
                {
                    world.BlockAccessor.SetBlock(0, clearPos, BlockLayersAccess.Solid);
                    world.BlockAccessor.MarkBlockDirty(clearPos, (IPlayer)null);
                    world.BlockAccessor.TriggerNeighbourBlockUpdate(clearPos);
                    clearPos.Up(1);
                    if (++cleared > 16) break;
                }

                world.Logger.Notification(
                    "[InterestingMeFix] Pickup at {0} (bottom {1}): {2} layer(s) total — {3} block(s) cleared, {4} BE-tracked, {5} synthesized.",
                    clickPos, bottomPos, totalLayers, cleared, blocksWithBe, blocksSynthesized);

                __result = true;
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] Right-click pickup error: {0}", ex.Message);
            }
        }
    }

    // Bugged piles in older worlds have no BlockEntityMuckPile attached. Every IME code path
    // (break-with-bag, layer extract, right-click) is gated on `is BlockEntityMuckPile be`, so
    // without a BE the block falls through to Block.OnBlockBroken — one-shot destroy with no
    // drops, no bag deposit, no layered extraction.
    //
    // The block-code variant (muckpile-{processing}-{display}-{ore}-{rock}-{layer}) is enough
    // to reconstruct a faithful composition. We spawn the BE in-place on first interact/break
    // and let the original IME code run unmodified afterwards.
    internal static class MuckPileBEAutoHeal
    {
        internal static bool TryHeal(IWorldAccessor world, BlockPos pos, out int healedLayers)
        {
            healedLayers = 0;
            if (world == null || world.Side != EnumAppSide.Server) return false;
            if (pos == null) return false;

            Block atBlock = world.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
            if (!(atBlock is BlockMuckPile)) return false;
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMuckPile) return false;

            MuckComposition comp;
            if (MuckCompositionRecoveryCache.TryGet(pos, out MuckComposition cached))
            {
                comp = cached;
                healedLayers = comp.TotalLayers;
            }
            else
            {
                if (!TryBuildCompositionFromVisibleBlock(atBlock, out comp, out healedLayers, out bool displayOnlyStone)) return false;
                if (displayOnlyStone &&
                    InterestingMeFixModSystem.Config.BypassStoneMuck &&
                    !InterestingMeFixModSystem.Config.RecoverDisplayOnlyStoneMuckWhenStoneBypassIsEnabled)
                {
                    world.Logger.Warning(
                        "[InterestingMeFix] Refused display-only stone muck recovery at {0}; original ore composition is unavailable.",
                        pos);
                    return false;
                }
            }

            world.BlockAccessor.SpawnBlockEntity("MuckPile", pos);
            if (!(world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMuckPile be)) return false;

            be.AddComposition(comp);
            return true;
        }

        internal static bool TryBuildCompositionFromVisibleBlock(Block block, out MuckComposition comp, out int layer, out bool displayOnlyStone)
        {
            comp = null;
            layer = 0;
            displayOnlyStone = false;

            string path = block?.Code?.Path ?? "";
            string[] parts = path.Split('-');
            if (parts.Length < 6) return false;

            string processing = NormalizeProcessingVariant(parts[1]);
            string display = parts[2];
            string ore = parts[3];
            string rock = string.IsNullOrWhiteSpace(parts[4]) ? "granite" : parts[4];
            if (!int.TryParse(parts[5], out layer) || layer <= 0) return false;

            comp = new MuckComposition();
            if (string.Equals(display, "ore", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ore, "none", StringComparison.OrdinalIgnoreCase))
            {
                comp.Add("game:ore-" + ore, layer, "game:rock-" + rock, processing);
            }
            else
            {
                displayOnlyStone = true;
                comp.Add("game:rock-" + rock, layer, null, processing);
            }

            return comp.TotalLayers > 0;
        }

        private static string NormalizeProcessingVariant(string processing)
        {
            if (string.IsNullOrWhiteSpace(processing)) return "raw";
            string normalized = processing.ToLowerInvariant();
            return normalized == "coarse" || normalized == "fine" ? normalized : "raw";
        }
    }

    [HarmonyPatch(typeof(BlockEntityMuckPile), "TryDepositCompositionIntoColumn")]
    internal static class MuckDepositCompositionSafetyPatch
    {
        private const long MissingBeWarningIntervalMs = 30000;
        private static long nextMissingBeWarningMs;
        private static int suppressedMissingBeWarnings;

        [HarmonyPrefix]
        public static bool Prefix(IWorldAccessor world, BlockPos targetPos, MuckComposition composition, ref bool __result)
        {
            if (!InterestingMeFixModSystem.Config.AutoHealMissingBE) return true;
            if (world == null || world.Side != EnumAppSide.Server || targetPos == null || composition == null || composition.TotalLayers <= 0)
            {
                __result = false;
                return false;
            }

            try
            {
                __result = TryDepositSafely(world, targetPos, composition);
                return false;
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] Safe muck deposit failed at {0}: {1}", targetPos, ex.Message);
                return true;
            }
        }

        private static bool TryDepositSafely(IWorldAccessor world, BlockPos targetPos, MuckComposition composition)
        {
            if (ShouldDiscardStoneOnlyMuck(composition))
            {
                return true;
            }

            Block targetBlock = world.BlockAccessor.GetBlock(targetPos, BlockLayersAccess.Solid);
            if (targetBlock is BlockMuckPile)
            {
                bool hadBlockEntity = world.BlockAccessor.GetBlockEntity(targetPos) is BlockEntityMuckPile;
                BlockEntityMuckPile targetBe = EnsureMuckPileBE(world, targetPos);
                if (targetBe == null)
                {
                    ClearMuckBlock(world, targetPos);
                    WarnMissingBlockEntity(world, targetPos, targetBlock);
                    return true;
                }

                if (!hadBlockEntity && MuckCompositionRecoveryCache.TryGet(targetPos, out MuckComposition cached))
                {
                    targetBe.AddComposition(cached);
                }
                targetBe.AddComposition(composition);
                MuckCompositionRecoveryCache.Remember(targetPos, targetBe.Composition);
                return true;
            }

            MuckDisplayVariant displayVariant = ResolveDisplayVariant(composition);
            int startLayer = Math.Max(1, Math.Min(8, composition.TotalLayers));
            Block layerBlock = world.GetBlock(BlockMuckPile.CodeForLayer(startLayer, displayVariant));
            if (layerBlock == null || targetBlock == null || !targetBlock.IsReplacableBy(layerBlock)) return false;

            world.BlockAccessor.SetBlock(layerBlock.Id, targetPos, BlockLayersAccess.Solid);
            world.BlockAccessor.MarkBlockDirty(targetPos, (IPlayer)null);

            BlockEntityMuckPile newBe = EnsureMuckPileBE(world, targetPos);
            if (newBe == null)
            {
                ClearMuckBlock(world, targetPos);
                WarnMissingBlockEntity(world, targetPos, layerBlock);
                return true;
            }

            newBe.AddComposition(composition);
            MuckCompositionRecoveryCache.Remember(targetPos, newBe.Composition);
            return true;
        }

        private static bool ShouldDiscardStoneOnlyMuck(MuckComposition composition)
        {
            if (!InterestingMeFixModSystem.Config.BypassStoneMuck) return false;
            if (composition == null || composition.TotalLayers <= 0) return false;
            return composition.GetTotalOreLayers() <= 0;
        }

        private static BlockEntityMuckPile EnsureMuckPileBE(IWorldAccessor world, BlockPos pos)
        {
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMuckPile existing) return existing;

            world.BlockAccessor.SpawnBlockEntity("MuckPile", pos);
            return world.BlockAccessor.GetBlockEntity(pos) as BlockEntityMuckPile;
        }

        private static void ClearMuckBlock(IWorldAccessor world, BlockPos pos)
        {
            world.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Solid);
            world.BlockAccessor.MarkBlockDirty(pos, (IPlayer)null);
            world.BlockAccessor.TriggerNeighbourBlockUpdate(pos);
        }

        private static void WarnMissingBlockEntity(IWorldAccessor world, BlockPos targetPos, Block layerBlock)
        {
            long now = Environment.TickCount64;
            if (now < nextMissingBeWarningMs)
            {
                suppressedMissingBeWarnings++;
                return;
            }

            int suppressed = suppressedMissingBeWarnings;
            suppressedMissingBeWarnings = 0;
            nextMissingBeWarningMs = now + MissingBeWarningIntervalMs;

            if (suppressed > 0)
            {
                world.Logger.Warning(
                    "[InterestingMeFix] Muck deposit at {0} placed {1} but could not create BlockEntity; cleared unplaceable muck to stop retry loop. Suppressed {2} similar warning(s).",
                    targetPos, layerBlock.Code, suppressed);
                return;
            }

            world.Logger.Warning(
                "[InterestingMeFix] Muck deposit at {0} placed {1} but could not create BlockEntity; cleared unplaceable muck to stop retry loop.",
                targetPos, layerBlock.Code);
        }

        private static void ScheduleCachedHeal(IWorldAccessor world, BlockPos pos)
        {
            BlockPos healPos = pos.Copy();
            world.RegisterCallback((dt) =>
            {
                try
                {
                    MuckPileBEAutoHeal.TryHeal(world, healPos, out int _);
                }
                catch (Exception ex)
                {
                    world.Logger.Warning("[InterestingMeFix] Delayed muck BE heal failed at {0}: {1}", healPos, ex.Message);
                }
            }, 50);
        }

        private static MuckDisplayVariant ResolveDisplayVariant(MuckComposition composition)
        {
            string processing = NormalizeProcessingVariant(composition.GetDominantProcessingVariant());
            string dominantStoneCode = composition.GetDominantDisplayStoneCode() ?? composition.GetDominantStoneCode();
            string rock = RockVariantFromStoneCode(dominantStoneCode);

            int totalLayers = composition.TotalLayers;
            int oreLayers = composition.GetTotalOreLayers();
            if (totalLayers > 0 && oreLayers * 2 >= totalLayers)
            {
                MuckEntry dominantOre = composition.GetDominantOreEntry();
                string ore = OreVariantFromOreCode(dominantOre?.OreCode);
                string hostRock = RockVariantFromStoneCode(dominantOre?.HostRockCode ?? dominantStoneCode);
                if (!string.Equals(ore, "none", StringComparison.OrdinalIgnoreCase))
                {
                    return new MuckDisplayVariant(processing, "ore", ore, hostRock);
                }
            }

            return new MuckDisplayVariant(processing, "stone", "none", rock);
        }

        private static string NormalizeProcessingVariant(string processing)
        {
            if (string.IsNullOrWhiteSpace(processing)) return "raw";
            string normalized = processing.ToLowerInvariant();
            return normalized == "coarse" || normalized == "fine" ? normalized : "raw";
        }

        private static string RockVariantFromStoneCode(string stoneCode)
        {
            if (string.IsNullOrWhiteSpace(stoneCode)) return "granite";
            int dash = stoneCode.LastIndexOf('-');
            if (dash < 0 || dash >= stoneCode.Length - 1) return "granite";
            return stoneCode.Substring(dash + 1);
        }

        private static string OreVariantFromOreCode(string oreCode)
        {
            if (string.IsNullOrWhiteSpace(oreCode)) return "none";
            int marker = oreCode.IndexOf("ore-", StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return "none";
            string ore = oreCode.Substring(marker + 4);
            if (ore.StartsWith("native-", StringComparison.OrdinalIgnoreCase))
            {
                ore = "native" + ore.Substring("native-".Length);
            }
            return string.IsNullOrWhiteSpace(ore) ? "none" : ore;
        }
    }

    internal static class MuckCompositionRecoveryCache
    {
        private static readonly Dictionary<string, MuckComposition> CompositionByPos = new Dictionary<string, MuckComposition>();

        public static void Remember(BlockPos pos, MuckComposition composition)
        {
            if (pos == null || composition == null || composition.TotalLayers <= 0) return;
            CompositionByPos[GetKey(pos)] = composition.Clone();
        }

        public static bool TryGet(BlockPos pos, out MuckComposition composition)
        {
            composition = null;
            if (pos == null) return false;
            if (!CompositionByPos.TryGetValue(GetKey(pos), out MuckComposition cached)) return false;
            composition = cached.Clone();
            return composition.TotalLayers > 0;
        }

        private static string GetKey(BlockPos pos)
        {
            return pos.dimension + ":" + pos.X + ":" + pos.Y + ":" + pos.Z;
        }
    }

    [HarmonyPatch(typeof(BlockMuckPile), nameof(BlockMuckPile.OnBlockBroken))]
    internal static class MuckPileHealOnBreakPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(IWorldAccessor world, BlockPos pos, IPlayer byPlayer)
        {
            if (!InterestingMeFixModSystem.Config.AutoHealMissingBE) return true;
            if (world == null || world.Side != EnumAppSide.Server) return true;
            if (pos == null || byPlayer == null) return true;

            try
            {
                if (MuckPileBEAutoHeal.TryHeal(world, pos, out int n))
                {
                    world.Logger.Notification(
                        "[InterestingMeFix] Healed missing BE on muckpile at {0} (break): {1} layer(s) reconstructed.",
                        pos, n);
                }
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] HealOnBreak error: {0}", ex.Message);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BlockMuckPile), nameof(BlockMuckPile.OnBlockInteractStart))]
    internal static class MuckPileHealOnInteractPatch
    {
        [HarmonyPrefix]
        public static void Prefix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!InterestingMeFixModSystem.Config.AutoHealMissingBE) return;
            if (world == null || world.Side != EnumAppSide.Server) return;
            if (blockSel?.Position == null) return;

            try
            {
                if (MuckPileBEAutoHeal.TryHeal(world, blockSel.Position, out int n))
                {
                    world.Logger.Notification(
                        "[InterestingMeFix] Healed missing BE on muckpile at {0} (interact): {1} layer(s) reconstructed.",
                        blockSel.Position, n);
                }
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] HealOnInteract error: {0}", ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(BlockMuckPile), nameof(BlockMuckPile.GetDrops))]
    internal static class MuckPileDropRecoveryPatch
    {
        [HarmonyPostfix]
        public static void Postfix(
            IWorldAccessor world,
            BlockPos pos,
            ref ItemStack[] __result)
        {
            if (!InterestingMeFixModSystem.Config.RecoverMuckOnDestroy) return;
            if (world == null || world.Side != EnumAppSide.Server) return;
            if (pos == null) return;
            if (__result != null && __result.Length > 0) return;

            try
            {
                if (!(world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMuckPile be)) return;
                MuckComposition comp = be.Composition;
                if (comp == null || comp.TotalLayers <= 0) return;

                Item muckItem = world.GetItem(new AssetLocation("interestingme:muck"));
                if (muckItem == null) return;

                ItemStack stack = new ItemStack(muckItem, comp.TotalLayers);
                MuckComposition.ToItemStack(stack, comp, comp.GetDominantProcessingVariant());
                __result = new[] { stack };

                world.Logger.Notification(
                    "[InterestingMeFix] Recovered {0} muck layer(s) at {1} from fallback drop path.",
                    comp.TotalLayers, pos);
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] GetDrops recovery error: {0}", ex.Message);
            }
        }
    }
}
