using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;

namespace FastCraftingGrid.Internal;

internal sealed class CraftingRecipeIndex
{
    public readonly Dictionary<AssetLocation, RecipeBucket> ByCode = new Dictionary<AssetLocation, RecipeBucket>();
    private GridRecipe[] recipesById = Array.Empty<GridRecipe>();
    private int bitsetWordCount;

    public List<GridRecipe> GatherCandidates(ItemSlot[] slots)
    {
        RecipeBucket baseBucket = null;
        List<RecipeBucket> requiredBuckets = null;
        HashSet<AssetLocation> seenCodes = null;

        foreach (ItemSlot slot in slots)
        {
            ItemStack stack = slot?.Itemstack;
            if (stack == null || stack.StackSize == 0) continue;

            AssetLocation code = stack.Collectible?.Code;
            if (code == null) return new List<GridRecipe>();

            seenCodes ??= new HashSet<AssetLocation>();
            if (!seenCodes.Add(code)) continue;

            if (!ByCode.TryGetValue(code, out RecipeBucket bucket))
            {
                return new List<GridRecipe>();
            }

            if (baseBucket == null || bucket.Count < baseBucket.Count)
            {
                if (baseBucket != null) (requiredBuckets ??= new List<RecipeBucket>()).Add(baseBucket);
                baseBucket = bucket;
            }
            else
            {
                (requiredBuckets ??= new List<RecipeBucket>()).Add(bucket);
            }
        }

        if (baseBucket == null) return new List<GridRecipe>();
        if (requiredBuckets == null) return Materialize(baseBucket.Bits, baseBucket.Count);

        ulong[] bits = new ulong[bitsetWordCount];
        Array.Copy(baseBucket.Bits, bits, baseBucket.Bits.Length);

        foreach (RecipeBucket bucket in requiredBuckets)
        {
            for (int i = 0; i < bits.Length; i++)
            {
                bits[i] &= bucket.Bits[i];
            }
        }

        return Materialize(bits, baseBucket.Count);
    }

    private List<GridRecipe> Materialize(ulong[] bits, int maxExpectedCount)
    {
        List<GridRecipe> candidates = new List<GridRecipe>(Math.Min(maxExpectedCount, recipesById.Length));

        for (int wordIndex = 0; wordIndex < bits.Length; wordIndex++)
        {
            ulong word = bits[wordIndex];
            while (word != 0)
            {
                int bit = BitOperations.TrailingZeroCount(word);
                int recipeId = wordIndex * 64 + bit;
                if (recipeId < recipesById.Length)
                {
                    candidates.Add(recipesById[recipeId]);
                }

                word &= word - 1;
            }
        }

        return candidates;
    }

    public int EntryCount
    {
        get
        {
            int count = 0;
            foreach (KeyValuePair<AssetLocation, RecipeBucket> entry in ByCode)
            {
                count += entry.Value.Count;
            }
            return count;
        }
    }

    public int RecipeCount => recipesById.Length;

    private sealed class Slot
    {
        public volatile CraftingRecipeIndex Index;
        public int Building;
    }

    private static readonly ConditionalWeakTable<IWorldAccessor, Slot> ByWorld = new ConditionalWeakTable<IWorldAccessor, Slot>();

    public static void Invalidate()
    {
        ByWorld.Clear();
    }

    public static CraftingRecipeIndex GetReady(IWorldAccessor world)
    {
        if (world == null)
        {
            return null;
        }

        Slot slot = ByWorld.GetValue(world, _ => new Slot());
        return slot.Index;
    }

    public static void StartPrewarm(IWorldAccessor world)
    {
        if (world == null)
        {
            return;
        }

        Slot slot = ByWorld.GetValue(world, _ => new Slot());
        StartBuildIfNeeded(world, slot);
    }

    private static void StartBuildIfNeeded(IWorldAccessor world, Slot slot)
    {
        CraftingRecipeIndex index = slot.Index;
        if (index != null) return;

        if (Interlocked.CompareExchange(ref slot.Building, 1, 0) == 0)
        {
            Task.Run(() =>
            {
                try
                {
                    slot.Index = Build(world);
                }
                catch (Exception exception)
                {
                    world.Logger.Warning("[fastcraftinggrid] index build failed, using vanilla matcher: " + exception);
                    Interlocked.Exchange(ref slot.Building, 0);
                }
            });
        }
    }

    public static CraftingRecipeIndex Get(IWorldAccessor world)
    {
        Slot slot = ByWorld.GetValue(world, _ => new Slot());
        if (slot.Index != null) return slot.Index;

        lock (slot)
        {
            return slot.Index ?? (slot.Index = Build(world));
        }
    }

    private static CraftingRecipeIndex Build(IWorldAccessor world)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        System.Collections.Generic.OrderedDictionary<IRecipeIngredientBase, List<IRecipeBase>> source = world.FastSearchRecipesByIngredient;

        Dictionary<AssetLocation, HashSet<GridRecipe>> building = new Dictionary<AssetLocation, HashSet<GridRecipe>>();

