namespace VRPG.Inventory;

public interface IPlayerItemStore
{
    long Revision { get; }
    PlayerItemCapacitySummary CapacitySummary();
    PlayerItemPage Query(PlayerItemQuery query);
    PlayerItemCategoryPage QueryCategories(PlayerItemLocation location, int offset, int limit);
    bool TryGet(string entryId, out PlayerItemDetail? detail);
    PlayerItemMutationResult Insert(
        string requestId,
        PlayerItemPrototype prototype,
        long quantity,
        long expectedRevision);
    PlayerItemMutationResult Extract(
        string requestId,
        string entryId,
        long quantity,
        long expectedRevision);
    PlayerItemReservationResult Reserve(
        string requestId,
        PlayerItemAmount[] amounts,
        long expectedRevision);
    PlayerItemReservationSnapshot Reservation(string reservationId);
    PlayerItemOperationResult CommitReservation(string requestId, string reservationId);
    PlayerItemOperationResult RollbackReservation(string requestId, string reservationId);
    bool TryGetCompletedEquip(
        string requestId,
        string entryId,
        string roleCode,
        long expectedRevision,
        out PlayerItemOperationResult? result);
    PlayerItemOperationResult Equip(
        string requestId,
        string entryId,
        string roleCode,
        long expectedRevision);
    PlayerItemOperationResult Unequip(string requestId, string roleCode, long expectedRevision);
    PlayerItemOperationResult SetFlags(
        string requestId,
        string entryId,
        PlayerItemFlags flags,
        long expectedRevision);
    PlayerItemOperationResult MoveToRecovery(
        string requestId,
        string entryId,
        PlayerItemLocation destination,
        PlayerItemRecoveryReason reason,
        string source,
        long expectedRevision);
    PlayerItemMutationResult ImportRecovery(
        string requestId,
        PlayerItemPrototype prototype,
        long quantity,
        PlayerItemLocation destination,
        PlayerItemRecoveryReason reason,
        string source,
        long expectedRevision);
    PlayerItemOperationResult Recover(
        string requestId,
        string entryId,
        long expectedRevision);
    PlayerItemOperationResult Destroy(
        string requestId,
        string entryId,
        long quantity,
        bool confirmed,
        long expectedRevision);
    PlayerEquipmentSnapshot Equipment();
    PlayerRecoverySnapshot Recovery();
}
