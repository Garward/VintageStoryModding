using Vintagestory.API.Client;

namespace VintageKinematics.Api
{
    /// <summary>
    /// Safe disposal helpers for client-side block entity dialogs.
    /// </summary>
    public static class GuiDialogUtil
    {
        /// <summary>
        /// Disposes a block entity dialog without re-entrancy NPEs. <c>TryClose</c> synchronously
        /// fires the <c>OnClosed</c> event, and almost every dialog hookup nulls the holding field
        /// from there. The naive close-then-dispose sequence then dereferences a now-null field.
        /// This helper snapshots the reference and nulls the field before any callback can fire,
        /// so it is safe to call from <c>OnBlockUnloaded</c> / <c>OnBlockRemoved</c> regardless
        /// of how the consumer wired up <c>OnClosed</c>.
        /// </summary>
        public static void SafeDispose<T>(ref T dialog) where T : GuiDialogBlockEntity
        {
            if (dialog == null) return;
            T snapshot = dialog;
            dialog = null;
            if (snapshot.IsOpened()) snapshot.TryClose();
            snapshot.Dispose();
        }
    }
}
