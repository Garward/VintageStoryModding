using System.Threading;

namespace ResponsiveVS.Transactions;

public sealed class TransactionIds
{
    private long nextId;

    public long Next()
    {
        return Interlocked.Increment(ref nextId);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref nextId, 0);
    }
}
