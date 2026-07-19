using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Attribute-aware identity for aggregating stored item stacks.
    /// Runtime ids are useful for fast in-memory lookup; code and class are the stable identity.
    /// </summary>
    public readonly struct ItemKey : IEquatable<ItemKey>
    {
        public static readonly ItemKey Empty = new ItemKey(EnumItemClass.Item, string.Empty, 0, 0);

        public readonly EnumItemClass ItemClass;
        public readonly string Code;
        public readonly int RuntimeCollectibleId;
        public readonly int AttributeHash;

        public ItemKey(EnumItemClass itemClass, string code, int runtimeCollectibleId, int attributeHash)
        {
            ItemClass = itemClass;
            Code = code ?? string.Empty;
            RuntimeCollectibleId = runtimeCollectibleId;
            AttributeHash = attributeHash;
        }

        public bool IsEmpty => string.IsNullOrEmpty(Code) && RuntimeCollectibleId == 0 && AttributeHash == 0;

        public static ItemKey FromStack(ItemStack stack)
        {
            if (stack?.Collectible == null) return Empty;

            int attrHash = stack.Attributes?.GetHashCode(GlobalConstants.IgnoredStackAttributes) ?? 0;
            return new ItemKey(
                stack.Class,
                stack.Collectible.Code?.ToString() ?? string.Empty,
                stack.Collectible.Id,
                attrHash);
        }

        public bool Equals(ItemKey other)
        {
            return ItemClass == other.ItemClass
                && RuntimeCollectibleId == other.RuntimeCollectibleId
                && AttributeHash == other.AttributeHash
                && string.Equals(Code, other.Code, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ItemKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (int)ItemClass;
                hash = hash * 31 + RuntimeCollectibleId;
                hash = hash * 31 + AttributeHash;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Code ?? string.Empty);
                return hash;
            }
        }

        public override string ToString()
        {
            return $"{ItemClass}:{Code}#{AttributeHash}";
        }

        public static bool operator ==(ItemKey left, ItemKey right) => left.Equals(right);
        public static bool operator !=(ItemKey left, ItemKey right) => !left.Equals(right);
    }
}
