# Realm Runtime Architecture

Status: Adopted decision; selective derivative implementation not yet started  
Parent: [Complete game design](complete-game-design.md)  
Related: [Player housing and building](player-housing-and-building.md) · [World generation and world mutation](world-generation-and-world-mutation.md)  
Index: [VRPG design index](README.md)

## 1. Decision

End-state VRPG will not require the external Manifold mod for core gameplay. VRPG will own a focused internal realm runtime for physical dimensions, logical instances, generation, travel, persistence, isolation, recovery, and tests.

The implementation will selectively derive from Manifold's proven MIT-licensed code and test patterns rather than clean-room rewrite dimension handling. It will remove general library features VRPG does not need and strengthen VRPG-specific transaction and recovery behavior.

The current optional Manifold adapter remains only until the owned runtime passes parity tests. Housing and expeditions must not treat a reflective optional dependency as permanent bedrock.

## 2. Licensing and reuse

The inspected Manifold checkout contains one root `LICENSE` file:

```text
MIT License
Copyright (c) 2026 Pixnop
```

MIT permits use, copying, modification, merging, publication, distribution, sublicensing, and sale. A complete rewrite is not legally required. The condition relevant to VRPG is that Manifold's copyright notice and MIT permission notice remain included in all copies or substantial portions of its software.

Implementation requirements:

- Pin the reviewed upstream commit in a provenance record. The currently inspected checkout is `83894af382fdab10d45491b3d763b5fd68e00533`.
- Add the unmodified Manifold license as `licenses/Manifold-LICENSE.txt` or an equivalent distributed path.
- Add a third-party notice identifying which VRPG realm components and tests derive from Manifold.
- Retain provenance comments on substantially copied files even though the license does not require a particular per-file header.
- Include the notice and license in source distributions and packaged VRPG releases containing derived code.
- Do not reuse Manifold branding or logo; they provide no value to the internal runtime and may carry concerns separate from source licensing.
- Review newly introduced dependencies separately. The inspected repository lists only game-provided runtime references, while test packages retain their own licenses.

Ideas, architecture, and independently rewritten behavior do not require copying code, but preserving a project-level notice remains useful and honest when Manifold materially informed the design. This document records engineering intent and is not legal advice.

### 2.1 Local verification result

The Manifold pure test project was run against the locally installed Vintage Story 1.22.7 API on 2026-09-04:

- Total: 227
- Passed: 197
- Failed: 30
- Skipped: 0

The observed failures were concentrated in transit/facade tests whose NSubstitute/Castle-generated player proxy could not implement the current API's `IsInInteractionRangeOf` member. This is test-double/API compatibility debt, not evidence that 30 realm assertions produced incorrect results. It still means the checkout is not a drop-in verified baseline for VRPG's exact engine build.

Before deriving implementation, update or replace those player test doubles, rerun the complete pure suite, then run the headless scenarios against the same Vintage Story build. Preserve the current failures as evidence until the harness—not the assertion—is shown to be the cause.

## 3. Why VRPG owns this layer

Realms are failure-critical:

- Expeditions allocate, generate, checkpoint, clean, archive, or recycle isolated spaces.
- Housing requires persistent ownership, loading, permissions, recovery, and migration.
- Travel must enforce combat, extraction, party, companion, inventory, and return-anchor rules.
- Worldgen, encounters, loot, temporary terrain, and outcomes must commit against the correct space.
- Disconnects, crashes, missing chunks, and generator failures must never strand players or duplicate rewards.
- Automated tests need deterministic control and fault injection over the entire lifecycle.

Manifold is a strong generic dimension library. VRPG needs a smaller domain-owned realm system integrated with its global runtime profile, persistence, world mutation, expeditions, housing, and recovery.

## 4. Current transitional boundary

Current VRPG code:

- Detects the `manifold` mod at runtime.
- Resolves Manifold APIs through reflection.
- Emits a dynamic proxy for `IWorldgenStrategy`.
- Registers one persistent dungeon dimension with fixed spawn and bounded or streaming generation.
- Disables dimension-backed dungeons when registration fails and can use an overworld breach fallback.

`DungeonModuleConfig.RequireManifold` exists but currently has no enforcement outside its declaration, and VRPG's `modinfo.json` does not declare Manifold. This is prototype scaffolding, not a stable requirement.

## 5. Selective derivation plan

Retain or adapt these proven Manifold concepts and their tests:

