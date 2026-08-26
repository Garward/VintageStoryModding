using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Entities;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEGantryShaft : BEKinetic
    {
        private const int MoveIntervalMs = 10;
        private const float BlocksPerRotation = 1.2f;
        private const float ContraptionBaseStressImpact = 1f;
        private const float ContraptionStressImpactPerBlock = 0.25f;
        private const long MoveSoundIntervalMs = 450;
        private const long AutoRestoreSettleDelayMs = 250;
        private static readonly AssetLocation GantryMoveSound = new AssetLocation("sounds/effect/gearbox_turn.ogg");
        private static readonly AssetLocation GantryStartSound = new AssetLocation("sounds/effect/woodswitch.ogg");
        private static readonly AssetLocation GantryStopSound = new AssetLocation("sounds/effect/latch.ogg");
        private static readonly Dictionary<long, long> LastMovedEntityMs = new Dictionary<long, long>();
        private static readonly Dictionary<long, long> AutoRestoreSettleStartMs = new Dictionary<long, long>();
        private static readonly Dictionary<string, GantrySoundState> SoundStates = new Dictionary<string, GantrySoundState>();

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnMoveTick, MoveIntervalMs);
            }
        }

        private void OnMoveTick(float dt)
        {
            if (!TryGetAxis(out EnumKineticAxis axis)) return;

            ExpandTrack(axis, out BlockPos trackMin, out BlockPos trackMax);
            if (!IsCanonicalTrackHost(trackMin)) return;
            string soundKey = TrackKey(trackMin, trackMax, axis);

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = kinetic?.ActualRPM ?? 0f;
            bool canDrive = MathF.Abs(rpm) >= 0.001f && kinetic != null && !kinetic.IsConflicted && !(kinetic.Network?.IsOverstressed ?? false);
            if (!canDrive)
            {
                RestoreStoppedContraptions(trackMin, trackMax, axis);
                UpdateGantrySound(soundKey, false, rpm, trackMin, trackMax);
                return;
            }

            Vec3i axisVec = EnumKineticAxisExtensions.UnitVector(axis);
            double delta = rpm * BlocksPerRotation * dt / 60.0;
            TryAssembleControllersThatCanMove(trackMin, trackMax, axis, delta);

            double pendingDx = axisVec.X * delta;
            double pendingDy = axisVec.Y * delta;
            double pendingDz = axisVec.Z * delta;

            if (!TryApplyContraptionLoad(trackMin, trackMax, axis, rpm, dt, pendingDx, pendingDy, pendingDz, out List<EntityVKContraption> movingContraptions))
            {
                RestoreStoppedContraptions(trackMin, trackMax, axis);
                UpdateGantrySound(soundKey, false, rpm, trackMin, trackMax);
                return;
            }

            bool movedAny = false;
            foreach (EntityVKContraption contraption in movingContraptions)
            {
                if (!TryClaimEntityMove(contraption)) continue;
                if (!contraption.TryGetControllerWorldPosition(out Vec3d controllerPos)) continue;
                contraption.RunContraptionWorkTick(rpm, dt, pendingDx, pendingDy, pendingDz);
                if (contraption.IsMovementPaused(out _)) continue;

                double railAnchorCoord = AxisCoord(controllerPos, axis);
                double minRailCoord = AxisCoord(trackMin, axis);
                double maxRailCoord = AxisCoord(trackMax, axis);
                if (maxRailCoord < minRailCoord)
                {
                    double tmp = minRailCoord;
                    minRailCoord = maxRailCoord;
                    maxRailCoord = tmp;
                }

                if (!CanMoveInDirection(railAnchorCoord, minRailCoord, maxRailCoord, delta))
                {
                    HoldWorkingOrRestoreAfterSettle(contraption, rpm, dt, pendingDx, pendingDy, pendingDz);
                    continue;
                }

                double nextRailAnchorCoord = GameMath.Clamp(railAnchorCoord + delta, minRailCoord, maxRailCoord);
                double move = nextRailAnchorCoord - railAnchorCoord;
                if (Math.Abs(move) < 0.000001)
                {
                    HoldWorkingOrRestoreAfterSettle(contraption, rpm, dt, pendingDx, pendingDy, pendingDz);
                    continue;
                }

                double dx = axisVec.X * move;
                double dy = axisVec.Y * move;
                double dz = axisVec.Z * move;
                if (contraption.WouldMovementHitWorldBlock(dx, dy, dz, out string blockReason, out bool protectedClaim))
                {
                    if (protectedClaim && contraption.TryRestoreToWorld(null, overwrite: false))
                    {
                        AutoRestoreSettleStartMs.Remove(contraption.EntityId);
                        LastMovedEntityMs.Remove(contraption.EntityId);
                        continue;
                    }

                    contraption.RequestMovementPause("gantry-blocked", 250, blockReason);
                    AutoRestoreSettleStartMs.Remove(contraption.EntityId);
                    continue;
                }

                AutoRestoreSettleStartMs.Remove(contraption.EntityId);
                contraption.MoveBy(dx, dy, dz);
                movedAny = true;
            }

            UpdateGantrySound(soundKey, movedAny, rpm, trackMin, trackMax);
        }

        private bool TryApplyContraptionLoad(BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis, float rpm, float dt, double moveX, double moveY, double moveZ, out List<EntityVKContraption> movingContraptions)
        {
            movingContraptions = new List<EntityVKContraption>();
            float stressImpact = 0f;

            foreach (EntityVKContraption contraption in FindContraptionsNearTrack(trackMin, trackMax, axis))
            {
                if (!contraption.TryGetControllerWorldPosition(out _)) continue;

                movingContraptions.Add(contraption);
                stressImpact += ContraptionBaseStressImpact + ContraptionStressImpactPerBlock * MathF.Max(1, contraption.CapturedBlockCount);
                stressImpact += contraption.GetActiveContraptionWorkStressImpact(rpm, dt, moveX, moveY, moveZ);
            }

            if (movingContraptions.Count == 0) return true;

            KineticNetworkManager networks = Api.ModLoader.GetModSystem<KineticNetworkManager>();
            if (networks == null) return true;

            string loadKey = TrackKey(trackMin, trackMax, axis) + ":contraption-load";
            if (networks.TryApplyTransientStress(Pos, loadKey, stressImpact, out _)) return true;

            KineticNetwork net = networks.GetNetworkAt(Pos);
            return net == null || !net.IsOverstressed;
        }

        private bool IsCanonicalTrackHost(BlockPos trackMin)
        {
            return Pos.X == trackMin.X
                && Pos.InternalY == trackMin.InternalY
                && Pos.Z == trackMin.Z
                && Pos.dimension == trackMin.dimension;
        }

        private void UpdateGantrySound(string soundKey, bool moving, float rpm, BlockPos trackMin, BlockPos trackMax)
        {
            long now = Api.World.ElapsedMilliseconds;
            if (!SoundStates.TryGetValue(soundKey, out GantrySoundState state))
            {
                state = new GantrySoundState();
                SoundStates[soundKey] = state;
            }

            Vec3d soundPos = TrackCenter(trackMin, trackMax);
            if (moving)
            {
                if (!state.Active)
                {
                    PlayGantrySound(GantryStartSound, soundPos, 0.75f, 0.9f);
                    state.Active = true;
                }

                if (now - state.LastMoveSoundMs >= MoveSoundIntervalMs)
                {
                    float rpmScale = MathF.Min(MathF.Abs(rpm) / 64f, 2f);
                    PlayGantrySound(GantryMoveSound, soundPos, 0.18f + 0.12f * rpmScale, 0.65f + 0.08f * rpmScale);
                    state.LastMoveSoundMs = now;
                }

                return;
            }

            if (state.Active)
            {
                PlayGantrySound(GantryStopSound, soundPos, 0.6f, 0.75f);
                state.Active = false;
            }
        }

        private void PlayGantrySound(AssetLocation sound, Vec3d pos, float volume, float range)
        {
            Api.World.PlaySoundAt(sound, pos.X, pos.Y, pos.Z, null, randomizePitch: true, range: 18f, volume: volume);
        }

        private static Vec3d TrackCenter(BlockPos trackMin, BlockPos trackMax)
        {
            return new Vec3d(
                (trackMin.X + trackMax.X) * 0.5 + 0.5,
                (trackMin.InternalY + trackMax.InternalY) * 0.5 + 0.5,
                (trackMin.Z + trackMax.Z) * 0.5 + 0.5);
        }

        private static string TrackKey(BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis)
        {
            return trackMin.dimension + ":" + axis + ":"
                + trackMin.X + "," + trackMin.InternalY + "," + trackMin.Z + ":"
                + trackMax.X + "," + trackMax.InternalY + "," + trackMax.Z;
        }

        private void TryAssembleControllersThatCanMove(BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis, double delta)
        {
            double minRailCoord = AxisCoord(trackMin, axis);
            double maxRailCoord = AxisCoord(trackMax, axis);
            if (maxRailCoord < minRailCoord)
            {
                double tmp = minRailCoord;
                minRailCoord = maxRailCoord;
                maxRailCoord = tmp;
            }

            foreach (BEGantryCarriage controller in FindWorldControllersNearTrack(trackMin, trackMax, axis))
            {
                if (!controller.TryGetGantryAnchor(axis, out BlockPos anchorPos)) continue;

                double controllerCoord = AxisCoord(anchorPos, axis);
                if (!CanMoveInDirection(controllerCoord, minRailCoord, maxRailCoord, delta)) continue;

                controller.TryAssembleForGantry();
            }
        }

        private void RestoreStoppedContraptions(BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis)
        {
            foreach (EntityVKContraption contraption in FindContraptionsNearTrack(trackMin, trackMax, axis))
            {
                if (!TryClaimEntityMove(contraption)) continue;
                if (contraption.IsMovementPaused(out _)) continue;
                TryAutoRestoreAfterSettle(contraption);
            }
        }

        private static bool ShouldHoldAsWorkingEntity(EntityVKContraption contraption, float rpm, float dt, double moveX, double moveY, double moveZ)
        {
            if (contraption == null) return false;
            if (contraption.IsMovementPaused(out _)) return true;
            return contraption.GetActiveContraptionWorkStressImpact(rpm, dt, moveX, moveY, moveZ) > 0f;
        }

        private void HoldWorkingOrRestoreAfterSettle(EntityVKContraption contraption, float rpm, float dt, double moveX, double moveY, double moveZ)
        {
            if (ShouldHoldAsWorkingEntity(contraption, rpm, dt, moveX, moveY, moveZ))
            {
                AutoRestoreSettleStartMs.Remove(contraption.EntityId);
                return;
            }

            TryAutoRestoreAfterSettle(contraption);
        }

        private bool TryAutoRestoreAfterSettle(EntityVKContraption contraption)
        {
            if (contraption == null) return false;

            long now = Api.World.ElapsedMilliseconds;
            if (!AutoRestoreSettleStartMs.TryGetValue(contraption.EntityId, out long startMs))
            {
                AutoRestoreSettleStartMs[contraption.EntityId] = now;
                return false;
            }

            if (now - startMs < AutoRestoreSettleDelayMs) return false;

            bool restored = contraption.TryAutoRestoreWhenStopped();
            if (restored)
            {
                AutoRestoreSettleStartMs.Remove(contraption.EntityId);
                LastMovedEntityMs.Remove(contraption.EntityId);
            }

            return restored;
        }

        private IEnumerable<BEGantryCarriage> FindWorldControllersNearTrack(BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis)
        {
            HashSet<string> seen = new HashSet<string>();
            GetControllerScanBounds(trackMin, trackMax, axis, out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ);

            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        BlockPos pos = new BlockPos(x, y, z, Pos.dimension);
                        if (Api.World.BlockAccessor.GetBlockEntity(pos) is not BEGantryCarriage controller) continue;
                        if (!seen.Add(PositionKey(pos))) continue;
                        if (!controller.TryGetGantryAnchor(axis, out BlockPos anchorPos)) continue;
                        if (!IsAnchorOnTrack(anchorPos, trackMin, trackMax, axis)) continue;

                        yield return controller;
                    }
                }
            }
        }

        private void GetControllerScanBounds(BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis, out int minX, out int maxX, out int minY, out int maxY, out int minZ, out int maxZ)
        {
            const int perpendicularRange = 2;
            minX = Math.Min(trackMin.X, trackMax.X) - perpendicularRange;
            maxX = Math.Max(trackMin.X, trackMax.X) + perpendicularRange;
            minY = Math.Min(trackMin.InternalY, trackMax.InternalY) - perpendicularRange;
            maxY = Math.Max(trackMin.InternalY, trackMax.InternalY) + perpendicularRange;
            minZ = Math.Min(trackMin.Z, trackMax.Z) - perpendicularRange;
            maxZ = Math.Max(trackMin.Z, trackMax.Z) + perpendicularRange;

            if (axis == EnumKineticAxis.X)
            {
                minX = Math.Min(trackMin.X, trackMax.X);
                maxX = Math.Max(trackMin.X, trackMax.X);
            }
            else if (axis == EnumKineticAxis.Y)
            {
                minY = Math.Min(trackMin.InternalY, trackMax.InternalY);
                maxY = Math.Max(trackMin.InternalY, trackMax.InternalY);
            }
            else
            {
                minZ = Math.Min(trackMin.Z, trackMax.Z);
                maxZ = Math.Max(trackMin.Z, trackMax.Z);
            }
        }

        private static bool CanMoveInDirection(double currentCoord, double minRailCoord, double maxRailCoord, double delta)
        {
            const double endpointEpsilon = 0.000001;
            if (delta > 0) return currentCoord < maxRailCoord - endpointEpsilon;
            if (delta < 0) return currentCoord > minRailCoord + endpointEpsilon;
            return false;
        }

        private bool TryClaimEntityMove(EntityVKContraption contraption)
        {
            long now = Api.World.ElapsedMilliseconds;
            if (LastMovedEntityMs.TryGetValue(contraption.EntityId, out long lastMs) && now - lastMs < MoveIntervalMs) return false;

            LastMovedEntityMs[contraption.EntityId] = now;
            return true;
        }

        private IEnumerable<EntityVKContraption> FindContraptionsNearTrack(BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis)
        {
            HashSet<string> seen = new HashSet<string>();
            Vec3d center = new Vec3d(
                (trackMin.X + trackMax.X) * 0.5 + 0.5,
                (trackMin.InternalY + trackMax.InternalY) * 0.5 + 0.5,
                (trackMin.Z + trackMax.Z) * 0.5 + 0.5);
            float radius = Math.Max(2f, Math.Abs(AxisInt(trackMax, axis) - AxisInt(trackMin, axis)) + 2f);
            Entity[] entities = Api.World.GetEntitiesAround(center, radius, radius, entity => entity is EntityVKContraption contraption && contraption.Alive && contraption.AssemblyReady && !contraption.SnapshotRestored);

            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] is not EntityVKContraption contraption) continue;
                if (!contraption.Alive || !contraption.AssemblyReady || contraption.SnapshotRestored) continue;
                if (!seen.Add(contraption.EntityId.ToString())) continue;
                if (!contraption.TryGetControllerAxis(out EnumKineticAxis controllerAxis) || controllerAxis != axis) continue;
                if (!TryGetContraptionAnchorBlockPos(contraption, out BlockPos anchorPos)) continue;
                if (!IsAnchorOnTrack(anchorPos, trackMin, trackMax, axis)) continue;

                yield return contraption;
            }
        }

        private bool TryGetContraptionAnchorBlockPos(EntityVKContraption contraption, out BlockPos anchorPos)
        {
            anchorPos = null;
            if (contraption == null || !contraption.TryGetControllerWorldPosition(out Vec3d anchorWorldPos)) return false;

            anchorPos = new BlockPos(
                (int)Math.Floor(anchorWorldPos.X + 0.5),
                (int)Math.Floor(anchorWorldPos.Y + 0.5) % BlockPos.DimensionBoundary,
                (int)Math.Floor(anchorWorldPos.Z + 0.5),
                Pos.dimension);
            return true;
        }

        private bool IsAnchorOnTrack(BlockPos anchorPos, BlockPos trackMin, BlockPos trackMax, EnumKineticAxis axis)
        {
            if (anchorPos == null || trackMin == null || trackMax == null) return false;
            if (anchorPos.dimension != trackMin.dimension || anchorPos.dimension != trackMax.dimension) return false;
            if (!IsSameTrackLine(anchorPos, trackMin, axis)) return false;

            int anchorCoord = AxisInt(anchorPos, axis);
            int minCoord = Math.Min(AxisInt(trackMin, axis), AxisInt(trackMax, axis));
            int maxCoord = Math.Max(AxisInt(trackMin, axis), AxisInt(trackMax, axis));
            if (anchorCoord < minCoord || anchorCoord > maxCoord) return false;

            Block anchorBlock = Api.World.BlockAccessor.GetBlock(anchorPos);
            return IsGantryShaft(anchorBlock, out EnumKineticAxis anchorAxis) && anchorAxis == axis;
        }

        private static bool IsSameTrackLine(BlockPos pos, BlockPos trackPos, EnumKineticAxis axis)
        {
            return axis switch
            {
                EnumKineticAxis.X => pos.InternalY == trackPos.InternalY && pos.Z == trackPos.Z,
                EnumKineticAxis.Y => pos.X == trackPos.X && pos.Z == trackPos.Z,
                EnumKineticAxis.Z => pos.X == trackPos.X && pos.InternalY == trackPos.InternalY,
                _ => false
            };
        }

        private void ExpandTrack(EnumKineticAxis axis, out BlockPos trackMin, out BlockPos trackMax)
        {
            Vec3i axisVec = EnumKineticAxisExtensions.UnitVector(axis);
            trackMin = Pos.Copy();
            trackMax = Pos.Copy();

            while (TryStepTrack(trackMin, -axisVec.X, -axisVec.Y, -axisVec.Z, axis, out BlockPos nextMin))
            {
                trackMin = nextMin;
            }

            while (TryStepTrack(trackMax, axisVec.X, axisVec.Y, axisVec.Z, axis, out BlockPos nextMax))
            {
                trackMax = nextMax;
            }
        }

        private bool TryStepTrack(BlockPos from, int dx, int dy, int dz, EnumKineticAxis axis, out BlockPos next)
        {
            next = from.AddCopy(dx, dy, dz);
            Block block = Api.World.BlockAccessor.GetBlock(next);
            if (IsGantryShaft(block, out EnumKineticAxis nextAxis) && nextAxis == axis) return true;
            return false;
        }

        private bool TryGetAxis(out EnumKineticAxis axis)
        {
            return IsGantryShaft(Block, out axis);
        }

        private static bool IsGantryShaft(Block block, out EnumKineticAxis axis)
        {
            axis = EnumKineticAxis.X;
            if (block?.Code == null) return false;
            if (block.Code.Domain != "vintagekinematics" || block.Code.FirstCodePart() != "gantryshaft") return false;

            string axisCode = block.Variant["axis"];
            if (axisCode == "y") axis = EnumKineticAxis.Y;
            else if (axisCode == "z") axis = EnumKineticAxis.Z;
            else axis = EnumKineticAxis.X;
            return true;
        }

        private static int AxisInt(BlockPos pos, EnumKineticAxis axis)
        {
            return axis switch
            {
                EnumKineticAxis.X => pos.X,
                EnumKineticAxis.Y => pos.InternalY,
                EnumKineticAxis.Z => pos.Z,
                _ => pos.X
            };
        }

        private static double AxisCoord(BlockPos pos, EnumKineticAxis axis)
        {
            return axis switch
            {
                EnumKineticAxis.X => pos.X,
                EnumKineticAxis.Y => pos.InternalY,
                EnumKineticAxis.Z => pos.Z,
                _ => pos.X
            };
        }

        private static double AxisCoord(Vec3d pos, EnumKineticAxis axis)
        {
            return axis switch
            {
                EnumKineticAxis.X => pos.X,
                EnumKineticAxis.Y => pos.Y,
                EnumKineticAxis.Z => pos.Z,
                _ => pos.X
            };
        }

        private static string PositionKey(BlockPos pos)
        {
            return pos.dimension + ":" + pos.X + "," + pos.InternalY + "," + pos.Z;
        }

        private sealed class GantrySoundState
        {
            public bool Active;
            public long LastMoveSoundMs;
        }
    }
}
