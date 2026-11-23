using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor
{
    /// <summary>Result and losses for one lilToon material conversion.</summary>
    [Serializable]
    public sealed class FairyToonConvertReport
    {
        public string MaterialName;
        public string SourcePath;
        public string SourceShader;
        public string TargetPath;
        public bool Success;
        public int Mode;
        public bool Outline;
        public List<string> UnsupportedFeatures = new List<string>();
        public List<string> DroppedValues = new List<string>();
        public List<string> AppliedOverrides = new List<string>();
        public List<string> Errors = new List<string>();

        public string ModeName => Mode == FairyToonMaterialSetup.Cutout ? "Cutout"
            : Mode == FairyToonMaterialSetup.Transparent ? "Transparent"
            : Mode == FairyToonMaterialSetup.Refraction ? "Refraction"
            : Mode == FairyToonMaterialSetup.Fur ? "Fur"
            : Mode == FairyToonMaterialSetup.Gem ? "Gem" : "Opaque";

        public string ToMarkdown() => new[] { this }.ToMarkdown();
    }

    /// <summary>Markdown export for single reports and batches.</summary>
    public static class FairyToonConvertReportExtensions
    {
        public static string ToMarkdown(this IEnumerable<FairyToonConvertReport> reports)
        {
            var list = reports == null ? new List<FairyToonConvertReport>() : reports.Where(r => r != null).ToList();
            var text = new StringBuilder("# FairyToon conversion\n\n");
            text.AppendLine($"{list.Count(r => r.Success)} converted, {list.Count(r => !r.Success)} failed.");
            text.AppendLine("\n| Material | lilToon variant | FairyToon mode | Outline | Unsupported |\n|---|---|---|---|---|");
            foreach (var report in list)
                text.AppendLine($"| {Escape(report.MaterialName)} | {Escape(report.SourceShader)} | {(report.Success ? report.ModeName : "Failed")} | {(report.Outline ? "On" : "Off")} | {Escape(string.Join("; ", report.UnsupportedFeatures))} |");
            foreach (var report in list)
            {
                text.AppendLine($"\n## {Escape(report.MaterialName)}\n");
                text.AppendLine($"- Source: {Escape(report.SourcePath)} ({Escape(report.SourceShader)})");
                text.AppendLine($"- Target: {Escape(report.TargetPath)}");
                text.AppendLine($"- Result: {(report.Success ? report.ModeName : "Failed")}, outline {(report.Outline ? "on" : "off")}");
                AppendList(text, "Unsupported features in use", report.UnsupportedFeatures);
                AppendList(text, "Non-default values dropped", report.DroppedValues);
                AppendList(text, "Applied heuristics / profile overrides", report.AppliedOverrides);
                AppendList(text, "Errors", report.Errors);
            }
            return text.ToString();
        }

        private static void AppendList(StringBuilder text, string heading, List<string> values)
        {
            if (values.Count == 0) return;
            text.AppendLine($"\n### {heading}\n");
            foreach (string value in values) text.AppendLine("- " + Escape(value));
        }

        private static string Escape(string value) => (value ?? "").Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
    }

    /// <summary>Converts lilToon materials without depending on a lilToon assembly.</summary>
    public static class FairyToonConverter
    {
        private static readonly string[] IgnoredProperties =
        {
            "_BaseColor", "_BaseMap", "_ColorMask", "_DummyProperty", "_IgnoreEncryption", "_Keys",
            "_FurCull", "_FurSrcBlend", "_FurDstBlend", "_FurSrcBlendAlpha", "_FurDstBlendAlpha",
            "_FurBlendOp", "_FurBlendOpAlpha", "_FurZWrite", "_FurZTest", "_FurColorMask",
            "_FurOffsetFactor", "_FurOffsetUnits", "_FurAlphaToMask"
        };
        private static readonly string[] IgnoredPrefixes = { "_BitKey", "_lil", "_BlendOp", "_FurStencil", "_BaseColorMap" };
        // (property prefix, the lilToon toggle that turns it on: values of a feature that is off are not losses)
        // Supported features copy by name; only omitted feature options need gates here.
        private static readonly string[,] FeatureGates =
        {
            { "_Main2ndDissolve", "_UseMain2ndTex" }, { "_Main2ndDistanceFade", "_UseMain2ndTex" },
            { "_Main2ndTexDecal", "_UseMain2ndTex" }, { "_Main3rdDissolve", "_UseMain3rdTex" },
            { "_Main3rdDistanceFade", "_UseMain3rdTex" }, { "_Main3rdTexDecal", "_UseMain3rdTex" },
            { "_EmissionParallax", "_UseEmission" }, { "_Emission2ndParallax", "_UseEmission2nd" },
            { "_eg", "_UseEmission" }, { "_e2g", "_UseEmission2nd" },
            { "_Parallax", "_UseParallax" }, { "_AudioLink", "_UseAudioLink" },
            { "_OutlineLit", "_UseOutline" }, { "_Shadow3rd", "_UseShadow" }, { "_Fur", "_UseFur" },
        };
        private static readonly string[,] FeatureNames =
        {
            { "_UseDissolve", "Dissolve" },
            { "_UseDistanceFade", "Distance Fade" }, { "_UseAudioLink", "AudioLink" },
            { "_UseParallax", "Parallax" }, { "_UsePOM", "Parallax occlusion mapping" },
            { "_UseDither", "Dither" }, { "_UseClippingCanceller", "Clipping canceller" },
            { "_UseOutlineTexLighting", "Outline texture lighting" }
        };
        private static readonly string[,] VariantFeatures =
        {
            { "Tess", "Tessellation dropped" }, { "Overlay", "Overlay rendering dropped" },
            { "FakeShadow", "Fake Shadow rendering dropped" }, { "Lite", "Lite shading approximated by full FairyToon" }
        };

        public static bool IsLilToon(Material material) => material != null && IsLilToon(material.shader);

        public static bool IsLilToon(Shader shader)
        {
            if (shader == null) return false;
            string name = shader.name;
            return Contains(name, "lilToon") || name.StartsWith("Hidden/ltspass", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Hidden/ltsl", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("Hidden/ltsmulti", StringComparison.OrdinalIgnoreCase);
        }

        public static FairyToonConvertReport Convert(Material source, Material target, FairyToonConvertOptions options = null)
        {
            options = options ?? new FairyToonConvertOptions();
            return Convert(source, target, options, FindProfiles(options));
        }

        private static FairyToonConvertReport Convert(Material source, Material target, FairyToonConvertOptions options,
            List<FairyToonConvertProfile> profiles, string targetAssetPath = null)
        {
            var report = NewReport(source);
            string resolvedTargetPath = !string.IsNullOrEmpty(targetAssetPath)
                ? targetAssetPath
                : (target == null ? "" : AssetDatabase.GetAssetPath(target));
            report.TargetPath = resolvedTargetPath;
            if (!IsLilToon(source)) { report.Errors.Add("Source is not a lilToon material."); return report; }
            if (target == null) { report.Errors.Add("Target material is missing."); return report; }
            // (snapshot before the swap also makes in-place conversion safe)
            var snapshot = new Material(source) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var variant = MapVariant(snapshot, report);
                string shaderName = FairyToonMaterialSetup.GetShaderName(variant.Mode);
                var shader = Shader.Find(shaderName);
                if (shader == null) { report.Errors.Add(shaderName + " shader was not found."); return report; }
                // Determine the destination before auditing losses: special properties belong to their own shader.
                CollectLosses(snapshot, shader, report);
                target.shader = shader;
                // Shared lilToon names copy values, textures and texture scale/offset, including normal layers, anisotropy, backlight, glitter, main layers and both emissions.
                // Both MatCap custom-normal toggles, bump maps/scales and their ST use this same lossless copy.
                // Root/Main2nd/Main3rd dissolve vectors, gamma-HDR colors, masks, noise and their ST copy unchanged.
                target.CopyPropertiesFromMaterial(snapshot);
                target.shader = shader;
                target.shaderKeywords = Array.Empty<string>();
                // lilToon's installed generated feature set enables all three noise helpers, even with null textures.
                // These FairyToon controls preserve that default; never infer a feature exclusion from texture/strength.
                foreach (string prefix in new[] { "_Dissolve", "_Main2ndDissolve", "_Main3rdDissolve" })
                {
                    string noiseToggle = "_FT_" + prefix.Substring(1) + "UseNoise";
                    if (!snapshot.HasProperty(noiseToggle) && target.HasProperty(noiseToggle)) target.SetFloat(noiseToggle, 1f);
                }
                if (HasActiveDissolve(snapshot, "_Dissolve") || HasActiveDissolve(snapshot, "_Main2ndDissolve")
                    || HasActiveDissolve(snapshot, "_Main3rdDissolve"))
                    report.AppliedOverrides.Add("Dissolve noise helpers default to enabled, matching the installed lilToon feature set. Custom optimized source shaders with noise features excluded require the corresponding Use Noise Helper toggle to be disabled manually.");
                // Imported ordering belongs to the source, even if its queue is the Hair role's 2460.
                target.SetOverrideTag(FairyToonMaterialSetup.ManagedHairQueueTag, "0");
                target.renderQueue = -1;
                target.SetFloat("_TransparentMode", variant.Mode <= FairyToonMaterialSetup.Transparent
                    ? variant.Mode : variant.Mode == FairyToonMaterialSetup.Fur && variant.FurMode == FairyToonMaterialSetup.FurCutout
                        ? FairyToonMaterialSetup.Cutout : FairyToonMaterialSetup.Transparent);
                target.SetFloat("_UseOutline", variant.Outline ? 1f : 0f);
                if (variant.Mode == FairyToonMaterialSetup.Refraction) target.SetFloat("_RefractionBlur", variant.Blur ? 1f : 0f);
                if (variant.Mode == FairyToonMaterialSetup.Fur)
                {
                    target.SetFloat("_FurMode", variant.FurMode);
                    FairyToonMaterialSetup.SetFurShellDefaults(target, variant.FurMode);
                }
                // Preserve the effective source order as an explicit queue, even when it matches this shader's default.
                // Apply preserves authored offsets; lilToon defaults include Transparent 2460 and Refraction 2900.
                if (snapshot.renderQueue != target.renderQueue)
                {
                    report.AppliedOverrides.Add("Copied lilToon render queue " + snapshot.renderQueue + " before profile/coverage setup.");
                }
                target.renderQueue = snapshot.renderQueue;
                bool convertHair = options.TransparentHairToCutout && variant.Mode == FairyToonMaterialSetup.Transparent
                    && Contains(report.SourceShader, "Transparent") && Contains(source.name, "hair");
                if (convertHair)
                {
                    target.SetFloat("_TransparentMode", FairyToonMaterialSetup.Cutout);
                    target.SetFloat("_Cutoff", 0.5f);
                    target.SetFloat("_AlphaToMask", 1f);
                    report.AppliedOverrides.Add("Transparent hair to Cutout: _TransparentMode = 1, _Cutoff = 0.5, _AlphaToMask = 1.");
                }
                if (options.UpgradeToEndfield)
                {
                    if (variant.Mode <= FairyToonMaterialSetup.Transparent)
                        FairyToonEndfieldUpgrader.Apply(snapshot, target, report.SourcePath, resolvedTargetPath, variant.Outline, options, report);
                    else
                        report.AppliedOverrides.Add("Endfield upgrade skipped on special optical mode (" + FairyToonMaterialSetup.GetShaderName(variant.Mode) + ").");
                }
                var profileProperties = ApplyProfiles(source, target, profiles, report);
                if (convertHair && FairyToonMaterialSetup.GetRenderingMode(target) == FairyToonMaterialSetup.Cutout
                    && target.renderQueue > (int)RenderQueue.GeometryLast)
                {
                    target.renderQueue = FairyToonMaterialSetup.GetDefaultRenderQueue(FairyToonMaterialSetup.Cutout);
                    report.AppliedOverrides.Add("Moved converted hair from its transparent queue to the Cutout queue.");
                }
                ApplyLilToonSSAO(target, profileProperties);
                FairyToonMaterialSetup.Apply(target);
                ReportInactiveDissolve(snapshot, target, report);
                report.Mode = FairyToonMaterialSetup.GetRenderingMode(target);
                report.Outline = target.GetFloat("_UseOutline") > 0.5f;
                report.Success = true;
                EditorUtility.SetDirty(target);
            }
            finally { Object.DestroyImmediate(snapshot); }
            return report;
        }

        // lilToon has no screen-space AO. Only imported Toon materials opt out;
        // native defaults and explicit Endfield destinations retain their current settings.
        private static void ApplyLilToonSSAO(Material target, ISet<string> profileProperties)
        {
            if (Mathf.RoundToInt(target.GetFloat("_ShadingModel")) != FairyToonMaterialSetup.ShadingLilToon) return;
            if (!profileProperties.Contains("_SSAOStrength")) target.SetFloat("_SSAOStrength", 0f);
            if (!profileProperties.Contains("_SSAOLitStrength")) target.SetFloat("_SSAOLitStrength", 0f);
        }

        public static List<FairyToonConvertReport> ConvertAll(IEnumerable<Material> sources, FairyToonConvertOptions options)
        {
            options = options ?? new FairyToonConvertOptions();
            var list = (sources ?? Enumerable.Empty<Material>()).Where(m => m != null).Distinct()
                .OrderBy(m => AssetDatabase.GetAssetPath(m), StringComparer.Ordinal).ThenBy(m => m.name, StringComparer.Ordinal).ToList();
            var profiles = FindProfiles(options);
            var reports = new List<FairyToonConvertReport>();
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sourcePaths = new HashSet<string>(list.Select(m => AssetDatabase.GetAssetPath(m)), StringComparer.OrdinalIgnoreCase);
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert lilToon materials");
            // (no StartAssetEditing: output folders created inside it are not valid folders until it ends)
            try
            {
                foreach (var source in list)
                {
                    var report = NewReport(source);
                    Material created = null;
                    try
                    {
                        if (!IsLilToon(source)) { report.Errors.Add("Source is not a lilToon material."); reports.Add(report); continue; }
                        Material target = source;
                        string path = AssetDatabase.GetAssetPath(source);
                        report.TargetPath = path;
                        if (options.InPlace)
                        {
                            if (AssetDatabase.IsSubAsset(source) || (!string.IsNullOrEmpty(path) && !path.StartsWith("Assets/", StringComparison.Ordinal)))
                                throw new InvalidOperationException("In-place conversion requires an editable standalone material in Assets (or a scene material).");
                            if (!string.IsNullOrEmpty(path) && !AssetDatabase.IsOpenForEdit(source))
                                throw new InvalidOperationException("Source material is not open for editing.");
                            Undo.RecordObject(source, "Convert lilToon material");
                        }
                        else
                        {
                            string folder = GetOutputFolder(source, options.OutputFolder);
                            EnsureFolder(folder);
                            string stem = SafeName(source.name);
                            string name = stem;
                            path = folder + "/" + name + ".mat";
                            for (int i = 2; !usedPaths.Add(path); i++)
                            {
                                name = stem + " " + i;
                                path = folder + "/" + name + ".mat";
                            }
                            report.TargetPath = path;
                            if (sourcePaths.Contains(path)) throw new InvalidOperationException("Copy output would overwrite a source material. Choose another folder.");
                            target = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (target == null)
                            {
                                if (File.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                                    throw new InvalidOperationException("Output path is occupied by another asset.");
                                created = target = new Material(Shader.Find(FairyToon.ShaderName) ?? source.shader) { name = name };
                            }
                            else
                            {
                                if (!AssetDatabase.IsOpenForEdit(target)) throw new InvalidOperationException("Target material is not open for editing.");
                                Undo.RecordObject(target, "Refresh FairyToon material");
                            }
                        }
                        report = Convert(source, target, options, profiles, path);
                        report.TargetPath = path;
                        if (created != null && report.Success)
                        {
                            AssetDatabase.CreateAsset(created, path);
                            Undo.RegisterCreatedObjectUndo(created, "Create FairyToon material");
                            created = null;
                        }
                    }
                    catch (Exception exception) { report.Success = false; report.Errors.Add(exception.Message); }
                    finally { if (created != null) Object.DestroyImmediate(created); }
                    reports.Add(report);
                }
            }
            finally { Undo.CollapseUndoOperations(undoGroup); }
            AssetDatabase.SaveAssets();
            try
            {
                string folder = Path.Combine(Path.GetDirectoryName(Application.dataPath), "Logs");
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "FairyToonConvert.md"), reports.ToMarkdown(), new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                var failure = new FairyToonConvertReport { MaterialName = "Report export", TargetPath = "Logs/FairyToonConvert.md" };
                failure.Errors.Add(exception.Message);
                reports.Add(failure);
            }
            Debug.Log($"[FairyToon] {reports.Count(r => r.Success)} converted, {reports.Count(r => !r.Success)} failed; "
                + $"{reports.Sum(r => r.UnsupportedFeatures.Count)} unsupported features, {reports.Sum(r => r.DroppedValues.Count)} dropped values, "
                + $"{reports.Sum(r => r.Errors.Count)} errors. Report: Logs/FairyToonConvert.md");
            return reports;
        }

        private struct VariantMapping
        {
            public int Mode;
            public int FurMode;
            public bool Blur;
            public bool Outline;
        }

        private static VariantMapping MapVariant(Material source, FairyToonConvertReport report)
        {
            string variant = report.SourceShader;
            // These names are audited against lilToon/Shader/lts_{ref,ref_blur,fur,fur_cutout,fur_two,gem}.shader
            // and ltsmulti_{ref,fur,gem,o}.shader, including optional FurOnly variants.
            int mode = Contains(variant, "Gem") ? FairyToonMaterialSetup.Gem
                : Contains(variant, "Refraction") ? FairyToonMaterialSetup.Refraction
                : Contains(variant, "Fur") ? FairyToonMaterialSetup.Fur
                : Contains(variant, "Cutout") ? FairyToonMaterialSetup.Cutout
                : Contains(variant, "Transparent") ? FairyToonMaterialSetup.Transparent : FairyToonMaterialSetup.Opaque;
            int furMode = Contains(variant, "TwoPass") ? FairyToonMaterialSetup.FurTwoPass
                : Contains(variant, "Cutout") ? FairyToonMaterialSetup.FurCutout : FairyToonMaterialSetup.FurTransparent;
            bool multi = Contains(variant, "Multi");
            for (int i = 0; i < VariantFeatures.GetLength(0); i++)
                if (Contains(variant, VariantFeatures[i, 0])) Add(report.UnsupportedFeatures, VariantFeatures[i, 1]);
            if (Contains(variant, "ltsl")) Add(report.UnsupportedFeatures, "Lite shading approximated by full FairyToon");
            if (multi)
            {
                int property = source.shader.FindPropertyIndex("_TransparentMode");
                if (property >= 0 && IsNumber(source.shader.GetPropertyType(property)))
                {
                    float value = Number(source, property);
                    int multiMode = Mathf.RoundToInt(value);
                    if (Mathf.Approximately(value, multiMode) && multiMode >= 0 && multiMode <= 6)
                    {
                        // Named Multi special shaders are authoritative when their stored mode is a stale default.
                        if (multiMode >= 3 || mode <= FairyToonMaterialSetup.Transparent)
                            mode = multiMode == 3 ? FairyToonMaterialSetup.Refraction
                                : multiMode == 4 || multiMode == 5 ? FairyToonMaterialSetup.Fur
                                : multiMode == 6 ? FairyToonMaterialSetup.Gem : multiMode;
                        if (mode == FairyToonMaterialSetup.Fur)
                            furMode = multiMode == 5 ? FairyToonMaterialSetup.FurCutout : FairyToonMaterialSetup.FurTransparent;
                        report.AppliedOverrides.Add("Multi _TransparentMode = " + multiMode + " mapped to FairyToon mode " + mode + ".");
                    }
                    else Add(report.UnsupportedFeatures, "Unknown Multi rendering mode; used shader-name fallback");
                }
                else if (mode <= FairyToonMaterialSetup.Transparent && source.IsKeywordEnabled("UNITY_UI_ALPHACLIP")) mode = FairyToonMaterialSetup.Cutout;
                else if (mode <= FairyToonMaterialSetup.Transparent && source.IsKeywordEnabled("UNITY_UI_CLIP_RECT")) mode = FairyToonMaterialSetup.Transparent;
                else Add(report.UnsupportedFeatures, "Multi mode unavailable; used shader-name fallback");
            }
            if (Contains(variant, "TwoPass") && mode != FairyToonMaterialSetup.Fur)
                Add(report.UnsupportedFeatures, "Two-pass back faces dropped");
            if (Contains(variant, "FurOnly")) Add(report.UnsupportedFeatures, "Fur-only base suppression unavailable; base surface included");
            return new VariantMapping
            {
                Mode = mode,
                FurMode = furMode,
                Blur = Contains(variant, "RefractionBlur"),
                Outline = Contains(variant, "Outline") || (multi && source.HasProperty("_UseOutline") && source.GetFloat("_UseOutline") > 0.5f)
            };
        }

        private static void CollectLosses(Material source, Shader fairyToon, FairyToonConvertReport report)
        {
            var shader = source.shader;
            var visited = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                string property = shader.GetPropertyName(i);
                if (!visited.Add(property)) continue;
                bool storedDecalVector = property == "_Main2ndTexDecalAnimation" || property == "_Main2ndTexDecalSubParam"
                    || property == "_Main3rdTexDecalAnimation" || property == "_Main3rdTexDecalSubParam";
                if (!storedDecalVector && fairyToon.FindPropertyIndex(property) >= 0) continue;
                var type = shader.GetPropertyType(i);
                if (property.StartsWith("_Use", StringComparison.Ordinal) && IsNumber(type) && Number(source, i) > 0.5f)
                {
                    Add(report.UnsupportedFeatures, FeatureName(property) + " (" + property + ")");
                    continue;
                }
                if (Ignored(property) || GatedOff(source, property)) continue;
                string value = null;
                if (IsNumber(type))
                {
                    float actual = Number(source, i);
                    float fallback = type == ShaderPropertyType.Int ? shader.GetPropertyDefaultIntValue(i) : shader.GetPropertyDefaultFloatValue(i);
                    if (!Mathf.Approximately(actual, fallback)) value = F(actual) + " (default " + F(fallback) + ")";
                }
                else if (type == ShaderPropertyType.Color || type == ShaderPropertyType.Vector)
                {
                    Vector4 actual = type == ShaderPropertyType.Color ? (Vector4)source.GetColor(property) : source.GetVector(property);
                    Vector4 fallback = shader.GetPropertyDefaultVectorValue(i);
                    if (!Approximately(actual, fallback)) value = V(actual) + " (default " + V(fallback) + ")";
                }
                else if (type == ShaderPropertyType.Texture)
                {
                    var texture = source.GetTexture(property);
                    if (texture != null)
                    {
                        string path = AssetDatabase.GetAssetPath(texture);
                        value = string.IsNullOrEmpty(path) ? texture.name : path;
                    }
                }
                if (value == null) continue;
                if (storedDecalVector) Add(report.UnsupportedFeatures, property + " is retained. Decal atlas playback is unsupported.");
                else report.DroppedValues.Add(property + " = " + value);
            }
            foreach (string prefix in new[] { "_Dissolve", "_Main2ndDissolve", "_Main3rdDissolve" })
            {
                if (!HasActiveDissolve(source, prefix)) continue;
                Vector4 dissolve = source.GetVector(prefix + "Params");
                int dissolveMode = Mathf.RoundToInt(dissolve.x);
                if (dissolveMode < 1 || dissolveMode > 3 || fairyToon.FindPropertyIndex(prefix + "Params") < 0)
                    Add(report.UnsupportedFeatures, "Dissolve mode " + dissolveMode + " (" + prefix + "Params = " + V(dissolve)
                        + "); supported modes are texture mask (1), UV (2), and object position (3).");
            }
            ReportVectorFeature(source, fairyToon, "_DistanceFade", "Distance Fade", 2, report);
        }

        private static bool HasActiveDissolve(Material material, string prefix)
        {
            string property = prefix + "Params";
            int index = material.shader.FindPropertyIndex(property);
            if (index < 0 || material.shader.GetPropertyType(index) != ShaderPropertyType.Vector
                || Mathf.RoundToInt(material.GetVector(property).x) == 0) return false;
            string layerToggle = prefix == "_Main2ndDissolve" ? "_UseMain2ndTex"
                : prefix == "_Main3rdDissolve" ? "_UseMain3rdTex" : null;
            return layerToggle == null || (material.HasProperty(layerToggle) && material.GetFloat(layerToggle) > 0.5f);
        }

        // Audit the final material after profiles/coverage setup: Opaque and Cutout share a shader.
        // Whole-material and layer policies differ: opaque layers remain supported.
        private static void ReportInactiveDissolve(Material source, Material target, FairyToonConvertReport report)
        {
            int mode = FairyToonMaterialSetup.GetRenderingMode(target);
            bool root = HasActiveDissolve(source, "_Dissolve");
            bool layers = HasActiveDissolve(source, "_Main2ndDissolve") || HasActiveDissolve(source, "_Main3rdDissolve");
            if (root && (mode == FairyToonMaterialSetup.Opaque || mode == FairyToonMaterialSetup.Gem))
                Add(report.UnsupportedFeatures, "Dissolve is ignored on the "
                    + (mode == FairyToonMaterialSetup.Gem ? "Gem" : "Opaque") + " target in all whole-material modes.");
            if (layers && mode == FairyToonMaterialSetup.Gem)
                Add(report.UnsupportedFeatures, "Main2nd/Main3rd layer dissolve is ignored on the Gem target.");
            if ((root || layers) && mode == FairyToonMaterialSetup.Fur)
                Add(report.UnsupportedFeatures, "Whole-material and layer dissolve affect the Fur body only; Fur ribbon shells ignore dissolve.");
        }

        private static void ReportVectorFeature(Material source, Shader target, string property, string feature, int component,
            FairyToonConvertReport report)
        {
            int index = source.shader.FindPropertyIndex(property);
            if (index < 0 || target.FindPropertyIndex(property) >= 0 || source.shader.GetPropertyType(index) != ShaderPropertyType.Vector) return;
            Vector4 value = source.GetVector(property);
            Vector4 fallback = source.shader.GetPropertyDefaultVectorValue(index);
            if (!Mathf.Approximately(value[component], fallback[component])) Add(report.UnsupportedFeatures, feature + " (" + property + " = " + V(value) + ")");
        }

        private static List<FairyToonConvertProfile> FindProfiles(FairyToonConvertOptions options)
        {
            var profiles = new List<FairyToonConvertProfile>();
            if (options.UseProjectProfiles)
                foreach (string path in AssetDatabase.FindAssets("t:FairyToonConvertProfile").Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p, StringComparer.Ordinal))
                {
                    var profile = AssetDatabase.LoadAssetAtPath<FairyToonConvertProfile>(path);
                    if (profile != null && profile != options.Profile) profiles.Add(profile);
                }
            if (options.Profile != null) profiles.Add(options.Profile);
            return profiles;
        }

        private static HashSet<string> ApplyProfiles(Material source, Material target, List<FairyToonConvertProfile> profiles, FairyToonConvertReport report)
        {
            var properties = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in profiles)
            {
                if (profile.entries == null) continue;
                foreach (var entry in profile.entries)
                {
                    if (entry == null || entry.source != source || entry.overrides == null) continue;
                    foreach (var item in entry.overrides)
                    {
                        if (item == null) continue;
                        string label = "Profile " + profile.name + ": " + item.property;
                        int index = string.IsNullOrEmpty(item.property) ? -1 : target.shader.FindPropertyIndex(item.property);
                        if (index < 0) { report.Errors.Add(label + " is not a FairyToon property; skipped."); continue; }
                        var type = target.shader.GetPropertyType(index);
                        string value;
                        if (item.kind == FairyToonConvertProfile.Kind.Float && IsNumber(type))
                        {
                            if (type == ShaderPropertyType.Int) target.SetInteger(item.property, Mathf.RoundToInt(item.floatValue));
                            else target.SetFloat(item.property, item.floatValue);
                            value = F(item.floatValue);
                        }
                        else if (item.kind == FairyToonConvertProfile.Kind.Color && type == ShaderPropertyType.Color)
                        { target.SetColor(item.property, item.colorValue); value = V(item.colorValue); }
                        else if (item.kind == FairyToonConvertProfile.Kind.Vector && type == ShaderPropertyType.Vector)
                        { target.SetVector(item.property, item.vectorValue); value = V(item.vectorValue); }
                        else if (item.kind == FairyToonConvertProfile.Kind.Texture && type == ShaderPropertyType.Texture)
                        { target.SetTexture(item.property, item.textureValue); value = item.textureValue == null ? "null" : item.textureValue.name; }
                        else { report.Errors.Add(label + " has incompatible kind " + item.kind + "; skipped."); continue; }
                        properties.Add(item.property);
                        report.AppliedOverrides.Add(label + " = " + value);
                    }
                }
            }
            return properties;
        }

        /// <summary>Collects materials from materials, renderer hierarchies, models and recursive asset folders.</summary>
        public static List<Material> CollectMaterials(IEnumerable<Object> objects)
        {
            var materials = new HashSet<Material>();
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in objects ?? Enumerable.Empty<Object>())
            {
                if (item == null) continue;
                string path = AssetDatabase.GetAssetPath(item);
                if (AssetDatabase.IsValidFolder(path))
                {
                    foreach (string guid in AssetDatabase.FindAssets("t:Material t:GameObject", new[] { path }))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
                }
                else CollectObject(item, materials);
            }
            foreach (string path in paths)
                foreach (var item in AssetDatabase.LoadAllAssetsAtPath(path)) CollectObject(item, materials);
            return materials.OrderBy(m => AssetDatabase.GetAssetPath(m), StringComparer.Ordinal).ThenBy(m => m.name, StringComparer.Ordinal).ToList();
        }

        private static void CollectObject(Object item, HashSet<Material> materials)
        {
            if (item is Material material && IsLilToon(material)) materials.Add(material);
            var gameObject = item as GameObject;
            if (item is Component component) gameObject = component.gameObject;
            if (gameObject == null) return;
            foreach (var renderer in gameObject.GetComponentsInChildren<Renderer>(true))
                foreach (var shared in renderer.sharedMaterials)
                    if (IsLilToon(shared)) materials.Add(shared);
        }

        [MenuItem("Assets/FairyToon/Convert lilToon Material (copy)")]
        private static void ConvertSelectionCopy() => ConvertAll(CollectMaterials(Selection.objects), new FairyToonConvertOptions());

        [MenuItem("Assets/FairyToon/Convert lilToon Material to Endfield (copy)")]
        private static void ConvertSelectionToEndfieldCopy() => ConvertAll(
            CollectMaterials(Selection.objects),
            new FairyToonConvertOptions { UpgradeToEndfield = true });

        [MenuItem("Assets/FairyToon/Convert lilToon Material (in place)")]
        private static void ConvertSelectionInPlace() => ConvertAll(CollectMaterials(Selection.objects), new FairyToonConvertOptions { InPlace = true });

        [MenuItem("Assets/FairyToon/Convert lilToon Material (copy)", true)]
        [MenuItem("Assets/FairyToon/Convert lilToon Material to Endfield (copy)", true)]
        [MenuItem("Assets/FairyToon/Convert lilToon Material (in place)", true)]
        // (cheap on purpose: runs every time the context menu opens; folders and prefabs are only searched on convert)
        private static bool ValidateSelection() => Selection.objects.Any(o => o is Material m ? IsLilToon(m)
            : o is GameObject || o is Component || AssetDatabase.IsValidFolder(AssetDatabase.GetAssetPath(o)));

        private static FairyToonConvertReport NewReport(Material source) => new FairyToonConvertReport
        {
            MaterialName = source == null ? "Missing material" : source.name,
            SourcePath = source == null ? "" : AssetDatabase.GetAssetPath(source),
            SourceShader = source == null || source.shader == null ? "" : source.shader.name
        };

        private static string GetOutputFolder(Material source, string output)
        {
            if (string.IsNullOrWhiteSpace(output))
            {
                string path = AssetDatabase.GetAssetPath(source);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Choose an Assets output folder for package or scene materials.");
                output = Path.GetDirectoryName(path).Replace('\\', '/') + "/FairyToon";
            }
            output = output.Replace('\\', '/').TrimEnd('/');
            if (output != "Assets" && !output.StartsWith("Assets/", StringComparison.Ordinal))
                throw new InvalidOperationException("Output folder must be inside Assets.");
            if (output.Split('/').Any(p => string.IsNullOrWhiteSpace(p) || p == "." || p == ".." || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidOperationException("Output folder contains an invalid path segment.");
            return output;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, folder.Substring(parent.Length + 1));
            if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create output folder " + folder);
        }

        private static string SafeName(string name)
        {
            foreach (char character in Path.GetInvalidFileNameChars()) name = name.Replace(character, '_');
            name = name.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "Material" : name;
        }

        private static bool Ignored(string property) => IgnoredProperties.Contains(property)
            || IgnoredPrefixes.Any(p => property.StartsWith(p, StringComparison.Ordinal)) || property.EndsWith("FA", StringComparison.Ordinal);
        private static bool GatedOff(Material source, string property)
        {
            for (int i = 0; i < FeatureGates.GetLength(0); i++)
            {
                if (!property.StartsWith(FeatureGates[i, 0], StringComparison.Ordinal)) continue;
                string toggle = FeatureGates[i, 1];
                // (_UseOutline is decided by the variant name in lilToon, not the stored float)
                if (toggle == "_UseOutline") return !Contains(source.shader.name, "Outline");
                // (lilToon's 3rd shadow is on when its colour has alpha)
                if (property.StartsWith("_Shadow3rd", StringComparison.Ordinal) && source.HasProperty("_Shadow3rdColor"))
                    return source.GetColor("_Shadow3rdColor").a <= 0f || !source.HasProperty(toggle) || source.GetFloat(toggle) <= 0.5f;
                return source.HasProperty(toggle) && source.GetFloat(toggle) <= 0.5f;
            }
            return false;
        }

        private static bool Contains(string text, string part) => text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        private static bool IsNumber(ShaderPropertyType type) => type == ShaderPropertyType.Float || type == ShaderPropertyType.Range || type == ShaderPropertyType.Int;
        private static float Number(Material material, int index) => material.shader.GetPropertyType(index) == ShaderPropertyType.Int
            ? material.GetInteger(material.shader.GetPropertyName(index)) : material.GetFloat(material.shader.GetPropertyName(index));
        private static bool Approximately(Vector4 a, Vector4 b) => Mathf.Approximately(a.x, b.x) && Mathf.Approximately(a.y, b.y)
            && Mathf.Approximately(a.z, b.z) && Mathf.Approximately(a.w, b.w);
        private static string F(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
        private static string V(Vector4 value) => $"({F(value.x)}, {F(value.y)}, {F(value.z)}, {F(value.w)})";
        private static void Add(List<string> list, string value) { if (!list.Contains(value)) list.Add(value); }
        private static string FeatureName(string property)
        {
            for (int i = 0; i < FeatureNames.GetLength(0); i++) if (FeatureNames[i, 0] == property) return FeatureNames[i, 1];
            return ObjectNames.NicifyVariableName(property.Substring(4));
        }
    }

    /// <summary>Small conversion front end with a persistent last report.</summary>
    public sealed class FairyToonConverterWindow : EditorWindow
    {
        [SerializeField] private List<Object> sources = new List<Object>();
        [SerializeField] private FairyToonConvertOptions options = new FairyToonConvertOptions();
        [SerializeField] private string lastReport = "";
        private Vector2 sourcesScroll;
        private Vector2 reportScroll;

        [MenuItem("Tools/FairyToon/Convert lilToon Materials...")]
        public static void Open() => GetWindow<FairyToonConverterWindow>("lilToon converter");

        private void OnGUI()
        {
            options = options ?? new FairyToonConvertOptions();
            EditorGUILayout.LabelField("Sources (materials, folders, prefabs or models)", EditorStyles.boldLabel);
            sourcesScroll = EditorGUILayout.BeginScrollView(sourcesScroll, GUILayout.Height(130));
            int remove = -1;
            for (int i = 0; i < sources.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                sources[i] = EditorGUILayout.ObjectField(sources[i], typeof(Object), true);
                if (GUILayout.Button("-", GUILayout.Width(24))) remove = i;
                EditorGUILayout.EndHorizontal();
            }
            if (remove >= 0) sources.RemoveAt(remove);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Add source")) sources.Add(null);
            if (GUILayout.Button("Use selection")) sources = Selection.objects.ToList();
            EditorGUILayout.EndHorizontal();
            options.InPlace = EditorGUILayout.Toggle("In place (Undo supported)", options.InPlace);
            using (new EditorGUI.DisabledScope(options.InPlace))
                options.OutputFolder = EditorGUILayout.TextField("Output folder (optional)", options.OutputFolder ?? "");
            options.TransparentHairToCutout = EditorGUILayout.Toggle("Transparent hair to Cutout", options.TransparentHairToCutout);
            options.UpgradeToEndfield = EditorGUILayout.Toggle("Upgrade to Endfield 0.3.0 PBR", options.UpgradeToEndfield);
            if (options.UpgradeToEndfield)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    options.PackPBRMaps = EditorGUILayout.Toggle("Pack PBR Maps", options.PackPBRMaps);
                    using (new EditorGUI.DisabledScope(!options.PackPBRMaps))
                        options.PBRMapOutputFolder = EditorGUILayout.TextField("PBR Map folder (optional)", options.PBRMapOutputFolder ?? "");
                    options.MetalStyle = (FairyToonEndfieldMetalStyle)EditorGUILayout.EnumPopup("Metal style", options.MetalStyle);
                    options.BoostGlitterHDR = EditorGUILayout.Toggle("Boost Glitter HDR", options.BoostGlitterHDR);
                    options.EndfieldDiffuseRamp = (Texture2D)EditorGUILayout.ObjectField("Diffuse ramp (optional)", options.EndfieldDiffuseRamp, typeof(Texture2D), false);
                    options.EndfieldSpecRamp = (Texture2D)EditorGUILayout.ObjectField("Specular ramp (optional)", options.EndfieldSpecRamp, typeof(Texture2D), false);
                }
            }
            options.UseProjectProfiles = EditorGUILayout.Toggle("Use project profiles", options.UseProjectProfiles);
            options.Profile = (FairyToonConvertProfile)EditorGUILayout.ObjectField("Profile (applied last)", options.Profile, typeof(FairyToonConvertProfile), false);
            if (GUILayout.Button("Convert"))
            {
                var materials = FairyToonConverter.CollectMaterials(sources);
                lastReport = materials.Count == 0 ? "No lilToon materials found." : FairyToonConverter.ConvertAll(materials, options).ToMarkdown();
            }
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Last report", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(lastReport)))
                if (GUILayout.Button("Copy report", GUILayout.Width(100))) EditorGUIUtility.systemCopyBuffer = lastReport;
            EditorGUILayout.EndHorizontal();
            reportScroll = EditorGUILayout.BeginScrollView(reportScroll);
            EditorGUILayout.SelectableLabel(lastReport, EditorStyles.wordWrappedLabel,
                GUILayout.Height(Mathf.Max(100, EditorStyles.wordWrappedLabel.CalcHeight(new GUIContent(lastReport), Mathf.Max(100, position.width - 35)))));
            EditorGUILayout.EndScrollView();
        }
    }
}
