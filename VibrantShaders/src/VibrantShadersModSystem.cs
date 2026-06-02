using System;
using System.IO;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using ConfigLib;

namespace VibrantShaders
{
    public class VibrantShadersModSystem : ModSystem
    {
        private ICoreClientAPI? capi;
        private IConfigProvider? configLib;
        private string colorMapShaderPath = "";
        private string colorMapFragmentShaderPath = "";
        private bool pendingShaderReload = false;
        private bool canRewriteShaderAssets = false;
        private long? updateListenerId;
        private FinalShaderBindings? finalShaderBindings;

        private bool enableVibrantShaders = true;

        // Current settings
        private float vibranceStrength = 0.256f;
        private float warmShadows = 0.028f;
        private float coolHighlights = 0.023f;
        private float vignetteStrength = 0.037f;
        private float vignetteSoftness = 0.705f;
        private float filmGrain = 0.000f;

        // Moonlight settings
        private float moonlightStrength = 0.000f;
        private float moonlightBlueTint = 0.000f;

        // Color enhancement settings
        private float blueBoost = 0.148f;
        private float greenBoost = 0.000f;
        private float warmBoost = 0.209f;
        private float shadowBlueness = 0.055f;

        // Shaderpack-style finishing settings
        private float tonemapStrength = 0.773f;
        private float bloomStrength = 0.990f;
        private float bloomSoftKnee = 0.575f;
        private float depthHazeStrength = 0.449f;
        private float depthHazeDistance = 0.552f;
        private float godrayStrength = 0.967f;
        private float sceneLiftStrength = 0.092f;
        private float localContrastStrength = 0.187f;
        private float colorRichness = 0.800f;
        private float earthToneSeparation = 0.441f;
        private float seasonGrassCorrection = 0.000f;
        private float seasonalGrassInfluence = 1.000f;
        private float climatePlantInfluence = 0.443f;
        private float frostTintInfluence = 0.015f;
        private float colormapNaturalizeStrength = 0.670f;
        private float colormapChromaStrength = 1.256f;
        private float colormapYellowGreenGuard = 0.833f;
        private float colormapBrightness = 0.500f;
        private float warmLightStability = 0.650f;
        private float goldenHourStrength = 0.000f;
        private int seasonalGrassMapIndex = 14;
        private int climatePlantMapIndex = 0;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            canRewriteShaderAssets = Directory.Exists(Mod.SourcePath);
            if (canRewriteShaderAssets)
            {
                colorMapShaderPath = Path.Combine(Mod.SourcePath, "assets", "game", "shaderincludes", "colormap.vsh");
                colorMapFragmentShaderPath = Path.Combine(Mod.SourcePath, "assets", "game", "shaderincludes", "colormap.fsh");
            }

            // Get ConfigLib API
            configLib = api.ModLoader.GetModSystem<ConfigLibModSystem>();
            if (configLib != null)
            {
                // Listen for setting changes
                configLib.SettingChanged += OnSettingChanged;

                // Load initial values
                LoadSettings();

                api.Logger.Notification("[VibrantShaders] ConfigLib integration enabled");
            }
            else
            {
                api.Logger.Warning("[VibrantShaders] ConfigLib not found, using default values");
            }

            ResolveSeasonalColorMapIndices();

            // Initial shader update for unpacked dev installs. Zipped installs are driven by live uniforms.
            UpdateShader();

            finalShaderBindings = new FinalShaderBindings(this, api);
            api.Event.RegisterRenderer(finalShaderBindings, EnumRenderStage.AfterPostProcessing, "vibrantshaders-final-bindings");

            // Register tick listener for deferred shader reload and moon phase updates
            updateListenerId = api.Event.RegisterGameTickListener(OnGameTick, 500);
        }

        private void OnGameTick(float dt)
        {
            // Handle deferred shader reload (avoids crash during rendering)
            if (pendingShaderReload)
            {
                pendingShaderReload = false;
                try
                {
                    bool success = capi!.Shader.ReloadShaders();
                    if (success)
                    {
                        capi.Logger.Notification("[VibrantShaders] Shaders reloaded successfully");
                    }
                    else
                    {
                        capi.Logger.Error("[VibrantShaders] Shader reload failed - check for shader errors");
                    }
                }
                catch (Exception ex)
                {
                    capi!.Logger.Error($"[VibrantShaders] Failed to reload shaders: {ex.Message}");
                }
            }

        }

