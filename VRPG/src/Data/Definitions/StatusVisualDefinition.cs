namespace VRPG.Data.Definitions;

public sealed class StatusVisualDefinition
{
    public string Icon { get; set; } = "";
    public string Color { get; set; } = "";
    public string Aura { get; set; } = "";
    public float AuraIntensityPerStack { get; set; } = 1f;
    public bool ShowStacks { get; set; } = true;
    public StatusBuildupVisualDefinition? Buildup { get; set; }
}

public sealed class StatusBuildupVisualDefinition
{
    public bool ShowBar { get; set; } = true;
    public float Threshold { get; set; } = 100f;
    public bool FlashAtThreshold { get; set; } = true;
}
