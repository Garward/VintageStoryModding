using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace VintageKinematics.Crafting
{
    // Hybrid kinetic sieve loot roller. For inputs with `pannable=true` we read vanilla BlockPan's
    // resolved drops table (private field, accessed once via reflection at startup) and run the
    // same shuffle-and-roll algorithm vanilla uses for the gold pan, so vanilla rates "just work".
    // Custom (non-pannable) inputs fall through to a separate VK recipe registry — added later.
    public static class PanLootRoller
    {
        private const string DefaultRockType = "granite";
        private const string DefaultGravelSourceCode = "gravel-" + DefaultRockType;
        private const string DefaultSandSourceCode = "sand-" + DefaultRockType;

        private static Dictionary<string, PanningDrop[]> panDrops;
        private static bool initialized;

        public static void Init(ICoreAPI api)
        {
            if (initialized) return;
            initialized = true;

            BlockPan pan = null;
            foreach (Block b in api.World.Blocks)
            {
                if (b is BlockPan bp) { pan = bp; break; }
            }
            if (pan == null)
            {
                api.Logger.Warning("[VintageKinematics] PanLootRoller: no BlockPan found in world.Blocks; kinetic sieve will only use custom recipes.");
                return;
            }

            FieldInfo f = typeof(BlockPan).GetField("dropsBySourceMat", BindingFlags.Instance | BindingFlags.NonPublic);
            if (f == null)
            {
                api.Logger.Warning("[VintageKinematics] PanLootRoller: BlockPan.dropsBySourceMat not found via reflection; kinetic sieve pannable rolls disabled.");
                return;
            }

            panDrops = f.GetValue(pan) as Dictionary<string, PanningDrop[]>;
            if (panDrops == null)
            {
                api.Logger.Warning("[VintageKinematics] PanLootRoller: BlockPan.dropsBySourceMat was null; pan likely not loaded yet.");
                return;
            }
            api.Logger.Notification($"[VintageKinematics] PanLootRoller: cached {panDrops.Count} pan source-material entries.");
        }

        public static bool IsPannable(Block block)
        {
            return block?.Attributes?.IsTrue("pannable") == true;
        }

        public static bool IsSieveablePanningSource(Block block)
        {
            if (block == null) return false;
            if (IsPannable(block)) return true;

            string path = block.Code?.Path;
            return IsSandSourcePath(path) || IsGravelSourcePath(path);
        }

        // Mirrors BlockPan.CreateDrop, minus the player-stat multiplier and inventory delivery.
        // Returns one rolled ItemStack or null if no drop landed this cycle.
        public static ItemStack RollPannableDrop(IWorldAccessor world, Block inputBlock)
        {
            if (panDrops == null || inputBlock == null) return null;

            string code = inputBlock.Code?.ToShortString();
            if (code == null) return null;

            string sourceCode = GetPanSourceCode(inputBlock);
            if (sourceCode == null) return null;

            PanningDrop[] drops = FindDrops(sourceCode);
            if (drops == null || drops.Length == 0) return null;

            string rocktype = inputBlock.Variant["rock"];
            if (rocktype == null && sourceCode != code)
            {
                rocktype = world.GetBlock(new AssetLocation(sourceCode))?.Variant["rock"];
            }
            if (rocktype == null && (IsSandSourcePath(inputBlock.Code.Path) || IsGravelSourcePath(inputBlock.Code.Path)))
            {
                rocktype = DefaultRockType;
            }

            PanningDrop[] shuffled = (PanningDrop[])drops.Clone();
            shuffled.Shuffle(world.Rand);

            for (int i = 0; i < shuffled.Length; i++)
            {
                PanningDrop drop = shuffled[i];
                double rnd = world.Rand.NextDouble();
                float val = drop.Chance.nextFloat();

                ItemStack stack = drop.ResolvedItemstack;
                if (drop.Code.Path.Contains("{rocktype}") && rocktype != null)
                {
                    stack = ResolveRocktype(world, drop, rocktype);
                }
                if (rnd < val && stack != null) return stack.Clone();
            }
            return null;
        }

        private static string GetPanSourceCode(Block block)
        {
            string code = block.Code?.ToShortString();
            if (code == null) return null;

            if (FindDrops(code) != null) return code;

            string path = block.Code.Path;
            if (path != code && FindDrops(path) != null) return path;

            // VK sieves intentionally accept the broader sand/gravel family. Vanilla panning
            // excludes muddy/sludgy/dirty gravel, so map those to the standard gravel table.
            if (IsSandSourcePath(path)) return DefaultSandSourceCode;
            if (IsGravelSourcePath(path)) return DefaultGravelSourceCode;

            return code;
        }

        private static PanningDrop[] FindDrops(string sourceCode)
        {
            if (panDrops == null || sourceCode == null) return null;

            foreach (string key in panDrops.Keys)
            {
                if (WildcardUtil.Match(key, sourceCode)) return panDrops[key];
            }

            return null;
        }

        private static bool IsSandSourcePath(string path)
        {
            return path != null
                && (path == "sand"
                    || path.StartsWith("sand-", StringComparison.Ordinal)
                    || path == "sandwavy"
                    || path.StartsWith("sandwavy-", StringComparison.Ordinal));
        }

        private static bool IsGravelSourcePath(string path)
        {
            return path != null
                && (path == "gravel"
                    || path.StartsWith("gravel-", StringComparison.Ordinal)
                    || path.StartsWith("dirtygravel", StringComparison.Ordinal)
                    || path == "muddygravel"
                    || path.StartsWith("muddygravel-", StringComparison.Ordinal)
                    || path == "sludgygravel"
                    || path.StartsWith("sludgygravel-", StringComparison.Ordinal));
        }

        private static ItemStack ResolveRocktype(IWorldAccessor world, PanningDrop drop, string rocktype)
        {
            string path = drop.Code.Path.Replace("{rocktype}", rocktype);
            AssetLocation loc = new AssetLocation(drop.Code.Domain, path);
            switch (drop.Type)
            {
                case EnumItemClass.Block:
                    Block b = world.GetBlock(loc);
                    return b == null ? null : new ItemStack(b);
                case EnumItemClass.Item:
                    Item it = world.GetItem(loc);
                    return it == null ? null : new ItemStack(it);
                default: return null;
            }
        }
    }
}
