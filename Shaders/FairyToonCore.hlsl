// FairyToon lighting. The toon diffuse, rim, MatCap and reflection terms follow lilToon (MIT, lilxyzw) so converted
// materials keep their look; additional lights get their own toon step instead of lilToon's flat add.
#ifndef FAIRYTOON_CORE_INCLUDED
#define FAIRYTOON_CORE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "FairyToonCharacterShadow.hlsl"

struct FairyToonSurface
{
    half3  albedo;
    half3  emissionAlbedo; // layered albedo before Rim Shade, also used by post-lighting tints
    half3  rimShade;
    half   alpha;

    half   wet;         // shared soak/rain mask; zero in dry variants
    #if defined(_FT_WET)
        half wetStreak;
        float3 wetFilmNormal; // water bump only; preserve the fabric's own normal/specular
        float3 wetAlong;      // nearest limb bone's axis on the surface (the way water runs); zero when no bone
        half   wetDrop;       // tiny droplets on the streaks, coverage
        half   wetShine;      // free water on a few stretches of the streaks (glint), coverage
        float3 wetDropNormal; // the drop's dome normal (world)
    #endif
    half   dissolveEdge;
    half2  layerDissolveEdges;
    float3 N;           // shading normal (normal map applied)
    float3 origN;       // geometric normal
    #if defined(_FT_MATCAP)
        float3x3 matcapTBN; // original mesh frame, before mapped normals and face flipping
    #endif
    float3 V;
    #if defined(_FT_ANISOTROPY)
        float3 anisoT, anisoB, anisoN;
        float anisotropy;
    #endif
    float3 positionWS;
    float2 uvMain;
    float2 screenUV;
    FairyToonLayerContext layers;
    half4 layer2, layer3;
    half   facing;      // +1 front, -1 back
};

struct FairyToonLight
{
    #if defined(_FT_BACKLIGHT)
        float3 origL;
        half3 backlightColor;
    #endif
    float3 L;           // blended toon light direction (main light + SH)
    half3  lightColor;  // main light + SH towards L, clamped (lilToon's lightColor)
    half3  indLightColor;
    half   shadowAttenuation;
    half   charShadow;  // character self-shadow (FairyToonFeature), faded and scaled by _CharShadowReceive
    half3  addLightColor;
    half   shadowmix;   // 0 in shadow, 1 lit (used by rim / MatCap shadow masks)
    half   aoAmbient;
    half   aoLit;
};

// ---------------------------------------------------------------------------------------------------------------------
// Helpers

half FairyToonGray(half3 c) { return dot(c, half3(1.0 / 3.0, 1.0 / 3.0, 1.0 / 3.0)); }

// Toon step with screen-space antialiasing (lilToon's lilTooningNoSaturateScale).
float FairyToonStep(float aa, float value, float border, float blur, float borderRange)
{
    float borderMin = saturate(border - blur * 0.5 - borderRange);
    float borderMax = saturate(border + blur * 0.5);
    return (value - borderMin) / saturate(borderMax - borderMin + fwidth(value) * aa);
}

float FairyToonStep(float aa, float value, float border, float blur)
{
    return FairyToonStep(aa, value, border, blur, 0.0);
}

#include "FairyToonAnisotropy.hlsl"
#include "FairyToonGlitter.hlsl"
#if defined(_FT_WET)
    #include "FairyToonWet.hlsl"
#endif

// SH irradiance in one direction (legacy probes; APV goes through SampleProbeVolumePixel).
half3 FairyToonSH(float3 dir, FairyToonSurface s)
{
    #if defined(PROBE_VOLUMES_L1) || defined(PROBE_VOLUMES_L2)
        return SampleProbeVolumePixel(half3(0, 0, 0), s.positionWS, dir, s.V, s.screenUV * _ScreenSize.xy);
    #else
        float4 n = float4(dir, 1.0);
        half3 res;
        res.r = dot(unity_SHAr, n);
        res.g = dot(unity_SHAg, n);
        res.b = dot(unity_SHAb, n);
        float4 vB = n.xyzz * n.yzzx;
        res.r += dot(unity_SHBr, vB);
        res.g += dot(unity_SHBg, vB);
        res.b += dot(unity_SHBb, vB);
        res += unity_SHC.rgb * (n.x * n.x - n.y * n.y);
        return max(res, 0.0);
    #endif
}

half3 FairyToonCorrectLight(half3 c)
{
    c = clamp(c, _LightMinLimit, _LightMaxLimit);
    c = lerp(c, FairyToonGray(c), _MonochromeLighting);
    return lerp(c, 1.0, _AsUnlit);
}

// ---------------------------------------------------------------------------------------------------------------------
// Main light, SH and SSAO

