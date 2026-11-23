// FairyToon Endfield mode: stylised shading for cloth, metal, skin, face, hair and eyes, after the published
// breakdowns of Arknights: Endfield's character shading (LRyuki and qiudashu233 on Zhihu, Ronald Burns; see
// Documentation/EndfieldReferences.md in LiminalNW). Our own implementation: the structure and constants follow those
// write-ups, the code does not come from any of them.
// Included by FairyToonCore.hlsl; used by the Forward pass when _FT_ENDFIELD is on.
#ifndef FAIRYTOON_ENDFIELD_INCLUDED
#define FAIRYTOON_ENDFIELD_INCLUDED

// (the character globals _FT_EFLighting / _FT_EFTopColor / _FT_EFTopFill are declared in FairyToonInput.hlsl, shared
// with the see-through passes)

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

struct FairyToonEndfieldSurface
{
    half3 brdfDiffuse;          // albedo with the metal's diffuse removed
    half3 f0;
    half  metallic;
    half  perceptualRoughness;
    half  roughness;            // GGX alpha
    half  ao;
};

float3 FairyToonEFNormalize(float3 v) { return v * rsqrt(max(dot(v, v), 1e-8)); }

// 1D value noise in 0..1 (hashed lattice values, smoothstep between them), for the hair strand lines.
float FairyToonEFHash1D(float x) { return frac(sin(x * 127.1) * 43758.5453); }
float FairyToonEFNoise1D(float x)
{
    float i = floor(x), f = frac(x);
    return lerp(FairyToonEFHash1D(i), FairyToonEFHash1D(i + 1.0), f * f * (3.0 - 2.0 * f));
}
float2 FairyToonEFNormalize(float2 v) { return v * rsqrt(max(dot(v, v), 1e-8)); }

// Keeps the luminance, pushes the colour away from grey (the sources' "DesatBoost").
half3 FairyToonEFSaturate(half3 c)
{
    half luma = Luminance(c);
    return max(lerp(luma.xxx, c, 1.0 + _EFShadowSaturation), 0.0);
}

// Top fill light (FairyToonCharacter), straight down: saturate(N.y + offset) plus a constant fill that reaches every
// side (the sources' o2), at the sun or shade strength. In the shade it replaces the sun, so without the fill a face or
// a sideways ear would be left to the ambient light alone. Zero without the component (character.y = 0, day = 1).
half3 FairyToonEFTopLight(float3 N, float4 character)
{
    return (saturate(N.y + character.z) + _FT_EFTopFill.x) * lerp(_FT_EFTopColor.a, character.y, character.x) * _FT_EFTopColor.rgb;
}

// Diffuse ramp at half-Lambert u: RGB hue tint, A lit weight. Without a ramp texture, a white tint and the lilToon
// border / blur step.
half4 FairyToonEFRamp(float u)
{
    half4 step = half4(1.0, 1.0, 1.0, saturate(FairyToonStep(_AAStrength, u, _ShadowBorder, _ShadowBlur)));
    half4 ramp = SAMPLE_TEXTURE2D_LOD(_DiffuseRamp, sampler_ft_linear_clamp, float2(u, 0.5), 0);
    return _EFRampOn > 0.5 ? ramp : step;
}

// Split-sum environment BRDF as (scale on F0, bias), Karis' analytic fit (UE4 mobile). The breakdown uses a different
// fit of the same curve; any close fit works here.
half2 FairyToonEFEnvBRDF(half perceptualRoughness, half NoV)
{
    const half4 c0 = half4(-1.0, -0.0275, -0.572, 0.022);
    const half4 c1 = half4(1.0, 0.0425, 1.04, -0.04);
    half4 r = perceptualRoughness * c0 + c1;
    half a004 = min(r.x * r.x, exp2(-9.28 * NoV)) * r.x + r.y;
    return half2(-1.04, 1.04) * a004 + r.zw;
}

// Probe light softened towards its average, so normal-map detail does not shade the dark side. FairyToonCharacter can
// blend in its own directional ambient: a sky colour from above and a ground colour from below, by N.y (the community
// Endfield recreation's "brighter and bluer above, darker below"), so a character keeps its shape in any scene probe.
half3 FairyToonEFAmbient(float3 N, FairyToonSurface s)
{
    half3 average = (FairyToonSH(float3(0, 1, 0), s) + FairyToonSH(float3(0, -1, 0), s)) * 0.5;
    half3 probe = lerp(average, FairyToonSH(N, s), 0.5);
    half3 gradient = lerp(_FT_EFAmbientGround.rgb, _FT_EFAmbientSky.rgb, saturate(N.y * 0.5 + 0.5));
    half3 ambient = lerp(probe, gradient, saturate(_FT_EFAmbientSky.w));
    // Ambient cap (FairyToonCharacter): Endfield adds the ambient to a sun normalised to 1 without a clamp, so a strongly
    // coloured probe (a blue sky) lifts the whole character towards white. Saturation scales its chroma around its
    // luminance, Max Luminance scales it down (hue kept). Off without the component or at the defaults.
    if (_FT_EFAmbientCap.w > 0.5)
    {
        half luma = Luminance(ambient);
        if (_FT_EFAmbientCap.y != 1.0) ambient = max(luma + (ambient - luma) * max(_FT_EFAmbientCap.y, 0.0), 0.0);
        if (_FT_EFAmbientCap.x > 0.0) ambient *= min(1.0, _FT_EFAmbientCap.x / max(luma, 0.0001));
    }
    return ambient;
}

// Exposure of the main light (FairyToonCharacter): scales the normalised main light without touching DayStrength, so a
// character can darken (night, a dim interior) and keep her sun-side shading. Follow Light blends towards the light's
// own luminance over a reference, so a dim moon gives a dim key light. 1 without the component.
half FairyToonEFExposure(half rawLuminance)
{
    if (_FT_EFExposure.w < 0.5) return 1.0;
    half follow = saturate(rawLuminance / max(_FT_EFExposure.z, 0.0001));
    return max(_FT_EFExposure.x, 0.0) * lerp(1.0, follow, saturate(_FT_EFExposure.y));
}

// Soft ceiling on the summed main light (ambient + top + direct), off at 0: identity up to half the ceiling, then an
// exponential shoulder towards it, so the sum cannot run far past white. Hue kept.
half3 FairyToonEFLightCeiling(half3 sum)
{
    half ceiling = _FT_EFAmbientCap.z;
    if (_FT_EFAmbientCap.w < 0.5 || ceiling <= 0.0) return sum;
    half luma = Luminance(sum);
    half knee = 0.5 * ceiling;
    if (luma <= knee) return sum;
    half mapped = knee + (ceiling - knee) * (1.0 - exp(-(luma - knee) / (ceiling - knee)));
    return sum * (mapped / luma);
}

// Main light direction, optionally swapped for a light fixed to the camera (FairyToonCharacter Light Override, after the
// recreation's per-character main-light override), so a face keeps a chosen light whatever the sun does. The override
// light is built from the camera's horizontal right and back axes and world up.
float3 FairyToonEFLightDirection(float3 L)
{
    float3 result = L;
    UNITY_BRANCH
    if (_FT_EFLightOverride.w > 0.0)
    {
        float2 backXZ = FairyToonEFNormalize(UNITY_MATRIX_V[2].xz);
        float2 rightXZ = float2(backXZ.y, -backXZ.x);
        float3 overrideL = FairyToonEFNormalize(float3(rightXZ.x * _FT_EFLightOverride.x + backXZ.x * _FT_EFLightOverride.z,
            _FT_EFLightOverride.y, rightXZ.y * _FT_EFLightOverride.x + backXZ.y * _FT_EFLightOverride.z));
        result = FairyToonEFNormalize(lerp(L, overrideL, saturate(_FT_EFLightOverride.w)));
    }
    return result;
}

