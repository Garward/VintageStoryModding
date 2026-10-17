# Fast Crafting Grid regression checks

Run from the repository root with the .NET 10 SDK and a Vintage Story 1.22.7 installation:

```sh
dotnet run --project FastCraftingGrid.Tests -c Release \
  -p:VSPath=/path/to/vintagestory -p:PackageOnRelease=false -p:DeployToVS=false
```

The checks use the real game inventory, stack, output generator, and this mod's
matcher/cache. Reflection reaches private implementation so no production test
API is added. A recipe subclass models changing player/world matching context;
minimal interface proxies avoid starting a game or connecting to a server.
No game DLLs are included. These checks do not replace an in-game multiplayer
compatibility or performance test with the full affected modpack.
