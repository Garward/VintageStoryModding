using System;
using System.Diagnostics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResponsiveVS.Config;
using ResponsiveVS.Diagnostics;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace ResponsiveVS.RuntimeData;

public static class JsonObjectHotPathPatch
{
    public static bool AsObjectPrefix<T>(JsonObject __instance, T defaultValue, ref T __result, ref long __state)
    {
        return Prefix(__instance, typeof(T), GlobalConstants.DefaultDomain, settingsProvided: false, ref __result, ref __state);
    }

    public static void AsObjectPostfix<T>(JsonObject __instance, T defaultValue, ref T __result, long __state)
    {
        Postfix(__instance, typeof(T), GlobalConstants.DefaultDomain, __result, __state);
    }

    public static bool AsObjectDomainPrefix<T>(JsonObject __instance, T defaultValue, string domain, ref T __result, ref long __state)
    {
        return Prefix(__instance, typeof(T), domain, settingsProvided: false, ref __result, ref __state);
    }

    public static void AsObjectDomainPostfix<T>(JsonObject __instance, T defaultValue, string domain, ref T __result, long __state)
    {
        Postfix(__instance, typeof(T), domain, __result, __state);
    }

    public static bool AsObjectSettingsPrefix<T>(JsonObject __instance, JsonSerializerSettings settings, T defaultValue, string domain, ref T __result, ref long __state)
    {
        return Prefix(__instance, typeof(T), domain, settings != null, ref __result, ref __state);
    }

    public static void AsObjectSettingsPostfix<T>(JsonObject __instance, JsonSerializerSettings settings, T defaultValue, string domain, ref T __result, long __state)
    {
        if (settings != null)
        {
            return;
        }

        Postfix(__instance, typeof(T), domain, __result, __state);
    }

    private static bool Prefix<T>(JsonObject jsonObject, Type resultType, string domain, bool settingsProvided, ref T result, ref long state)
    {
        state = 0;

        if (!RuntimeDataCache.Enabled)
        {
            return true;
        }

        JToken token = SafeToken(jsonObject);
        if (token == null)
        {
            return true;
        }

        if (settingsProvided)
        {
            RuntimeDataStats.RecordAsObjectSkipped(resultType);
            return true;
        }

        if (RuntimeDataCache.TryGetAsObject(token, domain, out T cached))
        {
            result = cached;
            RuntimeDataStats.RecordAsObjectHit(resultType);
            return false;
        }

        RuntimeDataStats.RecordAsObjectMiss(resultType);
        state = Stopwatch.GetTimestamp();
        return true;
    }

    private static void Postfix<T>(JsonObject jsonObject, Type resultType, string domain, T result, long state)
    {
        if (state <= 0 || !RuntimeDataCache.AsObjectCacheEnabled)
        {
            return;
        }

        JToken token = SafeToken(jsonObject);
        if (token == null)
        {
            return;
        }

        long elapsedTicks = Stopwatch.GetTimestamp() - state;
        RuntimeDataCache.StoreAsObject(token, domain, result);
        RuntimeDataStats.RecordAsObjectStore(resultType, elapsedTicks);

        RuntimeDataConfig config = ResponsiveVSConfigSystem.Config.RuntimeData;
        if (config.TraceCallerStacks && ResponsiveDiagnostics.TraceEnabled)
        {
            ResponsiveDiagnostics.Trace(
                "RuntimeData AsObject miss stored type={0} domain={1} elapsedMs={2:0.###} caller={3}",
                resultType?.FullName ?? "<unknown>",
                domain ?? string.Empty,
                elapsedTicks * 1000.0 / Stopwatch.Frequency,
                new StackTrace(2, false).ToString().Replace(Environment.NewLine, " | "));
        }
    }

    private static JToken SafeToken(JsonObject jsonObject)
    {
        try
        {
            return jsonObject?.Token;
        }
        catch
        {
            return null;
        }
    }
}