// Small character lights (FairyToonCharacterLight, up to four, globals for the character in view), named by role as in
// the recreation: Fill adds a soft wrapped light, Tint colours the shade side, Highlight adds a toon specular dot, Rim
// lights the silhouette on the light's side, Fog pulls the colour towards the light colour. Each fades out at its range.
half3 FairyToonEFSmallLights(half3 col, FairyToonSurface s, float3 N, half3 diffuseLit, half sunLit, half ao, half eye)
{
    int count = min((int)_FT_EFParams.y, FT_EF_SMALL_LIGHTS);
    float NoV = saturate(dot(N, s.V));
    UNITY_LOOP
    for (int i = 0; i < count; i++)
    {
        float4 position = _FT_EFSmallLightPosition[i];
        float4 color = _FT_EFSmallLightColor[i];
        float role = _FT_EFSmallLightRole[i].x;
        float3 toLight = position.xyz - s.positionWS;
        float d2 = dot(toLight, toLight);
        half atten = saturate(1.0 - d2 / max(position.w * position.w, 1e-4));
        atten *= atten;
        if (atten <= 0.0) continue;
        float3 Ld = toLight * rsqrt(max(d2, 1e-8));
        half NoLd = dot(N, Ld);
        half3 light = color.rgb * color.a * atten;
        if (role < 0.5)
        {
            // (fill: half-Lambert squared, a soft wrap with no toon edge; eyes take it flat)
            half wrap = lerp(saturate(NoLd * 0.5 + 0.5), 1.0, eye);
            col += diffuseLit * light * ao * wrap * wrap;
        }
        else if (role < 1.5)
        {
            col *= lerp(1.0, color.rgb, saturate(color.a * atten) * (1.0 - sunLit));
        }
        else if (role < 2.5)
        {
            half spec = smoothstep(0.94, 0.97, saturate(dot(N, FairyToonEFNormalize(Ld + s.V)))) * (1.0 - eye);
            col += light * spec * ao;
        }
        else if (role < 3.5)
        {
            half rim = smoothstep(0.55, 0.8, 1.0 - NoV) * saturate(NoLd + 0.25) * (1.0 - eye);
            col += light * rim * lerp(0.5, diffuseLit, 0.5) * ao;
        }
        else
        {
            col = lerp(col, color.rgb, saturate(color.a * atten));
        }
    }
    return col;
}

FairyToonEndfieldSurface FairyToonEFGetSurface(FairyToonSurface s)
{
    FairyToonEndfieldSurface e;
    half4 pbr = _EFPBRMapOn > 0.5 ? SAMPLE_TEXTURE2D(_PBRMap, sampler_MainTex, s.uvMain) : half4(1, 1, 1, 1);
    e.metallic = _EFPBRMapOn > 0.5 ? pbr.r : _EFMetallic;
    half smoothness = pbr.a * _EFSmoothness;
    // (wetness does not raise this: a 0.96 material smoothness turned the fabric's own spec / IBL into a mirror of the
    // bright horizon, a grey wash over wet stockings, and fought GSAA; the water's own lobes on tracks and drops carry it)
    // (geometric specular AA: rough up where the normal changes quickly; DLAA cannot fix sub-pixel highlights)
    float3 dN = max(abs(ddx(s.N)), abs(ddy(s.N)));
    float variance = dot(dN, dN);
    smoothness = min(smoothness, 1.0 - saturate(variance / (variance * 5.0 + 0.002) * max(_GSAAStrength, 0.15)));
    e.perceptualRoughness = 1.0 - smoothness;
    e.roughness = max(e.perceptualRoughness * e.perceptualRoughness, 0.0078125);
    half occlusion = pbr.b;
    #if !defined(_FT_EF_FACE) && !defined(_FT_EF_EYE)
        if (_EFPBRMapOn <= 0.5)
            occlusion = SAMPLE_TEXTURE2D(_ShadowBorderMask, sampler_MainTex, s.uvMain).r;
    #endif
    e.ao = lerp(1.0, occlusion, _EFOcclusion);
    e.brdfDiffuse = s.albedo * (1.0 - 0.96 * e.metallic);
    e.f0 = lerp(0.04 * pbr.g * _EFReflectivity, s.albedo, e.metallic);
    return e;
}

// Multi-bounce colored AO (Jimenez et al. SIGGRAPH 2016) so deep creases retain albedo saturation instead of muddying to grey.
half3 FairyToonEFMultiBounceAO(half ao, half3 albedo)
{
    half3 a =  2.0404h * albedo - 0.3324h;
    half3 b = -4.7951h * albedo + 0.6417h;
    half3 c =  2.7552h * albedo + 0.6903h;
    return max(ao.xxx, ((ao * a + b) * ao + c) * ao);
}

// Screen-space curvature micro-shading (convex crest lift, concave fold carve) without extra textures.
half FairyToonEFCurvature(float3 N, float3 positionWS)
{
    float3 dNx = ddx(N);
    float3 dNy = ddy(N);
    float3 dPx = ddx(positionWS);
    float3 dPy = ddy(positionWS);
    float kx = dot(dNx, dPx) / max(dot(dPx, dPx), 1e-6);
    float ky = dot(dNy, dPy) / max(dot(dPy, dPy), 1e-6);
    float viewDepth = -TransformWorldToView(positionWS).z;
    float fade = 1.0 - smoothstep(2.5, 9.0, viewDepth);
    float kRaw = (kx + ky) * 0.008 * _EFCurvatureScale;
    float k = (kRaw / (1.0 + abs(kRaw))) * fade;
    half delta = k >= 0.0 ? k * 0.14 * _EFCurvature : k * 0.20 * _EFCurvature;
    return clamp(1.0 + delta, 0.72, 1.18);
}

half3 FairyToonEFEnvReflection(float3 R, float3 positionWS, float2 screenUV, half perceptualRoughness, half3 ambient)
{
    half3 env = _ReflectionCubeOverride > 0.5
        ? FairyToonSampleReflectionCube(R, perceptualRoughness) * ambient
        : GlossyEnvironmentReflection(R, positionWS, perceptualRoughness, 1.0, screenUV);
    UNITY_BRANCH if (_EFEnvContrast > 0.0)
    {
        half envLuma = Luminance(env);
        half pivoted = pow(max(envLuma / 0.40, 1e-4), 1.0 + _EFEnvContrast * 0.38) * 0.40;
        half scale = clamp(pivoted / max(envLuma, 1e-4), 0.30, 1.65);
        env *= scale;
        // Gentle studio horizon accent only when the scene probe itself is unbaked / nearly black.
        half missingProbe = 1.0 - smoothstep(0.005, 0.08, envLuma);
        half sharpFactor = (1.0 - perceptualRoughness * 0.72) * (1.0 - perceptualRoughness * 0.52);
        half keyBand = pow(saturate(1.0 - abs(R.y - 0.32) * 2.8), 3.5) * sharpFactor;
        env += (ambient * 0.35 + half3(0.06, 0.07, 0.09)) * (keyBand * missingProbe * saturate(_EFEnvContrast * 0.60));
    }
    return env;
}

// Skin shadow colour from the LUT (Unity's 2D LUT layout, 32 slices of 32 x 32, sRGB in and out).
half3 FairyToonEFSkinLUT(half3 albedo)
{
    float3 uvw = saturate(LinearToSRGB(albedo));
    return ApplyLut2D(TEXTURE2D_ARGS(_SkinLUT, sampler_ft_linear_clamp), uvw, float3(1.0 / 1024.0, 1.0 / 32.0, 31.0));
}

