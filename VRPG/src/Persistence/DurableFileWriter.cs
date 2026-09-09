using System;
using System.IO;
using System.Runtime.InteropServices;

namespace VRPG.Persistence;

/// <summary>Linux atomic replacement with file and containing-directory durability.</summary>
internal static class DurableFileWriter
{
    internal static void Write(string path, byte[] bytes, string temporaryPrefix)
    {
        string directory = Path.GetDirectoryName(path)!;
        EnsureDirectory(directory);
        string temporary = Path.Combine(directory, temporaryPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            SyncDirectory(directory);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    internal static void EnsureDirectory(string directory)
    {
        if (Directory.Exists(directory)) return;
        string? parent = Path.GetDirectoryName(directory);
        if (parent == null) throw new IOException("A durable archive parent directory is required.");
        EnsureDirectory(parent);
        Directory.CreateDirectory(directory);
        SyncDirectory(directory);
        SyncDirectory(parent);
    }

    internal static void SyncDirectory(string directory)
    {
        int descriptor = open(directory, 0x10000 /* O_DIRECTORY */);
        if (descriptor < 0) throw new IOException("Cannot open the archive directory for fsync.");
        try { if (fsync(descriptor) != 0) throw new IOException("Cannot durably flush the archive directory."); }
        finally { close(descriptor); }
    }

    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int close(int fd);
}
