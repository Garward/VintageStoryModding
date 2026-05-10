using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace VintageKinematics.BlockEntities
{
    public sealed class BeltItem
    {
        public ItemStack Stack;
        public float Progress;

        public ITreeAttribute ToTree()
        {
            var t = new TreeAttribute();
            t.SetItemstack("stack", Stack);
            t.SetFloat("progress", Progress);
            return t;
        }

        public static BeltItem FromTree(ITreeAttribute tree, IWorldAccessor world)
        {
            ItemStack stack = tree.GetItemstack("stack");
            if (stack == null) return null;
            stack.ResolveBlockOrItem(world);
            if (stack.Collectible == null) return null;
            return new BeltItem { Stack = stack, Progress = tree.GetFloat("progress") };
        }
    }
}