// The head frame lives in FairyToonInput.hlsl so camera depth and iris vertices share it.

// Kajiya-Kay lobe sin(T, H)^n with the strand tilted along the normal by `shift`. The exponent drops with the pixel
// footprint of T.H, so thin bands do not sparkle, and the peak drops with it to keep the energy (after Ronald Burns'
// Endfield-style renderer). `t` returns T.H for the band edge.
float FairyToonEFHairLobe(float3 strand, float3 normal, float3 H, float shift, float exponent, out float t)
{
    t = dot(FairyToonEFNormalize(strand + normal * shift), H);
    float w = fwidth(t);
    float k = 1.0 / (1.0 + 0.25 * exponent * w * w);
    return pow(sqrt(saturate(1.0 - t * t)), exponent * k) * sqrt(k);
}

// Face: NoL from the face shadow map in lilToon's format (R / G thresholds for light from either side, B blend back to
// the mesh NoL, e.g. for the neck). The light is projected into the head's horizontal plane; Endfield's smoothstep
// window around the threshold gives a soft gradient and the ramp then makes the toon edge. _EFFaceSDFScale sets how
// fast the threshold follows the light: 0.5 for Endfield-style maps (shadow sweeps 0-180 degrees), 1 for lilToon maps
// (60-120 degrees).
float FairyToonEFFaceNoL(FairyToonSurface s, float3 L, float3 faceForward, float3 faceRight, float meshNoL, half backLift,
    out half meshBlend)
{
    half4 sdfMap = SAMPLE_TEXTURE2D(_ShadowStrengthMask, sampler_MainTex, s.uvMain);
    float2 lf = FairyToonEFNormalize(float2(dot(L, faceRight), dot(L, faceForward)));
    // (light behind the head and camera in front: pushed forward around the sides so the face does not go black)
    lf.y = clamp(lf.y + backLift * saturate(-lf.y) * (0.5 - 0.5 * lf.y * lf.y), -1.0, 1.0);
    half sdf = dot(L.xz, -faceRight.xz) < 0 ? sdfMap.g : sdfMap.r;
    float t = 0.5 - _EFFaceSDFScale * lf.y;
    float lo = max(0.0, 2.0 * t - 1.0);
    float faceLit = smoothstep(lo, max(min(1.0, 2.0 * t), lo + 0.0001), sdf);
    // (past either end of the map the whole face is lit or shaded, including texels at exactly 0 or 1)
    faceLit = t <= 0.0 ? 1.0 : t >= 1.0 ? 0.0 : faceLit;
    meshBlend = sdfMap.b;
    return lerp(faceLit * 2.0 - 1.0, meshNoL, meshBlend);
}

// Depth at a screen UV, as a distance along the view axis (metres): the characters-only depth (_FT_CharacterDepth)
// when the feature's character mask is on, else the camera depth. Against the characters-only depth everything that is
// not a character counts as far away, so the rim keeps the whole silhouette (shoe soles on the floor, a shoulder
// against a near wall) and a prop in front of the character does not cut a rim into it.
float FairyToonEFSceneDepth(float2 uv)
{
    // (a texel load, so the mask costs no sampler; it is the camera depth's size, i.e. the scaled screen)
    int2 texel = clamp(int2(uv * _ScaledScreenParams.xy), 0, int2(_ScaledScreenParams.xy) - 1);
    float raw = _FT_DepthRim.y > 0.5 ? LOAD_TEXTURE2D(_FT_CharacterDepth, texel).r : SampleSceneDepth(uv);
    if (unity_OrthoParams.w > 0.5)
    {
        #if UNITY_REVERSED_Z
            raw = 1.0 - raw;
        #endif
        return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
    }
    return LinearEyeDepth(raw, _ZBufferParams);
}

// Depth edge rim (the idea from Ronald Burns' recreation and the ZMD demo; our own code): step outward along the
// normal's direction on screen and read the camera depth there. A pixel is on the true outline only where what lies just
// outside it is clearly farther away (the background, or a part well behind it), so the inner creases between hair
// strands, braid lobes or cloth folds, where both sides sit at about the same depth, get no rim. Two taps (half and full
// width) give the band a soft inner edge. Returns 0..1.
half FairyToonEFDepthEdge(FairyToonSurface s, float3 N)
{
    // (the normal's direction on screen, built like ComputeScreenPos so it follows the depth texture's orientation)
    float4 clip0 = TransformWorldToHClip(s.positionWS);
    float4 clip1 = TransformWorldToHClip(s.positionWS + N * 0.01);
    float2 flip = float2(0.5, 0.5 * _ProjectionParams.x);
    float2 stepPx = (clip1.xy * flip / clip1.w - clip0.xy * flip / clip0.w) * _ScaledScreenParams.xy;
    float stepLength = length(stepPx);
    // (a normal facing the camera has no direction on screen, and no outline either)
    float3 normalVS = mul((float3x3)UNITY_MATRIX_V, N);
    half side = smoothstep(0.15, 0.5, length(normalVS.xy));
    half edge = 0.0;
    UNITY_BRANCH if (stepLength > 1e-6 && side > 0.0)
    {
        // (width in pixels at 1080p, so it stays even on screen like the outline)
        float2 stepUV = stepPx / stepLength * (_EFRimDepthWidth * _ScaledScreenParams.y / 1080.0) / _ScaledScreenParams.xy;
        float selfDepth = -TransformWorldToView(s.positionWS).z;
        float gapNear = FairyToonEFSceneDepth(s.screenUV + 0.5 * stepUV) - selfDepth;
        float gapFar = FairyToonEFSceneDepth(s.screenUV + stepUV) - selfDepth;
        float gapStart = 0.3 * _EFRimDepthGap;
        edge = side * 0.5 * (smoothstep(gapStart, _EFRimDepthGap, gapNear) + smoothstep(gapStart, _EFRimDepthGap, gapFar));
    }
    return edge;
}

// Character-only micro-contact AO over _FT_CharacterDepth using tangent-plane elevation so tilted flat surfaces stay artifact-free.
half FairyToonEFContactAO(FairyToonSurface s, float3 geomN)
{
    if (_FT_DepthRim.x < 0.5 || _FT_DepthRim.y < 0.5) return 1.0;
    float3 selfVS = TransformWorldToView(s.positionWS);
    float selfDepth = -selfVS.z;
    float distFade = 1.0 - smoothstep(4.0, 12.0, selfDepth);
    if (distFade <= 0.0 || selfDepth <= 0.02) return 1.0;

    float3 viewN = FairyToonEFNormalize(mul((float3x3)UNITY_MATRIX_V, geomN));
    float2 px = s.screenUV * _ScaledScreenParams.xy;
    float baseAngle = frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))) * 6.2831853;
    float radiusM = _EFContactAORadius;
    float maxRange = radiusM * 2.5;
    float2 flip = float2(0.5, 0.5 * _ProjectionParams.x);

    float occSum = 0.0;
    UNITY_UNROLL
    for (int i = 0; i < 6; i++)
    {
        float angle = baseAngle + i * 2.39996323;
        float fracR = sqrt((i + 0.5) / 6.0);
        float2 dXY = float2(cos(angle), sin(angle)) * (radiusM * fracR);
        float dzTangent = -(dXY.x * viewN.x + dXY.y * viewN.y) / max(viewN.z, 0.35);
        float3 sampleVS = selfVS + float3(dXY, clamp(dzTangent, -radiusM, radiusM));
        float4 clip = mul(UNITY_MATRIX_P, float4(sampleVS, 1.0));
        float2 tapUV = clip.xy / max(abs(clip.w), 1e-5) * flip + 0.5;
        if (tapUV.x >= 0.0 && tapUV.x <= 1.0 && tapUV.y >= 0.0 && tapUV.y <= 1.0)
        {
            float expectedDepth = -sampleVS.z;
            float tapDepth = FairyToonEFSceneDepth(tapUV);
            float dz = expectedDepth - tapDepth;
            float occ = smoothstep(_EFContactAOBias, _EFContactAOBias + radiusM * 0.55, dz)
                * (1.0 - smoothstep(radiusM * 1.2, maxRange, dz));
            occSum += occ;
        }
    }
    return 1.0 - saturate((occSum / 6.0) * 2.2) * _EFContactAO * distFade;
}

