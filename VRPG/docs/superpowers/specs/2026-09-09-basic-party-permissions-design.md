# Basic party permissions

Status: design proposed for review. The user approved a basic permissions-only
party system and immediate revocation of shared loot access on leave/kick.
The implementation details below are proposed, not implemented or test evidence.

## Scope and approach

One world-scoped, server-owned party service supports invite, accept, decline,
leave, remove member, transfer leadership and disband. The replacement menu uses
that same service. No party health bars, combat bonuses, XP/loot reward scaling,
party chat, housing grants, matchmaking or automatic travel permissions.

A reusable party authority is preferred over storing independent membership lists
on loot: it gives UI and pickup one consistent answer and permits immediate
revocation. Engine chat groups are not treated as parties; they carry different
permissions and lifecycle semantics. This adds a small persisted subsystem, not
a general-purpose social or arbitrary-role framework.

## Membership and lifecycle

- One party per player, at most eight members including its leader. A one-member
  party is valid. A player explicitly creates a party or accepts an invitation.
- Only the leader invites, removes members, transfers leadership or disbands.
  Any member may leave. A leader leaving a nonempty party first transfers
  leadership or explicitly disbands; no automatic promotion on disconnect.
- Membership and leader identity survive disconnect and server restart. Online
  status is presentation, not membership. Stable player UIDs are authoritative;
  display names cannot authorize an operation.
- Invites require an online target and expire after five minutes. At most eight
  pending invitations per party and per recipient; one pending invite per pair.
  Invitations do not reserve a seat. Acceptance rechecks identity, current leader,
  capacity, membership and expiry. Leader change/disband invalidates its invites.
  Invitations are cleared on server restart; membership is not.
- Disband retires the party ID permanently. A later party, even with identical
  members, receives a new ID and cannot inherit old permissions.
  The last member leaving also retires it.

## Loot permission contract

User-approved rule: shared access follows current membership, not a frozen list
of whoever was present when an item dropped. Removal takes effect at the next
pickup authorization, including requests already queued but not committed.

Proposed precise interpretation: every deliberate drop stores its dropper UID,
audience and originating party ID. Everyone allows any eligible player. Party
only allows current members of that exact party; newly accepted members gain
shared access, and former members lose it. Drops do not follow the dropper into
a different party. Changing the preference affects future drops only.

Proposed owner exception: the dropper can still reclaim their own items, including
after leaving or disbanding. Revocation removes access to other members' drops,
not this independent ownership right. A solo Party-only drop is owner-only and
does not gain a party audience when its owner later joins. Disband never makes
loot public. This owner exception needs approval with this design.

Unknown party authority is not a known empty party: shared pickup refuses until
authority is healthy. Pile grouping never combines away per-item audience,
originating party or dropper identity. Exact quantities and durable ownership
remain the responsibility of the separately reviewed item/pile transaction.

## Authority and persistence

Keep party models, commands, persistence, network transport and client view state
in focused modules. Integrate via the existing module lifecycle and VRPG network
registration. No broad rewrite of VRPGModSystem or duplicate item authority.

Persist membership changes before reporting success or publishing new permissions.
Missing storage is distinct from unreadable existing data. Failed or ambiguous
writes make party authorization unavailable until reconciled; never keep granting
access from a possibly stale roster. Preserve unsupported schema data and refuse
operation rather than resetting it. Reuse proven world-scoped durable-storage
patterns without placing parties in fake player-inventory namespaces.

Commands derive the actor from the authenticated connection. Requests carry
bounded identifiers, request identity and expected party revision; reject stale
or forged changes and return current authorized state. Serialize membership
changes with the pickup permission/commit boundary so leave/kick cannot race a
stale grant. A pickup committed before removal is not retroactively reversed.
Do not hold network callbacks or disk waits inside arbitrary client-controlled
code. Rate-limit invitation and mutation attempts; bounded responses expose only
the caller's roster/invitations and the online invite targets they request.

## Replacement-menu panel

Player goal: invite a player or manage membership and understand who has shared
loot access. A compact Party button in the existing shell opens an inline panel;
Back restores the previous page. Keep the six existing top-level pages intact.

Show member names, leader, online/offline status and only permitted actions.
When solo, show Create party and incoming invitations. In a party, show the roster
and an invite control for the leader. Accept/Decline are beside each invitation;
Leave, Remove, Transfer leadership and Disband have direct mouse/keyboard paths.
Disband confirmation states that shared party access ends. Do not expose UUIDs,
revision numbers, health bars, fake future controls or a separate ordinary dialog.

Use the current menu theme, typography, clipping and focus treatments. The roster
is one quiet list, not a grid of decorative cards. Online status includes text,
not color alone. Preserve selection when results arrive; bounded scrolling handles
long names and compact layouts. Pending requests prevent conflicting repeats;
server-confirmed state updates the view. Rejection gives a short local reason,
unavailable state disables mutation, and disconnect invalidates stale requests.

## Verification and delivery

Tests must cover forged senders, double acceptance, full-party races, stale
revisions, replay, expired invites, leader departure, reconnect/restart, disband
and new-party identity, failed/ambiguous persistence, and missing versus corrupt
storage. Permission tests cover old/new members, kick/leave, solo drops, own-item
reclaim, public drops, party changes, unavailable authority and pickup races.

UI tests cover solo/invited/member/leader/offline/pending/rejected/unavailable
states, keyboard paths, long names and supported menu sizes. Inspect native
composition and actual server/client interaction before claiming the panel works
in-game. Party HP/HUD, reward changes and other permissions remain excluded.

Implement the service and persistence first, then authenticated transport/panel,
then consume the permission contract in durable drop/pile work. A pure permission
test does not certify a real loot pile. No deployment, user-world migration or
default menu activation is implied. The joined no-Vanilla Inventory/Character
acceptance gate remains open.
