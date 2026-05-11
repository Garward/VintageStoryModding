#version 330 core

// Vibrant Shaders - Enhanced post-processing for Vintage Story
// Only adds NEW effects not available in vanilla settings

uniform sampler2D primaryScene;
uniform sampler2D glowParts;
uniform sampler2D bloomParts;
uniform sampler2D godrayParts;
uniform sampler2D ssaoScene;
uniform sampler2D depthTex;

uniform float gammaLevel;
uniform float brightnessLevel;
uniform float contrastLevel;
uniform float sepiaLevel;
uniform float ambientBloomLevel;
uniform float damageVignetting;
uniform float damageVignettingSide;
uniform float frostVignetting;
uniform float extraGamma = 1.0;
uniform float windWaveCounter;
uniform float glitchEffectStrength;

uniform float minlight = 0.0;
uniform float maxlight = 1;
uniform float minsat = 0;
uniform float maxsat = 1;
uniform float zNear = 0.3;
uniform float zFar = 1500.0;
uniform int depthHazeEnabled = 0;

in vec2 invFrameSize;
in vec2 texCoord;
flat in float godrayIntensity;
flat in float nightAmount;       // From vertex shader: 0 = day, 1 = full night
flat in float sunHeight;         // Sun Y position for color temperature

layout(location = 0) out vec4 outColor;

#include fxaa.fsh
#include colorutil.ash
#include noise3d.ash

// ============================================
// VIBRANT SHADERS - NEW EFFECTS ONLY
// ============================================
const float VIBRANCE_STRENGTH = 0.91133004; // Boost muted colors (0.0-1.0)
const float WARM_SHADOWS = 0.035960592;    // Warm tint in dark areas
const float COOL_HIGHLIGHTS = 0.018719211; // Cool tint in bright areas
const float VIGNETTE_STRENGTH = 0.20197044; // Subtle edge darkening (0.0-0.5)
const float VIGNETTE_SOFTNESS = 0.44532022; // How soft the vignette is
const float FILM_GRAIN = 0.006650246;      // Film grain amount (0 to disable)

// MOONLIGHT SETTINGS
const float MOONLIGHT_STRENGTH = 0.17955671; // Base moonlight brightness (0.0-1.0)
const float MOONLIGHT_BLUE_TINT = 0.0;     // Blue tint amount (0.0-1.0)
const float MOON_PHASE_BRIGHTNESS = 1.0;   // Updated by C# based on moon phase (0.0-1.0)

// COLOR ENHANCEMENT SETTINGS
const float BLUE_BOOST = 0.13546798;       // Boost blues/cyans (snow, sky, water)
const float GREEN_BOOST = 0.018719226;     // Boost greens (foliage)
const float WARM_BOOST = 0.13231528;       // Boost oranges/yellows (autumn, warm light)
const float SHADOW_BLUENESS = 0.072413795; // Push shadows toward blue (outdoor realism)

// SHADERPACK-STYLE FINISHING SETTINGS
const float TONEMAP_STRENGTH = 0.35;       // ACES highlight rolloff amount
const float BLOOM_STRENGTH = 0.34;         // Bloom intensity after soft-knee shaping
const float BLOOM_SOFT_KNEE = 0.55;        // How gently bloom enters highlights
const float DEPTH_HAZE_STRENGTH = 0.16;    // Distance haze amount
const float DEPTH_HAZE_DISTANCE = 0.55;    // Lower = closer haze, higher = farther haze
const float GODRAY_STRENGTH = 0.86;        // Tames vanilla godray contribution before final grading
const float SCENE_LIFT_STRENGTH = 0.18;    // Raises dead shadows/midtones without washing highlights
const float LOCAL_CONTRAST_STRENGTH = 0.24; // Screen-space midtone detail contrast
const float COLOR_RICHNESS = 0.22;         // Filmic saturation for midtones
const float EARTH_TONE_SEPARATION = 0.20;  // Separates flat yellow/brown terrain into warmer/cooler hues
const float SEASON_GRASS_CORRECTION = 0.0; // Deprecated broad final-pass correction, kept off by default
const float GOLDEN_HOUR_STRENGTH = 0.08;   // Warm dusk/dawn color separation

// ============================================
// NEW EFFECT FUNCTIONS
// ============================================

float luminance(vec3 color) {
    return dot(color, vec3(0.2126, 0.7152, 0.0722));
}

