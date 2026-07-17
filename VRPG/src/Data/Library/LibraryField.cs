namespace VRPG.Data.Library;

public sealed class LibraryField
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";

    public LibraryField()
    {
    }

    public LibraryField(string label, string value)
    {
        Label = label;
        Value = value;
    }
}
