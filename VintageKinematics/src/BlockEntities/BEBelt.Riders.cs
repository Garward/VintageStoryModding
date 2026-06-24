using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Blocks;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        /// <summary>Per-tick lerp alpha for the in-block carry. Tuned high enough to overcome the
        /// player's own zero-input brake (so you don't get stuck after releasing sneak) without
        /// fully overriding walk input.</summary>
        private const float RiderGripFactor = 0.6f;
        /// <summary>Lower lerp alpha for the one-tick past-head carry, just enough nudge to clear
        /// the lip on step-down cascades, not a full take-over.</summary>
        private const float PastHeadGripFactor = 0.35f;
        /// <summary>Entity motion uses a different integration path than belt item progress. This
        /// scale keeps riders visually matched to carried items across normal belt speeds.</summary>
        private const float RiderSpeedScale = 0.125f;

        /// <summary>
        /// In-block push entry point. Called from <see cref="BlockBelt.OnEntityInside"/> on both
        /// sides; the side filter (server pushes mobs, client pushes the local player) is applied
        /// here so the call site stays trivial.
        /// </summary>
        public void PushRiderInBlock(Entity e)
        {
            if (Api == null || Direction == null) return;
            if (e == null || !e.Alive || e is not EntityAgent) return;
            if (e is EntityItem) return;
            if (e is EntityPlayer)
            {
                if (Api.Side != EnumAppSide.Client) return;
                if (Api is ICoreClientAPI capi && capi.World.Player?.Entity != e) return;
            }
            else if (Api.Side != EnumAppSide.Server) return;
            if (e is EntityAgent ea && ea.MountedOn != null) return;

            float velocity = CurrentChainVelocity() * RiderSpeedScale;
            if (velocity == 0f) return;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            ApplyRiderPush(e, fwd.X * velocity, fwd.Z * velocity, RiderGripFactor);
        }

        /// <summary>
        /// Looks up the controller and returns the current signed chain velocity. Returns 0 if the
        /// controller can't be resolved (chain torn down mid-tick, cross-chunk unload, etc.).
        /// </summary>
        private float CurrentChainVelocity()
        {
            BEBelt ctl = IsController ? this : Api.World.BlockAccessor.GetBlockEntity(ControllerPos) as BEBelt;
            if (ctl == null) return 0f;
            BEBehaviorKinetic kinetic = ctl.GetBehavior<BEBehaviorKinetic>();
            float rpm = kinetic?.ActualRPM ?? 0f;
            return ChainVelocity(rpm);
        }

        /// <summary>
        /// Past-head carry: pushes entities sitting in the single cell directly past the head face
        /// so step-down belt cascades work (player walks off the lip and gets one tick of forward
        /// motion to clear the gap). Controller-only.
        /// </summary>
        private void PushRiders(float dt, float velocity, bool clientLocalPlayerOnly)
        {
            if (velocity == 0f) return;
            if (Direction == null || ChainLength <= 0) return;
            if (!IsController) return;

            float riderVel = velocity * RiderSpeedScale;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            // Single cell directly past the head face.
            BlockPos head = Pos.AddCopy(fwd.X * (ChainLength - 1), 0, fwd.Z * (ChainLength - 1));
            BlockPos cell = head.AddCopy(fwd.X, 0, fwd.Z);
            // If the destination cell is another belt, defer entirely to its in-block push;
            // double-pushing would launch the rider diagonally off a perpendicular junction.
            if (Api.World.BlockAccessor.GetBlock(cell) is BlockBelt) return;
            BlockPos minBp = new BlockPos(cell.X, cell.Y, cell.Z, cell.dimension);
            BlockPos maxBp = new BlockPos(cell.X + 1, cell.Y + 1, cell.Z + 1, cell.dimension);

            double pushX = fwd.X * riderVel;
            double pushZ = fwd.Z * riderVel;
            // Only entities crossing through the head-face plane at belt-top height. Tight band so
            // we don't grab anyone who's just walking past at ground level a block beyond the belt.
            double yMin = head.Y + BeltTopY - 0.2;
            double yMax = head.Y + BeltTopY + 0.4;

            if (clientLocalPlayerOnly)
            {
                if (Api is not ICoreClientAPI capi) return;
                EntityPlayer ep = capi.World.Player?.Entity;
                if (ep == null || !ep.Alive) return;
                if (ep.MountedOn != null) return;
                double ex = ep.Pos.X, ey = ep.Pos.Y, ez = ep.Pos.Z;
                if (ex < minBp.X || ex >= maxBp.X || ez < minBp.Z || ez >= maxBp.Z) return;
                if (ey < yMin || ey > yMax) return;
                ApplyRiderPush(ep, pushX, pushZ, PastHeadGripFactor);
                return;
            }

            Entity[] found = Api.World.GetEntitiesInsideCuboid(minBp, maxBp,
                e => e is EntityAgent ea && ea.Alive && ea is not EntityPlayer);
            for (int i = 0; i < found.Length; i++)
            {
                EntityAgent ea = (EntityAgent)found[i];
                if (ea.MountedOn != null) continue;
                double ey = ea.Pos.Y;
                if (ey < yMin || ey > yMax) continue;
                ApplyRiderPush(ea, pushX, pushZ, PastHeadGripFactor);
            }
        }

        private static void ApplyRiderPush(Entity e, double pushX, double pushZ, float grip)
        {
            // Sneak (player only) gives full grip on the belt surface: stand still or walk off.
            if (e is EntityPlayer ep && ep.Controls?.Sneak == true) return;
            Vec3d motion = e.Pos.Motion;
            motion.X += (pushX - motion.X) * grip;
            motion.Z += (pushZ - motion.Z) * grip;
        }
    }
}
