// FairyToon material inputs. Property names follow lilToon (MIT, lilxyzw) so materials convert by shader swap.
// Names marked "(FairyToon)" are ours and have no lilToon counterpart.
#ifndef FAIRYTOON_INPUT_INCLUDED
#define FAIRYTOON_INPUT_INCLUDED

// Each existing feature group has off / UV0 / extra-UV alternatives, not a new keyword dimension.
#if defined(_FT_NORMAL2ND_UV)
    #define _FT_NORMAL2ND 1
#endif
#if defined(_FT_GLITTER_UV)
    #define _FT_GLITTER 1
#endif

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

// Separate heavy shaders supply an extension before including this file. The main shader expands this to nothing.
#ifndef FT_VARIANT_PROPERTIES
    #define FT_VARIANT_PROPERTIES
#endif

// Every pass declares the same buffer so the SRP Batcher can batch all of them.
CBUFFER_START(UnityPerMaterial)
    // Main
    float4 _MainTex_ST;
    float4 _MainTexHSVG;
    half4  _Color;
    half   _Cutoff;
    half   _AlphaMaskMode;
    half   _AlphaMaskScale;
    half   _AlphaMaskValue;
    float4 _AlphaMask_ST;
    float4 _DissolveParams;
    float4 _DissolvePos;
    half   _FT_DissolveUseNoise;
    float4 _DissolveMask_ST;
    float4 _DissolveNoiseMask_ST;
    float4 _DissolveNoiseMask_ScrollRotate;
    half4  _DissolveColor;
    half   _DissolveNoiseStrength;
    half   _AAStrength;
    half   _FlipNormal;
    half4  _BackfaceColor;

    // Main colour layers (unconditional: identical buffer in every pass)
    half   _UseMain2ndTex;
    half4  _Color2nd;
    float4 _Main2ndTex_ST;
    float4 _Main2ndTex_ScrollRotate;
    half   _Main2ndTexAngle;
    half   _Main2ndTex_UVMode;
    half   _Main2ndTex_Cull;
    half   _Main2ndTexIsDecal;
    half   _Main2ndTexIsLeftOnly;
    half   _Main2ndTexIsRightOnly;
    half   _Main2ndTexShouldCopy;
    half   _Main2ndTexShouldFlipMirror;
    half   _Main2ndTexShouldFlipCopy;
    half   _Main2ndTexIsMSDF;
    half   _Main2ndTexBlendMode;
    half   _Main2ndTexAlphaMode;
    half   _Main2ndEnableLighting;
    float4 _Main2ndDissolveParams;
    float4 _Main2ndDissolvePos;
    float4 _Main2ndDissolveMask_ST;
    float4 _Main2ndDissolveNoiseMask_ST;
    float4 _Main2ndDissolveNoiseMask_ScrollRotate;
    half4  _Main2ndDissolveColor;
    half   _Main2ndDissolveNoiseStrength;
    half   _FT_Main2ndDissolveUseNoise;
    half   _UseMain3rdTex;
    half4  _Color3rd;
    float4 _Main3rdTex_ST;
    float4 _Main3rdTex_ScrollRotate;
    half   _Main3rdTexAngle;
    half   _Main3rdTex_UVMode;
    half   _Main3rdTex_Cull;
    half   _Main3rdTexIsDecal;
    half   _Main3rdTexIsLeftOnly;
    half   _Main3rdTexIsRightOnly;
    half   _Main3rdTexShouldCopy;
    half   _Main3rdTexShouldFlipMirror;
    half   _Main3rdTexShouldFlipCopy;
    half   _Main3rdTexIsMSDF;
    half   _Main3rdTexBlendMode;
    half   _Main3rdTexAlphaMode;
    half   _Main3rdEnableLighting;
    float4 _Main3rdDissolveParams;
    float4 _Main3rdDissolvePos;
    float4 _Main3rdDissolveMask_ST;
    float4 _Main3rdDissolveNoiseMask_ST;
    float4 _Main3rdDissolveNoiseMask_ScrollRotate;
    half4  _Main3rdDissolveColor;
    half   _Main3rdDissolveNoiseStrength;
    half   _FT_Main3rdDissolveUseNoise;

    // Wetness (unconditional: identical buffer in every pass, including dry variants)
    half   _UseWet;
    half   _WetPorosity;
    half   _WetDarken;
    half   _WetSaturation;
    half   _WetSmoothness;
    half   _WetSpecular;
    half   _WetEdgeDarken;
    half   _WetEdgeNoise;
    float  _WetStreakAmount;
    float  _WetStreakScale;
    float  _WetStreakDarken;
    float  _WetStreakBump;
    float  _WetStreakFlow;
    float  _WetStreakAxis;
    float  _WetFilm;
    float  _WetFilmStretch;
    float  _WetDropAmount;
    float  _WetDropScale;
    half   _WetGlintAmount;
    half   _WetHaze;
    // Sheer fabric
    half   _SheerAmount;
    half4  _SheerColor;
    half   _SheerPower;
    half   _SheerAlpha;

    // Normal map
    float4 _BumpMap_ST;
    half   _BumpScale;

    // Normal map 2nd (unconditional: identical buffer in every pass)
    half   _UseBump2ndMap;
    float4 _Bump2ndMap_ST;
    half   _Bump2ndMap_UVMode;
    half   _Bump2ndScale;
    float4 _Bump2ndScaleMask_ST;

    // Anisotropy
    half   _UseAnisotropy;
    float4 _AnisotropyTangentMap_ST;
    half   _AnisotropyScale;
    float4 _AnisotropyScaleMask_ST;
    half   _AnisotropyTangentWidth;
    half   _AnisotropyBitangentWidth;
    half   _AnisotropyShift;
    half   _AnisotropyShiftNoiseScale;
    half   _AnisotropySpecularStrength;
    half   _Anisotropy2ndTangentWidth;
    half   _Anisotropy2ndBitangentWidth;
    half   _Anisotropy2ndShift;
    half   _Anisotropy2ndShiftNoiseScale;
    half   _Anisotropy2ndSpecularStrength;
    float4 _AnisotropyShiftNoiseMask_ST;
    half   _Anisotropy2Reflection;
    half   _Anisotropy2MatCap;
    half   _Anisotropy2MatCap2nd;

    // Backlight
    half   _UseBacklight;
    half4  _BacklightColor;
    float4 _BacklightColorTex_ST;
    half   _BacklightMainStrength;
    half   _BacklightNormalStrength;
    half   _BacklightBorder;
    half   _BacklightBlur;
    half   _BacklightDirectivity;
    half   _BacklightViewStrength;
    half   _BacklightReceiveShadow;
    half   _BacklightBackfaceMask;

    // Lighting
    half   _LightMinLimit;
    half   _LightMaxLimit;
    half   _MonochromeLighting;
    half   _AsUnlit;

    // Shadow
    half   _UseShadow;
    half   _ShadowMaskType;
    half   _ShadowStrength;
    half4  _ShadowColor;
    half   _ShadowBorder;
    half   _ShadowBlur;
    half   _ShadowNormalStrength;
    half   _ShadowReceive;
    half4  _Shadow2ndColor;
    half   _Shadow2ndBorder;
    half   _Shadow2ndBlur;
    half   _Shadow2ndNormalStrength;
    half   _Shadow2ndReceive;
    half4  _Shadow3rdColor;
    half   _Shadow3rdBorder;
    half   _Shadow3rdBlur;
    half   _Shadow3rdNormalStrength;
    half   _Shadow3rdReceive;
    half   _ShadowMainStrength;
    half   _ShadowEnvStrength;
    half4  _ShadowBorderColor;
    half   _ShadowBorderRange;
    half4  _ShadowAOShift;
    half4  _ShadowAOShift2;
    half   _ShadowPostAO;
    half   _BackfaceForceShadow;
    half   _ShadowFlatBorder;
    half   _ShadowFlatBlur;
    float4 _FaceForward;            // (FairyToon) world head forward, set by FairyToonFaceShadow; xyz = 0 uses the object axes
    float4 _FaceRight;              // (FairyToon) world head right
    half   _SSAOStrength;           // (FairyToon) SSAO on the shadow / ambient side
    half   _SSAOLitStrength;        // (FairyToon) SSAO on the lit side

    // Rim shade (unconditional so every pass has the same UnityPerMaterial layout)
    half4  _RimShadeColor;
    half   _RimShadeNormalStrength;
    half   _RimShadeBorder;
    half   _RimShadeBlur;
    half   _RimShadeFresnelPower;

    // Rim light. _RimColorTex_ST is unconditional so every pass of a shader shares one UnityPerMaterial layout.
    // Default material value is (1, 1, 0, 0), Unity's scale/offset for a texture declared without NoScaleOffset.
    half4  _RimColor;
    float4 _RimColorTex_ST;
    half   _RimBorder;
    half   _RimBlur;
    half   _RimFresnelPower;
    half   _RimNormalStrength;
    half   _RimShadowMask;
    half   _RimBackfaceMask;
    half   _RimMainStrength;
    half   _RimEnableLighting;
    half   _RimBlendMode;
    half   _RimApplyTransparency;
    half   _RimDirStrength;
    half   _RimDirRange;
    half4  _RimIndirColor;
    half   _RimIndirRange;
    half   _RimIndirBorder;
    half   _RimIndirBlur;

    // MatCap
    half   _UseMatCap;
    half4  _MatCapColor;
    float4 _MatCapTex_ST;
    float4 _MatCapBlendMask_ST;
    half   _MatCapBlend;
    half   _MatCapBlendMode;
    half   _MatCapEnableLighting;
    half   _MatCapShadowMask;
    half   _MatCapBackfaceMask;
    half   _MatCapLod;
    half   _MatCapMainStrength;
    half   _MatCapNormalStrength;
    half   _MatCapCustomNormal;
    float4 _MatCapBumpMap_ST;
    half   _MatCapBumpScale;
    half   _MatCapZRotCancel;
    half   _MatCapPerspective;
    half   _MatCapApplyTransparency;
    half   _UseMatCap2nd;
    half4  _MatCap2ndColor;
    float4 _MatCap2ndTex_ST;
    float4 _MatCap2ndBlendMask_ST;
    half   _MatCap2ndBlend;
    half   _MatCap2ndBlendMode;
    half   _MatCap2ndEnableLighting;
    half   _MatCap2ndShadowMask;
    half   _MatCap2ndBackfaceMask;
    half   _MatCap2ndLod;
    half   _MatCap2ndMainStrength;
    half   _MatCap2ndNormalStrength;
    half   _MatCap2ndCustomNormal;
    float4 _MatCap2ndBumpMap_ST;
    half   _MatCap2ndBumpScale;
    half   _MatCap2ndZRotCancel;
    half   _MatCap2ndPerspective;
    half   _MatCap2ndApplyTransparency;

    // Glitter
    half   _UseGlitter;
    half   _GlitterUVMode;
    half4  _GlitterColor;
    float4 _GlitterColorTex_ST;
    half   _GlitterColorTex_UVMode;
    half   _GlitterMainStrength;
    half   _GlitterNormalStrength;
    half   _GlitterScaleRandomize;
    half   _GlitterApplyShape;
    float4 _GlitterShapeTex_ST;
    float4 _GlitterShapeTex_TexelSize;
    float4 _GlitterAtras;
    half   _GlitterAngleRandomize;
    float4 _GlitterParams1;
    float4 _GlitterParams2;
    half   _GlitterPostContrast;
    half   _GlitterSensitivity;
    half   _GlitterEnableLighting;
    half   _GlitterShadowMask;
    half   _GlitterBackfaceMask;
    half   _GlitterApplyTransparency;
    half   _GlitterVRParallaxStrength;

    // Emission (both layers share one keyword)
    half   _UseEmission;
    half4  _EmissionColor;
    float4 _EmissionMap_ST;
    float4 _EmissionMap_ScrollRotate;
    float4 _EmissionBlendMask_ST;
    float4 _EmissionBlendMask_ScrollRotate;
    float4 _EmissionBlink;
    half   _EmissionMap_UVMode;
    half   _EmissionBlend;
    half   _EmissionBlendMode;
    half   _EmissionMainStrength;
    half   _EmissionUseGrad;
    half   _EmissionGradSpeed;
    half   _EmissionFluorescence;
    half   _UseEmission2nd;
    half4  _Emission2ndColor;
    float4 _Emission2ndMap_ST;
    float4 _Emission2ndMap_ScrollRotate;
    float4 _Emission2ndBlendMask_ST;
    float4 _Emission2ndBlendMask_ScrollRotate;
    float4 _Emission2ndBlink;
    half   _Emission2ndMap_UVMode;
    half   _Emission2ndBlend;
    half   _Emission2ndBlendMode;
    half   _Emission2ndMainStrength;
    half   _Emission2ndUseGrad;
    half   _Emission2ndGradSpeed;
    half   _Emission2ndFluorescence;

    // Reflection
    float4 _SmoothnessTex_ST;
    float4 _MetallicGlossMap_ST;
    float4 _ReflectionColorTex_ST;
    half   _Smoothness;
    half   _Metallic;
    half   _Reflectance;
    half4  _ReflectionColor;
    half   _ApplySpecular;
    half   _ApplyReflection;
    half   _SpecularToon;
    half   _SpecularBorder;
    half   _SpecularBlur;
    half   _SpecularNormalStrength;
    half   _ReflectionNormalStrength;
    half   _ReflectionBlendMode;
    half4  _ReflectionCubeColor;
    half   _ReflectionCubeOverride;
    half   _ReflectionCubeEnableLighting;
    half   _ReflectionApplyTransparency;
    half4  _ReflectionCubeTex_HDR;
    half   _GSAAStrength;

    // Outline
    half4  _OutlineColor;
    float4 _OutlineTex_ST;
    half   _OutlineWidth;
    half   _OutlineFixWidth;
    half   _OutlineVertexR2Width;
    half   _OutlineZBias;
    half   _OutlineEnableLighting;

    // Endfield shading model (FairyToon)
    half   _ShadingModel;
    half   _EFPart;
    half   _EFPBRMapOn;             // (set by FairyToonMaterialSetup when _PBRMap is assigned)
    half   _EFMetallic;
    half   _EFSmoothness;
    half   _EFReflectivity;
    half   _EFOcclusion;
    half   _EFRampOn;               // (set by FairyToonMaterialSetup when _DiffuseRamp is assigned)
    half   _EFShadowDarkness;
    half   _EFShadowDarker;
    half   _EFShadowSaturation;
    half   _EFBacklight;
    half   _EFSpecRampFresnel;
    half   _EFSpecular;
    half   _EFEnvironment;
    half4  _EFRimColor;
    half   _EFRimStrength;
    half   _EFRimWidth;
    half   _EFRimAlbedo;
    half   _EFLightRim;
    half   _EFRimDepth;
    half   _EFRimDepthWidth;
    half   _EFRimDepthGap;
    half   _EFSkinLUTOn;            // (set by FairyToonMaterialSetup when _SkinLUT is assigned)
    half4  _EFSSSColor;
    half   _EFSSSStrength;
    half   _EFFaceSDFScale;
    half   _EFMultiBounceAO;
    half   _EFContactAO;
    half   _EFContactAORadius;
    half   _EFContactAOBias;
    half   _EFCurvature;
    half   _EFCurvatureScale;
    half   _EFEnvContrast;
    half4  _EFMetalGrazingColor;
    half   _EFFabricSheen;
    half   _EFClearcoat;
    half   _EFClearcoatSmoothness;

    // See-through hair (FairyToon)
    half   _SeeThroughRole;
    half   _SeeThroughMaskChannel;
    half   _SeeThroughMaskCutoff;
    half   _SeeThroughOpacity;
    half   _SeeThroughEarlyZ;
    half   _SeeThroughFade;
    half   _SeeThroughFadeSideAlpha;
    float  _SeeThroughFadeHorizontalMin;
    float  _SeeThroughFadeHorizontalMax;
    float  _SeeThroughFadeVerticalMin;
    float  _SeeThroughFadeVerticalMax;
    float4 _SeeThroughHead;
    half   _UseFakeShadow;
    half4  _FakeShadowColor;
    float4 _FakeShadowVector;

    // Endfield hair and eyes (FairyToon)
    half   _EFHairCentre;
    half   _EFHairFlatten;
    half   _EFHairBandOffset;
    half   _EFHairLightFollow;
    half   _EFHairShift;
    half   _EFHairExponent;
    half   _EFHairSpecular;
    half   _EFHairTopCut;
    half   _EFHairShift2;
    half   _EFHairExponent2;
    half   _EFHairSpecular2;
    half   _EFHairJitter;
    half   _EFHairHalo;
    half   _EFHairRootAO;
    half   _EFHairRootInner;
    half   _EFHairRootOuter;
    half   _EFHairRootLift;
    half4  _EFHairRootColor;
    half   _EFHairStrandBreakup;
    half   _EFHairStrandBlur;
    half   _EFHairStrandLines;
    half   _EFHairStrandFreq;
    half4  _EFHairColor2;
    half   _EFEyeLight;
    half4  _EFEyeShadowColor;
    half4  _EFEyeScatterColor;
    half   _EFIris;
    float  _EFPupilLayer;
    float  _EFPupilDepth;
    float  _EFIrisWorldRadius;
    float  _EFIrisDish;
    float  _EFIrisDepth;
    float  _EFIrisScale;
    float  _EFIrisRim;
    float  _EFIrisEdgeFade;
    float  _EFIrisVertical;
    float  _EFIrisMaxShift;
    float  _EFIrisGaze;
    float  _EFCorneaBulge;
    float  _EFCorneaNormalRange;
    float  _EFIrisRefraction;
    float  _EFIrisEdge;
    float  _EFIrisEdgeSoftness;
    float4 _EFIrisCenterL;
    float4 _EFIrisCenterR;
    float  _EFIrisRadius;
    float  _EFPupil;
    float4 _EFPupilCenterL;
    float4 _EFPupilCenterR;
    float  _EFPupilRadius;
    float  _EFPupilYRatio;
    float  _EFPupilScale;
    float4 _EFPupilOffset;
    float  _EFIrisSparkle;
    float  _EFIrisGlowStrength;
    float  _EFIrisSparkleScale;
    float  _EFCorneaIOR;
    half4  _EFIrisGlowColor;
    half4  _EFFaceHighlightColor;
    half   _EFFaceHighlightShift;

    // Character self-shadow and outline additions (FairyToon)
    half   _CharShadowReceive;
    half   _OutlineMode;            // 0 object-space width (lilToon), 1 even screen width in pixels
    half   _OutlinePixelWidth;      // (pixels at 1080p)
    half   _OutlineMinPixels;
    float  _OutlineFadeStart;
    float  _OutlineFadeEnd;
    half   _OutlineCornerTaper;
    half4  _EFOutlineColor;
    half4  _EFOutlineEdge;
    half   _EFOutlineSaturation;
    half   _EFOutlineFlatten;
    half4  _EFOutlinePull;
    FT_VARIANT_PROPERTIES
    // Stored lilToon layer metadata. Atlas playback is not sampled by the shader.
    float4 _Main2ndTexDecalAnimation;
    float4 _Main2ndTexDecalSubParam;
    float4 _Main3rdTexDecalAnimation;
    float4 _Main3rdTexDecalSubParam;