vec3 srgbToLinear(vec3 color) {
    color = max(color, vec3(0.0));
    bvec3 isLow = lessThanEqual(color, vec3(0.04045));
    vec3 loPart = color / 12.92;
    vec3 hiPart = pow((color + 0.055) / 1.055, vec3(2.4));
    return mix(hiPart, loPart, isLow);
}

vec3 linearToSrgb(vec3 color) {
    color = clamp(color, 0.0, 1.0);
    bvec3 isLow = lessThanEqual(color, vec3(0.0031308));
    vec3 loPart = color * 12.92;
    vec3 hiPart = pow(color, vec3(1.0 / 2.4)) * 1.055 - 0.055;
    return mix(hiPart, loPart, isLow);
}

vec3 matchLuminance(vec3 color, float targetLum) {
    float currentLum = max(luminance(color), 0.001);
    return color * (targetLum / currentLum);
}

float hueBand(float hue, float center, float width, float feather) {
    float dist = abs(fract(hue - center + 0.5) - 0.5);
    return 1.0 - smoothstep(width, width + feather, dist);
}

// Convert RGB to HSL
vec3 rgb2hslLocal(vec3 c) {
    float maxC = max(max(c.r, c.g), c.b);
    float minC = min(min(c.r, c.g), c.b);
    float l = (maxC + minC) / 2.0;

    if (maxC == minC) {
        return vec3(0.0, 0.0, l);
    }

    float d = maxC - minC;
    float s = l > 0.5 ? d / (2.0 - maxC - minC) : d / (maxC + minC);
    float h;

    if (maxC == c.r) {
        h = (c.g - c.b) / d + (c.g < c.b ? 6.0 : 0.0);
    } else if (maxC == c.g) {
        h = (c.b - c.r) / d + 2.0;
    } else {
        h = (c.r - c.g) / d + 4.0;
    }
    h /= 6.0;

    return vec3(h, s, l);
}

// Selective color enhancement based on hue
vec3 applySelectiveColor(vec3 color) {
    vec3 hsl = rgb2hslLocal(clamp(color, 0.0, 1.0));
    float hue = hsl.x;        // 0-1 where: 0=red, 0.33=green, 0.5=cyan, 0.66=blue, 0.83=magenta
    float sat = hsl.y;
    float lum = hsl.z;

    // Skip only very dark or very bright pixels
    if (lum < 0.03 || lum > 0.97) return color;

    // Snow/ice enhancement - bright, low saturation pixels get a restrained cool tint
    if (lum > 0.5 && sat < 0.2) {
        float snowAmount = smoothstep(0.5, 0.8, lum) * (1.0 - smoothstep(0.05, 0.2, sat));
        vec3 snowTint = vec3(lum * 0.94, lum * 0.99, min(lum * 1.08, 1.0));
        color = mix(color, snowTint, BLUE_BOOST * 0.35 * snowAmount);
        hsl = rgb2hslLocal(clamp(color, 0.0, 1.0));
        hue = hsl.x;
        sat = hsl.y;
        lum = hsl.z;
    }

    float chromaMask = smoothstep(0.03, 0.35, sat) * (1.0 - smoothstep(0.55, 0.96, lum));
    float lowSatMask = (1.0 - smoothstep(0.04, 0.18, sat)) * smoothstep(0.03, 0.12, sat);

    // Hue bands: blue/cyan for sky/water/ice, green for foliage, warm for reds/yellows.
    float blueAmount = hueBand(hue, 0.58, 0.17, 0.06);
    float greenAmount = hueBand(hue, 0.33, 0.14, 0.05);
    float warmAmount = max(hueBand(hue, 0.08, 0.12, 0.05), hueBand(hue, 0.97, 0.05, 0.04));

    float boost = blueAmount * BLUE_BOOST + greenAmount * GREEN_BOOST + warmAmount * WARM_BOOST;
    hsl.y = clamp(hsl.y * (1.0 + boost * (0.65 * chromaMask + 0.35 * lowSatMask)), 0.0, 1.0);

    if (EARTH_TONE_SEPARATION > 0.0) {
        float earthMask = hueBand(hue, 0.17, 0.115, 0.055) *
            smoothstep(0.06, 0.32, sat) *
            smoothstep(0.06, 0.22, lum) *
            (1.0 - smoothstep(0.72, 0.92, lum));
        float oliveSide = smoothstep(0.145, 0.235, hue);
        float targetHue = mix(0.105, 0.285, oliveSide);
        hsl.x = mix(hsl.x, targetHue, EARTH_TONE_SEPARATION * 0.32 * earthMask);
        hsl.y = clamp(hsl.y * (1.0 + EARTH_TONE_SEPARATION * 0.75 * earthMask), 0.0, 1.0);
    }

    return hsl2rgb(hsl);
}

