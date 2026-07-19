using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.BlockEntities;
using VintageKinematics.Rendering;

#pragma warning disable CS0618
namespace VintageKinematics.Entities
{
    public class EntityVKContraption : Entity
    {
        private const string AttrControllerPos = "vkControllerPos";
        private const string AttrCapturedBlockCount = "vkCapturedBlockCount";
        private const string AttrSnapshotId = "vkSnapshotId";
        private const string AttrLocalMin = "vkLocalMin";
        private const string AttrLocalMax = "vkLocalMax";
        private const string AttrSnapshotOffsets = "vkSnapshotOffsets";
        private const string AttrSnapshotBlockCodes = "vkSnapshotBlockCodes";
        private const string AttrSnapshotBlockEntityTrees = "vkSnapshotBlockEntityTrees";
        private const string AttrPlacementMode = "vkPlacementMode";
        private const string AttrInitialYaw = "vkInitialYaw";
        private const string AttrOwnerPlayerUid = "vkOwnerPlayerUid";
        private const string AttrOwnerPlayerName = "vkOwnerPlayerName";
        private const float InitialAngleRestoreToleranceRad = 23f * GameMath.DEG2RAD;
        private const double CollisionEpsilon = 0.001;
        private const double HorizontalCollisionSkin = 0.0078125;
        private const double TopSupportSinkTolerance = 0.125;
        private const double TopSupportHoverTolerance = 0.015625;
        private const double TopSupportSnapThreshold = 0.000001;
        private const int CollisionResolveIterations = 4;
        private const bool DebugCollision = false;
        private const long DebugCollisionIntervalMs = 250;
        private const double SneakSupportBelowTolerance = 0.35;
        private const double SneakSupportAboveTolerance = 0.45;
        private const double SneakEdgeTolerance = 0.35;
        private const double RestoreSupportBelowTolerance = 1.10;
        private const double RestoreSupportAboveTolerance = 0.25;
        private const double RestoreSupportHorizontalSkin = 0.05;
        private const double RestoreMovingSupportBelowTolerance = 1.35;
        private const double RestoreMovingSupportAboveTolerance = 0.65;
        private const double RestoreRecentSupportBelowTolerance = 4.0;
        private const double RestoreRecentSupportHorizontalSkin = 0.35;
        private const long RestoreRecentSupportMs = 2500;
        private const long RiderSupportGraceMs = 350;
        private const double RiderSupportHorizontalSkin = 0.15;
        private const double RiderAnchorHorizontalTolerance = 0.75;
        private const double RiderAnchorVerticalTolerance = 0.03125;

        public override bool IsInteractable => true;
        public override bool ApplyGravity => false;

        public BlockPos ControllerPos { get; private set; }
        public int CapturedBlockCount => WatchedAttributes.GetInt(AttrCapturedBlockCount);
        public string SnapshotId => WatchedAttributes.GetString(AttrSnapshotId);
        public string OwnerPlayerUid => WatchedAttributes.GetString(AttrOwnerPlayerUid);
        public string OwnerPlayerName => WatchedAttributes.GetString(AttrOwnerPlayerName);
        public ContraptionPlacementMode PlacementMode => (ContraptionPlacementMode)WatchedAttributes.GetInt(AttrPlacementMode, (int)ContraptionPlacementMode.AlwaysPlaceWhenStopped);
        private Vec3i localMin = new Vec3i(0, 1, 0);
        private Vec3i localMax = new Vec3i(0, 1, 0);
        private Vec3i[] snapshotOffsets = new[] { new Vec3i(0, 1, 0) };
        private readonly Dictionary<long, Vec3d> lastEntityPositions = new Dictionary<long, Vec3d>();
        private readonly Dictionary<long, long> lastCollisionDebugMs = new Dictionary<long, long>();
        private readonly Dictionary<long, Vec3d> lastSupportContraptionPositions = new Dictionary<long, Vec3d>();
        private readonly Dictionary<long, Vec3d> recentSupportContraptionPositions = new Dictionary<long, Vec3d>();
        private readonly Dictionary<long, long> recentSupportMs = new Dictionary<long, long>();
        private readonly Dictionary<long, RiderSupportState> riderSupportStates = new Dictionary<long, RiderSupportState>();
        private readonly Dictionary<long, Entity> hookedEntities = new Dictionary<long, Entity>();
        private readonly Dictionary<long, Action> afterPhysicsHooks = new Dictionary<long, Action>();
        private readonly HashSet<long> entitiesSeenThisTick = new HashSet<long>();
        private readonly Dictionary<string, long> movementPauseUntilMs = new Dictionary<string, long>();
        private readonly Dictionary<string, float> workProgress = new Dictionary<string, float>();
        private readonly Dictionary<string, long> workVisualPulseMs = new Dictionary<string, long>();
        private readonly Dictionary<string, float> workVisualProgress = new Dictionary<string, float>();
        private long lastWorkSoundMs;
        private long lastWorkSoundTick = -1;
        private string movementPauseReason;
        private ContraptionEntityRenderer renderer;
        private ICoreAPI api;
        private ICoreClientAPI capi;
        private bool snapshotRestored;

        public bool SnapshotRestored => snapshotRestored;

        private sealed class RiderSupportState
        {
            public Vec3d LocalEntityPos;
            public long LastSupportMs;
        }

        public void Configure(BlockPos controllerPos, int capturedBlockCount)
        {
            Configure(controllerPos, new Vec3i(0, 1, 0), new Vec3i(0, 1, 0), capturedBlockCount);
        }

        public void Configure(BlockPos controllerPos, Vec3i localMin, Vec3i localMax, int capturedBlockCount)
        {
            Configure(controllerPos, localMin, localMax, new[] { new Vec3i(0, 1, 0) }, Array.Empty<string>(), capturedBlockCount);
        }

        public void Configure(BlockPos controllerPos, Vec3i localMin, Vec3i localMax, Vec3i[] snapshotOffsets, string[] snapshotBlockCodes, int capturedBlockCount)
        {
            Configure(controllerPos, localMin, localMax, snapshotOffsets, snapshotBlockCodes, Array.Empty<TreeAttribute>(), capturedBlockCount);
        }

        public void Configure(BlockPos controllerPos, Vec3i localMin, Vec3i localMax, Vec3i[] snapshotOffsets, string[] snapshotBlockCodes, TreeAttribute[] snapshotBlockEntityTrees, int capturedBlockCount)
        {
            Configure(controllerPos, localMin, localMax, snapshotOffsets, snapshotBlockCodes, snapshotBlockEntityTrees, capturedBlockCount, ContraptionPlacementMode.AlwaysPlaceWhenStopped);
        }

        public void Configure(BlockPos controllerPos, Vec3i localMin, Vec3i localMax, Vec3i[] snapshotOffsets, string[] snapshotBlockCodes, TreeAttribute[] snapshotBlockEntityTrees, int capturedBlockCount, ContraptionPlacementMode placementMode, string ownerPlayerUid = null, string ownerPlayerName = null)
        {
            ControllerPos = controllerPos?.Copy();
            if (ControllerPos != null)
            {
                WatchedAttributes.SetBlockPos(AttrControllerPos, ControllerPos);
            }

            NormalizeBounds(ref localMin, ref localMax);
            WatchedAttributes.SetVec3i(AttrLocalMin, localMin);
            WatchedAttributes.SetVec3i(AttrLocalMax, localMax);
            WatchedAttributes.SetVec3is(AttrSnapshotOffsets, snapshotOffsets ?? Array.Empty<Vec3i>());
            WatchedAttributes[AttrSnapshotBlockCodes] = new StringArrayAttribute(snapshotBlockCodes ?? Array.Empty<string>());
            WatchedAttributes[AttrSnapshotBlockEntityTrees] = new TreeArrayAttribute(NormalizeBlockEntityTrees(snapshotBlockEntityTrees, snapshotBlockCodes?.Length ?? 0));
            WatchedAttributes.SetInt(AttrCapturedBlockCount, capturedBlockCount);
            WatchedAttributes.SetInt(AttrPlacementMode, (int)placementMode);
            WatchedAttributes.SetFloat(AttrInitialYaw, (float)SidedPos.Yaw);
            if (!string.IsNullOrEmpty(ownerPlayerUid)) WatchedAttributes.SetString(AttrOwnerPlayerUid, ownerPlayerUid);
            if (!string.IsNullOrEmpty(ownerPlayerName)) WatchedAttributes.SetString(AttrOwnerPlayerName, ownerPlayerName);
            if (string.IsNullOrEmpty(SnapshotId) && ControllerPos != null)
            {
                WatchedAttributes.SetString(AttrSnapshotId, GameMath.MurmurHash3Mod(ControllerPos.X, ControllerPos.Y, ControllerPos.Z, int.MaxValue).ToString("x"));
            }

            ApplySnapshotCollisionBounds(localMin, localMax, snapshotOffsets, snapshotBlockCodes);
        }

        public bool TryGetSnapshot(out Vec3i min, out Vec3i max, out Vec3i[] offsets, out string[] blockCodes)
        {
            return TryGetSnapshot(out min, out max, out offsets, out blockCodes, out _);
        }

        public bool TryGetSnapshot(out Vec3i min, out Vec3i max, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)
        {
            min = WatchedAttributes.GetVec3i(AttrLocalMin, localMin)?.Clone();
            max = WatchedAttributes.GetVec3i(AttrLocalMax, localMax)?.Clone();
            offsets = WatchedAttributes.GetVec3is(AttrSnapshotOffsets, snapshotOffsets ?? Array.Empty<Vec3i>());
            blockCodes = (WatchedAttributes[AttrSnapshotBlockCodes] as StringArrayAttribute)?.value ?? Array.Empty<string>();
            blockEntityTrees = (WatchedAttributes[AttrSnapshotBlockEntityTrees] as TreeArrayAttribute)?.value ?? Array.Empty<TreeAttribute>();
            blockEntityTrees = NormalizeBlockEntityTrees(blockEntityTrees, blockCodes.Length);

            return min != null
                && max != null
                && offsets != null
                && blockCodes != null
                && offsets.Length > 0
                && offsets.Length == blockCodes.Length;
        }

        private bool RetireIfWorldAlreadyRestored()
        {
            if (snapshotRestored || !Alive) return false;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out _)) return false;

            int count = Math.Min(offsets.Length, blockCodes.Length);
            for (int i = 0; i < count; i++)
            {
                if (!IsControllerSnapshotBlock(offsets[i], blockCodes[i])) continue;

                BlockPos controllerPos = GetWorldBlockPositionForOffset(offsets[i]);
                Block existing = World.BlockAccessor.GetBlock(controllerPos);
                if (existing?.Code?.ToString() != blockCodes[i]) return false;

                snapshotRestored = true;
                Die(EnumDespawnReason.Removed);
                return true;
            }

