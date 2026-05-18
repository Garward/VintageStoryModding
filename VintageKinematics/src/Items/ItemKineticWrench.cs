using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;
using VintageKinematics.Network;

namespace VintageKinematics.Items
{
    public class ItemKineticWrench : Item
    {
        private static readonly AssetLocation RotateSound = new AssetLocation("vintagekinematics", "sounds/tool/ratchet.ogg");

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent || byEntity == null || blockSel == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefaultAction;

            IPlayer player = (byEntity as EntityPlayer)?.Player;
            if (!byEntity.World.Claims.TryAccess(player, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                api.World.BlockAccessor.MarkBlockEntityDirty(blockSel.Position.AddCopy(blockSel.Face));
                api.World.BlockAccessor.MarkBlockDirty(blockSel.Position.AddCopy(blockSel.Face));
                return;
            }

            if (api.Side != EnumAppSide.Server) return;

            if (TryHandleBeltInteraction(player, blockSel)) return;
            if (player?.Entity?.Controls?.Sneak == true && TryPickupKineticBlock(player, blockSel)) return;

            if (TryRotateBlock(player, blockSel))
            {
                (api.World as IClientWorldAccessor)?.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
            }
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new WorldInteraction[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-wrench-kinetic-rotate",
                    MouseButton = EnumMouseButton.Right
                }
            };
        }

        private bool TryHandleBeltInteraction(IPlayer player, BlockSelection blockSel)
        {
            if (api.World.BlockAccessor.GetBlockEntity(blockSel.Position) is not BEBelt belt) return false;

            bool sneaking = player?.Entity?.Controls?.Sneak == true;
            if (sneaking && belt.Part == EnumBeltPart.Middle && belt.HasShaft)
            {
                string axis = belt.InsertedShaftAxis ?? "y";
                Block shaftBlock = api.World.GetBlock(new AssetLocation("vintagekinematics", "shaft-" + axis));
                if (shaftBlock != null)
                {
                    ItemStack drop = new ItemStack(shaftBlock);
                    if (player == null || !player.InventoryManager.TryGiveItemstack(drop))
                    {
                        api.World.SpawnItemEntity(drop, blockSel.Position.ToVec3d().Add(0.5, 0.5, 0.5));
                    }
                }

                belt.SetShaft(false);
            }

            return true;
        }

        private bool TryPickupKineticBlock(IPlayer player, BlockSelection blockSel)
        {
            BlockPos pos = ResolveControllerPos(blockSel.Position);
            Block block = api.World.BlockAccessor.GetBlock(pos);
            BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos);

            if (!IsKineticPickupTarget(block, be)) return false;

            IInventory inv = InventoryOf(be);
            if (inv != null && !InventoryEmpty(inv))
            {
                Notify(player, "Empty the machine first.");
                return true;
            }

            ItemStack drop = block.OnPickBlock(api.World, pos) ?? new ItemStack(block);
            KineticNetworkManager networks = api.ModLoader.GetModSystem<KineticNetworkManager>();
            networks?.OnRemoved(pos);

