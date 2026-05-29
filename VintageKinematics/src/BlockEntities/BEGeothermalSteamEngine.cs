using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Rendering;

namespace VintageKinematics.BlockEntities
{
    public class BEGeothermalSteamEngine : BlockEntity
    {
        private const float WaterLitresPerSecond = 0.05f;
        private bool hasHeat;
        private bool hasWater;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnSteamTick, 1000);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            string[] excluded = KineticMeshSplitter.CollectManagedElements(this);
            MeshData body = KineticMeshSplitter.TesselateBodyExcluding(Api as ICoreClientAPI, Block, tessThreadTesselator, excluded);
            if (body != null) mesher.AddMeshData(body);
            return true;
        }

        private void OnSteamTick(float dt)
        {
            BEBehaviorKineticSource source = GetBehavior<BEBehaviorKineticSource>();
            if (source == null) return;

            hasHeat = HasAdjacentTappedBore();
            hasWater = false;
            if (!hasHeat)
            {
                source.Wind(0f);
                SetGlowState(false);
                MarkDirty(true);
                return;
            }

            float litres = WaterLitresPerSecond * GameMath.Max(0.001f, dt);
            hasWater = ConsumeAdjacentWater(litres);
            if (!hasWater)
            {
                source.Wind(0f);
                SetGlowState(false);
                MarkDirty(true);
                return;
            }

            source.Wind(1.25f);
            SetGlowState(true);
            MarkDirty(true);
        }

        private bool HasAdjacentTappedBore()
        {
            BlockPos pos = Pos.AddCopy(HeatInputFace());
            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(Api.World, pos);
            return be is IGeothermalHeatProvider provider && provider.CanProvideHeatTo(Pos);
        }

        private BlockFacing HeatInputFace()
        {
            return BlockFacing.FromFirstLetter(Block?.Variant?["side"] ?? "n") ?? BlockFacing.NORTH;
        }

        private bool ConsumeAdjacentWater(float litres)
        {
            foreach (BlockFacing face in BlockFacing.ALLFACES)
            {
                BlockPos pos = Pos.AddCopy(face);
                Block block = Api.World.BlockAccessor.GetBlock(pos);
                if (block is not ILiquidSource source) continue;

                ItemStack content = source.GetContent(pos);
                if (!IsWater(content)) continue;

                WaterTightContainableProps props = BlockLiquidContainerBase.GetContainableProps(content);
                if (props == null) continue;

                int consumeItems = ItemsForLitres(litres, props.ItemsPerLitre);
                if (content.StackSize < consumeItems) continue;

                ItemStack taken = source.TryTakeContent(pos, consumeItems);
                return taken != null && taken.StackSize >= consumeItems;
            }
            return false;
        }

        private static bool IsWater(ItemStack stack)
        {
            AssetLocation code = stack?.Collectible?.Code;
            if (code == null) return false;
            if (WildcardUtil.Match(new AssetLocation("game:*waterportion*"), code)) return true;
            return code.Path != null && code.Path.Contains("water");
        }

        private static int ItemsForLitres(float litres, float itemsPerLitre)
        {
            return System.Math.Max(1, (int)System.Math.Ceiling(litres * itemsPerLitre - 0.0001f));
        }

        private void SetGlowState(bool glow)
        {
            string desired = glow ? "glow" : "cool";
            if (Block?.Variant?["state"] == desired) return;

            Block target = Api.World.GetBlock(Block.CodeWithVariant("state", desired));
            if (target == null || target.BlockId == Block.BlockId) return;

            Api.World.BlockAccessor.ExchangeBlock(target.BlockId, Pos);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("hasHeat", hasHeat);
            tree.SetBool("hasWater", hasWater);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            hasHeat = tree.GetBool("hasHeat", false);
            hasWater = tree.GetBool("hasWater", false);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            if (!hasHeat) dsc.AppendLine(Lang.Get("vintagekinematics:geothermalsteamengine-missingtap"));
            else if (!hasWater) dsc.AppendLine(Lang.Get("vintagekinematics:geothermalsteamengine-missingwater"));
            else dsc.AppendLine(Lang.Get("vintagekinematics:geothermalsteamengine-active"));
        }
    }
}
