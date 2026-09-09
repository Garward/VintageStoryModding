# Player item custody audit

Task 3 implementation audit, 2026-09-09. Native source reference is the locally
decompiled installed engine under `VanillaSource/decompiled`. This audit is a
coverage ledger, **not approval to activate the full native profile**.

## Implemented authority and boundaries

`WorldScopedPlayerItemArchiveStorage` stores both existing checksum slots inside
one world-bound envelope. A receipt and its canonical store snapshot share an
archive generation. Linux file flush, atomic same-directory rename, and directory
fsync complete before an external adapter is invoked. Missing/damaged envelopes,
world identity/lineage mismatch, and recovery from an older corrupt generation
refuse custody activation. Other platforms have no durable capability yet.

The prior `SaveGame.StoreData` adapter is buffered: `VintagestoryLib/SaveGame.cs`
399–407 only updates `ModData`. `ServerSystemLoadAndSaveGame.cs` 45–74 writes the
savegame later, after chunk saves. It is retained only as the initial legacy
source and world-checkpoint carrier, not an external-effect durability barrier.

The initial sidecar imports both legacy slot byte strings unchanged and binds a
hash of those strings plus the current checkpoint. Each world-save event first
flushes a new checkpoint into the sidecar, then places its token in engine
moddata. A crash between those steps refuses the next load. It requires explicit
recovery; it is not an automatically repaired restart. Back up and restore the
world **and the `VRPG/player-items/<world hash>/` sidecar directory together**.
Never delete a sidecar to force a legacy reimport. No live import was run here.

## Pickup

`EntityBehaviorCollectEntities.OnFoundCollectible` is intercepted before
`EntityPlayer.TryGiveItemStack` and before any inventory slot selection. The old
fresh-UUID `TryGiveItemstack` canonical insertion has been removed.

Enabled adapter subset: exact `EntityItem` sources with a loaded birth token or
validated prepublication identity, unchanged quantity/exact payload, and the
base no-op `CollectibleObject.OnCollected` implementation. Capacity preflight
refuses without touching the source. The durable claim precedes source removal;
the same receipt acts as a tombstone when the engine restores that source.
Suppression requires the token, quantity and exact payload to match. Tombstones
also protect a source approached by a noncanonical collector. A save failure
quarantines the source at this boundary until the claim can be persisted.

The adapter does not emit Vanilla `onitemcollected` or replay unknown collection
callbacks; those event consumers require a separate reviewed integration.
Custom collectible collection callbacks and non-EntityItem collectables are
refused before `OnCollected` runs. Their source remains in the world.

Task 3b1 adds a prefix at the entry to `ServerMain.SpawnEntity_internal`, before
`EntityItem.Initialize`, `AfterInitialized(true)`, chunk publication, loaded-entity
registration/collision repair, and the ordinary spawn callbacks. The observed
1.22.7 engine method bodies and module IDs are pinned in `EngineItemFlowPatch.Birth`;
changed or missing methods, unexpected overrides, and conflicting Harmony patches
refuse fresh readiness. There is no fallback to minting identity at a spawn event.
Initialization can serialize from callbacks; the token and proof are already present.

Only previously unobserved, uninitialized ordinary `EntityItem` instances with
ID zero, no existing identity attributes, and `ExactSnapshot` content receive
`v1:<random GUID>` tokens. ID reassignment later does not affect identity. A watched
format marker and SHA-256 digest bind that token to the complete canonical encoded
stack, including quantity. Native `Entity.ToBytes` writes these watched attributes;
`EntityItem.FromBytes` restores them. New tokens require their proof even after
reload, so erasing both proof attributes cannot downgrade them into legacy evidence.
Other mods' entity attributes are preserved. This prefix is **not a disk flush**:
it establishes that subsequent native source snapshots contain the same identity.
The existing crash-durable Prepared claim still precedes removal and credit.

Fresh evidence also holds a live hook/observer lease. Unpatch/reset, module disposal,
conflicting patches, and observer failures refuse fresh pickup. On a real disk load,
valid versioned proof becomes loaded evidence; it no longer depends on that prior
process's lease. An older bare GUID without proof attributes retains the Task 3a
loaded-only snapshot contract (including existing frozen-recovery classification).
Contradictory versioned proof refuses. Spawn/load observers never replace a known
token, upgrade an in-memory event to loaded evidence, or refresh a changed snapshot.
The pickup slot must reference the watched stack which serialization will save.

