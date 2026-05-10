using System;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Network
{
    public static class KineticConnections
    {
        // Tooth counts on the cogwheel models. The half-tooth phase offsets used to
        // mesh adjacent cogs are derived from these. Update if the model changes.
        private const int SmallCogTeeth = 10;
        private const int LargeCogTeeth = 16;

        public static KineticConnection? GetConnection(KineticNode from, KineticNode to)
        {
            // Gearboxes and Custom-role nodes own their connection rules entirely — defer
            // to their IKineticConnector logic. Without this, a belt segment (Custom) would
            // be auto-coupled to any same-axis shaft on its pulley faces by Case 1 below.
            if (from.Role == EnumKineticRole.Gearbox || to.Role == EnumKineticRole.Gearbox) return null;
            if (from.Role == EnumKineticRole.Custom  || to.Role == EnumKineticRole.Custom)  return null;

            Vec3i offset = new Vec3i(to.Pos.X - from.Pos.X, to.Pos.Y - from.Pos.Y, to.Pos.Z - from.Pos.Z);
            int absSum = Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z);

            // Case 1: Coaxial (face neighbor, same axis)
            if (absSum == 1 && from.Axis == to.Axis)
            {
                Vec3i axisVec = EnumKineticAxisExtensions.UnitVector(from.Axis);
                if (IsAlongAxis(offset, axisVec))
                {
                    return new KineticConnection(1f, 1);
                }
            }

            // Case 2: Same-axis adjacent small-cog mesh.
            // Two small cogs sharing a rotation axis, placed face-adjacent in any direction
            // perpendicular to that axis, engage their teeth and reverse rotation. Restricted
            // to small+small per Create — large+large at this offset would clash (their tooth
            // circles overlap heavily) and is rejected by the placement validator.
            // Phase offset = half a tooth pitch (π / teeth) so a tooth on one falls in a gap on the other.
            if (absSum == 1
             && from.Role == EnumKineticRole.SmallCogwheel
             && to.Role == EnumKineticRole.SmallCogwheel
             && from.Axis == to.Axis)
            {
                EnumKineticAxis offsetAxis = EnumKineticAxisExtensions.FromVec(offset);
                if (offsetAxis != from.Axis)
                {
                    return new KineticConnection(1f, -1, MathF.PI / SmallCogTeeth);
                }
            }

            // Case 3: Small↔large diagonal corner mesh (parallel cogs).
            // Per Create's RotationPropagator.isLargeToSmallCog: both cogs share the same
            // rotation axis and sit diagonally adjacent in the plane perpendicular to that
            // axis. Their tooth circles intersect at the corner where their cells meet
            // (small radius + large radius > sqrt(2)), engaging teeth at a 2:1 ratio and
            // reversing direction.
            if (absSum == 2
             && IsSmallLargePair(from.Role, to.Role)
             && from.Axis == to.Axis)
            {
                EnumKineticAxis axis = from.Axis;
                bool axisOffsetZero = (axis == EnumKineticAxis.X && offset.X == 0)
                                   || (axis == EnumKineticAxis.Y && offset.Y == 0)
                                   || (axis == EnumKineticAxis.Z && offset.Z == 0);
                if (axisOffsetZero)
                {
                    // Connection.PhaseOffset is applied to the `to` node, so the half-tooth
                    // value depends on which cog is the destination.
                    bool fromIsLarge = from.Role == EnumKineticRole.LargeCogwheel;
                    if (fromIsLarge) return new KineticConnection(2f, -1, MathF.PI / SmallCogTeeth);
                    return new KineticConnection(0.5f, -1, MathF.PI / LargeCogTeeth);
                }
            }

            return null;
        }

        private static bool IsSmallLargePair(EnumKineticRole a, EnumKineticRole b)
        {
            return (a == EnumKineticRole.SmallCogwheel && b == EnumKineticRole.LargeCogwheel)
                || (a == EnumKineticRole.LargeCogwheel && b == EnumKineticRole.SmallCogwheel);
        }

        private static bool IsAlongAxis(Vec3i offset, Vec3i axisVec)
        {
            return Math.Abs(offset.X) == Math.Abs(axisVec.X)
                && Math.Abs(offset.Y) == Math.Abs(axisVec.Y)
                && Math.Abs(offset.Z) == Math.Abs(axisVec.Z);
        }
    }
}
