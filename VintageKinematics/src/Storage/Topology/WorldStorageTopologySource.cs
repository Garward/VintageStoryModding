using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Read-only Vintage Story adapter. All topology decisions remain in the scanner.
    /// </summary>
    public sealed class WorldStorageTopologySource : IStorageTopologySource
    {
        private readonly IBlockAccessor blockAccessor;

        public WorldStorageTopologySource(IBlockAccessor blockAccessor)
        {
            this.blockAccessor = blockAccessor ?? throw new ArgumentNullException(nameof(blockAccessor));
        }

        public StorageTopologyChunk GetChunk(StorageTopologyPosition position)
        {
            int size = GlobalConstants.ChunkSize;
            return new StorageTopologyChunk(
                FloorDivide(position.X, size),
                FloorDivide(position.InternalY, size),
                FloorDivide(position.Z, size),
                position.Dimension);
        }

        public bool IsChunkLoaded(StorageTopologyChunk chunk)
        {
            return blockAccessor.GetChunk(chunk.X, chunk.Y, chunk.Z) != null;
        }

        public bool TryGetMember(
            StorageTopologyPosition position,
            out StorageMemberSnapshot member)
        {
            BlockPos blockPos = ToBlockPos(position);
            BlockEntity blockEntity = blockAccessor.GetBlockEntity(blockPos);
            if (blockEntity is not IVKStorageStructureMember structureMember)
            {
                member = null;
                return false;
            }

            Block block = blockAccessor.GetBlock(blockPos);
            member = new StorageMemberSnapshot(
                position,
                NullIfEmpty(structureMember.WarehouseId),
                ResolveRole(structureMember),
                structureMember.CapacityContribution,
                structureMember.TypeCapacityContribution,
                block?.Code?.ToString() ?? "unknown:storage-member",
                ToTopologyPosition(structureMember.ControllerPos));
            return true;
        }

        public bool IsConfirmedStorageMemberAbsent(StorageTopologyPosition position)
        {
            StorageTopologyChunk chunk = GetChunk(position);
            if (!IsChunkLoaded(chunk)) return false;

            BlockPos blockPos = ToBlockPos(position);
            if (blockAccessor.GetBlockEntity(blockPos) is IVKStorageStructureMember) return false;
            Block block = blockAccessor.GetBlock(blockPos);
            return block?.Attributes?["vkStorageMember"].AsBool(false) != true;
        }

        public static StorageTopologyPosition FromBlockPos(BlockPos position)
        {
            if (position == null) throw new ArgumentNullException(nameof(position));
            return new StorageTopologyPosition(
                position.X,
                position.InternalY,
                position.Z,
                position.dimension);
        }

        public static BlockPos ToBlockPos(StorageTopologyPosition position)
        {
            int localY = position.InternalY - position.Dimension * BlockPos.DimensionBoundary;
            return new BlockPos(position.X, localY, position.Z, position.Dimension);
        }

        private static StorageTopologyPosition? ToTopologyPosition(BlockPos position)
        {
            return position == null ? null : FromBlockPos(position);
        }

        private static StorageMemberRole ResolveRole(IVKStorageStructureMember member)
        {
            if (member.IsController) return StorageMemberRole.Controller;
            if (member is IVKStoragePort port)
            {
                return port.PortRole == StoragePortRole.Export
                    ? StorageMemberRole.ExportPort
                    : StorageMemberRole.ImportPort;
            }
            return StorageMemberRole.CapacityCell;
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        private static int FloorDivide(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }
    }
}
