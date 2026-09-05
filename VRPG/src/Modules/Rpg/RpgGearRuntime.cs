using System;
using System.Collections.Generic;
using VRPG.Core;
using VRPG.Inventory;
using VRPG.Config;
using VRPG.Data;
using VRPG.Inventory.Gear;
using VRPG.Inventory.Persistence;
using VRPG.Modules.Rpg.Stats;
using VRPG.Modules.Rpg.Combat;
using VRPG.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace VRPG.Modules.Rpg;

public sealed class RpgGearRuntime : IDisposable
{
    private readonly RpgModuleConfig config;
    private readonly VRPGDataRegistry data;
    private readonly Func<PlayerItemArchiveService?> archive;
    private readonly EquippedGearStatResolver stats = new();
    private readonly Dictionary<string, IPlayerItemStore> snapshotStores = new(StringComparer.OrdinalIgnoreCase);
    private PlayerItemArchiveService? loadedArchive;
    private GearPrototypeFactory? prototypes;
    private bool ready, disposed;
    private System.Func<Entity, DamageSource, float, float?>? outgoingResolver;
    private System.Func<Entity, DamageSource, float, float>? incomingResolver;
    private System.Func<Entity, bool>? defenseActive;

    public RpgGearRuntime(RpgModuleConfig config, VRPGDataRegistry data, Func<PlayerItemArchiveService?> archive)
    {
        this.config = config;
        this.data = data;
        this.archive = archive;
        Equipment = new GearEquipmentService(data);
    }

    public GearEquipmentService Equipment { get; }
    public StarterGearSetFactory? Starters { get; private set; }
    public bool IsReady => !disposed && ready && config.Enabled && config.InventoryReplacement.Enabled
        && loadedArchive?.IsLoaded == true && ReferenceEquals(loadedArchive, archive());

    public GearReadinessResult ValidateAndRecover(System.Func<string, bool> chassisExists)
    {
        ready = false;
        stats.Clear();
        snapshotStores.Clear();
        loadedArchive = null;
        if (disposed || !config.Enabled || !config.InventoryReplacement.Enabled || archive() is not { IsLoaded: true } service)
            return new(false, 0, 0, "Player-item persistence is unavailable.");
        var result = GearReadinessValidator.ValidateAndRecover(service, data, chassisExists, config.GearFallbackChassisAssetCode);
        if (!result.Ready) return result;
        loadedArchive = service;
        prototypes = new GearPrototypeFactory(chassisExists, config.GearFallbackChassisAssetCode);
        Starters = new StarterGearSetFactory(data, new GearInstanceAuthor(data), prototypes);
        ready = true;
        return result;
    }

    public EquippedGearStats ResolveStats(string uid)
    {
        if (!IsReady || string.IsNullOrWhiteSpace(uid)) return EquippedGearStats.Invalid;
        IPlayerItemStore store = loadedArchive!.Directory.ForPlayer(uid);
        if (!snapshotStores.TryGetValue(uid, out var prior) || !ReferenceEquals(prior, store)) stats.RemovePlayer(uid);
        snapshotStores[uid] = store;
        GearEquipmentSnapshot snapshot = Equipment.Snapshot(store);
        return stats.Resolve(uid, store.Revision, snapshot);
    }

    public PlayerItemPrototype Materialize(PlayerItemPrototype prototype) => IsReady
        ? prototypes!.Materialize(prototype) : throw new InvalidOperationException("Gear is unavailable.");

    public bool Owns(EntityPlayer player) => IsReady && player.Player != null && CharacterRuntimeMode.IsOwned(
        config.Enabled, config.CharacterRuntime.Enabled, player.Player.WorldData.CurrentGameMode);

    public void RemovePlayer(string uid)
    {
        snapshotStores.Remove(uid);
        stats.RemovePlayer(uid);
    }

    public void BindDamageResolvers(System.Func<Entity, DamageSource, float, float?> outgoing,
        System.Func<Entity, DamageSource, float, float> incoming)
    {
        if (disposed) throw new ObjectDisposedException(nameof(RpgGearRuntime));
        outgoingResolver = (target, source, damage) => IsReady ? outgoing(target, source, damage) : null;
        defenseActive = target => target is EntityPlayer player && Owns(player) && ResolveStats(player.PlayerUID).Valid;
        incomingResolver = (target, source, damage) => defenseActive(target) ? incoming(target, source, damage) : damage;
        EntityDamageScalingPatch.PlayerOutgoingDamageResolver = outgoingResolver;
        EntityDamageScalingPatch.PlayerDamageResolver = incomingResolver;
        EntityDamageScalingPatch.PlayerDefenseActive = defenseActive;
    }

    public void Dispose()
    {
        if (EntityDamageScalingPatch.PlayerOutgoingDamageResolver == outgoingResolver) EntityDamageScalingPatch.PlayerOutgoingDamageResolver = null;
        if (EntityDamageScalingPatch.PlayerDamageResolver == incomingResolver) EntityDamageScalingPatch.PlayerDamageResolver = null;
        if (EntityDamageScalingPatch.PlayerDefenseActive == defenseActive) EntityDamageScalingPatch.PlayerDefenseActive = null;
        disposed = true;
        ready = false;
        stats.Clear();
        snapshotStores.Clear();
        loadedArchive = null;
        Starters = null;
        prototypes = null;
    }
}
