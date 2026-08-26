using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage;

namespace VintageKinematics.Blocks
{
    public sealed class ContraptionToolWorkProvider : IContraptionWorkProvider
    {
        private const float DrillActiveStressImpact = 2.5f;
        private const float SawActiveStressImpact = 4f;
        private const long ToolParticleIntervalMs = 120;
        private const long ToolDecalIntervalMs = 90;
        private const long ToolSoundIntervalMs = 220;
        private const string ToolMovementPauseKey = "toolwork";
        private static readonly AssetLocation DrillFallbackSound = new AssetLocation("sounds/block/rock-hit-pickaxe");
        private static readonly AssetLocation SawFallbackSound = new AssetLocation("sounds/block/chop1");

        public float GetActiveStressImpact(ContraptionWorkContext context)
        {
            string path = context.ToolBlock?.Code?.Path;
            if (string.IsNullOrEmpty(path) || context.WorkRate <= 0f) return 0f;
            if (!TryGetToolFacing(context.ToolBlock, out BlockFacing facing)) return 0f;
            if (!IsMovingIntoFacing(context, facing)) return 0f;

            if (path.StartsWith("contraptiondrill-", StringComparison.Ordinal))
            {
                BlockPos targetPos = GetToolTargetPos(context, facing);
                if (context.Contraption.ContainsSnapshotWorldBlockPosition(targetPos)) return 0f;

                Block target = context.World.BlockAccessor.GetBlock(targetPos);
                return CanDrillBreak(context, target, targetPos) ? DrillActiveStressImpact : 0f;
            }

            if (path.StartsWith("contraptionsaw-", StringComparison.Ordinal))
            {
                BlockPos frontPos = GetToolTargetPos(context, facing);
                Block frontBlock = context.World.BlockAccessor.GetBlock(frontPos);
                if (IsSawBreakableLeaves(context, frontBlock, frontPos)) return SawActiveStressImpact;
                return IsSawBreakableWood(context, frontBlock, frontPos) ? SawActiveStressImpact : 0f;
            }

            return 0f;
        }

        public void DoContraptionWork(ContraptionWorkContext context)
        {
            string path = context.ToolBlock?.Code?.Path;
            if (string.IsNullOrEmpty(path) || context.WorkRate <= 0f) return;

            if (!TryGetToolFacing(context.ToolBlock, out BlockFacing facing)) return;
            if (!IsMovingIntoFacing(context, facing)) return;
            if (path.StartsWith("contraptiondrill-", StringComparison.Ordinal))
            {
                DoDrillWork(context, facing);
                return;
            }

            if (path.StartsWith("contraptionsaw-", StringComparison.Ordinal))
            {
                DoSawWork(context, facing);
            }
        }

        private static void DoDrillWork(ContraptionWorkContext context, BlockFacing facing)
        {
            BlockPos targetPos = GetToolTargetPos(context, facing);
            if (context.Contraption.ContainsSnapshotWorldBlockPosition(targetPos)) return;

            Block target = context.World.BlockAccessor.GetBlock(targetPos);
            if (!CanDrillBreak(context, target, targetPos)) return;

            string key = WorkKey("drill", targetPos, target);
            float required = ContraptionToolRules.DrillRequiredWork(target);
            float workAmount = ContraptionToolRules.DrillWorkAmount(context.Rpm, context.Dt, required);
            if (!context.AddProgress(key, workAmount, required, out float progress))
            {
                EmitToolWorkEffects(context, key, targetPos, target, facing.Opposite, EnumTool.Drill, progress, 0.35f);
                context.RequestMovementPause(ToolMovementPauseKey, 150, "Drilling " + target.Code);
                return;
            }

            EmitToolWorkEffects(context, key, targetPos, target, facing.Opposite, EnumTool.Drill, progress, 0.45f);
            BreakBlockToContraptionOutput(context, targetPos, target);
        }

