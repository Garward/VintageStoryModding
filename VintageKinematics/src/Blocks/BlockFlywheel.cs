using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockFlywheel : Block, IPlacementPreviewProvider, IKineticActivatable
    {
        private const string StoredSecondsAttribute = "vkFlywheelStoredSeconds";
        private const string StoredUpdatedMsAttribute = "vkFlywheelStoredUpdatedMs";

        private WorldInteraction[] interactions;

        private static string OutputSideFacingPlayer(IPlayer byPlayer)
        {
            if (byPlayer?.Entity == null) return "e";
            BlockFacing facing = BlockFacing.HorizontalFromYaw(byPlayer.Entity.Pos.Yaw);
            if (facing == BlockFacing.NORTH) return "n";
            if (facing == BlockFacing.EAST)  return "e";
            if (facing == BlockFacing.SOUTH) return "s";
            if (facing == BlockFacing.WEST)  return "w";
            return "e";
        }

        public bool TryResolvePlacementPreview(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, out BlockPos targetPos, out Block variant)
        {
            targetPos = null;
            variant = null;
            if (blockSel?.Face == null) return false;

            targetPos = PlacementPreview.DefaultTargetPos(world, blockSel, this);
            string desired = OutputSideFacingPlayer(byPlayer);
            variant = world.GetBlock(CodeWithVariant("side", desired)) ?? this;
            return true;
        }

        public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemStack, BlockSelection blockSel, ref string failureCode)
        {
            if (!TryResolvePlacementPreview(world, byPlayer, blockSel, out _, out Block variant) || variant == this)
            {
                return base.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
            }
            return variant.TryPlaceBlock(world, byPlayer, itemStack, blockSel, ref failureCode);
        }

        public override void OnBlockPlaced(IWorldAccessor world, BlockPos blockPos, ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(world, blockPos, byItemStack);

            float storedSeconds = GetDecayedStoredSeconds(byItemStack, world);
            if (storedSeconds <= 0f) return;

            BEFlywheel be = MultiblockHelper.GetMultiblockAwareBE(world, blockPos) as BEFlywheel;
            be?.SetStoredSecondsFromItem(storedSeconds);
        }

        public override ItemStack[] GetDrops(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1f)
        {
            ItemStack[] drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);
            if (drops == null || drops.Length == 0) return drops;

            BEFlywheel be = MultiblockHelper.GetMultiblockAwareBE(world, pos) as BEFlywheel;
            if (be == null || be.StoredSeconds <= 0f) return drops;

            foreach (ItemStack drop in drops)
            {
                if (drop?.Block is BlockFlywheel)
                {
                    SetPortableCharge(drop, be.StoredSeconds, world);
                    break;
                }
            }

            return drops;
        }

        public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
        {
            ItemStack stack = base.OnPickBlock(world, pos);
            BEFlywheel be = MultiblockHelper.GetMultiblockAwareBE(world, pos) as BEFlywheel;
            if (be != null && be.StoredSeconds > 0f)
            {
                SetPortableCharge(stack, be.StoredSeconds, world);
            }
            return stack;
        }

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            if (api.Side != EnumAppSide.Client) return;
            interactions = new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-flywheel-toggle",
                    MouseButton = EnumMouseButton.Right
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:blockhelp-flywheel-burst",
                    MouseButton = EnumMouseButton.Right,
                    HotKeyCode = "sneak"
                }
            };
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            return interactions ?? base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);

            float storedSeconds = GetDecayedStoredSeconds(inSlot?.Itemstack, world);
            if (storedSeconds <= 0f) return;

            float maxStoredSeconds = MaxStoredSeconds();
            float percent = maxStoredSeconds > 0f ? storedSeconds / maxStoredSeconds * 100f : 0f;
            dsc.AppendLine(Lang.Get("vintagekinematics:flywheel-portable-charge", storedSeconds, maxStoredSeconds, percent));
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer?.Entity == null || blockSel == null) return false;
            if (KineticInteractionHelper.ShouldDeferToHeldWrench(byPlayer)) return false;

            BEFlywheel be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) as BEFlywheel;
            if (be == null) return false;

            bool configure = byPlayer.Entity.Controls?.Sneak == true;
            if (world.Side == EnumAppSide.Server) be.OnPlayerRightClick(byPlayer, configure);
            return true;
        }

        public bool OnKineticActivate(IWorldAccessor world, BlockPos targetPos, BlockFacing activatedFace, BlockPos activatorPos, float signedRPM)
        {
            if (world.Side != EnumAppSide.Server) return true;

            BEFlywheel be = MultiblockHelper.GetMultiblockAwareBE(world, targetPos) as BEFlywheel;
            if (be == null) return false;

            be.ToggleBankRelease();
            return true;
        }

        private void SetPortableCharge(ItemStack stack, float storedSeconds, IWorldAccessor world)
        {
            if (stack?.Attributes == null) return;

            storedSeconds = GameMath.Clamp(storedSeconds, 0f, MaxStoredSeconds());
            if (storedSeconds <= 0f)
            {
                stack.Attributes.RemoveAttribute(StoredSecondsAttribute);
                stack.Attributes.RemoveAttribute(StoredUpdatedMsAttribute);
                return;
            }

            stack.Attributes.SetFloat(StoredSecondsAttribute, storedSeconds);
            stack.Attributes.SetLong(StoredUpdatedMsAttribute, world?.ElapsedMilliseconds ?? 0L);
        }

        private float GetDecayedStoredSeconds(ItemStack stack, IWorldAccessor world)
        {
            if (stack?.Attributes == null) return 0f;

            float storedSeconds = stack.Attributes.GetFloat(StoredSecondsAttribute, 0f);
            if (storedSeconds <= 0f) return 0f;

            long updatedMs = stack.Attributes.GetLong(StoredUpdatedMsAttribute, world?.ElapsedMilliseconds ?? 0L);
            long nowMs = world?.ElapsedMilliseconds ?? updatedMs;
            if (updatedMs <= 0L || nowMs <= updatedMs) return GameMath.Clamp(storedSeconds, 0f, MaxStoredSeconds());

            float elapsedSeconds = (nowMs - updatedMs) / 1000f;
            float decayed = storedSeconds - LeakSecondsPerSecond() * elapsedSeconds;
            return GameMath.Clamp(decayed, 0f, MaxStoredSeconds());
        }

        private float MaxStoredSeconds()
        {
            JsonObject stats = Attributes?["flywheel"];
            return MathF.Max(1f, stats?["maxStoredSeconds"].AsFloat(180f) ?? 180f);
        }

        private float LeakSecondsPerSecond()
        {
            JsonObject stats = Attributes?["flywheel"];
            float maxStoredSeconds = MaxStoredSeconds();
            float leakFullToEmptySeconds = stats?["leakFullToEmptySeconds"].AsFloat(1800f) ?? 1800f;
            return leakFullToEmptySeconds > 0f ? maxStoredSeconds / leakFullToEmptySeconds : 0f;
        }
    }
}
