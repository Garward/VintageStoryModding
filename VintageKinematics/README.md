# Vintage Kinematics

Create style kinetic power, item logistics, automation, contraptions, and powered tools for [Vintage Story](https://www.vintagestory.at/).

Vintage Kinematics is its own kinetic power system. It has shafts, stress units, ratios, machines, animations, diagnostics, moving contraptions, and automation rules that are separate from vanilla mechanical power. Vanilla windmills and water wheels can still feed VK networks through the bridge, but VK is not a patch on top of vanilla mechanical power.

## Current Scope

Version: `1.3.18`

Side: universal, required on both client and server.

Vintage Story: developed against the 1.22 line using the local game assemblies referenced by `VSPath` or `VINTAGE_STORY`.

Dependencies: none beyond the base game assemblies.

## Gameplay

Power starts with the hand crank, treadwheel, and counterweight drive, then grows into coal motors, flywheel banks, vanilla mechanical power bridges, geothermal bores, and geothermal steam engines. The network tooltip shows RPM, stress demand, stress capacity, overstress, and source conflicts so players can debug builds in game.

Transmission uses shafts, encased shafts, cogwheels, large cogwheels, encased cogwheels, gearboxes, clutches, reversers, gantry shafts, and gantry carriages. Placement previews, animated parts, kinetic sounds, and conflict particles are used where they make machine state easier to read.

Logistics are handled by powered belts, copper and iron funnels, item filters, trashcans, machine face IO, reinforced storage, bulk crates, copper pipes, and pumps. Barrels, mixers, pumps, funnels, and belts are intended to support actual automated production lines rather than isolated machines.

Machines include primitive and kinetic sieves, the kinetic quern, crusher and crusher basin, kinetic sawmill, kinetic extractor, forge press, kinetic bellows, kinetic mixer, charcoal retort, kinetic igniter, kinetic activator, kinetic bore, geothermal bore, and several JSON driven machine templates for downstream mods.

Contraptions use gantry carriages and gantry shafts to move selected blocks as one entity. The Mechanical Binder supports custom multi box selections, removal boxes, live previews, and connected block validation. Contraption drills and saws can work while moving, and carried storage can collect drops.

Tools include the kinetic wrench, powered drill, powered saw, flywheel backpack, reinforced backpack, pogo rod, kinetic boots, and the Mechanical Binder.

## Version 1.3.18 Highlights

The Mechanical Binder was reworked with live previews, custom multi box selections, removal boxes, and connected capture validation. Fluids are ignored by contraption capture, rendering, collision, movement, and restore logic, which fixes broken behavior when contraptions are assembled around water or lava.

Moved kinetic parts now clear stale network and source state when assembled into a contraption, preventing old SU output from lingering visually until the next network update.

Admins now have recovery commands for stuck contraptions: `/vk contraptionset [radius]` force restores nearby contraption entities, while `/vk contraptiondelete [radius]` removes the entities without restoring blocks. Unsafe force restore is no longer exposed through normal player interaction.

The kinetic mixer now has pump friendly dough recipes, and forge press steel timing was adjusted so the blister steel step carries the real work time.

## Installation

1. Build or download `vintagekinematics-X.Y.Z.zip`.
2. Put the zip in the Vintage Story `Mods` folder.
3. Install it on both client and server for multiplayer worlds.

Typical Mods folders:

Linux: `~/.config/VintagestoryData/Mods/`

Windows: `%APPDATA%\VintagestoryData\Mods\`

## In Game Documentation

The mod ships a Vintage Kinematics handbook category with progression, power, machine IO, and contraption guides. Individual handbook entries document machine IO, recipes, power roles, filters, storage behavior, tool use, and special machine rules.

Key guide pages include Kinetics for Dummies, Progression Overview, Crank Era, Forge Press Era, Coal Motor Era, Machine IO, Logic Machines, and Contraptions.

## Configuration

Server config is generated at:

```text
ModConfig/vintagekinematics.json
```

The config covers global consumer speed, generator stress multipliers, per block overrides, vanilla bridge behavior, sieve yields, forge press timing, coal motor fuel usage, modded nugget smelting gates, and kinetic activator target filtering. Existing config files are updated with new default keys at startup.

## Diagnostics

Run `/vk netinfo` as a server control privileged user to list kinetic networks, node counts, source RPM, stress usage, stress capacity, conflict state, and vanilla bridge details.

Contraption recovery commands are also under `/vk` and require the same server control privilege:

```text
/vk contraptionset [radius]
/vk contraptiondelete [radius]
```

## For Mod Developers

Vintage Kinematics exposes API types in the `VintageKinematics.Api` namespace for downstream kinetic blocks and machines.

Useful docs:

[`docs/api-tutorial.md`](docs/api-tutorial.md) explains the JSON first workflow for downstream kinetic machines, including C# extension points only where needed.

[`docs/modeling-guide.md`](docs/modeling-guide.md) covers model setup for kinetic animation, placement, shape rotation, mesh splitting, and contraption friendly models.

[`docs/piston-guide.md`](docs/piston-guide.md) documents piston style animation behavior.

The API includes kinetic block entity behaviors, JSON processors, recipe registries, mesh splitting, multiblock helpers, placement preview hooks, work cycle helpers, kinetic tooltips, inventory pushing, IO face maps, item filters, machine output helpers, crusher processes, sound and animation coordination, contraption moving part registries, contraption work registries, and optional geothermal heat provider integration.

Reference the built `VintageKinematics.dll` from your mod project and add:

```csharp
using VintageKinematics.Api;
```

No source dependency is required for downstream mods.

## Building From Source

Set `VSPath` or `VINTAGE_STORY` so the project can resolve Vintage Story assemblies:

```sh
dotnet build /p:VSPath=/path/to/VintageStory
```

or:

```sh
export VINTAGE_STORY=/path/to/VintageStory
dotnet build
```

Release builds run the package target:

```sh
dotnet build -c Release
```

Manual packaging is also available:

```sh
scripts/package.sh "$PWD" "$PWD/bin/Release"
```

The package script requires `7z` and stages the DLL, `assets/`, `modinfo.json`, `README.md`, `CREDITS.md`, the repository `LICENSE`, and `docs/api-tutorial.md` into `dist/vintagekinematics-X.Y.Z.zip`.

## License

Mod code and original assets are released under the repository [MIT License](../LICENSE).

Bundled third party assets retain their original licenses. See [`CREDITS.md`](CREDITS.md) for attribution.
