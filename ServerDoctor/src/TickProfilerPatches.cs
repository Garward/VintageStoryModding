using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace ServerDoctor;

[HarmonyPatch(typeof(ServerMain), nameof(ServerMain.Process))]
internal static class Patch_ServerMain_Process
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            ServerDoctorModSystem.TickProfiler?.ApplyFrameProfilerStateAtFrameStart();
        }
        catch { }
    }
}

[HarmonyPatch(typeof(GameTickListener), nameof(GameTickListener.OnTriggered))]
internal static class Patch_GameTickListener_OnTriggered
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(GameTickListener __instance, long __state)
    {
        if (__state == 0) return;

        try
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            ServerDoctorModSystem.TickProfiler?.RecordCallback("game-listener", Describe(__instance), elapsed);
        }
        catch { }
    }

    private static string Describe(GameTickListener listener)
    {
        if (listener == null) return "?";
        return DescribeDelegate(listener.Handler) + " interval=" + listener.Millisecondinterval + "ms";
    }

    internal static string DescribeDelegate(Delegate handler)
    {
        if (handler == null) return "?";

        string type = handler.Method.DeclaringType?.FullName
            ?? handler.Target?.GetType().FullName
            ?? "?";

        return type + "." + handler.Method.Name;
    }
}

[HarmonyPatch(typeof(GameTickListenerBlock), nameof(GameTickListenerBlock.OnTriggered))]
internal static class Patch_GameTickListenerBlock_OnTriggered
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(GameTickListenerBlock __instance, long __state)
    {
        if (__state == 0) return;

        try
        {
            long elapsed = Stopwatch.GetTimestamp() - __state;
            ServerDoctorModSystem.TickProfiler?.RecordCallback("block-listener", Describe(__instance), elapsed);
            BlockPos pos = __instance?.Pos;
            if (pos != null)
            {
                ServerDoctorModSystem.TickProfiler?.RecordBlockCallback(pos.X, pos.Y, pos.Z, DescribeHandler(__instance), elapsed);
            }
        }
        catch { }
    }

    private static string Describe(GameTickListenerBlock listener)
    {
        if (listener == null) return "?";

        Delegate handler = listener.HandlerBare != null
            ? (Delegate)listener.HandlerBare
            : listener.Handler;

        string pos = listener.Pos == null
            ? ""
            : " @ " + listener.Pos.X + "," + listener.Pos.Y + "," + listener.Pos.Z;

        return Patch_GameTickListener_OnTriggered.DescribeDelegate(handler)
            + pos
            + " interval=" + listener.Millisecondinterval + "ms";
    }

    private static string DescribeHandler(GameTickListenerBlock listener)
    {
        Delegate handler = listener.HandlerBare != null
            ? (Delegate)listener.HandlerBare
            : listener.Handler;

        return Patch_GameTickListener_OnTriggered.DescribeDelegate(handler);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.SetChunks))]
internal static class Patch_GameDatabase_SetChunks
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(IEnumerable<DbChunk> chunks, long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("SetChunks", chunks, __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.SetMapChunks))]
internal static class Patch_GameDatabase_SetMapChunks
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(IEnumerable<DbChunk> mapchunks, long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("SetMapChunks", mapchunks, __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.SetMapRegions))]
internal static class Patch_GameDatabase_SetMapRegions
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(IEnumerable<DbChunk> mapregions, long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("SetMapRegions", mapregions, __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.GetChunk), new[] { typeof(int), typeof(int), typeof(int) })]
internal static class Patch_GameDatabase_GetChunk3
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("GetChunk", __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.GetChunk), new[] { typeof(int), typeof(int), typeof(int), typeof(int) })]
internal static class Patch_GameDatabase_GetChunk4
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("GetChunk dimension", __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.GetMapChunk))]
internal static class Patch_GameDatabase_GetMapChunk
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("GetMapChunk", __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.GetMapRegion))]
internal static class Patch_GameDatabase_GetMapRegion
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("GetMapRegion", __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.StoreSaveGame), new[] { typeof(SaveGame) })]
internal static class Patch_GameDatabase_StoreSaveGame
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("StoreSaveGame", __state);
    }
}

[HarmonyPatch(typeof(GameDatabase), nameof(GameDatabase.StoreSaveGame), new[] { typeof(SaveGame), typeof(FastMemoryStream) })]
internal static class Patch_GameDatabase_StoreSaveGameReusableStream
{
    [HarmonyPrefix]
    public static void Prefix(out long __state)
    {
        __state = ServerDoctorModSystem.TickProfilerEnabled ? Stopwatch.GetTimestamp() : 0;
    }

    [HarmonyPostfix]
    public static void Postfix(long __state)
    {
        ServerDoctorDatabasePatchUtil.Record("StoreSaveGame reusable-stream", __state);
    }
}

internal static class ServerDoctorDatabasePatchUtil
{
    public static void Record(string operation, long started)
    {
        if (started == 0) return;

        try
        {
            long elapsed = Stopwatch.GetTimestamp() - started;
            ServerDoctorModSystem.TickProfiler?.RecordDatabaseOperation(operation + ThreadLabel(), elapsed);
        }
        catch { }
    }

    public static void Record(string operation, IEnumerable<DbChunk> chunks, long started)
    {
        if (started == 0) return;

        try
        {
            long elapsed = Stopwatch.GetTimestamp() - started;
            ServerDoctorModSystem.TickProfiler?.RecordDatabaseOperation(operation + CountLabel(chunks) + ThreadLabel(), elapsed);
        }
        catch { }
    }

    private static string CountLabel(IEnumerable<DbChunk> chunks)
    {
        int count = TryGetCount(chunks);
        return count >= 0 ? " count=" + count : "";
    }

    private static int TryGetCount(IEnumerable<DbChunk> chunks)
    {
        if (chunks is ICollection<DbChunk> collection) return collection.Count;
        if (chunks is IReadOnlyCollection<DbChunk> readOnlyCollection) return readOnlyCollection.Count;
        return -1;
    }

    private static string ThreadLabel()
    {
        string name = Thread.CurrentThread.Name;
        return string.IsNullOrEmpty(name) ? "" : " thread=" + name;
    }
}
