# Vintage Story Modding

A monorepo of [Vintage Story](https://www.vintagestory.at/) mods by **garward**.

Most projects are standalone .NET mod projects. Some are full gameplay or client mods; others are narrowly scoped compatibility or diagnostic fixes kept here because they share the same Vintage Story modding workflow.

## Projects

| Project | Side | Description |
|---|---:|---|
| [**VintageKinematics**](VintageKinematics/) | Universal | Create-style kinetic power, belts, item logistics, powered machines, contraptions, geothermal power, powered tools, and a public kinetic API. |
| [**BetterHandbook**](BetterHandbook/) | Client | Faster handbook opening and scrolling, item usage lookup, mod-category tabs, bookmarks, recipe overlays, and auto-fill helpers. |
| [**VibrantShaders**](VibrantShaders/) | Client | Configurable shader and post-processing tweaks for vibrance, bloom, tonemapping, haze, vignette, color balance, seasonal tinting, and colormap behavior. |
| [**ClientFixes**](ClientFixes/) | Client | Configurable client-side sanity fixes and quality-of-life patches. |
| [**CropGrowthRate**](CropGrowthRate/) | Server | Server-side global crop growth speed multiplier with per-crop overrides. No client install required. |
| [**ServerDoctor**](ServerDoctor/) | Universal | Diagnostic mod for packet inspection, tick profiling, and in-game server health overlays. Install only while investigating server issues. |
| [**ActiveFarming**](ActiveFarming/) | Universal | Gitlink project for compressed farming timescales, tending bonuses, and configurable growth speed. |

## One-Off Fixes

| Project | Side | Description |
|---|---:|---|
| [**ClockmakerCivilFix**](OneoffFixes/ClockmakerCivilFix/) | Server | Content patch that removes the Civil trait from the GloomeClasses Clockmaker class. |
| [**HerbariumBerryBushFix**](OneoffFixes/HerbariumBerryBushFix/) | Universal | Compatibility patch for Herbarium Fixed Mojo tall berry bush growth data and pie filling stacks. |
| [**InterestingMeFix**](OneoffFixes/InterestingMeFix/) | Server | Restores ore blocks if Interesting Mining's muck spawning fails, preventing ore voiding. |
| [**InterestingMeFixClient**](OneoffFixes/InterestingMeFixClient/) | Client | Optional client companion that forwards right-clicks on bugged Interesting Mining muck piles so the server-side fix can repair them. |
| [**ItemSyncProbe**](OneoffFixes/ItemSyncProbe/) | Universal | Log-only probe for inventory client prediction and server confirmation behavior. |
| [**SmithingPlusTooltipFix**](OneoffFixes/SmithingPlusTooltipFix/) | Client | Client-side guard for SmithingPlus repair tooltip nulls in item and handbook descriptions. |
| [**StorageControllerClientFix**](OneoffFixes/StorageControllerClientFix/) | Client | Client-only performance patch that skips expensive empty-search filtering in Storage Controller GUIs. |

Additional local or experimental mods may live under this directory but remain gitignored or unpublished until they are cleaned up.

## Build Setup

Install the .NET 10.0 SDK and keep a local Vintage Story install available. Projects resolve Vintage Story assemblies from `VSPath`, which can be supplied in one of these ways:

```sh
dotnet build /p:VSPath=/path/to/VintageStory
```

or:

```sh
export VINTAGE_STORY=/path/to/VintageStory
dotnet build
```

Projects import optional root-level or project-local `Directory.Build.local.props` files for machine-local paths. Keep those files out of portable setup instructions.

For a persistent local setup, create an ignored `Directory.Build.local.props` at the repository root:

```xml
<Project>
  <PropertyGroup>
    <VSPath>/path/to/VintageStory</VSPath>
  </PropertyGroup>
</Project>
```

If `VSPath` and `VINTAGE_STORY` are both unset, the shared build will bootstrap the required Vintage Story server DLLs into `tmp/vs-dlls/<version>/vintagestory` before resolving references. The shared default is intentionally pinned in `Directory.Build.props`; bump `VintageStoryVersion` when this repo moves to a new game release:

```sh
dotnet build -c Release
```

You can also populate that cache explicitly:

```sh
scripts/bootstrap-vs-dlls.sh --version 1.22.7
```

To build against a different game version temporarily, pass:

```sh
dotnet build -c Release /p:VintageStoryVersion=1.22.7
```

The `tmp/` directory is ignored, so downloaded game DLLs and cached archives are not tracked.

`ActiveFarming` is stored as a gitlink rather than a normal directory in this repository. If it is empty after cloning, initialize submodules or populate that checkout before building it.

## Packaging And Deployment

From a project folder:

```sh
dotnet build
dotnet build -c Release
```

Release builds package mods with `modinfo.json` through the shared root packager at `scripts/package.py`. The zip is written to the project's `dist/` directory:

```sh
dotnet build -c Release
```

Projects can override the output zip name with `PackageFileName`; otherwise the package name is derived from `modinfo.json`.

Several projects support direct deployment when `VINTAGE_STORY_MODS`, `VINTAGE_STORY_MOD_OUTPUT`, or `ModOutputPath` is set. In that mode `dotnet build` copies the built DLL, `modinfo.json`, and assets into the configured Mods folder.

## Repository Notes

- `Sources/` and `tmp/` are scratch/reference directories and are intentionally ignored.
- `bin/`, `obj/`, `dist/`, and packaged zips are build outputs.
- Each mod owns its own `modinfo.json`, source tree, assets, and optional package filename, while the shared build files own Vintage Story reference resolution and Release packaging.

## License

Original code and assets in this repository are released under the [MIT License](LICENSE), unless a subproject states otherwise.

Bundled third-party assets, Vintage Story-derived files, or compatibility references retain their own licenses. Check each mod's credits or asset notes when publishing a package.