Native mutation audit: `ServerMain.SpawnItemEntity` constructs and spawns a new
entity; `EntityItem.OnGameTick`/base `CollectibleObject.OnGroundIdle` do not implement
automatic world-item merging in the inspected engine. `EntityItemSlot` inherits
ordinary slot mutation; native `ItemSlot.TryPutInto`/`CollectibleObject.TryMergeStacks`
can change both donor and recipient quantities. A real partial merge test confirms
both versioned sources refuse pickup before and after save/load. Proof is never
renewed for the merged quantity. A changed source stays in world custody; merge
support needs a separate durable source-lineage adapter. Temperature, transitions,
and other codec-detected evolving contents cannot obtain/use fresh proof, including
after reload. Arbitrary mod-specific callbacks/evolution remain outside this audit.

## Producers and native slot writes

- Hotbar, offhand, mouse cursor, crafting, character/equipment and backpack:
  legacy migration scans all managed inventories and restores exact snapshots.
  Their ordinary runtime clicks, swaps, hotkeys and inventory packets still use
  Vanilla `ItemSlot.TryPutInto`, `TakeOut`, and direct `Itemstack` assignment.
  Task 3b2 now refuses the audited server packet routes before mutation for
  canonical/recovery custody (details below). Task 3b4 also guards the two native
  inventory background ticks, and Task 3b5 guards packets 3, 22 and 31 at their
  native world-effect entrypoints. Direct producers and other native routes remain
  unguarded; Task 8 must prevent their exposure or install producer-level
  guards before enabling the profile.
- Direct engine grants: `PlayerInventoryManager.TryGiveItemstack` 184 onward
  wraps a dummy slot and calls `TryTransferAway`. It does not identify a source
  and is not a custody adapter. Vanilla behavior remains before activation.
- Signs: `BlockEntitySign` 149/158/170 ignores grant rejection. Labeled chests:
  `BlockEntityLabeledChest` 174/187/197 does likewise. Ground storage
  `TryTakeItem` ignores the `TryGiveItemstack` result, but spawns any remaining
  `stack.StackSize > 0` back into the world; it is not an unconditional source
  deletion. A manager `false` return is **not a canonical receipt or adapter**
  for these producers. Their named adapters remain absent, so the exposed native
  profile must stay unavailable.
- Containers and shift-click: manager `TryTransferAway` calls the source slot's
  `TryPutInto`; external slot sources can bypass `TryGiveItemstack`. No external
  container/trader adapter is registered. Producer guards/allowlists are pending.
- Trade: Vanilla trade inventories retain their own payment/output flows. No
  canonical trading or economy is introduced. Trade blocks full activation.
- Commands, rewards and admin grants: only explicitly registered VRPG generator
  codes can call `RegisterClaim` with a stable, globally unique operation ID.
  Prepared/Completed transfers ownership to a durable claim; capacity failures
  leave that claim claimable. ReconcileRequired means the producer must retain a
  pending claim with the same ID and must not issue a new grant. Arbitrary Vanilla
  commands/admin producers are not intercepted and block exposed activation.
- Held/use/drop/Home placement: the coordinator accepts only a registered named
  adapter. None of these native effect adapters is registered by default.
  Unknown/partial results retain the complete reservation; no network retry
  replays an effect. Unsupported coordinator requests refuse before reservation
  or materialization. Task 3b2 refuses native hand/tool packets, Task 3b4 refuses
  native inventory background ticks, and Task 3b5 refuses generic native packet
  3/22/31 world-item effects. Other effect routes and named adapters remain
  pending; this is not complete held/use or producer coverage.
- Death: engine `ServerPlayerInventoryManager.DropItem` and inventory death
  behavior still own Vanilla slot drops. The persistence module reconciles known
  canonical receipts on player death; a complete native death policy/guard is
  pending. Reserved canonical entries remain unavailable to a second consume.
- VRPG rift charts: `TemporalRiftService` calls `slot.TakeOut(1)` directly. This
  is another producer requiring a canonical use adapter before native exposure.

