// FairyToon character self-shadow: the per-character depth maps FairyToonFeature draws before opaques (Runtime/
// FairyToonCharacterShadow.cs), read by the forward passes. A 7x7 tent (16 bilinear compares, 64 depth tests, the
// sample count of Endfield's captured character projector) gives a smooth lit fraction; a narrow smoothstep around 0.5
// turns that into a crisp, antialiased toon edge, and _FT_CharShadowParams.z widens it back into a penumbra.
#ifndef FAIRYTOON_CHARACTER_SHADOW_INCLUDED
#define FAIRYTOON_CHARACTER_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Shadow/ShadowSamplingTent.hlsl"

#define FAIRYTOON_CHAR_SHADOW_SLICES 4

// x slice count (0 = off), y 1 / atlas resolution, z softness, w strength.
// Params2: x debug view (visibility only), y self-shadow only.
float4 _FT_CharShadowParams;
float4 _FT_CharShadowParams2;
float4x4 _FT_CharShadowMatrix[FAIRYTOON_CHAR_SHADOW_SLICES];   // world -> atlas UV, compare depth
float4 _FT_CharShadowRect[FAIRYTOON_CHAR_SHADOW_SLICES];       // tile in atlas UV: min xy, max xy
float4 _FT_CharShadowLight[FAIRYTOON_CHAR_SHADOW_SLICES];      // xyz towards the projector, w texel size (m)
float4 _FT_CharShadowOwners[FAIRYTOON_CHAR_SHADOW_SLICES];     // xy low and high halves of the caster's ID
TEXTURE2D_SHADOW(_FT_CharShadowMap);
SAMPLER_CMP(sampler_FT_CharShadowMap);

// (the last visibility computed in this pixel, for the debug view)
static half ftCharShadowVisibility = 1.0;

// Visibility (1 lit) and direction of the darkest eligible projector. Camera-aligned projectors belong to
// their caster, while main-light projectors share one direction and can shade nearby characters.
half FairyToonCharShadow(float3 positionWS, float3 normalWS, out float3 lightDir)
{
    lightDir = float3(0.0, 1.0, 0.0);
    float lit = 1.0;
    int count = min((int)_FT_CharShadowParams.x, FAIRYTOON_CHAR_SHADOW_SLICES);
    float texel = _FT_CharShadowParams.y;
    float4 texelSize = float4(texel, texel, 1.0 / max(texel, 1e-6), 1.0 / max(texel, 1e-6));
    [loop]
    for (int i = 0; i < count; i++)
    {
        if (_FT_CharShadowParams2.y > 0.5)
        {
            uint2 owner = (uint2)_FT_CharShadowOwners[i].xy;
            if ((owner.x | (owner.y << 16)) != unity_RendererUserValue) continue;
        }
        float4 light = _FT_CharShadowLight[i];
        // (receiver normal offset: one texel, three at grazing angles, so lit surfaces do not shadow themselves)
        float3 p = positionWS + normalWS * (light.w * (3.0 - 2.0 * saturate(dot(normalWS, light.xyz))));
        float3 uvz = mul(_FT_CharShadowMatrix[i], float4(p, 1.0)).xyz;
        float4 rect = _FT_CharShadowRect[i];
        // (the tent reaches 3.5 texels out; points closer to the tile edge would read the next tile)
        float inset = 4.0 * texel;
        if (any(uvz.xy < rect.xy + inset) || any(uvz.xy > rect.zw - inset)) continue;
        real weights[16];
        real2 uvs[16];
        SampleShadow_ComputeSamples_Tent_7x7(texelSize, uvz.xy, weights, uvs);
        float sliceLit = 0.0;
        [unroll]
        for (int k = 0; k < 16; k++)
            sliceLit += weights[k] * SAMPLE_TEXTURE2D_SHADOW(_FT_CharShadowMap, sampler_FT_CharShadowMap, float3(uvs[k], uvz.z));
        if (sliceLit < lit)
        {
            lit = sliceLit;
            lightDir = light.xyz;
        }
    }
    // Toon edge: the tent's 0.5 contour is smooth even when a texel covers many pixels, so a step there stays free of
    // stair steps; the window never gets narrower than a pixel.
    float window = max(0.5 * _FT_CharShadowParams.z, fwidth(lit));
    half visibility = smoothstep(0.5 - window, 0.5 + window, lit);
    visibility = lerp(1.0, visibility, _FT_CharShadowParams.w);
    ftCharShadowVisibility = visibility;
    return visibility;
}

// The same, faded in by the surface's angle to the projector (Ronald Burns: lerp(1, shadow, saturate(NoL * 4))), so the
// toon ramp alone shades the terminator and the shadow map's faceted bands there never show.
half FairyToonCharShadowFaded(float3 positionWS, float3 normalWS)
{
    float3 lightDir;
    half visibility = FairyToonCharShadow(positionWS, normalWS, lightDir);
    visibility = lerp(1.0, visibility, saturate(dot(normalWS, lightDir) * 4.0));
    ftCharShadowVisibility = visibility;
    return visibility;
}

#endif
