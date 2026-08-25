using System.Collections.Generic;

namespace ResponsiveVS.Transactions;

public sealed class PendingTransactionStore
{
    private readonly object lockObj = new();
    private readonly Dictionary<string, PendingTransaction> byPlayerOrSession = new();

    public bool TryBegin(string key, PendingTransaction transaction)
    {
        lock (lockObj)
        {
            if (byPlayerOrSession.ContainsKey(key))
            {
                return false;
            }

            byPlayerOrSession[key] = transaction;
            return true;
        }
    }

    public bool TryGet(string key, out PendingTransaction transaction)
    {
        lock (lockObj)
        {
            return byPlayerOrSession.TryGetValue(key, out transaction);
        }
    }

    public bool TryComplete(string key, long transactionId, out PendingTransaction transaction)
    {
        lock (lockObj)
        {
            if (!byPlayerOrSession.TryGetValue(key, out transaction) || transaction.TransactionId != transactionId)
            {
                transaction = null;
                return false;
            }

            byPlayerOrSession.Remove(key);
            return true;
        }
    }

    public void Clear(string key)
    {
        lock (lockObj)
        {
            byPlayerOrSession.Remove(key);
        }
    }

    public void ClearAll()
    {
        lock (lockObj)
        {
            byPlayerOrSession.Clear();
        }
    }
}