// Cloth / metal, skin (_FT_EF_SKIN), face (_FT_EF_FACE), hair (_FT_EF_HAIR) and eyes (_FT_EF_EYE). Fills `l` for the
// lilToon layers that run afterwards (Reflection, MatCap, Rim Light).
half3 FairyToonEndfieldShade(FairyToonSurface s, inout FairyToonLight l)
{
    FairyToonEndfieldSurface e = FairyToonEFGetSurface(s);
    float4 character = _FT_EFLighting.w > 0.5 ? _FT_EFLighting : float4(1.0, 0.0, 0.0, 0.0);
    half day = character.x;
    #if defined(_FT_EF_HAIR)
        half4 hairMask = SAMPLE_TEXTURE2D(_HairMask, sampler_MainTex, s.uvMain);
        e.ao *= lerp(1.0, hairMask.b, _EFOcclusion);
    #endif
    float3 headForward, headRight, headUp, headPosition;
    FairyToonEFHeadFrame(headForward, headRight, headUp, headPosition);
    #if defined(_FT_EF_HAIR)
        // Root occlusion: hair near its roots is darker than the tips, so the hair keeps its depth when there is no
        // highlight (shade, overcast). The roots are measured from a point Root Lift above the hair sphere centre: at the
        // crown, where the strands start, so the fringe and back hair brighten down their length (from the centre itself
        // the fringe, lying on the forehead at one distance, only shifts colour as a whole). A colour multiplier, not the
        // AO: plain darkening turns blonde olive; a warm multiplier turns it brown, as in the reference roots.
        half rootAO = (1.0 - smoothstep(_EFHairRootInner, _EFHairRootOuter,
            distance(s.positionWS, headPosition + headUp * (_EFHairCentre + _EFHairRootLift)))) * _EFHairRootAO;
    #endif
    // Eyes ignore the light direction and every shadow: the Eye part everywhere, the Face part inside its eye mask.
    half eye = 0.0;
    #if defined(_FT_EF_EYE)
        eye = 1.0;
    #elif defined(_FT_EF_FACE)
        eye = SAMPLE_TEXTURE2D(_EFEyeMask, sampler_MainTex, s.uvMain).r;
    #endif

    // ---- Light, shadow, occlusion
    float4 shadowCoord = TransformWorldToShadowCoord(s.positionWS);
    Light mainLight = GetMainLight(shadowCoord, s.positionWS, half4(1, 1, 1, 1));
    half3 lightColor = mainLight.color * mainLight.distanceAttenuation;
    #ifdef _LIGHT_LAYERS
        if (!IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer())) lightColor = 0;
    #endif
    // (the main light counts as a colour only, normalised to luminance 1 as in the captured Endfield shader; how strong
    // the sun is comes from DayStrength, so the character keeps one exposure under any sun intensity)
    half rawLightLuma = Luminance(lightColor);
    lightColor /= max(rawLightLuma, 0.0001);
    // (exposure scales every use of the direct light below: diffuse, specular, hair band, light rim, backlight; the
    // sun / shade weights still come from DayStrength alone)
    lightColor *= FairyToonEFExposure(rawLightLuma);
    float3 L = FairyToonEFLightDirection(mainLight.direction);
    // (scene shadow on the character, scaled out in the shade state; the character's own shadow comes from the
    // character shadow map below)
    half sceneLit = lerp(1.0, mainLight.shadowAttenuation, _ShadowReceive) * day;
    sceneLit = lerp(sceneLit, day, eye);

    half ssao = 1.0;
    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
        ssao = GetScreenSpaceAmbientOcclusion(s.screenUV).indirectAmbientOcclusion;
        ssao = lerp(1.0, ssao, SAMPLE_TEXTURE2D(_SSAOMask, sampler_MainTex, s.uvMain).r);
    #endif
    // Shared surface setup always faces Endfield N; ShadeBase faces geometric origN for Face.
    bool useBackfaceNormal = s.facing < 0.0;
    float3 N = s.N;
    #if defined(_FT_EF_FACE)
        // Keep the authored geometric face normal on fronts and its negative on backs.
        N = s.origN;
    #endif
    float3 toCamera = UNITY_MATRIX_V[2].xyz;
    float2 lightXZ = FairyToonEFNormalize(L.xz);
    float2 cameraXZ = FairyToonEFNormalize(toCamera.xz);
    float NoV = saturate(dot(N, s.V));

    // ---- Lit weight. The character's terminator offset moves the light-dark boundary as a whole (negative: more shade;
    // the recreation's cloth terminator offset, here on every part shaded by its normal; the face follows its SDF).
    // Backlight lift: with the light behind the character and the camera roughly level, NoL is pushed up around the
    // terminator so the side facing the camera does not go flat dark.
    float NoL = clamp(dot(N, L) + _FT_EFParams.x, -1.0, 1.0);
    half opposite = saturate(-dot(lightXZ, cameraXZ));
    half level = smoothstep(0.0, 1.0, saturate(2.0 * (0.75 - abs(toCamera.y))));
    NoL = clamp(NoL + opposite * level * _EFBacklight * (0.5 - 0.5 * NoL * NoL), -1.0, 1.0);
    #if defined(_FT_EF_FACE)
        half faceMeshBlend;
        float meshNoL = NoL;
        NoL = FairyToonEFFaceNoL(s, L, headForward, headRight, NoL, opposite * level * _EFBacklight, faceMeshBlend);
        // A flipped inside face follows its visible normal, including character-shadow reception.
        if (useBackfaceNormal)
        {
            NoL = meshNoL;
            faceMeshBlend = 1.0;
        }
    #endif
    half contactAO = 1.0;
    #if !defined(_FT_EF_EYE) && !defined(_SURFACE_TYPE_TRANSPARENT)
        UNITY_BRANCH if (_EFContactAO > 0.0)
        {
            contactAO = FairyToonEFContactAO(s, s.origN);
            #if defined(_FT_EF_FACE)
                contactAO = lerp(1.0, contactAO, faceMeshBlend);
            #endif
        }
    #endif
    half aoLit = e.ao * lerp(1.0, ssao, _SSAOLitStrength) * lerp(1.0, contactAO, 0.75);
    half aoAmbient = e.ao * lerp(1.0, ssao, _SSAOStrength) * contactAO;
    half3 aoAmbientColor = aoAmbient.xxx;
    half3 aoContactTint = contactAO.xxx;
    UNITY_BRANCH if (_EFMultiBounceAO > 0.0)
    {
        aoAmbientColor = lerp(aoAmbientColor, FairyToonEFMultiBounceAO(aoAmbient, s.albedo), _EFMultiBounceAO);
        aoContactTint = lerp(aoContactTint, FairyToonEFMultiBounceAO(contactAO, s.albedo), _EFMultiBounceAO);
    }
    // Character self-shadow (FairyToonFeature: a projector above the camera, as in Endfield, or the sun). It darkens both
    // the sun term and the view light, so the chin shadow on the neck and the hair's shadow on itself show in the sun and
    // in the shade. The face's SDF area ignores it (it would fight the SDF); the SDF's mesh blend (B: neck, ears) takes
    // it. Eyes are reset to their own values below.
    half selfShadow = lerp(1.0, FairyToonCharShadowFaded(s.positionWS, N), _CharShadowReceive);
    #if defined(_FT_EF_FACE)
        selfShadow = lerp(1.0, selfShadow, faceMeshBlend);
    #endif
    ftCharShadowVisibility = selfShadow;
    half4 ramp = FairyToonEFRamp(NoL * 0.5 + 0.5);
    half backface = s.facing < 0 ? 1.0 - _BackfaceForceShadow : 1.0;
    half lit = min(min(selfShadow, aoLit), ramp.a) * backface;
    // (eyes: only a gentle dimming as the light moves behind the face, so they stay clear when backlit)
    half eyeLit = 1.0 - _EFEyeLight * (0.5 - 0.5 * dot(lightXZ, FairyToonEFNormalize(headForward.xz)));
    if (s.facing < 0) eyeLit *= backface;
    lit = lerp(lit, eyeLit, eye);

    // View light: a light at the camera, raised a little. Its ramp weight darkens edges and creases from any angle.
    float3 viewLightDir = FairyToonEFNormalize(float3(toCamera.x, min(1.0, toCamera.y + 0.25), toCamera.z));
    half viewLit = selfShadow * aoLit * FairyToonEFRamp(dot(N, viewLightDir) * 0.5 + 0.5).a;
    viewLit = lerp(viewLit, 1.0, eye);
    // Force shadow acts on diffuse/border weights after the ramps, like lilToon; eyes cannot bypass it.
    // Keep front arithmetic unchanged and do not multiply the final specular/emission result.
    if (s.facing < 0)
    {
        viewLit *= backface;
        ramp.a *= backface;
    }

    // ---- Material diffuse (lit / dark / deeper dark tones, then the ramp hue)
    half3 diffuseLit = e.brdfDiffuse;
    #if defined(_FT_EF_HAIR)
        // Strand breakup: the painted strand detail in the albedo against a blurred copy of it (a mip `Breakup Blur`
        // levels up), pushed by Strand Breakup, so the locks and the lines between them read as separate strands. It is
        // tied to the UVs, so it moves with the hair (a world-space noise would slide when the hair swings).
        UNITY_BRANCH if (_EFHairStrandBreakup > 0.0)
        {
            half detailLuma = Luminance(SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, s.uvMain).rgb);
            half blurLuma = Luminance(SAMPLE_TEXTURE2D_BIAS(_MainTex, sampler_MainTex, s.uvMain, _EFHairStrandBlur).rgb);
            half strand = clamp(pow(max(detailLuma, 1e-3) / max(blurLuma, 1e-3), _EFHairStrandBreakup), 0.6, 1.4);
            diffuseLit *= strand;
        }
        // Strand lines: for hair painted as broad soft locks with no strands to push, a fine stripe noise in UV that
        // runs along the strands. The strand direction (down the head) is mapped into UV through the screen derivatives
        // and snapped to U or V, so the stripes hold still on UV strips laid out along an axis (the common hair-atlas
        // layout); tied to the UVs, they move with the hair. Faded out where the stripes get finer than a pixel.
        UNITY_BRANCH if (_EFHairStrandLines > 0.0)
        {
            float3 dpx = ddx(s.positionWS), dpy = ddy(s.positionWS);
            float2 duvx = ddx(s.uvMain), duvy = ddy(s.uvMain);
            float a11 = dot(dpx, dpx), a12 = dot(dpx, dpy), a22 = dot(dpy, dpy);
            float det = a11 * a22 - a12 * a12;
            float3 down = -headUp;
            float sa = (dot(down, dpx) * a22 - dot(down, dpy) * a12) / max(det, 1e-20);
            float sb = (a11 * dot(down, dpy) - a12 * dot(down, dpx)) / max(det, 1e-20);
            float2 uvDir = sa * duvx + sb * duvy;
            float across = (abs(uvDir.x) > abs(uvDir.y) ? s.uvMain.y : s.uvMain.x) * _EFHairStrandFreq;
            float noise = 0.65 * FairyToonEFNoise1D(across) + 0.35 * FairyToonEFNoise1D(across * 2.7 + 17.0);
            half fade = saturate(1.5 - fwidth(across)) * (det > 1e-20 ? 1.0 : 0.0);
            diffuseLit *= 1.0 + _EFHairStrandLines * fade * (noise - 0.5) * 2.0;
        }
    #endif
    #if defined(_FT_EF_SKIN) || defined(_FT_EF_FACE)
        // Fake SSS: a warm tint towards grazing angles; on the face only where it turns away from the camera.
        half sssArea = saturate(_EFSSSStrength * (1.0 - (NoV * 0.85 + 0.15))) * (1.0 - eye);
        #if defined(_FT_EF_FACE)
            sssArea *= lerp(saturate(dot(headForward, -toCamera) + 0.5), 1.0, faceMeshBlend);
        #endif
        diffuseLit *= lerp(1.0, _EFSSSColor.rgb, sssArea);
    #endif
    #if defined(_FT_EF_FACE) || defined(_FT_EF_EYE)
        // Eye overlay: R darkens the eye white under the upper lid, as Endfield's painted overlay-shadow sheet (目影)
        // does, so the eye sits in the socket; G lifts the lower iris by an HDR colour, light scattered through it.
        half2 eyeOverlay = SAMPLE_TEXTURE2D(_EFEyeOverlay, sampler_MainTex, s.uvMain).rg;
        diffuseLit *= lerp(1.0, _EFEyeShadowColor.rgb, eyeOverlay.r) * lerp(1.0, _EFEyeScatterColor.rgb, eyeOverlay.g);
    #endif
    half3 dark = _EFShadowDarkness * diffuseLit;
    #if defined(_FT_EF_SKIN) || defined(_FT_EF_FACE) || defined(_FT_EF_HAIR)
        // (skin: the shadow colour comes from the skin LUT, which shifts hue and saturation, not just brightness; warm
        // hair can use it too, which turns its undersides tan instead of grey, as in the community Endfield recreation)
        if (_EFSkinLUTOn > 0.5) dark = FairyToonEFSkinLUT(diffuseLit);
    #endif
    // In scene shadow or shade only the view light shapes the form.
    half3 shade = lerp(dark, FairyToonEFSaturate(diffuseLit), viewLit);
    // In the sun the view light still darkens edges inside the shadow side, so creases survive back light.
    half3 deeper = FairyToonEFSaturate(_EFShadowDarker * dark);
    half3 tone = lerp(deeper, dark, saturate(viewLit + ramp.a));
    tone = lerp(tone, diffuseLit, lit);
    // Ramp hue, weighted by its chroma (a grey ramp does nothing), luminance put back so only the hue changes.
    half chroma = max(ramp.r, max(ramp.g, ramp.b)) - min(ramp.r, min(ramp.g, ramp.b));
    half3 tinted = tone * lerp(1.0, ramp.rgb, chroma);
    tinted *= min(Luminance(tone) / max(Luminance(tinted), 0.0001), 4.0);
    half3 materialDiffuse = lerp(shade, tinted, sceneLit);
    // (eyes keep their own colour: only dimmed, never moved to the shadow tones)
    materialDiffuse = lerp(materialDiffuse, diffuseLit * eyeLit, eye);
    materialDiffuse *= lerp(1.0, aoContactTint, 0.40 * (1.0 - eye));
    #if !defined(_FT_EF_FACE) && !defined(_FT_EF_EYE) && !defined(_FT_EF_HAIR)
        UNITY_BRANCH if (_EFCurvature > 0.0)
        {
            float3 curvN = FairyToonEFNormalize(lerp(s.origN, N, 0.35));
            half curv = FairyToonEFCurvature(curvN, s.positionWS);
            materialDiffuse *= lerp(1.0, curv, smoothstep(0.08, 0.45, NoV));
        }
    #endif

    // ---- Light colour. In shadow the direct light only adds its brightness, so a coloured sun does not tint shadows.
    half3 ambient = FairyToonEFAmbient(N, s) * aoAmbientColor;
    // (in the shade the top light takes over from the sun, so it gets its own, stronger setting)
    half3 topLight = FairyToonEFTopLight(N, character);
    half lightLuma = Luminance(lightColor);
    half3 direct = lerp(lightLuma.xxx, lightColor, lit * sceneLit) * day;
    half3 mainSum = FairyToonEFLightCeiling(ambient + topLight + direct);

    l.addLightColor = 0;
    #if defined(_FT_EF_FACE)
        // Lamps on the face use a smooth sphere normal around the head, not the mesh normals, for the same reason the sun
        // uses the SDF: on the mesh, a lamp above and behind lit the upward-tilted nose bridge (and its painted white
        // highlight) as a bright patch on a dark face. The sphere gives one clean, soft gradient across the face.
        FairyToonSurface lampSurface = s;
        float3 headN = FairyToonEFNormalize(s.positionWS - (headPosition + headUp * 0.08));
        lampSurface.N = lampSurface.origN = lerp(headN, s.origN, faceMeshBlend);
        if (useBackfaceNormal) lampSurface.N = lampSurface.origN = N;
        FairyToonAdditionalLights(lampSurface, l);
    #else
        FairyToonAdditionalLights(s, l);
    #endif
    half3 addLight = l.addLightColor * aoLit;
    half3 col = materialDiffuse * mainSum + diffuseLit * addLight;
    #if defined(_FT_EF_HAIR)
        // (diffuse only: the band is added later at full strength, since it sits inside the root radius on the fringe)
        col *= lerp(1.0, _EFHairRootColor.rgb, rootAO);
    #endif

    // ---- Direct specular: GGX without 1/pi, half vector pulled towards the camera so highlights read from any angle
    // (none on the face, eyes or hair, which has its own band; only cloth and metal get environment reflection)
    #if !defined(_FT_EF_FACE) && !defined(_FT_EF_HAIR) && !defined(_FT_EF_EYE)
    half w = lerp(viewLit, lit, sceneLit);
    half specWeight = (w * 0.5 + 0.5) * (w * 0.65 + 0.35);
    float3 cameraLight = FairyToonEFNormalize(float3(toCamera.x, lerp(0.5, L.y, sceneLit), toCamera.z));
    float3 H = FairyToonEFNormalize(sceneLit * L + 2.0 * cameraLight + s.V * (sceneLit + 2.0));
    float NoH = saturate(dot(N, H));
    float a2 = e.roughness * e.roughness;
    float d = NoH * NoH * (a2 - 1.0) + 1.0;
    float D = a2 / (d * d);
    float vis = 1.0 / (4.0 * NoV + 2.0 * e.roughness);
    #if defined(_FT_ANISOTROPY) && defined(_FT_REFLECTION) && !defined(_FT_EF_SKIN)
        // lilToon has one Reflection specular. It owns the anisotropic cloth/metal highlight;
        // otherwise retain Endfield's isotropic GGX. Custom part highlights stay unchanged.
        if (_ApplySpecular > 0.5 && FairyToonAnisotropyReflection()) specWeight = 0.0;
    #endif
    // Specular ramp: U = highlight shape (centre 1) or Fresnel, V = roughness on non-metals.
    float rampU = lerp(saturate(D * a2), NoV * NoV, _EFSpecRampFresnel);
    half3 specRamp = SAMPLE_TEXTURE2D_LOD(_SpecRamp, sampler_ft_linear_clamp, float2(rampU, e.perceptualRoughness * (1.0 - e.metallic)), 0).rgb;
    half3 F = e.f0 * specRamp;
    half fresnel5 = pow(1.0 - NoV, 5.0);
    UNITY_BRANCH if (_EFMetalGrazingColor.a > 0.0)
    {
        half metalFresnel = pow(1.0 - NoV, 3.0);
        half3 metalBase = _EFMetalGrazingColor.rgb * specRamp;
        half brightSpec = smoothstep(0.12, 0.65, Luminance(_EFMetalGrazingColor.rgb));
        half3 metalEdge = max(metalBase, lerp(half3(0.42, 0.45, 0.52), half3(0.95, 0.97, 1.0), brightSpec));
        half3 metalTarget = lerp(metalBase, metalEdge, metalFresnel);
        F = lerp(F, metalTarget, saturate(e.metallic * _EFMetalGrazingColor.a));
    }
    half3 specLight = lerp(ambient + topLight, lightColor + topLight, day);
    col += min(max(D * vis - 6e-5, 0.0), 20.0) * F * specWeight * specLight * _EFSpecular;
    #if !defined(_FT_EF_SKIN)
        UNITY_BRANCH if (_EFClearcoat > 0.0)
        {
            // Mask dielectric clearcoat off metallic regions so buckles/hardware keep crisp obsidian-to-white chrome contrast.
            half ccMask = _EFClearcoat * (1.0 - e.metallic);
            float3 ccN = FairyToonEFNormalize(lerp(s.origN, N, 0.82));
            float3 ccDN = max(abs(ddx(ccN)), abs(ddy(ccN)));
            float ccVar = dot(ccDN, ccDN);
            half ccSmooth = saturate(_EFClearcoatSmoothness - smoothstep(0.05, 0.45, ccVar) * 0.28);
            float3 ccH = FairyToonEFNormalize(sceneLit * L + 0.85 * cameraLight + s.V * (sceneLit + 0.85));
            float ccNoH = saturate(dot(ccN, ccH));
            float ccNoV = saturate(dot(ccN, s.V));
            half ccPerceptual = saturate(1.0 - ccSmooth);
            half ccRough = max(ccPerceptual * ccPerceptual, 0.0078125);
            float ccA2 = ccRough * ccRough;
            float ccDenom = ccNoH * ccNoH * (ccA2 - 1.0) + 1.0;
            float ccD = ccA2 / (ccDenom * ccDenom);
            float ccVis = 1.0 / (4.0 * ccNoV + 2.0 * ccRough);
            half ccF = 0.04 + 0.76 * pow(1.0 - ccNoV, 5.0);
            col += min(max(ccD * ccVis - 6e-5, 0.0), 24.0) * ccF * ccMask * specWeight * specLight * _EFSpecular;
        }
    #endif
    #endif

    #if defined(_FT_EF_FACE)
    {
        // ---- Painted face highlight (nose tip, lips; the recreation's face highlight map): the UV is pushed by the view
        // in the head frame, so the highlight slides like a wet reflection instead of sitting in the albedo. It takes the
        // specular light and weight of the other parts. (Face UVs are laid out as seen from the front, so the head's
        // right is -U: a positive shift moves the highlight towards the camera.)
        float2 shift = float2(dot(s.V, headRight), -dot(s.V, headUp)) * _EFFaceHighlightShift;
        half highlight = SAMPLE_TEXTURE2D(_EFFaceHighlight, sampler_MainTex, s.uvMain + shift).r * (1.0 - eye);
        half w = lerp(viewLit, lit, sceneLit);
        half3 highlightLight = lerp(ambient + topLight, lightColor + topLight, day);
        col += highlight * (w * 0.5 + 0.5) * (w * 0.65 + 0.35) * highlightLight * _EFFaceHighlightColor.rgb;
    }
    #endif

    #if defined(_FT_EF_HAIR)
    {
        // ---- Hair band: Kajiya-Kay on a sphere around the head, not on the mesh normals, so the band is one clean
        // ring tied to the head. Removing the camera-right part of the sphere normal straightens it across the screen;
        // the strand runs from the crown down the sphere. The half vector is locked to the camera (Band Follows Light
        // mixes the light in) and raised by Band Height.
        float3 sphereN = FairyToonEFNormalize(s.positionWS - (headPosition + headUp * _EFHairCentre));
        // Preserve the authored front band; derive inside strands/lobes/ramp from the flipped surface normal.
        if (useBackfaceNormal) sphereN = N;
        float3 cameraRight = UNITY_MATRIX_V[0].xyz;
        float3 flatN = FairyToonEFNormalize(sphereN - cameraRight * (dot(sphereN, cameraRight) * _EFHairFlatten));
        float3 strand = FairyToonEFNormalize(headUp - flatN * dot(flatN, headUp));
        float3 bandH = FairyToonEFNormalize(lerp(toCamera, L, _EFHairLightFollow) + headUp * _EFHairBandOffset);
        float jitter = (hairMask.g - 0.5) * _EFHairJitter;
        float t1, t2;
        float lobe1 = FairyToonEFHairLobe(strand, flatN, bandH, _EFHairShift + jitter, _EFHairExponent, t1);
        float lobe2 = FairyToonEFHairLobe(strand, flatN, bandH, _EFHairShift2 + jitter, _EFHairExponent2, t2);
        // (flat top: the primary band ends sharply on the crown side, where T.H turns negative)
        float cut = (1.0 - _EFHairTopCut) * 3.0 * rsqrt(_EFHairExponent);
        float edge = max(fwidth(t1), 1e-4);
        lobe1 *= smoothstep(-cut - edge, -cut + edge, t1);
        // Colour from the hair spec ramp, looked up by the band's own strength (U: 0 at its edge, 1 at its centre), so a
        // ramp that steps up from black gives the band a clean toon edge; V = 0 facing the camera to 1 at the side.
        float bandV = 1.0 - saturate(dot(FairyToonEFNormalize(sphereN.xz), cameraXZ));
        half3 bandColor = SAMPLE_TEXTURE2D_LOD(_SpecRamp, sampler_ft_linear_clamp, float2(saturate(lobe1), bandV), 0).rgb;
        half bandLit = lerp(viewLit, lit, sceneLit);
        half bandWeight = (bandLit * 0.5 + 0.5) * (bandLit * 0.65 + 0.35);
        half3 bandLight = lerp(ambient + topLight, lightColor + topLight, day);
        // (the broad secondary takes the hair colour, or its own colour: a warm gold under blonde hair)
        half3 secondColor = lerp(diffuseLit, _EFHairColor2.rgb, _EFHairColor2.a);
        col += (lobe1 * _EFHairSpecular * bandColor + lobe2 * _EFHairSpecular2 * secondColor)
            * hairMask.r * e.ao * bandWeight * bandLight;
        // Back halo: with the light behind, the hair edges glow in the light colour.
        half halo = opposite * pow(1.0 - NoV, 3.0) * _EFHairHalo * day;
        col += halo * lerp(diffuseLit, 1.0, 0.5) * lightColor * e.ao;
    }
    #endif

    // ---- Environment specular: probe (or the fallback cubemap times the ambient light), split-sum with energy
    // compensation so rough metal does not go grey.
    #if !defined(_FT_EF_SKIN) && !defined(_FT_EF_FACE) && !defined(_FT_EF_HAIR) && !defined(_FT_EF_EYE)
    float3 R = reflect(-s.V, N);
    half3 env = FairyToonEFEnvReflection(R, s.positionWS, s.screenUV, e.perceptualRoughness, ambient);
    half tintLuma = _EFMetalGrazingColor.a > 0.0
        ? lerp(Luminance(e.f0), Luminance(_EFMetalGrazingColor.rgb), _EFMetalGrazingColor.a)
        : Luminance(e.f0);
    half brightMetal = smoothstep(0.12, 0.65, tintLuma);
    UNITY_BRANCH if (e.metallic > 0.0 && _EFEnvContrast > 0.0)
    {
        // Photographic light-tent chrome reflection in view reflection space (2x angular sweep across flat bevels).
        float3 viewN = FairyToonEFNormalize(mul((float3x3)UNITY_MATRIX_V, N));
        float3 viewV = FairyToonEFNormalize(mul((float3x3)UNITY_MATRIX_V, s.V));
        float3 viewR = reflect(-viewV, viewN);
        half smoothBias = 1.0 - e.perceptualRoughness * 0.72;
        float bandCoord = viewR.y + viewR.x * 0.28;
        half upperSoftbox = smoothstep(0.06, 0.42, bandCoord);
        half darkCore = pow(saturate(1.0 - abs(bandCoord + 0.06) * 2.6), 1.8);
        half lowerBounce = smoothstep(-0.22, -0.78, viewR.y) * lerp(0.16, 0.52, brightMetal);
        half keySpot = pow(saturate(dot(viewR, FairyToonEFNormalize(float3(-0.34, 0.62, 0.71)))), lerp(5.0, 26.0, smoothBias));
        half rimSpot = pow(saturate(dot(viewR, FairyToonEFNormalize(float3(0.66, 0.36, 0.66)))), lerp(4.0, 18.0, smoothBias));
        half baseBand = lerp(lerp(0.02, 0.10, brightMetal), lerp(0.42, 1.15, brightMetal), upperSoftbox);
        half domeLuma = baseBand * (1.0 - darkCore * lerp(0.92, 0.86, brightMetal))
            + lowerBounce + keySpot * lerp(1.45, 2.25, brightMetal) + rimSpot * lerp(0.85, 1.35, brightMetal);
        half3 chromeLight = max(ambient + topLight + lightColor * (day * 0.85), half3(0.45, 0.48, 0.54));
        half3 studioMetal = chromeLight * domeLuma;
        env = lerp(env, studioMetal, saturate(e.metallic * saturate(_EFEnvContrast * 0.65)));
    }
    half3 iblF = lerp(e.f0, F, _EFSpecRampFresnel);
    UNITY_BRANCH if (_EFMetalGrazingColor.a > 0.0)
    {
        half metalFresnelIbl = pow(1.0 - NoV, 3.0);
        half3 metalEdgeIbl = max(_EFMetalGrazingColor.rgb, lerp(half3(0.40, 0.44, 0.50), half3(0.95, 0.97, 1.0), brightMetal));
        half3 metalTargetIbl = lerp(_EFMetalGrazingColor.rgb, metalEdgeIbl, metalFresnelIbl);
        iblF = lerp(iblF, metalTargetIbl, saturate(e.metallic * _EFMetalGrazingColor.a));
    }
    half2 envBRDF = FairyToonEFEnvBRDF(e.perceptualRoughness, NoV);
    half3 envSpec = iblF * envBRDF.x + saturate(50.0 * iblF.g) * envBRDF.y;
    half ess = envBRDF.x + envBRDF.y;
    envSpec *= 1.0 + iblF * (1.0 - ess) / max(ess, 0.001);
    half3 aoEnv = lerp(aoAmbientColor, max(aoAmbientColor, lerp(0.18, 0.38, brightMetal)), e.metallic * saturate(_EFEnvContrast));
    col += env * envSpec * aoEnv * _EFEnvironment;
    UNITY_BRANCH if (_EFFabricSheen > 0.0)
    {
        // Dark-safe microfiber sheen: proportional tint (no flat grey floor on black cloth), tightened to lit fold crests.
        half sheenNdotL = saturate(dot(N, L) * 0.5 + 0.5);
        half sheenLight = lerp(0.16, 1.0, lit * sceneLit) * (0.25 + 0.75 * sheenNdotL);
        half sheenD = pow(1.0 - NoV, 3.8) * sheenLight;
        half lumaD = Luminance(diffuseLit);
        half3 fiberTint = lerp(diffuseLit, lumaD.xxx * 1.18 + min(lumaD * 0.35, 0.010), 0.45);
        col += sheenD * _EFFabricSheen * 0.50 * (1.0 - e.metallic) * aoAmbientColor * fiberTint * mainSum;
    }
    UNITY_BRANCH if (_EFClearcoat > 0.0)
    {
        half ccMaskIbl = _EFClearcoat * (1.0 - e.metallic);
        float3 ccN = FairyToonEFNormalize(lerp(s.origN, N, 0.82));
        float3 ccDN = max(abs(ddx(ccN)), abs(ddy(ccN)));
        float ccVar = dot(ccDN, ccDN);
        half ccSmooth = saturate(_EFClearcoatSmoothness - smoothstep(0.05, 0.45, ccVar) * 0.28);
        float3 ccR = reflect(-s.V, ccN);
        half ccNoV = saturate(dot(ccN, s.V));
        half ccPerceptual = saturate(1.0 - ccSmooth);
        half3 ccEnv = FairyToonEFEnvReflection(ccR, s.positionWS, s.screenUV, ccPerceptual, ambient);
        half ccF = 0.04 + 0.76 * pow(1.0 - ccNoV, 5.0);
        col += ccEnv * ccF * ccMaskIbl * aoAmbientColor * max(_EFEnvironment, 0.35);
    }
    #endif

    // ---- Rim: a Fresnel rim and a rim on the side the light comes from (horizontal light only). The face gets only
    // the Fresnel rim, inside its rim mask (_RimColorTex R) and on the camera's right side; eyes get none.
    // With Depth Edge Rim, both rims keep to the true outline instead of every surface that turns away from the camera
    // (FairyToonEFDepthEdge); cameras without a depth texture keep the N.V shape.
    half rimMask = min(e.ao, max(sceneLit, 1.0 - day));
    half fresnelRim = smoothstep(0.8 - 0.6 * _EFRimWidth, 0.9 - 0.4 * _EFRimWidth, 1.0 - NoV);
    half lightRimEdge = smoothstep(0.0, 1.0, saturate(5.0 * (0.4 - NoV)));
    half depthBlend = _EFRimDepth * saturate(_FT_DepthRim.x) * (1.0 - eye);
    UNITY_BRANCH if (depthBlend > 0.0)
    {
        half edge = FairyToonEFDepthEdge(s, N);
        fresnelRim = lerp(fresnelRim, edge, depthBlend);
        lightRimEdge = lerp(lightRimEdge, edge, depthBlend);
    }
    fresnelRim *= 1.0 - eye;
    #if defined(_FT_EF_FACE)
        fresnelRim *= SAMPLE_TEXTURE2D(_RimColorTex, sampler_MainTex, s.uvMain * _RimColorTex_ST.xy + _RimColorTex_ST.zw).r * saturate(dot(UNITY_MATRIX_V[0].xyz, N));
    #endif
    col += fresnelRim * rimMask * _EFRimStrength * _EFRimColor.rgb * lerp(0.25, diffuseLit, _EFRimAlbedo) * mainSum;
    #if !defined(_FT_EF_FACE) && !defined(_FT_EF_EYE)
        half x = dot(FairyToonEFNormalize(N.xz), lightXZ);
        half lightRim = saturate(0.5 + x - 0.5 * x * x) * lightRimEdge;
        col += lightRim * rimMask * day * _EFLightRim * diffuseLit * lightColor;
    #endif

    col = FairyToonEFSmallLights(col, s, N, diffuseLit, lit * sceneLit, e.ao, eye);

    // Reuse the Endfield main direction (including the character override) and its existing day / ambient lighting.
    // Only layer inputs change here; the Endfield result above is unchanged when the layers are disabled.
    #if defined(_FT_BACKLIGHT)
        l.origL = mainLight.direction;
        l.shadowAttenuation = mainLight.shadowAttenuation;
        l.backlightColor = lightColor * day; // Endfield direct main colour (fades with day), without ambient or top fill.
    #endif
    l.L = L;
    l.lightColor = mainSum;
    l.shadowmix = saturate(lit * sceneLit);
    return col;
}

