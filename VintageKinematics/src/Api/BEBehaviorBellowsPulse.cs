using System;
using System.Text;
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
        private const float FirepitTemperatureBonusRatio = 0.10f;
        private const float FirepitTemperatureBonusCap = 150f;
        private const float LowOrganicFuelCap = 850f;
        private const float PeatFuelCap = 950f;
        private const float CoalFuelCap = 1250f;
        private const float CharcoalFuelCap = 1400f;

        private float minRPM = KineticNetwork.MinAbsRPM;
        private float soundVolume = 0.18f;
        private float soundRange = 8f;
        private float minSoundIntervalSeconds = 0.65f;
        private long lastSoundMs;
        private long lastAirPulseSeconds = -1;
        private long lastStatusSyncMs;
        private string lastAirTargetCode;

        public BEBehaviorBellowsPulse(BlockEntity blockentity) : base(blockentity) { }

        public override void Initialize(ICoreAPI api, JsonObject properties)
        {
            base.Initialize(api, properties);
            minRPM = properties?["minRPM"].AsFloat(KineticNetwork.MinAbsRPM) ?? KineticNetwork.MinAbsRPM;
            soundVolume = properties?["soundVolume"].AsFloat(soundVolume) ?? soundVolume;
            soundRange = properties?["soundRange"].AsFloat(soundRange) ?? soundRange;
            minSoundIntervalSeconds = properties?["minSoundIntervalSeconds"].AsFloat(minSoundIntervalSeconds) ?? minSoundIntervalSeconds;

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

            long nowMs = Api.World.ElapsedMilliseconds;
            if (nowMs - lastSoundMs >= minSoundIntervalSeconds * 1000f)
            {
                lastSoundMs = nowMs;
                Api.World.PlaySoundAt(BellowsSound, pos.X, pos.Y, pos.Z, null, randomizePitch: true, range: soundRange, volume: soundVolume);
            }

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
            CombustibleProperties fuelProps = firepit.fuelCombustibleOpts;
            if (fuelProps == null || fuelProps.BurnTemperature <= 0) return;

            float extraSeconds = 60f * (FirepitBurnMultiplier - 1f) / MathF.Max(minRPM, rpm);
            float fuelTemperature = firepit.maxTemperature > 0 ? firepit.maxTemperature : fuelProps.BurnTemperature;
            float effectiveTemperature = EffectiveFirepitTemperature(fuelTemperature);
            firepit.fuelBurnTime = MathF.Max(0f, firepit.fuelBurnTime - extraSeconds);
            firepit.furnaceTemperature = firepit.changeTemperature(firepit.furnaceTemperature, effectiveTemperature, extraSeconds);

            if (firepit.canHeatInput()) firepit.heatInput(extraSeconds);
            if (firepit.canHeatOutput()) firepit.heatOutput(extraSeconds);

            lastAirPulseSeconds = Api.World.Calendar.ElapsedSeconds;
            lastAirTargetCode = be.Block?.Code?.ToShortString();
            long nowMs = Api.World.ElapsedMilliseconds;
            if (nowMs - lastStatusSyncMs >= 1000)
            {
                lastStatusSyncMs = nowMs;
                Blockentity.MarkDirty(true);
            }

            firepit.MarkDirty(true);
        }

        private static float EffectiveFirepitTemperature(float fuelTemperature)
        {
            float bonus = MathF.Min(FirepitTemperatureBonusCap, fuelTemperature * FirepitTemperatureBonusRatio);
            return MathF.Min(fuelTemperature + bonus, FirepitFuelTemperatureCap(fuelTemperature));
        }

        private static float FirepitFuelTemperatureCap(float fuelTemperature)
        {
            if (fuelTemperature < LowOrganicFuelCap) return LowOrganicFuelCap;
            if (fuelTemperature < 1000f) return PeatFuelCap;
            if (fuelTemperature < 1250f) return CoalFuelCap;
            return CharcoalFuelCap;
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

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            BEBehaviorKinetic kinetic = Blockentity.GetBehavior<BEBehaviorKinetic>();
            float rpm = MathF.Abs(kinetic?.ActualRPM ?? 0f);
            bool powered = rpm >= minRPM;
            BlockFacing facing = FrontFacing();

            dsc.AppendLine();
            dsc.AppendLine($"Bellows air: {(powered ? "active" : "idle")} ({rpm:F0} / {minRPM:F0} RPM)");

            if (facing == null)
            {
                dsc.AppendLine("Bellows target: unknown facing");
                return;
            }

            BlockPos targetPos = Pos.AddCopy(facing);
            BlockEntity targetBe = MultiblockHelper.GetMultiblockAwareBE(Api.World, targetPos);
            string facingName = facing.Code;

            if (targetBe is BlockEntityFirepit firepit)
            {
                dsc.AppendLine($"Bellows target: {facingName} firepit, {(firepit.IsBurning ? "burning" : "not burning")}");
                if (firepit.IsBurning)
                {
                    dsc.AppendLine($"Bellows firepit heat target: {EffectiveFirepitTemperature(firepit.maxTemperature):F0} C");
                }
            }
            else if ((targetBe?.Block?.Code?.Path ?? "").Contains("kineticforgepress"))
            {
                dsc.AppendLine($"Bellows target: {facingName} forge press; press boost is adjacency-based");
            }
            else
            {
                string targetName = targetBe?.Block?.Code?.ToShortString() ?? Api.World.BlockAccessor.GetBlock(targetPos)?.Code?.ToShortString() ?? "air";
                dsc.AppendLine($"Bellows target: {facingName} {targetName}");
            }

            if (lastAirPulseSeconds >= 0)
            {
                long age = Math.Max(0, Api.World.Calendar.ElapsedSeconds - lastAirPulseSeconds);
                string target = string.IsNullOrEmpty(lastAirTargetCode) ? "firepit" : lastAirTargetCode;
                dsc.AppendLine($"Last firepit heat boost: {age}s ago ({target})");
            }
            else
            {
                dsc.AppendLine("Last firepit heat boost: never");
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetLong("bellowsLastAirPulseSeconds", lastAirPulseSeconds);
            tree.SetString("bellowsLastAirTargetCode", lastAirTargetCode ?? "");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            lastAirPulseSeconds = tree.GetLong("bellowsLastAirPulseSeconds", -1);
            string code = tree.GetString("bellowsLastAirTargetCode", "");
            lastAirTargetCode = string.IsNullOrEmpty(code) ? null : code;
        }
    }
}