        private void LoadSettings()
        {
            var config = configLib?.GetConfig("vibrantshaders");
            if (config == null) return;

            enableVibrantShaders = GetBoolSetting(config, "enable_vibrant_shaders", enableVibrantShaders);
            vibranceStrength = GetFloatSetting(config, "vibrance_strength", vibranceStrength);
            warmShadows = GetFloatSetting(config, "warm_shadows", warmShadows);
            coolHighlights = GetFloatSetting(config, "cool_highlights", coolHighlights);
            vignetteStrength = GetFloatSetting(config, "vignette_strength", vignetteStrength);
            vignetteSoftness = GetFloatSetting(config, "vignette_softness", vignetteSoftness);
            filmGrain = GetFloatSetting(config, "film_grain", filmGrain);
            moonlightStrength = GetFloatSetting(config, "moonlight_strength", moonlightStrength);
            moonlightBlueTint = GetFloatSetting(config, "moonlight_blue_tint", moonlightBlueTint);
            blueBoost = GetFloatSetting(config, "blue_boost", blueBoost);
            greenBoost = GetFloatSetting(config, "green_boost", greenBoost);
            warmBoost = GetFloatSetting(config, "warm_boost", warmBoost);
            shadowBlueness = GetFloatSetting(config, "shadow_blueness", shadowBlueness);
            tonemapStrength = GetFloatSetting(config, "tonemap_strength", tonemapStrength);
            bloomStrength = GetFloatSetting(config, "bloom_strength", bloomStrength);
            bloomSoftKnee = GetFloatSetting(config, "bloom_soft_knee", bloomSoftKnee);
            depthHazeStrength = GetFloatSetting(config, "depth_haze_strength", depthHazeStrength);
            depthHazeDistance = GetFloatSetting(config, "depth_haze_distance", depthHazeDistance);
            godrayStrength = GetFloatSetting(config, "godray_strength", godrayStrength);
            sceneLiftStrength = GetFloatSetting(config, "scene_lift_strength", sceneLiftStrength);
            localContrastStrength = GetFloatSetting(config, "local_contrast_strength", localContrastStrength);
            colorRichness = GetFloatSetting(config, "color_richness", colorRichness);
            earthToneSeparation = GetFloatSetting(config, "earth_tone_separation", earthToneSeparation);
            seasonGrassCorrection = GetFloatSetting(config, "season_grass_correction", seasonGrassCorrection);
            seasonalGrassInfluence = GetFloatSetting(config, "seasonal_grass_influence", seasonalGrassInfluence);
            climatePlantInfluence = GetFloatSetting(config, "climate_plant_influence", climatePlantInfluence);
            frostTintInfluence = GetFloatSetting(config, "frost_tint_influence", frostTintInfluence);
            colormapNaturalizeStrength = GetFloatSetting(config, "colormap_naturalize_strength", colormapNaturalizeStrength);
            colormapChromaStrength = GetFloatSetting(config, "colormap_chroma_strength", colormapChromaStrength);
            colormapYellowGreenGuard = GetFloatSetting(config, "colormap_yellow_green_guard", colormapYellowGreenGuard);
            colormapBrightness = GetFloatSetting(config, "colormap_brightness", colormapBrightness);
            warmLightStability = GetFloatSetting(config, "warm_light_stability", warmLightStability);
            goldenHourStrength = GetFloatSetting(config, "golden_hour_strength", goldenHourStrength);
        }

        private float GetFloatSetting(IConfig config, string code, float defaultValue)
        {
            var setting = config.GetSetting(code);
            if (setting != null)
            {
                return setting.Value.AsFloat(defaultValue);
            }
            return defaultValue;
        }

        private bool GetBoolSetting(IConfig config, string code, bool defaultValue)
        {
            var setting = config.GetSetting(code);
            if (setting != null)
            {
                return setting.Value.AsBool(defaultValue);
            }
            return defaultValue;
        }

