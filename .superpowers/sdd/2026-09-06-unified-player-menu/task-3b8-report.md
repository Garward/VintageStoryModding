# Task3b8 implementation report — 2026-09-09

Status: DONE. Base: `cf61a5f`, user-authorized `main`.
Implementation commit: `5009294` — Require available native inventory evidence for migration recovery.
This report is a separate scoped documentation commit following the implementation.

## Reproduction and cause

The hypothesis in `legacy-inspection-status-audit-notes.md` was reproduced before
production edits. The actual `VintageStoryLegacyInventorySource` used the native
`ServerPlayer`, `ServerPlayerInventoryManager`, real managed inventory registry,
`InventoryBasePlayer` slots, and item codec/materializer. The healthy Task3b7 fixture
was extracted into `NativeLegacyInventoryFixture` and reused; its guarded native
Clear/Restore compatibility test still passes. API/world/network/logging stubs
replace engine services only. The source call counter delegates every operation
to the actual adapter; it does not implement fake comparison or mutation behavior.

Native source starts with 7 hotbar items and 3 mouse items. Controls inspect either
those two matching slots or healthy empty slots after a real source Clear. Faults:

- Missing one inventory: remove the empty `character-player` managed registration,
  leaving readable matching hotbar/mouse slots.
- Missing all: remove all five managed registrations, retaining the original
  inventory objects so hidden quantities can still be independently asserted.
- Paused: native inventory network utility reports paused hotbar updates.
- External: register an external chest and mark it opened for the native player;
  native `OpenedInventories` discovers it.
- Throwing: managed character inventory `Count` throws an exception containing a
  diagnostic marker. This is a throwing native inspection, not a fake source result.

Each fault was exercised with source quantities 0 and 10 and through real
coordinator Prepared Resume, RestorePending Resume, and Committed RestorePrior.

Pre-fix observations included:

```text
paused, empty=True: blockers=[Finish the current item move and try again.], visible=0, comparison=Empty
paused, empty=False: blockers=[Finish the current item move and try again.], visible=2, comparison=MatchesSnapshot
missing-one, empty=False: blockers=[The equipment inventory is unavailable.], visible=2, comparison=MatchesSnapshot
external, empty=False: blockers=[Close the open container and try again.], visible=2, comparison=MatchesSnapshot
missing-all, empty=False: blockers=[The hotbar inventory is unavailable.; The backpack inventory is unavailable.; The equipment inventory is unavailable.; The held item inventory is unavailable.; The crafting grid inventory is unavailable.], visible=0, comparison=Empty
Prepared, missing-all, empty=False: success=True, recovery=False, live=Committed, confirmed=PlayerItemMigrationConfirmation { IsConfirmed = True, Phase = Committed }, clear=0, restore=0
RestorePending, missing-one, empty=False: success=True, recovery=False, live=Restored, confirmed=PlayerItemMigrationConfirmation { IsConfirmed = True, Phase = Restored }, clear=0, restore=0
Prepared, paused, empty=False: success=False, recovery=True, live=RestorePending, confirmed=PlayerItemMigrationConfirmation { IsConfirmed = True, Phase = RestorePending }, clear=1, restore=1
```

Throwing inspection previously propagated the native exception. Healthy empty,
healthy matching, and native guarded Clear/Restore controls passed throughout.

Cause: CompareTo discarded Inspect's blocking reasons before treating readable
slots as Empty/MatchesSnapshot. Resume interpreted those results as authoritative
confirmation or permission to retry a source operation. RestorePrior could also
stage a rollback and call Restore when the source was unavailable.

## Fix and compatibility contract

- Append `LegacyInventoryState.Unavailable`, preserving the previous enum values
  and every archive/snapshot schema. Any inspection blocker returns Unavailable
  before testing slot emptiness or equality.
- Catch native inspection exceptions, preserve their technical detail in the
  server logger, and return an actionable blocker without raw exception text.
  Tests verify the diagnostic marker is logged and absent from the blocker.
- Prepared Resume returns a pending recovery result on Unavailable. RestorePrior
  checks the source before changing the journal, staging the prior canonical
  store, saving, or calling Restore.
- Preserve the existing final-write retry branches: when an in-memory completed
  mutation only needs its final phase saved, source Clear/Restore is not replayed.
  A validated reload of a stored final phase likewise remains sufficient. A
  reload retaining only Prepared/RestorePending must obtain valid source evidence.
- Preserve genuinely observed Changed/partial-source rollback. A native control
  removes only the hotbar stack from a prepared source; recovery restores all 10
  source items and the prior canonical quantity, with one Restore and no Clear.
- No native guard, source ownership policy, archive field, default activation,
  current-state export, spent-item restoration rule, or profile-reversal policy
  was changed.

All 30 uncertainty/coordinator cases assert unchanged live phase, confirmed phase
and custody policy result, persisted phase, canonical quantities in memory and on
disk, native quantities (including hidden inventory objects), zero archive writes,
and zero source Clear/Restore calls. Restoring source availability then completes
the intended operation once; another Resume does not repeat its source mutation.
The original guarded Clear/Restore roundtrip also still refuses ordinary native
inventory Clear under Recovery custody.

The same slot-only evidence error was independently reproduced in Restore's final
verification. A native slot modification callback pauses the inventory during
restoration. Before the correction, RestorePrior returned success and confirmed
Restored despite that final blocking inspection. The smallest correction checks
the final inspection's blockers before slot equality. In this case source effects
have already happened: all 10 source items are present and canonical quantity is
0. The corrected operation retains persisted RestorePending and Recovery custody;
after unpausing, Resume confirms Restored without calling Restore a second time.
This does not undo source effects or invent a rollback rule.

