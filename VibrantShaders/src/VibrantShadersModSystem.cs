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
        private string shaderPath = "";
        private string colorMapShaderPath = "";
        private string colorMapFragmentShaderPath = "";
        private bool pendingShaderReload = false;
        private long? updateListenerId;
        private FinalShaderBindings? finalShaderBindings;

        // Current settings
        private float vibranceStrength = 0.91133004f;
        private float warmShadows = 0.035960592f;
        private float coolHighlights = 0.018719211f;
        private float vignetteStrength = 0.20197044f;
        private float vignetteSoftness = 0.44532022f;
        private float filmGrain = 0.006650246f;

        // Moonlight settings
        private float moonlightStrength = 0.17955671f;
        private float moonlightBlueTint = 0.0f;

        // Color enhancement settings
        private float blueBoost = 0.13546798f;
        private float greenBoost = 0.018719226f;
        private float warmBoost = 0.13231528f;
        private float shadowBlueness = 0.072413795f;

        // Shaderpack-style finishing settings
        private float tonemapStrength = 0.35f;
        private float bloomStrength = 0.34f;
        private float bloomSoftKnee = 0.55f;
        private float depthHazeStrength = 0.16f;
        private float depthHazeDistance = 0.55f;
        private float godrayStrength = 0.86f;
        private float sceneLiftStrength = 0.18f;
        private float localContrastStrength = 0.24f;
        private float colorRichness = 0.22f;
        private float earthToneSeparation = 0.20f;
        private float seasonGrassCorrection = 0.0f;
        private float seasonalGrassInfluence = 0.45f;
        private float climatePlantInfluence = 0.35f;
        private float frostTintInfluence = 0.0f;
        private float colormapNaturalizeStrength = 0.35f;
        private float colormapChromaStrength = 0.85f;
        private float colormapYellowGreenGuard = 0.35f;
        private float colormapBrightness = 1.05f;
        private float goldenHourStrength = 0.08f;
        private int seasonalGrassMapIndex = 14;
        private int climatePlantMapIndex = 0;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Client;

        public override void StartClientSide(ICoreClientAPI api)
        {
            capi = api;

            // Find our shader file path using Mod.SourcePath
            shaderPath = Path.Combine(Mod.SourcePath, "assets", "game", "shaders", "final.fsh");
            colorMapShaderPath = Path.Combine(Mod.SourcePath, "assets", "game", "shaderincludes", "colormap.vsh");
            colorMapFragmentShaderPath = Path.Combine(Mod.SourcePath, "assets", "game", "shaderincludes", "colormap.fsh");

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

            // Initial shader update
            UpdateShader();

            finalShaderBindings = new FinalShaderBindings(api);
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

            // Update moon phase brightness in shader (every tick is fine, shader update only when changed)
            UpdateMoonPhaseBrightness();
        }

        private float lastMoonBrightness = -1f;

        private void UpdateMoonPhaseBrightness()
        {
            if (capi?.World?.Calendar == null) return;

            float moonBrightness = capi.World.Calendar.MoonPhaseBrightness;

            // Only update shader if brightness changed significantly
            if (Math.Abs(moonBrightness - lastMoonBrightness) > 0.01f)
            {
                lastMoonBrightness = moonBrightness;

                try
                {
                    if (File.Exists(shaderPath))
                    {
                        string shaderCode = File.ReadAllText(shaderPath);
                        shaderCode = UpdateConstValue(shaderCode, "MOON_PHASE_BRIGHTNESS", moonBrightness);
                        File.WriteAllText(shaderPath, shaderCode);

                        // Schedule reload for next tick
                        pendingShaderReload = true;
                    }
                }
                catch (Exception ex)
                {
                    capi.Logger.Debug($"[VibrantShaders] Moon phase update failed: {ex.Message}");
                }
            }
        }

        private void LoadSettings()
        {
            var config = configLib?.GetConfig("vibrantshaders");
            if (config == null) return;

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

        private void OnSettingChanged(string domain, IConfig config, ISetting setting)
        {
            if (domain != "vibrantshaders") return;

            capi?.Logger.Debug($"[VibrantShaders] Setting changed: {setting.YamlCode}");

            // Update the relevant setting
            switch (setting.YamlCode)
            {
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
                case "golden_hour_strength":
                    goldenHourStrength = setting.Value.AsFloat(goldenHourStrength);
                    break;
            }

            // Update shader and schedule deferred reload
            UpdateShader();
            pendingShaderReload = true;
        }

        private void UpdateShader()
        {
            UpdateFinalShader();
            UpdateColorMapShader();
        }

        private void UpdateFinalShader()
        {
            if (!File.Exists(shaderPath))
            {
                capi?.Logger.Error($"[VibrantShaders] Shader file not found: {shaderPath}");
                return;
            }

            try
            {
                string shaderCode = File.ReadAllText(shaderPath);

                // Update const values using regex
                shaderCode = UpdateConstValue(shaderCode, "VIBRANCE_STRENGTH", vibranceStrength);
                shaderCode = UpdateConstValue(shaderCode, "WARM_SHADOWS", warmShadows);
                shaderCode = UpdateConstValue(shaderCode, "COOL_HIGHLIGHTS", coolHighlights);
                shaderCode = UpdateConstValue(shaderCode, "VIGNETTE_STRENGTH", vignetteStrength);
                shaderCode = UpdateConstValue(shaderCode, "VIGNETTE_SOFTNESS", vignetteSoftness);
                shaderCode = UpdateConstValue(shaderCode, "FILM_GRAIN", filmGrain);
                shaderCode = UpdateConstValue(shaderCode, "MOONLIGHT_STRENGTH", moonlightStrength);
                shaderCode = UpdateConstValue(shaderCode, "MOONLIGHT_BLUE_TINT", moonlightBlueTint);
                shaderCode = UpdateConstValue(shaderCode, "BLUE_BOOST", blueBoost);
                shaderCode = UpdateConstValue(shaderCode, "GREEN_BOOST", greenBoost);
                shaderCode = UpdateConstValue(shaderCode, "WARM_BOOST", warmBoost);
                shaderCode = UpdateConstValue(shaderCode, "SHADOW_BLUENESS", shadowBlueness);
                shaderCode = UpdateConstValue(shaderCode, "TONEMAP_STRENGTH", tonemapStrength);
                shaderCode = UpdateConstValue(shaderCode, "BLOOM_STRENGTH", bloomStrength);
                shaderCode = UpdateConstValue(shaderCode, "BLOOM_SOFT_KNEE", bloomSoftKnee);
                shaderCode = UpdateConstValue(shaderCode, "DEPTH_HAZE_STRENGTH", depthHazeStrength);
                shaderCode = UpdateConstValue(shaderCode, "DEPTH_HAZE_DISTANCE", depthHazeDistance);
                shaderCode = UpdateConstValue(shaderCode, "GODRAY_STRENGTH", godrayStrength);
                shaderCode = UpdateConstValue(shaderCode, "SCENE_LIFT_STRENGTH", sceneLiftStrength);
                shaderCode = UpdateConstValue(shaderCode, "LOCAL_CONTRAST_STRENGTH", localContrastStrength);
                shaderCode = UpdateConstValue(shaderCode, "COLOR_RICHNESS", colorRichness);
                shaderCode = UpdateConstValue(shaderCode, "EARTH_TONE_SEPARATION", earthToneSeparation);
                shaderCode = UpdateConstValue(shaderCode, "SEASON_GRASS_CORRECTION", seasonGrassCorrection);
                shaderCode = UpdateConstValue(shaderCode, "GOLDEN_HOUR_STRENGTH", goldenHourStrength);

                File.WriteAllText(shaderPath, shaderCode);

                capi?.Logger.Debug("[VibrantShaders] Shader updated");
            }
            catch (Exception ex)
            {
                capi?.Logger.Error($"[VibrantShaders] Failed to update shader: {ex.Message}");
            }
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
                shaderCode = UpdateConstValue(shaderCode, "VIBRANT_SEASONAL_GRASS_INFLUENCE", seasonalGrassInfluence);
                shaderCode = UpdateConstValue(shaderCode, "VIBRANT_CLIMATE_PLANT_INFLUENCE", climatePlantInfluence);
                shaderCode = UpdateConstValue(shaderCode, "VIBRANT_FROST_TINT_INFLUENCE", frostTintInfluence);
                File.WriteAllText(colorMapShaderPath, shaderCode);

                if (File.Exists(colorMapFragmentShaderPath))
                {
                    shaderCode = File.ReadAllText(colorMapFragmentShaderPath);
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_NATURALIZE_STRENGTH", colormapNaturalizeStrength);
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_CHROMA_STRENGTH", colormapChromaStrength);
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_YELLOW_GREEN_GUARD", colormapYellowGreenGuard);
                    shaderCode = UpdateConstValue(shaderCode, "VIBRANT_COLORMAP_BRIGHTNESS", colormapBrightness);
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
            // Match: const float NAME = 0.123;
            // Using invariant culture for consistent decimal format
            string pattern = $@"(const\s+float\s+{name}\s*=\s*)-?[0-9.]+(\s*;)";
            string replacement = $"${{1}}{value.ToString("0.########", System.Globalization.CultureInfo.InvariantCulture)}${{2}}";
            return Regex.Replace(code, pattern, replacement);
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
            private readonly ICoreClientAPI capi;

            public double RenderOrder => 1.0;
            public int RenderRange => 1;

            public FinalShaderBindings(ICoreClientAPI capi)
            {
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

                if (!wasActive)
                {
                    finalShader.Stop();
                }
            }

            public void Dispose()
            {
            }
        }
    }
}