FairyToonLight FairyToonGetLight(FairyToonSurface s)
{
    FairyToonLight l = (FairyToonLight)0;

    // (characters are dynamic: no baked shadowmask)
    half4 shadowMask = half4(1, 1, 1, 1);
    float4 shadowCoord = TransformWorldToShadowCoord(s.positionWS);
    Light mainLight = GetMainLight(shadowCoord, s.positionWS, shadowMask);
    half3 mainColor = mainLight.color * mainLight.distanceAttenuation;
    #ifdef _LIGHT_LAYERS
        if (!IsMatchingLightLayer(mainLight.layerMask, GetMeshRenderingLayer())) mainColor = 0;
    #endif

    // Toon light direction: the main light weighted by brightness plus the SH dominant direction (lilToon).
    float3 shDir = (unity_SHAr.xyz + unity_SHAg.xyz + unity_SHAb.xyz) * 0.333333;
    float3 dir = mainLight.direction * Luminance(mainColor) + shDir;
    float3 shL = dot(dir, dir) < 0.000001 ? float3(0, 1, 0) : normalize(dir);
    l.L = normalize(dir + float3(0.001, 0.002, 0.001));

    l.lightColor = FairyToonCorrectLight(mainColor + FairyToonSH(shL * 0.666666, s));
    l.indLightColor = saturate(FairyToonSH(-shL * 0.666666, s));
    l.shadowAttenuation = mainLight.shadowAttenuation;
    #if defined(_FT_BACKLIGHT)
        l.origL = mainLight.direction;
    #endif
    l.charShadow = 1.0;
    #if defined(FAIRYTOON_PASS_FORWARD)
        float3 shadowN = s.facing < 0 ? -s.origN : s.origN;
        l.charShadow = lerp(1.0, FairyToonCharShadowFaded(s.positionWS, shadowN), _CharShadowReceive);
    #endif

    half ssao = 1.0;
    #if defined(_SCREEN_SPACE_OCCLUSION) && !defined(_SURFACE_TYPE_TRANSPARENT)
        ssao = GetScreenSpaceAmbientOcclusion(s.screenUV).indirectAmbientOcclusion;
        ssao = lerp(1.0, ssao, SAMPLE_TEXTURE2D(_SSAOMask, sampler_MainTex, s.uvMain).r);
    #endif
    l.aoAmbient = lerp(1.0, ssao, _SSAOStrength);
    l.aoLit = lerp(1.0, ssao, _SSAOLitStrength);
    return l;
}

// ---------------------------------------------------------------------------------------------------------------------
// Toon diffuse (lilToon lilGetShading, with optional ramp, face shadow map and SSAO)

half3 FairyToonDiffuse(FairyToonSurface s, inout FairyToonLight l)
{
    half3 albedo = s.albedo;
    if (_UseShadow < 0.5)
    {
        l.shadowmix = 1.0;
        return albedo * l.lightColor * l.aoLit;
    }

    float3 N1 = lerp(s.origN, s.N, _ShadowNormalStrength);
    float3 N2 = lerp(s.origN, s.N, _Shadow2ndNormalStrength);
    float3 N3 = lerp(s.origN, s.N, _Shadow3rdNormalStrength);
    half4 strengthMask = SAMPLE_TEXTURE2D(_ShadowStrengthMask, sampler_MainTex, s.uvMain);

    float aa = _AAStrength;
    float4 lns = 1.0;
    lns.x = saturate(dot(l.L, N1) * 0.5 + 0.5);
    lns.y = saturate(dot(l.L, N2) * 0.5 + 0.5);
    lns.z = saturate(dot(l.L, N3) * 0.5 + 0.5);

    // Face shadow map (lilToon _ShadowMaskType 2): R / G thresholds for light from either side, B blend back to
    // normal shading, A strength. Head vectors come from FairyToonFaceShadow; without it, the object axes.
    if (_ShadowMaskType > 1.5)
    {
        float3 faceF = _FaceForward.xyz;
        float3 faceRight = _FaceRight.xyz;
        if (dot(faceF, faceF) < 0.25)
        {
            faceF = mul((float3x3)UNITY_MATRIX_M, float3(0, 0, 1));
            faceRight = mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0));
        }
        float LdotR = dot(l.L.xz, -faceRight.xz);
        float sdf = LdotR < 0 ? strengthMask.g : strengthMask.r;
        faceF.y *= _ShadowFlatBlur;
        faceF = dot(faceF, faceF) == 0 ? 0 : normalize(faceF);
        float3 faceL = l.L;
        faceL.y *= _ShadowFlatBlur;
        faceL = dot(faceL, faceL) == 0 ? 0 : normalize(faceL);
        lns = lerp(saturate(dot(faceL, faceF) * 0.5 + sdf * 0.5 + 0.25), lns, strengthMask.b);
        aa = 0;
        strengthMask.r = strengthMask.a;
    }

    // Received shadow, and the character's own self-shadow (FairyToon)
    lns.x *= lerp(1.0, l.shadowAttenuation, _ShadowReceive) * l.charShadow;
    lns.y *= lerp(1.0, l.shadowAttenuation, _Shadow2ndReceive) * l.charShadow;
    lns.z *= lerp(1.0, l.shadowAttenuation, _Shadow3rdReceive) * l.charShadow;

    half4 blurMask = SAMPLE_TEXTURE2D(_ShadowBlurMask, sampler_MainTex, s.uvMain);
    float blur1 = _ShadowBlur * blurMask.r;
    float blur2 = _Shadow2ndBlur * blurMask.g;
    float blur3 = _Shadow3rdBlur * blurMask.b;

    // AO map (_ShadowBorderMask) shifts the border; _ShadowPostAO applies it after the step instead
    half4 borderMask = SAMPLE_TEXTURE2D(_ShadowBorderMask, sampler_MainTex, s.uvMain);
    borderMask.r = saturate(borderMask.r * _ShadowAOShift.x + _ShadowAOShift.y);
    borderMask.g = saturate(borderMask.g * _ShadowAOShift.z + _ShadowAOShift.w);
    borderMask.b = saturate(borderMask.b * _ShadowAOShift2.x + _ShadowAOShift2.y);
    float rampInput = lns.x;
    if (_ShadowPostAO < 0.5) lns.xyz *= borderMask.rgb;
    lns.w = lns.x;
    lns.x = FairyToonStep(aa, lns.x, _ShadowBorder, blur1);
    lns.y = FairyToonStep(aa, lns.y, _Shadow2ndBorder, blur2);
    lns.z = FairyToonStep(aa, lns.z, _Shadow3rdBorder, blur3);
    lns.w = FairyToonStep(aa, lns.w, _ShadowBorder, blur1, _ShadowBorderRange);
    if (_ShadowPostAO > 0.5) lns *= borderMask.rgbr;
    lns = saturate(lns);

    float backface = s.facing < 0 ? 1.0 - _BackfaceForceShadow : 1.0;
    lns *= backface;
    l.shadowmix = lns.x;

    float strength = _ShadowStrength;
    if (_ShadowMaskType > 0.5 && _ShadowMaskType < 1.5)
    {
        // Flat (lilToon type 1): blend towards one shade for the whole object
        float3 flatN = normalize(mul((float3x3)UNITY_MATRIX_M, float3(0.0, 0.25, 1.0)));
        float lnFlat = saturate((dot(flatN, l.L) + _ShadowFlatBorder) / _ShadowFlatBlur);
        lnFlat *= lerp(1.0, l.shadowAttenuation, _ShadowReceive) * l.charShadow;
        lns = lerp(lnFlat.xxxx, lns, strengthMask.r);
    }
    else
    {
        strength *= strengthMask.r;
    }

    half3 directCol = albedo * l.lightColor;

    #if defined(_FT_SHADOW_RAMP)
        // Ramp: U = half-lambert after received shadow; the ramp colour replaces both shadow colours.
        rampInput *= lerp(1.0, l.shadowAttenuation, _ShadowReceive) * backface;
        half3 ramp = SAMPLE_TEXTURE2D(_ShadowRampTex, sampler_ft_linear_clamp, float2(rampInput, 0.5)).rgb;
        ramp = lerp(1.0, ramp, strength);
        l.shadowmix = Luminance(ramp);
        return directCol * ramp * lerp(l.aoAmbient, l.aoLit, l.shadowmix);
    #else
        lns.x = lerp(1.0, lns.x, strength);

        half4 shadowColorTex = SAMPLE_TEXTURE2D(_ShadowColorTex, sampler_MainTex, s.uvMain);
        half4 shadow2ndColorTex = SAMPLE_TEXTURE2D(_Shadow2ndColorTex, sampler_MainTex, s.uvMain);
        half3 indirectCol = lerp(albedo, shadowColorTex.rgb, shadowColorTex.a) * _ShadowColor.rgb;
        half3 shadow2nd = lerp(albedo, shadow2ndColorTex.rgb, shadow2ndColorTex.a) * _Shadow2ndColor.rgb;
        indirectCol = lerp(indirectCol, shadow2nd, _Shadow2ndColor.a - lns.y * _Shadow2ndColor.a);
        if (_Shadow3rdColor.a > 0.0)
        {
            half4 shadow3rdColorTex = SAMPLE_TEXTURE2D(_Shadow3rdColorTex, sampler_MainTex, s.uvMain);
            half3 shadow3rd = lerp(albedo, shadow3rdColorTex.rgb, shadow3rdColorTex.a) * _Shadow3rdColor.rgb;
            indirectCol = lerp(indirectCol, shadow3rd, _Shadow3rdColor.a - lns.z * _Shadow3rdColor.a);
        }
        indirectCol = lerp(indirectCol, indirectCol * albedo, _ShadowMainStrength);

        indirectCol *= l.lightColor;
        indirectCol = lerp(indirectCol, albedo, saturate(l.indLightColor * _ShadowEnvStrength));
        indirectCol = min(indirectCol, directCol);
        indirectCol = lerp(indirectCol, directCol, lns.w * _ShadowBorderColor.rgb);

        return lerp(indirectCol * l.aoAmbient, directCol * l.aoLit, lns.x);
    #endif
}

