using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VintageKinematics.Network
{
    /// <summary>
    /// Replays authoritative VK block entities after initial chunk delivery. This repairs clients
    /// that received the blocks before the server finished unpacking the chunk's block entities.
    /// </summary>
    public sealed class BlockEntityClientResyncSystem : ModSystem
    {
        private const int ReplayDelayMs = 1_000;
        private const int ReplayBatchSize = 128;
        private const int ReplayIntervalMs = 50;
        private const int ChunkScanBatchSize = 1;

        private readonly Queue<BlockPos> pending = new();
        private readonly HashSet<BlockPos> pendingPositions = new();
        private readonly Queue<PendingChunkScan> pendingChunkScans = new();
        private readonly HashSet<ChunkScanKey> pendingChunkPositions = new();
        private ICoreServerAPI serverApi;
        private long replayListenerId;
        private int repairedStatelessEntities;
        private int missingStatefulEntities;
        private int failedEntityRepairs;

        public override void StartServerSide(ICoreServerAPI api)
        {
            serverApi = api;
            api.Event.PlayerNowPlaying += OnPlayerNowPlaying;
            replayListenerId = api.Event.RegisterGameTickListener(ProcessReplayBatch, ReplayIntervalMs);
        }

        private void OnPlayerNowPlaying(IServerPlayer player)
        {
            serverApi.Event.RegisterCallback(_ => QueueNearbyLoadedEntities(player), ReplayDelayMs);
        }

        private void QueueNearbyLoadedEntities(IServerPlayer player)
        {
            if (player?.Entity == null || player.ConnectionState != EnumClientState.Playing) return;

            int chunkSize = GlobalConstants.ChunkSize;
            int centerX = (int)Math.Floor(player.Entity.Pos.X / chunkSize);
            int centerZ = (int)Math.Floor(player.Entity.Pos.Z / chunkSize);
            int dimension = player.Entity.Pos.Dimension;
            int dimensionOffset = dimension * GlobalConstants.DimensionSizeInChunks;
            int verticalChunks = serverApi.World.BlockAccessor.MapSizeY / chunkSize;
            int radius = GameMath.Clamp(player.CurrentChunkSentRadius, 0, 64);
            int queued = 0;
            int chunksQueued = 0;

            lock (pending)
            {
                for (int ring = 0; ring <= radius; ring++)
                {
                    for (int offsetX = -ring; offsetX <= ring; offsetX++)
                    {
                        for (int offsetZ = -ring; offsetZ <= ring; offsetZ++)
                        {
                            if (ring > 0 && Math.Max(Math.Abs(offsetX), Math.Abs(offsetZ)) != ring) continue;
                            int chunkX = centerX + offsetX;
                            int chunkZ = centerZ + offsetZ;

                            for (int chunkY = 0; chunkY < verticalChunks; chunkY++)
                            {
                                IWorldChunk chunk = serverApi.World.BlockAccessor.GetChunk(
                                    chunkX,
                                    chunkY + dimensionOffset,
                                    chunkZ);
                                if (chunk == null) continue;

                                ChunkScanKey key = new(chunkX, chunkY, chunkZ, dimension);
                                if (!chunk.Empty && pendingChunkPositions.Add(key))
                                {
                                    pendingChunkScans.Enqueue(new PendingChunkScan(key, chunk));
                                    chunksQueued++;
                                }

                                if (chunk.BlockEntities == null) continue;
                                foreach (BlockEntity blockEntity in chunk.BlockEntities.Values)
                                {
                                    if (blockEntity?.Block?.Code?.Domain != "vintagekinematics") continue;
                                    BlockPos pos = blockEntity.Pos.Copy();
                                    if (!pendingPositions.Add(pos)) continue;
                                    pending.Enqueue(pos);
                                    queued++;
                                }
                            }
                        }
                    }
                }
            }

            if (queued > 0 || chunksQueued > 0)
            {
                serverApi.Logger.Notification(
                    "[VintageKinematics] Queued {0} loaded block entities and {1} nonempty chunks for bounded client recovery after {2} joined.",
                    queued,
                    chunksQueued,
                    player.PlayerName);
            }
        }

        private void ProcessReplayBatch(float deltaTime)
        {
            for (int i = 0; i < ChunkScanBatchSize; i++)
            {
                PendingChunkScan scan;
                lock (pending)
                {
                    if (pendingChunkScans.Count == 0) break;
                    scan = pendingChunkScans.Dequeue();
                    pendingChunkPositions.Remove(scan.Key);
                }
                ScanChunkForMissingEntities(scan);
            }

            for (int i = 0; i < ReplayBatchSize; i++)
            {
                BlockPos pos;
                lock (pending)
                {
                    if (pending.Count == 0) break;
                    pos = pending.Dequeue();
                    pendingPositions.Remove(pos);
                }

                serverApi.World.BlockAccessor.GetBlockEntity(pos)?.MarkDirty();
            }

            ReportCompletedRecoveryScan();
        }

        private void ScanChunkForMissingEntities(PendingChunkScan scan)
        {
            if (scan.Chunk.Disposed || !scan.Chunk.Unpack_ReadOnly()) return;

            int chunkSize = GlobalConstants.ChunkSize;
            int baseX = scan.Key.X * chunkSize;
            int baseY = scan.Key.Y * chunkSize;
            int baseZ = scan.Key.Z * chunkSize;
            int blockCount = chunkSize * chunkSize * chunkSize;

            for (int index = 0; index < blockCount; index++)
            {
                int blockId = scan.Chunk.Data[index];
                if (blockId == 0) continue;

                Block block = serverApi.World.GetBlock(blockId);
                if (block?.Code?.Domain != "vintagekinematics" || string.IsNullOrEmpty(block.EntityClass)) continue;

                int localX = index % chunkSize;
                int localZ = index / chunkSize % chunkSize;
                int localY = index / (chunkSize * chunkSize);
                BlockPos pos = new(baseX + localX, baseY + localY, baseZ + localZ, scan.Key.Dimension);
                if (scan.Chunk.BlockEntities.ContainsKey(pos)) continue;

                if (!IsSafeStatelessEntity(block.EntityClass))
                {
                    missingStatefulEntities++;
                    continue;
                }

                try
                {
                    serverApi.World.BlockAccessor.SpawnBlockEntity(block.EntityClass, pos);
                    BlockEntity repaired = serverApi.World.BlockAccessor.GetBlockEntity(pos);
                    if (repaired == null)
                    {
                        failedEntityRepairs++;
                        continue;
                    }

                    repaired.MarkDirty(true);
                    repairedStatelessEntities++;
                }
                catch (Exception exception)
                {
                    failedEntityRepairs++;
                    serverApi.Logger.Error(
                        "[VintageKinematics] Failed to reconstruct {0} at {1}: {2}",
                        block.EntityClass,
                        pos,
                        exception.Message);
                }
            }
        }

        private static bool IsSafeStatelessEntity(string entityClass)
        {
            return entityClass == "Kinetic" || entityClass == "KineticAnimated";
        }

        private void ReportCompletedRecoveryScan()
        {
            lock (pending)
            {
                if (pendingChunkScans.Count > 0 || pending.Count > 0) return;
            }
            if (repairedStatelessEntities == 0 && missingStatefulEntities == 0 && failedEntityRepairs == 0) return;

            serverApi.Logger.Warning(
                "[VintageKinematics] Client recovery scan completed: rebuilt {0} stateless block entities, found {1} missing stateful block entities, {2} repairs failed. Stateful entities were left untouched.",
                repairedStatelessEntities,
                missingStatefulEntities,
                failedEntityRepairs);
            repairedStatelessEntities = 0;
            missingStatefulEntities = 0;
            failedEntityRepairs = 0;
        }

        public override void Dispose()
        {
            if (serverApi != null)
            {
                serverApi.Event.PlayerNowPlaying -= OnPlayerNowPlaying;
                if (replayListenerId != 0)
                {
                    serverApi.Event.UnregisterGameTickListener(replayListenerId);
                }
            }

            lock (pending)
            {
                pending.Clear();
                pendingPositions.Clear();
                pendingChunkScans.Clear();
                pendingChunkPositions.Clear();
            }

            replayListenerId = 0;
            serverApi = null;
            base.Dispose();
        }

        private readonly record struct ChunkScanKey(int X, int Y, int Z, int Dimension);

        private readonly record struct PendingChunkScan(ChunkScanKey Key, IWorldChunk Chunk);
    }
}
