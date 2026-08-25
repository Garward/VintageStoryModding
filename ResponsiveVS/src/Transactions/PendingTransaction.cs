using System;
using System.Collections.Generic;

namespace ResponsiveVS.Transactions;

public sealed class PendingTransaction
{
    public long TransactionId { get; set; }
    public string ClientSessionId { get; set; }
    public InventoryOperationKind OperationKind { get; set; }
    public long StartedMs { get; set; }
    public HashSet<SlotKey> TouchedSlots { get; } = new();

    public bool IsTimedOut(long nowMs, int timeoutMs)
    {
        return nowMs - StartedMs >= timeoutMs;
    }

    public override string ToString()
    {
        return $"tx={TransactionId} op={OperationKind} session={ClientSessionId} touched={TouchedSlots.Count}";
    }
}
