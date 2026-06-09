# Fast Crafting Grid

Fast Crafting Grid reduces UI stalls caused by Vintage Story crafting grid recipe scans on heavily modded 1.22.x servers.

The mod is intentionally narrow: it only changes crafting-grid recipe matching and the global `GridRecipe.Matches()` prefilter path. It does not change recipe definitions, crafting output rules, inventories, block entities, or non-grid crafting systems.

## Recorded Performance

Measurements below came from a heavily modded Stratum test server on Vintage Story 1.22.x with roughly 90k collectibles and 80k indexed grid recipes.

| Build / path | Observed result | Notes |
| --- | ---: | --- |
| Vanilla `InventoryCraftingGrid.FindMatchingRecipe()` on problem recipes | 54-103ms stalls | Expensive enough to visibly freeze the client while editing the crafting grid. |
| Prototype indexed matcher | 100 matches, avg 442.38us, max 5519.53us, cache hits 71 | Major improvement, but still had a slow gather edge case. |
| Prototype before bitsets, visible behavior | Still had visible freezes around 300-700ms | The direct matcher logs did not account for the full user-visible pause, which means some time was outside the measured `FindMatchingRecipe` block or came from repeated caller bursts around it. |
| Prototype before bitsets, measured indexed slow sample | 16.55ms | The slow sample had `candidates=1`, `plausible=1`, and `gather=16430us`, proving the measured indexed cost was candidate gathering, not final recipe matching. |
| Fast Crafting Grid bitset candidate gathering | 100 matches, avg 117.11us, max 3271.53us | No `slow indexed match` log lines after the bitset change. |
| Fast Crafting Grid later session | 500 matches, avg 64.11us, max 3971.63us, cache hits 373 | Crafting felt instant in the tested problem recipe. |
| `GridRecipe.Matches()` global prefilter | Rejected 99.1-99.4% of calls | Remaining high-volume caller was `HandbookCache.CraftingOutputConflictSelector.FindMatches`. |

Important measurement caveat: the per-match summaries only time this mod's `InventoryCraftingGrid.FindMatchingRecipe` prefix. They do not measure total frame time, GUI redraw work, server packet handling, handbook conflict selector work before or after the call, GC pauses, or any other mod hooks around the same inventory update. That is why the pre-bitset prototype could show small direct matcher timings while still producing a visible 300-700ms pause.

Index build cost is paid once per world side after world load. In the same test environment the index built in about 1.3-2.2 seconds in the background:

```text
pre-expanded index built in 1327.7ms - 14113 distinct ingredients, 90812 collectibles swept, 17393 code buckets, 80910 recipes, 839088 entries
pre-expanded index built in 2191.8ms - 14109 distinct ingredients, 90812 collectibles swept, 17392 code buckets, 80871 recipes, 838962 entries
```

## Patch Summary

### `InventoryCraftingGrid.FindMatchingRecipe`

Reason:

Vanilla recipe matching can scan a very large ingredient lookup table and then run expensive recipe matching repeatedly on every grid edit. In large modpacks this can make ordinary crafting inputs freeze for tens of milliseconds sometimes over a second.

How it works:

- Builds an indexed recipe lookup from `world.FastSearchRecipesByIngredient`.
- For each occupied input slot, looks up recipes that can accept that item code.
- Intersects those recipe sets to produce a small candidate list.
- Runs shaped recipes first, then shapeless recipes, matching vanilla order more closely.
- Preserves vanilla behavior by falling back to vanilla matching when the index cannot produce candidates for an occupied grid.
- Generates the output stack with the original `GridRecipe.GenerateOutputStack()` method.
- Marks the output slot dirty after updating it.

Correctness notes:

- Recipe ids are assigned in `world.GridRecipes` order, so candidate materialization follows the vanilla recipe list order for normal loaded recipes.
- The patch has `HarmonyAfter` entries for `garward.itemsyncfixes.client` and `garward.itemsyncfixes.server` so ItemSyncFixes can keep its output-state cleanup behavior.
- The vanilla fallback is deliberately kept for unusual modded recipes or matching behavior not represented by the index.

### Pre-Expanded Ingredient Index

Reason:

The vanilla fast search dictionary is still expensive when many recipe ingredients are broad wildcard/tag matches. Those broad ingredients can match huge numbers of collectibles, which makes per-grid matching costly.

How it works:

- At world ready, starts building the index in the background.
- Exact ingredients map directly from `AssetLocation` to the recipes that use that exact code.
- Broad ingredients are expanded once by sweeping `world.Collectibles` and checking `SatisfiesAsIngredient(stack, checkStackSize: false)`.
- Each item code gets a bucket of recipes that can accept that item.
- If the index is not ready yet, the mod lets vanilla matching run.

Tradeoff:

The index uses more memory because broad recipe ingredients are pre-expanded into item-code buckets. That is intentional: it moves the expensive work away from every crafting-grid edit and into a one-time background build.

### Recipe-Id Bitset Candidate Gathering

Reason:

The first indexed version still used arrays and nested linear scans to intersect candidate recipe sets. On huge buckets, that could still spike. One recorded slow craft spent 16.43ms in gather time even though the final candidate list contained only one recipe.

