using System;
using Vintagestory.API.Common;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.Security.Cryptography;
using System.Text;

namespace VRPG.Inventory.Bridges;

/// <summary>Legacy producer diagnostics. Adventure admission uses its central refusal guards instead.</summary>
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
    bool HasPrepublicationIdentity => false;
    ItemStack? Stack { get; }
    void Remove();
}

/// <summary>Tracks loaded evidence separately from a token established before any native spawn publication.</summary>
public sealed class EngineItemPickupSource(EntityItem entity) : IEngineItemPickupSource
{
    public const string BirthTokenAttribute = "vrpg:item-birth";
    private const string BirthFormatAttribute = "vrpg:item-birth-format";
    private const string BirthSnapshotAttribute = "vrpg:item-birth-snapshot";
    private static readonly ConditionalWeakTable<EntityItem, Identity> Identities = new();
    public string Token => entity.WatchedAttributes.GetString(BirthTokenAttribute, "");
    public bool HasPersistedIdentity => HasIdentity(persisted: true);
    public bool HasPrepublicationIdentity => HasIdentity(persisted: false);
    public ItemStack? Stack => entity.Slot.Itemstack;
    public void Remove()
    {
        entity.Itemstack = null;
        entity.Die(EnumDespawnReason.PickedUp);
    }
    public static void Observe(EntityItem entity, bool loaded)
    {
        // Events may repeat or arrive after the prepublication observer. Never renew proof.
        if (Identities.TryGetValue(entity, out _)) return;
        string token = entity.WatchedAttributes.GetString(BirthTokenAttribute, "");
        bool freshToken = token.StartsWith("v1:", StringComparison.Ordinal);
        bool persisted = loaded && Guid.TryParseExact(freshToken ? token[3..] : token, "N", out _);
        bool birthProof = freshToken || entity.WatchedAttributes.HasAttribute(BirthFormatAttribute)
            || entity.WatchedAttributes.HasAttribute(BirthSnapshotAttribute);
        string snapshot = Snapshot(entity, requireExactPolicy: birthProof);
        persisted &= !birthProof || MatchesBirthProof(entity, token, snapshot);
        Identities.Add(entity, new Identity(token, persisted && snapshot.Length > 0, snapshot, birthProof, null));
    }

    /// <summary>Called only by the validated server spawn entry prefix; this is not a disk flush.</summary>
    public static void EstablishBeforePublication(EntityItem entity, System.Func<bool> hookReady)
    {
        if (!hookReady() || entity.GetType() != typeof(EntityItem) || entity.World != null || entity.Api != null
            || entity.EntityId != 0 || Identities.TryGetValue(entity, out _)) return;
        // Existing attributes may represent an older saved source. Never mint a replacement identity.
        if (entity.WatchedAttributes.HasAttribute(BirthTokenAttribute)
            || entity.WatchedAttributes.HasAttribute(BirthFormatAttribute)
            || entity.WatchedAttributes.HasAttribute(BirthSnapshotAttribute)) return;
        string snapshot = Snapshot(entity, requireExactPolicy: true);
        if (snapshot.Length == 0) return;
        string token = "v1:" + Guid.NewGuid().ToString("N");
        entity.WatchedAttributes.SetString(BirthTokenAttribute, token);
        entity.WatchedAttributes.SetInt(BirthFormatAttribute, 1);
        entity.WatchedAttributes.SetString(BirthSnapshotAttribute, Digest(token, snapshot));
        Identities.Add(entity, new Identity(token, false, snapshot, true, hookReady));
    }

    private bool HasIdentity(bool persisted)
    {
        if (entity.GetType() != typeof(EntityItem) || !Identities.TryGetValue(entity, out var identity)
            || identity.Persisted != persisted || identity.Token != Token || identity.Snapshot.Length == 0
            || (!persisted && identity.HookReady?.Invoke() != true)) return false;
        string snapshot = Snapshot(entity, requireExactPolicy: identity.BirthProof);
        return snapshot == identity.Snapshot
            && (!identity.BirthProof || MatchesBirthProof(entity, Token, snapshot));
    }

    private static string Snapshot(EntityItem entity, bool requireExactPolicy)
    {
        try
        {
            var stack = entity.Slot.Itemstack;
            if (stack?.Collectible?.Code == null || stack.StackSize <= 0 || !ReferenceEquals(stack, entity.Itemstack)
                || (requireExactPolicy && ItemStackCodec.Describe(stack).StatePolicy != PlayerItemStatePolicy.ExactSnapshot)) return "";
            return JsonConvert.SerializeObject(ItemStackCodec.Encode(stack));
        }
        catch { return ""; }
    }

    private static bool MatchesBirthProof(EntityItem entity, string token, string snapshot) =>
        entity.WatchedAttributes.GetInt(BirthFormatAttribute) == 1 && snapshot.Length > 0
        && entity.WatchedAttributes.GetString(BirthSnapshotAttribute, "") == Digest(token, snapshot);
    private static string Digest(string token, string snapshot) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token + "\n" + snapshot)));
    private sealed record Identity(string Token, bool Persisted, string Snapshot, bool BirthProof, System.Func<bool>? HookReady);
}