CBUFFER_END

// Endfield character globals from FairyToonCharacter: x day strength (1 sun, 0 shade), y top light strength in the
// sun, z top light offset, w = 1 when set. _FT_EFTopColor: rgb linear colour, a top light strength in the shade.
float4 _FT_EFLighting;
float4 _FT_EFTopColor;
// x: the top light's constant fill, added to saturate(N.y + offset) (the sources' o2 term)
float4 _FT_EFTopFill;
// Main light override: xyz the override light in camera terms (x camera right, y world up, z towards the camera), w the
// amount (0 the scene's main light, 1 the override)
float4 _FT_EFLightOverride;
// x terminator offset added to N.L on mesh-shaded parts, y number of small character lights
float4 _FT_EFParams;
// Character ambient: rgb linear sky (from above) and ground (from below) colours; _FT_EFAmbientSky.w blends this
// gradient over the scene's ambient probe
float4 _FT_EFAmbientSky;
float4 _FT_EFAmbientGround;
// Endfield exposure (FairyToonCharacter): x multiplier on the main light's direct term (it leaves the sun / shade state
// alone, unlike DayStrength), y how far the main light's own intensity is followed (0 normalised to luminance 1 as
// before, 1 its luminance over z), z reference luminance, w = 1 when set (0 everywhere = no change)
float4 _FT_EFExposure;
// Endfield ambient cap (FairyToonCharacter): x most luminance the ambient light may have (0 off), y ambient
// saturation (1 unchanged), z soft ceiling on the luminance of ambient + top + direct (0 off), w = 1 when set
float4 _FT_EFAmbientCap;
// Small character lights (FairyToonCharacterLight): xyz world position, w range; rgb linear colour, a intensity; x role
// (0 fill, 1 tint, 2 highlight, 3 rim, 4 fog)
// x = 1 when the FairyToon feature has asked URP for the camera depth texture before opaques (the depth edge rim needs
// it); 0 for cameras without it, where the rim falls back to N.V. y = 1 when _FT_CharacterDepth (a depth texture of the
// FairyToon characters alone, same encoding as the camera depth) is bound; the rim then measures gaps against it
float4 _FT_DepthRim;
TEXTURE2D_FLOAT(_FT_CharacterDepth);
#define FT_EF_SMALL_LIGHTS 4
float4 _FT_EFSmallLightPosition[FT_EF_SMALL_LIGHTS];
float4 _FT_EFSmallLightColor[FT_EF_SMALL_LIGHTS];
float4 _FT_EFSmallLightRole[FT_EF_SMALL_LIGHTS];

