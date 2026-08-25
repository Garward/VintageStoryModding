namespace ResponsiveVS.Transactions;

public enum InventoryOperationKind
{
    Unknown = 0,
    ActivateSlot = 1,
    ShiftClick = 2,
    Wheel = 3,
    DragRight = 4,
    DragLeft = 5,
    CraftOutput = 6
}

public enum TransactionClassification
{
    Owned = 0,
    FallbackVanilla = 1,
    BlockedPending = 2,
    RejectedLocal = 3
}

public enum TransactionRejectReason
{
    None = 0,
    HandshakeMissing = 1,
    PendingTransaction = 2,
    InventoryMissing = 3,
    SlotMissing = 4,
    AccessDenied = 5,
    ProtocolMismatch = 6,
    ServerError = 7,
    UnsupportedOperation = 8,
    StaleClientSession = 9
}

public sealed class TransactionDecision
{
    public TransactionClassification Classification { get; set; }
    public string Reason { get; set; }

    public static TransactionDecision Owned(string reason = "owned")
    {
        return new TransactionDecision { Classification = TransactionClassification.Owned, Reason = reason };
    }

    public static TransactionDecision Fallback(string reason)
    {
        return new TransactionDecision { Classification = TransactionClassification.FallbackVanilla, Reason = reason };
    }

    public static TransactionDecision Blocked(string reason)
    {
        return new TransactionDecision { Classification = TransactionClassification.BlockedPending, Reason = reason };
    }

    public static TransactionDecision Rejected(string reason)
    {
        return new TransactionDecision { Classification = TransactionClassification.RejectedLocal, Reason = reason };
    }
}
