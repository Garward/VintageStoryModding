using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using VintageKinematics.Api.Storage;

namespace VintageKinematics.Storage.Terminal
{
    /// <summary>Bounded binary transport for terminal intents and authoritative snapshots.</summary>
    public static class StorageTerminalPacketCodec
    {
        private const byte Version = 3;

        public static byte[] EncodeQuery(long sessionId, StorageTerminalQuery query)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Version);
            writer.Write(sessionId);
            writer.Write(query.RequestId);
            writer.Write(query.Search);
            writer.Write(query.Page);
            writer.Write((byte)query.Sort);
            writer.Write((byte)query.RequestedPageSize);
            return stream.ToArray();
        }

        public static bool TryDecodeQuery(
            byte[] data,
            out long sessionId,
            out StorageTerminalQuery query)
        {
            sessionId = 0;
            query = null;
            if (data == null || data.Length == 0 || data.Length > 512) return false;
            try
            {
                using var stream = new MemoryStream(data, writable: false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadByte() != Version) return false;
                sessionId = reader.ReadInt64();
                query = new StorageTerminalQuery(
                    reader.ReadInt64(),
                    reader.ReadString(),
                    reader.ReadInt32(),
                    (StorageTerminalSort)reader.ReadByte(),
                    reader.ReadByte());
                return stream.Position == stream.Length;
            }
            catch
            {
                sessionId = 0;
                query = null;
                return false;
            }
        }

        public static byte[] EncodeAction(long sessionId, StorageTerminalActionRequest request)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Version);
            writer.Write(sessionId);
            writer.Write((byte)request.Action);
            writer.Write(request.EntryId);
            writer.Write(request.SourceInventoryId);
            writer.Write(request.SourceSlotId);
            writer.Write(request.RefreshQuery.RequestId);
            writer.Write(request.RefreshQuery.Search);
            writer.Write(request.RefreshQuery.Page);
            writer.Write((byte)request.RefreshQuery.Sort);
            writer.Write((byte)request.RefreshQuery.RequestedPageSize);
            return stream.ToArray();
        }

        public static bool TryDecodeAction(
            byte[] data,
            out long sessionId,
            out StorageTerminalActionRequest request)
        {
            sessionId = 0;
            request = null;
            if (data == null || data.Length == 0 || data.Length > 512) return false;
            try
            {
                using var stream = new MemoryStream(data, writable: false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadByte() != Version) return false;
                sessionId = reader.ReadInt64();
                StorageTerminalAction action = (StorageTerminalAction)reader.ReadByte();
                if (!Enum.IsDefined(action)) return false;
                long entryId = reader.ReadInt64();
                string sourceInventoryId = reader.ReadString();
                int sourceSlotId = reader.ReadInt32();
                var query = new StorageTerminalQuery(
                    reader.ReadInt64(),
                    reader.ReadString(),
                    reader.ReadInt32(),
                    (StorageTerminalSort)reader.ReadByte(),
                    reader.ReadByte());
                if (stream.Position != stream.Length) return false;
                bool withdraw = action == StorageTerminalAction.WithdrawStackToInventory
                    || action == StorageTerminalAction.WithdrawOneToCursor;
                if (withdraw && entryId <= 0) return false;
                if (action == StorageTerminalAction.DepositInventorySlot
                    && (string.IsNullOrWhiteSpace(sourceInventoryId)
                        || sourceInventoryId.Length > 128
                        || sourceSlotId < 0)) return false;
                request = new StorageTerminalActionRequest(
                    action,
                    entryId,
                    query,
                    sourceInventoryId,
                    sourceSlotId);
                return true;
            }
            catch
            {
                sessionId = 0;
                request = null;
                return false;
            }
        }

        public static byte[] EncodePage(string title, long sessionId, StorageTerminalPage page)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);
            writer.Write(Version);
            writer.Write(title ?? string.Empty);
            writer.Write(sessionId);
            writer.Write(page.RequestId);
            writer.Write(page.Revision);
            WriteStats(writer, page.Stats);
            writer.Write(page.Search);
            writer.Write((byte)page.Sort);
            writer.Write(page.Page);
            writer.Write(page.PageCount);
            writer.Write(page.MatchingEntries);
            writer.Write((byte)page.PageSize);
            writer.Write((byte)page.Entries.Count);
            foreach (StoredEntry entry in page.Entries)
            {
                writer.Write(entry.EntryId);
                writer.Write(entry.Quantity);
                writer.Write(entry.CachedSearchText ?? string.Empty);
                entry.Exemplar.ToBytes(writer);
            }
            return stream.ToArray();
        }

        public static bool TryDecodePage(
            byte[] data,
            IWorldAccessor world,
            out string title,
            out long sessionId,
            out StorageTerminalPage page)
        {
            title = string.Empty;
            sessionId = 0;
            page = null;
            if (data == null || data.Length == 0 || data.Length > 4 * 1024 * 1024) return false;
            try
            {
                using var stream = new MemoryStream(data, writable: false);
                using var reader = new BinaryReader(stream);
                if (reader.ReadByte() != Version) return false;
                title = reader.ReadString();
                sessionId = reader.ReadInt64();
                long requestId = reader.ReadInt64();
                long revision = reader.ReadInt64();
                StorageStats stats = ReadStats(reader);
                string search = reader.ReadString();
                StorageTerminalSort sort = (StorageTerminalSort)reader.ReadByte();
                int pageIndex = reader.ReadInt32();
                int pageCount = reader.ReadInt32();
                int matchingEntries = reader.ReadInt32();
                int pageSize = reader.ReadByte();
                int count = reader.ReadByte();
                if (pageSize < StorageTerminalQuery.PageSize
                    || pageSize > StorageTerminalQuery.MaxPageSize
                    || count > pageSize) return false;

                var entries = new List<StoredEntry>(count);
                for (int index = 0; index < count; index++)
                {
                    long entryId = reader.ReadInt64();
                    long quantity = reader.ReadInt64();
                    string cachedSearchText = reader.ReadString();
                    ItemStack exemplar = new ItemStack(reader, world);
                    if (entryId <= 0 || quantity <= 0 || exemplar.Collectible == null) return false;
                    exemplar.StackSize = 1;
                    entries.Add(new StoredEntry(
                        entryId,
                        ItemKey.FromStack(exemplar),
                        exemplar,
                        quantity,
                        cachedSearchText));
                }
                if (stream.Position != stream.Length) return false;

                page = new StorageTerminalPage(
                    requestId,
                    revision,
                    stats,
                    search,
                    sort,
                    pageIndex,
                    pageCount,
                    matchingEntries,
                    pageSize,
                    entries);
                return true;
            }
            catch
            {
                title = string.Empty;
                sessionId = 0;
                page = null;
                return false;
            }
        }

        private static void WriteStats(BinaryWriter writer, StorageStats stats)
        {
            writer.Write(stats.StoredItems);
            writer.Write(stats.ItemCapacity);
            writer.Write(stats.EntryCount);
            writer.Write(stats.TypeCapacity);
            writer.Write((byte)stats.State);
            writer.Write(stats.ImportRate);
            writer.Write(stats.ExportRate);
            writer.Write(stats.PowerRequired);
            writer.Write(stats.Powered);
        }

        private static StorageStats ReadStats(BinaryReader reader)
        {
            return new StorageStats(
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                (StorageState)reader.ReadByte(),
                reader.ReadInt32(),
                reader.ReadInt32(),
                reader.ReadBoolean(),
                reader.ReadBoolean());
        }
    }
}
