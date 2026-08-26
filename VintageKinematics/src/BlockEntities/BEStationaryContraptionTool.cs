using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage;

namespace VintageKinematics.BlockEntities
{
    public sealed partial class BEStationaryContraptionTool : BlockEntity
    {
        private const int WorkIntervalMs = 100;
        private const long EffectIntervalMs = 220;

        private string activeTargetKey;
        private float progress;
        private long lastEffectMs;
        private List<BlockPos> cachedSawTargets;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server) RegisterGameTickListener(OnWorkTick, WorkIntervalMs);
            if (api is Vintagestory.API.Client.ICoreClientAPI capi) InitializeRenderer(capi);
        }

        private void OnWorkTick(float dt)
        {
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            float rpm = kinetic?.ActualRPM ?? 0f;
            if (MathF.Abs(rpm) < 0.001f || kinetic.IsConflicted || (kinetic.Network?.IsOverstressed ?? false)) return;
            if (!ContraptionToolRules.TryGetFacing(Block, out BlockFacing facing)) return;

            string path = Block?.Code?.Path ?? "";
            if (path.StartsWith("contraptiondrill-", StringComparison.Ordinal))
            {
                WorkDrill(rpm, dt, facing);
            }
            else if (path.StartsWith("contraptionsaw-", StringComparison.Ordinal))
            {
                WorkSaw(rpm, dt, facing);
            }
        }

        private void WorkDrill(float rpm, float dt, BlockFacing facing)
        {
            BlockPos targetPos = Pos.AddCopy(facing);
            Block target = Api.World.BlockAccessor.GetBlock(targetPos);
            if (!ContraptionToolRules.CanDrillBreak(Api.World, target, targetPos, CanBreakAt))
            {
                ResetWork();
                return;
            }

            string key = TargetKey("drill", targetPos, target);
            float required = ContraptionToolRules.DrillRequiredWork(target);
            float amount = ContraptionToolRules.DrillWorkAmount(rpm, dt, required);
            if (!Advance(key, amount, required, targetPos, target, facing.Opposite, EnumTool.Drill)) return;

            BreakToOutput(targetPos, target);
            ResetWork();
        }

        private void WorkSaw(float rpm, float dt, BlockFacing facing)
        {
            BlockPos front = Pos.AddCopy(facing);
            string frontKey = TargetKey("sawfront", front, Api.World.BlockAccessor.GetBlock(front));
            if (cachedSawTargets == null || !string.Equals(activeTargetKey, frontKey, StringComparison.Ordinal))
            {
                cachedSawTargets = ContraptionToolTreeSearch.Find(Api.World, front, CanBreakAt);
                activeTargetKey = frontKey;
                progress = 0f;
            }

            RemoveInvalidSawTargets();
            if (cachedSawTargets.Count == 0)
            {
                ResetWork();
                return;
            }

            BlockPos rootPos = cachedSawTargets[0];
            Block root = Api.World.BlockAccessor.GetBlock(rootPos);
            bool leafOnly = cachedSawTargets.Count == 1
                && ContraptionToolRules.CanSawLeaves(Api.World, root, rootPos, CanBreakAt);
            float required = leafOnly
                ? ContraptionToolRules.SawLeafRequiredWork(root)
                : ContraptionToolRules.SawRequiredWork(Api.World, cachedSawTargets);

            if (!Advance(frontKey, MathF.Abs(rpm) * dt, required, rootPos, root, facing.Opposite, EnumTool.Saw)) return;

            foreach (BlockPos pos in cachedSawTargets)
            {
                Block block = Api.World.BlockAccessor.GetBlock(pos);
                if (!CanSawBreak(block, pos)) continue;
                BreakToOutput(pos, block);
            }
            ResetWork();
        }

        private bool Advance(string key, float amount, float required, BlockPos effectPos, Block effectBlock, BlockFacing hitFace, EnumTool tool)
        {
            if (!string.Equals(activeTargetKey, key, StringComparison.Ordinal))
            {
                activeTargetKey = key;
                progress = 0f;
            }

            progress = MathF.Min(required, progress + MathF.Max(0f, amount));
            EmitEffects(effectPos, effectBlock, hitFace, tool);
            return progress >= required;
        }

        private void EmitEffects(BlockPos pos, Block block, BlockFacing hitFace, EnumTool tool)
        {
            long now = Api.World.ElapsedMilliseconds;
            if (now - lastEffectMs < EffectIntervalMs) return;
            lastEffectMs = now;

            Vec3d at = pos.ToVec3d().Add(0.5, 0.5, 0.5);
            at.Add(hitFace.Normali.X * 0.45, hitFace.Normali.Y * 0.45, hitFace.Normali.Z * 0.45);
            Api.World.SpawnCubeParticles(at, new ItemStack(block), 0.16f, 3, 0.4f);

            AssetLocation fallback = tool == EnumTool.Saw
                ? new AssetLocation("sounds/block/chop1")
                : new AssetLocation("sounds/block/rock-hit-pickaxe");
            Api.World.PlaySoundAt(fallback, at.X, at.Y, at.Z, null, true, 12f, 0.32f);
        }

        private void BreakToOutput(BlockPos pos, Block block)
        {
            if (!CanBreakAt(pos)) return;
            StorageRemovalCheck removal = KineticStorageRemovalService.Check(Api.World, pos, StorageRemovalKind.ContraptionCapture);
            if (!removal.Allowed) return;

            ItemStack[] drops = block.GetDrops(Api.World, pos, null, 1f);
            AutomationBlockMutation.RemoveAndNotify(Api.World, pos);

            if (drops == null) return;
            foreach (ItemStack drop in drops)
            {
                if (drop == null || drop.StackSize <= 0) continue;
                StationaryToolOutput.EmitBelow(this, drop);
            }
        }

        private void RemoveInvalidSawTargets()
        {
            cachedSawTargets.RemoveAll(pos => !CanSawBreak(Api.World.BlockAccessor.GetBlock(pos), pos));
        }

        private bool CanSawBreak(Block block, BlockPos pos)
        {
            return ContraptionToolRules.CanSawWood(Api.World, block, pos, CanBreakAt)
                || ContraptionToolRules.CanSawLeaves(Api.World, block, pos, CanBreakAt);
        }

        private bool CanBreakAt(BlockPos targetPos)
        {
            return AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, targetPos, EnumBlockAccessFlags.BuildOrBreak);
        }

        private void ResetWork()
        {
            activeTargetKey = null;
            progress = 0f;
            cachedSawTargets = null;
        }

        private static string TargetKey(string prefix, BlockPos pos, Block block)
        {
            return prefix + ":" + pos.dimension + ":" + pos.X + "," + pos.InternalY + "," + pos.Z + ":" + block?.Code;
        }
    }
}
