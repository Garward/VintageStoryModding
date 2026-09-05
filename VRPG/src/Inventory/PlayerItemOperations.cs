namespace VRPG.Inventory;

public enum PlayerItemOperationRejection
{
    None,
    InvalidRequest,
    StaleRevision,
    RequestIdConflict,
    EntryMissing,
    EntryUnavailable,
    EntryReserved,
    EntryProtected,
    InsufficientQuantity,
    EquipmentRoleMissing,
    NotEquippable,
    ReservationMissing,
    ConfirmationRequired,
    InvalidGear,
    EquipmentRoleMismatch,
    RequirementNotMet
}

public sealed record PlayerItemOperationResult(
    bool Success,
    long Revision,
    string RequestId,
    string EntryId,
    string RoleCode,
    PlayerItemOperationRejection Rejection)
{
    public static PlayerItemOperationResult Accepted(
        long revision,
        string requestId,
        string entryId = "",
        string roleCode = "") =>
        new(true, revision, requestId, entryId, roleCode, PlayerItemOperationRejection.None);

    public static PlayerItemOperationResult Rejected(
        long revision,
        string requestId,
        PlayerItemOperationRejection rejection) =>
        new(false, revision, requestId, "", "", rejection);
}

public sealed record PlayerItemAmount(string EntryId, long Quantity);

public sealed record PlayerItemReservationResult(
    bool Success,
    long Revision,
    string RequestId,
    string ReservationId,
    PlayerItemOperationRejection Rejection)
{
    public static PlayerItemReservationResult Accepted(
        long revision,
        string requestId,
        string reservationId) =>
        new(true, revision, requestId, reservationId, PlayerItemOperationRejection.None);

    public static PlayerItemReservationResult Rejected(
        long revision,
        string requestId,
        PlayerItemOperationRejection rejection) =>
        new(false, revision, requestId, "", rejection);
}

public sealed record PlayerItemReservedEntry(
    string EntryId,
    PlayerItemPrototype Prototype,
    long Quantity);

public sealed record PlayerItemReservationSnapshot(
    long Revision,
    bool Exists,
    string ReservationId,
    PlayerItemReservedEntry[] Entries);

public sealed record PlayerEquipmentSnapshot(
    long Revision,
    EquippedPlayerItemRecord[] Entries);

public sealed record PlayerRecoverySummary(
    string EntryId,
    PlayerItemLocation Location,
    PlayerItemRecoveryReason Reason,
    string Source);

public sealed record PlayerRecoverySnapshot(
    long Revision,
    PlayerRecoverySummary[] Entries);
