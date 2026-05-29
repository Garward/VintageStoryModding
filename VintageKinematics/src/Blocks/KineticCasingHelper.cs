using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.Blocks
{
    internal static class KineticCasingHelper
    {
        private const string CasingPrefix = "casing-";

        public static bool TryApplyCasing(IWorldAccessor world, IPlayer player, BlockSelection blockSel, string targetPrefix)
        {
            if (world == null || blockSel == null) return false;

            ItemSlot slot = player?.InventoryManager?.ActiveHotbarSlot;
            if (!TryGetHeldCasingWood(slot, out string wood)) return false;

            Block block = world.BlockAccessor.GetBlock(blockSel.Position);
            string path = block?.Code?.Path;
            if (path == null || !path.StartsWith(targetPrefix + "-")) return false;
            string axis = block?.Variant?["axis"];
            if (axis != "x" && axis != "y" && axis != "z") return false;

            string encasedPrefix = targetPrefix switch
            {
                "shaft" => "encasedshaft",
                "cogwheel" => "encasedcogwheel",
                "largecogwheel" => "encasedlargecogwheel",
                _ => null
            };
            if (encasedPrefix == null) return false;

            string encasedCode = targetPrefix == "shaft"
                ? $"{encasedPrefix}-{wood}-{axis}"
                : $"{encasedPrefix}-{wood}-{ResolveAttachedShaftPort(world, blockSel, axis)}-{axis}";

            Block encased = world.GetBlock(new AssetLocation("vintagekinematics", encasedCode));
            if (encased == null || encased.Id == 0) return false;

            if (world.Side != EnumAppSide.Server) return true;
            if (!world.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) return true;

            ReplaceKineticBlock(world, blockSel.Position, encased);

            if (player?.WorldData?.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            world.PlaySoundAt(new AssetLocation("sounds/block/planks"), blockSel.Position, 0, player, true, 16, 0.8f);
            return true;
        }

        public static bool TryRetextureCasing(IWorldAccessor world, IPlayer player, BlockSelection blockSel)
        {
            if (world == null || blockSel == null) return false;
            if (!IsSneaking(player)) return false;

            ItemSlot slot = player.InventoryManager?.ActiveHotbarSlot;
            if (!TryGetHeldPlankWood(slot, out string wood)) return false;

            Block block = world.BlockAccessor.GetBlock(blockSel.Position);
            if (!TryResolveEncasedBlock(world, block, out string encasedPrefix, out string currentWood, out string axis, out string shaftPort)) return false;

            if (wood == currentWood) return true;

            string replacementCode = shaftPort == null
                ? $"{encasedPrefix}{wood}-{axis}"
                : $"{encasedPrefix}{wood}-{shaftPort}-{axis}";
            Block replacement = world.GetBlock(new AssetLocation("vintagekinematics", replacementCode));
            if (replacement == null || replacement.Id == 0) return false;

            if (world.Side != EnumAppSide.Server) return true;
            if (!world.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) return true;

            ReplaceKineticBlock(world, blockSel.Position, replacement);

            if (player.WorldData?.CurrentGameMode != EnumGameMode.Creative)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
            }

            world.PlaySoundAt(new AssetLocation("sounds/block/planks"), blockSel.Position, 0, player, true, 16, 0.9f);
            return true;
        }

        public static bool TryRemoveCasing(ICoreAPI api, IPlayer player, BlockSelection blockSel)
        {
            if (api?.World == null || blockSel == null) return false;

            Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);
            if (!TryResolveUncasedBlock(api.World, block, out Block uncased, out string wood)) return false;

            if (api.Side != EnumAppSide.Server) return true;
            if (!api.World.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak)) return true;

            ReplaceKineticBlock(api.World, blockSel.Position, uncased);

            Item casing = api.World.GetItem(new AssetLocation("vintagekinematics", $"{CasingPrefix}{wood}"));
            if (casing != null && casing.Id != 0 && player?.WorldData?.CurrentGameMode != EnumGameMode.Creative)
            {
                ItemStack stack = new ItemStack(casing);
                if (player == null || !player.InventoryManager.TryGiveItemstack(stack, true))
                {
                    api.World.SpawnItemEntity(stack, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                }
            }

            api.World.PlaySoundAt(new AssetLocation("sounds/block/planks"), blockSel.Position, 0, player, true, 16, 0.8f);
            return true;
        }

        private static bool TryGetHeldCasingWood(ItemSlot slot, out string wood)
        {
            wood = null;
            string path = slot?.Itemstack?.Collectible?.Code?.Path;
            if (path == null || !path.StartsWith(CasingPrefix)) return false;
            wood = path.Substring(CasingPrefix.Length);
            return !string.IsNullOrEmpty(wood);
        }

        private static bool TryGetHeldPlankWood(ItemSlot slot, out string wood)
        {
            wood = null;
            AssetLocation code = slot?.Itemstack?.Collectible?.Code;
            if (code == null) return false;

            if (code.Path.StartsWith("plank-"))
            {
                wood = code.Path.Substring("plank-".Length);
            }
            else if (code.Path.StartsWith("planks-"))
            {
                wood = code.Path.Substring("planks-".Length);
            }
            else
            {
                return false;
            }

            wood = StripPlankOrientationSuffix(wood);
            return !string.IsNullOrEmpty(wood);
        }

        private static string StripPlankOrientationSuffix(string wood)
        {
            if (string.IsNullOrEmpty(wood)) return wood;
            string[] suffixes = { "-ns", "-we", "-ud", "-hor", "-ver" };
            foreach (string suffix in suffixes)
            {
                if (wood.EndsWith(suffix))
                {
                    return wood.Substring(0, wood.Length - suffix.Length);
                }
            }
            return wood;
        }

        private static bool IsSneaking(IPlayer player)
        {
            return player?.Entity?.Controls?.Sneak == true
                || player?.Entity?.Controls?.ShiftKey == true;
        }

        private static string ResolveAttachedShaftPort(IWorldAccessor world, BlockSelection blockSel, string axis)
        {
            Vec3i axisVec = AxisUnit(axis);
            if (axisVec == null) return "none";

            bool neg = HasCoaxialShaftLikeNeighbor(world, blockSel.Position, axis, new Vec3i(-axisVec.X, -axisVec.Y, -axisVec.Z));
            bool pos = HasCoaxialShaftLikeNeighbor(world, blockSel.Position, axis, axisVec);

            if (neg && !pos) return "neg";
            if (pos && !neg) return "pos";

            if (blockSel.Face != null)
            {
                Vec3i face = blockSel.Face.Normali;
                if (face.X == -axisVec.X && face.Y == -axisVec.Y && face.Z == -axisVec.Z && neg) return "neg";
                if (face.X == axisVec.X && face.Y == axisVec.Y && face.Z == axisVec.Z && pos) return "pos";
            }

            return "none";
        }

        private static bool HasCoaxialShaftLikeNeighbor(IWorldAccessor world, BlockPos pos, string axis, Vec3i offset)
        {
            BlockPos neighborPos = pos.AddCopy(offset.X, offset.Y, offset.Z);
            WorldNodeProvider nodes = new WorldNodeProvider(world);
            if (!nodes.TryGetNode(neighborPos, out KineticNode node)) return false;

            if (node.Role == EnumKineticRole.Gearbox)
            {
                // Gearbox Axis is the closed axis, not the shaft/port axis. A gearbox exposes
                // ports on every face perpendicular to that closed axis, so the coaxial port
                // exists when the shared face axis is not the stored gearbox axis.
                return AxisToString(node.Axis) != axis;
            }

            if (AxisToString(node.Axis) != axis) return false;
            if (node.Role == EnumKineticRole.Shaft
                || node.Role == EnumKineticRole.EncasedShaft
                || node.Role == EnumKineticRole.HandCrank
                )
            {
                return true;
            }

            if (node.Role == EnumKineticRole.SmallCogwheel || node.Role == EnumKineticRole.LargeCogwheel)
            {
                return true;
            }

            if (node.Role == EnumKineticRole.EncasedSmallCogwheel || node.Role == EnumKineticRole.EncasedLargeCogwheel)
            {
                return EncasedCogHasPortToward(node.BlockCode, axis, new Vec3i(-offset.X, -offset.Y, -offset.Z));
            }

            return false;
        }

        private static bool EncasedCogHasPortToward(string path, string axis, Vec3i offsetFromNeighbor)
        {
            if (string.IsNullOrEmpty(path)) return false;

            Vec3i axisVec = AxisUnit(axis);
            if (axisVec == null) return false;

            bool hasNegPort = path.Contains("-neg-");
            bool hasPosPort = path.Contains("-pos-");
            if (!hasNegPort && !hasPosPort) return false;

            int sign = 0;
            if (axisVec.X != 0) sign = System.Math.Sign(offsetFromNeighbor.X);
            else if (axisVec.Y != 0) sign = System.Math.Sign(offsetFromNeighbor.Y);
            else if (axisVec.Z != 0) sign = System.Math.Sign(offsetFromNeighbor.Z);

            return (sign < 0 && hasNegPort) || (sign > 0 && hasPosPort);
        }

        private static Vec3i AxisUnit(string axis)
        {
            return axis switch
            {
                "x" => new Vec3i(1, 0, 0),
                "y" => new Vec3i(0, 1, 0),
                "z" => new Vec3i(0, 0, 1),
                _ => null
            };
        }

        private static string AxisToString(EnumKineticAxis axis)
        {
            return axis switch
            {
                EnumKineticAxis.X => "x",
                EnumKineticAxis.Y => "y",
                EnumKineticAxis.Z => "z",
                _ => null
            };
        }

        private static bool TryResolveEncasedBlock(IWorldAccessor world, Block block, out string encasedPrefix, out string wood, out string axis, out string shaftPort)
        {
            encasedPrefix = null;
            wood = null;
            axis = null;
            shaftPort = null;

            string path = block?.Code?.Path;
            axis = block?.Variant?["axis"];
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(axis)) return false;

            if (path.StartsWith("encasedlargecogwheel-")) encasedPrefix = "encasedlargecogwheel-";
            else if (path.StartsWith("encasedcogwheel-")) encasedPrefix = "encasedcogwheel-";
            else if (path.StartsWith("encasedshaft-")) encasedPrefix = "encasedshaft-";
            else return false;

            string suffix = "-" + axis;
            if (!path.EndsWith(suffix)) return false;

            string body = path.Substring(encasedPrefix.Length, path.Length - encasedPrefix.Length - suffix.Length);
            if (encasedPrefix == "encasedshaft-")
            {
                wood = body;
                return !string.IsNullOrEmpty(wood);
            }

            int lastDash = body.LastIndexOf('-');
            if (lastDash < 0)
            {
                wood = body;
                shaftPort = "none";
                return !string.IsNullOrEmpty(wood);
            }

            wood = body.Substring(0, lastDash);
            shaftPort = body.Substring(lastDash + 1);
            if (shaftPort != "none" && shaftPort != "neg" && shaftPort != "pos") return false;
            return !string.IsNullOrEmpty(wood);
        }

        private static bool TryResolveUncasedBlock(IWorldAccessor world, Block block, out Block uncased, out string wood)
        {
            uncased = null;
            wood = null;

            string path = block?.Code?.Path;
            string axis = block?.Variant?["axis"];
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(axis)) return false;

            if (!TryResolveEncasedBlock(world, block, out string encasedPrefix, out wood, out axis, out _)) return false;

            string targetPrefix = encasedPrefix switch
            {
                "encasedlargecogwheel-" => "largecogwheel",
                "encasedcogwheel-" => "cogwheel",
                "encasedshaft-" => "shaft",
                _ => null
            };
            if (targetPrefix == null) return false;

            uncased = world.GetBlock(new AssetLocation("vintagekinematics", $"{targetPrefix}-{axis}"));
            return uncased != null && uncased.Id != 0;
        }

        private static void ReplaceKineticBlock(IWorldAccessor world, BlockPos pos, Block newBlock)
        {
            KineticNetworkManager networks = world.Api?.ModLoader.GetModSystem<KineticNetworkManager>();
            networks?.OnRemoved(pos);
            world.BlockAccessor.SetBlock(0, pos);
            world.BlockAccessor.SetBlock(newBlock.Id, pos);
            world.BlockAccessor.MarkBlockDirty(pos);
            networks?.OnPlaced(pos);
        }
    }
}
