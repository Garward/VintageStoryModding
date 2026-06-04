using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    public static class AutomationClaimUtil
    {
        public static bool CanAutomatedBlockAccess(IWorldAccessor world, BlockPos actorPos, BlockPos targetPos, EnumBlockAccessFlags accessFlag)
        {
            if (world?.Claims == null || targetPos == null) return true;

            LandClaim[] targetClaims = world.Claims.Get(targetPos);
            if (targetClaims == null || targetClaims.Length == 0) return true;

            for (int i = 0; i < targetClaims.Length; i++)
            {
                LandClaim claim = targetClaims[i];
                if (claim == null || !claim.PositionInside(targetPos)) continue;
                if (accessFlag == EnumBlockAccessFlags.Use && claim.AllowUseEveryone) return true;
                if (actorPos != null && claim.PositionInside(actorPos)) return true;
            }

            return false;
        }

        public static bool CanOwnerAccessClaim(IWorldAccessor world, BlockPos targetPos, string ownerPlayerUid, IPlayer ownerPlayer, EnumBlockAccessFlags accessFlag)
        {
            if (world?.Claims == null || targetPos == null) return true;

            LandClaim[] claims = world.Claims.Get(targetPos);
            if (claims == null || claims.Length == 0) return true;

            for (int i = 0; i < claims.Length; i++)
            {
                LandClaim claim = claims[i];
                if (claim == null || !claim.PositionInside(targetPos)) continue;
                if (accessFlag == EnumBlockAccessFlags.Use && claim.AllowUseEveryone) continue;
                if (IsClaimOwnedByOrAccessibleTo(claim, ownerPlayerUid, ownerPlayer, accessFlag)) continue;
                return false;
            }

            return true;
        }

        public static string GetClaimOwnerUidAt(IWorldAccessor world, BlockPos pos)
        {
            LandClaim claim = GetFirstClaimAt(world, pos);
            return claim?.OwnedByPlayerUid;
        }

        public static string GetClaimOwnerNameAt(IWorldAccessor world, BlockPos pos)
        {
            LandClaim claim = GetFirstClaimAt(world, pos);
            return claim?.LastKnownOwnerName;
        }

        private static LandClaim GetFirstClaimAt(IWorldAccessor world, BlockPos pos)
        {
            if (world?.Claims == null || pos == null) return null;

            LandClaim[] claims = world.Claims.Get(pos);
            if (claims == null || claims.Length == 0) return null;

            for (int i = 0; i < claims.Length; i++)
            {
                LandClaim claim = claims[i];
                if (claim != null && claim.PositionInside(pos)) return claim;
            }

            return null;
        }

        private static bool IsClaimOwnedByOrAccessibleTo(LandClaim claim, string ownerPlayerUid, IPlayer ownerPlayer, EnumBlockAccessFlags accessFlag)
        {
            if (claim == null) return true;

            if (!string.IsNullOrEmpty(ownerPlayerUid)
                && !string.IsNullOrEmpty(claim.OwnedByPlayerUid)
                && string.Equals(claim.OwnedByPlayerUid, ownerPlayerUid, StringComparison.Ordinal))
            {
                return true;
            }

            return ownerPlayer != null && claim.TestPlayerAccess(ownerPlayer, accessFlag) != EnumPlayerAccessResult.Denied;
        }
    }
}