// ---------------------------------------------------------------------------------------------------------------------
// Additional lights: each one gets the same toon step as the main light (Forward+ cluster loop, light layers).
// The step's antialiasing width comes from the normal's screen-space change, measured once before the loop
// (derivatives inside the dynamic cluster loop do not compile).

half3 FairyToonAdditionalLight(Light light, float3 N, float aaWidth)
{
    half3 color = light.color * light.distanceAttenuation;
    half receive = lerp(1.0, light.shadowAttenuation, max(_ShadowReceive, _Shadow2ndReceive));
    float ln = saturate(dot(light.direction, N) * 0.5 + 0.5) * receive;
    float borderMin = saturate(_ShadowBorder - _ShadowBlur * 0.5);
    float borderMax = saturate(_ShadowBorder + _ShadowBlur * 0.5);
    half lit = _UseShadow > 0.5 ? saturate((ln - borderMin) / saturate(borderMax - borderMin + aaWidth)) : ln;
    // (the shadow side keeps 1 - strength of the light, like the main light, and fades out facing away)
    lit = lerp(1.0 - _ShadowStrength, 1.0, lit) * saturate(ln * 4.0 - 1.0);
    return color * lit;
}

void FairyToonAdditionalLights(FairyToonSurface s, inout FairyToonLight l)
{
    #if defined(_ADDITIONAL_LIGHTS)
        float3 N = lerp(s.origN, s.N, _ShadowNormalStrength);
        float aaWidth = length(fwidth(N)) * 0.5 * _AAStrength;
        uint meshRenderingLayers = GetMeshRenderingLayer();
        half4 shadowMask = half4(1, 1, 1, 1);
        InputData inputData = (InputData)0;
        inputData.positionWS = s.positionWS;
        inputData.normalizedScreenSpaceUV = s.screenUV;
        uint pixelLightCount = GetAdditionalLightsCount();

        #if USE_CLUSTER_LIGHT_LOOP
            [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
            {
                CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
                Light light = GetAdditionalLight(lightIndex, s.positionWS, shadowMask);
                #ifdef _LIGHT_LAYERS
                    if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
                #endif
                    l.addLightColor += FairyToonAdditionalLight(light, N, aaWidth);
            }
        #endif

        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light light = GetAdditionalLight(lightIndex, s.positionWS, shadowMask);
            #ifdef _LIGHT_LAYERS
                if (IsMatchingLightLayer(light.layerMask, meshRenderingLayers))
            #endif
                l.addLightColor += FairyToonAdditionalLight(light, N, aaWidth);
        LIGHT_LOOP_END
    #endif
    l.addLightColor *= 1.0 - _AsUnlit;
}

// ---------------------------------------------------------------------------------------------------------------------
// Rim shade (lilToon: albedo multiplier before either lighting model)

half3 FairyToonRimShadeMultiplier(FairyToonSurface s)
{
    float3 N = lerp(s.origN, s.N, _RimShadeNormalStrength);
    float nvabs = abs(dot(N, s.V));
    float rim = pow(saturate(1.0 - nvabs), _RimShadeFresnelPower);
    rim = saturate(FairyToonStep(_AAStrength, rim, _RimShadeBorder, _RimShadeBlur));
    rim *= _RimShadeColor.a * SAMPLE_TEXTURE2D(_RimShadeMask, sampler_MainTex, s.uvMain).r;
    return lerp(half3(1, 1, 1), _RimShadeColor.rgb, rim);
}

// ---------------------------------------------------------------------------------------------------------------------
// Match lilToon's blend on premultiplied P = alpha * col while returning straight RGB.
// The unmasked route cannot represent nonzero light at alpha zero with finite SrcAlpha RGB.
half3 FairyToonTransparentBlend(half3 col, half3 layer, half3 amount, half mode, half alpha, half applyTransparency)
{
    #if defined(_SURFACE_TYPE_TRANSPARENT)
        float3 premultiplied = (float3)col * alpha;
        float3 delta = mode < 0.5 ? (float3)layer - premultiplied
                     : mode < 1.5 ? (float3)layer
                     : mode < 2.5 ? max((float3)layer - premultiplied * layer, 0.0)
                     : premultiplied * ((float3)layer - 1.0);
        float3 scale = applyTransparency > 0.5 ? 1.0 : alpha > 0.0 ? 1.0 / max(alpha, 1.0 / 4096.0) : 0.0;
        return clamp((float3)col + delta * amount * scale, -65504.0, 65504.0);
    #else
        return FairyToonBlend(col, layer, amount, mode);
    #endif
}

// Rim light (lilToon, with the light-direction option)

half3 FairyToonRim(FairyToonSurface s, FairyToonLight l, half3 col)
{
    half4 rimColor = _RimColor;
    half4 rimIndirColor = _RimIndirColor;
    half4 rimTex = SAMPLE_TEXTURE2D(_RimColorTex, sampler_MainTex, s.uvMain * _RimColorTex_ST.xy + _RimColorTex_ST.zw);
    rimColor *= rimTex;
    rimIndirColor *= rimTex;
    rimColor.rgb = lerp(rimColor.rgb, rimColor.rgb * s.emissionAlbedo, _RimMainStrength);

    float3 N = lerp(s.origN, s.N, _RimNormalStrength);
    float nvabs = abs(dot(N, s.V));
    float lnRaw = dot(l.L, N) * 0.5 + 0.5;
    float lnDir = saturate((lnRaw + _RimDirRange) / (1.0 + _RimDirRange));
    float lnIndir = saturate((1.0 - lnRaw + _RimIndirRange) / (1.0 + _RimIndirRange));
    float rim = pow(saturate(1.0 - nvabs), _RimFresnelPower);
    rim = s.facing < (_RimBackfaceMask - 1.0) ? 0.0 : rim;
    float rimDir = lerp(rim, rim * lnDir, _RimDirStrength);
    float rimIndir = rim * lnIndir * _RimDirStrength;
    rimDir = saturate(FairyToonStep(_AAStrength, rimDir, _RimBorder, _RimBlur));
    rimIndir = saturate(FairyToonStep(_AAStrength, rimIndir, _RimIndirBorder, _RimIndirBlur));
    rimDir = lerp(rimDir, rimDir * l.shadowmix, _RimShadowMask);
    rimIndir = lerp(rimIndir, rimIndir * l.shadowmix, _RimShadowMask);

    half3 lightMul = 1.0 - _RimEnableLighting + l.lightColor * _RimEnableLighting;
    col = FairyToonTransparentBlend(col, rimColor.rgb * lightMul, rimDir * rimColor.a, _RimBlendMode, s.alpha, _RimApplyTransparency);
    col = FairyToonTransparentBlend(col, rimIndirColor.rgb * lightMul, rimIndir * rimIndirColor.a, _RimBlendMode, s.alpha, _RimApplyTransparency);
    return col;
}

// ---------------------------------------------------------------------------------------------------------------------
// MatCap (lilToon UV: view-space with perspective correction and optional Z-roll cancel)

float2 FairyToonMatCapUV(float3 N, float3 V, float4 st, half zRotCancel, half perspective)
{
    float3 normalVD = (unity_OrthoParams.w == 0 && perspective > 0.5) ? V : UNITY_MATRIX_V[2].xyz;
    float3 bitangentVD = zRotCancel > 0.5 ? float3(0, 1, 0) : UNITY_MATRIX_V[1].xyz;
    bitangentVD = normalize(bitangentVD - normalVD * dot(normalVD, bitangentVD));
    float3 tangentVD = cross(normalVD, bitangentVD);
    float2 uv = float2(dot(tangentVD, N), dot(bitangentVD, N));
    return (uv * st.xy + st.zw) * 0.5 + 0.5;
}

float3 FairyToonMatCapNormal(FairyToonSurface s, bool second)
{
    float3 mappedNormal = s.N;
    #if defined(_FT_ANISOTROPY)
        if (_UseAnisotropy > 0.5 && (second ? _Anisotropy2MatCap2nd : _Anisotropy2MatCap) > 0.5) mappedNormal = s.anisoN;
    #endif
    float3 N = normalize(lerp(s.origN, mappedNormal, second ? _MatCap2ndNormalStrength : _MatCapNormalStrength));
    #if defined(_FT_MATCAP)
        // lilToon custom normals replace both the normal-strength blend and anisotropy.
        // Use uvMain plus each map's own ST and the original mesh TBN; an unassigned map is flat "bump".
        if (second ? _MatCap2ndCustomNormal > 0.5 : _MatCapCustomNormal > 0.5)
        {
            half3 normalTS;
            if (second)
                normalTS = FairyToonUnpackLayerNormal(SAMPLE_TEXTURE2D(_MatCap2ndBumpMap, sampler_MainTex,
                    s.uvMain * _MatCap2ndBumpMap_ST.xy + _MatCap2ndBumpMap_ST.zw), _MatCap2ndBumpScale);
            else
                normalTS = FairyToonUnpackLayerNormal(SAMPLE_TEXTURE2D(_MatCapBumpMap, sampler_MainTex,
                    s.uvMain * _MatCapBumpMap_ST.xy + _MatCapBumpMap_ST.zw), _MatCapBumpScale);
            N = normalize(mul(normalTS, s.matcapTBN));
            #if defined(_FT_ENDFIELD)
                N = s.facing < 0.0 ? -N : N;
            #else
                N = FairyToonFacingNormal(N, s.facing);
            #endif
        }
    #endif
    return N;
}

half3 FairyToonMatCapLayer(FairyToonSurface s, FairyToonLight l, half3 col, TEXTURE2D_PARAM(tex, samp), TEXTURE2D(mask),
    half4 color, float4 st, float4 maskST, half blend, half mode, half enableLighting, half shadowMask, half backfaceMask, half lod,
    half mainStrength, half zRotCancel, half perspective, half applyTransparency, float3 N)
{
    float2 uv = FairyToonMatCapUV(N, s.V, st, zRotCancel, perspective);
    half4 c = color * SAMPLE_TEXTURE2D_LOD(tex, samp, uv, lod);
    c.rgb = lerp(c.rgb, c.rgb * l.lightColor, enableLighting);
    c.a = lerp(c.a, c.a * l.shadowmix, shadowMask);
    c.a = s.facing < (backfaceMask - 1.0) ? 0.0 : c.a;
    half3 m = SAMPLE_TEXTURE2D(mask, sampler_MainTex, s.uvMain * maskST.xy + maskST.zw).rgb;
    c.rgb = lerp(c.rgb, c.rgb * s.emissionAlbedo, mainStrength);
    return FairyToonTransparentBlend(col, c.rgb, blend * c.a * m, mode, s.alpha, applyTransparency);
}

half3 FairyToonMatCap(FairyToonSurface s, FairyToonLight l, half3 col)
{
    if (_UseMatCap > 0.5)
    {
        col = FairyToonMatCapLayer(s, l, col, TEXTURE2D_ARGS(_MatCapTex, sampler_ft_linear_repeat), _MatCapBlendMask,
            _MatCapColor, _MatCapTex_ST, _MatCapBlendMask_ST, _MatCapBlend, _MatCapBlendMode, _MatCapEnableLighting, _MatCapShadowMask,
            _MatCapBackfaceMask, _MatCapLod, _MatCapMainStrength, _MatCapZRotCancel,
            _MatCapPerspective, _MatCapApplyTransparency, FairyToonMatCapNormal(s, false));
    }
    if (_UseMatCap2nd > 0.5)
    {
        col = FairyToonMatCapLayer(s, l, col, TEXTURE2D_ARGS(_MatCap2ndTex, sampler_ft_linear_repeat), _MatCap2ndBlendMask,
            _MatCap2ndColor, _MatCap2ndTex_ST, _MatCap2ndBlendMask_ST, _MatCap2ndBlend, _MatCap2ndBlendMode, _MatCap2ndEnableLighting,
            _MatCap2ndShadowMask, _MatCap2ndBackfaceMask, _MatCap2ndLod, _MatCap2ndMainStrength,
            _MatCap2ndZRotCancel, _MatCap2ndPerspective, _MatCap2ndApplyTransparency, FairyToonMatCapNormal(s, true));
    }
    return col;
}

// ---------------------------------------------------------------------------------------------------------------------
// Reflection (lilToon: GGX or toon specular from the toon light, plus environment reflection; off by default)

float FairyToonReflectionSmoothness(float smoothness, float3 N, float strength)
{
    float3 dx = ddx(N);
    float3 dy = ddy(N);
    float variance = max(dot(dx, dx), dot(dy, dy));
    float roughnessAA = max(0.0, variance / (variance * 5.0 + 0.002) * strength);
    return min(smoothness, saturate(1.0 - roughnessAA));
}

// Defend the GGX helper as well as its caller against a perfectly smooth input.
float FairyToonReflectionGGX(float roughness, float nv, float nl, float nh)
{
    float r = max(roughness, 0.002);
    float lambdaV = nl * (nv * (1.0 - r) + r);
    float lambdaL = nv * (nl * (1.0 - r) + r);
    float r2 = r * r;
    float d = (nh * r2 - nh) * nh + 1.0;
    float ggx = r2 / (d * d + 1e-7);
    return 0.5 / (lambdaV + lambdaL + 1e-5) * ggx;
}

// Match lilDecodeHDR for imported LDR cubes as well as encoded/native HDR cubes.
float3 FairyToonDecodeReflectionCube(float4 data, float4 hdr)
{
    float alpha = hdr.w * (data.a - 1.0) + 1.0;
    #if defined(UNITY_COLORSPACE_GAMMA)
        return (hdr.x * alpha) * data.rgb;
    #elif defined(UNITY_USE_NATIVE_HDR)
        return hdr.x * data.rgb;
    #else
        return (hdr.x * pow(abs(alpha), hdr.y)) * data.rgb;
    #endif
}

float3 FairyToonSampleReflectionCube(float3 R, float perceptualRoughness)
{
    float mip = perceptualRoughness * (10.2 - 4.2 * perceptualRoughness);
    return FairyToonDecodeReflectionCube(
        SAMPLE_TEXTURECUBE_LOD(_ReflectionCubeTex, sampler_ft_trilinear_repeat, R, mip), _ReflectionCubeTex_HDR);
}

bool FairyToonUseReflectionCube()
{
    #if defined(_FT_ENDFIELD) || USE_CLUSTER_LIGHT_LOOP
        // Clustered probes may exist only in the atlas, with an empty legacy slot.
        return _ReflectionCubeOverride > 0.5;
    #else
        uint width, height, levels;
        unity_SpecCube0.GetDimensions(0, width, height, levels);
        return _ReflectionCubeOverride > 0.5 || width < 15 || unity_SpecCube0_HDR.x == 0;
    #endif
}

half3 FairyToonReflection(FairyToonSurface s, FairyToonLight l, half3 col)
{
    float smoothness = _Smoothness * SAMPLE_TEXTURE2D(_SmoothnessTex, sampler_MainTex, s.uvMain * _SmoothnessTex_ST.xy + _SmoothnessTex_ST.zw).r;
    smoothness = FairyToonReflectionSmoothness(smoothness, s.N, _GSAAStrength);
    float perceptualRoughness = 1.0 - smoothness;
    #if defined(_FT_ANISOTROPY)
        if (FairyToonAnisotropyReflection()) perceptualRoughness *= saturate(1.2 - abs(s.anisotropy));
    #endif
    // Keep smoothness one finite for both GGX and the toon exponent.
    float roughness = max(0.002, perceptualRoughness * perceptualRoughness);

    half metallic = _Metallic * SAMPLE_TEXTURE2D(_MetallicGlossMap, sampler_MainTex, s.uvMain * _MetallicGlossMap_ST.xy + _MetallicGlossMap_ST.zw).r;
    col -= metallic * col;
    half3 specular = lerp(_Reflectance, s.emissionAlbedo, metallic);
    half4 reflectionColor = _ReflectionColor * SAMPLE_TEXTURE2D(_ReflectionColorTex, sampler_MainTex, s.uvMain * _ReflectionColorTex_ST.xy + _ReflectionColorTex_ST.zw);

    if (_ApplySpecular > 0.5)
    {
        float3 N = lerp(s.origN, s.N, _SpecularNormalStrength);
        float3 H = normalize(s.V + l.L);
        float nh = saturate(dot(N, H));
        half3 spec;
        // (FairyToon's combined shadow: toon terminator, Receive Shadow sliders, AO, face map and the character
        // self-shadow, so highlights never sit inside them; same in both shading models)
        half specularAttenuation = l.shadowmix;
        #if defined(_FT_ANISOTROPY)
        if (FairyToonAnisotropyReflection())
        {
            float distribution, visibility;
            FairyToonAnisotropicGGX(s, N, l.L, H, perceptualRoughness * perceptualRoughness, distribution, visibility);
            float term = distribution * visibility;
            #if defined(UNITY_COLORSPACE_GAMMA)
                term = sqrt(max(1e-4, term));
            #endif
            term *= saturate(dot(N, l.L)) * specularAttenuation;
            float lh = saturate(dot(l.L, H));
            half3 F = specular + (1.0 - specular) * pow(1.0 - lh, 5.0);
            spec = _SpecularToon > 0.5 ? FairyToonAnisoToon(term, fwidth(term) * _AAStrength) : term * F;
        }
        else
        #endif
        if (_SpecularToon > 0.5)
        {
            spec = saturate(FairyToonStep(_AAStrength, pow(nh, 1.0 / roughness), _SpecularBorder, _SpecularBlur));
        }
        else
        {
            float nv = saturate(dot(N, s.V));
            float nl = saturate(dot(N, l.L));
            float lh = saturate(dot(l.L, H));
            float term = FairyToonReflectionGGX(roughness, nv, nl, nh);
            #if defined(UNITY_COLORSPACE_GAMMA)
                term = sqrt(max(1e-4, term));
            #endif
            half3 F = specular + (1.0 - specular) * pow(1.0 - lh, 5.0);
            spec = term * nl * F;
        }
        #if defined(_FT_ANISOTROPY)
            if (!FairyToonAnisotropyReflection()) spec *= specularAttenuation;
        #else
            spec *= specularAttenuation;
        #endif
        col = FairyToonTransparentBlend(col, reflectionColor.rgb * l.lightColor, spec * reflectionColor.a, _ReflectionBlendMode, s.alpha, _ReflectionApplyTransparency);
    }

    if (_ApplyReflection > 0.5)
    {
        float3 reflectionN = s.N;
        #if defined(_FT_ANISOTROPY)
            if (FairyToonAnisotropyReflection()) reflectionN = s.anisoN;
        #endif
        float3 N = lerp(s.origN, reflectionN, _ReflectionNormalStrength);
        float3 R = reflect(-s.V, N);
        half3 env;
        if (FairyToonUseReflectionCube())
        {
            env = FairyToonSampleReflectionCube(R, perceptualRoughness);
            env *= _ReflectionCubeColor.rgb * lerp(1.0, l.lightColor, _ReflectionCubeEnableLighting);
        }
        else
        {
            env = GlossyEnvironmentReflection(R, s.positionWS, perceptualRoughness, 1.0, s.screenUV);
        }
        #if defined(UNITY_COLORSPACE_GAMMA)
            half oneMinusReflectivity = (1.0 - 0.220916301) * (1.0 - metallic);
        #else
            half oneMinusReflectivity = kDielectricSpec.a - metallic * kDielectricSpec.a;
        #endif
        half grazingTerm = saturate(smoothness + (1.0 - oneMinusReflectivity));
        #if defined(UNITY_COLORSPACE_GAMMA)
            float surfaceReduction = 1.0 - 0.28 * roughness * perceptualRoughness;
        #else
            float surfaceReduction = 1.0 / (roughness * roughness + 1.0);
        #endif
        // fd.nv in lilToon is the mapped/facing normal, independent of reflection strength/aniso.
        float nv = saturate(dot(s.N, s.V));
        half3 reflectCol = surfaceReduction * env * lerp(specular, grazingTerm, pow(1.0 - nv, 5.0));
        col = FairyToonTransparentBlend(col, reflectionColor.rgb, reflectCol * reflectionColor.a, _ReflectionBlendMode, s.alpha, _ReflectionApplyTransparency);
    }
    return col;
}

// ---------------------------------------------------------------------------------------------------------------------
// Emission

half FairyToonBlink(float4 blink)
{
    float wave = sin(_Time.y * blink.z + blink.w) * 0.5 + 0.5;
    if (blink.y > 0.5) wave = round(wave);
    return lerp(1.0, wave, blink.x);
}

// Straight RGB whose SrcAlpha blend equals lilToon's premultiplied emission blend.
// Keep this division-free so zero surface alpha and consecutive layers remain finite.
half3 FairyToonEmissionBlend(half3 col, half3 emission, half amount, half mode, half alpha)
{
    half3 premultiplied = col * alpha;
    half3 delta = mode < 0.5 ? emission - premultiplied
                : mode < 1.5 ? emission
                : mode < 2.5 ? max(emission - premultiplied * emission, 0.0)
                : premultiplied * (emission - 1.0);
    return col + delta * amount;
}

half3 FairyToonEmissionLayer(FairyToonSurface s, FairyToonLight l, half3 col,
    TEXTURE2D_PARAM(tex, samp), TEXTURE2D(mask), TEXTURE2D(grad), half4 color,
    float4 st, float4 sr, half uvMode, float4 maskST, float4 maskSR,
    half blend, half mode, half mainStrength, float4 blink, half useGrad, half gradSpeed, half fluorescence)
{
    float2 uv = FairyToonAnimateUV(FairyToonLayerUV(s.layers, uvMode, true), st, sr);
    half4 e = color * SAMPLE_TEXTURE2D(tex, samp, uv);
    // Stock lilToon enables animated mask UVs even when scroll/rotation is zero.
    float2 maskUV = FairyToonAnimateUV(s.layers.uv0, maskST, maskSR);
    // lilToon multiplies RGBA, including the mask's alpha.
    e *= SAMPLE_TEXTURE2D(mask, sampler_MainTex, maskUV);
    if (useGrad > 0.5) e *= SAMPLE_TEXTURE2D(grad, sampler_ft_linear_repeat, float2(_Time.y * gradSpeed, 0.5));
    half3 invLighting = saturate((1.0 - l.lightColor) * sqrt(max(l.lightColor, 0.0)));
    e.rgb = lerp(e.rgb, e.rgb * invLighting, fluorescence);
    e.rgb = lerp(e.rgb, e.rgb * s.emissionAlbedo, mainStrength);
    half amount = e.a * blend * FairyToonBlink(blink);
    half alpha = 1.0;
    #if defined(_SURFACE_TYPE_TRANSPARENT)
        alpha = s.alpha;
    #endif
    return FairyToonEmissionBlend(col, e.rgb, amount, mode, alpha);
}

half3 FairyToonEmission(FairyToonSurface s, FairyToonLight l, half3 col)
{
    if (_UseEmission > 0.5)
        col = FairyToonEmissionLayer(s, l, col, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap), _EmissionBlendMask, _EmissionGradTex, _EmissionColor,
            _EmissionMap_ST, _EmissionMap_ScrollRotate, _EmissionMap_UVMode, _EmissionBlendMask_ST, _EmissionBlendMask_ScrollRotate,
            _EmissionBlend, _EmissionBlendMode, _EmissionMainStrength, _EmissionBlink, _EmissionUseGrad, _EmissionGradSpeed, _EmissionFluorescence);
    if (_UseEmission2nd > 0.5)
        col = FairyToonEmissionLayer(s, l, col, TEXTURE2D_ARGS(_Emission2ndMap, sampler_MainTex), _Emission2ndBlendMask, _Emission2ndGradTex, _Emission2ndColor,
            _Emission2ndMap_ST, _Emission2ndMap_ScrollRotate, _Emission2ndMap_UVMode, _Emission2ndBlendMask_ST, _Emission2ndBlendMask_ScrollRotate,
            _Emission2ndBlend, _Emission2ndBlendMode, _Emission2ndMainStrength, _Emission2ndBlink, _Emission2ndUseGrad, _Emission2ndGradSpeed, _Emission2ndFluorescence);
    return col;
}

