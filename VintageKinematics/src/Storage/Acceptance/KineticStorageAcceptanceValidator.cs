using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace VintageKinematics.Storage.Acceptance
{
    /// <summary>
    /// Release-one item safety policy. It rejects items whose state would need ticking,
    /// nested inventories, or specialized container behavior while stored virtually.
    /// </summary>
    public sealed class KineticStorageAcceptanceValidator : IStorageAcceptanceValidator
    {
        private readonly StorageAcceptanceRules rules;

        public KineticStorageAcceptanceValidator(StorageAcceptanceRules rules = null)
        {
            this.rules = rules ?? new StorageAcceptanceRules();
        }

        public StorageAcceptanceResult Validate(IWorldAccessor world, ItemStack stack, int requestedQuantity)
        {
            if (stack?.Collectible == null)
            {
                return StorageAcceptanceResult.Reject(StorageRejectionCodes.InvalidStack);
            }
            if (requestedQuantity <= 0 || stack.StackSize <= 0)
            {
                return StorageAcceptanceResult.Reject(StorageRejectionCodes.InvalidQuantity);
            }
            if (string.IsNullOrEmpty(stack.Collectible.Code?.ToString()))
            {
                return StorageAcceptanceResult.Reject(StorageRejectionCodes.MissingCode);
            }

            try
            {
                if (stack.Collectible.GetTransitionableProperties(world, stack, null)?.Length > 0)
                {
                    return StorageAcceptanceResult.Reject(StorageRejectionCodes.Transitioning);
                }
                if (HasTemperatureState(stack))
                {
                    return StorageAcceptanceResult.Reject(StorageRejectionCodes.Temperature);
                }
                if (StorageAttributeInspector.ContainsItemStack(stack.Attributes))
                {
                    return StorageAcceptanceResult.Reject(StorageRejectionCodes.NestedStack);
                }
                if (HasHeldBagContents(stack))
                {
                    return StorageAcceptanceResult.Reject(StorageRejectionCodes.Backpack);
                }
                if (HasLiquidContents(stack))
                {
                    return StorageAcceptanceResult.Reject(StorageRejectionCodes.LiquidContainer);
                }
                if (rules.IsBlocked(stack.Collectible))
                {
                    return StorageAcceptanceResult.Reject(StorageRejectionCodes.Blacklisted);
                }
            }
            catch
            {
                return StorageAcceptanceResult.Reject(StorageRejectionCodes.InspectionFailed);
            }

            return StorageAcceptanceResult.Allow();
        }

        private static bool HasTemperatureState(ItemStack stack)
        {
            return stack.Attributes?.HasAttribute("temperature") == true
                || stack.Attributes?.HasAttribute("timeFrozen") == true;
        }

        private static bool HasHeldBagContents(ItemStack stack)
        {
            IHeldBag heldBag = stack.Collectible.GetCollectibleInterface<IHeldBag>();
            return heldBag != null && !heldBag.IsEmpty(stack);
        }

        private static bool HasLiquidContents(ItemStack stack)
        {
            ILiquidInterface liquid = stack.Collectible.GetCollectibleInterface<ILiquidInterface>();
            if (liquid == null) return false;

            ItemStack content = liquid.GetContent(stack);
            return liquid.GetCurrentLitres(stack) > 0 || content != null;
        }
    }
}
