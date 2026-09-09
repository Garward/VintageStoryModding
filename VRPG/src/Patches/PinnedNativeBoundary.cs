using System;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;

namespace VRPG.Patches;

/// <summary>Exact installed-engine identity and unmodified-body validation shared by native guards.</summary>
internal static class PinnedNativeBoundary
{
    internal const string LibModule = "8a3ad3aa-2940-4c39-ae1b-881de9e65346";
    internal const string ApiModule = "83c0addd-72c9-42ba-a23e-5e05fe098ba0";
    internal const string EssentialsModule = "0d8db7e9-fd74-4129-bbe8-1ff82baf23fc";

    internal static void Validate(MethodBase? method, Type owner, string name, Type[] parameters, string hash,
        string module = LibModule)
    {
        if (method?.DeclaringType != owner || method.Name != name || method.IsStatic
            || !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)
            || method.Module.ModuleVersionId.ToString() != module
            || Convert.ToHexString(SHA256.HashData(method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>())) != hash
            || Harmony.GetPatchInfo(method)?.Owners.Count > 0)
            throw new InvalidOperationException("Unsupported native boundary or routing witness: " + owner.FullName + "." + name);
    }
}
