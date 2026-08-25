using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api.Storage;
using VintageKinematics.BlockEntities.Storage;

namespace VintageKinematics.Storage.Topology
{
    /// <summary>
    /// Retries loaded controllers when a nearby chunk column becomes available.
    /// </summary>
    public sealed class StorageTopologyRescanSystem : ModSystem
    {
        private readonly HashSet<BEKineticWarehouseTerminal> controllers =
            new HashSet<BEKineticWarehouseTerminal>();
        private ICoreServerAPI serverApi;

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);
            serverApi = api;
            api.Event.ChunkColumnLoaded += OnChunkColumnLoaded;
        }

        public void Register(BEKineticWarehouseTerminal controller)
        {
            if (controller != null) controllers.Add(controller);
        }

        public void Unregister(BEKineticWarehouseTerminal controller)
        {
            if (controller != null) controllers.Remove(controller);
        }

        public override void Dispose()
        {
            if (serverApi != null)
            {
                serverApi.Event.ChunkColumnLoaded -= OnChunkColumnLoaded;
            }
            controllers.Clear();
            base.Dispose();
        }

        private void OnChunkColumnLoaded(Vec2i chunkCoord, IWorldChunk[] chunks)
        {
            foreach (BEKineticWarehouseTerminal controller in controllers)
            {
                if (controller?.Api?.Side != EnumAppSide.Server || controller.Pos == null) continue;
                if (!CouldAffect(controller.Pos, chunkCoord)) continue;
                controller.RequestStructureRebuild(StorageChangeReason.ChunkLoaded);
            }
        }

        internal static bool CouldAffect(BlockPos controller, Vec2i loadedColumn)
        {
            int size = GlobalConstants.ChunkSize;
            int radius = StorageTopologyLimits.DefaultMaxGraphDistance;
            int minimumX = FloorDivide(controller.X - radius, size);
            int maximumX = FloorDivide(controller.X + radius, size);
            int minimumZ = FloorDivide(controller.Z - radius, size);
            int maximumZ = FloorDivide(controller.Z + radius, size);
            return loadedColumn.X >= minimumX
                && loadedColumn.X <= maximumX
                && loadedColumn.Y >= minimumZ
                && loadedColumn.Y <= maximumZ;
        }

        private static int FloorDivide(int value, int divisor)
        {
            int quotient = value / divisor;
            return value % divisor < 0 ? quotient - 1 : quotient;
        }
    }
}
