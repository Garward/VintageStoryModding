using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageKinematics.Items;

namespace VintageKinematics.Blocks
{
    public class BlockShaft : BlockAxisOriented
    {
        private const string BackpackFlywheelChargeStepAttribute = "vkBackpackFlywheelBlockChargeStep";

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (CanChargeWornBackpackFlywheel(world, byPlayer, blockSel))
            {
                byPlayer.Entity.Attributes.SetFloat(BackpackFlywheelChargeStepAttribute, 0f);
                return true;
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }

        public override bool OnBlockInteractStep(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (!CanChargeWornBackpackFlywheel(world, byPlayer, blockSel)) return false;

            float last = byPlayer.Entity.Attributes.GetFloat(BackpackFlywheelChargeStepAttribute, 0f);
            float dt = MathF.Max(0f, secondsUsed - last);
            byPlayer.Entity.Attributes.SetFloat(BackpackFlywheelChargeStepAttribute, secondsUsed);
            return ItemBackpackFlywheel.TryChargeEquippedFromKinetic(byPlayer, world, blockSel, dt);
        }

        public override bool OnBlockInteractCancel(float secondsUsed, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, EnumItemUseCancelReason cancelReason)
        {
            return true;
        }

        private static bool CanChargeWornBackpackFlywheel(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            if (byPlayer?.Entity?.Controls?.Sneak != true || blockSel == null) return false;
            if (byPlayer.InventoryManager?.ActiveHotbarSlot?.Empty != true) return false;
            return ItemBackpackFlywheel.CanChargeEquippedFromKinetic(byPlayer, world, blockSel);
        }
    }
}
