using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace NW.FairyToon.Editor.Tests
{
    public class FairyToonEndfieldUpgradeTests
    {
        [Test]
        public void ClassifySurfaceDetectsAllArchetypesDeterministically()
        {
            Assert.AreEqual(FairyToonEndfieldSurfaceClass.DarkRimShadeMesh,
                FairyToonEndfieldUpgrader.ClassifySurface(new FairyToonEndfieldUpgrader.SourceSignals
                {
                    MaterialName = "S_Mesh_gray",
                    HasMainTex = true,
                    HasRimShade = true,
                    RimShadeColor = new Color(0.157f, 0.157f, 0.157f, 1f)
                }));

            Assert.AreEqual(FairyToonEndfieldSurfaceClass.Enamel,
                FairyToonEndfieldUpgrader.ClassifySurface(new FairyToonEndfieldUpgrader.SourceSignals
                {
                    MaterialName = "Enamel",
                    HasMainTex = true,
                    HasBumpMap = true,
                    HasMatCap = true,
                    HasMatCapMask = false,
                    HasMatCap2nd = true,
                    HasMatCap2ndMask = true
                }));

            Assert.AreEqual(FairyToonEndfieldSurfaceClass.OiledLeather,
                FairyToonEndfieldUpgrader.ClassifySurface(new FairyToonEndfieldUpgrader.SourceSignals
                {
                    MaterialName = "Gray_L",
                    FolderName = "Leather",
                    HasMainTex = true,
                    HasBumpMap = true,
                    BumpScale = 1.8f,
                    HasRim = true,
                    RimColor = new Color(0.93f, 0.97f, 1f, 0.527f),
                    RimBlur = 0.122f
                }));

            Assert.AreEqual(FairyToonEndfieldSurfaceClass.MatteSole,
                FairyToonEndfieldUpgrader.ClassifySurface(new FairyToonEndfieldUpgrader.SourceSignals
                {
                    MaterialName = "Gary_S",
                    FolderName = "Sole",
                    HasMainTex = true
                }));

            Assert.AreEqual(FairyToonEndfieldSurfaceClass.PureMetal,
                FairyToonEndfieldUpgrader.ClassifySurface(new FairyToonEndfieldUpgrader.SourceSignals
                {
                    MaterialName = "Gray",
                    HasMainTex = false,
                    HasMatCap = true,
                    HasMatCapMask = false
                }));

            Assert.AreEqual(FairyToonEndfieldSurfaceClass.Cloth,
                FairyToonEndfieldUpgrader.ClassifySurface(new FairyToonEndfieldUpgrader.SourceSignals
                {
                    MaterialName = "Black_denim",
                    HasMainTex = true,
                    HasBumpMap = true,
                    BumpScale = 1.2f
                }));
        }

        [Test]
        public void ResolveMetalStyleAutoDistinguishesSilverAndSmokedGunmetal()
        {
            var silverSignals = new FairyToonEndfieldUpgrader.SourceSignals
            {
                MaterialName = "Basic_Black",
                HasMainTex = true,
                HasMatCap = true,
                HasMatCapMask = true,
                MatCapBlendMode = 1, // Add
                MatCapColor = Color.white
            };
            var gunmetalSignals = new FairyToonEndfieldUpgrader.SourceSignals
            {
                MaterialName = "Black_denim",
                HasMainTex = true,
                HasMatCap = true,
                HasMatCapMask = true,
                MatCapBlendMode = 0, // Normal
                MatCapColor = new Color(0.755f, 0.755f, 0.755f, 1f)
            };

            Assert.AreEqual(FairyToonEndfieldMetalStyle.BrightSilver,
                FairyToonEndfieldUpgrader.ResolveMetalStyle(silverSignals, FairyToonEndfieldMetalStyle.Auto));
            Assert.AreEqual(FairyToonEndfieldMetalStyle.SmokedGunmetal,
                FairyToonEndfieldUpgrader.ResolveMetalStyle(gunmetalSignals, FairyToonEndfieldMetalStyle.Auto));
            Assert.AreEqual(FairyToonEndfieldMetalStyle.SmokedGunmetal,
                FairyToonEndfieldUpgrader.ResolveMetalStyle(silverSignals, FairyToonEndfieldMetalStyle.SmokedGunmetal));
            Assert.AreEqual(FairyToonEndfieldMetalStyle.BrightSilver,
                FairyToonEndfieldUpgrader.ResolveMetalStyle(gunmetalSignals, FairyToonEndfieldMetalStyle.BrightSilver));
        }

        [Test]
        public void ResolvePBRPathRejectsOutsideAssetsAndTraversalSegments()
        {
            Assert.AreEqual("Assets/Out/PBRMaps/Shoe_PBR.png",
                FairyToonEndfieldUpgrader.ResolvePBRPath("Assets/Out/Shoe.mat", "Shoe", null));
            Assert.IsNull(FairyToonEndfieldUpgrader.ResolvePBRPath("", "Transient", null));
            Assert.Throws<InvalidOperationException>(() =>
                FairyToonEndfieldUpgrader.ResolvePBRPath("Assets/Out/Shoe.mat", "Shoe", "Packages/com.nw.fairytoon"));
            Assert.Throws<InvalidOperationException>(() =>
                FairyToonEndfieldUpgrader.ResolvePBRPath("Assets/Out/Shoe.mat", "Shoe", "Assets/Out/../Escape"));
        }

        [Test]
        public void DefaultConversionStaysInLilToonShadingModel()
        {
            Shader lil = Shader.Find("Hidden/lilToonOutline") ?? Shader.Find("lilToon");
            if (lil == null) Assert.Ignore("The converter integration test requires lilToon.");

            var source = new Material(lil) { name = "DefaultCloth" };
            var target = new Material(Shader.Find(FairyToon.ShaderName));
            try
            {
                var report = FairyToonConverter.Convert(source, target, new FairyToonConvertOptions { UseProjectProfiles = false });
                Assert.IsTrue(report.Success, string.Join(", ", report.Errors));
                Assert.AreEqual(FairyToonMaterialSetup.ShadingLilToon, Mathf.RoundToInt(target.GetFloat("_ShadingModel")));
                Assert.IsFalse(target.IsKeywordEnabled("_FT_ENDFIELD"));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(source);
            }
        }

        [Test]
        public void EndfieldUpgradePreservesAuthorOutlineRimShadeGlitterAndHonorsProfiles()
        {
            Shader lilOutline = Shader.Find("Hidden/lilToonOutline");
            if (lilOutline == null) Assert.Ignore("The converter integration test requires Hidden/lilToonOutline.");

            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var sourceEnamel = new Material(lilOutline) { name = "Enamel" };
            var targetEnamel = new Material(Shader.Find(FairyToon.ShaderName));
            var sourceMesh = new Material(lilOutline) { name = "S_Mesh_gray_denim" };
            var targetMesh = new Material(Shader.Find(FairyToon.ShaderName));
            var profile = ScriptableObject.CreateInstance<FairyToonConvertProfile>();

            try
            {
                // Enamel shoe with silver buckle, outline width 0.08, and custom glitter params
                sourceEnamel.SetTexture("_MainTex", tex);
                sourceEnamel.SetTexture("_BumpMap", tex);
                sourceEnamel.SetFloat("_OutlineWidth", 0.08f);
                sourceEnamel.SetColor("_OutlineColor", Color.black);
                sourceEnamel.SetFloat("_UseMatCap", 1f);
                sourceEnamel.SetFloat("_MatCapBlend", 1f);
                sourceEnamel.SetFloat("_UseMatCap2nd", 1f);
                sourceEnamel.SetFloat("_MatCap2ndBlend", 1f);
                sourceEnamel.SetFloat("_MatCap2ndBlendMode", 1f); // Add in lilToon
                sourceEnamel.SetTexture("_MatCap2ndBlendMask", tex);
                sourceEnamel.SetFloat("_UseGlitter", 1f);
                var authoredGlitterParams = new Vector4(97.7f, 97.7f, 0.0324f, 9.352f);
                sourceEnamel.SetVector("_GlitterParams1", authoredGlitterParams);
                sourceEnamel.SetColor("_GlitterColor", new Color(16f, 16f, 16f, 1f));

                // Explicit profile override must still win over Endfield upgrader defaults.
                profile.entries.Add(new FairyToonConvertProfile.Entry
                {
                    source = sourceEnamel,
                    overrides =
                    {
                        new FairyToonConvertProfile.Override
                        {
                            property = "_EFClearcoat",
                            kind = FairyToonConvertProfile.Kind.Float,
                            floatValue = 0.42f
                        }
                    }
                });

                var options = new FairyToonConvertOptions
                {
                    UpgradeToEndfield = true,
                    PackPBRMaps = false,
                    UseProjectProfiles = false,
                    Profile = profile
                };

                var enamelReport = FairyToonConverter.Convert(sourceEnamel, targetEnamel, options);
                Assert.IsTrue(enamelReport.Success, string.Join(", ", enamelReport.Errors));
                Assert.AreEqual(FairyToonMaterialSetup.ShadingEndfield, Mathf.RoundToInt(targetEnamel.GetFloat("_ShadingModel")));
                Assert.IsTrue(targetEnamel.IsKeywordEnabled("_FT_ENDFIELD"));
                Assert.AreEqual(1f, targetEnamel.GetFloat("_UseOutline"), "Non-zero authored outline must survive upgrade.");
                Assert.AreEqual(0f, targetEnamel.GetFloat("_UseReflection"), "Endfield PBR replaces legacy lilToon Reflection.");
                Assert.AreEqual(0f, targetEnamel.GetFloat("_MatCap2ndBlendMode"), "Enamel 2nd metal MatCap uses Normal blend to keep chrome blacks.");
                Assert.AreEqual(0f, targetEnamel.GetFloat("_EFRimDepth"), "Enamel shoes disable artificial depth rim.");
                Assert.AreEqual(0.42f, targetEnamel.GetFloat("_EFClearcoat"), 1e-4f, "Explicit convert profile override must win.");
                Assert.AreEqual(authoredGlitterParams, targetEnamel.GetVector("_GlitterParams1"), "Per-material glitter size/density must stay intact.");
                Assert.AreEqual(18f, targetEnamel.GetColor("_GlitterColor").r, 1e-3f, "Glitter HDR boost preserves subtle 1.125x R gain.");

                // Dark RimShade fishnet mesh with zero-width outline
                sourceMesh.SetTexture("_MainTex", tex);
                sourceMesh.SetFloat("_OutlineWidth", 0f);
                sourceMesh.SetFloat("_UseRimShade", 1f);
                sourceMesh.SetColor("_RimShadeColor", new Color(0.157f, 0.157f, 0.157f, 1f));
                sourceMesh.SetFloat("_UseRim", 1f);
                sourceMesh.SetColor("_RimColor", new Color(1f, 1f, 1f, 0.286f));

                var meshReport = FairyToonConverter.Convert(sourceMesh, targetMesh, options);
                Assert.IsTrue(meshReport.Success, string.Join(", ", meshReport.Errors));
                Assert.AreEqual(0f, targetMesh.GetFloat("_UseOutline"), "Zero-width outline stays disabled.");
                Assert.AreEqual(1f, targetMesh.GetFloat("_UseRimShade"), "Dark RimShade must survive Endfield upgrade.");
                Assert.AreEqual(0f, targetMesh.GetFloat("_EFRimStrength"), "Conflicting Endfield bright rim must stay off on dark RimShade mesh.");
                Assert.Less(targetMesh.GetFloat("_EFEnvironment"), 0.30f, "Dark RimShade mesh clamps env lift.");
                Assert.Less(targetMesh.GetColor("_RimColor").a, 0.10f, "Dark RimShade mesh softens white author rim.");
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(targetMesh);
                Object.DestroyImmediate(sourceMesh);
                Object.DestroyImmediate(targetEnamel);
                Object.DestroyImmediate(sourceEnamel);
                Object.DestroyImmediate(tex);
            }
        }

        [Test]
        public void EndfieldBatchConvertPacksPBRMapInAssetsAndCleansUp()
        {
            Shader lilOutline = Shader.Find("Hidden/lilToonOutline") ?? Shader.Find("lilToon");
            if (lilOutline == null) Assert.Ignore("The converter integration test requires lilToon.");

            string tempRoot = "Assets/_FairyToonTempUpgradeTest_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            string srcMatPath = tempRoot + "/SrcBoots_Leather.mat";
            string outDir = tempRoot + "/Converted";
            string pbrDir = tempRoot + "/PBR";

            try
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(tempRoot));
                var src = new Material(lilOutline) { name = "SrcBoots_Leather" };
                src.SetFloat("_UseMatCap", 1f);
                src.SetFloat("_MatCapBlendMode", 0f);
                src.SetColor("_MatCapColor", new Color(0.75f, 0.75f, 0.75f, 1f));
                src.SetFloat("_OutlineWidth", 0.075f);
                AssetDatabase.CreateAsset(src, srcMatPath);
                AssetDatabase.SaveAssets();

                var options = new FairyToonConvertOptions
                {
                    OutputFolder = outDir,
                    UpgradeToEndfield = true,
                    PackPBRMaps = true,
                    PBRMapOutputFolder = pbrDir,
                    UseProjectProfiles = false
                };

                var reports = FairyToonConverter.ConvertAll(new[] { src }, options);
                Assert.AreEqual(1, reports.Count);
                Assert.IsTrue(reports[0].Success, string.Join(", ", reports[0].Errors));

                var converted = AssetDatabase.LoadAssetAtPath<Material>(reports[0].TargetPath);
                Assert.IsNotNull(converted);
                Assert.AreEqual(FairyToonMaterialSetup.ShadingEndfield, Mathf.RoundToInt(converted.GetFloat("_ShadingModel")));
                var packed = converted.GetTexture("_PBRMap");
                Assert.IsNotNull(packed, "Batch Endfield conversion must pack and assign _PBRMap.");
                StringAssert.StartsWith(pbrDir + "/", AssetDatabase.GetAssetPath(packed));
            }
            finally
            {
                if (AssetDatabase.IsValidFolder(tempRoot))
                    AssetDatabase.DeleteAsset(tempRoot);
            }
        }
    }
}