`PlayerItemPersistenceModule.CanActivateNativeProfile` requires every named
exposed route in its allowlist to have a live readiness/producer guard, as well
as loaded durable storage and the installed pickup hook. No blanket readiness is
registered in this slice. Task 3b1 registers persisted/fresh pickup entries; Task
3b2 adds only `native:packets` and `native:cursor-securing`; Task 3b4 adds the exact
`native:OnUsingTick` and `native:UpdateTransitionStates` boundaries. Task 3b5 adds
only `native:packet3-block-place-break`, `native:packet22-block-entity`, and
`native:packet31-entity`. None registers whole slot/held/use, producer, Home,
trade, container or death coverage. Other named routes still block activation. Task 8 must use this gate in the custody activation
protocol; the current task does not remove either Vanilla menu.

## Outbound receipt contract and remaining integration

Adapters synchronously report actual consumed and exactly returned units plus
their external evidence. Returned means the exact original payload is no longer
owned externally. Altered payloads, mixed consumed/returned stacks, and unknown
effects remain `ReconcileRequired`; the whole reservation stays locked. A
registered reconciler may settle the external side, then report a complete
all-consumed or all-returned result. It must never infer absence from a lost
network response. No unsafe partial canonical refund is implemented.

The coordinator supplies idempotent retry and `Reconcile(playerUid)` entry points.
Startup, disconnect, death and active-slot swap are wired. Task 6/8 must call
reconcile before cancel/realm transitions and block transitions while unknown
effects remain. Concrete held/use/drop/external/Home adapters, producer guards,
mutated world-source support, and automatic recovery of ambiguous world checkpoints remain open.

Codec inspection freezes declared transitions, persisted temperature and
transition state, dynamic transition-property overrides, and nested evolving
contents without advancing their clocks. Generic ingress, explicit claims and
legacy capture propagate that policy into recovery. Arbitrary mod-specific
evolution needs an explicit audit; merely assigning a policy flag is not detection.

No live engine transfer, server join, installation deployment, or menu-removal
acceptance was performed. Task 3b1 adds real native initialization, serialization,
and partial slot-merge execution under deterministic API fixtures, plus Harmony
installation checks against the installed assemblies. The controller's isolated
loopback server startup at `/tmp/vrpg-pickup-startup.SUUv9l` accepted the pinned hook
and loaded zero player stores; this is startup evidence only. Tests use real
canonical stores/archives, fake external destinations at the effect boundary,
and real temporary filesystem sidecars. The full suite passed 899/899 on 2026-09-09.

## Task 3b2: phase-aware native packet and cursor refusal

`PlayerItemCustodyPolicy` distinguishes verified Vanilla, canonical, and recovery
ownership. A successfully validated archive with no migration, or a successful
Restored journal, permits Vanilla. Prepared/RestorePending, invalid phases,
unknown archive load, and lost player identity refuse competing native operations.
Committed refuses Vanilla even when persistence, bridge reconciliation, or hooks
are unhealthy; canonical operations then become read-only. A pre-migration
validation/checkpoint failure cannot invent Committed ownership. Lost archive
validation retains an observed Committed owner read-only; other unknown ownership
freezes to Recovery. Connection observations are forgotten at disconnect, and
world/module start/disposal resets them. Snapshots are current observations, not
durable or reusable activation credentials.

