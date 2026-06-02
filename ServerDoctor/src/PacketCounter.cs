using System.Collections.Concurrent;
using System.Threading;

namespace ServerDoctor;

internal sealed class PacketCounter
{
    internal sealed class Counter
    {
        public long Bytes;
        public long Count;
    }

    private ConcurrentDictionary<(string playerUid, string kind, string source), Counter> live =
        new ConcurrentDictionary<(string, string, string), Counter>();

    public void Record(string playerUid, string kind, string source, int bytes)
    {
        var key = (playerUid ?? "?", kind ?? "?", source ?? "-");
        var c = live.GetOrAdd(key, _ => new Counter());
        Interlocked.Add(ref c.Bytes, bytes);
        Interlocked.Increment(ref c.Count);
    }

    public ConcurrentDictionary<(string playerUid, string kind, string source), Counter> SnapshotAndReset()
    {
        var prev = Interlocked.Exchange(ref live,
            new ConcurrentDictionary<(string, string, string), Counter>());
        return prev;
    }
}
