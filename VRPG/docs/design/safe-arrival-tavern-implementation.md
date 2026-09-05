# Safe-arrival tavern implementation

Status: Generator and reservation preparation implemented; live realm allocation and transit are pending.

Contracts: [Onboarding](character-creation-and-onboarding.md),
[world generation](world-generation-and-world-mutation.md),
[realm runtime](realm-runtime-architecture.md), and
[architecture ownership](architecture-ownership-map.md).

## Generated instance

`TavernGenerator` produces a fixed 19 × 9 × 23 local volume. Version 1 uses the
seed only for a ceiling inset; all circulation and anchor positions are stable.
The creator room opens through a five-block passage into the common room.
Solid booths and a bar use inert blocks. A full-width, supported bottom-slab
transition connects the common floor to a raised exit landing. The exit is a
semantic interaction anchor inside the envelope, not an opening into a void.
Ceiling light blocks provide illumination without fire or a ticking block entity.

The immutable half-step raster is the shared navigation and validation input.
Arrival, recovery, common-room and exit anchors have level 3 × 3 landings. Spawn
coordinates use the cell center and exact feet height. The recovery anchor is
deterministic within the same instance; a failure of the entire instance still
requires the independent recovery realm described in the onboarding spec.

The four authored blocks have no drops, block entities, behaviors, combustion,
creative entries or population hooks. The palette rejects missing assets, altered
collision, top slabs, rewards, block entities and combustible replacements before
writes. There is no entity, encounter, reward or inventory generation API here.

## Ownership and preparation

`TavernInstanceIdentity` carries logical realm ID, owner UID, seed, definition and
generator version. Persist it with the realm manifest. Anchor IDs derive from the
logical realm, so remapping a physical dimension does not change their identity.
Unknown versions are quarantined instead of silently regenerating occupied saves.

`TavernInstancePreparation.Prepare` consumes an exclusive, empty, loaded
`ITavernGenerationReservation`. Realm Runtime supplies that reservation and must
hold it through commit or quarantine. World Mutation supplies authorized block
writes; `TavernVoxelAdapter` maps local cells to the reserved physical dimension.
The mutation adapter must flush block storage and lighting before readback. The
reservation owner checks world-height bounds, protected allocation bounds and
generation budgets before granting the reservation.

Preparation validates the plan, writes the entire bounded volume including air,
flushes, rechecks reservation safety, validates actual geometry and checks every
voxel against the plan before committing anchors. Fluid layers and block entities
fail readback. Exceptions and partial writes quarantine the reservation. Only the
realm owner may authorize an unoccupied retry; replay restores the same geometry.
An exclusive reservation must prevent concurrent writes and entry during these
checks and commit.

`OnboardingInstancePolicy` allows only the owner's creator context and authored
exit interaction. It denies hostile and ambient population, encounters, hazards,
damage, combat, rewards, loot, pickup, trade, building, breaking, liquids, fire,
public entry and ordinary travel. This policy must be enforced by the respective
server owners for the entire reservation lifetime, including after the guide is
acknowledged. The exit interaction asks the travel owner to validate a destination;
it is not permission for unrestricted travel. `IsPolicyEnforced` must describe
installed enforcement and the absence of existing hazards/actors, not intention.

## Remaining live integration gate

The current project has no owned allocator, realm manifest, isolation enforcement,
world mutation service or recoverable safe-transfer runtime. This implementation
does not substitute the reflective dungeon adapter or place new players in the
public overworld. No join handler calls it until those owners can provide the
reservation guarantees above.

The realm/onboarding integration must reserve and persist identity, enforce the
policy, prepare the tavern, persist its validated anchors, protect the player
before transit, and confirm actual arrival. Readiness is generation readiness,
not `SafeReleaseReady`. Reconnect and release must revalidate the loaded location,
realm ownership and active safety state. A previously returned result cannot
release protection. Inventory custody and starter-transaction ownership are
unchanged.

## Automated evidence

Run `dotnet test tests/VRPG.Tests/VRPG.Tests.csproj -p:DisableSharedDeploy=true
-p:PackageOnRelease=false --filter FullyQualifiedName~Tavern` from `VRPG` (one line).
The tests cover 1,024 seeds, deterministic replay, floor and ceiling corruption,
headroom, blocked passages, supported slab/full seams, anchors, reservation loss,
partial writes, retry, policy denials, unknown versions, missing/unsafe palettes,
fluid rejection and dimension-preserving writes across positive and negative
chunk boundaries.

Real-server lighting, collision, multiplayer isolation, spawn suppression,
reconnect and recovery tests remain required with the live runtime. Pure and API
adapter tests do not establish those engine integration guarantees.
