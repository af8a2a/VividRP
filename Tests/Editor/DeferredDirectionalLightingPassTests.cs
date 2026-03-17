using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Editor.RenderGraph;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class DeferredDirectionalLightingPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredDeferredDirectionalLightingPassNode : RenderPassNodeData
        {
            protected override string RegisteredPassTypeName => typeof(DeferredDirectionalLightingPass).AssemblyQualifiedName;
        }

        [Test]
        public void Initialize_RegistersGBufferInputsClassificationBuffersAndColorOutput_WhenPassIsCreated()
        {
            IRenderPass renderPass = new DeferredDirectionalLightingPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();
            var bufferEntries = resources.Buffers.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "Color",
                "Depth",
                "GBuffer0",
                "GBuffer1",
                "GBuffer2",
                "GBuffer3"
            }));
            Assert.That(textureEntries.Single(entry => entry.Name == "Color").Access, Is.EqualTo(AccessFlags.Write));
            Assert.That(textureEntries.Single(entry => entry.Name == "Color").AttachmentIndex, Is.EqualTo(0));
            Assert.That(textureEntries.Where(entry => entry.Name != "Color").Select(entry => entry.Access).Distinct(), Is.EqualTo(new[] { AccessFlags.Read }));

            Assert.That(bufferEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "ClearCoatIndirectArgs",
                "ClearCoatMaterialIndices",
                "FabricIndirectArgs",
                "FabricMaterialIndices",
                "StandardIndirectArgs",
                "StandardMaterialIndices"
            }));
            Assert.That(bufferEntries.Select(entry => entry.Access).Distinct(), Is.EqualTo(new[] { AccessFlags.Read }));
        }

        [Test]
        public void Prepare_ResizesInputAndOutputTextures_WhenCameraSizeChanges()
        {
            var pass = new DeferredDirectionalLightingPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 511;
            cameraData.actualHeight = 257;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_GBuffer0", 511, 257);
            AssertTextureSize(pass, "m_GBuffer1", 511, 257);
            AssertTextureSize(pass, "m_GBuffer2", 511, 257);
            AssertTextureSize(pass, "m_GBuffer3", 511, 257);
            AssertTextureSize(pass, "m_DepthTexture", 511, 257);
            AssertTextureSize(pass, "m_ColorTexture", 511, 257);

            Assert.That(GetFieldValue<int>(pass, "m_LightingWidth"), Is.EqualTo(511));
            Assert.That(GetFieldValue<int>(pass, "m_LightingHeight"), Is.EqualTo(257));
            Assert.That(GetFieldValue<int>(pass, "m_ClearDispatchGroupCountX"), Is.EqualTo(64));
            Assert.That(GetFieldValue<int>(pass, "m_ClearDispatchGroupCountY"), Is.EqualTo(33));
            Assert.That(GetFieldValue<int>(pass, "m_MaterialDispatchGroupCountX"), Is.EqualTo(2112));

            var outputTexture = GetFieldValue<RenderGraphTexture>(pass, "m_ColorTexture");
            Assert.That(outputTexture.desc.EnableRandomWrite, Is.True);
            Assert.That(outputTexture.desc.ClearBuffer, Is.True);
            Assert.That(outputTexture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
        }

        [Test]
        public void DeferredDirectionalLightingPass_InheritsFromUnsafePass()
        {
            Assert.That(typeof(UnsafePass).IsAssignableFrom(typeof(DeferredDirectionalLightingPass)), Is.True);
        }

        [Test]
        public void BuildSkyIblParams_UsesHdrpCompatibleSkyLayout_WhenSkyIsAvailable()
        {
            var cubemap = new Cubemap(16, TextureFormat.RGBA32, true);

            try
            {
                var skyParams = DeferredDirectionalLightingPass.BuildSkyIblParams(cubemap, 1.5f, 30f);

                Assert.That(skyParams.x, Is.EqualTo(1.5f));
                Assert.That(skyParams.y, Is.EqualTo(-30f));
                Assert.That(skyParams.z, Is.EqualTo(cubemap.mipmapCount - 1));
                Assert.That(skyParams.w, Is.EqualTo(1f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cubemap);
            }
        }

        [Test]
        public void DeferredDirectionalLightingPassNode_DoesNotExposeAsyncComputeOption()
        {
            Assert.That(typeof(IAsyncComputeSupportedPass).IsAssignableFrom(typeof(DeferredDirectionalLightingPass)), Is.False);
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsFalse_ForDeferredDirectionalLightingPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(DeferredDirectionalLightingPass)), Is.False);
        }

        private static void AssertTextureSize(DeferredDirectionalLightingPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static T GetFieldValue<T>(DeferredDirectionalLightingPass pass, string fieldName)
        {
            var field = typeof(DeferredDirectionalLightingPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            return (T)field.GetValue(pass);
        }
    }
}