vec3 applySeasonGrassCorrection(vec3 color) {
    if (SEASON_GRASS_CORRECTION <= 0.0) return color;

    vec3 hsl = rgb2hslLocal(clamp(color, 0.0, 1.0));
    float hue = hsl.x;
    float sat = hsl.y;
    float lum = hsl.z;

    if (lum < 0.05 || lum > 0.92 || sat < 0.08) return color;

    float depthMask = 1.0;
    if (depthHazeEnabled != 0) {
        float depthSample = texture(depthTex, texCoord).r;
        depthMask = 1.0 - smoothstep(0.995, 0.99999, depthSample);
    }

    float yellowGrass = hueBand(hue, 0.155, 0.082, 0.045);
    float oliveGrass = hueBand(hue, 0.235, 0.090, 0.050);
    float terrainMask = max(yellowGrass, oliveGrass) *
        smoothstep(0.10, 0.42, sat) *
        smoothstep(0.10, 0.28, lum) *
        (1.0 - smoothstep(0.74, 0.94, lum)) *
        depthMask;

    float correction = clamp(SEASON_GRASS_CORRECTION * terrainMask, 0.0, 1.0);
    if (correction <= 0.0) return color;

    float targetHue = mix(0.255, 0.305, smoothstep(0.18, 0.30, hue));
    hsl.x = mix(hsl.x, targetHue, correction * 0.72);

    float neonMask = smoothstep(0.42, 0.78, sat) * smoothstep(0.38, 0.72, lum);
    hsl.y = mix(hsl.y, hsl.y * mix(0.86, 0.58, neonMask), correction);
    hsl.z = mix(hsl.z, min(hsl.z, 0.68 + lum * 0.12), correction * neonMask * 0.45);

    return hsl2rgb(hsl);
}

// Add blue tint to shadows (outdoor realism)
vec3 applyShadowBlue(vec3 color) {
    if (SHADOW_BLUENESS <= 0.0) return color;

    vec3 linear = srgbToLinear(color);
    float lum = luminance(linear);

    // Affect shadow and mid-shadow range
    float shadowAmount = 1.0 - smoothstep(0.08, 0.45, lum);

    // Push shadows toward sky ambient color while preserving perceived brightness
    vec3 skyTint = vec3(0.88, 0.96, 1.16);
    vec3 blueShadow = mix(linear, linear * skyTint, SHADOW_BLUENESS);
    blueShadow = matchLuminance(blueShadow, lum);

    return linearToSrgb(mix(linear, blueShadow, shadowAmount));
}

// Apply moonlight ambient during night
vec3 applyMoonlight(vec3 color, float nightAmt) {
    if (nightAmt <= 0.0 || MOONLIGHT_STRENGTH <= 0.0) return color;

    vec3 linear = srgbToLinear(color);
    float lum = luminance(linear);

    // How much this pixel should receive moonlight (dark areas get more)
    // Bright areas (torches, lamps) shouldn't be affected
    float darkAmount = 1.0 - smoothstep(0.01, 0.28, lum);

    // Moonlight base color - silver-blue
    vec3 moonTint = srgbToLinear(mix(vec3(0.8), vec3(0.6, 0.75, 1.0), MOONLIGHT_BLUE_TINT));

    // Calculate the moonlight floor - minimum brightness for dark areas
    float moonFloor = srgbToLinear(vec3(MOONLIGHT_STRENGTH * MOON_PHASE_BRIGHTNESS * nightAmt * 0.15)).r;
    vec3 moonlitFloor = moonTint * moonFloor;

    // For dark pixels, lift them toward the moonlit floor
    // Brighter pixels stay as they are
    vec3 moonlitColor = max(linear, moonlitFloor * darkAmount);

    // Blend based on how dark the original was
    linear = mix(linear, moonlitColor, darkAmount * nightAmt);

    // Desaturate dark areas slightly (scotopic vision simulation)
    float desat = nightAmt * 0.2 * darkAmount;
    vec3 nightVision = matchLuminance(moonTint, luminance(linear)) * 0.9;
    linear = mix(linear, nightVision, desat);

    return linearToSrgb(linear);
}

