using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Acceptance
{
    /// <summary>
    /// Configurable exact-match deny lists applied after the built-in safety checks.
    /// </summary>
    public sealed class StorageAcceptanceRules
    {
        private readonly HashSet<string> blockedCodes;
        private readonly HashSet<string> blockedClasses;

        public StorageAcceptanceRules(
            IEnumerable<string> blockedCodes = null,
            IEnumerable<string> blockedClasses = null)
        {
            this.blockedCodes = CopyNonEmpty(blockedCodes);
            this.blockedClasses = CopyNonEmpty(blockedClasses);
        }

        public bool IsBlocked(CollectibleObject collectible)
        {
            if (collectible == null) return true;

            string code = collectible.Code?.ToString();
            if (code != null && blockedCodes.Contains(code)) return true;

            Type type = collectible.GetType();
            return blockedClasses.Contains(type.Name)
                || (type.FullName != null && blockedClasses.Contains(type.FullName));
        }

        private static HashSet<string> CopyNonEmpty(IEnumerable<string> values)
        {
            HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
            if (values == null) return result;

            foreach (string value in values)
            {
                if (!string.IsNullOrWhiteSpace(value)) result.Add(value.Trim());
            }
            return result;
        }
    }
}
