using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage
{
    /// <summary>
    /// Shared preflight gate for every VK-owned path that can remove a storage structure block.
    /// Direct block-accessor changes bypass OnBlockBroken, so tools and contraptions must call
    /// this service explicitly before changing the world.
    /// </summary>
    public static class KineticStorageRemovalService
    {
        public const string StorageMemberAttribute = "vkStorageMember";

        public static StorageRemovalCheck Check(
            IWorldAccessor world,
            BlockPos pos,
            StorageRemovalKind kind,
            IPlayer byPlayer = null)
        {
            if (world == null || pos == null)
            {
                return StorageRemovalCheck.Deny(
                    pos,
                    0,
                    0,
                    0,
                    "vintagekinematics:storage-structure-unknown");
            }

            IVKStorageRemovalGuard guard = ResolveGuard(world, pos);
            if (guard != null)
            {
                return guard.CanRemoveStorageBlock(pos, kind, byPlayer);
            }

            Block block = world.BlockAccessor.GetBlock(pos);
            bool taggedMember = block?.Attributes?[StorageMemberAttribute].AsBool(false) == true;
            if (taggedMember)
            {
                // A missing member/controller BE is a recovery condition, not permission to
                // remove the only structural evidence that remains in the world.
                return StorageRemovalCheck.Deny(
                    pos,
                    0,
                    0,
                    0,
                    "vintagekinematics:storage-structure-unknown");
            }

            return StorageRemovalCheck.Allow(pos, 0, 0, 0);
        }

        private static IVKStorageRemovalGuard ResolveGuard(IWorldAccessor world, BlockPos pos)
        {
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
    }
}
