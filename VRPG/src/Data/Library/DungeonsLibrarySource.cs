using System.Collections.Generic;
using VRPG.Data.Definitions;

namespace VRPG.Data.Library;

public sealed class DungeonsLibrarySource : ILibrarySource
{
    public string Code => "dungeons";

    public IEnumerable<LibraryEntry> Build(VRPGDataRegistry data)
    {
        foreach (DungeonThemeDefinition theme in data.DungeonThemes.All)
        {
            yield return new LibraryEntry
            {
                Code = theme.Code,
                Name = theme.Name,
                Category = "rifts/themes",
                Summary = string.IsNullOrWhiteSpace(theme.Summary)
                    ? "Temporal rift theme using floor, wall, and accent block definitions."
                    : theme.Summary,
                Tags = theme.Tags.Length > 0 ? theme.Tags : new[] { "rift", "theme" },
                Fields = new[]
                {
                    new LibraryField("Floor", theme.FloorBlock),
                    new LibraryField("Wall", theme.WallBlock),
                    new LibraryField("Accent", theme.AccentBlock),
                    new LibraryField("Room", $"Y {theme.FloorY}, height {theme.RoomHeight}, door {theme.DoorWidth}")
                }
            };
        }
    }
}