// KWin/NVIDIA-style vibrance in linear RGB.
// amount is a boost over neutral: 0.30 = 130%, 0.50 = 150%.
vec3 applyVibrance(vec3 color, float amount) {
    vec3 linear = srgbToLinear(color);
    float lum = luminance(linear);

    float maxCol = max(max(linear.r, linear.g), linear.b);
    float minCol = min(min(linear.r, linear.g), linear.b);
    float currentSat = (maxCol - minCol) / max(maxCol, 0.001);

    float adjustedAmount = 1.0 + amount * (1.0 - currentSat * 0.5);
    vec3 saturated = vec3(lum) + adjustedAmount * (linear - vec3(lum));

    return linearToSrgb(saturated);
}

// Color temperature: warm shadows, cool highlights
vec3 applyColorTemperature(vec3 color) {
    vec3 linear = srgbToLinear(color);
    float lum = luminance(linear);

    float shadowAmount = 1.0 - smoothstep(0.08, 0.5, lum);
    float highlightAmount = smoothstep(0.45, 0.9, lum);

    vec3 warmTint = vec3(1.0 + WARM_SHADOWS * 2.0, 1.0 + WARM_SHADOWS * 0.7, 1.0 - WARM_SHADOWS);
    vec3 coolTint = vec3(1.0 - COOL_HIGHLIGHTS * 0.6, 1.0, 1.0 + COOL_HIGHLIGHTS * 2.0);

    vec3 adjusted = mix(linear, linear * warmTint, shadowAmount);
    adjusted = mix(adjusted, adjusted * coolTint, highlightAmount);
    adjusted = matchLuminance(adjusted, lum);

    return linearToSrgb(adjusted);
}

// Film grain
float grain(vec2 uv, float time) {
    return fract(sin(dot(uv, vec2(12.9898, 78.233)) + time) * 43758.5453);
}

float linearEyeDepth(float depthSample) {
    float z = depthSample * 2.0 - 1.0;
    return (2.0 * zNear * zFar) / max(zFar + zNear - z * (zFar - zNear), 0.0001);
}

vec3 acesFitted(vec3 color) {
    color = max(color, vec3(0.0));
    return clamp((color * (2.51 * color + 0.03)) / (color * (2.43 * color + 0.59) + 0.14), 0.0, 1.0);
}

vec3 applyTonemap(vec3 color) {
    if (TONEMAP_STRENGTH <= 0.0) return color;

    vec3 linear = srgbToLinear(color);
    vec3 mapped = acesFitted(linear * 1.12);
    return mix(color, linearToSrgb(mapped), TONEMAP_STRENGTH);
}

vec3 composeBloom(vec3 color, vec3 bloom, float glowLevel, float ambientLevel) {
    if (BLOOM_STRENGTH <= 0.0) return color;

    vec3 sceneLinear = srgbToLinear(color);
    vec3 bloomLinear = srgbToLinear(bloom);
    float bloomLum = luminance(bloomLinear);
    float knee = smoothstep(0.02, max(0.03, BLOOM_SOFT_KNEE), bloomLum);
    float glowBoost = 0.65 + clamp(glowLevel, 0.0, 1.0) * 1.2;
    float ambientBoost = 0.35 + ambientLevel * 0.65;
    vec3 bloomAdd = bloomLinear * (BLOOM_STRENGTH * glowBoost * ambientBoost * (0.35 + 0.65 * knee));

    // Exponential screen blend keeps bright bloom soft without washing the whole scene gray.
    vec3 composed = 1.0 - (1.0 - clamp(sceneLinear, 0.0, 1.0)) * exp(-bloomAdd);
    return linearToSrgb(composed);
}

