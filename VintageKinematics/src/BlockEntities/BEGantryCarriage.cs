using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.Entities;
using VintageKinematics.Network;
using VintageKinematics.Storage;

#pragma warning disable CS0618
namespace VintageKinematics.BlockEntities
{
    public class BEGantryCarriage : BlockEntity, IContraptionController
    {
        private static readonly AssetLocation ContraptionEntityCode = new AssetLocation("vintagekinematics", "contraption");
        private const int DefaultMaxSelectedBlocks = 512;
        private const int SnapshotWatchIntervalMs = 500;
        private const long GantryAutoAssembleDelayMs = 750;

        private long linkedEntityId;
        private Vec3i localMin = new Vec3i(0, 1, 0);
        private Vec3i localMax = new Vec3i(0, 1, 0);
        private Vec3i[] selectionCellOffsets = Array.Empty<Vec3i>();
        private Vec3i[] snapshotOffsets = new[] { new Vec3i(0, 1, 0) };
        private string[] snapshotBlockCodes = Array.Empty<string>();
        private TreeAttribute[] snapshotBlockEntityTrees = Array.Empty<TreeAttribute>();
        private bool assembled;
        private double assembledX;
        private double assembledY;
        private double assembledZ;
        private string gantryDebug = "No gantry update";
        private bool assemblingEntity;
        private ContraptionPlacementMode placementMode = ContraptionPlacementMode.AlwaysPlaceWhenStopped;
        private long placedAtMs;
        private int attachedShaftDx;
        private int attachedShaftDy;
        private int attachedShaftDz;
        private bool hasAttachedShaft;

