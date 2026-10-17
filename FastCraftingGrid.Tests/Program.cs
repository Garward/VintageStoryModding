using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.Common;
using FastCraftingGrid;

var assembly = typeof(FastCraftingGridModSystem).Assembly;
var patch = assembly.GetType("FastCraftingGrid.Internal.FindMatchingRecipePatch", true)!;
var hashMethod = patch.GetMethod("ComputeGridHash", BindingFlags.NonPublic | BindingFlags.Static)!;
long Hash(ItemSlot[] slots) => (long)hashMethod.Invoke(null, [slots])!;
var failures = new List<string>();
int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) failures.Add(message); }

// Item and block IDs occupy independent registries. Equal IDs are normal.
var input = new InventoryGeneric(1, "test", "cache", null);
var item = new TestItem { ItemId = 73, Code = new AssetLocation("test:ingredient-item") };
var block = new Block { BlockId = 73, Code = new AssetLocation("test:ingredient-block") };
input[0].Itemstack = new ItemStack(item, 1);
long itemHash = Hash([input[0]]);
Check(itemHash == Hash([input[0]]), "unchanged grid keeps the same hash");
input[0].Itemstack = new ItemStack(block, 1);
Check(itemHash != Hash([input[0]]), "item/block with the same ID must not reuse a cached result");
input[0].Itemstack = new ItemStack(item, 1);
input[0].Itemstack!.Attributes.SetInt("durability", 17);
Check(itemHash != Hash([input[0]]), "attribute change invalidates the grid hash");

// Seed the actual production cache, then invoke the actual matcher. Simulate
// player-dependent matching changing without any inventory hash change.
var world = DispatchProxy.Create<IWorldAccessor, EmptyProxy>();
var api = DispatchProxy.Create<ICoreAPI, EmptyProxy>();
((EmptyProxy)(object)api).World = world;
var grid = new InventoryCraftingGrid("craftinggrid-test", null) { Api = api };
grid[0].Itemstack = new ItemStack(item, 1);
var slots = Enumerable.Range(0, 9).Select(i => grid[i]).ToArray();
var cacheType = patch.GetNestedType("GridMatchCache", BindingFlags.NonPublic)!;
var table = patch.GetField("MatchCaches", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
var cache = Activator.CreateInstance(cacheType, nonPublic: true)!;
cacheType.GetField("Valid")!.SetValue(cache, true);
cacheType.GetField("Hash")!.SetValue(cache, Hash(slots));
var recipe = new ContextRecipe { Enabled = true };
cacheType.GetField("MatchedRecipe")!.SetValue(cache, recipe);
table.GetType().GetMethod("Add")!.Invoke(table, [grid, cache]);
var indexType = assembly.GetType("FastCraftingGrid.Internal.CraftingRecipeIndex", true)!;
var index = Activator.CreateInstance(indexType, nonPublic: true)!;
var match = patch.GetMethod("DoMatch", BindingFlags.NonPublic | BindingFlags.Static)!;
object? Run()
{
    try { return match.Invoke(null, [grid, index]); }
    catch (TargetInvocationException ex)
    {
        Console.Error.WriteLine("Unexpected output generation: " + ex.InnerException?.Message);
        return null;
    }
}
bool Fallback(object? profile) => profile != null && (bool)profile.GetType().GetField("RunVanilla")!.GetValue(profile)!;
Check(Fallback(Run()), "stale positive cache must resume matching and fall back when candidates are absent");
Check(recipe.Calls == 1, "cached recipe is revalidated against current player/world context");
cacheType.GetField("Valid")!.SetValue(cache, true);
cacheType.GetField("Hash")!.SetValue(cache, Hash(slots));
recipe.Enabled = false;
int previousCalls = recipe.Calls;
Check(Fallback(Run()), "disabled cached recipe must not generate output");
Check(recipe.Calls == previousCalls, "disabled recipe is rejected before invoking Matches");

// A cache hit that is still valid should retain the indexed fast path and
// generate output using the native recipe generator.
recipe.Enabled = true;
recipe.Accept = true;
recipe.ResolvedIngredients = [new CraftingRecipeIngredient { ResolvedItemStack = new ItemStack(item, 1), Quantity = 1 }];
recipe.Output = new CraftingRecipeIngredient { ResolvedItemStack = new ItemStack(item, 2), Quantity = 2 };
cacheType.GetField("Valid")!.SetValue(cache, true);
cacheType.GetField("Hash")!.SetValue(cache, Hash(slots));
var valid = Run();
Check(valid != null && !Fallback(valid), "valid positive cache retains the fast path");
Check(grid[9].Itemstack?.StackSize == 2, "valid cached recipe generates its native output");
Check(grid[0].StackSize == 1, "matching and output prediction do not consume ingredients");

foreach (string failure in failures) Console.Error.WriteLine("FAIL: " + failure);
Console.WriteLine($"{checks - failures.Count}/{checks} regression checks passed.");
return failures.Count == 0 ? 0 : 1;

public class EmptyProxy : DispatchProxy
{
    public IWorldAccessor? World;
    protected override object? Invoke(MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "get_World") return World;
        return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
    }
}
public class ContextRecipe : GridRecipe
{
    public int Calls;
    public bool Accept;
    public override bool Matches(IPlayer player, IWorldAccessor world, ItemSlot[] ingredients, int gridWidth)
    { Calls++; return Accept; }
}

public class TestItem : Item
{
    public override void OnCreatedByCrafting(ItemSlot[] inputs, ItemSlot output, IRecipeBase recipe) { }
}
