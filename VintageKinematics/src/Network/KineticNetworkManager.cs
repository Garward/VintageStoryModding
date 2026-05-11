using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.Network
{
    public class KineticNetworkManager : ModSystem
    {
        public override double ExecuteOrder() => 0.5;

        private ICoreAPI api;
        private long nextNetworkId = 1;
        private readonly Dictionary<long, KineticNetwork> networks = new Dictionary<long, KineticNetwork>();
        private readonly Dictionary<BlockPos, long> posToNetwork = new Dictionary<BlockPos, long>();
        private readonly object lockObj = new object();

        public event Action<KineticNetwork> NetworkBuilt;
        public event Action<KineticNetwork> NetworkRemoved;
        public event Action<KineticNetwork> NetworkConflictChanged;

        public override void Start(ICoreAPI api)
        {
            this.api = api;
            api.Logger.Notification("[VintageKinematics] KineticNetworkManager starting.");
        }

        private VintageKinematics.Rendering.ConflictParticleManager particleMgr;

        public override void StartClientSide(ICoreClientAPI capi)
        {
            base.StartClientSide(capi);
            particleMgr = new VintageKinematics.Rendering.ConflictParticleManager(capi);
            capi.Event.RegisterGameTickListener(_ => UpdateAndEmitConflictParticles(), 1000);
        }

        public override void StartServerSide(Vintagestory.API.Server.ICoreServerAPI sapi)
        {
            base.StartServerSide(sapi);
            // Poll vanilla MP bridge sources ~4×/sec. Vanilla networks change speed continuously
            // (windmill catching wind, water wheel under load) and we have no event hook into
            // them, so we sample and push updates into VK only when the change is meaningful.
            sapi.Event.RegisterGameTickListener(_ => PollVanillaBridgeSources(), 250);
            sapi.ChatCommands
                .Create("vk")
                .WithDescription("Vintage Kinematics debug commands")
                .RequiresPrivilege(Vintagestory.API.Server.Privilege.controlserver)
                .BeginSubCommand("netinfo")
                    .WithDescription("List all kinetic networks")
                    .HandleWith(args =>
                    {
                        var sb = new System.Text.StringBuilder();
                        lock (lockObj)
                        {
                            sb.AppendLine($"Networks: {networks.Count}");
                            foreach (var net in networks.Values)
                            {
                                sb.AppendLine($"  #{net.NetworkId}: {net.NodeCount} nodes, source={net.SourceRPM:F1} RPM, stress={net.StressTotal:F0}/{net.StressCapacity:F0}, conflict={net.IsConflicted}");
                                foreach (var kvp in net.Nodes)
                                {
                                    if (!kvp.Value.IsVanillaBridge) continue;
                                    var b = kvp.Value;
                                    sb.AppendLine($"    bridge @ {kvp.Key.X},{kvp.Key.Y},{kvp.Key.Z}: vNet={b.VanillaNetworkId}, smTorque={b.SmoothedTorque:F3}, ratedRPM={b.RatedRPM:F1}, impact={b.StressImpact:F1}");
                                }
                            }
                        }
                        return Vintagestory.API.Common.TextCommandResult.Success(sb.ToString());
                    })
                .EndSubCommand();
        }

        private void UpdateAndEmitConflictParticles()
        {
            var allConflicts = new System.Collections.Generic.List<BlockPos>();
            lock (lockObj)
            {
                foreach (var net in networks.Values)
                {
                    if (!net.IsConflicted) continue;
                    allConflicts.AddRange(net.ConflictPositions);
                }
            }
            particleMgr?.Update(allConflicts);
            particleMgr?.EmitOnTick();
        }

        public KineticNetwork GetNetwork(long id)
        {
            lock (lockObj)
            {
                networks.TryGetValue(id, out KineticNetwork net);
                return net;
            }
        }

        public KineticNetwork GetNetworkAt(BlockPos pos)
        {
            lock (lockObj)
            {
                if (!posToNetwork.TryGetValue(pos, out long id)) return null;
                networks.TryGetValue(id, out KineticNetwork net);
                return net;
            }
        }

        public long AllocateNetworkId()
        {
            lock (lockObj) { return nextNetworkId++; }
        }

        // Placement is allowed even if face-adjacent kinetic neighbors don't join the same
        // network. Two perpendicular shafts, a shaft next to a gearbox face that doesn't expose
        // a stub there, or a small cog flush against a large cog without the diagonal offset —
        // all valid placements that just shouldn't connect. The player sees connections form
        // (or not) visually; that's clearer than mysteriously dropping items. We still reject
        // overspeed/underspeed below since "this gear cannot run on this network" is a real
        // error, not a missed visual hint.

        // Returns the nominal RPM the source in this network produces when active. Used by
        // OnPlaced to validate that a new cog won't end up outside the [Min, Max] speed band.
        // Reads TargetRPM unconditionally (not gated on DecaySeconds) — the source might be
        // idle at placement time, but TargetRPM is its intrinsic output, which is what matters
        // for "would this cog overspeed/underspeed once cranked".
        private float ExpectedSourceRPM(KineticNetwork net)
        {
            foreach (var kvp in net.Nodes)
            {
                if (kvp.Value.StressImpact >= 0f) continue;
                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(kvp.Key);
                var src = be?.GetBehavior<Api.BEBehaviorKineticSource>();
                if (src == null) continue;
                return src.TargetRPM;
            }
            return 0f;
        }

        // Defer the actual world mutation: we're called from inside the placement flow, and
        // synchronously deleting our own block entity from there is unsafe. A short callback
        // lets the placement settle, then we remove the block and drop the item back.
        private void ScheduleRejectPlacement(BlockPos pos)
        {
            api.Event.RegisterCallback(_ =>
            {
                Block placed = api.World.BlockAccessor.GetBlock(pos);
                if (placed == null || placed.Id == 0) return;
                ItemStack drop = placed.OnPickBlock(api.World, pos) ?? new ItemStack(placed);
                api.World.BlockAccessor.SetBlock(0, pos);
                api.World.BlockAccessor.MarkBlockDirty(pos);
                api.World.SpawnItemEntity(drop, new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5));
            }, 50);
        }

        // After BuildFrom seeds phase at the BFS start, re-anchor the whole network to an
        // existing node's previously-stored phase. Relative offsets between nodes are
        // determined by edge offsets and so are preserved; only the global anchor shifts.
        // Result: every node that was already in the world keeps its old visual phase, and
        // newly-added nodes get a phase consistent with the chain they connected to.
        private void ReanchorPhase(KineticNetwork net, BlockPos excludePos)
        {
            float anchorDelta = 0f;
            bool found = false;
            foreach (var kvp in net.Nodes)
            {
                if (excludePos != null && kvp.Key.Equals(excludePos)) continue;
                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(kvp.Key);
                Api.BEBehaviorKinetic beh = be?.GetBehavior<Api.BEBehaviorKinetic>();
                if (beh == null) continue;
                anchorDelta = kvp.Value.PhaseOffset - beh.PhaseOffset;
                found = true;
                break;
            }
            if (!found || anchorDelta == 0f) return;

            var keys = new System.Collections.Generic.List<BlockPos>(net.Nodes.Keys);
            foreach (var k in keys)
            {
                var n = net.Nodes[k];
                n.PhaseOffset -= anchorDelta;
                net.Nodes[k] = n;
            }
        }

        internal void StoreNetwork(KineticNetwork net)
        {
            lock (lockObj)
            {
                networks[net.NetworkId] = net;
                foreach (var pos in net.Nodes.Keys) posToNetwork[pos] = net.NetworkId;
            }
            NetworkBuilt?.Invoke(net);
        }

        internal void RemoveNetwork(long id)
        {
            KineticNetwork removed;
            lock (lockObj)
            {
                if (!networks.TryGetValue(id, out removed)) return;
                networks.Remove(id);
                foreach (var pos in removed.Nodes.Keys) posToNetwork.Remove(pos);
            }
            NetworkRemoved?.Invoke(removed);
        }

        internal void NotifyConflictChanged(KineticNetwork net)
        {
            NetworkConflictChanged?.Invoke(net);
        }

        // Seeds net.SourceRPM from any active source's TargetRPM so freshly-built networks
        // (placement / split) report correct stress totals immediately, without waiting for
        // the next source tick (up to 1s lag). Vanilla MP bridge nodes (no VK source behavior)
        // contribute their live signed RPM.
        private void SeedSourceRPM(KineticNetwork net)
        {
            foreach (var kvp in net.Nodes)
            {
                if (kvp.Value.StressImpact >= 0f) continue;
                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(kvp.Key);
                var src = be?.GetBehavior<Api.BEBehaviorKineticSource>();
                if (src != null)
                {
                    if (!src.IsActive) continue;
                    net.SourceRPM = src.TargetRPM;
                    net.SourcePos = kvp.Key;
                    return;
                }
                if (VanillaMPBridge.TryGetState(api.World, kvp.Key, out _, out float vRPM, out _, out _) && vRPM != 0f)
                {
                    net.SourceRPM = vRPM;
                    net.SourcePos = kvp.Key;
                    return;
                }
            }
        }

        // Refreshes each source node's RatedRPM to reflect its current active state. An
        // unwound crank (DecaySeconds == 0) gets RatedRPM = 0 so it contributes no capacity,
        // regardless of whether some OTHER source is currently driving the network. Vanilla
        // bridges report their live |RPM| as rated capacity so a stalled windmill stops
        // contributing.
        private void RefreshSourceState(KineticNetwork net)
        {
            var keys = new System.Collections.Generic.List<BlockPos>(net.Nodes.Keys);
            foreach (var pos in keys)
            {
                var node = net.Nodes[pos];
                if (node.StressImpact >= 0f) continue;
                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos);
                var src = be?.GetBehavior<Api.BEBehaviorKineticSource>();
                if (src != null)
                {
                    node.RatedRPM = src.IsActive ? src.TargetRPM : 0f;
                }
                else if (VanillaMPBridge.TryGetState(api.World, pos, out _, out float vRPM, out float vTorque, out long vNetId))
                {
                    node.RatedRPM = MathF.Abs(vRPM);
                    // Re-seed the EMA history on rebuild (otherwise carries stale torque from
                    // before the network changed) and derive impact from that fresh reading.
                    node.VanillaNetworkId = vNetId;
                    node.SmoothedTorque = vTorque;
                    node.StressImpact = VanillaMPBridge.ComputeStressImpact(vTorque);
                }
                else
                {
                    node.RatedRPM = 0f;
                }
                net.Nodes[pos] = node;
            }
        }

        // Polls every network for vanilla MP bridge sources and pushes updates when either:
        // (a) live vanilla speed has zero-crossed or reversed sign — SourceRPM jumps to ±StableRPM
        //     or 0, since the bridge publishes a fixed magnitude regardless of vanilla speed; or
        // (b) live vanilla torque has drifted beyond a small deadband — capacity scales linearly
        //     with TotalAvailableTorque, so a 4-sail windmill genuinely outpowers a 1-sail.
        //
        // Bridge detection uses the IsVanillaBridge flag stamped at node-build time. Vanilla axle
        // removal does not notify VK, so the bridge node persists in net.Nodes after the BE is
        // gone; matching on a flag (not a live BE lookup) keeps the "axle removed mid-flight"
        // flush path below reachable. Without this, a broken vanilla axle would leave SourceRPM
        // frozen at its last polled value (+SU exploit).
        //
        // Reading a snapshot of the network list under lock and processing outside it avoids
        // holding the lock across BlockAccessor calls (which can be slow under load).
        private const float TorqueDriftDeadband = 0.05f; // ~25 SU at CapacityPerTorque=500

        private void PollVanillaBridgeSources()
        {
            if (api.Side != EnumAppSide.Server) return;
            System.Collections.Generic.List<KineticNetwork> snapshot;
            lock (lockObj) { snapshot = new System.Collections.Generic.List<KineticNetwork>(networks.Values); }

            foreach (var net in snapshot)
            {
                var bridgePositions = new List<BlockPos>();
                foreach (var kvp in net.Nodes)
                {
                    if (kvp.Value.IsVanillaBridge) bridgePositions.Add(kvp.Key);
                }
                if (bridgePositions.Count == 0) continue;

                bool anyChange = false;
                bool sourceFlushed = false;
                BlockPos firstLiveRPMChange = null;
                float firstLiveRPMValue = 0f;

                foreach (var vanillaPos in bridgePositions)
                {
                    if (!VanillaMPBridge.TryGetState(api.World, vanillaPos, out _, out float liveRPM, out float liveTorque, out long liveNetId))
                    {
                        // Vanilla axle was removed mid-flight; flush the source so consumers stop.
                        // The phantom bridge node stays in net.Nodes until the next adjacent VK
                        // placement/removal triggers a rebuild, but SourceRPM = 0 keeps the network
                        // idle in the meantime.
                        if (!sourceFlushed && net.SourceRPM != 0f)
                        {
                            OnSourceChanged(vanillaPos, 0f);
                            sourceFlushed = true;
                        }
                        continue;
                    }

                    // 0.5 RPM deadband — picks up the only meaningful RPM transitions (stopped ↔
                    // running, direction reversal) since the bridge publishes ±StableRPM otherwise.
                    // Only the first bridge's RPM change drives OnSourceChanged; all bridges on a
                    // shared vanilla network publish the same RPM, and different vanilla networks
                    // shouldn't both win the SourceRPM seat — pick one and stick with it.
                    if (firstLiveRPMChange == null && MathF.Abs(net.SourceRPM - liveRPM) > 0.5f)
                    {
                        firstLiveRPMChange = vanillaPos;
                        firstLiveRPMValue = liveRPM;
                    }

                    // Live torque tracking: EMA-blend the fresh reading into the bridge's
                    // SmoothedTorque, then derive capacity from the smoothed value. Raw vanilla
                    // wind/torque jitters per tick; smoothing flattens sub-second noise while
                    // still tracking the genuine wind drift on a ~1-2s timescale.
                    var node = net.Nodes[vanillaPos];
                    float alpha = VanillaMPBridge.TorqueSmoothing;
                    bool netIdChanged = node.VanillaNetworkId != liveNetId;
                    if (netIdChanged)
                    {
                        // Network swap (axle topology changed under us): drop history rather than
                        // blending a value from a different source.
                        node.SmoothedTorque = liveTorque;
                    }
                    else
                    {
                        node.SmoothedTorque = node.SmoothedTorque * (1f - alpha) + liveTorque * alpha;
                    }
                    float newImpact = VanillaMPBridge.ComputeStressImpact(node.SmoothedTorque);
                    float newRated = MathF.Abs(liveRPM);
                    bool driftPastDeadband = MathF.Abs(MathF.Abs(node.StressImpact) - MathF.Abs(newImpact)) > VanillaMPBridge.CapacityPerTorque * TorqueDriftDeadband / VanillaMPBridge.StableRPM;
                    // Always keep RatedRPM live with the vanilla reading. Without this, a bridge
                    // node born at a moment Network.Speed=0 keeps RatedRPM=0 forever (RPM updates
                    // only flow through OnSourceChanged when |Δ| > 0.5, which never fires once
                    // the network steady-states). That bridge then wins dedupe and zeroes capacity
                    // despite a spinning windmill.
                    bool ratedChanged = MathF.Abs(node.RatedRPM - newRated) > 0.5f;
                    if (driftPastDeadband || netIdChanged || ratedChanged)
                    {
                        node.StressImpact = newImpact;
                        node.VanillaNetworkId = liveNetId;
                        node.RatedRPM = newRated;
                        net.Nodes[vanillaPos] = node;
                        anyChange = true;
                    }
                    else
                    {
                        // SmoothedTorque advanced but didn't cross the deadband — still persist the
                        // EMA history so it keeps integrating across ticks instead of resetting.
                        net.Nodes[vanillaPos] = node;
                    }
                }

                if (firstLiveRPMChange != null)
                {
                    OnSourceChanged(firstLiveRPMChange, firstLiveRPMValue);
                }
                else if (anyChange)
                {
                    net.RecomputeStressForRPM(net.SourceRPM);
                    PropagateNetworkState(net);
                }
            }
        }

        // Propagates per-network state (RPM, stress totals, conflict/overstress flags) into
        // every BEBehaviorKinetic in the network and marks the block entity dirty so the
        // values reach the client. Call this after any change to network topology or source
        // state to keep client-side tooltips accurate.
        private void PropagateNetworkState(KineticNetwork net)
        {
            foreach (var kvp in net.Nodes)
            {
                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(kvp.Key);
                Api.BEBehaviorKinetic beh = be?.GetBehavior<Api.BEBehaviorKinetic>();
                if (beh == null) continue;

                beh.NetworkId = kvp.Value.NetworkId;
                beh.Ratio = kvp.Value.Ratio;
                beh.Direction = kvp.Value.Direction;
                beh.PhaseOffset = kvp.Value.PhaseOffset;
                beh.CurrentRPM = net.GetActualRPM(kvp.Key);
                beh.NetworkConflicted = net.IsConflicted || net.IsOverstressed;
                beh.NetStressTotal = net.StressTotal;
                beh.NetStressCapacity = net.StressCapacity;
                beh.NetOverstressed = net.IsOverstressed;
                beh.NetNodeCount = net.NodeCount;
                be.MarkDirty(true);
                beh.RaiseRPMChanged(beh.CurrentRPM);

                if (be is IKineticConsumer cons)
                {
                    cons.OnNetworkRPMChanged(beh.CurrentRPM, net);
                }
            }
        }

        public void OnPlaced(BlockPos pos)
        {
            if (api.Side != EnumAppSide.Server) return;
            var prov = new WorldNodeProvider(api.World);
            if (!prov.TryGetNode(pos, out _)) return;

            long newId = AllocateNetworkId();
            KineticNetwork net = NetworkBuilder.BuildFrom(prov, pos, newId);

            // Speed-bounds check: if the just-placed cog would land outside [MinAbsRPM, MaxAbsRPM]
            // at the source's intrinsic RPM, drop it as an item. Visible feedback for "you geared
            // it too far" — beats silently going idle. Only applies when there's an actual source
            // in the network (we have something to compare against). Skip when conflicted — the
            // network won't actually run, so "would overspeed" isn't a meaningful rejection.
            float expectedSrc = net.IsConflicted ? 0f : ExpectedSourceRPM(net);
            if (expectedSrc != 0f && net.Nodes.TryGetValue(pos, out KineticNode placed))
            {
                float rawRpm = MathF.Abs(expectedSrc * placed.Ratio * placed.Direction);
                if (rawRpm > net.MaxRPM || rawRpm < KineticNetwork.MinAbsRPM)
                {
                    ScheduleRejectPlacement(pos);
                    return;
                }
            }

            FinalizeAndStore(net, reanchorExcludePos: pos);
        }

        // Called from BEBehaviorKinetic when a kinetic block loads from a chunk and the manager
        // doesn't yet track its position. Networks are RAM-only state, so every save load and
        // chunk load needs to rebuild them lazily. Skips the placement-time rejection checks
        // (IsValidPlacement, speed-bounds) — those would unfairly drop saved blocks as items.
        // Idempotent: no-op when the position is already tracked.
        public void EnsureTrackedFromLoad(BlockPos pos)
        {
            if (api.Side != EnumAppSide.Server) return;
            lock (lockObj) { if (posToNetwork.ContainsKey(pos)) return; }

            var prov = new WorldNodeProvider(api.World);
            if (!prov.TryGetNode(pos, out _)) return;

            long newId = AllocateNetworkId();
            KineticNetwork net = NetworkBuilder.BuildFrom(prov, pos, newId);
            if (net.NodeCount == 0) return;

            FinalizeAndStore(net, reanchorExcludePos: null);
        }

        // Shared tail of OnPlaced and EnsureTrackedFromLoad: re-anchor phase to existing nodes,
        // remove any old network ids that this BFS swept up (merge), seed source state, recompute,
        // and store. <paramref name="reanchorExcludePos"/> is the freshly-placed pos for OnPlaced
        // (so we don't anchor against the brand-new node that has no committed phase yet) and
        // null for load-time rebuilds.
        private void FinalizeAndStore(KineticNetwork net, BlockPos reanchorExcludePos)
        {
            ReanchorPhase(net, excludePos: reanchorExcludePos);

            var neighborNetworkIds = new System.Collections.Generic.HashSet<long>();
            lock (lockObj)
            {
                foreach (var p in net.Nodes.Keys)
                {
                    if (posToNetwork.TryGetValue(p, out long oldId) && oldId != net.NetworkId)
                        neighborNetworkIds.Add(oldId);
                }
            }
            foreach (var oldId in neighborNetworkIds) RemoveNetwork(oldId);

            SeedSourceRPM(net);
            RefreshSourceState(net);
            net.ComputeMaxRPM();
            net.RecomputeStressForRPM(net.SourceRPM);
            PropagateNetworkState(net);

            StoreNetwork(net);
        }

        public void OnRemoved(BlockPos pos)
        {
            if (api.Side != EnumAppSide.Server) return;
            long oldId = 0;
            lock (lockObj)
            {
                if (!posToNetwork.TryGetValue(pos, out oldId)) return;
            }

            RemoveNetwork(oldId);

            var prov = new WorldNodeProvider(api.World, excludePos: pos);
            var visited = new System.Collections.Generic.HashSet<BlockPos>();
            int[] offsets = { -1, 0, 1 };
            foreach (int dx in offsets) foreach (int dy in offsets) foreach (int dz in offsets)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                BlockPos nbr = new BlockPos(pos.X + dx, pos.Y + dy, pos.Z + dz, pos.dimension);
                if (visited.Contains(nbr)) continue;
                if (!prov.TryGetNode(nbr, out _)) continue;

                long newId = AllocateNetworkId();
                KineticNetwork frag = NetworkBuilder.BuildFrom(prov, nbr, newId);
                if (frag.NodeCount == 0) continue;
                ReanchorPhase(frag, excludePos: null);
                foreach (var p in frag.Nodes.Keys) visited.Add(p);

                SeedSourceRPM(frag);
                RefreshSourceState(frag);
                frag.ComputeMaxRPM();
                frag.RecomputeStressForRPM(frag.SourceRPM);
                PropagateNetworkState(frag);

                StoreNetwork(frag);
            }
        }

        public void OnSourceChanged(BlockPos sourcePos, float newRPM)
        {
            KineticNetwork net = GetNetworkAt(sourcePos);
            if (net == null) return;
            net.SourceRPM = newRPM;
            net.SourcePos = sourcePos;
            RefreshSourceState(net);
            net.ComputeMaxRPM();
            net.RecomputeStressForRPM(newRPM);
            PropagateNetworkState(net);
        }
    }
}
