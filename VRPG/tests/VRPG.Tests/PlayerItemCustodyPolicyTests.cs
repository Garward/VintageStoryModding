using System;
using System.Collections.Generic;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Inventory.Bridges;
using VRPG.Inventory.Migration;
using VRPG.Inventory.Persistence;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class PlayerItemCustodyPolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequiredProtectionLossStopsBootstrapBeforeArchiveLoadOrCheckpointWrite(bool pickup)
    {
        using var scope = new ModuleScope(false);
        scope.Load(PlayerItemMigrationStatus.Committed);
        if (pickup)
            scope.Harmony.Unpatch(AccessTools.Method(typeof(Vintagestory.GameContent.EntityBehaviorCollectEntities), "OnFoundCollectible"),
                HarmonyPatchType.Prefix, scope.Harmony.Id);
        else NativeInventoryPacketGuardPatch.Reset();
        int writes = scope.Storage.Writes;
        scope.Storage.FailReads = true;
        scope.LoadWorld();
        Assert.Equal(1, scope.ShutdownCalls);
        Assert.False(scope.Module.IsReady);
        scope.SaveWorld();
        scope.LoadWorld();
        Assert.Equal(writes, scope.Storage.Writes);
        Assert.Equal(1, scope.ShutdownCalls);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
    }

    [Fact]
    public void UnreadableBootstrapArchiveStopsAdmissionInsteadOfLeavingUnknownTombstonesLive()
    {
        using var scope = new ModuleScope(false);
        scope.Storage.FailReads = true;
        scope.LoadWorld();
        Assert.Equal(1, scope.ShutdownCalls);
        Assert.False(scope.Module.IsReady);
        Assert.Equal(0, scope.Storage.Writes);
    }

    [Theory]
    [InlineData(PlayerItemMigrationStatus.Committed, PlayerItemCustodyOwner.Canonical, false)]
    [InlineData(PlayerItemMigrationStatus.Prepared, PlayerItemCustodyOwner.Recovery, false)]
    [InlineData(PlayerItemMigrationStatus.RestorePending, PlayerItemCustodyOwner.Recovery, false)]
    [InlineData(PlayerItemMigrationStatus.Restored, PlayerItemCustodyOwner.Vanilla, true)]
    public void DisabledOperationsPreserveConfirmedCustodyAndNativePacketDecision(
        PlayerItemMigrationStatus phase, PlayerItemCustodyOwner owner, bool vanilla)
    {
        using var scope = new ModuleScope(false);
        scope.Load(phase);
        Assert.Equal(owner, scope.Module.Custody("player").Owner);
        Assert.Equal(vanilla, scope.Module.Custody("player").AllowsVanilla);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        Assert.False(scope.Module.CanActivateNativeProfile);
        var nativeType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemInventory");
        var native = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(nativeType);
        var fixture = new NativeInventoryGuardFixture(native);
        AccessTools.Method(nativeType, "HandleToolMode").Invoke(native, new object[] { fixture.Packet("HandleToolMode"), fixture.Client });
        Assert.Equal(vanilla ? 1 : 0, fixture.Item.ToolCalls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, true, false)]
    public void ActualMigrationFinalConfirmationFailureRefusesUntilSavedWithoutReplayingSource(bool restore, bool writeThenThrow, bool reload, bool requested = true)
    {
        using var scope = new ModuleScope(requested);
        scope.Load();
        var source = new LegacySource();
        var migration = new PlayerItemMigrationCoordinator(scope.Service);
        if (restore) Assert.True(migration.Commit(source).Success);
        scope.Storage.FailOnWriteNumber = scope.Storage.Attempts + 2;
        scope.Storage.WriteThenThrow = writeThenThrow;
        var result = restore ? migration.RestorePrior(source) : migration.Commit(source);
        Assert.False(result.Success);
        Assert.True(result.RecoveryRequired);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        var nativeType = AccessTools.TypeByName("Vintagestory.Server.ServerSystemInventory");
        var native = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(nativeType);
        var fixture = new NativeInventoryGuardFixture(native);
        AccessTools.Method(nativeType, "HandleToolMode").Invoke(native, new object[] { fixture.Packet("HandleToolMode"), fixture.Client });
        Assert.Equal(0, fixture.Item.ToolCalls);
        int cleared = source.ClearCalls, restored = source.RestoreCalls;
        if (!reload)
        {
            scope.Storage.FailWrites = true;
            Assert.False(migration.Resume(source).Success);
            Assert.False(scope.Module.Custody("player").AllowsCanonical);
            Assert.False(scope.Module.Custody("player").AllowsVanilla);
            scope.Storage.FailWrites = false;
        }
        scope.Storage.FailOnWriteNumber = -1;
        if (reload) Assert.True(scope.Service.Load().Success);
        Assert.True(migration.Resume(source).Success);
        Assert.Equal(cleared, source.ClearCalls);
        Assert.Equal(restored, source.RestoreCalls);
        Assert.Equal(restore, scope.Module.Custody("player").AllowsVanilla);
        Assert.Equal(!restore && requested, scope.Module.Custody("player").AllowsCanonical);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AmbiguousPrepareFailureCannotAuthorizeVanillaFromAbortedLiveMetadata(bool writeThenThrow)
    {
        using var scope = new ModuleScope();
        scope.Load();
        var source = new LegacySource();
        scope.Storage.FailOnWriteNumber = 1;
        scope.Storage.WriteThenThrow = writeThenThrow;
        var migration = new PlayerItemMigrationCoordinator(scope.Service);
        var failed = migration.Commit(source);
        Assert.False(failed.Success);
        Assert.True(failed.RecoveryRequired);
        Assert.Null(scope.Service.Migration("player"));
        Assert.Equal(0, source.ClearCalls);
        Assert.Equal(5, source.Quantity);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        scope.Storage.FailOnWriteNumber = -1;
        Assert.True(migration.Resume(source).Success);
        Assert.True(scope.Module.Custody("player").AllowsVanilla);
        Assert.Equal(0, source.ClearCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownNoWritePremigrationFailureRetainsVerifiedVanilla(bool repositoryReadFailure)
    {
        using var scope = new ModuleScope();
        scope.Load();
        var source = new LegacySource { Blocked = !repositoryReadFailure };
        scope.Storage.FailReads = repositoryReadFailure;
        Assert.False(new PlayerItemMigrationCoordinator(scope.Service).Commit(source).Success);
        Assert.True(scope.Module.Custody("player").AllowsVanilla);
        Assert.Equal(0, scope.Storage.Attempts);
        Assert.Equal(0, source.ClearCalls);
    }

    [Fact]
    public void MutableJournalReferenceCannotChangeConfirmedOwnership()
    {
        using var scope = new ModuleScope();
        scope.Load(PlayerItemMigrationStatus.Committed);
        scope.Service.Migration("player")!.Status = PlayerItemMigrationStatus.Restored;
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
    }

    [Fact]
    public void MutablePhaseDriftCannotUseNoOpSaveAsConfirmation()
    {
        using var scope = new ModuleScope();
        scope.Load(PlayerItemMigrationStatus.Committed);
        scope.Service.Migration("player")!.Status = PlayerItemMigrationStatus.Restored;
        int attempts = scope.Storage.Attempts;
        scope.Storage.FailWrites = true;
        Assert.False(scope.Service.SaveDirty().Success);
        Assert.Equal(attempts + 1, scope.Storage.Attempts);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
    }

    [Fact]
    public void LostIdentityAndWorldOrConnectionResetCannotReuseCanonicalEvidence()
    {
        var policy = new PlayerItemCustodyPolicy();
        policy.Observe("a", true, PlayerItemMigrationStatus.Committed, true, true);
        Assert.Equal(PlayerItemCustodyOwner.Canonical, policy.Observe("a", false, null, false, false).Owner);
        Assert.False(policy.Observe("a", false, null, true, true).AllowsCanonical);
        Assert.Equal(PlayerItemCustodyOwner.Recovery, policy.Observe("b", false, null, true, true).Owner);
        Assert.Equal(PlayerItemCustodyOwner.Recovery, policy.Observe(" ", true, null, true, true).Owner);
        policy.Forget("a");
        Assert.Equal(PlayerItemCustodyOwner.Recovery, policy.Observe("a", false, null, true, true).Owner);
        policy.Observe("a", true, PlayerItemMigrationStatus.Committed, true, true);
        policy.Reset();
        Assert.Equal(PlayerItemCustodyOwner.Recovery, policy.Observe("a", false, null, true, true).Owner);
    }

    [Fact]
    public void LivePersistenceModuleCannotTreatUnavailableArchiveAsNoMigration()
    {
        using var module = new ModuleScope();
        Assert.Equal(PlayerItemCustodySnapshot.Unresolved, module.Module.Custody("player"));
    }

    [Fact]
    public void CursorRecoveryCannotBeReopenedByRestoredPhaseOrByAnotherPlayer()
    {
        using var scope = new ModuleScope();
        scope.Load(PlayerItemMigrationStatus.Committed);
        Assert.True(scope.Module.Custody("player").AllowsCanonical);
        scope.Module.RequireCursorRecovery("player");
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        scope.Service.SetMigrationStatus("player", PlayerItemMigrationStatus.Restored);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        Assert.True(scope.Module.Custody("other").AllowsVanilla);
        Assert.False(scope.Module.CanActivateNativeProfile);
    }

    [Fact]
    public void ResolvingCustodyDoesNotReplayReconciliationOrWriteReceipts()
    {
        using var scope = new ModuleScope();
        scope.Load(PlayerItemMigrationStatus.Committed);
        var bridge = scope.Module.Bridges!;
        bridge.RegisterClaimProducer("reward", PlayerItemBridgeOperation.Reward);
        Assert.Equal(PlayerItemBridgeStage.Completed, bridge.RegisterClaim(new("claim", "player", PlayerItemBridgeOperation.Reward,
            "reward", "", 3, "objective", "canonical", 0), new ItemStack(scope.Item, 3)).Stage);
        int writes = scope.Storage.Writes;
        Assert.True(scope.Module.Custody("player").AllowsCanonical);
        Assert.Equal(writes, scope.Storage.Writes);
    }

    [Fact]
    public void FailedHealthAndUnsavedCommittedPhaseCannotInventCanonicalOwnership()
    {
        using var scope = new ModuleScope();
        scope.Load();
        Assert.True(scope.Module.Custody("player").AllowsVanilla);
        scope.Storage.FailWrites = true;
        scope.SaveWorld();
        Assert.False(scope.Module.IsReady);
        Assert.True(scope.Module.Custody("player").AllowsVanilla);
        scope.Service.StageMigration("player", new InMemoryPlayerItemStore(), new() { MigrationVersion = 1, Status = PlayerItemMigrationStatus.Committed });
        Assert.Equal(PlayerItemCustodyOwner.Recovery, scope.Module.Custody("player").Owner);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        scope.Storage.FailReads = true;
        Assert.False(scope.Service.Load().Success);
        Assert.Equal(PlayerItemCustodyOwner.Recovery, scope.Module.Custody("player").Owner);
        scope.Module.Dispose();
        Assert.Equal(PlayerItemCustodySnapshot.Unresolved, scope.Module.Custody("player"));
    }

    [Fact]
    public void GuardRevocationWithdrawsCanonicalOperationsWithoutOpeningVanilla()
    {
        using var scope = new ModuleScope();
        scope.Load(PlayerItemMigrationStatus.Committed);
        Assert.True(scope.Module.Custody("player").AllowsCanonical);
        scope.Harmony.UnpatchAll(scope.Harmony.Id);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        Assert.False(scope.Module.CanActivateNativeProfile);
    }

    [Fact]
    public void PickupPrefixRevocationAlsoWithdrawsCanonicalOperations()
    {
        using var scope = new ModuleScope();
        scope.Load(PlayerItemMigrationStatus.Committed);
        Assert.True(scope.Module.Custody("player").AllowsCanonical);
        var target = AccessTools.Method(typeof(Vintagestory.GameContent.EntityBehaviorCollectEntities), "OnFoundCollectible");
        scope.Harmony.Unpatch(target, HarmonyPatchType.Prefix, scope.Harmony.Id);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
    }

    private sealed class ModuleScope : IDisposable
    {
        public Harmony Harmony { get; } = new("vrpg.tests.custody." + Guid.NewGuid());
        public PlayerItemPersistenceModule Module { get; }
        public MemoryStorage Storage { get; } = new();
        public PlayerItemArchiveService Service { get; }
        public Item Item { get; } = new() { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        public int ShutdownCalls;
        public ModuleScope(bool requested = true)
        {
            Module = new(requested);
            Service = new(new PlayerItemArchiveRepository(Storage));
            AccessTools.Field(typeof(PlayerItemPersistenceModule), "<Service>k__BackingField").SetValue(Module, Service);
            var logger = PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
            var events = PlayerDefenseServiceTests.Proxy.Create<IServerEventAPI>((_, _) => null);
            var server = PlayerDefenseServiceTests.Proxy.Create<IServerAPI>((method, _) =>
            { if (method == "ShutDown") ShutdownCalls++; return null; });
            var api = PlayerDefenseServiceTests.Proxy.Create<ICoreServerAPI>((method, _) => method switch
            { "get_Logger" => logger, "get_Event" => events, "get_Server" => server, _ => null });
            AccessTools.Field(typeof(PlayerItemPersistenceModule), "api").SetValue(Module, api);
            NativeInventoryPacketGuardPatch.CustodyResolver = Module.Custody;
            NativeInventoryPacketGuardPatch.CursorRecoveryObserver = Module.RequireCursorRecovery;
            NativeInventoryPacketGuardPatch.Patch(Harmony, logger);
            EngineItemFlowPatch.Patch(Harmony, logger);
        }
        public void Load(PlayerItemMigrationStatus? phase = null)
        {
            Assert.True(Service.Load().Success);
            if (phase.HasValue)
            {
                Service.StageMigration("player", new InMemoryPlayerItemStore(), new() { MigrationVersion = 1, Status = phase.Value });
                Assert.True(Service.SaveDirty().Success);
            }
            var resolver = PlayerDefenseServiceTests.Proxy.Create<ICollectibleResolver>((method, _) => method == "GetItem" ? Item : null);
            AccessTools.Field(typeof(PlayerItemPersistenceModule), "<Bridges>k__BackingField").SetValue(Module, new PlayerItemBridgeCoordinator(Service, resolver));
        }
        public void SaveWorld() => AccessTools.Method(typeof(PlayerItemPersistenceModule), "OnGameWorldSave").Invoke(Module, null);
        public void LoadWorld() => AccessTools.Method(typeof(PlayerItemPersistenceModule), "OnSaveGameLoaded").Invoke(Module, null);
        public void Dispose()
        {
            AccessTools.Field(typeof(PlayerItemPersistenceModule), "api").SetValue(Module, null);
            Module.Dispose();
            Harmony.UnpatchAll(Harmony.Id);
            NativeInventoryPacketGuardPatch.Reset();
            EngineItemFlowPatch.Reset();
        }
    }

    private sealed class MemoryStorage : IDurablePlayerItemArchiveStorage
    {
        private readonly Dictionary<string, byte[]> slots = new();
        public bool IsCrashDurable => true;
        public bool FailWrites;
        public bool FailReads;
        public int Writes;
        public int Attempts;
        public int FailOnWriteNumber = -1;
        public bool WriteThenThrow;
        public byte[]? Read(string key) => FailReads ? throw new InvalidOperationException("read unavailable") : slots.GetValueOrDefault(key);
        public void Write(string key, byte[] bytes)
        {
            bool fail = ++Attempts == FailOnWriteNumber || FailWrites;
            if (fail && !WriteThenThrow) throw new InvalidOperationException("write unavailable");
            slots[key] = (byte[])bytes.Clone();
            Writes++;
            if (fail) throw new InvalidOperationException("write reached storage but confirmation unavailable");
        }
    }

    private sealed class LegacySource : ILegacyPlayerInventorySource
    {
        public string PlayerUid => "player";
        public int Quantity = 5;
        public int ClearCalls;
        public int RestoreCalls;
        public bool Blocked;
        public LegacyInventoryInspection Inspect() => new(PlayerUid,
            Quantity == 0 ? Array.Empty<LegacyItemSlotState>() : new[] { new LegacyItemSlotState
            {
                InventoryClass = "hotbar", Quantity = Quantity, Prototype = new(PlayerItemStorageKind.Aggregate,
                    "game:stone", PlayerItemCategories.Materials, "Stone", new byte[] { 1 })
            } }, Blocked ? new[] { "test validation refusal" } : Array.Empty<string>());
        public LegacyInventoryState CompareTo(LegacyItemSlotState[] snapshot) => Quantity == 0
            ? LegacyInventoryState.Empty : Quantity == snapshot[0].Quantity ? LegacyInventoryState.MatchesSnapshot : LegacyInventoryState.Changed;
        public LegacyInventoryMutationResult Clear(LegacyItemSlotState[] expected)
        { ClearCalls++; Quantity = 0; return LegacyInventoryMutationResult.Accepted(); }
        public LegacyInventoryMutationResult Restore(LegacyItemSlotState[] snapshot)
        { RestoreCalls++; Quantity = snapshot[0].Quantity; return LegacyInventoryMutationResult.Accepted(); }
    }

    [Theory]
    [InlineData(true, null, true, true, "Vanilla", true, false)]
    [InlineData(true, PlayerItemMigrationStatus.Restored, true, true, "Vanilla", true, false)]
    [InlineData(true, PlayerItemMigrationStatus.Prepared, true, true, "Recovery", false, false)]
    [InlineData(true, PlayerItemMigrationStatus.RestorePending, true, true, "Recovery", false, false)]
    [InlineData(true, PlayerItemMigrationStatus.Committed, true, true, "Canonical", false, true)]
    [InlineData(true, PlayerItemMigrationStatus.Committed, false, true, "Canonical", false, false)]
    [InlineData(true, PlayerItemMigrationStatus.Committed, true, false, "Canonical", false, false)]
    [InlineData(true, null, false, false, "Vanilla", true, false)]
    [InlineData(false, null, true, true, "Recovery", false, false)]
    [InlineData(true, (PlayerItemMigrationStatus)999, true, true, "Recovery", false, false)]
    public void OwnershipRequiresValidatedPhaseAndCanonicalWritesRequireHealth(bool loaded,
        PlayerItemMigrationStatus? phase, bool healthy, bool bridges, string owner, bool vanilla, bool canonical)
    {
        var result = new PlayerItemCustodyPolicy().Observe("player", loaded, phase, healthy, bridges);
        Assert.Equal(owner, result.Owner.ToString());
        Assert.Equal(vanilla, result.AllowsVanilla);
        Assert.Equal(canonical, result.AllowsCanonical);
    }
}
