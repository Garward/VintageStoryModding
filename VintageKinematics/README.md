# Vintage Kinematics

Create-style kinetic power, item logistics, automation, contraptions, and powered tools for [Vintage Story](https://www.vintagestory.at/).

Vintage Kinematics is a separate kinetic power system from vanilla mechanical power. It has its own shafts, stress units, ratios, machines, animations, and diagnostics. Vanilla windmills and water wheels can still feed VK networks through the built-in bridge, but VK is not a patch on top of the vanilla mechanical-power system.

## Current Scope

- Version: `1.3.8`
- Side: universal, required on both client and server
- Vintage Story: targets the local game assemblies referenced by `VSPath`; currently developed against the 1.22 line
- Dependencies: none beyond the base game assemblies

## Gameplay Features

### Power

- Hand crank for short manual bursts.
- Treadwheel for sustained player-powered rotation.
- Counterweight drive for stored early-game work.
- Coal motor for fueled mid-game power.
- Flywheel and reinforced flywheel banks for charge/release buffering.
- Geothermal bore plus geothermal steam engine for late-game sustained power.
- Creative motor for testing and creative builds.
- Vanilla mechanical-power bridge for stable VK input from wind or water networks.

### Transmission

- Shafts, encased shafts, cogwheels, large cogwheels, encased cogwheels, encased large cogwheels, and gearboxes.
- Clutches and reversers for controllable networks.
- Gantry shafts and gantry carriages for moving builds.
- Placement previews, kinetic tooltips, conflict particles, and animated/sounding machine parts.

### Item Logistics And Storage

- Belts that move item stacks across powered belt lines.
- Copper and iron funnels with whitelist/blacklist filtering.
- Filtered trashcan for disposal lines.
- Machine face IO for direct pushing into inventories, chutes, hoppers, funnels, and moving belts.
- Reinforced chests, double reinforced chests, and bulk crates.

### Machines

- Primitive sieve and kinetic sieve for pannable materials, crushed rock, grit, and dust.
- Kinetic quern for vanilla quern work and grit-to-dust processing.
- Crusher and crusher basin for rock, stone, and ore processing.
- Kinetic sawmill with selectable output modes.
- Kinetic extractor for oils, juices, and solid byproducts.
- Kinetic forge press for hot metal work, dies, plates, bloom refining, nugget smelting, glass melting, and tier-3 lining progression.
- Kinetic bellows for forge press and firepit heat support.
- Kinetic mixer for mortar, plaster, blasting powder, raw refractory mixes, and other dry blends.
- Kinetic charcoal retort, kinetic igniter, and kinetic activator.
- Kinetic bore for heavy 3x3 drilling.
- Geothermal bore for pipe installation down to bedrock/world bottom.

### Contraptions And Tools

- Moving contraption entity support with registered moving-part and work providers.
- Contraption drill and contraption saw tool blocks.
- Kinetic wrench for network and block interactions.
- Powered drill and powered saw tool items.
- Flywheel backpack, reinforced backpack, pogo rod, kinetic boots, mechanical binder, and kinetic alloy/press component progression.

## Installation

1. Build or download `vintagekinematics-X.Y.Z.zip`.
2. Drop the zip into the Vintage Story `Mods` folder.
3. Install it on both client and server for multiplayer worlds.

Typical Mods folders:

- Linux: `~/.config/VintagestoryData/Mods/`
- Windows: `%APPDATA%\VintagestoryData\Mods\`

## In-Game Documentation

The mod ships a Kinematics handbook category with progression pages and IO notes:

- Kinetics for Dummies
- Progression Overview
- Crank Era
- Forge Press Era
- Coal Motor Era
- Machine IO

Individual handbook entries also document machine IO, power roles, recipes, and special behavior for the major blocks and tools.

## Configuration

Server config is generated at:

```text
ModConfig/vintagekinematics.json
```

Important tunables include:

- Global consumer speed and generator stress multipliers.
- Per-consumer and per-generator overrides.
- Vanilla bridge RPM, torque capacity, and smoothing.
- Sieve yield multipliers, vanilla panning-drop use, and per-output yield overrides.
- Primitive sieve and kinetic sieve panning yield multipliers.
- Forge press and coal motor fuel usage speed.
- Opt-in modded nugget smelting gates for the forge press.
- Kinetic activator target blacklist.

Existing config files are updated with new default keys at startup.

## Diagnostics

Run `/vk netinfo` as a server-control privileged user to list kinetic networks, node counts, source RPM, stress usage/capacity, conflict state, and vanilla bridge details.

## For Mod Developers

Vintage Kinematics exposes API types in the `VintageKinematics.Api` namespace for downstream kinetic blocks and machines.

Useful docs:

- [`docs/api-tutorial.md`](docs/api-tutorial.md): walkthrough for a downstream kinetic machine.
- [`docs/modeling-guide.md`](docs/modeling-guide.md): model setup for kinetic animation and mesh splitting.
- [`docs/piston-guide.md`](docs/piston-guide.md): piston-style animation behavior guide.

The API covers:

- Kinetic block entity behaviors: `Kinetic`, `KineticSource`, `KineticWorker`, `KineticAnimator`, `KineticSound`, `KineticPiston`, `KineticStretch`, `KineticLinkedPleat`, `KineticAnimationDriver`, and `KineticMultiblock`.
- Mesh splitting via `KineticMeshSplitter`.
- Multiblock helpers and placement preview hooks.
- Work-cycle helpers, kinetic tooltips, inventory pushing, IO face maps, item filters, machine output helpers, crusher processes, and sound/animation coordination.
- Recipe registries under `assets/<modid>/vkrecipe/` for crusher, sieve, sawmill, forge press, mixer, and extractor recipes.
- Contraption extension points through moving-part and work registries.
- Optional geothermal heat provider integration.

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

Bundled third-party assets retain their original licenses. See [`CREDITS.md`](CREDITS.md) for attribution.
