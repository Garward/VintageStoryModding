using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace RecipeExplorer
{
    /// <summary>
    /// Simplified recipe index focusing on Grid recipes with reflection for other types
    /// </summary>
    public class RecipeIndexSystem
    {
        private ICoreClientAPI capi;

        // Reverse indexes: item code -> recipes
        private Dictionary<string, HashSet<RecipeInfo>> ingredientToRecipes;
        private Dictionary<string, HashSet<RecipeInfo>> toolToRecipes;
        private Dictionary<string, HashSet<RecipeInfo>> outputToRecipes;
        // machine block code -> recipes that block can run
        private Dictionary<string, HashSet<RecipeInfo>> machineToRecipes;

        private Dictionary<string, List<CollectibleObject>> wildcardCache;

        public RecipeIndexSystem(ICoreClientAPI api)
        {
            capi = api;
            ingredientToRecipes = new Dictionary<string, HashSet<RecipeInfo>>();
            toolToRecipes = new Dictionary<string, HashSet<RecipeInfo>>();
            outputToRecipes = new Dictionary<string, HashSet<RecipeInfo>>();
            machineToRecipes = new Dictionary<string, HashSet<RecipeInfo>>();
            wildcardCache = new Dictionary<string, List<CollectibleObject>>();
        }

        public int IndexedItemCount => ingredientToRecipes.Count;

        public void BuildIndex()
        {
            ingredientToRecipes.Clear();
            toolToRecipes.Clear();
            outputToRecipes.Clear();
            machineToRecipes.Clear();
            wildcardCache.Clear();

            int totalRecipes = 0;

            // Index grid recipes (this is the main one and directly accessible)
            totalRecipes += IndexGridRecipes();

            // Try to index other recipe types via reflection
            totalRecipes += IndexRecipesViaReflection();

            // Index machine/process recipes from any mod that exposes Ingredient/Outputs registries
            totalRecipes += IndexModRecipeRegistries();

            BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] Indexed {0} recipes, tracking {1} unique ingredients, {2} unique outputs ({3} wildcard patterns cached)",
                totalRecipes, ingredientToRecipes.Count, outputToRecipes.Count, wildcardCache.Count);
        }

        public List<RecipeInfo> GetRecipesThatUse(ItemStack stack)
        {
            if (stack == null) return new List<RecipeInfo>();

            string key = GetItemKey(stack);
            var results = new List<RecipeInfo>();

            if (ingredientToRecipes.TryGetValue(key, out var recipes))
            {
                results.AddRange(recipes);
            }

            return results;
        }

        public List<RecipeInfo> GetRecipesUsingAsTool(ItemStack stack)
        {
            if (stack == null) return new List<RecipeInfo>();

            string key = GetItemKey(stack);
            var results = new List<RecipeInfo>();

            if (toolToRecipes.TryGetValue(key, out var recipes))
            {
                results.AddRange(recipes);
            }

            return results;
        }

        public List<RecipeInfo> GetRecipesThatProduce(ItemStack stack)
        {
            if (stack == null) return new List<RecipeInfo>();

            string key = GetItemKey(stack);
            var results = new List<RecipeInfo>();

            if (outputToRecipes.TryGetValue(key, out var recipes))
            {
                results.AddRange(recipes);
            }

            return results;
        }

        public List<RecipeInfo> GetRecipesProducedByMachine(ItemStack stack)
        {
            var results = new List<RecipeInfo>();
            if (stack?.Collectible?.Code == null) return results;

            string code = stack.Collectible.Code.ToString();
            if (machineToRecipes.TryGetValue(code, out var recipes))
            {
                results.AddRange(recipes);
            }
            return results;
        }

        private int IndexGridRecipes()
        {
            int count = 0;
            int skippedIngredients = 0;
            var recipes = ResolveGridRecipes();

            if (recipes == null)
            {
                BetterHandbookLog.Failure(capi, "[BetterHandbook/RecipeExplorer] No grid recipe source found; index will be empty");
                return 0;
            }

            foreach (var recipe in recipes)
            {
                if (recipe == null) continue;
                if (recipe.Output == null) continue;

                // VS 1.22: the keyed Ingredients dict is dead post-resolution; use ResolvedIngredients[].
                var resolved = recipe.ResolvedIngredients;
                if (resolved == null || resolved.Length == 0) continue;

                var recipeInfo = new RecipeInfo
                {
                    Name = recipe.Output.ResolvedItemStack?.GetName() ?? "Unknown Recipe",
                    OutputStack = recipe.Output.ResolvedItemStack
                };

                if (recipe.Output.ResolvedItemStack != null)
                {
                    IndexIngredient(recipe.Output.ResolvedItemStack, recipeInfo, outputToRecipes);
                }

                foreach (var ingredient in resolved)
                {
                    if (ingredient == null) continue;

                    // Treat as "tool" if vanilla flags it, takes durability damage, isn't consumed, or is returned.
                    bool isToolUse = ingredient.IsTool
                                     || ingredient.ToolDurabilityCost > 0
                                     || ingredient.Consume == false
                                     || ingredient.ReturnedStack != null;
                    var targetIndex = isToolUse ? toolToRecipes : ingredientToRecipes;

                    if (ingredient.ResolvedItemStack != null)
                    {
                        IndexIngredient(ingredient.ResolvedItemStack, recipeInfo, targetIndex);
                    }
                    else if (ingredient.Code != null)
                    {
                        IndexIngredientFromCode(ingredient.Code, ingredient.Type, recipeInfo, targetIndex);
                        skippedIngredients++;
                    }
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// Tries every known way to get the grid recipe collection. VS shuffles where this lives
        /// across versions; the public <c>capi.World.GridRecipes</c> wrapper started returning empty
        /// on the client in 1.22, so fall back to the recipe registry directly.
        /// </summary>
        private System.Collections.Generic.IEnumerable<Vintagestory.API.Common.GridRecipe> ResolveGridRecipes()
        {
            int worldCount = capi.World.GridRecipes?.Count ?? 0;
            if (worldCount > 0)
            {
                BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] Grid recipes via capi.World.GridRecipes: {0}", worldCount);
                return capi.World.GridRecipes;
            }

            // Fallback: walk RecipeRegistrySystem for a property holding GridRecipe[]/List<GridRecipe>.
            try
            {
                var rrs = capi.ModLoader.GetModSystem("Vintagestory.GameContent.RecipeRegistrySystem");
                if (rrs != null)
                {
                    foreach (var prop in rrs.GetType().GetProperties())
                    {
                        var val = prop.GetValue(rrs) as System.Collections.IEnumerable;
                        if (val == null) continue;
                        var typed = new System.Collections.Generic.List<Vintagestory.API.Common.GridRecipe>();
                        foreach (var r in val)
                        {
                            if (r is Vintagestory.API.Common.GridRecipe g) typed.Add(g);
                        }
                        if (typed.Count > 0)
                        {
                            BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] Grid recipes via RecipeRegistrySystem.{0}: {1}", prop.Name, typed.Count);
                            return typed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug("[BetterHandbook/RecipeExplorer] RecipeRegistrySystem walk failed: {0}", ex.Message);
            }

            return null;
        }

        private int IndexRecipesViaReflection()
        {
            int count = 0;

            try
            {
                // Get RecipeRegistrySystem via reflection
                var recipeRegistry = capi.ModLoader.GetModSystem("Vintagestory.GameContent.RecipeRegistrySystem");
                if (recipeRegistry == null)
                {
                    BetterHandbookLog.Failure(capi, "[BetterHandbook/RecipeExplorer] Could not find RecipeRegistrySystem");
                    return 0;
                }

                var type = recipeRegistry.GetType();

                // Index each recipe type
                count += IndexRecipeList(type, recipeRegistry, "CookingRecipes");
                count += IndexRecipeList(type, recipeRegistry, "SmithingRecipes");
                count += IndexRecipeList(type, recipeRegistry, "ClayFormingRecipes");
                count += IndexRecipeList(type, recipeRegistry, "KnappingRecipes");
                count += IndexRecipeList(type, recipeRegistry, "BarrelRecipes");
                count += IndexRecipeList(type, recipeRegistry, "MetalAlloys");
            }
            catch (Exception ex)
            {
                BetterHandbookLog.Failure(capi, "[BetterHandbook/RecipeExplorer] Failed to index additional recipes: {0}", ex.Message);
            }

            return count;
        }

        private int IndexRecipeList(Type registryType, object registry, string propertyName)
        {
            int count = 0;

            try
            {
                var property = registryType.GetProperty(propertyName);
                if (property == null) return 0;

                var recipes = property.GetValue(registry) as System.Collections.IList;
                if (recipes == null) return 0;

                foreach (var recipe in recipes)
                {
                    if (recipe == null) continue;

                    // Create RecipeInfo for this recipe
                    var recipeInfo = GetRecipeInfo(recipe);

                    if (recipeInfo.OutputStack != null)
                    {
                        IndexIngredient(recipeInfo.OutputStack, recipeInfo, outputToRecipes);
                    }

                    // Get ingredients (these recipe types don't have tool/ingredient distinction)
                    var ingredients = GetRecipeIngredients(recipe);
                    foreach (var ingredient in ingredients)
                    {
                        if (ingredient != null)
                        {
                            IndexIngredient(ingredient, recipeInfo, ingredientToRecipes);
                        }
                    }

                    count++;
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug("[BetterHandbook/RecipeExplorer] Failed to index {0}: {1}", propertyName, ex.Message);
            }

            return count;
        }

        /// <summary>
        /// Walks every loaded ModSystem looking for recipe collections whose elements expose
        /// `Ingredient` (single JsonItemStack) and `Outputs` (JsonItemStack[]). This covers all four
        /// VintageKinematics machines plus any other mod following the same shape, without
        /// hard-binding to those types.
        /// </summary>
        private int IndexModRecipeRegistries()
        {
            int count = 0;

            try
            {
                foreach (var sys in capi.ModLoader.Systems)
                {
                    if (sys == null) continue;
                    var sysType = sys.GetType();

                    // Look at every public instance property/field returning an enumerable
                    foreach (var member in EnumerateMembers(sysType))
                    {
                        System.Collections.IEnumerable enumerable;
                        try { enumerable = GetMemberValue(member, sys) as System.Collections.IEnumerable; }
                        catch { continue; }

                        if (enumerable == null) continue;
                        if (enumerable is string) continue;

                        int added = TryIndexRecipeEnumerable(sysType.Name, enumerable);
                        count += added;
                    }
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug("[BetterHandbook/RecipeExplorer] Mod registry scan failed: {0}", ex.Message);
            }

            return count;
        }

        private static IEnumerable<System.Reflection.MemberInfo> EnumerateMembers(Type t)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance;
            foreach (var p in t.GetProperties(flags))
            {
                if (p.GetIndexParameters().Length == 0) yield return p;
            }
            foreach (var f in t.GetFields(flags)) yield return f;
        }

        private static object GetMemberValue(System.Reflection.MemberInfo m, object instance)
        {
            if (m is System.Reflection.PropertyInfo p) return p.GetValue(instance);
            if (m is System.Reflection.FieldInfo f) return f.GetValue(instance);
            return null;
        }

        private int TryIndexRecipeEnumerable(string ownerName, System.Collections.IEnumerable items)
        {
            int count = 0;
            string sampledTypeName = null;
            List<string> machineCodes = null;

            foreach (var item in items)
            {
                if (item == null) continue;
                var itemType = item.GetType();

                var ingMember = FindMember(itemType, "Ingredient");
                var outMember = FindMember(itemType, "Outputs");
                if (ingMember == null || outMember == null) return 0; // Not a recipe-shaped collection

                if (sampledTypeName == null)
                {
                    sampledTypeName = itemType.Name;
                    machineCodes = FindMachineCodesFor(itemType.Name);
                }

                var outputStacks = ResolveJsonItemStackArray(GetMemberValue(outMember, item));
                if (outputStacks == null || outputStacks.Count == 0) continue;

                var ingredientStack = ResolveJsonItemStack(GetMemberValue(ingMember, item));

                // Recipe label uses primary output for searchability.
                var primary = outputStacks[0];
                var recipeInfo = new RecipeInfo
                {
                    Name = primary?.GetName() ?? itemType.Name,
                    OutputStack = primary,
                    SourceName = ownerName
                };

                foreach (var output in outputStacks)
                {
                    if (output != null) IndexIngredient(output, recipeInfo, outputToRecipes);
                }

                if (ingredientStack != null)
                {
                    IndexIngredient(ingredientStack, recipeInfo, ingredientToRecipes);
                }

                if (machineCodes != null)
                {
                    foreach (var mc in machineCodes) AddToIndex(machineToRecipes, mc, recipeInfo);
                }

                count++;
            }

            if (count > 0)
            {
                string machineDesc = (machineCodes != null && machineCodes.Count > 0)
                    ? string.Format(" -> machine(s): {0}", string.Join(", ", machineCodes))
                    : "";
                BetterHandbookLog.Info(capi, "[BetterHandbook/RecipeExplorer] Indexed {0} {1} recipe(s) from {2}{3}",
                    count, sampledTypeName ?? "?", ownerName, machineDesc);
            }

            return count;
        }

        /// <summary>
        /// Heuristic: derive candidate "machine subjects" from the recipe class name and find
        /// blocks whose code path matches. Tries the full name plus progressively shorter
        /// CamelCase suffixes (e.g. KineticCrusherRecipe → "kineticcrusher", "crusher")
        /// because mod naming isn't always consistent (VK has KineticSawmill but plain crusher).
        /// Prefers exact path matches; falls back to "starts with" only if nothing exact lands.
        /// </summary>
        private List<string> FindMachineCodesFor(string recipeTypeName)
        {
            var hits = new List<string>();
            if (string.IsNullOrEmpty(recipeTypeName)) return hits;

            string trimmed = recipeTypeName;
            string[] suffixes = { "Recipe", "Recipes", "Registry", "RegistrySystem", "ModSystem", "System" };
            bool stripped = true;
            while (stripped)
            {
                stripped = false;
                foreach (var s in suffixes)
                {
                    if (trimmed.Length > s.Length && trimmed.EndsWith(s, StringComparison.OrdinalIgnoreCase))
                    {
                        trimmed = trimmed.Substring(0, trimmed.Length - s.Length);
                        stripped = true;
                    }
                }
            }
            if (trimmed.Length < 3) return hits;

            // Build candidates: full lowercase, plus each progressively shorter suffix
            // formed by chopping off leading CamelCase words.
            // KineticCrusher -> ["kineticcrusher", "crusher"]
            // CrusherKinetic -> ["crusherkinetic", "kinetic"]
            var camelStarts = new List<int>();
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (i == 0 || char.IsUpper(trimmed[i])) camelStarts.Add(i);
            }
            var candidates = new List<string>();
            foreach (int start in camelStarts)
            {
                var c = trimmed.Substring(start).ToLowerInvariant();
                if (c.Length >= 3 && !candidates.Contains(c)) candidates.Add(c);
            }

            try
            {
                // Pass 1: exact path match on any candidate.
                foreach (var block in capi.World.Blocks)
                {
                    if (block?.Code?.Path == null) continue;
                    string path = block.Code.Path;
                    if (candidates.Contains(path))
                    {
                        hits.Add(block.Code.ToString());
                    }
                }

                if (hits.Count > 0) return hits;

                // Pass 2: starts-with match. Avoids matching unrelated blocks like
                // "crusherbasin" being linked to crusher recipes (but accepts variants).
                foreach (var block in capi.World.Blocks)
                {
                    if (block?.Code?.Path == null) continue;
                    string path = block.Code.Path;
                    foreach (var c in candidates)
                    {
                        if (path.StartsWith(c, StringComparison.Ordinal))
                        {
                            hits.Add(block.Code.ToString());
                            break;
                        }
                    }
                }
            }
            catch { }

            return hits;
        }

        private static System.Reflection.MemberInfo FindMember(Type t, string name)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase;
            var p = t.GetProperty(name, flags);
            if (p != null) return p;
            return t.GetField(name, flags);
        }

        private List<ItemStack> ResolveJsonItemStackArray(object value)
        {
            var result = new List<ItemStack>();
            if (value == null) return result;

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                foreach (var entry in enumerable)
                {
                    var stack = ResolveJsonItemStack(entry);
                    if (stack != null) result.Add(stack);
                }
                return result;
            }

            var single = ResolveJsonItemStack(value);
            if (single != null) result.Add(single);
            return result;
        }

        private ItemStack ResolveJsonItemStack(object value)
        {
            if (value == null) return null;
            if (value is ItemStack already) return already;

            try
            {
                var t = value.GetType();
                var resolvedProp = t.GetProperty("ResolvedItemstack") ?? t.GetProperty("ResolvedItemStack");
                if (resolvedProp != null)
                {
                    var stack = resolvedProp.GetValue(value) as ItemStack;
                    if (stack != null) return stack;
                }

                // Fall back to manual resolve from Type+Code+Quantity if present
                var typeProp = t.GetProperty("Type");
                var codeProp = t.GetProperty("Code");
                if (typeProp != null && codeProp != null)
                {
                    var typeVal = typeProp.GetValue(value);
                    var codeVal = codeProp.GetValue(value) as AssetLocation;
                    if (codeVal != null && typeVal is EnumItemClass cls)
                    {
                        CollectibleObject coll = cls == EnumItemClass.Block
                            ? (CollectibleObject)capi.World.GetBlock(codeVal)
                            : capi.World.GetItem(codeVal);
                        if (coll != null) return new ItemStack(coll);
                    }
                }
            }
            catch { }

            return null;
        }

        private RecipeInfo GetRecipeInfo(object recipe)
        {
            var recipeInfo = new RecipeInfo
            {
                Name = "Unknown Recipe",
                OutputStack = null
            };

            try
            {
                // Try Output.ResolvedItemStack
                var outputProp = recipe.GetType().GetProperty("Output");
                if (outputProp != null)
                {
                    var output = outputProp.GetValue(recipe);
                    if (output != null)
                    {
                        var resolvedProp = output.GetType().GetProperty("ResolvedItemStack");
                        if (resolvedProp != null)
                        {
                            var stack = resolvedProp.GetValue(output) as ItemStack;
                            if (stack != null)
                            {
                                recipeInfo.Name = stack.GetName();
                                recipeInfo.OutputStack = stack;
                                return recipeInfo;
                            }
                        }
                    }
                }

                // Try Code property as fallback
                var codeProp = recipe.GetType().GetProperty("Code");
                if (codeProp != null)
                {
                    var code = codeProp.GetValue(recipe);
                    if (code != null)
                    {
                        recipeInfo.Name = code.ToString();
                    }
                }
            }
            catch { }

            return recipeInfo;
        }

        private List<ItemStack> GetRecipeIngredients(object recipe)
        {
            var result = new List<ItemStack>();

            try
            {
                // Try Ingredients array
                var ingredientsProp = recipe.GetType().GetProperty("Ingredients");
                if (ingredientsProp != null)
                {
                    var ingredients = ingredientsProp.GetValue(recipe) as System.Collections.IEnumerable;
                    if (ingredients != null)
                    {
                        foreach (var ing in ingredients)
                        {
                            var stack = GetResolvedStack(ing);
                            if (stack != null)
                            {
                                result.Add(stack);
                            }
                        }
                    }
                }

                // Try single Ingredient property
                var ingredientProp = recipe.GetType().GetProperty("Ingredient");
                if (ingredientProp != null)
                {
                    var ingredient = ingredientProp.GetValue(recipe);
                    var stack = GetResolvedStack(ingredient);
                    if (stack != null)
                    {
                        result.Add(stack);
                    }
                }
            }
            catch { }

            return result;
        }

        private ItemStack GetResolvedStack(object ingredient)
        {
            if (ingredient == null) return null;

            try
            {
                var resolvedProp = ingredient.GetType().GetProperty("ResolvedItemStack");
                if (resolvedProp != null)
                {
                    return resolvedProp.GetValue(ingredient) as ItemStack;
                }
            }
            catch { }

            return null;
        }

        private void IndexIngredient(ItemStack stack, RecipeInfo recipeInfo, Dictionary<string, HashSet<RecipeInfo>> targetIndex)
        {
            if (stack == null || recipeInfo == null) return;

            string key = GetItemKey(stack);
            AddToIndex(targetIndex, key, recipeInfo);
        }

        private void IndexIngredientFromCode(AssetLocation code, EnumItemClass type, RecipeInfo recipeInfo, Dictionary<string, HashSet<RecipeInfo>> targetIndex)
        {
            if (code == null) return;

            try
            {
                string codeStr = code.ToString();

                // Check if code contains wildcard
                if (codeStr.Contains("*"))
                {
                    IndexWildcardIngredient(codeStr, type, recipeInfo, targetIndex);
                    return;
                }

                CollectibleObject collectible = null;

                if (type == EnumItemClass.Block)
                {
                    collectible = capi.World.GetBlock(code);
                }
                else
                {
                    collectible = capi.World.GetItem(code);
                }

                if (collectible != null)
                {
                    var tempStack = new ItemStack(collectible);
                    IndexIngredient(tempStack, recipeInfo, targetIndex);
                }
                else
                {
                    string key = string.Format("{0}:{1}", type, code);
                    AddToIndex(targetIndex, key, recipeInfo);
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug("[BetterHandbook/RecipeExplorer] Failed to index ingredient {0}: {1}", code, ex.Message);
            }
        }

        private void IndexWildcardIngredient(string codePattern, EnumItemClass type, RecipeInfo recipeInfo, Dictionary<string, HashSet<RecipeInfo>> targetIndex)
        {
            try
            {
                string cacheKey = string.Format("{0}:{1}", type, codePattern);

                // Check cache first
                List<CollectibleObject> matches;
                if (!wildcardCache.TryGetValue(cacheKey, out matches))
                {
                    // Not in cache - compute it
                    matches = new List<CollectibleObject>();

                    // Convert wildcard pattern to regex
                    string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(codePattern)
                        .Replace("\\*", ".*")
                        .Replace("\\?", ".") + "$";

                    var regex = new System.Text.RegularExpressions.Regex(regexPattern);

                    // Get all collectibles of the specified type
                    IEnumerable<CollectibleObject> collectibles;
                    if (type == EnumItemClass.Block)
                    {
                        collectibles = capi.World.Blocks;
                    }
                    else
                    {
                        collectibles = capi.World.Items;
                    }

                    foreach (var collectible in collectibles)
                    {
                        if (collectible == null || collectible.Code == null) continue;

                        string fullCode = collectible.Code.ToString();
                        if (regex.IsMatch(fullCode))
                        {
                            matches.Add(collectible);
                        }
                    }

                    // Store in cache
                    wildcardCache[cacheKey] = matches;
                }

                // Skip wildcards that are too broad to be meaningful in a "what uses this" lookup
                // (e.g. "*:*" matches every collectible). Such slots are usually filler/decorative.
                const int MaxWildcardMatches = 200;
                if (matches.Count > MaxWildcardMatches) return;

                foreach (var collectible in matches)
                {
                    var tempStack = new ItemStack(collectible);
                    IndexIngredient(tempStack, recipeInfo, targetIndex);
                }
            }
            catch (Exception ex)
            {
                BetterHandbookLog.Failure(capi, "[BetterHandbook/RecipeExplorer] Failed to index wildcard ingredient {0}: {1}", codePattern, ex.Message);
            }
        }

        private void AddToIndex(Dictionary<string, HashSet<RecipeInfo>> index, string key, RecipeInfo recipeInfo)
        {
            if (!index.ContainsKey(key))
            {
                index[key] = new HashSet<RecipeInfo>();
            }

            index[key].Add(recipeInfo);
        }

        private string GetItemKey(ItemStack stack)
        {
            if (stack == null) return "";

            // Use just the base code without variants for more flexible matching
            string code = stack.Collectible.Code.ToString();

            return string.Format("{0}:{1}", stack.Class, code);
        }
    }

    /// <summary>
    /// Information about a recipe for display and linking
    /// </summary>
    public class RecipeInfo
    {
        public string Name { get; set; }
        public string SourceName { get; set; }
        public ItemStack OutputStack { get; set; }

        public string GetHandbookPageCode()
        {
            if (OutputStack?.Collectible == null) return "";
            // Match vanilla's GuiHandbookItemStackPage.PageCodeForStack: "{class}-{shortcode}"
            return string.Format("{0}-{1}",
                OutputStack.Class.ToString().ToLowerInvariant(),
                OutputStack.Collectible.Code.ToShortString());
        }

        public override bool Equals(object obj)
        {
            return obj is RecipeInfo other && Name == other.Name && SourceName == other.SourceName;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (Name?.GetHashCode() ?? 0);
                hash = hash * 31 + (SourceName?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }
}
