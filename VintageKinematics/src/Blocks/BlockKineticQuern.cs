using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;
using VintageKinematics.Network;

namespace VintageKinematics.Blocks
{
    public class BlockKineticQuern : BlockQuern
    {
        // Network RPM at which the kinetic quern's entity-yaw effect matches vanilla intensity.
        private const float QuernYawRefRPM = 32f;

        // Replaces vanilla BlockQuern.OnEntityCollide, which dereferences a null
        // BEBehaviorMPConsumer on the client side because we use our own kinetic
        // network instead of vanilla mechanical power.
        public override void OnEntityCollide(IWorldAccessor world, Entity entity, BlockPos pos, BlockFacing facing, Vec3d collideSpeed, bool isImpact)
        {
            if (facing != BlockFacing.UP) return;

            BEKineticQuern be = world.BlockAccessor.GetBlockEntity(pos) as BEKineticQuern;
            BEBehaviorKinetic beh = be?.GetBehavior<BEBehaviorKinetic>();
            if (beh == null || beh.NetworkConflicted) return;

            float rpm = beh.CurrentRPM;
            if (System.MathF.Abs(rpm) < KineticNetwork.MinAbsRPM) return;

            float frameTime = GlobalConstants.PhysicsFrameTime;
            float yawDelta = frameTime * (rpm / QuernYawRefRPM) * 2.5f;

            if (entity.World.Side == EnumAppSide.Server)
            {
                entity.Pos.Yaw += yawDelta;
                return;
            }

            ICoreClientAPI capi = api as ICoreClientAPI;
            if (capi == null || capi.World.Player.Entity.EntityId != entity.EntityId) return;

            if (capi.World.Player.CameraMode != EnumCameraMode.Overhead)
            {
                capi.Input.MouseYaw += yawDelta;
            }
            capi.World.Player.Entity.BodyYaw += yawDelta;
            capi.World.Player.Entity.WalkYaw += yawDelta;
            capi.World.Player.Entity.Pos.Yaw += yawDelta;
        }
    }
}