TEXTURE2D(_MainTex);            SAMPLER(sampler_MainTex);
TEXTURE2D(_MainColorAdjustMask);
// (own sampler: the depth-normals pass reads the normal map without the main texture, and a sampler is only
// bound when the texture it is named after is used)
TEXTURE2D(_BumpMap);            SAMPLER(sampler_BumpMap);
TEXTURE2D(_Bump2ndMap);
TEXTURE2D(_Bump2ndScaleMask);
TEXTURE2D(_AnisotropyTangentMap);
TEXTURE2D(_AnisotropyScaleMask);
TEXTURE2D(_AnisotropyShiftNoiseMask);
TEXTURE2D(_BacklightColorTex);
TEXTURE2D(_GlitterColorTex);
TEXTURE2D(_GlitterShapeTex);
TEXTURE2D(_AlphaMask);
TEXTURE2D(_DissolveMask);
TEXTURE2D(_DissolveNoiseMask);
TEXTURE2D(_Main2ndTex); SAMPLER(sampler_Main2ndTex);
TEXTURE2D(_Main2ndBlendMask);
TEXTURE2D(_Main2ndDissolveMask);
TEXTURE2D(_Main2ndDissolveNoiseMask);
TEXTURE2D(_Main3rdTex);
TEXTURE2D(_Main3rdBlendMask);
TEXTURE2D(_Main3rdDissolveMask);
TEXTURE2D(_Main3rdDissolveNoiseMask);
TEXTURE2D(_ShadowColorTex);
TEXTURE2D(_Shadow2ndColorTex);
TEXTURE2D(_Shadow3rdColorTex);
TEXTURE2D(_ShadowStrengthMask);
TEXTURE2D(_ShadowBorderMask);
TEXTURE2D(_ShadowBlurMask);
TEXTURE2D(_ShadowRampTex);      // (FairyToon)
TEXTURE2D(_SSAOMask);           // (FairyToon)
TEXTURE2D(_RimColorTex);
TEXTURE2D(_RimShadeMask);
TEXTURE2D(_MatCapTex);
TEXTURE2D(_MatCapBlendMask);
TEXTURE2D(_MatCapBumpMap);
TEXTURE2D(_MatCap2ndTex);
TEXTURE2D(_MatCap2ndBlendMask);
TEXTURE2D(_MatCap2ndBumpMap);
TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
TEXTURE2D(_EmissionBlendMask);
TEXTURE2D(_EmissionGradTex);
TEXTURE2D(_Emission2ndMap);
TEXTURE2D(_Emission2ndBlendMask);
TEXTURE2D(_Emission2ndGradTex);
TEXTURE2D(_SmoothnessTex);
TEXTURE2D(_MetallicGlossMap);
TEXTURE2D(_ReflectionColorTex);
TEXTURECUBE(_ReflectionCubeTex);
TEXTURE2D(_OutlineTex);
TEXTURE2D(_OutlineWidthMask);
TEXTURE2D(_PBRMap);             // (FairyToon) Endfield: R metallic, G reflectivity, B AO, A smoothness
TEXTURE2D(_DiffuseRamp);        // (FairyToon) Endfield: 256x1, RGB hue tint, A lit weight
TEXTURE2D(_SpecRamp);           // (FairyToon) Endfield: F0 colour, U highlight shape / Fresnel, V roughness
TEXTURE2D(_SkinLUT);            // (FairyToon) Endfield skin / face: albedo -> shadow colour, 2D LUT strip
TEXTURE2D(_HairMask);           // (FairyToon) Endfield hair: R highlight filter, G strand noise, B AO
TEXTURE2D(_EFIrisFrame);        // linear signed iris coordinates / UV scale / island code
TEXTURE2D(_EFIrisSparkleTex);   // refracted cornea matcap: RGB light, A screened glow
TEXTURE2D(_EFEyeMask);          // (FairyToon) Endfield face: R eye region (ignores light direction and shadows)
TEXTURE2D(_EFEyeOverlay);       // (FairyToon) Endfield face / eye: R lid shadow on the eye white, G iris scatter
TEXTURE2D(_EFFaceHighlight);    // (FairyToon) Endfield face: R painted highlight (nose tip, lips), view-shifted

