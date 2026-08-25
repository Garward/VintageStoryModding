using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>Compact, shared hover information for every storage structure member.</summary>
    public abstract partial class BEKineticStorageMember
    {
        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder description)
        {
            base.GetBlockInfo(forPlayer, description);
            if (this is BEKineticWarehouseTerminal controller)
            {
                AppendControllerInfo(description, controller);
                return;
            }

            if (this is BEKineticWarehousePort port)
            {
                string portCode = port.Block?.Variant?["port"] ?? "beltinput";
                description.AppendLine(Lang.Get(
                    "vintagekinematics:storage-tooltip-port-" + portCode));
                description.AppendLine(Lang.Get(
                    "vintagekinematics:storage-tooltip-port-facing",
                    port.Facing.Code));
                if (port.PortRole != StoragePortRole.ControllerAccess)
                {
                    string mode = port.FilterWhitelist ? "whitelist" : "blacklist";
                    description.AppendLine(Lang.Get(
                        "vintagekinematics:storage-tooltip-port-filter",
                        Lang.Get("vintagekinematics:storage-filter-" + mode),
                        port.ConfiguredFilterCount,
                        port.FilterSlotCount,
                        port.FilterFuzzy
                            ? Lang.Get("vintagekinematics:storage-filter-fuzzy")
                            : Lang.Get("vintagekinematics:storage-filter-exact")));
                    if (port.PortRole == StoragePortRole.Export)
                    {
                        description.AppendLine(Lang.Get(
                            "vintagekinematics:storage-tooltip-output-rate",
                            port.MaximumOutputItemsPerSecond.ToString("0.##")));
                    }
                }
                else
                {
                    description.AppendLine(Lang.Get(
                        "vintagekinematics:storage-tooltip-drive-load",
                        port.CurrentDriveStressImpact.ToString("0.##"),
                        (port.CurrentDriveStressImpact * 16f).ToString("0.##")));
                }
            }
            else
            {
                description.AppendLine(Lang.Get(
                    "vintagekinematics:storage-tooltip-cell-capacity",
                    CapacityContribution.ToString("N0")));
            }
            if (ControllerPos == null)
            {
                description.AppendLine(Lang.Get("vintagekinematics:storage-tooltip-unlinked"));
                return;
            }

            if (Api?.World.BlockAccessor.GetBlockEntity(ControllerPos)
                is BEKineticWarehouseTerminal linkedController
                && linkedController.WarehouseId == WarehouseId)
            {
                AppendControllerInfo(description, linkedController);
            }
            else
            {
                description.AppendLine(Lang.Get("vintagekinematics:storage-tooltip-controller-unloaded"));
            }
        }

        private static void AppendControllerInfo(
            StringBuilder description,
            BEKineticWarehouseTerminal controller)
        {
            string state = Lang.Get(
                "vintagekinematics:storage-state-"
                + controller.StructureState.ToString().ToLowerInvariant());
            description.AppendLine(Lang.Get("vintagekinematics:storage-tooltip-state", state));
            description.AppendLine(Lang.Get(
                "vintagekinematics:storage-tooltip-shared-capacity",
                controller.SyncedStoredItems.ToString("N0"),
                controller.VerifiedItemCapacity.ToString("N0")));
            description.AppendLine(Lang.Get(
                "vintagekinematics:storage-tooltip-network",
                controller.KnownMembers.Count.ToString("N0"),
                controller.SyncedEntryCount.ToString("N0"),
                controller.EffectiveTypeCapacity.ToString("N0")));
            description.AppendLine(Lang.Get(
                controller.PowerRequirementEnabled
                    ? controller.IsOperationallyPowered
                        ? "vintagekinematics:storage-tooltip-power-online"
                        : "vintagekinematics:storage-tooltip-power-offline"
                    : "vintagekinematics:storage-tooltip-power-disabled"));
        }
    }
}
