using System;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace VRPG.Modules.Rpg.Combat;

/// <summary>Preserves exact-hit server context through the engine's in-memory DoT queue.</summary>
public static class PlayerDamageOverTimeContext
{
    private sealed record QueuedHit(DamageSource Source, PlayerDefenseContext Context, bool Skill);
    private static readonly ConditionalWeakTable<DamageSource, ConditionalWeakTable<Entity, PlayerDefenseContext>> queuedContexts = new();
    private static readonly ConditionalWeakTable<DamageOverTimeEffect, QueuedHit> effects = new();
    private static readonly ConditionalWeakTable<DamageSource, PlayerDefenseContext> ticks = new();

    public static void Capture(Entity target, DamageSource source, PlayerDefenseContext context)
    {
        var targets = queuedContexts.GetOrCreateValue(source);
        targets.Remove(target);
        targets.Add(target, context with { DamageOverTime = true });
    }

    public static PlayerDefenseContext? TickContext(DamageSource source) =>
        ticks.TryGetValue(source, out var context) ? context : null;

    public static void AttachQueuedEffect(Entity target, DamageSource source, DamageOverTimeEffect effect)
    {
        if (!queuedContexts.TryGetValue(source, out var targets)
            || !targets.TryGetValue(target, out var context)) return;
        targets.Remove(target);
        effects.Add(effect, new QueuedHit(CloneSource(source), context, RpgDamageSourceTagger.IsSkill(source)));
    }

    public static DamageSource CreateTickSource(DamageOverTimeEffect effect)
    {
        bool captured = effects.TryGetValue(effect, out var hit);
        var source = captured ? CloneSource(hit!.Source) : new DamageSource();
        source.Duration = TimeSpan.Zero;
        source.DamageOverTimeType = effect.EffectType;
        ticks.Add(source, captured ? hit!.Context : new PlayerDefenseContext(DamageOverTime: true));
        if (captured && hit!.Skill) RpgDamageSourceTagger.MarkSkill(source);
        return source;
    }

    private static DamageSource CloneSource(DamageSource source) => new()
    {
        Source = source.Source, Type = source.Type, SourceEntity = source.SourceEntity, CauseEntity = source.CauseEntity,
        SourceBlock = source.SourceBlock, SourcePos = source.SourcePos?.Clone(), HitPosition = source.HitPosition?.Clone(),
        DamageTier = source.DamageTier, IgnoreInvFrames = source.IgnoreInvFrames, Duration = source.Duration,
        TicksPerDuration = source.TicksPerDuration, DamageOverTimeType = source.DamageOverTimeType,
        KnockbackStrength = source.KnockbackStrength, YDirKnockbackDiv = source.YDirKnockbackDiv
    };
}
