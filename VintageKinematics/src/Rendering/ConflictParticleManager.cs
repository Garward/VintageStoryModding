using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Network;

namespace VintageKinematics.Rendering
{
    public class ConflictParticleManager
    {
        private readonly ICoreClientAPI capi;
        private readonly HashSet<BlockPos> trackedConflicts = new HashSet<BlockPos>();

        private static readonly SimpleParticleProperties SmokePuff = new SimpleParticleProperties(
            minQuantity: 3, maxQuantity: 5,
            color: ColorUtil.ToRgba(180, 100, 100, 100),
            minPos: new Vec3d(), maxPos: new Vec3d(1, 1, 1),
            minVelocity: new Vec3f(-0.05f, 0.1f, -0.05f),
            maxVelocity: new Vec3f(0.05f, 0.2f, 0.05f),
            lifeLength: 1.5f, gravityEffect: -0.05f,
            minSize: 0.2f, maxSize: 0.4f
        );

        public ConflictParticleManager(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        public void Update(IEnumerable<BlockPos> conflictPositions)
        {
            trackedConflicts.Clear();
            foreach (var p in conflictPositions) trackedConflicts.Add(p);
        }

        public void EmitOnTick()
        {
            foreach (var pos in trackedConflicts)
            {
                SmokePuff.MinPos.Set(pos.X + 0.3, pos.Y + 0.5, pos.Z + 0.3);
                SmokePuff.AddPos.Set(0.4, 0.4, 0.4);
                capi.World.SpawnParticles(SmokePuff);
            }
        }
    }
}
