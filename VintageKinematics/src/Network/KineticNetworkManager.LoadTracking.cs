using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageKinematics.Network
{
    /// <summary>
    /// Rebinds block-entity instances after chunk reload and retries registration until a
    /// newly loaded chunk has finished constructing its kinetic members.
    /// </summary>
    public partial class KineticNetworkManager
    {
        private const int LoadTrackingIntervalMs = 250;
        private const int LoadTrackingBatchSize = 256;
        private const long LoadTrackingTimeoutMs = 30_000;

        private readonly Dictionary<BlockPos, long> pendingLoadTracking = new();
        private ICoreServerAPI loadTrackingApi;
        private long loadTrackingListenerId;

        private void StartLoadTracking(ICoreServerAPI sapi)
        {
            loadTrackingApi = sapi;
            loadTrackingListenerId = sapi.Event.RegisterGameTickListener(
                ProcessPendingLoadTracking,
                LoadTrackingIntervalMs);
        }

        public void QueueTrackFromLoad(BlockPos pos)
        {
            if (api?.Side != EnumAppSide.Server || pos == null) return;

            long expiresAt = api.World.ElapsedMilliseconds + LoadTrackingTimeoutMs;
            lock (lockObj)
            {
                pendingLoadTracking[pos.Copy()] = expiresAt;
            }
        }

        private void ProcessPendingLoadTracking(float deltaTime)
        {
            List<KeyValuePair<BlockPos, long>> batch = new(LoadTrackingBatchSize);
            lock (lockObj)
            {
                foreach (KeyValuePair<BlockPos, long> entry in pendingLoadTracking)
                {
                    batch.Add(entry);
                    if (batch.Count >= LoadTrackingBatchSize) break;
                }
            }

            long now = api.World.ElapsedMilliseconds;
            foreach (KeyValuePair<BlockPos, long> entry in batch)
            {
                BlockPos pos = entry.Key;
                EnsureTrackedFromLoad(pos);

                bool finished;
                lock (lockObj)
                {
                    finished = posToNetwork.ContainsKey(pos);
                }
                if (!finished && now < entry.Value) continue;

                lock (lockObj)
                {
                    pendingLoadTracking.Remove(pos);
                }
            }
        }

        private bool TryRebindTrackedNode(BlockPos pos)
        {
            KineticNetwork network;
            KineticNode node;
            lock (lockObj)
            {
                if (!posToNetwork.TryGetValue(pos, out long networkId)
                    || !networks.TryGetValue(networkId, out network)
                    || !network.Nodes.TryGetValue(pos, out node))
                {
                    return false;
                }
            }

            return PropagateNodeState(network, pos, node);
        }

        public override void Dispose()
        {
            if (loadTrackingApi != null && loadTrackingListenerId != 0)
            {
                loadTrackingApi.Event.UnregisterGameTickListener(loadTrackingListenerId);
            }
            lock (lockObj)
            {
                pendingLoadTracking.Clear();
            }
            loadTrackingApi = null;
            loadTrackingListenerId = 0;
            base.Dispose();
        }
    }
}
