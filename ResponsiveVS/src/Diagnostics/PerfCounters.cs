using System;
using System.Text;
using ResponsiveVS.RuntimeData;

namespace ResponsiveVS.Diagnostics;

public static class PerfCounters
{
    private static readonly object LockObj = new();

    private static long transactionsStarted;
    private static long transactionsCompleted;
    private static long transactionsRejected;
    private static long timeouts;
    private static long fallbacks;
    private static long blockedInputs;
    private static long totalWaitMs;
    private static long maxWaitMs;

    public static void RecordStarted()
    {
        lock (LockObj) transactionsStarted++;
    }

    public static void RecordCompleted(long waitMs)
    {
        lock (LockObj)
        {
            transactionsCompleted++;
            totalWaitMs += Math.Max(0, waitMs);
            if (waitMs > maxWaitMs) maxWaitMs = waitMs;
        }
    }

    public static void RecordRejected()
    {
        lock (LockObj) transactionsRejected++;
    }

    public static void RecordTimeout()
    {
        lock (LockObj) timeouts++;
    }

    public static void RecordFallback()
    {
        lock (LockObj) fallbacks++;
    }

    public static void RecordBlockedInput()
    {
        lock (LockObj) blockedInputs++;
    }

    public static void Reset()
    {
        lock (LockObj)
        {
            transactionsStarted = 0;
            transactionsCompleted = 0;
            transactionsRejected = 0;
            timeouts = 0;
            fallbacks = 0;
            blockedInputs = 0;
            totalWaitMs = 0;
            maxWaitMs = 0;
        }

        RuntimeDataStats.Reset();
    }

    public static string Summary()
    {
        lock (LockObj)
        {
            long avgWaitMs = transactionsCompleted == 0 ? 0 : totalWaitMs / transactionsCompleted;
            StringBuilder sb = new StringBuilder();
            sb.Append("ResponsiveVS counters: ");
            sb.Append("started=").Append(transactionsStarted);
            sb.Append(", completed=").Append(transactionsCompleted);
            sb.Append(", rejected=").Append(transactionsRejected);
            sb.Append(", timeouts=").Append(timeouts);
            sb.Append(", fallbacks=").Append(fallbacks);
            sb.Append(", blockedInputs=").Append(blockedInputs);
            sb.Append(", avgWaitMs=").Append(avgWaitMs);
            sb.Append(", maxWaitMs=").Append(maxWaitMs);
            sb.Append('.');
            sb.Append(RuntimeDataStats.Summary());
            return sb.ToString();
        }
    }
}
