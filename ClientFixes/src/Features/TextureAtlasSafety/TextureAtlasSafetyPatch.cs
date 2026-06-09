using System;
using System.Reflection;
using HarmonyLib;
using ClientFixes.Config;

namespace ClientFixes.Features.TextureAtlasSafety
{
    [HarmonyPatch]
    internal static class TextureAtlasRegenMipMapsPatch
    {
        private static int suppressedCount;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("Vintagestory.Client.NoObf.TextureAtlasManager:RegenMipMaps", new[] { typeof(int) });
        }

        private static Exception Finalizer(Exception __exception, int atlasNumber)
        {
            if (__exception == null)
            {
                return null;
            }

            TextureAtlasSafetyConfig config = ClientFixesModSystem.Config?.TextureAtlasSafety;
            if (config == null
                || !config.Enabled
                || !config.SuppressRegenMipMapsIndexCrash
                || __exception is not ArgumentOutOfRangeException)
            {
                return __exception;
            }

            suppressedCount++;
            if (config.LogSuppressedCrashes && (suppressedCount == 1 || suppressedCount % 10 == 0))
            {
                ClientFixesModSystem.Api?.Logger.Warning(
                    "[ClientFixes/TextureAtlasSafety] Suppressed RegenMipMaps index crash atlas={0} count={1}: {2}",
                    atlasNumber,
                    suppressedCount,
                    __exception.Message);
            }

            return null;
        }
    }

    [HarmonyPatch]
    internal static class TextureAtlasMainThreadTaskPatch
    {
        private static int suppressedCount;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method("Vintagestory.Client.NoObf.ClientMain:ExecuteMainThreadTasks", new[] { typeof(float) });
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception == null)
            {
                return null;
            }

            TextureAtlasSafetyConfig config = ClientFixesModSystem.Config?.TextureAtlasSafety;
            if (config == null
                || !config.Enabled
                || !config.SuppressRegenMipMapsIndexCrash
                || __exception is not ArgumentOutOfRangeException
                || !IsRuntimeTextureMipMapCrash(__exception))
            {
                return __exception;
            }

            suppressedCount++;
            if (config.LogSuppressedCrashes && (suppressedCount == 1 || suppressedCount % 10 == 0))
            {
                ClientFixesModSystem.Api?.Logger.Warning(
                    "[ClientFixes/TextureAtlasSafety] Suppressed runtime texture mipmap task crash count={0}: {1}",
                    suppressedCount,
                    __exception.Message);
            }

            return null;
        }

        private static bool IsRuntimeTextureMipMapCrash(Exception exception)
        {
            string stackTrace = exception.StackTrace;
            return stackTrace != null
                && stackTrace.IndexOf("TextureAtlasManager.RegenMipMaps", StringComparison.Ordinal) >= 0
                && stackTrace.IndexOf("RuntimeUploadTextureToPos", StringComparison.Ordinal) >= 0;
        }
    }
}