TEXTURE2D(_SeeThroughMask);

// (inline sampler states; the names select the filter and wrap mode)
SAMPLER(sampler_ft_linear_repeat);
SAMPLER(sampler_ft_trilinear_repeat);
SAMPLER(sampler_ft_linear_clamp);

// Gate UV transfer separately from geometry needed by main-layer coverage.
// Material UV selectors are uniforms: off / UV0 / extra-UV alternatives strip unused VS outputs on FXC / Vulkan.
#if !defined(FAIRYTOON_PASS_FORWARD) && !defined(FAIRYTOON_PASS_OUTLINE) && defined(_FT_MAIN_LAYERS) && \
    (defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)) && \
    !(defined(FAIRYTOON_PASS_MOTIONVECTORS) && defined(_FT_OUTLINE))
    #define FT_LAYER_COVERAGE
#endif

#if (defined(FAIRYTOON_PASS_FORWARD) && \
     (defined(_FT_MAIN_LAYERS) || defined(_FT_EMISSION) || defined(_FT_NORMAL2ND_UV) || defined(_FT_GLITTER_UV))) || \
    defined(FT_LAYER_COVERAGE) || (defined(FAIRYTOON_PASS_DEPTHNORMALS) && defined(_FT_NORMAL2ND_UV))
    #define FT_LAYER_EXTRA_UVS
    #define FT_LAYER_ATTRIBUTES \
        float2 uv1 : TEXCOORD1; \
        float2 uv2 : TEXCOORD2; \
        float2 uv3 : TEXCOORD3;
    #define FT_UV_VARYINGS \
        float4 layerUV12 : TEXCOORD8; \
        float2 layerUV3 : TEXCOORD9;
    #define FT_TRANSFER_UVS(i,o) \
        o.layerUV12 = float4(i.uv1, i.uv2); \
        o.layerUV3 = i.uv3;
