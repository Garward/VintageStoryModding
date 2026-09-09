using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory.Bridges;

/// <summary>Serializes prepare/effect/commit receipts; a retry never blindly replays an external effect.</summary>
public sealed class PlayerItemBridgeCoordinator
{
    private readonly ICollectibleResolver resolver;
    private readonly Dictionary<string, EngineItemBridgeAdapter> adapters = new(StringComparer.Ordinal);
    private readonly PlayerItemBridgeJournal journal;
    private readonly HashSet<(string Code, PlayerItemBridgeOperation Operation)> claimProducers = new();

    public PlayerItemBridgeCoordinator(PlayerItemArchiveService archive, ICollectibleResolver resolver)
    {
        Archive = archive ?? throw new ArgumentNullException(nameof(archive));
        this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        journal = new PlayerItemBridgeJournal(archive);
    }

    public PlayerItemArchiveService Archive { get; }
    public bool HasPickupClaim(string token)
    {
        lock (Archive.TransactionSync) return journal.Find("pickup:" + token)?.Claim != null;
    }

    public bool SuppressClaimedPickup(IEngineItemPickupSource source)
    {
        lock (Archive.TransactionSync)
        {
            if (!Archive.IsLoaded || !Archive.IsCrashDurable || !source.HasPersistedIdentity
                || journal.Find("pickup:" + source.Token) is not { Claim: not null } receipt) return false;
            if (source.Stack == null || source.Stack.StackSize != receipt.Request.Quantity
                || JsonConvert.SerializeObject(DescribeClaim(source.Stack)) != JsonConvert.SerializeObject(receipt.Claim))
                return false;
            if (!journal.Save(receipt)) return false;
            source.Remove();
            return true;
        }
    }

    public PlayerItemBridgeReceipt Pickup(string playerUid, IEngineItemPickupSource source)
    {
        lock (Archive.TransactionSync)
        {
            var request = new PlayerItemBridgeRequest("pickup:" + source.Token, playerUid, PlayerItemBridgeOperation.Pickup,
                "engine:persisted-item", "", source.Stack?.StackSize ?? 0, source.Token, "canonical", 0);
            var receipt = new PlayerItemBridgeReceipt { Request = request };
            if (!ValidRequest(request, ingress: true) || !Archive.IsLoaded || !Archive.IsCrashDurable || !source.HasPersistedIdentity)
                return Refused(receipt, "Pickup requires a persisted source birth identity and durable custody.");
            if (journal.Find(request.OperationId) is { } prior)
            {
                if (!SuppressClaimedPickup(source)) return Pending(prior, "Pickup source is awaiting durable suppression.");
                return Claim(prior);
            }
            receipt.Claim = DescribeClaim(source.Stack!);
            var store = Archive.Directory.ForPlayer(playerUid);
            if (store is not InMemoryPlayerItemStore persistent
                || !InMemoryPlayerItemStore.TryRestore(persistent.Capture().State, out var preview, out _)
                || !preview!.Insert("pickup-preflight:" + source.Token, receipt.Claim, request.Quantity, store.Revision).Success)
                return Refused(receipt, "Pickup cannot fit in canonical custody; the source remains untouched.");
            receipt.Stage = PlayerItemBridgeStage.Prepared;
            if (!journal.Save(receipt)) return Pending(receipt, "Pickup claim is awaiting durable persistence; source must remain quarantined.");
            try { source.Remove(); }
            catch (Exception error) { return Pending(receipt, error.Message); }
            return Claim(receipt);
        }
    }
    public void RegisterClaimProducer(string code, PlayerItemBridgeOperation operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (operation is not (PlayerItemBridgeOperation.Reward or PlayerItemBridgeOperation.AdminGrant))
            throw new ArgumentException("Only explicit reward and admin claim producers may register.", nameof(operation));
        lock (Archive.TransactionSync) claimProducers.Add((code, operation));
    }

