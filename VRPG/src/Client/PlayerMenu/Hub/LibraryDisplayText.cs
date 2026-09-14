using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;
using VRPG.Client.PlayerMenu.Inventory;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Turns technical library tokens into the same player-facing words used by display and search.</summary>
public static partial class LibraryDisplayText
{
    private static readonly HashSet<string> IdentifierFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Applies To", "Base profile", "Chance Stat", "Chassis", "Class", "Damage Profile",
        "Delivery", "Duration Stat", "Family", "Kind", "Layer", "Minimum rarity", "Model",
        "Operation", "Polarity", "Proc Mode", "Projectile impact", "Role", "Trigger Damage"
    };

    public static string Format(string? text) => TechnicalToken().Replace(text ?? "",
        match => PlayerItemDisplayNameResolver.HumanizeAssetPath(match.Value));

    /// <summary>Formats a value whose packet field is defined as a machine identifier.</summary>
    public static string FormatIdentifier(string? value)
    {
        string text = value?.Trim() ?? "";
        return AtomicIdentifier().IsMatch(text) ? PlayerItemDisplayNameResolver.HumanizeAssetPath(text) : Format(text);
    }

    /// <summary>Formats identifiers only in fields whose schema declares identifier values.</summary>
    public static string FormatField(string? label, string? value) =>
        IdentifierFields.Contains(label?.Trim() ?? "") ? FormatIdentifier(value) : Format(value);

    [GeneratedRegex(@"(?<![A-Za-z0-9])(?:[a-z][a-z0-9_-]*:[A-Za-z0-9_./-]+|[A-Za-z0-9]+(?:_[A-Za-z0-9]+)+)(?![A-Za-z0-9])")]
    private static partial Regex TechnicalToken();

    [GeneratedRegex(@"^(?:[a-z][a-z0-9_-]*:)?[A-Za-z0-9]+(?:[-_./][A-Za-z0-9]+)+$")]
    private static partial Regex AtomicIdentifier();
}
