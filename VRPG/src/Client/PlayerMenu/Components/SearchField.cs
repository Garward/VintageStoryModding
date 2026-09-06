namespace VRPG.Client.PlayerMenu.Components;

public sealed class SearchField
{
    public bool Focused { get; set; }
    public string Text { get; private set; } = "";
    public void SetText(string value) => Text = value.Length <= 128 ? value : value[..128];
    public bool ConsumesSemanticMenuAction => Focused;
}
