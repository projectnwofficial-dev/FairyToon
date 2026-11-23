using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor
{
    /// <summary>Detected surface archetype driving lossless Endfield 0.3.0 PBR upgrade defaults.</summary>
    public enum FairyToonEndfieldSurfaceClass
    {
        Cloth = 0,
        DarkRimShadeMesh = 1,
        Enamel = 2,
        OiledLeather = 3,
        MatteSole = 4,
        PureMetal = 5
    }

    /// <summary>
    /// Lossless lilToon-to-Endfield 0.3.0 material upgrade rules and Unity applicator.
    /// Plain C# classification and recipe functions keep material decisions deterministic and unit-testable.
    /// </summary>
    public static class FairyToonEndfieldUpgrader
    {
        /// <summary>Pure-data summary of source lilToon material signals used for deterministic classification.</summary>
        public struct SourceSignals
        {
            public string MaterialName;
            public string FolderName;
            public bool HasMainTex;
            public bool HasBumpMap;
            public float BumpScale;
            public bool VariantHasOutline;
            public float OutlineWidth;
            public Color OutlineColor;
            public bool HasRimShade;
            public Color RimShadeColor;
            public bool HasRim;
            public Color RimColor;
            public float RimBlur;
            public bool HasMatCap;
            public bool HasMatCapMask;
            public int MatCapBlendMode;
            public float MatCapBlend;
            public Color MatCapColor;
            public bool HasMatCap2nd;
            public bool HasMatCap2ndMask;
            public int MatCap2ndBlendMode;
            public float MatCap2ndBlend;
            public Color MatCap2ndColor;
            public bool HasMetallicGlossMap;
            public float Metallic;
            public bool HasGlitter;
            public Color GlitterColor;

            public float RimShadeLuma => Luminance(RimShadeColor);
            public float MatCapColorLuma => Luminance(MatCapColor);
            public float MatCap2ndColorLuma => Luminance(MatCap2ndColor);
            public float OutlineColorLuma => Luminance(OutlineColor);
        }

        /// <summary>Calibrated Endfield 0.3.0 upgrade parameters produced from <see cref="SourceSignals"/>.</summary>
        public struct UpgradeRecipe
        {
            public FairyToonEndfieldSurfaceClass SurfaceClass;
            public FairyToonEndfieldMetalStyle MetalStyle;
            public float ClothSmoothness;
            public float DefaultReflectivity;
            public float MetalStrength;
            public float MetalSmoothness;
            public Color MetalGrazingColor;
            public Color PureMetalFallbackColor;
            public float MultiBounceAO;
            public float ContactAO;
            public float ContactAORadius;
            public float ContactAOBias;
            public float Curvature;
            public float CurvatureScale;
            public float EnvContrast;
            public float EnvironmentStrength;
            public float FabricSheen;
            public float Clearcoat;
            public float ClearcoatSmoothness;
            public float GSAAStrength;
            public float ShadowDarkness;
            public float ShadowDarker;
            public float SSAOStrength;
            public float SSAOLitStrength;
            public bool PreserveOutline;
            public Color EFOutlineColor;
            public float EFOutlineSaturation;
            public bool PreserveRimShade;
            public float AuthorRimAlphaScale;
            public float EFRimDepth;
            public float EFRimStrength;
            public float EnamelBaseMatCapBlendScale;
            public float MetalMatCapBlendScale;
            public float MetalMatCapColorScale;
            public bool ForceEnamel2ndNormalBlend;
            public bool BoostGlitter;
            public float GlitterEnableLighting;
        }

        /// <summary>Classifies a lilToon material into a surface archetype from pure source signals.</summary>
        public static FairyToonEndfieldSurfaceClass ClassifySurface(SourceSignals s)
        {
            string name = s.MaterialName ?? "";
            string folder = s.FolderName ?? "";

            if (s.HasRimShade && s.RimShadeLuma < 0.35f)
                return FairyToonEndfieldSurfaceClass.DarkRimShadeMesh;

            if (ContainsAny(name, folder, "enamel", "patent", "vinyl", "lacquer")
                || (s.HasMatCap && !s.HasMatCapMask && s.HasMatCap2nd && s.HasMatCap2ndMask && s.HasBumpMap))
                return FairyToonEndfieldSurfaceClass.Enamel;

            if (ContainsAny(name, folder, "sole", "rubber"))
                return FairyToonEndfieldSurfaceClass.MatteSole;

            if (ContainsAny(name, folder, "leather", "boot")
                || (s.HasBumpMap && s.BumpScale >= 1.5f && s.HasRim && s.RimColor.a >= 0.40f && s.RimBlur <= 0.25f))
                return FairyToonEndfieldSurfaceClass.OiledLeather;

            if (!s.HasMainTex && s.HasMatCap && !s.HasMatCapMask)
                return FairyToonEndfieldSurfaceClass.PureMetal;

            if (!s.HasMainTex && !s.HasMatCapMask && ContainsAny(name, folder, "metal", "silver", "chrome", "gunmetal", "chain"))
                return FairyToonEndfieldSurfaceClass.PureMetal;

            return FairyToonEndfieldSurfaceClass.Cloth;
        }

        /// <summary>Resolves BrightSilver vs SmokedGunmetal from source MatCap blend mode and tint when Auto is selected.</summary>
        public static FairyToonEndfieldMetalStyle ResolveMetalStyle(SourceSignals s, FairyToonEndfieldMetalStyle requested)
        {
            if (requested != FairyToonEndfieldMetalStyle.Auto) return requested;

            var surface = ClassifySurface(s);
            if (surface != FairyToonEndfieldSurfaceClass.Enamel
                && s.HasMatCap && (s.HasMatCapMask || !s.HasMainTex))
            {
                if (s.MatCapBlendMode == 0 && (s.MatCapColorLuma < 0.90f || s.MatCapColor.a < 0.85f))
                    return FairyToonEndfieldMetalStyle.SmokedGunmetal;
                return FairyToonEndfieldMetalStyle.BrightSilver;
            }

            if (s.HasMatCap2nd && s.HasMatCap2ndMask)
            {
                if (s.MatCap2ndBlendMode == 0 && (s.MatCap2ndColorLuma < 0.90f || s.MatCap2ndColor.a < 0.85f))
                    return FairyToonEndfieldMetalStyle.SmokedGunmetal;
                return FairyToonEndfieldMetalStyle.BrightSilver;
            }

            return FairyToonEndfieldMetalStyle.BrightSilver;
        }

        /// <summary>Computes the calibrated Endfield 0.3.0 upgrade recipe from pure source signals and options.</summary>
        public static UpgradeRecipe ComputeRecipe(SourceSignals s, FairyToonConvertOptions options)
        {
            options = options ?? new FairyToonConvertOptions();
            var surface = ClassifySurface(s);
            var metal = ResolveMetalStyle(s, options.MetalStyle);
            bool gunmetal = metal == FairyToonEndfieldMetalStyle.SmokedGunmetal;

            var r = new UpgradeRecipe
            {
                SurfaceClass = surface,
                MetalStyle = metal,
                ClothSmoothness = 0.26f,
                DefaultReflectivity = 0.50f,
                MetalStrength = gunmetal ? 0.94f : 0.96f,
                MetalSmoothness = gunmetal ? 0.88f : 0.92f,
                MetalGrazingColor = gunmetal
                    ? new Color(0.50f, 0.53f, 0.59f, 0.92f)
                    : new Color(0.94f, 0.96f, 1.00f, 0.92f),
                PureMetalFallbackColor = gunmetal
                    ? new Color(0.48f, 0.51f, 0.57f, 1.0f)
                    : new Color(0.86f, 0.88f, 0.92f, 1.0f),
                MultiBounceAO = 0.92f,
                ContactAO = 0.72f,
                ContactAORadius = 0.048f,
                ContactAOBias = 0.004f,
                Curvature = 0.58f,
                CurvatureScale = 2.4f,
                EnvContrast = gunmetal ? 1.45f : 1.35f,
                EnvironmentStrength = 1.0f,
                FabricSheen = 0.34f,
                Clearcoat = 0.0f,
                ClearcoatSmoothness = 0.85f,
                GSAAStrength = 0.0f,
                ShadowDarkness = 0.26f,
                ShadowDarker = 0.60f,
                SSAOStrength = 1.0f,
                SSAOLitStrength = 0.38f,
                PreserveOutline = s.VariantHasOutline && s.OutlineWidth > 0.001f,
                EFOutlineColor = s.OutlineColorLuma < 0.15f
                    ? new Color(0.10f, 0.10f, 0.12f, 1.0f)
                    : new Color(s.OutlineColor.r * 0.45f, s.OutlineColor.g * 0.45f, s.OutlineColor.b * 0.45f, 1.0f),
                EFOutlineSaturation = 1.2f,
                PreserveRimShade = s.HasRimShade,
                AuthorRimAlphaScale = 0.60f,
                EFRimDepth = 1.0f,
                EFRimStrength = s.HasRimShade ? 0.0f : 0.10f,
                EnamelBaseMatCapBlendScale = 0.0f,
                MetalMatCapBlendScale = gunmetal ? 0.58f : 0.52f,
                MetalMatCapColorScale = gunmetal ? 0.96f : 1.0f,
                ForceEnamel2ndNormalBlend = false,
                BoostGlitter = options.BoostGlitterHDR && s.HasGlitter,
                GlitterEnableLighting = 0.70f,
            };

            switch (surface)
            {
                case FairyToonEndfieldSurfaceClass.Enamel:
                    r.ClothSmoothness = 0.78f;
                    r.DefaultReflectivity = 0.90f;
                    r.EnvContrast = 1.45f;
                    r.FabricSheen = 0.0f;
                    r.Clearcoat = 0.76f;
                    r.ClearcoatSmoothness = 0.86f;
                    r.ShadowDarkness = 0.22f;
                    r.AuthorRimAlphaScale = 1.0f;
                    r.EFRimDepth = 0.0f;
                    r.EFRimStrength = 0.0f;
                    r.EnamelBaseMatCapBlendScale = 0.18f;
                    r.ForceEnamel2ndNormalBlend = true;
                    break;

                case FairyToonEndfieldSurfaceClass.OiledLeather:
                    r.ClothSmoothness = 0.76f;
                    r.DefaultReflectivity = 0.84f;
                    r.EnvContrast = 1.40f;
                    r.FabricSheen = 0.0f;
                    r.Clearcoat = 0.56f;
                    r.ClearcoatSmoothness = 0.82f;
                    r.GSAAStrength = 0.25f;
                    r.ShadowDarkness = 0.20f;
                    r.AuthorRimAlphaScale = 0.90f;
                    r.EFRimDepth = 0.0f;
                    r.EFRimStrength = 0.0f;
                    break;

                case FairyToonEndfieldSurfaceClass.MatteSole:
                    r.ClothSmoothness = 0.30f;
                    r.DefaultReflectivity = 0.45f;
                    r.EnvContrast = 1.0f;
                    r.FabricSheen = 0.0f;
                    r.Clearcoat = 0.0f;
                    r.ShadowDarkness = 0.20f;
                    r.AuthorRimAlphaScale = 1.0f;
                    r.EFRimDepth = 0.0f;
                    r.EFRimStrength = 0.0f;
                    break;

                case FairyToonEndfieldSurfaceClass.DarkRimShadeMesh:
                    r.EnvironmentStrength = 0.22f;
                    r.FabricSheen = 0.0f;
                    r.Clearcoat = 0.0f;
                    r.ShadowDarkness = 0.16f;
                    r.AuthorRimAlphaScale = 0.22f;
                    r.EFRimStrength = 0.0f;
                    break;

                case FairyToonEndfieldSurfaceClass.PureMetal:
                    r.FabricSheen = 0.0f;
                    r.Clearcoat = 0.0f;
                    r.ShadowDarkness = 0.22f;
                    break;
            }

            return r;
        }

        /// <summary>Extracts pure classification signals from a lilToon source snapshot without mutating it.</summary>
        public static SourceSignals ReadSignals(Material source, string sourcePath, bool variantHasOutline)
        {
            string folder = string.IsNullOrEmpty(sourcePath)
                ? ""
                : Path.GetFileName(Path.GetDirectoryName(sourcePath) ?? "") ?? "";

            return new SourceSignals
            {
                MaterialName = source == null ? "" : source.name,
                FolderName = folder,
                HasMainTex = GetTex(source, "_MainTex") != null,
                HasBumpMap = GetTex(source, "_BumpMap") != null,
                BumpScale = GetFloat(source, "_BumpScale", 1f),
                VariantHasOutline = variantHasOutline,
                OutlineWidth = GetFloat(source, "_OutlineWidth", 0f),
                OutlineColor = GetColor(source, "_OutlineColor", Color.black),
                HasRimShade = GetFloat(source, "_UseRimShade", 0f) > 0.5f,
                RimShadeColor = GetColor(source, "_RimShadeColor", Color.gray),
                HasRim = GetFloat(source, "_UseRim", 0f) > 0.5f,
                RimColor = GetColor(source, "_RimColor", Color.white),
                RimBlur = GetFloat(source, "_RimBlur", 0.65f),
                HasMatCap = GetFloat(source, "_UseMatCap", 0f) > 0.5f,
                HasMatCapMask = GetTex(source, "_MatCapBlendMask") != null,
                MatCapBlendMode = Mathf.RoundToInt(GetFloat(source, "_MatCapBlendMode", 0f)),
                MatCapBlend = GetFloat(source, "_MatCapBlend", 1f),
                MatCapColor = GetColor(source, "_MatCapColor", Color.white),
                HasMatCap2nd = GetFloat(source, "_UseMatCap2nd", 0f) > 0.5f,
                HasMatCap2ndMask = GetTex(source, "_MatCap2ndBlendMask") != null,
                MatCap2ndBlendMode = Mathf.RoundToInt(GetFloat(source, "_MatCap2ndBlendMode", 0f)),
                MatCap2ndBlend = GetFloat(source, "_MatCap2ndBlend", 1f),
                MatCap2ndColor = GetColor(source, "_MatCap2ndColor", Color.white),
                HasMetallicGlossMap = GetTex(source, "_MetallicGlossMap") != null,
                Metallic = GetFloat(source, "_Metallic", 0f),
                HasGlitter = GetFloat(source, "_UseGlitter", 0f) > 0.5f,
                GlitterColor = GetColor(source, "_GlitterColor", Color.white),
            };
        }

        /// <summary>Applies the lossless Endfield 0.3.0 upgrade recipe to <paramref name="target"/> before profile overrides.</summary>
        public static void Apply(
            Material sourceSnapshot,
            Material target,
            string sourcePath,
            string targetAssetPath,
            bool variantHasOutline,
            FairyToonConvertOptions options,
            FairyToonConvertReport report)
        {
            if (sourceSnapshot == null || target == null) return;
            options = options ?? new FairyToonConvertOptions();

            var signals = ReadSignals(sourceSnapshot, sourcePath, variantHasOutline);
            var recipe = ComputeRecipe(signals, options);

            SetIf(target, "_ShadingModel", FairyToonMaterialSetup.ShadingEndfield);
            SetIf(target, "_EFPart", FairyToonMaterialSetup.PartCloth);
            if (options.EndfieldDiffuseRamp != null) SetTexIf(target, "_DiffuseRamp", options.EndfieldDiffuseRamp);
            if (options.EndfieldSpecRamp != null) SetTexIf(target, "_SpecRamp", options.EndfieldSpecRamp);

            if (recipe.SurfaceClass == FairyToonEndfieldSurfaceClass.PureMetal && !signals.HasMainTex)
                SetColorIf(target, "_Color", recipe.PureMetalFallbackColor);

            SetIf(target, "_UseBumpMap", signals.HasBumpMap ? 1f : 0f);
            SetIf(target, "_UseReflection", 0f);
            SetIf(target, "_ReflectionCubeOverride", 0f);
            SetIf(target, "_EFSmoothness", 1f);
            SetIf(target, "_EFReflectivity", recipe.DefaultReflectivity);
            SetIf(target, "_EFOcclusion", 1f);
            SetIf(target, "_EFSpecular", 1f);
            SetIf(target, "_EFEnvironment", recipe.EnvironmentStrength);
            SetIf(target, "_GSAAStrength", recipe.GSAAStrength);
            SetIf(target, "_EFMultiBounceAO", recipe.MultiBounceAO);
            SetIf(target, "_EFContactAO", recipe.ContactAO);
            SetIf(target, "_EFContactAORadius", recipe.ContactAORadius);
            SetIf(target, "_EFContactAOBias", recipe.ContactAOBias);
            SetIf(target, "_EFCurvature", recipe.Curvature);
            SetIf(target, "_EFCurvatureScale", recipe.CurvatureScale);
            SetIf(target, "_EFEnvContrast", recipe.EnvContrast);
            SetColorIf(target, "_EFMetalGrazingColor", recipe.MetalGrazingColor);
            SetIf(target, "_EFFabricSheen", recipe.FabricSheen);
            SetIf(target, "_EFClearcoat", recipe.Clearcoat);
            SetIf(target, "_EFClearcoatSmoothness", recipe.ClearcoatSmoothness);
            SetIf(target, "_SSAOStrength", recipe.SSAOStrength);
            SetIf(target, "_SSAOLitStrength", recipe.SSAOLitStrength);
            SetIf(target, "_EFShadowDarkness", recipe.ShadowDarkness);
            SetIf(target, "_EFShadowDarker", recipe.ShadowDarker);
            SetIf(target, "_EFRimDepth", recipe.EFRimDepth);
            SetIf(target, "_EFRimDepthWidth", 3f);
            SetColorIf(target, "_EFRimColor", new Color(0.953f, 0.963f, 1f, 1f));
            SetIf(target, "_EFRimStrength", recipe.EFRimStrength);

            ApplyPBRMapOrScalars(sourceSnapshot, target, targetAssetPath, signals, recipe, options, report);
            ApplyOutline(target, recipe);
            ApplyRimAndRimShade(target, signals, recipe);
            ApplyMatCaps(target, signals, recipe);
            ApplyGlitter(target, signals, recipe);

            report?.AppliedOverrides.Add(
                $"Endfield 0.3.0 upgrade: surface={recipe.SurfaceClass}, metal={recipe.MetalStyle}, clearcoat={recipe.Clearcoat:0.##}, sheen={recipe.FabricSheen:0.##}, outline={(recipe.PreserveOutline ? "on" : "off")}.");
        }

        private static void ApplyPBRMapOrScalars(
            Material sourceSnapshot,
            Material target,
            string targetAssetPath,
            SourceSignals signals,
            UpgradeRecipe recipe,
            FairyToonConvertOptions options,
            FairyToonConvertReport report)
        {
            string pbrPath = options.PackPBRMaps ? ResolvePBRPath(targetAssetPath, target.name, options.PBRMapOutputFolder) : null;
            if (!string.IsNullOrEmpty(pbrPath))
            {
                EnsureFolder(Path.GetDirectoryName(pbrPath).Replace('\\', '/'));
                Material packSource = sourceSnapshot;
                Material tempClone = null;
                try
                {
                    if (recipe.SurfaceClass == FairyToonEndfieldSurfaceClass.PureMetal
                        && !signals.HasMatCapMask && !signals.HasMetallicGlossMap)
                    {
                        tempClone = new Material(sourceSnapshot) { hideFlags = HideFlags.HideAndDontSave };
                        SetIf(tempClone, "_Metallic", 1f);
                        packSource = tempClone;
                    }
                    var pbr = FairyToonTextureTools.PackPBRFromLilToonMaterial(
                        packSource,
                        pbrPath,
                        clothSmoothness: recipe.ClothSmoothness,
                        metalSmoothness: recipe.MetalSmoothness,
                        matCapMaskAsMetal: true,
                        metalThreshold: 0.22f,
                        defaultReflectivity: recipe.DefaultReflectivity,
                        maxSize: 2048,
                        metalStrength: recipe.MetalStrength);
                    SetTexIf(target, "_PBRMap", pbr);
                    report?.AppliedOverrides.Add("Packed Endfield _PBRMap at " + pbrPath + ".");
                }
                finally
                {
                    if (tempClone != null) Object.DestroyImmediate(tempClone);
                }
                return;
            }

            SetTexIf(target, "_PBRMap", null);
            bool pureMetal = recipe.SurfaceClass == FairyToonEndfieldSurfaceClass.PureMetal;
            SetIf(target, "_EFMetallic", pureMetal ? recipe.MetalStrength : Mathf.Clamp01(signals.Metallic));
            SetIf(target, "_EFSmoothness", pureMetal ? recipe.MetalSmoothness : recipe.ClothSmoothness);
            if (options.PackPBRMaps)
                report?.AppliedOverrides.Add("Packed PBR skipped (no Assets output path); applied scalar Endfield PBR fallback.");
        }

        private static void ApplyOutline(Material target, UpgradeRecipe recipe)
        {
            SetIf(target, "_UseOutline", recipe.PreserveOutline ? 1f : 0f);
            if (!recipe.PreserveOutline) return;
            SetColorIf(target, "_EFOutlineColor", recipe.EFOutlineColor);
            SetIf(target, "_EFOutlineSaturation", recipe.EFOutlineSaturation);
            SetIf(target, "_EFOutlineFlatten", 0f);
        }

        private static void ApplyRimAndRimShade(Material target, SourceSignals signals, UpgradeRecipe recipe)
        {
            SetIf(target, "_UseRimShade", recipe.PreserveRimShade ? 1f : 0f);
            if (signals.HasRim && recipe.AuthorRimAlphaScale > 0.001f)
            {
                SetIf(target, "_UseRim", 1f);
                var rc = signals.RimColor;
                SetColorIf(target, "_RimColor", new Color(rc.r, rc.g, rc.b, rc.a * recipe.AuthorRimAlphaScale));
            }
            else
            {
                SetIf(target, "_UseRim", 0f);
            }
        }

        private static void ApplyMatCaps(Material target, SourceSignals signals, UpgradeRecipe recipe)
        {
            bool keepBaseEnamel = recipe.SurfaceClass == FairyToonEndfieldSurfaceClass.Enamel
                && signals.HasMatCap && recipe.EnamelBaseMatCapBlendScale > 0.001f;
            bool keepMetalMatCap = recipe.SurfaceClass != FairyToonEndfieldSurfaceClass.Enamel
                && signals.HasMatCap && (signals.HasMatCapMask || recipe.SurfaceClass == FairyToonEndfieldSurfaceClass.PureMetal);

            if (keepBaseEnamel)
            {
                SetIf(target, "_UseMatCap", 1f);
                SetIf(target, "_MatCapBlend", signals.MatCapBlend * recipe.EnamelBaseMatCapBlendScale);
            }
            else if (keepMetalMatCap)
            {
                SetIf(target, "_UseMatCap", 1f);
                SetIf(target, "_MatCapBlend", signals.MatCapBlend * recipe.MetalMatCapBlendScale);
                if (Mathf.Abs(recipe.MetalMatCapColorScale - 1f) > 0.001f)
                {
                    var mc = signals.MatCapColor;
                    SetColorIf(target, "_MatCapColor", new Color(
                        mc.r * recipe.MetalMatCapColorScale,
                        mc.g * recipe.MetalMatCapColorScale,
                        mc.b * recipe.MetalMatCapColorScale,
                        mc.a));
                }
            }
            else
            {
                SetIf(target, "_UseMatCap", 0f);
            }

            bool keepMatCap2nd = signals.HasMatCap2nd && signals.HasMatCap2ndMask;
            if (keepMatCap2nd)
            {
                SetIf(target, "_UseMatCap2nd", 1f);
                SetIf(target, "_MatCap2ndBlend", signals.MatCap2ndBlend * recipe.MetalMatCapBlendScale);
                if (recipe.ForceEnamel2ndNormalBlend)
                    SetIf(target, "_MatCap2ndBlendMode", 0f);
            }
            else
            {
                SetIf(target, "_UseMatCap2nd", 0f);
            }
        }

        private static void ApplyGlitter(Material target, SourceSignals signals, UpgradeRecipe recipe)
        {
            if (!signals.HasGlitter)
            {
                SetIf(target, "_UseGlitter", 0f);
                return;
            }
            SetIf(target, "_UseGlitter", 1f);
            if (!recipe.BoostGlitter) return;

            // Preserve authored per-material _GlitterParams1/2 particle size & contrast; gently lift HDR intensity.
            var gc = signals.GlitterColor;
            SetColorIf(target, "_GlitterColor", new Color(gc.r * 1.125f, gc.g * 1.15625f, gc.b * 1.25f, gc.a));
            SetIf(target, "_GlitterEnableLighting", recipe.GlitterEnableLighting);
            SetIf(target, "_GlitterShadowMask", 0f);
        }

        internal static string ResolvePBRPath(string targetAssetPath, string materialName, string overrideFolder)
        {
            string folder = null;
            if (!string.IsNullOrWhiteSpace(overrideFolder))
            {
                folder = overrideFolder.Replace('\\', '/').TrimEnd('/');
            }
            else if (!string.IsNullOrEmpty(targetAssetPath) && targetAssetPath.StartsWith("Assets/", StringComparison.Ordinal))
            {
                string dir = Path.GetDirectoryName(targetAssetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir)) folder = dir + "/PBRMaps";
            }

            if (string.IsNullOrEmpty(folder)) return null;
            if (folder != "Assets" && !folder.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("PBRMapOutputFolder must be inside Assets.");
            if (folder.Split('/').Any(p => string.IsNullOrWhiteSpace(p) || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidOperationException("PBRMapOutputFolder contains an invalid path segment.");

            string stem = SafeName(string.IsNullOrEmpty(targetAssetPath)
                ? materialName
                : Path.GetFileNameWithoutExtension(targetAssetPath));
            return folder + "/" + stem + "_PBR.png";
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, folder.Substring(parent.Length + 1));
            if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create PBR output folder " + folder);
        }

        private static string SafeName(string name)
        {
            name = name ?? "";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "Material" : name;
        }

        private static bool ContainsAny(string a, string b, params string[] terms)
        {
            for (int i = 0; i < terms.Length; i++)
            {
                string t = terms[i];
                if (a.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                    || b.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static float Luminance(Color c) => c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
        private static float GetFloat(Material m, string prop, float fallback) => m != null && m.HasProperty(prop) ? m.GetFloat(prop) : fallback;
        private static Color GetColor(Material m, string prop, Color fallback) => m != null && m.HasProperty(prop) ? m.GetColor(prop) : fallback;
        private static Texture GetTex(Material m, string prop) => m != null && m.HasProperty(prop) ? m.GetTexture(prop) : null;
        private static void SetIf(Material m, string prop, float v) { if (m.HasProperty(prop)) m.SetFloat(prop, v); }
        private static void SetColorIf(Material m, string prop, Color c) { if (m.HasProperty(prop)) m.SetColor(prop, c); }
        private static void SetTexIf(Material m, string prop, Texture t) { if (m.HasProperty(prop)) m.SetTexture(prop, t); }
    }
}
