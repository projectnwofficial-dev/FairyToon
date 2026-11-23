using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor
{
    /// <summary>Authors colour ramps, skin LUTs and packed Endfield PBR maps.</summary>
    public static class FairyToonTextureTools
    {
        [Serializable]
        public struct PBRChannel
        {
            public Texture texture;
            public int channel;
            public float scale;
            public float constant;
        }

        public static PBRChannel Constant(float value) => new PBRChannel { constant = value, scale = 1f };

        public static PBRChannel FromTexture(Texture texture, int channel = 0, float scale = 1f) => texture == null
            ? Constant(scale) : new PBRChannel { texture = texture, channel = channel, scale = scale };

        /// <summary>Writes sRGB gradient colours and a linear lit-weight alpha, without changing an existing GUID.</summary>
        public static Texture2D WriteRamp(string assetPath, Gradient color, AnimationCurve alpha, int width = 256)
            => Write(assetPath, BuildRamp(color, alpha, width), false);

        /// <summary>Writes the smooth / metal row at V = 0 and rough dielectric row at V = 1.</summary>
        public static Texture2D WriteSpecRamp(string assetPath, Gradient smoothRow, Gradient roughRow, int size = 256)
            => Write(assetPath, BuildSpecRamp(smoothRow, roughRow, size), false);

        /// <summary>Writes an sRGB skin colour LUT in Unity's ApplyLut2D layout, with blue slices along X.</summary>
        public static Texture2D WriteSkinLUT(string assetPath, Color tint, float saturation, float value,
            float hueShiftDegrees, int size = 32)
            => Write(assetPath, BuildSkinLUT(tint, saturation, value, hueShiftDegrees, size), false);

        /// <summary>Packs RGBA data from GPU-sampled textures or constants; source import settings are untouched.</summary>
        public static Texture2D PackPBR(string assetPath, PBRChannel metallic, PBRChannel reflectivity, PBRChannel occlusion,
            PBRChannel smoothness, int maxSize = 2048)
            => Write(assetPath, BuildPBR(metallic, reflectivity, occlusion, smoothness, maxSize), true);

        /// <summary>Packs an Endfield _PBRMap from a lilToon-style material (supporting metal or MatCap mask as metal), assigns it and applies setup.</summary>
        public static Texture2D PackPBRFromLilToonMaterial(Material material, string assetPath, float clothSmoothness = 0.25f,
            float metalSmoothness = 0.85f, bool matCapMaskAsMetal = false, float metalThreshold = 0.25f,
            float defaultReflectivity = 0.5f, int maxSize = 2048, float metalStrength = 0.85f)
        {
            if (material == null) throw new ArgumentNullException(nameof(material));
            CheckSize(maxSize);
            Texture metalTex = material.HasProperty("_MetallicGlossMap") ? material.GetTexture("_MetallicGlossMap") : null;
            bool useMatCap = !material.HasProperty("_UseMatCap") || material.GetFloat("_UseMatCap") > 0.5f;
            bool useMatCap2nd = material.HasProperty("_UseMatCap2nd") && material.GetFloat("_UseMatCap2nd") > 0.5f;
            if (metalTex == null && matCapMaskAsMetal && useMatCap && material.HasProperty("_MatCapBlendMask"))
                metalTex = material.GetTexture("_MatCapBlendMask");
            if (metalTex == null && matCapMaskAsMetal && useMatCap2nd && material.HasProperty("_MatCap2ndBlendMask"))
                metalTex = material.GetTexture("_MatCap2ndBlendMask");
            Texture smoothTex = material.HasProperty("_SmoothnessTex") ? material.GetTexture("_SmoothnessTex") : null;
            Texture aoTex = material.HasProperty("_ShadowBorderMask") ? material.GetTexture("_ShadowBorderMask") : null;
            var sources = new[] { metalTex, smoothTex, aoTex }.Where(t => t != null).Distinct().ToList();
            int width = sources.Count == 0 ? 512 : sources.Max(t => t.width);
            int height = sources.Count == 0 ? 512 : sources.Max(t => t.height);
            float factor = Mathf.Min(1f, maxSize / (float)Mathf.Max(width, height));
            width = Mathf.Max(1, Mathf.RoundToInt(width * factor));
            height = Mathf.Max(1, Mathf.RoundToInt(height * factor));

            Color[] metalPx = metalTex != null ? Sample(metalTex, width, height) : null;
            Color[] smoothPx = smoothTex != null ? Sample(smoothTex, width, height) : null;
            Color[] aoPx = aoTex != null ? Sample(aoTex, width, height) : null;

            float baseMetal = material.HasProperty("_Metallic") ? Mathf.Clamp01(material.GetFloat("_Metallic")) : 0f;
            float baseSmooth = material.HasProperty("_Smoothness") ? Mathf.Clamp01(material.GetFloat("_Smoothness")) : 1f;
            float thresholdLow = Mathf.Clamp01(metalThreshold * 0.5f);
            float thresholdHigh = Mathf.Max(thresholdLow + 1e-4f, Mathf.Clamp01(metalThreshold));
            float refl = Mathf.Clamp01(defaultReflectivity);
            float clampedMetalStrength = Mathf.Clamp01(metalStrength);

            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                float rawMask = metalPx != null
                    ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(thresholdLow, thresholdHigh, metalPx[i].r))
                    : baseMetal;
                float m = rawMask * clampedMetalStrength;
                float ao = aoPx != null ? Mathf.Clamp01(aoPx[i].r) : 1f;
                float sm = smoothPx != null
                    ? Mathf.Clamp01(smoothPx[i].r * baseSmooth)
                    : Mathf.Lerp(clothSmoothness, metalSmoothness, rawMask);
                pixels[i] = new Color(m, refl, ao, sm);
            }

            var written = Write(assetPath, TextureOf(width, height, TextureFormat.RGBA32, true, pixels), true);
            if (material.HasProperty("_PBRMap")) material.SetTexture("_PBRMap", written);
            FairyToonMaterialSetup.Apply(material);
            return written;
        }

        public static Gradient ClothRampColor() => GradientOf(
            new GradientColorKey(new Color(0.62f, 0.58f, 0.92f), 0f),
            new GradientColorKey(new Color(0.70f, 0.66f, 0.95f), 0.40f),
            new GradientColorKey(new Color(1f, 0.72f, 0.60f), 0.47f),
            new GradientColorKey(Color.white, 0.55f), new GradientColorKey(Color.white, 1f));

        public static AnimationCurve ClothRampAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.42f, 0f, 0f, 0f),
            new Keyframe(0.52f, 1f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        public static (Color tint, float saturation, float value, float hueShift) SkinLUTPreset()
            => (new Color(1f, 0.86f, 0.84f), 1.15f, 0.72f, 0f);

        // (near-neutral shadow side so the skin LUT sets the shadow hue; a soft warm band at the terminator)
        public static Gradient SkinRampColor() => GradientOf(
            new GradientColorKey(new Color(1f, 0.96f, 0.95f), 0f),
            new GradientColorKey(new Color(1f, 0.96f, 0.95f), 0.40f),
            new GradientColorKey(new Color(1f, 0.82f, 0.76f), 0.47f),
            new GradientColorKey(Color.white, 0.55f), new GradientColorKey(Color.white, 1f));

        public static AnimationCurve SkinRampAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.40f, 0f, 0f, 0f),
            new Keyframe(0.56f, 1f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        // Face: in Endfield the face shadow is one soft, light tone with a wide edge (about a sixth of the face).
        public static (Color tint, float saturation, float value, float hueShift) FaceLUTPreset()
            => (new Color(1f, 0.93f, 0.91f), 1.1f, 0.86f, 0f);

        public static Gradient FaceRampColor() => GradientOf(
            new GradientColorKey(new Color(1f, 0.97f, 0.96f), 0f),
            new GradientColorKey(new Color(1f, 0.97f, 0.96f), 0.38f),
            new GradientColorKey(new Color(1f, 0.88f, 0.83f), 0.48f),
            new GradientColorKey(Color.white, 0.60f), new GradientColorKey(Color.white, 1f));

        public static AnimationCurve FaceRampAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.36f, 0f, 0f, 0f),
            new Keyframe(0.62f, 1f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        // (warm shadow side: darkened pale hair turns olive without it; Endfield's light hair shades towards beige)
        public static Gradient HairRampColor() => GradientOf(
            new GradientColorKey(new Color(1f, 0.82f, 0.7f), 0f),
            new GradientColorKey(new Color(1f, 0.82f, 0.7f), 0.38f),
            new GradientColorKey(new Color(1f, 0.9f, 0.82f), 0.47f),
            new GradientColorKey(Color.white, 0.56f), new GradientColorKey(Color.white, 1f));

        public static AnimationCurve HairRampAlpha() => new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f), new Keyframe(0.38f, 0f, 0f, 0f),
            new Keyframe(0.56f, 1f, 0f, 0f), new Keyframe(1f, 1f, 0f, 0f));

        // (hair spec LUT: U = 1 - (T.H)^2; V = 0 front, V = 1 side / back)
        public static Gradient HairSpecRampFrontRow() => GradientOf(
            new GradientColorKey(new Color(1f, 0.93f, 0.82f), 0f), new GradientColorKey(Color.white, 1f));

        public static Gradient HairSpecRampSideRow() => GradientOf(
            new GradientColorKey(new Color(0.85f, 0.78f, 0.70f), 0f),
            new GradientColorKey(new Color(1f, 0.95f, 0.88f), 1f));

        public static Gradient SpecRampSmoothRow() => GradientOf(
            new GradientColorKey(new Color(0.90f, 0.80f, 0.70f), 0f), new GradientColorKey(Color.white, 1f));

        public static Gradient SpecRampRoughRow() => GradientOf(
            new GradientColorKey(new Color(0.80f, 0.85f, 1f), 0f), new GradientColorKey(Color.white, 1f));

        private static Gradient GradientOf(params GradientColorKey[] keys)
        {
            var gradient = new Gradient();
            gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return gradient;
        }

        internal static Texture2D BuildRamp(Gradient color, AnimationCurve alpha, int width)
        {
            if (color == null) throw new ArgumentNullException(nameof(color));
            if (alpha == null) throw new ArgumentNullException(nameof(alpha));
            CheckSize(width);
            var pixels = new Color[width];
            for (int x = 0; x < width; x++)
            {
                float u = width > 1 ? x / (float)(width - 1) : 0f;
                pixels[x] = color.Evaluate(u);
                pixels[x].a = Mathf.Clamp01(alpha.Evaluate(u));
            }
            // (SetPixels / EncodeToPNG keep authored sRGB values as-is; the importer handles decoding)
            return TextureOf(width, 1, TextureFormat.RGBA32, false, pixels);
        }

        internal static Texture2D BuildSpecRamp(Gradient smoothRow, Gradient roughRow, int size)
        {
            if (smoothRow == null) throw new ArgumentNullException(nameof(smoothRow));
            if (roughRow == null) throw new ArgumentNullException(nameof(roughRow));
            CheckSize(size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = size > 1 ? x / (float)(size - 1) : 0f;
                float v = size > 1 ? y / (float)(size - 1) : 0f;
                pixels[y * size + x] = Color.Lerp(smoothRow.Evaluate(u), roughRow.Evaluate(u), v);
            }
            return TextureOf(size, size, TextureFormat.RGB24, false, pixels);
        }

        internal static Texture2D BuildSkinLUT(Color tint, float saturation, float value, float hueShiftDegrees, int size)
        {
            CheckSize(size);
            if (size < 2 || size > SystemInfo.maxTextureSize / size)
                throw new ArgumentOutOfRangeException(nameof(size), "LUT size squared must fit the maximum texture width.");
            int width = size * size;
            var pixels = new Color[width * size];
            for (int slice = 0; slice < size; slice++)
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var input = new Color(x / (float)(size - 1), y / (float)(size - 1), slice / (float)(size - 1));
                Color.RGBToHSV(input, out float h, out float s, out float v);
                var color = Color.HSVToRGB(Mathf.Repeat(h + hueShiftDegrees / 360f, 1f),
                    Mathf.Clamp01(s * saturation), Mathf.Clamp01(v * value)) * tint;
                color.a = 1f;
                // (SetPixels row 0 is the bottom: red runs inside each blue slice, green runs up)
                pixels[y * width + slice * size + x] = color;
            }
            return TextureOf(width, size, TextureFormat.RGBA32, false, pixels);
        }

        internal static Texture2D BuildPBR(PBRChannel metallic, PBRChannel reflectivity, PBRChannel occlusion,
            PBRChannel smoothness, int maxSize)
        {
            CheckSize(maxSize);
            var channels = new[] { metallic, reflectivity, occlusion, smoothness };
            foreach (var channel in channels)
                if (channel.texture != null && (channel.channel < 0 || channel.channel > 3))
                    throw new ArgumentOutOfRangeException(nameof(channel.channel), "Source channel must be R, G, B or A.");
            var sources = channels.Where(c => c.texture != null).Select(c => c.texture).Distinct().ToList();
            int width = sources.Count == 0 ? 1024 : sources.Max(t => t.width);
            int height = sources.Count == 0 ? 1024 : sources.Max(t => t.height);
            float factor = Mathf.Min(1f, maxSize / (float)Mathf.Max(width, height));
            width = Mathf.Max(1, Mathf.RoundToInt(width * factor));
            height = Mathf.Max(1, Mathf.RoundToInt(height * factor));
            var pixels = new Color[width * height];
            for (int c = 0; c < channels.Length; c++)
                if (channels[c].texture == null)
                    for (int i = 0; i < pixels.Length; i++) pixels[i][c] = Mathf.Clamp01(channels[c].constant);
            foreach (var source in sources)
            {
                var sampled = Sample(source, width, height);
                for (int c = 0; c < channels.Length; c++)
                {
                    var channel = channels[c];
                    if (channel.texture != source) continue;
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i][c] = Mathf.Clamp01(sampled[i][channel.channel] * channel.scale);
                }
            }
            return TextureOf(width, height, TextureFormat.RGBA32, true, pixels);
        }

        private static Color[] Sample(Texture source, int width, int height)
        {
            RenderTexture previous = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture target = null;
            Texture2D readable = null;
            try
            {
                // (linear target prevents a second sRGB encoding; texture sampling decodes colour sources on the GPU)
                target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                GL.sRGBWrite = false;
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                readable = new Texture2D(width, height, TextureFormat.RGBAHalf, false, true);
                readable.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                readable.Apply(false, false);
                return readable.GetPixels();
            }
            finally
            {
                RenderTexture.active = previous;
                GL.sRGBWrite = previousSrgb;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (readable != null) Object.DestroyImmediate(readable);
            }
        }

        private static Texture2D TextureOf(int width, int height, TextureFormat format, bool linear, Color[] pixels)
        {
            var texture = new Texture2D(width, height, format, false, linear)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void CheckSize(int size)
        {
            if (size < 1 || size > SystemInfo.maxTextureSize) throw new ArgumentOutOfRangeException(nameof(size));
        }

        private static Texture2D Write(string assetPath, Texture2D texture, bool data)
        {
            try
            {
                assetPath = (assetPath ?? "").Replace('\\', '/');
                if (!assetPath.StartsWith("Assets/", StringComparison.Ordinal)
                    || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                    || assetPath.Split('/').Any(p => string.IsNullOrWhiteSpace(p) || p == "." || p == ".."
                        || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new ArgumentException("Choose a PNG asset path inside Assets.", nameof(assetPath));
                EnsureFolder(Path.GetDirectoryName(assetPath).Replace('\\', '/'));
                File.WriteAllBytes(assetPath, texture.EncodeToPNG());
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter
                    ?? throw new InvalidOperationException("Texture importer missing: " + assetPath);
                int maxSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(texture.width, texture.height)));
                var compression = data ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
                var format = data ? TextureImporterFormat.BC7 : TextureImporterFormat.RGBA32;
                var platform = importer.GetPlatformTextureSettings("Standalone");
                bool changed = importer.textureType != TextureImporterType.Default || importer.sRGBTexture == data
                    || importer.wrapMode != (data ? TextureWrapMode.Repeat : TextureWrapMode.Clamp)
                    || importer.filterMode != FilterMode.Bilinear || importer.mipmapEnabled != data || importer.isReadable
                    || importer.textureCompression != compression || importer.crunchedCompression || importer.alphaIsTransparency
                    || importer.alphaSource != TextureImporterAlphaSource.FromInput || importer.npotScale != TextureImporterNPOTScale.None
                    || importer.maxTextureSize != maxSize || !platform.overridden || platform.format != format
                    || platform.maxTextureSize != maxSize || platform.textureCompression != compression
                    || platform.crunchedCompression;
                if (changed)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.sRGBTexture = !data;
                    importer.wrapMode = data ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                    importer.filterMode = FilterMode.Bilinear;
                    importer.mipmapEnabled = data;
                    importer.isReadable = false;
                    importer.alphaIsTransparency = false;
                    importer.alphaSource = TextureImporterAlphaSource.FromInput;
                    importer.npotScale = TextureImporterNPOTScale.None;
                    importer.maxTextureSize = maxSize;
                    importer.textureCompression = compression;
                    importer.crunchedCompression = false;
                    platform.name = "Standalone";
                    platform.overridden = true;
                    platform.format = format;
                    platform.maxTextureSize = maxSize;
                    platform.textureCompression = compression;
                    platform.crunchedCompression = false;
                    importer.SetPlatformTextureSettings(platform);
                    importer.SaveAndReimport();
                }
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath)
                    ?? throw new InvalidOperationException("Texture missing after import: " + assetPath);
            }
            finally { Object.DestroyImmediate(texture); }
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = folder.Substring(0, folder.LastIndexOf('/'));
            EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, folder.Substring(parent.Length + 1));
            if (string.IsNullOrEmpty(guid)) throw new IOException("Could not create output folder " + folder);
        }
    }

    /// <summary>Small IMGUI front end for colour ramps, PBR channel packing and skin LUTs.</summary>
    public sealed class FairyToonTextureToolsWindow : EditorWindow
    {
        [SerializeField] private int tab;
        [SerializeField] private Gradient color = FairyToonTextureTools.ClothRampColor();
        [SerializeField] private AnimationCurve alpha = FairyToonTextureTools.ClothRampAlpha();
        [SerializeField] private Gradient smoothRow = FairyToonTextureTools.SpecRampSmoothRow();
        [SerializeField] private Gradient roughRow = FairyToonTextureTools.SpecRampRoughRow();
        [SerializeField] private FairyToonTextureTools.PBRChannel metallic = FairyToonTextureTools.Constant(0f);
        [SerializeField] private FairyToonTextureTools.PBRChannel reflectivity = FairyToonTextureTools.Constant(1f);
        [SerializeField] private FairyToonTextureTools.PBRChannel occlusion = FairyToonTextureTools.Constant(1f);
        [SerializeField] private FairyToonTextureTools.PBRChannel smoothness = FairyToonTextureTools.Constant(0.3f);
        [SerializeField] private int pbrMaxSize = 2048;
        [SerializeField] private Color skinTint = FairyToonTextureTools.SkinLUTPreset().tint;
        [SerializeField] private float skinSaturation = FairyToonTextureTools.SkinLUTPreset().saturation;
        [SerializeField] private float skinValue = FairyToonTextureTools.SkinLUTPreset().value;
        [SerializeField] private float skinHueShift = FairyToonTextureTools.SkinLUTPreset().hueShift;
        private Texture2D preview;
        private string error;
        private static readonly string[] Channels = { "R", "G", "B", "A" };

        [MenuItem("Tools/FairyToon/Texture Tools")]
        public static void Open() => GetWindow<FairyToonTextureToolsWindow>("Texture Tools");

        private void OnDisable()
        {
            if (preview != null) DestroyImmediate(preview);
            preview = null;
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            tab = GUILayout.Toolbar(tab, new[] { "Ramp", "Spec Ramp", "Pack PBR", "Skin LUT" });
            EditorGUILayout.Space();
            if (tab == 0)
            {
                color = EditorGUILayout.GradientField("Shadow tint (sRGB)", color);
                alpha = EditorGUILayout.CurveField("Lit weight (alpha)", alpha);
            }
            else if (tab == 1)
            {
                smoothRow = EditorGUILayout.GradientField("Smooth / metal (V = 0)", smoothRow);
                roughRow = EditorGUILayout.GradientField("Rough dielectric (V = 1)", roughRow);
            }
            else if (tab == 2)
            {
                metallic = ChannelRow("R: Metallic", metallic);
                reflectivity = ChannelRow("G: Reflectivity", reflectivity);
                occlusion = ChannelRow("B: Occlusion", occlusion);
                smoothness = ChannelRow("A: Smoothness", smoothness);
                pbrMaxSize = EditorGUILayout.IntPopup("Max size", pbrMaxSize, new[] { "256", "512", "1024", "2048", "4096" },
                    new[] { 256, 512, 1024, 2048, 4096 });
            }
            else
            {
                skinTint = EditorGUILayout.ColorField("Tint (sRGB)", skinTint);
                skinSaturation = EditorGUILayout.FloatField("Saturation", skinSaturation);
                skinValue = EditorGUILayout.FloatField("Value", skinValue);
                skinHueShift = EditorGUILayout.FloatField("Hue shift (degrees)", skinHueShift);
            }
            bool changed = EditorGUI.EndChangeCheck();
            if (changed || (preview == null && error == null))
            {
                OnDisable();
                try
                {
                    preview = tab == 0 ? FairyToonTextureTools.BuildRamp(color, alpha, 256)
                        : tab == 1 ? FairyToonTextureTools.BuildSpecRamp(smoothRow, roughRow, 256)
                        : tab == 2 ? FairyToonTextureTools.BuildPBR(metallic, reflectivity, occlusion, smoothness, 256)
                        : FairyToonTextureTools.BuildSkinLUT(skinTint, skinSaturation, skinValue, skinHueShift, 32);
                    error = null;
                }
                catch (Exception e) { error = e.Message; }
            }
            EditorGUILayout.Space();
            if (preview != null)
            {
                Rect rect = GUILayoutUtility.GetRect(64, tab == 0 ? 64 : 256, GUILayout.ExpandWidth(true));
                EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (GUILayout.Button("Save As...")) Save();
        }

        private static FairyToonTextureTools.PBRChannel ChannelRow(string label, FairyToonTextureTools.PBRChannel value)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            value.texture = (Texture)EditorGUILayout.ObjectField("Texture", value.texture, typeof(Texture), false);
            if (value.texture != null)
            {
                value.channel = EditorGUILayout.Popup("Channel", value.channel, Channels);
                value.scale = EditorGUILayout.FloatField("Scale", value.scale);
            }
            else value.constant = EditorGUILayout.FloatField("Constant", value.constant);
            return value;
        }

        private void Save()
        {
            string path = EditorUtility.SaveFilePanelInProject("Save FairyToon texture",
                tab == 0 ? "Ramp_Cloth_RD" : tab == 1 ? "Ramp_Cloth_RS" : tab == 2 ? "PBR" : "Skin_LUT",
                "png", "Choose a texture asset path.");
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (tab == 0) FairyToonTextureTools.WriteRamp(path, color, alpha);
                else if (tab == 1) FairyToonTextureTools.WriteSpecRamp(path, smoothRow, roughRow);
                else if (tab == 2) FairyToonTextureTools.PackPBR(path, metallic, reflectivity, occlusion, smoothness, pbrMaxSize);
                else FairyToonTextureTools.WriteSkinLUT(path, skinTint, skinSaturation, skinValue, skinHueShift);
                error = null;
                Debug.Log("[FairyToon] saved texture " + path);
            }
            catch (Exception e) { error = e.Message; Debug.LogException(e); }
        }
    }
}
