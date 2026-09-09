using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ProtoBuf;
using VRPG.Network;

namespace VRPG.Inventory.Queries;

/// <summary>Validates before store work, bounds outgoing metadata, and measures serialized response cost.</summary>
public sealed class PlayerItemQueryService
{
    public const int MaximumEntries = 128;
    public const int MaximumSearchLength = 128;
    public const int MaximumIdentityLength = 128;
    public const int MaximumDisplayLength = 256;
    public const int MaximumResponseBytes = 512 * 1024;
    public long LastResponseBytes { get; private set; }
    public TimeSpan LastQueryTime { get; private set; }

    public PlayerItemQueryResult Query(IPlayerItemStore store, PlayerItemQueryRequest request, long buildRevision = 0)
    {
        var timer = Stopwatch.StartNew();
        var result = new PlayerItemQueryResult { ConnectionGeneration = request.ConnectionGeneration,
            QueryId = request.QueryId, StoreRevision = store.Revision, BuildRevision = buildRevision, RejectionCode = Validate(request) };
        if (result.RejectionCode.Length == 0)
        {
            var page = store.Query(new(request.Category, request.Search, request.Offset, request.Limit,
                request.Location, request.Sort, request.Filters));
            var capacity = store.CapacitySummary();
            result.StoreRevision = page.Revision;
            result.Total = page.Total;
            result.Entries = page.Entries.Select(ToPacket).ToArray();
            var catalog = store.QueryCategories(request.Location, request.CategoryOffset, request.CategoryLimit);
            result.CategoryTotal = catalog.Total;
            result.CoreCategories = (catalog.CoreCategories ?? Array.Empty<PlayerItemCategorySummary>()).Select(category =>
                new PlayerItemCategoryPacket { Code = category.Code, DisplayName = category.DisplayName, Count = category.Count }).ToArray();
            result.Categories = catalog.Categories.Select(category => new PlayerItemCategoryPacket
                { Code = category.Code, DisplayName = Bounded(category.DisplayName, MaximumDisplayLength), Count = category.Count }).ToArray();
            if (catalog.Revision != page.Revision) result.RejectionCode = "stale-store";
            if (result.Categories.Any(category => !Text(category.Code, 128, true) || !Text(category.DisplayName, 256)))
            { result.Categories = Array.Empty<PlayerItemCategoryPacket>(); result.RejectionCode = "metadata-limit"; }
            if (result.Entries.Any(entry => !ValidEntry(entry)))
            {
                result.Entries = Array.Empty<PlayerItemEntryPacket>();
                result.RejectionCode = "metadata-limit";
            }
            result.CapacitySummary = new() { UniqueEntriesUsed = capacity.UniqueEntriesUsed,
                AggregateEntriesUsed = capacity.AggregateEntriesUsed, UniqueEntryLimit = capacity.Limits.UniqueEntryLimit,
                AggregateEntryLimit = capacity.Limits.AggregateEntryLimit,
                AggregateQuantityPerEntry = capacity.Limits.AggregateQuantityPerEntry };
            result.RecoveryCount = store.Query(new("", "", 0, 1, PlayerItemLocation.Overflow)).Total
                + store.Query(new("", "", 0, 1, PlayerItemLocation.Unresolved)).Total;
            if (store.Revision != page.Revision)
            {
                result.Entries = Array.Empty<PlayerItemEntryPacket>();
                result.RejectionCode = "stale-store";
            }
        }
        using var bytes = new MemoryStream();
        Serializer.Serialize(bytes, result);
        LastResponseBytes = bytes.Length;
        LastQueryTime = timer.Elapsed;
        return result;
    }

    public static string Validate(PlayerItemQueryRequest request)
    {
        if (request.ProtocolVersion != 1) return "protocol-version";
        if (request.ConnectionGeneration <= 0 || request.QueryId <= 0) return "invalid-identity";
        if (!Enum.IsDefined(request.Location) || !Enum.IsDefined(request.Sort)
            || ((int)request.Filters & ~127) != 0) return "invalid-filter";
        if (request.Offset < 0 || request.Limit is < 1 or > MaximumEntries
            || request.CategoryOffset < 0 || request.CategoryLimit is < 1 or > MaximumEntries) return "request-limit";
        if (!Text(request.Search, MaximumSearchLength) || !Text(request.Category, MaximumIdentityLength)) return "invalid-search";
        return "";
    }

    public static bool Text(string? value, int limit, bool required = false) => value != null && value.Length <= limit
        && (!required || !string.IsNullOrWhiteSpace(value)) && !value.Any(char.IsControl)
        && WellFormed(value);

    private static bool WellFormed(string value)
    {
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i])) continue;
            if (!char.IsHighSurrogate(value[i]) || i + 1 >= value.Length || !char.IsLowSurrogate(value[++i])) return false;
        }
        return true;
    }

    public static PlayerItemEntryPacket ToPacket(PlayerItemSummary entry) => new()
    {
        EntryId = entry.EntryId, AssetCode = entry.AssetCode,
        Category = entry.Category, DisplayName = Bounded(entry.DisplayName, MaximumDisplayLength),
        Quantity = entry.Quantity, ReservedQuantity = entry.ReservedQuantity, Kind = entry.Kind, Location = entry.Location,
        Flags = entry.Flags, ProtectionSource = entry.ProtectionSource,
        AuthoredCategory = entry.AuthoredCategory, AssetKind = entry.AssetKind, CategoryName = Bounded(entry.CategoryName, MaximumDisplayLength)
    };

    public static bool ValidEntry(PlayerItemEntryPacket? entry) => entry != null
        && Text(entry.EntryId, MaximumIdentityLength, true) && Text(entry.AssetCode, MaximumIdentityLength)
        && Text(entry.Category, MaximumIdentityLength) && Text(entry.AuthoredCategory, MaximumIdentityLength)
        && Text(entry.DisplayName, MaximumDisplayLength) && Text(entry.CategoryName, MaximumDisplayLength) && entry.Quantity > 0 && entry.ReservedQuantity >= 0
        && entry.ReservedQuantity <= entry.Quantity && Enum.IsDefined(entry.Kind) && Enum.IsDefined(entry.AssetKind)
        && Enum.IsDefined(entry.Location) && ((int)entry.Flags & ~7) == 0 && ((int)entry.ProtectionSource & ~3) == 0;

    internal static string Bounded(string value, int limit) => value.Length <= limit ? value
        : value[..(char.IsHighSurrogate(value[limit - 1]) ? limit - 1 : limit)];
}
