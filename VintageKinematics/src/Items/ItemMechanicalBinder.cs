using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;

namespace VintageKinematics.Items
{
    public class ItemMechanicalBinder : Item
    {
        private const string StartKey = "vkBinderStart";
        private const string EndKey = "vkBinderEnd";
        private const string BoxStartsKey = "vkBinderBoxStarts";
        private const string BoxEndsKey = "vkBinderBoxEnds";
        private const string RemoveBoxStartsKey = "vkBinderRemoveBoxStarts";
        private const string RemoveBoxEndsKey = "vkBinderRemoveBoxEnds";
        private const string OpBoxStartsKey = "vkBinderOpBoxStarts";
        private const string OpBoxEndsKey = "vkBinderOpBoxEnds";
        private const string OpBoxRemovesKey = "vkBinderOpBoxRemoves";
        private const string BaseSelectionKey = "vkBinderBaseSelection";
        private const string PendingRemoveKey = "vkBinderPendingRemove";
        private const string BoxDimensionKey = "vkBinderBoxDimension";
        private const string TargetKey = "vkBinderTarget";
        private const int MaxPreviewCells = 8192;
        private const int HighlightSlotId = 6510;
        private static bool clientHighlightsVisible;

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            if (slot?.Itemstack == null || byEntity == null || blockSel == null)
            {
                base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefaultAction;
            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;

            if (api.Side == EnumAppSide.Client)
            {
                SelectCorner(slot, byEntity, blockSel.Position, byPlayer);
                return;
            }

            if (!byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.BuildOrBreak))
            {
                return;
            }

            SelectCorner(slot, byEntity, blockSel.Position, byPlayer);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            if (!firstEvent || slot?.Itemstack == null || byEntity == null)
            {
                base.OnHeldInteractStart(slot, byEntity, blockSel, entitySel, firstEvent, ref handling);
                return;
            }

            handling = EnumHandHandling.PreventDefaultAction;
            IPlayer byPlayer = (byEntity as EntityPlayer)?.Player;
            bool sneak = byEntity.Controls?.Sneak == true;

            if (api.Side == EnumAppSide.Client)
            {
                if (!sneak || blockSel == null)
                {
                    ClearSelection(slot);
                    ClearHighlights(api as ICoreClientAPI);
                    return;
                }

                TrySetClientTarget(slot, byEntity.World, blockSel);
                return;
            }

            if (!sneak || blockSel == null)
            {
                ClearSelection(slot);
                Notify(byPlayer, "Mechanical Binder selection cleared.");
                return;
            }

            if (!byEntity.World.Claims.TryAccess(byPlayer, blockSel.Position, EnumBlockAccessFlags.Use))
            {
                return;
            }

            ITreeAttribute attr = slot.Itemstack.Attributes;
            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(byEntity.World, blockSel.Position);
            if (be is IContraptionController controller)
            {
                SetTarget(attr, be.Pos);
                slot.MarkDirty();

                if (HasAssignableSelection(attr))
                {
                    SeedBaseSelectionFromTargetIfNeeded(attr, byEntity.World, allowWithOperations: true);
                    List<BlockPos> positions = GetSelectedWorldPositions(attr, includePendingCompleteBox: true);
                    controller.SetSelectionFromWorldPositions(positions, byPlayer);
                    return;
                }

                Notify(byPlayer, "Mechanical Binder target set. Left click corners to add selection boxes, then sneak right click the carriage again.");
                return;
            }

            Notify(byPlayer, "Mechanical Binder: left click blocks to select corners, then sneak right click a contraption controller.");
        }