vec3 applySceneLiftAndRichness(vec3 color) {
    vec3 linear = srgbToLinear(color);
    float lum = luminance(linear);

    float shadowMask = 1.0 - smoothstep(0.035, 0.34, lum);
    float midMask = smoothstep(0.06, 0.26, lum) * (1.0 - smoothstep(0.62, 0.92, lum));
    float dayAmount = 1.0 - nightAmount * 0.55;

    if (SCENE_LIFT_STRENGTH > 0.0) {
        vec3 liftTint = mix(vec3(1.02, 0.93, 0.78), vec3(0.72, 0.80, 1.0), nightAmount);
        linear += liftTint * (SCENE_LIFT_STRENGTH * 0.075 * shadowMask);
        linear += vec3(SCENE_LIFT_STRENGTH * 0.025 * midMask * dayAmount);
    }

    if (COLOR_RICHNESS > 0.0) {
        float richMask = (0.35 + 0.65 * midMask) * dayAmount;
        linear = mix(vec3(lum), linear, 1.0 + COLOR_RICHNESS * richMask);
    }

    if (GOLDEN_HOUR_STRENGTH > 0.0) {
        float horizon = 1.0 - smoothstep(0.025, 0.31, abs(sunHeight - 0.075));
        float highlightMask = smoothstep(0.16, 0.68, lum);
        vec3 warm = linear * vec3(1.08, 1.015, 0.90);
        vec3 coolShadow = linear * vec3(0.88, 0.96, 1.10);
        linear = mix(linear, warm, GOLDEN_HOUR_STRENGTH * horizon * highlightMask);
        linear = mix(linear, matchLuminance(coolShadow, lum), GOLDEN_HOUR_STRENGTH * horizon * shadowMask * 0.55);
    }

    return linearToSrgb(linear);
}

vec3 applyLocalContrast(vec3 color) {
    if (LOCAL_CONTRAST_STRENGTH <= 0.0) return color;

    vec2 px = invFrameSize * 1.75;
    vec3 linear = srgbToLinear(color);
    float lum = luminance(linear);
    float neighborLum = 0.0;
    neighborLum += luminance(srgbToLinear(texture(primaryScene, texCoord + vec2(px.x, 0.0)).rgb));
    neighborLum += luminance(srgbToLinear(texture(primaryScene, texCoord - vec2(px.x, 0.0)).rgb));
    neighborLum += luminance(srgbToLinear(texture(primaryScene, texCoord + vec2(0.0, px.y)).rgb));
    neighborLum += luminance(srgbToLinear(texture(primaryScene, texCoord - vec2(0.0, px.y)).rgb));
    neighborLum *= 0.25;

    float midMask = smoothstep(0.04, 0.24, lum) * (1.0 - smoothstep(0.72, 1.0, lum));
    float edge = clamp(lum - neighborLum, -0.16, 0.16);
    linear += vec3(edge * LOCAL_CONTRAST_STRENGTH * midMask);

    float contrast = 1.0 + LOCAL_CONTRAST_STRENGTH * 0.32 * midMask;
    linear = (linear - vec3(0.18)) * contrast + vec3(0.18);

    return linearToSrgb(linear);
}

vec3 applyDepthHaze(vec3 color) {
    if (DEPTH_HAZE_STRENGTH <= 0.0 || depthHazeEnabled == 0) return color;

    float depthSample = texture(depthTex, texCoord).r;
    if (depthSample <= 0.0001 || depthSample >= 0.99999) return color;

    float viewDepth = linearEyeDepth(depthSample);
    float hazeRange = max(1.0, zFar * mix(0.18, 0.85, clamp(DEPTH_HAZE_DISTANCE, 0.0, 1.0)));
    float hazeAmount = pow(clamp(viewDepth / hazeRange, 0.0, 1.0), 1.35) * DEPTH_HAZE_STRENGTH;

    float duskAmount = 1.0 - smoothstep(0.02, 0.25, abs(sunHeight - 0.08));
    vec3 dayHaze = mix(vec3(0.64, 0.73, 0.86), vec3(0.94, 0.67, 0.48), duskAmount);
    vec3 nightHaze = vec3(0.08, 0.10, 0.14);
    vec3 hazeColor = mix(dayHaze, nightHaze, nightAmount);

    float lum = luminance(color);
    hazeColor = matchLuminance(srgbToLinear(hazeColor), max(srgbToLinear(vec3(lum)).r, 0.015));
    return linearToSrgb(mix(srgbToLinear(color), hazeColor, hazeAmount));
}

// ============================================
// VANILLA COLOR GRADING (UNCHANGED)
// ============================================

float SmoothStep(float x) { return x * x * (3.0f - 2.0f * x); }

