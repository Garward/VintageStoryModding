using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Blocks;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public partial class BEBelt
    {
        /// <summary>Called by <see cref="BlockBelt.OnNeighbourBlockChange"/>.</summary>
        public void OnAxisNeighborChanged(BlockPos neighbor)
        {
            if (Api?.Side != EnumAppSide.Server) return;
            if (Direction == null) return;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);
            BlockPos forward = Pos.AddCopy(fwd.X, 0, fwd.Z);
            BlockPos backward = Pos.AddCopy(-fwd.X, 0, -fwd.Z);
            if (neighbor.Equals(forward) || neighbor.Equals(backward))
            {
                RebuildChain();
            }
        }

        private void NotifyBeltAt(BlockPos pos)
        {
            if (Api?.World?.BlockAccessor.GetBlockEntity(pos) is BEBelt belt)
            {
                belt.RebuildChain();
            }
        }

        public void RepairChainSoon()
        {
            if (Api?.Side != EnumAppSide.Server) return;
            RegisterDelayedCallback(_ => RebuildChain(), 0);
        }

        /// <summary>
        /// Walks backward to find the chain start, then forward to assign indices. Each segment
        /// writes its own state (controllerPos, index, chainLength, part). Side-effect: marks
        /// all visited segments dirty so the data syncs to clients.
        /// </summary>
        private void RebuildChain()
        {
            if (Direction == null) return;
            Vec3i fwd = BlockBelt.HeadOffset(Direction);

            // 1. Walk backward to find the start.
            BlockPos start = Pos.Copy();
            int safetyBack = MaxChainLength;
            while (safetyBack-- > 0)
            {
                BlockPos behind = start.AddCopy(-fwd.X, 0, -fwd.Z);
                if (Api.World.BlockAccessor.GetBlockEntity(behind) is BEBelt prev
                    && prev.Direction == Direction)
                {
                    start = behind;
                }
                else break;
            }

            // If our controller identity is about to change (different start), drop any
            // owned items at their current world position so nothing is silently lost.
            if (IsController && items.Count > 0 && !start.Equals(Pos))
            {
                DumpAllItems();
            }

            // 2. Walk forward from start, capping at MaxChainLength, assigning indices.
            BlockPos cursor = start.Copy();
            int idx = 0;
            BEBelt prevSeg = null;
            while (idx < MaxChainLength)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(cursor) is not BEBelt seg
                    || seg.Direction != Direction) break;

                seg.ControllerPos = start.Copy();
                seg.IndexInChain = idx;
                if (prevSeg != null) prevSeg.MarkDirty(true);
                prevSeg = seg;
                cursor = cursor.AddCopy(fwd.X, 0, fwd.Z);
                idx++;
            }
            int length = idx;

            // 3. Walk through again to write ChainLength + Part now that length is known.
            cursor = start.Copy();
            for (int i = 0; i < length; i++)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(cursor) is not BEBelt seg) break;
                seg.ChainLength = length;
                seg.Part = length == 1 ? EnumBeltPart.Solo
                          : i == 0 ? EnumBeltPart.Start
                          : i == length - 1 ? EnumBeltPart.End
                          : EnumBeltPart.Middle;
                seg.UpdateKineticState(triggerRebuild: true);
                seg.MarkDirty(true);
                cursor = cursor.AddCopy(fwd.X, 0, fwd.Z);
            }
        }

        /// <summary>
        /// Drives the kinetic node's <see cref="BEBehaviorKinetic.Role"/> and
        /// <see cref="BEBehaviorKinetic.Axis"/> from the segment's current Part / HasShaft.
        /// Endpoints (Solo/Start/End) and Middle+HasShaft expose a Shaft-role kinetic port so the
        /// network builder couples them to neighbouring shafts via the default coaxial rule;
        /// Middle without an inserted shaft is Custom (defers to <see cref="BlockBelt.TryConnect"/>)
        /// so an unrelated perpendicular shaft running past it doesn't bleed into the belt's network.
        /// When <paramref name="triggerRebuild"/> is true and the values changed, the network manager
        /// is asked to retopologize this position.
        /// </summary>
        private void UpdateKineticState(bool triggerRebuild)
        {
            if (string.IsNullOrEmpty(Direction)) return;
            var kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return;

            EnumKineticAxis pulleyAxis = BlockBelt.TravelAxis(Direction) == EnumKineticAxis.X
                ? EnumKineticAxis.Z
                : EnumKineticAxis.X;

            // Axis is always pulleyAxis: the visual shaft (whether the endpoint pulley axle or the
            // mid-chain inserted shaft via belt-shaft.json) is locked to that axis by the shape.
            // Driving kinetic.Axis with anything else would make the renderer spin the visual on
            // the wrong axis (the inserted shaft would rotate like a gear instead of rolling).
            EnumKineticAxis newAxis = pulleyAxis;
            EnumKineticRole newRole = (Part == EnumBeltPart.Middle && !HasShaft)
                ? EnumKineticRole.Custom
                : EnumKineticRole.Shaft;

            bool changed = kinetic.Role != newRole || kinetic.Axis != newAxis;
            kinetic.Role = newRole;
            kinetic.Axis = newAxis;

            if (changed && triggerRebuild && Api?.Side == EnumAppSide.Server)
            {
                var mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
                if (mgr != null)
                {
                    mgr.OnRemoved(Pos);
                    mgr.OnPlaced(Pos);
                }
            }
        }
    }
}
