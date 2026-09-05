using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.BlockEntities.Storage;
using VintageKinematics.Connections;
using VintageKinematics.Storage.Rendering;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.Blocks
{
    /// <summary>Notifies storage block entities when their visual neighbor mask changes.</summary>
    public sealed class BlockKineticWarehouseMember : Block, IPlacementPreviewProvider
    {
        private const string PortableWarehouseId = "vkPortableWarehouseId";
        private const string PortableControllerX = "vkPortableControllerX";
        private const string PortableControllerY = "vkPortableControllerY";
        private const string PortableControllerZ = "vkPortableControllerZ";
        private const string PortableControllerDimension = "vkPortableControllerDimension";
        private const string PortableHasMembers = "vkPortableHasMembers";

        public bool TryResolvePlacementPreview(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel,
            out BlockPos targetPos,
            out Block variant)
        {
            targetPos = null;
            variant = null;
            if (world == null || blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            if (Variant == null || !Variant.ContainsKey("side"))
            {
                variant = this;
                return true;
            }

            BlockFacing playerFacing = byPlayer?.Entity == null
                ? null
                : BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            string side = SideFacingPlayer(playerFacing);
            variant = world.GetBlock(CodeWithVariant("side", side)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(
            IWorldAccessor world,
            IPlayer byPlayer,
            ItemStack itemStack,
            BlockSelection blockSel,
            ref string failureCode)
        {
            if (!TryResolvePlacementPreview(
                world,
                byPlayer,
                blockSel,
                out BlockPos targetPos,
                out Block variant))
            {
                return base.TryPlaceBlock(
                    world,
                    byPlayer,
                    itemStack,
                    blockSel,
                    ref failureCode);
            }

            if (TryReadPortableControllerIdentity(
                    itemStack,
                    out string portableWarehouseId,
                    out BlockPos portableController,
                    out bool portableHasMembers))
            {
                StoragePlacementDecision placement = WorldStoragePlacementPolicy.Evaluate(
                    world,
                    targetPos,
                    StoragePlacementRole.Controller);
                if (portableHasMembers
                    && HasOrphanedWarehouseMembers(
                        world,
                        portableWarehouseId,
                        portableController)
                    && string.IsNullOrWhiteSpace(placement.WarehouseId))
                {
                    failureCode = "storage-placement-controllerrequireswarehouse";
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(placement.WarehouseId)
                    && placement.WarehouseId != portableWarehouseId)
                {
                    failureCode = "storage-placement-controllertouchesstorage";
                    return false;
                }
            }

            return PlacementPreview.TryPlaceResolvedVariant(
                variant,
                world,
                byPlayer,
                itemStack,
                blockSel,
                targetPos,
                ref failureCode);
        }

        public override bool OnBlockInteractStart(
            IWorldAccessor world,
            IPlayer byPlayer,
            BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position)
                is BEKineticWarehousePort port)
            {
                return port.OnPlayerRightClick(byPlayer);
            }
            if (world.BlockAccessor.GetBlockEntity(blockSel.Position)
                is not BEKineticWarehouseTerminal controller)
            {
                return base.OnBlockInteractStart(world, byPlayer, blockSel);
            }

            return controller.OnPlayerRightClick(byPlayer);
        }

        internal static string SideFacingPlayer(BlockFacing facing)
        {
            if (facing == BlockFacing.NORTH) return "s";
            if (facing == BlockFacing.EAST) return "e";
            if (facing == BlockFacing.SOUTH) return "n";
            if (facing == BlockFacing.WEST) return "w";
            return "s";
        }

        public override void OnBlockPlaced(
            IWorldAccessor world,
            BlockPos blockPos,
            ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);
            RefreshAtAndNeighbors(world, blockPos);
        }

        public override ItemStack[] GetDrops(
            IWorldAccessor world,
            BlockPos pos,
            IPlayer byPlayer,
            float dropQuantityMultiplier = 1f)
        {
            ItemStack[] drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
            AddPortableControllerIdentity(world, pos, drops);
            return drops;
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack picked = base.OnPickBlock(world, pos);
            AddPortableControllerIdentity(world, pos, picked == null ? null : new[] { picked });
            return picked;
        }

        internal static bool TryReadPortableControllerIdentity(
            ItemStack stack,
            out string warehouseId,
            out BlockPos controllerPos,
            out bool hasMembers)
        {
            warehouseId = stack?.Attributes?.GetString(PortableWarehouseId, null);
            controllerPos = null;
            hasMembers = stack?.Attributes?.GetBool(PortableHasMembers, false) == true;
            if (!Guid.TryParse(warehouseId, out Guid parsed)
                || parsed == Guid.Empty
                || stack?.Attributes?.HasAttribute(PortableControllerX) != true)
            {
                warehouseId = null;
                return false;
            }

            warehouseId = parsed.ToString("D");
            controllerPos = new BlockPos(
                stack.Attributes.GetInt(PortableControllerX),
                stack.Attributes.GetInt(PortableControllerY),
                stack.Attributes.GetInt(PortableControllerZ),
                stack.Attributes.GetInt(PortableControllerDimension));
            return true;
        }

        private static void AddPortableControllerIdentity(
            IWorldAccessor world,
            BlockPos pos,
            ItemStack[] drops)
        {
            if (drops == null
                || world?.BlockAccessor.GetBlockEntity(pos)
                    is not BEKineticWarehouseTerminal terminal
                || string.IsNullOrWhiteSpace(terminal.WarehouseId))
            {
                return;
            }

            bool hasMembers = HasAttachedWarehouseMember(world, pos, terminal);
            if ((terminal.ItemIndex?.StoredItems ?? 0) == 0 && !hasMembers) return;

            foreach (ItemStack drop in drops)
            {
                if (drop == null) continue;
                drop.Attributes ??= new TreeAttribute();
                drop.Attributes.SetString(PortableWarehouseId, terminal.WarehouseId);
                drop.Attributes.SetInt(PortableControllerX, pos.X);
                drop.Attributes.SetInt(PortableControllerY, pos.InternalY);
                drop.Attributes.SetInt(PortableControllerZ, pos.Z);
                drop.Attributes.SetInt(PortableControllerDimension, pos.dimension);
                drop.Attributes.SetBool(
                    PortableHasMembers,
                    hasMembers);
            }
        }

        private static bool HasAttachedWarehouseMember(
            IWorldAccessor world,
            BlockPos controllerPos,
            BEKineticWarehouseTerminal terminal)
        {
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (world.BlockAccessor.GetBlockEntity(controllerPos.AddCopy(face))
                        is IVKStorageStructureMember member
                    && !member.IsController
                    && member.WarehouseId == terminal.WarehouseId
                    && member.ControllerPos != null
                    && member.ControllerPos.Equals(controllerPos))
                {
                    return true;
                }
            }
            return false;
        }

        internal static bool HasOrphanedWarehouseMembers(
            IWorldAccessor world,
            string warehouseId,
            BlockPos oldControllerPos)
        {
            if (world == null
                || string.IsNullOrWhiteSpace(warehouseId)
                || oldControllerPos == null)
            {
                return false;
            }

            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                if (world.BlockAccessor.GetBlockEntity(oldControllerPos.AddCopy(face))
                        is IVKStorageStructureMember member
                    && !member.IsController
                    && member.WarehouseId == warehouseId
                    && member.ControllerPos != null
                    && member.ControllerPos.Equals(oldControllerPos))
                {
                    return true;
                }
            }
            return false;
        }

        public override void OnNeighbourBlockChange(
            IWorldAccessor world,
            BlockPos pos,
            BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);
            RefreshAt(world, pos);
        }

        public override void OnBlockRemoved(IWorldAccessor world, BlockPos pos)
        {
            base.OnBlockRemoved(world, pos);
            RefreshNeighbors(world, pos);
            RefreshEdgeDiagonals(world, pos);
        }

        private static void RefreshAtAndNeighbors(IWorldAccessor world, BlockPos pos)
        {
            RefreshAt(world, pos);
            RefreshNeighbors(world, pos);
            RefreshEdgeDiagonals(world, pos);
        }

        private static void RefreshNeighbors(IWorldAccessor world, BlockPos pos)
        {
            foreach (BlockFacing face in FaceConnectionMask.Faces)
            {
                RefreshAt(world, pos.AddCopy(face));
            }
        }

        private static void RefreshEdgeDiagonals(IWorldAccessor world, BlockPos pos)
        {
            foreach (StorageConcaveElbow elbow in StorageConcaveElbow.All)
            {
                RefreshAt(world, pos.AddCopy(elbow.First).Add(elbow.Second));
            }
        }

        private static void RefreshAt(IWorldAccessor world, BlockPos pos)
        {
            if (world?.Side != EnumAppSide.Client || pos == null) return;
            if (world.BlockAccessor.GetBlockEntity(pos)
                is BEKineticStorageMember member)
            {
                member.RefreshVisualConnections();
            }
        }
    }
}
