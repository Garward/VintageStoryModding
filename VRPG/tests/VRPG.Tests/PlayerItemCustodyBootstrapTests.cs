using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using VRPG.Inventory;
using VRPG.Inventory.Bridges;
using VRPG.Inventory.Persistence;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Xunit;

namespace VRPG.Tests;

[Collection("EngineItemBirth")]
public sealed class PlayerItemCustodyBootstrapTests
{
    [Fact]
    public void MissingWorldIdentityRefusesAdmissionInsteadOfEscapingAsAnUnprotectedModError()
    {
        using var scope = new LifecycleScope { WorldIdentity = "" };
        Assert.Null(Record.Exception(() => scope.Start(false)));
        Assert.False(Directory.Exists(scope.DataRoot));
        scope.Raise("SaveGameLoaded");
        Assert.Equal(1, scope.ShutdownCalls);
        scope.Raise("SaveGameLoaded");
        Assert.Equal(1, scope.ShutdownCalls);
    }

    [Fact]
    public void InaccessibleFilesystemParentNeverProvesEmptyCustody()
    {
        if (OperatingSystem.IsWindows()) return;
        using var scope = new Scope();
        Directory.CreateDirectory(scope.Root);
        var mode = File.GetUnixFileMode(scope.Root);
        try
        {
            File.SetUnixFileMode(scope.Root, UnixFileMode.None);
            Assert.Equal("Uncertain", Property<object>(Inspect(scope.Storage, false), "Presence").ToString());
        }
        finally { File.SetUnixFileMode(scope.Root, mode); }
        Assert.Empty(Directory.GetFileSystemEntries(scope.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualFactoryAndLifecycleMaterializeOnlyRequestedEmptyWorld(bool requested)
    {
        using var scope = new LifecycleScope();
        scope.Start(requested);
        Assert.False(Directory.Exists(scope.DataRoot));
        if (!requested) { Assert.Null(scope.Module); return; }
        Assert.NotNull(scope.Module);
        Assert.False(scope.Module.IsReady);
        scope.Raise("SaveGameLoaded");
        Assert.True(scope.Module.IsReady);
        Assert.True(File.Exists(scope.Storage.ArchivePath));
        Assert.True(scope.Module.Custody("player").AllowsVanilla);
        Assert.Equal(0, scope.ShutdownCalls);
    }

    [Theory]
    [InlineData(PlayerItemMigrationStatus.Committed, false)]
    [InlineData(PlayerItemMigrationStatus.Prepared, false)]
    [InlineData(PlayerItemMigrationStatus.RestorePending, false)]
    [InlineData(PlayerItemMigrationStatus.Restored, true)]
    public void DisabledSidecarBootRestartAndDisposalKeepConfirmedOwnership(PlayerItemMigrationStatus phase, bool vanilla)
    {
        using var scope = new LifecycleScope();
        var seed = new PlayerItemArchiveService(new(scope.Storage));
        Assert.True(seed.Load().Success);
        seed.StageMigration("player", new InMemoryPlayerItemStore(), new() { MigrationVersion = 1, Status = phase });
        Assert.True(seed.SaveDirty().Success);
        byte[] original = File.ReadAllBytes(scope.Storage.ArchivePath);
        for (int restart = 0; restart < 2; restart++)
        {
            scope.Start(false);
            Assert.NotNull(scope.Module);
            scope.Raise("SaveGameLoaded");
            Assert.Equal(vanilla, scope.Module.Custody("player").AllowsVanilla);
            Assert.False(scope.Module.Custody("player").AllowsCanonical);
            Assert.False(scope.Module.CanActivateNativeProfile);
            Assert.Equal(original, File.ReadAllBytes(scope.Storage.ArchivePath));
            var old = scope.Module;
            scope.Stop();
            Assert.Equal(PlayerItemCustodySnapshot.Unresolved, old.Custody("player"));
            scope.Raise("SaveGameLoaded"); // Disposed listeners cannot reopen the old world.
            Assert.Null(old.Service);
        }
        Assert.Equal(0, scope.ShutdownCalls);
        scope.WorldIdentity = "different-world";
        scope.Start(false);
        Assert.Null(scope.Module);
    }

    [Fact]
    public void DisabledBootRetainsCompletedPickupTombstoneButRefusesFreshPickup()
    {
        using var scope = new LifecycleScope();
        scope.Start(true);
        scope.Raise("SaveGameLoaded");
        var source = new EntityItem { Code = new("game:item"), Itemstack = new(scope.Item, 3) };
        EngineItemFlowPatch.BeforeSpawnPrefix(source);
        byte[] original;
        using (var stream = new MemoryStream())
        {
            using var writer = new BinaryWriter(stream);
            source.ToBytes(writer, false);
            original = stream.ToArray();
        }
        Assert.Equal(PlayerItemBridgeStage.Completed, scope.Module!.Bridges!.Pickup("player", new EngineItemPickupSource(source)).Stage);
        scope.Module.Service!.StageMigration("player", (InMemoryPlayerItemStore)scope.Module.Service!.Directory.ForPlayer("player"),
            new() { MigrationVersion = 1, Status = PlayerItemMigrationStatus.Committed });
        Assert.True(scope.Module.Service.SaveDirty().Success);
        scope.Stop();
        scope.Start(false);
        scope.Raise("SaveGameLoaded");
        var restored = new EntityItem();
        using (var reader = new BinaryReader(new MemoryStream(original))) restored.FromBytes(reader, false);
        Assert.True(restored.Itemstack.ResolveBlockOrItem(scope.World));
        scope.Raise("OnEntityLoaded", restored);
        Assert.Null(restored.Itemstack);
        scope.Raise("OnEntityLoaded", restored);
        var fresh = new EntityItem { Code = new("game:item"), Itemstack = new(scope.Item, 4) };
        EngineItemFlowPatch.BeforeSpawnPrefix(fresh);
        var player = new EntityPlayer();
        player.WatchedAttributes.SetString("playerUID", "player");
        bool result = true;
        Assert.False(EngineItemFlowPatch.OnFoundCollectiblePrefix(
            new Vintagestory.GameContent.EntityBehaviorCollectEntities(player), fresh, ref result));
        Assert.False(result);
        Assert.Equal(4, fresh.Itemstack.StackSize);
        Assert.Equal(3, scope.Module!.Service!.Directory.ForPlayer("player").Query(new("", "", 0, 100)).Entries.Sum(e => e.Quantity));
        Assert.Single(new PlayerItemArchiveRepository(scope.Storage).Load().Archive.Bridges);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void EmptyWorldDecisionDoesNotMaterializeAnArchive(bool requested, bool protect)
    {
        using var scope = new Scope();
        var decision = Inspect(scope.Storage, requested);
        Assert.Equal(protect, Property<bool>(decision, "RequiresProtection"));
        Assert.Equal(requested, Property<bool>(decision, "OperationsRequested"));
        Assert.Equal("Absent", Property<object>(decision, "Presence").ToString());
        Assert.False(Directory.Exists(scope.Root));
        Assert.Equal(0, scope.Legacy.Writes);
    }

    [Theory]
    [InlineData(PlayerItemArchiveRepository.SlotAKey)]
    [InlineData(PlayerItemArchiveRepository.SlotBKey)]
    [InlineData(WorldScopedPlayerItemArchiveStorage.CheckpointKey)]
    public void LegacyEvidenceRequiresInspectionWithoutImporting(string key)
    {
        using var scope = new Scope();
        scope.Legacy.Values[key] = new byte[] { 17 };
        Assert.True(Property<bool>(Inspect(scope.Storage, false), "RequiresProtection"));
        Assert.False(Directory.Exists(scope.Root));
        Assert.Equal(new byte[] { 17 }, scope.Legacy.Values[key]);
        Assert.Equal(0, scope.Legacy.Writes);
    }

    [Theory]
    [InlineData("deleted")]
    [InlineData("corrupt")]
    [InlineData("incompatible")]
    [InlineData("checkpoint")]
    public void ExistingCustodyCannotMasqueradeAsEmptyOrBeRewrittenByProbe(string damage)
    {
        using var scope = new Scope();
        Assert.Null(scope.Storage.Read(PlayerItemArchiveRepository.SlotAKey));
        switch (damage)
        {
            case "deleted": File.Delete(scope.Storage.ArchivePath); break;
            case "corrupt": File.WriteAllText(scope.Storage.ArchivePath, "broken"); break;
            case "incompatible": File.WriteAllText(scope.Storage.ArchivePath, "{\"Format\":99}"); break;
            case "checkpoint": scope.Legacy.Values[WorldScopedPlayerItemArchiveStorage.CheckpointKey] = new byte[] { 9 }; break;
        }
        byte[]? before = File.Exists(scope.Storage.ArchivePath) ? File.ReadAllBytes(scope.Storage.ArchivePath) : null;
        Assert.True(Property<bool>(Inspect(scope.Storage, false), "RequiresProtection"));
        var service = new PlayerItemArchiveService(new PlayerItemArchiveRepository(scope.Storage));
        Assert.False(service.Load().Success);
        Assert.Equal(before, File.Exists(scope.Storage.ArchivePath) ? File.ReadAllBytes(scope.Storage.ArchivePath) : null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadErrorsAndObstructedDirectoriesAreUncertainty(bool legacyError)
    {
        using var scope = new Scope();
        if (legacyError) scope.Legacy.FailReads = true;
        else File.WriteAllText(scope.Root, "not a directory");
        var decision = Inspect(scope.Storage, false);
        Assert.True(Property<bool>(decision, "RequiresProtection"));
        Assert.Equal("Uncertain", Property<object>(decision, "Presence").ToString());
        Assert.False(File.Exists(scope.Storage.ArchivePath));
    }

    [Fact]
    public void AnotherWorldsDirectoryDoesNotRememberCustodyAcrossDecisions()
    {
        using var scope = new Scope();
        Assert.Null(scope.Storage.Read(PlayerItemArchiveRepository.SlotAKey));
        Assert.True(Property<bool>(Inspect(scope.Storage, false), "RequiresProtection"));
        var other = new WorldScopedPlayerItemArchiveStorage(scope.Root, "other-world", new LegacyStorage());
        Assert.False(Property<bool>(Inspect(other, false), "RequiresProtection"));
        Assert.False(Directory.Exists(Path.GetDirectoryName(other.ArchivePath)));
    }

    private static object Inspect(WorldScopedPlayerItemArchiveStorage storage, bool requested)
    {
        return PlayerItemCustodyBootstrap.Inspect(storage, requested);
    }

    private static T Property<T>(object value, string name) => (T)value.GetType().GetProperty(name)!.GetValue(value)!;

    private sealed class Scope : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "vrpg-bootstrap-" + Guid.NewGuid().ToString("N"));
        public LegacyStorage Legacy { get; } = new();
        public WorldScopedPlayerItemArchiveStorage Storage { get; }
        public Scope() => Storage = new(Root, "world", Legacy);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            else if (File.Exists(Root)) File.Delete(Root);
        }
    }

    private sealed class LegacyStorage : IPlayerItemArchiveStorage
    {
        public Dictionary<string, byte[]> Values { get; } = new();
        public bool FailReads;
        public int Writes;
        public byte[]? Read(string key) => FailReads ? throw new IOException("Read unavailable") : Values.GetValueOrDefault(key);
        public void Write(string key, byte[] data) { Values[key] = data; Writes++; }
    }

    private sealed class LifecycleScope : IDisposable
    {
        private readonly string previousDataPath = GamePaths.DataPath;
        private readonly Dictionary<string, Delegate?> events = new();
        private readonly LegacyStorage legacy = new();
        private readonly Harmony harmony = new("vrpg.tests.bootstrap." + Guid.NewGuid());
        public string DataRoot { get; } = Path.Combine(Path.GetTempPath(), "vrpg-boot-runtime-" + Guid.NewGuid().ToString("N"));
        public string WorldIdentity = "world";
        public WorldScopedPlayerItemArchiveStorage Storage => new(Path.Combine(DataRoot, "VRPG/player-items"), WorldIdentity, legacy);
        public PlayerItemPersistenceModule? Module;
        public int ShutdownCalls;
        public Item Item { get; } = new() { Code = new("game:stone"), MaxStackSize = 64 };
        public IWorldAccessor World { get; }
        public ICoreServerAPI Api { get; }
        public LifecycleScope()
        {
            GamePaths.DataPath = DataRoot;
            var save = PlayerDefenseServiceTests.Proxy.Create<ISaveGame>((name, args) => name switch
            {
                "get_SavegameIdentifier" => WorldIdentity,
                "GetData" => legacy.Read((string)args![0]!),
                "StoreData" => Store((string)args![0]!, (byte[])args[1]!),
                _ => null
            });
            var manager = PlayerDefenseServiceTests.Proxy.Create<IWorldManagerAPI>((name, _) => name == "get_SaveGame" ? save : null);
            var eventApi = PlayerDefenseServiceTests.Proxy.Create<IServerEventAPI>((name, args) =>
            {
                if (name.StartsWith("add_")) events[name[4..]] = Delegate.Combine(events.GetValueOrDefault(name[4..]), (Delegate)args![0]!);
                if (name.StartsWith("remove_")) events[name[7..]] = Delegate.Remove(events.GetValueOrDefault(name[7..]), (Delegate)args![0]!);
                return null;
            });
            var server = PlayerDefenseServiceTests.Proxy.Create<IServerAPI>((name, _) => { if (name == "ShutDown") ShutdownCalls++; return null; });
            var logger = PlayerDefenseServiceTests.Proxy.Create<ILogger>((_, _) => null);
            World = PlayerDefenseServiceTests.Proxy.Create<IServerWorldAccessor>((name, _) => name == "GetItem" ? Item : null);
            IChatCommand? command = null;
            command = PlayerDefenseServiceTests.Proxy.Create<IChatCommand>((_, _) => command);
            CommandArgumentParsers? parsers = null;
            var chat = PlayerDefenseServiceTests.Proxy.Create<IChatCommandApi>((name, _) => name == "get_Parsers" ? parsers : command);
            Api = PlayerDefenseServiceTests.Proxy.Create<ICoreServerAPI>((name, _) => name switch
            {
                "get_WorldManager" => manager, "get_Event" => eventApi, "get_Server" => server,
                "get_Logger" => logger, "get_ChatCommands" => chat, "get_World" => World,
                "GetOrCreateDataPath" => throw new InvalidOperationException("Bootstrap must use its probed storage path."),
                _ => null
            });
            parsers = new(Api);
        }
        private object? Store(string key, byte[] data) { legacy.Write(key, data); return null; }
        public void Start(bool requested)
        {
            Module = PlayerItemCustodyBootstrap.CreateModule(Api, requested);
            Module?.StartServerSide(Api);
            Module?.InstallProtection(harmony);
        }
        public void Raise(string name, params object[] args) => events.GetValueOrDefault(name)?.DynamicInvoke(args);
        public void Stop()
        {
            Module?.Dispose();
            Module = null;
            harmony.UnpatchAll(harmony.Id);
            NativeInventoryPacketGuardPatch.Reset();
            EngineItemFlowPatch.Reset();
        }
        public void Dispose()
        {
            Stop();
            GamePaths.DataPath = previousDataPath;
            if (Directory.Exists(DataRoot)) Directory.Delete(DataRoot, true);
        }
    }
}