vec4 ColorGrade(vec4 color) {
    color.a = dot(color.rgb, vec3(0.299, 0.587, 0.114));

    vec3 hsl = rgb2hsl(color.rgb);

    float lightRange = maxlight - minlight;
    float satRange = maxsat - minsat;

    hsl.z = pow((clamp(hsl.z, minlight, maxlight) - minlight) / lightRange, 1/gammaLevel);
    hsl.y = pow((clamp(hsl.y, minsat, maxsat) - minsat) / satRange, 1);

    color.rgb = hsl2rgb(hsl);
    color.rgb = pow(color.rgb, vec3(1.0 / extraGamma));
    color.rgb *= brightnessLevel;

    // Sepia
    vec3 sepia = vec3(
        (color.r * 0.393) + (color.g * 0.769) + (color.b * 0.189),
        (color.r * 0.349) + (color.g * 0.686) + (color.b * 0.168),
        (color.r * 0.272) + (color.g * 0.534) + (color.b * 0.131)
    ) * 0.85;

    color.rgb = mix(color.rgb, sepia, sepiaLevel);
    color.rgb = color.rgb * (contrastLevel+1) - contrastLevel;

    // Glitch effect (temporal storms)
    if (glitchEffectStrength > 0) {
        float g = gnoise(vec3(texCoord.x * 2000.0, texCoord.y * 2000.0, mod(windWaveCounter*30, 100)));
        color.rgb *= mix(1, clamp(0.7 + g / 2, 0.7, 1), glitchEffectStrength);

        vec3 rust = vec3(
            (color.r * 0.393) + (color.g * 0.769) + (color.b * 0.189),
            (color.r * 0.349) + (color.g * 0.686) + (color.b * 0.168),
            (color.r * 0.272) + (color.g * 0.534) + (color.b * 0.131)
        );

        float gdiff = min(color.g, 0.1);
        float bdiff = min(color.b, 0.1);
        rust.g -= gdiff;
        rust.b -= bdiff;
        rust.r += gdiff + bdiff;

        color.rgb = mix(color.rgb, rust, glitchEffectStrength);
        color.a += glitchEffectStrength/3;
    }

    return color;
}


