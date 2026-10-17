# Crafting slowdown and prediction investigation, 2026-10-17

Two players on a heavily modded Vintage Story 1.22.7 dedicated server reported
using Fast Crafting Grid 1.0.4 on their clients. The server operator confirmed
those versions; client binaries/logs were not available for independent checking.
Fast Crafting Grid was absent from the server's installed mod archives.

This report supplies sanitized observations and a separately reproduced cache
correctness fix. It does **not** establish that Fast Crafting Grid generated the
original request burst or that the proposed cache fix resolves that incident.
No intentional automation or successful copper duplication was established.

## Environment and provenance

- Game/server: Vintage Story 1.22.7, .NET 10, Linux dedicated server.
- Client mod: Fast Crafting Grid 1.0.4, reported for both affected players.
- Public release inspected: https://mods.vintagestory.at/show/mod/54768
  (`download/104336/fastcraftinggrid-1.0.4.zip`). Release includes MIT license.
- Upstream inspected: https://github.com/Garward/VintageStoryModding,
  base commit `1d67330f9d3ab0ce59b05b1fde85e3fc1067b504`.
- `server-mods.json` contains 159 successfully decoded installed archive mod
  IDs/versions, captured during this investigation. It excludes built-in game
  modules and standalone DLL metadata, and is not a client mod list. The custom
  `prospercraftguard` mod was added after the original stall.
- Original evidence retained privately: server audit, two live main-thread
  snapshots, ADS block/container logger, four historical player backups,
  post-incident saved inventories and surrounding world records.
- Public excerpts replace player names and inventory identifiers. Account UIDs,
  IPs, world/base coordinates, credentials and unrelated chat are omitted.

## Observed timeline (UTC)

### Original incident: Player A, before the guard existed

Between 16:47:14 and 17:00:12, the retained audit contains **2,881 copper-rod move
requests**: **2,496 requesting zero** and **385 requesting one**. The peak is ten
logged rod requests in one wall-clock second. These are only rod move log entries,
not a full packet capture; audit times may reflect delayed backlog processing.
The quantity is the *client-requested quantity*, not a measured successful move.

`rod-request-summary.json` gives the full per-second distribution;
`rod-request-samples.txt` gives initial/final sanitized entries. Zero-quantity
native operations were independently tested offline: no item moves, while
`MovedQuantity == RequestedQuantity` can still report success for 0 == 0.

Two live main-thread snapshots showed recipe lookup while handling inventory
move packets. See `first-main-thread.txt` and `second-main-thread.txt`. The first
was in `ListExtensions.AddIfNotPresent`; the second was in recipe matching via a
Harmony-patched `GridRecipe.Matches`. A patched frame alone does not identify the
patch owner. Neither snapshot proves an infinite loop or malicious intent.

Manual ingredient placements matched a fruit-press layout: hammer, copper rod,
raw fat, chisel, ordinary oak boards and resin. The native oak recipe requires
rendered fat or beeswax. An installed VanillaVarnished variant accepts raw fat
but requires varnished planks. This is evidence of an ingredient mismatch, not a
complete enumeration of every modded recipe or proof of a client matching defect.

`material-balance.json` records combined player and surrounding-area counts at
16:00 and the 17:00:17 save, based on 48 chunks per save plus player inventories.
Independent inventories/world accounting found one copper rod both before and
after, with unchanged checked ingot/nugget/bit/plate/chute stocks. Inputs remained
and no fruit press was recorded. This was not evidence of copper duplication.

### After restart: two processing-budget notices

The server then had a custom protection that measured cumulative handler **wall
time**, initially 1,000 ms per five seconds, and temporarily paused crafting for
15 seconds with player/admin notices. It also independently rejected
non-positive moves and bounded repeated no-change attempts and request rate.

- **17:45:28 Player A:** processing-budget notice during successful manual
  ingredient placement; the log does not show a rate/failure violation here.
- **17:45:31-34 Player B:** picks up an iron saw, puts it in the grid, then adds a
  stack of 64 oak logs.
- **17:45:36-37 Player B:** ordinary shift-click on crafting output produces oak
  boards (native audit lists 12 boards per recipe and twelve 64-board destination
  stacks), followed by the processing-budget notice.
- **17:46:** Player B reported a ghost saw in chat. This report is a user symptom,
  not an independently captured client/server state comparison. A guard rejection
  of client prediction may also contribute to desynchronization in this phase.
