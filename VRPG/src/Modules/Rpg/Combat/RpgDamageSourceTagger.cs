using System.Runtime.CompilerServices;
using Vintagestory.API.Common;

namespace VRPG.Modules.Rpg.Combat;

/// <summary>Server-only metadata, tied to source object identity and never persisted.</summary>
public static class RpgDamageSourceTagger
{
    private static readonly ConditionalWeakTable<DamageSource, RpgDamageSourceMarker> skills = new();

    public static void MarkSkill(DamageSource source) => skills.GetValue(source, _ => new RpgDamageSourceMarker());
    public static bool IsSkill(DamageSource source) => source != null && skills.TryGetValue(source, out _);

    private sealed class RpgDamageSourceMarker { }
}
