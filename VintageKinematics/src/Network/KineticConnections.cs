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
        private const int LargeCogTeeth = 20;
        private const float LargeCogGapBias = 1.35f;

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
            if (absSum == 1
             && from.Axis == to.Axis
             && AllowsCoaxialShaftConnection(from, offset)
             && AllowsCoaxialShaftConnection(to, new Vec3i(-offset.X, -offset.Y, -offset.Z)))
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
             && IsSmallCogRole(from.Role)
             && IsSmallCogRole(to.Role)
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
                    float phaseOffset = MixedCogPhaseOffset(from, to, offset);
                    if (IsLargeCogRole(from.Role)) return new KineticConnection(2f, -1, phaseOffset);
                    return new KineticConnection(0.5f, -1, phaseOffset);
                }
            }

            return null;
        }

        private static bool IsSmallLargePair(EnumKineticRole a, EnumKineticRole b)
        {
            return (IsSmallCogRole(a) && IsLargeCogRole(b))
                || (IsLargeCogRole(a) && IsSmallCogRole(b));
        }

        private static bool IsSmallCogRole(EnumKineticRole role)
        {
            return role == EnumKineticRole.SmallCogwheel || role == EnumKineticRole.EncasedSmallCogwheel;
        }

        private static bool IsLargeCogRole(EnumKineticRole role)
        {
            return role == EnumKineticRole.LargeCogwheel || role == EnumKineticRole.EncasedLargeCogwheel;
        }

        private static float MixedCogPhaseOffset(KineticNode from, KineticNode to, Vec3i offsetFromTo)
        {
            Vec3i toFrom = new Vec3i(-offsetFromTo.X, -offsetFromTo.Y, -offsetFromTo.Z);
            return DesiredMixedCogPhase(to, toFrom) - DesiredMixedCogPhase(from, offsetFromTo);
        }

        private static float DesiredMixedCogPhase(KineticNode node, Vec3i vectorToOther)
        {
            float contactAngle = ContactAngle(node.Axis, vectorToOther);
            if (IsLargeCogRole(node.Role))
            {
                // Large cog teeth are twice as dense. Put the contacted point in the center
                // of a large-cog gap so the small cog tooth does not render tooth-on-tooth.
                // The model's cuboid teeth read slightly over-centered at the mathematical
                // half-tooth pitch, so keep a small visual bias for cleaner idle alignment.
                return contactAngle - ((MathF.PI / LargeCogTeeth) * LargeCogGapBias);
            }
            return contactAngle;
        }

        private static float ContactAngle(EnumKineticAxis axis, Vec3i vector)
        {
            return axis switch
            {
                // Base Z-axis cog shape has tooth_000 pointing toward -Y.
                EnumKineticAxis.Z => MathF.Atan2(vector.X, -vector.Y),
                // X-axis variants keep tooth_000 on -Y; positive spin follows the Z side
                // of the Y/Z plane after the block rotation.
                EnumKineticAxis.X => MathF.Atan2(vector.Z, -vector.Y),
                // Y-axis variants rotate the base shape so tooth_000 points toward +Z.
                EnumKineticAxis.Y => MathF.Atan2(vector.X, vector.Z),
                _ => 0f
            };
        }

        private static bool AllowsCoaxialShaftConnection(KineticNode node, Vec3i offsetFromNode)
        {
            if (IsStationaryContraptionTool(node.BlockCode))
            {
                return IsRearToolConnection(node.BlockCode, offsetFromNode);
            }

            if (node.Role != EnumKineticRole.EncasedSmallCogwheel && node.Role != EnumKineticRole.EncasedLargeCogwheel)
            {
                return true;
            }

            string path = node.BlockCode ?? "";
            bool hasNegPort = path.Contains("-neg-");
            bool hasPosPort = path.Contains("-pos-");
            if (!hasNegPort && !hasPosPort) return false;

            Vec3i axisVec = EnumKineticAxisExtensions.UnitVector(node.Axis);
            if (!IsAlongAxis(offsetFromNode, axisVec)) return false;

            int sign = 0;
            if (axisVec.X != 0) sign = Math.Sign(offsetFromNode.X);
            else if (axisVec.Y != 0) sign = Math.Sign(offsetFromNode.Y);
            else if (axisVec.Z != 0) sign = Math.Sign(offsetFromNode.Z);

            return (sign < 0 && hasNegPort) || (sign > 0 && hasPosPort);
        }

        private static bool IsStationaryContraptionTool(string path)
        {
            return path?.StartsWith("contraptiondrill-", StringComparison.Ordinal) == true
                || path?.StartsWith("contraptionsaw-", StringComparison.Ordinal) == true;
        }

        private static bool IsRearToolConnection(string path, Vec3i offset)
        {
            if (path.EndsWith("-n", StringComparison.Ordinal)) return offset.Z > 0;
            if (path.EndsWith("-e", StringComparison.Ordinal)) return offset.X < 0;
            if (path.EndsWith("-s", StringComparison.Ordinal)) return offset.Z < 0;
            if (path.EndsWith("-w", StringComparison.Ordinal)) return offset.X > 0;
            if (path.EndsWith("-u", StringComparison.Ordinal)) return offset.Y < 0;
            if (path.EndsWith("-d", StringComparison.Ordinal)) return offset.Y > 0;
            return false;
        }

        private static bool IsAlongAxis(Vec3i offset, Vec3i axisVec)
        {
            return Math.Abs(offset.X) == Math.Abs(axisVec.X)
                && Math.Abs(offset.Y) == Math.Abs(axisVec.Y)
                && Math.Abs(offset.Z) == Math.Abs(axisVec.Z);
        }
    }
}
