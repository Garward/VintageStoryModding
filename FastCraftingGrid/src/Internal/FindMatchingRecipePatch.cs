using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Common;

namespace FastCraftingGrid.Internal;

[HarmonyPatch(typeof(InventoryCraftingGrid), "FindMatchingRecipe")]
[HarmonyAfter(new[] { "garward.itemsyncfixes.client", "garward.itemsyncfixes.server" })]
internal static class FindMatchingRecipePatch
{
    private const long LogEvery = 100;
    private const double SlowMatchMilliseconds = 10;

    private static long calls;
    private static long cacheHits;
    private static long totalTicks;
    private static long maxTicks;
    private static readonly ConditionalWeakTable<InventoryCraftingGrid, GridMatchCache> MatchCaches = new ConditionalWeakTable<InventoryCraftingGrid, GridMatchCache>();

    private static bool Prefix(InventoryCraftingGrid __instance)
    {
        CraftingRecipeIndex index = CraftingRecipeIndex.GetReady(__instance.Api.World);
        if (index == null) return true;

        long start = Stopwatch.GetTimestamp();
        MatchProfile profile = DoMatch(__instance, index);
        long elapsedTicks = Stopwatch.GetTimestamp() - start;
        Record(__instance, elapsedTicks, profile);

        return profile.RunVanilla;
    }

    private static MatchProfile DoMatch(InventoryCraftingGrid inventory, CraftingRecipeIndex index)
    {
        IWorldAccessor world = inventory.Api.World;

        int outputIndex = inventory.Count - 1;
        int gridWidth = (int)Math.Round(Math.Sqrt(outputIndex));

        ItemSlot[] slots = new ItemSlot[outputIndex];
        for (int i = 0; i < outputIndex; i++)
        {
            slots[i] = inventory[i];
        }

        ItemSlot outputSlot = inventory[outputIndex];
        bool hadOutput = outputSlot?.Itemstack != null;
        long gridHash = ComputeGridHash(slots);
        GridMatchCache cache = MatchCaches.GetValue(inventory, _ => new GridMatchCache());

        if (cache.Valid && cache.Hash == gridHash)
        {
            if (ApplyCachedMatch(inventory, slots, outputSlot, outputIndex, cache, hadOutput))
            {
                Interlocked.Increment(ref cacheHits);
                return MatchProfile.FromCache(cache.MatchedRecipe?.Name?.ToString() ?? "none");
            }

            cache.Valid = false;
            return MatchProfile.FromCache("errored-output");
        }

        bool hasOccupiedSlots = HasOccupiedSlots(slots);


        long gatherStart = Stopwatch.GetTimestamp();
        List<GridRecipe> candidates = index.GatherCandidates(slots);
        long gatherTicks = Stopwatch.GetTimestamp() - gatherStart;

        if (candidates.Count == 0 && hasOccupiedSlots)
        {
            cache.Valid = false;
            return MatchProfile.VanillaFallback(gatherTicks);
        }

        inventory.MatchingRecipe = null;
        outputSlot.Itemstack = null;

        IPlayer player = inventory.Player;
        int plausibleCandidates = 0;
        long shapedTicks = 0;
        long shapelessTicks = 0;
        long outputTicks;

        long shapedStart = Stopwatch.GetTimestamp();
        foreach (GridRecipe recipe in candidates)
        {
            if (!GridRecipeMatchesPatch.CanPossiblyMatch(recipe, slots, gridWidth)) continue;
            plausibleCandidates++;
            if (!recipe.Shapeless && recipe.Matches(player, world, slots, gridWidth))
            {
                shapedTicks = Stopwatch.GetTimestamp() - shapedStart;
                if (!TryFoundMatch(inventory, recipe, slots, outputSlot, outputIndex, out outputTicks))
                {
                    StoreCache(cache, gridHash, null);
                    return new MatchProfile(candidates.Count, plausibleCandidates, "errored-output", gatherTicks, shapedTicks, 0, outputTicks);
                }

                StoreCache(cache, gridHash, recipe);
                return new MatchProfile(candidates.Count, plausibleCandidates, recipe.Name?.ToString() ?? "unknown", gatherTicks, shapedTicks, 0, outputTicks);
            }
        }
        shapedTicks = Stopwatch.GetTimestamp() - shapedStart;

        long shapelessStart = Stopwatch.GetTimestamp();
        foreach (GridRecipe recipe in candidates)
        {
            if (!GridRecipeMatchesPatch.CanPossiblyMatch(recipe, slots, gridWidth)) continue;
            plausibleCandidates++;
            if (recipe.Shapeless && recipe.Matches(player, world, slots, gridWidth))
            {
                shapelessTicks = Stopwatch.GetTimestamp() - shapelessStart;
                if (!TryFoundMatch(inventory, recipe, slots, outputSlot, outputIndex, out outputTicks))
                {
                    StoreCache(cache, gridHash, null);
                    return new MatchProfile(candidates.Count, plausibleCandidates, "errored-output", gatherTicks, shapedTicks, shapelessTicks, outputTicks);
                }

                StoreCache(cache, gridHash, recipe);
                return new MatchProfile(candidates.Count, plausibleCandidates, recipe.Name?.ToString() ?? "unknown", gatherTicks, shapedTicks, shapelessTicks, outputTicks);
            }
        }
        shapelessTicks = Stopwatch.GetTimestamp() - shapelessStart;

        MarkOutputDirtyIfNeeded(inventory, outputIndex, hadOutput);
        StoreCache(cache, gridHash, null);
        return new MatchProfile(candidates.Count, plausibleCandidates, "none", gatherTicks, shapedTicks, shapelessTicks, 0);
    }

