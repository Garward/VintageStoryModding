using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;

namespace FastCraftingGrid.Internal;

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Matches), new[] { typeof(IPlayer), typeof(IWorldAccessor), typeof(ItemSlot[]), typeof(int) })]
internal static class GridRecipeMatchesPatch
{
    private const long LogEvery = 250000;

    private static readonly ConditionalWeakTable<GridRecipe, RecipeMeta> RecipeMetas = new ConditionalWeakTable<GridRecipe, RecipeMeta>();

    [ThreadStatic]
    private static GridSnapshot gridSnapshot;

    private static long calls;
    private static long rejected;

    private static bool Prefix(GridRecipe __instance, IPlayer forPlayer, IWorldAccessor world, ItemSlot[] ingredients, int gridWidth, ref bool __result)
    {
        long callCount = Interlocked.Increment(ref calls);

        if (!__instance.Enabled || __instance.ResolvedIngredients == null)
        {
            __result = false;
            CountRejected(world, callCount);
            return false;
        }

        if (!CanPossiblyMatch(__instance, ingredients, gridWidth))
        {
            __result = false;
            CountRejected(world, callCount);
            return false;
        }

        if (FastCraftingGridConfigSystem.Config.EnableDiagnostics && callCount % LogEvery == 0)
        {
            LogStats(world, callCount, BuildCallerSummary());
        }

        return true;
    }

    public static bool CanPossiblyMatch(GridRecipe recipe, ItemSlot[] ingredients, int gridWidth)
    {
        RecipeMeta meta = RecipeMetas.GetValue(recipe, value => new RecipeMeta(value));
        GridSnapshot snapshot = GetSnapshot(ingredients);

        if (snapshot.FilledSlots < meta.RequiredIngredientCount) return false;

        if (!meta.Shapeless)
        {
            int gridHeight = ingredients.Length / gridWidth;
            if (gridWidth < meta.Width || gridHeight < meta.Height) return false;
        }

        foreach (int itemId in meta.RequiredConcreteItemIds)
        {
            if (!snapshot.ItemIds.Contains(itemId)) return false;
        }

        return true;
    }

    private static GridSnapshot GetSnapshot(ItemSlot[] ingredients)
    {
        long hash = ComputeGridHash(ingredients);
        GridSnapshot snapshot = gridSnapshot;

        if (snapshot == null)
        {
            snapshot = new GridSnapshot();
            gridSnapshot = snapshot;
        }

        if (snapshot.Hash == hash) return snapshot;

        snapshot.Hash = hash;
        snapshot.FilledSlots = 0;
        snapshot.ItemIds.Clear();

        for (int i = 0; i < ingredients.Length; i++)
        {
            ItemStack stack = ingredients[i]?.Itemstack;
            CollectibleObject collectible = stack?.Collectible;
            if (collectible == null) continue;

            snapshot.FilledSlots++;
            snapshot.ItemIds.Add(collectible.Id);
        }

        return snapshot;
    }

    private static long ComputeGridHash(ItemSlot[] ingredients)
    {
        long hash = 17;
        for (int i = 0; i < ingredients.Length; i++)
        {
            ItemStack stack = ingredients[i]?.Itemstack;
            CollectibleObject collectible = stack?.Collectible;
            if (collectible == null) continue;

            hash = hash * 31 + i;
            hash = hash * 31 + collectible.Id;
            hash = hash * 31 + stack.StackSize;
        }

        return hash;
    }

    private static void CountRejected(IWorldAccessor world, long callCount)
    {
        Interlocked.Increment(ref rejected);
        if (FastCraftingGridConfigSystem.Config.EnableDiagnostics && callCount % LogEvery == 0)
        {
            LogStats(world, callCount, BuildCallerSummary());
        }
    }

    private static void LogStats(IWorldAccessor world, long callCount, string callerSummary)
    {
        if (!FastCraftingGridConfigSystem.Config.EnableDiagnostics)
        {
            return;
        }

        long rejectedCount = Interlocked.Read(ref rejected);
        world?.Logger?.Notification(
            "[fastcraftinggrid] GridRecipe.Matches prefilter " +
            $"{callCount} calls | rejected {rejectedCount} ({(rejectedCount * 100.0 / Math.Max(1, callCount)):F1}%) | caller {callerSummary}");
    }

    private static string BuildCallerSummary()
    {
        StackTrace trace = new StackTrace(1, false);
        List<string> frames = new List<string>();

        for (int i = 0; i < trace.FrameCount && frames.Count < 8; i++)
        {
            MethodBase method = trace.GetFrame(i)?.GetMethod();
            if (method == null) continue;

            Type type = method.DeclaringType;
            string typeName = type?.FullName ?? "?";
            if (typeName.StartsWith("FastCraftingGrid.", StringComparison.Ordinal)) continue;
            if (typeName.StartsWith("HarmonyLib.", StringComparison.Ordinal)) continue;
            if (typeName.StartsWith("System.", StringComparison.Ordinal)) continue;
            if (typeName.StartsWith("Microsoft.", StringComparison.Ordinal)) continue;

            frames.Add(typeName + "." + method.Name);
        }

        return frames.Count == 0 ? "unknown" : string.Join(" <- ", frames);
    }

    private sealed class GridSnapshot
    {
        public long Hash;
        public int FilledSlots;
        public readonly HashSet<int> ItemIds = new HashSet<int>();
    }

    private sealed class RecipeMeta
    {
        public readonly int RequiredIngredientCount;
        public readonly int Width;
        public readonly int Height;
        public readonly bool Shapeless;
        public readonly int[] RequiredConcreteItemIds;

        public RecipeMeta(GridRecipe recipe)
        {
            Width = recipe.Width;
            Height = recipe.Height;
            Shapeless = recipe.Shapeless;

            HashSet<int> concreteIds = new HashSet<int>();
            CraftingRecipeIngredient[] resolvedIngredients = recipe.ResolvedIngredients;

            if (resolvedIngredients != null)
            {
                foreach (CraftingRecipeIngredient ingredient in resolvedIngredients)
                {
                    if (ingredient == null) continue;

                    RequiredIngredientCount++;

                    if (ingredient.MatchingType == EnumRecipeMatchType.Exact)
                    {
                        CollectibleObject collectible = ingredient.ResolvedItemStack?.Collectible;
                        if (collectible != null)
                        {
                            concreteIds.Add(collectible.Id);
                        }
                    }
                }
            }

            RequiredConcreteItemIds = new int[concreteIds.Count];
            concreteIds.CopyTo(RequiredConcreteItemIds);
        }
    }
}
