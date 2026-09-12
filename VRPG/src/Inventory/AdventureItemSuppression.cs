using System;
using System.Threading;

namespace VRPG.Inventory;

/// <summary>Adventure native world items have no delivery authority. Diagnostics are bounded per world.</summary>
public sealed class AdventureItemSuppression(bool enabled, Action<string> diagnostic)
{
    [ThreadStatic] private static int materializationDepth;
    private int reported;
    public bool Enabled { get; } = enabled;

    public bool RefusePublication()
    {
        if (!Enabled || materializationDepth > 0) return false;
        if (Interlocked.Exchange(ref reported, 1) == 0)
            diagnostic("Adventure suppressed a native item spawn before publication; no canonical delivery occurred. Further spawn diagnostics are suppressed for this world.");
        return true;
    }

    /// <summary>Reserved for future server-authorized materialization; never grants pickup or delivery authority.</summary>
    public static IDisposable AllowMaterialization() => new MaterializationScope();

    private sealed class MaterializationScope : IDisposable
    {
        private readonly int ownerThread = Environment.CurrentManagedThreadId;
        private bool disposed;
        public MaterializationScope() => materializationDepth++;
        public void Dispose()
        {
            if (Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Materialization scopes must be disposed on their creating thread.");
            if (disposed) return;
            disposed = true;
            materializationDepth--;
        }
    }
}