        public ContraptionPlacementMode PlacementMode => placementMode;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side == EnumAppSide.Server)
            {
                NormalizePlacedAtTimestamp();
                TryInferAttachedShaftFromBlock();
                RegisterGameTickListener(OnSnapshotWatchTick, SnapshotWatchIntervalMs);
            }
        }

        public bool OnPlayerRightClick(IPlayer byPlayer)
        {
            if (!CheckClaim(byPlayer)) return true;

            if (byPlayer?.Entity?.Controls?.Sneak == true)
            {
                CyclePlacementMode(byPlayer);
                MarkDirty(true);
                return true;
            }

            Entity linked = GetLinkedEntity();
            if (linked is EntityVKContraption contraption)
            {
                if (!contraption.TryRestoreToWorld(byPlayer, overwrite: false))
                {
                    Notify(byPlayer, "Contraption cannot disassemble: target space is blocked.");
                    return true;
                }

                linkedEntityId = 0;
                assembled = false;
                MarkDirty(true);
                return true;
            }
            if (linked != null) return true;

            TryAssemble(byPlayer, notify: true);
            return true;
        }

        public bool TryAssembleForGantry()
        {
            if (GetLinkedEntity() != null) return false;
            if (HasLiveContraptionAtControllerPosition()) return false;
            NormalizePlacedAtTimestamp();
            if (Api?.World != null && placedAtMs > 0 && Api.World.ElapsedMilliseconds - placedAtMs < GantryAutoAssembleDelayMs)
            {
                SetGantryDebug("Waiting for newly placed carriage");
                return false;
            }

            return TryAssemble(null, notify: false);
        }

        private bool HasLiveContraptionAtControllerPosition()
        {
            if (Api?.World == null || Pos == null) return false;

            Vec3d center = new Vec3d(Pos.X + 0.5, Pos.InternalY + 0.5, Pos.Z + 0.5);
            Entity[] entities = Api.World.GetEntitiesAround(center, 3f, 3f, entity => entity is EntityVKContraption);
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] is EntityVKContraption contraption
                    && contraption.Alive
                    && !contraption.SnapshotRestored
                    && contraption.HasControllerAtWorldBlock(Pos))
                {
                    return true;
                }
            }

            return false;
        }

        private void NormalizePlacedAtTimestamp()
        {
            if (Api?.World == null || placedAtMs <= 0) return;
            if (placedAtMs <= Api.World.ElapsedMilliseconds) return;

            placedAtMs = 0;
            gantryDebug = "Recovered stale gantry placement delay";
            if (Api.Side == EnumAppSide.Server) MarkDirty(false);
        }

        public override void OnBlockPlaced(ItemStack byItemStack = null)
        {
            base.OnBlockPlaced(byItemStack);
            MarkPlacedOnGantry();
        }

        public void MarkPlacedOnGantry()
        {
            placedAtMs = Api?.World?.ElapsedMilliseconds ?? 0;
            gantryDebug = hasAttachedShaft ? "Placed on gantry shaft" : "Placed without gantry anchor";
            MarkDirty(false);
        }

        public void MarkPlacedOnGantry(BlockPos shaftPos)
        {
            if (shaftPos != null && Pos != null)
            {
                attachedShaftDx = shaftPos.X - Pos.X;
                attachedShaftDy = shaftPos.InternalY - Pos.InternalY;
                attachedShaftDz = shaftPos.Z - Pos.Z;
                hasAttachedShaft = true;
            }

            MarkPlacedOnGantry();
        }

        public bool TryGetAttachedShaftPos(out BlockPos shaftPos)
        {
            shaftPos = null;
            if (!hasAttachedShaft)
            {
                TryInferAttachedShaftFromBlock();
            }

            if (!hasAttachedShaft || Pos == null) return false;

            shaftPos = new BlockPos(
                Pos.X + attachedShaftDx,
                (Pos.InternalY + attachedShaftDy) % BlockPos.DimensionBoundary,
                Pos.Z + attachedShaftDz,
                Pos.dimension);
            return true;
        }

        private bool TryInferAttachedShaftFromBlock()
        {
            if (hasAttachedShaft) return true;
            if (Api?.World == null || Pos == null || Block?.Code == null) return false;
            if (!TryGetExpectedShaftDelta(Block, out int dx, out int dy, out int dz, out string axisCode)) return false;

            BlockPos shaftPos = new BlockPos(
                Pos.X + dx,
                (Pos.InternalY + dy) % BlockPos.DimensionBoundary,
                Pos.Z + dz,
                Pos.dimension);

            Block shaftBlock = Api.World.BlockAccessor.GetBlock(shaftPos);
            if (!IsGantryShaft(shaftBlock, out EnumKineticAxis shaftAxis)) return false;
            if (!AxisMatches(axisCode, shaftAxis)) return false;

            attachedShaftDx = dx;
            attachedShaftDy = dy;
            attachedShaftDz = dz;
            hasAttachedShaft = true;
            gantryDebug = "Recovered gantry shaft anchor";
            if (Api.Side == EnumAppSide.Server) MarkDirty(false);
            return true;
        }

        private static bool TryGetExpectedShaftDelta(Block block, out int dx, out int dy, out int dz, out string axisCode)
        {
            dx = 0;
            dy = 0;
            dz = 0;
            axisCode = block?.Variant?["axis"];
            string side = block?.Variant?["side"];
            if (string.IsNullOrEmpty(axisCode) || string.IsNullOrEmpty(side)) return false;

            if ((axisCode == "x" || axisCode == "z") && side == "u")
            {
                dy = -1;
                return true;
            }

            if (axisCode != "y") return false;

            switch (side)
            {
                case "n": dz = 1; return true;
                case "e": dx = -1; return true;
                case "s": dz = -1; return true;
                case "w": dx = 1; return true;
                default: return false;
            }
        }

        private static bool AxisMatches(string axisCode, EnumKineticAxis axis)
        {
            return (axisCode == "x" && axis == EnumKineticAxis.X)
                || (axisCode == "y" && axis == EnumKineticAxis.Y)
                || (axisCode == "z" && axis == EnumKineticAxis.Z);
        }

        public bool TryGetGantryAnchor(EnumKineticAxis axis, out BlockPos shaftPos)
        {
            if (!TryGetAttachedShaftPos(out shaftPos)) return false;

            Block shaftBlock = Api?.World?.BlockAccessor.GetBlock(shaftPos);
            return IsGantryShaft(shaftBlock, out EnumKineticAxis shaftAxis) && shaftAxis == axis;
        }

        private bool TryAssemble(IPlayer byPlayer, bool notify)
        {
            RefreshSnapshotFromCurrentWorld();
            if (snapshotOffsets == null || snapshotOffsets.Length == 0)
            {
                if (notify) Notify(byPlayer, "Contraption selection has no blocks to assemble.");
                return true;
            }
            if (ContainsStorageMemberSnapshot())
            {
                if (notify) Notify(byPlayer, Lang.Get("vintagekinematics:storage-contraption-unsupported"));
                return true;
            }
            if (!CanAssembleSnapshot(byPlayer))
            {
                if (notify) Notify(byPlayer, "Contraption selection crosses a protected claim.");
                return true;
            }

            EntityProperties entityType = Api.World.GetEntityType(ContraptionEntityCode);
            if (entityType == null)
            {
                Api.World.Logger.Error("[VintageKinematics] Missing contraption entity type {0}", ContraptionEntityCode);
                return true;
            }

            if (Api.World.ClassRegistry.CreateEntity(entityType) is not EntityVKContraption entity)
            {
                Api.World.Logger.Error("[VintageKinematics] Could not create contraption entity for {0}", ContraptionEntityCode);
                return true;
            }

            NormalizeBounds(ref localMin, ref localMax);
            RefreshControllerTreeInSnapshot();
            Vec3d spawnPos = GetEntityOrigin(localMin, localMax);
            entity.Pos.SetPosWithDimension(spawnPos);
            entity.ServerPos.SetFrom(entity.Pos);
            entity.PreviousServerPos.SetFrom(entity.Pos);
            entity.PositionBeforeFalling.Set(spawnPos.X, spawnPos.Y, spawnPos.Z);
            string ownerUid = byPlayer?.PlayerUID ?? AutomationClaimUtil.GetClaimOwnerUidAt(Api.World, Pos);
            string ownerName = byPlayer?.PlayerName ?? AutomationClaimUtil.GetClaimOwnerNameAt(Api.World, Pos);
            entity.Configure(Pos, localMin.Clone(), localMax.Clone(), snapshotOffsets, snapshotBlockCodes, snapshotBlockEntityTrees, snapshotOffsets.Length, placementMode, ownerUid, ownerName);

            Api.World.SpawnEntity(entity);
            assemblingEntity = true;
            RemoveSnapshotBlocksFromWorld();
            return true;
        }

        private bool ContainsStorageMemberSnapshot()
        {
            if (Api?.World == null || snapshotOffsets == null) return false;

            for (int i = 0; i < snapshotOffsets.Length; i++)
            {
                Vec3i offset = snapshotOffsets[i];
                if (offset == null) continue;

                Block block = Api.World.BlockAccessor.GetBlock(WorldPosFromOffset(offset));
                if (block?.Attributes?[KineticStorageRemovalService.StorageMemberAttribute].AsBool(false) == true)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanAssembleSnapshot(IPlayer byPlayer)
        {
            if (Api?.World == null || snapshotOffsets == null) return false;

            for (int i = 0; i < snapshotOffsets.Length; i++)
            {
                Vec3i offset = snapshotOffsets[i];
                if (offset == null) continue;

                BlockPos blockPos = WorldPosFromOffset(offset);
                if (byPlayer != null)
                {
                    if (!Api.World.Claims.TryAccess(byPlayer, blockPos, EnumBlockAccessFlags.BuildOrBreak)) return false;
                    continue;
                }

                if (!AutomationClaimUtil.CanAutomatedBlockAccess(Api.World, Pos, blockPos, EnumBlockAccessFlags.BuildOrBreak)) return false;
            }

            return true;
        }

        private void CyclePlacementMode(IPlayer byPlayer)
        {
            placementMode = placementMode switch
            {
                ContraptionPlacementMode.AlwaysPlaceWhenStopped => ContraptionPlacementMode.OnlyPlaceNearInitialAngle,
                ContraptionPlacementMode.OnlyPlaceNearInitialAngle => ContraptionPlacementMode.OnlyPlaceWhenAnchorDestroyed,
                _ => ContraptionPlacementMode.AlwaysPlaceWhenStopped
            };

            Notify(byPlayer, "Contraption placement mode: " + PlacementModeName(placementMode));
        }

        private void RefreshControllerTreeInSnapshot()
        {
            if (snapshotOffsets == null || snapshotBlockEntityTrees == null) return;

            snapshotBlockEntityTrees = NormalizeBlockEntityTrees(snapshotBlockEntityTrees, snapshotOffsets.Length);
            for (int i = 0; i < snapshotOffsets.Length; i++)
            {
                Vec3i offset = snapshotOffsets[i];
                if (offset == null || offset.X != 0 || offset.Y != 0 || offset.Z != 0) continue;

                TreeAttribute tree = new TreeAttribute();
                ToTreeAttributes(tree);
                ContraptionApi.ClearKineticRuntimeState(tree);
                tree.SetLong("linkedEntityId", 0);
                tree.SetBool("assembled", false);
                tree.SetDouble("assembledX", Pos.X);
                tree.SetDouble("assembledY", Pos.InternalY);
                tree.SetDouble("assembledZ", Pos.Z);
                tree.SetString("gantryDebug", "Restored from contraption");
                snapshotBlockEntityTrees[i] = tree;
                return;
            }
        }

        public bool SetSelectionFromWorldBounds(BlockPos start, BlockPos end, IPlayer byPlayer)
        {
            if (start == null || end == null || !CheckClaim(byPlayer)) return false;
            if (start.dimension != Pos.dimension || end.dimension != Pos.dimension)
            {
                Notify(byPlayer, "Contraption selection must be in the same dimension as the controller.");
                return true;
            }

            List<BlockPos> positions = new List<BlockPos>();
            int minX = Math.Min(start.X, end.X);
            int minY = Math.Min(start.Y, end.Y);
            int minZ = Math.Min(start.Z, end.Z);
            int maxX = Math.Max(start.X, end.X);
            int maxY = Math.Max(start.Y, end.Y);
            int maxZ = Math.Max(start.Z, end.Z);

            for (int y = minY; y <= maxY; y++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        positions.Add(new BlockPos(x, y, z, Pos.dimension));
                    }
                }
            }

            return SetSelectionFromWorldPositions(positions, byPlayer);
        }

        public bool SetSelectionFromWorldPositions(IEnumerable<BlockPos> positions, IPlayer byPlayer)
        {
            if (positions == null || !CheckClaim(byPlayer)) return false;

            Vec3i[] cells = BuildSelectionCellOffsets(positions, Pos, out bool wrongDimension);
            if (wrongDimension)
            {
                Notify(byPlayer, "Contraption selection must be in the same dimension as the controller.");
                return true;
            }

            CaptureSnapshot(cells, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees);
            int removedDisconnected = PruneDisconnectedSnapshot(ref offsets, ref blockCodes, ref blockEntityTrees);
            if (offsets.Length == 0)
            {
                Notify(byPlayer, "Contraption selection has no blocks to capture.");
                return true;
            }
            if (offsets.Length > DefaultMaxSelectedBlocks)
            {
                Notify(byPlayer, $"Contraption selection is too large ({offsets.Length}/{DefaultMaxSelectedBlocks} captured blocks).");
                return true;
            }

            selectionCellOffsets = cells;
            SetBoundsFromOffsets(offsets, ref localMin, ref localMax);
            snapshotOffsets = offsets;
            snapshotBlockCodes = blockCodes;
            snapshotBlockEntityTrees = blockEntityTrees;
            assembled = false;
            MarkDirty(true);
            string suffix = removedDisconnected > 0 ? $" {removedDisconnected} disconnected blocks excluded." : string.Empty;
            Notify(byPlayer, $"Contraption selection assigned: {offsets.Length} blocks captured.{suffix}");
            return true;
        }

        public IEnumerable<BlockPos> GetSelectionWorldPositions()
        {
            Vec3i[] offsets = snapshotOffsets != null && snapshotOffsets.Length > 0
                ? snapshotOffsets
                : selectionCellOffsets;

            for (int i = 0; i < (offsets?.Length ?? 0); i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null) continue;
                yield return WorldPosFromOffset(offset);
            }
        }

        public override void OnBlockRemoved()
        {
            if (Api?.Side == EnumAppSide.Server)
            {
                if (assemblingEntity)
                {
                    base.OnBlockRemoved();
                    return;
                }

                Entity linked = GetLinkedEntity();
                SyncSnapshotFromEntity(linked);
                bool restored = !assembled;
                if (assembled)
                {
                    restored = linked is EntityVKContraption contraption
                        ? contraption.TryRestoreToWorld(null, overwrite: false)
                        : TryRestoreSnapshotBlocks(false, linked as EntityVKContraption);
                    assembled = !restored;
                }
                if (restored)
                {
                    if (linked is not EntityVKContraption) linked?.Die(EnumDespawnReason.Removed);
                    linkedEntityId = 0;
                }
            }
            base.OnBlockRemoved();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetLong("linkedEntityId", linkedEntityId);
            tree.SetVec3i("localMin", localMin);
            tree.SetVec3i("localMax", localMax);
            tree.SetVec3is("selectionCellOffsets", selectionCellOffsets ?? Array.Empty<Vec3i>());
            tree.SetVec3is("snapshotOffsets", snapshotOffsets ?? Array.Empty<Vec3i>());
            tree["snapshotBlockCodes"] = new StringArrayAttribute(snapshotBlockCodes ?? Array.Empty<string>());
            tree["snapshotBlockEntityTrees"] = new TreeArrayAttribute(NormalizeBlockEntityTrees(snapshotBlockEntityTrees, snapshotBlockCodes?.Length ?? 0));
            tree.SetBool("assembled", assembled);
            tree.SetDouble("assembledX", assembledX);
            tree.SetDouble("assembledY", assembledY);
            tree.SetDouble("assembledZ", assembledZ);
            tree.SetString("gantryDebug", gantryDebug ?? "");
            tree.SetInt("placementMode", (int)placementMode);
            tree.SetLong("placedAtMs", placedAtMs);
            tree.SetBool("hasAttachedShaft", hasAttachedShaft);
            tree.SetInt("attachedShaftDx", attachedShaftDx);
            tree.SetInt("attachedShaftDy", attachedShaftDy);
            tree.SetInt("attachedShaftDz", attachedShaftDz);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            linkedEntityId = tree.GetLong("linkedEntityId");
            localMin = tree.GetVec3i("localMin", new Vec3i(0, 1, 0));
            localMax = tree.GetVec3i("localMax", new Vec3i(0, 1, 0));
            selectionCellOffsets = tree.GetVec3is("selectionCellOffsets", Array.Empty<Vec3i>());
            snapshotOffsets = tree.GetVec3is("snapshotOffsets", new[] { new Vec3i(0, 1, 0) });
            snapshotBlockCodes = (tree["snapshotBlockCodes"] as StringArrayAttribute)?.value ?? Array.Empty<string>();
            snapshotBlockEntityTrees = NormalizeBlockEntityTrees((tree["snapshotBlockEntityTrees"] as TreeArrayAttribute)?.value, snapshotBlockCodes.Length);
            assembled = tree.GetBool("assembled");
            assembledX = tree.GetDouble("assembledX", Pos?.X ?? 0);
            assembledY = tree.GetDouble("assembledY", Pos?.InternalY ?? 0);
            assembledZ = tree.GetDouble("assembledZ", Pos?.Z ?? 0);
            gantryDebug = tree.GetString("gantryDebug", gantryDebug);
            placementMode = (ContraptionPlacementMode)tree.GetInt("placementMode", (int)ContraptionPlacementMode.AlwaysPlaceWhenStopped);
            placedAtMs = tree.GetLong("placedAtMs");
            hasAttachedShaft = tree.GetBool("hasAttachedShaft");
            attachedShaftDx = tree.GetInt("attachedShaftDx");
            attachedShaftDy = tree.GetInt("attachedShaftDy");
            attachedShaftDz = tree.GetInt("attachedShaftDz");
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);

            Entity linked = GetLinkedEntity();
            if (linked == null)
            {
                sb.AppendLine(Lang.Get("vintagekinematics:gantrycarriage-status-idle"));
            }
            else
            {
                sb.AppendLine(Lang.Get("vintagekinematics:gantrycarriage-status-linked", linked.EntityId));
            }

            NormalizeBounds(ref localMin, ref localMax);
            sb.AppendLine(Lang.Get("vintagekinematics:gantrycarriage-selection", snapshotOffsets?.Length ?? 0));
            sb.AppendLine("Placement mode: " + PlacementModeName(placementMode));
            if (TryGetAttachedShaftPos(out BlockPos shaftPos)) sb.AppendLine($"Gantry anchor: {shaftPos.X}, {shaftPos.InternalY}, {shaftPos.Z}");
            if (assembled) sb.AppendLine(Lang.Get("vintagekinematics:gantrycarriage-assembled"));
            sb.AppendLine($"Gantry debug: assembled={assembled}, linked={(linked != null ? linked.EntityId.ToString() : "none")}");
            sb.AppendLine($"Gantry pos: {assembledX:0.000}, {assembledY:0.000}, {assembledZ:0.000}");
            sb.AppendLine($"Gantry state: {gantryDebug}");
        }

        private Entity GetLinkedEntity()
        {
            return linkedEntityId == 0 ? null : Api?.World?.GetEntityById(linkedEntityId);
        }

        public bool TryGetLinkedContraption(out EntityVKContraption contraption)
        {
            contraption = GetLinkedEntity() as EntityVKContraption;
            return assembled && contraption != null;
        }

        public double AssembledAxisCoord(EnumKineticAxis axis)
        {
            return axis switch
            {
                EnumKineticAxis.X => assembledX,
                EnumKineticAxis.Y => assembledY,
                EnumKineticAxis.Z => assembledZ,
                _ => assembledX
            };
        }

        public void SetGantryDebug(string message)
        {
            gantryDebug = message ?? "";
            MarkDirty(false);
        }

        public void MoveAssembledController(double dx, double dy, double dz, EntityVKContraption contraption)
        {
            if (Api?.Side != EnumAppSide.Server)
            {
                return;
            }

            if (!assembled)
            {
                SetGantryDebug("Move rejected: not assembled");
                return;
            }

            if (contraption == null)
            {
                SetGantryDebug("Move rejected: no linked entity");
                return;
            }

            double nextAssembledX = assembledX + dx;
            double nextAssembledY = assembledY + dy;
            double nextAssembledZ = assembledZ + dz;

            BlockPos targetPos = new BlockPos(
                (int)Math.Round(nextAssembledX),
                (int)Math.Round(nextAssembledY) % BlockPos.DimensionBoundary,
                (int)Math.Round(nextAssembledZ),
                Pos.dimension);

            bool relocating = !targetPos.Equals(Pos);
            if (relocating)
            {
                Block existing = Api.World.BlockAccessor.GetBlock(targetPos);
                if (existing != null && existing.Id != 0 && !SnapshotContainsWorldPos(targetPos))
                {
                    SetGantryDebug($"Move blocked: target {targetPos.X},{targetPos.InternalY},{targetPos.Z} has {existing.Code}");
                    return;
                }
            }

            MoveLinkedContraption(contraption, dx, dy, dz);
            assembledX = nextAssembledX;
            assembledY = nextAssembledY;
            assembledZ = nextAssembledZ;

            if (!relocating)
            {
                SetGantryDebug($"Moved entity only d=({dx:0.0000},{dy:0.0000},{dz:0.0000})");
                MarkDirty(false);
                return;
            }

            Block controllerBlock = Api.World.BlockAccessor.GetBlock(Pos);
            TreeAttribute tree = new TreeAttribute();
            ToTreeAttributes(tree);
            tree.SetInt("posx", targetPos.X);
            tree.SetInt("posy", targetPos.InternalY);
            tree.SetInt("posz", targetPos.Z);
            tree.SetDouble("assembledX", assembledX);
            tree.SetDouble("assembledY", assembledY);
            tree.SetDouble("assembledZ", assembledZ);

            BlockPos oldPos = Pos.Copy();
            Api.World.BlockAccessor.SetBlock(0, oldPos);
            Api.World.BlockAccessor.SetBlock(controllerBlock.Id, targetPos);
            BEGantryCarriage moved = Api.World.BlockAccessor.GetBlockEntity(targetPos) as BEGantryCarriage;
            if (moved != null)
            {
                moved.FromTreeAttributes(tree, Api.World);
                moved.MarkDirty(true);
            }

            Api.World.BlockAccessor.MarkBlockDirty(oldPos);
            Api.World.BlockAccessor.MarkBlockDirty(targetPos);
            moved?.SetGantryDebug($"Moved block {oldPos.X},{oldPos.InternalY},{oldPos.Z} -> {targetPos.X},{targetPos.InternalY},{targetPos.Z}");
        }

        private bool SnapshotContainsWorldPos(BlockPos worldPos)
        {
            if (snapshotOffsets == null) return false;
            for (int i = 0; i < snapshotOffsets.Length; i++)
            {
                BlockPos snapshotPos = WorldPosFromOffset(snapshotOffsets[i]);
                if (snapshotPos.Equals(worldPos)) return true;
            }

            return false;
        }

        private void DisassembleLinkedEntity(Entity linked)
        {
            SyncSnapshotFromEntity(linked);

            bool restored = !assembled;
            if (assembled)
            {
                restored = linked is EntityVKContraption contraption
                    ? contraption.TryRestoreToWorld(null, overwrite: false)
                    : TryRestoreSnapshotBlocks(false, linked as EntityVKContraption);
            }

            if (!restored) return;

            if (linked is not EntityVKContraption) linked?.Die(EnumDespawnReason.Removed);
            linkedEntityId = 0;
            assembled = false;
        }

        private void SyncSnapshotFromEntity(Entity linked)
        {
            if (linked is not EntityVKContraption contraption) return;
            if (!contraption.TryGetSnapshot(out Vec3i min, out Vec3i max, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)) return;

            localMin = min;
            localMax = max;
            snapshotOffsets = offsets;
            snapshotBlockCodes = blockCodes;
            snapshotBlockEntityTrees = blockEntityTrees;
        }

        private bool CheckClaim(IPlayer player)
        {
            if (player == null) return false;
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} tried to use gantry carriage at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
            return false;
        }

        private Vec3d GetEntityOrigin(Vec3i min, Vec3i max)
        {
            double width = max.X - min.X + 1;
            double depth = max.Z - min.Z + 1;
            return Pos.ToVec3d().Add(min.X + width / 2.0, min.Y, min.Z + depth / 2.0);
        }

        private static int CountBlocks(Vec3i min, Vec3i max)
        {
            NormalizeBounds(ref min, ref max);
            return (max.X - min.X + 1) * (max.Y - min.Y + 1) * (max.Z - min.Z + 1);
        }

        public static List<BlockPos> GetConnectedSelectionPreview(IWorldAccessor world, BlockPos controllerPos, IEnumerable<BlockPos> positions)
        {
            List<BlockPos> preview = new List<BlockPos>();
            if (world == null || controllerPos == null || positions == null) return preview;

            Vec3i[] cells = BuildSelectionCellOffsets(positions, controllerPos, out _);
            List<Vec3i> offsets = new List<Vec3i>();
            List<string> blockCodes = new List<string>();

            for (int i = 0; i < cells.Length; i++)
            {
                Vec3i cell = cells[i];
                if (cell == null) continue;

                BlockPos blockPos = new BlockPos(controllerPos.X + cell.X, controllerPos.Y + cell.Y, controllerPos.Z + cell.Z, controllerPos.dimension);
                Block block = world.BlockAccessor.GetBlock(blockPos);
                if (block == null || block.Id == 0 || block.Code == null) continue;
                if (IsFluidBlock(block)) continue;
                if (IsGantryShaft(block, out _)) continue;

                offsets.Add(cell.Clone());
                blockCodes.Add(block.Code.ToString());
            }

            Vec3i[] offsetArray = offsets.ToArray();
            string[] codeArray = blockCodes.ToArray();
            PruneDisconnectedSnapshot(ref offsetArray, ref codeArray);

            for (int i = 0; i < offsetArray.Length; i++)
            {
                Vec3i offset = offsetArray[i];
                if (offset == null) continue;
                preview.Add(new BlockPos(controllerPos.X + offset.X, controllerPos.Y + offset.Y, controllerPos.Z + offset.Z, controllerPos.dimension));
            }

            return preview;
        }

        private static Vec3i[] BuildSelectionCellOffsets(IEnumerable<BlockPos> positions, BlockPos controllerPos, out bool wrongDimension)
        {
            wrongDimension = false;
            if (positions == null || controllerPos == null) return Array.Empty<Vec3i>();

            Dictionary<string, Vec3i> cells = new Dictionary<string, Vec3i>();
            AddSelectionCell(cells, 0, 0, 0);

            foreach (BlockPos pos in positions)
            {
                if (pos == null) continue;
                if (pos.dimension != controllerPos.dimension)
                {
                    wrongDimension = true;
                    continue;
                }

                AddSelectionCell(cells, pos.X - controllerPos.X, pos.Y - controllerPos.Y, pos.Z - controllerPos.Z);
            }

            Vec3i[] result = new Vec3i[cells.Count];
            int index = 0;
            foreach (Vec3i cell in cells.Values)
            {
                result[index++] = cell;
            }

            Array.Sort(result, CompareOffset);
            return result;
        }

        private static void AddSelectionCell(Dictionary<string, Vec3i> cells, int x, int y, int z)
        {
            string key = OffsetKey(x, y, z);
            if (!cells.ContainsKey(key)) cells[key] = new Vec3i(x, y, z);
        }

        private static int CompareOffset(Vec3i left, Vec3i right)
        {
            if (left == null && right == null) return 0;
            if (left == null) return -1;
            if (right == null) return 1;
            int y = left.Y.CompareTo(right.Y);
            if (y != 0) return y;
            int z = left.Z.CompareTo(right.Z);
            if (z != 0) return z;
            return left.X.CompareTo(right.X);
        }

        private static void SetBoundsFromOffsets(Vec3i[] offsets, ref Vec3i min, ref Vec3i max)
        {
            if (offsets == null || offsets.Length == 0)
            {
                min = new Vec3i(0, 1, 0);
                max = new Vec3i(0, 1, 0);
                return;
            }

            int minX = int.MaxValue;
            int minY = int.MaxValue;
            int minZ = int.MaxValue;
            int maxX = int.MinValue;
            int maxY = int.MinValue;
            int maxZ = int.MinValue;

            for (int i = 0; i < offsets.Length; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null) continue;
                minX = Math.Min(minX, offset.X);
                minY = Math.Min(minY, offset.Y);
                minZ = Math.Min(minZ, offset.Z);
                maxX = Math.Max(maxX, offset.X);
                maxY = Math.Max(maxY, offset.Y);
                maxZ = Math.Max(maxZ, offset.Z);
            }

            if (minX == int.MaxValue)
            {
                min = new Vec3i(0, 1, 0);
                max = new Vec3i(0, 1, 0);
                return;
            }

            min = new Vec3i(minX, minY, minZ);
            max = new Vec3i(maxX, maxY, maxZ);
        }

        private void CaptureSnapshot(Vec3i min, Vec3i max, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)
        {
            List<Vec3i> capturedOffsets = new List<Vec3i>();
            List<string> capturedCodes = new List<string>();
            List<TreeAttribute> capturedBlockEntityTrees = new List<TreeAttribute>();

            for (int y = min.Y; y <= max.Y; y++)
            {
                for (int z = min.Z; z <= max.Z; z++)
                {
                    for (int x = min.X; x <= max.X; x++)
                    {
                        BlockPos blockPos = new BlockPos(Pos.X + x, Pos.Y + y, Pos.Z + z, Pos.dimension);

                        Block block = Api.World.BlockAccessor.GetBlock(blockPos);
                        if (block == null || block.Id == 0 || block.Code == null) continue;
                        if (IsFluidBlock(block)) continue;
                        if (IsGantryShaft(block, out _)) continue;

                        capturedOffsets.Add(new Vec3i(x, y, z));
                        capturedCodes.Add(block.Code.ToString());
                        capturedBlockEntityTrees.Add(ContraptionApi.CaptureContraptionBlockEntityTree(Api.World, blockPos));
                    }
                }
            }

            offsets = capturedOffsets.ToArray();
            blockCodes = capturedCodes.ToArray();
            blockEntityTrees = capturedBlockEntityTrees.ToArray();
        }

        private void CaptureSnapshot(Vec3i[] cells, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)
        {
            List<Vec3i> capturedOffsets = new List<Vec3i>();
            List<string> capturedCodes = new List<string>();
            List<TreeAttribute> capturedBlockEntityTrees = new List<TreeAttribute>();

            for (int i = 0; i < (cells?.Length ?? 0); i++)
            {
                Vec3i cell = cells[i];
                if (cell == null) continue;

                BlockPos blockPos = new BlockPos(Pos.X + cell.X, Pos.Y + cell.Y, Pos.Z + cell.Z, Pos.dimension);
                Block block = Api.World.BlockAccessor.GetBlock(blockPos);
                if (block == null || block.Id == 0 || block.Code == null) continue;
                if (IsFluidBlock(block)) continue;
                if (IsGantryShaft(block, out _)) continue;

                capturedOffsets.Add(cell.Clone());
                capturedCodes.Add(block.Code.ToString());
                capturedBlockEntityTrees.Add(ContraptionApi.CaptureContraptionBlockEntityTree(Api.World, blockPos));
            }

            offsets = capturedOffsets.ToArray();
            blockCodes = capturedCodes.ToArray();
            blockEntityTrees = capturedBlockEntityTrees.ToArray();
        }

        private void CaptureSelectionSnapshot(out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)
        {
            if (selectionCellOffsets != null && selectionCellOffsets.Length > 0)
            {
                CaptureSnapshot(selectionCellOffsets, out offsets, out blockCodes, out blockEntityTrees);
                return;
            }

            NormalizeBounds(ref localMin, ref localMax);
            IncludeControllerInBounds(ref localMin, ref localMax);
            IncludeControllerAttachmentInBounds(ref localMin, ref localMax);
            CaptureSnapshot(localMin, localMax, out offsets, out blockCodes, out blockEntityTrees);
        }

        private void RefreshSnapshotFromCurrentWorld()
        {
            CaptureSelectionSnapshot(out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees);
            PruneDisconnectedSnapshot(ref offsets, ref blockCodes, ref blockEntityTrees);
            if (offsets.Length > 0) SetBoundsFromOffsets(offsets, ref localMin, ref localMax);
            snapshotOffsets = offsets;
            snapshotBlockCodes = blockCodes;
            snapshotBlockEntityTrees = blockEntityTrees;
        }

        private void OnSnapshotWatchTick(float dt)
        {
            if (assembled || GetLinkedEntity() != null) return;

            CaptureSelectionSnapshot(out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees);
            PruneDisconnectedSnapshot(ref offsets, ref blockCodes, ref blockEntityTrees);
            if (offsets.Length > 0) SetBoundsFromOffsets(offsets, ref localMin, ref localMax);
            if (SnapshotMatches(offsets, blockCodes)) return;

            snapshotOffsets = offsets;
            snapshotBlockCodes = blockCodes;
            snapshotBlockEntityTrees = blockEntityTrees;
            MarkDirty(false);
        }

        private static void MoveLinkedContraption(EntityVKContraption linked, double dx, double dy, double dz)
        {
            linked.ServerPos.X += dx;
            linked.ServerPos.Y += dy;
            linked.ServerPos.Z += dz;
            linked.Pos.SetFrom(linked.ServerPos);
            linked.PositionBeforeFalling.Set(linked.ServerPos.X, linked.ServerPos.InternalY, linked.ServerPos.Z);
        }

        private bool SnapshotMatches(Vec3i[] offsets, string[] blockCodes)
        {
            if (snapshotOffsets == null || snapshotBlockCodes == null) return false;
            if (offsets == null || blockCodes == null) return false;
            if (snapshotOffsets.Length != offsets.Length || snapshotBlockCodes.Length != blockCodes.Length) return false;

            for (int i = 0; i < offsets.Length; i++)
            {
                Vec3i current = snapshotOffsets[i];
                Vec3i next = offsets[i];
                if (current == null || next == null) return false;
                if (current.X != next.X || current.Y != next.Y || current.Z != next.Z) return false;
                if (snapshotBlockCodes[i] != blockCodes[i]) return false;
            }

            return true;
        }

        private static int PruneDisconnectedSnapshot(ref Vec3i[] offsets, ref string[] blockCodes)
        {
            TreeAttribute[] emptyBlockEntityTrees = Array.Empty<TreeAttribute>();
            return PruneDisconnectedSnapshot(ref offsets, ref blockCodes, ref emptyBlockEntityTrees);
        }

        private static int PruneDisconnectedSnapshot(ref Vec3i[] offsets, ref string[] blockCodes, ref TreeAttribute[] blockEntityTrees)
        {
            if (offsets == null || blockCodes == null)
            {
                offsets = Array.Empty<Vec3i>();
                blockCodes = Array.Empty<string>();
                blockEntityTrees = Array.Empty<TreeAttribute>();
                return 0;
            }

            int count = Math.Min(offsets.Length, blockCodes.Length);
            blockEntityTrees = NormalizeBlockEntityTrees(blockEntityTrees, count);
            int skippedToolTargets = RemoveContraptionToolTargetBlocks(ref offsets, ref blockCodes, ref blockEntityTrees, count);
            count = Math.Min(offsets.Length, blockCodes.Length);
            if (count <= 1)
            {
                if (count != offsets.Length || count != blockCodes.Length)
                {
                    Array.Resize(ref offsets, count);
                    Array.Resize(ref blockCodes, count);
                    Array.Resize(ref blockEntityTrees, count);
                }
                return skippedToolTargets;
            }

            Dictionary<string, int> indexByOffset = new Dictionary<string, int>();
            for (int i = 0; i < count; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null) continue;
                indexByOffset[OffsetKey(offset.X, offset.Y, offset.Z)] = i;
            }

            bool[] keep = FindControllerConnectedComponent(offsets, count, indexByOffset);
            int keptCount = CountKept(keep);
            if (keptCount == count && count == offsets.Length && count == blockCodes.Length) return 0;

            Vec3i[] keptOffsets = new Vec3i[keptCount];
            string[] keptCodes = new string[keptCount];
            TreeAttribute[] keptBlockEntityTrees = new TreeAttribute[keptCount];
            int writeIndex = 0;
            for (int i = 0; i < count; i++)
            {
                if (!keep[i]) continue;
                keptOffsets[writeIndex] = offsets[i];
                keptCodes[writeIndex] = blockCodes[i];
                keptBlockEntityTrees[writeIndex] = blockEntityTrees[i] ?? new TreeAttribute();
                writeIndex++;
            }

            offsets = keptOffsets;
            blockCodes = keptCodes;
            blockEntityTrees = keptBlockEntityTrees;
            return skippedToolTargets + count - keptCount;
        }

        private static int RemoveContraptionToolTargetBlocks(ref Vec3i[] offsets, ref string[] blockCodes, ref TreeAttribute[] blockEntityTrees, int count)
        {
            Dictionary<string, int> indexByOffset = new Dictionary<string, int>();
            for (int i = 0; i < count; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null) continue;
                indexByOffset[OffsetKey(offset.X, offset.Y, offset.Z)] = i;
            }

            HashSet<int> remove = new HashSet<int>();
            for (int i = 0; i < count; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null || string.IsNullOrEmpty(blockCodes[i])) continue;
                if (!TryGetContraptionToolFacing(blockCodes[i], out int dx, out int dy, out int dz)) continue;

                string targetKey = OffsetKey(offset.X + dx, offset.Y + dy, offset.Z + dz);
                if (indexByOffset.TryGetValue(targetKey, out int targetIndex))
                {
                    remove.Add(targetIndex);
                }
            }

            if (remove.Count == 0) return 0;

            int keptCount = count - remove.Count;
            Vec3i[] keptOffsets = new Vec3i[keptCount];
            string[] keptCodes = new string[keptCount];
            TreeAttribute[] keptTrees = new TreeAttribute[keptCount];
            int writeIndex = 0;
            for (int i = 0; i < count; i++)
            {
                if (remove.Contains(i)) continue;
                keptOffsets[writeIndex] = offsets[i];
                keptCodes[writeIndex] = blockCodes[i];
                keptTrees[writeIndex] = blockEntityTrees[i] ?? new TreeAttribute();
                writeIndex++;
            }

            offsets = keptOffsets;
            blockCodes = keptCodes;
            blockEntityTrees = keptTrees;
            return remove.Count;
        }

        private static bool TryGetContraptionToolFacing(string blockCode, out int dx, out int dy, out int dz)
        {
            dx = 0;
            dy = 0;
            dz = 0;

            string path = blockCode ?? "";
            int domainSep = path.IndexOf(':');
            if (domainSep >= 0) path = path.Substring(domainSep + 1);

            if (!path.StartsWith("contraptiondrill-", StringComparison.Ordinal)
                && !path.StartsWith("contraptionsaw-", StringComparison.Ordinal))
            {
                return false;
            }

            string side = path.Substring(path.LastIndexOf('-') + 1);
            switch (side)
            {
                case "n": dz = -1; return true;
                case "e": dx = 1; return true;
                case "s": dz = 1; return true;
                case "w": dx = -1; return true;
                case "u": dy = 1; return true;
                case "d": dy = -1; return true;
                default: return false;
            }
        }

        private static bool[] FindControllerConnectedComponent(Vec3i[] offsets, int count, Dictionary<string, int> indexByOffset)
        {
            bool[] keep = new bool[count];
            Queue<int> open = new Queue<int>();

            for (int i = 0; i < count; i++)
            {
                if (!IsControllerOffset(offsets[i]) && !IsFaceAdjacentToControllerOrTopAnchor(offsets[i])) continue;
                keep[i] = true;
                open.Enqueue(i);
            }

            while (open.Count > 0)
            {
                VisitNeighbors(offsets, open.Dequeue(), indexByOffset, keep, open);
            }

            return keep;
        }

        private static void VisitNeighbors(Vec3i[] offsets, int index, Dictionary<string, int> indexByOffset, bool[] keep, Queue<int> open)
        {
            Vec3i offset = offsets[index];
            if (offset == null) return;

            TryVisitKept(offset.X + 1, offset.Y, offset.Z, indexByOffset, keep, open);
            TryVisitKept(offset.X - 1, offset.Y, offset.Z, indexByOffset, keep, open);
            TryVisitKept(offset.X, offset.Y + 1, offset.Z, indexByOffset, keep, open);
            TryVisitKept(offset.X, offset.Y - 1, offset.Z, indexByOffset, keep, open);
            TryVisitKept(offset.X, offset.Y, offset.Z + 1, indexByOffset, keep, open);
            TryVisitKept(offset.X, offset.Y, offset.Z - 1, indexByOffset, keep, open);
        }

        private static void TryVisitKept(int x, int y, int z, Dictionary<string, int> indexByOffset, bool[] keep, Queue<int> open)
        {
            if (!indexByOffset.TryGetValue(OffsetKey(x, y, z), out int index)) return;
            if (keep[index]) return;

            keep[index] = true;
            open.Enqueue(index);
        }

        private static int CountKept(bool[] keep)
        {
            int count = 0;
            for (int i = 0; i < keep.Length; i++)
            {
                if (keep[i]) count++;
            }

            return count;
        }

        private static bool IsFaceAdjacentToControllerOrTopAnchor(Vec3i offset)
        {
            if (offset == null) return false;
            if (offset.X == 0 && offset.Y == -1 && offset.Z == 0) return false;
            if (Math.Abs(offset.X) + Math.Abs(offset.Y) + Math.Abs(offset.Z) == 1) return true;

            int dx = offset.X;
            int dy = offset.Y - 1;
            int dz = offset.Z;
            return Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz) == 1;
        }

        private static bool IsControllerOffset(Vec3i offset)
        {
            return offset != null && offset.X == 0 && offset.Y == 0 && offset.Z == 0;
        }

        private static string OffsetKey(int x, int y, int z)
        {
            return x + "," + y + "," + z;
        }

        private void RemoveSnapshotBlocksFromWorld()
        {
            if (snapshotOffsets == null) return;

            List<BlockPos> kineticPositions = new List<BlockPos>();
            for (int i = 0; i < snapshotOffsets.Length; i++)
            {
                BlockPos blockPos = WorldPosFromOffset(snapshotOffsets[i]);
                BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(blockPos);
                if (be?.GetBehavior<BEBehaviorKinetic>() != null)
                {
                    kineticPositions.Add(blockPos.Copy());
                }

                Api.World.BlockAccessor.SetBlock(0, blockPos);
                Api.World.BlockAccessor.MarkBlockDirty(blockPos);
            }

            KineticNetworkManager manager = Api.ModLoader.GetModSystem<KineticNetworkManager>();
            for (int i = 0; i < kineticPositions.Count; i++)
            {
                manager?.OnRemoved(kineticPositions[i]);
            }
        }

        private bool TryRestoreSnapshotBlocks(bool overwrite, EntityVKContraption contraption = null)
        {
            if (snapshotOffsets == null || snapshotBlockCodes == null) return false;

            int count = Math.Min(snapshotOffsets.Length, snapshotBlockCodes.Length);
            snapshotBlockEntityTrees = NormalizeBlockEntityTrees(snapshotBlockEntityTrees, count);
            BlockPos[] restorePositions = new BlockPos[count];
            HashSet<string> occupiedTargets = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(snapshotBlockCodes[i])) continue;

                Block block = Api.World.GetBlock(new AssetLocation(snapshotBlockCodes[i]));
                if (block == null || block.Id == 0) continue;

                BlockPos blockPos = WorldPosFromOffset(snapshotOffsets[i], contraption);
                restorePositions[i] = blockPos;
                if (blockPos == null) continue;
                if (!occupiedTargets.Add(WorldPositionKey(blockPos))) return false;

                Block existing = Api.World.BlockAccessor.GetBlock(blockPos);
                if (!overwrite && existing != null && existing.Id != 0) return false;
                if (overwrite && existing != null && existing.Id != 0)
                {
                    StorageRemovalCheck replacement = KineticStorageRemovalService.Check(
                        Api.World,
                        blockPos,
                        StorageRemovalKind.BlockReplacement);
                    if (!replacement.Allowed) return false;
                }
            }

            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(snapshotBlockCodes[i])) continue;

                Block block = Api.World.GetBlock(new AssetLocation(snapshotBlockCodes[i]));
                if (block == null || block.Id == 0) continue;

                BlockPos blockPos = restorePositions[i];
                if (blockPos == null) continue;
                Api.World.BlockAccessor.SetBlock(block.Id, blockPos);
                RestoreBlockEntityTree(blockPos, snapshotBlockEntityTrees[i]);
                Api.World.BlockAccessor.MarkBlockDirty(blockPos);
            }

            return true;
        }

        private static string WorldPositionKey(BlockPos pos)
        {
            return pos.dimension + ":" + pos.X + "," + pos.InternalY + "," + pos.Z;
        }

        private void RestoreBlockEntityTree(BlockPos blockPos, TreeAttribute savedTree)
        {
            if (savedTree == null || savedTree.Count == 0) return;

            BlockEntity be = Api.World.BlockAccessor.GetBlockEntity(blockPos);
            if (be == null) return;

            TreeAttribute tree = savedTree.Clone() as TreeAttribute ?? new TreeAttribute();
            tree.SetInt("posx", blockPos.X);
            tree.SetInt("posy", blockPos.InternalY);
            tree.SetInt("posz", blockPos.Z);
            be.FromTreeAttributes(tree, Api.World);
            be.MarkDirty(true);
        }

        private static TreeAttribute[] NormalizeBlockEntityTrees(TreeAttribute[] trees, int count)
        {
            if (count <= 0) return Array.Empty<TreeAttribute>();

            TreeAttribute[] normalized = new TreeAttribute[count];
            for (int i = 0; i < count; i++)
            {
                normalized[i] = i < (trees?.Length ?? 0) && trees[i] != null
                    ? trees[i]
                    : new TreeAttribute();
            }

            return normalized;
        }

        private BlockPos WorldPosFromOffset(Vec3i offset, EntityVKContraption contraption = null)
        {
            if (contraption != null)
            {
                return contraption.GetWorldBlockPositionForOffset(offset);
            }

            return new BlockPos(Pos.X + offset.X, Pos.Y + offset.Y, Pos.Z + offset.Z, Pos.dimension);
        }

        private static void NormalizeBounds(ref Vec3i min, ref Vec3i max)
        {
            min ??= new Vec3i(0, 1, 0);
            max ??= min.Clone();

            int minX = Math.Min(min.X, max.X);
            int minY = Math.Min(min.Y, max.Y);
            int minZ = Math.Min(min.Z, max.Z);
            int maxX = Math.Max(min.X, max.X);
            int maxY = Math.Max(min.Y, max.Y);
            int maxZ = Math.Max(min.Z, max.Z);

            min.Set(minX, minY, minZ);
            max.Set(maxX, maxY, maxZ);
        }

        private static void IncludeControllerInBounds(ref Vec3i min, ref Vec3i max)
        {
            NormalizeBounds(ref min, ref max);
            min.Set(Math.Min(min.X, 0), Math.Min(min.Y, 0), Math.Min(min.Z, 0));
            max.Set(Math.Max(max.X, 0), Math.Max(max.Y, 0), Math.Max(max.Z, 0));
        }

        private static void IncludeControllerAttachmentInBounds(ref Vec3i min, ref Vec3i max)
        {
            NormalizeBounds(ref min, ref max);
            min.Set(Math.Min(min.X, 0), Math.Min(min.Y, 1), Math.Min(min.Z, 0));
            max.Set(Math.Max(max.X, 0), Math.Max(max.Y, 1), Math.Max(max.Z, 0));
        }

        private static void Notify(IPlayer player, string message)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        private static string PlacementModeName(ContraptionPlacementMode mode)
        {
            return mode switch
            {
                ContraptionPlacementMode.OnlyPlaceNearInitialAngle => "Only place near initial angle",
                ContraptionPlacementMode.OnlyPlaceWhenAnchorDestroyed => "Only place when anchor destroyed",
                _ => "Always place when stopped"
            };
        }

        private static bool IsGantryShaft(Block block, out EnumKineticAxis axis)
        {
            axis = EnumKineticAxis.X;
            if (block?.Code == null) return false;
            if (block.Code.Domain != "vintagekinematics" || block.Code.FirstCodePart() != "gantryshaft") return false;

            string axisCode = block.Variant["axis"];
            if (axisCode == "y") axis = EnumKineticAxis.Y;
            else if (axisCode == "z") axis = EnumKineticAxis.Z;
            else axis = EnumKineticAxis.X;
            return true;
        }

        private static bool IsFluidBlock(Block block)
        {
            return block != null
                && (block.IsLiquid()
                    || !string.IsNullOrEmpty(block.LiquidCode)
                    || block.BlockMaterial == EnumBlockMaterial.Lava);
        }
    }
}
#pragma warning restore CS0618
