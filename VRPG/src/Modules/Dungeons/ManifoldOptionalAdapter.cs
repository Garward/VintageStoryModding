using System;
using System.Linq;
using System.Reflection;
using VRPG.Config;
using VRPG.Data;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace VRPG.Modules.Dungeons;

public sealed class ManifoldOptionalAdapter
{
    private readonly ICoreServerAPI api;
    private readonly ModSystem owner;
    private readonly DungeonModuleConfig config;
    private readonly VRPGDataRegistry data;

    public ManifoldOptionalAdapter(ICoreServerAPI api, ModSystem owner, DungeonModuleConfig config, VRPGDataRegistry data)
    {
        this.api = api;
        this.owner = owner;
        this.config = config;
        this.data = data;
    }

    public bool TryRegisterDungeonDimension(out string reason)
    {
        reason = "";

        if (api.ModLoader.IsModEnabled("manifold") != true)
        {
            reason = "Manifold is not installed.";
            return false;
        }

        try
        {
            Type extensionsType = FindType("Manifold.Api.Server.CoreServerApiExtensions");
            Type worldgenInterface = FindType("Manifold.Api.Worldgen.IWorldgenStrategy");
            MethodInfo getServer = extensionsType
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(method => method.Name == "GetManifoldServer" && method.GetParameters().Length == 2);

            object manifold = getServer.Invoke(null, new object[] { api, owner })!;
            bool isHealthy = (bool)manifold.GetType().GetProperty("IsHealthy")!.GetValue(manifold)!;
            if (!isHealthy)
            {
                reason = "Manifold is installed but unhealthy.";
                return false;
            }

            object registry = manifold.GetType().GetProperty("Registry")!.GetValue(manifold)!;
            object builder = registry.GetType().GetMethod("Define")!.Invoke(registry, new object[] { DimensionAssetLocation() })!;

            builder = InvokeBuilder(builder, "Persistent");
            builder = InvokeBuilder(builder, "WithWorldgen", ManifoldWorldgenProxyFactory.Create(
                worldgenInterface,
                new DungeonWorldgenRuntime(config, data)));
            builder = InvokeBuilder(builder, "WithFixedSpawn", new BlockPos(config.SpawnX, config.SpawnY, config.SpawnZ, 0));

            if (config.Streaming)
            {
                builder = InvokeBuilder(builder, "Streaming", Math.Clamp(config.StreamingRadiusChunks, 1, 32));
                builder = InvokeBuilder(builder, "WithStreamingBudget", Math.Clamp(config.StreamingBudgetColumnsPerTick, 1, 64));
            }
            else
            {
                builder = InvokeBuilder(builder, "WithGenerationRadius", Math.Clamp(config.GenerationRadiusChunks, 0, 16));
            }

            builder = InvokeBuilder(builder, "WithMetadata", "vrpg_module", "dungeons");
            builder = InvokeBuilder(builder, "WithMetadata", "display_name", config.DisplayName);
            builder = InvokeBuilder(builder, "WithMetadata", "entry_item_name", config.EntryItemName);
            builder.GetType().GetMethod("RegisterStatic")!.Invoke(builder, Array.Empty<object>());

            reason = "Registered " + config.DimensionCode;
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.InnerException?.Message ?? ex.Message;
            return false;
        }
    }

    private AssetLocation DimensionAssetLocation()
    {
        return new AssetLocation(config.DimensionCode);
    }

    private static object InvokeBuilder(object builder, string methodName, params object[] args)
    {
        MethodInfo method = builder.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .First(method => method.Name == methodName && method.GetParameters().Length == args.Length);
        return method.Invoke(builder, args)!;
    }

    private static Type FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, false);
            if (type != null)
            {
                return type;
            }
        }

        throw new TypeLoadException(fullName);
    }
}
