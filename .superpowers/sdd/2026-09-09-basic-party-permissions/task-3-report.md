# Task 3 implementation report

## Design rationale and pre-build review

Screen contract: manage membership and invitations from the actual connection-owned party service. Create or join when alone; select a person and choose a permitted action when in a party. Existing six pages and Back/Close remain persistent. Names, leadership, presence, count, incoming invitations and local results support decisions; IDs, health bars, bonuses and future controls are excluded.

Tokens: existing Iron950 #0d1010 and Iron900 #141817 surfaces, CopperDull #9b6a42 structure, CopperWorn #bd8450 interaction, Text #eadfce and Muted #aa9d8b. Existing Lora face retains readable 14–18px labels. Everything aligns left in one bounded workspace. The forged iron/copper shell supplies the visual identity.

Wide: `Back | Party | Close / six pages / members-or-invitations-or-online list | selected person + permitted actions`.
Narrow: `Back | Inspect | Party | Close / six pages / list OR full selected identity / anchored actions`.

Pre-build review: individual member cards and duplicated Party headings would dilute the established menu. Use shared fitted buttons, one clipped list and contextual detail instead. Narrow layouts alternate list/detail to preserve font size and reachability. Full selected identity wraps in a scrollable detail area; actions remain anchored. A dedicated native directory search replaces Inventory native fields while Party is open, then restores inventory browse context. Loading/unavailable never imply no party. Pending blocks repeat mutations; errors use localized actionable language.

## Verification

Base `da2601a`, including reviewed Task2 `4bf1b6a`; worked in place on main as dispatched.

All test commands ran from repository root with test runner socket escalation, `-p:DisableSharedDeploy=true -p:PackageOnRelease=false`. No packaging, installed deployment, game controls or world changes were performed by this worker.

Focused command:

```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj -p:DisableSharedDeploy=true -p:PackageOnRelease=false --filter 'FullyQualifiedName~PartyPanelTests|FullyQualifiedName~PartyMenuIntegrationTests' --logger 'console;verbosity=minimal'
```

Behavioral panel RED: 18 failed / 0 passed against an empty panel/controller seam. Missing sends, disabled reasons, bounded search and exact Back context failed. Preliminary compile repair added the item-client namespace. GREEN after implementation: 18/18. During GREEN the fixtures were corrected to actual Guid-format invitation/party IDs required by wire validation; no authority contract changed.

Native shell RED: 4 failed / 0 passed because the existing shell fell through to a null gallery without Inventory. Native mouse signature, language initialization and root-bounds/GUI-scale fixtures were corrected before meaningful shell assertions. Integrated GREEN: 22/22. Actual drawn native controls are inspected for six pages, bounded/non-overlapping header rectangles, mouse Create, and Tab/Enter Create.

Self-review feedback/authority RED: 2 failed / 1 passed. Initial snapshot `ok` falsely displayed success; an offline existing member was incorrectly barred from becoming leader. The Task1 authority permits offline leadership transfer, so UI now follows it. Success is tied to a pending mutation completing and hidden/cleared when starting Disband confirmation. GREEN: 24/24.

Unavailable-directory RED: 1 failed / 0 passed because a search control remained exposed despite unavailable authority. Shell and actual native field composition now share the health-aware `ShowSearch` predicate; health/reset transitions recompose the native fields. GREEN integrated: 30/30.

Mouse Party-entry RED: 1 failed / 0 passed because hit handling assigned `Focus=party` before capturing the return snapshot. Party entry now captures prior browse focus first. GREEN integrated: 31/31.

Final focused GREEN, after full-name scrolling, maximum-list and real server roundtrip checks:

```text
Passed!  - Failed: 0, Passed: 34, Skipped: 0, Total: 34, Duration: 198 ms
```

One full integration suite:

```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj -p:DisableSharedDeploy=true -p:PackageOnRelease=false --logger 'console;verbosity=minimal'
```

```text
Passed!  - Failed: 0, Passed: 1563, Skipped: 0, Total: 1563, Duration: 20 s
```

No compiler warnings. `git diff --check` passed. Tests exercise every mutation's actual packet action/current revision/target identity, pending repeat protection, permitted role/target guards, Disband confirm/cancel, search text normalization/explicit submit/bounded paging, close/reopen pending retention and reset, exact Back context including real mouse Party entry, health state copy, maximum list scrolling, full 128-character identity scrolling with anchored actions, and authenticated registered server-handler create/disband roundtrips against real temporary persistence.

## Exact integration and launch controls

