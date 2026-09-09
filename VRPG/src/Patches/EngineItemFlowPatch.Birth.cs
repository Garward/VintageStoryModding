using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Patches;

public static partial class EngineItemFlowPatch
{
    public static Action<EntityItem, System.Func<bool>>? BeforePublicationObserver { get; set; }
    private static BirthInstallation? birthInstallation;
    public static bool IsFreshIdentityInstalled => birthInstallation?.IsReady() == true;

    private static void PatchBirth(Harmony harmony, ILogger logger,
        System.Func<Type, string, Type[], MethodInfo?> resolve)
    {
        ResetBirthInstallation();
        try
        {
            Type server = AccessTools.TypeByName("Vintagestory.Server.ServerMain")
                ?? throw new InvalidOperationException("ServerMain is unavailable.");
            // These exact native bodies were audited in the installed 1.22.7 build. A new engine
            // build needs a new audit: method names alone do not prove publication/serialization order.
            var targets = new List<MethodInfo>();
            void Require(Type type, string name, Type[] parameters, string hash, string module)
            {
                MethodInfo? method = resolve(type, name, parameters);
                if (method?.DeclaringType != type || method.Module.ModuleVersionId.ToString() != module
                    || Convert.ToHexString(SHA256.HashData(method.GetMethodBody()?.GetILAsByteArray() ?? Array.Empty<byte>())) != hash
                    || Harmony.GetPatchInfo(method)?.Owners.Count > 0)
                    throw new InvalidOperationException("Unsupported native birth boundary: " + type.Name + "." + name
                        + "; expected audited 1.22.7 module " + module + ", observed " + method?.Module.ModuleVersionId);
                targets.Add(method);
            }
            const string apiModule = "83c0addd-72c9-42ba-a23e-5e05fe098ba0";
            Require(server, "SpawnEntity_internal", new[] { typeof(EntityProperties), typeof(Entity) },
                "E3D57BECAEA27743B23EE007E9871999B5A5DC75C15C7F0D487C6A73FF06BACB", "8a3ad3aa-2940-4c39-ae1b-881de9e65346");
            Require(typeof(EntityItem), nameof(EntityItem.Initialize), new[] { typeof(EntityProperties), typeof(ICoreAPI), typeof(long) },
                "8FC5251ABE9D7E18F2064179F2451406495148FAAF96517029F7043FE7880E36", apiModule);
            Require(typeof(Entity), nameof(Entity.AfterInitialized), new[] { typeof(bool) },
                "548D8A4A180BF5BB5B2978BA4B5A1D31AAF5A6D718C2FB12F46629D87F45A09C", apiModule);
            Require(typeof(Entity), nameof(Entity.ToBytes), new[] { typeof(BinaryWriter), typeof(bool) },
                "E7C4CCDAACEB299CAA63106A6CB65753CDEF1B8330C1EC7BC9727A2806705C2C", apiModule);
            Require(typeof(EntityItem), nameof(EntityItem.FromBytes), new[] { typeof(BinaryReader), typeof(bool) },
                "2FFF18C0B8DF6E0D31DA9A430334A4345D8274E5B9455042A57F44F5BB314C78", apiModule);
            if (typeof(EntityItem).GetMethod(nameof(Entity.AfterInitialized))?.DeclaringType != typeof(Entity)
                || typeof(EntityItem).GetMethod(nameof(Entity.ToBytes))?.DeclaringType != typeof(Entity))
                throw new InvalidOperationException("EntityItem publication/serialization override changed.");
            var installation = new BirthInstallation(harmony, targets.ToArray(), logger);
            birthInstallation = installation;
            harmony.Patch(targets[0], prefix: new HarmonyMethod(typeof(EngineItemFlowPatch), nameof(BeforeSpawnPrefix))
            { priority = Priority.First });
            if (!installation.HasOnlyExpectedPatches()) throw new InvalidOperationException("Birth prefix installation was not confirmed.");
            logger.Notification("[VRPG/Inventory] Validated prepublication item identity hook installed (native 1.22.7); fresh pickup requires a live persistence observer.");
        }
        catch (Exception error)
        {
            ResetBirthInstallation();
            logger.Error("[VRPG/Inventory] Fresh pickup is unavailable; native birth validation failed: {0}", error.Message);
        }
    }

    public static void BeforeSpawnPrefix(Entity entity)
    {
        var installation = birthInstallation;
        var observer = BeforePublicationObserver;
        if (entity.GetType() != typeof(EntityItem) || installation?.IsReady() != true || observer == null) return;
        // The evidence carries this installation's lease. Reset, unpatch, or a conflicting patch
        // cannot leave an old in-memory source eligible under a new hook installation.
        try
        {
            observer((EntityItem)entity, () => ReferenceEquals(installation, birthInstallation)
                && ReferenceEquals(observer, BeforePublicationObserver) && installation.IsReady());
        }
        catch (Exception error)
        {
            installation.Failed = true;
            installation.Logger.Error("[VRPG/Inventory] Fresh source identity observer failed; fresh pickup is unavailable: {0}", error.Message);
        }
    }

    private static void ResetBirth()
    {
        BeforePublicationObserver = null;
        ResetBirthInstallation();
    }

    private static void ResetBirthInstallation()
    {
        var prior = birthInstallation;
        birthInstallation = null;
        prior?.Harmony.Unpatch(prior.Targets[0], AccessTools.Method(typeof(EngineItemFlowPatch), nameof(BeforeSpawnPrefix)));
    }

    private sealed record BirthInstallation(Harmony Harmony, MethodInfo[] Targets, ILogger Logger)
    {
        public bool Failed { get; set; }
        public bool IsReady() => !Failed && BeforePublicationObserver != null && HasOnlyExpectedPatches();
        public bool HasOnlyExpectedPatches()
        {
            var info = HarmonyLib.Harmony.GetPatchInfo(Targets[0]);
            return info != null && info.Prefixes.Count == 1 && info.Prefixes[0].owner == Harmony.Id
                && info.Prefixes[0].PatchMethod == AccessTools.Method(typeof(EngineItemFlowPatch), nameof(BeforeSpawnPrefix))
                && info.Postfixes.Count == 0 && info.Transpilers.Count == 0 && info.Finalizers.Count == 0
                && Targets.Skip(1).All(method => HarmonyLib.Harmony.GetPatchInfo(method)?.Owners.Count is null or 0);
        }
    }
}
