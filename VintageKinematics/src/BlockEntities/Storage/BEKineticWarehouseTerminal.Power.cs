using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using VintageKinematics.Api;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Operations;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.BlockEntities.Storage
{
    /// <summary>Read-only operational power view. Power never gates repair or topology work.</summary>
    public partial class BEKineticWarehouseTerminal
    {
        private readonly HashSet<StorageTopologyPosition> poweredDrivePorts = new();
        private bool syncedPowerAvailable;

        public bool PowerRequirementEnabled =>
            Api?.ModLoader.GetModSystem<KineticConfigSystem>()?.Config?.StorageRequiresKineticPower
            ?? true;

        public float MinimumOperationalRPM => StoragePowerPolicy.NormalizeMinimumRPM(
            Api?.ModLoader.GetModSystem<KineticConfigSystem>()?.Config?.StorageMinimumRPM ?? 16f);

        public bool IsOperationallyPowered
        {
            get
            {
                if (!PowerRequirementEnabled) return true;
                return Api?.Side == EnumAppSide.Server
                    ? poweredDrivePorts.Count > 0
                    : syncedPowerAvailable;
            }
        }

        internal void UpdateDrivePortPower(BEKineticWarehousePort port, bool powered)
        {
            lock (persistenceSync)
            {
                UpdateDrivePortPowerSynchronized(port, powered);
            }
        }

        private void UpdateDrivePortPowerSynchronized(BEKineticWarehousePort port, bool powered)
        {
            if (Api?.Side != EnumAppSide.Server
                || port?.ControllerPos == null
                || !port.ControllerPos.Equals(Pos)
                || port.WarehouseId != WarehouseId
                || port.PortRole != StoragePortRole.ControllerAccess)
            {
                return;
            }

            StorageTopologyPosition position = WorldStorageTopologySource.FromBlockPos(port.Pos);
            if (!knownMembers.Contains(position)) powered = false;
            bool changed = powered
                ? poweredDrivePorts.Add(position)
                : poweredDrivePorts.Remove(position);
            if (!changed) return;
            syncedPowerAvailable = poweredDrivePorts.Count > 0;
            MarkDirty(true);
        }

        private void RefreshDrivePowerFromMembers()
        {
            if (Api?.Side != EnumAppSide.Server) return;
            poweredDrivePorts.Clear();
            int capacityCellCount = 0;
            foreach (StorageTopologyPosition memberPosition in knownMembers)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(
                    WorldStorageTopologySource.ToBlockPos(memberPosition))
                    is BEKineticWarehouseCell and not BEKineticWarehousePort)
                {
                    capacityCellCount++;
                }
            }
            foreach (StorageTopologyPosition memberPosition in knownMembers)
            {
                if (Api.World.BlockAccessor.GetBlockEntity(
                    WorldStorageTopologySource.ToBlockPos(memberPosition))
                    is not BEKineticWarehousePort port
                    || port.WarehouseId != WarehouseId
                    || port.PortRole != StoragePortRole.ControllerAccess)
                {
                    continue;
                }
                port.UpdateStructureStress(capacityCellCount);
                if (port.IsDrivePowered) poweredDrivePorts.Add(memberPosition);
            }
            syncedPowerAvailable = poweredDrivePorts.Count > 0;
            MarkDirty(true);
        }

        internal void WritePowerAttributes(Vintagestory.API.Datastructures.ITreeAttribute tree)
        {
            tree.SetBool("storagePowerAvailable", IsOperationallyPowered);
        }

        internal void ReadPowerAttributes(Vintagestory.API.Datastructures.ITreeAttribute tree)
        {
            syncedPowerAvailable = tree.GetBool("storagePowerAvailable", false);
        }
    }
}
