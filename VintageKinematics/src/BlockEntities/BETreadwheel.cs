using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using VintageKinematics.Api;

namespace VintageKinematics.BlockEntities
{
    public class BETreadwheel : BEKineticAnimated, IMountableSeat, IMountable
    {
        private const int TickMs = 50;
        private const float WindSeconds = 0.2f;

        private readonly EntityControls controls = new EntityControls();
        private readonly EntityPos seatPos = new EntityPos();
        private readonly Vec3f eyePos = new Vec3f(0f, 1.45f, 0f);
        private readonly AnimationMetaData walkAnimation = new AnimationMetaData
        {
            Code = "treadwheel-walk",
            Animation = "walk",
            AnimationSpeed = 1.4f
        }.Init();

        private EntityAgent mountedBy;
        private long mountedByEntityId;
        private string mountedByPlayerUid;
        private bool blockBroken;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);
            controls.OnAction = OnControls;
            RestoreMountedEntity(api);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnServerTick, TickMs);
            }
        }

        private void OnServerTick(float dt)
        {
            BEBehaviorKineticSource src = GetBehavior<BEBehaviorKineticSource>();
            if (src == null || mountedBy == null || !mountedBy.Alive) return;
            if (!controls.TriesToMove || controls.Jump || controls.IsFlying) return;

            int direction = controls.Backward && !controls.Forward ? -1 : 1;
            src.Wind(WindSeconds, direction);
        }

        public override void GetBlockInfo(IPlayer forPlayer, StringBuilder sb)
        {
            base.GetBlockInfo(forPlayer, sb);
            sb.AppendLine(Lang.Get("vintagekinematics:treadwheel-idle-info"));
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

        private void OnControls(EnumEntityAction action, bool on, ref EnumHandling handled)
        {
            if (action == EnumEntityAction.Sneak && on)
            {
                mountedBy?.TryUnmount();
                controls.StopAllMovement();
            }
        }

        public override void OnBlockRemoved()
        {
            blockBroken = true;
            mountedBy?.TryUnmount();
            base.OnBlockRemoved();
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolving)
        {
            base.FromTreeAttributes(tree, worldForResolving);
            mountedByEntityId = tree.GetLong("mountedByEntityId");
            mountedByPlayerUid = tree.GetString("mountedByPlayerUid");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetLong("mountedByEntityId", mountedByEntityId);
            tree.SetString("mountedByPlayerUid", mountedByPlayerUid);
        }

        public static IMountableSeat GetMountable(IWorldAccessor world, TreeAttribute tree)
        {
            BlockPos pos = new BlockPos(tree.GetInt("posx"), tree.GetInt("posy"), tree.GetInt("posz"));
            return world.BlockAccessor.GetBlockEntity(pos) as BETreadwheel;
        }

        public void MountableToTreeAttributes(TreeAttribute tree)
        {
            tree.SetString("className", "vktreadwheel");
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

            if (!blockBroken && Api?.World != null)
            {
                TeleportToExit(entityAgent);
            }

            MarkDirty(false);
        }

        private void TeleportToExit(EntityAgent entityAgent)
        {
            Vec3d exit;
            switch (Side())
            {
                case "n":
                    exit = new Vec3d(Pos.X + 0.5, Pos.Y + 0.01, Pos.Z + 2.35);
                    break;
                case "e":
                    exit = new Vec3d(Pos.X - 0.35, Pos.Y + 0.01, Pos.Z + 0.5);
                    break;
                case "s":
                    exit = new Vec3d(Pos.X + 0.5, Pos.Y + 0.01, Pos.Z - 0.35);
                    break;
                default:
                    exit = new Vec3d(Pos.X + 2.35, Pos.Y + 0.01, Pos.Z + 0.5);
                    break;
            }

            if (!Api.World.CollisionTester.IsColliding(Api.World.BlockAccessor, entityAgent.SelectionBox, exit, false))
            {
                entityAgent.TeleportTo(exit);
            }
        }

        private string Side()
        {
            return Block?.Variant?["side"] ?? "w";
        }

        public bool CanUnmount(EntityAgent entityAgent)
        {
            return true;
        }

        public bool CanMount(EntityAgent entityAgent)
        {
            return mountedBy == null && entityAgent is EntityPlayer;
        }

        public bool AnyMounted()
        {
            return mountedBy != null;
        }

        public EntityPos Position => SeatPosition;

        public EntityPos SeatPosition
        {
            get
            {
                seatPos.SetPos(Pos);
                seatPos.Y += 0.05;
                switch (Side())
                {
                    case "n":
                        seatPos.X += 0.5;
                        seatPos.Z += 1.0;
                        seatPos.Yaw = GameMath.PIHALF;
                        break;
                    case "e":
                        seatPos.X += 0.0;
                        seatPos.Z += 0.5;
                        seatPos.Yaw = GameMath.PI;
                        break;
                    case "s":
                        seatPos.X += 0.5;
                        seatPos.Z += 0.0;
                        seatPos.Yaw = -GameMath.PIHALF;
                        break;
                    default:
                        seatPos.X += 1.0;
                        seatPos.Z += 0.5;
                        seatPos.Yaw = 0f;
                        break;
                }
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
        public string SeatId { get; set; } = "treadwheel-0";
        public long PassengerEntityIdForInit { get => mountedByEntityId; set => mountedByEntityId = value; }
        public bool DoTeleportOnUnmount { get; set; } = true;
        public Entity Entity => null;
        public Entity Passenger => mountedBy;
        public IMountable MountSupplier => this;
        public bool CanControl => true;
        public EnumMountAngleMode AngleMode => EnumMountAngleMode.FixateYaw;
        public AnimationMetaData SuggestedAnimation => controls.TriesToMove ? walkAnimation : null;
        public bool SkipIdleAnimation => false;
        public float FpHandPitchFollow => 1f;
        public Vec3f LocalEyePos => eyePos;
        public Matrixf RenderTransform => null;
    }
}
