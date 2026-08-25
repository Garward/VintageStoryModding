using Vintagestory.API.Common;

namespace VintageKinematics.Storage.Acceptance
{
    public interface IStorageAcceptanceValidator
    {
        StorageAcceptanceResult Validate(IWorldAccessor world, ItemStack stack, int requestedQuantity);
    }
}
