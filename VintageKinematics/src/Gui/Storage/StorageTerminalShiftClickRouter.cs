using System;
using Vintagestory.API.Common;

namespace VintageKinematics.Gui.Storage
{
    /// <summary>Routes inventory shift-clicks only while one storage terminal owns focus.</summary>
    internal static class StorageTerminalShiftClickRouter
    {
        private static object owner;
        private static System.Func<ItemSlot, bool> deposit;

        public static void Activate(object nextOwner, System.Func<ItemSlot, bool> handler)
        {
            owner = nextOwner;
            deposit = handler;
        }

        public static void Deactivate(object currentOwner)
        {
            if (!ReferenceEquals(owner, currentOwner)) return;
            owner = null;
            deposit = null;
        }

        public static bool TryDeposit(ItemSlot slot)
        {
            return deposit?.Invoke(slot) == true;
        }
    }
}
