using System;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticActivator : BEKineticAnimated, IKineticConnector
    {
        private const int ServerTickIntervalMs = 100;
        private static readonly int[] DelayStepsMs = { 0, 500, 1000, 1500, 2000, 5000 };
        private static readonly int[] PulseCountSteps = { 1, 2, 3, 4, 5, 8, 16 };
        private static readonly AssetLocation ActivateSound = new AssetLocation("sounds/effect/woodswitch");

        private float elapsedSec;
        private bool lastActivationWorked;
        private bool wasPowered;
        private bool activatedThisRun;
        private int lastDirectionActivated;
        private int pendingDirection;
        private int burstPulsesRemaining;
        private int burstDirection;

        public KineticActivatorMode Mode { get; private set; } = KineticActivatorMode.RepeatWhileRotating;
        public int ActivationDelayMs { get; private set; } = 500;
        public int PulseCount { get; private set; } = 2;

        public void CycleMode(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            Mode = Mode switch
            {
                KineticActivatorMode.RepeatWhileRotating => KineticActivatorMode.OnceUntilStopped,
                KineticActivatorMode.OnceUntilStopped => KineticActivatorMode.OncePerDirection,
                KineticActivatorMode.OncePerDirection => KineticActivatorMode.PulseBurst,
                _ => KineticActivatorMode.RepeatWhileRotating
            };
            ResetActivationState();
            Api.World.PlaySoundAt(ActivateSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, randomizePitch: true, range: 12, volume: 0.45f);
            MarkDirty(true);
        }

        public void CycleDelay(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            int nextIndex = 0;
            for (int i = 0; i < DelayStepsMs.Length; i++)
            {
                if (ActivationDelayMs != DelayStepsMs[i]) continue;
                nextIndex = (i + 1) % DelayStepsMs.Length;
                break;
            }

            ActivationDelayMs = DelayStepsMs[nextIndex];
            ResetActivationState();
            Api.World.PlaySoundAt(ActivateSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, randomizePitch: true, range: 12, volume: 0.45f);
            MarkDirty(true);
        }

        public void CyclePulseCount(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            int nextIndex = 0;
            for (int i = 0; i < PulseCountSteps.Length; i++)
            {
                if (PulseCount != PulseCountSteps[i]) continue;
                nextIndex = (i + 1) % PulseCountSteps.Length;
                break;
            }

            PulseCount = PulseCountSteps[nextIndex];
            ResetActivationState();
            Api.World.PlaySoundAt(ActivateSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, randomizePitch: true, range: 12, volume: 0.45f);
            MarkDirty(true);
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, ServerTickIntervalMs);
            }
        }

        private void OnServerTick(float dt)
        {
            float rpm = SignedRPM();
            if (MathF.Abs(rpm) < KineticNetwork.MinAbsRPM)
            {
                if (wasPowered) ResetActivationState();
                return;
            }

            bool newlyPowered = !wasPowered;
            wasPowered = true;

            if (!ShouldActivateThisTick(dt, rpm, newlyPowered)) return;

            elapsedSec = 0f;
            lastActivationWorked = TryActivateTarget(rpm);
            if (Mode != KineticActivatorMode.RepeatWhileRotating && Mode != KineticActivatorMode.PulseBurst)
            {
                activatedThisRun = true;
                lastDirectionActivated = MathF.Sign(rpm);
                pendingDirection = 0;
            }
            if (lastActivationWorked)
            {
                BlockFacing front = FrontFacing();
                Api.World.PlaySoundAt(ActivateSound, Pos.X + 0.5 + front.Normalf.X * 0.35, Pos.Y + 0.5, Pos.Z + 0.5 + front.Normalf.Z * 0.35, null, randomizePitch: true, range: 10, volume: 0.25f);
            }
            MarkDirty(false);
        }

        private bool ShouldActivateThisTick(float dt, float rpm, bool newlyPowered)
        {
            int dir = MathF.Sign(rpm);
            float delaySeconds = ActivationDelayMs / 1000f;

            switch (Mode)
            {
                case KineticActivatorMode.OnceUntilStopped:
                    if (activatedThisRun) return false;
                    elapsedSec += dt;
                    return elapsedSec >= delaySeconds;

                case KineticActivatorMode.OncePerDirection:
                    if (dir == 0 || dir == lastDirectionActivated) return false;
                    if (dir != pendingDirection)
                    {
                        pendingDirection = dir;
                        elapsedSec = 0f;
                    }
                    elapsedSec += dt;
                    return elapsedSec >= delaySeconds;

                case KineticActivatorMode.PulseBurst:
                    if (dir == 0) return false;
                    if (newlyPowered || burstDirection == 0 || dir != burstDirection)
                    {
                        burstPulsesRemaining = NormalizePulseCount(PulseCount);
                        burstDirection = dir;
                        elapsedSec = 0f;
                    }
                    if (burstPulsesRemaining <= 0) return false;
                    elapsedSec += dt;
                    if (elapsedSec < delaySeconds) return false;
                    burstPulsesRemaining--;
                    elapsedSec = 0f;
                    return true;

                default:
                    elapsedSec += dt;
                    return elapsedSec >= delaySeconds;
            }
        }

        private void ResetActivationState()
        {
            elapsedSec = 0f;
            wasPowered = false;
            activatedThisRun = false;
            lastDirectionActivated = 0;
            pendingDirection = 0;
            burstPulsesRemaining = 0;
            burstDirection = 0;
        }

        private float SignedRPM()
        {
            BEBehaviorKinetic beh = GetBehavior<BEBehaviorKinetic>();
            if (beh == null || beh.NetworkConflicted) return 0f;
            return beh.ActualRPM;
        }

        private bool TryActivateTarget(float signedRPM)
        {
            BlockFacing front = FrontFacing();
            BlockPos targetPos = Pos.AddCopy(front);
            Block targetBlock = Api.World.BlockAccessor.GetBlock(targetPos);
            if (targetBlock == null || targetBlock.Id == 0) return false;

            BlockFacing activatedFace = front.Opposite;
            BlockEntity targetBe = Api.World.BlockAccessor.GetBlockEntity(targetPos);
            if (IsBlacklistedTarget(targetBlock, targetBe))
            {
                return false;
            }

            if (!AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, targetPos, EnumBlockAccessFlags.Use))
            {
                return false;
            }

            if (targetBe is IKineticActivatable beTarget
                && beTarget.OnKineticActivate(Api.World, targetPos, activatedFace, Pos, signedRPM))
            {
                return true;
            }

            if (targetBlock is IKineticActivatable blockTarget
                && blockTarget.OnKineticActivate(Api.World, targetPos, activatedFace, Pos, signedRPM))
            {
                return true;
            }

            if (targetBe is BlockEntityBarrel barrel)
            {
                return TrySealBarrel(barrel);
            }

            try
            {
                Caller caller = new Caller
                {
                    Pos = Pos.ToVec3d(),
                    Type = EnumCallerType.Block
                };
                targetBlock.Activate(Api.World, caller, new BlockSelection(targetPos, activatedFace, targetBlock));
                return true;
            }
            catch (Exception e)
            {
                Api.Logger.Warning("[VintageKinematics] Kinetic Activator failed to activate {0} at {1}: {2}", targetBlock.Code, targetPos, e.Message);
                return false;
            }
        }

        private static bool TrySealBarrel(BlockEntityBarrel barrel)
        {
            if (barrel == null || barrel.Sealed) return false;
            if (!barrel.GetCanSeal(null)) return false;

            barrel.SealBarrel();
            return true;
        }

        private bool IsBlacklistedTarget(Block targetBlock, BlockEntity targetBe)
        {
            VintageKinematicsConfig cfg = Api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;
            return cfg != null && cfg.IsKineticActivatorTargetBlacklisted(targetBlock, targetBe);
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            BlockFacing back = FrontFacing().Opposite;
            if (toPos.X != fromPos.X + back.Normali.X) return null;
            if (toPos.Y != fromPos.Y + back.Normali.Y) return null;
            if (toPos.Z != fromPos.Z + back.Normali.Z) return null;

            if (other.Role == EnumKineticRole.Gearbox)
            {
                Vec3i offset = new Vec3i(toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);
                EnumKineticAxis faceAxis = EnumKineticAxisExtensions.FromVec(offset);
                if (faceAxis != self.Axis || faceAxis == other.Axis) return null;

                return new KineticConnectionResult(1f, -(offset.X + offset.Y + offset.Z));
            }

            if (other.Axis != self.Axis) return null;
            if (other.Role == EnumKineticRole.Custom) return null;

            return new KineticConnectionResult(1f, 1);
        }

        private BlockFacing FrontFacing()
        {
            return Block?.Variant["side"] switch
            {
                "n" => BlockFacing.NORTH,
                "e" => BlockFacing.EAST,
                "s" => BlockFacing.SOUTH,
                "w" => BlockFacing.WEST,
                "u" => BlockFacing.UP,
                "d" => BlockFacing.DOWN,
                _ => BlockFacing.NORTH
            };
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetBool("lastActivationWorked", lastActivationWorked);
            tree.SetInt("mode", (int)Mode);
            tree.SetInt("activationDelayMs", ActivationDelayMs);
            tree.SetInt("pulseCount", PulseCount);
            tree.SetBool("activatedThisRun", activatedThisRun);
            tree.SetInt("lastDirectionActivated", lastDirectionActivated);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            lastActivationWorked = tree.GetBool("lastActivationWorked", false);
            Mode = (KineticActivatorMode)tree.GetInt("mode", (int)KineticActivatorMode.RepeatWhileRotating);
            ActivationDelayMs = NormalizeDelay(tree.GetInt("activationDelayMs", 500));
            PulseCount = NormalizePulseCount(tree.GetInt("pulseCount", 2));
            activatedThisRun = tree.GetBool("activatedThisRun", false);
            lastDirectionActivated = tree.GetInt("lastDirectionActivated", 0);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine($"Activator mode: {ModeName()}");
            dsc.AppendLine($"Activation delay: {DelayName()}");
            dsc.AppendLine($"Burst pulses: {PulseCount}");
            dsc.AppendLine(lastActivationWorked ? "Activator: target accepted last click" : "Activator: waiting for valid target");
        }

        private static int NormalizeDelay(int delayMs)
        {
            foreach (int step in DelayStepsMs)
            {
                if (delayMs <= step) return step;
            }
            return DelayStepsMs[DelayStepsMs.Length - 1];
        }

        private static int NormalizePulseCount(int pulseCount)
        {
            foreach (int step in PulseCountSteps)
            {
                if (pulseCount <= step) return step;
            }
            return PulseCountSteps[PulseCountSteps.Length - 1];
        }

        private string ModeName()
        {
            return Mode switch
            {
                KineticActivatorMode.OnceUntilStopped => "once until stopped",
                KineticActivatorMode.OncePerDirection => "once per direction",
                KineticActivatorMode.PulseBurst => "pulse burst",
                _ => "repeat while rotating"
            };
        }

        private string DelayName()
        {
            return ActivationDelayMs <= 0 ? "instant" : $"{ActivationDelayMs} ms";
        }
    }
}