        private void OnSettingChanged(string domain, IConfig config, ISetting setting)
        {
            if (domain != "vibrantshaders") return;

            capi?.Logger.Debug($"[VibrantShaders] Setting changed: {setting.YamlCode}");

            // Update the relevant setting
            switch (setting.YamlCode)
            {
                case "enable_vibrant_shaders":
                    enableVibrantShaders = setting.Value.AsBool(enableVibrantShaders);
                    break;
                case "vibrance_strength":
                    vibranceStrength = setting.Value.AsFloat(vibranceStrength);
                    break;
                case "warm_shadows":
                    warmShadows = setting.Value.AsFloat(warmShadows);
                    break;
                case "cool_highlights":
                    coolHighlights = setting.Value.AsFloat(coolHighlights);
                    break;
                case "vignette_strength":
                    vignetteStrength = setting.Value.AsFloat(vignetteStrength);
                    break;
                case "vignette_softness":
                    vignetteSoftness = setting.Value.AsFloat(vignetteSoftness);
                    break;
                case "film_grain":
                    filmGrain = setting.Value.AsFloat(filmGrain);
                    break;
                case "moonlight_strength":
                    moonlightStrength = setting.Value.AsFloat(moonlightStrength);
                    break;
                case "moonlight_blue_tint":
                    moonlightBlueTint = setting.Value.AsFloat(moonlightBlueTint);
                    break;
                case "blue_boost":
                    blueBoost = setting.Value.AsFloat(blueBoost);
                    break;
                case "green_boost":
                    greenBoost = setting.Value.AsFloat(greenBoost);
                    break;
                case "warm_boost":
                    warmBoost = setting.Value.AsFloat(warmBoost);
                    break;
                case "shadow_blueness":
                    shadowBlueness = setting.Value.AsFloat(shadowBlueness);
                    break;
                case "tonemap_strength":
                    tonemapStrength = setting.Value.AsFloat(tonemapStrength);
                    break;
                case "bloom_strength":
                    bloomStrength = setting.Value.AsFloat(bloomStrength);
                    break;
                case "bloom_soft_knee":
                    bloomSoftKnee = setting.Value.AsFloat(bloomSoftKnee);
                    break;
                case "depth_haze_strength":
                    depthHazeStrength = setting.Value.AsFloat(depthHazeStrength);
                    break;
                case "depth_haze_distance":
                    depthHazeDistance = setting.Value.AsFloat(depthHazeDistance);
                    break;
                case "godray_strength":
                    godrayStrength = setting.Value.AsFloat(godrayStrength);
                    break;
                case "scene_lift_strength":
                    sceneLiftStrength = setting.Value.AsFloat(sceneLiftStrength);
                    break;
                case "local_contrast_strength":
                    localContrastStrength = setting.Value.AsFloat(localContrastStrength);
                    break;
                case "color_richness":
                    colorRichness = setting.Value.AsFloat(colorRichness);
                    break;
                case "earth_tone_separation":
                    earthToneSeparation = setting.Value.AsFloat(earthToneSeparation);
                    break;
                case "season_grass_correction":
                    seasonGrassCorrection = setting.Value.AsFloat(seasonGrassCorrection);
                    break;
                case "seasonal_grass_influence":
                    seasonalGrassInfluence = setting.Value.AsFloat(seasonalGrassInfluence);
                    break;
                case "climate_plant_influence":
                    climatePlantInfluence = setting.Value.AsFloat(climatePlantInfluence);
                    break;
                case "frost_tint_influence":
                    frostTintInfluence = setting.Value.AsFloat(frostTintInfluence);
                    break;
                case "colormap_naturalize_strength":
                    colormapNaturalizeStrength = setting.Value.AsFloat(colormapNaturalizeStrength);
                    break;
                case "colormap_chroma_strength":
                    colormapChromaStrength = setting.Value.AsFloat(colormapChromaStrength);
                    break;
                case "colormap_yellow_green_guard":
                    colormapYellowGreenGuard = setting.Value.AsFloat(colormapYellowGreenGuard);
                    break;
                case "colormap_brightness":
                    colormapBrightness = setting.Value.AsFloat(colormapBrightness);
                    break;
                case "warm_light_stability":
                    warmLightStability = setting.Value.AsFloat(warmLightStability);
                    break;
                case "golden_hour_strength":
                    goldenHourStrength = setting.Value.AsFloat(goldenHourStrength);
                    break;
            }

            // Live uniforms handle in-game changes. Asset rewriting is only for unpacked dev installs.
            UpdateShader();
        }

        private void UpdateShader()
        {
            if (!canRewriteShaderAssets) return;

            UpdateColorMapShader();
            pendingShaderReload = true;
        }