    private static bool ApplyCachedMatch(
        InventoryCraftingGrid inventory,
        ItemSlot[] slots,
        ItemSlot outputSlot,
        int outputIndex,
        GridMatchCache cache,
        bool hadOutput)
    {
        inventory.MatchingRecipe = cache.MatchedRecipe;
        outputSlot.Itemstack = null;

        if (cache.MatchedRecipe != null)
        {
            if (!TryGenerateOutputStack(inventory, cache.MatchedRecipe, slots, outputSlot))
            {
                ClearErroredOutput(inventory, outputSlot, outputIndex);
                return false;
            }
        }

        MarkOutputDirtyIfNeeded(inventory, outputIndex, hadOutput || cache.MatchedRecipe != null);
        return true;
    }

    private static void StoreCache(GridMatchCache cache, long gridHash, GridRecipe recipe)
    {
        cache.Valid = true;
        cache.Hash = gridHash;
        cache.MatchedRecipe = recipe;
    }

    private static bool HasOccupiedSlots(ItemSlot[] slots)
    {
        foreach (ItemSlot slot in slots)
        {
            if (slot?.Itemstack != null && slot.Itemstack.StackSize > 0) return true;
        }

        return false;
    }

    private static long ComputeGridHash(ItemSlot[] slots)
    {
        long hash = 17;

        for (int i = 0; i < slots.Length; i++)
        {
            ItemStack stack = slots[i]?.Itemstack;
            CollectibleObject collectible = stack?.Collectible;
            if (collectible == null || stack.StackSize <= 0) continue;

            hash = hash * 31 + i;
            hash = hash * 31 + collectible.Id;
            hash = hash * 31 + stack.StackSize;
            hash = hash * 31 + stack.Attributes.GetHashCode(Array.Empty<string>());
        }

        return hash;
    }

    private static bool TryFoundMatch(
        InventoryCraftingGrid inventory,
        GridRecipe recipe,
        ItemSlot[] slots,
        ItemSlot outputSlot,
        int outputIndex,
        out long ticks)
    {
        long start = Stopwatch.GetTimestamp();
        inventory.MatchingRecipe = recipe;
        outputSlot.Itemstack = null;
        if (!TryGenerateOutputStack(inventory, recipe, slots, outputSlot))
        {
            ticks = Stopwatch.GetTimestamp() - start;
            ClearErroredOutput(inventory, outputSlot, outputIndex);
            return false;
        }

        inventory.MarkSlotDirty(outputIndex);
        ticks = Stopwatch.GetTimestamp() - start;
        return true;
    }

    private static bool TryGenerateOutputStack(InventoryCraftingGrid inventory, GridRecipe recipe, ItemSlot[] slots, ItemSlot outputSlot)
    {
        try
        {
            recipe.GenerateOutputStack(slots, outputSlot);
            return true;
        }
        catch (InvalidOperationException ex) when (IsMissingOutputResultException(ex))
        {
            inventory.Api?.Logger.Warning(
                "[fastcraftinggrid] Suppressed crafting output generation error for {0}: {1}",
                inventory.InventoryID ?? "?",
                ex.Message);
            return false;
        }
    }

    private static bool IsMissingOutputResultException(Exception exception)
    {
        return exception is InvalidOperationException invalid
            && invalid.Message.StartsWith("Missing or errored output result for recipe", StringComparison.Ordinal);
    }

    private static void ClearErroredOutput(InventoryCraftingGrid inventory, ItemSlot outputSlot, int outputIndex)
    {
        inventory.MatchingRecipe = null;
        outputSlot.Itemstack = null;
        inventory.MarkSlotDirty(outputIndex);
    }

