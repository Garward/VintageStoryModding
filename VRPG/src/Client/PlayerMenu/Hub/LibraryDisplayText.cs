using System.Text.RegularExpressions;
using VRPG.Client.PlayerMenu.Inventory;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Turns technical library tokens into the same player-facing words used by display and search.</summary>
public static partial class LibraryDisplayText
{
    public static string Format(string? text) => TechnicalToken().Replace(text ?? "",
        match => PlayerItemDisplayNameResolver.HumanizeAssetPath(match.Value));

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[a-z][a-z0-9_-]*:[A-Za-z0-9_./-]+|[A-Za-z0-9]+(?:_[A-Za-z0-9]+)+)(?![A-Za-z0-9])")]
    private static partial Regex TechnicalToken();
}
