using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Entities;
using VintageKinematics.Api.Storage;
using VintageKinematics.Storage;
using VintageKinematics.Storage.Recovery;

namespace VintageKinematics.Network
{
    public partial class KineticNetworkManager : ModSystem
    {
        public override double ExecuteOrder() => 0.5;

        private ICoreAPI api;
        private long nextNetworkId = 1;
        private readonly Dictionary<long, KineticNetwork> networks = new Dictionary<long, KineticNetwork>();
        private readonly Dictionary<BlockPos, long> posToNetwork = new Dictionary<BlockPos, long>();
        private readonly object lockObj = new object();
        private const long TransientStressTtlMs = 500;

        public event Action<KineticNetwork> NetworkBuilt;
        public event Action<KineticNetwork> NetworkRemoved;
        public event Action<KineticNetwork> NetworkConflictChanged;
        public event Action<KineticNetwork> NetworkStateChanged;

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
            StartLoadTracking(sapi);
            // Poll vanilla MP bridge sources ~4×/sec. Vanilla networks change speed continuously
            // (windmill catching wind, water wheel under load) and we have no event hook into
            // them, so we sample and push updates into VK only when the change is meaningful.
            sapi.Event.RegisterGameTickListener(_ => PollVanillaBridgeSources(), VanillaMPBridge.PollIntervalMs);
            sapi.Event.RegisterGameTickListener(_ => PurgeTransientStressLoads(), 250);
            sapi.ChatCommands
                .Create("vk")
                .WithDescription("Vintage Kinematics debug commands")
                .RequiresPrivilege(Vintagestory.API.Server.Privilege.controlserver)
                .BeginSubCommand("netinfo")
                    .WithDescription("Summarize kinetic networks; add 'verbose' for the complete listing")
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalWordRange("detail", "summary", "verbose"))
                    .HandleWith(HandleNetworkInfoCommand)
                .EndSubCommand()
                .BeginSubCommand("contraptionset")
                    .WithDescription("Emergency restore nearby VK contraption entities, overwriting target blocks")
                    .RequiresPlayer()
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("radius", 8))
                    .HandleWith(args =>
                    {
                        int radius = GameMath.Clamp((int)args[0], 1, 128);
                        EntityPos pos = args.Caller.Entity.Pos;
                        List<EntityVKContraption> contraptions = FindContraptionsNear(sapi, pos.XYZ, radius);
                        int restored = 0;
                        int failed = 0;

                        for (int i = 0; i < contraptions.Count; i++)
                        {
                            if (contraptions[i].TryAdminForceRestoreToWorld()) restored++;
                            else failed++;
                        }

                        sapi.Logger.Warning(
                            "[VintageKinematics] Admin {0} force-restored {1}/{2} contraption(s) within radius {3} at {4:F1},{5:F1},{6:F1}.",
                            args.Caller.Player?.PlayerName ?? args.Caller.FromChatGroupId.ToString(),
                            restored,
                            contraptions.Count,
                            radius,
                            pos.X,
                            pos.Y,
                            pos.Z);

                        string message = failed > 0
                            ? $"Force-restored {restored}/{contraptions.Count} contraption(s); {failed} failed."
                            : $"Force-restored {restored} contraption(s).";
                        return TextCommandResult.Success(message);
                    })
                .EndSubCommand()
                .BeginSubCommand("contraptiondelete")
                    .WithDescription("Emergency delete nearby VK contraption entities without restoring blocks")
                    .RequiresPlayer()
                    .WithArgs(sapi.ChatCommands.Parsers.OptionalInt("radius", 8))
                    .HandleWith(args =>
                    {
                        int radius = GameMath.Clamp((int)args[0], 1, 128);
                        EntityPos pos = args.Caller.Entity.Pos;
                        List<EntityVKContraption> contraptions = FindContraptionsNear(sapi, pos.XYZ, radius);

                        for (int i = 0; i < contraptions.Count; i++)
                        {
                            contraptions[i].AdminDeleteEntityOnly();
                        }

                        sapi.Logger.Warning(
                            "[VintageKinematics] Admin {0} deleted {1} contraption entity/entities without restore within radius {2} at {3:F1},{4:F1},{5:F1}.",
                            args.Caller.Player?.PlayerName ?? args.Caller.FromChatGroupId.ToString(),
                            contraptions.Count,
                            radius,
                            pos.X,
                            pos.Y,
                            pos.Z);

                        return TextCommandResult.Success($"Deleted {contraptions.Count} contraption entity/entities without restoring blocks.");
                    })
                .EndSubCommand()
                .BeginSubCommand("storagerecover")
                    .WithDescription("Inspect or explicitly converge divergent kinetic warehouse copies")
                    .RequiresPlayer()
                    .WithArgs(
                        sapi.ChatCommands.Parsers.OptionalInt("radius", 8),
                        sapi.ChatCommands.Parsers.OptionalWordRange("source", "controller", "recovery", "empty"),
                        sapi.ChatCommands.Parsers.OptionalWord("confirmation token"))
                    .HandleWith(args => StorageRecoveryCommands.HandleRecover(sapi, args))
                .EndSubCommand();
        }

        private static List<EntityVKContraption> FindContraptionsNear(Vintagestory.API.Server.ICoreServerAPI sapi, Vec3d center, int radius)
        {
            Entity[] entities = sapi.World.GetEntitiesAround(
                center,
                radius,
                radius,
                entity => entity is EntityVKContraption contraption && contraption.Alive && !contraption.SnapshotRestored);

            List<EntityVKContraption> contraptions = new List<EntityVKContraption>();
            for (int i = 0; i < entities.Length; i++)
            {
                if (entities[i] is EntityVKContraption contraption)
                {
                    contraptions.Add(contraption);
                }
            }

            return contraptions;
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

        public bool TryApplyTransientStress(BlockPos pos, string key, float stressImpact, out float localRpm)
        {
            localRpm = 0f;
            if (api?.Side != EnumAppSide.Server || pos == null || string.IsNullOrEmpty(key)) return false;

            KineticNetwork net = GetNetworkAt(pos);
            if (net == null || net.IsConflicted || MathF.Abs(net.SourceRPM) < KineticNetwork.MinAbsRPM) return false;
            if (!net.Nodes.TryGetValue(pos, out KineticNode node)) return false;

            float rawLocalRpm = net.ApplyRPMCap(net.SourceRPM * node.Ratio * node.Direction);
            if (MathF.Abs(rawLocalRpm) < KineticNetwork.MinAbsRPM) return false;

            net.TransientStressLoads[key] = new TransientStressLoad
            {
                StressUnits = MathF.Max(0f, stressImpact) * MathF.Abs(rawLocalRpm),
                ExpiresMs = api.World.ElapsedMilliseconds + TransientStressTtlMs
            };

            net.RecomputeStressForRPM(net.SourceRPM);
            PropagateNetworkState(net);

            if (net.IsOverstressed) return false;

            localRpm = MathF.Abs(rawLocalRpm);
            return true;
        }

        /// <summary>Updates a loaded consumer's persistent demand without rebuilding topology.</summary>
        public bool TryUpdateConsumerStressImpact(BlockPos pos, float stressImpact)
        {
            if (api?.Side != EnumAppSide.Server
                || pos == null
                || !float.IsFinite(stressImpact)
                || stressImpact < 0f)
            {
                return false;
            }

            BlockEntity blockEntity = api.World.BlockAccessor.GetBlockEntity(pos);
            BEBehaviorKinetic behavior = blockEntity?.GetBehavior<BEBehaviorKinetic>();
            if (behavior == null) return false;
            behavior.StressImpact = stressImpact;

            KineticNetwork net = GetNetworkAt(pos);
            if (net == null || !net.Nodes.TryGetValue(pos, out KineticNode node))
            {
                blockEntity.MarkDirty(true);
                return true;
            }

            node.StressImpact = stressImpact;
            net.Nodes[pos] = node;
            net.RecomputeStressForRPM(net.SourceRPM);
            PropagateNetworkState(net);
            return true;
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
                StorageRemovalCheck removal = KineticStorageRemovalService.Check(
                    api.World,
                    pos,
                    StorageRemovalKind.BlockReplacement);
                if (!removal.Allowed) return;
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
            ClearNetworkState(removed);
            NetworkRemoved?.Invoke(removed);
        }

        private void ClearNetworkState(KineticNetwork net)
        {
            foreach (BlockPos pos in net.Nodes.Keys)
            {
                BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos);
                Api.BEBehaviorKinetic beh = be?.GetBehavior<Api.BEBehaviorKinetic>();
                if (beh == null) continue;

                beh.ClearNetworkState();
                be.MarkDirty(true);

                if (be is IKineticConsumer cons)
                {
                    cons.OnNetworkRPMChanged(0f, null);
                }
            }
        }

        internal void NotifyConflictChanged(KineticNetwork net)
        {
            NetworkConflictChanged?.Invoke(net);
            NetworkStateChanged?.Invoke(net);
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
                    // Re-seed bridge metadata from the live vanilla source. Dynamic mode tracks
                    // torque continuously; sampled/fixed modes deliberately keep capacity stable.
                    node.VanillaNetworkId = vNetId;
                    if (VanillaMPBridge.Mode == VanillaMPBridge.BridgeMode.Dynamic)
                    {
                        node.SmoothedTorque = vTorque;
                        node.StressImpact = VanillaMPBridge.ComputeStressImpact(vTorque);
                    }
                    else if (VanillaMPBridge.Mode == VanillaMPBridge.BridgeMode.Fixed)
                    {
                        node.StressImpact = VanillaMPBridge.ComputeFixedStressImpact();
                    }
                    else if (MathF.Abs(node.StressImpact) < 0.0001f)
                    {
                        node.SmoothedTorque = vTorque;
                        node.StressImpact = VanillaMPBridge.ComputeStressImpact(vTorque);
                    }
                }
                else
                {
                    node.RatedRPM = 0f;
                }
                net.Nodes[pos] = node;
            }
        }

        // Chooses one stable global RPM anchor for the whole network. Multiple active sources
        // should contribute capacity through RatedRPM; they must not take turns rebuilding the
        // graph just because their tick callbacks fire at different times. Keep the current
        // source while it is still active, otherwise choose a deterministic source by position.
        private void ResolveSourceRPM(KineticNetwork net)
        {
            BlockPos chosenPos = null;
            float chosenRPM = 0f;

            if (net.SourcePos != null && TryGetImpliedSourceRPM(net, net.SourcePos, out chosenRPM))
            {
                chosenPos = net.SourcePos;
            }
            else
            {
                var keys = new System.Collections.Generic.List<BlockPos>(net.Nodes.Keys);
                keys.Sort(ComparePos);
                foreach (var pos in keys)
                {
                    if (!TryGetImpliedSourceRPM(net, pos, out chosenRPM)) continue;
                    chosenPos = pos;
                    break;
                }
            }

            net.SourcePos = chosenPos;
            net.SourceRPM = chosenPos == null ? 0f : chosenRPM;
        }

        private bool TryGetImpliedSourceRPM(KineticNetwork net, BlockPos pos, out float rpm)
        {
            rpm = 0f;
            if (pos == null || !net.Nodes.TryGetValue(pos, out KineticNode node)) return false;
            if (node.StressImpact >= 0f) return false;
            if (MathF.Abs(node.Ratio) < 0.0001f || node.Direction == 0) return false;

            float localRPM = 0f;
            BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos);
            var src = be?.GetBehavior<Api.BEBehaviorKineticSource>();
            if (src != null)
            {
                if (!src.IsActive) return false;
                localRPM = src.SignedTargetRPM;
            }
            else if (VanillaMPBridge.TryGetState(api.World, pos, out _, out float vRPM, out _, out _))
            {
                localRPM = vRPM;
            }

            if (MathF.Abs(localRPM) < 0.0001f) return false;

            rpm = localRPM / (node.Ratio * node.Direction);
            return MathF.Abs(rpm) >= 0.0001f;
        }

        private static int ComparePos(BlockPos left, BlockPos right)
        {
            int dim = left.dimension.CompareTo(right.dimension);
            if (dim != 0) return dim;
            int y = left.Y.CompareTo(right.Y);
            if (y != 0) return y;
            int x = left.X.CompareTo(right.X);
            if (x != 0) return x;
            return left.Z.CompareTo(right.Z);
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
            if (VanillaMPBridge.Mode == VanillaMPBridge.BridgeMode.Disabled) return;

            bool dynamicCapacity = VanillaMPBridge.Mode == VanillaMPBridge.BridgeMode.Dynamic;
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
                    bool netIdChanged = node.VanillaNetworkId != liveNetId;
                    float newImpact = node.StressImpact;
                    if (dynamicCapacity)
                    {
                        float alpha = VanillaMPBridge.TorqueSmoothing;
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
                        newImpact = VanillaMPBridge.ComputeStressImpact(node.SmoothedTorque);
                    }
                    else if (VanillaMPBridge.Mode == VanillaMPBridge.BridgeMode.Fixed)
                    {
                        newImpact = VanillaMPBridge.ComputeFixedStressImpact();
                    }

                    float newRated = MathF.Abs(liveRPM);
                    bool driftPastDeadband = dynamicCapacity
                        && MathF.Abs(MathF.Abs(node.StressImpact) - MathF.Abs(newImpact)) > VanillaMPBridge.CapacityPerTorque * TorqueDriftDeadband / VanillaMPBridge.StableRPM;
                    // Always keep RatedRPM live with the vanilla reading. Without this, a bridge
                    // node born at a moment Network.Speed=0 keeps RatedRPM=0 forever (RPM updates
                    // only flow through OnSourceChanged when |Δ| > 0.5, which never fires once
                    // the network steady-states). That bridge then wins dedupe and zeroes capacity
                    // despite a spinning windmill.
                    bool ratedChanged = MathF.Abs(node.RatedRPM - newRated) > 0.5f;
                    bool fixedCapacityChanged = VanillaMPBridge.Mode == VanillaMPBridge.BridgeMode.Fixed
                        && MathF.Abs(node.StressImpact - newImpact) > 0.0001f;
                    if (driftPastDeadband || netIdChanged || ratedChanged || fixedCapacityChanged)
                    {
                        node.StressImpact = newImpact;
                        node.VanillaNetworkId = liveNetId;
                        node.RatedRPM = newRated;
                        net.Nodes[vanillaPos] = node;
                        anyChange = true;
                    }
                    else
                    {
                        // Dynamic mode may have advanced SmoothedTorque without crossing the
                        // capacity deadband; persist the EMA history so it keeps integrating.
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

        private void PurgeTransientStressLoads()
        {
            if (api.Side != EnumAppSide.Server) return;

            long now = api.World.ElapsedMilliseconds;
            System.Collections.Generic.List<KineticNetwork> changed = null;
            lock (lockObj) { changed = new System.Collections.Generic.List<KineticNetwork>(networks.Values); }

            foreach (var net in changed)
            {
                if (net.TransientStressLoads.Count == 0) continue;

                bool removed = false;
                var keys = new System.Collections.Generic.List<string>(net.TransientStressLoads.Keys);
                foreach (string key in keys)
                {
                    if (net.TransientStressLoads[key].ExpiresMs > now) continue;
                    net.TransientStressLoads.Remove(key);
                    removed = true;
                }

                if (!removed) continue;
                net.RecomputeStressForRPM(net.SourceRPM);
                PropagateNetworkState(net);
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
                PropagateNodeState(net, kvp.Key, kvp.Value);
            }
            NetworkStateChanged?.Invoke(net);
        }

        private bool PropagateNodeState(KineticNetwork net, BlockPos pos, KineticNode node)
        {
            BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos);
            Api.BEBehaviorKinetic beh = be?.GetBehavior<Api.BEBehaviorKinetic>();
            if (beh == null) return false;

            beh.NetworkId = node.NetworkId;
            beh.Ratio = node.Ratio;
            beh.Direction = node.Direction;
            beh.PhaseOffset = node.PhaseOffset;
            beh.CurrentRPM = net.GetActualRPM(pos);
            beh.NetworkConflicted = net.IsConflicted || net.IsOverstressed;
            beh.NetStressTotal = net.StressTotal;
            beh.NetStressCapacity = net.StressCapacity;
            beh.NetOverstressed = net.IsOverstressed;
            beh.NetNodeCount = net.NodeCount;
            be.MarkDirty(true);
            beh.RaiseRPMChanged(beh.CurrentRPM);

            if (be is IKineticConsumer consumer)
            {
                consumer.OnNetworkRPMChanged(beh.CurrentRPM, net);
            }
            return true;
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
            if (TryRebindTrackedNode(pos)) return;

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

            RefreshSourceState(net);
            ResolveSourceRPM(net);
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

                RefreshSourceState(frag);
                ResolveSourceRPM(frag);
                frag.RecomputeStressForRPM(frag.SourceRPM);
                PropagateNetworkState(frag);

                StoreNetwork(frag);
            }
        }

        public void OnSourceChanged(BlockPos sourcePos, float newRPM)
        {
            KineticNetwork net = GetNetworkAt(sourcePos);
            if (net == null) return;

            RefreshSourceState(net);
            ResolveSourceRPM(net);
            net.RecomputeStressForRPM(net.SourceRPM);
            PropagateNetworkState(net);
        }
    }
}
