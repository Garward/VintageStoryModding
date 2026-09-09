using System.Text;

namespace VRPG.Client.PlayerMenu.Components;

public sealed class SearchField
{
    public bool Focused { get; set; }
    public string Text { get; private set; } = "";
    public void SetText(string value)
    {
        var text = new StringBuilder(128);
        foreach (var rune in value.EnumerateRunes())
        {
            if (Rune.IsControl(rune)) continue;
            if (text.Length + rune.Utf16SequenceLength > 128) break;
            text.Append(rune.ToString());
        }
        Text = text.ToString();
    }
    public bool ConsumesSemanticMenuAction => Focused;
}