// Outline colour in Endfield mode, after Floraison's frame capture: the local shadow tone (diffuse ramp hue, the skin LUT
// on skin and face, a little lighter on the lit side) times _EFOutlineColor, lit mainly by the ambient and top light,
// plus a per-character edge tint at grazing angles. No keywords here: the outline pass branches on _ShadingModel.
half3 FairyToonEFOutlineColor(FairyToonSurface s, half3 albedo)
{
    float4 character = _FT_EFLighting.w > 0.5 ? _FT_EFLighting : float4(1.0, 0.0, 0.0, 0.0);
    half day = character.x;
    Light mainLight = GetMainLight();
    float3 L = FairyToonEFLightDirection(mainLight.direction);
    half4 ramp = FairyToonEFRamp(clamp(dot(s.N, L) + _FT_EFParams.x, -1.0, 1.0) * 0.5 + 0.5);
    half3 dark = _EFShadowDarkness * albedo;
    if (_EFSkinLUTOn > 0.5) dark = FairyToonEFSkinLUT(albedo);
    half3 tone = lerp(dark, albedo, 0.35 * ramp.a * day);
    half chroma = max(ramp.r, max(ramp.g, ramp.b)) - min(ramp.r, min(ramp.g, ramp.b));
    tone *= lerp(1.0, ramp.rgb, chroma);
    half exposure = FairyToonEFExposure(Luminance(mainLight.color * mainLight.distanceAttenuation));
    half3 light = FairyToonEFAmbient(s.N, s) + FairyToonEFTopLight(s.N, character) + day * exposure * lerp(0.35, 1.0, ramp.a);
    half3 col = tone * _EFOutlineColor.rgb;
    // Endfield's lines are the local colour pushed far into saturation (a red-orange line on skin, a deep hue on
    // hair), not a dark grey: scale the chroma around the luminance. Before the light, so a cool shade light greys the
    // line as it greys the surface beside it (after the light, the boost brought the colour back: orange lines on
    // grey hair in the shade).
    half luminance = dot(col, half3(0.2126, 0.7152, 0.0722));
    col = max(luminance + (col - luminance) * _EFOutlineSaturation, 0.0);
    // The capture's final grade: a contrast squeeze towards mid grey (0.85, so Flatten 0.15) and a constant tint pull.
    // Here before the light, so the line still darkens with its surroundings instead of glowing in the dark.
    col = lerp(col, 0.5, _EFOutlineFlatten);
    col = lerp(col, _EFOutlinePull.rgb, _EFOutlinePull.a) * light;
    half grazing = 1.0 - abs(dot(s.V, s.N));
    col += grazing * _EFOutlineEdge.rgb * _EFOutlineEdge.a * day * 0.25;
    return col;
}

#endif