The persistence module supplies this same policy to pickup and native guards.
After review correction, `IsLoaded` plus a mutable journal phase is explicitly
insufficient. `PlayerItemArchiveService.MigrationConfirmation` supplies detached
phase evidence established only by successful load/save. Stage/status changes
remain pending, and any failed call into storage keeps affected ownership
uncertain even when `AbortStagedMigration` restores live metadata to no journal.
Only a known failure before storage was invoked can restore that prior Vanilla
permission without another authoritative save/load. A storage call throwing
before its actual write is still ambiguous to the caller, just like write-then-
throw; both require confirmation. Mutable phase drift also forces a real save
attempt instead of permitting a no-op save to masquerade as confirmation. Saved
journal candidates are detached from mutable references and preserve unknown
fields using the archive serializer's same settings, including its depth-128
contract. Snapshot preparation exceptions return an explicit no-write failure;
they leave dirty state and pending/uncertain phase evidence intact.
Terminal-phase `Resume` retries only confirmation; after reloading a
confirmed RestorePending phase whose source already exactly matches the snapshot,
recovery confirms without replaying `Restore`. Aborted initial preparation can
likewise confirm its untouched fallback without clearing the source.
Custody lookup reads existing bridge receipts without replaying reconciliation or
writing receipts. Existing explicit startup/disconnect/death/selection lifecycle
reconciliation remains. Pending/nonterminal receipts and recorded stray cursors
block canonical operations. Pickup tombstones are checked before collector custody,
including for Vanilla/recovery collectors. Pickup readiness now checks its live
Harmony prefix. Deferred fresh-birth leases carry a monotonic observer lifetime,
so detach/reattach of the same cached delegate cannot revive old evidence.

Audited `ServerSystemInventory` entry points in native 1.22.7 (module
`8a3ad3aa-2940-4c39-ae1b-881de9e65346`) are pinned by exact signature and SHA-256 IL
body hashes in `NativeInventoryPacketGuardPatch`:

- `HandleSelectedHotbarSlot(Packet_Client, ConnectedClient)`: before selection
  events, active-slot assignment, `DropSlotIfHot`, animation callbacks/broadcasts.
- `HandleActivateInventorySlot(Packet_Client, ConnectedClient)`: before inventory
  lookup, `itemFilter`, and `InventoryNetworkUtil.HandleClientPacket`.
- `HandleCreateItemstack(Packet_Client, ConnectedClient)`: before conversion,
  direct target assignment, dirty callbacks, and native rejection packets.
- `HandleMoveItemstack(Packet_Client, ConnectedClient)` and
  `HandleFlipItemStacks(Packet_Client, ConnectedClient)`: before filtering,
  transfer/network callbacks and hotbar broadcasts.
- `HandleHandInteraction(Packet_Client, ConnectedClient)`: before block-interaction
  dispatch, slot lookup, event handlers and collectible use callbacks.
- `HandleToolMode(Packet_Client, ConnectedClient)`: before slot/selection lookup
  and `SetToolMode`.
- `HandleInvOpenClose(Packet_Client, ConnectedClient)`: before inventory lookup
  and open/close callbacks.
- `secureMouseSlotContents(ServerPlayer)`: before cursor access, `TryTransferAway`
  and `DropAll`, for both native disconnect and shutdown callers.

Constructor registration (packet IDs 13, 7, 10, 8, 9, 25, 27, 30) and native
`OnPlayerDisconnect(ServerPlayer)`/`OnBeginShutdown()` are also pinned witnesses.
Missing/signature/body mismatch or existing conflicting patches reject installation.
Removed prefixes, changed authority, and later handler/caller patch conflicts
withdraw readiness. A missing/throwing cursor recovery observer makes readiness
unavailable for that installation; the observer is never blindly replayed. Reset
unpatches only owned prefixes and clears authority. Foreign code cannot be made
safe by these readiness checks: conflicts block activation, they are not certified
as preserving custody.

Identity comes only from `ConnectedClient.Player.PlayerUID`, or the server player
passed by the cursor lifecycle caller. Packet inventory IDs and `SentPlayerUid`
never establish custody. Exceptions/lost identity refuse. Verified Vanilla passes
through unchanged. Private `itemFilter` is untouched; no post-mutation refund,
generic insertion rejection, outbound adapter, quantity narrowing, or new economy
was introduced.

Cursor limitation: `InventoryPlayerMouseCursor.ToTreeAttributes` and
`FromTreeAttributes` are empty in this engine. A refused stray cursor stays in its
source slot in RAM; an in-memory recovery marker blocks activation and native
reopening for that player, even if the journal changes to Restored. This is not a
crash-durable cursor recovery store, and the marker does not survive process exit.
Full activation needs an audited empty-slot/materialized-lease precondition and a
durable recovery contract before relying on this boundary. Retaining RAM cannot
make shutdown or a crash preserve the cursor. This slice does not erase it or
claim that problem solved.

