using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage
{
    /// <summary>
    /// Pure state gate shared by destructive storage paths.
    /// </summary>
    internal static class StorageRemovalPolicy
    {
        public static bool CanEvaluateCapacity(
            StorageState state,
            bool itemIndexReady,
            bool rebuildInProgress,
            StorageRemovalKind kind)
        {
            if (kind == StorageRemovalKind.ContraptionCapture
                || kind == StorageRemovalKind.BlockReplacement)
            {
                return false;
            }
            return state == StorageState.Online && itemIndexReady && !rebuildInProgress;
        }
    }
}
