namespace VRPG.Client.PlayerMenu.Party;

public sealed class PartyPanelState
{
    public bool IsOpen { get; internal set; }
    public bool ConfirmDisband { get; internal set; }
    public string SelectedId { get; internal set; } = "";
    public string Section { get; internal set; } = "members";
    public string Search { get; internal set; } = "";
    public bool ShowDetails { get; internal set; }
    public int Scroll { get; internal set; }
    public int DetailScroll { get; internal set; }
    internal PlayerMenuViewState? ReturnContext { get; set; }
}