Remaining producers: `OnUsingTick` invokes `OnBlockBreaking`, `OnHeldIdle` on both
hands and `callOnUsing` without a new packet. `UpdateTransitionStates` walks native
slots and invokes evolving-item callbacks. Direct grants, hotbar/offhand/crafting/
equipment/backpack insertion, containers, trade, signs/labeled chests/ground
storage, death and direct use/drop/placement remain open. The two new coverage
names do not certify any of these routes or all native slots as frozen. Outbound
destination durability remains a separate parent Task3 requirement.

Task3b3 brings the minimum ownership bootstrap forward from Task8. Replacement
configuration now controls requested operations; RPG configuration does not gate
custody inspection. A nonmutating filesystem/legacy-key probe omits persistence
only for disabled replacement with affirmative absence of all world-specific
custody-directory, sidecar, legacy-slot and checkpoint evidence. The probe uses
accessible parent listings, not `File.Exists` error suppression, and treats I/O,
permission, obstructed paths and symlinks as uncertainty. It never imports an
archive, creates a directory or changes a checkpoint. The path uses the same
`GamePaths.DataPath` root as the native `GetOrCreateDataPath` implementation, without
its directory-creation side effect; the storage class still owns world-ID hashing.

Requested replacement or any history/uncertainty constructs the same persistence
module and native packet/pickup/background-tick guards. Actual archive inspection still uses the
existing `ReadOrImport` and format/checkpoint validation, including deleted-sidecar
refusal; it is not called a nonmutating probe. Disabled Committed remains canonical
but read-only, Prepared/RestorePending remain recovery, confirmed Restored and
confirmed no-journal retain Vanilla. Existing completed pickup claims suppress
restored duplicates for all collectors. No new canonical pickup/action or full
native-profile readiness is authorized when replacement is disabled. Existing
explicit admin resume/restore and lifecycle receipt reconciliation remain available.

If mandatory packet/pickup/background-tick hooks fail to install or are unavailable at
`SaveGameLoaded`, or archive validation fails, the module calls the actual server
shutdown API before gameplay admission. It marks bootstrap refused before doing
so and skips archive/checkpoint writes during the engine's soft-shutdown world-save
callback. Invalid world/path setup also creates an owned refusal module, rather
than escaping into the modloader's exception handling with no protection. Disposal
removes its event callback. No readiness flag is used as a substitute for this
startup refusal.

Native source boundary: `ServerSystemLoadAndSaveGame.OnBeginConfiguration` opens
the database before normal mod startup. Its `OnBeginModsAndConfigReady` invokes
`SaveGameLoaded` during LoadGamePre. `ServerAPI.ShutDown` calls
`ServerMain.AttemptShutdown`, which synchronously calls `Stop` on the main thread;
`Stop` sets `exitState.Mode` before shutdown work. `GameExitState.Exit` is true for
any non-None mode, `ServerMain.Launch` checks it after LoadGamePre before opening
the subsequent gameplay path, and `ProcessNetMessage` refuses it. The native
phase/event callers do continue their remaining initialization after the callback.
Root's isolated 1.22.7 damaged-sidecar smoke confirmed automatic clean exit before
RunGame, no extra exception despite post-callback initialization logs, and identical
sidecar SHA256 before/after. Fresh disabled startup reached RunGame without creating
the custody path; historical disabled startup installed the hooks and loaded its
archive. Both clean cases stopped normally. The existing unavailable-gear log with
disabled operations remains, and these smokes establish no joined-menu acceptance.
A late `PlayerJoin` listener was not used: native systems receive that join first.

This is a bounded startup safety boundary, not the full Task8 handshake. No new
runtime guard-revocation shutdown policy, complete tick/direct-producer freeze,
durable cursor contract, outbound adapter, migration, default activation or joined
menu acceptance is established. Config or GUI teardown never establishes or
reverses item ownership.

Verification executes the actual nine patched handlers, real native disconnect
and shutdown methods, real slots/managers, and controlled downstream callbacks.
Move/flip/activate use a controlled `InventoryNetworkUtil` callback, not a claim
of full native transfer protocol acceptance. Selected-hotbar Vanilla verification
ends at its first event callback, and creative verification establishes assignment
and dirty callback before the headless fixture's absent static logger. Cursor
tests execute native transfer attempts with no eligible target, then a controlled
drop callback; they also prove native mouse serialization contains no slot.
No deployment, live migration, native joined-menu acceptance, or full native
profile activation was performed for Task 3b2.

