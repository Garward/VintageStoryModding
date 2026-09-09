using System;
using Vintagestory.API.Common;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VRPG.Inventory.Bridges;

/// <summary>Activation must name every exposed producer; an installed manager prefix is not coverage.</summary>
public sealed class PlayerItemBridgeCoverage
{
    private readonly Dictionary<string, System.Func<bool>> guards = new(StringComparer.Ordinal);
    public void Register(string producer, System.Func<bool> isReady) => guards.Add(producer, isReady);
    public bool CanActivate(IEnumerable<string> exposedProducers)
    {
        var producers = exposedProducers.Distinct(StringComparer.Ordinal).ToArray();
        return producers.Length > 0 && producers.All(code => guards.TryGetValue(code, out var ready) && ready());
    }
}

/// <summary>Only explicitly registered, audited server adapters may mutate an external destination.</summary>
public sealed class EngineItemBridgeAdapter(string code, PlayerItemBridgeOperation operation,
    System.Func<PlayerItemBridgeReceipt, ItemStack, PlayerItemExternalResult> apply,
    System.Func<PlayerItemBridgeReceipt, PlayerItemExternalResult> reconcile)
{
    public string Code { get; } = code;
    public PlayerItemBridgeOperation Operation { get; } = operation;
    public PlayerItemExternalResult Apply(PlayerItemBridgeReceipt receipt, ItemStack stack) => apply(receipt.Clone(), stack);
    public PlayerItemExternalResult Reconcile(PlayerItemBridgeReceipt receipt) => reconcile(receipt.Clone());
}

public interface IEngineItemPickupSource
{
    string Token { get; }
    bool HasPersistedIdentity { get; }
    ItemStack? Stack { get; }
    void Remove();
}

/// <summary>A loaded birth token is persisted evidence; a new or newly tagged source cannot yet transfer.</summary>
public sealed class EngineItemPickupSource(EntityItem entity) : IEngineItemPickupSource
{
    public const string BirthTokenAttribute = "vrpg:item-birth";
    private static readonly ConditionalWeakTable<EntityItem, Identity> Identities = new();
    public string Token => entity.WatchedAttributes.GetString(BirthTokenAttribute, "");
    public bool HasPersistedIdentity => Identities.TryGetValue(entity, out var identity)
        && identity.Persisted && identity.Token == Token && Stack != null
        && identity.Snapshot == JsonConvert.SerializeObject(ItemStackCodec.Encode(Stack));
    public ItemStack? Stack => entity.Slot.Itemstack;
    public void Remove()
    {
        entity.Itemstack = null;
        entity.Die(EnumDespawnReason.PickedUp);
    }
    public static void Observe(EntityItem entity, bool loaded)
    {
        string token = entity.WatchedAttributes.GetString(BirthTokenAttribute, "");
        bool persisted = loaded && Guid.TryParseExact(token, "N", out _);
        if (!persisted) entity.WatchedAttributes.SetString(BirthTokenAttribute, token = Guid.NewGuid().ToString("N"));
        Identities.Remove(entity);
        string snapshot = "";
        try
        {
            if (entity.Slot.Itemstack?.Collectible?.Code != null)
                snapshot = JsonConvert.SerializeObject(ItemStackCodec.Encode(entity.Slot.Itemstack));
        }
        catch { persisted = false; }
        Identities.Add(entity, new Identity(token, persisted && snapshot.Length > 0, snapshot));
    }
    private sealed record Identity(string Token, bool Persisted, string Snapshot);
}