        private static void DoSawWork(ContraptionWorkContext context, BlockFacing facing)
        {
            BlockPos frontPos = GetToolTargetPos(context, facing);
            Block frontBlock = context.World.BlockAccessor.GetBlock(frontPos);
            if (IsSawBreakableLeaves(context, frontBlock, frontPos))
            {
                DoSawLeafWork(context, frontPos, facing);
                return;
            }

            if (!IsSawBreakableWood(context, frontBlock, frontPos)) return;

            List<BlockPos> treeBlocks = ContraptionToolTreeSearch.Find(
                context.World,
                frontPos,
                pos => !context.Contraption.ContainsSnapshotWorldBlockPosition(pos) && CanBreakAt(context, pos));
            if (treeBlocks.Count == 0) return;

            string key = WorkKey("saw", frontPos, frontBlock);
            float required = ContraptionToolRules.SawRequiredWork(context.World, treeBlocks);
            if (!context.AddProgress(key, context.WorkRate, required, out float progress))
            {
                EmitToolWorkEffects(context, key, frontPos, frontBlock, facing.Opposite, EnumTool.Saw, progress, 0.4f);
                context.RequestMovementPause(ToolMovementPauseKey, 150, "Cutting tree");
                return;
            }

            EmitToolWorkEffects(context, key, frontPos, frontBlock, facing.Opposite, EnumTool.Saw, progress, 0.55f);
            for (int i = 0; i < treeBlocks.Count; i++)
            {
                BlockPos pos = treeBlocks[i];
                if (context.Contraption.ContainsSnapshotWorldBlockPosition(pos)) continue;

                Block block = context.World.BlockAccessor.GetBlock(pos);
                if (!IsSawBreakableTreeBlock(context, block, pos)) continue;

                BreakBlockToContraptionOutput(context, pos, block);
            }
        }

        private static void DoSawLeafWork(ContraptionWorkContext context, BlockPos leafPos, BlockFacing facing)
        {
            if (context.Contraption.ContainsSnapshotWorldBlockPosition(leafPos)) return;

            Block leaf = context.World.BlockAccessor.GetBlock(leafPos);
            if (!IsSawBreakableLeaves(context, leaf, leafPos)) return;

            string key = WorkKey("sawleaf", leafPos, leaf);
            float required = ContraptionToolRules.SawLeafRequiredWork(leaf);
            if (!context.AddProgress(key, context.WorkRate, required, out float progress))
            {
                EmitToolWorkEffects(context, key, leafPos, leaf, facing.Opposite, EnumTool.Saw, progress, 0.28f);
                context.RequestMovementPause(ToolMovementPauseKey, 80, "Cutting leaves");
                return;
            }

            EmitToolWorkEffects(context, key, leafPos, leaf, facing.Opposite, EnumTool.Saw, progress, 0.35f);
            BreakBlockToContraptionOutput(context, leafPos, leaf);
        }

        private static bool CanDrillBreak(ContraptionWorkContext context, Block block, BlockPos pos)
        {
            return ContraptionToolRules.CanDrillBreak(context.World, block, pos, target => CanBreakAt(context, target));
        }

        private static bool IsSawBreakableTreeBlock(ContraptionWorkContext context, Block block, BlockPos pos)
        {
            return IsSawBreakableWood(context, block, pos) || IsSawBreakableLeaves(context, block, pos);
        }

        private static bool IsSawBreakableWood(ContraptionWorkContext context, Block block, BlockPos pos)
        {
            return ContraptionToolRules.CanSawWood(context.World, block, pos, target => CanBreakAt(context, target));
        }

        private static bool IsSawBreakableLeaves(ContraptionWorkContext context, Block block, BlockPos pos)
        {
            return ContraptionToolRules.CanSawLeaves(context.World, block, pos, target => CanBreakAt(context, target));
        }

