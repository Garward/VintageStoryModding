using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Network;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Phase-synced effects for the kinetic bellows. The actual forge-press boost is driven by
    /// BEBehaviorKinetic.ActualRPM so this behavior stays visual/audio only.
    /// </summary>
    public class BEBehaviorBellowsPulse : BlockEntityBehavior
    {
        private static readonly AssetLocation BellowsSound = new AssetLocation("sounds/effect/bellows");
        private const float FirepitBurnMultiplier = 3f;

        private float minRPM = KineticNetwork.MinAbsRPM;

        public BEBehaviorBellowsPulse(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            minRPM = properties?["minRPM"].AsFloat(KineticNetwork.MinAbsRPM) ?? KineticNetwork.MinAbsRPM;

            BEBehaviorKineticPiston piston = Blockentity.GetBehavior<BEBehaviorKineticPiston>();
            if (piston == null)
            {
                api.Logger.Warning($"[VintageKinematics] BellowsPulse at {Pos} has no sibling KineticPiston - inert");
                return;
            }

            piston.OnPhaseCross("TopBoard", MathF.PI, OnCompressionStroke);
        }

        private void OnCompressionStroke()
        {
            BEBehaviorKinetic kinetic = Blockentity.GetBehavior<BEBehaviorKinetic>();
            float rpm = MathF.Abs(kinetic?.ActualRPM ?? 0f);
            if (rpm < minRPM) return;

            BlockFacing facing = FrontFacing();
            if (facing == null) return;

            if (Api.Side == EnumAppSide.Server)
            {
                ApplyAirToFrontTarget(facing, rpm);
                return;
            }

            Vec3d pos = new Vec3d(
                Pos.X + 0.5 + facing.Normalf.X * 0.52,
                Pos.Y + 0.45,
                Pos.Z + 0.5 + facing.Normalf.Z * 0.52);

            Api.World.PlaySoundAt(BellowsSound, pos.X, pos.Y, pos.Z, null, randomizePitch: true, range: 14, volume: 0.45f);

            SimpleParticleProperties particles = new SimpleParticleProperties(
                minQuantity: 2,
                maxQuantity: 4,
                color: ColorUtil.ToRgba(75, 190, 185, 160),
                minPos: pos.AddCopy(-0.025, -0.025, -0.025),
                maxPos: pos.AddCopy(0.025, 0.025, 0.025),
                minVelocity: new Vec3f(facing.Normalf.X * 0.025f - 0.005f, 0.002f, facing.Normalf.Z * 0.025f - 0.005f),
                maxVelocity: new Vec3f(facing.Normalf.X * 0.055f + 0.005f, 0.018f, facing.Normalf.Z * 0.055f + 0.005f),
                lifeLength: 0.35f,
                gravityEffect: 0f,
                minSize: 0.08f,
                maxSize: 0.16f,
                model: EnumParticleModel.Quad
            );
            Api.World.SpawnParticles(particles);
        }

        private void ApplyAirToFrontTarget(BlockFacing facing, float rpm)
        {
            BlockPos targetPos = Pos.AddCopy(facing);
            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(targetPos);
            if (be is not BlockEntityFirepit firepit || !firepit.IsBurning) return;

            float extraSeconds = 60f * (FirepitBurnMultiplier - 1f) / MathF.Max(minRPM, rpm);
            firepit.fuelBurnTime = MathF.Max(0f, firepit.fuelBurnTime - extraSeconds);
            firepit.furnaceTemperature = firepit.changeTemperature(firepit.furnaceTemperature, firepit.maxTemperature, extraSeconds);

            if (firepit.canHeatInput()) firepit.heatInput(extraSeconds);
            if (firepit.canHeatOutput()) firepit.heatOutput(extraSeconds);

            firepit.MarkDirty(true);
        }

        private BlockFacing FrontFacing()
        {
            switch (Blockentity.Block?.Variant?["side"])
            {
                case "n": return BlockFacing.NORTH;
                case "e": return BlockFacing.EAST;
                case "s": return BlockFacing.SOUTH;
                case "w": return BlockFacing.WEST;
                default: return null;
            }
        }
    }
}