            return false;
        }

        public bool HasControllerAtWorldBlock(BlockPos pos)
        {
            if (pos == null || !Alive || snapshotRestored) return false;
            if (!TryGetControllerWorldPosition(out Vec3d controllerWorldPos)) return false;

            return pos.dimension == SidedPos.Dimension
                && pos.X == (int)Math.Floor(controllerWorldPos.X + 0.5)
                && pos.InternalY == (int)Math.Floor(controllerWorldPos.Y + 0.5) % BlockPos.DimensionBoundary
                && pos.Z == (int)Math.Floor(controllerWorldPos.Z + 0.5);
        }

        private static TreeAttribute[] NormalizeBlockEntityTrees(TreeAttribute[] trees, int count)
        {
            if (count <= 0) return Array.Empty<TreeAttribute>();

            TreeAttribute[] normalized = new TreeAttribute[count];
            for (int i = 0; i < count; i++)
            {
                normalized[i] = i < (trees?.Length ?? 0) && trees[i] != null
                    ? trees[i]
                    : new TreeAttribute();
            }

            return normalized;
        }

        public override void Initialize(EntityProperties properties, ICoreAPI api, long chunkindex3d)
        {
            base.Initialize(properties, api, chunkindex3d);
            this.api = api;
            ControllerPos = WatchedAttributes.GetBlockPos(AttrControllerPos);
            ApplySnapshotCollisionBounds(
                WatchedAttributes.GetVec3i(AttrLocalMin, new Vec3i(0, 1, 0)),
                WatchedAttributes.GetVec3i(AttrLocalMax, new Vec3i(0, 1, 0)),
                WatchedAttributes.GetVec3is(AttrSnapshotOffsets, new[] { new Vec3i(0, 1, 0) }),
                (WatchedAttributes[AttrSnapshotBlockCodes] as StringArrayAttribute)?.value);

            capi = api as ICoreClientAPI;
            if (capi != null)
            {
                renderer = new ContraptionEntityRenderer(capi, this);
                capi.Event.RegisterRenderer(renderer, EnumRenderStage.Opaque, "vintagekinematics:contraption");
            }
        }

        public override void OnEntityDespawn(EntityDespawnData despawn)
        {
            base.OnEntityDespawn(despawn);
            RemoveAllAfterPhysicsHooks();

            if (capi != null && renderer != null)
            {
                capi.Event.UnregisterRenderer(renderer, EnumRenderStage.Opaque);
                renderer.Dispose();
                renderer = null;
            }
        }

        public override void Die(EnumDespawnReason reason = EnumDespawnReason.Death, DamageSource damageSourceForDeath = null)
        {
            if (World?.Side == EnumAppSide.Server && ShouldRestoreOnDie(reason))
            {
                TryRestoreSnapshotToWorld(null, overwrite: false);
            }

            base.Die(reason, damageSourceForDeath);
        }

        public override bool ReceiveDamage(DamageSource damageSource, float damage)
        {
            if (World?.Side != EnumAppSide.Server) return base.ReceiveDamage(damageSource, damage);

            IPlayer player = (damageSource?.GetCauseEntity() as EntityPlayer)?.Player;
            return TryPlayerDisassemble(player);
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);
            if (World?.Side == EnumAppSide.Server && RetireIfWorldAlreadyRestored()) return;
            ResolveEntityCollisions();
        }

        public override void OnInteract(EntityAgent byEntity, ItemSlot itemslot, Vec3d hitPosition, EnumInteractMode mode)
        {
            base.OnInteract(byEntity, itemslot, hitPosition, mode);
            if (World?.Side != EnumAppSide.Server) return;
            if (mode != EnumInteractMode.Interact && mode != EnumInteractMode.Attack) return;

            IPlayer player = (byEntity as EntityPlayer)?.Player;
            TryPlayerDisassemble(player);
        }

        private bool TryPlayerDisassemble(IPlayer player)
        {
            if (!TryRestoreSnapshotToWorld(player, overwrite: false))
            {
                Notify(player, "Contraption cannot disassemble: target space is blocked. Ask an admin to use /vk contraptionset or /vk contraptiondelete if it is stuck.");
                return false;
            }

            Die(EnumDespawnReason.Removed);
            return true;
        }

        private static bool ShouldRestoreOnDie(EnumDespawnReason reason)
        {
            return reason != EnumDespawnReason.Unload
                && reason != EnumDespawnReason.Disconnect
                && reason != EnumDespawnReason.OutOfRange;
        }

        private void ResolveEntityCollisions()
        {
            if (World == null || !Alive || CollisionBox == null) return;

            Cuboidd contraptionBox = GetWorldCollisionBox().GrowBy(CollisionEpsilon, CollisionEpsilon, CollisionEpsilon);
            Cuboidd[] collisionBoxes = GetWorldSnapshotCollisionBoxes();
            Entity[] entities = World.GetEntitiesAround(
                SidedPos.XYZ,
                (float)Math.Max(1.5, contraptionBox.Width + 1),
                (float)Math.Max(1.5, contraptionBox.Height + 1),
                entity => ShouldCollideWith(entity));

            entitiesSeenThisTick.Clear();
            for (int i = 0; i < entities.Length; i++)
            {
                Entity entity = entities[i];
                entitiesSeenThisTick.Add(entity.EntityId);
                EnsureAfterPhysicsHook(entity);
            }

            RemoveStaleAfterPhysicsHooks(entitiesSeenThisTick);
        }

        private void EnsureAfterPhysicsHook(Entity entity)
        {
            if (afterPhysicsHooks.ContainsKey(entity.EntityId)) return;

            Action hook = () => ResolveHookedEntityAfterPhysics(entity);
            hookedEntities[entity.EntityId] = entity;
            afterPhysicsHooks[entity.EntityId] = hook;
            entity.AfterPhysicsTick += hook;
        }

        private void ResolveHookedEntityAfterPhysics(Entity entity)
        {
            if (World == null || !Alive || entity == null || !ShouldCollideWith(entity))
            {
                RemoveAfterPhysicsHook(entity);
                return;
            }

            Cuboidd contraptionBox = GetWorldCollisionBox().GrowBy(CollisionEpsilon, CollisionEpsilon, CollisionEpsilon);
            Cuboidd entityBox = GetWorldCollisionBox(entity);
            if (!contraptionBox.Clone().GrowBy(1, 1, 1).Intersects(entityBox))
            {
                return;
            }

            ResolveEntityCollision(entity, contraptionBox, GetWorldSnapshotCollisionBoxes());
        }

        private void RemoveStaleAfterPhysicsHooks(HashSet<long> currentEntityIds)
        {
            if (afterPhysicsHooks.Count == 0) return;

            List<long> staleIds = null;
            foreach (KeyValuePair<long, Entity> pair in hookedEntities)
            {
                Entity entity = pair.Value;
                if (currentEntityIds.Contains(pair.Key) && entity != null && entity.Alive) continue;
                staleIds ??= new List<long>();
                staleIds.Add(pair.Key);
            }

            if (staleIds == null) return;

            for (int i = 0; i < staleIds.Count; i++)
            {
                RemoveAfterPhysicsHook(staleIds[i]);
            }
        }

        private void RemoveAfterPhysicsHook(Entity entity)
        {
            if (entity == null) return;
            RemoveAfterPhysicsHook(entity.EntityId);
        }

        private void RemoveAfterPhysicsHook(long entityId)
        {
            if (hookedEntities.TryGetValue(entityId, out Entity entity) && afterPhysicsHooks.TryGetValue(entityId, out Action hook))
            {
                entity.AfterPhysicsTick -= hook;
            }

            hookedEntities.Remove(entityId);
            afterPhysicsHooks.Remove(entityId);
            lastEntityPositions.Remove(entityId);
            lastCollisionDebugMs.Remove(entityId);
            lastSupportContraptionPositions.Remove(entityId);
            riderSupportStates.Remove(entityId);
        }

        private void RemoveAllAfterPhysicsHooks()
        {
            List<long> entityIds = new List<long>(afterPhysicsHooks.Keys);
            for (int i = 0; i < entityIds.Count; i++)
            {
                RemoveAfterPhysicsHook(entityIds[i]);
            }
        }

        private bool ShouldCollideWith(Entity entity)
        {
            if (entity == null || entity == this || !entity.Alive || entity.CollisionBox == null) return false;
            if (entity is EntityVKContraption) return false;
            if (entity is EntityAgent agent && agent.MountedOn != null) return false;

            // The first contraption collision target is living/player physics. Items and projectiles
            // need separate rules once moving contraptions exist.
            return entity is EntityAgent;
        }

        private void ApplySnapshotCollisionBounds(Vec3i localMin, Vec3i localMax, Vec3i[] offsets, string[] blockCodes = null)
        {
            NormalizeBounds(ref localMin, ref localMax);
            this.localMin = localMin.Clone();
            this.localMax = localMax.Clone();
            snapshotOffsets = offsets == null || offsets.Length == 0 ? new[] { new Vec3i(0, 1, 0) } : offsets;

            float width = Math.Max(1, localMax.X - localMin.X + 1);
            float height = Math.Max(1, localMax.Y - localMin.Y + 1);
            float depth = Math.Max(1, localMax.Z - localMin.Z + 1);
            Cuboidf bounds = BuildLocalCollisionBounds(localMin, localMax, snapshotOffsets, blockCodes);

            CollisionBox = bounds ?? new Cuboidf
            {
                X1 = -width / 2f,
                Y1 = 0,
                Z1 = -depth / 2f,
                X2 = width / 2f,
                Y2 = height,
                Z2 = depth / 2f
            };
            OriginCollisionBox = CollisionBox.Clone();
            SelectionBox = CollisionBox.Clone();
            OriginSelectionBox = SelectionBox.Clone();
        }

        private Cuboidf BuildLocalCollisionBounds(Vec3i localMin, Vec3i localMax, Vec3i[] offsets, string[] blockCodes)
        {
            if (offsets == null || offsets.Length == 0) return null;

            double width = localMax.X - localMin.X + 1;
            double depth = localMax.Z - localMin.Z + 1;
            double originLocalX = localMin.X + width / 2.0;
            double originLocalY = localMin.Y;
            double originLocalZ = localMin.Z + depth / 2.0;
            bool found = false;
            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            int count = Math.Min(offsets.Length, blockCodes?.Length ?? offsets.Length);
            for (int i = 0; i < count; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null) continue;

                Block block = ResolveSnapshotBlock(blockCodes, i);
                Cuboidf[] boxes = ResolveSnapshotCollisionBoxes(block, offset);
                if (boxes == null || boxes.Length == 0) continue;

                double baseX = offset.X - originLocalX;
                double baseY = offset.Y - originLocalY;
                double baseZ = offset.Z - originLocalZ;
                for (int j = 0; j < boxes.Length; j++)
                {
                    Cuboidf box = boxes[j];
                    if (box == null) continue;

                    minX = Math.Min(minX, baseX + box.X1);
                    minY = Math.Min(minY, baseY + box.Y1);
                    minZ = Math.Min(minZ, baseZ + box.Z1);
                    maxX = Math.Max(maxX, baseX + box.X2);
                    maxY = Math.Max(maxY, baseY + box.Y2);
                    maxZ = Math.Max(maxZ, baseZ + box.Z2);
                    found = true;
                }
            }

            if (!found) return null;
            return new Cuboidf((float)minX, (float)minY, (float)minZ, (float)maxX, (float)maxY, (float)maxZ);
        }

        private static void NormalizeBounds(ref Vec3i min, ref Vec3i max)
        {
            min ??= new Vec3i(0, 1, 0);
            max ??= min.Clone();

            int minX = Math.Min(min.X, max.X);
            int minY = Math.Min(min.Y, max.Y);
            int minZ = Math.Min(min.Z, max.Z);
            int maxX = Math.Max(min.X, max.X);
            int maxY = Math.Max(min.Y, max.Y);
            int maxZ = Math.Max(min.Z, max.Z);

            min.Set(minX, minY, minZ);
            max.Set(maxX, maxY, maxZ);
        }

        private Cuboidd GetWorldCollisionBox()
        {
            Cuboidd box = new Cuboidd();
            box.SetAndTranslate(CollisionBox, SidedPos.X, SidedPos.InternalY, SidedPos.Z);
            return box;
        }

        private static Cuboidd GetWorldCollisionBox(Entity entity)
        {
            return GetWorldCollisionBox(entity, entity.SidedPos.XYZ);
        }

        private static Cuboidd GetWorldCollisionBox(Entity entity, Vec3d position)
        {
            Cuboidd box = new Cuboidd();
            box.SetAndTranslate(entity.CollisionBox, position.X, position.Y, position.Z);
            return box;
        }

        private Cuboidd[] GetWorldSnapshotCollisionBoxes()
        {
            Vec3i[] offsets = snapshotOffsets == null || snapshotOffsets.Length == 0 ? new[] { new Vec3i(0, 1, 0) } : snapshotOffsets;
            string[] blockCodes = (WatchedAttributes[AttrSnapshotBlockCodes] as StringArrayAttribute)?.value;
            List<Cuboidd> boxes = new List<Cuboidd>();
            double width = localMax.X - localMin.X + 1;
            double depth = localMax.Z - localMin.Z + 1;
            double originLocalX = localMin.X + width / 2.0;
            double originLocalY = localMin.Y;
            double originLocalZ = localMin.Z + depth / 2.0;

            for (int i = 0; i < offsets.Length; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null) continue;

                Block block = ResolveSnapshotBlock(blockCodes, i);
                Cuboidf[] blockBoxes = ResolveSnapshotCollisionBoxes(block, offset);
                if (blockBoxes == null || blockBoxes.Length == 0) continue;

                double baseX = SidedPos.X + offset.X - originLocalX;
                double baseY = SidedPos.InternalY + offset.Y - originLocalY;
                double baseZ = SidedPos.Z + offset.Z - originLocalZ;
                for (int j = 0; j < blockBoxes.Length; j++)
                {
                    Cuboidf box = blockBoxes[j];
                    if (box == null) continue;
                    boxes.Add(new Cuboidd(
                        baseX + box.X1,
                        baseY + box.Y1,
                        baseZ + box.Z1,
                        baseX + box.X2,
                        baseY + box.Y2,
                        baseZ + box.Z2));
                }
            }

            return boxes.ToArray();
        }

        private Block ResolveSnapshotBlock(string[] blockCodes, int index)
        {
            if (World == null || blockCodes == null || index < 0 || index >= blockCodes.Length || string.IsNullOrEmpty(blockCodes[index])) return null;
            return World.GetBlock(new AssetLocation(blockCodes[index]));
        }

        private Cuboidf[] ResolveSnapshotCollisionBoxes(Block block, Vec3i offset)
        {
            if (block == null || block.Id == 0) return null;
            if (IsFluidBlock(block)) return null;
            if (World == null) return block.CollisionBoxes;

            BlockPos pos = GetWorldBlockPositionForOffset(offset);
            return block.GetCollisionBoxes(World.BlockAccessor, pos) ?? block.CollisionBoxes;
        }

        public BlockPos GetWorldBlockPositionForOffset(Vec3i offset)
        {
            Vec3d pos = GetWorldPositionForOffset(offset);
            return new BlockPos(
                (int)Math.Floor(pos.X + 0.5),
                (int)Math.Floor(pos.Y + 0.5) % BlockPos.DimensionBoundary,
                (int)Math.Floor(pos.Z + 0.5),
                SidedPos.Dimension);
        }

        private BlockPos GetWorldBlockPositionForOffsetAfterMove(Vec3i offset, double dx, double dy, double dz)
        {
            Vec3d pos = GetWorldPositionForOffset(offset);
            return new BlockPos(
                (int)Math.Floor(pos.X + dx + 0.5),
                (int)Math.Floor(pos.Y + dy + 0.5) % BlockPos.DimensionBoundary,
                (int)Math.Floor(pos.Z + dz + 0.5),
                SidedPos.Dimension);
        }

        public Vec3d GetWorldPositionForOffset(Vec3i offset)
        {
            double width = localMax.X - localMin.X + 1;
            double depth = localMax.Z - localMin.Z + 1;
            double originLocalX = localMin.X + width / 2.0;
            double originLocalY = localMin.Y;
            double originLocalZ = localMin.Z + depth / 2.0;

            double x = SidedPos.X + offset.X - originLocalX;
            double y = SidedPos.InternalY + offset.Y - originLocalY;
            double z = SidedPos.Z + offset.Z - originLocalZ;

            return new Vec3d(x, y, z);
        }

        public bool TryGetControllerWorldPosition(out Vec3d position)
        {
            position = null;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)) return false;

            for (int i = 0; i < offsets.Length && i < blockCodes.Length; i++)
            {
                if (offsets[i] == null) continue;
                if (offsets[i].X != 0 || offsets[i].Y != 0 || offsets[i].Z != 0) continue;
                if (blockCodes[i] == null || !blockCodes[i].StartsWith("vintagekinematics:gantrycarriage", StringComparison.Ordinal)) return false;

                position = GetWorldPositionForOffset(offsets[i]);
                Vec3i attachedShaftDelta = GetAttachedShaftDelta(blockEntityTrees, i);
                position.Add(attachedShaftDelta.X, attachedShaftDelta.Y, attachedShaftDelta.Z);
                return true;
            }

            return false;
        }

        public bool TryGetControllerAxis(out EnumKineticAxis axis)
        {
            axis = EnumKineticAxis.X;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out _)) return false;

            for (int i = 0; i < offsets.Length && i < blockCodes.Length; i++)
            {
                if (offsets[i] == null) continue;
                if (offsets[i].X != 0 || offsets[i].Y != 0 || offsets[i].Z != 0) continue;
                if (blockCodes[i] == null || !blockCodes[i].StartsWith("vintagekinematics:gantrycarriage", StringComparison.Ordinal)) return false;

                axis = ParseGantryCarriageAxis(blockCodes[i]);
                return true;
            }

            return false;
        }

        private static EnumKineticAxis ParseGantryCarriageAxis(string blockCode)
        {
            string path = blockCode ?? "";
            int domainSep = path.IndexOf(':');
            if (domainSep >= 0) path = path.Substring(domainSep + 1);

            string[] parts = path.Split('-');
            if (parts.Length >= 2)
            {
                if (parts[1] == "y") return EnumKineticAxis.Y;
                if (parts[1] == "z") return EnumKineticAxis.Z;
            }

            return EnumKineticAxis.X;
        }

        private static Vec3i GetAttachedShaftDelta(TreeAttribute[] blockEntityTrees, int index)
        {
            if (blockEntityTrees == null || index < 0 || index >= blockEntityTrees.Length) return new Vec3i(0, 0, 0);

            TreeAttribute tree = blockEntityTrees[index];
            if (tree == null || !tree.GetBool("hasAttachedShaft")) return new Vec3i(0, 0, 0);

            return new Vec3i(
                tree.GetInt("attachedShaftDx"),
                tree.GetInt("attachedShaftDy"),
                tree.GetInt("attachedShaftDz"));
        }

        public void MoveBy(double dx, double dy, double dz)
        {
            List<Entity> carriedEntities = FindMovementCarriedEntities();

            ServerPos.X += dx;
            ServerPos.Y += dy;
            ServerPos.Z += dz;
            Pos.SetFrom(ServerPos);
            PositionBeforeFalling.Set(ServerPos.X, ServerPos.InternalY, ServerPos.Z);

            CarryEntitiesByMovement(carriedEntities, dx, dy, dz);
        }

        public void RunContraptionWorkTick(float rpm, float dt, double moveX, double moveY, double moveZ)
        {
            if (World == null || World.Side != EnumAppSide.Server) return;
            if (MathF.Abs(rpm) < 0.001f) return;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)) return;

            int count = Math.Min(offsets.Length, blockCodes.Length);
            blockEntityTrees = NormalizeBlockEntityTrees(blockEntityTrees, count);
            for (int i = 0; i < count; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null || string.IsNullOrEmpty(blockCodes[i])) continue;

                Block block = World.GetBlock(new AssetLocation(blockCodes[i]));
                if (block == null || block.Id == 0) continue;

                ContraptionWorkRegistry.DoContraptionWork(new ContraptionWorkContext(
                    World,
                    this,
                    block,
                    offset,
                    blockEntityTrees[i],
                    GetWorldBlockPositionForOffset(offset),
                    rpm,
                    dt,
                    moveX,
                    moveY,
                    moveZ,
                    api));
            }
        }

        public float GetActiveContraptionWorkStressImpact(float rpm, float dt, double moveX, double moveY, double moveZ)
        {
            if (World == null || World.Side != EnumAppSide.Server) return 0f;
            if (MathF.Abs(rpm) < 0.001f) return 0f;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)) return 0f;

            int count = Math.Min(offsets.Length, blockCodes.Length);
            blockEntityTrees = NormalizeBlockEntityTrees(blockEntityTrees, count);
            float stressImpact = 0f;
            for (int i = 0; i < count; i++)
            {
                Vec3i offset = offsets[i];
                if (offset == null || string.IsNullOrEmpty(blockCodes[i])) continue;

                Block block = World.GetBlock(new AssetLocation(blockCodes[i]));
                if (block == null || block.Id == 0) continue;

                stressImpact += ContraptionWorkRegistry.GetActiveStressImpact(new ContraptionWorkContext(
                    World,
                    this,
                    block,
                    offset,
                    blockEntityTrees[i],
                    GetWorldBlockPositionForOffset(offset),
                    rpm,
                    dt,
                    moveX,
                    moveY,
                    moveZ,
                    api));
            }

            return stressImpact;
        }

        public bool AddContraptionWorkProgress(string key, float amount, float required, out float progress)
        {
            progress = 0f;
            if (string.IsNullOrEmpty(key) || required <= 0f) return false;

            workProgress.TryGetValue(key, out float current);
            current += Math.Max(0f, amount);
            if (current >= required)
            {
                ClearContraptionWorkTracking(key);
                progress = 1f;
                return true;
            }

            workProgress[key] = current;
            progress = current / required;
            return false;
        }

        public void ResetContraptionWorkProgress(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            ClearContraptionWorkTracking(key);
        }

        private void ClearContraptionWorkTracking(string key)
        {
            workProgress.Remove(key);
            workVisualPulseMs.Remove(key);
            workVisualPulseMs.Remove(key + ":decal");
            workVisualPulseMs.Remove(key + ":particles");
            workVisualPulseMs.Remove(key + ":sound");
            workVisualProgress.Remove(key);
            workVisualProgress.Remove(key + ":decal");
        }

        public bool ShouldRunContraptionWorkVisual(string key, long intervalMs)
        {
            if (string.IsNullOrEmpty(key) || World == null) return true;

            long now = World.ElapsedMilliseconds;
            if (workVisualPulseMs.TryGetValue(key, out long lastMs) && now - lastMs < Math.Max(1, intervalMs))
            {
                return false;
            }

            workVisualPulseMs[key] = now;
            return true;
        }

        public bool ShouldRunContraptionWorkSound(long intervalMs)
        {
            if (World == null) return true;

            long tick = World.ElapsedMilliseconds / 50;
            if (lastWorkSoundTick == tick) return false;

            long now = World.ElapsedMilliseconds;
            if (now - lastWorkSoundMs < Math.Max(1, intervalMs)) return false;

            lastWorkSoundTick = tick;
            lastWorkSoundMs = now;
            return true;
        }

        public float AdvanceContraptionWorkVisualProgress(string key, float targetProgress)
        {
            if (string.IsNullOrEmpty(key)) return 0f;

            targetProgress = GameMath.Clamp(targetProgress, 0f, 1f);
            workVisualProgress.TryGetValue(key, out float previous);
            if (targetProgress <= previous) return 0f;

            workVisualProgress[key] = targetProgress;
            return targetProgress - previous;
        }

        public bool ContainsSnapshotWorldBlockPosition(BlockPos pos)
        {
            if (pos == null) return false;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out _)) return false;
            return ContainsWorldBlockPosition(offsets, blockCodes, pos);
        }

        public void DepositOutput(ItemStack stack, Vec3d fallbackAt)
        {
            if (World == null || stack == null || stack.StackSize <= 0) return;

            TryDepositIntoCapturedStorage(stack);
            if (stack.StackSize <= 0) return;

            World.SpawnItemEntity(stack, fallbackAt ?? SidedPos.XYZ);
        }

        private void TryDepositIntoCapturedStorage(ItemStack stack)
        {
            if (stack == null || stack.StackSize <= 0) return;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)) return;
            if (api == null) return;

            int count = Math.Min(offsets.Length, blockCodes.Length);
            blockEntityTrees = NormalizeBlockEntityTrees(blockEntityTrees, count);
            bool changed = false;
            for (int i = 0; i < count && stack.StackSize > 0; i++)
            {
                if (offsets[i] == null || string.IsNullOrEmpty(blockCodes[i])) continue;

                Block block = World.GetBlock(new AssetLocation(blockCodes[i]));
                if (block == null || block.Id == 0 || string.IsNullOrEmpty(block.EntityClass)) continue;

                if (TryDepositIntoCapturedStorageBlock(block, GetWorldBlockPositionForOffset(offsets[i]), blockEntityTrees[i], stack, out TreeAttribute updatedTree))
                {
                    blockEntityTrees[i] = updatedTree;
                    changed = true;
                }
            }

            if (!changed) return;
            WatchedAttributes[AttrSnapshotBlockEntityTrees] = new TreeArrayAttribute(blockEntityTrees);
            WatchedAttributes.MarkPathDirty(AttrSnapshotBlockEntityTrees);
        }

        private bool TryDepositIntoCapturedStorageBlock(Block block, BlockPos pos, TreeAttribute savedTree, ItemStack stack, out TreeAttribute updatedTree)
        {
            updatedTree = savedTree;
            if (stack == null || stack.StackSize <= 0) return false;

            BlockEntity be = null;
            try
            {
                be = World.ClassRegistry.CreateBlockEntity(block.EntityClass);
                if (be == null) return false;

                be.Pos = pos.Copy();
                be.Block = block;
                be.CreateBehaviors(block, World);
                be.FromTreeAttributes(savedTree?.Clone() as TreeAttribute ?? new TreeAttribute(), World);
                be.Initialize(api);

                if (be is not IBlockEntityContainer container || container.Inventory == null || container.Inventory.PutLocked) return false;

                int moved = TryInsertIntoInventory(container.Inventory, stack);
                if (moved <= 0) return false;

                TreeAttribute tree = new TreeAttribute();
                be.ToTreeAttributes(tree);
                updatedTree = tree;
                return true;
            }
            finally
            {
                be?.OnBlockUnloaded();
            }
        }

        private int TryInsertIntoInventory(IInventory inventory, ItemStack stack)
        {
            if (inventory == null || stack == null || stack.StackSize <= 0) return 0;

            DummySlot source = new DummySlot(stack.Clone());
            int startSize = source.Itemstack.StackSize;
            List<ItemSlot> skip = new List<ItemSlot>();

            while (!source.Empty && source.Itemstack.StackSize > 0 && skip.Count < inventory.Count)
            {
                WeightedSlot weighted = inventory.GetBestSuitedSlot(source, null, skip);
                ItemSlot target = weighted?.slot;
                if (target == null) break;

                ItemStackMoveOperation op = new ItemStackMoveOperation(
                    World,
                    EnumMouseButton.Left,
                    0,
                    EnumMergePriority.DirectMerge,
                    source.Itemstack.StackSize);
                int moved = source.TryPutInto(target, ref op);
                if (moved <= 0)
                {
                    skip.Add(target);
                    continue;
                }

                target.MarkDirty();
                skip.Clear();
            }

            int remaining = source.Empty ? 0 : source.Itemstack.StackSize;
            int movedTotal = startSize - remaining;
            if (movedTotal <= 0) return 0;

            stack.StackSize -= movedTotal;
            if (stack.StackSize < 0) stack.StackSize = 0;
            return movedTotal;
        }

        public void RequestMovementPause(string key, long durationMs, string reason = null)
        {
            if (string.IsNullOrEmpty(key) || World == null) return;

            long untilMs = World.ElapsedMilliseconds + Math.Max(1, durationMs);
            movementPauseUntilMs[key] = untilMs;
            if (!string.IsNullOrEmpty(reason)) movementPauseReason = reason;
        }

        public bool IsMovementPaused(out string reason)
        {
            reason = null;
            if (World == null || movementPauseUntilMs.Count == 0) return false;

            long now = World.ElapsedMilliseconds;
            List<string> expired = null;
            foreach (var kvp in movementPauseUntilMs)
            {
                if (kvp.Value > now) continue;
                expired ??= new List<string>();
                expired.Add(kvp.Key);
            }

            if (expired != null)
            {
                for (int i = 0; i < expired.Count; i++) movementPauseUntilMs.Remove(expired[i]);
            }

            if (movementPauseUntilMs.Count == 0)
            {
                movementPauseReason = null;
                return false;
            }

            reason = movementPauseReason ?? "Movement paused";
            return true;
        }

        public bool WouldMovementHitWorldBlock(double dx, double dy, double dz, out string reason)
        {
            return WouldMovementHitWorldBlock(dx, dy, dz, out reason, out _);
        }

        public bool WouldMovementHitWorldBlock(double dx, double dy, double dz, out string reason, out bool protectedClaim)
        {
            reason = null;
            protectedClaim = false;
            if (World == null) return false;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out _)) return false;

            if (WouldMovementEnterProtectedClaim(offsets, blockCodes, dx, dy, dz, out BlockPos claimPos))
            {
                protectedClaim = true;
                reason = $"Blocked by protected claim at {claimPos.X},{claimPos.InternalY},{claimPos.Z}";
                return true;
            }

            Cuboidd[] currentBoxes = GetWorldSnapshotCollisionBoxes();
            Cuboidd[] movedBoxes = OffsetBoxes(currentBoxes, dx, dy, dz);
            if (movedBoxes.Length == 0) return false;

            GetBounds(movedBoxes, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ);
            if (minX == double.MaxValue) return false;

            int bx1 = (int)Math.Floor(minX - CollisionEpsilon);
            int by1 = (int)Math.Floor(minY - CollisionEpsilon);
            int bz1 = (int)Math.Floor(minZ - CollisionEpsilon);
            int bx2 = (int)Math.Floor(maxX + CollisionEpsilon);
            int by2 = (int)Math.Floor(maxY + CollisionEpsilon);
            int bz2 = (int)Math.Floor(maxZ + CollisionEpsilon);

            for (int y = by1; y <= by2; y++)
            {
                for (int z = bz1; z <= bz2; z++)
                {
                    for (int x = bx1; x <= bx2; x++)
                    {
                        BlockPos targetPos = new BlockPos(x, y % BlockPos.DimensionBoundary, z, SidedPos.Dimension);
                        if (ContainsWorldBlockPosition(offsets, blockCodes, targetPos)) continue;

                        Block existing = World.BlockAccessor.GetBlock(targetPos);
                        if (existing == null || existing.Id == 0) continue;
                        if (IsFluidBlock(existing)) continue;
                        if (IsGantryShaft(existing)) continue;

                        Cuboidd[] worldBlockBoxes = GetWorldBlockCollisionBoxes(existing, targetPos);
                        if (worldBlockBoxes.Length == 0) continue;

                        if (!MovementIncreasesOverlap(currentBoxes, movedBoxes, worldBlockBoxes)) continue;

                        reason = $"Blocked by {existing.Code} at {targetPos.X},{targetPos.InternalY},{targetPos.Z}";
                        return true;
                    }
                }
            }

            return false;
        }

        private bool WouldMovementEnterProtectedClaim(Vec3i[] offsets, string[] blockCodes, double dx, double dy, double dz, out BlockPos claimPos)
        {
            claimPos = null;
            int count = Math.Min(offsets?.Length ?? 0, blockCodes?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (offsets[i] == null || string.IsNullOrEmpty(blockCodes[i])) continue;
                if (IsGantryShaftCode(blockCodes[i])) continue;
                if (IsSnapshotFluidCode(blockCodes[i])) continue;

                BlockPos targetPos = GetWorldBlockPositionForOffsetAfterMove(offsets[i], dx, dy, dz);
                if (CanAutomationBuildOrBreakAt(targetPos)) continue;

                claimPos = targetPos;
                return true;
            }

            return false;
        }

        private static Cuboidd[] OffsetBoxes(Cuboidd[] boxes, double dx, double dy, double dz)
        {
            if (boxes == null || boxes.Length == 0) return Array.Empty<Cuboidd>();

            Cuboidd[] moved = new Cuboidd[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                Cuboidd box = boxes[i];
                if (box == null) continue;
                moved[i] = new Cuboidd(box.X1 + dx, box.Y1 + dy, box.Z1 + dz, box.X2 + dx, box.Y2 + dy, box.Z2 + dz);
            }

            return moved;
        }

        private Cuboidd[] GetWorldBlockCollisionBoxes(Block block, BlockPos pos)
        {
            Cuboidf[] localBoxes = block.GetCollisionBoxes(World.BlockAccessor, pos) ?? block.CollisionBoxes;
            if (localBoxes == null || localBoxes.Length == 0) return Array.Empty<Cuboidd>();

            List<Cuboidd> boxes = new List<Cuboidd>(localBoxes.Length);
            for (int i = 0; i < localBoxes.Length; i++)
            {
                Cuboidf box = localBoxes[i];
                if (box == null) continue;
                boxes.Add(new Cuboidd(
                    pos.X + box.X1,
                    pos.InternalY + box.Y1,
                    pos.Z + box.Z1,
                    pos.X + box.X2,
                    pos.InternalY + box.Y2,
                    pos.Z + box.Z2));
            }

            return boxes.ToArray();
        }

        private static bool MovementIncreasesOverlap(Cuboidd[] currentBoxes, Cuboidd[] movedBoxes, Cuboidd[] worldBlockBoxes)
        {
            double currentOverlap = TotalIntersectionVolume(currentBoxes, worldBlockBoxes);
            double movedOverlap = TotalIntersectionVolume(movedBoxes, worldBlockBoxes);
            return movedOverlap > CollisionEpsilon && movedOverlap > currentOverlap + CollisionEpsilon;
        }

        private static double TotalIntersectionVolume(Cuboidd[] aBoxes, Cuboidd[] bBoxes)
        {
            double total = 0;
            if (aBoxes == null || bBoxes == null) return total;

            for (int i = 0; i < aBoxes.Length; i++)
            {
                Cuboidd a = aBoxes[i];
                if (a == null) continue;

                for (int j = 0; j < bBoxes.Length; j++)
                {
                    Cuboidd b = bBoxes[j];
                    if (b == null || !a.Intersects(b)) continue;

                    double ix = Math.Min(a.X2, b.X2) - Math.Max(a.X1, b.X1);
                    double iy = Math.Min(a.Y2, b.Y2) - Math.Max(a.Y1, b.Y1);
                    double iz = Math.Min(a.Z2, b.Z2) - Math.Max(a.Z1, b.Z1);
                    if (ix <= 0 || iy <= 0 || iz <= 0) continue;
                    total += ix * iy * iz;
                }
            }

            return total;
        }

        private List<Entity> FindMovementCarriedEntities()
        {
            List<Entity> entities = new List<Entity>();
            HashSet<long> entityIds = new HashSet<long>();
            Cuboidd[] movingBoxes = GetWorldSnapshotCollisionBoxes();
            if (World == null || movingBoxes.Length == 0) return entities;

            GetBounds(movingBoxes, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ);
            if (minX == double.MaxValue) return entities;

            Vec3d center = new Vec3d(
                (minX + maxX) * 0.5,
                (minY + maxY) * 0.5,
                (minZ + maxZ) * 0.5);
            float horizontalRadius = (float)Math.Max(2.0, Math.Max(maxX - minX, maxZ - minZ) * 0.5 + 2.0);
            float verticalRadius = (float)Math.Max(2.0, (maxY - minY) * 0.5 + 2.0);

            Entity[] nearbyEntities = World.GetEntitiesAround(center, horizontalRadius, verticalRadius, ShouldCollideWith);
            for (int i = 0; i < nearbyEntities.Length; i++)
            {
                AddMovementCarriedEntity(nearbyEntities[i], movingBoxes, entities, entityIds);
            }

            foreach (long entityId in lastSupportContraptionPositions.Keys)
            {
                AddTrackedMovementCarriedEntity(World.GetEntityById(entityId), entities, entityIds);
            }

            CleanupExpiredRiderSupportStates();
            List<long> riderEntityIds = new List<long>(riderSupportStates.Keys);
            for (int i = 0; i < riderEntityIds.Count; i++)
            {
                AddRecentRiderMovementCarriedEntity(World.GetEntityById(riderEntityIds[i]), entities, entityIds);
            }

            return entities;
        }

        private void AddMovementCarriedEntity(Entity entity, Cuboidd[] movingBoxes, List<Entity> entities, HashSet<long> entityIds)
        {
            if (!ShouldCollideWith(entity)) return;
            if (!IsEntitySupportedByBoxes(entity, movingBoxes, RestoreMovingSupportBelowTolerance, RestoreMovingSupportAboveTolerance, RestoreSupportHorizontalSkin)) return;
            if (!entityIds.Add(entity.EntityId)) return;

            entities.Add(entity);
        }

        private void AddTrackedMovementCarriedEntity(Entity entity, List<Entity> entities, HashSet<long> entityIds)
        {
            if (!ShouldCollideWith(entity)) return;
            if (!entityIds.Add(entity.EntityId)) return;

            entities.Add(entity);
        }

        private void AddRecentRiderMovementCarriedEntity(Entity entity, List<Entity> entities, HashSet<long> entityIds)
        {
            if (!ShouldCollideWith(entity)) return;
            if (!IsRecentRiderSupport(entity.EntityId)) return;
            if (entity.SidedPos.Motion != null && entity.SidedPos.Motion.Y > 0.01) return;
            if (!entityIds.Add(entity.EntityId)) return;

            entities.Add(entity);
        }

        private void CarryEntitiesByMovement(List<Entity> carriedEntities, double dx, double dy, double dz)
        {
            if (carriedEntities == null || carriedEntities.Count == 0) return;

            List<Cuboidd> movedBoxes = new List<Cuboidd>(GetWorldSnapshotCollisionBoxes());
            for (int i = 0; i < carriedEntities.Count; i++)
            {
                Entity entity = carriedEntities[i];
                if (!ShouldCollideWith(entity)) continue;

                MoveCarriedEntity(entity, dx, dy, dz);

                SnapEntityToRestoredTop(entity, movedBoxes, RestoreMovingSupportBelowTolerance, RestoreMovingSupportAboveTolerance);

                if (entity.World?.Side == EnumAppSide.Server)
                {
                    entity.Pos.SetFrom(entity.ServerPos);
                }

                lastSupportContraptionPositions[entity.EntityId] = SidedPos.XYZ.Clone();
                recentSupportContraptionPositions[entity.EntityId] = SidedPos.XYZ.Clone();
                recentSupportMs[entity.EntityId] = World.ElapsedMilliseconds;
                lastEntityPositions[entity.EntityId] = entity.SidedPos.XYZ.Clone();
                RecordRiderSupport(entity);
            }
        }

        private void MoveCarriedEntity(Entity entity, double dx, double dy, double dz)
        {
            entity.SidedPos.X += dx;
            entity.SidedPos.Y += dy;
            entity.SidedPos.Z += dz;

            TrySnapRiderToLocalSupportAnchor(entity);
        }

        private bool TrySnapRiderToLocalSupportAnchor(Entity entity)
        {
            if (entity == null || !IsRecentRiderSupport(entity.EntityId)) return false;
            if (entity.SidedPos.Motion != null && entity.SidedPos.Motion.Y > 0.01) return false;
            if (!riderSupportStates.TryGetValue(entity.EntityId, out RiderSupportState state) || state?.LocalEntityPos == null) return false;

            double targetX = SidedPos.X + state.LocalEntityPos.X;
            double targetY = SidedPos.InternalY + state.LocalEntityPos.Y;
            double targetZ = SidedPos.Z + state.LocalEntityPos.Z;
            bool closeHorizontally = Math.Abs(entity.SidedPos.X - targetX) <= RiderAnchorHorizontalTolerance
                && Math.Abs(entity.SidedPos.Z - targetZ) <= RiderAnchorHorizontalTolerance;
            if (!closeHorizontally) return false;

            double verticalDelta = targetY - entity.SidedPos.Y;
            if (Math.Abs(verticalDelta) < RiderAnchorVerticalTolerance) return false;

            entity.SidedPos.Y = targetY;
            if (entity.SidedPos.Motion != null && entity.SidedPos.Motion.Y < 0)
            {
                entity.SidedPos.Motion.Y = 0;
            }
            entity.CollidedVertically = true;
            entity.OnGround = true;
            entity.PositionBeforeFalling.Set(entity.SidedPos.X, entity.SidedPos.InternalY, entity.SidedPos.Z);
            return true;
        }

        private void RecordRiderSupport(Entity entity)
        {
            if (entity == null || World == null) return;

            riderSupportStates[entity.EntityId] = new RiderSupportState
            {
                LocalEntityPos = new Vec3d(
                    entity.SidedPos.X - SidedPos.X,
                    entity.SidedPos.InternalY - SidedPos.InternalY,
                    entity.SidedPos.Z - SidedPos.Z),
                LastSupportMs = World.ElapsedMilliseconds
            };
        }

        private bool IsRecentRiderSupport(long entityId)
        {
            return World != null
                && riderSupportStates.TryGetValue(entityId, out RiderSupportState state)
                && state != null
                && World.ElapsedMilliseconds - state.LastSupportMs <= RiderSupportGraceMs;
        }

        private void CleanupExpiredRiderSupportStates()
        {
            if (World == null || riderSupportStates.Count == 0) return;

            long now = World.ElapsedMilliseconds;
            List<long> staleIds = null;
            foreach (KeyValuePair<long, RiderSupportState> pair in riderSupportStates)
            {
                if (pair.Value != null && now - pair.Value.LastSupportMs <= RiderSupportGraceMs) continue;
                staleIds ??= new List<long>();
                staleIds.Add(pair.Key);
            }

            if (staleIds == null) return;
            for (int i = 0; i < staleIds.Count; i++)
            {
                riderSupportStates.Remove(staleIds[i]);
            }
        }

        public bool CanAutoRestoreWhenStopped()
        {
            return PlacementMode switch
            {
                ContraptionPlacementMode.OnlyPlaceNearInitialAngle => IsNearInitialAngle(),
                ContraptionPlacementMode.OnlyPlaceWhenAnchorDestroyed => false,
                _ => true
            };
        }

        public bool TryAutoRestoreWhenStopped()
        {
            if (!CanAutoRestoreWhenStopped()) return false;
            if (!TryRestoreSnapshotToWorld(null, overwrite: false)) return false;

            Die(EnumDespawnReason.Removed);
            return true;
        }

        public bool TryRestoreToWorld(IPlayer player = null, bool overwrite = false, bool despawnOnSuccess = true)
        {
            if (!TryRestoreSnapshotToWorld(player, overwrite)) return false;
            if (despawnOnSuccess) Die(EnumDespawnReason.Removed);
            return true;
        }

        public bool TryAdminForceRestoreToWorld(bool despawnOnSuccess = true)
        {
            if (!TryRestoreSnapshotToWorld(null, overwrite: true, bypassClaims: true)) return false;
            if (despawnOnSuccess) Die(EnumDespawnReason.Removed);
            return true;
        }

        public void AdminDeleteEntityOnly()
        {
            snapshotRestored = true;
            Die(EnumDespawnReason.Removed);
        }

        private bool IsNearInitialAngle()
        {
            float initialYaw = WatchedAttributes.GetFloat(AttrInitialYaw, (float)SidedPos.Yaw);
            return Math.Abs(GameMath.AngleRadDistance((float)SidedPos.Yaw, initialYaw)) <= InitialAngleRestoreToleranceRad;
        }

        private bool TryRestoreSnapshotToWorld(IPlayer player, bool overwrite, bool bypassClaims = false)
        {
            if (snapshotRestored) return true;
            if (!TryGetSnapshot(out _, out _, out Vec3i[] offsets, out string[] blockCodes, out TreeAttribute[] blockEntityTrees)) return false;

            int count = Math.Min(offsets.Length, blockCodes.Length);
            blockEntityTrees = NormalizeBlockEntityTrees(blockEntityTrees, count);
            Cuboidd[] movingBoxes = GetWorldSnapshotCollisionBoxes();
            BlockPos[] restorePositions = new BlockPos[count];
            int controllerIndex = -1;
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(blockCodes[i])) continue;
                if (IsGantryShaftCode(blockCodes[i])) continue;
                if (IsSnapshotFluidCode(blockCodes[i])) continue;

                restorePositions[i] = GetWorldBlockPositionForOffset(offsets[i]);
                if (IsControllerSnapshotBlock(offsets[i], blockCodes[i])) controllerIndex = i;
            }

            if (controllerIndex >= 0)
            {
                Block existingControllerTarget = World.BlockAccessor.GetBlock(restorePositions[controllerIndex]);
                if (!overwrite && IsGantryShaft(existingControllerTarget))
                {
                    if (!TryFindControllerRestorePosition(restorePositions[controllerIndex], restorePositions, count, player, overwrite, bypassClaims, out BlockPos controllerRestorePos))
                    {
                        return false;
                    }

                    Vec3i restoreDelta = new Vec3i(
                        controllerRestorePos.X - restorePositions[controllerIndex].X,
                        controllerRestorePos.InternalY - restorePositions[controllerIndex].InternalY,
                        controllerRestorePos.Z - restorePositions[controllerIndex].Z);

                    restorePositions[controllerIndex] = controllerRestorePos;
                    AdjustControllerTreeForRestoreDelta(blockEntityTrees[controllerIndex], restoreDelta);
                }
            }

            HashSet<string> occupiedTargets = new HashSet<string>();
            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(blockCodes[i])) continue;
                if (IsGantryShaftCode(blockCodes[i])) continue;
                if (IsSnapshotFluidCode(blockCodes[i])) continue;
                BlockPos blockPos = restorePositions[i];
                if (blockPos == null) continue;

                if (!occupiedTargets.Add(PositionKey(blockPos))) return false;
                if (!CanRestoreBlockAt(blockPos, player, overwrite, bypassClaims)) return false;
            }

            List<Entity> restoreCarriedEntities = FindRestoreCarriedEntities(movingBoxes);
            Vec3d worldRestoreDelta = GetRestoreDelta(offsets, blockCodes, restorePositions, count);
            List<Cuboidd> restoredBoxes = BuildRestoredBoxes(restorePositions, blockCodes, count, out double restoreMinX, out double restoreMinY, out double restoreMinZ, out double restoreMaxX, out double restoreMaxY, out double restoreMaxZ);
            HashSet<long> snappedEntityIds = CarryEntitiesThroughRestore(restoreCarriedEntities, worldRestoreDelta, restoredBoxes);

            for (int i = 0; i < count; i++)
            {
                if (string.IsNullOrEmpty(blockCodes[i])) continue;
                if (IsGantryShaftCode(blockCodes[i])) continue;
                if (IsSnapshotFluidCode(blockCodes[i])) continue;

                Block block = World.GetBlock(new AssetLocation(blockCodes[i]));
                if (block == null || block.Id == 0) continue;
                if (IsFluidBlock(block)) continue;

                BlockPos blockPos = restorePositions[i];
                if (blockPos == null) continue;
                World.BlockAccessor.SetBlock(block.Id, blockPos);
                RestoreBlockEntityTree(blockPos, blockEntityTrees[i]);
                World.BlockAccessor.MarkBlockDirty(blockPos);
            }

            SnapSupportedEntitiesToRestoredBlocks(restoredBoxes, restoreMinX, restoreMinY, restoreMinZ, restoreMaxX, restoreMaxY, restoreMaxZ, snappedEntityIds);
            snapshotRestored = true;
            return true;
        }

        private List<Entity> FindRestoreCarriedEntities(Cuboidd[] movingBoxes)
        {
            List<Entity> entities = new List<Entity>();
            HashSet<long> entityIds = new HashSet<long>();
            if (movingBoxes == null || movingBoxes.Length == 0) return entities;

            GetBounds(movingBoxes, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ);
            if (minX == double.MaxValue) return entities;

            Vec3d center = new Vec3d(
                (minX + maxX) * 0.5,
                (minY + maxY) * 0.5,
                (minZ + maxZ) * 0.5);
            float horizontalRadius = (float)Math.Max(2.0, Math.Max(maxX - minX, maxZ - minZ) * 0.5 + 2.0);
            float verticalRadius = (float)Math.Max(2.0, (maxY - minY) * 0.5 + 2.0);

            Entity[] nearbyEntities = World.GetEntitiesAround(center, horizontalRadius, verticalRadius, ShouldCollideWith);
            for (int i = 0; i < nearbyEntities.Length; i++)
            {
                AddRestoreCarriedEntity(nearbyEntities[i], movingBoxes, entities, entityIds);
            }

            foreach (long entityId in lastSupportContraptionPositions.Keys)
            {
                Entity entity = World.GetEntityById(entityId);
                AddTrackedRestoreCarriedEntity(entity, entities, entityIds);
            }

            List<long> recentEntityIds = new List<long>(recentSupportMs.Keys);
            for (int i = 0; i < recentEntityIds.Count; i++)
            {
                long entityId = recentEntityIds[i];
                if (!IsRecentRestoreSupport(entityId))
                {
                    recentSupportMs.Remove(entityId);
                    recentSupportContraptionPositions.Remove(entityId);
                    continue;
                }

                Entity entity = World.GetEntityById(entityId);
                AddRecentRestoreCarriedEntity(entity, movingBoxes, entities, entityIds);
            }

            return entities;
        }

        private void AddRestoreCarriedEntity(Entity entity, Cuboidd[] movingBoxes, List<Entity> entities, HashSet<long> entityIds)
        {
            if (!ShouldCollideWith(entity)) return;
            if (!IsEntitySupportedByBoxes(entity, movingBoxes, RestoreMovingSupportBelowTolerance, RestoreMovingSupportAboveTolerance, RestoreSupportHorizontalSkin)) return;
            if (!entityIds.Add(entity.EntityId)) return;

            entities.Add(entity);
        }

        private void AddRecentRestoreCarriedEntity(Entity entity, Cuboidd[] movingBoxes, List<Entity> entities, HashSet<long> entityIds)
        {
            if (!ShouldCollideWith(entity)) return;
            if (!IsEntitySupportedByBoxes(entity, movingBoxes, RestoreRecentSupportBelowTolerance, RestoreMovingSupportAboveTolerance, RestoreRecentSupportHorizontalSkin)) return;
            if (!entityIds.Add(entity.EntityId)) return;

            entities.Add(entity);
        }

        private bool IsRecentRestoreSupport(long entityId)
        {
            return World != null
                && recentSupportMs.TryGetValue(entityId, out long lastMs)
                && World.ElapsedMilliseconds - lastMs <= RestoreRecentSupportMs;
        }

        private void AddTrackedRestoreCarriedEntity(Entity entity, List<Entity> entities, HashSet<long> entityIds)
        {
            if (!ShouldCollideWith(entity)) return;
            if (!entityIds.Add(entity.EntityId)) return;

            entities.Add(entity);
        }

        private Vec3d GetRestoreDelta(Vec3i[] offsets, string[] blockCodes, BlockPos[] restorePositions, int count)
        {
            for (int i = 0; i < count && i < offsets.Length && i < blockCodes.Length && i < restorePositions.Length; i++)
            {
                if (IsControllerSnapshotBlock(offsets[i], blockCodes[i])) continue;
                if (TryGetRestoreDeltaForIndex(offsets, blockCodes, restorePositions, i, out Vec3d delta)) return delta;
            }

            for (int i = 0; i < count && i < offsets.Length && i < blockCodes.Length && i < restorePositions.Length; i++)
            {
                if (TryGetRestoreDeltaForIndex(offsets, blockCodes, restorePositions, i, out Vec3d delta)) return delta;
            }

            return new Vec3d();
        }

        private Vec3d GetRestoreDeltaForBlock(Vec3i offset, BlockPos blockPos)
        {
            Vec3d movingPos = GetWorldPositionForOffset(offset);
            return new Vec3d(
                blockPos.X - movingPos.X,
                blockPos.InternalY - movingPos.Y,
                blockPos.Z - movingPos.Z);
        }

        private bool TryGetRestoreDeltaForIndex(Vec3i[] offsets, string[] blockCodes, BlockPos[] restorePositions, int index, out Vec3d delta)
        {
            delta = null;
            if (index < 0 || index >= offsets.Length || index >= blockCodes.Length || index >= restorePositions.Length) return false;
            if (string.IsNullOrEmpty(blockCodes[index])) return false;
            if (IsGantryShaftCode(blockCodes[index])) return false;
            if (IsSnapshotFluidCode(blockCodes[index])) return false;

            BlockPos blockPos = restorePositions[index];
            if (blockPos == null) return false;

            delta = GetRestoreDeltaForBlock(offsets[index], blockPos);
            return true;
        }

        private List<Cuboidd> BuildRestoredBoxes(BlockPos[] restorePositions, string[] blockCodes, int count, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ)
        {
            List<Cuboidd> restoredBoxes = new List<Cuboidd>();
            minX = double.MaxValue;
            minY = double.MaxValue;
            minZ = double.MaxValue;
            maxX = double.MinValue;
            maxY = double.MinValue;
            maxZ = double.MinValue;

            for (int i = 0; i < count && i < restorePositions.Length && i < blockCodes.Length; i++)
            {
                if (string.IsNullOrEmpty(blockCodes[i])) continue;
                if (IsGantryShaftCode(blockCodes[i])) continue;
                if (IsSnapshotFluidCode(blockCodes[i])) continue;

                BlockPos blockPos = restorePositions[i];
                if (blockPos == null) continue;

                double y = blockPos.InternalY;
                Cuboidd box = new Cuboidd(blockPos.X, y, blockPos.Z, blockPos.X + 1, y + 1, blockPos.Z + 1);
                restoredBoxes.Add(box);
                minX = Math.Min(minX, box.X1);
                minY = Math.Min(minY, box.Y1);
                minZ = Math.Min(minZ, box.Z1);
                maxX = Math.Max(maxX, box.X2);
                maxY = Math.Max(maxY, box.Y2);
                maxZ = Math.Max(maxZ, box.Z2);
            }

            return restoredBoxes;
        }

        private HashSet<long> CarryEntitiesThroughRestore(List<Entity> restoreCarriedEntities, Vec3d restoreDelta, List<Cuboidd> restoredBoxes)
        {
            HashSet<long> snappedEntityIds = new HashSet<long>();
            if (restoredBoxes == null || restoredBoxes.Count == 0) return snappedEntityIds;
            if (restoreCarriedEntities == null) return snappedEntityIds;

            for (int i = 0; i < restoreCarriedEntities.Count; i++)
            {
                Entity entity = restoreCarriedEntities[i];
                if (!ShouldCollideWith(entity)) continue;
                snappedEntityIds.Add(entity.EntityId);
                CarryEntityThroughRestore(entity, restoreDelta, restoredBoxes);
            }

            return snappedEntityIds;
        }

        private void SnapSupportedEntitiesToRestoredBlocks(List<Cuboidd> restoredBoxes, double minX, double minY, double minZ, double maxX, double maxY, double maxZ, HashSet<long> snappedEntityIds)
        {
            if (restoredBoxes == null || restoredBoxes.Count == 0) return;

            Vec3d center = new Vec3d(
                (minX + maxX) * 0.5,
                (minY + maxY) * 0.5,
                (minZ + maxZ) * 0.5);
            float horizontalRadius = (float)Math.Max(2.0, Math.Max(maxX - minX, maxZ - minZ) * 0.5 + 2.0);
            float verticalRadius = (float)Math.Max(2.0, (maxY - minY) * 0.5 + 2.0);

            Entity[] entities = World.GetEntitiesAround(center, horizontalRadius, verticalRadius, ShouldCollideWith);
            for (int i = 0; i < entities.Length; i++)
            {
                if (snappedEntityIds != null && snappedEntityIds.Contains(entities[i].EntityId)) continue;
                SnapEntityToRestoredTop(entities[i], restoredBoxes);
            }
        }

        private void CarryEntityThroughRestore(Entity entity, Vec3d restoreDelta, List<Cuboidd> restoredBoxes)
        {
            if (entity == null) return;

            double dx = restoreDelta?.X ?? 0;
            double dy = restoreDelta?.Y ?? 0;
            double dz = restoreDelta?.Z ?? 0;
            if (lastSupportContraptionPositions.TryGetValue(entity.EntityId, out Vec3d lastContraptionPos))
            {
                dx += SidedPos.X - lastContraptionPos.X;
                dy += SidedPos.InternalY - lastContraptionPos.Y;
                dz += SidedPos.Z - lastContraptionPos.Z;
            }
            else if (IsRecentRestoreSupport(entity.EntityId) && recentSupportContraptionPositions.TryGetValue(entity.EntityId, out Vec3d recentContraptionPos))
            {
                dx += SidedPos.X - recentContraptionPos.X;
                dy += SidedPos.InternalY - recentContraptionPos.Y;
                dz += SidedPos.Z - recentContraptionPos.Z;
            }

            if (dx * dx + dy * dy + dz * dz > 0.00000001)
            {
                entity.SidedPos.X += dx;
                entity.SidedPos.Y += dy;
                entity.SidedPos.Z += dz;

                if (entity.World?.Side == EnumAppSide.Server)
                {
                    entity.Pos.SetFrom(entity.ServerPos);
                }
            }

            SnapEntityToRestoredTop(entity, restoredBoxes, RestoreRecentSupportBelowTolerance, RestoreMovingSupportAboveTolerance);
        }

        private static void SnapEntityToRestoredTop(Entity entity, List<Cuboidd> restoredBoxes, double belowTolerance = RestoreSupportBelowTolerance, double aboveTolerance = RestoreSupportAboveTolerance)
        {
            if (entity == null) return;

            Cuboidd entityBox = GetWorldCollisionBox(entity);
            double bestTop = double.MinValue;
            double bestAbsDelta = double.MaxValue;

            for (int i = 0; i < restoredBoxes.Count; i++)
            {
                Cuboidd box = restoredBoxes[i];
                if (!OverlapsXZ(entityBox, box, RestoreSupportHorizontalSkin)) continue;

                double feetDelta = entityBox.Y1 - box.Y2;
                if (feetDelta < -belowTolerance || feetDelta > aboveTolerance) continue;

                double absDelta = Math.Abs(feetDelta);
                if (box.Y2 < bestTop - CollisionEpsilon) continue;
                if (Math.Abs(box.Y2 - bestTop) <= CollisionEpsilon && absDelta >= bestAbsDelta) continue;

                bestTop = box.Y2;
                bestAbsDelta = absDelta;
            }

            if (bestTop == double.MinValue) return;

            double correction = bestTop - entityBox.Y1;
            if (Math.Abs(correction) > TopSupportSnapThreshold)
            {
                entity.SidedPos.Y += correction;
            }

            if (entity.SidedPos.Motion != null && entity.SidedPos.Motion.Y < 0)
            {
                entity.SidedPos.Motion.Y = 0;
            }

            entity.CollidedVertically = true;
            entity.OnGround = true;
            entity.PositionBeforeFalling.Set(entity.SidedPos.X, entity.SidedPos.InternalY, entity.SidedPos.Z);
            if (entity.World?.Side == EnumAppSide.Server)
            {
                entity.Pos.SetFrom(entity.ServerPos);
            }
        }

        private static bool IsEntitySupportedByBoxes(Entity entity, Cuboidd[] boxes, double belowTolerance, double aboveTolerance, double skin)
        {
            if (entity == null || boxes == null) return false;

            Cuboidd entityBox = GetWorldCollisionBox(entity);
            for (int i = 0; i < boxes.Length; i++)
            {
                Cuboidd box = boxes[i];
                if (box == null) continue;

                Cuboidd grownBox = box.Clone().GrowBy(skin, CollisionEpsilon, skin);
                if (grownBox.Intersects(entityBox)) return true;
                if (!OverlapsXZ(entityBox, box, skin)) continue;

                double feetDelta = entityBox.Y1 - box.Y2;
                if (feetDelta >= -belowTolerance && feetDelta <= aboveTolerance) return true;
            }

            return false;
        }

        private static void GetBounds(Cuboidd[] boxes, out double minX, out double minY, out double minZ, out double maxX, out double maxY, out double maxZ)
        {
            minX = double.MaxValue;
            minY = double.MaxValue;
            minZ = double.MaxValue;
            maxX = double.MinValue;
            maxY = double.MinValue;
            maxZ = double.MinValue;

            for (int i = 0; i < boxes.Length; i++)
            {
                Cuboidd box = boxes[i];
                if (box == null) continue;

                minX = Math.Min(minX, box.X1);
                minY = Math.Min(minY, box.Y1);
                minZ = Math.Min(minZ, box.Z1);
                maxX = Math.Max(maxX, box.X2);
                maxY = Math.Max(maxY, box.Y2);
                maxZ = Math.Max(maxZ, box.Z2);
            }
        }

        private bool TryFindControllerRestorePosition(BlockPos preferred, BlockPos[] reservedPositions, int count, IPlayer player, bool overwrite, bool bypassClaims, out BlockPos restorePos)
        {
            restorePos = null;
            int[][] candidates =
            {
                new[] { 0, 1, 0 },
                new[] { 1, 0, 0 },
                new[] { -1, 0, 0 },
                new[] { 0, 0, 1 },
                new[] { 0, 0, -1 },
                new[] { 0, -1, 0 },
                new[] { 1, 1, 0 },
                new[] { -1, 1, 0 },
                new[] { 0, 1, 1 },
                new[] { 0, 1, -1 }
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                int[] offset = candidates[i];
                BlockPos candidate = preferred.AddCopy(offset[0], offset[1], offset[2]);
                if (IsReservedRestorePosition(candidate, reservedPositions, count)) continue;
                if (!CanRestoreBlockAt(candidate, player, overwrite, bypassClaims)) continue;

                restorePos = candidate;
                return true;
            }

            return false;
        }

        private bool CanRestoreBlockAt(BlockPos blockPos, IPlayer player, bool overwrite, bool bypassClaims = false)
        {
            if (!bypassClaims)
            {
                if (player != null && !World.Claims.TryAccess(player, blockPos, EnumBlockAccessFlags.BuildOrBreak)) return false;
                if (player == null && !CanAutomationBuildOrBreakAt(blockPos)) return false;
            }

            Block existing = World.BlockAccessor.GetBlock(blockPos);
            if (IsFluidBlock(existing)) return true;
            return overwrite || existing == null || existing.Id == 0;
        }

        public bool CanAutomationBuildOrBreakAt(BlockPos blockPos)
        {
            if (World?.Claims == null || blockPos == null) return true;

            string ownerUid = OwnerPlayerUid;
            IPlayer ownerPlayer = string.IsNullOrEmpty(ownerUid) ? null : World.PlayerByUid(ownerUid);
            if (!string.IsNullOrEmpty(ownerUid))
            {
                return AutomationClaimUtil.CanOwnerAccessClaim(World, blockPos, ownerUid, ownerPlayer, EnumBlockAccessFlags.BuildOrBreak);
            }

            return AutomationClaimUtil.CanAutomatedBlockAccess(World, ControllerPos, blockPos, EnumBlockAccessFlags.BuildOrBreak);
        }

        private static bool IsReservedRestorePosition(BlockPos candidate, BlockPos[] reservedPositions, int count)
        {
            for (int i = 0; i < count && i < reservedPositions.Length; i++)
            {
                BlockPos reserved = reservedPositions[i];
                if (reserved == null) continue;
                if (reserved.X == candidate.X && reserved.InternalY == candidate.InternalY && reserved.Z == candidate.Z && reserved.dimension == candidate.dimension) return true;
            }

            return false;
        }

        private static bool IsControllerSnapshotBlock(Vec3i offset, string blockCode)
        {
            return offset != null
                && offset.X == 0
                && offset.Y == 0
                && offset.Z == 0
                && blockCode != null
                && blockCode.StartsWith("vintagekinematics:gantrycarriage", StringComparison.Ordinal);
        }

        private static bool IsGantryShaft(Block block)
        {
            return block?.Code != null
                && block.Code.Domain == "vintagekinematics"
                && block.Code.FirstCodePart() == "gantryshaft";
        }

        private bool ContainsWorldBlockPosition(Vec3i[] offsets, string[] blockCodes, BlockPos pos)
        {
            int count = Math.Min(offsets?.Length ?? 0, blockCodes?.Length ?? 0);
            for (int i = 0; i < count; i++)
            {
                if (offsets[i] == null || string.IsNullOrEmpty(blockCodes[i])) continue;
                if (IsGantryShaftCode(blockCodes[i])) continue;
                if (IsSnapshotFluidCode(blockCodes[i])) continue;

                if (PositionKey(GetWorldBlockPositionForOffset(offsets[i])) == PositionKey(pos)) return true;
            }

            return false;
        }

        private static bool IsGantryShaftCode(string blockCode)
        {
            return blockCode != null
                && blockCode.StartsWith("vintagekinematics:gantryshaft", StringComparison.Ordinal);
        }

        private bool IsSnapshotFluidCode(string blockCode)
        {
            if (World == null || string.IsNullOrEmpty(blockCode)) return false;
            Block block = World.GetBlock(new AssetLocation(blockCode));
            return IsFluidBlock(block);
        }

        private static bool IsFluidBlock(Block block)
        {
            return block != null
                && (block.IsLiquid()
                    || !string.IsNullOrEmpty(block.LiquidCode)
                    || block.BlockMaterial == EnumBlockMaterial.Lava);
        }

        private static string PositionKey(BlockPos blockPos)
        {
            return blockPos.dimension + ":" + blockPos.X + "," + blockPos.InternalY + "," + blockPos.Z;
        }

        private static void AdjustControllerTreeForRestoreDelta(TreeAttribute controllerTree, Vec3i restoreDelta)
        {
            if (controllerTree == null || (restoreDelta.X == 0 && restoreDelta.Y == 0 && restoreDelta.Z == 0)) return;

            Vec3i min = controllerTree.GetVec3i("localMin");
            Vec3i max = controllerTree.GetVec3i("localMax");
            if (min != null) controllerTree.SetVec3i("localMin", OffsetFromRestoreDelta(min, restoreDelta));
            if (max != null) controllerTree.SetVec3i("localMax", OffsetFromRestoreDelta(max, restoreDelta));

            Vec3i[] controllerOffsets = controllerTree.GetVec3is("snapshotOffsets");
            if (controllerOffsets != null)
            {
                controllerTree.SetVec3is("snapshotOffsets", OffsetArrayFromRestoreDelta(controllerOffsets, restoreDelta));
            }

            Vec3i[] selectionOffsets = controllerTree.GetVec3is("selectionCellOffsets");
            if (selectionOffsets != null)
            {
                controllerTree.SetVec3is("selectionCellOffsets", OffsetArrayFromRestoreDelta(selectionOffsets, restoreDelta));
            }
        }

        private static Vec3i[] OffsetArrayFromRestoreDelta(Vec3i[] values, Vec3i restoreDelta)
        {
            Vec3i[] adjusted = new Vec3i[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                adjusted[i] = OffsetFromRestoreDelta(values[i], restoreDelta);
            }

            return adjusted;
        }

        private static Vec3i OffsetFromRestoreDelta(Vec3i value, Vec3i restoreDelta)
        {
            if (value == null) return null;
            return new Vec3i(value.X - restoreDelta.X, value.Y - restoreDelta.Y, value.Z - restoreDelta.Z);
        }

        private void RestoreBlockEntityTree(BlockPos blockPos, TreeAttribute savedTree)
        {
            if (savedTree == null || savedTree.Count == 0) return;

            BlockEntity be = World.BlockAccessor.GetBlockEntity(blockPos);
            if (be == null) return;

            TreeAttribute tree = savedTree.Clone() as TreeAttribute ?? new TreeAttribute();
            tree.SetInt("posx", blockPos.X);
            tree.SetInt("posy", blockPos.InternalY);
            tree.SetInt("posz", blockPos.Z);
            be.FromTreeAttributes(tree, World);
            be.MarkDirty(true);
        }

        private static void Notify(IPlayer player, string message)
        {
            if (player is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.GeneralChatGroup, message, EnumChatType.Notification);
            }
        }

        private void ResolveEntityCollision(Entity entity, Cuboidd contraptionBox, Cuboidd[] collisionBoxes)
        {
            CollisionDebugInfo debug = new CollisionDebugInfo();
            TryApplyPlatformCarry(entity, ref debug);

            Vec3d lastPos = GetLastEntityPosition(entity);
            Cuboidd previousEntityBox = GetWorldCollisionBox(entity, lastPos);
            Cuboidd entityBox = GetWorldCollisionBox(entity);
            bool startedOnGround = entity.OnGround;
            bool startedCollidedVertically = entity.CollidedVertically;
            Vec3d startMotion = entity.SidedPos.Motion?.Clone();

            bool supported = TryApplyTopSurfaceSupport(entity, entityBox, collisionBoxes, ref debug);
            if (!supported && IsRecentRiderSupport(entity.EntityId))
            {
                supported = TryApplyRecentTopSurfaceSupport(entity, entityBox, collisionBoxes, ref debug);
            }
            if (supported)
            {
                entityBox = GetWorldCollisionBox(entity);
                previousEntityBox = entityBox.Clone();
            }

            if (TryApplySneakEdgeStick(entity, entityBox, contraptionBox))
            {
                entityBox = GetWorldCollisionBox(entity);
                previousEntityBox = entityBox.Clone();
            }

            bool collided = false;
            int iterations = 0;
            if (!supported || debug.SupportCorrection >= TopSupportSnapThreshold)
            {
                for (int i = 0; i < CollisionResolveIterations; i++)
                {
                    Cuboidd collidingBox = FindBestCollidingBox(entityBox, collisionBoxes);
                    if (collidingBox == null) break;

                    GetEntryAwarePush(previousEntityBox, entityBox, collidingBox, out double pushX, out double pushY, out double pushZ, ref debug);
                    if (pushX == 0 && pushY == 0 && pushZ == 0) break;

                    ApplyCollisionPush(entity, pushX, pushY, pushZ);
                    entityBox = GetWorldCollisionBox(entity);
                    previousEntityBox.Translate(pushX, pushY, pushZ);
                    debug.PushX += pushX;
                    debug.PushY += pushY;
                    debug.PushZ += pushZ;
                    iterations++;
                    collided = true;
                }
            }

            if ((supported || collided) && World.Side == EnumAppSide.Server)
            {
                entity.Pos.SetFrom(entity.ServerPos);
            }

            if (supported)
            {
                lastSupportContraptionPositions[entity.EntityId] = SidedPos.XYZ.Clone();
                recentSupportContraptionPositions[entity.EntityId] = SidedPos.XYZ.Clone();
                recentSupportMs[entity.EntityId] = World.ElapsedMilliseconds;
                RecordRiderSupport(entity);
            }
            else
            {
                lastSupportContraptionPositions.Remove(entity.EntityId);
            }

            LogCollisionDebug(entity, startedOnGround, startedCollidedVertically, startMotion, supported, collided, iterations, debug);
            lastEntityPositions[entity.EntityId] = entity.SidedPos.XYZ.Clone();
        }

        private bool TryApplyPlatformCarry(Entity entity, ref CollisionDebugInfo debug)
        {
            if (!lastSupportContraptionPositions.TryGetValue(entity.EntityId, out Vec3d lastContraptionPos)) return false;
            if (entity.SidedPos.Motion != null && entity.SidedPos.Motion.Y > 0.01)
            {
                lastSupportContraptionPositions.Remove(entity.EntityId);
                riderSupportStates.Remove(entity.EntityId);
                return false;
            }

            Vec3d now = SidedPos.XYZ;
            double dx = now.X - lastContraptionPos.X;
            double dy = now.Y - lastContraptionPos.Y;
            double dz = now.Z - lastContraptionPos.Z;
            if (dx * dx + dy * dy + dz * dz < 0.00000001) return false;

            EntityPos targetPos = entity.SidedPos;
            targetPos.X += dx;
            targetPos.Y += dy;
            targetPos.Z += dz;
            if (World.Side == EnumAppSide.Server)
            {
                entity.Pos.SetFrom(entity.ServerPos);
            }

            debug.PlatformX = dx;
            debug.PlatformY = dy;
            debug.PlatformZ = dz;
            return true;
        }

        private Vec3d GetLastEntityPosition(Entity entity)
        {
            if (lastEntityPositions.TryGetValue(entity.EntityId, out Vec3d lastPos))
            {
                return lastPos;
            }

            Vec3d current = entity.SidedPos.XYZ.Clone();
            if (entity.SidedPos.Motion != null)
            {
                current.X -= entity.SidedPos.Motion.X;
                current.Y -= entity.SidedPos.Motion.Y;
                current.Z -= entity.SidedPos.Motion.Z;
            }

            return current;
        }

        private void ApplyCollisionPush(Entity entity, double pushX, double pushY, double pushZ)
        {
            EntityPos targetPos = entity.SidedPos;
            targetPos.X += pushX;
            targetPos.Y += pushY;
            targetPos.Z += pushZ;

            if (pushX != 0)
            {
                entity.CollidedHorizontally = true;
                DampMotionAxis(targetPos.Motion, EnumAxis.X, pushX);
            }

            if (pushZ != 0)
            {
                entity.CollidedHorizontally = true;
                DampMotionAxis(targetPos.Motion, EnumAxis.Z, pushZ);
            }

            if (pushY != 0)
            {
                entity.CollidedVertically = true;
                DampMotionAxis(targetPos.Motion, EnumAxis.Y, pushY);
                if (pushY > 0)
                {
                    entity.OnGround = true;
                    entity.PositionBeforeFalling.Set(targetPos.X, targetPos.InternalY, targetPos.Z);
                }
            }
        }

        private static Cuboidd FindBestCollidingBox(Cuboidd entityBox, Cuboidd[] collisionBoxes)
        {
            Cuboidd best = null;
            double bestPush = double.MaxValue;

            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                Cuboidd box = collisionBoxes[i].Clone().GrowBy(HorizontalCollisionSkin, 0, HorizontalCollisionSkin);
                if (!box.Intersects(entityBox)) continue;
                GetSmallestPush(entityBox, box, out double pushX, out double pushY, out double pushZ);
                double push = Math.Abs(pushX) + Math.Abs(pushY) + Math.Abs(pushZ);
                if (push < bestPush)
                {
                    bestPush = push;
                    best = box;
                }
            }

            return best;
        }

        private static bool TryApplyTopSurfaceSupport(Entity entity, Cuboidd entityBox, Cuboidd[] collisionBoxes, ref CollisionDebugInfo debug)
        {
            if (entity.SidedPos.Motion != null && entity.SidedPos.Motion.Y > 0.01) return false;

            Cuboidd supportBox = null;
            double bestDelta = double.MaxValue;
            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                Cuboidd box = collisionBoxes[i];
                if (!OverlapsXZ(entityBox, box)) continue;

                double feetDelta = entityBox.Y1 - box.Y2;
                if (feetDelta < -TopSupportSinkTolerance || feetDelta > TopSupportHoverTolerance) continue;

                double absDelta = Math.Abs(feetDelta);
                if (absDelta >= bestDelta) continue;

                bestDelta = absDelta;
                supportBox = box;
            }

            if (supportBox == null) return false;

            EntityPos targetPos = entity.SidedPos;
            double correction = supportBox.Y2 - entityBox.Y1;
            debug.SupportFeetDelta = entityBox.Y1 - supportBox.Y2;
            debug.SupportCorrection = correction >= TopSupportSnapThreshold ? correction : 0;
            debug.SupportTopY = supportBox.Y2;
            if (correction >= TopSupportSnapThreshold)
            {
                targetPos.Y += correction;
            }

            if (targetPos.Motion != null && targetPos.Motion.Y < 0)
            {
                targetPos.Motion.Y = 0;
            }

            entity.CollidedVertically = true;
            entity.OnGround = true;
            entity.PositionBeforeFalling.Set(targetPos.X, targetPos.InternalY, targetPos.Z);
            return true;
        }

        private static bool TryApplyRecentTopSurfaceSupport(Entity entity, Cuboidd entityBox, Cuboidd[] collisionBoxes, ref CollisionDebugInfo debug)
        {
            if (entity.SidedPos.Motion != null && entity.SidedPos.Motion.Y > 0.01) return false;

            Cuboidd supportBox = null;
            double bestDelta = double.MaxValue;
            for (int i = 0; i < collisionBoxes.Length; i++)
            {
                Cuboidd box = collisionBoxes[i];
                if (!OverlapsXZ(entityBox, box, RiderSupportHorizontalSkin)) continue;

                double feetDelta = entityBox.Y1 - box.Y2;
                if (feetDelta < -RestoreMovingSupportBelowTolerance || feetDelta > RestoreMovingSupportAboveTolerance) continue;

                double absDelta = Math.Abs(feetDelta);
                if (absDelta >= bestDelta) continue;

                bestDelta = absDelta;
                supportBox = box;
            }

            if (supportBox == null) return false;

            EntityPos targetPos = entity.SidedPos;
            double correction = supportBox.Y2 - entityBox.Y1;
            debug.SupportFeetDelta = entityBox.Y1 - supportBox.Y2;
            debug.SupportCorrection = Math.Abs(correction) >= TopSupportSnapThreshold ? correction : 0;
            debug.SupportTopY = supportBox.Y2;
            if (Math.Abs(correction) >= TopSupportSnapThreshold)
            {
                targetPos.Y += correction;
            }

            if (targetPos.Motion != null && targetPos.Motion.Y < 0)
            {
                targetPos.Motion.Y = 0;
            }

            entity.CollidedVertically = true;
            entity.OnGround = true;
            entity.PositionBeforeFalling.Set(targetPos.X, targetPos.InternalY, targetPos.Z);
            return true;
        }

        private static bool OverlapsXZ(Cuboidd entityBox, Cuboidd solidBox)
        {
            return entityBox.X2 > solidBox.X1
                && entityBox.X1 < solidBox.X2
                && entityBox.Z2 > solidBox.Z1
                && entityBox.Z1 < solidBox.Z2;
        }

        private static bool OverlapsXZ(Cuboidd entityBox, Cuboidd solidBox, double skin)
        {
            return entityBox.X2 > solidBox.X1 - skin
                && entityBox.X1 < solidBox.X2 + skin
                && entityBox.Z2 > solidBox.Z1 - skin
                && entityBox.Z1 < solidBox.Z2 + skin;
        }

        private static void GetEntryAwarePush(Cuboidd previousEntityBox, Cuboidd entityBox, Cuboidd solidBox, out double pushX, out double pushY, out double pushZ, ref CollisionDebugInfo debug)
        {
            pushX = 0;
            pushY = 0;
            pushZ = 0;

            double dx = Center(entityBox.X1, entityBox.X2) - Center(previousEntityBox.X1, previousEntityBox.X2);
            double dy = Center(entityBox.Y1, entityBox.Y2) - Center(previousEntityBox.Y1, previousEntityBox.Y2);
            double dz = Center(entityBox.Z1, entityBox.Z2) - Center(previousEntityBox.Z1, previousEntityBox.Z2);

            double bestAmount = double.MaxValue;
            EnumAxis bestAxis = EnumAxis.X;
            double bestPush = 0;

            TryEntryAxis(Math.Abs(dx), dx > 0 && previousEntityBox.X2 <= solidBox.X1, solidBox.X1 - entityBox.X2, EnumAxis.X, ref bestAmount, ref bestAxis, ref bestPush);
            TryEntryAxis(Math.Abs(dx), dx < 0 && previousEntityBox.X1 >= solidBox.X2, solidBox.X2 - entityBox.X1, EnumAxis.X, ref bestAmount, ref bestAxis, ref bestPush);
            TryEntryAxis(Math.Abs(dy), dy > 0 && previousEntityBox.Y2 <= solidBox.Y1, solidBox.Y1 - entityBox.Y2, EnumAxis.Y, ref bestAmount, ref bestAxis, ref bestPush);
            TryEntryAxis(Math.Abs(dy), dy < 0 && previousEntityBox.Y1 >= solidBox.Y2, solidBox.Y2 - entityBox.Y1, EnumAxis.Y, ref bestAmount, ref bestAxis, ref bestPush);
            TryEntryAxis(Math.Abs(dz), dz > 0 && previousEntityBox.Z2 <= solidBox.Z1, solidBox.Z1 - entityBox.Z2, EnumAxis.Z, ref bestAmount, ref bestAxis, ref bestPush);
            TryEntryAxis(Math.Abs(dz), dz < 0 && previousEntityBox.Z1 >= solidBox.Z2, solidBox.Z2 - entityBox.Z1, EnumAxis.Z, ref bestAmount, ref bestAxis, ref bestPush);

            if (bestAmount == double.MaxValue)
            {
                GetSmallestPush(entityBox, solidBox, out pushX, out pushY, out pushZ);
                debug.FallbackPushes++;
                debug.LastAxis = AxisName(pushX, pushY, pushZ);
                return;
            }

            switch (bestAxis)
            {
                case EnumAxis.X:
                    pushX = bestPush;
                    debug.LastAxis = "X";
                    break;
                case EnumAxis.Y:
                    pushY = bestPush;
                    debug.LastAxis = "Y";
                    break;
                case EnumAxis.Z:
                    pushZ = bestPush;
                    debug.LastAxis = "Z";
                    break;
            }
        }

        private void LogCollisionDebug(Entity entity, bool startedOnGround, bool startedCollidedVertically, Vec3d startMotion, bool supported, bool collided, int iterations, CollisionDebugInfo debug)
        {
            if (!DebugCollision || World?.Side != EnumAppSide.Server) return;
            if (entity is not EntityPlayer player) return;
            if (!supported && !collided) return;

            long now = World.ElapsedMilliseconds;
            if (lastCollisionDebugMs.TryGetValue(entity.EntityId, out long lastMs) && now - lastMs < DebugCollisionIntervalMs) return;
            lastCollisionDebugMs[entity.EntityId] = now;

            Vec3d motion = entity.SidedPos.Motion;
            World.Logger.Notification(
                "[VK collision] player={0} pos=({1:0.000},{2:0.000},{3:0.000}) motion {4}->{5} support={6} feetDelta={7:0.0000} corr={8:0.0000} topY={9:0.000} collided={10} it={11} push=({12:0.0000},{13:0.0000},{14:0.0000}) axis={15} fallback={16} ground {17}->{18} vert {19}->{20}",
                player.Player?.PlayerName ?? player.PlayerUID ?? entity.EntityId.ToString(),
                entity.SidedPos.X,
                entity.SidedPos.InternalY,
                entity.SidedPos.Z,
                FormatVec(startMotion),
                FormatVec(motion),
                supported,
                debug.SupportFeetDelta,
                debug.SupportCorrection,
                debug.SupportTopY,
                collided,
                iterations,
                debug.PushX,
                debug.PushY,
                debug.PushZ,
                debug.LastAxis ?? "-",
                debug.FallbackPushes,
                startedOnGround,
                entity.OnGround,
                startedCollidedVertically,
                entity.CollidedVertically);
        }

        private static string FormatVec(Vec3d vec)
        {
            return vec == null ? "(null)" : $"({vec.X:0.000},{vec.Y:0.000},{vec.Z:0.000})";
        }

        private static string AxisName(double pushX, double pushY, double pushZ)
        {
            if (pushX != 0) return "X-fallback";
            if (pushY != 0) return "Y-fallback";
            if (pushZ != 0) return "Z-fallback";
            return "none";
        }

        private struct CollisionDebugInfo
        {
            public double SupportFeetDelta;
            public double SupportCorrection;
            public double SupportTopY;
            public double PushX;
            public double PushY;
            public double PushZ;
            public double PlatformX;
            public double PlatformY;
            public double PlatformZ;
            public int FallbackPushes;
            public string LastAxis;
        }

        private static void TryEntryAxis(double motionAmount, bool enteredFromThisSide, double push, EnumAxis axis, ref double bestAmount, ref EnumAxis bestAxis, ref double bestPush)
        {
            if (!enteredFromThisSide || motionAmount > bestAmount) return;

            bestAmount = motionAmount;
            bestAxis = axis;
            bestPush = push;
        }

        private static double Center(double min, double max)
        {
            return (min + max) * 0.5;
        }

        private static bool TryApplySneakEdgeStick(Entity entity, Cuboidd entityBox, Cuboidd solidBox)
        {
            if (entity is not EntityAgent agent) return false;
            if (!IsSneaking(agent)) return false;
            if (!IsCloseToTopSurface(entityBox, solidBox)) return false;
            if (!OverlapsOrNearlyOverlapsXZ(entityBox, solidBox, SneakEdgeTolerance)) return false;

            EntityPos targetPos = entity.SidedPos;
            double oldX = targetPos.X;
            double oldZ = targetPos.Z;

            targetPos.X = ClampAxisInsideSolid(
                solidBox.X1,
                solidBox.X2,
                entity.CollisionBox.X1,
                entity.CollisionBox.X2,
                targetPos.X);

            targetPos.Z = ClampAxisInsideSolid(
                solidBox.Z1,
                solidBox.Z2,
                entity.CollisionBox.Z1,
                entity.CollisionBox.Z2,
                targetPos.Z);

            bool moved = Math.Abs(oldX - targetPos.X) > 0.00001 || Math.Abs(oldZ - targetPos.Z) > 0.00001;
            if (!moved) return false;

            entity.OnGround = true;
            entity.CollidedHorizontally = true;
            if (Math.Abs(oldX - targetPos.X) > 0.00001) targetPos.Motion.X = 0;
            if (Math.Abs(oldZ - targetPos.Z) > 0.00001) targetPos.Motion.Z = 0;

            if (entity.World.Side == EnumAppSide.Server)
            {
                entity.Pos.SetFrom(entity.ServerPos);
            }

            return true;
        }

        private static bool IsSneaking(EntityAgent agent)
        {
            return agent.Controls?.Sneak == true
                || agent.Controls?.ShiftKey == true
                || agent.ServerControls?.Sneak == true
                || agent.ServerControls?.ShiftKey == true
                || (agent.CurrentControls & EnumEntityActivity.SneakMode) != 0;
        }

        private static bool IsCloseToTopSurface(Cuboidd entityBox, Cuboidd solidBox)
        {
            double feetDelta = entityBox.Y1 - solidBox.Y2;
            return feetDelta >= -SneakSupportBelowTolerance && feetDelta <= SneakSupportAboveTolerance;
        }

        private static bool OverlapsOrNearlyOverlapsXZ(Cuboidd entityBox, Cuboidd solidBox, double tolerance)
        {
            return entityBox.X2 > solidBox.X1 - tolerance
                && entityBox.X1 < solidBox.X2 + tolerance
                && entityBox.Z2 > solidBox.Z1 - tolerance
                && entityBox.Z1 < solidBox.Z2 + tolerance;
        }

        private static double ClampAxisInsideSolid(double solidMin, double solidMax, double entityRelMin, double entityRelMax, double entityPosAxis)
        {
            double minPos = solidMin - entityRelMin;
            double maxPos = solidMax - entityRelMax;
            if (minPos > maxPos)
            {
                return (minPos + maxPos) * 0.5;
            }

            return GameMath.Clamp(entityPosAxis, minPos, maxPos);
        }

        private static void GetSmallestPush(Cuboidd entityBox, Cuboidd solidBox, out double pushX, out double pushY, out double pushZ)
        {
            double pushWest = solidBox.X1 - entityBox.X2;
            double pushEast = solidBox.X2 - entityBox.X1;
            double pushDown = solidBox.Y1 - entityBox.Y2;
            double pushUp = solidBox.Y2 - entityBox.Y1;
            double pushNorth = solidBox.Z1 - entityBox.Z2;
            double pushSouth = solidBox.Z2 - entityBox.Z1;

            pushX = Math.Abs(pushWest) < Math.Abs(pushEast) ? pushWest : pushEast;
            pushY = Math.Abs(pushDown) < Math.Abs(pushUp) ? pushDown : pushUp;
            pushZ = Math.Abs(pushNorth) < Math.Abs(pushSouth) ? pushNorth : pushSouth;

            double absX = Math.Abs(pushX);
            double absY = Math.Abs(pushY);
            double absZ = Math.Abs(pushZ);

            if (absY <= absX && absY <= absZ)
            {
                pushX = 0;
                pushZ = 0;
                return;
            }

            if (absX <= absZ)
            {
                pushY = 0;
                pushZ = 0;
                return;
            }

            pushX = 0;
            pushY = 0;
        }

        private static void DampMotionAxis(Vec3d motion, EnumAxis axis, double push)
        {
            if (motion == null) return;

            switch (axis)
            {
                case EnumAxis.X:
                    if (Math.Sign(motion.X) != Math.Sign(push)) motion.X = 0;
                    break;
                case EnumAxis.Y:
                    if (Math.Sign(motion.Y) != Math.Sign(push)) motion.Y = 0;
                    break;
                case EnumAxis.Z:
                    if (Math.Sign(motion.Z) != Math.Sign(push)) motion.Z = 0;
                    break;
            }
        }
    }
}
#pragma warning restore CS0618
