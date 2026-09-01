using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class GPUDrivenMaterialProxyTests
    {
        private static readonly MethodInfo s_OnValidateMethod =
            typeof(GPUDrivenMaterialProxy).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);

        [Test]
        public void TextureMode_DefaultsToLegacyCompatibleBindlessValue()
        {
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                Assert.That((int) GPUDrivenMaterialProxyTextureMode.Bindless, Is.Zero);
                Assert.That(materialProxy.TextureMode, Is.EqualTo(GPUDrivenMaterialProxyTextureMode.Bindless));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
            }
        }

        [Test]
        public void Model_DefaultsToStandardLit()
        {
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                Assert.That(
                    materialProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.StandardLit));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
            }
        }

        [Test]
        public void StreamedVirtualTexture_SwitchesToVirtualTextureAndClearsRawMapsButKeepsCommonData()
        {
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var baseMap = new Texture2D(1, 1);
            var bumpMap = new Texture2D(1, 1);
            var maskMap = new Texture2D(1, 1);
            var streamedVirtualTexture = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();

            try
            {
                var baseColor = new Color(0.8f, 0.6f, 0.4f, 0.5f);
                var textureTilingOffset = new Vector4(2.0f, 3.0f, 0.25f, 0.5f);
                var emissionColor = new Color(0.1f, 0.2f, 0.3f, 1.0f);
                materialProxy.BaseMap = baseMap;
                materialProxy.BumpMap = bumpMap;
                materialProxy.MaskMap = maskMap;
                materialProxy.BaseColor = baseColor;
                materialProxy.TextureTilingOffset = textureTilingOffset;
                materialProxy.BumpScale = 0.4f;
                materialProxy.MaskMode = GPUDrivenMaterialMaskMode.Roughness;
                materialProxy.Metallic = 0.75f;
                materialProxy.Roughness = 0.35f;
                materialProxy.MetallicRemap = new Vector2(0.1f, 0.8f);
                materialProxy.SmoothnessRemap = new Vector2(0.2f, 0.9f);
                materialProxy.AmbientOcclusionRemap = new Vector2(0.3f, 0.7f);
                materialProxy.EmissionColor = emissionColor;
                materialProxy.AlphaClip = true;
                materialProxy.Cutoff = 0.33f;
                materialProxy.CullMode = CullMode.Off;
                materialProxy.DisableLighting = true;

                materialProxy.StreamedVirtualTexture = streamedVirtualTexture;

                Assert.That(materialProxy.TextureMode, Is.EqualTo(GPUDrivenMaterialProxyTextureMode.VirtualTexture));
                Assert.That(materialProxy.StreamedVirtualTexture, Is.SameAs(streamedVirtualTexture));
                Assert.That(materialProxy.BaseMap, Is.Null);
                Assert.That(materialProxy.BumpMap, Is.Null);
                Assert.That(materialProxy.MaskMap, Is.Null);
                Assert.That(materialProxy.BaseColor, Is.EqualTo(baseColor));
                Assert.That(materialProxy.TextureTilingOffset, Is.EqualTo(textureTilingOffset));
                Assert.That(materialProxy.BumpScale, Is.EqualTo(0.4f));
                Assert.That(materialProxy.MaskMode, Is.EqualTo(GPUDrivenMaterialMaskMode.Roughness));
                Assert.That(materialProxy.Metallic, Is.EqualTo(0.75f));
                Assert.That(materialProxy.Roughness, Is.EqualTo(0.35f));
                Assert.That(materialProxy.MetallicRemap, Is.EqualTo(new Vector2(0.1f, 0.8f)));
                Assert.That(materialProxy.SmoothnessRemap, Is.EqualTo(new Vector2(0.2f, 0.9f)));
                Assert.That(materialProxy.AmbientOcclusionRemap, Is.EqualTo(new Vector2(0.3f, 0.7f)));
                Assert.That(materialProxy.EmissionColor, Is.EqualTo(emissionColor));
                Assert.That(materialProxy.AlphaClip, Is.True);
                Assert.That(materialProxy.Cutoff, Is.EqualTo(0.33f));
                Assert.That(materialProxy.CullMode, Is.EqualTo(CullMode.Off));
                Assert.That(materialProxy.DisableLighting, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(bumpMap);
                Object.DestroyImmediate(maskMap);
                Object.DestroyImmediate(streamedVirtualTexture);
            }
        }

        [Test]
        public void RawMap_SwitchesToBindlessAndClearsStreamedVirtualTexture()
        {
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var baseMap = new Texture2D(1, 1);
            var streamedVirtualTexture = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();

            try
            {
                materialProxy.StreamedVirtualTexture = streamedVirtualTexture;

                materialProxy.BaseMap = baseMap;

                Assert.That(materialProxy.TextureMode, Is.EqualTo(GPUDrivenMaterialProxyTextureMode.Bindless));
                Assert.That(materialProxy.BaseMap, Is.SameAs(baseMap));
                Assert.That(materialProxy.StreamedVirtualTexture, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(streamedVirtualTexture);
            }
        }

        [Test]
        public void OnValidate_PreservesLegacyDualTexturePayload()
        {
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var baseMap = new Texture2D(1, 1);
            var bumpMap = new Texture2D(1, 1);
            var maskMap = new Texture2D(1, 1);
            var streamedVirtualTexture = ScriptableObject.CreateInstance<VividVirtualTextureAsset>();

            try
            {
                var serializedProxy = new SerializedObject(materialProxy);
                serializedProxy.FindProperty("m_TextureMode").enumValueIndex =
                    (int) GPUDrivenMaterialProxyTextureMode.Bindless;
                serializedProxy.FindProperty("m_BaseMap").objectReferenceValue = baseMap;
                serializedProxy.FindProperty("m_BumpMap").objectReferenceValue = bumpMap;
                serializedProxy.FindProperty("m_MaskMap").objectReferenceValue = maskMap;
                serializedProxy.FindProperty("m_StreamedVirtualTexture").objectReferenceValue =
                    streamedVirtualTexture;
                serializedProxy.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(s_OnValidateMethod, Is.Not.Null);
                s_OnValidateMethod.Invoke(materialProxy, null);

                Assert.That(materialProxy.TextureMode, Is.EqualTo(GPUDrivenMaterialProxyTextureMode.Bindless));
                Assert.That(materialProxy.BaseMap, Is.SameAs(baseMap));
                Assert.That(materialProxy.BumpMap, Is.SameAs(bumpMap));
                Assert.That(materialProxy.MaskMap, Is.SameAs(maskMap));
                Assert.That(materialProxy.StreamedVirtualTexture, Is.SameAs(streamedVirtualTexture));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(baseMap);
                Object.DestroyImmediate(bumpMap);
                Object.DestroyImmediate(maskMap);
                Object.DestroyImmediate(streamedVirtualTexture);
            }
        }

        [Test]
        public void OnValidate_IncrementsRevision_WhenProxyChanges()
        {
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                uint initialRevision = materialProxy.Revision;
                materialProxy.BaseColor = Color.red;

                Assert.That(s_OnValidateMethod, Is.Not.Null);
                s_OnValidateMethod.Invoke(materialProxy, null);

                Assert.That(materialProxy.Revision, Is.GreaterThan(initialRevision));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
            }
        }

        [Test]
        public void Setter_IncrementsRevision_WhenValueChanges()
        {
            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();

            try
            {
                uint initialRevision = materialProxy.Revision;
                materialProxy.BaseColor = Color.green;

                Assert.That(materialProxy.Revision, Is.GreaterThan(initialRevision));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
            }
        }

        [Test]
        public void MaterialGraphSetter_IncrementsRevision_WhenBindingChanges()
        {
            var materialProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var materialGraph =
                ScriptableObject.CreateInstance<MaterialGraphImportAsset>();

            try
            {
                uint initialRevision = materialProxy.Revision;

                materialProxy.MaterialGraph = materialGraph;

                Assert.That(materialProxy.MaterialGraph, Is.SameAs(materialGraph));
                Assert.That(materialProxy.Revision, Is.GreaterThan(initialRevision));
            }
            finally
            {
                Object.DestroyImmediate(materialGraph);
                Object.DestroyImmediate(materialProxy);
            }
        }

        [Test]
        public void ParameterOverride_IsDeclarationAddressedAndTracksRevision()
        {
            var materialProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            try
            {
                uint initialRevision = materialProxy.Revision;
                var initialValue = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);

                materialProxy.SetParameterOverride(
                    "UserTint",
                    GPUDrivenMaterialParameterType.Float4,
                    initialValue);

                Assert.That(materialProxy.ParameterOverrides, Has.Count.EqualTo(1));
                Assert.That(
                    materialProxy.ParameterOverrides[0].Symbol,
                    Is.EqualTo("UserTint"));
                Assert.That(
                    materialProxy.ParameterOverrides[0].Type,
                    Is.EqualTo(GPUDrivenMaterialParameterType.Float4));
                Assert.That(
                    materialProxy.ParameterOverrides[0].Value,
                    Is.EqualTo(initialValue));
                Assert.That(materialProxy.Revision, Is.GreaterThan(initialRevision));

                uint addedRevision = materialProxy.Revision;
                var replacementValue = new Vector4(0.9f, 0.8f, 0.7f, 0.6f);
                materialProxy.SetParameterOverride(
                    "UserTint",
                    GPUDrivenMaterialParameterType.Float4,
                    replacementValue);

                Assert.That(materialProxy.ParameterOverrides, Has.Count.EqualTo(1));
                Assert.That(
                    materialProxy.ParameterOverrides[0].Value,
                    Is.EqualTo(replacementValue));
                Assert.That(materialProxy.Revision, Is.GreaterThan(addedRevision));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
            }
        }

        [Test]
        public void VirtualTextureOverride_ReplacesTexture2DBySymbolAndTracksRevision()
        {
            var materialProxy =
                ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var texture = new Texture2D(1, 1);
            var virtualTexture =
                ScriptableObject.CreateInstance<VividVirtualTextureAsset>();
            try
            {
                materialProxy.SetTextureOverride("UserTexture", texture);
                uint textureRevision = materialProxy.Revision;

                materialProxy.SetVirtualTextureOverride(
                    "UserTexture",
                    virtualTexture,
                    new Vector4(2.0f, 3.0f, 0.25f, 0.5f));

                Assert.That(materialProxy.TextureOverrides, Has.Count.EqualTo(1));
                Assert.That(
                    materialProxy.TextureOverrides[0].Symbol,
                    Is.EqualTo("UserTexture"));
                Assert.That(materialProxy.TextureOverrides[0].Texture, Is.Null);
                Assert.That(
                    materialProxy.TextureOverrides[0].StreamedVirtualTexture,
                    Is.SameAs(virtualTexture));
                Assert.That(
                    materialProxy.TextureOverrides[0].TilingOffset,
                    Is.EqualTo(new Vector4(2.0f, 3.0f, 0.25f, 0.5f)));
                Assert.That(materialProxy.Revision, Is.GreaterThan(textureRevision));
            }
            finally
            {
                Object.DestroyImmediate(virtualTexture);
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(materialProxy);
            }
        }

        [Test]
        public void SyncFromSourceMaterial_MapsStandardLitCoreProperties_WhenMaterialIsSupported()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            Texture2D baseMap = null;
            Texture2D bumpMap = null;
            Texture2D maskMap = null;
            Material material = null;

            try
            {
                baseMap = new Texture2D(1, 1);
                bumpMap = new Texture2D(1, 1);
                maskMap = new Texture2D(1, 1);
                material = new Material(shader);
                material.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.75f, 1.0f));
                material.SetTexture("_BaseMap", baseMap);
                material.SetTextureScale("_BaseMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_BaseMap", new Vector2(0.1f, 0.2f));
                material.SetTexture("_BumpMap", bumpMap);
                material.SetFloat("_BumpScale", 0.6f);
                material.SetTexture("_RMOMap", maskMap);
                material.SetFloat("_Metallic", 0.4f);
                material.SetFloat("_Smoothness", 0.3f);
                material.SetColor("_EmissionColor", new Color(1.0f, 0.5f, 0.0f, 1.0f));
                material.SetFloat("_AlphaClip", 1.0f);
                material.SetFloat("_Cutoff", 0.33f);
                material.SetFloat("_Cull", (float) CullMode.Off);

                uint initialRevision = materialProxy.Revision;
                GPUDrivenMaterialProxySyncResult result =
                    materialProxy.SyncFromSourceMaterial(material);

                Assert.That(result.Success, Is.True);
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(material));
                Assert.That(materialProxy.Model, Is.EqualTo(GPUDrivenMaterialProxyModel.StandardLit));
                Assert.That(materialProxy.BaseMap, Is.SameAs(baseMap));
                Assert.That(materialProxy.BaseColor.r, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(materialProxy.BaseColor.g, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(materialProxy.BaseColor.b, Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(materialProxy.BaseColor.a, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(materialProxy.TextureTilingOffset, Is.EqualTo(new Vector4(2.0f, 3.0f, 0.1f, 0.2f)));
                Assert.That(materialProxy.BumpMap, Is.SameAs(bumpMap));
                Assert.That(materialProxy.BumpScale, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(materialProxy.MaskMap, Is.SameAs(maskMap));
                Assert.That(materialProxy.MaskMode, Is.EqualTo(GPUDrivenMaterialMaskMode.RoughnessMetallicOcclusion));
                Assert.That(materialProxy.Metallic, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(materialProxy.Roughness, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(materialProxy.EmissionColor.r, Is.EqualTo(1.0f).Within(0.0001f));
                Assert.That(materialProxy.AlphaClip, Is.True);
                Assert.That(materialProxy.Cutoff, Is.EqualTo(0.33f).Within(0.0001f));
                Assert.That(materialProxy.CullMode, Is.EqualTo(CullMode.Off));
                Assert.That(materialProxy.DisableLighting, Is.False);
                Assert.That(materialProxy.Revision, Is.GreaterThan(initialRevision));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);

                if (material != null)
                {
                    Object.DestroyImmediate(material);
                }

                if (baseMap != null)
                {
                    Object.DestroyImmediate(baseMap);
                }

                if (bumpMap != null)
                {
                    Object.DestroyImmediate(bumpMap);
                }

                if (maskMap != null)
                {
                    Object.DestroyImmediate(maskMap);
                }
            }
        }

        [Test]
        public void SyncFromSourceMaterial_PreservesDualSlabTopologyAndUpdatesBasePayload()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            var materialProxy = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var topSlab = ScriptableObject.CreateInstance<GPUDrivenMaterialProxy>();
            var definition =
                ScriptableObject.CreateInstance<GPUDrivenDualSlabMaterialDefinition>();
            Material material = null;

            try
            {
                material = new Material(shader);
                material.SetColor("_BaseColor", new Color(0.3f, 0.5f, 0.7f, 1.0f));
                material.SetFloat("_Metallic", 0.65f);
                material.SetFloat("_Smoothness", 0.2f);
                definition.TopSlab = topSlab;
                materialProxy.Model = GPUDrivenMaterialProxyModel.DualSlab;
                materialProxy.DualSlabDefinition = definition;
                materialProxy.LayerWeight = 0.35f;

                GPUDrivenMaterialProxySyncResult result =
                    materialProxy.SyncFromSourceMaterial(material);

                Assert.That(result.Success, Is.True, result.ErrorMessage);
                Assert.That(result.Changed, Is.True);
                Assert.That(materialProxy.SourceMaterial, Is.SameAs(material));
                Assert.That(
                    materialProxy.Model,
                    Is.EqualTo(GPUDrivenMaterialProxyModel.DualSlab));
                Assert.That(materialProxy.DualSlabDefinition, Is.SameAs(definition));
                Assert.That(materialProxy.LayerWeight, Is.EqualTo(0.35f).Within(0.0001f));
                Assert.That(materialProxy.BaseColor.r, Is.EqualTo(0.3f).Within(0.0001f));
                Assert.That(materialProxy.BaseColor.g, Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(materialProxy.BaseColor.b, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(materialProxy.Metallic, Is.EqualTo(0.65f).Within(0.0001f));
                Assert.That(materialProxy.Roughness, Is.EqualTo(0.8f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(materialProxy);
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(topSlab);

                if (material != null)
                {
                    Object.DestroyImmediate(material);
                }
            }
        }

        [Test]
        public void CollectUnsupportedWarnings_ReturnsWarnings_WhenMaterialUsesUnsupportedFeatures()
        {
            Shader shader = Shader.Find("VividRP/Material/StandardLit");
            if (shader == null)
            {
                Assert.Ignore("VividRP/Material/StandardLit shader is not available.");
            }

            Texture2D opacityMap = null;
            Texture2D metallicGlossMap = null;
            Texture2D roughnessMap = null;
            Texture2D emissionMap = null;
            Texture2D occlusionMap = null;
            Material material = null;

            try
            {
                opacityMap = new Texture2D(1, 1);
                metallicGlossMap = new Texture2D(1, 1);
                roughnessMap = new Texture2D(1, 1);
                emissionMap = new Texture2D(1, 1);
                occlusionMap = new Texture2D(1, 1);
                material = new Material(shader);
                material.SetTexture("_OpacityMap", opacityMap);
                material.SetTexture("_MetallicGlossMap", metallicGlossMap);
                material.SetTexture("_RoughnessMap", roughnessMap);
                material.SetTexture("_EmissionMap", emissionMap);
                material.SetTexture("_OcclusionMap", occlusionMap);
                material.SetFloat("_ClearCoatMask", 1.0f);
                material.SetFloat("_SmoothnessTextureChannel", 1.0f);

                string[] warnings = material.CollectUnsupportedWarnings();
                string warningText = string.Join("\n", warnings);

                Assert.That(warnings, Has.Length.GreaterThanOrEqualTo(6));
                Assert.That(warningText, Does.Contain("_OpacityMap"));
                Assert.That(warningText, Does.Contain("_MetallicGlossMap"));
                Assert.That(warningText, Does.Contain("_RoughnessMap"));
                Assert.That(warningText, Does.Contain("_EmissionMap"));
                Assert.That(warningText, Does.Contain("_OcclusionMap"));
                Assert.That(warningText, Does.Contain("_ClearCoatMask"));
                Assert.That(warningText, Does.Contain("_SmoothnessTextureChannel"));
            }
            finally
            {
                if (material != null)
                {
                    Object.DestroyImmediate(material);
                }

                if (opacityMap != null)
                {
                    Object.DestroyImmediate(opacityMap);
                }

                if (metallicGlossMap != null)
                {
                    Object.DestroyImmediate(metallicGlossMap);
                }

                if (roughnessMap != null)
                {
                    Object.DestroyImmediate(roughnessMap);
                }

                if (emissionMap != null)
                {
                    Object.DestroyImmediate(emissionMap);
                }

                if (occlusionMap != null)
                {
                    Object.DestroyImmediate(occlusionMap);
                }
            }
        }
    }
}
