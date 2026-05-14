using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;
using CropGrowthRate.Core;

namespace CropGrowthRate.Behaviors
{
    public class BlockEntityGrowthTicker : BlockEntityBehavior
    {
        private const double Unscheduled = -1.0;

        private double nextStageTotalHours = Unscheduled;
        private long tickListenerId = -1;

        public BlockEntityGrowthTicker(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            if (api.Side != EnumAppSide.Server) return;

            var modSys = api.ModLoader.GetModSystem<CropGrowthRateModSystem>();
            int interval = modSys?.Config?.growthTickIntervalMs ?? 3000;
            tickListenerId = Blockentity.RegisterGameTickListener(OnGrowthTick, interval);
        }

        public override void OnBlockRemoved()
        {
            if (tickListenerId != -1)
            {
                Blockentity.UnregisterGameTickListener(tickListenerId);
                tickListenerId = -1;
            }
            base.OnBlockRemoved();
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            nextStageTotalHours = tree.GetDouble("cgr_nextStage", Unscheduled);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("cgr_nextStage", nextStageTotalHours);
        }

        private void OnGrowthTick(float dt)
        {
            var api = Blockentity.Api;
            var farmland = Blockentity as BlockEntityFarmland;
            if (farmland == null) return;

            var modSys = api.ModLoader.GetModSystem<CropGrowthRateModSystem>();
            var config = modSys?.Config;
            if (config == null) return;

            Block cropBlock = api.World.BlockAccessor.GetBlock(farmland.UpPos);
            if (cropBlock?.CropProps == null)
            {
                nextStageTotalHours = Unscheduled;
                return;
            }
            if (farmland.HasRipeCrop()) return;

            string cropCode = cropBlock.Code?.ToString() ?? string.Empty;
            float cropOverride = BlockCodeMatcher.ResolveOverride(cropCode, config.cropOverrides);

            float vanillaConditions = farmland.GetGrowthRate(cropBlock.CropProps.RequiredNutrient);
            float speed = config.growthSpeedMultiplier * cropOverride * vanillaConditions;
            if (speed <= 0f) return;

            double scaledStageHours = ComputeScaledStageHours(api, cropBlock, speed);
            if (scaledStageHours <= 0) return;

            double nowTotalHours = api.World.Calendar.TotalHours;

            if (nextStageTotalHours <= Unscheduled)
            {
                nextStageTotalHours = nowTotalHours + scaledStageHours;
                Blockentity.MarkDirty(false);
                return;
            }

            int advanced = 0;
            int maxPerTick = config.growthStagesPerTickMax;
            if (maxPerTick < 1) maxPerTick = 1;

            while (nextStageTotalHours <= nowTotalHours && advanced < maxPerTick)
            {
                if (!farmland.TryGrowCrop(nextStageTotalHours)) break;
                nextStageTotalHours += scaledStageHours;
                advanced++;
                if (farmland.HasRipeCrop()) break;
            }

            if (advanced > 0) Blockentity.MarkDirty(false);
        }

        private static double ComputeScaledStageHours(ICoreAPI api, Block cropBlock, float speed)
        {
            var cropProps = cropBlock.CropProps;
            if (cropProps == null || cropProps.GrowthStages <= 0) return 0;

            double totalDays = cropProps.TotalGrowthDays;
            if (totalDays > 0)
            {
                double defaultMonths = totalDays / 12.0;
                totalDays = defaultMonths * api.World.Calendar.DaysPerMonth;
            }
            else
            {
                totalDays = cropProps.TotalGrowthMonths * api.World.Calendar.DaysPerMonth;
            }

            double stageHours = api.World.Calendar.HoursPerDay * totalDays / cropProps.GrowthStages;
            return stageHours / speed;
        }
    }
}