        private static void EmitToolWorkEffects(ContraptionWorkContext context, string key, BlockPos pos, Block block, BlockFacing hitFace, EnumTool tool, float progress, float volume)
        {
            if (context.World == null || pos == null || block == null || block.Id == 0 || string.IsNullOrEmpty(key)) return;

            try
            {
                if (progress < 1f && context.ShouldRunVisualPulse(key + ":decal", ToolDecalIntervalMs))
                {
                    EmitDamageDecal(context, key, pos, block, hitFace, progress);
                }

                if (context.ShouldRunVisualPulse(key + ":particles", ToolParticleIntervalMs))
                {
                    SpawnMiningParticles(context, pos, block, hitFace);
                }

                if (context.ShouldRunSoundPulse(ToolSoundIntervalMs))
                {
                    PlayMiningSound(context, pos, block, hitFace, tool, volume);
                }
            }
            catch
            {
                // Tool effects are cosmetic; never let a bad sound or particle path affect mining progress.
            }
        }

        private static void EmitDamageDecal(ContraptionWorkContext context, string key, BlockPos pos, Block block, BlockFacing hitFace, float progress)
        {
            float visualTarget = Math.Clamp(progress * 0.92f, 0.04f, 0.92f);
            float visualDelta = context.AdvanceVisualProgress(key + ":decal", visualTarget);
            if (visualDelta <= 0.001f) return;

            float resistance = MathF.Max(1f, block.GetResistance(context.World.BlockAccessor, pos));
            context.World.BlockAccessor.DamageBlock(pos, hitFace ?? BlockFacing.UP, resistance * visualDelta);
        }

        private static void SpawnMiningParticles(ContraptionWorkContext context, BlockPos pos, Block block, BlockFacing hitFace)
        {
            Vec3d particlePos = pos.ToVec3d().Add(0.5, 0.5, 0.5);
            if (hitFace != null)
            {
                particlePos.Add(hitFace.Normali.X * 0.45, hitFace.Normali.Y * 0.45, hitFace.Normali.Z * 0.45);
            }

            context.World.SpawnCubeParticles(particlePos, new ItemStack(block), 0.18f, 4, 0.45f);
        }

        private static void PlayMiningSound(ContraptionWorkContext context, BlockPos pos, Block block, BlockFacing hitFace, EnumTool tool, float volume)
        {
            SoundAttributes sound = GetMiningSound(context, pos, block, hitFace, tool);
            AssetLocation location = sound.Location ?? FallbackMiningSound(tool);

            context.World.PlaySoundAt(location, pos.X + 0.5, pos.InternalY + 0.5, pos.Z + 0.5, null, randomizePitch: true, range: MathF.Max(8f, sound.Range), volume: volume);
        }

        private static SoundAttributes GetMiningSound(ContraptionWorkContext context, BlockPos pos, Block block, BlockFacing hitFace, EnumTool tool)
        {
            try
            {
                BlockSelection blockSel = new BlockSelection
                {
                    Position = pos.Copy(),
                    Face = hitFace ?? BlockFacing.UP
                };
                BlockSounds sounds = block.GetSounds(context.World.BlockAccessor, blockSel) ?? block.Sounds;
                if (sounds != null) return sounds.GetHitSound(tool);
            }
            catch
            {
                if (block.Sounds != null) return block.Sounds.GetHitSound(tool);
            }

            return new SoundAttributes(FallbackMiningSound(tool), true);
        }

        private static AssetLocation FallbackMiningSound(EnumTool tool)
        {
            return tool == EnumTool.Saw ? SawFallbackSound : DrillFallbackSound;
        }

        private static void BreakBlockToContraptionOutput(ContraptionWorkContext context, BlockPos pos, Block block)
        {
            if (!CanBreakAt(context, pos)) return;
            StorageRemovalCheck removal = KineticStorageRemovalService.Check(
                context.World,
                pos,
                StorageRemovalKind.ContraptionCapture);
            if (!removal.Allowed) return;

            ItemStack[] drops = null;
            try
            {
                drops = block.GetDrops(context.World, pos, null, 1f);
            }
            catch
            {
                drops = null;
            }

            if (drops != null)
            {
                foreach (ItemStack drop in drops)
                {
                    if (drop == null || drop.StackSize <= 0) continue;
                    context.DepositOutput(drop, pos);
                }
            }

            AutomationBlockMutation.RemoveAndNotify(context.World, pos);
        }

