using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;

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
            if (CanRemove(world, pos, StorageRemovalKind.PlayerBreak, byPlayer, out StorageRemovalCheck check)) return;

            SendDeniedMessage(world, byPlayer, check);
            handling = EnumHandling.PreventSubsequent;
        }

        public override void OnBlockExploded(IWorldAccessor world, BlockPos pos, BlockPos explosionCenter, EnumBlastType blastType, ref EnumHandling handling)
        {
            if (CanRemove(world, pos, StorageRemovalKind.Explosion, null, out _)) return;

            // For explosions, PreventDefault is required. PreventSubsequent only stops later
            // behaviors and would still let the base explosion code remove the block.
            handling = EnumHandling.PreventDefault;
        }

        private bool CanRemove(IWorldAccessor world, BlockPos pos, StorageRemovalKind kind, IPlayer byPlayer, out StorageRemovalCheck check)
        {
            check = default;
            IVKStorageRemovalGuard guard = ResolveGuard(world, pos);
            if (guard == null) return true;

            check = guard.CanRemoveStorageBlock(pos, kind, byPlayer);
            return check.Allowed;
        }

        private IVKStorageRemovalGuard ResolveGuard(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null) return null;

            BlockEntity local = MultiblockHelper.GetMultiblockAwareBE(world, pos)
                ?? world.BlockAccessor.GetBlockEntity(pos);
            if (local is IVKStorageRemovalGuard directGuard) return directGuard;

            if (local is IVKStorageStructureMember member && member.ControllerPos != null)
            {
                BlockEntity controller = world.BlockAccessor.GetBlockEntity(member.ControllerPos);
                if (controller is IVKStorageRemovalGuard controllerGuard) return controllerGuard;
            }

            return null;
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
    }
}
