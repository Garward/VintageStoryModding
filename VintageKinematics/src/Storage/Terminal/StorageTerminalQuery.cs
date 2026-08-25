using System;

namespace VintageKinematics.Storage.Terminal
{
    /// <summary>Validated client intent for one bounded terminal page.</summary>
    public sealed class StorageTerminalQuery
    {
        public const int PageSize = 50;
        public const int MaxPageSize = 120;
        public const int MaxSearchLength = 64;

        public long RequestId { get; }
        public string Search { get; }
        public int Page { get; }
        public StorageTerminalSort Sort { get; }
        public int RequestedPageSize { get; }

        public StorageTerminalQuery(
            long requestId,
            string search,
            int page,
            StorageTerminalSort sort,
            int requestedPageSize = PageSize)
        {
            RequestId = Math.Max(0, requestId);
            Search = NormalizeSearch(search);
            Page = Math.Max(0, page);
            Sort = Enum.IsDefined(sort) ? sort : StorageTerminalSort.Name;
            RequestedPageSize = Math.Clamp(requestedPageSize, PageSize, MaxPageSize);
        }

        private static string NormalizeSearch(string search)
        {
            string normalized = (search ?? string.Empty).Trim();
            return normalized.Length <= MaxSearchLength
                ? normalized
                : normalized.Substring(0, MaxSearchLength);
        }
    }
}
