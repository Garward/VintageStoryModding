# Vintage Kinematics

A Create-inspired kinetic power, item logistics, and automation mod for [Vintage Story](https://www.vintagestory.at/).

Build a network of shafts, cogwheels, and gearboxes; power it with hand cranks or coal motors (or hook into vanilla wind/water); use it to drive querns, crushers, sieves, belts, and more.

> **Important:** Vintage Kinematics is a **completely separate kinetic power system** from vanilla mechanical rotation. It has its own shafts, stress values (SU), gearboxes, and machines designed to feel more polished, consistent, and fun to use.
>
> You can still connect the two systems using the included **Vanilla → VK Bridge** (so your windmills and water wheels can power Kinematics machines). However, Kinematics is not an expansion or replacement of vanilla mechanical power, it's its own thing with an optional compatibility bridge.

## Features

### Power
- **Hand crank** for short bursts of small-network power.
- **Coal motor** as a sustained mid-tier source. Open the firedoor to fuel it.
- **Creative motor** that spins forever, for testing and creative builds.
- **Vanilla bridge**: place a VK shaft against a vanilla wind- or water-driven axle and the rotation flows into the network.

### Transmission
- **Shaft** and **encased shaft** for routing rotation between machines and through walls.
- **Cogwheel** and **large cogwheel** for parallel-shaft coupling and speed step-up/down.
- **Gearbox** for redirecting rotation between perpendicular shafts at a corner.

### Item logistics
- **Belts** that chain together Create-style. Right-click two belts to link them.
- **Funnels** (copper and iron) for sorted item I/O. Open one to set whitelist / blacklist filters; the iron funnel has more filter slots than the copper one.

### Automation
- **Kinetic Quern**: drop grist on top, take flour from the side. Grinds while powered.
- **Crusher + Crusher Basin**: feed crushable items into the basin, the crusher head reduces them to grit.
- **Kinetic Sieve**: an auto-sieve / trommel for crushed rock, dirt, and sand. Pulls vanilla `panningDrops` so any moddable pannable material works.
- **Friction Igniter**: ignites the block in front of it on a 3 to 5 second pulse while powered. Also relights extinguished torches in your hand on right-click. Targets all vanilla "lit by torch" blocks (firepits, bloomeries, charcoal piles, oil lamps, ovens, torches).

## Installation

1. Download the latest `vintagekinematics-X.Y.Z.zip` from the releases.
2. Drop the zip into your Vintage Story `Mods` folder. On Linux this is typically `~/.config/VintagestoryData/Mods/`; on Windows it's `%APPDATA%\VintagestoryData\Mods\`.
3. Launch the game. The mod is required on both client and server, so multiplayer servers must also have it installed.

No configuration files. Defaults aim to feel like a mid-game progression sitting between hand-crafting and the proper electrical mods.

## Requirements

- Vintage Story 1.22.x (universal: client and server).
- No mod dependencies.

## Diagnostics

Run `/vk netinfo` in chat (cheat-mode or admin) to list every kinetic network with node count, source RPM, total stress, MaxRPM, and conflict state. Useful for diagnosing why a network isn't spinning or has stalled.

## For mod developers

Vintage Kinematics exposes a small public API in the `VintageKinematics.Api` namespace, designed to make it cheap to add new kinetic machines as a downstream mod.

A walkthrough of building a "Kinetic Pulper" (block JSON, recipe JSON, ~50 lines of C#) lives at [`docs/api-tutorial.md`](docs/api-tutorial.md). It covers:

- Block-entity behaviors: `Kinetic`, `KineticWorker`, `KineticAnimator`, `KineticSound`, `KineticPiston`, `KineticAnimationDriver`.
- Mesh splitting via `KineticMeshSplitter` so animated and static parts coexist in one block.
- Multiblock machines (`MultiblockHelper`) so adjacency probes resolve filler cells back to their controller.
- What the API handles for you (network membership, stress, tooltips, work cycles, animation, sound dedup, auto-pause on overstress) versus what you still write yourself (recipe matching, inventory wiring, model, textures).

Reference your built `VintageKinematics.dll` from your mod's `.csproj` and `using VintageKinematics.Api;`. No source dependency required.

## Building from source

```sh
dotnet build
scripts/package.sh "$PWD" "$PWD/bin/Debug"
```

The first command produces `bin/Debug/VintageKinematics.dll`. The second stages it alongside `assets/`, `modinfo.json`, `README.md`, `CREDITS.md`, `LICENSE`, and the public `docs/` into a zip at `dist/vintagekinematics-X.Y.Z.zip`, ready to drop into the game's `Mods` folder.

You will need:
- .NET 10.0 SDK
- A copy of Vintage Story; the `.csproj` references `VintagestoryAPI.dll` and the survival mod assemblies from your VS install.

## License

Mod code and original assets are released under the [MIT License](LICENSE).

Bundled third-party assets (textures, sounds, models sourced from Vintage Story or other mods) retain their original licenses. See [`CREDITS.md`](CREDITS.md) for full attribution.

## Credits

By garward. See [`CREDITS.md`](CREDITS.md) for asset attribution.
