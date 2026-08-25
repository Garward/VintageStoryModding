using System.Collections.Generic;
using Vintagestory.API.Datastructures;

namespace VintageKinematics.Storage.Acceptance
{
    internal static class StorageAttributeInspector
    {
        public static bool ContainsItemStack(ITreeAttribute tree)
        {
            if (tree == null) return false;

            foreach (KeyValuePair<string, IAttribute> pair in tree)
            {
                // Vanilla initializes every empty backpack slot as an
                // ItemstackAttribute whose value is null. Only a real nested
                // stack is unsafe to virtualize.
                if (pair.Value is ItemstackAttribute stackAttribute)
                {
                    if (stackAttribute.value != null) return true;
                    continue;
                }
                if (pair.Value is ITreeAttribute child && ContainsItemStack(child)) return true;
            }

            return false;
        }
    }
}