- **17:50:58 Player A:** another processing-budget notice during ingredient edits.
- **17:52:03 Player A:** a further processing-budget notice (see `guard-notices.txt`).
- **17:53:00:** production budget raised from 1,000 to 5,000 ms per five seconds
  and live-reloaded; other defenses retained. The old limit was too strict for
  observed legitimate crafting. Wall time can include GC/scheduling delays.

These later alerts establish expensive processing, not cheating, automation, or
a recurrence of the original zero-quantity burst. A stock server without this
custom guard will not produce these specific notices.

## Reproduced source defects and proposed fix

`FindMatchingRecipePatch.ComputeGridHash` originally hashes slot position,
collectible numeric ID, stack size and attribute hash, but omits collectible class.
Item and block IDs come from separate registries. Replacing an item by a block
with the same numeric ID, quantity and attributes can therefore reuse a cached
positive or negative result even though the ingredient changed.

On a positive cache hit, `ApplyCachedMatch` originally generates output directly
without checking `Enabled` or calling `Matches`. Native `GridRecipe.Matches` also
runs player recipe-matching events. Their result can change without a grid hash
change. Replaying an unvalidated cached recipe can predict stale output.

The patch:

1. Includes `stack.Class` in the grid hash.
2. Revalidates a positive cached recipe through `Enabled` and `Matches` with the
   current player, world and grid. If invalid, resumes candidate selection and
   existing vanilla fallback instead of generating stale output.
3. Adds isolated regression checks using the actual production matcher/cache and
   native game inventory, stacks and output generator, without a live server.

Positive cache hits now run one recipe match, avoiding a complete recipe scan.
Negative cache behavior, arbitrary hash collisions affecting negative results,
recipe-priority changes and all custom dynamic matching semantics are not fully
solved here. This is a bounded correctness improvement, not an inventory protocol
or packet retry fix. Full modpack compatibility and performance remain to test.

## Validation

Run `FastCraftingGrid.Tests` as documented in its README with game 1.22.7 DLLs.
The original implementation passes six of ten checks and fails four.
The reproduction checks fail on the original implementation for item/block hash
aliasing, stale positive reuse, missing context revalidation, and disabled recipe
reuse. The patched implementation passes all ten checks, including normal cached
output generation and unchanged input quantities. Minimal interface proxies and
a collectible callback stub avoid requiring a running world; no real multiplayer
reproduction, client log comparison or full modpack performance test is claimed.

## Other possible fixes and the next experiment

These are separate investigations; they are not silently included in the cache
patch or deployed to the production server.

- **Profile the server recipe path.** A client-only speedup leaves the server's
  expensive native recipe scan unchanged. Test this universal mod on both sides
  in an isolated copy, after checking compatibility with installed matching hooks
  (including xSkills), rather than assuming client performance fixes server cost.
- **Reject non-positive transfers before recipe recalculation.** The server guard
  already does this for crafting move packets. A native/game or appropriate
  networking-layer fix should avoid emitting or processing zero-quantity moves.
  Preserve legitimate mouse actions and server inventory correction behavior.
- **Bound no-progress retries.** Instrument handbook/autofill and inventory
  prediction paths to locate the actual request producer. Stop when no item can
  move, wait for authoritative state, and back off/cancel stale attempts. The
  reviewed Fast Crafting Grid move/activate patches only record diagnostics;
  they do not send these packets, so their presence alone does not identify the
  producer. Avoid adding an unrelated packet hook to this optimization mod on
  the strength of this incident alone.
- **Compare recipe decisions.** On a test copy, enable this mod's existing
  `ModConfig/fastcraftinggrid.json` `EnableDiagnostics` on both sides. Record exact
  inputs/attributes, selected recipe identifiers, index readiness and fallback
  flags, requested versus moved quantities, and before/after server state.
  Capture the full client mod lists too, especially inventory/handbook mods.
- **A/B reproduction.** Repeat the same fruit-press recipe with raw fat and then
  rendered fat; test ordinary saw/log bulk crafting. Compare stock client, client
  with only Fast Crafting Grid, then the full client pack; independently compare
  server optimization absent/present. Test initially without the custom guard in
  an isolated environment, then with it, to distinguish mod behavior from guard
  prediction rejection. Include output selection and reconnect/resync tests.
- **Cache/index follow-up.** Consider snapshot equality rather than hash-only
  negative reuse, index generation invalidation, and dynamic matching-hook
  compatibility. Do not assume raw-fat versus rendered-fat items share an ID;
  the demonstrated item/block alias is an independent defect.

No live exploit or synthetic flood was sent, and this upstream patch was not
installed on the production server during this review.
