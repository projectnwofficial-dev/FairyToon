using UnityEngine;
using UnityEngine.Rendering;

namespace NW.FairyToon.Editor
{
    /// <summary>Derives a FairyToon material's keywords, passes, blend state and queue from its properties.</summary>
    /// <remarks>Called by the inspector on every change; tools that set properties from script call it too.</remarks>
    public static class FairyToonMaterialSetup
    {
        // Rendering modes select a shader; _TransparentMode remains coverage (0/1/2) in every shader.
        public const int Opaque = 0, Cutout = 1, Transparent = 2, Refraction = 3, Fur = 4, Gem = 5;
        public const int FurTransparent = 0, FurCutout = 1, FurTwoPass = 2;
        public const string RefractionShaderName = "FairyToon/Refraction";
        public const string FurShaderName = "FairyToon/Fur";
        public const string GemShaderName = "FairyToon/Gem";
        internal const string ManagedHairQueueTag = "FairyToonManagedHairQueue";
        public const int RoleNone = 0, RoleFace = 1, RoleHair = 2;
        public const int ShadingLilToon = 0, ShadingEndfield = 1;
        public const int PartCloth = 0, PartSkin = 1, PartFace = 2, PartHair = 3, PartEye = 4;

        public static bool IsFairyToonShader(Shader shader) => shader != null
            && (shader.name == FairyToon.ShaderName || shader.name == RefractionShaderName
                || shader.name == FurShaderName || shader.name == GemShaderName);

        public static int GetRenderingMode(Material material)
        {
            if (material == null || material.shader == null) return Opaque;
            if (material.shader.name == RefractionShaderName) return Refraction;
            if (material.shader.name == FurShaderName) return Fur;
            if (material.shader.name == GemShaderName) return Gem;
            return Mathf.Clamp(Mathf.RoundToInt(Get(material, "_TransparentMode")), Opaque, Transparent);
        }

        public static string GetShaderName(int renderingMode)
        {
            switch (renderingMode)
            {
                case Opaque: case Cutout: case Transparent: return FairyToon.ShaderName;
                case Refraction: return RefractionShaderName;
                case Fur: return FurShaderName;
                case Gem: return GemShaderName;
                default: return null;
            }
        }

        public static int GetDefaultRenderQueue(int renderingMode, int furMode = FurTransparent)
        {
            switch (renderingMode)
            {
                case Cutout: return (int)RenderQueue.AlphaTest;
                case Transparent: return (int)RenderQueue.Transparent;
                case Refraction: case Gem: return (int)RenderQueue.Transparent - 100;
                case Fur: return furMode == FurCutout ? (int)RenderQueue.AlphaTest : (int)RenderQueue.Transparent;
                default: return (int)RenderQueue.Geometry;
            }
        }

        /// <summary>Swaps variants while retaining saved named values, including textures and their scale/offset.</summary>
        /// <summary>Same default-queue rule as <see cref="SetRenderingMode"/> when coverage changes on the main shader.</summary>
        public static void MigrateMainCoverageQueue(Material material, int previousMode, int previousQueue, int previousRawQueue)
        {
            if (material == null || !IsFairyToonShader(material.shader)) return;
            int mode = GetRenderingMode(material);
            if (previousMode > Transparent || mode > Transparent || previousMode == mode) return;
            bool useDefaultQueue = previousRawQueue < 0 || previousQueue == GetDefaultRenderQueue(previousMode);
            // -1 is the native From Shader value. A mode change that wants the canonical queue sets it explicitly.
            material.renderQueue = useDefaultQueue ? GetDefaultRenderQueue(mode) : previousQueue;
        }

