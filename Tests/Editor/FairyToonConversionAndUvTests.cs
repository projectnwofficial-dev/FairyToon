using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor.Tests
{
    public class FairyToonConversionAndUvTests
    {
        [TestCase(-1, true, 2460)]
        [TestCase(3000, true, 2450)]
        [TestCase(3100, true, 2450)]
        [TestCase(2463, true, 2463)]
        [TestCase(3000, false, 3000)]
        [TestCase(3100, false, 3100)]
        public void TransparentHairConversionUsesTheSelectedCoverageQueue(int sourceQueue, bool cutout, int expectedQueue)
        {
            Shader shader = Shader.Find("Hidden/lilToonTransparent");
            if (shader == null) Assert.Ignore("The converter integration test requires lilToon.");
            var source = new Material(shader) { name = "Test Hair", renderQueue = sourceQueue };
            var target = new Material(Shader.Find(FairyToon.ShaderName));
            try
            {
                var options = new FairyToonConvertOptions { TransparentHairToCutout = cutout, UseProjectProfiles = false };
                var report = FairyToonConverter.Convert(source, target, options);
                Assert.IsTrue(report.Success, string.Join(", ", report.Errors));
                Assert.AreEqual(cutout ? FairyToonMaterialSetup.Cutout : FairyToonMaterialSetup.Transparent, report.Mode);
                Assert.AreEqual(expectedQueue, target.renderQueue);
                Assert.AreEqual(cutout, target.IsKeywordEnabled("_ALPHATEST_ON"));
                Assert.AreEqual(cutout, target.GetShaderPassEnabled(FairyToon.CharacterMaskLightMode));
                Assert.AreEqual(sourceQueue, source.rawRenderQueue);
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void FurModeTransitionsSetShellDefaultsAndKeepLaterEdits()
        {
            var material = new Material(Shader.Find(FairyToon.ShaderName));
            try
            {
                Assert.IsTrue(FairyToonMaterialSetup.SetRenderingMode(material, FairyToonMaterialSetup.Fur));
                foreach (int mode in new[] { FairyToonMaterialSetup.FurTwoPass, FairyToonMaterialSetup.FurCutout, FairyToonMaterialSetup.FurTransparent })
                {
                    Assert.IsTrue(FairyToonMaterialSetup.SetFurMode(material, mode));
                    int expected = mode == FairyToonMaterialSetup.FurTransparent ? 0 : 1;
                    Assert.AreEqual(expected, material.GetFloat("_FurZWrite"), "Fur depth, mode " + mode);
                    Assert.AreEqual(expected, material.GetFloat("_FurAlphaToMask"), "Fur alpha to coverage, mode " + mode);
                    Assert.AreEqual(mode == FairyToonMaterialSetup.FurCutout ? 1 : 0, material.GetFloat("_AlphaToMask"));
                    material.SetFloat("_FurZWrite", 1 - expected);
                    material.SetFloat("_FurAlphaToMask", 1 - expected);
                    FairyToonMaterialSetup.Apply(material);
                    Assert.AreEqual(1 - expected, material.GetFloat("_FurZWrite"));
                    Assert.AreEqual(1 - expected, material.GetFloat("_FurAlphaToMask"));
                }
            }
            finally { Object.DestroyImmediate(material); }
        }

        [Test]
        public void ConversionRetainsDecalVectorsAndReportsUnsupportedPlayback()
        {
            var lil = Shader.Find("lilToon");
            if (lil == null) Assert.Ignore("The converter integration test requires lilToon.");
            var source = new Material(lil);
            var target = new Material(Shader.Find(FairyToon.ShaderName));
            try
            {
                source.SetFloat("_UseMain2ndTex", 1f);
                const string animation = "_Main2ndTexDecalAnimation";
                const string subParam = "_Main2ndTexDecalSubParam";
                source.SetVector(animation, new Vector4(3, 4, 12, 10));
                source.SetVector(subParam, new Vector4(2, 3, 0.1f, 2));
                var report = FairyToonConverter.Convert(source, target, new FairyToonConvertOptions { UseProjectProfiles = false });
                Assert.IsTrue(report.Success, string.Join(", ", report.Errors));
                foreach (string name in new[] { animation, subParam })
                {
                    Assert.AreEqual(source.GetVector(name), target.GetVector(name), name);
                    Assert.IsTrue(report.UnsupportedFeatures.Exists(message => message.Contains(name)), name);
                    Assert.IsFalse(report.DroppedValues.Exists(message => message.Contains(name)), name);
                }
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(source);
            }
        }

        [TestCase("_Main2ndTex", true)]
        [TestCase("_Main2ndTex", false)]
        [TestCase("_Main3rdTex", true)]
        [TestCase("_Main3rdTex", false)]
        public void ResetUvRespectsPreviewAndUndoesOnlyTheLayerReset(string tex, bool keepPreview)
        {
            var first = new Material(Shader.Find(FairyToon.ShaderName));
            var second = new Material(first);
            var materials = new[] { first, second };
            var flagsAndAngle = new[] { "_UVMode", "IsDecal", "IsLeftOnly", "IsRightOnly", "ShouldCopy", "ShouldFlipMirror", "ShouldFlipCopy", "Angle" };
            var start = new Vector4(2f, 3f, 4f, 5f);
            var expectedWrites = new HashSet<string> { tex + "DecalAnimation", tex + "DecalSubParam" };
            foreach (string suffix in flagsAndAngle) expectedWrites.Add(tex + suffix);
            try
            {
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    foreach (string suffix in flagsAndAngle) material.SetFloat(tex + suffix, 1f + i);
                    material.SetVector(tex + "DecalAnimation", start * (i + 1));
                    material.SetVector(tex + "DecalSubParam", start * (i + 1));
                    material.SetVector(tex + "_ScrollRotate", start * (i + 1));
                    material.SetFloat("_DstBlendAlpha", 7f);
                    material.SetTextureScale(tex, new Vector2(2, 3));
                    material.SetTextureOffset(tex, new Vector2(0.25f, 0.5f));
                }
                var offered = new HashSet<string>();
                var properties = MaterialEditor.GetMaterialProperties(materials);
                foreach (var property in properties)
                    property.applyPropertyCallback = (changed, mask, previous) =>
                    {
                        offered.Add(changed.name);
                        return keepPreview && changed.name != tex + "IsDecal";
                    };

                Undo.IncrementCurrentGroup();
                FairyToonGUI.ResetLayerUv(properties, tex + "_ScrollRotate");
                CollectionAssert.AreEquivalent(expectedWrites, offered);
                for (int i = 0; i < materials.Length; i++)
                {
                    Material material = materials[i];
                    foreach (string suffix in flagsAndAngle)
                        Assert.AreEqual(keepPreview && suffix != "IsDecal" ? 1f + i : 0f, material.GetFloat(tex + suffix), suffix);
                    Assert.AreEqual(keepPreview ? start * (i + 1) : new Vector4(1, 1, 1, 30), material.GetVector(tex + "DecalAnimation"));
                    Assert.AreEqual(keepPreview ? start * (i + 1) : new Vector4(1, 1, 0.01f, 1), material.GetVector(tex + "DecalSubParam"));
                    Assert.AreEqual(start * (i + 1), material.GetVector(tex + "_ScrollRotate"));
                    Assert.AreEqual(7f, material.GetFloat("_DstBlendAlpha"), "Reset UV must preserve authored rendering state.");
                    Assert.AreEqual(new Vector2(2, 3), material.GetTextureScale(tex));
                    Assert.AreEqual(new Vector2(0.25f, 0.5f), material.GetTextureOffset(tex));
                }
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                for (int i = 0; i < materials.Length; i++)
                {
                    foreach (string suffix in flagsAndAngle) Assert.AreEqual(1f + i, materials[i].GetFloat(tex + suffix), suffix);
                    Assert.AreEqual(start * (i + 1), materials[i].GetVector(tex + "DecalAnimation"));
                    Assert.AreEqual(start * (i + 1), materials[i].GetVector(tex + "DecalSubParam"));
                }
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }
    }
}