        void Add(AssetLocation code, GridRecipe[] recipes)
        {
            if (!building.TryGetValue(code, out HashSet<GridRecipe> set))
            {
                set = new HashSet<GridRecipe>();
                building[code] = set;
            }

            foreach (GridRecipe recipe in recipes)
            {
                set.Add(recipe);
            }
        }

        List<CollectibleObject> collectibles = world.Collectibles;
        ItemStack[] stacks = null;

        foreach (KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>> entry in source)
        {
            IRecipeIngredientBase ingredient = entry.Key;

            GridRecipe[] recipes = EnabledGridRecipes(entry.Value);
            if (recipes.Length == 0) continue;

            if (ingredient.MatchingType == EnumRecipeMatchType.Exact && ingredient.Code != null)
            {
                Add(ingredient.Code, recipes);
            }
            else
            {
                stacks ??= BuildStacks(collectibles);
                for (int i = 0; i < stacks.Length; i++)
                {
                    ItemStack stack = stacks[i];
                    if (stack != null && ingredient.SatisfiesAsIngredient(stack, checkStackSize: false))
                    {
                        Add(collectibles[i].Code, recipes);
                    }
                }
            }
        }

        CraftingRecipeIndex index = new CraftingRecipeIndex();
        index.recipesById = BuildRecipeIdList(world, building, out Dictionary<GridRecipe, int> recipeIds);
        index.bitsetWordCount = (index.recipesById.Length + 63) / 64;

        foreach (KeyValuePair<AssetLocation, HashSet<GridRecipe>> entry in building)
        {
            index.ByCode[entry.Key] = ToBucket(entry.Value, recipeIds, index.bitsetWordCount);
        }

        stopwatch.Stop();
        world.Logger.Notification(
            "[fastcraftinggrid] pre-expanded index built in " +
            $"{stopwatch.Elapsed.TotalMilliseconds:F1}ms - {source.Count} distinct ingredients, " +
            $"{collectibles.Count} collectibles swept, {index.ByCode.Count} code buckets, " +
            $"{index.RecipeCount} recipes, {index.EntryCount} entries");

        return index;
    }

    private static GridRecipe[] EnabledGridRecipes(List<IRecipeBase> recipes)
    {
        List<GridRecipe> enabled = null;
        foreach (IRecipeBase recipe in recipes)
        {
            if (recipe is GridRecipe { Enabled: true } gridRecipe)
            {
                (enabled ??= new List<GridRecipe>()).Add(gridRecipe);
            }
        }

        return enabled?.ToArray() ?? Array.Empty<GridRecipe>();
    }

    private static ItemStack[] BuildStacks(List<CollectibleObject> collectibles)
    {
        ItemStack[] stacks = new ItemStack[collectibles.Count];
        for (int i = 0; i < collectibles.Count; i++)
        {
            CollectibleObject collectible = collectibles[i];
            stacks[i] = collectible == null || collectible.Code == null ? null : new ItemStack(collectible, 1);
        }

        return stacks;
    }

    private static GridRecipe[] BuildRecipeIdList(IWorldAccessor world, Dictionary<AssetLocation, HashSet<GridRecipe>> building, out Dictionary<GridRecipe, int> recipeIds)
    {
        recipeIds = new Dictionary<GridRecipe, int>();
        HashSet<GridRecipe> indexedRecipes = new HashSet<GridRecipe>();
        List<GridRecipe> recipes = new List<GridRecipe>();

        foreach (KeyValuePair<AssetLocation, HashSet<GridRecipe>> entry in building)
        {
            foreach (GridRecipe recipe in entry.Value)
            {
                indexedRecipes.Add(recipe);
            }
        }

        foreach (GridRecipe recipe in world.GridRecipes)
        {
            if (!indexedRecipes.Remove(recipe)) continue;
            AddRecipeId(recipeIds, recipes, recipe);
        }

        foreach (GridRecipe recipe in indexedRecipes)
        {
            AddRecipeId(recipeIds, recipes, recipe);
        }

        return recipes.ToArray();
    }

    private static void AddRecipeId(Dictionary<GridRecipe, int> recipeIds, List<GridRecipe> recipes, GridRecipe recipe)
    {
        recipeIds[recipe] = recipes.Count;
        recipes.Add(recipe);
    }

    private static RecipeBucket ToBucket(HashSet<GridRecipe> set, Dictionary<GridRecipe, int> recipeIds, int wordCount)
    {
        ulong[] bits = new ulong[wordCount];

        foreach (GridRecipe recipe in set)
        {
            int recipeId = recipeIds[recipe];
            bits[recipeId / 64] |= 1UL << (recipeId % 64);
        }

        return new RecipeBucket(bits, set.Count);
    }

    internal sealed class RecipeBucket
    {
        public readonly ulong[] Bits;
        public readonly int Count;

        public RecipeBucket(ulong[] bits, int count)
        {
            Bits = bits;
            Count = count;
        }
    }
}
