using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Editor.GPUDriven;
using VividRP.Runtime.GPUDriven;
using Object = UnityEngine.Object;

namespace VividRP.Editor.Tests
{
    public class GPUDrivenMaterialProxyTests
    {
        private static readonly MethodInfo s_OnValidateMethod =
            typeof(GPUDrivenMaterialProxy).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);

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
            Material material = null;

            try
            {
                baseMap = new Texture2D(1, 1);
                bumpMap = new Texture2D(1, 1);
                material = new Material(shader);
                material.SetColor("_BaseColor", new Color(0.25f, 0.5f, 0.75f, 1.0f));
                material.SetTexture("_BaseMap", baseMap);
                material.SetTextureScale("_BaseMap", new Vector2(2.0f, 3.0f));
                material.SetTextureOffset("_BaseMap", new Vector2(0.1f, 0.2f));
                material.SetTexture("_BumpMap", bumpMap);
                material.SetFloat("_BumpScale", 0.6f);
                material.SetFloat("_Metallic", 0.4f);
                material.SetFloat("_Smoothness", 0.3f);
                material.SetColor("_EmissionColor", new Color(1.0f, 0.5f, 0.0f, 1.0f));
                material.SetFloat("_AlphaClip", 1.0f);
                material.SetFloat("_Cutoff", 0.33f);
                material.SetFloat("_Cull", (float) CullMode.Off);

                uint initialRevision = materialProxy.Revision;
                GPUDrivenMaterialProxySyncResult result =
                    GPUDrivenMaterialProxySyncUtility.SyncFromSourceMaterial(materialProxy, material);

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

                string[] warnings = GPUDrivenMaterialProxySyncUtility.CollectUnsupportedWarnings(material);
                string warningText = string.Join("\n", warnings);

                Assert.That(warnings, Has.Length.GreaterThanOrEqualTo(7));
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
