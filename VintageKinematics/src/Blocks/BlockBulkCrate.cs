using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    public class BlockBulkCrate : BlockVKStorage
    {
        private WorldInteraction[] bulkCrateInteractions;

        public override void OnLoaded(ICoreAPI api)
        {
            base.OnLoaded(api);
            PlacedPriorityInteract = true;
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (blockSel == null) return false;
            if (MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position) is BEBulkCrate bulkCrate)
            {
                return bulkCrate.OnPlayerRightClick(byPlayer, blockSel);
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override WorldInteraction[] GetPlacedBlockInteractionHelp(IWorldAccessor world, BlockSelection selection, IPlayer forPlayer)
        {
            WorldInteraction[] baseInteractions = base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
            bulkCrateInteractions ??= new[]
            {
                new WorldInteraction { ActionLangCode = "blockhelp-crate-add", MouseButton = EnumMouseButton.Right, HotKeyCode = "shift" },
                new WorldInteraction { ActionLangCode = "blockhelp-crate-addall", MouseButton = EnumMouseButton.Right, HotKeyCodes = new[] { "shift", "ctrl" } },
                new WorldInteraction { ActionLangCode = "blockhelp-crate-remove", MouseButton = EnumMouseButton.Right },
                new WorldInteraction { ActionLangCode = "blockhelp-crate-removeall", MouseButton = EnumMouseButton.Right, HotKeyCode = "ctrl" }
            };

            return baseInteractions == null
                ? bulkCrateInteractions
                : baseInteractions.Concat(bulkCrateInteractions).ToArray();
        }
    }
}