        private void UpdateColorMapShader()
        {
            if (!File.Exists(colorMapShaderPath))
            {
                capi?.Logger.Debug($"[VibrantShaders] Color map shader include not found: {colorMapShaderPath}");
                return;
            }

            try
            {
                string shaderCode = File.ReadAllText(colorMapShaderPath);
                shaderCode = UpdateConstIntValue(shaderCode, "VIBRANT_SEASONAL_GRASS_MAP_INDEX", seasonalGrassMapIndex);
                shaderCode = UpdateConstIntValue(shaderCode, "VIBRANT_CLIMATE_PLANT_MAP_INDEX", climatePlantMapIndex);
                shaderCode = UpdateConstValue(shaderCode, "VIBRANT_SEASONAL_GRASS_INFLUENCE", Effective(seasonalGrassInfluence, 1f));
                shaderCode = UpdateConstValue(shaderCode, "VIBRANT_CLIMATE_PLANT_INFLUENCE", Effective(climatePlantInfluence, 1f));
                shaderCode = UpdateConstValue(shaderCode, "VIBRANT_FROST_TINT_INFLUENCE", Effective(frostTintInfluence, 1f));
                File.WriteAllText(colorMapShaderPath, shaderCode);

                if (File.Exists(colorMapFragmentShaderPath))
                {
                    shaderCode = File.ReadAllText(colorMapFragmentShaderPath);
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_NATURALIZE_STRENGTH", Effective(colormapNaturalizeStrength, 0f));
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_CHROMA_STRENGTH", Effective(colormapChromaStrength, 1f));
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_YELLOW_GREEN_GUARD", Effective(colormapYellowGreenGuard, 0f));
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_BRIGHTNESS", Effective(colormapBrightness, 1f));
                    File.WriteAllText(colorMapFragmentShaderPath, shaderCode);
                }
            }
            catch (Exception ex)
            {
                capi?.Logger.Error($"[VibrantShaders] Failed to update color map shader include: {ex.Message}");
            }
        }

        private void ResolveSeasonalColorMapIndices()
        {
            seasonalGrassMapIndex = 14;
            climatePlantMapIndex = 0;
            if (capi?.World?.Blocks == null) return;

            bool foundSeasonalGrass = false;
            bool foundClimatePlant = false;

            foreach (Block block in capi.World.Blocks)
            {
                if (!foundSeasonalGrass && block?.SeasonColorMapResolved?.Code == "seasonalGrass")
                {
                    seasonalGrassMapIndex = block.SeasonColorMapResolved.RectIndex;
                    capi.Logger.Notification($"[VibrantShaders] seasonalGrass colormap index: {seasonalGrassMapIndex}");
                    foundSeasonalGrass = true;
                }

                if (!foundClimatePlant && block?.ClimateColorMapResolved?.Code == "climatePlantTint")
                {
                    climatePlantMapIndex = block.ClimateColorMapResolved.RectIndex;
                    capi.Logger.Notification($"[VibrantShaders] climatePlantTint colormap index: {climatePlantMapIndex}");
                    foundClimatePlant = true;
                }

                if (foundSeasonalGrass && foundClimatePlant)
                {
                    return;
                }
            }

            if (!foundSeasonalGrass)
            {
                capi.Logger.Warning("[VibrantShaders] Could not resolve seasonalGrass colormap index; using vanilla fallback index 14");
            }
            if (!foundClimatePlant)
            {
                capi.Logger.Warning("[VibrantShaders] Could not resolve climatePlantTint colormap index; using vanilla fallback index 0");
            }
        }

        private string UpdateConstValue(string code, string name, float value)
        {
            // Match: const/uniform float NAME = 0.123;
            // Using invariant culture for consistent decimal format
            string pattern = $@"((?:const|uniform)\s+float\s+{name}\s*=\s*)-?[0-9.]+(\s*;)";
            string replacement = $"${{1}}{value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}${{2}}";
            return Regex.Replace(code, pattern, replacement);
        }

        private float Effective(float configuredValue, float disabledValue)
        {
            return enableVibrantShaders ? configuredValue : disabledValue;
        }

