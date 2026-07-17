using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Rpg.StatusEffects;

public sealed class StatusEffectTracker
{
    private readonly VRPGDataRegistry data;
    private readonly Dictionary<long, List<StatusEffectInstance>> byEntityId = new Dictionary<long, List<StatusEffectInstance>>();

    public event System.Action<long>? Changed;

    public StatusEffectTracker(VRPGDataRegistry data)
    {
        this.data = data;
    }

    public bool Apply(long targetEntityId, string effectCode, long sourceEntityId = 0, float durationSeconds = 0, int stacks = 1, float magnitude = 0f)
    {
        StatusEffectDefinition? definition = data.StatusEffects.Get(NormalizeCode(effectCode));
        if (definition == null)
        {
            return false;
        }

        if (!byEntityId.TryGetValue(targetEntityId, out List<StatusEffectInstance>? effects))
        {
            effects = new List<StatusEffectInstance>();
            byEntityId[targetEntityId] = effects;
        }

        float duration = durationSeconds > 0 ? durationSeconds : definition.DefaultDurationSeconds;
        int clampedStacks = Math.Max(1, Math.Min(Math.Max(1, definition.MaxStacks), stacks));

        if (!string.Equals(definition.StackMode, "independent_stacks", StringComparison.OrdinalIgnoreCase))
        {
            StatusEffectInstance? existing = effects.FirstOrDefault(effect => string.Equals(effect.Definition.Code, definition.Code, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Refresh(duration, clampedStacks);
                existing.AddMagnitude(magnitude);
                Changed?.Invoke(targetEntityId);
                return true;
            }
        }

        effects.Add(new StatusEffectInstance(definition, sourceEntityId, duration, clampedStacks));
        if (magnitude != 0f)
        {
            effects[effects.Count - 1].AddMagnitude(magnitude);
        }

        Changed?.Invoke(targetEntityId);
        return true;
    }

    public void Tick(float dt)
    {
        if (dt <= 0)
        {
            return;
        }

        foreach (long entityId in byEntityId.Keys.ToArray())
        {
            List<StatusEffectInstance> effects = byEntityId[entityId];
            bool removedAny = false;
            for (int i = effects.Count - 1; i >= 0; i--)
            {
                effects[i].Tick(dt);
                if (effects[i].RemainingSeconds <= 0)
                {
                    effects.RemoveAt(i);
                    removedAny = true;
                }
            }

            if (removedAny)
            {
                Changed?.Invoke(entityId);
            }

            if (effects.Count == 0)
            {
                byEntityId.Remove(entityId);
            }
        }
    }

    public IReadOnlyList<StatusEffectInstance> Get(long entityId)
    {
        return byEntityId.TryGetValue(entityId, out List<StatusEffectInstance>? effects)
            ? effects
            : Array.Empty<StatusEffectInstance>();
    }

    public string Format(long entityId)
    {
        IReadOnlyList<StatusEffectInstance> effects = Get(entityId);
        if (effects.Count == 0)
        {
            return "No VRPG status effects.";
        }

        var sb = new StringBuilder();
        sb.Append("VRPG status effects:");
        foreach (StatusEffectInstance effect in effects.OrderBy(effect => effect.Definition.Polarity).ThenBy(effect => effect.Definition.Name))
        {
            sb.AppendLine()
                .Append("- ").Append(effect.Definition.Name)
                .Append(" x").Append(effect.Stacks)
                .Append(" (").Append(effect.RemainingSeconds.ToString("0.0")).Append("s)")
                .Append(": ").Append(effect.Definition.Description);
        }

        return sb.ToString();
    }

    private static string NormalizeCode(string code)
    {
        return code != null && code.Contains(':') ? code : "vrpg:" + code;
    }
}