// The unlit portion of each main layer is composited after lighting, before emission (lilToon).
half3 FairyToonMainLayersUnlit(FairyToonSurface s, half3 col)
{
    #if defined(_FT_MAIN_LAYERS)
        bool unlit2 = _UseMain2ndTex > 0.5 && _Main2ndEnableLighting < 1.0;
        bool unlit3 = _UseMain3rdTex > 0.5 && _Main3rdEnableLighting < 1.0;
        if (!unlit2 && !unlit3) return col;
        half3 layerTint = 1.0;
        #if defined(_FT_ENDFIELD)
            layerTint = s.rimShade;
        #endif
        if (_UseMain2ndTex > 0.5)
            col = FairyToonBlend(col, s.layer2.rgb, s.layer2.a * (1.0 - _Main2ndEnableLighting), _Main2ndTexBlendMode, layerTint);
        if (_UseMain3rdTex > 0.5)
            col = FairyToonBlend(col, s.layer3.rgb, s.layer3.a * (1.0 - _Main3rdEnableLighting), _Main3rdTexBlendMode, layerTint);
    #endif
    return col;
}

// ---------------------------------------------------------------------------------------------------------------------
// Backlight precedes lilToon's premultiply: straight RGB adds it directly, then blending applies alpha once.
#if defined(_FT_BACKLIGHT)
half3 FairyToonBacklight(FairyToonSurface s, FairyToonLight l, half3 col)
{
    if (_UseBacklight < 0.5) return col;
    float3 N = lerp(s.origN, s.N, _BacklightNormalStrength);
    half4 color = _BacklightColor * SAMPLE_TEXTURE2D(_BacklightColorTex, sampler_MainTex,
        s.uvMain * _BacklightColorTex_ST.xy + _BacklightColorTex_ST.zw);
    float factor = pow(saturate(-dot(s.V, l.L) * 0.5 + 0.5), _BacklightDirectivity);
    float ln = dot(SafeNormalize(-s.V * _BacklightViewStrength + l.L), N) * 0.5 + 0.5;
    if (_BacklightReceiveShadow > 0.5)
    {
        // (lilToon uses the raw scene attenuation; Endfield honours its own Shadow Receive setting)
        #if defined(_FT_ENDFIELD)
            half attenuation = lerp(1.0, l.shadowAttenuation, _ShadowReceive);
        #else
            half attenuation = l.shadowAttenuation;
        #endif
        ln *= saturate(attenuation + distance(l.L, l.origL));
    }
    ln = saturate(FairyToonStep(_AAStrength, ln, _BacklightBorder, _BacklightBlur));
    float amount = saturate(factor * ln);
    amount = s.facing < (_BacklightBackfaceMask - 1.0) ? 0.0 : amount;
    color.rgb = lerp(color.rgb, color.rgb * s.emissionAlbedo, _BacklightMainStrength);
    return col + amount * color.a * color.rgb * l.backlightColor;
}
#endif

