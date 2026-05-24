using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Blocks;
using VintageKinematics.Items;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEBackpackFlywheelPlaced : BEKineticAnimated, IKineticConnector
    {
        private const int TickMs = 250;
        private const float ChargeStress = 64f;
        private const float ChargeEfficiency = 0.75f;
        private const float MaxOutputRPM = 16f;
        private ItemStack storedStack;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            storedStack?.ResolveBlockOrItem(api.World);
            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, TickMs);
            }
        }

        public void SetStoredStack(ItemStack stack)
        {
            storedStack = stack?.Clone();
            if (storedStack != null) storedStack.StackSize = 1;
            MarkDirty(true);
        }

        public ItemStack GetStoredStack()
        {
            if (storedStack != null)
            {
                ItemStack clone = storedStack.Clone();
                clone.ResolveBlockOrItem(Api.World);
                clone.StackSize = 1;
                return clone;
            }

            Item item = Api?.World?.GetItem(new AssetLocation("vintagekinematics", "backpackflywheel"));
            return item == null ? null : new ItemStack(item);
        }

        private void OnServerTick(float dt)
        {
            storedStack ??= CreateFallbackStack();
            if (storedStack == null) return;

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = MathF.Abs(kinetic?.ActualRPM ?? 0f);
            if (rpm < KineticNetwork.MinAbsRPM) return;

            float secondsGained = ChargeStress * rpm / (ChargeStress * MaxOutputRPM) * ChargeEfficiency * MathF.Max(0f, dt);
            if (!ItemBackpackFlywheel.AddCharge(storedStack, secondsGained)) return;

            MarkDirty(true);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }

        private ItemStack CreateFallbackStack()
        {
            Item item = Api?.World?.GetItem(new AssetLocation("vintagekinematics", "backpackflywheel"));
            if (item == null) return null;
            return new ItemStack(item);
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            return BlockBackpackFlywheelPlaced.TryConnectInline(self, other, fromPos, toPos, Block?.Variant?["side"]);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            float stored = ItemBackpackFlywheel.GetStoredSeconds(storedStack);
            float max = ItemBackpackFlywheel.MaxStoredSecondsValue;
            float percent = max > 0f ? stored / max * 100f : 0f;
            dsc.AppendLine(Lang.Get("vintagekinematics:backpackflywheel-charge", stored, max, percent));
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            if (storedStack != null) tree.SetItemstack("storedStack", storedStack);
            else tree.RemoveAttribute("storedStack");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            storedStack = tree.GetItemstack("storedStack");
            storedStack?.ResolveBlockOrItem(worldAccessForResolve);
        }
    }
}