#else
    #define FT_LAYER_ATTRIBUTES
    #define FT_UV_VARYINGS
    #define FT_TRANSFER_UVS(i,o)
#endif

#if defined(FT_LAYER_COVERAGE)
    #if defined(FAIRYTOON_PASS_DEPTHNORMALS)
        // The depth-normal pass already carries its geometric normal and tangent handedness.
        #define FT_GEOMETRY_VARYINGS float3 layerPosition : TEXCOORD11;
        #define FT_TRANSFER_GEOMETRY(i,o) o.layerPosition = TransformObjectToWorld(i.positionOS.xyz);
        #define FT_RAW_LAYER_CONTEXT(i,f) FairyToonMakeLayerContext(i.uv0.xy, i.layerUV12, i.layerUV3, float4(i.normalWS.xyz, i.tangentWS.w), i.layerPosition, f)
    #else
        #define FT_GEOMETRY_VARYINGS \
            float4 layerNormal : TEXCOORD10; \
            float3 layerPosition : TEXCOORD11;
        #define FT_TRANSFER_GEOMETRY(i,o) \
            o.layerNormal = float4(TransformObjectToWorldNormal(i.normalOS), i.tangentOS.w * GetOddNegativeScale()); \
            o.layerPosition = TransformObjectToWorld(i.positionOS.xyz);
        #define FT_RAW_LAYER_CONTEXT(i,f) FairyToonMakeLayerContext(i.uv0.xy, i.layerUV12, i.layerUV3, i.layerNormal, i.layerPosition, f)
    #endif
#else
    #define FT_GEOMETRY_VARYINGS
    #define FT_TRANSFER_GEOMETRY(i,o)
    #if defined(FAIRYTOON_PASS_FORWARD) && (defined(_FT_MAIN_LAYERS) || defined(_FT_EMISSION))
        #define FT_RAW_LAYER_CONTEXT(i,f) FairyToonMakeLayerContext(i.uv0.xy, i.layerUV12, i.layerUV3, float4(i.normalWS, i.tangentWS.w), i.positionWS, f)
    #elif defined(FT_LAYER_EXTRA_UVS)
        // Normal Map 2nd / Glitter only read UVs; no view, normal or position context is needed.
        #define FT_RAW_LAYER_CONTEXT(i,f) FairyToonUVLayerContext(i.uv0.xy, i.layerUV12, i.layerUV3, f)
    #else
        #define FT_RAW_LAYER_CONTEXT(i,f) FairyToonBaseLayerContext(i.uv0.xy, f)
    #endif
#endif
// Reuse unbiased world positions in body passes (including post-skinning POSITION).
// Outline uses its rendered hull position, as lil_vert_outline runs before lil's OS copy.
// Never reconstruct OS from raster coordinates: camera VP/I_VP orientation can disagree
// in a DepthOnly prepass, and viewport offsets/render scale add further assumptions.
// Reuse spare depth/shadow/motion components; see-through needs direct OS transport.
#if !defined(FAIRYTOON_GEM) && !defined(FAIRYTOON_FUR_SHELL) && \
    (defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT) || \
     (defined(FAIRYTOON_PASS_FORWARD) && defined(_FT_MAIN_LAYERS)))
    #if defined(FAIRYTOON_PASS_FORWARD) || defined(FAIRYTOON_PASS_OUTLINE)
        #define FT_DISSOLVE_POSITION_OS(i) FairyToonDissolvePositionOS(i.positionWS)
    #elif defined(FT_LAYER_COVERAGE)
        #define FT_DISSOLVE_POSITION_OS(i) FairyToonDissolvePositionOS(i.layerPosition)
    #elif defined(FAIRYTOON_PASS_DEPTHNORMALS)
        // Preserve normal XYZ, tangent XYZW and mesh UV XY for normal-map evaluation.
        #define FT_DISSOLVE_DEPTHNORMALS_PACKED
        #define FT_DISSOLVE_VARYINGS
        #define FT_TRANSFER_DISSOLVE(i,o) o.uv0.zw = i.positionOS.xy; o.normalWS.w = i.positionOS.z;
        #define FT_DISSOLVE_POSITION_OS(i) float3(i.uv0.zw, i.normalWS.w)
    #elif defined(FAIRYTOON_PASS_DEPTH) || defined(FAIRYTOON_PASS_SHADOWCASTER) || defined(FAIRYTOON_PASS_CHARSHADOW)
        #define FT_DISSOLVE_VARYINGS
        #define FT_TRANSFER_DISSOLVE(i,o) o.normalWS = i.positionOS.xyz;
        #define FT_DISSOLVE_POSITION_OS(i) i.normalWS
    #elif defined(FAIRYTOON_PASS_MOTIONVECTORS)
        #define FT_DISSOLVE_MOTION_PACKED
        #define FT_DISSOLVE_POSITION_OS(i) float3(i.positionCSNoJitter.z, i.previousPositionCSNoJitter.z, i.uv0.z)
    #else
        // SeeThroughMask/FakeShadow/EarlyZ have only UV.xy, with two spare scalars.
        // A complete OS requires a second register; keep it independent of raster bias.
        #define FT_DISSOLVE_VARYING_OS
        #define FT_DISSOLVE_VARYINGS float3 dissolvePositionOS : TEXCOORD12;
        #define FT_TRANSFER_DISSOLVE(i,o) o.dissolvePositionOS = i.positionOS.xyz;
        #define FT_DISSOLVE_POSITION_OS(i) i.dissolvePositionOS
    #endif
#else
    #define FT_DISSOLVE_POSITION_OS(i) float3(0,0,0)
#endif
#ifndef FT_DISSOLVE_VARYINGS
    #define FT_DISSOLVE_VARYINGS
    #define FT_TRANSFER_DISSOLVE(i,o)
#endif
#define FT_LAYER_CONTEXT(i,f) FairyToonDissolveContext(FT_RAW_LAYER_CONTEXT(i,f), FT_DISSOLVE_POSITION_OS(i))
#define FT_LAYER_VARYINGS FT_UV_VARYINGS FT_GEOMETRY_VARYINGS FT_DISSOLVE_VARYINGS
#define FT_TRANSFER_LAYERS(i,o) FT_TRANSFER_UVS(i,o) FT_TRANSFER_GEOMETRY(i,o) FT_TRANSFER_DISSOLVE(i,o)

