using VRPG.Data.Definitions;

namespace VRPG.Modules.Rpg.StatusEffects;

public sealed class StatusEffectInstance
{
    public StatusEffectInstance(StatusEffectDefinition definition, long sourceEntityId, float durationSeconds, int stacks)
    {
        Definition = definition;
        SourceEntityId = sourceEntityId;
        RemainingSeconds = durationSeconds;
        DurationSeconds = durationSeconds;
        Stacks = stacks;
    }

    public StatusEffectDefinition Definition { get; }
    public long SourceEntityId { get; }
    public float RemainingSeconds { get; private set; }
    public float DurationSeconds { get; private set; }
    public int Stacks { get; private set; }
    public float Magnitude { get; private set; }

    public void Tick(float dt)
    {
        RemainingSeconds -= dt;
    }

    public void Refresh(float durationSeconds, int stacks)
    {
        RemainingSeconds = System.Math.Max(RemainingSeconds, durationSeconds);
        DurationSeconds = System.Math.Max(DurationSeconds, durationSeconds);
        Stacks = System.Math.Min(System.Math.Max(1, Definition.MaxStacks), System.Math.Max(Stacks, stacks));
    }

    public void AddMagnitude(float amount)
    {
        Magnitude = System.Math.Clamp(Magnitude + amount, 0f, 10000f);
    }
}
