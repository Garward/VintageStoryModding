using System;
using System.Collections.Generic;
using System.Globalization;
using VRPG.Data.Definitions;
using Vintagestory.API.Common;

namespace VRPG.Data;

public static class SkillDefinitionValidator
{
    private static readonly HashSet<string> Deliveries = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "raycast_aoe",
        "projectile_aoe",
        "circle",
        "melee_arc",
        "melee_line",
        "melee_single"
    };

    private static readonly HashSet<string> TimingModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "instant",
        "sequence",
        "channel"
    };

    private static readonly HashSet<string> ResourceCostModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "cast",
        "per_second"
    };

    private static readonly HashSet<string> Resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "none",
        "mana",
        "blood"
    };

    public static void Validate(ICoreAPI api, VRPGDataRegistry data)
    {
        var errors = new List<string>();
        foreach (SkillDefinition skill in data.Skills.All)
        {
            ValidateSkill(api, data, skill, errors);
        }
        ValidateClasses(data, errors);
        ValidateTalents(data, errors);

        if (errors.Count == 0)
        {
            return;
        }

        throw new InvalidOperationException(
            "VRPG gameplay data validation failed:\n- " + string.Join("\n- ", errors));
    }

    public static bool TryParseColor(string value, out int color)
    {
        color = -1;
        string hex = (value ?? "").Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8)
        {
            return false;
        }

        if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint packed))
        {
            return false;
        }

        int red;
        int green;
        int blue;
        int alpha;
        if (hex.Length == 6)
        {
            red = (int)((packed >> 16) & 0xff);
            green = (int)((packed >> 8) & 0xff);
            blue = (int)(packed & 0xff);
            alpha = 255;
        }
        else
        {
            red = (int)((packed >> 24) & 0xff);
            green = (int)((packed >> 16) & 0xff);
            blue = (int)((packed >> 8) & 0xff);
            alpha = (int)(packed & 0xff);
        }

        color = Vintagestory.API.MathTools.ColorUtil.ToRgba(alpha, red, green, blue);
        return true;
    }

    private static void ValidateSkill(ICoreAPI api, VRPGDataRegistry data, SkillDefinition skill, List<string> errors)
    {
        string label = string.IsNullOrWhiteSpace(skill.Code) ? "<missing code>" : skill.Code;
        if (!label.Contains(':'))
        {
            errors.Add(label + ": code must be namespaced (for example vrpg:rust_lance).");
        }

        if (string.IsNullOrWhiteSpace(skill.Name))
        {
            errors.Add(label + ": name is required.");
        }

        if (data.Classes.Get(NormalizeCode(skill.ClassCode)) == null)
        {
            errors.Add(label + ": unknown classCode " + skill.ClassCode + ".");
        }

        if (string.IsNullOrWhiteSpace(skill.Icon))
        {
            errors.Add(label + ": icon is required.");
        }

        if (!Deliveries.Contains(skill.Delivery))
        {
            errors.Add(label + ": delivery must be raycast_aoe, projectile_aoe, circle, melee_arc, melee_line, or melee_single.");
        }

        if (skill.RequiredLevel < 1 || skill.MaxLevel < 1)
        {
            errors.Add(label + ": requiredLevel and maxLevel must be at least 1.");
        }

        if (skill.CooldownSeconds < 0.1f)
        {
            errors.Add(label + ": cooldownSeconds must be at least 0.1 to bound network and combat spam.");
        }

        bool radiusDelivery = string.Equals(skill.Delivery, "raycast_aoe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skill.Delivery, "circle", StringComparison.OrdinalIgnoreCase);
        if ((radiusDelivery && skill.Radius <= 0f) || skill.Radius < 0f || skill.Radius > 32f)
        {
            errors.Add(label + ": area deliveries need radius greater than 0; radius cannot be negative or exceed 32 blocks.");
        }

        if (!string.Equals(skill.Delivery, "circle", StringComparison.OrdinalIgnoreCase)
            && (skill.Range <= 0f || skill.Range > 128f))
        {
            errors.Add(label + ": aimed skills need a range greater than 0 and no more than 128 blocks.");
        }

        if (skill.Damage == null
            || skill.Damage.Base < 0f
            || skill.Damage.PerLevel < 0f
            || skill.Damage.WeaponDamagePercent < 0f
            || skill.Damage.WeaponDamagePerLevelPercent < 0f)
        {
            errors.Add(label + ": damage base, perLevel, weaponDamagePercent, and weaponDamagePerLevelPercent cannot be negative.");
        }
        else if (data.DamageTypes.Get(NormalizeCode(skill.Damage.Type)) == null)
        {
            errors.Add(label + ": unknown damage type " + skill.Damage.Type + ".");
        }

        if (skill.Resource == null || !Resources.Contains(skill.Resource.Type))
        {
            errors.Add(label + ": resource.type must be none, mana, or blood.");
        }
        else if (skill.Resource.Base < 0f || skill.Resource.PerLevel < 0f)
        {
            errors.Add(label + ": resource base and perLevel cannot be negative.");
        }
        else if (!ResourceCostModes.Contains(skill.Resource.CostMode))
        {
            errors.Add(label + ": resource.costMode must be cast or per_second.");
        }

        ValidateMeleeAndTiming(skill, label, errors);

        if (!TryParseColor(skill.Color, out _))
        {
            errors.Add(label + ": color must be #RRGGBB or #RRGGBBAA.");
        }

        if (skill.Particles == null
            || (!string.Equals(skill.Particles.Model, "quad", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(skill.Particles.Model, "cube", StringComparison.OrdinalIgnoreCase))
            || skill.Particles.BurstQuantity < 0f
            || skill.Particles.TrailQuantity < 0f
            || skill.Particles.LifetimeSeconds <= 0f
            || skill.Particles.TrailLifetimeSeconds <= 0f
            || skill.Particles.Scale <= 0f
            || skill.Particles.Velocity < 0f
            || skill.Particles.OriginVerticalOffset < -2f
            || skill.Particles.OriginVerticalOffset > 2f
            || skill.Particles.OriginHorizontalOffset < -2f
            || skill.Particles.OriginHorizontalOffset > 2f
            || skill.Particles.OriginForwardOffset < 0f
            || skill.Particles.OriginForwardOffset > 3f)
        {
            errors.Add(label + ": particles need model quad/cube, valid quantities/scale/velocity/lifetime, origin offsets within 2 blocks, and originForwardOffset within 0-3.");
        }

        if (!string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(skill.Model))
        {
            errors.Add(label + ": projectile_aoe skills require a model shape path.");
        }
        else
        {
            try
            {
                AssetLocation shapeLocation = ShapeLocation(skill.Model);
                if (api.Assets.TryGet(shapeLocation) == null)
                {
                    errors.Add(label + ": model asset not found at " + shapeLocation + ".");
                }
            }
            catch (Exception ex)
            {
                errors.Add(label + ": model path is invalid (" + ex.Message + ").");
            }
        }

        if (skill.Projectile == null || skill.Projectile.Speed <= 0f || skill.Projectile.LifetimeSeconds <= 0f)
        {
            errors.Add(label + ": projectile speed and lifetimeSeconds must be greater than 0.");
        }
        else if (!string.Equals(skill.Projectile.ImpactMode, "entity", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skill.Projectile.ImpactMode, "ground", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(label + ": projectile impactMode must be entity or ground.");
        }
        else if (skill.Projectile.VerticalOffset < -2f
            || skill.Projectile.VerticalOffset > 2f
            || skill.Projectile.HorizontalOffset < -2f
            || skill.Projectile.HorizontalOffset > 2f
            || skill.Projectile.ForwardOffset < 0f
            || skill.Projectile.ForwardOffset > 3f
            || skill.Projectile.AimConvergenceDistance < 1f
            || skill.Projectile.AimConvergenceDistance > 128f)
        {
            errors.Add(label + ": projectile offsets must stay within 2 blocks, forwardOffset within 0-3, and aimConvergenceDistance within 1-128.");
        }
    }

    private static void ValidateMeleeAndTiming(SkillDefinition skill, string label, List<string> errors)
    {
        bool melee = (skill.Delivery ?? "").StartsWith("melee_", StringComparison.OrdinalIgnoreCase);
        if (melee && (skill.Range <= 0f || skill.Range > 16f))
        {
            errors.Add(label + ": melee range must be greater than 0 and no more than 16 blocks; it is authored independently of vanilla weapon reach.");
        }

        if (melee && (skill.Melee == null
            || skill.Melee.ArcDegrees <= 0f
            || skill.Melee.ArcDegrees > 360f
            || skill.Melee.Width <= 0f
            || skill.Melee.Width > 16f
            || skill.Melee.VerticalTolerance <= 0f
            || skill.Melee.VerticalTolerance > 8f))
        {
            errors.Add(label + ": melee needs arcDegrees within 0-360, width within 0-16, and verticalTolerance within 0-8.");
        }

        if (skill.Timing == null || !TimingModes.Contains(skill.Timing.Mode))
        {
            errors.Add(label + ": timing.mode must be instant, sequence, or channel.");
            return;
        }

        string mode = skill.Timing.Mode.ToLowerInvariant();
        if (mode == "instant" && skill.Timing.HitCount != 1)
        {
            errors.Add(label + ": instant skills must have timing.hitCount 1; use sequence for intentional multi-hit skills.");
        }
        else if (mode == "sequence"
            && (skill.Timing.HitCount < 2
                || skill.Timing.HitCount > 32
                || skill.Timing.HitIntervalSeconds < 0.05f
                || skill.Timing.HitIntervalSeconds > 5f))
        {
            errors.Add(label + ": sequence skills need 2-32 hits and hitIntervalSeconds within 0.05-5.");
        }
        else if (mode == "channel"
            && (skill.Timing.HitIntervalSeconds < 0.05f
                || skill.Timing.HitIntervalSeconds > 2f
                || skill.Timing.MaxDurationSeconds < skill.Timing.HitIntervalSeconds
                || skill.Timing.MaxDurationSeconds > 60f))
        {
            errors.Add(label + ": channel skills need hitIntervalSeconds within 0.05-2 and maxDurationSeconds from one tick through 60 seconds.");
        }

        if (skill.Resource != null
            && !string.Equals(mode, "channel", StringComparison.OrdinalIgnoreCase)
            && string.Equals(skill.Resource.CostMode, "per_second", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(label + ": per_second resource cost is only valid for channel skills.");
        }

        if (skill.Damage != null
            && (mode == "sequence" || mode == "channel")
            && !skill.Damage.IgnoreInvFrames)
        {
            errors.Add(label + ": intentional repeated-hit skills must set damage.ignoreInvFrames true so every authored hit can resolve.");
        }
    }

    private static void ValidateClasses(VRPGDataRegistry data, List<string> errors)
    {
        foreach (ClassDefinition definition in data.Classes.All)
        {
            string label = string.IsNullOrWhiteSpace(definition.Code) ? "<missing class code>" : definition.Code;
            if (string.IsNullOrWhiteSpace(definition.Name)) errors.Add(label + ": class name is required.");
            if (string.IsNullOrWhiteSpace(definition.Icon)) errors.Add(label + ": class icon is required.");
            if (!TryParseColor(definition.Color, out _)) errors.Add(label + ": class color must be #RRGGBB or #RRGGBBAA.");
        }
    }

    private static void ValidateTalents(VRPGDataRegistry data, List<string> errors)
    {
        var startersByFoundation = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frontier = new Queue<TalentNodeDefinition>();

        foreach (TalentNodeDefinition talent in data.Talents.All)
        {
            string label = string.IsNullOrWhiteSpace(talent.Code) ? "<missing talent code>" : talent.Code;
            if (string.IsNullOrWhiteSpace(talent.Name)) errors.Add(label + ": talent name is required.");
            if (string.IsNullOrWhiteSpace(talent.Foundation)) errors.Add(label + ": foundation is required for tree presentation and starter locking.");
            if (talent.Cost < 1) errors.Add(label + ": cost must be at least 1.");

            if (talent.Starter)
            {
                string foundation = (talent.Foundation ?? "").Trim();
                if (foundation.Length > 0 && startersByFoundation.TryGetValue(foundation, out string? existing))
                {
                    errors.Add(label + ": duplicate starter foundation " + foundation + " (already used by " + existing + ").");
                }
                else if (foundation.Length > 0)
                {
                    startersByFoundation[foundation] = label;
                }
                frontier.Enqueue(talent);
                reachable.Add(NormalizeCode(talent.Code));
            }

            for (int i = 0; i < talent.Links.Length; i++)
            {
                string link = NormalizeCode(talent.Links[i]);
                if (string.Equals(link, NormalizeCode(talent.Code), StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add(label + ": cannot link to itself.");
                }
                else if (data.Talents.Get(link) == null)
                {
                    errors.Add(label + ": unknown talent link " + talent.Links[i] + ".");
                }
            }
        }

        if (frontier.Count == 0 && data.Talents.Count > 0)
        {
            errors.Add("Talent tree: at least one starter node is required.");
            return;
        }

        while (frontier.Count > 0)
        {
            TalentNodeDefinition current = frontier.Dequeue();
            foreach (TalentNodeDefinition candidate in data.Talents.All)
            {
                string candidateCode = NormalizeCode(candidate.Code);
                if (reachable.Contains(candidateCode)) continue;
                if (LinksTo(current, candidate.Code) || LinksTo(candidate, current.Code))
                {
                    reachable.Add(candidateCode);
                    frontier.Enqueue(candidate);
                }
            }
        }

        foreach (TalentNodeDefinition talent in data.Talents.All)
        {
            if (!reachable.Contains(NormalizeCode(talent.Code)))
            {
                errors.Add(talent.Code + ": talent is unreachable from every starter node.");
            }
        }
    }

    private static bool LinksTo(TalentNodeDefinition from, string toCode)
    {
        string normalizedTarget = NormalizeCode(toCode);
        for (int i = 0; i < from.Links.Length; i++)
        {
            if (string.Equals(NormalizeCode(from.Links[i]), normalizedTarget, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static AssetLocation ShapeLocation(string model)
    {
        return new AssetLocation(model)
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json");
    }

    private static string NormalizeCode(string code)
    {
        return code != null && code.Contains(':') ? code : "vrpg:" + code;
    }
}
