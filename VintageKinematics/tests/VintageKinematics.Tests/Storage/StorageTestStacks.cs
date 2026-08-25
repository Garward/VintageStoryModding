using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Storage.Acceptance;

namespace VintageKinematics.Tests.Storage
{
    internal static class StorageTestStacks
    {
        public static ItemStack Create(string code, int quantity = 1, int maxStackSize = 64)
        {
            Item item = new Item
            {
                Code = new AssetLocation(code),
                MaxStackSize = maxStackSize
            };
            return new ItemStack(item, quantity);
        }

        public static bool ExactMatch(ItemStack left, ItemStack right)
        {
            return left?.Collectible?.Code?.Equals(right?.Collectible?.Code) == true
                && left.Class == right.Class
                && left.Attributes.Equals(null, right.Attributes);
        }
    }

    internal sealed class AcceptAllStorageValidator : IStorageAcceptanceValidator
    {
        public StorageAcceptanceResult Validate(IWorldAccessor world, ItemStack stack, int requestedQuantity)
        {
            return StorageAcceptanceResult.Allow();
        }
    }
}