- Stable code-to-engine-dimension allocation using mod-safe IDs and manifest-restored mappings.
- Immutable authoritative registry snapshots.
- Persistent, ephemeral, active, pending, quarantined, and failed lifecycle states.
- Generated-column tracking and active custom-dimension generation.
- Bounded and streaming generation planners.
- Destination generation before transit.
- Player and entity transit, safe-position memory, and dimension-aware events.
- Occupancy checks, evacuation, cleanup, and safe ID reuse.
- Client-side realm descriptors.
- Isolated subscriber failure and explicit service health.
- Pure tests plus headless real-server scenarios for transit, multiplayer, worldgen, inventory isolation, cleanup, and restart.

Change or omit these areas:

- Replace generic consumer-mod ownership with VRPG realm kinds and owners.
- Remove external facade discovery, reflection, and emitted proxy types.
- Remove per-dimension Vanilla inventory swapping; VRPG owns one typed inventory with explicit expedition security.
- Omit generic portal/command helpers, forced game modes, and block transit unless a concrete VRPG use needs them.
- Replace silently empty handling of a corrupt manifest with quarantine, backup evidence, administrator recovery, and safe evacuation.
- Replace synchronous region preparation on the travel call path with staged, cancellable, globally budgeted generation.
- Integrate realm records with VRPG revisions, migrations, semantic world state, temporary-terrain cleanup, and transactions.

Copying a proven class is acceptable when its contract matches. Rewrite it when VRPG's domain or failure behavior differs materially. Do not preserve generic abstraction merely to minimize the textual diff from Manifold.

## 6. Logical realms versus physical dimensions

Engine dimension IDs are finite physical resources. A VRPG realm is a logical gameplay space.

```text
RealmAddress
├── RealmId          stable VRPG identity
├── RealmKind        main, housing, expedition, recovery, test
├── DimensionId      physical Vintage Story dimension
├── InstanceBounds   optional isolated plot/cell
├── GenerationKey    seed + definition + version
└── StateRevision
```

Several isolated homes or expeditions may share a physical dimension while occupying protected, widely separated bounds. A dedicated dimension remains available when different sky, lighting, streaming, generation, or isolation rules require it.

Default mapping direction:

- **Main:** engine dimension 0 with VRPG worldgen and protected mutation.
- **Housing:** persistent logical plots in one or a small pool of housing dimensions.
- **Expeditions:** ephemeral or checkpoint-persistent instances in a reusable dimension pool.
- **Recovery:** a known-safe realm or main-world anchor independent of content generation.
- **Tests:** deterministic isolated instances allocated and purged by the scenario harness.

Persistent state references `RealmId`, never a raw dimension ID as identity. The registry resolves the current physical address.

## 7. Runtime components

- `IVrpgRealmService`: resolve, allocate, prepare, enter, leave, archive, destroy, recover, and inspect.
- `RealmRegistry`: logical records, lifecycle, physical mappings, and immutable snapshots.
- `RealmAllocator`: reserved dimension IDs, dimension pools, plot bounds, collisions, and reclamation.
- `RealmGenerationService`: staged generation, streaming, validation, relight, cancellation, and quarantine.
- `RealmTransitionService`: travel state machine, parties, companions, safe anchors, and reconciliation.
- `RealmPersistence`: versioned manifest, generated units, positions, revisions, and migrations.
- `RealmLifecycleService`: active, idle, archive, reset, purge, and cleanup transitions.
- `RealmClientMirror`: descriptors needed for loading, maps, ambience, UI, and diagnostics.
- `RealmRecoveryService`: evacuation, orphan repair, manifest reconstruction, and previews.

These should live in a dedicated `VRPG.Realms` project or similarly isolated module. World mutation, housing, expeditions, inventory, and encounters consume the public contract without storing private realm state.

## 8. Travel transaction

1. Validate player or party, source activity, destination, extraction, and content.
2. Reserve the destination realm and transition request.
3. Persist a source safe anchor and pending transition.
4. Generate or load the destination under a bounded budget.
5. Validate landing space, revision, permissions, and required content.
6. Move players and allowed companions; clear incompatible transient state.
7. Confirm chunk visibility, entity partition, inventory continuity, and arrival hooks.
8. Commit transition state and release source reservations.

Failure before movement leaves the player at the source. Failure after movement invokes recovery to the last confirmed safe anchor. Repeated requests return the same pending operation rather than duplicating travel.

