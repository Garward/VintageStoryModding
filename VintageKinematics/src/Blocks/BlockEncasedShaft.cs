namespace VintageKinematics.Blocks
{
    public class BlockEncasedShaft : BlockAxisOriented
    {
        public override void OnLoaded(Vintagestory.API.Common.ICoreAPI api)
        {
            base.OnLoaded(api);
            PlacedPriorityInteract = true;
        }

        public override bool OnBlockInteractStart(Vintagestory.API.Common.IWorldAccessor world, Vintagestory.API.Common.IPlayer byPlayer, Vintagestory.API.Common.BlockSelection blockSel)
        {
            if (KineticCasingHelper.TryRetextureCasing(world, byPlayer, blockSel)) return true;
            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