struct FairyToonLayerContext
{
    float2 uv0, uv1, uv2, uv3, uvMat;
    float nv, nvabs;
    float3 positionOS;
    half rightHand, facing;
};

bool FairyToonNeedsDissolvePosition()
{
    bool needed = false;
    #if (defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)) && !defined(FAIRYTOON_GEM) && !defined(FAIRYTOON_FUR_SHELL)
        needed = round(_DissolveParams.x) == 3.0;
    #endif
    #if defined(_FT_MAIN_LAYERS) && !defined(FAIRYTOON_GEM) && !defined(FAIRYTOON_FUR_SHELL)
        needed = needed || (_UseMain2ndTex > 0.5 && round(_Main2ndDissolveParams.x) == 3.0)
                        || (_UseMain3rdTex > 0.5 && round(_Main3rdDissolveParams.x) == 3.0);
    #endif
    return needed;
}

// HLSL ?: may evaluate both operands. Keep matrix ALU inside a real uniform branch.
float3 FairyToonDissolvePositionOS(float3 positionWS)
{
    // A single initialized return avoids FXC's "potentially uninitialized" warning on D3D12.
    float3 positionOS = float3(0,0,0);
    UNITY_BRANCH if (FairyToonNeedsDissolvePosition())
    {
        positionOS = TransformWorldToObject(positionWS);
    }
    return positionOS;
}

FairyToonLayerContext FairyToonDissolveContext(FairyToonLayerContext c, float3 positionOS)
{
    c.positionOS = positionOS;
    return c;
}

FairyToonLayerContext FairyToonBaseLayerContext(float2 uv0, half facing)
{
    FairyToonLayerContext c = (FairyToonLayerContext)0;
    c.uv0 = uv0;
    c.facing = facing;
    return c;
}

FairyToonLayerContext FairyToonUVLayerContext(float2 uv0, float4 uv12, float2 uv3, half facing)
{
    FairyToonLayerContext c = FairyToonBaseLayerContext(uv0, facing);
    c.uv1 = uv12.xy;
    c.uv2 = uv12.zw;
    c.uv3 = uv3;
    return c;
}

FairyToonLayerContext FairyToonMakeLayerContext(float2 uv0, float4 uv12, float2 uv3,
    float4 normal, float3 positionWS, half facing)
{
    FairyToonLayerContext c = (FairyToonLayerContext)0;
    c.uv0 = uv0;
    c.uv1 = uv12.xy;
    c.uv2 = uv12.zw;
    c.uv3 = uv3;
    float3 N = normalize(normal.xyz);
    c.uvMat = TransformWorldToViewDir(N).xy * 0.5 + 0.5;
    float nv = dot(N, GetWorldSpaceNormalizeViewDir(positionWS));
    c.nv = saturate(nv);
    c.nvabs = abs(nv);
    c.rightHand = normal.w > 0.0;
    c.facing = facing;
    return c;
}

float2 FairyToonLayerUV(FairyToonLayerContext c, half mode, bool emission)
{
    if (mode < 0.5) return c.uv0;
    if (mode < 1.5) return c.uv1;
    if (mode < 2.5) return c.uv2;
    if (mode < 3.5) return c.uv3;
    return emission ? float2(c.nvabs, c.nvabs) : c.uvMat;
}

float2 FairyToonRotateUV(float2 uv, float angle)
{
    float sn, cs;
    sincos(angle, sn, cs);
    uv -= 0.5;
    return float2(uv.x * cs - uv.y * sn, uv.x * sn + uv.y * cs) + 0.5;
}

float2 FairyToonAnimateUV(float2 uv, float4 st, float4 sr)
{
    return FairyToonRotateUV(uv * st.xy + st.zw, sr.z + sr.w * _Time.y) + frac(sr.xy * _Time.y);
}

// An optional pre-lighting multiplier keeps unlit layers under Rim Shade without dividing by dark channels.
half3 FairyToonBlend(half3 dst, half3 src, half3 srcA, half mode, half3 multiplier)
{
    half3 outCol = mode < 0.5 ? src * multiplier
                 : mode < 1.5 ? dst + src * multiplier
                 : mode < 2.5 ? max(dst + src * multiplier - dst * src, dst)
                 : dst * src;
    return lerp(dst, outCol, srcA);
}

half3 FairyToonBlend(half3 dst, half3 src, half3 srcA, half mode)
{
    return FairyToonBlend(dst, src, srcA, mode, half3(1, 1, 1));
}

// lilToon decal copy/mirror operations precede rotation in the untiled UV frame.
half4 FairyToonMainLayer(FairyToonLayerContext c, float2 uvMain, TEXTURE2D_PARAM(tex, samp), TEXTURE2D(mask),
    half4 color, float4 st, float4 sr, half angle, half uvMode, half cull, half decal,
    half leftOnly, half rightOnly, half copy, half flipMirror, half flipCopy, half msdf)
{
    float2 uv = FairyToonLayerUV(c, uvMode, false);
    float2 outUV = uv;
    if (copy > 0.5) outUV.x = abs(outUV.x - 0.5) + 0.5;
    st.zw += sr.xy * _Time.y;
    // Preserve negative tiling and avoid division by zero.
    float2 scale = float2(st.x < 0.0 ? -1.0 : 1.0, st.y < 0.0 ? -1.0 : 1.0) * max(abs(st.xy), 1e-6);
    outUV = outUV * scale + st.zw;
    if (flipCopy > 0.5 && uv.x < 0.5) outUV.x = 1.0 - outUV.x;
    if (flipMirror > 0.5 && c.rightHand > 0.5) outUV.x = 1.0 - outUV.x;
    if ((leftOnly > 0.5 && c.rightHand > 0.5) || (rightOnly > 0.5 && c.rightHand < 0.5)) outUV.x = -1.0;
    outUV = FairyToonRotateUV((outUV - st.zw) / scale, angle + sr.w * _Time.y) * scale + st.zw;
    half4 layer = SAMPLE_TEXTURE2D(tex, samp, outUV);
    if (msdf > 0.5)
    {
        float sd = max(min(layer.r, layer.g), min(max(layer.r, layer.g), layer.b));
        layer = half4(1, 1, 1, saturate((sd - 0.5) / clamp(fwidth(sd), 0.01, 1.0)));
    }
    if (decal > 0.5)
    {
        float2 edge = min(outUV, 1.0 - outUV);
        float2 coverage = saturate(edge / clamp(fwidth(edge), 0.0001, max(saturate(c.nv - 0.05), 0.0001)));
        layer.a *= coverage.x * coverage.y;
    }
    layer *= color;
    layer.a *= SAMPLE_TEXTURE2D(mask, sampler_MainTex, uvMain).r;
    if ((cull > 0.5 && cull < 1.5 && c.facing > 0.0) || (cull > 1.5 && c.facing < 0.0)) layer.a = 0;
    return layer;
}