## 9. Realm lifecycle

| State | Meaning |
|---|---|
| Declared | Stable identity and definition exist |
| Preparing | Address reserved; generation or loading active |
| Ready | Validated and available for entry |
| Active | Occupied or processing encounter state |
| Idle | Empty but retained for possible reuse |
| Archived | Persistent state retained without loaded chunks |
| Quarantined | Content, manifest, generation, or migration failure |
| Destroying | Cleanup transaction in progress |
| Destroyed | Tombstoned; physical address released |

Homes never become destroyed merely because they are empty. Expedition addresses are not released until occupants, entities, projectiles, temporary terrain, rewards, scheduled jobs, and persistent references are resolved.

## 10. Persistence and migration

The manifest records stable realm ID, kind, owner, lifecycle, physical address, generator and version, dependencies, generated bounds or units, safe anchors, revision, and recovery state.

- Persistence is checksummed and versioned with explicit corruption handling.
- ID or plot remapping is atomic or recoverable.
- Generation and cleanup markers are idempotent.
- Destructive purge, backend migration, and manifest reconstruction require backups and previews.
- Physical addresses are not reused until cleanup commits durably.
- Existing Manifold prototype data can be imported by mapping dimension code, ID, generated columns, positions, and retained chunks to VRPG realm records.
- Failed import leaves source data untouched and players at safe anchors.

## 11. Test ownership

Adapt Manifold's pure and headless patterns into the VRPG build.

Pure tests cover allocation, exhaustion, registry transitions, manifest corruption, migration, generation planning, cancellation, transition partial failure, cleanup, and deterministic reuse.

Headless scenarios cover:

- Player round trips and recovery.
- Two players at identical local coordinates in separate realms.
- Party travel, partial eligibility, late join, reconnect, and restart.
- Failure before generation, after generation, during movement, and before commit.
- Entity partitioning, AI targets, companions, projectiles, drops, and temporary terrain.
- Chunk unload/reload, lighting, block entities, scheduled ticks, weather, and map isolation.
- Home persistence, expedition teardown, and absence of cross-instance leakage.
- Dimension reuse only after cleanup.
- Corrupt manifests and missing definitions evacuating safely without overwriting chunks.

Performance scenarios cover many archived homes, several occupied homes, concurrent expedition preparation, streaming in several dimensions, and large-manifest server startup.

## 12. Implementation sequence

1. Freeze the minimal service contract and manifest schema.
2. Create the internal realm project and provenance/license files.
3. Port selected Manifold tests before or alongside their corresponding implementation.
4. Replace stale proxy-based player fakes with controlled adapters that compile against the pinned Vintage Story API.
5. Adapt allocator, registry, generated-column tracking, transit primitives, and generation planners.
6. Add logical plot allocation above physical dimensions.
7. Integrate VRPG persistence, capabilities, world mutation, temporary terrain, and recovery.
8. Move current dungeon worldgen behind the owned service while retaining Manifold as a parity fallback.
9. Prove multiplayer, restart, generator failure, quarantine, and cleanup scenarios.
10. Import or explicitly retire prototype Manifold saves.
11. Remove reflection, dynamic proxies, `RequireManifold`, Manifold-specific player text, and the external fallback only after parity.
12. Build the first persistent Home and complete expedition on the owned runtime.

## 13. Acceptance criteria

- Default VRPG provides main, housing, expedition, recovery, and test realm capabilities without Manifold installed.
- Missing Manifold cannot disable an end-state feature.
- Logical realm identity is stable across physical address migration.
- Generation and travel are budgeted, observable, repeat-safe, and recoverable.
- Failure cannot strand a player, duplicate inventory, leak enemies, or release occupied state.
- Pure and headless multiplayer tests run with the VRPG build.
- Removing the adapter changes no player-facing behavior in a fresh default world.
- Prototype data imports losslessly or refuses before mutation.
- Packaged derivative code includes Manifold's full MIT notice and recorded provenance.

## 14. Open decisions

- Exact classes/tests to copy versus rewrite around VRPG-specific contracts.
- Physical dimension-pool counts and purposes.
- Plot geometry and isolation distance.
- Map behavior per realm kind.
- Whether an active expedition survives full server restart by default.
- Allowed companions, mounts, drops, and transient effects during travel.
- Administrator evidence required for manifest reconstruction.
