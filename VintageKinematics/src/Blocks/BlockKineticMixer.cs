using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Blocks
{
    /// <summary>
    /// Mixer: quern-style vertical cog drives a vertical paddle in a tub. The retained
    /// side variants are used to orient automation faces.
    /// </summary>
    public class BlockKineticMixer : BlockKineticOpenableMachine, ILiquidSink
    {
        public bool AllowHeldLiquidTransfer => false;
        public float CapacityLitres => BEKineticMixer.LiquidCapacityLitres;
        public float TransferSizeLitres => 1f;

        public float GetCurrentLitres(ItemStack containerStack)
        {
            return 0f;
        }

        public float GetCurrentLitres(BlockPos pos)
        {
            return MixerAt(pos)?.GetLiquidLitres() ?? 0f;
        }

        public bool IsFull(ItemStack containerStack)
        {
            return true;
        }

        public bool IsFull(BlockPos pos)
        {
            return MixerAt(pos)?.IsLiquidFull() ?? true;
        }

        public WaterTightContainableProps GetContentProps(ItemStack containerStack)
        {
            return null;
        }

        public WaterTightContainableProps GetContentProps(BlockPos pos)
        {
            return MixerAt(pos)?.GetLiquidContentProps();
        }

        public ItemStack GetContent(ItemStack containerStack)
        {
            return null;
        }

        public ItemStack GetContent(BlockPos pos)
        {
            return MixerAt(pos)?.GetLiquidContent()?.Clone();
        }

        public void SetContent(ItemStack containerStack, ItemStack content)
        {
        }

        public void SetContent(BlockPos pos, ItemStack content)
        {
            MixerAt(pos)?.SetLiquidContent(content);
        }

        public int TryPutLiquid(BlockPos pos, ItemStack liquidStack, float desiredLitres)
        {
            return MixerAt(pos)?.TryPutLiquid(liquidStack, desiredLitres) ?? 0;
        }

        public int TryPutLiquid(ItemStack containerStack, ItemStack liquidStack, float desiredLitres)
        {
            return 0;
        }

        private BEKineticMixer MixerAt(BlockPos pos)
        {
            return MultiblockHelper.GetMultiblockAwareBE(api?.World, pos) as BEKineticMixer;
        }
    }
}