void FairyToonMainLayerAlpha(inout half alpha, inout half layerAlpha, half mode)
{
    #if defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)
        if (mode > 0.5)
        {
            alpha = mode < 1.5 ? layerAlpha
                  : mode < 2.5 ? alpha * layerAlpha
                  : mode < 3.5 ? saturate(alpha + layerAlpha)
                  : saturate(alpha - layerAlpha);
            layerAlpha = 1.0;
        }
    #endif
}

// Base colour and alpha, including the alpha mask. Shared by every pass that clips.
void FairyToonApplyAlphaMask(inout half4 col, float2 uvMain)
{
    #if defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)
        if (_AlphaMaskMode > 0.5)
        {
            half mask = saturate(SAMPLE_TEXTURE2D(_AlphaMask, sampler_MainTex, uvMain * _AlphaMask_ST.xy + _AlphaMask_ST.zw).r * _AlphaMaskScale + _AlphaMaskValue);
            col.a = _AlphaMaskMode < 1.5 ? mask
                  : _AlphaMaskMode < 2.5 ? col.a * mask
                  : _AlphaMaskMode < 3.5 ? saturate(col.a + mask)
                  : saturate(col.a - mask);
        }
    #endif
}

// lilCalcDissolve / lilCalcDissolveWithNoise (MIT, lilxyzw). Always raw UV0.
// Noise uses scroll XY only, and its UV-line formula intentionally differs from the no-noise helper.
// Uniform helper selection mirrors lilToon's feature defines without multiplying shader variants.
void FairyToonCalcDissolve(inout half alpha, float2 uv0, float3 positionOS,
    float4 dissolveParams, float4 dissolvePos, TEXTURE2D(mask), float4 maskST,
    TEXTURE2D(noiseMask), float4 noiseST, float4 noiseScrollRotate,
    float noiseStrength, float useNoise, out half edge)
{
    edge = 0.0;
    dissolveParams.xy = round(dissolveParams.xy);
    UNITY_BRANCH
    if (dissolveParams.x == 0.0) return;
    float value = 1.0;
    if (dissolveParams.x == 1.0)
        value = SAMPLE_TEXTURE2D(mask, sampler_MainTex, uv0 * maskST.xy + maskST.zw).r;
    else if (dissolveParams.x == 2.0)
        value = dissolveParams.y == 1.0
            ? (useNoise > 0.5 ? dot(uv0, normalize(dissolvePos.xy)) : FairyToonRotateUV(uv0, dissolvePos.w).x)
            : distance(uv0, dissolvePos.xy);
    else if (dissolveParams.x == 3.0)
        value = dissolveParams.y == 1.0 ? dot(positionOS, normalize(dissolvePos.xyz)) : distance(positionOS, dissolvePos.xyz);
    else return;
    UNITY_BRANCH
    if (useNoise > 0.5)
    {
        float2 noiseUV = uv0 * noiseST.xy + noiseST.zw + frac(noiseScrollRotate.xy * _Time.y);
        value += (SAMPLE_TEXTURE2D(noiseMask, sampler_MainTex, noiseUV).r - 0.5) * noiseStrength;
    }
    float distanceToEdge = value - dissolveParams.z;
    // Keep the existing finite zero-width policy; lilToon divides by width directly.
    edge = 1.0 - saturate(abs(distanceToEdge) / max(dissolveParams.w, 1e-6));
    alpha *= distanceToEdge > 0.0 ? 1.0 : 0.0;
}

void FairyToonApplyDissolve(inout half alpha, float2 uv0, float3 positionOS, out half edge)
{
    edge = 0.0;
    // lilToon's Gem forward has no dissolve, but its shadow/depth/motion auxiliary passes do.
    // FairyToon deliberately skips both Gem forward and auxiliaries; retain Opaque/shell exclusions.
    #if (defined(_ALPHATEST_ON) || defined(_SURFACE_TYPE_TRANSPARENT)) && !defined(FAIRYTOON_FUR_SHELL) && !defined(FAIRYTOON_GEM)
        FairyToonCalcDissolve(alpha, uv0, positionOS, _DissolveParams, _DissolvePos,
            _DissolveMask, _DissolveMask_ST, _DissolveNoiseMask, _DissolveNoiseMask_ST,
            _DissolveNoiseMask_ScrollRotate, _DissolveNoiseStrength, _FT_DissolveUseNoise, edge);
    #endif
}

// lilToon (MIT, lilxyzw): tone correction runs in sampled RGB space, before the main tint.
// HSVG is a Vector, not a Color; gamma here is the authored exponent, not a colour-space conversion.
float3 FairyToonToneCorrection(float3 c, float4 hsvg)
{
    c = pow(abs(c), hsvg.w);
    float4 p = (c.b > c.g) ? float4(c.bg, -1.0, 2.0/3.0) : float4(c.gb, 0.0, -1.0/3.0);
    float4 q = (p.x > c.r) ? float4(p.xyw, c.r) : float4(c.r, p.yzx);
    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    float3 hsv = float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
    hsv = float3(hsv.x + hsvg.x, saturate(hsv.y * hsvg.y), saturate(hsv.z * hsvg.z));
    return hsv.z - hsv.z * hsv.y + hsv.z * hsv.y * saturate(abs(frac(hsv.x + float3(1.0, 2.0/3.0, 1.0/3.0)) * 6.0 - 3.0) - 1.0);
}

float3 FairyToonMainColorAdjust(float3 col, float2 uvMain)
{
    // Keep default/native RGB exact, including HDR values above one. No mask read in this path.
    UNITY_BRANCH if (any(_MainTexHSVG != float4(0, 1, 1, 1)))
    {
        float mask = SAMPLE_TEXTURE2D(_MainColorAdjustMask, sampler_MainTex, uvMain).r;
        col = lerp(col, FairyToonToneCorrection(col, _MainTexHSVG), mask);
    }
    return col;
}

// lilToon applies main tone correction only in lil_pass_forward_normal (LIL_PASS_FORWARD_NORMAL_INCLUDED,
// lil_common_frag.hlsl:310,336-351): Gem forward and Fur shells run OVERRIDE_MAIN without it, and the outline
// colour comes from _OutlineColor / outline texture, never the main HSVG.
half4 FairyToonSampleMainColorRaw(float2 uvMain)
{
    return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain) * _Color;
}

half4 FairyToonSampleMainColor(float2 uvMain)
{
    #if defined(FAIRYTOON_GEM) || defined(FAIRYTOON_FUR_SHELL)
        return FairyToonSampleMainColorRaw(uvMain);
    #else
        half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uvMain);
        col.rgb = FairyToonMainColorAdjust(col.rgb, uvMain);
        return col * _Color;
    #endif
}