In practice this was worse than the 16ms instrumentation implied. The pre-bitset prototype still produced visible 300-700ms freezes during testing, so the gather spike was likely combining with surrounding UI/caller work or repeated matching bursts in the same frame. Removing the nested scans was the change that made the same tested craft feel instant.

How it works:

- Assigns every indexed `GridRecipe` a dense integer id.
- Stores each item-code bucket as a `ulong[]` bitset.
- Gathers candidates by copying the smallest bucket and bitwise-ANDing it with the other distinct occupied item-code buckets.
- Materializes only set bits back into `GridRecipe` references.
- Ignores duplicate item codes in the same grid while gathering, because repeated stacks of the same item do not narrow the candidate set further.

Why it helped:

The previous method performed repeated reference searches through large arrays. The bitset path turns that into fixed-size word operations and only visits matching set bits at the end.

### Grid Fingerprint Cache

Reason:

Crafting grids can be asked to find a matching recipe multiple times for the same visible state, especially after server inventory updates and UI refreshes.

How it works:

- Computes a hash from slot index, collectible id, stack size, and stack attributes hash.
- Stores the last matched recipe, including the no-match result, in a `ConditionalWeakTable<InventoryCraftingGrid, GridMatchCache>`.
- If the grid hash is unchanged, reuses the cached recipe and regenerates the output stack without re-running the candidate search.

Observed result:

In the recorded sessions, many calls became cache hits:

```text
100 matches | avg 117.11us | max 3271.53us | cache hits 71
500 matches | avg 64.11us | max 3971.63us | cache hits 373
```

### `GridRecipe.Matches()` Prefilter

Reason:

Some systems outside `InventoryCraftingGrid.FindMatchingRecipe()` call `GridRecipe.Matches()` in very large loops. The main observed caller was:

```text
HandbookCache.CraftingOutputConflictSelector.FindMatches
HandbookCache.CraftingOutputConflictSelector.ReapplySelection
InventoryCraftingGrid.DidModifyItemSlot
InventoryNetworkUtil.UpdateFromPacket
GeneralPacketHandler.HandleInventoryUpdate
```

This means a client inventory update can indirectly trigger a large handbook conflict scan.

How it works:

- Harmony-prefixes `GridRecipe.Matches(IPlayer, IWorldAccessor, ItemSlot[], int)`.
- Rejects disabled or unresolved recipes immediately.
- Caches simple metadata per recipe:
  - required ingredient count
  - shaped width and height
  - whether the recipe is shapeless
  - required exact concrete collectible ids
- Builds a thread-local snapshot of the current grid:
  - filled slot count
  - item ids present
  - grid hash
- Rejects recipes before vanilla matching if:
  - the grid has fewer filled slots than the recipe requires
  - the shaped recipe cannot fit in the current grid dimensions
  - a required exact item id is not present in the grid

Observed result:

```text
GridRecipe.Matches prefilter 250000 calls | rejected 248052 (99.2%)
GridRecipe.Matches prefilter 1250000 calls | rejected 1242601 (99.4%)
```

The prefilter does not prove that a recipe matches. It only rejects recipes that cannot possibly match, then lets vanilla `GridRecipe.Matches()` handle the rest.

## Config

Fast Crafting Grid creates this config file:

```text
ModConfig/fastcraftinggrid.json
```

Default settings:

```json
{
  "EnableDiagnostics": false
}
```

Diagnostics are off by default to avoid log spam on busy servers. Set `EnableDiagnostics` to `true` to enable periodic matcher summaries, slow indexed match logs, and `GridRecipe.Matches()` prefilter caller summaries. One-time prewarm and index build notifications still log by default.

## Diagnostics

When diagnostics are enabled, the mod logs indexed matcher summaries every 100 `FindMatchingRecipe` calls:

```text
[fastcraftinggrid] 500 matches | avg 64.11us | max 3971.63us | cache hits 373 | last 1.34us (0 candidates, 0 plausible, cache=True, fallback=False)
```

It logs slow indexed matches over 10ms with timing split by phase:

```text
[fastcraftinggrid] slow indexed match 16.55ms | recipe=... | candidates=1 | plausible=1 | cache=False fallback=False | gather=16430.47us shaped=39.02us shapeless=0.00us output=50.48us
```

It logs global `GridRecipe.Matches()` prefilter volume every 250,000 calls and includes a sampled caller chain:

```text
[fastcraftinggrid] GridRecipe.Matches prefilter 250000 calls | rejected 248052 (99.2%) | caller ...
```

Useful things to watch:

- `slow indexed match` means the indexed path is still taking over 10ms.
- `fallback=True` means the mod deliberately let vanilla matching handle that grid state.
- A high `GridRecipe.Matches prefilter` count with the handbook caller means the remaining work is outside the direct crafting-grid matcher.

## Scope And Limitations

- Universal mod: it can run on both client and server.
- If only one side has it, that side gets its local crafting-grid matching improvement.
- It does not fix inventory desync, ghost items, click prediction, or server/client packet timing directly.
- It does not optimize knapping, smithing, barrel recipes, machine recipes, or handbook page generation outside the observed `GridRecipe.Matches()` prefilter path.
- It relies on the standard resolved grid recipe data. Unusual mods with custom matching side effects may still need the vanilla fallback path.

## Build

```bash
dotnet build FastCraftingGrid.csproj -c Release
```

Release builds run `scripts/package.sh` and write the mod zip to `dist/`.
