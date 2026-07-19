using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BEKineticSensor : BEKineticAnimated, IKineticExclusiveConnector
    {
        private static readonly AssetLocation ToggleSound = new AssetLocation("sounds/effect/woodswitch");

        private readonly List<InventoryBase> monitoredInventories = new List<InventoryBase>();
        private bool active;
        private bool lastTriggerWorked;

        public KineticSensorMode Mode { get; private set; } = KineticSensorMode.Overstressed;
        public KineticSensorTriggerMode TriggerMode { get; private set; } = KineticSensorTriggerMode.RisingEdge;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            if (api.Side != EnumAppSide.Server) return;

            var mgr = api.ModLoader.GetModSystem<KineticNetworkManager>();
            if (mgr != null)
            {
                mgr.NetworkBuilt += OnNetworkChanged;
                mgr.NetworkStateChanged += OnNetworkChanged;
                mgr.NetworkRemoved += OnNetworkRemoved;
            }

            api.Event.RegisterCallback(_ =>
            {
                RefreshMonitoredInventory();
                SetActive(EvaluateCondition(), trigger: false);
            }, 0);
        }

        public void CycleMode(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            Mode = Mode switch
            {
                KineticSensorMode.Overstressed => KineticSensorMode.StorageFull,
                KineticSensorMode.StorageFull => KineticSensorMode.Powered,
                _ => KineticSensorMode.Overstressed
            };

            if (Mode == KineticSensorMode.StorageFull)
            {
                RefreshMonitoredInventory();
            }
            else
            {
                ClearMonitoredInventories();
                SetActive(EvaluateCondition(), trigger: false);
            }

            Api.World.PlaySoundAt(ToggleSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, randomizePitch: true, range: 12, volume: 0.45f);
            MarkDirty(true);
        }

        public void CycleTriggerMode(IPlayer byPlayer)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            TriggerMode = TriggerMode switch
            {
                KineticSensorTriggerMode.RisingEdge => KineticSensorTriggerMode.FallingEdge,
                KineticSensorTriggerMode.FallingEdge => KineticSensorTriggerMode.AnyEdge,
                KineticSensorTriggerMode.AnyEdge => KineticSensorTriggerMode.WhileTrue,
                KineticSensorTriggerMode.WhileTrue => KineticSensorTriggerMode.WhileFalse,
                _ => KineticSensorTriggerMode.RisingEdge
            };

            Api.World.PlaySoundAt(ToggleSound, Pos.X + 0.5, Pos.Y + 0.5, Pos.Z + 0.5, byPlayer, randomizePitch: true, range: 12, volume: 0.45f);
            MarkDirty(true);
        }

        public void RefreshMonitoredInventory()
        {
            if (Api?.Side != EnumAppSide.Server) return;

            ClearMonitoredInventories();

            HashSet<InventoryBase> seen = new HashSet<InventoryBase>();
            foreach (BlockFacing facing in BlockFacing.ALLFACES)
            {
                BlockPos monitorPos = Pos.AddCopy(facing);
                BlockEntity be = MultiblockHelper.GetMultiblockAwareBE(Api.World, monitorPos)
                    ?? Api.World.BlockAccessor.GetBlockEntity(monitorPos);
                InventoryBase inv = InventoryOf(be);
                if (inv == null) continue;
                if (!seen.Add(inv)) continue;

                monitoredInventories.Add(inv);
                inv.SlotModified += OnMonitoredInventorySlotModified;
            }

            SetActive(EvaluateCondition(), trigger: true);
        }

        private void OnMonitoredInventorySlotModified(int slotId)
        {
            if (Mode == KineticSensorMode.StorageFull)
            {
                SetActive(EvaluateCondition(), trigger: true);
            }
        }

        private void OnNetworkChanged(KineticNetwork net)
        {
            if (net == null || !net.Nodes.ContainsKey(Pos)) return;
            if (Mode == KineticSensorMode.Overstressed || Mode == KineticSensorMode.Powered)
            {
                SetActive(EvaluateCondition(), trigger: true);
            }
        }

        private void OnNetworkRemoved(KineticNetwork net)
        {
            if (net == null || !net.Nodes.ContainsKey(Pos)) return;
            Api.Event.RegisterCallback(_ =>
            {
                if (Api?.World?.BlockAccessor.GetBlockEntity(Pos) != this) return;
                SetActive(EvaluateCondition(), trigger: Mode == KineticSensorMode.Powered);
            }, 0);
        }

        private bool EvaluateCondition()
        {
            if (Mode == KineticSensorMode.StorageFull)
            {
                return IsAnyMonitoredInventoryFull();
            }

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            if (Mode == KineticSensorMode.Powered)
            {
                return kinetic != null
                    && MathF.Abs(kinetic.ActualRPM) >= KineticNetwork.MinAbsRPM
                    && !kinetic.IsConflicted
                    && !(kinetic.EffectiveNetwork?.IsOverstressed ?? false);
            }

            return KineticConditionEvaluator.Evaluate(
                kinetic?.EffectiveNetwork,
                new KineticConditionSettings(KineticConditionType.Overstressed));
        }

        private bool IsAnyMonitoredInventoryFull()
        {
            if (monitoredInventories.Count == 0) return false;
            foreach (InventoryBase inventory in monitoredInventories)
            {
                if (IsInventoryFull(inventory)) return true;
            }
            return false;
        }

        private static InventoryBase InventoryOf(BlockEntity be)
        {
            if (be == null) return null;
            if (be is IBlockEntityContainer container && container.Inventory is InventoryBase inv)
            {
                return inv;
            }

            Type type = be.GetType();
            PropertyInfo prop = type.GetProperty("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (prop?.GetValue(be) is InventoryBase propInv)
            {
                return propInv;
            }

            FieldInfo field = type.GetField("inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?? type.GetField("Inventory", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(be) as InventoryBase;
        }

        private static bool IsInventoryFull(IInventory inventory)
        {
            if (inventory == null || inventory.Count <= 0) return false;

            for (int i = 0; i < inventory.Count; i++)
            {
                ItemSlot slot = inventory[i];
                if (slot == null || slot.Empty || slot.Itemstack?.Collectible == null) return false;
            }

            return true;
        }

        private void SetActive(bool next, bool trigger)
        {
            bool previous = active;
            bool changed = next != previous;
            bool rising = next && !previous;
            bool falling = !next && previous;

            active = next;
            if (trigger && ShouldTrigger(next, changed, rising, falling))
            {
                TryTriggerFrontTarget();
            }

            if (changed) MarkDirty(true);
        }

        private bool ShouldTrigger(bool conditionActive, bool changed, bool rising, bool falling)
        {
            return TriggerMode switch
            {
                KineticSensorTriggerMode.RisingEdge => rising,
                KineticSensorTriggerMode.FallingEdge => falling,
                KineticSensorTriggerMode.AnyEdge => changed,
                KineticSensorTriggerMode.WhileTrue => conditionActive,
                KineticSensorTriggerMode.WhileFalse => !conditionActive,
                _ => rising
            };
        }

        private bool TryTriggerFrontTarget()
        {
            BlockFacing front = FrontFacing();
            float rpm = GetBehavior<BEBehaviorKinetic>()?.ActualRPM ?? 0f;
            lastTriggerWorked = KineticActivationUtil.TryActivateTarget(
                Api,
                Pos,
                front,
                rpm,
                allowFallbackActivate: false,
                useActivatorBlacklist: false,
                out _);
            MarkDirty(true);
            return lastTriggerWorked;
        }

        public KineticConnectionResult? TryConnect(KineticNodeInfo self, KineticNodeInfo other, BlockPos fromPos, BlockPos toPos)
        {
            BlockFacing back = BackFacing();
            if (toPos.X != fromPos.X + back.Normali.X) return null;
            if (toPos.Y != fromPos.Y + back.Normali.Y) return null;
            if (toPos.Z != fromPos.Z + back.Normali.Z) return null;

            if (other.Role == EnumKineticRole.Gearbox)
            {
                Vec3i offset = new Vec3i(toPos.X - fromPos.X, toPos.Y - fromPos.Y, toPos.Z - fromPos.Z);
                EnumKineticAxis faceAxis = EnumKineticAxisExtensions.FromVec(offset);
                if (faceAxis != self.Axis || faceAxis == other.Axis) return null;

                return new KineticConnectionResult(1f, -(offset.X + offset.Y + offset.Z));
            }

            if (other.Axis != self.Axis) return null;
            if (other.Role == EnumKineticRole.Custom) return null;

            return new KineticConnectionResult(1f, 1);
        }

        private BlockFacing FrontFacing()
        {
            return Block?.Variant["side"] switch
            {
                "n" => BlockFacing.NORTH,
                "e" => BlockFacing.EAST,
                "s" => BlockFacing.SOUTH,
                "w" => BlockFacing.WEST,
                "u" => BlockFacing.UP,
                "d" => BlockFacing.DOWN,
                _ => BlockFacing.NORTH
            };
        }

        private BlockFacing BackFacing()
        {
            return FrontFacing().Opposite;
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetInt("sensorMode", (int)Mode);
            tree.SetInt("sensorTriggerMode", (int)TriggerMode);
            tree.SetBool("sensorActive", active);
            tree.SetBool("lastTriggerWorked", lastTriggerWorked);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            Mode = (KineticSensorMode)tree.GetInt("sensorMode", (int)KineticSensorMode.Overstressed);
            TriggerMode = (KineticSensorTriggerMode)tree.GetInt("sensorTriggerMode", (int)KineticSensorTriggerMode.RisingEdge);
            active = tree.GetBool("sensorActive", false);
            lastTriggerWorked = tree.GetBool("lastTriggerWorked", false);
        }

        protected override void ConfigureStaticShape(Shape shape)
        {
            if (!active)
            {
                shape?.RemoveElements(new[] { "indicatorLensOn" });
            }
        }

        public override void OnBlockUnloaded()
        {
            Unsubscribe();
            base.OnBlockUnloaded();
        }

        public override void OnBlockRemoved()
        {
            Unsubscribe();
            base.OnBlockRemoved();
        }

        private void Unsubscribe()
        {
            ClearMonitoredInventories();

            var mgr = Api?.ModLoader.GetModSystem<KineticNetworkManager>();
            if (mgr == null) return;
            mgr.NetworkBuilt -= OnNetworkChanged;
            mgr.NetworkStateChanged -= OnNetworkChanged;
            mgr.NetworkRemoved -= OnNetworkRemoved;
        }

        private void ClearMonitoredInventories()
        {
            foreach (InventoryBase inventory in monitoredInventories)
            {
                inventory.SlotModified -= OnMonitoredInventorySlotModified;
            }
            monitoredInventories.Clear();
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);
            dsc.AppendLine($"Sensor mode: {ModeName()}");
            dsc.AppendLine($"Trigger mode: {TriggerModeName()}");
            dsc.AppendLine(active ? "Sensor: active" : "Sensor: inactive");
            if (Mode == KineticSensorMode.StorageFull)
            {
                dsc.AppendLine($"Watched storages: {monitoredInventories.Count}");
            }
            dsc.AppendLine(lastTriggerWorked ? "Last trigger: accepted" : "Last trigger: waiting");
        }

        private string ModeName()
        {
            return Mode switch
            {
                KineticSensorMode.StorageFull => "storage full",
                KineticSensorMode.Powered => "powered",
                _ => "overstressed network"
            };
        }

        private string TriggerModeName()
        {
            return TriggerMode switch
            {
                KineticSensorTriggerMode.RisingEdge => "turns true",
                KineticSensorTriggerMode.FallingEdge => "turns false",
                KineticSensorTriggerMode.AnyEdge => "changes",
                KineticSensorTriggerMode.WhileTrue => "while true",
                KineticSensorTriggerMode.WhileFalse => "while false",
                _ => "turns true"
            };
        }
    }
}
