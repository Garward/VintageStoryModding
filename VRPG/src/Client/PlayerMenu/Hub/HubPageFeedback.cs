using System;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Config;

namespace VRPG.Client.PlayerMenu.Hub;

/// <summary>Bounds player feedback without changing authoritative packets or diagnostic messages.</summary>
internal static class HubPageFeedback
{
    private static readonly Regex diagnostic = new(
        @"[a-z0-9_.-]+:[a-z0-9_./-]+|\b[a-z0-9]+_[a-z0-9_]+\b|\b[0-9a-f]{8}-[0-9a-f-]{27,}\b|\b[0-9a-f]{32,}\b|exception|stack trace|checksum|schema version",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static string Present(string? message, bool error, PlayerMenuPage page)
    {
        string text = message?.Trim() ?? "";
        if (text.Length == 0) return "";
        string route = page == PlayerMenuPage.Stats ? "stats" : page == PlayerMenuPage.Talents ? "talents" : "skills";
        if (text.StartsWith("Unknown skill:", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Unknown base stat:", StringComparison.OrdinalIgnoreCase))
            return Lang.Get("vrpg:menu-feedback-" + route + "-unavailable");
        // Inspect only a short, single-line message. Never wrap an unbounded server diagnostic in the UI.
        if (text.Length > 240 || text.Any(char.IsControl) || text.Contains('\\') || diagnostic.IsMatch(text))
            return Lang.Get(error ? "vrpg:menu-feedback-" + route + "-failed" : "vrpg:menu-feedback-saved");
        return text;
    }
}
