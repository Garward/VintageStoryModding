using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using IME;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

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
                "[InterestingMeFix] Config: restoreOre={0} bypassStone={1} rightClickPickup={2} recoverOnDestroy={3}",
                Config.RestoreOreOnMuckSpawnFail,
                Config.BypassStoneMuck,
                Config.RightClickPickup,
                Config.RecoverMuckOnDestroy);

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
                Block b = world.BlockAccessor.GetBlock(scan);
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
            if (__result) return;
            if (world == null || world.Side != EnumAppSide.Server) return;
            if (byPlayer == null || blockSel?.Position == null) return;
            if (byPlayer.Entity?.Controls?.Sneak == true) return;

            if (!(world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityMuckPile)) return;

            try
            {
                ((Block)__instance).OnBlockBroken(world, blockSel.Position, byPlayer, 1f);
                __result = true;
            }
            catch (Exception ex)
            {
                world.Logger.Warning("[InterestingMeFix] Right-click pickup error: {0}", ex.Message);
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
