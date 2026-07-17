namespace VRPG.Data.Library;

public sealed class LibraryEntry
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "general";
    public string Summary { get; set; } = "";
    public string Source { get; set; } = "";
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    public LibraryField[] Fields { get; set; } = System.Array.Empty<LibraryField>();
}
