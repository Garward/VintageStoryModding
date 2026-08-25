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
        "targeted_drop",
        "circle",
        "melee_arc",
        "melee_line",
        "melee_single"
    };

    private static readonly HashSet<string> TimingModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "instant",
        "sequence",
        "channel",
        "targeted_release"
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

    private static readonly HashSet<string> OnHitOperations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "apply",
        "add_stacks",
        "add_buildup",
        "consume_buildup"
    };

    private static readonly HashSet<string> TriggerEvents = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "break",
        "counter",
        "consume",
        "mark",
        "windowopen"
    };

    private static readonly HashSet<string> FxRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "debris", "dust", "sparks", "fire", "rim", "custom"
    };

    private static readonly HashSet<string> FxEvolveFunctions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "linear", "quadratic", "root", "sinus", "clamp", "identical"
    };

    public static void Validate(ICoreAPI api, VRPGDataRegistry data)
    {
        var errors = new List<string>();
        ValidateFxPresets(data, errors);
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

    public static bool IsFxRole(string value) => FxRoles.Contains(value ?? "");

    public static bool IsParticleModel(string value)
    {
        return string.Equals(value, "quad", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "cube", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFxColor(string value)
    {
        return string.Equals(value, "$skill", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "$ground", StringComparison.OrdinalIgnoreCase)
            || TryParseColor(value, out _);
    }

    public static bool IsFxEvolveFunction(string value) => FxEvolveFunctions.Contains(value ?? "");

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
            errors.Add(label + ": delivery must be raycast_aoe, projectile_aoe, targeted_drop, circle, melee_arc, melee_line, or melee_single.");
        }

        if (skill.RequiredLevel < 1 || skill.MaxLevel < 1)
        {
            errors.Add(label + ": requiredLevel and maxLevel must be at least 1.");
        }

        if (skill.CooldownSeconds < 0.1f)
        {
            errors.Add(label + ": cooldownSeconds must be at least 0.1 to bound network and combat spam.");
        }

        if (skill.Charges == null || skill.Charges.Maximum < 1 || skill.Charges.Maximum > 20)
        {
            errors.Add(label + ": charges.maximum must be between 1 and 20.");
        }

        bool radiusDelivery = string.Equals(skill.Delivery, "raycast_aoe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase)
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
        ValidateOnHitEffects(data, skill, label, errors);

        if (skill.GroundArea?.Enabled == true
            && (skill.GroundArea.DurationSeconds <= 0f
                || skill.GroundArea.DurationSeconds > 120f
                || skill.GroundArea.Radius < 0f
                || skill.GroundArea.Radius > 32f))
        {
            errors.Add(label + ": enabled groundArea needs durationSeconds within 0-120 and radius within 0-32 blocks; zero radius inherits the skill radius.");
        }

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

        SkillImpactVisualDefinition? impact = skill.ImpactVisual;
        if (impact == null)
        {
            errors.Add(label + ": impactVisual cannot be null.");
        }
        else if (impact.Enabled)
        {
            ValidateImpactVisual(api, data, skill, label, impact, errors);
        }

        bool modelDelivery = string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase);
        if (!modelDelivery)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(skill.Model))
        {
            errors.Add(label + ": model projectile and targeted-drop skills require a model shape path.");
        }
        else
        {
            ValidateShapeAsset(api, label + "/model", skill.Model, errors);
        }

        if (string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.Equals(skill.Timing.Mode, "targeted_release", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(label + ": targeted_drop requires timing.mode targeted_release.");
            }

            if (skill.TargetedDrop == null
                || skill.TargetedDrop.Height < 1f
                || skill.TargetedDrop.Height > 64f
                || skill.TargetedDrop.FallSpeed <= 0f
                || skill.TargetedDrop.FallSpeed > 64f
                || skill.TargetedDrop.Gravity <= 0f
                || skill.TargetedDrop.Gravity > 128f
                || skill.TargetedDrop.LifetimeSeconds <= 0f
                || skill.TargetedDrop.LifetimeSeconds > 60f)
            {
                errors.Add(label + ": targetedDrop needs height within 1-64 blocks, initial fallSpeed within 0-64 blocks per second, gravity within 0-128 blocks per second squared, and lifetimeSeconds within 0-60.");
            }
            else
            {
                double fallSeconds = (-skill.TargetedDrop.FallSpeed + Math.Sqrt(
                    skill.TargetedDrop.FallSpeed * skill.TargetedDrop.FallSpeed
                    + 2d * skill.TargetedDrop.Gravity * skill.TargetedDrop.Height)) / skill.TargetedDrop.Gravity;
                if (skill.TargetedDrop.LifetimeSeconds < fallSeconds + 0.5d)
                {
                    errors.Add(label + ": targetedDrop lifetimeSeconds must cover the complete accelerated fall plus a 0.5 second margin.");
                }
            }

            return;
        }

        if (skill.Projectile == null)
        {
            errors.Add(label + ": projectile speed and lifetimeSeconds must be greater than 0.");
            return;
        }

        if (skill.Projectile.Speed <= 0f || skill.Projectile.LifetimeSeconds <= 0f)
        {
            errors.Add(label + ": projectile speed and lifetimeSeconds must be greater than 0.");
        }
        else if (!string.Equals(skill.Projectile.ImpactMode, "entity", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skill.Projectile.ImpactMode, "ground", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skill.Projectile.ImpactMode, "either", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(label + ": projectile impactMode must be entity, ground, or either.");
        }

        if (skill.Projectile.CreatureCollisionRadius < 0.05f
            || skill.Projectile.CreatureCollisionRadius > 2f)
        {
            errors.Add(label + ": projectile.creatureCollisionRadius must be within 0.05-2 blocks.");
        }

        if (skill.Projectile.VerticalOffset < -2f
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

        if (skill.Projectile.Ballistic
            && (!string.Equals(skill.Timing.Mode, "targeted_release", StringComparison.OrdinalIgnoreCase)
                || skill.Projectile.MinimumFlightSeconds < 0.2f
                || skill.Projectile.MinimumFlightSeconds > 3f))
        {
            errors.Add(label + ": ballistic projectiles require timing.mode targeted_release and minimumFlightSeconds within 0.2-3.");
        }

        if (!string.Equals(skill.Projectile.RotationMode, "flight", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skill.Projectile.RotationMode, "tumble", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(skill.Projectile.RotationMode, "stable", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(label + ": projectile rotationMode must be flight, tumble, or stable.");
        }

        if (skill.Projectile.ModelVariants == null || skill.Projectile.ModelVariants.Length > 32)
        {
            errors.Add(label + ": projectile modelVariants must be a non-null list with at most 32 entries.");
        }
        else
        {
            for (int i = 0; i < skill.Projectile.ModelVariants.Length; i++)
            {
                string variant = skill.Projectile.ModelVariants[i] ?? "";
                if (string.IsNullOrWhiteSpace(variant))
                {
                    errors.Add(label + $"/projectile/modelVariants[{i}]: model path is required.");
                }
                else
                {
                    ValidateShapeAsset(api, label + $"/projectile/modelVariants[{i}]", variant, errors);
                }
            }
        }
    }

    private static void ValidateShapeAsset(ICoreAPI api, string label, string model, List<string> errors)
    {
        try
        {
            AssetLocation shapeLocation = ShapeLocation(model);
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

    private static void ValidateFxPresets(VRPGDataRegistry data, List<string> errors)
    {
        foreach (SkillFxPresetDefinition preset in data.SkillFxPresets.All)
        {
            string label = string.IsNullOrWhiteSpace(preset.Code) ? "<missing FX preset code>" : preset.Code;
            if (!label.Contains(':'))
            {
                errors.Add(label + ": FX preset code must be namespaced.");
            }

            ValidateFxLayers(label, preset.Layers, errors);
        }
    }

    private static void ValidateImpactVisual(
        ICoreAPI api,
        VRPGDataRegistry data,
        SkillDefinition skill,
        string label,
        SkillImpactVisualDefinition impact,
        List<string> errors)
    {
        bool hasPreset = !string.IsNullOrWhiteSpace(impact.Preset);
        bool hasLayers = impact.Layers is { Length: > 0 };
        if (hasPreset && hasLayers)
        {
            errors.Add(label + ": impactVisual cannot supply both preset and layers; layers would replace the preset.");
        }
        else if (!hasPreset && !hasLayers)
        {
            errors.Add(label + ": enabled impactVisual needs a preset or a direct layers list.");
        }

        SkillFxLayerDefinition[] layers = impact.Layers ?? Array.Empty<SkillFxLayerDefinition>();
        if (hasPreset)
        {
            SkillFxPresetDefinition? preset = data.SkillFxPresets.Get(NormalizeCode(impact.Preset));
            if (preset == null)
            {
                errors.Add(label + ": unknown impact FX preset " + impact.Preset + ".");
                layers = Array.Empty<SkillFxLayerDefinition>();
            }
            else
            {
                layers = preset.Layers;
            }
        }

        ValidateFxLayers(label, layers, errors);
        if (impact.Overrides == null)
        {
            errors.Add(label + ": impactVisual.overrides cannot be null.");
        }
        else
        {
            foreach (KeyValuePair<string, SkillFxLayerOverrideDefinition> entry in impact.Overrides)
            {
                bool roleExists = Array.Exists(layers, layer => layer != null && string.Equals(layer.Role, entry.Key, StringComparison.OrdinalIgnoreCase));
                if (!roleExists)
                {
                    errors.Add(label + ": impact override refers to missing layer role " + entry.Key + ".");
                }

                ValidateFxOverride(label + "/" + entry.Key, entry.Value, errors);
            }
        }

        if (impact.ParticleDurationScale < 0.2f || impact.ParticleDurationScale > 3f
            || impact.ExpansionSpeedScale < 0.2f || impact.ExpansionSpeedScale > 3f
            || impact.ShockwaveDurationSeconds < 0.08f || impact.ShockwaveDurationSeconds > 2f
            || impact.CameraShake < 0f || impact.CameraShake > 2f
            || impact.CameraShakeRange <= 0f || impact.CameraShakeRange > 128f
            || impact.Sounds == null || impact.Sounds.Length > 4
            || impact.SoundRange <= 0f || impact.SoundRange > 128f
            || impact.SoundVolume < 0f || impact.SoundVolume > 2f)
        {
            errors.Add(label + ": enabled impactVisual needs particle duration and expansion scales within 0.2-3, bounded shockwave and camera-shake values, at most four sounds, soundRange within 0-128, and soundVolume within 0-2.");
        }

        bool hasRim = Array.Exists(layers, layer => layer != null && string.Equals(layer.Role, "rim", StringComparison.OrdinalIgnoreCase));
        if (skill.Radius > 0f && !hasRim)
        {
            api.Logger.Warning("[VRPG] {0}: area impact FX has no rim layer; its gameplay radius will not be legible.", label);
        }

        foreach (SkillFxLayerDefinition layer in layers)
        {
            if (layer == null) continue;
            SkillFxLayerOverrideDefinition? layerOverride = FindOverride(impact.Overrides, layer.Role);
            bool informative = layerOverride?.Informative ?? layer.Informative;
            float delay = layerOverride?.DelaySeconds ?? layer.DelaySeconds;
            if (string.Equals(layer.Role, "rim", StringComparison.OrdinalIgnoreCase) && delay > 0f)
            {
                api.Logger.Warning("[VRPG] {0}/{1}: rim delay is ignored and pinned to zero.", label, layer.Role);
            }
            else if (informative && delay > 0f)
            {
                api.Logger.Warning(
                    delay > 0.2f
                        ? "[VRPG] {0}/{1}: informative delay {2:0.###}s exceeds 0.2s and will be demoted to decorative."
                        : "[VRPG] {0}/{1}: informative delay {2:0.###}s should be zero.",
                    label,
                    layer.Role,
                    delay);
            }
        }
    }

    private static void ValidateFxLayers(string label, SkillFxLayerDefinition[]? layers, List<string> errors)
    {
        if (layers == null)
        {
            errors.Add(label + ": layers cannot be null.");
            return;
        }

        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SkillFxLayerDefinition layer in layers)
        {
            string layerLabel = label + "/" + (layer?.Role ?? "<null>");
            if (layer == null)
            {
                errors.Add(layerLabel + ": layer cannot be null.");
                continue;
            }

            if (!IsFxRole(layer.Role)) errors.Add(layerLabel + ": unknown FX role.");
            if (!roles.Add(layer.Role)) errors.Add(layerLabel + ": layer roles must be unique for role-keyed overrides.");
            if (!IsParticleModel(layer.Model)) errors.Add(layerLabel + ": model must be quad or cube.");
            if (!IsFxColor(layer.Color)) errors.Add(layerLabel + ": color must be $skill, $ground, #RRGGBB, or #RRGGBBAA.");
            if (layer.Quantity < 0f || layer.Quantity > 300f) errors.Add(layerLabel + ": quantity must be within 0-300.");
            if (layer.SizeMin <= 0f || layer.SizeMax < layer.SizeMin || layer.SizeMax > 8f) errors.Add(layerLabel + ": size range is invalid.");
            if (layer.LifetimeSeconds <= 0f || layer.LifetimeSeconds > 5f) errors.Add(layerLabel + ": lifetimeSeconds must be within 0-5.");
            if (layer.Coverage < 0f || layer.Coverage > 4f) errors.Add(layerLabel + ": coverage must be within 0-4.");
            if (layer.OriginCoverage < 0f || layer.OriginCoverage > 1f) errors.Add(layerLabel + ": originCoverage must be within 0-1.");
            if (layer.Glow < 0 || layer.Glow > 255) errors.Add(layerLabel + ": glow must be within 0-255.");
            if (layer.DelaySeconds < 0f || layer.DelaySeconds > 5f) errors.Add(layerLabel + ": delaySeconds must be within 0-5.");
            ValidateEvolve(layerLabel + "/opacityEvolve", layer.OpacityEvolve, errors);
            ValidateEvolve(layerLabel + "/sizeEvolve", layer.SizeEvolve, errors);
        }
    }

    private static void ValidateFxOverride(string label, SkillFxLayerOverrideDefinition? value, List<string> errors)
    {
        if (value == null)
        {
            errors.Add(label + ": override cannot be null.");
            return;
        }

        if (value.Model != null && !IsParticleModel(value.Model)) errors.Add(label + ": override model must be quad or cube.");
        if (value.Color != null && !IsFxColor(value.Color)) errors.Add(label + ": override color is invalid.");
        if (value.Quantity is < 0f or > 300f) errors.Add(label + ": override quantity must be within 0-300.");
        if (value.SizeMin is <= 0f or > 8f || value.SizeMax is <= 0f or > 8f) errors.Add(label + ": override sizes must be within 0-8.");
        if (value.LifetimeSeconds is <= 0f or > 5f) errors.Add(label + ": override lifetimeSeconds must be within 0-5.");
        if (value.Coverage is < 0f or > 4f) errors.Add(label + ": override coverage must be within 0-4.");
        if (value.OriginCoverage is < 0f or > 1f) errors.Add(label + ": override originCoverage must be within 0-1.");
        if (value.Glow is < 0 or > 255) errors.Add(label + ": override glow must be within 0-255.");
        if (value.DelaySeconds is < 0f or > 5f) errors.Add(label + ": override delaySeconds must be within 0-5.");
        ValidateEvolve(label + "/opacityEvolve", value.OpacityEvolve, errors);
        ValidateEvolve(label + "/sizeEvolve", value.SizeEvolve, errors);
    }

    private static void ValidateEvolve(string label, SkillFxEvolveDefinition? evolve, List<string> errors)
    {
        if (evolve != null && !IsFxEvolveFunction(evolve.Fn))
        {
            errors.Add(label + ": unknown evolve function " + evolve.Fn + ".");
        }
    }

    private static SkillFxLayerOverrideDefinition? FindOverride(
        Dictionary<string, SkillFxLayerOverrideDefinition>? overrides,
        string role)
    {
        if (overrides != null)
        {
            foreach (KeyValuePair<string, SkillFxLayerOverrideDefinition> entry in overrides)
            {
                if (string.Equals(entry.Key, role, StringComparison.OrdinalIgnoreCase)) return entry.Value;
            }
        }

        return null;
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
            errors.Add(label + ": timing.mode must be instant, sequence, channel, or targeted_release.");
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

        else if (mode == "targeted_release"
            && !string.Equals(skill.Delivery, "targeted_drop", StringComparison.OrdinalIgnoreCase)
            && !(string.Equals(skill.Delivery, "projectile_aoe", StringComparison.OrdinalIgnoreCase)
                && skill.Projectile?.Ballistic == true))
        {
            errors.Add(label + ": targeted_release requires targeted_drop or a ballistic projectile_aoe delivery.");
        }

        if (skill.Timing.RepeatWhileHeld
            && (mode != "targeted_release"
                || (skill.Charges?.Maximum ?? 1) <= 1
                || skill.Timing.HoldRepeatDelaySeconds < 0.15f
                || skill.Timing.HoldRepeatDelaySeconds > 2f
                || skill.Timing.HoldRepeatIntervalSeconds < 0.1f
                || skill.Timing.HoldRepeatIntervalSeconds > 5f))
        {
            errors.Add(label + ": repeatWhileHeld requires a targeted_release skill with multiple charges, holdRepeatDelaySeconds within 0.15-2, and holdRepeatIntervalSeconds within 0.1-5.");
        }

        if (mode == "channel" && (skill.Charges?.Maximum ?? 1) > 1)
        {
            errors.Add(label + ": channel skills cannot store multiple activations; channel cooldown starts when the hold ends.");
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

    private static void ValidateOnHitEffects(
        VRPGDataRegistry data,
        SkillDefinition skill,
        string label,
        List<string> errors)
    {
        SkillOnHitEffectDefinition[] effects = skill.OnHitEffects ?? Array.Empty<SkillOnHitEffectDefinition>();
        for (int i = 0; i < effects.Length; i++)
        {
            SkillOnHitEffectDefinition effect = effects[i];
            string effectLabel = label + $": onHitEffects[{i}]";
            if (data.StatusEffects.Get(NormalizeCode(effect.StatusCode)) == null)
            {
                errors.Add(effectLabel + ": unknown statusCode " + effect.StatusCode + ".");
            }

            if (!OnHitOperations.Contains(effect.Operation))
            {
                errors.Add(effectLabel + ": operation must be apply, add_stacks, add_buildup, or consume_buildup.");
            }

            if (effect.Stacks < 1
                || effect.PrimaryMagnitude < 0f
                || effect.SecondaryMagnitude < 0f
                || effect.DurationSeconds < 0f
                || effect.MaximumMagnitude <= 0f
                || effect.TriggerThreshold < 0f
                || effect.ResultDurationSeconds < 0f)
            {
                errors.Add(effectLabel + ": stacks and all duration/magnitude fields must use valid non-negative values, with stacks and maximumMagnitude above zero.");
            }

            if ((string.Equals(effect.Operation, "add_buildup", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(effect.Operation, "consume_buildup", StringComparison.OrdinalIgnoreCase))
                && effect.PrimaryMagnitude <= 0f)
            {
                errors.Add(effectLabel + ": buildup operations require primaryMagnitude above zero.");
            }

            if (!string.IsNullOrWhiteSpace(effect.TriggerEvent) && !TriggerEvents.Contains(effect.TriggerEvent))
            {
                errors.Add(effectLabel + ": unknown triggerEvent " + effect.TriggerEvent + ".");
            }

            if (!string.IsNullOrWhiteSpace(effect.ResultStatusCode)
                && data.StatusEffects.Get(NormalizeCode(effect.ResultStatusCode)) == null)
            {
                errors.Add(effectLabel + ": unknown resultStatusCode " + effect.ResultStatusCode + ".");
            }
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
