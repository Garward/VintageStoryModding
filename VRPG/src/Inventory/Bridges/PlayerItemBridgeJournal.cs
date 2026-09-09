using System;
using System.Linq;
using VRPG.Inventory.Persistence;

namespace VRPG.Inventory.Bridges;

/// <summary>Receipt and canonical snapshots use the same archive write and generation.</summary>
public sealed class PlayerItemBridgeJournal(PlayerItemArchiveService archive)
{
    public PlayerItemBridgeReceipt? Find(string operationId) =>
        archive.BridgeSnapshot().SingleOrDefault(r => r.Request.OperationId == operationId);
    public PlayerItemBridgeReceipt[] Snapshot() => archive.BridgeSnapshot();
    public bool Save(PlayerItemBridgeReceipt receipt)
    {
        if (!Validate(receipt.Request.OperationId, receipt)) return false;
        archive.StageBridge(receipt);
        return archive.SaveDirty().Success;
    }

    public static bool Validate(string key, PlayerItemBridgeReceipt? receipt)
    {
        if (receipt?.Request is not { } request || receipt.Version != 1 || key != request.OperationId
            || string.IsNullOrWhiteSpace(request.OperationId) || string.IsNullOrWhiteSpace(request.PlayerUid)
            || string.IsNullOrWhiteSpace(request.AdapterCode) || string.IsNullOrWhiteSpace(request.Source)
            || string.IsNullOrWhiteSpace(request.Destination) || request.Quantity <= 0
            || !Enum.IsDefined(request.Operation) || !Enum.IsDefined(receipt.Stage) || receipt.Amounts == null)
            return false;
        if (receipt.ExternalResult is { } result && (result.Consumed < 0 || result.Returned < 0
            || result.Consumed > request.Quantity || result.Returned > request.Quantity - result.Consumed)) return false;
        if (receipt.Claim != null) return InMemoryPlayerItemStore.IsValid(receipt.Claim, request.Quantity)
            && string.IsNullOrEmpty(request.EntryId) && string.IsNullOrEmpty(receipt.ReservationId)
            && receipt.Amounts.Length == 0 && receipt.ExternalResult == null
            && request.Operation is PlayerItemBridgeOperation.Pickup or PlayerItemBridgeOperation.Reward or PlayerItemBridgeOperation.AdminGrant
            && receipt.Stage is PlayerItemBridgeStage.Prepared or PlayerItemBridgeStage.CanonicalCommitted or PlayerItemBridgeStage.Completed;
        if (receipt.Stage is PlayerItemBridgeStage.ExternalRecorded or PlayerItemBridgeStage.CanonicalCommitted or PlayerItemBridgeStage.Completed)
        {
            var external = receipt.ExternalResult;
            if (external?.Known != true || external.Consumed != 0 && external.Returned != 0
                || external.Returned != request.Quantity - external.Consumed) return false;
        }
        return receipt.Stage is PlayerItemBridgeStage.New or PlayerItemBridgeStage.Refused
            || !string.IsNullOrWhiteSpace(receipt.ReservationId)
                && receipt.Amounts.Length == 1 && receipt.Amounts[0] == new PlayerItemAmount(request.EntryId, request.Quantity);
    }

    internal static bool MatchesCustody(PlayerItemBridgeReceipt receipt, PlayerItemStoreState? store)
    {
        string prefix = "bridge:" + receipt.Request.OperationId + ":";
        if (receipt.Claim != null && receipt.Stage == PlayerItemBridgeStage.Prepared)
            return store == null || !store.CompletedRequests.Any(r => r.Success
                && r.RequestId.StartsWith(prefix + "claim:", StringComparison.Ordinal));
        if (store == null) return false;
        if (receipt.Claim != null)
            return store.CompletedRequests.Any(r => r.Success && r.ResultKind == PlayerItemPersistedResultKind.Mutation
                && r.RequestId.StartsWith(prefix + "claim:", StringComparison.Ordinal) && r.Quantity >= receipt.Request.Quantity);
        if (receipt.Stage is PlayerItemBridgeStage.CanonicalCommitted or PlayerItemBridgeStage.Completed or PlayerItemBridgeStage.RolledBack)
        {
            string key = prefix + (receipt.Stage == PlayerItemBridgeStage.RolledBack ? "rollback" : "commit");
            return store.CompletedRequests.Any(r => r.RequestId == key && r.Success && r.ResultKind == PlayerItemPersistedResultKind.Operation)
                && store.Reservations.All(r => r.ReservationId != receipt.ReservationId);
        }
        return store.Reservations.Any(r => r.ReservationId == receipt.ReservationId && r.Amounts.SequenceEqual(receipt.Amounts));
    }
}