        public override WorldInteraction[] GetHeldInteractionHelp(ItemSlot inSlot)
        {
            return new[]
            {
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-mechanicalbinder-select",
                    MouseButton = EnumMouseButton.Left
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-mechanicalbinder-remove",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Left
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-mechanicalbinder-clear",
                    MouseButton = EnumMouseButton.Right,
                },
                new WorldInteraction
                {
                    ActionLangCode = "vintagekinematics:heldhelp-mechanicalbinder-assign",
                    HotKeyCode = "shift",
                    MouseButton = EnumMouseButton.Right,
                }
            };
        }

        public override void OnHeldIdle(ItemSlot slot, EntityAgent byEntity)
        {
            base.OnHeldIdle(slot, byEntity);
            if (api.Side != EnumAppSide.Client || slot?.Itemstack == null || byEntity == null) return;

            ICoreClientAPI capi = api as ICoreClientAPI;
            if (capi?.World?.Player == null) return;

            ITreeAttribute attr = slot.Itemstack.Attributes;
            BlockSelection currentSelection = capi.World.Player.CurrentBlockSelection;
            List<BlockPos> highlighted = null;
            List<BlockPos> candidates;
            string mode;
            BlockEntity be = currentSelection == null ? null : MultiblockHelper.GetMultiblockAwareBE(capi.World, currentSelection.Position);
            if (!HasLocalSelection(attr) && TryGetControllerPreviewPositions(capi.World, attr, be, out candidates, out mode))
            {
                highlighted = ExistingSelectedBlocks(capi.World, candidates);
            }
            else
            {
                candidates = GetSelectedWorldPositions(attr, includePendingCompleteBox: true);
                AddPendingHoverBox(attr, currentSelection, candidates);
                mode = "raw";
                if (TryGetTarget(attr, out BlockPos targetPos) && capi.World.BlockAccessor.GetBlockEntity(targetPos) is IContraptionController)
                {
                    highlighted = BEGantryCarriage.GetConnectedSelectionPreview(capi.World, targetPos, candidates);
                    mode = "stored-target";
                }
                else if (be is IContraptionController)
                {
                    SetTarget(attr, be.Pos);
                    highlighted = BEGantryCarriage.GetConnectedSelectionPreview(capi.World, be.Pos, candidates);
                    mode = "aimed-controller";
                }
                else if (TryFindSelectedController(capi.World, candidates, out BlockPos controllerPos))
                {
                    highlighted = BEGantryCarriage.GetConnectedSelectionPreview(capi.World, controllerPos, candidates);
                    mode = "selected-controller";
                }
            }

            if (candidates.Count == 0)
            {
                ClearHighlights(capi);
                return;
            }

            highlighted ??= ExistingSelectedBlocks(capi.World, candidates);
            if (highlighted.Count == 0)
            {
                ClearHighlights(capi);
                return;
            }

            List<int> colors = new List<int>(highlighted.Count);
            int color = ColorUtil.ColorFromRgba(80, 220, 120, 90);
            for (int i = 0; i < highlighted.Count; i++) colors.Add(color);
            capi.World.HighlightBlocks(capi.World.Player, HighlightSlotId, highlighted, colors, EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
            clientHighlightsVisible = true;
        }

        public static void OnClientTick(ICoreClientAPI capi)
        {
            if (!clientHighlightsVisible || capi?.World?.Player == null) return;

            ItemSlot slot = capi.World.Player.InventoryManager?.ActiveHotbarSlot;
            if (slot?.Itemstack?.Collectible is ItemMechanicalBinder) return;

            ClearHighlights(capi);
        }

        private static void SelectCorner(ItemSlot slot, EntityAgent byEntity, BlockPos pos, IPlayer byPlayer)
        {
            ITreeAttribute attr = slot.Itemstack.Attributes;
            bool removeMode = byEntity.Controls?.Sneak == true;

            if (!attr.HasAttribute(StartKey + "X"))
            {
                SeedBaseSelectionFromTargetIfNeeded(attr, byEntity.World, allowWithOperations: false);
                attr.SetBlockPos(StartKey, pos);
                attr.SetBool(PendingRemoveKey, removeMode);
                attr.RemoveAttribute(EndKey + "X");
                attr.RemoveAttribute(EndKey + "Y");
                attr.RemoveAttribute(EndKey + "Z");
                slot.MarkDirty();
                Notify(byPlayer, removeMode
                    ? $"Mechanical Binder removal start: {FormatPos(pos)}"
                    : $"Mechanical Binder start: {FormatPos(pos)}");
                return;
            }

            attr.SetBlockPos(EndKey, pos);
            bool removeBox = attr.GetBool(PendingRemoveKey) || removeMode;
            AddCompletedBox(attr, attr.GetBlockPos(StartKey), pos, removeBox);
            ClearPendingCorners(attr);
            slot.MarkDirty();
            Notify(byPlayer, removeBox
                ? $"Mechanical Binder removal box added: {GetRemoveBoxCount(attr)} removal box(es)."
                : $"Mechanical Binder box added: {GetBoxCount(attr)} box(es). Sneak right click a contraption controller to assign it, or left click another corner.");
        }

        private static bool HasAssignableSelection(ITreeAttribute attr)
        {
            return HasBaseSelection(attr)
                || GetBoxCount(attr) > 0
                || GetRemoveBoxCount(attr) > 0
                || (attr.HasAttribute(StartKey + "X") && attr.HasAttribute(EndKey + "X"));
        }

        private static void ClearSelection(ItemSlot slot)
        {
            ITreeAttribute attr = slot.Itemstack.Attributes;
            ClearPendingCorners(attr);
            attr.RemoveAttribute(BoxStartsKey);
            attr.RemoveAttribute(BoxEndsKey);
            attr.RemoveAttribute(RemoveBoxStartsKey);
            attr.RemoveAttribute(RemoveBoxEndsKey);
            attr.RemoveAttribute(OpBoxStartsKey);
            attr.RemoveAttribute(OpBoxEndsKey);
            attr.RemoveAttribute(OpBoxRemovesKey);
            attr.RemoveAttribute(BaseSelectionKey + "X");
            attr.RemoveAttribute(BaseSelectionKey + "Y");
            attr.RemoveAttribute(BaseSelectionKey + "Z");
            attr.RemoveAttribute(BoxDimensionKey);
            attr.RemoveAttribute(TargetKey + "X");
            attr.RemoveAttribute(TargetKey + "Y");
            attr.RemoveAttribute(TargetKey + "Z");
            slot.MarkDirty();
        }

        private static void ClearPendingCorners(ITreeAttribute attr)
        {
            attr.RemoveAttribute(StartKey + "X");
            attr.RemoveAttribute(StartKey + "Y");
            attr.RemoveAttribute(StartKey + "Z");
            attr.RemoveAttribute(EndKey + "X");
            attr.RemoveAttribute(EndKey + "Y");
            attr.RemoveAttribute(EndKey + "Z");
            attr.RemoveAttribute(PendingRemoveKey);
        }

        private static void AddCompletedBox(ITreeAttribute attr, BlockPos start, BlockPos end, bool remove)
        {
            if (start == null || end == null) return;
            EnsureLegacyBoxesMigrated(attr);

            int existingDimension = attr.GetInt(BoxDimensionKey, start.dimension);
            if (existingDimension != start.dimension || existingDimension != end.dimension)
            {
                attr.RemoveAttribute(BoxStartsKey);
                attr.RemoveAttribute(BoxEndsKey);
                attr.RemoveAttribute(RemoveBoxStartsKey);
                attr.RemoveAttribute(RemoveBoxEndsKey);
                attr.RemoveAttribute(OpBoxStartsKey);
                attr.RemoveAttribute(OpBoxEndsKey);
                attr.RemoveAttribute(OpBoxRemovesKey);
            }

            Vec3i[] starts = attr.GetVec3is(OpBoxStartsKey, System.Array.Empty<Vec3i>());
            Vec3i[] ends = attr.GetVec3is(OpBoxEndsKey, System.Array.Empty<Vec3i>());
            bool[] removes = (attr[OpBoxRemovesKey] as BoolArrayAttribute)?.value ?? System.Array.Empty<bool>();
            int count = System.Math.Min(starts.Length, System.Math.Min(ends.Length, removes.Length));

            Vec3i[] nextStarts = new Vec3i[count + 1];
            Vec3i[] nextEnds = new Vec3i[count + 1];
            bool[] nextRemoves = new bool[count + 1];
            for (int i = 0; i < count; i++)
            {
                nextStarts[i] = starts[i];
                nextEnds[i] = ends[i];
                nextRemoves[i] = removes[i];
            }

            nextStarts[count] = new Vec3i(start.X, start.Y, start.Z);
            nextEnds[count] = new Vec3i(end.X, end.Y, end.Z);
            nextRemoves[count] = remove;
            attr.SetVec3is(OpBoxStartsKey, nextStarts);
            attr.SetVec3is(OpBoxEndsKey, nextEnds);
            attr[OpBoxRemovesKey] = new BoolArrayAttribute(nextRemoves);
            attr.SetInt(BoxDimensionKey, start.dimension);
        }

        private static void EnsureLegacyBoxesMigrated(ITreeAttribute attr)
        {
            if (attr == null || attr.HasAttribute(OpBoxStartsKey + "X")) return;

            Vec3i[] addStarts = attr.GetVec3is(BoxStartsKey, System.Array.Empty<Vec3i>());
            Vec3i[] addEnds = attr.GetVec3is(BoxEndsKey, System.Array.Empty<Vec3i>());
            Vec3i[] removeStarts = attr.GetVec3is(RemoveBoxStartsKey, System.Array.Empty<Vec3i>());
            Vec3i[] removeEnds = attr.GetVec3is(RemoveBoxEndsKey, System.Array.Empty<Vec3i>());
            int addCount = System.Math.Min(addStarts.Length, addEnds.Length);
            int removeCount = System.Math.Min(removeStarts.Length, removeEnds.Length);
            int count = addCount + removeCount;
            if (count == 0) return;

            Vec3i[] starts = new Vec3i[count];
            Vec3i[] ends = new Vec3i[count];
            bool[] removes = new bool[count];
            int writeIndex = 0;
            for (int i = 0; i < addCount; i++)
            {
                starts[writeIndex] = addStarts[i];
                ends[writeIndex] = addEnds[i];
                removes[writeIndex] = false;
                writeIndex++;
            }

            for (int i = 0; i < removeCount; i++)
            {
                starts[writeIndex] = removeStarts[i];
                ends[writeIndex] = removeEnds[i];
                removes[writeIndex] = true;
                writeIndex++;
            }

            attr.SetVec3is(OpBoxStartsKey, starts);
            attr.SetVec3is(OpBoxEndsKey, ends);
            attr[OpBoxRemovesKey] = new BoolArrayAttribute(removes);
        }

        private static void TrySetClientTarget(ItemSlot slot, IWorldAccessor world, BlockSelection blockSel)
        {
            if (slot?.Itemstack == null || world == null || blockSel == null) return;

            BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(world, blockSel.Position);
            if (be is not IContraptionController) return;

            SetTarget(slot.Itemstack.Attributes, be.Pos);
            slot.MarkDirty();
        }

        private static void SeedBaseSelectionFromTargetIfNeeded(ITreeAttribute attr, IWorldAccessor world, bool allowWithOperations)
        {
            if (attr == null || world == null) return;
            if (attr.HasAttribute(BaseSelectionKey + "X")) return;
            if (!allowWithOperations && (GetBoxCount(attr) > 0 || GetRemoveBoxCount(attr) > 0)) return;
            if (!TryGetTarget(attr, out BlockPos targetPos)) return;
            if (world.BlockAccessor.GetBlockEntity(targetPos) is not IContraptionController controller) return;

            List<Vec3i> cells = new List<Vec3i>();
            int dimension = targetPos.dimension;
            foreach (BlockPos pos in controller.GetSelectionWorldPositions())
            {
                if (pos == null) continue;
                dimension = pos.dimension;
                cells.Add(new Vec3i(pos.X, pos.Y, pos.Z));
                if (cells.Count >= MaxPreviewCells) break;
            }

            if (cells.Count == 0) return;

            attr.SetVec3is(BaseSelectionKey, cells.ToArray());
            attr.SetInt(BoxDimensionKey, dimension);
        }

        private static bool TryGetControllerPreviewPositions(IWorldAccessor world, ITreeAttribute attr, BlockEntity aimedBe, out List<BlockPos> positions, out string mode)
        {
            positions = null;
            mode = "none";

            if (aimedBe is IContraptionController aimedController)
            {
                SetTarget(attr, aimedBe.Pos);
                positions = CopyControllerSelection(aimedController);
                mode = "aimed-controller-selection";
                return positions.Count > 0;
            }

            if (TryGetTarget(attr, out BlockPos targetPos)
                && world?.BlockAccessor?.GetBlockEntity(targetPos) is IContraptionController targetController)
            {
                positions = CopyControllerSelection(targetController);
                mode = "stored-target-selection";
                return positions.Count > 0;
            }

            return false;
        }

        private static List<BlockPos> CopyControllerSelection(IContraptionController controller)
        {
            List<BlockPos> positions = new List<BlockPos>();
            if (controller == null) return positions;

            foreach (BlockPos pos in controller.GetSelectionWorldPositions())
            {
                if (pos == null) continue;
                positions.Add(pos.Copy());
                if (positions.Count >= MaxPreviewCells) break;
            }

            return positions;
        }

        private static void SetTarget(ITreeAttribute attr, BlockPos pos)
        {
            if (attr == null || pos == null) return;
            attr.SetBlockPos(TargetKey, pos);
        }

        private static bool TryGetTarget(ITreeAttribute attr, out BlockPos pos)
        {
            pos = null;
            if (attr == null || !attr.HasAttribute(TargetKey + "X")) return false;

            pos = attr.GetBlockPos(TargetKey);
            return pos != null;
        }

        private static int GetBoxCount(ITreeAttribute attr)
        {
            if (attr?.HasAttribute(OpBoxStartsKey + "X") == true)
            {
                return CountOperations(attr, remove: false);
            }

            Vec3i[] starts = attr.GetVec3is(BoxStartsKey, System.Array.Empty<Vec3i>());
            Vec3i[] ends = attr.GetVec3is(BoxEndsKey, System.Array.Empty<Vec3i>());
            return System.Math.Min(starts.Length, ends.Length);
        }

        private static int GetRemoveBoxCount(ITreeAttribute attr)
        {
            if (attr?.HasAttribute(OpBoxStartsKey + "X") == true)
            {
                return CountOperations(attr, remove: true);
            }

            Vec3i[] starts = attr.GetVec3is(RemoveBoxStartsKey, System.Array.Empty<Vec3i>());
            Vec3i[] ends = attr.GetVec3is(RemoveBoxEndsKey, System.Array.Empty<Vec3i>());
            return System.Math.Min(starts.Length, ends.Length);
        }

        private static int CountOperations(ITreeAttribute attr, bool remove)
        {
            Vec3i[] starts = attr.GetVec3is(OpBoxStartsKey, System.Array.Empty<Vec3i>());
            Vec3i[] ends = attr.GetVec3is(OpBoxEndsKey, System.Array.Empty<Vec3i>());
            bool[] removes = (attr[OpBoxRemovesKey] as BoolArrayAttribute)?.value ?? System.Array.Empty<bool>();
            int count = System.Math.Min(starts.Length, System.Math.Min(ends.Length, removes.Length));
            int matches = 0;
            for (int i = 0; i < count; i++)
            {
                if (removes[i] == remove) matches++;
            }

            return matches;
        }

        private static List<BlockPos> GetSelectedWorldPositions(ITreeAttribute attr, bool includePendingCompleteBox)
        {
            List<BlockPos> positions = new List<BlockPos>();
            int dimension = attr.GetInt(BoxDimensionKey, 0);
            AddBaseSelectionPositions(attr, positions, dimension);

            if (attr.HasAttribute(OpBoxStartsKey + "X"))
            {
                Vec3i[] starts = attr.GetVec3is(OpBoxStartsKey, System.Array.Empty<Vec3i>());
                Vec3i[] ends = attr.GetVec3is(OpBoxEndsKey, System.Array.Empty<Vec3i>());
                bool[] removes = (attr[OpBoxRemovesKey] as BoolArrayAttribute)?.value ?? System.Array.Empty<bool>();
                int count = System.Math.Min(starts.Length, System.Math.Min(ends.Length, removes.Length));

                for (int i = 0; i < count && positions.Count < MaxPreviewCells; i++)
                {
                    if (removes[i])
                    {
                        RemoveBoxPositions(positions, starts[i], ends[i], dimension);
                    }
                    else
                    {
                        AddBoxPositions(positions, starts[i], ends[i], dimension);
                    }
                }
            }
            else
            {
                Vec3i[] starts = attr.GetVec3is(BoxStartsKey, System.Array.Empty<Vec3i>());
                Vec3i[] ends = attr.GetVec3is(BoxEndsKey, System.Array.Empty<Vec3i>());
                int count = System.Math.Min(starts.Length, ends.Length);

                for (int i = 0; i < count && positions.Count < MaxPreviewCells; i++)
                {
                    AddBoxPositions(positions, starts[i], ends[i], dimension);
                }

                ApplyRemovalBoxes(attr, positions, includePendingCompleteBox);
            }

            if (includePendingCompleteBox && attr.HasAttribute(StartKey + "X") && attr.HasAttribute(EndKey + "X") && !attr.GetBool(PendingRemoveKey))
            {
                BlockPos start = attr.GetBlockPos(StartKey);
                BlockPos end = attr.GetBlockPos(EndKey);
                AddBoxPositions(positions, start, end, start?.dimension ?? dimension);
            }

            if (attr.HasAttribute(OpBoxStartsKey + "X")
                && includePendingCompleteBox
                && attr.HasAttribute(StartKey + "X")
                && attr.HasAttribute(EndKey + "X")
                && attr.GetBool(PendingRemoveKey))
            {
                BlockPos start = attr.GetBlockPos(StartKey);
                BlockPos end = attr.GetBlockPos(EndKey);
                RemoveBoxPositions(positions, start, end, start?.dimension ?? dimension);
            }

            return positions;
        }

        private static void AddBaseSelectionPositions(ITreeAttribute attr, List<BlockPos> positions, int fallbackDimension)
        {
            Vec3i[] baseCells = attr.GetVec3is(BaseSelectionKey, System.Array.Empty<Vec3i>());
            for (int i = 0; i < baseCells.Length && positions.Count < MaxPreviewCells; i++)
            {
                Vec3i cell = baseCells[i];
                if (cell == null) continue;
                positions.Add(new BlockPos(cell.X, cell.Y, cell.Z, fallbackDimension));
            }
        }

        private static void AddPendingHoverBox(ITreeAttribute attr, BlockSelection currentSelection, List<BlockPos> positions)
        {
            if (currentSelection == null || positions == null || positions.Count >= MaxPreviewCells) return;
            if (!attr.HasAttribute(StartKey + "X") || attr.HasAttribute(EndKey + "X")) return;

            BlockPos start = attr.GetBlockPos(StartKey);
            if (attr.GetBool(PendingRemoveKey))
            {
                RemoveBoxPositions(positions, start, currentSelection.Position, start?.dimension ?? currentSelection.Position.dimension);
                return;
            }

            AddBoxPositions(positions, start, currentSelection.Position, start?.dimension ?? currentSelection.Position.dimension);
        }

        private static void ApplyRemovalBoxes(ITreeAttribute attr, List<BlockPos> positions, bool includePendingCompleteBox)
        {
            int dimension = attr.GetInt(BoxDimensionKey, 0);
            Vec3i[] starts = attr.GetVec3is(RemoveBoxStartsKey, System.Array.Empty<Vec3i>());
            Vec3i[] ends = attr.GetVec3is(RemoveBoxEndsKey, System.Array.Empty<Vec3i>());
            int count = System.Math.Min(starts.Length, ends.Length);

            for (int i = 0; i < count; i++)
            {
                RemoveBoxPositions(positions, starts[i], ends[i], dimension);
            }

            if (includePendingCompleteBox && attr.HasAttribute(StartKey + "X") && attr.HasAttribute(EndKey + "X") && attr.GetBool(PendingRemoveKey))
            {
                BlockPos start = attr.GetBlockPos(StartKey);
                BlockPos end = attr.GetBlockPos(EndKey);
                RemoveBoxPositions(positions, start, end, start?.dimension ?? dimension);
            }
        }

        private static void AddBoxPositions(List<BlockPos> positions, BlockPos start, BlockPos end, int dimension)
        {
            if (start == null || end == null) return;
            AddBoxPositions(
                positions,
                new Vec3i(start.X, start.Y, start.Z),
                new Vec3i(end.X, end.Y, end.Z),
                dimension);
        }

        private static void AddBoxPositions(List<BlockPos> positions, Vec3i start, Vec3i end, int dimension)
        {
            if (positions == null || start == null || end == null) return;

            int minX = System.Math.Min(start.X, end.X);
            int minY = System.Math.Min(start.Y, end.Y);
            int minZ = System.Math.Min(start.Z, end.Z);
            int maxX = System.Math.Max(start.X, end.X);
            int maxY = System.Math.Max(start.Y, end.Y);
            int maxZ = System.Math.Max(start.Z, end.Z);

            for (int y = minY; y <= maxY && positions.Count < MaxPreviewCells; y++)
            {
                for (int z = minZ; z <= maxZ && positions.Count < MaxPreviewCells; z++)
                {
                    for (int x = minX; x <= maxX && positions.Count < MaxPreviewCells; x++)
                    {
                        positions.Add(new BlockPos(x, y, z, dimension));
                    }
                }
            }
        }

        private static void RemoveBoxPositions(List<BlockPos> positions, BlockPos start, BlockPos end, int dimension)
        {
            if (start == null || end == null) return;
            RemoveBoxPositions(
                positions,
                new Vec3i(start.X, start.Y, start.Z),
                new Vec3i(end.X, end.Y, end.Z),
                dimension);
        }

        private static void RemoveBoxPositions(List<BlockPos> positions, Vec3i start, Vec3i end, int dimension)
        {
            if (positions == null || start == null || end == null || positions.Count == 0) return;

            int minX = System.Math.Min(start.X, end.X);
            int minY = System.Math.Min(start.Y, end.Y);
            int minZ = System.Math.Min(start.Z, end.Z);
            int maxX = System.Math.Max(start.X, end.X);
            int maxY = System.Math.Max(start.Y, end.Y);
            int maxZ = System.Math.Max(start.Z, end.Z);

            positions.RemoveAll(pos =>
                pos != null
                && pos.dimension == dimension
                && pos.X >= minX && pos.X <= maxX
                && pos.Y >= minY && pos.Y <= maxY
                && pos.Z >= minZ && pos.Z <= maxZ);
        }

        private static List<BlockPos> ExistingSelectedBlocks(IWorldAccessor world, List<BlockPos> candidates)
        {
            List<BlockPos> result = new List<BlockPos>();
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < candidates.Count && i < MaxPreviewCells; i++)
            {
                BlockPos pos = candidates[i];
                if (pos == null) continue;

                string key = pos.dimension + ":" + pos.X + "," + pos.Y + "," + pos.Z;
                if (!seen.Add(key)) continue;

                Block block = world.BlockAccessor.GetBlock(pos);
                if (block == null || block.Id == 0 || block.Code == null) continue;
                result.Add(pos.Copy());
            }

            return result;
        }

        private static bool TryFindSelectedController(IWorldAccessor world, List<BlockPos> candidates, out BlockPos controllerPos)
        {
            controllerPos = null;
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < candidates.Count && i < MaxPreviewCells; i++)
            {
                BlockPos pos = candidates[i];
                if (pos == null) continue;

                string key = pos.dimension + ":" + pos.X + "," + pos.Y + "," + pos.Z;
                if (!seen.Add(key)) continue;

                BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(world, pos);
                if (be is not IContraptionController) continue;

                controllerPos = be.Pos.Copy();
                return true;
            }

            return false;
        }

        private static void ClearHighlights(ICoreClientAPI capi)
        {
            if (capi == null) return;
            capi.World.HighlightBlocks(capi.World.Player, HighlightSlotId, new List<BlockPos>(), EnumHighlightBlocksMode.Absolute, EnumHighlightShape.Arbitrary);
            clientHighlightsVisible = false;
        }

        private static string FormatPos(BlockPos pos)
        {
            if (pos == null) return "null";
            return $"{pos.X}, {pos.Y}, {pos.Z}";
        }

        private static bool HasStart(ITreeAttribute attr)
        {
            return attr?.HasAttribute(StartKey + "X") == true;
        }

        private static bool HasBaseSelection(ITreeAttribute attr)
        {
            return attr?.HasAttribute(BaseSelectionKey + "X") == true;
        }

        private static bool HasLocalSelection(ITreeAttribute attr)
        {
            return HasBaseSelection(attr)
                || GetBoxCount(attr) > 0
                || GetRemoveBoxCount(attr) > 0
                || HasStart(attr);
        }

        private static void Notify(IPlayer player, string message)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }
    }
}
