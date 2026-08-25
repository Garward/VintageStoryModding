using ResponsiveVS.Config;
using ResponsiveVS.Network;

namespace ResponsiveVS.Transactions;

public sealed class TransactionClassifier
{
    private readonly ResponsiveNetwork network;
    private readonly PendingTransactionStore pendingStore;

    public TransactionClassifier(ResponsiveNetwork network, PendingTransactionStore pendingStore)
    {
        this.network = network;
        this.pendingStore = pendingStore;
    }

    public TransactionDecision Classify(string pendingKey, InventoryOperationKind operationKind)
    {
        if (!ResponsiveVSConfigSystem.Config.Transactions.EnableInventoryOwnership)
        {
            return TransactionDecision.Fallback("ownership disabled");
        }

        if (network == null || !network.IsOwnershipEnabled)
        {
            return TransactionDecision.Fallback("handshake missing");
        }

        if (pendingStore.TryGet(pendingKey, out _))
        {
            return TransactionDecision.Blocked("pending transaction");
        }

        if (operationKind == InventoryOperationKind.Unknown)
        {
            return TransactionDecision.Fallback("unknown operation");
        }

        return TransactionDecision.Owned();
    }
}
