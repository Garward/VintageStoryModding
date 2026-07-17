using System;
using VRPG.Data.Definitions;
using VRPG.Modules.Rpg.Combat;
using VRPG.Modules.Rpg.StatusEffects;
using VRPG.Network;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace VRPG.Modules.Rpg.Skills;

/// <summary>
/// Applies data-authored on-hit status operations and emits their confirmed
/// payoff events. Persistent presentation remains driven by StatusSync.
/// </summary>
public sealed class SkillStatusEffectService
{
    private readonly StatusEffectTracker statuses;
    private readonly CombatVisualBroadcaster visuals;

    public SkillStatusEffectService(StatusEffectTracker statuses, CombatVisualBroadcaster visuals)
    {
        this.statuses = statuses;
        this.visuals = visuals;
    }

    public void ApplyOnHit(EntityPlayer caster, Entity target, SkillDefinition skill, bool primaryTarget)
    {
        long sourceEntityId = caster.EntityId;
        SkillOnHitEffectDefinition[] effects = skill.OnHitEffects ?? Array.Empty<SkillOnHitEffectDefinition>();
        for (int i = 0; i < effects.Length; i++)
        {
            SkillOnHitEffectDefinition effect = effects[i];
            float magnitude = primaryTarget ? effect.PrimaryMagnitude : effect.SecondaryMagnitude;
            switch ((effect.Operation ?? "").Trim().ToLowerInvariant())
            {
                case "apply":
                    statuses.Apply(
                        target.EntityId,
                        effect.StatusCode,
                        sourceEntityId,
                        effect.DurationSeconds,
                        Math.Max(1, effect.Stacks),
                        magnitude);
                    break;
                case "add_stacks":
                    statuses.AddStacks(
                        target.EntityId,
                        effect.StatusCode,
                        sourceEntityId,
                        Math.Max(1, effect.Stacks),
                        effect.DurationSeconds);
                    break;
                case "add_buildup":
                    float total = statuses.AddMagnitude(
                        target.EntityId,
                        effect.StatusCode,
                        sourceEntityId,
                        magnitude,
                        effect.MaximumMagnitude,
                        effect.DurationSeconds);
                    if (total >= effect.MaximumMagnitude && effect.MaximumMagnitude > 0f)
                    {
                        statuses.Remove(target.EntityId, effect.StatusCode, sourceEntityId);
                        ApplyResult(target, sourceEntityId, effect);
                        Emit(effect.TriggerEvent, caster, target, skill, total);
                    }

                    break;
                case "consume_buildup":
                    float consumed = statuses.ConsumeMagnitude(
                        target.EntityId,
                        effect.StatusCode,
                        sourceEntityId,
                        magnitude);
                    if (consumed > 0f)
                    {
                        Emit(effect.TriggerEvent, caster, target, skill, consumed);
                        if (consumed >= effect.TriggerThreshold)
                        {
                            ApplyResult(target, sourceEntityId, effect);
                        }
                    }

                    break;
            }
        }
    }

    private void ApplyResult(Entity target, long sourceEntityId, SkillOnHitEffectDefinition effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.ResultStatusCode))
        {
            statuses.Apply(
                target.EntityId,
                effect.ResultStatusCode,
                sourceEntityId,
                effect.ResultDurationSeconds,
                1);
        }
    }

    private void Emit(
        string eventCode,
        EntityPlayer caster,
        Entity target,
        SkillDefinition skill,
        float magnitude)
    {
        if (!Enum.TryParse(eventCode, true, out CombatVisualKind kind))
        {
            return;
        }

        Vec3d center = new Vec3d(
            target.Pos.X,
            target.Pos.Y + Math.Max(0.1f, target.CollisionBox.YSize) * 0.5,
            target.Pos.Z);
        visuals.Send(new CombatVisualEventPacket
        {
            Kind = (byte)kind,
            StyleCode = skill.Code,
            SourceEntityId = caster.EntityId,
            TargetEntityId = target.EntityId,
            X = center.X,
            Y = center.Y,
            Z = center.Z,
            Radius = skill.Radius,
            Magnitude = magnitude,
            DamageType = VisualDamageTypes.FromCode(skill.Damage.Type)
        });
    }
}
