using System.Reflection;
using HarmonyLib;
using FastCraftingGrid.Internal;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace FastCraftingGrid;

public class FastCraftingGridModSystem : ModSystem
{
    public const string HarmonyId = "garward.fastcraftinggrid";

    private Harmony harmony;

    public override void Start(ICoreAPI api)
    {
        FastCraftingGridConfigSystem.Load(api);
        CraftingRecipeIndex.Invalidate();

        if (!Harmony.HasAnyPatches(HarmonyId))
        {
            harmony = new Harmony(HarmonyId);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.SaveGameLoaded += () => Prewarm(api);
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.LevelFinalize += () => Prewarm(api);
    }

    private static void Prewarm(ICoreAPI api)
    {
        api.Logger.Notification("[fastcraftinggrid] world ready - prewarming recipe index in the background");
        CraftingRecipeIndex.StartPrewarm(api.World);
    }

    public override void Dispose()
    {
        harmony?.UnpatchAll(HarmonyId);
        CraftingRecipeIndex.Invalidate();
    }
}

public sealed class FastCraftingGridConfig
{
    public bool EnableDiagnostics { get; set; }
}

public static class FastCraftingGridConfigSystem
{
    private const string ConfigFileName = "fastcraftinggrid.json";

    public static FastCraftingGridConfig Config { get; private set; } = new();

    public static void Load(ICoreAPI api)
    {
        try
        {
            Config = api.LoadModConfig<FastCraftingGridConfig>(ConfigFileName) ?? new FastCraftingGridConfig();
            api.StoreModConfig(Config, ConfigFileName);
        }
        catch (System.Exception exception)
        {
            Config = new FastCraftingGridConfig();
            api.Logger.Warning("[fastcraftinggrid] Failed to load config, using defaults: {0}", exception);
        }
    }
}
