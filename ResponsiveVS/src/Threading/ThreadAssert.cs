using System;
using System.Threading;

namespace ResponsiveVS.Threading;

public static class ThreadAssert
{
    private static int clientMainThreadId;
    private static int serverMainThreadId;

    public static void CaptureClientMainThread()
    {
        clientMainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    public static void CaptureServerMainThread()
    {
        serverMainThreadId = Thread.CurrentThread.ManagedThreadId;
    }

    public static bool IsClientMainThread => clientMainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == clientMainThreadId;

    public static bool IsServerMainThread => serverMainThreadId != 0 && Thread.CurrentThread.ManagedThreadId == serverMainThreadId;

    public static void AssertClientMainThread(string code)
    {
        if (!IsClientMainThread)
        {
            throw new InvalidOperationException($"ResponsiveVS client main-thread assertion failed in {code}");
        }
    }

    public static void AssertServerMainThread(string code)
    {
        if (!IsServerMainThread)
        {
            throw new InvalidOperationException($"ResponsiveVS server main-thread assertion failed in {code}");
        }
    }
}
