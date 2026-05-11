# Vintage Story Modding

A monorepo of [Vintage Story](https://www.vintagestory.at/) mods by **garward**.

## Mods in this repo

| Mod | Description |
|---|---|
| [**VintageKinematics**](VintageKinematics/) | Create-inspired kinetic power, item logistics, and automation. Shafts, cogwheels, belts, funnels, querns, crushers, sieves, friction igniters. Ships a public API for downstream kinetic machines (see [`VintageKinematics/docs/api-tutorial.md`](VintageKinematics/docs/api-tutorial.md)). |
| [**RecipeExplorer**](RecipeExplorer/) | JEI-style recipe browsing. Press U to see recipes that use an item, with auto-fill buttons on handbook pages. Optimized recipe indexing for large modpacks. Client-side. |
| [**NetSpy**](NetSpy/) | Diagnostic mod that dumps the top outbound packet sources every 5 seconds. Server-side only. Install when you need to track down packet floods, uninstall after. |

Each mod folder has its own README with installation, features, and (where applicable) developer-facing docs.

## Work-in-progress mods

Several other mods live on disk under this folder but are gitignored until they're cleaned up for publication: ActionCombat, ButterflyFix, MagicPainting, MeleeRaycastMod, RustboundMagicFix_src, VibrantShaders. They will be un-ignored individually as each one reaches a publishable state.

## Building any mod from source

Each mod is a standalone .NET project. From the mod's folder:

```sh
dotnet build
scripts/package.sh "$PWD" "$PWD/bin/Debug"  # if the mod has one
```

You'll need the .NET 10.0 SDK and a copy of Vintage Story (the `.csproj` files reference `VintagestoryAPI.dll` and the survival mod assemblies from your VS install).

The output zip lands in the mod's `dist/` folder, ready to drop into Vintage Story's `Mods` directory.

## License

All original mod code and assets in this repo are released under the [MIT License](LICENSE).

Bundled third-party assets (textures, sounds, models from Vintage Story or other sources) retain their own licenses. Each mod's `CREDITS.md` lists per-asset attribution.
