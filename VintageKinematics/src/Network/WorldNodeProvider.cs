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
                    // If this BE is a multiblock controller whose own cell isn't designated as
                    // a shaft cell, demote it to Role.Custom so the default coaxial rule short-
                    // circuits. Internal connectivity to the designated shaft fillers is handled
                    // by the intra-multiblock edge in TryGetCustomConnection. Stress and other
                    // node attributes are unaffected — only edge-formation cares about Role here.
                    IKineticMultiblockController mbcSelf = ResolveMultiblockInterface(be);
                    if (mbcSelf != null && !mbcSelf.IsKineticShaftCell(pos))
                    {
                        node.Role = EnumKineticRole.Custom;
                    }
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
            //
            // Designated shaft cells get Role.Shaft so the default coaxial rule forms external
            // edges to matching-axis sources. Non-shaft cells get Role.Custom so the default
            // short-circuits — they remain reachable internally through the intra-multiblock
            // edge in TryGetCustomConnection, but external blocks can't connect to them.
            // Multiblock controllers without IKineticMultiblockController fall back to "every
            // cell is a shaft cell", which preserves the existing 1×N sieve behaviour.
            if (world.BlockAccessor.GetBlock(pos) is BlockMultiblock mb)
            {
                BlockPos ctrlPos = new BlockPos(pos.X + mb.OffsetInv.X, pos.Y + mb.OffsetInv.Y, pos.Z + mb.OffsetInv.Z, pos.dimension);
                BlockEntity ctrlBe = world.BlockAccessor.GetBlockEntity(ctrlPos);
                BEBehaviorKinetic ctrlKin = ctrlBe?.GetBehavior<BEBehaviorKinetic>();
                if (ctrlKin == null) return false;

                IKineticMultiblockController mbc = ResolveMultiblockInterface(ctrlBe);
                bool isShaftCell = mbc != null ? mbc.IsKineticShaftCell(pos) : true;

                node = new KineticNode
                {
                    Pos = pos,
                    Axis = ctrlKin.Axis,
                    Role = isShaftCell ? EnumKineticRole.Shaft : EnumKineticRole.Custom,
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
            // Intra-multiblock edge: any two cells belonging to the same multiblock controller
            // are a single rigid body, so the network treats them as freely connected regardless
            // of axis alignment or offset direction. Without this, a non-linear multiblock (e.g.
            // a 3x3x2 bore with controller in a corner and shaft cells in the middle column) has
            // its controller islanded from the shaft cells because the default coaxial rule only
            // links same-axis fillers along the rotation axis.
            BlockPos fromCtrl = ResolveMultiblockController(from.Pos);
            BlockPos toCtrl = ResolveMultiblockController(to.Pos);
            if (fromCtrl != null && toCtrl != null && fromCtrl.Equals(toCtrl))
            {
                return new KineticConnection(1f, 1, 0f);
            }
            if (fromCtrl != null && fromCtrl.Equals(to.Pos))
            {
                return new KineticConnection(1f, 1, 0f);
            }
            if (toCtrl != null && toCtrl.Equals(from.Pos))
            {
                return new KineticConnection(1f, 1, 0f);
            }

            // Vanilla MP ↔ VK shaft bridge: coaxial face-neighbour with matching axis. Stays
            // one-edge — never bridges two vanilla blocks against each other (those should
            // remain on the vanilla MP graph). Returns null when neither end is a bridge so
            // the regular IKineticConnector dispatch below still gets a chance.
            bool fromVanilla = VanillaMPBridge.IsVanillaMP(world, from.Pos);
            bool toVanilla = VanillaMPBridge.IsVanillaMP(world, to.Pos);
            if (fromVanilla && toVanilla) return null;
            if (fromVanilla || toVanilla)
            {
                // The bridge has no Role check by default. Refuse to bridge into a non-vanilla
                // Custom-role neighbour: that's either a multiblock internal filler (must stay
                // internal-only) or a self-routing block like a belt segment (handles its own
                // connectivity through IKineticConnector).
                if (!fromVanilla && from.Role == EnumKineticRole.Custom) return null;
                if (!toVanilla && to.Role == EnumKineticRole.Custom) return null;
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

        private static IKineticMultiblockController ResolveMultiblockInterface(BlockEntity be)
        {
            if (be == null) return null;
            if (be is IKineticMultiblockController direct) return direct;
            foreach (var b in be.Behaviors)
            {
                if (b is IKineticMultiblockController viaBehavior) return viaBehavior;
            }
            return null;
        }

        private BlockPos ResolveMultiblockController(BlockPos pos)
        {
            if (world.BlockAccessor.GetBlock(pos) is BlockMultiblock mb)
            {
                return new BlockPos(pos.X + mb.OffsetInv.X, pos.Y + mb.OffsetInv.Y, pos.Z + mb.OffsetInv.Z, pos.dimension);
            }
            return null;
        }

        private static KineticNodeInfo ToInfo(KineticNode n, Block block) =>
            new KineticNodeInfo(n.Pos, n.Axis, n.Role, n.Ratio, n.Direction, block?.Code?.Path);

        private static KineticConnection Translate(KineticConnectionResult r) =>
            new KineticConnection(r.Ratio, r.Direction, r.PhaseOffset);
    }
}
