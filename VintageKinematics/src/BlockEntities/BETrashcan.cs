using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Filtered automation sink. It is deliberately not an inventory; only belts and funnels call
    /// its accept methods, so adjacent machine outputs cannot delete items directly.
    /// </summary>
    public class BETrashcan : BlockEntity, IAutomationItemSink
    {
        private const int FilterSlotCount = 6;

        public const int PacketIdOpenDialog = 5400;
        public const int PacketIdToggleMode = 5401;
        public const int PacketIdSetFilter = 5402;
        public const int PacketIdToggleFuzzy = 5403;

        private readonly FilterDialogController filter;

        public bool Whitelist => filter.Whitelist;
        public bool Fuzzy => filter.Fuzzy;

        public BETrashcan()
        {
            filter = new FilterDialogController(
                this,
                new InventoryFunnelFilter(FilterSlotCount, "trashcanfilter", null),
                "trashcanfilter",
                "vintagekinematics:trashcan-filter-title",
                "Trashcan Filter",
                "trashcan",
                PacketIdOpenDialog,
                PacketIdToggleMode,
                PacketIdSetFilter,
                PacketIdToggleFuzzy,
                () => FilterSlotCount);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            filter.Initialize(api);
        }

        public bool TryAcceptFromBelt(ItemStack stack) => TryDelete(stack);

        public bool TryAcceptFromFunnel(ItemStack stack) => TryDelete(stack);

        public bool MatchesFilter(ItemStack stack) => filter.Matches(stack);

        private bool TryDelete(ItemStack stack)
        {
            if (Api?.Side != EnumAppSide.Server) return false;
            if (stack == null || stack.StackSize <= 0) return true;
            if (!MatchesFilter(stack)) return false;

            stack.StackSize = 0;
            MarkDirty(false);
            return true;
        }

        public bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            return filter.Open(byPlayer);
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (filter.OnReceivedClientPacket(player, packetid, data)) return;
            base.OnReceivedClientPacket(player, packetid, data);
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (filter.OnReceivedServerPacket(packetid, data)) return;
            base.OnReceivedServerPacket(packetid, data);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            filter.WriteToTree(tree);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            filter.ReadFromTree(tree);
        }

        public override void OnBlockUnloaded()
        {
            base.OnBlockUnloaded();
            filter.DisposeDialog();
        }

        public override void OnBlockRemoved()
        {
            base.OnBlockRemoved();
            filter.DisposeDialog();
        }
    }
}
