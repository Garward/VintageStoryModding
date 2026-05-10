# NetSpy — Vintage Story server packet diagnostic mod

**Status:** approved design, pending implementation
**Target:** Vintage Story 1.22, server-side
**Date:** 2026-05-10

## Problem

A heavily-modded VS 1.22 server is emitting ~33 Mbps outbound to 4 clients (normal is <2 Mbps total). Two specific players experience rubberbanding/ping spikes; one player joining alone pushes outbound to 3 MiB/s. The server logs are silent — no exceptions, no repeated lines — so the flood is at the binary packet layer. We need to know which packet kind, which recipient, and which source object (BlockEntity, Entity, chunk) is responsible, so the offending mod or block can be removed.

## Goal

A drop-in server mod that intercepts outbound packets and prints, every 5 seconds, the top 20 `(recipient, packet kind, source)` tuples by bytes-out, to the server console.

## Non-goals

- Client-side instrumentation, HUD, or chat commands.
- Persistent metrics, file output, or web dashboards.
- Configurability — all knobs are constants.
- Long-term suitability — this mod is diagnostic-only and gets uninstalled once the offender is found.

## Architecture

Single ModSystem, server-side, ~150 LoC.

```
NetSpy/
├── modinfo.json
├── NetSpy.csproj
├── src/
│   ├── NetSpyModSystem.cs       # ModSystem; sets up Harmony patch + 5s tick
│   ├── PacketInspector.cs       # Packet_Server → (kind, source, sizeBytes)
│   └── PacketCounter.cs         # Thread-safe aggregation + snapshot/reset
└── docs/
    └── 2026-05-10-netspy-design.md
```

## Components

### NetSpyModSystem
- `Side = EnumAppSide.Server`
- `StartServerSide(ICoreServerAPI api)`:
  - Construct `Harmony("netspy")`, call `Patch(...)` on the verified send method (see Hook below).
  - Construct `PacketCounter`.
  - Register tick listener: `api.Event.RegisterGameTickListener(_ => DumpAndReset(api), 5000)`.
- `Dispose()`: unpatch all, null out statics.

### PacketInspector
Static class with one entry point:
```csharp
(string kind, string source, int sizeBytes) Inspect(object packetServer)
```

Implementation:
- On first call, reflect `Packet_Server`'s fields once (cached) — list of `(FieldInfo, kindName)`.
- Each call: walk fields in order, find the first non-null one — that's the kind.
- Source descriptor by kind:
  - `BlockEntityMessage` → `"({X},{Y},{Z})"` from packet's position fields
  - `BlockEntities`, `BlockEntitiesUpdate` → if list, format first entry's coords + `"+N more"`
  - `EntityPosition`, `EntityPositions` → entity ID(s); resolve to `entity.Code` via `api.World.GetEntityById` if cheap
  - `EntityAttributes` → entity ID + code
  - `BlockUpdate` → block coords
  - `ChunkColumn`, `MapChunk` → chunk coords
  - everything else → `"-"`