// Outline colour and alpha-only clip paths: unadjusted main colour (tone correction never changes alpha).
half4 FairyToonBaseColor(float2 uvMain, float2 uv0, float3 positionOS)
{
    half4 col = FairyToonSampleMainColorRaw(uvMain);
    FairyToonApplyAlphaMask(col, uvMain);
    half dissolveEdge;
    FairyToonApplyDissolve(col.a, uv0, positionOS, dissolveEdge);
    return col;
}

// Resolve body colour and coverage together. Outline and the motion hull use base alpha only.
half4 FairyToonLayeredBase(FairyToonLayerContext c, out half4 layer2, out half4 layer3, out half dissolveEdge, out half2 layerDissolveEdges)
{
    float2 uvMain = c.uv0 * _MainTex_ST.xy + _MainTex_ST.zw;
    half4 col = FairyToonSampleMainColor(uvMain);
    layer2 = layer3 = 0;
    layerDissolveEdges = 0;
    #if defined(_FT_MAIN_LAYERS)
        if (_UseMain2ndTex > 0.5)
        {
            layer2 = FairyToonMainLayer(c, uvMain, TEXTURE2D_ARGS(_Main2ndTex, sampler_Main2ndTex), _Main2ndBlendMask, _Color2nd,
                _Main2ndTex_ST, _Main2ndTex_ScrollRotate, _Main2ndTexAngle, _Main2ndTex_UVMode, _Main2ndTex_Cull,
                _Main2ndTexIsDecal, _Main2ndTexIsLeftOnly, _Main2ndTexIsRightOnly, _Main2ndTexShouldCopy,
                _Main2ndTexShouldFlipMirror, _Main2ndTexShouldFlipCopy, _Main2ndTexIsMSDF);
            // After blend mask, before layer AlphaMode / lit blend. Edges are emitted later,
            // independently of the layer alpha, just like lil_pass_forward_normal.
            #if !defined(FAIRYTOON_GEM) && !defined(FAIRYTOON_FUR_SHELL)
                FairyToonCalcDissolve(layer2.a, c.uv0, c.positionOS, _Main2ndDissolveParams, _Main2ndDissolvePos,
                    _Main2ndDissolveMask, _Main2ndDissolveMask_ST, _Main2ndDissolveNoiseMask, _Main2ndDissolveNoiseMask_ST,
                    _Main2ndDissolveNoiseMask_ScrollRotate, _Main2ndDissolveNoiseStrength, _FT_Main2ndDissolveUseNoise, layerDissolveEdges.x);
            #endif
            FairyToonMainLayerAlpha(col.a, layer2.a, _Main2ndTexAlphaMode);
            col.rgb = FairyToonBlend(col.rgb, layer2.rgb, layer2.a * _Main2ndEnableLighting, _Main2ndTexBlendMode);
        }
        if (_UseMain3rdTex > 0.5)
        {
            layer3 = FairyToonMainLayer(c, uvMain, TEXTURE2D_ARGS(_Main3rdTex, sampler_MainTex), _Main3rdBlendMask, _Color3rd,
                _Main3rdTex_ST, _Main3rdTex_ScrollRotate, _Main3rdTexAngle, _Main3rdTex_UVMode, _Main3rdTex_Cull,
                _Main3rdTexIsDecal, _Main3rdTexIsLeftOnly, _Main3rdTexIsRightOnly, _Main3rdTexShouldCopy,
                _Main3rdTexShouldFlipMirror, _Main3rdTexShouldFlipCopy, _Main3rdTexIsMSDF);
            // After blend mask, before layer AlphaMode / lit blend. Edges are emitted later,
            // independently of the layer alpha, just like lil_pass_forward_normal.
            #if !defined(FAIRYTOON_GEM) && !defined(FAIRYTOON_FUR_SHELL)
                FairyToonCalcDissolve(layer3.a, c.uv0, c.positionOS, _Main3rdDissolveParams, _Main3rdDissolvePos,
                    _Main3rdDissolveMask, _Main3rdDissolveMask_ST, _Main3rdDissolveNoiseMask, _Main3rdDissolveNoiseMask_ST,
                    _Main3rdDissolveNoiseMask_ScrollRotate, _Main3rdDissolveNoiseStrength, _FT_Main3rdDissolveUseNoise, layerDissolveEdges.y);
            #endif
            FairyToonMainLayerAlpha(col.a, layer3.a, _Main3rdTexAlphaMode);
            col.rgb = FairyToonBlend(col.rgb, layer3.rgb, layer3.a * _Main3rdEnableLighting, _Main3rdTexBlendMode);
        }
    #endif
    // lilToon applies the main alpha mask after the layer alpha modes, then the material dissolve.
    FairyToonApplyAlphaMask(col, uvMain);
    FairyToonApplyDissolve(col.a, c.uv0, c.positionOS, dissolveEdge);
    return col;
}

half4 FairyToonLayeredBase(FairyToonLayerContext c, out half4 layer2, out half4 layer3, out half dissolveEdge)
{
    half2 layerDissolveEdges;
    return FairyToonLayeredBase(c, layer2, layer3, dissolveEdge, layerDissolveEdges);
}

half4 FairyToonLayeredBase(FairyToonLayerContext c, out half4 layer2, out half4 layer3)
{
    half dissolveEdge;
    return FairyToonLayeredBase(c, layer2, layer3, dissolveEdge);
}

half4 FairyToonLayeredBase(FairyToonLayerContext c)
{
    half4 layer2, layer3;
    return FairyToonLayeredBase(c, layer2, layer3);
}

// Cutout: a screen-space sharpened edge (stable under TAA / DLAA) and a hard clip.
half FairyToonClipAlpha(half alpha)
{
    #if defined(_ALPHATEST_ON)
        alpha = saturate((alpha - _Cutoff) / max(fwidth(alpha), 0.0001) + 0.5);
        clip(alpha - 0.0001);
    #elif defined(_SURFACE_TYPE_TRANSPARENT)
        clip(alpha - _Cutoff);
    #endif
    return alpha;
}

// Head frame from FairyToonFaceShadow (per-renderer property block): face forward / right, head up and the head-bone
// position. Without the component: the object axes and origin.
void FairyToonEFHeadFrame(out float3 forward, out float3 right, out float3 up, out float3 position)
{
    forward = _FaceForward.xyz;
    right = _FaceRight.xyz;
    if (dot(forward, forward) < 0.25)
    {
        forward = mul((float3x3)UNITY_MATRIX_M, float3(0, 0, 1));
        right = mul((float3x3)UNITY_MATRIX_M, float3(1, 0, 0));
    }
    forward = forward * rsqrt(max(dot(forward, forward), 1e-8));
    right = right * rsqrt(max(dot(right, right), 1e-8));
    up = cross(forward, right);
    up *= rsqrt(max(dot(up, up), 1e-8));
    position = _SeeThroughHead.w > 0.5 ? _SeeThroughHead.xyz : TransformObjectToWorld(float3(0, 0, 0));
}

#endif
