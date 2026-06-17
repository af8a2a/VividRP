using System.IO;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class StandardLitMaterialImportUtilityTests
    {
        private const string StandardLitShaderAssetPath = "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLit/StandardLit.shader";
        private const string GeneratedAssetsFolderPath = "Assets/Tests/VividRP/GeneratedMaterialImportUtility";
        private const string NormalTextureAssetPath = GeneratedAssetsFolderPath + "/Wall_Normal_ImportTest.png";

        [Test]
        public void TryImport_Maps3DsMaxPhysicalMaterialProperties_ToStandardLit()
        {
            Material material = CreateMaterial();
            Texture2D baseMap = CreateTexture();
            Texture2D roughnessMap = CreateTexture();
            Texture2D metalnessMap = CreateTexture();
            Texture2D bumpMap = CreateTexture();
            Texture2D opacityMap = CreateTexture();

            try
            {
                var description = new FakeImportedMaterialDescription()
                    .WithFloat("ClassIDa", 4.1222316e+08f)
                    .WithFloat("ClassIDb", -5.5903846e+08f)
                    .WithFloat("base_weight", 0.8f)
                    .WithFloat("transparency", 0.2f)
                    .WithFloat("metalness", 0.6f)
                    .WithFloat("roughness", 0.25f)
                    .WithFloat("bump_map_amt", 0.35f)
                    .WithFloat("emission", 2.0f)
                    .WithVector("base_color", new Vector4(0.4f, 0.5f, 0.6f, 1.0f))
                    .WithVector("emit_color", new Vector4(0.1f, 0.2f, 0.3f, 1.0f))
                    .WithTexture("base_color_map", baseMap)
                    .WithTexture("roughness_map", roughnessMap)
                    .WithTexture("metalness_map", metalnessMap)
                    .WithTexture("bump_map", bumpMap)
                    .WithTexture("transparency_map", opacityMap);

                bool imported = StandardLitMaterialImportUtility.TryImport(
                    description,
                    material,
                    StandardLitMaterialImportUtility.GetStandardLitShader());

                Assert.That(imported, Is.True);
                Assert.That(material.shader.name, Is.EqualTo(StandardLitMaterialImportUtility.StandardLitShaderName));
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(baseMap));
                Assert.That(material.GetTexture("_RoughnessMap"), Is.SameAs(roughnessMap));
                Assert.That(material.GetTexture("_MetallicGlossMap"), Is.SameAs(metalnessMap));
                Assert.That(material.GetTexture("_BumpMap"), Is.SameAs(bumpMap));
                Assert.That(material.GetTexture("_OpacityMap"), Is.SameAs(opacityMap));
                Assert.That(material.GetFloat("_Metallic"), Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(material.GetFloat("_BumpScale"), Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(material.GetFloat("_AlphaClip"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(0.32f, 0.4f, 0.48f, 0.8f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetColor("_EmissionColor"), Is.EqualTo(new Color(0.2f, 0.4f, 0.6f, 1.0f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.IsKeywordEnabled("_OPACITYMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_ROUGHNESSMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(roughnessMap);
                Object.DestroyImmediate(metalnessMap);
                Object.DestroyImmediate(bumpMap);
                Object.DestroyImmediate(opacityMap);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TryImport_Maps3DsMaxSimplifiedPhysicalMaterialProperties_ToStandardLit()
        {
            Material material = CreateMaterial();
            Texture2D baseMap = CreateTexture();
            Texture2D roughnessMap = CreateTexture();
            Texture2D metalnessMap = CreateTexture();
            Texture2D normalMap = CreateTexture();
            Texture2D emissionMap = CreateTexture();
            Texture2D occlusionMap = CreateTexture();
            Texture2D opacityMap = CreateTexture();

            try
            {
                var description = new FakeImportedMaterialDescription()
                    .WithFloat("ClassIDa", -8.0431565e+08f)
                    .WithFloat("ClassIDb", -1.0994388e+09f)
                    .WithFloat("useGlossiness", 2.0f)
                    .WithFloat("metalness", 0.25f)
                    .WithFloat("roughness", 0.4f)
                    .WithFloat("bump_map_amt", 0.7f)
                    .WithVector("basecolor", new Vector4(0.3f, 0.4f, 0.5f, 0.65f))
                    .WithTexture("base_color_map", baseMap)
                    .WithTexture("roughness_map", roughnessMap)
                    .WithTexture("metalness_map", metalnessMap)
                    .WithTexture("norm_map", normalMap)
                    .WithTexture("emit_color_map", emissionMap)
                    .WithTexture("ao_map", occlusionMap)
                    .WithTexture("opacity_map", opacityMap);

                bool imported = StandardLitMaterialImportUtility.TryImport(
                    description,
                    material,
                    StandardLitMaterialImportUtility.GetStandardLitShader());

                Assert.That(imported, Is.True);
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(baseMap));
                Assert.That(material.GetTexture("_RoughnessMap"), Is.SameAs(roughnessMap));
                Assert.That(material.GetTexture("_MetallicGlossMap"), Is.SameAs(metalnessMap));
                Assert.That(material.GetTexture("_BumpMap"), Is.SameAs(normalMap));
                Assert.That(material.GetTexture("_EmissionMap"), Is.SameAs(emissionMap));
                Assert.That(material.GetTexture("_OcclusionMap"), Is.SameAs(occlusionMap));
                Assert.That(material.GetTexture("_OpacityMap"), Is.SameAs(opacityMap));
                Assert.That(material.GetFloat("_Metallic"), Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(material.GetFloat("_BumpScale"), Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(material.GetFloat("_OcclusionStrength"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(0.3f, 0.4f, 0.5f, 0.65f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetColor("_EmissionColor"), Is.EqualTo(Color.white).Using(ColorEqualityComparer.Instance));
                Assert.That(material.IsKeywordEnabled("_ROUGHNESSMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_OCCLUSIONMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(roughnessMap);
                Object.DestroyImmediate(metalnessMap);
                Object.DestroyImmediate(normalMap);
                Object.DestroyImmediate(emissionMap);
                Object.DestroyImmediate(occlusionMap);
                Object.DestroyImmediate(opacityMap);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TryImport_MapsLegacyStandardMaterialProperties_ToStandardLit()
        {
            Material material = CreateMaterial();
            Texture2D bumpMap = CreateTexture();
            Texture2D opacityMap = CreateTexture();

            try
            {
                var description = new FakeImportedMaterialDescription()
                    .WithFloat("TransparencyFactor", 0.25f)
                    .WithFloat("DiffuseFactor", 0.5f)
                    .WithFloat("BumpFactor", 0.8f)
                    .WithFloat("EmissiveFactor", 1.5f)
                    .WithFloat("Shininess", 25.0f)
                    .WithVector("DiffuseColor", new Vector4(0.5f, 0.6f, 0.7f, 1.0f))
                    .WithVector("EmissiveColor", new Vector4(0.1f, 0.2f, 0.3f, 1.0f))
                    .WithTexture("Bump", bumpMap)
                    .WithTexture("Opacity", opacityMap);

                bool imported = StandardLitMaterialImportUtility.TryImport(
                    description,
                    material,
                    StandardLitMaterialImportUtility.GetStandardLitShader());

                Assert.That(imported, Is.True);
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(0.25f, 0.3f, 0.35f, 0.75f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(material.GetFloat("_BumpScale"), Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(material.GetTexture("_BumpMap"), Is.SameAs(bumpMap));
                Assert.That(material.GetTexture("_OpacityMap"), Is.SameAs(opacityMap));
                Assert.That(material.GetColor("_EmissionColor"), Is.EqualTo(new Color(0.15f, 0.3f, 0.45f, 1.0f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True);
                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(bumpMap);
                Object.DestroyImmediate(opacityMap);
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TryImport_ReturnsFalse_WhenDescriptionDoesNotMatchSupportedMaterial()
        {
            Material material = CreateMaterial();
            try
            {
                bool imported = StandardLitMaterialImportUtility.TryImport(
                    new FakeImportedMaterialDescription(),
                    material,
                    StandardLitMaterialImportUtility.GetStandardLitShader());

                Assert.That(imported, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(material);
            }
        }

        [Test]
        public void TryImport_ConfiguresNormalTextureImporter_WhenTextureNameContainsNormal()
        {
            Material material = CreateMaterial();
            Texture2D normalTexture = null;

            try
            {
                normalTexture = CreateTextureAsset(NormalTextureAssetPath);

                TextureImporter textureImporter = AssetImporter.GetAtPath(NormalTextureAssetPath) as TextureImporter;
                Assert.That(textureImporter, Is.Not.Null);

                textureImporter.textureType = TextureImporterType.Default;
                textureImporter.wrapMode = TextureWrapMode.Clamp;
                textureImporter.filterMode = FilterMode.Point;
                textureImporter.SaveAndReimport();

                normalTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalTextureAssetPath);
                Assert.That(normalTexture, Is.Not.Null);
                Assert.That(StandardLitMaterialImportUtility.ShouldImportTextureAsNormalMap(normalTexture), Is.True);

                var description = new FakeImportedMaterialDescription()
                    .WithFloat("ClassIDa", 4.1222316e+08f)
                    .WithFloat("ClassIDb", -5.5903846e+08f)
                    .WithTexture("bump_map", normalTexture);

                bool imported = StandardLitMaterialImportUtility.TryImport(
                    description,
                    material,
                    StandardLitMaterialImportUtility.GetStandardLitShader());

                textureImporter = AssetImporter.GetAtPath(NormalTextureAssetPath) as TextureImporter;

                Assert.That(imported, Is.True);
                Assert.That(textureImporter, Is.Not.Null);
                Assert.That(textureImporter.textureType, Is.EqualTo(TextureImporterType.NormalMap));
                Assert.That(textureImporter.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(textureImporter.filterMode, Is.EqualTo(FilterMode.Trilinear));
            }
            finally
            {
                Object.DestroyImmediate(material);
                DeleteGeneratedAssetIfExists(NormalTextureAssetPath);
                DeleteGeneratedFolderIfExists(GeneratedAssetsFolderPath);
            }
        }

        private static Material CreateMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(StandardLitShaderAssetPath);
            Assert.That(shader, Is.Not.Null, $"Expected shader asset at '{StandardLitShaderAssetPath}'.");
            return new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static Texture2D CreateTexture()
        {
            return new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private sealed class FakeImportedMaterialDescription : IImportedMaterialDescription
        {
            private readonly Dictionary<string, float> m_Floats = new Dictionary<string, float>();
            private readonly Dictionary<string, Vector4> m_Vectors = new Dictionary<string, Vector4>();
            private readonly Dictionary<string, ImportedTextureProperty> m_Textures = new Dictionary<string, ImportedTextureProperty>();
            private readonly Dictionary<string, string> m_Strings = new Dictionary<string, string>();

            public FakeImportedMaterialDescription WithFloat(string propertyName, float value)
            {
                m_Floats[propertyName] = value;
                return this;
            }

            public FakeImportedMaterialDescription WithVector(string propertyName, Vector4 value)
            {
                m_Vectors[propertyName] = value;
                return this;
            }

            public FakeImportedMaterialDescription WithTexture(string propertyName, Texture texture)
            {
                m_Textures[propertyName] = new ImportedTextureProperty(texture, Vector2.zero, Vector2.one);
                return this;
            }

            public FakeImportedMaterialDescription WithString(string propertyName, string value)
            {
                m_Strings[propertyName] = value;
                return this;
            }

            public bool TryGetFloat(string propertyName, out float value)
            {
                return m_Floats.TryGetValue(propertyName, out value);
            }

            public bool TryGetVector(string propertyName, out Vector4 value)
            {
                return m_Vectors.TryGetValue(propertyName, out value);
            }

            public bool TryGetTexture(string propertyName, out ImportedTextureProperty value)
            {
                return m_Textures.TryGetValue(propertyName, out value);
            }

            public bool TryGetString(string propertyName, out string value)
            {
                return m_Strings.TryGetValue(propertyName, out value);
            }
        }

        private static Texture2D CreateTextureAsset(string assetPath)
        {
            string absolutePath = GetAbsoluteAssetPath(assetPath);
            string directoryPath = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            Texture2D sourceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };

            try
            {
                sourceTexture.SetPixels(new[]
                {
                    new Color(0.5f, 0.5f, 1.0f, 1.0f),
                    new Color(0.5f, 0.5f, 1.0f, 1.0f),
                    new Color(0.5f, 0.5f, 1.0f, 1.0f),
                    new Color(0.5f, 0.5f, 1.0f, 1.0f),
                });
                sourceTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
                File.WriteAllBytes(absolutePath, sourceTexture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(sourceTexture);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            Assert.That(importedTexture, Is.Not.Null, $"Expected texture asset at '{assetPath}'.");
            return importedTexture;
        }

        private static string GetAbsoluteAssetPath(string assetPath)
        {
            return Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                assetPath));
        }

        private static void DeleteGeneratedAssetIfExists(string assetPath)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(assetPath) != null)
            {
                AssetDatabase.DeleteAsset(assetPath);
                return;
            }

            string absolutePath = GetAbsoluteAssetPath(assetPath);
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }

        private static void DeleteGeneratedFolderIfExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.DeleteAsset(folderPath);
            }
        }

        private sealed class ColorEqualityComparer : IEqualityComparer<Color>
        {
            internal static readonly ColorEqualityComparer Instance = new ColorEqualityComparer();

            private const float Tolerance = 0.0001f;

            public bool Equals(Color x, Color y)
            {
                return Mathf.Abs(x.r - y.r) <= Tolerance
                    && Mathf.Abs(x.g - y.g) <= Tolerance
                    && Mathf.Abs(x.b - y.b) <= Tolerance
                    && Mathf.Abs(x.a - y.a) <= Tolerance;
            }

            public int GetHashCode(Color obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