void main(void)
{
    // FXAA (vanilla)
    #if FXAA == 1
        vec4 color = fxaaTexturePixel(primaryScene, texCoord, invFrameSize);
    #else
        vec4 color = texture(primaryScene, texCoord);
    #endif

    color.a=1;
    float bloomSub = 0;

    // Bloom (vanilla)
    #if BLOOM == 1
        vec4 bloomCol = texture(bloomParts, texCoord);
        float glowLevel = texture(glowParts, texCoord).r;

        float ambLevel = ambientBloomLevel / 2.0;
        color.rgb = composeBloom(color.rgb, bloomCol.rgb, glowLevel, ambLevel);

        bloomSub = glowLevel * (bloomCol.r + bloomCol.b + bloomCol.g);
    #endif

    // SSAO (vanilla)
    #if SSAOLEVEL > 0
        #if SSAOLEVEL > 1
            float ssao = min(texture(ssaoScene, texCoord).r, texture(ssaoScene, texCoord - vec2(0, invFrameSize.y*1)).r);
        #else
            float ssao = texture(ssaoScene, texCoord).r;
        #endif

        color.rgb *= min(1, ssao + bloomSub);
    #endif

    // God rays (vanilla)
    #if GODRAYS > 0
        vec4 grc = texture(godrayParts, texCoord);
        color.rgb += grc.rgb * GODRAY_STRENGTH;
        color.rgb = min(color.rgb, vec3(1));
        color.a=1;
    #endif

    // Vanilla color grading
    vec4 gradedColor = ColorGrade(color);
    outColor = mix(color, gradedColor, gradedColor.a);

    // ============================================
    // NEW EFFECTS (not in vanilla)
    // ============================================

    // Moonlight ambient - illuminate dark areas at night based on moon phase
    outColor.rgb = applyMoonlight(outColor.rgb, nightAmount);

    // Vibrance - boost muted colors
    outColor.rgb = applyVibrance(outColor.rgb, VIBRANCE_STRENGTH);

    // Selective color enhancement - boost specific hue ranges
    outColor.rgb = applySelectiveColor(outColor.rgb);

    // Correct vanilla seasonal grass tint when it collapses into neon yellow/olive.
    outColor.rgb = applySeasonGrassCorrection(outColor.rgb);

    // Lift dead shadows/midtones and restore richer color separation
    outColor.rgb = applySceneLiftAndRichness(outColor.rgb);

    // Blue shadows for outdoor realism
    outColor.rgb = applyShadowBlue(outColor.rgb);

    // Color temperature - warm shadows, cool highlights
    outColor.rgb = applyColorTemperature(outColor.rgb);

    // Depth-aware atmospheric haze
    outColor.rgb = applyDepthHaze(outColor.rgb);

    // Screen-space local contrast after haze so flat terrain keeps shape
    outColor.rgb = applyLocalContrast(outColor.rgb);

    // ACES-style highlight rolloff
    outColor.rgb = applyTonemap(outColor.rgb);

    // Artistic vignette - darkens edges/corners for cinematic focus
    vec2 position = (gl_FragCoord.xy * invFrameSize.xy) - vec2(0.5);
    vec2 artisticPosition = texCoord - vec2(0.5);
    artisticPosition.x *= invFrameSize.y / invFrameSize.x;
    float artisticDist = length(artisticPosition) * 2.0;  // 0 at center, aspect-aware at edges
    float vignetteAmount = smoothstep(VIGNETTE_SOFTNESS, VIGNETTE_SOFTNESS + 0.8, artisticDist);
    float vignetteDarken = 1.0 - (vignetteAmount * VIGNETTE_STRENGTH * 2.0);  // Strength now actually controls darkness
    outColor.rgb *= max(vignetteDarken, 0.3);  // Don't go darker than 30%

    // Film grain
    if (FILM_GRAIN > 0.0) {
        float grainValue = grain(texCoord, windWaveCounter) * 2.0 - 1.0;
        float grainLum = luminance(outColor.rgb);
        float grainMask = smoothstep(0.03, 0.18, grainLum) * (1.0 - smoothstep(0.65, 1.0, grainLum));
        outColor.rgb += grainValue * FILM_GRAIN * grainMask;
    }

    // ============================================
    // VANILLA VIGNETTE EFFECTS (unchanged)
    // ============================================

    float dist = length(position) * 2.0;  // 0 at center, ~1.4 at corners
    float grayvignette = 1 - smoothstep(1.1, 0.75 - 0.45, dist);

    // Frost vignetting
    if (frostVignetting > 0) {
        float str = -0.05 + 1.05*clamp(1 - smoothstep(1.1 - frostVignetting / 4, 0.75 - 0.45, dist), 0, 1) - grayvignette;
        float g = 0;

        float wx = gnoise(vec3(gl_FragCoord.x / 20.0, str, gl_FragCoord.x / 11.0 + gl_FragCoord.y / 10.0));
        float wy = gnoise(vec3(gl_FragCoord.x / 20.0, str, gl_FragCoord.x / 10.0 - gl_FragCoord.y / 9.0));

        g = 2*gnoise(vec3(wx / 3.0, wy / 3.0, 0.2)) + 0.8;
        g *= gnoise(vec3(gl_FragCoord.x / 20.0, gl_FragCoord.y / 20.0, 1.5)) + 0.2;
        g -= gnoise(vec3(wx * 2.0, wy * 2.0, 1))/5;
        g -= str*2;
        g *= frostVignetting;

        float v = 0.9 + gnoise(vec3(wx, -wy, 0)) / 15.0;
        vec3 vignetteColor = vec3(v, v, 0.95);

        outColor.rgb = mix(outColor.rgb, vignetteColor, max(0.0, str - g) + 0.5*str);
    }

    // Damage vignetting
    if (damageVignetting > 0) {
        float str = clamp(1 - smoothstep(1.1 - damageVignetting / 4, 0.75 - 0.45, dist), 0, 1) - grayvignette;
        float g = 0;

        g = gnoise(vec3(gl_FragCoord.x / 20.0, gl_FragCoord.y / 20.0, 0)) + 0.5;
        g += gnoise(vec3(gl_FragCoord.x / 5.0, gl_FragCoord.y / 5.0, 0))/5;
        g -= str*2;

        g*=damageVignetting;

        vec3 vignetteColor = vec3(0.8 * damageVignetting/2, 0, 0);

        float centerness = pow(1 - abs(damageVignettingSide), 3);
        float side = clamp(centerness + pow(mix(texCoord.x, 1 - texCoord.x, (1 + damageVignettingSide) / 2), 1.5), 0, 1);

        outColor.rgb = mix(outColor.rgb, vignetteColor, max(0.0, str - g) * side);
    }

    outColor.a=1;
}