## Task 3b4: per-player native background inventory refusal

`NativeInventoryTickGuardPatch` adds only these mandatory boundaries in the same
installed 1.22.7 module (`8a3ad3aa-2940-4c39-ae1b-881de9e65346`):

- `ServerSystemInventory.OnUsingTick(float)`, registered every 20 ms. Original IL
  SHA-256 `0F8E037B6D7E54F81093350434B6A15D02B065411411C56FBBE65DE3FE8D4B87`.
  An inserted custody decision precedes original `IL_0023` (the player state
  check) and branches to the existing outer enumerator continuation `IL_01d6`
  for refused players. Thus refusal precedes active-slot lookup, primary block
  breaking/counter writes, either hand's idle callback, ongoing held-use callbacks,
  empty-stack clearing, dirty notifications, and hand-control changes.
- `ServerSystemInventory.UpdateTransitionStates(float)`, registered every 4000 ms.
  Original IL SHA-256
  `FA705EAFA20131F82011DA6F4D83547F8C17D81F08ECF7005CFB8EA2E2A476D9`.
  A custody decision precedes original `IL_0048` and branches to the existing
  client-enumerator continuation `IL_00eb`. It runs before inventory enumeration
  or `UpdateAndGetTransitionStates`. The native global item-filter config refresh
  still runs; it is not a per-player slot or custody mutation.

Each insertion is `ldloc.1; call permission; brfalse original-next-player`.
The original playing checks, loop bodies, allowed-player behavior and enumerator
finally blocks remain native. The audited insertion sites have no incoming label
or exception boundary; insertion and destination share the same outer try, before
any nested inventory enumerator begins. Signature, module, method body, unique
playing check, branch shape and continuation are checked before installation.
Constructor registration is pinned to the same exact constructor hash used by
the packet guard. Actual transpiler execution must acknowledge both insertions;
missing/ambiguous insertion fails rather than reporting a no-op as installed.
Partial installation is removed, retaining foreign patches. Existing conflicts,
late conflicts, constructor patches and hook removal make readiness unavailable.

Only the server player UID establishes custody. Missing players/identity and
absent or throwing resolution refuse without slot or control access. Verified
Vanilla and non-playing behavior are retained. No roster removal, connection-state
spoofing, inventory swapping, copied native loop, compensating refund, archive
mutation or canonical adapter is used.

Tick owner resolution shares the persistence module's `ResolveCustody` and
`MigrationConfirmation` path: confirmed phase, pending changes, failed-write
uncertainty, sticky canonical observations and unresolved cursors are identical to
ordinary custody queries. Tick queries skip canonical-operation readiness and
bridge receipt snapshots. The path performs keyed in-memory lookups under the
existing confirmation lock, with no filesystem reads/writes, store/receipt clones,
or roster/inventory copies per player tick. Permission is re-evaluated each time;
no cached permission survives phase changes. Native collection enumeration is
unchanged. This is call-path evidence, not a performance benchmark.

The guards install alongside packet/pickup protection for historical custody even
with replacement disabled. Missing or conflicting ticks at `SaveGameLoaded` use
the existing actual startup shutdown path before archive load/gameplay admission.
Verified empty disabled worlds still create neither sidecar nor custody hooks.
Module disposal and mod-system teardown clear tick authority and remove the owned
transpilers. Exact readiness entries are `native:OnUsingTick` and
`native:UpdateTransitionStates`; no `held`, `use`, `slots:*` or full-profile entry
is registered by this protection.

Tests execute both actual native methods and actual `GameTickListener` delegates
registered by the native constructor **before** patch installation. Mixed rosters
verify both allowed players (including one after canonical/recovery players),
both hands' idle callbacks, breaking, use step/stop, slot depletion/dirty state,
hand controls, transition source changes and offline skipping. Additional tests
cover missing identity/authority, malformed insertion input, atomic cleanup,
registration/patch conflicts, removal, reset/reinstall, historical disabled boot,
distinct world identity, and custody parity through failed and confirmed phases.

