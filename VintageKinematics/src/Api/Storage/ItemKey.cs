using System;
using Vintagestory.API.Common;

namespace VintageKinematics.Api.Storage
{
    /// <summary>
    /// Coarse, attribute-aware lookup key for stored entries.
    ///
    /// This is not sufficient proof that two stacks may aggregate: AttributeHash can collide.
    /// Implementations must keep collision buckets and confirm candidates with
    /// <see cref="VKStorageKeys.CanAggregate"/>. Runtime ids are an in-session accelerator only;
    /// code and class are the stable collectible identity.
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

            int attrHash = stack.Attributes?.GetHashCode() ?? 0;
            return new ItemKey(
                stack.Class,
                stack.Collectible.Code?.ToString() ?? string.Empty,
                stack.Collectible.Id,
                attrHash);
        }

        public bool Equals(ItemKey other)
        {
            return ItemClass == other.ItemClass
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
