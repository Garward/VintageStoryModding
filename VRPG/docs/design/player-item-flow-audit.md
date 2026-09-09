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
  canonical/recovery custody (details below). Direct producer and tick routes
  remain unguarded; Task 8 must prevent their exposure or install producer-level
  guards before enabling the profile.
- Direct engine grants: `PlayerInventoryManager.TryGiveItemstack` 184 onward
  wraps a dummy slot and calls `TryTransferAway`. It does not identify a source
  and is not a custody adapter. Vanilla behavior remains before activation.
- Signs: `BlockEntitySign` 149/158/170 ignores grant rejection. Labeled chests:
  `BlockEntityLabeledChest` 174/187/197 does likewise. Ground storage:
  `BlockEntityGroundStorage` 1073 ignores rejection. A manager `false` return is
  **not safe refusal** for these producers. Their guards are absent; the exposed
  native profile must stay unavailable.
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
  or materialization. Task 3b2 refuses native hand/tool packets, but ongoing held
  ticks and direct producer entry guards remain pending.
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
3b2 adds only `native:packets` and `native:cursor-securing`, without registering
whole slot/held/use coverage. Other named routes still block activation. Task 8 must use this gate in the custody activation
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
module and native packet/pickup guards. Actual archive inspection still uses the
existing `ReadOrImport` and format/checkpoint validation, including deleted-sidecar
refusal; it is not called a nonmutating probe. Disabled Committed remains canonical
but read-only, Prepared/RestorePending remain recovery, confirmed Restored and
confirmed no-journal retain Vanilla. Existing completed pickup claims suppress
restored duplicates for all collectors. No new canonical pickup/action or full
native-profile readiness is authorized when replacement is disabled. Existing
explicit admin resume/restore and lifecycle receipt reconciliation remain available.

If mandatory packet/pickup hooks fail to install or are unavailable at
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
phase/event callers do continue their remaining initialization after the callback;
the root-owned isolated smoke must verify exit behavior. A late `PlayerJoin`
listener was not used: native systems receive that join first.

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
