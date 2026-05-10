using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Declarative routing: which inventory slots a given block face exposes for push (input) and
    /// pull (output). Wires <see cref="InventoryGeneric.OnGetAutoPushIntoSlot"/> and
    /// <see cref="InventoryGeneric.OnGetAutoPullFromSlot"/> from the same configuration so adjacent
    /// pipes/funnels see a consistent face-restricted I/O surface, and exposes the declared output
    /// faces so a BE can iterate them when actively pushing into neighbours.
    /// </summary>
    public class IOFaceMap
    {
        private readonly Dictionary<BlockFacing, List<int>> inputs = new Dictionary<BlockFacing, List<int>>();
        private readonly Dictionary<BlockFacing, List<int>> outputs = new Dictionary<BlockFacing, List<int>>();

        /// <summary>Add an input slot accessible from <paramref name="face"/>. Multiple calls stack.</summary>
        public IOFaceMap MapInput(BlockFacing face, int slotId)
        {
            if (!inputs.TryGetValue(face, out var list))
            {
                list = new List<int>();
                inputs[face] = list;
            }
            if (!list.Contains(slotId)) list.Add(slotId);
            return this;
        }

        /// <summary>Add an output slot accessible from <paramref name="face"/>. Multiple calls stack.</summary>
        public IOFaceMap MapOutput(BlockFacing face, int slotId)
        {
            if (!outputs.TryGetValue(face, out var list))
            {
                list = new List<int>();
                outputs[face] = list;
            }
            if (!list.Contains(slotId)) list.Add(slotId);
            return this;
        }

        /// <summary>All faces that expose at least one output slot.</summary>
        public IEnumerable<BlockFacing> OutputFaces => outputs.Keys;

        /// <summary>Slot ids exposed as outputs on the given face. Empty if none.</summary>
        public IReadOnlyList<int> OutputSlotsFor(BlockFacing face)
        {
            return outputs.TryGetValue(face, out var list) ? list : System.Array.Empty<int>();
        }

        /// <summary>Slot ids exposed as inputs on the given face. Empty if none.</summary>
        public IReadOnlyList<int> InputSlotsFor(BlockFacing face)
        {
            return inputs.TryGetValue(face, out var list) ? list : System.Array.Empty<int>();
        }

        /// <summary>
        /// Wire <paramref name="inv"/>'s auto-push/pull delegates to honour this map. Push picks the
        /// first input slot for the face that can accept the source; pull picks the first non-empty
        /// output slot. Faces with no mapping return null, so adjacent pipes treat them as walls.
        /// </summary>
        public void Apply(InventoryGeneric inv)
        {
            inv.OnGetAutoPushIntoSlot = (face, fromSlot) =>
            {
                if (!inputs.TryGetValue(face, out var slotIds)) return null;
                foreach (int slotId in slotIds)
                {
                    ItemSlot slot = inv[slotId];
                    if (slot.Empty) return slot;
                    if (fromSlot != null && slot.Itemstack != null && fromSlot.Itemstack != null
                        && slot.Itemstack.Collectible == fromSlot.Itemstack.Collectible
                        && slot.Itemstack.StackSize < slot.Itemstack.Collectible.MaxStackSize)
                    {
                        return slot;
                    }
                }
                return null;
            };

            inv.OnGetAutoPullFromSlot = face =>
            {
                if (!outputs.TryGetValue(face, out var slotIds)) return null;
                foreach (int slotId in slotIds)
                {
                    ItemSlot slot = inv[slotId];
                    if (!slot.Empty) return slot;
                }
                return null;
            };
        }
    }
}
