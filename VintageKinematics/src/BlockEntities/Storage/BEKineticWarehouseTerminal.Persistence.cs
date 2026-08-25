using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage.Topology;

namespace VintageKinematics.BlockEntities.Storage
{
    public partial class BEKineticWarehouseTerminal
    {
        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            lock (persistenceSync)
            {
                base.ToTreeAttributes(tree);
                WriteControllerStorageAttributes(tree);
                WriteControllerItemAttributes(tree);
                WritePowerAttributes(tree);
            }
        }

        internal void WriteControllerStorageAttributes(ITreeAttribute tree)
        {
            tree.SetInt(StorageBlockEntityKeys.StructureState, (int)StructureState);
            tree.SetLong(StorageBlockEntityKeys.ItemCapacity, VerifiedItemCapacity);
            tree.SetInt(StorageBlockEntityKeys.TypeCapacity, VerifiedTypeCapacity);
            tree.SetLong(StorageBlockEntityKeys.StoredItems, itemIndex?.StoredItems ?? SyncedStoredItems);
            tree.SetInt(StorageBlockEntityKeys.StoredEntryCount, itemIndex?.EntryCount ?? SyncedEntryCount);
            tree.SetInt(StorageBlockEntityKeys.KnownMemberCount, knownMembers.Count);
            for (int i = 0; i < knownMembers.Count; i++)
            {
                StorageTopologyPosition member = knownMembers[i];
                TreeAttribute stored = new TreeAttribute();
                stored.SetInt("x", member.X);
                stored.SetInt("y", member.InternalY);
                stored.SetInt("z", member.Z);
                stored.SetInt("dimension", member.Dimension);
                tree[StorageBlockEntityKeys.KnownMemberPrefix + i] = stored;
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            lock (persistenceSync)
            {
                base.FromTreeAttributes(tree, worldForResolving);
                ReadControllerStorageAttributes(tree);
                ReadControllerItemAttributes(tree);
                ReadPowerAttributes(tree);
            }
        }

        internal void ReadControllerStorageAttributes(ITreeAttribute tree)
        {
            StructureState = ReadState(tree.GetInt(
                StorageBlockEntityKeys.StructureState,
                (int)StorageState.StructureUnknown));
            VerifiedItemCapacity = System.Math.Max(
                0,
                tree.GetLong(StorageBlockEntityKeys.ItemCapacity));
            VerifiedTypeCapacity = System.Math.Max(
                0,
                tree.GetInt(StorageBlockEntityKeys.TypeCapacity));
            SyncedStoredItems = System.Math.Max(
                0,
                tree.GetLong(StorageBlockEntityKeys.StoredItems));
            SyncedEntryCount = System.Math.Max(
                0,
                tree.GetInt(StorageBlockEntityKeys.StoredEntryCount));

            knownMembers.Clear();
            int count = System.Math.Min(
                tree.GetInt(StorageBlockEntityKeys.KnownMemberCount),
                StorageTopologyLimits.DefaultMaxNonControllerMembers + 1);
            for (int i = 0; i < count; i++)
            {
                if (tree[StorageBlockEntityKeys.KnownMemberPrefix + i] is not ITreeAttribute stored)
                {
                    continue;
                }
                knownMembers.Add(new StorageTopologyPosition(
                    stored.GetInt("x"),
                    stored.GetInt("y"),
                    stored.GetInt("z"),
                    stored.GetInt("dimension")));
            }
        }

        private static StorageState ReadState(int value)
        {
            return System.Enum.IsDefined(typeof(StorageState), value)
                ? (StorageState)value
                : StorageState.StructureUnknown;
        }
    }
}
