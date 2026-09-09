using System;
using System.Collections.Generic;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Inventory.Bridges;
using VRPG.Inventory.Persistence;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class PlayerItemCustodyPolicyTests
{
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
    public void FailureBeforeMigrationPreservesVanillaButCommittedCustodyStaysReadOnly()
    {
        using var scope = new ModuleScope();
        scope.Load();
        Assert.True(scope.Module.Custody("player").AllowsVanilla);
        scope.Storage.FailWrites = true;
        scope.SaveWorld();
        Assert.False(scope.Module.IsReady);
        Assert.True(scope.Module.Custody("player").AllowsVanilla);
        scope.Service.StageMigration("player", new InMemoryPlayerItemStore(), new() { MigrationVersion = 1, Status = PlayerItemMigrationStatus.Committed });
        Assert.Equal(PlayerItemCustodyOwner.Canonical, scope.Module.Custody("player").Owner);
        Assert.False(scope.Module.Custody("player").AllowsCanonical);
        Assert.False(scope.Module.Custody("player").AllowsVanilla);
        scope.Storage.FailReads = true;
        Assert.False(scope.Service.Load().Success);
        Assert.Equal(PlayerItemCustodyOwner.Canonical, scope.Module.Custody("player").Owner);
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
        public PlayerItemPersistenceModule Module { get; } = new();
        public MemoryStorage Storage { get; } = new();
        public PlayerItemArchiveService Service { get; }
        public Item Item { get; } = new() { Code = new AssetLocation("game:stone"), MaxStackSize = 64 };
        public ModuleScope()
        {
            Service = new(new PlayerItemArchiveRepository(Storage));
            AccessTools.Field(typeof(PlayerItemPersistenceModule), "<Service>k__BackingField").SetValue(Module, Service);
            var logger = PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
            var events = PlayerDefenseServiceTests.Proxy.Create<IServerEventAPI>((_, _) => null);
            var api = PlayerDefenseServiceTests.Proxy.Create<ICoreServerAPI>((method, _) => method switch
            { "get_Logger" => logger, "get_Event" => events, _ => null });
            AccessTools.Field(typeof(PlayerItemPersistenceModule), "api").SetValue(Module, api);
            NativeInventoryPacketGuardPatch.CustodyResolver = Module.Custody;
            NativeInventoryPacketGuardPatch.CursorRecoveryObserver = Module.RequireCursorRecovery;
            NativeInventoryPacketGuardPatch.Patch(Harmony, logger);
            EngineItemFlowPatch.Patch(Harmony, logger);
        }
        public void Load(PlayerItemMigrationStatus? phase = null)
        {
            Assert.True(Service.Load().Success);
            if (phase.HasValue) Service.StageMigration("player", new InMemoryPlayerItemStore(), new() { MigrationVersion = 1, Status = phase.Value });
            var resolver = PlayerDefenseServiceTests.Proxy.Create<ICollectibleResolver>((method, _) => method == "GetItem" ? Item : null);
            AccessTools.Field(typeof(PlayerItemPersistenceModule), "<Bridges>k__BackingField").SetValue(Module, new PlayerItemBridgeCoordinator(Service, resolver));
        }
        public void SaveWorld() => AccessTools.Method(typeof(PlayerItemPersistenceModule), "OnGameWorldSave").Invoke(Module, null);
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
        public byte[]? Read(string key) => FailReads ? throw new InvalidOperationException("read unavailable") : slots.GetValueOrDefault(key);
        public void Write(string key, byte[] bytes)
        {
            if (FailWrites) throw new InvalidOperationException("write unavailable");
            slots[key] = (byte[])bytes.Clone();
            Writes++;
        }
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
