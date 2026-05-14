using System;
using System.Collections.Generic;
using System.Reflection;
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
                "[InterestingMeFix] Config: restoreOre={0} bypassStone={1} rightClickPickup={2} recoverOnDestroy={3} autoHealBE={4}",
                Config.RestoreOreOnMuckSpawnFail,
                Config.BypassStoneMuck,
                Config.RightClickPickup,
                Config.RecoverMuckOnDestroy,
                Config.AutoHealMissingBE);

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
            };
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
            if (__result) return; // IME already handled (e.g. bag-deposit while sneaking)
            if (world == null || world.Side != EnumAppSide.Server) return;
            if (byPlayer == null || blockSel?.Position == null) return;
            if (byPlayer.Entity?.Controls?.Sneak == true) return; // Sneak is reserved for IME's bag-deposit

            BlockPos clickPos = blockSel.Position;

            // Walk down to column bottom (last contiguous BlockMuckPile).
            BlockPos bottomPos = clickPos.Copy();
            while (bottomPos.Y > 0 && world.BlockAccessor.GetBlock(bottomPos.DownCopy(1)) is BlockMuckPile)
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
                while (world.BlockAccessor.GetBlock(scanPos) is BlockMuckPile mp && blocksScanned < 16)
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
                        // No BE — synthesize from variant. Code path is
                        // muckpile-{processing}-{display}-{ore}-{rock}-{layer}.
                        string path = mp.Code?.Path ?? "";
                        string[] parts = path.Split('-');
                        if (parts.Length >= 6)
                        {
                            string processing = parts[1];
                            string display = parts[2];
                            string ore = parts[3];
                            string rock = parts[4];
                            int layer = 0;
                            int.TryParse(parts[5], out layer);
                            if (layer > 0)
                            {
                                if (string.Equals(display, "ore", StringComparison.OrdinalIgnoreCase) &&
                                    !string.Equals(ore, "none", StringComparison.OrdinalIgnoreCase))
                                {
                                    aggregate.Add("game:ore-" + ore, layer, "game:rock-" + rock, processing);
                                }
                                else
                                {
                                    aggregate.Add("game:rock-" + rock, layer, null, processing);
                                }
                                totalLayers += layer;
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
                while (world.BlockAccessor.GetBlock(clearPos) is BlockMuckPile)
                {
                    world.BlockAccessor.SetBlock(0, clearPos);
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

            Block atBlock = world.BlockAccessor.GetBlock(pos);
            if (!(atBlock is BlockMuckPile)) return false;
            if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMuckPile) return false;

            string path = atBlock.Code?.Path ?? "";
            string[] parts = path.Split('-');
            if (parts.Length < 6) return false;

            string processing = parts[1];
            string display = parts[2];
            string ore = parts[3];
            string rock = parts[4];
            if (!int.TryParse(parts[5], out int layer) || layer <= 0) return false;

            MuckComposition comp = new MuckComposition();
            if (string.Equals(display, "ore", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ore, "none", StringComparison.OrdinalIgnoreCase))
            {
                comp.Add("game:ore-" + ore, layer, "game:rock-" + rock, processing);
            }
            else
            {
                comp.Add("game:rock-" + rock, layer, null, processing);
            }

            world.BlockAccessor.SpawnBlockEntity("MuckPile", pos);
            if (!(world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMuckPile be)) return false;

            be.AddComposition(comp);
            healedLayers = layer;
            return true;
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