// Whole forward shading

// lilToon applies this RGB override after emission/premultiply, before fog. Convert back to straight RGB.
// Color alpha is a blend weight, not coverage; use the existing bounded transparent-layer conversion.
half3 FairyToonBackfaceColor(FairyToonSurface s, FairyToonLight l, half3 col)
{
    if (s.facing < 0.0 && _BackfaceColor.a != 0.0)
        return FairyToonTransparentBlend(col, _BackfaceColor.rgb * l.lightColor, _BackfaceColor.a, 0.0, s.alpha, 0.0);
    return col;
}

#include "FairyToonEndfield.hlsl"

// Shared by ordinary Forward and optical variants up to the point where scene colour is composed.
// inout preserves pre-RimShade emission albedo and the light state for all later layers.
half3 FairyToonShadeBase(inout FairyToonSurface s, out FairyToonLight l)
{
    #if defined(_FT_ENDFIELD)
        // Forward already faces mapped N. Keep the geometric normal geometric: Face needs -origN on backs.
        s.origN = s.facing < 0.0 ? -s.origN : s.origN;
    #endif
    // Emission main-strength uses the layered albedo before Rim Shade (lilToon).
    s.emissionAlbedo = s.albedo;
    s.rimShade = 1.0;
    #if defined(_FT_RIMSHADE)
        s.rimShade = FairyToonRimShadeMultiplier(s);
        #if defined(_FT_ENDFIELD)
            s.albedo *= s.rimShade;
        #endif
    #endif

    // Endfield replaces diffuse / shadow; every later layer shares the same order.
    l = (FairyToonLight)0;
    #if defined(_FT_ENDFIELD)
        half3 col = FairyToonEndfieldShade(s, l);
    #else
        l = FairyToonGetLight(s);
        half3 col = FairyToonDiffuse(s, l);
        FairyToonAdditionalLights(s, l);
        col += s.albedo * l.addLightColor * l.aoLit;
        l.lightColor = min(l.lightColor + l.addLightColor, _LightMaxLimit);
        l.shadowmix = saturate(l.shadowmix + Luminance(l.addLightColor));
        col = min(col, s.albedo * _LightMaxLimit);
    #endif
    col = FairyToonMainLayersUnlit(s, col);
    #if defined(_FT_RIMSHADE) && !defined(_FT_ENDFIELD)
        // lilToon shades the authored albedo first, then applies Rim Shade once.
        // Pre-darkening albedo also tinted the ShadowMainStrength term a second time.
        col *= s.rimShade;
    #endif
    #if defined(_FT_BACKLIGHT)
        #if !defined(_FT_ENDFIELD)
            l.backlightColor = l.lightColor;
        #endif
        col = FairyToonBacklight(s, l, col);
    #endif
    return col;
}