    private static void MarkOutputDirtyIfNeeded(InventoryCraftingGrid inventory, int outputIndex, bool hadOutput)
    {
        if (hadOutput || inventory.MatchingRecipe != null || inventory[outputIndex]?.Itemstack != null)
        {
            inventory.MarkSlotDirty(outputIndex);
        }
    }

    private static void Record(InventoryCraftingGrid inventory, long ticks, MatchProfile profile)
    {
        CraftingBurstDiagnostics.RecordFind(
            inventory,
            ticks,
            profile.CacheHit,
            profile.RunVanilla,
            profile.CandidateCount,
            profile.PlausibleCandidateCount,
            profile.GatherTicks,
            profile.ShapedTicks,
            profile.ShapelessTicks,
            profile.OutputTicks);

        long count = Interlocked.Increment(ref calls);
        Interlocked.Add(ref totalTicks, ticks);
        UpdateMax(ticks);

        if (!FastCraftingGridConfigSystem.Config.EnableDiagnostics)
        {
            return;
        }

        ILogger logger = inventory.Api.Logger;
        double toMicroseconds = 1_000_000.0 / Stopwatch.Frequency;
        double elapsedMs = ticks * 1000.0 / Stopwatch.Frequency;
        if (elapsedMs >= SlowMatchMilliseconds)
        {
            logger.Notification(
                "[fastcraftinggrid] slow indexed match " +
                $"{elapsedMs:F2}ms | recipe={profile.RecipeName} | candidates={profile.CandidateCount} | " +
                $"plausible={profile.PlausibleCandidateCount} | " +
                $"cache={profile.CacheHit} fallback={profile.RunVanilla} | gather={profile.GatherTicks * toMicroseconds:F2}us " +
                $"shaped={profile.ShapedTicks * toMicroseconds:F2}us shapeless={profile.ShapelessTicks * toMicroseconds:F2}us " +
                $"output={profile.OutputTicks * toMicroseconds:F2}us");
        }

        if (count % LogEvery != 0) return;

        double average = totalTicks * toMicroseconds / count;

        logger.Notification(
            "[fastcraftinggrid] " +
            $"{count} matches | avg {average:F2}us | max {Interlocked.Read(ref maxTicks) * toMicroseconds:F2}us " +
            $"| cache hits {Interlocked.Read(ref cacheHits)} | last {ticks * toMicroseconds:F2}us " +
            $"({profile.CandidateCount} candidates, {profile.PlausibleCandidateCount} plausible, cache={profile.CacheHit}, fallback={profile.RunVanilla})");
    }

    private static void UpdateMax(long ticks)
    {
        while (true)
        {
            long current = Interlocked.Read(ref maxTicks);
            if (ticks <= current) return;
            if (Interlocked.CompareExchange(ref maxTicks, ticks, current) == current) return;
        }
    }

    private readonly struct MatchProfile
    {
        public readonly int CandidateCount;
        public readonly int PlausibleCandidateCount;
        public readonly string RecipeName;
        public readonly bool CacheHit;
        public readonly bool RunVanilla;
        public readonly long GatherTicks;
        public readonly long ShapedTicks;
        public readonly long ShapelessTicks;
        public readonly long OutputTicks;

        public MatchProfile(int candidateCount, int plausibleCandidateCount, string recipeName, long gatherTicks, long shapedTicks, long shapelessTicks, long outputTicks)
        {
            CandidateCount = candidateCount;
            PlausibleCandidateCount = plausibleCandidateCount;
            RecipeName = recipeName;
            CacheHit = false;
            RunVanilla = false;
            GatherTicks = gatherTicks;
            ShapedTicks = shapedTicks;
            ShapelessTicks = shapelessTicks;
            OutputTicks = outputTicks;
        }

        private MatchProfile(int candidateCount, int plausibleCandidateCount, string recipeName, bool cacheHit, bool runVanilla, long gatherTicks)
        {
            CandidateCount = candidateCount;
            PlausibleCandidateCount = plausibleCandidateCount;
            RecipeName = recipeName;
            CacheHit = cacheHit;
            RunVanilla = runVanilla;
            GatherTicks = gatherTicks;
            ShapedTicks = 0;
            ShapelessTicks = 0;
            OutputTicks = 0;
        }

        public static MatchProfile FromCache(string recipeName)
        {
            return new MatchProfile(0, 0, recipeName, true, false, 0);
        }

        public static MatchProfile VanillaFallback(long gatherTicks)
        {
            return new MatchProfile(0, 0, "vanilla-fallback", false, true, gatherTicks);
        }
    }

    private sealed class GridMatchCache
    {
        public bool Valid;
        public long Hash;
        public GridRecipe MatchedRecipe;
    }
}
