using System;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VintageKinematics.Entities
{
    public partial class EntityVKContraption
    {
        private const string AttrAssemblyReady = "vkAssemblyReady";
        private const int RestoreVisualHandoffMs = 75;
        private const int PendingAssemblyRecoveryMs = 250;

        private bool restoredDespawnScheduled;

        public bool AssemblyReady => WatchedAttributes.GetBool(AttrAssemblyReady, true);

        public void MarkAssemblyReady()
        {
            WatchedAttributes.SetBool(AttrAssemblyReady, true);
        }

        private void SchedulePendingAssemblyRecovery()
        {
            if (api?.Side != EnumAppSide.Server || AssemblyReady) return;
            api.Event.RegisterCallback(_ => RecoverPendingAssembly(), PendingAssemblyRecoveryMs);
        }

        private void RecoverPendingAssembly()
        {
            if (!Alive || AssemblyReady) return;
            if (RetireIfWorldAlreadyRestored()) return;

            // The source blocks were removed before an interrupted handoff could mark the
            // entity ready. In that state the entity snapshot is the surviving authority.
            MarkAssemblyReady();
        }

        public bool ShouldHideDuringAssemblyOverlap()
        {
            if (AssemblyReady || World == null) return false;
            if (!TryGetControllerWorldPosition(out Vec3d controllerWorldPos)) return false;

            BlockPos pos = new BlockPos(
                (int)Math.Floor(controllerWorldPos.X + 0.5),
                (int)Math.Floor(controllerWorldPos.Y + 0.5) % BlockPos.DimensionBoundary,
                (int)Math.Floor(controllerWorldPos.Z + 0.5),
                Pos.Dimension);
            return World.BlockAccessor.GetBlock(pos)?.Code?.FirstCodePart() == "gantrycarriage";
        }

        private void ScheduleRestoredDespawn()
        {
            if (restoredDespawnScheduled) return;
            if (World?.Side != EnumAppSide.Server)
            {
                base.Die(EnumDespawnReason.Removed);
                return;
            }

            restoredDespawnScheduled = true;
            api.Event.RegisterCallback(_ => DespawnAfterVisualHandoff(), RestoreVisualHandoffMs);
        }

        private void DespawnAfterVisualHandoff()
        {
            if (Alive) base.Die(EnumDespawnReason.Removed);
        }
    }
}
