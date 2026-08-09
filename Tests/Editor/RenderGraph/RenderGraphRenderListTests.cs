using System.Linq;
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
        private sealed class RenderListParameterPass : RasterPass
        {
            public RenderGraphRenderListDesc PublicDescriptor = RenderGraphRenderListDesc.CreateOpaque("Public");

            [SerializeField]
            private RenderGraphRenderListDesc m_SerializedDescriptor =
                RenderGraphRenderListDesc.CreateTransparent("Serialized");

            private RenderGraphRenderListDesc m_PrivateDescriptor =
                RenderGraphRenderListDesc.CreateOpaque("Private");

            public override void Create() { }
            public override void Prepare(ContextContainer frameData) { }
            public override void Record(RasterPassContext context) { }
            public override void Dispose() { }
        }

        [Test]
        public void RenderListDescParameterReflection_IncludesPublicAndSerializedFieldsOnly()
        {
            var fieldNames = RenderGraphPassRenderListDescParameterUtility
                .EnumerateSerializableFields(typeof(RenderListParameterPass))
                .Select(field => field.Name)
                .ToArray();

            Assert.That(fieldNames, Is.EquivalentTo(new[]
            {
                nameof(RenderListParameterPass.PublicDescriptor),
                "m_SerializedDescriptor",
            }));
            Assert.That(fieldNames, Does.Not.Contain("m_PrivateDescriptor"));
        }

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
        public void CreateOpaque_IncludesObjectMotionVectorRenderers_ByDefault()
        {
            var descriptor = RenderGraphRenderListDesc.CreateOpaque();

            Assert.That(descriptor.ExcludeObjectMotionVectors, Is.False);
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
