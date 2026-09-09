using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using VRPG.Persistence;

namespace VRPG.Parties.Persistence;

internal sealed class PartyWorldLease
{
    private static readonly Dictionary<string, PartyWorldLease> Leases = new(StringComparer.Ordinal);
    private readonly string path;
    private FileStream? stream;
    private int references;
    internal long? CommittedGeneration;

    private PartyWorldLease(string path) { this.path = path; }

    internal static PartyWorldLease Retain(string path)
    {
        lock (Leases)
        {
            if (!Leases.TryGetValue(path, out var lease)) Leases.Add(path, lease = new PartyWorldLease(path));
            lease.references++;
            return lease;
        }
    }

    internal bool HasHistory
    {
        get
        {
            if (stream == null) throw new InvalidOperationException("Party ownership must be acquired first.");
            if (stream.Length == 0) return false;
            stream.Position = 0;
            if (stream.Length != 1 || stream.ReadByte() != 1) throw new InvalidDataException("Party history marker is invalid.");
            return true;
        }
    }

    // The stream and flock live until the final same-process repository is disposed. The lock file is
    // never unlinked: replacing its inode could let two server processes both believe they own the world.
    internal void Acquire()
    {
        if (stream != null) return;
        WorldScopedPartyRepository.PathPresent(path);
        string directory = Path.GetDirectoryName(path)!;
        DurableFileWriter.EnsureDirectory(directory);
        var candidate = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        try
        {
            if (flock(candidate.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4 /* LOCK_EX | LOCK_NB */) != 0)
                throw new IOException("Another server owns this world's party authority.");
            candidate.Flush(flushToDisk: true);
            DurableFileWriter.SyncDirectory(directory);
            stream = candidate;
        }
        catch { candidate.Dispose(); throw; }
    }

    internal void MarkHistory()
    {
        if (HasHistory) return;
        stream!.Position = 0;
        stream.WriteByte(1);
        stream.Flush(flushToDisk: true);
    }

    internal void Release()
    {
        lock (Leases)
        {
            if (--references != 0) return;
            stream?.Dispose();
            stream = null;
            CommittedGeneration = null;
            Leases.Remove(path);
        }
    }

    [DllImport("libc", SetLastError = true)] private static extern int flock(int fd, int operation);
}
