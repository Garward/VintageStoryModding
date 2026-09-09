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

Enabled adapter subset: exact `EntityItem` sources with an existing birth token
observed during engine load, unchanged quantity/exact payload since load, and
the base no-op `CollectibleObject.OnCollected` implementation. Capacity preflight
refuses without touching the source. The durable claim precedes source removal;
the same receipt acts as a tombstone when the engine restores that source.
Suppression requires the token, quantity and exact payload to match. Tombstones
also protect a source approached by a noncanonical collector. A save failure
quarantines the source at this boundary until the claim can be persisted.

The adapter does not emit Vanilla `onitemcollected` or replay unknown collection
callbacks; those event consumers require a separate reviewed integration.
Custom collectible collection callbacks and non-EntityItem collectables are
refused before `OnCollected` runs. Their source remains in the world.

**Pending:** fresh/unmarked drops. `ServerMain.SpawnEntity_internal` publishes the
entity into the chunk before `TriggerEntitySpawned`; the ordinary spawn event is
too late to prove the birth token preceded all serialization. New sources get a
token but cannot transfer until it is confirmed by a later engine load. A narrow
pre-publication hook or validated persistence barrier is needed for ordinary
fresh pickup. No fresh-source or full-profile readiness is claimed.

## Producers and native slot writes

- Hotbar, offhand, mouse cursor, crafting, character/equipment and backpack:
  legacy migration scans all managed inventories and restores exact snapshots.
  Their ordinary runtime clicks, swaps, hotkeys and inventory packets still use
  Vanilla `ItemSlot.TryPutInto`, `TakeOut`, and direct `Itemstack` assignment.
  These routes are **not guarded for canonical activation**. Task 8 must prevent
  exposure or install producer-level guards before enabling the profile.
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
  or materialization. Producer entry guards for Vanilla actions remain pending.
- Death: engine `ServerPlayerInventoryManager.DropItem` and inventory death
  behavior still own Vanilla slot drops. The persistence module reconciles known
  canonical receipts on player death; a complete native death policy/guard is
  pending. Reserved canonical entries remain unavailable to a second consume.
- VRPG rift charts: `TemporalRiftService` calls `slot.TakeOut(1)` directly. This
  is another producer requiring a canonical use adapter before native exposure.

`PlayerItemPersistenceModule.CanActivateNativeProfile` requires every named
exposed route in its allowlist to have a live readiness/producer guard, as well
as loaded durable storage and the installed pickup hook. No blanket readiness is
registered in this slice. Task 8 must use this gate in the custody activation
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
fresh pickup, and automatic recovery of ambiguous world checkpoints remain open.

Codec inspection freezes declared transitions, persisted temperature and
transition state, dynamic transition-property overrides, and nested evolving
contents without advancing their clocks. Generic ingress, explicit claims and
legacy capture propagate that policy into recovery. Arbitrary mod-specific
evolution needs an explicit audit; merely assigning a policy flag is not detection.

No live engine transfer, server join, deployment, or menu-removal acceptance was
performed. Tests use real canonical stores/archives, fake external destinations
at the effect boundary, and real temporary filesystem sidecars.
