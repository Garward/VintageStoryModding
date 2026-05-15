using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Declarative routing: which inventory slots a given (world cell, face) pair exposes for
    /// push (input) and pull (output). Entries are keyed by absolute <see cref="BlockPos"/> so
    /// a multiblock controller can advertise IO on cells other than its own <c>Pos</c>; the
    /// cell-aware lookups are used by <see cref="InventoryPusher"/> and the funnel via
    /// <see cref="IFaceMappedContainer"/>. Entries on the constructor-supplied <c>defaultCell</c>
    /// are additionally exposed through the face-only <c>OnGetAutoPushIntoSlot</c> /
    /// <c>OnGetAutoPullFromSlot</c> delegates so vanilla pipes still see a sensible single-cell
    /// IO surface.
    /// </summary>
    public class IOFaceMap
    {
        private readonly BlockPos defaultCell;
        private readonly Dictionary<BlockPos, Dictionary<BlockFacing, List<int>>> inputs
            = new Dictionary<BlockPos, Dictionary<BlockFacing, List<int>>>();
        private readonly Dictionary<BlockPos, Dictionary<BlockFacing, List<int>>> outputs
            = new Dictionary<BlockPos, Dictionary<BlockFacing, List<int>>>();

        public IOFaceMap(BlockPos defaultCell)
        {
            this.defaultCell = defaultCell;
        }

        /// <summary>Add an input slot accessible from <paramref name="face"/> on the default cell.</summary>
        public IOFaceMap MapInput(BlockFacing face, int slotId) => MapInput(defaultCell, face, slotId);

        /// <summary>Add an input slot accessible from <paramref name="face"/> on a specific world cell.</summary>
        public IOFaceMap MapInput(BlockPos cell, BlockFacing face, int slotId)
        {
            Add(inputs, cell, face, slotId);
            return this;
        }

        /// <summary>Add an output slot accessible from <paramref name="face"/> on the default cell.</summary>
        public IOFaceMap MapOutput(BlockFacing face, int slotId) => MapOutput(defaultCell, face, slotId);

        /// <summary>Add an output slot accessible from <paramref name="face"/> on a specific world cell.</summary>
        public IOFaceMap MapOutput(BlockPos cell, BlockFacing face, int slotId)
        {
            Add(outputs, cell, face, slotId);
            return this;
        }

        private static void Add(Dictionary<BlockPos, Dictionary<BlockFacing, List<int>>> dict, BlockPos cell, BlockFacing face, int slotId)
        {
            if (cell == null || face == null) return;
            if (!dict.TryGetValue(cell, out var byFace))
            {
                byFace = new Dictionary<BlockFacing, List<int>>();
                dict[cell] = byFace;
            }
            if (!byFace.TryGetValue(face, out var list))
            {
                list = new List<int>();
                byFace[face] = list;
            }
            if (!list.Contains(slotId)) list.Add(slotId);
        }

        /// <summary>Faces on the default cell that expose at least one output slot.</summary>
        public IEnumerable<BlockFacing> OutputFaces
            => defaultCell != null && outputs.TryGetValue(defaultCell, out var byFace)
                ? (IEnumerable<BlockFacing>)byFace.Keys
                : Array.Empty<BlockFacing>();

        /// <summary>Output slots on the default cell for the given face.</summary>
        public IReadOnlyList<int> OutputSlotsFor(BlockFacing face) => SlotsAt(outputs, defaultCell, face);

        /// <summary>Input slots on the default cell for the given face.</summary>
        public IReadOnlyList<int> InputSlotsFor(BlockFacing face) => SlotsAt(inputs, defaultCell, face);

        private static IReadOnlyList<int> SlotsAt(Dictionary<BlockPos, Dictionary<BlockFacing, List<int>>> dict, BlockPos cell, BlockFacing face)
        {
            if (cell == null || face == null) return Array.Empty<int>();
            if (!dict.TryGetValue(cell, out var byFace)) return Array.Empty<int>();
            return byFace.TryGetValue(face, out var list) ? (IReadOnlyList<int>)list : Array.Empty<int>();
        }

        /// <summary>All output entries, regardless of cell. Use this when the owning BE iterates its
        /// outputs to actively push into neighbours.</summary>
        public IEnumerable<FaceMapEntry> OutputEntries => Enumerate(outputs);

        /// <summary>All input entries, regardless of cell.</summary>
        public IEnumerable<FaceMapEntry> InputEntries => Enumerate(inputs);

        private static IEnumerable<FaceMapEntry> Enumerate(Dictionary<BlockPos, Dictionary<BlockFacing, List<int>>> dict)
        {
            foreach (var cellPair in dict)
            {
                foreach (var facePair in cellPair.Value)
                {
                    yield return new FaceMapEntry(cellPair.Key, facePair.Key, facePair.Value);
                }
            }
        }

        /// <summary>
        /// Cell-aware push lookup: returns the first input slot mapped to (<paramref name="cell"/>,
        /// <paramref name="face"/>) that can accept <paramref name="fromSlot"/>. Null if either no
        /// mapping exists or every mapped slot is full / non-stackable with the source.
        /// </summary>
        public ItemSlot GetPushSlot(IInventory inv, BlockPos cell, BlockFacing face, ItemSlot fromSlot)
        {
            if (cell == null || face == null) return null;
            if (!inputs.TryGetValue(cell, out var byFace)) return null;
            if (!byFace.TryGetValue(face, out var slotIds)) return null;
            foreach (int slotId in slotIds)
            {
                ItemSlot slot = inv[slotId];
                if (fromSlot != null && !slot.CanHold(fromSlot)) continue;
                if (slot.Empty) return slot;
                if (fromSlot != null && slot.Itemstack != null && fromSlot.Itemstack != null
                    && slot.Itemstack.Collectible == fromSlot.Itemstack.Collectible
                    && slot.Itemstack.StackSize < slot.Itemstack.Collectible.MaxStackSize)
                {
                    return slot;
                }
            }
            return null;
        }

        /// <summary>
        /// Cell-aware pull lookup: returns the first non-empty output slot mapped to
        /// (<paramref name="cell"/>, <paramref name="face"/>), or null if either no mapping exists
        /// or every mapped slot is empty.
        /// </summary>
        public ItemSlot GetPullSlot(IInventory inv, BlockPos cell, BlockFacing face)
        {
            if (cell == null || face == null) return null;
            if (!outputs.TryGetValue(cell, out var byFace)) return null;
            if (!byFace.TryGetValue(face, out var slotIds)) return null;
            foreach (int slotId in slotIds)
            {
                ItemSlot slot = inv[slotId];
                if (!slot.Empty) return slot;
            }
            return null;
        }

        /// <summary>Any input entry exists at the given cell (across all faces).</summary>
        public bool HasInputAt(BlockPos cell) => cell != null && inputs.ContainsKey(cell);

        /// <summary>Any output entry exists at the given cell (across all faces).</summary>
        public bool HasOutputAt(BlockPos cell) => cell != null && outputs.ContainsKey(cell);

        /// <summary>
        /// Wire the inventory's face-only auto-push / auto-pull delegates to honour the default-cell
        /// entries. Non-default-cell mappings are unreachable via the face-only API and must be
        /// driven through <see cref="IFaceMappedContainer"/> instead.
        /// </summary>
        public void Apply(InventoryGeneric inv)
        {
            inv.OnGetAutoPushIntoSlot = (face, fromSlot) => GetPushSlot(inv, defaultCell, face, fromSlot);
            inv.OnGetAutoPullFromSlot = face => GetPullSlot(inv, defaultCell, face);
        }
    }

    /// <summary>One (cell, face, slotIds) triple emitted by <see cref="IOFaceMap.OutputEntries"/>
    /// or <see cref="IOFaceMap.InputEntries"/>.</summary>
    public readonly struct FaceMapEntry
    {
        public readonly BlockPos Cell;
        public readonly BlockFacing Face;
        public readonly IReadOnlyList<int> SlotIds;

        public FaceMapEntry(BlockPos cell, BlockFacing face, IReadOnlyList<int> slotIds)
        {
            Cell = cell;
            Face = face;
            SlotIds = slotIds;
        }
    }
}
