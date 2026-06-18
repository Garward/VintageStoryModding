using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    public readonly struct CogPlacement
    {
        public readonly BlockPos TargetPos;
        public readonly EnumKineticAxis Axis;
        public readonly bool Redirected;

        public CogPlacement(BlockPos targetPos, EnumKineticAxis axis, bool redirected)
        {
            TargetPos = targetPos;
            Axis = axis;
            Redirected = redirected;
        }
    }

    // Single source of truth for "given a player's blockSel and what cog they're holding, where
    // should the cog actually go and which axis variant should it be?" Used by both the runtime
    // placement (BlockKineticCogwheel.TryPlaceBlock) and the client-side ghost preview renderer
    // so what the player sees is always what they get. Mirrors Create's CogwheelBlockItem +
    // DiagonalCogHelper logic, adapted for VS BlockSelection conventions.
    public static class CogPlacementResolver
    {
        // Broad-face mixed-size cog placement should require an intentional corner/quadrant click.
        // Without this, tiny aim drift around 0.5 redirects to a diagonal cog position.
        private const double DiagonalHitDeadzone = 0.18;

        // VS pre-applies face offset before TryPlaceBlock (DidOffset=true). The originally-clicked
        // cell is one step back along -face from blockSel.Position.
        public static BlockPos ClickedPos(BlockSelection sel)
        {
            if (sel?.Face == null || sel.Position == null) return null;
            if (!sel.DidOffset) return sel.Position;
            Vec3i n = sel.Face.Normali;
            return new BlockPos(sel.Position.X - n.X, sel.Position.Y - n.Y, sel.Position.Z - n.Z, sel.Position.dimension);
        }

        // Returns the resolved (target, axis) the cog should be placed at. Falls back to
        // (default target, face axis) if nothing kinetic is nearby.
        public static CogPlacement Resolve(IWorldAccessor world, BlockSelection sel, Block held, EnumKineticRole myRole)
        {
            if (sel == null) return new CogPlacement(null, EnumKineticAxis.Y, redirected: false);

            BlockPos defaultTarget = PlacementPreview.DefaultTargetPos(world, sel, held);
            EnumKineticAxis fallbackAxis = FaceToAxis(sel.Face);

            if (sel.Face == null || sel.HitPosition == null)
            {
                return new CogPlacement(defaultTarget, fallbackAxis, redirected: false);
            }

            BlockPos clicked = ClickedPos(sel);
            BEBehaviorKinetic clickedKin = GetKinetic(world, clicked);

            // Case A: no kinetic block clicked — default placement, fallback axis. Do not scan
            // nearby kinetics here: dense cog builds often have several valid neighbouring axes,
            // and choosing the first one by offset order makes placement feel random. Click a
            // kinetic block directly when the cog should inherit that block's axis.
            if (clickedKin == null)
            {
                return new CogPlacement(defaultTarget, fallbackAxis, redirected: false);
            }

            // The new cog's axis always inherits from the clicked cog/shaft. Same convention
            // Create uses — clicking a kinetic block "joins the shaft" rather than picking a
            // perpendicular axis from the face.
            EnumKineticAxis axis = clickedKin.Axis;

            // Case B: clicked something that isn't a cog (shaft, gearbox, etc.). Default position
            // with inherited axis — except gearboxes, whose stored Axis is the CLOSED axis
            // (no rotation axis exists for the whole block). Use the click face axis instead,
            // which is the port direction at that face — the only axis the cog can mesh with.
            bool clickedIsCog = IsCogRole(clickedKin.Role);
            if (!clickedIsCog)
            {
                EnumKineticAxis chosenAxis = clickedKin.Role == EnumKineticRole.Gearbox
                    ? FaceToAxis(sel.Face)
                    : axis;
                return new CogPlacement(defaultTarget, chosenAxis, redirected: false);
            }

            // Case C: clicked a cog of the SAME role. No corner redirect — face-adjacent placement
            // is the right outcome. Either coaxial (face along axis) or same-axis face mesh
            // (face perpendicular to axis).
            if (IsSameCogSize(clickedKin.Role, myRole))
            {
                return new CogPlacement(defaultTarget, axis, redirected: false);
            }

            // Case D: clicked a cog of the OPPOSITE size — corner redirect candidate.
            // Corner mesh forms diagonally in the plane perpendicular to the rotation axis.
            // If the clicked face is a side face, use that face as the first plane direction
            // and the hit position for the other. If the clicked face is along the cog axis
            // (common on vertical cogs now that the shaft mesh is not selectable), infer both
            // perpendicular directions from the hit position on the broad cog face.
            Vec3i faceN = sel.Face.Normali;
            Vec3d hit = sel.HitPosition;
            bool facePerpToAxis = IsFacePerpToAxis(sel.Face, axis);

            int sx = 0, sy = 0, sz = 0;
            switch (axis)
            {
                case EnumKineticAxis.X:
                    if (facePerpToAxis)
                    {
                        if (faceN.Y != 0)
                        {
                            if (!TryHitSign(hit.Z, out sz)) return new CogPlacement(defaultTarget, axis, redirected: false);
                        }
                        else if (!TryHitSign(hit.Y, out sy)) return new CogPlacement(defaultTarget, axis, redirected: false);
                    }
                    else
                    {
                        if (!TryHitSign(hit.Y, out sy) || !TryHitSign(hit.Z, out sz)) return new CogPlacement(defaultTarget, axis, redirected: false);
                    }
                    break;
                case EnumKineticAxis.Y:
                    if (facePerpToAxis)
                    {
                        if (faceN.X != 0)
                        {
                            if (!TryHitSign(hit.Z, out sz)) return new CogPlacement(defaultTarget, axis, redirected: false);
                        }
                        else if (!TryHitSign(hit.X, out sx)) return new CogPlacement(defaultTarget, axis, redirected: false);
                    }
                    else
                    {
                        if (!TryHitSign(hit.X, out sx) || !TryHitSign(hit.Z, out sz)) return new CogPlacement(defaultTarget, axis, redirected: false);
                    }
                    break;
                case EnumKineticAxis.Z:
                    if (facePerpToAxis)
                    {
                        if (faceN.X != 0)
                        {
                            if (!TryHitSign(hit.Y, out sy)) return new CogPlacement(defaultTarget, axis, redirected: false);
                        }
                        else if (!TryHitSign(hit.X, out sx)) return new CogPlacement(defaultTarget, axis, redirected: false);
                    }
                    else
                    {
                        if (!TryHitSign(hit.X, out sx) || !TryHitSign(hit.Y, out sy)) return new CogPlacement(defaultTarget, axis, redirected: false);
                    }
                    break;
            }

            BlockPos cornerPos = new BlockPos(
                clicked.X + (facePerpToAxis ? faceN.X : 0) + sx,
                clicked.Y + (facePerpToAxis ? faceN.Y : 0) + sy,
                clicked.Z + (facePerpToAxis ? faceN.Z : 0) + sz,
                clicked.dimension);

            Block atCorner = world.BlockAccessor.GetBlock(cornerPos);
            if (atCorner != null && atCorner.Id != 0 && !atCorner.IsReplacableBy(held))
            {
                // Corner blocked — fall back to face-adjacent placement (coaxial-ish, won't mesh
                // as a corner pair, but at least the click does something).
                return new CogPlacement(defaultTarget, axis, redirected: false);
            }

            return new CogPlacement(cornerPos, axis, redirected: true);
        }

        private static bool TryHitSign(double value, out int sign)
        {
            sign = 0;
            double delta = value - 0.5;
            if (System.Math.Abs(delta) < DiagonalHitDeadzone) return false;
            sign = delta > 0 ? 1 : -1;
            return true;
        }

        private static BEBehaviorKinetic GetKinetic(IWorldAccessor world, BlockPos pos)
        {
            if (pos == null) return null;
            BlockEntity be = world.BlockAccessor.GetBlockEntity(pos);
            return be?.GetBehavior<BEBehaviorKinetic>();
        }

        private static EnumKineticAxis FaceToAxis(BlockFacing face)
        {
            if (face == null) return EnumKineticAxis.Y;
            return face.Axis switch
            {
                EnumAxis.X => EnumKineticAxis.X,
                EnumAxis.Y => EnumKineticAxis.Y,
                EnumAxis.Z => EnumKineticAxis.Z,
                _ => EnumKineticAxis.Y
            };
        }

        private static bool IsFacePerpToAxis(BlockFacing face, EnumKineticAxis axis)
        {
            EnumAxis fa = face.Axis;
            return (axis == EnumKineticAxis.X && fa != EnumAxis.X)
                || (axis == EnumKineticAxis.Y && fa != EnumAxis.Y)
                || (axis == EnumKineticAxis.Z && fa != EnumAxis.Z);
        }

        public static string AxisToVariant(EnumKineticAxis axis) => axis switch
        {
            EnumKineticAxis.X => "x",
            EnumKineticAxis.Y => "y",
            EnumKineticAxis.Z => "z",
            _ => "y"
        };

        // Placement should not be stricter than the runtime graph. Dense layouts can have cogs
        // physically near each other without those two cogs being the intended connection, and
        // the wrench can already rotate blocks into those states. Occupancy/collision is handled
        // by CanPlaceBlock; connection validity/conflict is handled by the kinetic network.
        public static bool IsValidCogPlacement(IWorldAccessor world, BlockPos pos, EnumKineticAxis cogAxis, EnumKineticRole cogRole)
        {
            return true;
        }

        private static bool IsCogRole(EnumKineticRole role)
        {
            return role == EnumKineticRole.SmallCogwheel
                || role == EnumKineticRole.LargeCogwheel
                || role == EnumKineticRole.EncasedSmallCogwheel
                || role == EnumKineticRole.EncasedLargeCogwheel;
        }

        private static bool IsSameCogSize(EnumKineticRole left, EnumKineticRole right)
        {
            return (IsSmallCogRole(left) && IsSmallCogRole(right))
                || (IsLargeCogRole(left) && IsLargeCogRole(right));
        }

        private static bool IsSmallCogRole(EnumKineticRole role)
        {
            return role == EnumKineticRole.SmallCogwheel || role == EnumKineticRole.EncasedSmallCogwheel;
        }

        private static bool IsLargeCogRole(EnumKineticRole role)
        {
            return role == EnumKineticRole.LargeCogwheel || role == EnumKineticRole.EncasedLargeCogwheel;
        }
    }
}