            api.World.BlockAccessor.SetBlock(0, pos);
            api.World.BlockAccessor.MarkBlockDirty(pos);
            api.World.SpawnItemEntity(drop, pos.ToVec3d().Add(0.5, 0.5, 0.5));
            PlayRotateSound(pos);
            return true;
        }

        private BlockPos ResolveControllerPos(BlockPos pos)
        {
            Block block = api.World.BlockAccessor.GetBlock(pos);
            if (block is BlockMultiblock mb)
            {
                return new BlockPos(pos.X + mb.OffsetInv.X, pos.Y + mb.OffsetInv.Y, pos.Z + mb.OffsetInv.Z, pos.dimension);
            }
            return pos;
        }

        private static bool IsKineticPickupTarget(Block block, BlockEntity be)
        {
            if (block?.Code?.Domain != "vintagekinematics") return false;

            string path = block.Code.Path ?? "";
            if (path.StartsWith("belt-") || path.StartsWith("kineticboreshaft-") || path.StartsWith("creativemotor-")) return false;

            if (be?.GetBehavior<BEBehaviorKinetic>() != null) return true;
            if (block.BlockEntityBehaviors == null) return false;

            foreach (BlockEntityBehaviorType behavior in block.BlockEntityBehaviors)
            {
                if (behavior?.Name == "Kinetic") return true;
            }

            return false;
        }

        private static IInventory InventoryOf(BlockEntity be)
        {
            if (be is IBlockEntityContainer container) return container.Inventory;

            System.Reflection.PropertyInfo prop = be?.GetType().GetProperty("Inventory");
            return prop?.GetValue(be) as IInventory;
        }

        private static bool InventoryEmpty(IInventory inventory)
        {
            for (int i = 0; i < inventory.Count; i++)
            {
                if (!inventory[i].Empty) return false;
            }
            return true;
        }

        private static void Notify(IPlayer player, string message)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        private bool TryRotateBlock(IPlayer player, BlockSelection blockSel)
        {
            Block block = api.World.BlockAccessor.GetBlock(blockSel.Position);
            if (block == null || block.Id == 0 || block.Code == null) return false;

            Block newBlock = TryGetVariantRotatedBlock(block, blockSel.Face);

            if (newBlock == null && (blockSel.Face == BlockFacing.UP || blockSel.Face == BlockFacing.DOWN))
            {
                AssetLocation rotatedCode = block.GetRotatedBlockCode(90);
                if (rotatedCode != null && !rotatedCode.Equals(block.Code))
                {
                    newBlock = api.World.GetBlock(rotatedCode);
                }
            }

            if (newBlock == null)
            {
                IWrenchOrientable orientable = block.GetInterface<IWrenchOrientable>(api.World, blockSel.Position);
                if (orientable == null) return false;

                PlayRotateSound(blockSel.Position);
                orientable.Rotate(player.Entity, blockSel, -1);
                api.World.BlockAccessor.MarkBlockDirty(blockSel.Position);
                return true;
            }

            if (newBlock.Id == block.Id) return false;

            KineticNetworkManager networks = api.ModLoader.GetModSystem<KineticNetworkManager>();
            networks?.OnRemoved(blockSel.Position);
            ReplaceBlockForRotation(block, newBlock, blockSel.Position);
            api.World.BlockAccessor.MarkBlockDirty(blockSel.Position);
            networks?.OnPlaced(blockSel.Position);

            PlayRotateSound(blockSel.Position);
            return true;
        }

        private void ReplaceBlockForRotation(Block oldBlock, Block newBlock, BlockPos pos)
        {
            if (oldBlock.Code?.Domain != "vintagekinematics")
            {
                api.World.BlockAccessor.ExchangeBlock(newBlock.Id, pos);
                return;
            }

            BlockEntity oldBe = api.World.BlockAccessor.GetBlockEntity(pos);
            TreeAttribute tree = null;
            System.Type oldBeType = oldBe?.GetType();
            if (oldBe != null)
            {
                tree = new TreeAttribute();
                oldBe.ToTreeAttributes(tree);
                ClearKineticNetworkSnapshot(tree);
            }

            // ExchangeBlock keeps block entities alive, which leaves client-side kinetic
            // renderers/mesh splitters on the old shape. A real remove/set cycle rebuilds
            // those renderers while restoring saved BE data for same-class rotations.
            api.World.BlockAccessor.SetBlock(0, pos);
            api.World.BlockAccessor.SetBlock(newBlock.Id, pos);

            if (tree == null) return;
            BlockEntity newBe = api.World.BlockAccessor.GetBlockEntity(pos);
            if (newBe != null && (oldBeType == null || newBe.GetType() == oldBeType))
            {
                newBe.FromTreeAttributes(tree, api.World);
                newBe.MarkDirty(true);
            }
        }

        private static void ClearKineticNetworkSnapshot(ITreeAttribute tree)
        {
            // These values describe the old orientation/network and must be recomputed from
            // the newly placed block JSON plus the rebuilt network. Keeping them is what lets
            // an x-axis shaft visually rotate to z while still behaving as an x-axis node.
            tree.RemoveAttribute("axis");
            tree.RemoveAttribute("ratio");
            tree.RemoveAttribute("phaseOffset");
            tree.RemoveAttribute("networkId");
            tree.RemoveAttribute("currentRPM");
            tree.RemoveAttribute("netConflicted");
            tree.RemoveAttribute("netStressTotal");
            tree.RemoveAttribute("netStressCapacity");
            tree.RemoveAttribute("netOverstressed");
            tree.RemoveAttribute("netNodeCount");
        }

        private void PlayRotateSound(BlockPos pos)
        {
            api.World.PlaySoundAt(RotateSound, pos, 0, null, true, 16, 0.85f);
        }

        private Block TryGetVariantRotatedBlock(Block block, BlockFacing clickedFace)
        {
            Block axisBlock = TryGetAxisRotatedBlock(block, clickedFace);
            if (axisBlock != null) return axisBlock;

            string[] keys = { "side", "facing", "direction", "orientation" };
            foreach (string key in keys)
            {
                if (block.Variant == null || !block.Variant.TryGetValue(key, out string value)) continue;
                Vec3i direction = DirectionToVector(value);
                if (direction == null) continue;

                Vec3i rotated = RotateDirection(direction, clickedFace);
                if (rotated == null || Same(direction, rotated)) continue;

                Block rotatedBlock = TryGetBlockWithDirectionVariant(block, key, value, rotated);
                if (rotatedBlock != null) return rotatedBlock;
            }

            return null;
        }

        private Block TryGetAxisRotatedBlock(Block block, BlockFacing clickedFace)
        {
            if (block.Variant == null || !block.Variant.TryGetValue("axis", out string axis)) return null;

            string newAxis = axis;
            if (clickedFace == BlockFacing.UP || clickedFace == BlockFacing.DOWN)
            {
                if (axis == "x") newAxis = "z";
                else if (axis == "z") newAxis = "x";
            }
            else if (clickedFace == BlockFacing.EAST || clickedFace == BlockFacing.WEST)
            {
                if (axis == "y") newAxis = "z";
                else if (axis == "z") newAxis = "y";
            }
            else if (clickedFace == BlockFacing.NORTH || clickedFace == BlockFacing.SOUTH)
            {
                if (axis == "x") newAxis = "y";
                else if (axis == "y") newAxis = "x";
            }

            if (newAxis == axis) return null;
            return TryGetBlockWithVariant(block, "axis", newAxis);
        }

        private Block TryGetBlockWithDirectionVariant(Block block, string key, string currentValue, Vec3i direction)
        {
            string preferred = VectorToDirection(direction, UseLongDirectionNames(currentValue));
            Block result = TryGetBlockWithVariant(block, key, preferred);
            if (result != null) return result;

            string fallback = VectorToDirection(direction, !UseLongDirectionNames(currentValue));
            return TryGetBlockWithVariant(block, key, fallback);
        }

        private Block TryGetBlockWithVariant(Block block, string key, string value)
        {
            if (value == null) return null;
            AssetLocation code = block.CodeWithVariant(key, value);
            if (code == null || code.Equals(block.Code)) return null;

            Block result = api.World.GetBlock(code);
            return result != null && result.Id != 0 ? result : null;
        }

        private static bool UseLongDirectionNames(string value)
        {
            return value == "north" || value == "east" || value == "south" || value == "west" || value == "up" || value == "down";
        }

        private static Vec3i DirectionToVector(string value)
        {
            switch (value)
            {
                case "n":
                case "north": return new Vec3i(0, 0, -1);
                case "e":
                case "east": return new Vec3i(1, 0, 0);
                case "s":
                case "south": return new Vec3i(0, 0, 1);
                case "w":
                case "west": return new Vec3i(-1, 0, 0);
                case "up": return new Vec3i(0, 1, 0);
                case "down": return new Vec3i(0, -1, 0);
                default: return null;
            }
        }

        private static string VectorToDirection(Vec3i direction, bool longNames)
        {
            if (direction.X == 0 && direction.Y == 0 && direction.Z == -1) return longNames ? "north" : "n";
            if (direction.X == 1 && direction.Y == 0 && direction.Z == 0) return longNames ? "east" : "e";
            if (direction.X == 0 && direction.Y == 0 && direction.Z == 1) return longNames ? "south" : "s";
            if (direction.X == -1 && direction.Y == 0 && direction.Z == 0) return longNames ? "west" : "w";
            if (direction.X == 0 && direction.Y == 1 && direction.Z == 0) return "up";
            if (direction.X == 0 && direction.Y == -1 && direction.Z == 0) return "down";
            return null;
        }

        private static Vec3i RotateDirection(Vec3i direction, BlockFacing clickedFace)
        {
            if (clickedFace == BlockFacing.UP || clickedFace == BlockFacing.DOWN)
            {
                return new Vec3i(-direction.Z, direction.Y, direction.X);
            }

            if (clickedFace == BlockFacing.WEST)
            {
                return new Vec3i(direction.X, -direction.Z, direction.Y);
            }
            if (clickedFace == BlockFacing.EAST)
            {
                return new Vec3i(direction.X, direction.Z, -direction.Y);
            }
            if (clickedFace == BlockFacing.NORTH)
            {
                return new Vec3i(-direction.Y, direction.X, direction.Z);
            }
            if (clickedFace == BlockFacing.SOUTH)
            {
                return new Vec3i(direction.Y, -direction.X, direction.Z);
            }

            return null;
        }

        private static bool Same(Vec3i left, Vec3i right)
        {
            return left.X == right.X && left.Y == right.Y && left.Z == right.Z;
        }
    }
}
