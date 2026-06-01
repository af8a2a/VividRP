using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class HDRPMaterialConverterTests
    {
        private const string GeneratedAssetsFolderPath = "Assets/Tests/VividRP/GeneratedHDRPMaterialConverter";
        private const string SourceShaderAssetPath = GeneratedAssetsFolderPath + "/HDRPLitSource.shader";
        private const string UnlitSourceShaderAssetPath = GeneratedAssetsFolderPath + "/HDRPUnlitSource.shader";
        private const string MissingShaderStandInAssetPath = GeneratedAssetsFolderPath + "/MissingShaderStandIn.shader";

        [Test]
        public void Convert_MapsHDRPLitProperties_ToStandardLit()
        {
            Shader sourceShader = CreateSourceShader();
            Material sourceMaterial = null;
            Material destinationMaterial = null;
            Texture2D baseMap = CreateTexture();
            Texture2D maskMap = CreateTexture();
            Texture2D normalMap = CreateTexture();
            Texture2D emissionMap = CreateTexture();

            try
            {
                sourceMaterial = new Material(sourceShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                destinationMaterial = new Material(sourceShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };

                sourceMaterial.SetTexture("_BaseColorMap", baseMap);
                sourceMaterial.SetTextureScale("_BaseColorMap", new Vector2(2.0f, 3.0f));
                sourceMaterial.SetTextureOffset("_BaseColorMap", new Vector2(0.25f, 0.5f));
                sourceMaterial.SetTexture("_MaskMap", maskMap);
                sourceMaterial.SetTexture("_NormalMap", normalMap);
                sourceMaterial.SetTexture("_EmissiveColorMap", emissionMap);
                sourceMaterial.SetColor("_BaseColor", new Color(0.2f, 0.3f, 0.4f, 0.75f));
                sourceMaterial.SetColor("_EmissionColor", Color.white);
                sourceMaterial.SetColor("_EmissiveColor", new Color(1.0f, 0.5f, 0.25f, 1.0f));
                sourceMaterial.SetFloat("_Metallic", 0.2f);
                sourceMaterial.SetFloat("_MetallicRemapMax", 0.7f);
                sourceMaterial.SetFloat("_Smoothness", 0.3f);
                sourceMaterial.SetFloat("_SmoothnessRemapMax", 0.8f);
                sourceMaterial.SetFloat("_NormalScale", 0.4f);
                sourceMaterial.SetFloat("_AlphaCutoff", 0.33f);
                sourceMaterial.SetFloat("_AlphaCutoffEnable", 1.0f);
                sourceMaterial.SetFloat("_CoatMask", 0.6f);
                sourceMaterial.SetFloat("_CullMode", 2.0f);
                sourceMaterial.SetFloat("_DoubleSidedEnable", 1.0f);

                var upgrader = HDRPMaterialConverter.CreateHDRPLitUpgrader(sourceShader.name);
                upgrader.Convert(sourceMaterial, destinationMaterial);

                Assert.That(destinationMaterial.shader.name, Is.EqualTo(StandardLitMaterialImportUtility.StandardLitShaderName));
                Assert.That(destinationMaterial.GetTexture("_BaseMap"), Is.SameAs(baseMap));
                Assert.That(destinationMaterial.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(2.0f, 3.0f)));
                Assert.That(destinationMaterial.GetTextureOffset("_BaseMap"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(destinationMaterial.GetTexture("_MetallicGlossMap"), Is.SameAs(maskMap));
                Assert.That(destinationMaterial.GetTexture("_OcclusionMap"), Is.SameAs(maskMap));
                Assert.That(destinationMaterial.GetTexture("_BumpMap"), Is.SameAs(normalMap));
                Assert.That(destinationMaterial.GetTexture("_EmissionMap"), Is.SameAs(emissionMap));
                Assert.That(destinationMaterial.GetColor("_BaseColor"), Is.EqualTo(new Color(0.2f, 0.3f, 0.4f, 0.75f)).Using(ColorEqualityComparer.Instance));
                Assert.That(destinationMaterial.GetColor("_EmissionColor"), Is.EqualTo(new Color(1.0f, 0.5f, 0.25f, 1.0f)).Using(ColorEqualityComparer.Instance));
                Assert.That(destinationMaterial.GetFloat("_Metallic"), Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_Smoothness"), Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_BumpScale"), Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_Cutoff"), Is.EqualTo(0.33f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_AlphaClip"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_ClearCoatMask"), Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_Cull"), Is.EqualTo(0.0f).Within(0.0001f));
                Assert.That(destinationMaterial.IsKeywordEnabled("_METALLICSPECGLOSSMAP"), Is.True);
                Assert.That(destinationMaterial.IsKeywordEnabled("_OCCLUSIONMAP"), Is.True);
                Assert.That(destinationMaterial.IsKeywordEnabled("_NORMALMAP"), Is.True);
                Assert.That(destinationMaterial.IsKeywordEnabled("_EMISSION"), Is.True);
                Assert.That(destinationMaterial.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(destinationMaterial.IsKeywordEnabled("_CLEARCOAT"), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(maskMap);
                Object.DestroyImmediate(normalMap);
                Object.DestroyImmediate(emissionMap);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(destinationMaterial);
                DeleteGeneratedAssetIfExists(SourceShaderAssetPath);
                DeleteGeneratedFolderIfExists(GeneratedAssetsFolderPath);
            }
        }

        [Test]
        public void Convert_MapsHDRPUnlitProperties_ToUnlit()
        {
            Shader sourceShader = CreateUnlitSourceShader();
            Material sourceMaterial = null;
            Material destinationMaterial = null;
            Texture2D colorMap = CreateTexture();
            Texture2D emissiveMap = CreateTexture();

            try
            {
                sourceMaterial = new Material(sourceShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };
                destinationMaterial = new Material(sourceShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };

                sourceMaterial.SetTexture("_UnlitColorMap", colorMap);
                sourceMaterial.SetTextureScale("_UnlitColorMap", new Vector2(2.0f, 3.0f));
                sourceMaterial.SetTextureOffset("_UnlitColorMap", new Vector2(0.25f, 0.5f));
                sourceMaterial.SetTexture("_EmissiveColorMap", emissiveMap);
                sourceMaterial.SetColor("_UnlitColor", new Color(0.2f, 0.3f, 0.4f, 0.75f));
                sourceMaterial.SetColor("_EmissiveColor", new Color(1.0f, 0.5f, 0.25f, 1.0f));
                sourceMaterial.SetFloat("_AlphaCutoff", 0.33f);
                sourceMaterial.SetFloat("_AlphaCutoffEnable", 1.0f);
                sourceMaterial.SetFloat("_AlphaRemapMin", 0.1f);
                sourceMaterial.SetFloat("_AlphaRemapMax", 0.9f);
                sourceMaterial.SetFloat("_SurfaceType", UnlitMaterialUtility.TransparentSurface);
                sourceMaterial.SetFloat("_BlendMode", 1.0f);
                sourceMaterial.SetFloat("_DoubleSidedEnable", 1.0f);
                sourceMaterial.SetFloat("_TransparentZWrite", 1.0f);
                sourceMaterial.SetFloat("_TransparentSortPriority", 4.0f);
                sourceMaterial.SetFloat("_EmissiveExposureWeight", 0.25f);

                var upgrader = HDRPMaterialConverter.CreateHDRPUnlitUpgrader(sourceShader.name);
                upgrader.Convert(sourceMaterial, destinationMaterial);

                Assert.That(destinationMaterial.shader.name, Is.EqualTo(UnlitMaterialUtility.UnlitShaderName));
                Assert.That(destinationMaterial.GetTexture("_UnlitColorMap"), Is.SameAs(colorMap));
                Assert.That(destinationMaterial.GetTextureScale("_UnlitColorMap"), Is.EqualTo(new Vector2(2.0f, 3.0f)));
                Assert.That(destinationMaterial.GetTextureOffset("_UnlitColorMap"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(destinationMaterial.GetTexture("_EmissiveColorMap"), Is.SameAs(emissiveMap));
                Assert.That(destinationMaterial.GetColor("_UnlitColor"), Is.EqualTo(new Color(0.2f, 0.3f, 0.4f, 0.75f)).Using(ColorEqualityComparer.Instance));
                Assert.That(destinationMaterial.GetColor("_EmissiveColor"), Is.EqualTo(new Color(1.0f, 0.5f, 0.25f, 1.0f)).Using(ColorEqualityComparer.Instance));
                Assert.That(destinationMaterial.GetFloat("_AlphaCutoff"), Is.EqualTo(0.33f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_AlphaCutoffEnable"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_AlphaRemapMin"), Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_AlphaRemapMax"), Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_SurfaceType"), Is.EqualTo(UnlitMaterialUtility.TransparentSurface).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_BlendMode"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_CullMode"), Is.EqualTo((float)UnityEngine.Rendering.CullMode.Off).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_TransparentZWrite"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_EmissiveExposureWeight"), Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(destinationMaterial.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(destinationMaterial.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), Is.True);
                Assert.That(destinationMaterial.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                Assert.That(destinationMaterial.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent + 4));
                Assert.That(destinationMaterial.GetFloat("_ZWrite"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_SrcBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.One).Within(0.0001f));
                Assert.That(destinationMaterial.GetFloat("_DstBlend"), Is.EqualTo((float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(colorMap);
                Object.DestroyImmediate(emissiveMap);
                Object.DestroyImmediate(sourceMaterial);
                Object.DestroyImmediate(destinationMaterial);
                DeleteGeneratedAssetIfExists(UnlitSourceShaderAssetPath);
                DeleteGeneratedFolderIfExists(GeneratedAssetsFolderPath);
            }
        }

        [Test]
        public void TryConvertMaterial_MapsSerializedHDRPProperties_WhenCurrentShaderHasNoHDRPProperties()
        {
            Shader sourceShader = CreateSourceShader();
            Shader missingShaderStandIn = CreateMissingShaderStandIn();
            Material material = null;
            Texture2D baseMap = CreateTexture();
            Texture2D maskMap = CreateTexture();
            Texture2D normalMap = CreateTexture();
            Texture2D emissionMap = CreateTexture();

            try
            {
                material = new Material(sourceShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };

                material.SetTexture("_BaseColorMap", baseMap);
                material.SetTextureScale("_BaseColorMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_BaseColorMap", new Vector2(0.25f, 0.5f));
                material.SetTexture("_MaskMap", maskMap);
                material.SetTexture("_NormalMap", normalMap);
                material.SetTexture("_EmissiveColorMap", emissionMap);
                material.SetColor("_BaseColor", new Color(0.2f, 0.3f, 0.4f, 0.75f));
                material.SetColor("_EmissiveColor", new Color(1.0f, 0.5f, 0.25f, 1.0f));
                material.SetFloat("_MetallicRemapMax", 0.7f);
                material.SetFloat("_SmoothnessRemapMax", 0.8f);
                material.SetFloat("_NormalScale", 0.4f);
                material.SetFloat("_AlphaCutoff", 0.33f);
                material.SetFloat("_AlphaCutoffEnable", 1.0f);
                material.SetFloat("_CoatMask", 0.6f);
                material.SetFloat("_DoubleSidedEnable", 1.0f);

                material.shader = missingShaderStandIn;
                Assert.That(material.HasProperty("_BaseColorMap"), Is.False);
                Assert.That(HDRPMaterialConversionUtility.CanConvertMaterial(material), Is.True);

                bool converted = HDRPMaterialConversionUtility.TryConvertMaterial(material, false, false);

                Assert.That(converted, Is.True);
                Assert.That(material.shader.name, Is.EqualTo(StandardLitMaterialImportUtility.StandardLitShaderName));
                Assert.That(material.GetTexture("_BaseMap"), Is.SameAs(baseMap));
                Assert.That(material.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(2.0f, 3.0f)));
                Assert.That(material.GetTextureOffset("_BaseMap"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(material.GetTexture("_MetallicGlossMap"), Is.SameAs(maskMap));
                Assert.That(material.GetTexture("_OcclusionMap"), Is.SameAs(maskMap));
                Assert.That(material.GetTexture("_BumpMap"), Is.SameAs(normalMap));
                Assert.That(material.GetTexture("_EmissionMap"), Is.SameAs(emissionMap));
                Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(0.2f, 0.3f, 0.4f, 0.75f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetColor("_EmissionColor"), Is.EqualTo(new Color(1.0f, 0.5f, 0.25f, 1.0f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetFloat("_Metallic"), Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(material.GetFloat("_Smoothness"), Is.EqualTo(0.8f).Within(0.0001f));
                Assert.That(material.GetFloat("_BumpScale"), Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(material.GetFloat("_Cutoff"), Is.EqualTo(0.33f).Within(0.0001f));
                Assert.That(material.GetFloat("_AlphaClip"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(material.GetFloat("_ClearCoatMask"), Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(material.GetFloat("_Cull"), Is.EqualTo(0.0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(maskMap);
                Object.DestroyImmediate(normalMap);
                Object.DestroyImmediate(emissionMap);
                Object.DestroyImmediate(material);
                DeleteGeneratedAssetIfExists(SourceShaderAssetPath);
                DeleteGeneratedAssetIfExists(MissingShaderStandInAssetPath);
                DeleteGeneratedFolderIfExists(GeneratedAssetsFolderPath);
            }
        }

        [Test]
        public void TryConvertMaterial_MapsSerializedHDRPUnlitProperties_WhenCurrentShaderHasNoHDRPProperties()
        {
            Shader sourceShader = CreateUnlitSourceShader();
            Shader missingShaderStandIn = CreateMissingShaderStandIn();
            Material material = null;
            Texture2D colorMap = CreateTexture();
            Texture2D emissiveMap = CreateTexture();

            try
            {
                material = new Material(sourceShader)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                };

                material.SetTexture("_UnlitColorMap", colorMap);
                material.SetTextureScale("_UnlitColorMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_UnlitColorMap", new Vector2(0.25f, 0.5f));
                material.SetTexture("_EmissiveColorMap", emissiveMap);
                material.SetColor("_UnlitColor", new Color(0.2f, 0.3f, 0.4f, 0.75f));
                material.SetColor("_EmissiveColor", new Color(1.0f, 0.5f, 0.25f, 1.0f));
                material.SetFloat("_AlphaCutoff", 0.33f);
                material.SetFloat("_AlphaCutoffEnable", 1.0f);
                material.SetFloat("_SurfaceType", UnlitMaterialUtility.TransparentSurface);
                material.SetFloat("_TransparentSortPriority", 4.0f);

                material.shader = missingShaderStandIn;
                Assert.That(material.HasProperty("_UnlitColorMap"), Is.False);
                Assert.That(HDRPMaterialConversionUtility.CanConvertMaterial(material), Is.True);

                bool converted = HDRPMaterialConversionUtility.TryConvertMaterial(material, false, false);

                Assert.That(converted, Is.True);
                Assert.That(material.shader.name, Is.EqualTo(UnlitMaterialUtility.UnlitShaderName));
                Assert.That(material.GetTexture("_UnlitColorMap"), Is.SameAs(colorMap));
                Assert.That(material.GetTextureScale("_UnlitColorMap"), Is.EqualTo(new Vector2(2.0f, 3.0f)));
                Assert.That(material.GetTextureOffset("_UnlitColorMap"), Is.EqualTo(new Vector2(0.25f, 0.5f)));
                Assert.That(material.GetTexture("_EmissiveColorMap"), Is.SameAs(emissiveMap));
                Assert.That(material.GetColor("_UnlitColor"), Is.EqualTo(new Color(0.2f, 0.3f, 0.4f, 0.75f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetColor("_EmissiveColor"), Is.EqualTo(new Color(1.0f, 0.5f, 0.25f, 1.0f)).Using(ColorEqualityComparer.Instance));
                Assert.That(material.GetFloat("_AlphaCutoff"), Is.EqualTo(0.33f).Within(0.0001f));
                Assert.That(material.GetFloat("_AlphaCutoffEnable"), Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(material.IsKeywordEnabled("_ALPHATEST_ON"), Is.True);
                Assert.That(material.IsKeywordEnabled("_EMISSIVE_COLOR_MAP"), Is.True);
                Assert.That(material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"), Is.True);
                Assert.That(material.renderQueue, Is.EqualTo((int)UnityEngine.Rendering.RenderQueue.Transparent + 4));
            }
            finally
            {
                Object.DestroyImmediate(colorMap);
                Object.DestroyImmediate(emissiveMap);
                Object.DestroyImmediate(material);
                DeleteGeneratedAssetIfExists(UnlitSourceShaderAssetPath);
                DeleteGeneratedAssetIfExists(MissingShaderStandInAssetPath);
                DeleteGeneratedFolderIfExists(GeneratedAssetsFolderPath);
            }
        }

        private static Shader CreateSourceShader()
        {
            return CreateShader(SourceShaderAssetPath, GetSourceShaderText());
        }

        private static Shader CreateUnlitSourceShader()
        {
            return CreateShader(UnlitSourceShaderAssetPath, GetUnlitSourceShaderText());
        }

        private static Shader CreateMissingShaderStandIn()
        {
            return CreateShader(
                MissingShaderStandInAssetPath,
@"Shader ""Hidden/VividRP/Tests/MissingShaderStandIn""
{
    SubShader
    {
        Pass
        {
        }
    }
}");
        }

        private static Shader CreateShader(string assetPath, string shaderText)
        {
            string absolutePath = GetAbsoluteAssetPath(assetPath);
            string directoryPath = Path.GetDirectoryName(absolutePath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            File.WriteAllText(absolutePath, shaderText);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(assetPath);
            Assert.That(shader, Is.Not.Null, $"Expected generated shader asset at '{assetPath}'.");
            return shader;
        }

        private static string GetSourceShaderText()
        {
            return
@"Shader ""Hidden/VividRP/Tests/HDRPLitSource""
{
    Properties
    {
        _BaseColorMap(""Base Color Map"", 2D) = ""white"" {}
        _MainTex(""Main Tex"", 2D) = ""white"" {}
        _MaskMap(""Mask Map"", 2D) = ""white"" {}
        _NormalMap(""Normal Map"", 2D) = ""bump"" {}
        _EmissiveColorMap(""Emissive Color Map"", 2D) = ""black"" {}
        _BaseColor(""Base Color"", Color) = (1, 1, 1, 1)
        _Color(""Color"", Color) = (1, 1, 1, 1)
        _EmissionColor(""HDRP Emission Color"", Color) = (1, 1, 1, 1)
        _EmissiveColor(""Emissive Color"", Color) = (0, 0, 0, 1)
        _Metallic(""Metallic"", Float) = 0
        _MetallicRemapMax(""Metallic Remap Max"", Float) = 1
        _Smoothness(""Smoothness"", Float) = 0.5
        _SmoothnessRemapMax(""Smoothness Remap Max"", Float) = 1
        _NormalScale(""Normal Scale"", Float) = 1
        _AlphaCutoff(""Alpha Cutoff"", Float) = 0.5
        _AlphaCutoffEnable(""Alpha Cutoff Enable"", Float) = 0
        _CoatMask(""Coat Mask"", Float) = 0
        _CullMode(""Cull Mode"", Float) = 2
        _DoubleSidedEnable(""Double Sided Enable"", Float) = 0
    }

    SubShader
    {
        Pass
        {
        }
    }
}";
        }

        private static string GetUnlitSourceShaderText()
        {
            return
@"Shader ""Hidden/VividRP/Tests/HDRPUnlitSource""
{
    Properties
    {
        _UnlitColorMap(""Color Map"", 2D) = ""white"" {}
        _EmissiveColorMap(""Emissive Color Map"", 2D) = ""white"" {}
        _MainTex(""Main Tex"", 2D) = ""white"" {}
        _UnlitColor(""Color"", Color) = (1, 1, 1, 1)
        _Color(""Legacy Color"", Color) = (1, 1, 1, 1)
        _EmissiveColor(""Emissive Color"", Color) = (0, 0, 0, 1)
        _EmissiveColorLDR(""Emissive Color LDR"", Color) = (0, 0, 0, 1)
        _AlphaCutoff(""Alpha Cutoff"", Float) = 0.5
        _AlphaCutoffEnable(""Alpha Cutoff Enable"", Float) = 0
        _AlphaRemapMin(""Alpha Remap Min"", Float) = 0
        _AlphaRemapMax(""Alpha Remap Max"", Float) = 1
        _SurfaceType(""Surface Type"", Float) = 0
        _BlendMode(""Blend Mode"", Float) = 0
        _CullMode(""Cull Mode"", Float) = 2
        _DoubleSidedEnable(""Double Sided Enable"", Float) = 0
        _TransparentZWrite(""Transparent ZWrite"", Float) = 0
        _TransparentSortPriority(""Transparent Sort Priority"", Float) = 0
        _AlbedoAffectEmissive(""Albedo Affect Emissive"", Float) = 0
        _EmissiveExposureWeight(""Emissive Exposure Weight"", Float) = 1
        _UseEmissiveIntensity(""Use Emissive Intensity"", Float) = 0
        _EmissiveIntensity(""Emissive Intensity"", Float) = 1
    }

    SubShader
    {
        Pass
        {
        }
    }
}";
        }

        private static Texture2D CreateTexture()
        {
            return new Texture2D(1, 1)
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
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
