using System;
using System.IO;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using VintageKinematics.Api;
using VintageKinematics.Gui;
using VintageKinematics.Network;

namespace VintageKinematics.BlockEntities
{
    public class BETrebuchet : BEKineticAnimated, IMountableSeat, IMountable
    {
        public const int PacketIdOpenDialog = 5800;
        public const int PacketIdSetSettings = 5801;
        public const int PacketIdLaunch = 5802;
        public const int PacketIdApplyLaunch = 5803;

        private const float MinDistance = 8f;
        private const float MaxDistance = 1024f;
        private const float MinAngle = 15f;
        private const float MaxAngle = 75f;
        private const float RatedRpm = 16f;
        private const int LaunchCollisionGraceMs = 2000;
        private const string TreeDistanceKey = "trebuchetDistance";
        private const string TreeAngleKey = "trebuchetAngle";
        private const string TreeLaunchCollisionRemainingKey = "trebuchetLaunchCollisionRemainingMs";
        private readonly EntityControls controls = new EntityControls();
        private readonly EntityPos seatPos = new EntityPos();
        private readonly Vec3f eyePos = new Vec3f(0f, 1.35f, 0f);

        private EntityAgent mountedBy;
        private long mountedByEntityId;
        private string mountedByPlayerUid;
        private bool blockBroken;
        private bool launching;

        private float distance = 64f;
        private float angle = 45f;
        private string lastStatus = "Ready";
        private long launchCollisionDisabledUntilMs;

        private GuiDialogTrebuchet clientDialog;

        public float RequiredSu => ComputeRequiredSu(distance, angle);
        public bool LaunchCollisionSuppressed => Api?.World != null && launchCollisionDisabledUntilMs > Api.World.ElapsedMilliseconds;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            controls.OnAction = OnControls;
            RestoreMountedEntity(api);
            ApplyStress(false);
        }

        public bool OnPlayerRightClick(IPlayer byPlayer)
        {
            if (Api.World is IServerWorldAccessor)
            {
                SendDialogState(byPlayer);
            }
            return true;
        }

        private void SendDialogState(IPlayer byPlayer)
        {
            if (Api.Side != EnumAppSide.Server || byPlayer is not IServerPlayer serverPlayer) return;

            string title = Lang.Get("vintagekinematics:trebuchet-title");
            if (string.IsNullOrEmpty(title) || title == "vintagekinematics:trebuchet-title") title = "Kinetic Trebuchet";

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(title);
            bw.Write(distance);
            bw.Write(angle);
            bw.Write(RequiredSu);
            bw.Write(lastStatus ?? "");
            ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(serverPlayer, Pos, PacketIdOpenDialog, ms.ToArray());
        }

        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (!CheckClaim(player)) return;

            if (packetid == PacketIdSetSettings)
            {
                ReadSettingsPacket(data, out float newDistance, out float newAngle);
                SetSettings(newDistance, newAngle, true);
                lastStatus = Lang.Get("vintagekinematics:trebuchet-status-ready");
                MarkDirty(true);
                SendDialogState(player);
                return;
            }

