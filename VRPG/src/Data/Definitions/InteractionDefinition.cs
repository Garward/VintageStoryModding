using System;
using VRPG.Data;

namespace VRPG.Data.Definitions;

/// <summary>Declares one server-authoritative interaction exposed by eligible entities.</summary>
public sealed class InteractionDefinition : IVrpgDataRecord
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string PromptKey { get; set; } = "vrpg-interaction-prompt-interact";
    public string HandlerCode { get; set; } = "";
    public string[] TargetTags { get; set; } = Array.Empty<string>();
    public string[] AllowedContexts { get; set; } = { "adventure" };
    public float Range { get; set; } = 4f;
    public bool RequireLineOfSight { get; set; } = true;
    public int Priority { get; set; }
    public float CooldownSeconds { get; set; }
}
