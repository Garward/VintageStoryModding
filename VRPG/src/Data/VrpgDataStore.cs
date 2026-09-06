using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Vintagestory.API.Common;

namespace VRPG.Data;

public sealed class VrpgDataStore<T> where T : class, IVrpgDataRecord
{
    private readonly string assetPath;
    private readonly List<T> ordered = new List<T>();
    private readonly Dictionary<string, T> byCode = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

    public VrpgDataStore(string assetPath)
    {
        this.assetPath = assetPath;
    }

    public IReadOnlyList<T> All => ordered;
    public int Count => ordered.Count;

    public void Load(ICoreAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);

        var assets = api.Assets.GetMany<T>(api.Logger, assetPath);
        if (assets.Count == 0)
        {
            string normalized = assetPath.Trim('/');
            int separator = normalized.IndexOf('/');
            if (separator > 0)
            {
                string domain = normalized.Substring(0, separator);
                string path = normalized.Substring(separator + 1);
                assets = api.Assets.GetMany<T>(api.Logger, path, domain);
            }
        }

        Replace(assets.Select(entry => new KeyValuePair<string, T>(entry.Key.ToString(), entry.Value)));
    }

    /// <summary>Loads deserialized records through the same canonical indexing contract as assets.</summary>
    public void Load(IEnumerable<T> records)
    {
        ArgumentNullException.ThrowIfNull(records);
        Replace(records.Select(record => new KeyValuePair<string, T>(record?.Code ?? "", record!)));
    }

    public T? Get(string code)
    {
        string normalized = NormalizeCode(code);
        return normalized.Length > 0 && byCode.TryGetValue(normalized, out T? record) ? record : null;
    }

    private void Replace(IEnumerable<KeyValuePair<string, T>> records)
    {
        ordered.Clear();
        byCode.Clear();

        foreach (KeyValuePair<string, T> entry in records)
        {
            T record = entry.Value;
            if (record == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(record.Code))
            {
                record.Code = entry.Key;
            }

            record.Code = NormalizeCode(record.Code);

            if (string.IsNullOrWhiteSpace(record.Name))
            {
                record.Name = record.Code;
            }

            byCode.TryAdd(record.Code, record);
            ordered.Add(record);
        }

        ordered.Sort((left, right) => string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeCode(string? code)
    {
        string value = (code ?? "").Trim().ToLowerInvariant();
        return value.Length == 0 || value.Contains(':') ? value : "vrpg:" + value;
    }

    public string FormatList(string title, int limit = 16)
    {
        if (ordered.Count == 0)
        {
            return title + ": none loaded.";
        }

        var sb = new StringBuilder();
        sb.Append(title).Append(": ").Append(ordered.Count).Append(" loaded");

        int count = Math.Min(limit, ordered.Count);
        for (int i = 0; i < count; i++)
        {
            sb.AppendLine().Append("- ").Append(ordered[i].Code).Append(": ").Append(ordered[i].Name);
        }

        if (ordered.Count > count)
        {
            sb.AppendLine().Append("... ").Append(ordered.Count - count).Append(" more");
        }

        return sb.ToString();
    }
}
