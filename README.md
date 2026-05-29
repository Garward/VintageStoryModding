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
| [**NetSpy**](NetSpy/) | Server | Diagnostic mod that reports the top outbound packet sources every 5 seconds. Install only while investigating packet floods. |
| [**ActiveFarming**](ActiveFarming/) | Universal | Linked project for compressed farming timescales, tending bonuses, and configurable growth speed. |

## One-Off Fixes

| Project | Side | Description |
|---|---:|---|
| [**ClockmakerCivilFix**](OneoffFixes/ClockmakerCivilFix/) | Server | Content patch that removes the Civil trait from the GloomeClasses Clockmaker class. |
| [**InterestingMeFix**](OneoffFixes/InterestingMeFix/) | Server | Restores ore blocks if Interesting Mining's muck spawning fails, preventing ore voiding. |
| [**InterestingMeFixClient**](OneoffFixes/InterestingMeFixClient/) | Client | Optional client companion that forwards right-clicks on bugged Interesting Mining muck piles so the server-side fix can repair them. |

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

Most projects also import an optional `Directory.Build.local.props` file for machine-local paths. Keep those files out of portable setup instructions.

## Packaging And Deployment

From a project folder:

```sh
dotnet build
dotnet build -c Release
```

Release builds package projects that define a packaging target, usually into `dist/`. `VintageKinematics` also has `scripts/package.sh` for explicit packaging:

```sh
cd VintageKinematics
dotnet build -c Release
scripts/package.sh "$PWD" "$PWD/bin/Release"
```

Several projects support direct deployment when `VINTAGE_STORY_MODS`, `VINTAGE_STORY_MOD_OUTPUT`, or `ModOutputPath` is set. In that mode `dotnet build` copies the built DLL, `modinfo.json`, and assets into the configured Mods folder.

## Repository Notes

- `Sources/` and `tmp/` are scratch/reference directories and are intentionally ignored.
- `bin/`, `obj/`, `dist/`, and packaged zips are build outputs.
- Each mod owns its own `modinfo.json`, source tree, assets, and optional packaging behavior.

## License

Original code and assets in this repository are released under the [MIT License](LICENSE), unless a subproject states otherwise.

Bundled third-party assets, Vintage Story-derived files, or compatibility references retain their own licenses. Check each mod's credits or asset notes when publishing a package.