        private static bool CanBreakAt(ContraptionWorkContext context, BlockPos pos)
        {
            if (context.Contraption != null)
            {
                return context.Contraption.CanAutomationBuildOrBreakAt(pos);
            }

            return AutomationClaimUtil.CanAutomatedBlockAccess(context.World, context.ToolWorldPos, pos, EnumBlockAccessFlags.BuildOrBreak);
        }

        private static bool TryGetToolFacing(Block block, out BlockFacing facing)
        {
            return ContraptionToolRules.TryGetFacing(block, out facing);
        }

        private static bool IsMovingIntoFacing(ContraptionWorkContext context, BlockFacing facing)
        {
            if (facing == null) return false;
            double dot = context.MoveX * facing.Normali.X
                + context.MoveY * facing.Normali.Y
                + context.MoveZ * facing.Normali.Z;
            return dot > 0.000001;
        }

        private static BlockPos GetToolTargetPos(ContraptionWorkContext context, BlockFacing facing)
        {
            Vec3d exact = context.Contraption?.GetWorldPositionForOffset(context.LocalOffset);
            if (exact == null) return context.ToolWorldPos.AddCopy(facing);

            exact = exact.AddCopy(context.MoveX, context.MoveY, context.MoveZ);

            const double eps = 0.0001;
            int x = CellAtCenter(exact.X);
            int y = CellAtCenter(exact.Y);
            int z = CellAtCenter(exact.Z);

            Cuboidf[] boxes = context.ToolBlock.GetCollisionBoxes(context.World.BlockAccessor, context.ToolWorldPos)
                ?? context.ToolBlock.CollisionBoxes;

            if (facing == BlockFacing.EAST) x = (int)Math.Floor(exact.X + ToolPositiveReach(boxes, 0) + eps);
            else if (facing == BlockFacing.WEST) x = (int)Math.Floor(exact.X + ToolNegativeReach(boxes, 0) - eps);
            else if (facing == BlockFacing.UP) y = (int)Math.Floor(exact.Y + ToolPositiveReach(boxes, 1) + eps);
            else if (facing == BlockFacing.DOWN) y = (int)Math.Floor(exact.Y + ToolNegativeReach(boxes, 1) - eps);
            else if (facing == BlockFacing.SOUTH) z = (int)Math.Floor(exact.Z + ToolPositiveReach(boxes, 2) + eps);
            else if (facing == BlockFacing.NORTH) z = (int)Math.Floor(exact.Z + ToolNegativeReach(boxes, 2) - eps);

            return new BlockPos(x, y % BlockPos.DimensionBoundary, z, context.ToolWorldPos.dimension);
        }

        private static double ToolPositiveReach(Cuboidf[] boxes, int axis)
        {
            if (boxes == null || boxes.Length == 0) return 1.0;
            double reach = 1.0;
            for (int i = 0; i < boxes.Length; i++)
            {
                Cuboidf box = boxes[i];
                if (box == null) continue;
                reach = Math.Max(reach, axis == 0 ? box.X2 : axis == 1 ? box.Y2 : box.Z2);
            }
            return reach;
        }

        private static double ToolNegativeReach(Cuboidf[] boxes, int axis)
        {
            if (boxes == null || boxes.Length == 0) return 0.0;
            double reach = 0.0;
            for (int i = 0; i < boxes.Length; i++)
            {
                Cuboidf box = boxes[i];
                if (box == null) continue;
                reach = Math.Min(reach, axis == 0 ? box.X1 : axis == 1 ? box.Y1 : box.Z1);
            }
            return reach;
        }

        private static int CellAtCenter(double blockLowerCoord)
        {
            return (int)Math.Floor(blockLowerCoord + 0.5);
        }

        private static string WorkKey(string prefix, BlockPos pos, Block block)
        {
            return prefix + ":" + PositionKey(pos) + ":" + block?.Code;
        }

        private static string PositionKey(BlockPos pos)
        {
            return pos.dimension + ":" + pos.X + "," + pos.InternalY + "," + pos.Z;
        }
    }
}