- Size: serialize via `ProtoBuf.Serializer.NonGeneric.Serialize(MemoryStream, packetServer)`, return `stream.Length` cast to int. Wrapped in try/catch returning `0` on failure (so a bad packet shape doesn't kill the prefix).

### PacketCounter
- `ConcurrentDictionary<(string playerUid, string kind, string source), Counter>` where `Counter` holds `long bytes, long count` (use `Interlocked.Add` on the struct's fields, or wrap in `class` and use `lock`).
- `Record(playerUid, kind, source, bytes)` — increments.
- `SnapshotAndReset()` — atomically swap dict, return previous one.

### Hook (the load-bearing assumption)
Method to patch must be discovered before coding. Plan:
1. Open `VintagestoryLib.dll` (under `~/Games/Games/VintageStory/vintagestory/`) in dnSpy or ILSpy.
2. Find the central server→client send method. Best candidates by name:
   - `ServerMain.SendPacket(ConnectedClient, Packet_Server)`
   - `ConnectedClient.SendPacket(Packet_Server, ...)`
   - `NetworkChannel.SendPacket(...)`
3. Confirm it's the funnel point (i.e., entity sync, BE updates, chunk sends all flow through it). If multiple methods are hot paths, patch each.
4. Write `Postfix(ConnectedClient client, Packet_Server packet)` (signature matching the discovered method). The Postfix:
   - Wraps everything in `try/catch` — any throw in a Harmony patch kills the host method.
   - Resolves player UID from `client` (likely `client.Player.PlayerUID` or similar).
   - Calls `PacketInspector.Inspect(packet)` → records into counter.

If `SendPacket` is `internal`, use `AccessTools.Method(typeof(ServerMain), "SendPacket", new[] { typeof(ConnectedClient), typeof(Packet_Server) })`.

### Reporter (in NetSpyModSystem)
Every 5s:
1. `var snap = counter.SnapshotAndReset();`
2. Sum total bytes; if 0, skip.
3. Sort entries by `bytes` desc, take top 20.
4. Log to server console:
   ```
   [NetSpy] === 5s window: {totalMiB} MiB total ===
   [NetSpy]  {bytesPerSec MiB/s} → {playerName}  {kind} @ {source}  count={n}
   ```
5. Resolve `playerName` from UID via `api.World.AllOnlinePlayers` lookup (cache the map per dump).

## Data flow

```
NetworkThread → ServerMain.SendPacket → [Harmony Postfix]
                                          ↓
                                 PacketInspector.Inspect
                                          ↓
                               PacketCounter.Record (atomic)

Server tick (every 5000ms) → DumpAndReset:
  PacketCounter.SnapshotAndReset → sort → log to console
```

## Failure modes & mitigations

| Risk | Mitigation |
|---|---|
| Harmony Postfix throws on unexpected packet shape | All inspection logic in `try/catch`; on failure, record as `(kind="<error>", source="-", bytes=0)`. |
| `SendPacket` signature differs in 1.22 | Verify with dnSpy before coding. If method signature changes, only the patch site changes — the rest is independent. |
| Reflection cost per packet | Field list cached after first packet; per-call cost is one cached `FieldInfo[]` walk + one ProtoBuf serialize. |
| ProtoBuf serialize doubles per-packet work | Acceptable: this is diagnostic-only, runs while investigating, gets uninstalled after. |
| Multi-threaded `SendPacket` calls race the dict | `ConcurrentDictionary` with `AddOrUpdate`; counter increments use `Interlocked` or `lock` on counter object. |
| Reporter dumps while patch is recording | Snapshot-and-replace pattern: build new empty dict, swap reference, work on the snapshot — patch path only ever sees the live dict. |

## Build & deploy

- `.csproj` references `VintagestoryAPI.dll`, `VintagestoryLib.dll`, `0Harmony.dll`, `protobuf-net.dll` — all from `~/Games/Games/VintageStory/vintagestory/`.
- `dotnet build -c Release` produces `NetSpy.dll`.
- Package: zip containing `NetSpy.dll` + `modinfo.json` at root.
- Deploy: upload zip via Pterodactyl panel to `mods/` directory; restart.

## Verification

After install:
1. Restart server, watch console.
2. Within 5 seconds of first player activity, `[NetSpy] === 5s window` lines appear.
3. Have a baseline player (no flood) connect — top entries should be small (<100 KB/5s for normal entity/BE traffic).
4. Have Aggerrath connect — expect top entry to jump to multi-MiB. The `kind` and `source` fields name the culprit.

## What we expect to find

Given the symptoms (silent flood, targeted at one player, recent onset, sustained ~3 MiB/s per affected client):
- **Most likely:** a `BlockEntityMessage` flood from one specific BE position — points to a bugged BlockEntity (likely from `electricalprogressive*`, `signals`, or a recent placement near Aggerrath's load radius).
- **Second:** an `EntityPosition` flood from one entity ID — points to a stuck/oscillating entity (mob, dragon, custom player model).
- **Third:** continuous `ChunkColumn` re-sends — would point to a chunk-cache bug.

The output names the offender directly; remediation (remove that mod / break that block / kill that entity) is then trivial.