        /// <summary>Rebuilds the current mode's queue, instancing and keywords. Unlike <see cref="SetRenderingMode"/>, the same mode is not a no-op.</summary>
        public static void ResetRendering(Material material)
        {
            if (material == null || !IsFairyToonShader(material.shader)) return;
            int mode = GetRenderingMode(material);
            int furMode = Mathf.Clamp(Mathf.RoundToInt(Get(material, "_FurMode")), FurTransparent, FurTwoPass);
            material.enableInstancing = false;
            material.renderQueue = GetDefaultRenderQueue(mode, furMode);
            material.SetFloat("_ZTest", (int)CompareFunction.LessEqual);
            material.SetFloat("_OffsetFactor", 0f);
            material.SetFloat("_OffsetUnits", 0f);
            // Gem's shader and lilToon both default ZWrite to 0. Every other mode starts at 1.
            material.SetFloat("_ZWrite", mode == Gem ? 0f : 1f);
            if (mode == Gem && material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            // Fur and Gem hide the Outline section but keep its saved values. Reset must not change them.
            if (mode != Fur && mode != Gem && material.HasProperty("_OutlineCull")) material.SetFloat("_OutlineCull", 1f);
            if (mode == Fur)
            {
                material.SetFloat("_FurZTest", (int)CompareFunction.LessEqual);
                material.SetFloat("_FurOffsetFactor", 0f);
                material.SetFloat("_FurOffsetUnits", 0f);
                material.SetFloat("_FurColorMask", 15f);
                material.SetFloat("_FurBlendOp", (int)BlendOp.Add);
                material.SetFloat("_FurBlendOpAlpha", (int)BlendOp.Add);
                SetFurShellDefaults(material, furMode);
            }
            Apply(material);
        }

        public static bool SetRenderingMode(Material material, int renderingMode)
        {
            if (material == null || !IsFairyToonShader(material.shader)) return false;
            int previousMode = GetRenderingMode(material);
            if (previousMode == renderingMode) return true;
            string shaderName = GetShaderName(renderingMode);
            var shader = shaderName == null ? null : Shader.Find(shaderName);
            if (shader == null) return false;
            int previousQueue = material.renderQueue;
            int furMode = Mathf.Clamp(Mathf.RoundToInt(Get(material, "_FurMode")), FurTransparent, FurTwoPass);
            bool useDefaultQueue = material.rawRenderQueue < 0
                || previousQueue == GetDefaultRenderQueue(previousMode, furMode);
            if (material.shader != shader)
            {
                // CopyPropertiesFromMaterial includes saved special properties while the main shader hides them.
                var snapshot = new Material(material) { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    material.shader = shader;
                    material.CopyPropertiesFromMaterial(snapshot);
                    material.shader = shader;
                    material.shaderKeywords = System.Array.Empty<string>();
                    // Transparent special variants disable these; restore the main shader's existing defaults.
                    material.SetShaderPassEnabled("ShadowCaster", true);
                    material.SetShaderPassEnabled(FairyToon.CharacterShadowLightMode, true);
                }
                finally { Object.DestroyImmediate(snapshot); }
            }
            if (renderingMode <= Transparent) material.SetFloat("_TransparentMode", renderingMode);
            // A mode change restores that mode's depth default. A later edit of ZWrite is left alone by Apply.
            if (renderingMode == Opaque || renderingMode == Cutout)
                material.SetFloat("_ZWrite", 1f);
            else if (renderingMode == Gem)
                material.SetFloat("_ZWrite", 0f);
            // Explicit transitions migrate canonical defaults; Apply also repairs incompatible coverage queues.
            material.renderQueue = useDefaultQueue ? GetDefaultRenderQueue(renderingMode, furMode) : previousQueue;
            if (renderingMode == Fur) SetFurShellDefaults(material, furMode);
            Apply(material);
            return true;
        }

        public static bool SetFurMode(Material material, int furMode)
        {
            if (material == null || GetRenderingMode(material) != Fur
                || furMode < FurTransparent || furMode > FurTwoPass) return false;
            int previousMode = Mathf.Clamp(Mathf.RoundToInt(Get(material, "_FurMode")), FurTransparent, FurTwoPass);
            if (previousMode == furMode) return true;
            bool useDefaultQueue = material.rawRenderQueue < 0
                || material.renderQueue == GetDefaultRenderQueue(Fur, previousMode);
            material.SetFloat("_FurMode", furMode);
            if (useDefaultQueue) material.renderQueue = GetDefaultRenderQueue(Fur, furMode);
            SetFurShellDefaults(material, furMode);
            Apply(material);
            return true;
        }

        // The Cutout shell and the TwoPass first shell write depth and alpha to coverage.
        internal static void SetFurShellDefaults(Material material, int furMode)
        {
            bool writeShellDepth = furMode == FurCutout || furMode == FurTwoPass;
            if (material.HasProperty("_FurZWrite")) material.SetFloat("_FurZWrite", writeShellDepth ? 1f : 0f);
            if (material.HasProperty("_FurAlphaToMask")) material.SetFloat("_FurAlphaToMask", writeShellDepth ? 1f : 0f);
            if (material.HasProperty("_FurSrcBlend"))
                material.SetInt("_FurSrcBlend", furMode == FurTransparent ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
            if (material.HasProperty("_FurDstBlend"))
                material.SetInt("_FurDstBlend", furMode == FurTransparent ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
            if (material.HasProperty("_FurSrcBlendAlpha")) material.SetInt("_FurSrcBlendAlpha", (int)BlendMode.One);
            if (material.HasProperty("_FurDstBlendAlpha")) material.SetInt("_FurDstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
        }

        internal static void SetCoverageBlends(Material material)
        {
            if (material == null || !IsFairyToonShader(material.shader)) return;
            int mode = Mathf.RoundToInt(Get(material, "_TransparentMode"));
            bool gem = GetRenderingMode(material) == Gem;
            bool fur = GetRenderingMode(material) == Fur;
            int furMode = Mathf.Clamp(Mathf.RoundToInt(Get(material, "_FurMode")), FurTransparent, FurTwoPass);
            if (gem)
            {
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.One);
                return;
            }
            if (fur)
            {
                material.SetInt("_SrcBlend", furMode == FurTransparent || mode == Transparent ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
                material.SetInt("_DstBlend", furMode == FurTransparent || mode == Transparent ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
                material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                return;
            }
            if (mode == Transparent)
            {
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                return;
            }
            material.SetInt("_SrcBlend", (int)BlendMode.One);
            material.SetInt("_DstBlend", (int)BlendMode.Zero);
            material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
            material.SetInt("_DstBlendAlpha", (int)BlendMode.Zero);
        }

        public static void Apply(Material material, bool writeCoverageBlends = true)
        {
            if (material == null || !IsFairyToonShader(material.shader)) return;

            int renderingMode = GetRenderingMode(material);
            bool special = renderingMode >= Refraction;
            bool fur = renderingMode == Fur;
            bool gem = renderingMode == Gem;
            int furMode = Mathf.Clamp(Mathf.RoundToInt(Get(material, "_FurMode")), FurTransparent, FurTwoPass);
            bool furTwoPass = fur && furMode == FurTwoPass;
            // Read the previous Apply state before the coverage keyword is updated below.
            bool managedHairQueue = IsManagedHairQueue(material);
            // rawRenderQueue < 0 is Unity's From Shader. Leave it so the popup can stay on that choice.
            bool inheritQueue = material.rawRenderQueue < 0;
            if (special)
            {
                material.SetFloat("_TransparentMode", fur && furMode == FurCutout ? Cutout : Transparent);
            }
            int mode = Mathf.RoundToInt(Get(material, "_TransparentMode"));
            // Explicit queues, including a custom Geometry value on a Transparent material, stay put.
            // Only the managed see-through hair queue moves when coverage becomes Transparent.
            // Mode switches set their own queue in SetRenderingMode before this runs.
            if (!special && !inheritQueue && managedHairQueue && mode == Transparent)
                material.renderQueue = (int)RenderQueue.Transparent;
            SetKeyword(material, "_ALPHATEST_ON", mode == Cutout);
            SetKeyword(material, "_SURFACE_TYPE_TRANSPARENT", mode == Transparent);
            if (mode == Transparent)
            {
                if (writeCoverageBlends)
                {
                    material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                    material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                }
                material.SetInt("_AlphaToMask", 0);
                material.SetOverrideTag("RenderType", "Transparent");
            }
            else
            {
                if (writeCoverageBlends)
                {
                    material.SetInt("_SrcBlend", (int)BlendMode.One);
                    material.SetInt("_DstBlend", (int)BlendMode.Zero);
                    material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                    material.SetInt("_DstBlendAlpha", (int)BlendMode.Zero);
                }
                if (mode == Opaque) material.SetInt("_AlphaToMask", 0);
                material.SetOverrideTag("RenderType", mode == Cutout ? "TransparentCutout" : "Opaque");
            }
            if (gem && writeCoverageBlends)
            {
                // PRE clears the silhouette; every front/interior face then accumulates, as in lilToon Gem.
                material.SetInt("_SrcBlend", (int)BlendMode.One);
                material.SetInt("_DstBlend", (int)BlendMode.One);
            }
            // Two-pass Fur keeps a transparent body and clips only its first shell pass.
            if (furTwoPass)
            {
                material.SetInt("_AlphaToMask", 0);
                material.SetOverrideTag("RenderType", "Transparent");
            }
            if (fur)
            {
                // The base surface and shell geometry have independent render states, as in lilToon's URP Fur.
                // lilToon's LIL_RENDER 2 body returns premultiplied RGB under One/OneMinusSrcAlpha.
                // FairyToon returns straight RGB, so SrcAlpha produces the same framebuffer contribution.
                if (writeCoverageBlends)
                {
                    material.SetInt("_SrcBlend", mode == Transparent ? (int)BlendMode.SrcAlpha : (int)BlendMode.One);
                    material.SetInt("_DstBlend", mode == Transparent ? (int)BlendMode.OneMinusSrcAlpha : (int)BlendMode.Zero);
                    material.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                    material.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                }
                material.SetInt("_ZWrite", 1);
                material.SetInt("_AlphaToMask", furMode == FurCutout ? 1 : 0);
            }
            // (transparent surfaces stay out of the depth prepass, SSAO normals and object motion)
            bool depthPasses = fur || mode != Transparent;
            material.SetShaderPassEnabled("DepthOnly", depthPasses);
            material.SetShaderPassEnabled("DepthNormals", depthPasses);
            material.SetShaderPassEnabled("MotionVectors", depthPasses);
            material.SetShaderPassEnabled(FairyToon.CharacterMaskLightMode, depthPasses);
            if (special)
            {
                bool shadows = fur;
                material.SetShaderPassEnabled("ShadowCaster", shadows);
                material.SetShaderPassEnabled(FairyToon.CharacterShadowLightMode, shadows);
            }
            if (special) SetKeyword(material, "_FT_REFRACTION_BLUR", renderingMode == Refraction && On(material, "_RefractionBlur"));

            // (Endfield replaces toon shadow; lilToon surface layers work in either shading model)
            bool endfield = Mathf.RoundToInt(Get(material, "_ShadingModel")) == ShadingEndfield;
            SetKeyword(material, "_FT_ENDFIELD", endfield);
            // The baked frame selects iris islands independently of the shading model or Endfield part.
            SetKeyword(material, "_FT_EF_IRIS", On(material, "_EFIris"));
            SetKeyword(material, "_FT_WET", On(material, "_UseWet"));
            int part = endfield ? Mathf.RoundToInt(Get(material, "_EFPart")) : PartCloth;
            SetKeyword(material, "_FT_EF_SKIN", part == PartSkin);
            SetKeyword(material, "_FT_EF_FACE", part == PartFace);
            SetKeyword(material, "_FT_EF_HAIR", part == PartHair);
            SetKeyword(material, "_FT_EF_EYE", part == PartEye);
            SetFlag(material, "_EFPBRMapOn", material.HasProperty("_PBRMap") && material.GetTexture("_PBRMap") != null);
            SetFlag(material, "_EFRampOn", material.HasProperty("_DiffuseRamp") && material.GetTexture("_DiffuseRamp") != null);
            SetFlag(material, "_EFSkinLUTOn", material.HasProperty("_SkinLUT") && material.GetTexture("_SkinLUT") != null);

            SetKeyword(material, "_NORMALMAP", On(material, "_UseBumpMap") && material.GetTexture("_BumpMap") != null);
            bool normal2nd = On(material, "_UseBump2ndMap") && material.GetTexture("_Bump2ndMap") != null;
            bool normal2ndUV = normal2nd && Get(material, "_Bump2ndMap_UVMode") > 0.5f;
            SetKeyword(material, "_FT_NORMAL2ND", normal2nd && !normal2ndUV);
            SetKeyword(material, "_FT_NORMAL2ND_UV", normal2ndUV);
            SetKeyword(material, "_FT_ANISOTROPY", On(material, "_UseAnisotropy"));
            SetKeyword(material, "_FT_BACKLIGHT", On(material, "_UseBacklight"));
            bool glitter = On(material, "_UseGlitter");
            bool glitterUV = glitter && (Get(material, "_GlitterUVMode") > 0.5f || Get(material, "_GlitterColorTex_UVMode") > 0.5f);
            SetKeyword(material, "_FT_GLITTER", glitter && !glitterUV);
            SetKeyword(material, "_FT_GLITTER_UV", glitterUV);
            SetKeyword(material, "_FT_SHADOW_RAMP", On(material, "_UseShadowRamp") && !endfield);
            SetKeyword(material, "_FT_REFLECTION", On(material, "_UseReflection"));
            // Both custom-normal layers use the existing MatCap variant, independently of _NORMALMAP.
            // Keep authored custom-normal toggles when no texture is assigned: the shader default is flat bump.
            SetKeyword(material, "_FT_MATCAP", On(material, "_UseMatCap") || On(material, "_UseMatCap2nd"));
            SetKeyword(material, "_FT_RIM", On(material, "_UseRim"));
            SetKeyword(material, "_FT_RIMSHADE", On(material, "_UseRimShade"));
            SetKeyword(material, "_FT_MAIN_LAYERS", On(material, "_UseMain2ndTex") || On(material, "_UseMain3rdTex"));
            SetKeyword(material, "_FT_EMISSION", On(material, "_UseEmission") || On(material, "_UseEmission2nd"));
            bool outline = On(material, "_UseOutline");
            SetKeyword(material, "_FT_OUTLINE", outline && !fur && !gem);
            // Fur reserves this tag for its body; Gem reserves it for the black PRE draw.
            material.SetShaderPassEnabled("SRPDefaultUnlit", fur || gem || outline);

            if (special)
            {
                // Special shaders have no see-through passes. FurSecond shares the main mask's LightMode.
                material.SetShaderPassEnabled(FairyToon.FakeShadowLightMode, false);
                material.SetShaderPassEnabled(FairyToon.SeeThroughEarlyZLightMode, false);
                material.SetShaderPassEnabled(FairyToon.SeeThroughHairLightMode, false);
                material.SetShaderPassEnabled(FairyToon.SeeThroughMaskLightMode, furTwoPass);
                ResetManagedStencil(material, true);
                SetHairQueueTag(material, false);
            }
            else ApplySeeThrough(material, mode, managedHairQueue);
        }

        private static bool IsManagedHairQueue(Material material)
        {
            if (material.renderQueue != (int)RenderQueue.AlphaTest + 10) return false;
            string provenance = material.GetTag(ManagedHairQueueTag, false, "");
            // Older materials predate the tag. Only a prior opaque/cutout Hair Apply can own 2460.
            return provenance == "1" || (provenance == ""
                && !material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")
                && material.GetShaderPassEnabled(FairyToon.SeeThroughHairLightMode)
                && !material.GetShaderPassEnabled(FairyToon.SeeThroughMaskLightMode));
        }

        private static void ApplySeeThrough(Material material, int mode, bool managedHairQueue)
        {
            int role = Mathf.RoundToInt(Get(material, "_SeeThroughRole"));
            bool face = role == RoleFace;
            bool hair = role == RoleHair;
            material.SetShaderPassEnabled(FairyToon.SeeThroughMaskLightMode, face && mode != Transparent
                && material.HasProperty("_SeeThroughMask") && material.GetTexture("_SeeThroughMask") != null);
            material.SetShaderPassEnabled(FairyToon.FakeShadowLightMode, hair && On(material, "_UseFakeShadow"));
            material.SetShaderPassEnabled(FairyToon.SeeThroughEarlyZLightMode, hair && On(material, "_SeeThroughEarlyZ"));
            material.SetShaderPassEnabled(FairyToon.SeeThroughHairLightMode, hair);

            if (face || hair)
            {
                material.SetInt("_StencilRef", face ? FairyToon.FaceBit : FairyToon.SeeThroughBit);
                material.SetInt("_StencilReadMask", face ? 255 : FairyToon.SeeThroughBit);
                material.SetInt("_StencilWriteMask", FairyToon.FaceBit);
                material.SetInt("_StencilComp", (int)(face ? CompareFunction.Always : CompareFunction.NotEqual));
                material.SetInt("_StencilPass", (int)(face ? StencilOp.Replace : StencilOp.Zero));
                material.SetInt("_StencilFail", (int)StencilOp.Keep);
                material.SetInt("_StencilZFail", (int)StencilOp.Keep);
            }
            else ResetManagedStencil(material);

            int hairQueue = (int)RenderQueue.AlphaTest + 10;
            // An explicit queue below 2460 is the managed hair slot. From Shader (raw -1) stays inherited.
            if (hair && mode != Transparent && material.rawRenderQueue >= 0 && material.renderQueue < hairQueue)
            {
                material.renderQueue = hairQueue;
                managedHairQueue = true;
            }
            else if (role == RoleNone && managedHairQueue && material.renderQueue == hairQueue)
                material.renderQueue = mode == Cutout ? (int)RenderQueue.AlphaTest
                    : mode == Transparent ? (int)RenderQueue.Transparent : (int)RenderQueue.Geometry;
            SetHairQueueTag(material, managedHairQueue && material.renderQueue == hairQueue);
        }

        // Writes the hair-queue provenance only where it matters (queue 2460, or a tag already present), so applying
        // setup to an ordinary material does not add a tag to its .mat file. "0" records an authored 2460 so later role
        // changes cannot mistake it for legacy managed state.
        private static void SetHairQueueTag(Material material, bool managed)
        {
            bool relevant = material.renderQueue == (int)RenderQueue.AlphaTest + 10
                || material.GetTag(ManagedHairQueueTag, false, "") != "";
            if (managed) material.SetOverrideTag(ManagedHairQueueTag, "1");
            else if (relevant) material.SetOverrideTag(ManagedHairQueueTag, "0");
        }

        private static void ResetManagedStencil(Material material, bool exact = false)
        {
            if (exact && (Get(material, "_StencilFail") != (int)StencilOp.Keep
                || Get(material, "_StencilZFail") != (int)StencilOp.Keep)) return;
            if (!StencilIs(material, FairyToon.FaceBit, 255, CompareFunction.Always, StencilOp.Replace)
                && !StencilIs(material, FairyToon.SeeThroughBit, FairyToon.SeeThroughBit, CompareFunction.NotEqual, StencilOp.Zero)) return;
            // A removed or unavailable role hands its generated stencil state back to the normal defaults.
            material.SetInt("_StencilRef", 0);
            material.SetInt("_StencilReadMask", 255);
            material.SetInt("_StencilWriteMask", 255);
            material.SetInt("_StencilComp", (int)CompareFunction.Always);
            material.SetInt("_StencilPass", (int)StencilOp.Keep);
        }

        private static bool StencilIs(Material material, int reference, int readMask, CompareFunction comp, StencilOp pass) =>
            material.HasProperty("_StencilRef") && Mathf.RoundToInt(material.GetFloat("_StencilRef")) == reference
            && Mathf.RoundToInt(material.GetFloat("_StencilReadMask")) == readMask
            && Mathf.RoundToInt(material.GetFloat("_StencilWriteMask")) == FairyToon.FaceBit
            && Mathf.RoundToInt(material.GetFloat("_StencilComp")) == (int)comp
            && Mathf.RoundToInt(material.GetFloat("_StencilPass")) == (int)pass;

        private static float Get(Material material, string name) => material.HasProperty(name) ? material.GetFloat(name) : 0f;
        private static bool On(Material material, string name) => Get(material, name) > 0.5f;

        private static void SetKeyword(Material material, string keyword, bool on)
        {
            if (on) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        // (hidden float the shader branches on; written only when it changes, so lilToon-mode materials stay untouched)
        private static void SetFlag(Material material, string name, bool on)
        {
            if (!material.HasProperty(name)) return;
            float value = on ? 1f : 0f;
            if (material.GetFloat(name) != value) material.SetFloat(name, value);
        }
    }
}
