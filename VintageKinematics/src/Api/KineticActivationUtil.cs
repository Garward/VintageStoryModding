using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VintageKinematics.Api
{
    public static class KineticActivationUtil
    {
        public static bool TryActivateTarget(
            ICoreAPI api,
            BlockPos activatorPos,
            BlockFacing front,
            float signedRPM,
            bool allowFallbackActivate,
            bool useActivatorBlacklist,
            out string status)
        {
            status = "not attempted";
            if (api?.World == null || activatorPos == null || front == null)
            {
                status = "missing activation context";
                return false;
            }

            BlockPos targetPos = activatorPos.AddCopy(front);
            Block targetBlock = api.World.BlockAccessor.GetBlock(targetPos);
            if (targetBlock == null || targetBlock.Id == 0)
            {
                status = $"no target at {targetPos}";
                return false;
            }

            BlockFacing activatedFace = front.Opposite;
            BlockEntity targetBe = api.World.BlockAccessor.GetBlockEntity(targetPos);
            BlockEntity activatableBe = MultiblockHelper.GetMultiblockAwareBE(api.World, targetPos) ?? targetBe;
            Block activatableBlock = activatableBe?.Block ?? targetBlock;

            if (useActivatorBlacklist
                && (IsBlacklistedTarget(api, targetBlock, targetBe) || IsBlacklistedTarget(api, activatableBlock, activatableBe)))
            {
                status = $"target blacklisted: {targetBlock.Code}";
                return false;
            }

            if (!AutomationClaimUtil.CanAutomatedBlockAccess(api.World, activatorPos, targetPos, EnumBlockAccessFlags.Use))
            {
                status = $"claim denied at {targetPos}";
                return false;
            }

            bool handledByActivatorApi = false;

            if (activatableBe is IKineticActivatable beTarget)
            {
                handledByActivatorApi = true;
                if (beTarget.OnKineticActivate(api.World, targetPos, activatedFace, activatorPos, signedRPM))
                {
                    status = $"activated BE {activatableBe.GetType().Name} at {targetPos}";
                    return true;
                }
            }

            if (!handledByActivatorApi && activatableBlock is IKineticActivatable blockTarget)
            {
                handledByActivatorApi = true;
                if (blockTarget.OnKineticActivate(api.World, targetPos, activatedFace, activatorPos, signedRPM))
                {
                    status = $"activated block {activatableBlock.Code}";
                    return true;
                }
            }

            if (handledByActivatorApi)
            {
                status = $"target rejected kinetic activation: {targetBlock.Code}";
                return false;
            }

            if (targetBe is BlockEntityBarrel barrel)
            {
                bool sealedBarrel = TrySealBarrel(barrel);
                status = sealedBarrel ? "sealed barrel" : "barrel could not seal";
                return sealedBarrel;
            }

            if (!allowFallbackActivate)
            {
                status = $"target is not kinetic activatable: {targetBlock.Code}";
                return false;
            }

            try
            {
                Caller caller = new Caller
                {
                    Pos = activatorPos.ToVec3d(),
                    Type = EnumCallerType.Block
                };
                targetBlock.Activate(api.World, caller, new BlockSelection(targetPos, activatedFace, targetBlock));
                status = $"fallback activated {targetBlock.Code}";
                return true;
            }
            catch (Exception e)
            {
                api.Logger.Warning("[VintageKinematics] Kinetic activation failed for {0} at {1}: {2}", targetBlock.Code, targetPos, e.Message);
                status = $"activation exception: {e.Message}";
                return false;
            }
        }

        private static bool TrySealBarrel(BlockEntityBarrel barrel)
        {
            if (barrel == null || barrel.Sealed) return false;
            if (!barrel.GetCanSeal(null)) return false;

            barrel.SealBarrel();
            return true;
        }

        private static bool IsBlacklistedTarget(ICoreAPI api, Block block, BlockEntity blockEntity)
        {
            VintageKinematicsConfig cfg = api.ModLoader.GetModSystem<KineticConfigSystem>()?.Config;
            return cfg != null && cfg.IsKineticActivatorTargetBlacklisted(block, blockEntity);
        }
    }
}