            if (packetid == PacketIdLaunch)
            {
                ReadSettingsPacket(data, out float newDistance, out float newAngle);
                SetSettings(newDistance, newAngle, true);
                TryLaunch(player);
                SendDialogState(player);
            }
        }

        public override void OnReceivedServerPacket(int packetid, byte[] data)
        {
            if (packetid == PacketIdApplyLaunch)
            {
                ReadLaunchPacket(data, out Vec3d launchPos, out Vec3d motion);
                EntityPlayer player = (Api as ICoreClientAPI)?.World.Player?.Entity;
                if (player != null)
                {
                    SuppressLaunchCollision();
                    ApplyLaunch(player, launchPos, motion);
                }
                return;
            }

            if (packetid != PacketIdOpenDialog) return;

            ICoreClientAPI capi = Api as ICoreClientAPI;
            if (capi == null) return;

            using var ms = new MemoryStream(data);
            using var br = new BinaryReader(ms);
            string title = br.ReadString();
            distance = br.ReadSingle();
            angle = br.ReadSingle();
            float requiredSu = br.ReadSingle();
            lastStatus = br.ReadString();

            if (clientDialog == null)
            {
                clientDialog = new GuiDialogTrebuchet(
                    title,
                    Pos,
                    distance,
                    angle,
                    requiredSu,
                    lastStatus,
                    SendSettingsPacket,
                    SendLaunchPacket,
                    capi);
                clientDialog.OnClosed += OnDialogClosed;
                clientDialog.TryOpen();
            }
            else
            {
                clientDialog.UpdateState(distance, angle, requiredSu, lastStatus);
            }
        }

        private void SendSettingsPacket(float newDistance, float newAngle)
        {
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdSetSettings, WriteSettingsPacket(newDistance, newAngle));
        }

        private void SendLaunchPacket(float newDistance, float newAngle)
        {
            ((ICoreClientAPI)Api).Network.SendBlockEntityPacket(Pos, PacketIdLaunch, WriteSettingsPacket(newDistance, newAngle));
        }

        private void OnDialogClosed()
        {
            clientDialog = null;
        }

        private static byte[] WriteSettingsPacket(float newDistance, float newAngle)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(newDistance);
            bw.Write(newAngle);
            return ms.ToArray();
        }

        private static void ReadSettingsPacket(byte[] data, out float newDistance, out float newAngle)
        {
            using var ms = new MemoryStream(data ?? Array.Empty<byte>());
            using var br = new BinaryReader(ms);
            newDistance = br.ReadSingle();
            newAngle = br.ReadSingle();
        }

        private static byte[] WriteLaunchPacket(Vec3d launchPos, Vec3d motion)
        {
            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            bw.Write(launchPos.X);
            bw.Write(launchPos.Y);
            bw.Write(launchPos.Z);
            bw.Write(motion.X);
            bw.Write(motion.Y);
            bw.Write(motion.Z);
            return ms.ToArray();
        }

        private static void ReadLaunchPacket(byte[] data, out Vec3d launchPos, out Vec3d motion)
        {
            using var ms = new MemoryStream(data ?? Array.Empty<byte>());
            using var br = new BinaryReader(ms);
            launchPos = new Vec3d(br.ReadDouble(), br.ReadDouble(), br.ReadDouble());
            motion = new Vec3d(br.ReadDouble(), br.ReadDouble(), br.ReadDouble());
        }

        private void SetSettings(float newDistance, float newAngle, bool rebuildNetwork)
        {
            distance = GameMath.Clamp(newDistance, MinDistance, MaxDistance);
            angle = GameMath.Clamp(newAngle, MinAngle, MaxAngle);
            ApplyStress(rebuildNetwork);
            MarkDirty(true);
        }

        private void ApplyStress(bool rebuildNetwork)
        {
            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            if (kinetic == null) return;

            float desired = RequiredSu / RatedRpm;
            if (MathF.Abs(kinetic.StressImpact - desired) < 0.001f) return;

            kinetic.StressImpact = desired;
            if (Api?.Side == EnumAppSide.Server && rebuildNetwork)
            {
                KineticNetworkManager mgr = Api.ModLoader.GetModSystem<KineticNetworkManager>();
                mgr?.OnRemoved(Pos);
                mgr?.OnPlaced(Pos);
            }
        }

        private void TryLaunch(IPlayer byPlayer)
        {
            if (Api.Side != EnumAppSide.Server) return;

            if (mountedBy == null || !mountedBy.Alive)
            {
                lastStatus = Lang.Get("vintagekinematics:trebuchet-status-no-passenger");
                Notify(byPlayer, lastStatus);
                MarkDirty(true);
                return;
            }

            BEBehaviorKinetic kinetic = GetBehavior<BEBehaviorKinetic>();
            IKineticNetworkInfo net = kinetic?.EffectiveNetwork;
            float rpm = MathF.Abs(kinetic?.ActualRPM ?? 0f);
            if (net == null || net.IsConflicted || net.IsOverstressed || rpm < KineticNetwork.MinAbsRPM)
            {
                lastStatus = Lang.Get("vintagekinematics:trebuchet-status-need-power", RequiredSu);
                Notify(byPlayer, lastStatus);
                MarkDirty(true);
                return;
            }

            EntityAgent passenger = mountedBy;
            launching = true;
            passenger.TryUnmount();
            launching = false;

            StartLaunch(passenger, byPlayer as IServerPlayer);
            lastStatus = Lang.Get("vintagekinematics:trebuchet-status-launched", distance, angle);
            Notify(byPlayer, lastStatus);
            MarkDirty(true);
        }

        private void StartLaunch(EntityAgent entity, IServerPlayer serverPlayer)
        {
            BlockFacing facing = LaunchFacing().Opposite;
            ComputeLaunchMotion(out double horizontal, out double vertical);
            double x = facing.Normali.X * horizontal;
            double z = facing.Normali.Z * horizontal;

            Vec3d launchPos = Pos.ToVec3d().Add(0.5, 2.05, 0.5);
            launchPos.X += facing.Normali.X * 2.6;
            launchPos.Z += facing.Normali.Z * 2.6;
            Vec3d motion = new Vec3d(x, vertical, z);

            SuppressLaunchCollision();
            Api.Event.RegisterCallback(_ => PlaceAndLaunch(entity, serverPlayer, launchPos, motion), 50);
        }

        private void SuppressLaunchCollision()
        {
            if (Api?.World == null) return;

            launchCollisionDisabledUntilMs = Api.World.ElapsedMilliseconds + LaunchCollisionGraceMs;
            MarkDirty(true);
            Api.Event.RegisterCallback(_ =>
            {
                if (!LaunchCollisionSuppressed) MarkDirty(true);
            }, LaunchCollisionGraceMs + 50);
        }

        private void ComputeLaunchMotion(out double horizontal, out double vertical)
        {
            double distance01 = GameMath.Clamp((distance - MinDistance) / (MaxDistance - MinDistance), 0f, 1f);
            double angle01 = GameMath.Clamp((angle - MinAngle) / (MaxAngle - MinAngle), 0f, 1f);

            double distanceScale = 0.28 + Math.Sqrt(distance) / 4.9;
            double angleHorizontalScale = 1.05 - angle01 * 0.62;
            horizontal = GameMath.Clamp(distanceScale * angleHorizontalScale * 1.8, 0.55, 11.2);

            double verticalBase = 0.24 + angle01 * (0.62 + distance01 * 0.34);
            vertical = GameMath.Clamp(verticalBase, 0.24, 1.2);
        }

        private void PlaceAndLaunch(EntityAgent entity, IServerPlayer serverPlayer, Vec3d launchPos, Vec3d motion)
        {
            if (entity?.World == null || !entity.Alive) return;

            ApplyLaunch(entity, launchPos, motion);
            if (serverPlayer != null)
            {
                ((ICoreServerAPI)Api).Network.SendBlockEntityPacket(
                    serverPlayer,
                    Pos,
                    PacketIdApplyLaunch,
                    WriteLaunchPacket(launchPos, motion));
            }
        }

        private static void ApplyLaunch(EntityAgent entity, Vec3d launchPos, Vec3d motion)
        {
            entity.Pos.SetPos(launchPos.X, launchPos.Y, launchPos.Z);
#pragma warning disable CS0618
            entity.ServerPos.SetPos(launchPos.X, launchPos.Y, launchPos.Z);
            entity.ServerPos.Motion.Set(motion.X, motion.Y, motion.Z);
#pragma warning restore CS0618
            entity.Pos.Motion.Set(motion.X, motion.Y, motion.Z);
            entity.WatchedAttributes.SetDouble("kbdirX", motion.X);
            entity.WatchedAttributes.SetDouble("kbdirY", motion.Y);
            entity.WatchedAttributes.SetDouble("kbdirZ", motion.Z);
            entity.Attributes.SetInt("dmgkb", 1);
            entity.OnGround = false;
        }

        private void Notify(IPlayer byPlayer, string message)
        {
            if (byPlayer is IServerPlayer serverPlayer)
            {
                serverPlayer.SendMessage(GlobalConstants.InfoLogChatGroup, message, EnumChatType.Notification);
            }
        }

        private bool CheckClaim(IPlayer player)
        {
            if (Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) return true;
            Api.World.Logger.Audit("Player {0} sent trebuchet packet at {1} but has no claim access. Rejected.", player.PlayerName, Pos);
            return false;
        }

        private void OnControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (action == EnumEntityAction.Sneak && on)
            {
                mountedBy?.TryUnmount();
                controls.StopAllMovement();
                handled = EnumHandling.PreventDefault;
                return;
            }

            if (action == EnumEntityAction.Jump && on && mountedBy is EntityPlayer entityPlayer)
            {
                TryLaunch(entityPlayer.Player);
                handled = EnumHandling.PreventDefault;
            }
        }

        public bool TryMount(EntityAgent entityAgent)
        {
            return entityAgent != null && entityAgent.TryMount(this);
        }

        private void RestoreMountedEntity(ICoreAPI api)
        {
            if (mountedBy != null || (mountedByEntityId == 0 && mountedByPlayerUid == null)) return;

            EntityAgent entity = null;
            if (mountedByPlayerUid != null)
            {
                entity = api.World.PlayerByUid(mountedByPlayerUid)?.Entity;
            }
            else
            {
                entity = api.World.GetEntityById(mountedByEntityId) as EntityAgent;
            }

            if (entity?.SidedProperties != null)
            {
                entity.TryMount(this);
            }
        }

        public override void OnBlockRemoved()
        {
            blockBroken = true;
            mountedBy?.TryUnmount();
            base.OnBlockRemoved();
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetFloat(TreeDistanceKey, distance);
            tree.SetFloat(TreeAngleKey, angle);
            tree.SetLong(TreeLaunchCollisionRemainingKey, LaunchCollisionSuppressed ? launchCollisionDisabledUntilMs - Api.World.ElapsedMilliseconds : 0L);
            tree.SetFloat("distance", distance);
            tree.SetFloat("angle", angle);
            tree.SetString("lastStatus", lastStatus);
            tree.SetLong("mountedByEntityId", mountedByEntityId);
            tree.SetString("mountedByPlayerUid", mountedByPlayerUid);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            distance = tree.GetFloat(TreeDistanceKey, tree.GetFloat("distance", distance));
            angle = tree.GetFloat(TreeAngleKey, tree.GetFloat("angle", angle));
            long remainingCollisionMs = tree.GetLong(TreeLaunchCollisionRemainingKey);
            launchCollisionDisabledUntilMs = remainingCollisionMs > 0 && Api?.World != null
                ? Api.World.ElapsedMilliseconds + remainingCollisionMs
                : 0L;
            lastStatus = tree.GetString("lastStatus", lastStatus);
            mountedByEntityId = tree.GetLong("mountedByEntityId");
            mountedByPlayerUid = tree.GetString("mountedByPlayerUid");
            ApplyStress(false);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            sb.AppendLine(Lang.Get("vintagekinematics:trebuchet-tooltip", distance, angle, RequiredSu));
            sb.AppendLine(Lang.Get("vintagekinematics:trebuchet-tooltip-controls"));
        }

        public static IMountableSeat GetMountable(IWorldAccessor world, TreeAttribute tree)
        {
            BlockPos pos = new BlockPos(tree.GetInt("posx"), tree.GetInt("posy"), tree.GetInt("posz"));
            return world.BlockAccessor.GetBlockEntity(pos) as BETrebuchet;
        }

        public void MountableToTreeAttributes(TreeAttribute tree)
        {
            tree.SetString("className", "vktrebuchet");
            tree.SetInt("posx", Pos.X);
            tree.SetInt("posy", Pos.InternalY);
            tree.SetInt("posz", Pos.Z);
        }

        public void DidMount(EntityAgent entityAgent)
        {
            if (mountedBy != null && mountedBy != entityAgent)
            {
                entityAgent.TryUnmount();
                return;
            }

            mountedBy = entityAgent;
            mountedByEntityId = entityAgent.EntityId;
            mountedByPlayerUid = (entityAgent as EntityPlayer)?.PlayerUID;
            MarkDirty(false);
        }

        public void DidUnmount(EntityAgent entityAgent)
        {
            mountedBy = null;
            mountedByEntityId = 0;
            mountedByPlayerUid = null;
            controls.StopAllMovement();

            if (!blockBroken && !launching && Api?.World != null)
            {
                TeleportToExit(entityAgent);
            }

            MarkDirty(false);
        }

        private void TeleportToExit(EntityAgent entityAgent)
        {
            Vec3d exit = Pos.ToVec3d().Add(0.5, 0.01, 0.5);
            BlockFacing back = LaunchFacing().Opposite;
            exit.X += back.Normali.X * 2.8;
            exit.Z += back.Normali.Z * 2.8;

            if (!Api.World.CollisionTester.IsColliding(Api.World.BlockAccessor, entityAgent.SelectionBox, exit, false))
            {
                entityAgent.TeleportTo(exit);
            }
        }

        private BlockFacing LaunchFacing()
        {
            int rotateY = (int)(Block?.Shape?.rotateY ?? 0f);
            int steps = (((rotateY / 90) % 4) + 4) % 4;
            return steps switch
            {
                1 => BlockFacing.WEST,
                2 => BlockFacing.SOUTH,
                3 => BlockFacing.EAST,
                _ => BlockFacing.NORTH
            };
        }

        private static float ComputeRequiredSu(float targetDistance, float targetAngle)
        {
            float clampedDistance = GameMath.Clamp(targetDistance, MinDistance, MaxDistance);
            float clampedAngle = GameMath.Clamp(targetAngle, MinAngle, MaxAngle);
            float anglePenalty = 1f + MathF.Abs(clampedAngle - 45f) / 45f;
            return 2048f + clampedDistance * 96f * anglePenalty;
        }

        public bool CanUnmount(EntityAgent entityAgent) => true;
        public bool CanMount(EntityAgent entityAgent) => mountedBy == null && entityAgent is EntityPlayer;
        public bool AnyMounted() => mountedBy != null;

        public EntityPos Position => SeatPosition;

        public EntityPos SeatPosition
        {
            get
            {
                seatPos.SetPos(Pos);
                seatPos.X += 0.5;
                seatPos.Y += 0.35;
                seatPos.Z += 0.5;

                BlockFacing facing = LaunchFacing();
                seatPos.X += facing.Normali.X * 1.7;
                seatPos.Z += facing.Normali.Z * 1.7;

                if (facing == BlockFacing.NORTH) seatPos.Yaw = GameMath.PI;
                else if (facing == BlockFacing.EAST) seatPos.Yaw = -GameMath.PIHALF;
                else if (facing == BlockFacing.SOUTH) seatPos.Yaw = 0f;
                else seatPos.Yaw = GameMath.PIHALF;

                seatPos.Pitch = 0f;
                seatPos.Roll = 0f;
                return seatPos;
            }
        }

        public double StepPitch => 0;
        public Entity Controller => mountedBy;
        public Entity OnEntity => null;
        public EntityControls ControllingControls => controls;
        public EntityControls Controls => controls;
        public IMountableSeat[] Seats => new IMountableSeat[] { this };
        public SeatConfig Config { get; set; }
        public string SeatId { get; set; } = "trebuchet-0";
        public long PassengerEntityIdForInit { get => mountedByEntityId; set => mountedByEntityId = value; }
        public bool DoTeleportOnUnmount { get; set; } = true;
        public Entity Entity => null;
        public Entity Passenger => mountedBy;
        public IMountable MountSupplier => this;
        public bool CanControl => true;
        public EnumMountAngleMode AngleMode => EnumMountAngleMode.FixateYaw;
        public AnimationMetaData SuggestedAnimation => null;
        public bool SkipIdleAnimation => false;
        public float FpHandPitchFollow => 1f;
        public Vec3f LocalEyePos => eyePos;
        public Matrixf RenderTransform => null;
    }
}
