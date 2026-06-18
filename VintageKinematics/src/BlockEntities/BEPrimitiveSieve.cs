using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VintageKinematics.Api;
using VintageKinematics.Crafting;
using VintageKinematics.Gui;

namespace VintageKinematics.BlockEntities
{
    /// <summary>
    /// Single-cell hand-crank-friendly sieve. The shared sieve base owns inventory, packets,
    /// output pushing, vanilla pannable rolls, effects, and yield scaling.
    /// </summary>
    public class BEPrimitiveSieve : BEKineticSieveProcessorBase
    {
        public const int SlotOutputLast = 4;
        public const int InventorySize = 5;
        public const int PacketIdOpenDialog = 5610;

        private const float ProgressSyncIntervalMs = 500f;
        private const float DustParticleIntervalMs = 350f;
        private const float PanTopY = 0.6875f;

        private float prevSyncedProgress;

        public BEPrimitiveSieve() : base("primitivesieve", InventorySize, SlotOutputLast) { }

        protected override int OpenDialogPacketId => PacketIdOpenDialog;
        protected override string TitleLangCode => "vintagekinematics:primitivesieve-title";
        protected override string FallbackTitle => "Primitive Sieve";
        protected override bool AllowCustomSieveRecipes => false;
        protected override float EffectVolume => 0.4f;
        protected override int EffectParticleCount => 8;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                RegisterGameTickListener(OnProgressSyncTick, (int)ProgressSyncIntervalMs);
                RegisterGameTickListener(OnDustParticleTick, (int)DustParticleIntervalMs);
            }
        }

        protected override IOFaceMap BuildIOFaceMap()
        {
            BlockFacing inputFace = AutomationInputFace();
            return MachineIoLayouts.SideInputOppositeAndDownOutput(Pos, inputFace, SlotInput, SlotOutputFirst, SlotOutputLast);
        }

        protected override GuiDialogBlockEntity CreateClientDialog(string title, ICoreClientAPI capi)
        {
            var dialog = new GuiDialogPrimitiveSieve(title, MachineInventory, Pos, capi);
            if (Worker != null) dialog.Update(Worker.CurrentProgress, Worker.WorkPerCycle);
            return dialog;
        }

        protected override float PanningYieldMultiplier(VintageKinematicsConfig cfg)
        {
            return cfg?.ResolvePrimitiveSievePanningYield() ?? 1f;
        }

        protected override Vec3d ParticlePosition()
        {
            return new Vec3d(Pos.X + 0.5, Pos.Y + PanTopY, Pos.Z + 0.5);
        }

        private void OnProgressSyncTick(float dt)
        {
            if (Worker == null) return;
            ItemSlot input = MachineInventory[SlotInput];
            float cur = Worker.CurrentProgress;
            bool active = !input.Empty && cur != prevSyncedProgress;
            prevSyncedProgress = cur;
            if (active) MarkDirty();
        }

        private void OnDustParticleTick(float dt)
        {
            if (Kinetic == null || System.Math.Abs(Kinetic.CurrentRPM) < 0.01f) return;
            if (Kinetic.IsConflicted || (Kinetic.Network?.IsOverstressed ?? false)) return;

            ItemSlot input = MachineInventory[SlotInput];
            if (input.Empty || input.Itemstack.Block == null) return;
            if (!PanLootRoller.IsSieveablePanningSource(input.Itemstack.Block)) return;

            Vec3d at = ParticlePosition();
            Api.World.SpawnCubeParticles(at, new ItemStack(input.Itemstack.Block), 0.25f, 3, 0.15f);
        }

        private BlockFacing AutomationInputFace()
        {
            string side = Block?.Variant?["side"] ?? "n";
            switch (side)
            {
                case "n":
                case "s": return BlockFacing.EAST;
                case "e":
                case "w": return BlockFacing.SOUTH;
                default:  return BlockFacing.EAST;
            }
        }
    }
}
