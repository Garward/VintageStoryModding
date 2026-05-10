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

        // Reverse index: item code -> list of recipe info
        private Dictionary<string, HashSet<RecipeInfo>> ingredientToRecipes;

        // Cache for wildcard expansions to avoid recomputing
        private Dictionary<string, List<CollectibleObject>> wildcardCache;

        public RecipeIndexSystem(ICoreClientAPI api)
        {
            capi = api;
            ingredientToRecipes = new Dictionary<string, HashSet<RecipeInfo>>();
            wildcardCache = new Dictionary<string, List<CollectibleObject>>();
        }

        public void BuildIndex()
        {
            ingredientToRecipes.Clear();
            wildcardCache.Clear();

            int totalRecipes = 0;

            // Index grid recipes (this is the main one and directly accessible)
            totalRecipes += IndexGridRecipes();

            // Try to index other recipe types via reflection
            totalRecipes += IndexRecipesViaReflection();

            capi.Logger.Notification("[RecipeExplorer] Indexed {0} recipes, tracking {1} unique ingredients ({2} wildcard patterns cached)",
                totalRecipes, ingredientToRecipes.Count, wildcardCache.Count);
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

        private int IndexGridRecipes()
        {
            int count = 0;
            int skippedIngredients = 0;
            var recipes = capi.World.GridRecipes;

            foreach (var recipe in recipes)
            {
                if (recipe?.Ingredients == null || recipe.Output == null) continue;

                var recipeInfo = new RecipeInfo
                {
                    Name = recipe.Output.ResolvedItemStack?.GetName() ?? "Unknown Recipe",
                    OutputStack = recipe.Output.ResolvedItemStack
                };

                // Ingredients are stored in a dictionary<string, CraftingRecipeIngredient>
                foreach (var kvp in recipe.Ingredients)
                {
                    var ingredient = kvp.Value;

                    // Index the resolved itemstack if it exists
                    if (ingredient?.ResolvedItemStack != null)
                    {
                        IndexIngredient(ingredient.ResolvedItemStack, recipeInfo, ingredient.IsTool);
                    }
                    else if (ingredient != null)
                    {
                        // If no ResolvedItemStack, try to resolve from Code
                        IndexIngredientFromCode(ingredient.Code, ingredient.Type, recipeInfo, ingredient.IsTool);
                        skippedIngredients++;
                    }
                }

                count++;
            }

            return count;
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
                    capi.Logger.Warning("[RecipeExplorer] Could not find RecipeRegistrySystem");
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
                capi.Logger.Warning("[RecipeExplorer] Failed to index additional recipes: {0}", ex.Message);
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

                    // Get ingredients
                    var ingredients = GetRecipeIngredients(recipe);
                    foreach (var ingredient in ingredients)
                    {
                        if (ingredient != null)
                        {
                            IndexIngredient(ingredient, recipeInfo);
                        }
                    }

                    count++;
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug("[RecipeExplorer] Failed to index {0}: {1}", propertyName, ex.Message);
            }

            return count;
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

        private void IndexIngredient(ItemStack stack, RecipeInfo recipeInfo, bool isTool = false)
        {
            if (stack == null || recipeInfo == null) return;

            // Mark recipes that require tools
            if (isTool)
            {
                recipeInfo = new RecipeInfo
                {
                    Name = recipeInfo.Name + " [requires tool]",
                    OutputStack = recipeInfo.OutputStack
                };
            }

            string key = GetItemKey(stack);
            AddToIndex(key, recipeInfo);
        }

        private void IndexIngredientFromCode(AssetLocation code, EnumItemClass type, RecipeInfo recipeInfo, bool isTool = false)
        {
            if (code == null) return;

            try
            {
                string codeStr = code.ToString();

                // Check if code contains wildcard
                if (codeStr.Contains("*"))
                {
                    // Index all matching items
                    IndexWildcardIngredient(codeStr, type, recipeInfo, isTool);
                    return;
                }

                // Try to get the collectible object
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
                    // Create a temporary itemstack for key generation
                    var tempStack = new ItemStack(collectible);
                    IndexIngredient(tempStack, recipeInfo, isTool);
                }
                else
                {
                    // If we can't resolve, index by code pattern directly
                    string key = string.Format("{0}:{1}", type, code);
                    AddToIndex(key, isTool ? new RecipeInfo { Name = recipeInfo.Name + " [requires tool]", OutputStack = recipeInfo.OutputStack } : recipeInfo);
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Debug("[RecipeExplorer] Failed to index ingredient {0}: {1}", code, ex.Message);
            }
        }

        private void IndexWildcardIngredient(string codePattern, EnumItemClass type, RecipeInfo recipeInfo, bool isTool)
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

                // Index all matches
                foreach (var collectible in matches)
                {
                    var tempStack = new ItemStack(collectible);
                    IndexIngredient(tempStack, recipeInfo, isTool);
                }
            }
            catch (Exception ex)
            {
                capi.Logger.Warning("[RecipeExplorer] Failed to index wildcard ingredient {0}: {1}", codePattern, ex.Message);
            }
        }

        private void AddToIndex(string key, RecipeInfo recipeInfo)
        {
            if (!ingredientToRecipes.ContainsKey(key))
            {
                ingredientToRecipes[key] = new HashSet<RecipeInfo>();
            }

            ingredientToRecipes[key].Add(recipeInfo);
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
        public ItemStack OutputStack { get; set; }

        public string GetHandbookPageCode()
        {
            if (OutputStack == null) return "";
            return string.Format("stack-{0}-{1}",
                OutputStack.Class.ToString().ToLowerInvariant(),
                OutputStack.Collectible.Code);
        }

        public override bool Equals(object obj)
        {
            return obj is RecipeInfo other && Name == other.Name;
        }

        public override int GetHashCode()
        {
            return Name?.GetHashCode() ?? 0;
        }
    }
}
