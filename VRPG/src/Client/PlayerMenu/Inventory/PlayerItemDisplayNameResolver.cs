using System;
using System.Globalization;
using System.Text;
using VRPG.Inventory;
using VRPG.Network;

namespace VRPG.Client.PlayerMenu.Inventory;

/// <summary>Resolves presentation-only item names without changing canonical item identity.</summary>
public sealed class PlayerItemDisplayNameResolver
{
    private readonly Func<string> unknownItemName;

    public PlayerItemDisplayNameResolver(Func<string> unknownItemName)
    {
        this.unknownItemName = unknownItemName ?? throw new ArgumentNullException(nameof(unknownItemName));
    }

    public string Resolve(PlayerItemEntryPacket entry, Func<PlayerItemEntryPacket, string?> localizedCollectibleName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(localizedCollectibleName);
        if (entry.Location == PlayerItemLocation.Unresolved) return UnknownItemName();

        string storedName = entry.DisplayName?.Trim() ?? "";
        bool storedNameIsAuthored = storedName.Length > 0 && !IsTechnicalName(storedName, entry.AssetCode);
        if (entry.Kind == PlayerItemStorageKind.Unique
            && string.Equals(entry.Category, PlayerItemCategories.Gear, StringComparison.OrdinalIgnoreCase)
            && storedNameIsAuthored)
        {
            return storedName;
        }

        string localizedName;
        try
        {
            localizedName = localizedCollectibleName(entry)?.Trim() ?? "";
        }
        catch
        {
            localizedName = "";
        }
        if (localizedName.Length > 0 && !IsTechnicalName(localizedName, entry.AssetCode)) return localizedName;

        string fallback = HumanizeAssetPath(entry.AssetCode);
        return fallback.Length > 0 ? fallback : UnknownItemName();
    }

    public static string HumanizeAssetPath(string assetCode)
    {
        string path = AssetPath(assetCode);
        if (path.Length == 0) return "";
        var result = new StringBuilder(path.Length + 8);
        for (int index = 0; index < path.Length; index++)
        {
            char current = path[index];
            if (IsSeparator(current))
            {
                AppendSpace(result);
                continue;
            }
            if (index > 0 && char.IsUpper(current)
                && (char.IsLower(path[index - 1]) || char.IsDigit(path[index - 1])
                    || char.IsUpper(path[index - 1]) && index + 1 < path.Length && char.IsLower(path[index + 1])))
            {
                AppendSpace(result);
            }
            result.Append(current);
        }
        string words = result.ToString().Trim();
        return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words.ToLower(CultureInfo.CurrentCulture));
    }

    private string UnknownItemName()
    {
        string value = unknownItemName()?.Trim() ?? "";
        return value.Length > 0 ? value : "Unknown item";
    }

    private static bool IsTechnicalName(string candidate, string assetCode)
    {
        string value = candidate.Trim();
        string asset = assetCode?.Trim() ?? "";
        string path = AssetPath(asset);
        if (value.Length == 0) return true;
        if (string.Equals(value, asset, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, path, StringComparison.OrdinalIgnoreCase)) return true;

        string candidatePath = AssetPath(value);
        if (candidatePath.StartsWith("item-", StringComparison.OrdinalIgnoreCase)) candidatePath = candidatePath[5..];
        else if (candidatePath.StartsWith("block-", StringComparison.OrdinalIgnoreCase)) candidatePath = candidatePath[6..];
        return string.Equals(NormalizedTechnicalPath(candidatePath), NormalizedTechnicalPath(path), StringComparison.OrdinalIgnoreCase);
    }

    private static string AssetPath(string assetCode)
    {
        string value = assetCode?.Trim() ?? "";
        int separator = value.IndexOf(':');
        return separator >= 0 ? value[(separator + 1)..].Trim() : value;
    }

    private static string NormalizedTechnicalPath(string value)
    {
        var normalized = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (!IsSeparator(character)) normalized.Append(character);
        }
        return normalized.ToString();
    }

    private static bool IsSeparator(char value) => value is '-' or '_' or '/' or '\\' || char.IsWhiteSpace(value);

    private static void AppendSpace(StringBuilder value)
    {
        if (value.Length > 0 && value[^1] != ' ') value.Append(' ');
    }
}
