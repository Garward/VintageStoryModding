using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Shared safety hook for future indexed storage blocks. It asks the controller whether
    /// removing this structure part would leave enough capacity for the stored contents.
    /// </summary>
    public class BlockBehaviorRequireStorageCapacityOnRemove : BlockBehavior
    {
        private string fallbackMessageLangCode = "vintagekinematics:storage-removal-would-overflow";

        public BlockBehaviorRequireStorageCapacityOnRemove(Block block) : base(block)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            fallbackMessageLangCode = properties["messageLangCode"].AsString(fallbackMessageLangCode);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier, ref EnumHandling handling)
        {
            // The client has only synchronized summary fields and can never prove the live
            // index or a post-removal topology. Let it submit the break intent; the server
            // executes this same behavior with the authoritative controller and may deny it.
            if (world?.Side == EnumAppSide.Client) return;

            StorageRemovalCheck check = KineticStorageRemovalService.Check(
                world,
                pos,
                StorageRemovalKind.PlayerBreak,
                byPlayer);
            if (check.Allowed) return;

            SendDeniedMessage(world, byPlayer, check);
            ResynchronizeDeniedBlock(world, pos);
            handling = EnumHandling.PreventSubsequent;
        }

        public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, ref EnumHandling handling)
        {
            StorageRemovalCheck check = KineticStorageRemovalService.Check(
                world,
                pos,
                StorageRemovalKind.Explosion);
            if (check.Allowed) return;

            // For explosions, PreventDefault is required. PreventSubsequent only stops later
            // behaviors and would still let the base explosion code remove the block.
            handling = EnumHandling.PreventDefault;
        }

        private void SendDeniedMessage(IWorldAccessor world, IPlayer byPlayer, StorageRemovalCheck check)
        {
            if (world?.Side != EnumAppSide.Server || byPlayer is not IServerPlayer serverPlayer) return;

            string langCode = string.IsNullOrEmpty(check.MessageLangCode)
                ? fallbackMessageLangCode
                : check.MessageLangCode;
            serverPlayer.SendMessage(
                GlobalConstants.InfoLogChatGroup,
                Lang.Get(langCode, check.StoredItems, check.CapacityAfterRemoval, check.CurrentCapacity),
                EnumChatType.Notification);
        }

        private static void ResynchronizeDeniedBlock(IWorldAccessor world, BlockPos pos)
        {
            if (world?.Side != EnumAppSide.Server || pos == null) return;
            world.BlockAccessor.MarkBlockDirty(pos);
            world.BlockAccessor.GetBlockEntity(pos)?.MarkDirty(true);
        }
    }
}
