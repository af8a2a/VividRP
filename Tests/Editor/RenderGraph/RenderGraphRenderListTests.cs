using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class RenderGraphRenderListTests
    {
        [Test]
        public void Clone_CopiesShaderTagNames_WhenDescriptorIsCloned()
        {
            var descriptor = new RenderGraphRenderListDesc
            {
                ShaderTagNames = new[] { "SRPDefaultUnlit", "Forward" },
                RenderQueueRange = RenderGraphRenderQueueRange.Transparent,
            };

            var clone = descriptor.Clone();
            clone.ShaderTagNames[0] = "Changed";

            Assert.That(clone, Is.Not.SameAs(descriptor));
            Assert.That(clone.ShaderTagNames, Is.Not.SameAs(descriptor.ShaderTagNames));
            Assert.That(descriptor.ShaderTagNames[0], Is.EqualTo("SRPDefaultUnlit"));
            Assert.That(clone.RenderQueueRange, Is.EqualTo(RenderGraphRenderQueueRange.Transparent));
        }

        [Test]
        public void CreateRendererListDesc_AppliesSerializedFilteringSettings()
        {
            var gameObject = new GameObject("RenderListCamera");
            try
            {
                var camera = gameObject.AddComponent<Camera>();
                var descriptor = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = new[] { "SRPDefaultUnlit" },
                    RenderQueueRange = RenderGraphRenderQueueRange.Transparent,
                    SortingCriteria = SortingCriteria.CommonTransparent,
                    LayerMask = 9,
                    RenderingLayerMask = 17,
                    RendererConfiguration = PerObjectData.Lightmaps,
                    ExcludeObjectMotionVectors = true,
                    OverrideMaterialPassIndex = 2,
                    OverrideShaderPassIndex = 1,
                };

                var rendererListDesc = descriptor.CreateRendererListDesc(default, camera);

                Assert.That(rendererListDesc.renderQueueRange, Is.EqualTo(RenderQueueRange.transparent));
                Assert.That(rendererListDesc.sortingCriteria, Is.EqualTo(SortingCriteria.CommonTransparent));
                Assert.That(rendererListDesc.layerMask, Is.EqualTo(9));
                Assert.That(rendererListDesc.renderingLayerMask, Is.EqualTo(17u));
                Assert.That(rendererListDesc.rendererConfiguration, Is.EqualTo(PerObjectData.Lightmaps));
                Assert.That(rendererListDesc.excludeObjectMotionVectors, Is.True);
                Assert.That(rendererListDesc.overrideMaterialPassIndex, Is.EqualTo(2));
                Assert.That(rendererListDesc.overrideShaderPassIndex, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void CreateOpaque_UsesForwardThenUnlitShaderTags_WhenNoTagsProvided()
        {
            var descriptor = RenderGraphRenderListDesc.CreateOpaque();

            Assert.That(descriptor.ShaderTagNames, Is.EqualTo(new[]
            {
                RenderGraphRenderListDesc.ForwardShaderTagName,
                RenderGraphRenderListDesc.DefaultUnlitShaderTagName,
            }));
        }

        [Test]
        public void Constructor_DoesNotCreateShaderTagIds_WhenRenderListIsCreated()
        {
            var renderList = new RenderGraphRenderList();

            Assert.That(GetCachedShaderTagIds(renderList.desc), Is.Null);
        }

        [Test]
        public void CreateRendererListDesc_ReusesShaderTagCache_WhenMultipleTagsAreUnchanged()
        {
            var gameObject = new GameObject("RenderListCacheCamera");
            try
            {
                var camera = gameObject.AddComponent<Camera>();
                var descriptor = new RenderGraphRenderListDesc
                {
                    ShaderTagNames = new[]
                    {
                        RenderGraphRenderListDesc.ForwardShaderTagName,
                        RenderGraphRenderListDesc.DefaultUnlitShaderTagName,
                    }
                };

                descriptor.CreateRendererListDesc(default, camera);
                var cachedShaderTags = GetCachedShaderTagIds(descriptor);

                descriptor.CreateRendererListDesc(default, camera);
                Assert.That(GetCachedShaderTagIds(descriptor), Is.SameAs(cachedShaderTags));

                descriptor.ShaderTagNames[1] = "Changed";
                descriptor.CreateRendererListDesc(default, camera);
                Assert.That(GetCachedShaderTagIds(descriptor), Is.Not.SameAs(cachedShaderTags));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SimpleLitShader_DeclaresPreDepthPass_ForDrawObjectPass()
        {
            var shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "SimpleLit.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");

            var shaderSource = File.ReadAllText(shaderPath);

            Assert.That(shaderSource, Does.Contain($"Name \"{RenderGraphRenderListDesc.PreDepthShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain($"\"LightMode\" = \"{RenderGraphRenderListDesc.PreDepthShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain("#pragma fragment FragPreDepth"));
        }

        [Test]
        public void StandardLitShader_DeclaresMotionVectorPass_ForMotionVectorPass()
        {
            var shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "StandardLit.shader"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected shader source at '{shaderPath}'.");

            var shaderSource = File.ReadAllText(shaderPath);

            Assert.That(shaderSource, Does.Contain($"Name \"{MotionVectorPass.MotionVectorsShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain($"\"LightMode\" = \"{MotionVectorPass.MotionVectorsShaderTagName}\""));
            Assert.That(shaderSource, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Material/ShaderPass/StandardLitMotionVectorPass.hlsl\""));
        }

        [Test]
        public void StandardLitMotionVectorPass_UsesSameUnityPerMaterialLayout_AsGBufferPass()
        {
            var gBufferPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "ShaderPass",
                "StandardLitGBufferPass.hlsl"));
            var motionVectorPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "ShaderPass",
                "StandardLitMotionVectorPass.hlsl"));

            Assert.That(File.Exists(gBufferPath), Is.True, $"Expected shader source at '{gBufferPath}'.");
            Assert.That(File.Exists(motionVectorPath), Is.True, $"Expected shader source at '{motionVectorPath}'.");

            var gBufferCBuffer = ExtractUnityPerMaterialBlock(File.ReadAllText(gBufferPath));
            var motionVectorCBuffer = ExtractUnityPerMaterialBlock(File.ReadAllText(motionVectorPath));

            Assert.That(motionVectorCBuffer, Is.EqualTo(gBufferCBuffer));
        }

        private static string ExtractUnityPerMaterialBlock(string source)
        {
            var match = Regex.Match(
                source,
                @"CBUFFER_START\(UnityPerMaterial\)(.*?)CBUFFER_END",
                RegexOptions.Singleline);

            Assert.That(match.Success, Is.True, "Expected UnityPerMaterial cbuffer block.");
            return match.Value;
        }

        private static ShaderTagId[] GetCachedShaderTagIds(RenderGraphRenderListDesc descriptor)
        {
            var field = typeof(RenderGraphRenderListDesc).GetField(
                "m_CachedShaderTagIds",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            return (ShaderTagId[])field.GetValue(descriptor);
        }
    }
}
