using VRPG.Data;

namespace VRPG.Data.Definitions;

public sealed class StatusEffectDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Kind { get; set; } = "status";
    public string Polarity { get; set; } = "neutral";
    public string TriggerDamageType { get; set; } = "";
    public string ProcMode { get; set; } = "duration";
    public string ChanceStat { get; set; } = "";
    public string DurationStat { get; set; } = "";
    public string DamageProfile { get; set; } = "";
    public string StackMode { get; set; } = "refresh";
    public int MaxStacks { get; set; } = 1;
    public float DefaultDurationSeconds { get; set; } = 5f;
    public float TickSeconds { get; set; }
    public string[] Tags { get; set; } = System.Array.Empty<string>();
    public StatModifierDefinition[] Modifiers { get; set; } = System.Array.Empty<StatModifierDefinition>();
    public string VisualHint { get; set; } = "";
    public StatusVisualDefinition Visual { get; set; } = new StatusVisualDefinition();
}
