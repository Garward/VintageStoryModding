using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;

namespace VintageKinematics.Blocks
{
    public class BlockBehaviorRequireBoreRetractedOnBreak : BlockBehavior
    {
        private string messageLangCode = "vintagekinematics:bore-must-be-retracted";

        public BlockBehaviorRequireBoreRetractedOnBreak(Block block) : base(block)
        {
        }

        public override void Initialize(JsonObject properties)
        {
            base.Initialize(properties);
            messageLangCode = properties["messageLangCode"].AsString(messageLangCode);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier, ref EnumHandling handling)
        {
            if (world == null || pos == null) return;
            if (MultiblockHelper.GetMultiblockAwareBE(world, pos) is not BEBoreBase bore) return;
            if (!bore.HasUnretractedColumn) return;

            if (world.Side == EnumAppSide.Server && byPlayer is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, Lang.Get(messageLangCode), EnumChatType.Notification);
            }

            handling = EnumHandling.PreventSubsequent;
        }
    }
}
