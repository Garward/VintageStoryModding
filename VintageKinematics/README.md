# Vintage Kinematics

Create style kinetic power, item logistics, automation, contraptions, and powered tools for [Vintage Story](https://www.vintagestory.at/).

Vintage Kinematics is its own kinetic power system. It has shafts, stress units, ratios, machines, animations, diagnostics, moving contraptions, and automation rules that are separate from vanilla mechanical power. Vanilla windmills and water wheels can still feed VK networks through the bridge, but VK is not a patch on top of vanilla mechanical power.

## Current Scope

Version: `1.4.2`

Side: universal, required on both client and server.

Vintage Story: developed against the 1.22 line using the local game assemblies referenced by `VSPath` or `VINTAGE_STORY`.

Dependencies: none beyond the base game assemblies.

## Gameplay

Power starts with the hand crank, treadwheel, and counterweight drive, then grows into coal motors, flywheel banks, vanilla mechanical power bridges, geothermal bores, and geothermal steam engines. The network tooltip shows RPM, stress demand, stress capacity, overstress, and source conflicts so players can debug builds in game.

Transmission uses shafts, encased shafts, cogwheels, large cogwheels, encased cogwheels, gearboxes, clutches, reversers, gantry shafts, and gantry carriages. Placement previews, animated parts, kinetic sounds, and conflict particles are used where they make machine state easier to read.

Logistics are handled by powered belts, copper and iron funnels, item filters, trashcans, machine face IO, reinforced storage, bulk crates, the Kinetic Warehouse, copper pipes, and pumps. Barrels, mixers, pumps, funnels, belts, and warehouse ports are intended to support actual automated production lines rather than isolated machines.

Machines include primitive and kinetic sieves, the kinetic quern, crusher and crusher basin, kinetic sawmill, kinetic extractor, forge press, kinetic bellows, kinetic mixer, charcoal retort, kinetic igniter, kinetic activator, kinetic bore, geothermal bore, and several JSON driven machine templates for downstream mods.

Contraptions use gantry carriages and gantry shafts to move selected blocks as one entity. The Mechanical Binder supports custom multi box selections, removal boxes, live previews, and connected block validation. Contraption drills and saws can work while moving, and carried storage can collect drops.

Tools include the kinetic wrench, powered drill, powered saw, flywheel backpack, reinforced backpack, pogo rod, kinetic boots, and the Mechanical Binder.

## Version 1.4.2 Highlights

Contraption drills and saws can now run as stationary machines with kinetic power connected behind the tool. Their moving parts and rear input shafts animate from actual network RPM. Completed drops feed storage directly below when possible, which avoids loose-item buildup, and remaining items fall consistently from the center beneath the machine.

Stationary and moving drills now handle regenerated blocks consistently and notify nearby fluids after mining, allowing vanilla basalt generators to refill normally. Saw targeting starts at the block touching the blade instead of searching for unrelated wood nearby. Tree cutting now uses the same tree-group traversal as a vanilla axe, including connected branches and leaves, with faster leaf-aware work timing at high RPM.

Gantry contraptions can now contain controllers at both ends and transfer control to whichever anchor can move. The inactive anchor ignores movement commands until the contraption is placed again. Assembly also received a safer visual handoff that prevents flashing during conversion, interrupted handoffs recover according to whichever copy survived, single-carriage movement works normally, and connected gantry shafts now visibly rotate with their kinetic network.

The kinetic quern now limits excessive client animation updates while retaining the same processing throughput. Pumps distinguish renewable water from finite world liquids, so water remains an infinite source while lava does not. Full lava sources can be collected as atomic 10 litre transfers only when a connected 100 litre Iron Fluid Tank has room for the complete source. Stored water, salt water, and lava can also be pumped into supported world cells in 10 litre batches. When aimed at an existing source, the pump expands horizontally across the supported layer to fill enclosed pools.

## Version 1.4.0 Highlights

This release adds the Kinetic Warehouse, a scalable shared storage system built from one searchable terminal, connected capacity cells, a kinetic drive port, and filtered belt input and output ports. Wooden cells provide inexpensive early capacity, while reinforced cells store four times as many items in the same space.

The terminal supports search, sorting, a resizable item grid, item tooltips, carried-stack deposits, single-item withdrawal, full-stack transfer, and inventory Shift-click deposits. Client prediction keeps normal interaction responsive while the server remains authoritative and corrects rejected actions.

Warehouse cells automatically reshape into one readable structure with exterior framing and open internal connections. Structures can be expanded after reloading, and parts can be removed whenever the remaining cells still have enough capacity. Placement and removal guards prevent two terminals from claiming the same cell, accidental overflow, contraption capture, wrench pickup, or automated block replacement from compromising stored items.

Storage changes use server-authoritative transactions and mirrored, checksummed recovery records. Startup reconciliation handles interrupted saves, chunk load order, missing terminals, stale copies, and older or damaged records conservatively. Empty portable containers can be stored, while containers holding items or liquid, heated items, and transitioning perishables are rejected.

Warehouse item transfers require kinetic power by default. Power demand scales gradually with physical cell count, and the drive port operates from 16 RPM. Server owners can disable the power requirement, adjust its demand and minimum RPM, or set a maximum number of distinct stored item types.

Belt and funnel automation now supports direction-aware warehouse inputs. The shared connection scanner used by warehouse cells also replaces duplicated copper-pipe connection logic, and the build/package pipeline now produces release archives consistently across the repository.

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

The config covers global consumer speed, generator stress multipliers, per block overrides, vanilla bridge behavior, sieve yields, forge press timing, coal motor fuel usage, modded nugget smelting gates, kinetic activator target filtering, and Kinetic Warehouse power and type limits. Existing config files are updated with new default keys at startup.

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
