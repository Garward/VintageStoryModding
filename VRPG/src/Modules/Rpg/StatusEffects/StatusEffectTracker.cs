using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using VRPG.Data;
using VRPG.Data.Definitions;

namespace VRPG.Modules.Rpg.StatusEffects;

public sealed class StatusEffectTracker
{
    private readonly Func<string, StatusEffectDefinition?> resolveDefinition;
    private readonly Dictionary<long, List<StatusEffectInstance>> byEntityId = new Dictionary<long, List<StatusEffectInstance>>();

    public event System.Action<long>? Changed;

    public StatusEffectTracker(VRPGDataRegistry data)
        : this(code => data.StatusEffects.Get(code))
    {
    }

    public StatusEffectTracker(Func<string, StatusEffectDefinition?> resolveDefinition)
    {
        this.resolveDefinition = resolveDefinition;
    }

    public bool Apply(long targetEntityId, string effectCode, long sourceEntityId = 0, float durationSeconds = 0, int stacks = 1, float magnitude = 0f)
    {
        StatusEffectDefinition? definition = resolveDefinition(NormalizeCode(effectCode));
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
            StatusEffectInstance? existing = FindOwned(effects, definition.Code, sourceEntityId);
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

    public int AddStacks(long targetEntityId, string effectCode, long sourceEntityId, int stacks, float durationSeconds = 0f)
    {
        StatusEffectDefinition? definition = resolveDefinition(NormalizeCode(effectCode));
        if (definition == null || stacks <= 0)
        {
            return 0;
        }

        List<StatusEffectInstance> effects = GetOrCreate(targetEntityId);
        StatusEffectInstance? existing = FindOwned(effects, definition.Code, sourceEntityId);
        if (existing == null)
        {
            int initialStacks = Math.Min(Math.Max(1, definition.MaxStacks), stacks);
            float duration = durationSeconds > 0f ? durationSeconds : definition.DefaultDurationSeconds;
            existing = new StatusEffectInstance(definition, sourceEntityId, duration, initialStacks);
            effects.Add(existing);
        }
        else
        {
            existing.Refresh(durationSeconds > 0f ? durationSeconds : definition.DefaultDurationSeconds, existing.Stacks);
            existing.AddStacks(stacks);
        }

        Changed?.Invoke(targetEntityId);
        return existing.Stacks;
    }

    public float AddMagnitude(
        long targetEntityId,
        string effectCode,
        long sourceEntityId,
        float magnitude,
        float maximum,
        float durationSeconds = 0f)
    {
        StatusEffectDefinition? definition = resolveDefinition(NormalizeCode(effectCode));
        if (definition == null || magnitude <= 0f || maximum <= 0f)
        {
            return 0f;
        }

        List<StatusEffectInstance> effects = GetOrCreate(targetEntityId);
        StatusEffectInstance? existing = FindOwned(effects, definition.Code, sourceEntityId);
        if (existing == null)
        {
            float duration = durationSeconds > 0f ? durationSeconds : definition.DefaultDurationSeconds;
            existing = new StatusEffectInstance(definition, sourceEntityId, duration, 1);
            effects.Add(existing);
        }
        else
        {
            existing.Refresh(durationSeconds > 0f ? durationSeconds : definition.DefaultDurationSeconds, existing.Stacks);
        }

        existing.AddMagnitude(magnitude, maximum);
        Changed?.Invoke(targetEntityId);
        return existing.Magnitude;
    }

    public float ConsumeMagnitude(long targetEntityId, string effectCode, long sourceEntityId, float amount)
    {
        if (!byEntityId.TryGetValue(targetEntityId, out List<StatusEffectInstance>? effects))
        {
            return 0f;
        }

        StatusEffectInstance? existing = FindOwned(effects, NormalizeCode(effectCode), sourceEntityId);
        if (existing == null)
        {
            return 0f;
        }

        float consumed = existing.RemoveMagnitude(amount);
        if (existing.Magnitude <= 0f)
        {
            effects.Remove(existing);
            if (effects.Count == 0)
            {
                byEntityId.Remove(targetEntityId);
            }
        }

        if (consumed > 0f)
        {
            Changed?.Invoke(targetEntityId);
        }

        return consumed;
    }

    public bool Remove(long targetEntityId, string effectCode, long sourceEntityId)
    {
        if (!byEntityId.TryGetValue(targetEntityId, out List<StatusEffectInstance>? effects))
        {
            return false;
        }

        StatusEffectInstance? existing = FindOwned(effects, NormalizeCode(effectCode), sourceEntityId);
        if (existing == null)
        {
            return false;
        }

        effects.Remove(existing);
        if (effects.Count == 0)
        {
            byEntityId.Remove(targetEntityId);
        }

        Changed?.Invoke(targetEntityId);
        return true;
    }

    public StatusEffectInstance? GetOwned(long targetEntityId, string effectCode, long sourceEntityId)
    {
        return byEntityId.TryGetValue(targetEntityId, out List<StatusEffectInstance>? effects)
            ? FindOwned(effects, NormalizeCode(effectCode), sourceEntityId)
            : null;
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

    private List<StatusEffectInstance> GetOrCreate(long targetEntityId)
    {
        if (!byEntityId.TryGetValue(targetEntityId, out List<StatusEffectInstance>? effects))
        {
            effects = new List<StatusEffectInstance>();
            byEntityId[targetEntityId] = effects;
        }

        return effects;
    }

    private static StatusEffectInstance? FindOwned(
        IEnumerable<StatusEffectInstance> effects,
        string effectCode,
        long sourceEntityId)
    {
        return effects.FirstOrDefault(effect =>
            effect.SourceEntityId == sourceEntityId
            && string.Equals(effect.Definition.Code, effectCode, StringComparison.OrdinalIgnoreCase));
    }
}
