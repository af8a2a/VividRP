using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class DefaultSkyCubemapImportUtilityTests
    {
        private const string GeneratedAssetsFolderPath = "Assets/Tests/VividRP/GeneratedDefaultSkyImport";
        private const string TestTextureAssetPath = GeneratedAssetsFolderPath + "/DefaultHDRISky_ImportTest.exr";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(GeneratedAssetsFolderPath))
            {
                AssetDatabase.DeleteAsset(TestTextureAssetPath);
                AssetDatabase.DeleteAsset(GeneratedAssetsFolderPath);
                AssetDatabase.Refresh();
            }
        }

        [Test]
        public void IsDefaultSkyAsset_ReturnsTrue_ForConfiguredPackagePaths()
        {
            foreach (var candidatePath in VividPackagePathUtility.GetCandidateAssetPaths(DefaultSkyCubemapImportUtility.DefaultSkyRelativeAssetPath))
                Assert.That(DefaultSkyCubemapImportUtility.IsDefaultSkyAsset(candidatePath), Is.True);

            Assert.That(DefaultSkyCubemapImportUtility.IsDefaultSkyAsset("Assets/Textures/OtherSky.exr"), Is.False);
        }

        [Test]
        public void ApplyImportSettings_AlignsWithHdrpDefaultSkySettings_WhenTextureUsesWrongImportState()
        {
            CreateTestTextureAsset();

            var textureImporter = AssetImporter.GetAtPath(TestTextureAssetPath) as TextureImporter;
            Assert.That(textureImporter, Is.Not.Null);

            textureImporter.textureShape = TextureImporterShape.Texture2D;
            textureImporter.generateCubemap = TextureImporterGenerateCubemap.FullCubemap;
            textureImporter.mipmapEnabled = false;
            textureImporter.npotScale = TextureImporterNPOTScale.None;
            textureImporter.filterMode = FilterMode.Bilinear;
            textureImporter.anisoLevel = 1;
            textureImporter.mipMapBias = 0.0f;
            textureImporter.wrapModeU = TextureWrapMode.Repeat;
            textureImporter.wrapModeV = TextureWrapMode.Repeat;
            textureImporter.wrapModeW = TextureWrapMode.Repeat;

            var standaloneSettings = textureImporter.GetPlatformTextureSettings("Standalone");
            standaloneSettings.overridden = true;
            standaloneSettings.format = TextureImporterFormat.BC6H;
            standaloneSettings.maxTextureSize = 2048;
            textureImporter.SetPlatformTextureSettings(standaloneSettings);

            var hasChanges = DefaultSkyCubemapImportUtility.ApplyImportSettings(textureImporter);

            Assert.That(hasChanges, Is.True);
            Assert.That(textureImporter.textureShape, Is.EqualTo(TextureImporterShape.TextureCube));
            Assert.That(textureImporter.generateCubemap, Is.EqualTo(TextureImporterGenerateCubemap.AutoCubemap));
            Assert.That(textureImporter.mipmapEnabled, Is.True);
            Assert.That(textureImporter.npotScale, Is.EqualTo(TextureImporterNPOTScale.ToNearest));
            Assert.That(textureImporter.filterMode, Is.EqualTo(FilterMode.Trilinear));
            Assert.That(textureImporter.anisoLevel, Is.EqualTo(-1));
            Assert.That(textureImporter.mipMapBias, Is.EqualTo(-100.0f));
            Assert.That(textureImporter.wrapModeU, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(textureImporter.wrapModeV, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(textureImporter.wrapModeW, Is.EqualTo(TextureWrapMode.Clamp));
            Assert.That(textureImporter.GetPlatformTextureSettings("Standalone").overridden, Is.False);
        }

        [Test]
        public void Source_AlignsDefaultSkyImportWithHdrpSettings()
        {
            var source = File.ReadAllText(GetPackageFilePath("Editor", "PipelineResource", "DefaultSkyCubemapImportUtility.cs"));

            Assert.That(source, Does.Contain("DefaultSkyRelativeAssetPath = \"Texture/Default/DefaultHDRISky.exr\""));
            Assert.That(source, Does.Contain("TextureImporterShape.TextureCube"));
            Assert.That(source, Does.Contain("TextureImporterGenerateCubemap.AutoCubemap"));
            Assert.That(source, Does.Contain("textureImporter.mipmapEnabled = true;"));
            Assert.That(source, Does.Contain("TextureImporterNPOTScale.ToNearest"));
            Assert.That(source, Does.Contain("FilterMode.Trilinear"));
            Assert.That(source, Does.Contain("TextureWrapMode.Clamp"));
            Assert.That(source, Does.Contain("textureImporter.ClearPlatformTextureSettings(\"Standalone\")"));
        }

        private static void CreateTestTextureAsset()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Tests"))
                AssetDatabase.CreateFolder("Assets", "Tests");

            if (!AssetDatabase.IsValidFolder("Assets/Tests/VividRP"))
                AssetDatabase.CreateFolder("Assets/Tests", "VividRP");

            if (!AssetDatabase.IsValidFolder(GeneratedAssetsFolderPath))
                AssetDatabase.CreateFolder("Assets/Tests/VividRP", "GeneratedDefaultSkyImport");

            File.Copy(GetPackageFilePath("Texture", "Default", "DefaultHDRISky.exr"), TestTextureAssetPath, true);
            AssetDatabase.ImportAsset(TestTextureAssetPath, ImportAssetOptions.ForceUpdate);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
