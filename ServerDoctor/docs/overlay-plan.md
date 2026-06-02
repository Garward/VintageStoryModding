# ServerDoctor Overlay Plan

ServerDoctor is currently server-side only. A future optional client side can show hot block positions reported by the server without making the client required for players who only need the server profiler.

The `blocksoverlay` reference mod uses this client-side pattern:

- Load only on `EnumAppSide.Client` and initialize from `StartClientSide`.
- Register hotkeys with `ICoreClientAPI.Input.RegisterHotKey` and toggle a `HudElement`.
- Build the overlay in `LevelFinalize`, then save config on `LeaveWorld`.
- Keep overlay UI non-focusable and unregister renderers/listeners when closed.
- For through-block block highlighting, register an `IRenderer` in a world render stage, use the engine wireframe shader, disable depth test, disable depth writes, disable culling, render a `MeshRef`, then restore depth/cull state.
- For labels, project world positions with the render API projection/view matrices and render generated text textures in a screen-space render stage.
- Rebuild heavy scan/render meshes off-thread, then upload meshes with `api.Event.EnqueueMainThreadTask`.

Useful shape for ServerDoctor:

- Server records block-position offenders when tick callbacks expose a `BlockPos` or when packet/tick sources can be mapped to a block entity.
- Server sends compact snapshots over a mod channel only to clients that have ServerDoctor installed.
- Optional client keeps the latest offender snapshot, colors blocks by recent tick cost, and renders a through-block wireframe plus labels using the `blocksoverlay` renderer pattern.
- Client GUI should only visualize server-provided diagnostics; profiling remains server-authoritative.
