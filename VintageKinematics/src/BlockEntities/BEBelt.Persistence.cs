using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (ControllerPos != null)
            {
                tree.SetInt("ctlX", ControllerPos.X);
                tree.SetInt("ctlY", ControllerPos.Y);
                tree.SetInt("ctlZ", ControllerPos.Z);
                tree.SetInt("ctlDim", ControllerPos.dimension);
            }
            tree.SetInt("idx", IndexInChain);
            tree.SetInt("len", ChainLength);
            tree.SetInt("part", (int)Part);
            tree.SetBool("hasShaft", HasShaft);
            tree.SetString("shaftAxis", InsertedShaftAxis ?? "");

            tree.SetInt("itemCount", items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                tree["it" + i] = items[i].ToTree();
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            if (tree.HasAttribute("ctlX"))
            {
                ControllerPos = new BlockPos(
                    tree.GetInt("ctlX"),
                    tree.GetInt("ctlY"),
                    tree.GetInt("ctlZ"),
                    tree.GetInt("ctlDim", Pos.dimension));
            }
            IndexInChain = tree.GetInt("idx", 0);
            ChainLength = tree.GetInt("len", 1);
            Part = (EnumBeltPart)tree.GetInt("part", 0);
            HasShaft = tree.GetBool("hasShaft", false);
            InsertedShaftAxis = tree.GetString("shaftAxis", "");
            if (string.IsNullOrEmpty(InsertedShaftAxis)) InsertedShaftAxis = null;
            Direction = Block?.Variant?["direction"];
            UpdateKineticState(triggerRebuild: false);

            items.Clear();
            int itemCount = tree.GetInt("itemCount", 0);
            for (int i = 0; i < itemCount; i++)
            {
                if (tree["it" + i] is ITreeAttribute sub)
                {
                    BeltItem bi = BeltItem.FromTree(sub, worldForResolving);
                    if (bi != null) items.Add(bi);
                }
            }
        }
    }
}