Player-facing-ui guided the brief recovery action and sanitized inspection error;
technical exception detail stays in the server log.

## Exact test commands and results

Working directory for all commands: `/home/garward/Games/Games/VintageStory/Modding`.
All runs disabled shared deployment and packaging. No installed-world state changed.

Initial RED (after correcting an ambiguous `OrderedDictionary` fixture import;
the compile error was not counted as reproduction):

```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --no-restore -p:DisableSharedDeploy=true -p:PackageOnRelease=false --filter 'FullyQualifiedName~NativeLegacyInspectionEvidenceTests|FullyQualifiedName~NativeClearMigrationCompatibilityTests' --logger 'console;verbosity=normal' > /tmp/task3b8-red.log 2>&1
```

```text
Test Run Failed.
Total tests: 43
     Passed: 3
     Failed: 40
 Total time: 2.4162 Seconds
```

GREEN for the same 43 cases:

```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --no-restore -p:DisableSharedDeploy=true -p:PackageOnRelease=false --filter 'FullyQualifiedName~NativeLegacyInspectionEvidenceTests|FullyQualifiedName~NativeClearMigrationCompatibilityTests' --logger 'console;verbosity=normal' > /tmp/task3b8-green.log 2>&1
```

```text
Test Run Successful.
Total tests: 43
     Passed: 43
 Total time: 2.3309 Seconds
```

An intermediate run passed 41/43: the two diagnostic assertions exposed that the
test logger was stringifying the logger's `object[]` argument instead of formatting
it. Correcting that fixture formatter yielded the GREEN above; no production
behavior was weakened.

Second RED for final native restore inspection, with compatibility controls:

```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --no-restore -p:DisableSharedDeploy=true -p:PackageOnRelease=false --filter 'FullyQualifiedName~RestoreCannotConfirmMatchingSlotsWhenItsFinalNativeInspectionIsPaused|FullyQualifiedName~FailedFinalWriteRetainsKnownMutationWithoutDuplicateNativeClearOrRestore|FullyQualifiedName~AvailablePartialNativeSourceStillRollsBackAndPreservesEveryItem' --logger 'console;verbosity=normal' > /tmp/task3b8-final-inspection-red.log 2>&1
```

```text
RestoreCannotConfirmMatchingSlotsWhenItsFinalNativeInspectionIsPaused [FAIL]
Assert.False() Failure
Expected: False
Actual:   True
Test Run Failed.
Total tests: 10
     Passed: 9
     Failed: 1
 Total time: 0.6853 Seconds
```

Final focused GREEN, including existing migration, custody policy, and bootstrap:

```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --no-restore -p:DisableSharedDeploy=true -p:PackageOnRelease=false --filter 'FullyQualifiedName~NativeLegacyInspectionEvidenceTests|FullyQualifiedName~NativeClearMigrationCompatibilityTests|FullyQualifiedName~PlayerItemMigrationTests|FullyQualifiedName~PlayerItemCustodyPolicyTests|FullyQualifiedName~PlayerItemCustodyBootstrapTests' --logger 'console;verbosity=normal' > /tmp/task3b8-focused-green.log 2>&1
```

```text
Test Run Successful.
Total tests: 166
     Passed: 166
 Total time: 8.8254 Seconds
```

One full integration suite, after the final source change:

```sh
dotnet test VRPG/tests/VRPG.Tests/VRPG.Tests.csproj --no-restore -p:DisableSharedDeploy=true -p:PackageOnRelease=false --logger 'console;verbosity=minimal' > /tmp/task3b8-full.log 2>&1
```

```text
Passed!  - Failed:     0, Passed:  1450, Skipped:     0, Total:  1450, Duration: 21 s - VRPG.Tests.dll (net10.0)
```

Both final runs exited 0 with no compiler warnings or test errors. `git diff --check`
and the staged diff check passed. Commit required normal sandbox escalation because
the sandbox makes `.git` read-only; the scoped action succeeded.

## Files and self-review

- Production: `PlayerItemMigrationModels.cs`, `VintageStoryLegacyInventorySource.cs`,
  `PlayerItemMigrationCoordinator.cs` under `VRPG/src/Inventory/Migration/`.
- Tests: new `NativeLegacyInventoryFixture.cs`, new
  `NativeLegacyInspectionEvidenceTests.cs`, existing
  `NativeClearMigrationCompatibilityTests.cs`, under `VRPG/tests/VRPG.Tests/`.
- This report; broad audit/flow/runtime documentation belongs to the controller.

Self-review checked every CompareTo consumer and every raw Inspect().Slots
confirmation boundary in the adapter, the preserved final-write branches, the
persisted/live custody assertions, source/canonical quantities, actual mutation
call counts, and the extracted compatibility test. No unresolved implementation
findings. Tests cover a real adapter with controlled native fixtures; they do not
claim arbitrary modded inventory behavior or engine/world runtime parity.

No deployment, push, startup smoke, app control, live player migration, default
menu activation, or installed-world check was performed. The controller explicitly
kept the earlier Task3b7 startup evidence scoped to its prior build. The unrelated
`animation-workbench/idle-ready/` and `overhead/previews/` directories were untouched.
Parent Task3 and the joined no-Vanilla Inventory/Character gate remain open.
