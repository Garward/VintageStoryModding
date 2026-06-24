using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockBehaviorRequireEmptyInventoryOnBreak : BlockBehavior
    {
        private string messageLangCode = "vintagekinematics:storage-must-be-empty";

        public BlockBehaviorRequireEmptyInventoryOnBreak(Block block) : base(block)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            messageLangCode = properties["messageLangCode"].AsString(messageLangCode);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier, ref EnumHandling handling)
        {
            BlockEntity blockEntity = world?.BlockAccessor.GetBlockEntity(pos);
            IInventory inventory = (blockEntity as IBlockEntityContainer)?.Inventory
                ?? (blockEntity as BEBoreBase)?.Inventory;
            if (inventory == null || IsEmpty(inventory)) return;

            if (world?.Side == EnumAppSide.Server && byPlayer is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get(messageLangCode), EnumChatType.Notification);
            }

            handling = EnumHandling.PreventSubsequent;
        }

        private static bool IsEmpty(IInventory inventory)
        {
            foreach (ItemSlot slot in inventory)
            {
                if (slot?.Empty == false) return false;
            }

            return true;
        }
    }
}
