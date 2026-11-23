// Refraction and gem optics adapted from lilToon (MIT, lilxyzw). URP scene colour replaces its grab texture.
#ifndef FAIRYTOON_VARIANTS_INCLUDED
#define FAIRYTOON_VARIANTS_INCLUDED

#include "FairyToonVariantInput.hlsl"

#if defined(FAIRYTOON_GEM_PRE)
#if defined(_FT_EF_IRIS)
// The camera silhouette must follow the pupil layer drawn by Gem Forward.
#include "FairyToonPasses.hlsl"
#endif
// Gem PRE keeps its black fogged silhouette; only the iris variant needs surface inputs to move the pupil.
struct FairyToonGemPreAttributes
{
    float4 positionOS : POSITION;
    #if defined(_FT_EF_IRIS)
        float3 normalOS : NORMAL;
        float4 tangentOS : TANGENT;
        float2 uv0 : TEXCOORD0;
    #endif
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct FairyToonGemPreVaryings
{
    float4 positionCS : SV_POSITION;
    half fogFactor : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
FairyToonGemPreVaryings FairyToonGemPreVertex(FairyToonGemPreAttributes input)
{
    FairyToonGemPreVaryings output = (FairyToonGemPreVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    #if defined(_FT_EF_IRIS)
        float3 positionWS = FairyToonEFPupilVertexPositionWS(input.positionOS.xyz, input.normalOS, input.tangentOS, input.uv0);
        output.positionCS = TransformWorldToHClip(positionWS);
    #else
        output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
    #endif
    output.fogFactor = ComputeFogFactor(output.positionCS.z);
    return output;
}
half4 FairyToonGemPreFragment(FairyToonGemPreVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    return half4(MixFog(half3(0, 0, 0), input.fogFactor), 0.0);
}
#else
#include "FairyToonPasses.hlsl"

#if defined(FAIRYTOON_REFRACTION) || defined(FAIRYTOON_GEM)
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

float FairyToonOpticalSmoothness(FairyToonSurface s)
{
    #if defined(FAIRYTOON_REFRACTION)
        float2 uv = s.uvMain * _SmoothnessTex_ST.xy + _SmoothnessTex_ST.zw;
    #else
        // lilToon Gem deliberately samples the main UV without the reflection-map transform.
        float2 uv = s.uvMain;
    #endif
    return _Smoothness * SAMPLE_TEXTURE2D(_SmoothnessTex, sampler_MainTex, uv).r;
}

float FairyToonOpticalRoughness(FairyToonSurface s)
{
    // Blur2 precomputation intentionally precedes reflection GSAA in lilToon: do not roughen or multiply twice.
    float perceptualRoughness = 1.0 - FairyToonOpticalSmoothness(s);
    #if defined(_FT_ANISOTROPY)
        if (FairyToonAnisotropyReflection()) perceptualRoughness *= saturate(1.2 - abs(s.anisotropy));
    #endif
    return perceptualRoughness;
}

#if defined(FAIRYTOON_REFRACTION)
float3 FairyToonRefractedScene(float2 uv, float perceptualRoughness, float viewDepth)
{
    #if defined(_FT_REFRACTION_BLUR)
        // lil_common_frag LIL_REFRACTION_BLUR2: same roughness/depth -> LOD relation, evaluated only once.
        float refractLod = min(sqrt(max(0.0, perceptualRoughness) / sqrt(max(viewDepth, 1e-4)) * 5.0), 10.0);
        // URP's copy has no mip chain. A nine-tap tent approximates that LOD without requiring another render pass.
        float2 radius = (exp2(refractLod) - 1.0) * _CameraOpaqueTexture_TexelSize.xy;
        float3 col = SampleSceneColor(uv) * 4.0;
        col += (SampleSceneColor(uv + float2(radius.x, 0)) + SampleSceneColor(uv - float2(radius.x, 0))
              + SampleSceneColor(uv + float2(0, radius.y)) + SampleSceneColor(uv - float2(0, radius.y))) * 2.0;
        col += SampleSceneColor(uv + radius) + SampleSceneColor(uv - radius)
             + SampleSceneColor(uv + radius * float2(1, -1)) + SampleSceneColor(uv + radius * float2(-1, 1));
        return col * (1.0 / 16.0);
    #else
        return SampleSceneColor(uv);
    #endif
}
#endif

#if defined(FAIRYTOON_GEM)
float3 FairyToonGemEnvironment(FairyToonSurface s, FairyToonLight l, float3 N, float roughness)
{
    float3 R = reflect(-s.V, N);
    if (FairyToonUseReflectionCube())
        return FairyToonSampleReflectionCube(R, roughness) * _ReflectionCubeColor.rgb
            * lerp(1.0, l.lightColor, _ReflectionCubeEnableLighting);
    return GlossyEnvironmentReflection(R, s.positionWS, roughness, 1.0, s.screenUV);
}

float3 FairyToonGemContrast(float3 col, float3 tint)
{
    float contrast = max(_GemEnvContrast, 1e-4);
    col = pow(saturate(col), contrast) * contrast * tint;
    return lerp(dot(col, float3(1.0/3.0, 1.0/3.0, 1.0/3.0)), col, saturate(1.0 / contrast));
}

float3 FairyToonGemOptics(FairyToonSurface s, FairyToonLight l, float3 shaded)
{
    float nv = abs(dot(s.N, s.V));
    float3 gemViewDirection = s.V;
    #if defined(USING_STEREO_MATRICES)
        float3 headPosition = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * 0.5;
        float3 headV = normalize(headPosition - s.positionWS);
        gemViewDirection = normalize(lerp(headV, s.V, _GemVRParallaxStrength));
    #endif
    // In a mono PC camera headV == V, so VR parallax has no visible effect (as in lilToon).
    float nv1 = abs(dot(s.N, gemViewDirection));
    float nv2 = abs(dot(s.N, gemViewDirection.yzx));
    float nv3 = abs(dot(s.N, gemViewDirection.zxy));
    float2 ref = TransformWorldToViewDir(s.N).xy;
    float nvRef = pow(saturate(1.0 - nv), _RefractionFresnelPower);
    float3 refractColor;
    refractColor.r = SampleSceneColor(s.screenUV + nvRef * _RefractionStrength * ref).r;
    refractColor.g = SampleSceneColor(s.screenUV + nvRef * (_RefractionStrength + _GemChromaticAberration) * ref).g;
    refractColor.b = SampleSceneColor(s.screenUV + nvRef * (_RefractionStrength + _GemChromaticAberration * 2.0) * ref).b;
    refractColor = FairyToonGemContrast(refractColor, 1.0);

    float perceptualRoughness = FairyToonOpticalRoughness(s);
    float3 normalG = s.facing < 0.0 ? normalize(s.N + s.V * (1.0 - nv1) * _GemChromaticAberration) : s.N;
    float3 normalB = s.facing < 0.0 ? normalize(s.N + s.V * (1.0 - nv1) * _GemChromaticAberration * 2.0) : s.N;
    float3 environment;
    environment.r = FairyToonGemEnvironment(s, l, s.N, perceptualRoughness).r;
    environment.g = FairyToonGemEnvironment(s, l, normalG, perceptualRoughness).g;
    environment.b = FairyToonGemEnvironment(s, l, normalB, perceptualRoughness).b;
    environment = FairyToonGemContrast(environment, _GemEnvColor.rgb);
    float3 baseCol = shaded * nv;
    if (s.facing < 0.0) environment *= baseCol;
    float roughness = perceptualRoughness * perceptualRoughness;
    float smoothness = FairyToonOpticalSmoothness(s);
    #if defined(UNITY_COLORSPACE_GAMMA)
        float grazingTerm = saturate(smoothness + 0.220916301);
        float surfaceReduction = 1.0 - 0.28 * roughness * perceptualRoughness;
    #else
        float grazingTerm = saturate(smoothness + 0.04);
        float surfaceReduction = rcp(roughness * roughness + 1.0);
    #endif
    float particle = step(0.5, frac(nv1 * _GemParticleLoop)) * step(0.5, frac(nv2 * _GemParticleLoop))
                   * step(0.5, frac(nv3 * _GemParticleLoop));
    float3 particleColor = s.facing < 0.0 ? 1.0 + particle * _GemParticleColor.rgb : 1.0;
    float fresnel = lerp(_Reflectance, grazingTerm, pow(1.0 - nv, 5.0));
    return baseCol * 0.75 * refractColor + (surfaceReduction * fresnel + 0.5) * 0.5 * particleColor * environment;
}
#endif

half3 FairyToonOpticalRim(FairyToonSurface s, FairyToonLight l, half3 col, half coverage)
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

    // Weight each sequential rim mask before blending, including Normal/Screen/Multiply.
    rimDir *= lerp(1.0, coverage, _RimApplyTransparency);
    rimIndir *= lerp(1.0, coverage, _RimApplyTransparency);
    half3 lightMul = 1.0 - _RimEnableLighting + l.lightColor * _RimEnableLighting;
    col = FairyToonTransparentBlend(col, rimColor.rgb * lightMul, rimDir * rimColor.a, _RimBlendMode, s.alpha, _RimApplyTransparency);
    col = FairyToonTransparentBlend(col, rimIndirColor.rgb * lightMul, rimIndir * rimIndirColor.a, _RimBlendMode, s.alpha, _RimApplyTransparency);
    return col;
}

// Operate on already-composed scene RGB. Unit alpha makes the shared surface helpers use framebuffer-space
// deltas; Gem's material alpha is then applied to each eligible mask once, not to the composite a second time.
half3 FairyToonOpticalLayers(FairyToonSurface s, FairyToonLight l, half3 col, half coverage)
{
    s.alpha = 1.0;
    #if defined(_FT_REFLECTION) && defined(FAIRYTOON_REFRACTION)
        col = FairyToonReflection(s, l, col);
    #endif
    #if defined(_FT_MATCAP)
        if (_UseMatCap > 0.5)
            col = FairyToonMatCapLayer(s, l, col, TEXTURE2D_ARGS(_MatCapTex, sampler_ft_linear_repeat), _MatCapBlendMask,
                _MatCapColor, _MatCapTex_ST, _MatCapBlendMask_ST, _MatCapBlend * lerp(1.0, coverage, _MatCapApplyTransparency),
                _MatCapBlendMode, _MatCapEnableLighting, _MatCapShadowMask, _MatCapBackfaceMask, _MatCapLod,
                _MatCapMainStrength, _MatCapZRotCancel, _MatCapPerspective,
                _MatCapApplyTransparency, FairyToonMatCapNormal(s, false));
        if (_UseMatCap2nd > 0.5)
            col = FairyToonMatCapLayer(s, l, col, TEXTURE2D_ARGS(_MatCap2ndTex, sampler_ft_linear_repeat), _MatCap2ndBlendMask,
                _MatCap2ndColor, _MatCap2ndTex_ST, _MatCap2ndBlendMask_ST, _MatCap2ndBlend * lerp(1.0, coverage, _MatCap2ndApplyTransparency),
                _MatCap2ndBlendMode, _MatCap2ndEnableLighting, _MatCap2ndShadowMask, _MatCap2ndBackfaceMask, _MatCap2ndLod,
                _MatCap2ndMainStrength, _MatCap2ndZRotCancel, _MatCap2ndPerspective,
                _MatCap2ndApplyTransparency, FairyToonMatCapNormal(s, true));
    #endif
    #if defined(_FT_RIM)
        col = FairyToonOpticalRim(s, l, col, coverage);
    #endif
    #if defined(_FT_GLITTER)
        col = lerp(col, FairyToonGlitter(s, l, col), lerp(1.0, coverage, _GlitterApplyTransparency));
    #endif
    #if defined(_FT_EMISSION)
        // Keep the two layers sequential: Normal/Screen/Multiply depend on the updated framebuffer RGB.
        if (_UseEmission > 0.5)
            col = FairyToonEmissionLayer(s, l, col, TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap), _EmissionBlendMask, _EmissionGradTex, _EmissionColor,
                _EmissionMap_ST, _EmissionMap_ScrollRotate, _EmissionMap_UVMode, _EmissionBlendMask_ST, _EmissionBlendMask_ScrollRotate,
                _EmissionBlend * coverage, _EmissionBlendMode, _EmissionMainStrength, _EmissionBlink, _EmissionUseGrad,
                _EmissionGradSpeed, _EmissionFluorescence);
        if (_UseEmission2nd > 0.5)
            col = FairyToonEmissionLayer(s, l, col, TEXTURE2D_ARGS(_Emission2ndMap, sampler_MainTex), _Emission2ndBlendMask, _Emission2ndGradTex, _Emission2ndColor,
                _Emission2ndMap_ST, _Emission2ndMap_ScrollRotate, _Emission2ndMap_UVMode, _Emission2ndBlendMask_ST, _Emission2ndBlendMask_ScrollRotate,
                _Emission2ndBlend * coverage, _Emission2ndBlendMode, _Emission2ndMainStrength, _Emission2ndBlink, _Emission2ndUseGrad,
                _Emission2ndGradSpeed, _Emission2ndFluorescence);
    #endif
    // Refraction adds the edge after optical composition; Gem follows transparent coverage.
    col += (_DissolveColor.rgb * s.dissolveEdge
          + _Main2ndDissolveColor.rgb * s.layerDissolveEdges.x
          + _Main3rdDissolveColor.rgb * s.layerDissolveEdges.y) * coverage;
    #if defined(FAIRYTOON_REFRACTION)
        // Optics already produced framebuffer RGB. Unit alpha avoids a second coverage division.
        col = FairyToonBackfaceColor(s, l, col);
    #endif
    return col;
}

half3 FairyToonOpticalShade(FairyToonSurface s, float2 uv0)
{
    FairyToonLight l;
    half3 col = FairyToonShadeBase(s, l);
    #if defined(FAIRYTOON_REFRACTION)
        float2 refractUV = s.screenUV + pow(1.0 - s.layers.nv, _RefractionFresnelPower)
            * _RefractionStrength * TransformWorldToViewDir(s.N).xy;
        float viewDepth = -TransformWorldToView(s.positionWS).z;
        float3 scene = FairyToonRefractedScene(refractUV, FairyToonOpticalRoughness(s), viewDepth) * _RefractionColor.rgb;
        if (_RefractionColorFromMain > 0.5) scene *= s.emissionAlbedo;
        // lilToon's Refraction first premultiplies diffuse, then mixes it with the captured scene using alpha.
        // This is internal optical composition; output coverage is one. Post-optical additive layers stay unmasked.
        col = lerp(scene, col * s.alpha, s.alpha);
        return FairyToonOpticalLayers(s, l, col, 1.0);
    #else
        col = FairyToonGemOptics(s, l, col);
        return FairyToonOpticalLayers(s, l, col, s.alpha);
    #endif
}

half4 FairyToonVariantFragment(FairyToonVaryings input, FRONT_FACE_TYPE isFront : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    FairyToonSurface s = FairyToonForwardSurface(input, IS_FRONT_VFACE(isFront, 1.0, -1.0));
    half3 col = FairyToonOpticalShade(s, input.uv0);
    #if defined(FAIRYTOON_GEM)
        // PRE contributes the fog colour once; each additive interior face fades towards black.
        return half4(MixFogColor(col, 0.0, input.fogFactor), s.alpha);
    #else
        return half4(MixFog(col, input.fogFactor), 1.0);
    #endif
}
#endif

#endif
#endif