This closes two background callbacks only. Direct grants, sign/chest/ground-storage
producers, other slot/held/use routes, death, trade, containers, durable outbound
destinations and durable native cursor remain
separate blockers. Runtime removal by external code is detected by readiness but
is **not** an ongoing admission guard or automatic runtime shutdown: the removed
native hook cannot still protect callbacks. Full handshake, default activation,
live migration and the joined Inventory/Character menu acceptance gate remain
unimplemented by this slice.

## Task 3b5: native world-item packet refusal

`NativeInventoryPacketGuardPatch` now treats three additional generic native
entrypoints as mandatory, atomically installed refusal boundaries:

- packet 3: `ServerSystemBlockSimulation.HandleBlockPlaceOrBreak(Packet_Client,
  ConnectedClient)`, IL SHA-256
  `8BF099DE44F80AC84D3019FFD0779355FF593D4AC2C5F3A21297180DB22FF355`;
- packet 22: `ServerSystemBlockSimulation.HandleBlockEntityPacket(Packet_Client,
  ConnectedClient)`, IL SHA-256
  `814B4A3FF08C6A204333FF805D15A2FB25C3B52518DD818E748110564573E7E8`;
- packet 31: `ServerMain.HandleEntityPacket(Packet_Client, ConnectedClient)`, IL
  SHA-256
  `92C421A4DEFAB2D9EC83BE894068A3DA49420454984780A02F5B6B0EC3FD3966`.

All are instance `void` methods in installed module
`8a3ad3aa-2940-4c39-ae1b-881de9e65346`. They reuse the packet guard's connected
`client.Player.PlayerUID` custody decision. Canonical, recovery, missing/blank
identity, absent resolution and throwing resolution return before reading any
packet field, world position, active slot or target. Neither
`ConnectedClient.SentPlayerUid` nor the target entity/block entity supplies actor
authority. Verified Vanilla proceeds through the unmodified original handler.

Routing is pinned separately from the targets. The actual
`ServerSystemBlockSimulation(ServerMain)` constructor registers packets 3 and 22;
its IL hash is
`D68C1AA9DCEE80AE52DB22FAE55A67FBFCEA8105AD2DE1337CE5F146E5E94CBF`.
`ServerMain.InitBasicPacketHandlers()` registers packet 31; its IL hash is
`DE1CF0E6B35FE1DEA644B4EFB85D5B23343CDC374F96FE8F9141BB06F033063B`.
Existing/late foreign patches, missing or wrong-owner methods, removed prefixes,
conflicting routes and partial installation withdraw readiness. Runtime removal is
detected by readiness but remains neither automatic shutdown nor an ongoing
admission mechanism after the prefix is gone.

Tests capture the real packet delegates before Harmony installation and then
invoke those same delegates. Vanilla packet 22 resolves a real `ServerWorldMap`
loaded chunk and calls a real `BlockEntity.OnReceivedClientPacket`; packet 31
resolves `ServerMain.LoadedEntities` and calls a real
`Entity.OnReceivedClientPacket`. Both receive the connected player and mutate a
controlled external target witness. Canonical/recovery and unknown authority keep
the source hotbar and external target state unchanged, including malformed payloads.
Packet 3 Vanilla controls observe real permission dispatch and distinguish native
break, place and decor branches. The headless fixture deliberately stops at
controlled branch callbacks; it does not claim a complete installed-world block
mutation or gameplay-parity test.

Historical disabled custody still installs this protection. Removing or conflicting
any of the three boundaries before `SaveGameLoaded` uses the reviewed pre-game
shutdown refusal and leaves the archive unchanged. Module disposal now resets both
packet/world-packet and tick authority; mod-system teardown retains its independent
reset. The three new readiness names do not confer sign/chest/ground-storage, Home,
trade, container, death or arbitrary producer readiness. Current VRPG source has no
`SendEntityPacket`, `SendBlockEntityPacket`, or `OnReceivedClientPacket` usage, so
separate server-authoritative VRPG channels and server-to-client entity updates are
not intercepted by these prefixes.

This slice is refusal, not an adapter. No packet-ID-only Home/appearance/trader
exception, default UI, economy authority, outbound receipt, live migration,
deployment, joined-menu acceptance or full handshake is added.
