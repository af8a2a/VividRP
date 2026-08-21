using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class StandardLitRMOTexturePackerTests
    {
        private const string StandardLitShaderAssetPath =
            "Packages/com.af8a2a.vividrp/Shaders/Material/StandardLit/StandardLit.shader";
        private const string PackingShaderAssetPath =
            "Packages/com.af8a2a.vividrp/Editor/Shader/StandardLitRMOTexturePacker.shader";
        private const string GeneratedOutputFolder =
            "Assets/VividRPTests/StandardLitRMOTexturePackerOutput";
        private const string GeneratedOutputAssetPath =
            GeneratedOutputFolder + "/FirstImportRMO.png";

        [Test]
        public void PackingShader_ImportsWithoutCompilerErrors()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(PackingShaderAssetPath);
            Assert.That(shader, Is.Not.Null);
            Assert.That(shader.name, Is.EqualTo(StandardLitRMOTexturePacker.PackingShaderName));

            ShaderMessage[] errors = ShaderUtil.GetShaderMessages(shader)
                .Where(message => message.severity.ToString() == "Error")
                .ToArray();
            Assert.That(
                errors,
                Is.Empty,
                string.Join(
                    "\n",
                    errors.Select(message =>
                        $"{message.file}:{message.line}: {message.message}")));
        }

        [Test]
        public void Pack_StoresSourceRedChannelsInRoughnessMetallicAOOrder()
        {
            Texture2D roughnessMap = CreateTexture(new Color(0.2f, 0.9f, 0.8f, 1.0f));
            Texture2D metallicMap = CreateTexture(new Color(0.4f, 0.8f, 0.7f, 1.0f));
            Texture2D ambientOcclusionMap = CreateTexture(new Color(0.6f, 0.7f, 0.9f, 1.0f));
            Texture2D packedTexture = null;

            try
            {
                packedTexture = StandardLitRMOTexturePacker.Pack(
                    roughnessMap,
                    metallicMap,
                    ambientOcclusionMap,
                    1.0f,
                    0.0f,
                    1.0f);

                Color packedPixel = packedTexture.GetPixel(0, 0);
                Assert.That(packedPixel.r, Is.EqualTo(0.2f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.g, Is.EqualTo(0.4f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.b, Is.EqualTo(0.6f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.a, Is.EqualTo(1.0f).Within(1.0f / 255.0f));
            }
            finally
            {
                Object.DestroyImmediate(roughnessMap);
                Object.DestroyImmediate(metallicMap);
                Object.DestroyImmediate(ambientOcclusionMap);
                if (packedTexture != null)
                {
                    Object.DestroyImmediate(packedTexture);
                }
            }
        }

        [Test]
        public void Pack_UsesFallbackForUnassignedChannels()
        {
            Texture2D metallicMap = CreateTexture(new Color(0.75f, 0.0f, 0.0f, 1.0f));
            Texture2D packedTexture = null;

            try
            {
                packedTexture = StandardLitRMOTexturePacker.Pack(
                    null,
                    metallicMap,
                    null,
                    0.25f,
                    0.0f,
                    0.9f);

                Color packedPixel = packedTexture.GetPixel(0, 0);
                Assert.That(packedPixel.r, Is.EqualTo(0.25f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.g, Is.EqualTo(0.75f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.b, Is.EqualTo(0.9f).Within(1.0f / 255.0f));
            }
            finally
            {
                Object.DestroyImmediate(metallicMap);
                if (packedTexture != null)
                {
                    Object.DestroyImmediate(packedTexture);
                }
            }
        }

        [Test]
        public void Pack_PreservesRawValueFromSRGBDataTextureWithoutReimportingSource()
        {
            Texture2D roughnessMap = CreateTexture(
                new Color(0.5f, 0.0f, 0.0f, 1.0f),
                linear: false);
            Texture2D packedTexture = null;

            try
            {
                bool sourceWasSRGB = roughnessMap.isDataSRGB;

                packedTexture = StandardLitRMOTexturePacker.Pack(
                    roughnessMap,
                    null,
                    null,
                    0.0f,
                    0.0f,
                    1.0f);

                Assert.That(
                    packedTexture.GetPixel(0, 0).r,
                    Is.EqualTo(0.5f).Within(1.0f / 255.0f));
                Assert.That(roughnessMap.isDataSRGB, Is.EqualTo(sourceWasSRGB));
            }
            finally
            {
                Object.DestroyImmediate(roughnessMap);
                if (packedTexture != null)
                {
                    Object.DestroyImmediate(packedTexture);
                }
            }
        }

        [Test]
        public void TryPackToAsset_ConfiguresOutputImporterDuringFirstImport()
        {
            Texture2D roughnessMap = CreateTexture(new Color(0.25f, 0.0f, 0.0f, 1.0f));

            try
            {
                bool packed = StandardLitRMOTexturePacker.TryPackToAsset(
                    GeneratedOutputAssetPath,
                    roughnessMap,
                    null,
                    null,
                    0.0f,
                    0.0f,
                    1.0f,
                    string.Empty,
                    out Texture2D packedTexture,
                    out string errorMessage);

                Assert.That(packed, Is.True, errorMessage);
                Assert.That(packedTexture, Is.Not.Null);

                TextureImporter textureImporter =
                    AssetImporter.GetAtPath(GeneratedOutputAssetPath) as TextureImporter;
                Assert.That(textureImporter, Is.Not.Null);
                Assert.That(textureImporter.sRGBTexture, Is.False);
                Assert.That(textureImporter.alphaSource, Is.EqualTo(TextureImporterAlphaSource.None));
                Assert.That(textureImporter.wrapMode, Is.EqualTo(TextureWrapMode.Repeat));
                Assert.That(textureImporter.filterMode, Is.EqualTo(FilterMode.Trilinear));
                Assert.That(textureImporter.mipmapEnabled, Is.True);
                Assert.That(
                    textureImporter.userData,
                    Does.StartWith(StandardLitRMOTexturePacker.OutputImporterUserDataPrefix));
                Assert.That(
                    StandardLitRMOTexturePacker.IsRMOOutputAsset(GeneratedOutputAssetPath),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(roughnessMap);
                AssetDatabase.DeleteAsset(GeneratedOutputAssetPath);
                AssetDatabase.DeleteAsset(GeneratedOutputFolder);
            }
        }

        [Test]
        public void PackMaterial_UsesLegacyStandardLitTextureChannels()
        {
            Material material = CreateMaterial();
            Texture2D roughnessMap = CreateTexture(new Color(0.2f, 0.9f, 0.8f, 1.0f));
            Texture2D metallicMap = CreateTexture(new Color(0.4f, 0.8f, 0.7f, 0.25f));
            Texture2D ambientOcclusionMap = CreateTexture(new Color(0.9f, 0.6f, 0.1f, 1.0f));
            Texture2D packedTexture = null;

            try
            {
                material.SetTexture("_RoughnessMap", roughnessMap);
                material.SetTexture("_MetallicGlossMap", metallicMap);
                material.SetTexture("_OcclusionMap", ambientOcclusionMap);

                packedTexture = StandardLitRMOTexturePacker.PackMaterial(material);

                Color packedPixel = packedTexture.GetPixel(0, 0);
                Assert.That(packedPixel.r, Is.EqualTo(0.2f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.g, Is.EqualTo(0.4f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.b, Is.EqualTo(0.6f).Within(1.0f / 255.0f));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(roughnessMap);
                Object.DestroyImmediate(metallicMap);
                Object.DestroyImmediate(ambientOcclusionMap);
                if (packedTexture != null)
                {
                    Object.DestroyImmediate(packedTexture);
                }
            }
        }

        [Test]
        public void PackMaterial_ConvertsMetallicAlphaSmoothnessToRoughness()
        {
            Material material = CreateMaterial();
            Texture2D metallicMap = CreateTexture(new Color(0.4f, 0.8f, 0.7f, 0.25f));
            Texture2D packedTexture = null;

            try
            {
                material.SetTexture("_MetallicGlossMap", metallicMap);
                material.SetFloat("_SmoothnessTextureChannel", 0.0f);

                packedTexture = StandardLitRMOTexturePacker.PackMaterial(material);

                Color packedPixel = packedTexture.GetPixel(0, 0);
                Assert.That(packedPixel.r, Is.EqualTo(0.75f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.g, Is.EqualTo(0.4f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.b, Is.EqualTo(1.0f).Within(1.0f / 255.0f));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(metallicMap);
                if (packedTexture != null)
                {
                    Object.DestroyImmediate(packedTexture);
                }
            }
        }

        [Test]
        public void PackMaterial_ConvertsTintedAlbedoAlphaSmoothnessToRoughness()
        {
            Material material = CreateMaterial();
            Texture2D baseMap = CreateTexture(new Color(0.8f, 0.7f, 0.6f, 0.5f));
            Texture2D metallicMap = CreateTexture(new Color(0.4f, 0.8f, 0.7f, 0.1f));
            Texture2D packedTexture = null;

            try
            {
                material.SetTexture("_BaseMap", baseMap);
                material.SetColor("_BaseColor", new Color(1.0f, 1.0f, 1.0f, 0.5f));
                material.SetTexture("_MetallicGlossMap", metallicMap);
                material.SetFloat("_SmoothnessTextureChannel", 1.0f);

                packedTexture = StandardLitRMOTexturePacker.PackMaterial(material);

                Color packedPixel = packedTexture.GetPixel(0, 0);
                Assert.That(packedPixel.r, Is.EqualTo(0.75f).Within(1.0f / 255.0f));
                Assert.That(packedPixel.g, Is.EqualTo(0.4f).Within(1.0f / 255.0f));
            }
            finally
            {
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(metallicMap);
                if (packedTexture != null)
                {
                    Object.DestroyImmediate(packedTexture);
                }
            }
        }

        [Test]
        public void GetGeneratedAssetPath_PlacesTextureBesideModelInGeneratedFolder()
        {
            string assetPath = StandardLitRMOAutoPacker.GetGeneratedAssetPath(
                "Assets/Models/Robot.fbx",
                "Body");

            Assert.That(
                assetPath,
                Is.EqualTo("Assets/Models/VividRPGenerated/Robot_Body_RMO.png"));
        }

        private static Texture2D CreateTexture(Color color, bool linear = true)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, linear);
            texture.SetPixel(0, 0, color);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            return texture;
        }

        private static Material CreateMaterial()
        {
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(StandardLitShaderAssetPath);
            Assert.That(shader, Is.Not.Null);
            return new Material(shader);
        }
    }
}