half3 FairyToonShade(FairyToonSurface s, float2 uv0)
{
    FairyToonLight l;
    half3 col = FairyToonShadeBase(s, l);
    #if defined(_FT_REFLECTION)
        col = FairyToonReflection(s, l, col);
    #endif
    #if defined(_FT_WET)
        #if defined(_FT_ENDFIELD)
            col += FairyToonWetSpecular(s, ftCharShadowVisibility, uv0);
        #else
            col += FairyToonWetSpecular(s, l.charShadow, uv0);
        #endif
    #endif
    #if defined(_FT_WET)
        // Soaked fabric loses its dry haze: the MatCap / rim sheen that makes sheer dark fabric (stockings) look light
        // fades where it is wet, most on the soaked tracks. This is what makes wet read darker, not brighter.
        half3 wetHazeBase = col;
        half wetHaze = saturate((0.55 * s.wet + 0.45 * s.wetStreak) * _WetPorosity) * saturate(_WetHaze) * (_FT_WetDebug.y > 0.5 ? 0.0 : 1.0);
    #endif
    #if defined(_FT_MATCAP)
        col = FairyToonMatCap(s, l, col);
    #endif
    #if defined(_FT_RIM)
        col = FairyToonRim(s, l, col);
    #endif
    #if defined(_FT_WET)
        col = lerp(col, min(col, wetHazeBase + max(col - wetHazeBase, 0.0) * 0.25), wetHaze);
    #endif
    #if defined(_FT_GLITTER)
        col = FairyToonGlitter(s, l, col);
    #endif
    #if defined(_FT_EMISSION)
        col = FairyToonEmission(s, l, col);
    #endif
    // Straight RGB: the framebuffer SrcAlpha blend applies coverage to this edge once.
    col += _DissolveColor.rgb * s.dissolveEdge
         + _Main2ndDissolveColor.rgb * s.layerDissolveEdges.x
         + _Main3rdDissolveColor.rgb * s.layerDissolveEdges.y;
    return FairyToonBackfaceColor(s, l, col);
}

#endif