        private void ApplyFinalShaderUniforms(IShaderProgram shader, int depthTextureId)
        {
            SetFloatIfPresent(shader, "VIBRANCE_STRENGTH", Effective(vibranceStrength, 0f));
            SetFloatIfPresent(shader, "WARM_SHADOWS", Effective(warmShadows, 0f));
            SetFloatIfPresent(shader, "COOL_HIGHLIGHTS", Effective(coolHighlights, 0f));
            SetFloatIfPresent(shader, "VIGNETTE_STRENGTH", Effective(vignetteStrength, 0f));
            SetFloatIfPresent(shader, "VIGNETTE_SOFTNESS", vignetteSoftness);
            SetFloatIfPresent(shader, "FILM_GRAIN", Effective(filmGrain, 0f));
            SetFloatIfPresent(shader, "MOONLIGHT_STRENGTH", Effective(moonlightStrength, 0f));
            SetFloatIfPresent(shader, "MOONLIGHT_BLUE_TINT", Effective(moonlightBlueTint, 0f));
            SetFloatIfPresent(shader, "MOON_PHASE_BRIGHTNESS", capi?.World?.Calendar?.MoonPhaseBrightness ?? 1f);
            SetFloatIfPresent(shader, "BLUE_BOOST", Effective(blueBoost, 0f));
            SetFloatIfPresent(shader, "GREEN_BOOST", Effective(greenBoost, 0f));
            SetFloatIfPresent(shader, "WARM_BOOST", Effective(warmBoost, 0f));
            SetFloatIfPresent(shader, "SHADOW_BLUENESS", Effective(shadowBlueness, 0f));
            SetFloatIfPresent(shader, "TONEMAP_STRENGTH", Effective(tonemapStrength, 0f));
            SetFloatIfPresent(shader, "BLOOM_STRENGTH", Effective(bloomStrength, 0f));
            SetFloatIfPresent(shader, "BLOOM_SOFT_KNEE", bloomSoftKnee);
            SetFloatIfPresent(shader, "DEPTH_HAZE_STRENGTH", Effective(depthHazeStrength, 0f));
            SetFloatIfPresent(shader, "DEPTH_HAZE_DISTANCE", depthHazeDistance);
            SetFloatIfPresent(shader, "GODRAY_STRENGTH", Effective(godrayStrength, 1f));
            SetFloatIfPresent(shader, "SCENE_LIFT_STRENGTH", Effective(sceneLiftStrength, 0f));
            SetFloatIfPresent(shader, "LOCAL_CONTRAST_STRENGTH", Effective(localContrastStrength, 0f));
            SetFloatIfPresent(shader, "COLOR_RICHNESS", Effective(colorRichness, 0f));
            SetFloatIfPresent(shader, "EARTH_TONE_SEPARATION", Effective(earthToneSeparation, 0f));
            SetFloatIfPresent(shader, "SEASON_GRASS_CORRECTION", Effective(seasonGrassCorrection, 0f));
            SetFloatIfPresent(shader, "WARM_LIGHT_STABILITY", Effective(warmLightStability, 0f));
            SetFloatIfPresent(shader, "GOLDEN_HOUR_STRENGTH", Effective(goldenHourStrength, 0f));

            if (shader.HasUniform("depthHazeEnabled"))
            {
                shader.Uniform("depthHazeEnabled", depthTextureId > 0 && Effective(depthHazeStrength, 0f) > 0f ? 1 : 0);
            }
        }

        private void ApplyColorMapShaderUniforms(IShaderProgram shader)
        {
            SetFloatIfPresent(shader, "VIBRANT_SEASONAL_GRASS_INFLUENCE", Effective(seasonalGrassInfluence, 1f));
            SetFloatIfPresent(shader, "VIBRANT_CLIMATE_PLANT_INFLUENCE", Effective(climatePlantInfluence, 1f));
            SetFloatIfPresent(shader, "VIBRANT_FROST_TINT_INFLUENCE", Effective(frostTintInfluence, 1f));
            SetFloatIfPresent(shader, "VIBRANT_COLORMAP_NATURALIZE_STRENGTH", Effective(colormapNaturalizeStrength, 0f));
            SetFloatIfPresent(shader, "VIBRANT_COLORMAP_CHROMA_STRENGTH", Effective(colormapChromaStrength, 1f));
            SetFloatIfPresent(shader, "VIBRANT_COLORMAP_YELLOW_GREEN_GUARD", Effective(colormapYellowGreenGuard, 0f));
            SetFloatIfPresent(shader, "VIBRANT_COLORMAP_BRIGHTNESS", Effective(colormapBrightness, 1f));
        }

