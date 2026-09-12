# Basic Party Permissions Implementation Plan

> **Schedule note (2026-09-09):** Historical completed work. Further party and
> party-loot features are deferred by the [playable vertical-slice checklist](../../design/testable-version-todo.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver persisted, server-owned basic parties and real membership controls inside the replacement menu, with a reusable current-membership loot permission boundary.

**Architecture:** A world-scoped PartyService owns membership, invitations and authorization. An authenticated network service owns connection/request lifetimes; a connection-owned client service and compact inline Party panel reuse the existing player-menu shell. Durable item/pile transactions remain a separate consumer, not a second party authority.

**Tech Stack:** Existing C#/.NET10, Newtonsoft JSON, protobuf-net, native Vintage Story GUI/Cairo and xUnit. No added package or engine database-setting change.

**Spec:** [Approved basic-party design](../specs/2026-09-09-basic-party-permissions-design.md)

## Global Constraints

- One party per player, at most eight members including its leader. A one-member party is valid.
- Membership and leader identity survive disconnect and server restart. Online status is presentation, not membership.
- Invites require an online target and expire after five minutes. At most eight pending invitations per party and per recipient; one pending invite per pair.
- Invitations do not reserve a seat. Leader change/disband invalidates its invites. Invitations are cleared on server restart; membership is not.
- Shared access follows current membership, not a frozen list. Leaving/kick revokes shared permission, including queued but uncommitted pickup. The dropper retains own-item reclaim; solo drops do not gain an audience later; disband never makes loot public.
- Persist membership changes before reporting success or publishing new permissions. Failed or ambiguous writes make party authorization unavailable until reconciled.
- Commands derive the actor from the authenticated connection. Preserve unsupported schema data and refuse operation rather than resetting it.
- Keep the six existing top-level pages intact. No party health bars, combat bonuses, XP/loot reward scaling, party chat, housing grants, matchmaking or automatic travel permissions.
- Preserve unrelated work. User authorized scoped commits on main and subagent-driven implementation. Do not deploy, push, migrate user worlds, or activate default menu ownership.
- Do not say ready to test until actual joined-server ordinary Inventory/Character routes expose no Vanilla menu. Party or gallery tests are not that acceptance.

## Execution and integration

Paths below are repository-relative. Root coordinates reviews and native evidence.
Each worker is sole source writer and does not spawn agents. Use focused RED/GREEN,
one full suite at task integration, then covering tests only for review fixes.
No assertion-free diagnostic Facts; diagnostic output supplements assertions.

Common test command (append the named filter):
```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --no-restore -p:DisableSharedDeploy=true -p:PackageOnRelease=false
```
Read task reports for actual resulting interfaces before the next dispatch; amend
the next brief if names differ, without silently changing the approved behavior.

## Task 1: Durable membership and permission authority

Completed and reviewed through `6fbfd13` (foundation `6b893ee`). Full suite:
1,495 passed before the response-freshness fix; final party coverage: 48 passed.
Native lifecycle, transport, panel and actual loot piles are separate work below.

**Files:** Create `VRPG/src/Parties/PartyModels.cs`, `PartyService.cs`, focused service partials if needed, `PartyLootAccess.cs`, `Persistence/PartyArchive.cs`, `IPartyRepository.cs`, `WorldScopedPartyRepository.cs`. Create `VRPG/tests/VRPG.Tests/PartyServiceTests.cs`, `PartyPersistenceTests.cs`, `PartyLootAccessTests.cs` and focused fixtures. Do not wire network/UI yet.

**Interfaces:** Produce public types in VRPG.Parties:
```csharp
public enum PartyAction { Create, Invite, Accept, Decline, Leave, Remove, TransferLeader, Disband }
public sealed record PartyCommand(string RequestId, long ExpectedRevision,
    PartyAction Action, string TargetUid = "", string InvitationId = "");
public enum PartyLootAudience { Everyone, PartyOnly }
public sealed record PartyLootAccess(string DropperUid, PartyLootAudience Audience, string PartyId);
// PartyView contains Health, Revision, optional PartyId/LeaderUid, bounded member
// and incoming-invite records; never exposes mutable authority collections.
// PartyCommandResult contains Success, stable Code and current authorized View.
// IPartyRepository loads/saves PartyArchive with affirmative absence vs failure.
```
`PartyService(IPartyRepository repository, Func<string,bool> isOnline)` exposes
`Load()`, `Read(string actorUid, DateTimeOffset now)`,
`Execute(string actorUid, PartyCommand command, DateTimeOffset now)` and
`TryWithLootAccess(string actorUid, PartyLootAccess access, Func<bool> commit)`.
Define return models in PartyModels; stable codes are localized by consumers.
`TryWithLootAccess` is a trusted synchronous server integration boundary, never
an arbitrary client callback or a reusable permission lease. Its bool reports
both authorization and callback success; no callback runs on refusal.

- [x] Add RED tests for create/leader/member roles, all eight actions, max8,
  target-online, max8 invites each side, duplicate/expired invite, seat race,
  accepting two different parties, disband/newID, transfer and last-member leave.
  Use a test repository that captures independent serialized snapshots and can
  throw before write or after storing bytes. The fixture owns a controllable clock
  and online UID set, not hard-coded player authority.
```csharp
// Test shape: leader invites, target accepts, then target cannot invite.
var view = service.Read("leader", now);
var created = service.Execute("leader", new("create-1", view.Revision, PartyAction.Create), now);
Assert.True(created.Success);
Assert.Equal("leader", created.View.LeaderUid);
```
- [x] Run the three Party filters and inspect expected failures before production.
- [x] Implement copy/validate/persist/publish mutation ordering. Use stable random
  party IDs, immutable outward views, checked revisions, bounded actor/request
  replay records and stale revision rejection. No-op rejection must not write.
  A previous request with different payload is rejected; old requests cannot
  recreate a party after disband merely because response-cache entries expired.
  Distinguish known no-party from unavailable. Leadership is explicit; disconnect
  only changes presence. Startup clears invites without clearing membership.
- [x] Implement a world-identity-bound party file in its own namespace. Reuse the
  proven atomic-temp/flush/rename/directory-fsync pattern; if extracting a shared
  byte writer, keep existing item-storage behavior unchanged and run its covering
  tests. Do not copy the whole inventory serializer/repository or fake item UIDs.
  Enforce format, checksum, generation, size and structural membership invariants;
  preserve extension data and refuse unreadable, truncated, unsupported or
  mismatched-world data without overwriting it. Missing existing history is not a
  fresh world. Repository recovery must never silently choose an older roster
  that grants a removed member access. Explicitly model failed/uncertain saves and
  validated reload before grants resume. State platform limits honestly.
- [x] Add real temporary-file persistence tests: reload membership/leader, expired
  restart invites, isolation of two worlds, corrupt/missing historical data,
  unsupported format/unknown fields, truncated/failed commit, before/after write
  failure and stale instance writes. Assert bytes preserved on refusal.
- [x] Implement loot policy and commit gate: public, own-item, current members of
  originating party; new members gain, departed/kicked lose, solo remains owner
  only, changing parties never moves old drops. Shared grants refuse unhealthy
  state. Membership and commit serialize on the party gate; lock ordering is
  party gate then downstream item transaction. Reject reentrant party mutations
  from the trusted commit callback and never publish a cached grant afterward.
```csharp
bool called = false;
bool allowed = service.TryWithLootAccess("stranger",
    new("owner", PartyLootAudience.PartyOnly, "missing-party"),
    () => { called = true; return true; });
Assert.False(allowed);
Assert.False(called);
```
- [x] Cover concurrent leave/kick versus commit, callback exceptions/reentry,
  stale cached views, persistence uncertainty, spoofed identifiers and malformed
  audience. No production pile adapter or changes to enemy loot allocation.
- [x] Run focused tests, self-review, one full suite and diff check. Commit only
  Task1 files: `feat: add durable party membership and loot permissions`.

**Gate:** Direct server authority is durable/tested and permissions are reusable;
no claim of live UI or implemented loot piles.

## Task 2: Authenticated transport and world lifecycle

Completed and reviewed at `4bf1b6a`. Final full suite: 1,529 passed; focused
transport/client/module coverage: 31 passed. Intermediate native startup/cleanup
observed; no connected-player behavior or default menu ownership is claimed.

**Files:** Create `VRPG/src/Parties/PartyModule.cs`, `Networking/PartyNetworkService.cs`, `VRPG/src/Network/PartyPackets.cs`, `VRPG/src/Client/PlayerMenu/Party/PartyClientService.cs`; modify `VRPG/src/Network/VRPGNetwork.cs`, `VRPG/src/VRPGModSystem.cs` only at lifecycle/registration seams. Tests: `PartyNetworkTests.cs`, `PartyModuleTests.cs`, `PartyClientServiceTests.cs`.

**Interfaces:** Consume Task1 service/command/view. Produce PartyModule's loaded
`Service` and a connection-owned PartyClientService exposing `View`, `Pending`,
`Changed`, `RequestView()`, `Send(PartyCommand)`, `ResetConnection()` and `Dispose()`.
Packet types: `PartySessionPacket`, `PartyQueryPacket`, `PartyMutationPacket`,
`PartyResultPacket`, `PartySnapshotPacket`; both client/server register identical
types/order. Transport maps DTOs rather than serializing authority instances.

- [x] RED tests use captured actual registered handlers with two distinct
  authenticated player fixtures; never let a packet actor UID select authority.
```csharp
// Actual handler fixture: connection A cannot remove someone from B's party.
handler(connectionA, new PartyMutationPacket { SessionId = sessionB, RequestId = "wrong-session" });
Assert.Equal(before, repository.ReadBytes());
```
- [x] Add explicit server-generated per-connection session IDs, bounded DTO
  validation, action enums, party/revision/request correlation. Max identifier128
  characters, request/session64, search64; page of online invite targets at most16,
  roster8 and invitations8. Refuse oversize/malformed arrays before service work.
  Queries/mutations at most5 per second per connection with burst5; invitations
  also at most1 per2seconds per inviter. Reset connection buckets on disconnect.
- [x] Query only own roster/invites plus requested online target page; names are
  display only, selected targets are validated actual online identities. Bound
  roster/invite invalidations to affected players. Responses for old sessions,
  old requests or lower revisions cannot revive stale UI or pending mutations.
  Closing the menu retains tracking; disconnect/world change resets it.
- [x] Wire load/dispose/reconnect using existing module lifecycle and authenticated
  API callbacks; repository root comes from game data path/world identity, not
  hard-coded machine paths. No default menu activation or item archive schema
  changes. Register transport even when party unavailable so it can report truthful
  unavailability. Never replace corrupt data at startup.
- [x] Test startup absence vs corruption, health loss, two worlds, rejoin/session
  replacement, delayed results, invite invalidation, reconnect membership and
  handler cleanup. Exercise actual registration seams and stored bytes.
- [x] Focused RED/GREEN, self-review/full suite/diff check/scoped commit:
  `feat: connect party authority to authenticated clients`.

**Gate:** Real server/client requests use the persisted authority. No command-only
substitute for the required UI; actual joined network verification remains a
separate observed check if no client can be exercised.

## Task 3: Compact native Party panel and integration verification

Source implemented at `b1acd8b`, review fixes at `00b62f0`; per-task review clean.
Full integration suite after fixes: 1,568 passed; panel coverage: 39 passed. Native
Cairo matrix and targeted update/error captures inspected. Joined native text
input, GUI scales and actions await an approved reload of the temporary client.
Final whole-plan review remains in progress; default menu ownership is not enabled.

**Files:** Create `VRPG/src/Client/PlayerMenu/Party/PartyPanel.cs`, `PartyPanelState.cs`;
modify `PlayerMenuController.cs`, `GuiDialogVrpgPlayerMenu.cs`, `Shell/PlayerMenuShell.cs`
and a focused Party rendering partial, `Shell/PlayerMenuLayout.cs` if needed,
client hookup in VRPGModSystem and `VRPG/assets/vrpg/lang/en.json`. Extend
`VRPG/tools/VRPG.MenuCapture` for reproducible native composition. Tests:
`PartyPanelTests.cs`, `PartyMenuIntegrationTests.cs` and existing menu suites.

**Interfaces:** Consume connection-owned PartyClientService. PartyPanel translates
named intents to PartyCommand, owns selection/confirmation/filter/scroll only.
`PartyPanelState` records opened state and return context. Reuse current shell;
do not add PlayerMenuPage enum values or another ordinary dialog.

- [x] Use frontend-design and player-facing-ui/game-ui-review. Write RED tests
  for each visible control and Back restoration; server data drives all state.
```csharp
// Opening Party must preserve inventory browse context on return.
var before = controller.Session.Current;
controller.OpenParty();
controller.Back();
Assert.Equal(before, controller.Session.Current);
```
- [x] Implement Party button and inline list using existing iron/copper theme,
  typography, focus/buttons and clipping. Keep six tabs, semantic action close
  behavior and existing Inventory/Character routing. Target1120x680/800x520 and
  narrow400x350 without horizontal overflow. Add a focused shell partial rather
  than another large block in the existing long renderer.
- [x] Controls: Create, Invite online player with bounded search/page, Accept,
  Decline, Leave, Remove, Transfer leadership, Disband confirmation. Leader-only
  controls reflect actual role, valid targets and pending state. Show full member
  identity on focus/selection if row truncates. Empty/loading/offline/unavailable/
  expired/rejected/success states are localized plain language and local feedback.
  Health bars, bonuses and placeholder controls are absent.
- [x] Route actions through real connection service; close/reopen retains pending
  tracking and fresh results. Reset on disconnect; old updates cannot reopen the
  panel. Test mouse, Tab/Enter, text input and Back without losing prior selection.
- [x] Render realistic solo/invited/member/leader/full/offline/pending/error cases
  and long names at all sizes through existing native Cairo capture; inspect
  outputs and repair clipping/focus/overlap. No mock screenshots as runtime proof.
- [ ] Root observes final-build isolated joined client input, GUI scales and actions
  after user-approved reload/sign-in. No installed deployment/user world change.
  Report unavailable checks accurately; native composition does not pass this gate.
- [x] Run focused tests and one full suite, self-review/scoped commit:
  `feat: manage basic parties inside the player menu`.

**Gate:** Every panel action connects to real authority and is tested; distinguish
native composition from joined-game verification. Root performs final whole-plan
review and updates parent checklist. Durable loot piles and default menu ownership
remain open parent tasks; do not pretend Party tests implemented them.
