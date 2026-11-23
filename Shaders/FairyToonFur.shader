// FairyToon: one toon-first URP shader for characters and weapons. Property names and defaults follow lilToon
// (MIT, lilxyzw) so lilToon materials convert by shader swap; "(FairyToon)" marks our own additions.
Shader "FairyToon/Fur"
{
    Properties
    {
        // Base Setting / Rendering
        [Enum(Opaque, 0, Cutout, 1, Transparent, 2)] _TransparentMode ("Rendering Mode", Int) = 2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull Mode", Int) = 2
        [ToggleUI] _FlipNormal ("Flip Backface Normal", Int) = 0
        [HDR] _BackfaceColor ("Backface Color", Color) = (0, 0, 0, 0)
        [ToggleUI] _ZWrite ("ZWrite", Int) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Int) = 4
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("SrcBlend", Int) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("DstBlend", Int) = 10
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlendAlpha ("SrcBlendAlpha", Int) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlendAlpha ("DstBlendAlpha", Int) = 10
        [ToggleUI] _AlphaToMask ("Alpha To Coverage", Int) = 0
        _OffsetFactor ("Offset Factor", Float) = 0
        _OffsetUnits ("Offset Units", Float) = 0
        _AAStrength ("Toon Edge AA", Range(0, 1)) = 1

        // Main Color
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _MainTexHSVG ("Hue / Saturation / Value / Gamma", Vector) = (0, 1, 1, 1)
        [NoScaleOffset] _MainColorAdjustMask ("Color Adjustment Mask", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(-0.001, 1.001)) = 0.5
        [Enum(None, 0, Replace, 1, Multiply, 2, Add, 3, Subtract, 4)] _AlphaMaskMode ("Alpha Mask Mode", Int) = 0
        _AlphaMask ("Alpha Mask", 2D) = "white" {}
        _AlphaMaskScale ("Alpha Mask Scale", Float) = 1
        _AlphaMaskValue ("Alpha Mask Offset", Float) = 0
        _DissolveParams ("Dissolve: Mode (0 Off / 1 Mask / 2 UV / 3 Object), Shape, Threshold, Edge Width", Vector) = (0, 0, 0.5, 0.1)
        _DissolvePos ("Dissolve Position / Direction", Vector) = (0, 0, 0, 0)
        _DissolveMask ("Dissolve Mask", 2D) = "white" {}
        [ToggleUI] _FT_DissolveUseNoise ("Use Noise Helper (lilToon Feature)", Float) = 1
        _DissolveNoiseMask ("Dissolve Noise", 2D) = "gray" {}
        _DissolveNoiseMask_ScrollRotate ("Dissolve Noise Scroll XY", Vector) = (0, 0, 0, 0)
        _DissolveNoiseStrength ("Dissolve Noise Strength", Float) = 0.1
        [FairyToonHDR] _DissolveColor ("Dissolve Edge Color", Color) = (1, 1, 1, 1)

        // Main Color 2nd
        [ToggleUI] _UseMain2ndTex ("Main Color 2nd", Int) = 0
        [FairyToonHDR] _Color2nd ("Color", Color) = (1, 1, 1, 1)
        _Main2ndTex ("Texture", 2D) = "white" {}
        _Main2ndTexAngle ("Angle (radians)", Float) = 0
        _Main2ndTex_ScrollRotate ("Scroll XY / Rotation Speed W", Vector) = (0, 0, 0, 0)
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3, MatCap, 4)] _Main2ndTex_UVMode ("UV Mode", Int) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Main2ndTex_Cull ("Layer Cull", Int) = 0
        [ToggleUI] _Main2ndTexIsDecal ("Decal", Int) = 0
        [ToggleUI] _Main2ndTexIsLeftOnly ("Left Only", Int) = 0
        [ToggleUI] _Main2ndTexIsRightOnly ("Right Only", Int) = 0
        [ToggleUI] _Main2ndTexShouldCopy ("Copy", Int) = 0
        [ToggleUI] _Main2ndTexShouldFlipMirror ("Flip Mirror", Int) = 0
        [ToggleUI] _Main2ndTexShouldFlipCopy ("Flip Copy", Int) = 0
        [HideInInspector] _Main2ndTexDecalAnimation ("Decal Animation", Vector) = (1, 1, 1, 30)
        [HideInInspector] _Main2ndTexDecalSubParam ("Decal Sub-Parameters", Vector) = (1, 1, 0, 1)
        [ToggleUI] _Main2ndTexIsMSDF ("MSDF", Int) = 0
        [NoScaleOffset] _Main2ndBlendMask ("Mask", 2D) = "white" {}
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _Main2ndTexBlendMode ("Blend Mode", Int) = 0
        [Enum(None, 0, Replace, 1, Multiply, 2, Add, 3, Subtract, 4)] _Main2ndTexAlphaMode ("Alpha Mode", Int) = 0
        _Main2ndEnableLighting ("Enable Lighting", Range(0, 1)) = 1
        _Main2ndDissolveMask ("Dissolve Mask", 2D) = "white" {}
        [ToggleUI] _FT_Main2ndDissolveUseNoise ("Use Noise Helper (lilToon Feature)", Float) = 1
        _Main2ndDissolveNoiseMask ("Dissolve Noise Mask", 2D) = "gray" {}
        _Main2ndDissolveNoiseMask_ScrollRotate ("Dissolve Noise Scroll XY", Vector) = (0, 0, 0, 0)
        _Main2ndDissolveNoiseStrength ("Dissolve Noise Strength", Float) = 0.1
        [FairyToonHDR] _Main2ndDissolveColor ("Dissolve Edge Color", Color) = (1, 1, 1, 1)
        _Main2ndDissolveParams ("Dissolve: Mode, Shape, Threshold, Edge Width", Vector) = (0, 0, 0.5, 0.1)
        _Main2ndDissolvePos ("Dissolve Position / Direction", Vector) = (0, 0, 0, 0)

        // Main Color 3rd
        [ToggleUI] _UseMain3rdTex ("Main Color 3rd", Int) = 0
        [FairyToonHDR] _Color3rd ("Color", Color) = (1, 1, 1, 1)
        _Main3rdTex ("Texture", 2D) = "white" {}
        _Main3rdTexAngle ("Angle (radians)", Float) = 0
        _Main3rdTex_ScrollRotate ("Scroll XY / Rotation Speed W", Vector) = (0, 0, 0, 0)
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3, MatCap, 4)] _Main3rdTex_UVMode ("UV Mode", Int) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Main3rdTex_Cull ("Layer Cull", Int) = 0
        [ToggleUI] _Main3rdTexIsDecal ("Decal", Int) = 0
        [ToggleUI] _Main3rdTexIsLeftOnly ("Left Only", Int) = 0
        [ToggleUI] _Main3rdTexIsRightOnly ("Right Only", Int) = 0
        [ToggleUI] _Main3rdTexShouldCopy ("Copy", Int) = 0
        [ToggleUI] _Main3rdTexShouldFlipMirror ("Flip Mirror", Int) = 0
        [ToggleUI] _Main3rdTexShouldFlipCopy ("Flip Copy", Int) = 0
        [HideInInspector] _Main3rdTexDecalAnimation ("Decal Animation", Vector) = (1, 1, 1, 30)
        [HideInInspector] _Main3rdTexDecalSubParam ("Decal Sub-Parameters", Vector) = (1, 1, 0, 1)
        [ToggleUI] _Main3rdTexIsMSDF ("MSDF", Int) = 0
        [NoScaleOffset] _Main3rdBlendMask ("Mask", 2D) = "white" {}
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _Main3rdTexBlendMode ("Blend Mode", Int) = 0
        [Enum(None, 0, Replace, 1, Multiply, 2, Add, 3, Subtract, 4)] _Main3rdTexAlphaMode ("Alpha Mode", Int) = 0
        _Main3rdEnableLighting ("Enable Lighting", Range(0, 1)) = 1
        _Main3rdDissolveMask ("Dissolve Mask", 2D) = "white" {}
        [ToggleUI] _FT_Main3rdDissolveUseNoise ("Use Noise Helper (lilToon Feature)", Float) = 1
        _Main3rdDissolveNoiseMask ("Dissolve Noise Mask", 2D) = "gray" {}
        _Main3rdDissolveNoiseMask_ScrollRotate ("Dissolve Noise Scroll XY", Vector) = (0, 0, 0, 0)
        _Main3rdDissolveNoiseStrength ("Dissolve Noise Strength", Float) = 0.1
        [FairyToonHDR] _Main3rdDissolveColor ("Dissolve Edge Color", Color) = (1, 1, 1, 1)
        _Main3rdDissolveParams ("Dissolve: Mode, Shape, Threshold, Edge Width", Vector) = (0, 0, 0.5, 0.1)
        _Main3rdDissolvePos ("Dissolve Position / Direction", Vector) = (0, 0, 0, 0)

        // Lighting
        _LightMinLimit ("Light Min Limit", Range(0, 1)) = 0.05
        _LightMaxLimit ("Light Max Limit", Range(0, 10)) = 1
        _MonochromeLighting ("Monochrome Lighting", Range(0, 1)) = 0
        _AsUnlit ("As Unlit", Range(0, 1)) = 0

        // Shadow
        [ToggleUI] _UseShadow ("Shadow", Int) = 0
        [Enum(Strength, 0, Flat, 1, Face SDF, 2)] _ShadowMaskType ("Mask Type", Int) = 0
        _ShadowStrength ("Strength", Range(0, 1)) = 1
        [NoScaleOffset] _ShadowStrengthMask ("Strength Mask / Face SDF (R, G thresholds, B blend, A strength)", 2D) = "white" {}
        _ShadowColor ("Shadow Color", Color) = (0.82, 0.76, 0.85, 1.0)
        [NoScaleOffset] _ShadowColorTex ("Shadow Color Tex", 2D) = "black" {}
        _ShadowBorder ("Border", Range(0, 1)) = 0.5
        _ShadowBlur ("Blur", Range(0, 1)) = 0.1
        _ShadowNormalStrength ("Normal Strength", Range(0, 1)) = 1.0
        _ShadowReceive ("Receive Shadow", Range(0, 1)) = 0
        _Shadow2ndColor ("2nd Color (A = strength)", Color) = (0.68, 0.66, 0.79, 1)
        [NoScaleOffset] _Shadow2ndColorTex ("2nd Color Tex", 2D) = "black" {}
        _Shadow2ndBorder ("2nd Border", Range(0, 1)) = 0.15
        _Shadow2ndBlur ("2nd Blur", Range(0, 1)) = 0.1
        _Shadow2ndNormalStrength ("2nd Normal Strength", Range(0, 1)) = 1.0
        _Shadow2ndReceive ("2nd Receive Shadow", Range(0, 1)) = 0
        _Shadow3rdColor ("3rd Color (A = strength)", Color) = (0, 0, 0, 0)
        [NoScaleOffset] _Shadow3rdColorTex ("3rd Color Tex", 2D) = "black" {}
        _Shadow3rdBorder ("3rd Border", Range(0, 1)) = 0.25
        _Shadow3rdBlur ("3rd Blur", Range(0, 1)) = 0.1
        _Shadow3rdNormalStrength ("3rd Normal Strength", Range(0, 1)) = 1.0
        _Shadow3rdReceive ("3rd Receive Shadow", Range(0, 1)) = 0
        _ShadowMainStrength ("Multiply Main Color", Range(0, 1)) = 0
        _ShadowEnvStrength ("Environment Strength", Range(0, 1)) = 0
        _ShadowBorderColor ("Border Color", Color) = (1, 0.1, 0, 1)
        _ShadowBorderRange ("Border Range", Range(0, 1)) = 0.08
        [NoScaleOffset] _ShadowBorderMask ("AO Map", 2D) = "white" {}
        _ShadowAOShift ("AO Shift (1st scale, offset, 2nd scale, offset)", Vector) = (1, 0, 1, 0)
        _ShadowAOShift2 ("AO Shift (3rd scale, offset)", Vector) = (1, 0, 1, 0)
        [ToggleUI] _ShadowPostAO ("AO After Toon Step", Int) = 0
        [NoScaleOffset] _ShadowBlurMask ("Blur Mask", 2D) = "white" {}
        _BackfaceForceShadow ("Backface Force Shadow", Range(0, 1)) = 0
        _ShadowFlatBorder ("Flat Border / Face SDF -", Range(-2, 2)) = 1
        _ShadowFlatBlur ("Flat Blur / Face SDF Y Scale", Range(0.001, 2)) = 1
        [Toggle(_FT_SHADOW_RAMP)] _UseShadowRamp ("Ramp (FairyToon)", Int) = 0
        [NoScaleOffset] _ShadowRampTex ("Ramp (FairyToon)", 2D) = "white" {}
        [HideInInspector] _FaceForward ("Face Forward (FairyToon, set by FairyToonFaceShadow)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _FaceRight ("Face Right (FairyToon, set by FairyToonFaceShadow)", Vector) = (0, 0, 0, 0)
        _CharShadowReceive ("Character Shadow (FairyToon)", Range(0, 1)) = 1
        _SSAOStrength ("SSAO Ambient Strength (FairyToon)", Range(0, 1)) = 1
        _SSAOLitStrength ("SSAO Lit Strength (FairyToon)", Range(0, 1)) = 0.2
        [NoScaleOffset] _SSAOMask ("SSAO Mask (FairyToon)", 2D) = "white" {}

        // Endfield shading model (FairyToon): stylised PBR after Arknights: Endfield, replaces toon diffuse / shadow
        [Enum(lilToon, 0, Endfield, 1)] _ShadingModel ("Shading Model (FairyToon)", Int) = 0
        [Enum(Cloth, 0, Skin, 1, Face, 2, Hair, 3, Eye, 4)] _EFPart ("Part", Int) = 0
        [NoScaleOffset] _PBRMap ("PBR Map (R metallic, G reflectivity, B AO, A smoothness)", 2D) = "white" {}
        [HideInInspector] _EFPBRMapOn ("PBR Map On", Float) = 0
        _EFMetallic ("Metallic", Range(0, 1)) = 0
        _EFSmoothness ("Smoothness", Range(0, 1)) = 0.5
        _EFReflectivity ("Reflectivity", Range(0, 1)) = 1
        _EFOcclusion ("Occlusion", Range(0, 1)) = 1
        [NoScaleOffset] _DiffuseRamp ("Diffuse Ramp (RGB hue, A lit)", 2D) = "white" {}
        [HideInInspector] _EFRampOn ("Diffuse Ramp On", Float) = 0
        _EFShadowDarkness ("Shadow Tone", Range(0, 1)) = 0.35
        _EFShadowDarker ("Deep Shadow Tone", Range(0, 1)) = 0.65
        _EFShadowSaturation ("Shadow Saturation", Range(0, 1)) = 0.2
        _EFBacklight ("Backlight Lift", Range(0, 1)) = 1
        [NoScaleOffset] _SpecRamp ("Specular Ramp", 2D) = "white" {}
        _EFSpecRampFresnel ("Specular Ramp: Shape / Fresnel", Range(0, 1)) = 0
        _EFSpecular ("Specular", Range(0, 2)) = 1
        _EFEnvironment ("Environment Reflection", Range(0, 2)) = 1
        _EFRimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _EFRimStrength ("Rim", Range(0, 1)) = 0.3
        _EFRimWidth ("Rim Width", Range(0, 1)) = 0.5
        _EFRimAlbedo ("Rim Albedo", Range(0, 1)) = 0.5
        _EFLightRim ("Light Rim", Range(0, 1)) = 0.3
        _EFRimDepth ("Depth Edge Rim", Range(0, 1)) = 0
        _EFRimDepthWidth ("Depth Edge Width (px at 1080p)", Range(0.5, 8)) = 3
        _EFRimDepthGap ("Depth Edge Gap (m)", Range(0.01, 0.5)) = 0.12
        [NoScaleOffset] _SkinLUT ("Skin Shadow LUT (32 slices of 32 x 32)", 2D) = "white" {}
        [HideInInspector] _EFSkinLUTOn ("Skin LUT On", Float) = 0
        _EFSSSColor ("SSS Color", Color) = (1, 0.7, 0.62, 1)
        _EFSSSStrength ("SSS", Range(0, 1)) = 0.3
        _EFFaceSDFScale ("SDF Light Scale", Range(0.5, 1)) = 0.75
        _EFMultiBounceAO ("Multi-Bounce AO", Range(0, 1)) = 0
        _EFContactAO ("Character Contact AO", Range(0, 1)) = 0
        _EFContactAORadius ("Contact AO Radius (m)", Range(0.01, 0.15)) = 0.045
        _EFContactAOBias ("Contact AO Bias (m)", Range(0.001, 0.02)) = 0.004
        _EFCurvature ("Curvature Detail", Range(0, 1)) = 0
        _EFCurvatureScale ("Curvature Scale", Range(0.5, 8)) = 2.5
        _EFEnvContrast ("Metal Env Contrast", Range(0, 2)) = 0
        _EFMetalGrazingColor ("Metal Grazing Tint", Color) = (1, 1, 1, 0)
        _EFFabricSheen ("Fabric Sheen", Range(0, 1)) = 0
        _EFClearcoat ("Clearcoat", Range(0, 1)) = 0
        _EFClearcoatSmoothness ("Clearcoat Smoothness", Range(0, 1)) = 0.9
        [NoScaleOffset] _EFEyeMask ("Eye Mask (R)", 2D) = "black" {}
        _EFEyeLight ("Eye Backlight Dimming", Range(0, 1)) = 0.3
        [NoScaleOffset] _EFEyeOverlay ("Eye Overlay (R lid shadow, G iris scatter)", 2D) = "black" {}
        _EFEyeShadowColor ("Lid Shadow Colour", Color) = (0.75, 0.75, 0.85, 1)
        [HDR] _EFEyeScatterColor ("Iris Scatter Colour", Color) = (1, 1, 1, 1)
        [Toggle] _EFIris ("3D Iris", Float) = 0
        [NoScaleOffset] _EFIrisFrame ("Iris Frame (linear RG position, B UV scale, A code)", 2D) = "black" {}
        [ToggleUI] _EFPupilLayer ("Pupil Layer", Float) = 1
        _EFPupilDepth ("Pupil Depth", Range(0, 1)) = 0.5
        _EFIrisWorldRadius ("Iris World Radius", Float) = 0.01233
        _EFIrisDish ("Iris Dish", Range(0, 1)) = 0.24
        _EFIrisDepth ("Iris Bowl Depth", Range(0, 1)) = 0.3
        _EFIrisScale ("Iris Art Scale", Range(0.3, 1.5)) = 0.72
        _EFIrisRim ("Fixed Rim", Range(0, 1.5)) = 1.5
        _EFIrisEdgeFade ("Edge Fade", Range(0, 1)) = 0
        _EFIrisVertical ("Vertical Strength", Range(0, 1)) = 1
        _EFIrisMaxShift ("Max Shift", Range(0.05, 2)) = 2
        _EFIrisGaze ("Gaze Frame (0 mesh normal, 1 head forward)", Range(0, 1)) = 1
        _EFCorneaBulge ("Cornea Bulge", Range(0, 1)) = 0.2
        _EFCorneaNormalRange ("Cornea Normal Range", Range(0, 1)) = 0.4
        _EFIrisRefraction ("Iris Refraction", Range(0, 2)) = 1
        _EFIrisEdge ("Pattern Edge", Range(0.5, 1)) = 0.96
        _EFIrisEdgeSoftness ("Edge Softness", Range(0, 1)) = 0.4
        _EFIrisCenterL ("Iris Centre L", Vector) = (0.15347, 0.82393, 0, 0)
        _EFIrisCenterR ("Iris Centre R", Vector) = (0.84653, 0.82393, 0, 0)
        _EFIrisRadius ("Iris X Radius", Float) = 0.13507
        _EFPupil ("Pupil Fold", Range(0, 1)) = 1
        _EFPupilCenterL ("Pupil Centre L", Vector) = (0.34669, 0.86296, 0, 0)
        _EFPupilCenterR ("Pupil Centre R", Vector) = (0.65331, 0.86296, 0, 0)
        _EFPupilRadius ("Pupil X Radius", Float) = 0.03715
        _EFPupilYRatio ("Pupil Y / X", Float) = 1.27
        _EFPupilScale ("Pupil Scale in Iris Units", Float) = 0.2068
        _EFPupilOffset ("Pupil Offset (left iris units)", Vector) = (0.2342, -0.0708, 0, 0)
        [NoScaleOffset] _EFIrisSparkleTex ("Cornea Matcap (RGB light, A glow)", 2D) = "black" {}
        _EFIrisSparkle ("Reflection Strength", Range(0, 4)) = 1
        _EFIrisGlowStrength ("Glow Strength", Range(0, 2)) = 1
        _EFIrisSparkleScale ("Cornea Matcap Scale", Float) = 3
        _EFCorneaIOR ("Cornea IOR", Float) = 8
        [HDR] _EFIrisGlowColor ("Iris Glow Colour", Color) = (0.3645, 0.8106, 1.2403, 1)
        [NoScaleOffset] _EFFaceHighlight ("Face Highlight (R)", 2D) = "black" {}
        [HDR] _EFFaceHighlightColor ("Face Highlight Colour", Color) = (0.4, 0.38, 0.36, 1)
        _EFFaceHighlightShift ("Face Highlight View Shift", Range(-0.05, 0.05)) = 0.01
        [NoScaleOffset] _HairMask ("Hair Mask (R highlight, G strand noise, B AO)", 2D) = "white" {}
        _EFHairCentre ("Head Centre Height", Range(0, 0.3)) = 0.08
        _EFHairFlatten ("Camera-Right Flatten", Range(0, 1)) = 1
        _EFHairBandOffset ("Band Height", Range(-1, 2)) = 0.6
        _EFHairLightFollow ("Band Follows Light", Range(0, 1)) = 0.2
        _EFHairShift ("Primary Shift", Range(-1, 1)) = 0
        _EFHairExponent ("Primary Sharpness", Range(1, 512)) = 120
        _EFHairSpecular ("Primary Strength", Range(0, 2)) = 0.6
        _EFHairTopCut ("Primary Top Cut", Range(0, 1)) = 0.5
        _EFHairShift2 ("Secondary Shift", Range(-1, 1)) = -0.15
        _EFHairExponent2 ("Secondary Sharpness", Range(1, 512)) = 16
        _EFHairSpecular2 ("Secondary Strength", Range(0, 2)) = 0.25
        _EFHairColor2 ("Secondary Colour (A: 0 hair colour, 1 this colour)", Color) = (1, 1, 1, 0)
        _EFHairJitter ("Strand Jitter", Range(0, 1)) = 0.3
        _EFHairHalo ("Back Halo", Range(0, 1)) = 0.3
        _EFHairRootAO ("Root Occlusion", Range(0, 1)) = 0
        _EFHairRootInner ("Root Inner (m from head centre)", Range(0, 0.3)) = 0.07
        _EFHairRootOuter ("Root Outer (m from head centre)", Range(0, 0.4)) = 0.11
        _EFHairRootLift ("Root Lift (m above the hair centre)", Range(0, 0.3)) = 0
        _EFHairStrandBreakup ("Strand Breakup", Range(0, 2)) = 0
        _EFHairStrandBlur ("Breakup Blur (mip levels)", Range(1, 6)) = 3
        _EFHairStrandLines ("Strand Lines", Range(0, 0.5)) = 0
        _EFHairStrandFreq ("Strand Lines Density (per UV unit)", Range(20, 2000)) = 400
        _EFHairRootColor ("Root Colour (multiplier)", Color) = (0.75, 0.58, 0.47, 1)

        // Normal Map
        [Toggle(_NORMALMAP)] _UseBumpMap ("Normal Map", Int) = 0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Scale", Range(-10, 10)) = 1

        // Normal Map 2nd
        [ToggleUI] _UseBump2ndMap ("Normal Map 2nd", Int) = 0
        [Normal] _Bump2ndMap ("Normal Map", 2D) = "bump" {}
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3)] _Bump2ndMap_UVMode ("UV Mode", Int) = 0
        _Bump2ndScale ("Scale", Range(-10, 10)) = 1
        _Bump2ndScaleMask ("Mask", 2D) = "white" {}

        // Anisotropy
        [Toggle(_FT_ANISOTROPY)] _UseAnisotropy ("Anisotropy", Int) = 0
        [Normal] _AnisotropyTangentMap ("Tangent Map", 2D) = "bump" {}
        _AnisotropyScale ("Scale", Range(-1, 1)) = 1
        _AnisotropyScaleMask ("Scale Mask", 2D) = "white" {}
        _AnisotropyTangentWidth ("Tangent Width", Range(0, 10)) = 1
        _AnisotropyBitangentWidth ("Bitangent Width", Range(0, 10)) = 1
        _AnisotropyShift ("Offset", Range(-10, 10)) = 0
        _AnisotropyShiftNoiseScale ("Noise Strength", Range(-1, 1)) = 0
        _AnisotropySpecularStrength ("Strength", Range(0, 10)) = 1
        _Anisotropy2ndTangentWidth ("2nd Tangent Width", Range(0, 10)) = 1
        _Anisotropy2ndBitangentWidth ("2nd Bitangent Width", Range(0, 10)) = 1
        _Anisotropy2ndShift ("2nd Offset", Range(-10, 10)) = 0
        _Anisotropy2ndShiftNoiseScale ("2nd Noise Strength", Range(-1, 1)) = 0
        _Anisotropy2ndSpecularStrength ("2nd Strength", Range(0, 10)) = 0
        _AnisotropyShiftNoiseMask ("Noise", 2D) = "white" {}
        [ToggleUI] _Anisotropy2Reflection ("Apply to Reflection", Int) = 0
        [ToggleUI] _Anisotropy2MatCap ("Apply to MatCap", Int) = 0
        [ToggleUI] _Anisotropy2MatCap2nd ("Apply to MatCap 2nd", Int) = 0

        // Backlight
        [Toggle(_FT_BACKLIGHT)] _UseBacklight ("Backlight", Int) = 0
        [FairyToonHDR] _BacklightColor ("Color", Color) = (0.85, 0.8, 0.7, 1.0)
        _BacklightColorTex ("Texture", 2D) = "white" {}
        _BacklightMainStrength ("Multiply Main Color", Range(0, 1)) = 0
        _BacklightNormalStrength ("Normal Strength", Range(0, 1)) = 1.0
        _BacklightBorder ("Border", Range(0, 1)) = 0.35
        _BacklightBlur ("Blur", Range(0, 1)) = 0.05
        _BacklightDirectivity ("Directivity", Float) = 5.0
        _BacklightViewStrength ("View Direction Strength", Range(0, 1)) = 1
        [ToggleUI] _BacklightReceiveShadow ("Receive Shadow", Int) = 1
        [ToggleUI] _BacklightBackfaceMask ("Backface Mask", Int) = 1

        // Reflection (off by default)
        [Toggle(_FT_REFLECTION)] _UseReflection ("Reflection", Int) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 1
        _SmoothnessTex ("Smoothness", 2D) = "white" {}
        [Gamma] _Metallic ("Metallic", Range(0, 1)) = 0
        _MetallicGlossMap ("Metallic", 2D) = "white" {}
        [Gamma] _Reflectance ("Reflectance", Range(0, 1)) = 0.04
        _GSAAStrength ("GSAA", Range(0, 1)) = 0
        [ToggleUI] _ApplySpecular ("Apply Specular", Int) = 1
        [ToggleUI] _SpecularToon ("Specular Toon", Int) = 1
        _SpecularNormalStrength ("Specular Normal Strength", Range(0, 1)) = 1.0
        _SpecularBorder ("Specular Border", Range(0, 1)) = 0.5
        _SpecularBlur ("Specular Blur", Range(0, 1)) = 0.0
        [ToggleUI] _ApplyReflection ("Apply Reflection", Int) = 0
        _ReflectionNormalStrength ("Reflection Normal Strength", Range(0, 1)) = 1.0
        [HDR] _ReflectionColor ("Color", Color) = (1, 1, 1, 1)
        _ReflectionColorTex ("Color Tex", 2D) = "white" {}
        [ToggleUI] _ReflectionApplyTransparency ("Apply Transparency", Int) = 1
        [NoScaleOffset] _ReflectionCubeTex ("Cubemap Fallback", Cube) = "black" {}
        [HDR] _ReflectionCubeColor ("Cubemap Color", Color) = (0, 0, 0, 1)
        _ReflectionCubeEnableLighting ("Cubemap Enable Lighting", Range(0, 1)) = 1
        [ToggleUI] _ReflectionCubeOverride ("Cubemap Override", Int) = 0
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _ReflectionBlendMode ("Blend Mode", Int) = 1

        // MatCap
        [Toggle(_FT_MATCAP)] _UseMatCap ("MatCap", Int) = 0
        _MatCapColor ("Color", Color) = (1, 1, 1, 1)
        _MatCapTex ("Texture", 2D) = "white" {}
        _MatCapBlend ("Blend", Range(0, 1)) = 1
        _MatCapBlendMask ("Mask", 2D) = "white" {}
        _MatCapEnableLighting ("Enable Lighting", Range(0, 1)) = 1
        _MatCapShadowMask ("Shadow Mask", Range(0, 1)) = 0
        [ToggleUI] _MatCapBackfaceMask ("Backface Mask", Int) = 0
        _MatCapLod ("Blur", Range(0, 10)) = 0
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _MatCapBlendMode ("Blend Mode", Int) = 1
        [ToggleUI] _MatCapApplyTransparency ("Apply Transparency", Int) = 1
        _MatCapNormalStrength ("Normal Strength", Range(0, 1)) = 1.0
        [ToggleUI] _MatCapCustomNormal ("Custom Normal", Int) = 0
        [Normal] _MatCapBumpMap ("Normal Map", 2D) = "bump" {}
        _MatCapBumpScale ("Scale", Range(-10, 10)) = 1
        [ToggleUI] _MatCapZRotCancel ("Z-Rotation Cancel", Int) = 1
        [ToggleUI] _MatCapPerspective ("Fix Perspective", Int) = 1
        _MatCapMainStrength ("Multiply Main Color", Range(0, 1)) = 0
        [ToggleUI] _UseMatCap2nd ("MatCap 2nd", Int) = 0
        _MatCap2ndColor ("2nd Color", Color) = (1, 1, 1, 1)
        _MatCap2ndTex ("2nd Texture", 2D) = "white" {}
        _MatCap2ndBlend ("2nd Blend", Range(0, 1)) = 1
        _MatCap2ndBlendMask ("2nd Mask", 2D) = "white" {}
        _MatCap2ndEnableLighting ("2nd Enable Lighting", Range(0, 1)) = 1
        _MatCap2ndShadowMask ("2nd Shadow Mask", Range(0, 1)) = 0
        [ToggleUI] _MatCap2ndBackfaceMask ("2nd Backface Mask", Int) = 0
        _MatCap2ndLod ("2nd Blur", Range(0, 10)) = 0
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _MatCap2ndBlendMode ("2nd Blend Mode", Int) = 1
        [ToggleUI] _MatCap2ndApplyTransparency ("2nd Apply Transparency", Int) = 1
        _MatCap2ndNormalStrength ("2nd Normal Strength", Range(0, 1)) = 1.0
        [ToggleUI] _MatCap2ndCustomNormal ("2nd Custom Normal", Int) = 0
        [Normal] _MatCap2ndBumpMap ("2nd Normal Map", 2D) = "bump" {}
        _MatCap2ndBumpScale ("2nd Scale", Range(-10, 10)) = 1
        [ToggleUI] _MatCap2ndZRotCancel ("2nd Z-Rotation Cancel", Int) = 1
        [ToggleUI] _MatCap2ndPerspective ("2nd Fix Perspective", Int) = 1
        _MatCap2ndMainStrength ("2nd Multiply Main Color", Range(0, 1)) = 0

        // Rim Shade (lilToon: darkens the albedo before lighting)
        [Toggle(_FT_RIMSHADE)] _UseRimShade ("Rim Shade", Int) = 0
        _RimShadeColor ("Color", Color) = (0.5, 0.5, 0.5, 1.0)
        [NoScaleOffset] _RimShadeMask ("Mask", 2D) = "white" {}
        _RimShadeNormalStrength ("Normal Strength", Range(0, 1)) = 1.0
        _RimShadeBorder ("Border", Range(0, 1)) = 0.5
        _RimShadeBlur ("Blur", Range(0, 1)) = 1.0
        [PowerSlider(3.0)] _RimShadeFresnelPower ("Fresnel Power", Range(0.01, 50)) = 1.0

        // Rim Light
        [Toggle(_FT_RIM)] _UseRim ("Rim Light", Int) = 0
        _RimColor ("Color", Color) = (0.66, 0.5, 0.48, 1)
        _RimColorTex ("Texture", 2D) = "white" {}
        _RimMainStrength ("Multiply Main Color", Range(0, 1)) = 0
        _RimNormalStrength ("Normal Strength", Range(0, 1)) = 1.0
        _RimBorder ("Border", Range(0, 1)) = 0.5
        _RimBlur ("Blur", Range(0, 1)) = 0.65
        [PowerSlider(3.0)] _RimFresnelPower ("Fresnel Power", Range(0.01, 50)) = 3.5
        _RimEnableLighting ("Enable Lighting", Range(0, 1)) = 1
        _RimShadowMask ("Shadow Mask", Range(0, 1)) = 0.5
        [ToggleUI] _RimBackfaceMask ("Backface Mask", Int) = 1
        [ToggleUI] _RimApplyTransparency ("Apply Transparency", Int) = 1
        _RimDirStrength ("Light Direction Strength", Range(0, 1)) = 0
        _RimDirRange ("Direction Range", Range(-1, 1)) = 0
        _RimIndirRange ("Indirection Range", Range(-1, 1)) = 0
        _RimIndirColor ("Indirection Color", Color) = (1, 1, 1, 1)
        _RimIndirBorder ("Indirection Border", Range(0, 1)) = 0.5
        _RimIndirBlur ("Indirection Blur", Range(0, 1)) = 0.1
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _RimBlendMode ("Blend Mode", Int) = 1

        // Glitter
        [ToggleUI] _UseGlitter ("Glitter", Int) = 0
        [Enum(UV0, 0, UV1, 1)] _GlitterUVMode ("UV Mode", Int) = 0
        [FairyToonHDR] _GlitterColor ("Color", Color) = (1, 1, 1, 1)
        _GlitterColorTex ("Texture", 2D) = "white" {}
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3)] _GlitterColorTex_UVMode ("Color Texture UV Mode", Int) = 0
        _GlitterMainStrength ("Multiply Main Color", Range(0, 1)) = 0
        _GlitterNormalStrength ("Normal Strength", Range(0, 1)) = 1.0
        _GlitterScaleRandomize ("Randomize Size", Range(0, 1)) = 0
        [ToggleUI] _GlitterApplyShape ("Shape", Int) = 0
        _GlitterShapeTex ("Shape Texture", 2D) = "white" {}
        _GlitterAtras ("Shape Atlas Columns X / Rows Y", Vector) = (1, 1, 0, 0)
        [ToggleUI] _GlitterAngleRandomize ("Randomize Angle", Int) = 0
        _GlitterParams1 ("Tiling XY / Particle Size Squared Z / Contrast W", Vector) = (256, 256, 0.16, 50)
        _GlitterParams2 ("Blink Speed X / Angle Limit Y / Light Direction Z / Color Randomness W", Vector) = (0.25, 0, 0, 0)
        _GlitterPostContrast ("Post Contrast", Float) = 1
        _GlitterSensitivity ("Sensitivity", Float) = 0.25
        _GlitterEnableLighting ("Enable Lighting", Range(0, 1)) = 1
        _GlitterShadowMask ("Shadow Mask", Range(0, 1)) = 0
        [ToggleUI] _GlitterBackfaceMask ("Backface Mask", Int) = 0
        [ToggleUI] _GlitterApplyTransparency ("Apply Transparency", Int) = 1
        _GlitterVRParallaxStrength ("VR Parallax Strength", Range(0, 1)) = 0

        // Emission 1st
        [ToggleUI] _UseEmission ("Emission 1st", Int) = 0
        [HDR] _EmissionColor ("Color", Color) = (1, 1, 1, 1)
        _EmissionMap ("Texture", 2D) = "white" {}
        _EmissionMap_ScrollRotate ("Scroll XY / Angle Z / Rotation Speed W", Vector) = (0, 0, 0, 0)
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3, Rim, 4)] _EmissionMap_UVMode ("UV Mode", Int) = 0
        _EmissionBlend ("Blend", Range(0, 1)) = 1
        _EmissionBlendMask ("Mask", 2D) = "white" {}
        _EmissionBlendMask_ScrollRotate ("Mask Scroll XY / Angle Z / Rotation Speed W", Vector) = (0, 0, 0, 0)
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _EmissionBlendMode ("Blend Mode", Int) = 1
        _EmissionMainStrength ("Multiply Main Color", Range(0, 1)) = 0
        _EmissionBlink ("Blink Strength / Hard / Speed / Phase", Vector) = (0, 0, 3.141593, 0)
        [ToggleUI] _EmissionUseGrad ("Gradation", Int) = 0
        [NoScaleOffset] _EmissionGradTex ("Gradation Texture", 2D) = "white" {}
        _EmissionGradSpeed ("Gradation Speed", Float) = 1
        _EmissionFluorescence ("Fluorescence", Range(0, 1)) = 0

        // Emission 2nd
        [ToggleUI] _UseEmission2nd ("Emission 2nd", Int) = 0
        [HDR] _Emission2ndColor ("Color", Color) = (1, 1, 1, 1)
        _Emission2ndMap ("Texture", 2D) = "white" {}
        _Emission2ndMap_ScrollRotate ("Scroll XY / Angle Z / Rotation Speed W", Vector) = (0, 0, 0, 0)
        [Enum(UV0, 0, UV1, 1, UV2, 2, UV3, 3, Rim, 4)] _Emission2ndMap_UVMode ("UV Mode", Int) = 0
        _Emission2ndBlend ("Blend", Range(0, 1)) = 1
        _Emission2ndBlendMask ("Mask", 2D) = "white" {}
        _Emission2ndBlendMask_ScrollRotate ("Mask Scroll XY / Angle Z / Rotation Speed W", Vector) = (0, 0, 0, 0)
        [Enum(Normal, 0, Add, 1, Screen, 2, Multiply, 3)] _Emission2ndBlendMode ("Blend Mode", Int) = 1
        _Emission2ndMainStrength ("Multiply Main Color", Range(0, 1)) = 0
        _Emission2ndBlink ("Blink Strength / Hard / Speed / Phase", Vector) = (0, 0, 3.141593, 0)
        [ToggleUI] _Emission2ndUseGrad ("Gradation", Int) = 0
        [NoScaleOffset] _Emission2ndGradTex ("Gradation Texture", 2D) = "white" {}
        _Emission2ndGradSpeed ("Gradation Speed", Float) = 1
        _Emission2ndFluorescence ("Fluorescence", Range(0, 1)) = 0

        // Outline
        [Toggle(_FT_OUTLINE)] _UseOutline ("Outline", Int) = 0
        _OutlineColor ("Color", Color) = (0.6, 0.56, 0.73, 1)
        _OutlineTex ("Texture", 2D) = "white" {}
        _OutlineWidth ("Width (cm)", Range(0, 1)) = 0.08
        [NoScaleOffset] _OutlineWidthMask ("Width Mask", 2D) = "white" {}
        _OutlineFixWidth ("Fix Width", Range(0, 1)) = 0.5
        [Enum(Normal, 0, Vertex R Width, 1, Vertex A Width, 2, UV7 Smoothed Normal, 3)] _OutlineVertexR2Width ("Vertex Color / Normal Source", Int) = 0
        _OutlineZBias ("Z Bias", Float) = 0
        _OutlineEnableLighting ("Enable Lighting", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _OutlineCull ("Outline Cull", Int) = 1
        [Enum(Object, 0, Screen, 1)] _OutlineMode ("Width Mode (FairyToon)", Int) = 0
        _OutlinePixelWidth ("Screen Width (px at 1080p)", Range(0, 8)) = 1.5
        _OutlineMinPixels ("Minimum Width (px)", Range(0, 3)) = 1
        _OutlineFadeStart ("Fade Start (m)", Float) = 3
        _OutlineFadeEnd ("Fade End (m, 0 = off)", Float) = 6
        _OutlineCornerTaper ("Corner Taper (UV7 coherence)", Range(0, 1)) = 0.5
        _EFOutlineColor ("Endfield Outline Tint", Color) = (0.8, 0.7, 0.7, 1)
        _EFOutlineSaturation ("Endfield Outline Saturation", Range(0, 4)) = 2
        _EFOutlineEdge ("Endfield Edge Tint (A strength)", Color) = (1, 0.92, 0.88, 0)
        _EFOutlineFlatten ("Endfield Outline Flatten (contrast squeeze to mid grey)", Range(0, 0.5)) = 0
        _EFOutlinePull ("Endfield Outline Tint Pull (A amount)", Color) = (0.42, 0.36, 0.38, 0)

        // See-through hair (FairyToon; after SeeThroughHair by goorm)
        [Enum(None, 0, Face, 1, Hair, 2)] _SeeThroughRole ("Role", Float) = 0
        [NoScaleOffset] _SeeThroughMask ("Mask", 2D) = "black" {}
        [Enum(R, 0, G, 1, B, 2, A, 3)] _SeeThroughMaskChannel ("Channel", Float) = 0
        _SeeThroughMaskCutoff ("Cutoff", Range(0, 1)) = 0.5
        _SeeThroughOpacity ("Opacity", Range(0, 1)) = 0.7
        [ToggleUI] _SeeThroughEarlyZ ("Early Z", Float) = 1
        [ToggleUI] _SeeThroughFade ("View Angle Fade", Float) = 1
        _SeeThroughFadeSideAlpha ("Side Alpha", Range(0, 1)) = 1
        _SeeThroughFadeHorizontalMin ("Horizontal Start", Range(0, 180)) = 40
        _SeeThroughFadeHorizontalMax ("Horizontal End", Range(0, 180)) = 70
        _SeeThroughFadeVerticalMin ("Vertical Start", Range(0, 90)) = 30
        _SeeThroughFadeVerticalMax ("Vertical End", Range(0, 90)) = 50
        [HideInInspector] _SeeThroughHead ("Head", Vector) = (0, 0, 0, 0)
        [ToggleUI] _UseFakeShadow ("Fake Shadow", Float) = 0
        _FakeShadowColor ("Color", Color) = (1, 0.88, 0.88, 1)
        _FakeShadowVector ("Vector", Vector) = (0.2, -0.2, 0, 0.004)

        // Stencil
        [IntRange] _StencilRef ("Ref", Range(0, 255)) = 0
        [IntRange] _StencilReadMask ("ReadMask", Range(0, 255)) = 255
        [IntRange] _StencilWriteMask ("WriteMask", Range(0, 255)) = 255
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Compare Function", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Pass", Float) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilFail ("Fail", Float) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilZFail ("ZFail", Float) = 0
        // lilToon Fur controls
                        _FurNoiseMask               ("Noise", 2D) = "white" {}
        [NoScaleOffset] _FurMask                    ("Mask", 2D) = "white" {}
        [NoScaleOffset] _FurLengthMask              ("Length Mask", 2D) = "white" {}
        [NoScaleOffset][Normal] _FurVectorTex       ("Vector", 2D) = "bump" {}
                        _FurVectorScale             ("Vector scale", Range(-10,10)) = 1
          _FurVector                  ("sFurVectors", Vector) = (0.0,0.0,1.0,0.02)
        [ToggleUI]     _VertexColor2FurVector      ("sVertexColor2Vector", Int) = 0
                        _FurGravity                 ("sGravity", Range(0,1)) = 0.25
                        _FurRandomize               ("sRandomize", Float) = 0
                        _FurAO                      ("sAO", Range(0,1)) = 0
        [IntRange]      _FurLayerNum                ("sLayerNum", Range(1, 3)) = 2
                        _FurRootOffset              ("sRootWidth", Range(-1,0)) = 0
                        _FurCutoutLength            ("sLength+ (Cutout)", Float) = 0.8
                        _FurTouchStrength           ("sTouchStrength", Range(0, 1)) = 0
                        _FurRimColor                ("sColor", Color) = (0.0,0.0,0.0,1.0)
        [PowerSlider(3.0)]_FurRimFresnelPower       ("sFresnelPower", Range(0.01, 50)) = 3.0
                        _FurRimAntiLight            ("sAntiLight", Range(0, 1)) = 0.5
                                               _FurCull                ("sCullModes", Int) = 0
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurSrcBlend            ("sSrcBlendRGB", Int) = 5
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurDstBlend            ("sDstBlendRGB", Int) = 10
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurSrcBlendAlpha       ("sSrcBlendAlpha", Int) = 1
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurDstBlendAlpha       ("sDstBlendAlpha", Int) = 10
        [Enum(UnityEngine.Rendering.BlendOp)]           _FurBlendOp             ("sBlendOpRGB", Int) = 0
        [Enum(UnityEngine.Rendering.BlendOp)]           _FurBlendOpAlpha        ("sBlendOpAlpha", Int) = 0
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurSrcBlendFA          ("sSrcBlendRGB", Int) = 1
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurDstBlendFA          ("sDstBlendRGB", Int) = 1
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurSrcBlendAlphaFA     ("sSrcBlendAlpha", Int) = 0
        [Enum(UnityEngine.Rendering.BlendMode)]         _FurDstBlendAlphaFA     ("sDstBlendAlpha", Int) = 1
        [Enum(UnityEngine.Rendering.BlendOp)]           _FurBlendOpFA           ("sBlendOpRGB", Int) = 4
        [Enum(UnityEngine.Rendering.BlendOp)]           _FurBlendOpAlphaFA      ("sBlendOpAlpha", Int) = 4
        [ToggleUI]                                     _FurZClip               ("sZClip", Int) = 1
        [ToggleUI]                                     _FurZWrite              ("sZWrite", Int) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)]   _FurZTest               ("sZTest", Int) = 4
        [IntRange]                                      _FurStencilRef          ("Ref", Range(0, 255)) = 0
        [IntRange]                                      _FurStencilReadMask     ("ReadMask", Range(0, 255)) = 255
        [IntRange]                                      _FurStencilWriteMask    ("WriteMask", Range(0, 255)) = 255
        [Enum(UnityEngine.Rendering.CompareFunction)]   _FurStencilComp         ("Comp", Float) = 8
        [Enum(UnityEngine.Rendering.StencilOp)]         _FurStencilPass         ("Pass", Float) = 0
        [Enum(UnityEngine.Rendering.StencilOp)]         _FurStencilFail         ("Fail", Float) = 0
        [Enum(UnityEngine.Rendering.StencilOp)]         _FurStencilZFail        ("ZFail", Float) = 0
                                                        _FurOffsetFactor        ("sOffsetFactor", Float) = 0
                                                        _FurOffsetUnits         ("sOffsetUnits", Float) = 0
                                          _FurColorMask           ("sColorMask", Int) = 15
        [ToggleUI]                                     _FurAlphaToMask         ("sAlphaToMask", Int) = 0
        [Enum(Transparent, 0, Cutout, 1, 2 Pass, 2)] _FurMode ("Fur Mode", Int) = 0
        _SubpassCutoff ("Subpass Cutoff", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #pragma target 5.0
        #define FAIRYTOON_FUR 1
        #pragma only_renderers d3d11 vulkan
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull [_Cull]
            ZWrite [_ZWrite]
            ZTest [_ZTest]
            Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
            AlphaToMask [_AlphaToMask]
            Offset [_OffsetFactor], [_OffsetUnits]
            Stencil
            {
                Ref [_StencilRef]
                ReadMask [_StencilReadMask]
                WriteMask [_StencilWriteMask]
                Comp [_StencilComp]
                Pass [_StencilPass]
                Fail [_StencilFail]
                ZFail [_StencilZFail]
            }

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonForwardVertex
            #pragma fragment FairyToonForwardFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _FT_NORMAL2ND _FT_NORMAL2ND_UV
            #pragma shader_feature_local_fragment _FT_ANISOTROPY
            #pragma shader_feature_local_fragment _FT_BACKLIGHT
            #pragma shader_feature_local _ _FT_GLITTER _FT_GLITTER_UV
            #pragma shader_feature_local_fragment _FT_SHADOW_RAMP
            #pragma shader_feature_local_fragment _FT_REFLECTION
            #pragma shader_feature_local_fragment _FT_MATCAP
            #pragma shader_feature_local_fragment _FT_RIM
            #pragma shader_feature_local_fragment _FT_RIMSHADE
            #pragma shader_feature_local _FT_EMISSION
            #pragma shader_feature_local_fragment _FT_ENDFIELD
            #pragma shader_feature_local_fragment _ _FT_EF_SKIN _FT_EF_FACE _FT_EF_HAIR _FT_EF_EYE
            #pragma shader_feature_local _ _FT_EF_IRIS

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #define FAIRYTOON_PASS_FORWARD
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "FurForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull [_FurCull]
            ZWrite [_FurZWrite]
            ZTest [_FurZTest]
            Blend [_FurSrcBlend] [_FurDstBlend], [_FurSrcBlendAlpha] [_FurDstBlendAlpha]
            BlendOp [_FurBlendOp], [_FurBlendOpAlpha]
            ZClip [_FurZClip]
            ColorMask [_FurColorMask]
            AlphaToMask [_FurAlphaToMask]
            Offset [_FurOffsetFactor], [_FurOffsetUnits]
            Stencil
            {
                Ref [_FurStencilRef]
                ReadMask [_FurStencilReadMask]
                WriteMask [_FurStencilWriteMask]
                Comp [_FurStencilComp]
                Pass [_FurStencilPass]
                Fail [_FurStencilFail]
                ZFail [_FurStencilZFail]
            }

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonFurVertex
            #pragma shader_feature_local _ _FT_EF_IRIS
            #pragma geometry FairyToonFurGeometry
            #pragma fragment FairyToonFurFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS

            #pragma shader_feature_local_fragment _FT_SHADOW_RAMP

            #pragma shader_feature_local_fragment _FT_RIMSHADE

            #pragma shader_feature_local_fragment _FT_ENDFIELD

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #define FAIRYTOON_FUR_SHELL 1
            #define FAIRYTOON_PASS_FORWARD
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonFur.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "FurSecond"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull [_FurCull]
            ZWrite Off
            ZTest [_FurZTest]
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            BlendOp [_FurBlendOp], [_FurBlendOpAlpha]
            ZClip [_FurZClip]
            ColorMask [_FurColorMask]
            AlphaToMask Off
            Offset [_FurOffsetFactor], [_FurOffsetUnits]
            Stencil
            {
                Ref [_FurStencilRef]
                ReadMask [_FurStencilReadMask]
                WriteMask [_FurStencilWriteMask]
                Comp [_FurStencilComp]
                Pass [_FurStencilPass]
                Fail [_FurStencilFail]
                ZFail [_FurStencilZFail]
            }

            HLSLPROGRAM
            #define _SURFACE_TYPE_TRANSPARENT 1
            #pragma vertex FairyToonFurVertex
            #pragma shader_feature_local _ _FT_EF_IRIS
            #pragma geometry FairyToonFurGeometry
            #pragma fragment FairyToonFurFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS

            #pragma shader_feature_local_fragment _FT_SHADOW_RAMP

            #pragma shader_feature_local_fragment _FT_RIMSHADE

            #pragma shader_feature_local_fragment _FT_ENDFIELD

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS

            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ProbeVolumeVariants.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #define FAIRYTOON_FUR_SECOND 1
            #define FAIRYTOON_FUR_SHELL 1
            #define FAIRYTOON_PASS_FORWARD
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonFur.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonShadowVertex
            #pragma fragment FairyToonShadowFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing

            #define FAIRYTOON_PASS_SHADOWCASTER
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonPasses.hlsl"
            ENDHLSL
        }

        // Character self-shadow caster: FairyToonFeature draws it per renderer into the character shadow atlas.
        Pass
        {
            Name "CharacterShadow"
            Tags { "LightMode" = "FairyToonCharacterShadow" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonCharShadowVertex
            #pragma fragment FairyToonCharShadowFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS
            #pragma multi_compile_instancing

            #define FAIRYTOON_PASS_CHARSHADOW
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonPasses.hlsl"
            ENDHLSL
        }

        // Character mask: FairyToonFeature draws it before opaques into _FT_CharacterDepth (see FairyToon.shader).
        Pass
        {
            Name "CharacterMask"
            Tags { "LightMode" = "FairyToonCharacterMask" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonDepthVertex
            #pragma shader_feature_local _ _FT_EF_IRIS
            #pragma fragment FairyToonDepthFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS
            #pragma multi_compile_instancing

            #define FAIRYTOON_PASS_DEPTH
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonDepthVertex
            #pragma shader_feature_local _ _FT_EF_IRIS
            #pragma fragment FairyToonDepthFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS
            #pragma multi_compile_instancing

            #define FAIRYTOON_PASS_DEPTH
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonDepthVertex
            #pragma shader_feature_local _ _FT_EF_IRIS
            #pragma fragment FairyToonDepthNormalsFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ _FT_NORMAL2ND _FT_NORMAL2ND_UV
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #pragma multi_compile_instancing

            #define FAIRYTOON_PASS_DEPTH
            #define FAIRYTOON_PASS_DEPTHNORMALS
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            ColorMask RG
            ZWrite Off      // (the hull is pulled towards the camera; it must not move the depth DLAA reads)
            Cull [_Cull]

            HLSLPROGRAM
            #pragma shader_feature_local _ _ALPHATEST_ON _SURFACE_TYPE_TRANSPARENT
            #pragma vertex FairyToonMotionVertex
            #pragma shader_feature_local _ _FT_EF_IRIS
            #pragma fragment FairyToonMotionFragment
            #pragma shader_feature_local _FT_MAIN_LAYERS
            #pragma multi_compile_instancing

            #define FAIRYTOON_PASS_MOTIONVECTORS
            #include "FairyToonVariantInput.hlsl"
            #include "FairyToonPasses.hlsl"
            ENDHLSL
        }





    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "NW.FairyToon.Editor.FairyToonGUI"
}