- Reuse `VRPGModSystem.PartyClient` through `new PartyPanel(PartyClient)`; panel only subscribes/unsubscribes `Changed` and never disposes or resets the connection service.
- Combined shell: `new PlayerMenuController(inventoryPage, partyPanel)`. `OpenParty()` captures immutable browse context; `Back()` first dismisses confirmation/details, then restores that snapshot. It adds no `PlayerMenuPage` enum values.
- Explicit real-party-only launch: set `VRPG_PARTY_MENU=1` in the client process, join a server with Task2 authority, then run client command `.vrpgparty`. The same `GuiDialogVrpgPlayerMenu` hosts the panel. It creates an inert item client service with no item adapter or fake records. Other six-page content truthfully says unavailable. `VRPG_PLAYER_MENU_TEST_STORE` and `VRPG_PLAYER_MENU_GALLERY` are not enabled or used by this route.
- No default semantic Inventory/Character takeover. Existing semantic close behavior remains. Party/Back/Close and compact Inspect hit regions reserve actual width. Redundant compact header title is suppressed.
- Native `party-search` exists only for an open, healthy directory in its list view. Find or Enter submits; typing alone changes bounded local text. Native Inventory search and quantity fields are omitted while Party is open, then restored with saved value/focus. Asynchronous health/reset changes recompose field presence; ordinary snapshots refresh cached Cairo without destroying a live text caret.
- Named intents map to existing PartyCommand with fresh request ID/current revision. Member and invitation lists are immutable service views. Online search requests `includeTargets:true`; paging uses the authoritative current page/has-more. All domain/transport result codes have player language and unknown codes use a generic fallback.
- Panel state owns selection, confirmation, search and scroll only. Closing leaves pending in the service; the panel remains alive in the reusable dialog and receives results while closed. Disconnect clears local panel state through the real service reset; delayed packets cannot reopen it.
- Offline existing members can be removed or made leader, matching Task1. Only Invite requires an available directory target; server rejection remains authoritative. Leader-only controls are absent or disabled with a local role/target/full reason as appropriate.

## Native composition evidence and state matrix

Final immutable capture directory: `/tmp/vrpg-party-capture-final` (70 PNGs). Earlier `/tmp/vrpg-party-capture-round1` and `/tmp/vrpg-party-capture-round2-final` retain review evidence; final captures include visible overflow indicators. Root inspected both earlier checkpoints and confirmed the requested clarity repairs. Worker inspected final overflow indicators, maximum lists, full/offline rosters and readable names.

Reproduce from repository root:

```sh
VRPG_MENU_CAPTURE_MODE=party VRPG_MENU_CAPTURE_PATH=/tmp/vrpg-party-capture-output VRPG_MENU_LANG_PATH="$PWD/VRPG/assets/vrpg/lang/en.json" dotnet run --project VRPG/tools/VRPG.MenuCapture -p:DisableSharedDeploy=true -p:PackageOnRelease=false
```

All scenarios at `1120x680`, `800x520`, `400x350`: none, solo, invited, many-invites (8), expired, member, leader, full (8), offline, loading, unavailable, pending, error, success, long-name (128), long-unbroken (128 W characters), online, online-many (16), disband. Files follow `party-{scenario}-{width}x{height}.png`; narrow selected states also have `-list`, long names have `-scrolled`.

Key final evidence:

- `party-none-400x350.png`, `party-unavailable-400x350.png`, `party-loading-400x350.png`: distinguish no-party/loading/unavailable without false success or empty selection directions.
- `party-invited-400x350.png`, `party-expired-400x350.png`: actual named invitation and accept/decline versus expired restriction.
- `party-leader-1120x680.png`, `party-member-400x350.png`, `party-offline-800x520.png`, `party-full-800x520.png`: role/presence/count and contextual actions.
- `party-pending-400x350.png`, `party-error-400x350.png`, `party-success-400x350.png`, `party-disband-400x350.png`: local pending/rejection/completed result/confirmation.
- `party-online-many-400x350.png`, `party-many-invites-400x350.png`: clipped large lists with visible scroll tracks; unit input tests reach last target/invitation.
- `party-long-unbroken-400x350.png` and `party-long-unbroken-400x350-scrolled.png`, plus spaced-name counterparts: full identity is reachable by wheel/PageDown with fixed actions and visible scroll affordance.

Cairo uses the actual production drawing code and validated client snapshots. It does **not** draw native text inputs, resolve live bindings, render GL game models, or establish a joined client. Actual native text input, GUI scales/game presentation, joined mutation behavior and user-world no-Vanilla gate remain root-owned verification. No unavailable check is claimed passed.

## Changes and self-review

Focused new modules: `PartyPanel`, `PartyPanelState`, `PlayerMenuShell.Party` partial. Narrow integration edits: controller, existing dialog/shell, VRPGModSystem opt-in launch, language keys. Test additions: PartyPanelTests, PartyMenuIntegrationTests, PartyCompositionCapture; shared Cairo initialization extracted in existing test scope to avoid duplicate native resolver installation. MenuCapture keeps Inventory mode and adds explicit Party mode.

Self-review confirmed six pages only, no fake live inventory, no new ordinary dialog, no network/server contract changes, all eight mutations routed to the connection service, selected names remain full in detail, pending is never optimistic membership, captured inputs do not leak into Inventory, and old/disconnected updates cannot open the panel. Corrected feedback, offline-transfer guard, unavailable search visibility, real mouse return-focus ordering, duplicate Refresh, empty-state directions and scroll discoverability. Existing README/design README/overhaul guide/animation work is untouched and excluded from the commit.

Parent work left open: joined/native verification, durable loot piles, full default menu ownership and the overall no-Vanilla Inventory/Character gate. Party tests do not implement or certify those tasks.
