namespace VRPG.Client.Visuals;

/// <summary>
/// Client-local combat visual settings, persisted with the client mod config
/// and edited from the Hub Options "Combat Visuals" category.
/// </summary>
public sealed class CombatVisualsConfig
{
    public bool CombatTextEnabled { get; set; } = true;
    public bool DamageNumbers { get; set; } = true;
    public bool EventWords { get; set; } = true;
    public bool MergeNumbers { get; set; } = true;
    public bool StatusAuras { get; set; } = true;
    public float TelegraphOpacity { get; set; } = 1f;
    public string DegradationPolicy { get; set; } = "own-first";
    public float Intensity { get; set; } = 1f;
}
