using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace NW.FairyToon.Editor.Tests
{
    public class FairyToonClipboardTests
    {
        private readonly List<Object> m_Objects = new List<Object>();

        [SetUp]
        public void ClearClipboard()
        {
            FairyToonClipboard.CopiedProperties.Clear();
            FairyToonClipboard.CopiedVectorProperties.Clear();
        }

        [TearDown]
        public void DestroyObjects()
        {
            foreach (Object item in m_Objects)
                if (item != null) Object.DestroyImmediate(item);
            m_Objects.Clear();
            FairyToonClipboard.CopiedProperties.Clear();
            FairyToonClipboard.CopiedVectorProperties.Clear();
        }

        [Test]
        public void CopyMatCapThenRimLightAndPasteEachBlock()
        {
            Material source = NewMaterial();
            Material dest = NewMaterial();
            source.SetColor("_MatCapColor", Color.red);
            source.SetColor("_RimColor", Color.blue);
            dest.SetColor("_MatCapColor", Color.black);
            dest.SetColor("_RimColor", Color.black);
            MaterialProperty[] sourceProps = Properties(source);
            MaterialProperty[] destProps = Properties(dest);
            MaterialEditor sourceEditor = EditorFor(source);
            MaterialEditor destEditor = EditorFor(dest);

            FairyToonClipboard.CopyProperties(PropertyBlock.MatCaps, sourceEditor, sourceProps);
            FairyToonClipboard.CopyProperties(PropertyBlock.RimLight, sourceEditor, sourceProps);
            FairyToonClipboard.PasteProperties(PropertyBlock.MatCaps, destEditor, destProps, false);

            Assert.AreEqual(Color.red, dest.GetColor("_MatCapColor"));
            Assert.AreEqual(Color.black, dest.GetColor("_RimColor"));
            FairyToonClipboard.PasteProperties(PropertyBlock.RimLight, destEditor, destProps, false);
            Assert.AreEqual(Color.blue, dest.GetColor("_RimColor"));
        }

        [Test]
        public void OverlappingBlockPastesTheSharedPropertyOnly()
        {
            Material source = NewMaterial();
            Material dest = NewMaterial();
            source.SetFloat("_ShadowEnvStrength", 0.25f);
            source.SetColor("_ShadowColor", Color.green);
            dest.SetFloat("_ShadowEnvStrength", 1f);
            dest.SetColor("_ShadowColor", Color.white);
            MaterialProperty[] sourceProps = Properties(source);
            MaterialProperty[] destProps = Properties(dest);

            FairyToonClipboard.CopyProperties(PropertyBlock.Shadow, EditorFor(source), sourceProps);
            FairyToonClipboard.PasteProperties(PropertyBlock.Lighting, EditorFor(dest), destProps, false);

            Assert.AreEqual(0.25f, dest.GetFloat("_ShadowEnvStrength"), 0.0001f);
            Assert.AreEqual(Color.white, dest.GetColor("_ShadowColor"));
        }

        [Test]
        public void TexturePasteIsOptionalAndScaleOffsetUsesTheFirstTarget()
        {
            Material source = NewMaterial();
            Material first = NewMaterial();
            Material second = NewMaterial();
            Texture2D texture = Track(new Texture2D(4, 4));
            source.SetTexture("_MatCapTex", texture);
            source.SetTextureScale("_MatCapTex", new Vector2(2f, 3f));
            first.SetTexture("_MatCapTex", null);
            second.SetTexture("_MatCapTex", null);
            MaterialProperty[] sourceProps = Properties(source);
            MaterialEditor destEditor = (MaterialEditor)UnityEditor.Editor.CreateEditor(new Object[] { first, second });
            Track(destEditor);
            MaterialProperty[] destProps = MaterialEditor.GetMaterialProperties(new Object[] { first, second });

            FairyToonClipboard.CopyProperties(PropertyBlock.MatCaps, EditorFor(source), sourceProps);
            FairyToonClipboard.PasteProperties(PropertyBlock.MatCaps, destEditor, destProps, false);
            Assert.IsNull(first.GetTexture("_MatCapTex"));
            Assert.AreEqual(new Vector2(1f, 1f), first.GetTextureScale("_MatCapTex"));

            FairyToonClipboard.PasteProperties(PropertyBlock.MatCaps, destEditor, destProps, true);
            Assert.AreEqual(texture, first.GetTexture("_MatCapTex"));
            Assert.AreEqual(texture, second.GetTexture("_MatCapTex"));
            Assert.AreEqual(new Vector2(2f, 3f), first.GetTextureScale("_MatCapTex"));
            Assert.AreEqual(new Vector2(1f, 1f), second.GetTextureScale("_MatCapTex"));
        }

        [Test]
        public void ResetRestoresTheShaderDefault()
        {
            Material material = NewMaterial();
            Color original = material.GetColor("_RimColor");
            material.SetColor("_RimColor", Color.cyan);
            MaterialProperty[] props = Properties(material);
            FairyToonClipboard.ResetProperties(PropertyBlock.RimLight, EditorFor(material), props);
            Assert.AreEqual(original, material.GetColor("_RimColor"));
        }

        [Test]
        public void PasteIsOneUndoStep()
        {
            Material source = NewMaterial();
            Material dest = NewMaterial();
            source.SetColor("_RimColor", Color.yellow);
            dest.SetColor("_RimColor", Color.black);
            Color before = dest.GetColor("_RimColor");
            FairyToonClipboard.CopyProperties(PropertyBlock.RimLight, EditorFor(source), Properties(source));
            FairyToonClipboard.PasteProperties(PropertyBlock.RimLight, EditorFor(dest), Properties(dest), false);
            Assert.AreEqual(Color.yellow, dest.GetColor("_RimColor"));
            Undo.PerformUndo();
            Assert.AreEqual(before, dest.GetColor("_RimColor"));
        }

        [Test]
        public void BasePasteMigratesTheDefaultCoverageQueue()
        {
            Shader shader = Shader.Find(FairyToon.ShaderName);
            Assert.IsNotNull(shader, "FairyToon shader must be imported. EditMode tests fail instead of skipping a missing shader.");
            Material opaque = Track(new Material(shader));
            Material cutout = Track(new Material(shader));
            Material transparent = Track(new Material(shader));
            FairyToonMaterialSetup.SetRenderingMode(opaque, FairyToonMaterialSetup.Opaque);
            FairyToonMaterialSetup.SetRenderingMode(cutout, FairyToonMaterialSetup.Cutout);
            FairyToonMaterialSetup.SetRenderingMode(transparent, FairyToonMaterialSetup.Transparent);
            Assert.AreEqual(2450, cutout.renderQueue);
            Assert.AreEqual("TransparentCutout", cutout.GetTag("RenderType", false));
            Assert.IsFalse(transparent.GetShaderPassEnabled("DepthOnly"));
            Assert.AreEqual("Transparent", transparent.GetTag("RenderType", false));
            MaterialProperty[] sourceProps = Properties(opaque);
            MaterialProperty[] destProps = Properties(cutout);
            FairyToonClipboard.CopyProperties(PropertyBlock.Base, EditorFor(opaque), sourceProps);
            FairyToonClipboard.PasteProperties(PropertyBlock.Base, EditorFor(cutout), destProps, false);
            Assert.AreEqual(FairyToonMaterialSetup.Opaque, FairyToonMaterialSetup.GetRenderingMode(cutout));
            Assert.AreEqual(2000, cutout.renderQueue);
            Assert.IsFalse(cutout.IsKeywordEnabled("_ALPHATEST_ON"));
            Assert.AreEqual("Opaque", cutout.GetTag("RenderType", false));
            Undo.PerformUndo();
            Assert.AreEqual(FairyToonMaterialSetup.Cutout, FairyToonMaterialSetup.GetRenderingMode(cutout));
            Assert.AreEqual(2450, cutout.renderQueue);
            Assert.IsTrue(cutout.IsKeywordEnabled("_ALPHATEST_ON"));
            Assert.AreEqual("TransparentCutout", cutout.GetTag("RenderType", false));

            FairyToonClipboard.CopyProperties(PropertyBlock.Base, EditorFor(cutout), Properties(cutout));
            FairyToonClipboard.PasteProperties(PropertyBlock.Base, EditorFor(transparent), Properties(transparent), false);
            Assert.IsTrue(transparent.GetShaderPassEnabled("DepthOnly"));
            Assert.AreEqual("TransparentCutout", transparent.GetTag("RenderType", false));
            Undo.PerformUndo();
            Assert.IsFalse(transparent.GetShaderPassEnabled("DepthOnly"));
            Assert.AreEqual("Transparent", transparent.GetTag("RenderType", false));
            Assert.IsTrue(transparent.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"));
        }

        [Test]
        public void EnablingMatCapSetsTheKeyword()
        {
            Material material = NewMaterial();
            material.SetFloat("_UseMatCap", 1f);
            FairyToonMaterialSetup.Apply(material);
            Assert.IsTrue(material.IsKeywordEnabled("_FT_MATCAP"));
        }

        [Test]
        public void GemShaderReportsGemMode()
        {
            Shader shader = Shader.Find(FairyToonMaterialSetup.GemShaderName);
            Assert.IsNotNull(shader, "FairyToon/Gem must be imported.");
            Material material = Track(new Material(shader));
            Assert.AreEqual(FairyToonMaterialSetup.Gem, FairyToonMaterialSetup.GetRenderingMode(material));
        }

        [Test]
        public void LegacyShaderLabIntIsFloatBacked()
        {
            Shader shader = Shader.Find(FairyToon.ShaderName);
            Assert.IsNotNull(shader, "FairyToon shader must be imported. EditMode tests fail instead of skipping a missing shader.");
            AssertFloatBacked(shader, "_UseOutline");
            AssertFloatBacked(shader, "_AlphaMaskMode");
            AssertFloatBacked(shader, "_ShadingModel");
            AssertFloatBacked(shader, "_EFPart");
            Shader fur = Shader.Find(FairyToonMaterialSetup.FurShaderName);
            Assert.IsNotNull(fur, "FairyToon/Fur must be imported.");
            AssertFloatBacked(fur, "_FurMode");
        }

        private Material NewMaterial()
        {
            Shader shader = Shader.Find(FairyToon.ShaderName);
            Assert.IsNotNull(shader, "FairyToon shader must be imported. EditMode tests fail instead of skipping a missing shader.");
            return Track(new Material(shader));
        }

        private static void AssertFloatBacked(Shader shader, string name)
        {
            int index = shader.FindPropertyIndex(name);
            Assert.GreaterOrEqual(index, 0, name);
            Assert.AreEqual(UnityEngine.Rendering.ShaderPropertyType.Float, shader.GetPropertyType(index),
                name + " is ShaderLab Int/Float/Range, which Unity stores as a float. Integer is the real int type.");
        }

        private MaterialEditor EditorFor(Material material)
        {
            return Track((MaterialEditor)UnityEditor.Editor.CreateEditor(material));
        }

        private static MaterialProperty[] Properties(Material material)
        {
            return MaterialEditor.GetMaterialProperties(new Object[] { material });
        }

        private T Track<T>(T item) where T : Object
        {
            m_Objects.Add(item);
            return item;
        }
    }
}
