in vec2 climateColorMapUv;
in vec2 seasonColorMapUv;
in float climateWeight;
in float frostAlpha;
in float seasonWeight;
in float heretemp;

uniform float VIBRANT_COLORMAP_NATURALIZE_STRENGTH = 0.670;
uniform float VIBRANT_COLORMAP_CHROMA_STRENGTH = 1.256;
uniform float VIBRANT_COLORMAP_YELLOW_GREEN_GUARD = 0.833;
uniform float VIBRANT_COLORMAP_BRIGHTNESS = 0.500;

float vibrantColormapLuma(vec3 color) {
	return dot(color, vec3(0.2126, 0.7152, 0.0722));
}

vec3 vibrantPreserveColormapLuma(vec3 color, float targetLuma) {
	float currentLuma = max(vibrantColormapLuma(color), 0.0001);
	return color * (targetLuma / currentLuma);
}

vec4 vibrantNaturalizeColormapTint(vec4 tint) {
	float originalLuma = vibrantColormapLuma(tint.rgb);
	vec3 neutral = vec3(originalLuma);
	vec3 chromaAdjusted = neutral + (tint.rgb - neutral) * max(VIBRANT_COLORMAP_CHROMA_STRENGTH, 0.0);

	float yellowGreenBias = clamp(
		(chromaAdjusted.g - chromaAdjusted.b) * 1.4 +
		(chromaAdjusted.r - chromaAdjusted.b) * 0.7 -
		0.15,
		0.0,
		1.0
	);

	vec3 guarded = chromaAdjusted;
	guarded.r -= guarded.r * 0.18 * yellowGreenBias * VIBRANT_COLORMAP_YELLOW_GREEN_GUARD;
	guarded.g -= guarded.g * 0.08 * yellowGreenBias * VIBRANT_COLORMAP_YELLOW_GREEN_GUARD;
	guarded.b += (originalLuma - guarded.b) * 0.35 * yellowGreenBias * VIBRANT_COLORMAP_YELLOW_GREEN_GUARD;
	guarded = vibrantPreserveColormapLuma(max(guarded, vec3(0.0)), originalLuma);

	vec3 naturalized = mix(tint.rgb, guarded, clamp(VIBRANT_COLORMAP_NATURALIZE_STRENGTH, 0.0, 1.0));
	naturalized *= max(VIBRANT_COLORMAP_BRIGHTNESS, 0.0);
	return vec4(max(naturalized, vec3(0.0)), tint.a);
}

vec4 getColorMapped(sampler2D sourceTex, vec4 color) {
	vec4 tint = vec4(1);
	bool mapped = false;
	
	if (climateColorMapUv.x >= 0) {
		vec4 climateColor = vibrantNaturalizeColormapTint(texture(sourceTex, climateColorMapUv));
		tint = mix(vec4(1), climateColor, clamp(climateWeight, 0.0, 1.0));
		mapped = climateWeight > 0.0001;
	}
	
	if (seasonColorMapUv.x >= 0 && seasonWeight > 0) {
		vec4 seasonColor = vibrantNaturalizeColormapTint(texture(sourceTex, seasonColorMapUv));
		tint = mix(tint, seasonColor, seasonWeight);
		mapped=true;
	}
	
	if (frostAlpha > 0) {
		float w = clamp((0.333 - heretemp) * 15, 0, 1);
		
		if (mapped) {
			tint.rgb = mix(tint.rgb, tint.rgb * (1 - frostAlpha) + vec3(1) * frostAlpha, w);
		} else {
			float b = (color.r + color.g + color.b) / 3.0;
			
			vec3 frostColor = vec3(b + frostAlpha*0.2);		
			float faw = frostAlpha * w;
			color.rgb = color.rgb * (1 - faw) + frostColor * faw;
			return color;
		}
	}
	
	return color * tint;
}

vec4 getFrosted(vec4 color) {
	if (heretemp < 0.333 && frostAlpha > 0) {
		float w = clamp((0.333 - heretemp) * 15, 0, 1);
		
		float b = (color.r + color.g + color.b) / 3.0;
		
		vec3 frostColor = vec3(b + frostAlpha*0.2);		
		float faw = frostAlpha * w;
		color.rgb = color.rgb * (1 - faw) + frostColor * faw;
	}
	return color;
}
