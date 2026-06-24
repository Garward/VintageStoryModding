using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    internal static class BoreBreakGuard
    {
        public static bool PreventIfUnretracted(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, string messageLangCode)
        {
            if (MultiblockHelper.GetMultiblockAwareBE(world, pos) is not BEBoreBase bore) return false;
            if (!bore.HasUnretractedColumn) return false;

            Notify(world, byPlayer, messageLangCode);
            return true;
        }

        public static bool PreventIfInventoryNotEmpty(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, string messageLangCode)
        {
            if (MultiblockHelper.GetMultiblockAwareBE(world, pos) is not BEBoreBase bore) return false;
            foreach (ItemSlot slot in bore.Inventory)
            {
                if (slot?.Empty == false)
                {
                    Notify(world, byPlayer, messageLangCode);
                    return true;
                }
            }

            return false;
        }

        private static void Notify(IWorldAccessor world, IPlayer byPlayer, string messageLangCode)
        {
            if (world?.Side == EnumAppSide.Server && byPlayer is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get(messageLangCode), EnumChatType.Notification);
            }
        }
    }
}