    /// <summary>The registered generator yields ownership to the durable claim on Prepared or Completed.</summary>
    public PlayerItemBridgeReceipt RegisterClaim(PlayerItemBridgeRequest request, ItemStack stack)
    {
        lock (Archive.TransactionSync)
        {
            var receipt = new PlayerItemBridgeReceipt { Request = request };
            if (!ValidRequest(request, ingress: true) || !Archive.IsLoaded || !Archive.IsCrashDurable
                || !claimProducers.Contains((request.AdapterCode, request.Operation)) || stack.StackSize != request.Quantity)
                return Refused(receipt, "A durable registered claim producer is required.");
            receipt.Claim = DescribeClaim(stack);
            if (journal.Find(request.OperationId) is { } prior)
                return prior.Request != request || JsonConvert.SerializeObject(prior.Claim) != JsonConvert.SerializeObject(receipt.Claim)
                    ? Refused(receipt, "Claim identity conflicts with an existing receipt.") : Claim(prior);
            receipt.Stage = PlayerItemBridgeStage.Prepared;
            if (!journal.Save(receipt)) return Pending(receipt, "Claim persistence is unavailable; producer retains custody.");
            return Claim(receipt);
        }
    }

    private static PlayerItemPrototype DescribeClaim(ItemStack stack)
    {
        var encoded = ItemStackCodec.Encode(stack);
        var description = ItemStackCodec.Describe(stack);
        var classification = new PlayerItemClassifier().Classify(description);
        return new PlayerItemPrototype(classification.StorageKind, encoded.Payload.AssetCode,
            classification.Category, stack.Collectible.Code.Path, encoded.Payload.ExactState,
            encoded.Payload.AssetKind, encoded.Payload.EmbeddedReferences, description.StatePolicy);
    }
    public void Register(EngineItemBridgeAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter.Code);
        lock (Archive.TransactionSync) adapters.Add(adapter.Code, adapter);
    }

    public bool Supports(string code, PlayerItemBridgeOperation operation)
    {
        lock (Archive.TransactionSync)
            return Archive.IsLoaded && Archive.IsCrashDurable
                && adapters.TryGetValue(code, out var adapter) && adapter.Operation == operation;
    }

    public PlayerItemBridgeReceipt Execute(PlayerItemBridgeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (Archive.TransactionSync)
        {
            var receipt = new PlayerItemBridgeReceipt { Request = request };
            if (!ValidRequest(request) || !Archive.IsLoaded || !Archive.IsCrashDurable)
                return Refused(receipt, "Durable custody or request identity is unavailable.");
            if (journal.Find(request.OperationId) is { } prior)
                return prior.Request != request ? Refused(receipt, "Operation identity conflicts with an existing receipt.")
                    : Resume(prior);
            if (!Supports(request.AdapterCode, request.Operation))
                return Refused(receipt, "No audited adapter exists for this route.");

            IPlayerItemStore store = Archive.Directory.ForPlayer(request.PlayerUid);
            if (!store.TryGet(request.EntryId, out var entry) || PlayerItemPolicy.RequiresRecovery(entry!.Prototype))
                return Refused(receipt, "The canonical entry is missing or requires recovery.");
            ItemStackMaterialization materialized;
            try
            {
                var prototype = entry.Prototype;
                materialized = ItemStackCodec.Materialize(new CanonicalItemPayload(PlayerItemSchema.CurrentVersion,
                    prototype.AssetKind, prototype.AssetCode, prototype.ExactState, prototype.EmbeddedReferences),
                    checked((int)request.Quantity), resolver);
            }
            catch (Exception error) { return Refused(receipt, error.Message); }
            if (!materialized.Resolved) return Refused(receipt, materialized.Failure.ToString());
            if (ItemStackCodec.Describe(materialized.Stack!).StatePolicy == PlayerItemStatePolicy.FrozenInRecovery)
                return Refused(receipt, "Actual engine state requires an audited evolving-state adapter.");

            receipt.Amounts = new[] { new PlayerItemAmount(request.EntryId, request.Quantity) };
            var reserved = store.Reserve(Key(request, "reserve"), receipt.Amounts, request.ExpectedRevision);
            if (!reserved.Success) return Refused(receipt, reserved.Rejection.ToString());
            receipt.ReservationId = reserved.ReservationId;
            receipt.Stage = PlayerItemBridgeStage.Prepared;
            if (!journal.Save(receipt)) return Rollback(receipt, "Prepare could not be durably saved.");
            receipt.Stage = PlayerItemBridgeStage.EffectPending;
            if (!journal.Save(receipt)) return Rollback(receipt, "Effect intent could not be durably saved.");
            try { receipt.ExternalResult = adapters[request.AdapterCode].Apply(receipt, materialized.Stack!); }
            catch (Exception error) { return Uncertain(receipt, error.Message); }
            return RecordAndCommit(receipt);
        }
    }

    /// <summary>Use for cancel, swap, death, realm transition, disconnect, and startup.</summary>
    public PlayerItemBridgeReceipt[] Reconcile(string? playerUid = null)
    {
        lock (Archive.TransactionSync)
        {
            if (!Archive.IsLoaded || !Archive.IsCrashDurable) return Array.Empty<PlayerItemBridgeReceipt>();
            return journal.Snapshot()
                .Where(r => playerUid == null || string.Equals(r.Request.PlayerUid, playerUid, StringComparison.OrdinalIgnoreCase))
                .Select(Resume).ToArray();
        }
    }

    private PlayerItemBridgeReceipt Resume(PlayerItemBridgeReceipt receipt)
    {
        if (receipt.Claim != null) return Claim(receipt);
        if (receipt.Stage is PlayerItemBridgeStage.Completed or PlayerItemBridgeStage.RolledBack or PlayerItemBridgeStage.Refused)
        {
            // A prior write may have reached disk and then thrown; confirm it without redoing the effect.
            return journal.Save(receipt) ? receipt.Clone() : Pending(receipt, "Receipt persistence is unavailable.");
        }
        if (receipt.Stage == PlayerItemBridgeStage.Prepared) return Rollback(receipt, "Interrupted before external effect.");
        if (receipt.Stage == PlayerItemBridgeStage.CanonicalCommitted) return Complete(receipt);
        if (receipt.Stage == PlayerItemBridgeStage.ExternalRecorded) return Commit(receipt);
        if (receipt.Stage is PlayerItemBridgeStage.EffectPending or PlayerItemBridgeStage.ReconcileRequired)
        {
            if (!Supports(receipt.Request.AdapterCode, receipt.Request.Operation))
                return Uncertain(receipt, "The source adapter must reconcile the external effect.");
            try { receipt.ExternalResult = adapters[receipt.Request.AdapterCode].Reconcile(receipt); }
            catch (Exception error) { return Uncertain(receipt, error.Message); }
            return RecordAndCommit(receipt);
        }
        return Refused(receipt, "No resumable bridge transition exists.");
    }

    private PlayerItemBridgeReceipt Claim(PlayerItemBridgeReceipt receipt)
    {
        if (receipt.Stage is PlayerItemBridgeStage.Completed or PlayerItemBridgeStage.CanonicalCommitted)
            return Complete(receipt);
        var store = Archive.Directory.ForPlayer(receipt.Request.PlayerUid);
        var inserted = store.Insert(Key(receipt.Request, "claim:" + store.Revision), receipt.Claim!,
            receipt.Request.Quantity, store.Revision);
        if (!inserted.Success)
        {
            receipt.Error = inserted.Rejection.ToString();
            return journal.Save(receipt) ? receipt.Clone() : Pending(receipt, "Claim persistence is unavailable.");
        }
        receipt.Stage = PlayerItemBridgeStage.CanonicalCommitted;
        if (!journal.Save(receipt)) return Pending(receipt, "Claim insertion is awaiting durable persistence.");
        return Complete(receipt);
    }

    private PlayerItemBridgeReceipt RecordAndCommit(PlayerItemBridgeReceipt receipt)
    {
        var result = receipt.ExternalResult;
        long quantity = receipt.Request.Quantity;
        if (result != null && (result.Consumed < 0 || result.Returned < 0
            || result.Consumed > quantity || result.Returned > quantity - result.Consumed))
        {
            receipt.ExternalResult = new(false, 0, 0, "Malformed adapter result: " + JsonConvert.SerializeObject(result));
            return Uncertain(receipt, "Invalid external quantities require adapter reconciliation.");
        }
        if (result == null || !result.Known || result.Returned != quantity - result.Consumed)
            return Uncertain(receipt, "The external destination has not accounted for the complete materialized stack.");
        if (result.Consumed != 0 && result.Returned != 0)
            return Uncertain(receipt, "Partial consumption requires an adapter settlement; the entire reservation remains locked.");
        receipt.Stage = PlayerItemBridgeStage.ExternalRecorded;
        if (!journal.Save(receipt)) return Pending(receipt, "External receipt could not be durably saved.");
        return Commit(receipt);
    }

    private PlayerItemBridgeReceipt Commit(PlayerItemBridgeReceipt receipt)
    {
        if (receipt.ExternalResult?.Known != true) return Uncertain(receipt, "No complete external receipt exists.");
        if (receipt.ExternalResult.Consumed == 0) return Rollback(receipt, "All materialized units were returned.");
        if (receipt.ExternalResult.Consumed != receipt.Request.Quantity || receipt.ExternalResult.Returned != 0)
            return Uncertain(receipt, "Partial external custody is not committable.");
        var committed = Archive.Directory.ForPlayer(receipt.Request.PlayerUid)
            .CommitReservation(Key(receipt.Request, "commit"), receipt.ReservationId);
        if (!committed.Success) return Uncertain(receipt, committed.Rejection.ToString());
        receipt.Stage = PlayerItemBridgeStage.CanonicalCommitted;
        if (!journal.Save(receipt)) return Pending(receipt, "Canonical commit is awaiting durable persistence.");
        return Complete(receipt);
    }

    private PlayerItemBridgeReceipt Complete(PlayerItemBridgeReceipt receipt)
    {
        receipt.Stage = PlayerItemBridgeStage.Completed;
        receipt.Error = "";
        return journal.Save(receipt) ? receipt.Clone() : Pending(receipt, "Completion receipt is awaiting persistence.");
    }

    private PlayerItemBridgeReceipt Rollback(PlayerItemBridgeReceipt receipt, string error)
    {
        var rollback = Archive.Directory.ForPlayer(receipt.Request.PlayerUid)
            .RollbackReservation(Key(receipt.Request, "rollback"), receipt.ReservationId);
        if (!rollback.Success) return Uncertain(receipt, rollback.Rejection.ToString());
        receipt.Stage = PlayerItemBridgeStage.RolledBack;
        receipt.Error = error;
        return journal.Save(receipt) ? receipt.Clone() : Pending(receipt, "Rollback is awaiting durable persistence.");
    }

    private PlayerItemBridgeReceipt Uncertain(PlayerItemBridgeReceipt receipt, string error)
    {
        receipt.Stage = PlayerItemBridgeStage.ReconcileRequired;
        receipt.Error = error;
        journal.Save(receipt);
        return receipt.Clone();
    }

    private static PlayerItemBridgeReceipt Pending(PlayerItemBridgeReceipt receipt, string error) =>
        new() { Request = receipt.Request, Stage = PlayerItemBridgeStage.ReconcileRequired,
            ReservationId = receipt.ReservationId, Amounts = receipt.Amounts, ExternalResult = receipt.ExternalResult, Error = error };

    private static PlayerItemBridgeReceipt Refused(PlayerItemBridgeReceipt receipt, string error)
    {
        receipt.Stage = PlayerItemBridgeStage.Refused;
        receipt.Error = error;
        return receipt;
    }

    private static string Key(PlayerItemBridgeRequest request, string phase) => $"bridge:{request.OperationId}:{phase}";
    private static bool ValidRequest(PlayerItemBridgeRequest request, bool ingress = false) =>
        !string.IsNullOrWhiteSpace(request.OperationId) && !string.IsNullOrWhiteSpace(request.PlayerUid)
        && !string.IsNullOrWhiteSpace(request.AdapterCode) && (ingress || !string.IsNullOrWhiteSpace(request.EntryId))
        && !string.IsNullOrWhiteSpace(request.Source) && !string.IsNullOrWhiteSpace(request.Destination)
        && Enum.IsDefined(request.Operation) && request.Quantity > 0 && request.Quantity <= int.MaxValue;
}