        private void SetFloatIfPresent(IShaderProgram shader, string name, float value)
        {
            if (shader.HasUniform(name))
            {
                shader.Uniform(name, value);
            }
        }

        private string UpdateConstIntValue(string code, string name, int value)
        {
            string pattern = $@"(const\s+int\s+{name}\s*=\s*)-?[0-9]+(\s*;)";
            string replacement = $"${{1}}{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}${{2}}";
            return Regex.Replace(code, pattern, replacement);
        }

        public override void Dispose()
        {
            if (configLib != null)
            {
                configLib.SettingChanged -= OnSettingChanged;
            }
            if (updateListenerId.HasValue && capi != null)
            {
                capi.Event.UnregisterGameTickListener(updateListenerId.Value);
            }
            if (finalShaderBindings != null && capi != null)
            {
                capi.Event.UnregisterRenderer(finalShaderBindings, EnumRenderStage.AfterPostProcessing);
                finalShaderBindings.Dispose();
            }
            base.Dispose();
        }

        private sealed class FinalShaderBindings : IRenderer
        {
            private static readonly EnumShaderProgram[] ColorMapShaderPrograms =
            {
                EnumShaderProgram.Chunkopaque,
                EnumShaderProgram.Chunktopsoil,
                EnumShaderProgram.Chunktransparent,
                EnumShaderProgram.Chunkliquid,
                EnumShaderProgram.Helditem,
                EnumShaderProgram.Gui,
                EnumShaderProgram.Guitopsoil,
                EnumShaderProgram.Standard,
                EnumShaderProgram.Entityanimated,
                EnumShaderProgram.Entityanimated_Oit
            };

            private readonly VibrantShadersModSystem modSystem;
            private readonly ICoreClientAPI capi;

            public double RenderOrder => 1.0;
            public int RenderRange => 1;

            public FinalShaderBindings(VibrantShadersModSystem modSystem, ICoreClientAPI capi)
            {
                this.modSystem = modSystem;
                this.capi = capi;
            }

            public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
            {
                if (stage != EnumRenderStage.AfterPostProcessing) return;

                IShaderProgram finalShader = capi.Render.GetEngineShader(EnumShaderProgram.Final);
                if (finalShader == null || finalShader.Disposed || finalShader.LoadError) return;
                if (capi.Render.CurrentActiveShader != null && !ReferenceEquals(capi.Render.CurrentActiveShader, finalShader)) return;

                int depthTextureId = 0;
                if (capi.Render.FrameBuffers.Count > (int)EnumFrameBuffer.Primary)
                {
                    depthTextureId = capi.Render.FrameBuffers[(int)EnumFrameBuffer.Primary].DepthTextureId;
                }

                bool wasActive = ReferenceEquals(capi.Render.CurrentActiveShader, finalShader);
                if (!wasActive)
                {
                    finalShader.Use();
                }

                if (finalShader.HasUniform("depthHazeEnabled"))
                {
                    finalShader.Uniform("depthHazeEnabled", depthTextureId > 0 ? 1 : 0);
                }
                if (depthTextureId > 0 && finalShader.HasUniform("depthTex"))
                {
                    finalShader.BindTexture2D("depthTex", depthTextureId, 6);
                }
                if (finalShader.HasUniform("zNear"))
                {
                    finalShader.Uniform("zNear", capi.Render.ShaderUniforms.ZNear);
                }
                if (finalShader.HasUniform("zFar"))
                {
                    finalShader.Uniform("zFar", capi.Render.ShaderUniforms.ZFar);
                }
                modSystem.ApplyFinalShaderUniforms(finalShader, depthTextureId);

                if (!wasActive)
                {
                    finalShader.Stop();
                }

                if (capi.Render.CurrentActiveShader == null)
                {
                    ApplyColorMapUniformsToEngineShaders();
                }
            }

            private void ApplyColorMapUniformsToEngineShaders()
            {
                foreach (EnumShaderProgram program in ColorMapShaderPrograms)
                {
                    IShaderProgram shader = capi.Render.GetEngineShader(program);
                    if (shader == null || shader.Disposed || shader.LoadError) continue;

                    shader.Use();
                    modSystem.ApplyColorMapShaderUniforms(shader);
                    shader.Stop();
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
