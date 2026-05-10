using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;

namespace VintageKinematics.Network
{
    public class WorldNodeProvider : INodeProvider
    {
        private readonly IWorldAccessor world;
        private readonly BlockPos excludePos;

        public WorldNodeProvider(IWorldAccessor world, BlockPos excludePos = null)
        {
            this.world = world;
            this.excludePos = excludePos;
        }

        public bool TryGetNode(BlockPos pos, out KineticNode node)
        {
            node = default;
            if (excludePos != null && pos.Equals(excludePos)) return false;

            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            if (be != null)
            {
                BEBehaviorKinetic beh = be.GetBehavior<BEBehaviorKinetic>();
                if (beh != null)
                {
                    node = beh.ToNode();
                    return true;
                }

                // Vanilla mechanical-power block (axle, angled gear, windmill rotor, etc.)
                // adjacent to a VK shaft. Synthesize a Custom-role source node so the BFS sees
                // it as an endpoint that can drive the network. Custom role + null custom
                // connection between two vanilla bridges (handled in TryGetCustomConnection)
                // prevents BFS from spilling into the vanilla MP graph.
                if (VanillaMPBridge.TryGetState(world, pos, out EnumKineticAxis vAxis, out float vRPM))
                {
                    node = new KineticNode
                    {
                        Pos = pos,
                        Axis = vAxis,
                        Role = EnumKineticRole.Custom,
                        StressImpact = VanillaMPBridge.StressImpact,
                        RatedRPM = MathF.Abs(vRPM),
                        Tier = null,
                        TierMaxRPM = 0f
                    };
                    return true;
                }

                return false;
            }

            // Multiblock filler cells have no BE of their own; they redirect to a controller
            // block. Treat the filler as a virtual coaxial shaft segment so kinetic networks
            // pass through the multiblock body — otherwise a long block (e.g. kinetic sieve drum)
            // would only accept input on the controller-side face, since both axial faces of
            // a 1×1×N kinetic body lie on filler cells. Ghost nodes report StressImpact=0 and
            // no Tier so they don't double-count toward stress or MaxRPM.
            if (world.BlockAccessor.GetBlock(pos) is BlockMultiblock mb)
            {
                BlockPos ctrlPos = new BlockPos(pos.X + mb.OffsetInv.X, pos.Y + mb.OffsetInv.Y, pos.Z + mb.OffsetInv.Z, pos.dimension);
                BlockEntity ctrlBe = world.BlockAccessor.GetBlockEntity(ctrlPos);
                BEBehaviorKinetic ctrlKin = ctrlBe?.GetBehavior<BEBehaviorKinetic>();
                if (ctrlKin == null) return false;

                node = new KineticNode
                {
                    Pos = pos,
                    Axis = ctrlKin.Axis,
                    Role = EnumKineticRole.Shaft,
                    StressImpact = 0f,
                    RatedRPM = 0f,
                    Tier = null,
                    TierMaxRPM = 0f
                };
                return true;
            }

            return false;
        }

        public KineticConnection? TryGetCustomConnection(KineticNode from, KineticNode to)
        {
            // Vanilla MP ↔ VK shaft bridge: coaxial face-neighbour with matching axis. Stays
            // one-edge — never bridges two vanilla blocks against each other (those should
            // remain on the vanilla MP graph). Returns null when neither end is a bridge so
            // the regular IKineticConnector dispatch below still gets a chance.
            bool fromVanilla = VanillaMPBridge.IsVanillaMP(world, from.Pos);
            bool toVanilla = VanillaMPBridge.IsVanillaMP(world, to.Pos);
            if (fromVanilla && toVanilla) return null;
            if (fromVanilla || toVanilla)
            {
                if (from.Axis != to.Axis) return null;
                Vec3i offset = new Vec3i(to.Pos.X - from.Pos.X, to.Pos.Y - from.Pos.Y, to.Pos.Z - from.Pos.Z);
                int absSum = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);
                if (absSum != 1) return null;
                Vec3i axisVec = EnumKineticAxisExtensions.UnitVector(from.Axis);
                if (Math.Abs(offset.X) != Math.Abs(axisVec.X)) return null;
                if (Math.Abs(offset.Y) != Math.Abs(axisVec.Y)) return null;
                if (Math.Abs(offset.Z) != Math.Abs(axisVec.Z)) return null;
                return new KineticConnection(1f, 1, 0f);
            }

            Block fromBlock = world.BlockAccessor.GetBlock(from.Pos);
            Block toBlock = world.BlockAccessor.GetBlock(to.Pos);
            var fromInfo = ToInfo(from, fromBlock);
            var toInfo   = ToInfo(to, toBlock);

            if (fromBlock is IKineticConnector connFrom)
            {
                var result = connFrom.TryConnect(fromInfo, toInfo, from.Pos, to.Pos);
                if (result.HasValue) return Translate(result.Value);
            }
            if (toBlock is IKineticConnector connTo)
            {
                var result = connTo.TryConnect(toInfo, fromInfo, to.Pos, from.Pos);
                if (result.HasValue) return Translate(result.Value);
            }
            return null;
        }

        private static KineticNodeInfo ToInfo(KineticNode n, Block block) =>
            new KineticNodeInfo(n.Pos, n.Axis, n.Role, n.Ratio, n.Direction, block?.Code?.Path);

        private static KineticConnection Translate(KineticConnectionResult r) =>
            new KineticConnection(r.Ratio, r.Direction, r.PhaseOffset);
    }
}
