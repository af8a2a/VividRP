using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class GBufferPassTests
    {
        [Test]
        public void Initialize_RegistersFiveColorAttachmentsAndDepth_WhenPassIsCreated()
        {
            IRenderPass renderPass = new GBufferPass();

            var resources = renderPass.Initialize();
            var colorEntries = resources.Textures
                .Where(entry => !entry.IsDepthAttachment)
                .OrderBy(entry => entry.AttachmentIndex)
                .ToArray();
            var depthEntry = resources.Textures.Single(entry => entry.IsDepthAttachment);

            Assert.That(resources.RenderLists, Has.Length.EqualTo(1));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Has.Member("DecalData"));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Has.Member("LayeredOffset"));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Has.Member("LayeredLightList"));
            Assert.That(resources.Buffers.Select(entry => entry.Name), Has.Member("LogBaseBuffer"));
            Assert.That(resources.Textures, Has.Length.EqualTo(6));
            Assert.That(resources.RenderLists[0].RenderList.desc.ShaderTagNames, Is.EqualTo(new[] { "VividGBuffer" }));
            Assert.That(resources.RenderLists[0].RenderList.desc.RendererConfiguration, Is.EqualTo(PerObjectData.Lightmaps));

            Assert.That(colorEntries, Has.Length.EqualTo(5));
            Assert.That(colorEntries.Select(entry => entry.AttachmentIndex), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(colorEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "GBuffer0", "GBuffer1", "GBuffer2", "GBuffer3", "GBuffer4" }));

            Assert.That(colorEntries[0].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(colorEntries[1].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.A2B10G10R10_UNormPack32));
            Assert.That(colorEntries[2].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(colorEntries[3].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(colorEntries[4].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));
        }

        [Test]
        public void Prepare_ResizesAllGBufferTargets_WhenCameraSizeChanges()
        {
            var pass = new GBufferPass();
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_GBuffer0", 960, 540);
            AssertTextureSize(pass, "m_GBuffer1", 960, 540);
            AssertTextureSize(pass, "m_GBuffer2", 960, 540);
            AssertTextureSize(pass, "m_GBuffer3", 960, 540);
            AssertTextureSize(pass, "m_GBuffer4", 960, 540);
            AssertTextureSize(pass, "m_GBufferDepth", 960, 540);
        }

        [Test]
        public void Prepare_SelectsGPUDrivenDecalShaderTag_WhenDecalFrameDataIsEnabled()
        {
            var pass = new GBufferPass();
            var frameData = CreateFrameData();
            frameData.GetOrCreate<VividGPUDrivenDecalData>().isEnabled = true;

            pass.Prepare(frameData);

            var renderList = GetFieldValue<RenderGraphRenderList>(pass, "m_RenderList");
            Assert.That(renderList.desc.ShaderTagNames, Is.EqualTo(new[] { GBufferPass.GPUDrivenDecalGBufferShaderTagName }));
        }

        [Test]
        public void Prepare_KeepsDefaultShaderTag_WhenGPUDrivenDecalFrameDataIsDisabled()
        {
            var pass = new GBufferPass();
            var frameData = CreateFrameData();
            frameData.GetOrCreate<VividGPUDrivenDecalData>().isEnabled = false;

            pass.Prepare(frameData);

            var renderList = GetFieldValue<RenderGraphRenderList>(pass, "m_RenderList");
            Assert.That(renderList.desc.ShaderTagNames, Is.EqualTo(new[] { GBufferPass.GBufferShaderTagName }));
        }

        [Test]
        public void Prepare_ReusesShaderTagNameArrays_WhenDecalModeIsStable()
        {
            var pass = new GBufferPass();
            var frameData = CreateFrameData();
            frameData.GetOrCreate<VividGPUDrivenDecalData>().isEnabled = true;

            pass.Prepare(frameData);

            var renderList = GetFieldValue<RenderGraphRenderList>(pass, "m_RenderList");
            var renderListShaderTagNames = renderList.desc.ShaderTagNames;

            pass.Prepare(frameData);

            Assert.That(renderList.desc.ShaderTagNames, Is.SameAs(renderListShaderTagNames));
        }

        [Test]
        public void GBufferPass_DoesNotOwnVirtualTextureStateOrRenderLists()
        {
            var resources = ((IRenderPass)new GBufferPass()).Initialize();

            Assert.That(resources.RenderLists.Select(entry => entry.Name), Is.EqualTo(new[] { "RenderList" }));
            Assert.That(
                typeof(GBufferPass).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Select(field => field.Name),
                Does.Not.Contain("m_VirtualTextureRenderList"));
            Assert.That(
                typeof(GBufferPass).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Select(field => field.Name),
                Does.Not.Contain("m_VirtualTextureFeedbackSampleRate"));
        }

        [Test]
        public void Prepare_EnablesClusteredDecalGrid_WhenLightGridResourcesAreBound()
        {
            var pass = new GBufferPass();
            var frameData = CreateFrameData();
            frameData.GetOrCreate<VividGPUDrivenDecalData>().isEnabled = true;

            var clusteredLightingData = frameData.GetOrCreate<VividClusteredLightingData>();
            clusteredLightingData.decalCount = 2;
            clusteredLightingData.supportsClusteredPunctualLights = true;
            clusteredLightingData.isLogBaseBufferEnabled = true;
            clusteredLightingData.clusterTileSize = LightGridPass.ClusterTileSize;
            clusteredLightingData.clusterSliceCount = LightGridPass.ClusterSliceCount;
            clusteredLightingData.clusterTileCountX = 3;
            clusteredLightingData.clusterTileCountY = 2;
            clusteredLightingData.clusterNearClip = 0.25f;
            clusteredLightingData.clusterFarClip = 250.0f;
            clusteredLightingData.clusterScale = 2.0f;
            clusteredLightingData.clusterBase = LightGridPass.ClusterLogBase;
            clusteredLightingData.clusterLog2SliceCount = LightGridPass.ClusterLog2SliceCount;

            var decalDataBuffer = CreateImportedStructuredBuffer("DecalData", VividLightData.DecalClusterData.Stride);
            var layeredOffsetBuffer = CreateImportedStructuredBuffer("LayeredOffset", sizeof(uint));
            var layeredLightListBuffer = CreateImportedStructuredBuffer("LayeredLightList", sizeof(uint));
            var logBaseBuffer = CreateImportedStructuredBuffer("LogBaseBuffer", sizeof(float));

            try
            {
                SetFieldValue(pass, "m_DecalDataBuffer", decalDataBuffer);
                SetFieldValue(pass, "m_LayeredOffsetBuffer", layeredOffsetBuffer);
                SetFieldValue(pass, "m_LayeredLightListBuffer", layeredLightListBuffer);
                SetFieldValue(pass, "m_LogBaseBuffer", logBaseBuffer);

                pass.Prepare(frameData);

                Assert.That(GetFieldValue<bool>(pass, "m_SupportsClusteredDecals"), Is.True);
                Assert.That(GetFieldValue<bool>(pass, "m_IsLogBaseBufferEnabled"), Is.True);
                Assert.That(GetFieldValue<int>(pass, "m_ClusterTileCountX"), Is.EqualTo(3));
                Assert.That(GetFieldValue<int>(pass, "m_ClusterTileCountY"), Is.EqualTo(2));
            }
            finally
            {
                decalDataBuffer.ClearImportedBuffer();
                layeredOffsetBuffer.ClearImportedBuffer();
                layeredLightListBuffer.ClearImportedBuffer();
                logBaseBuffer.ClearImportedBuffer();
                pass.Dispose();
            }
        }

        private static void AssertTextureSize(GBufferPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var field = typeof(GBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);

            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static ContextContainer CreateFrameData()
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = 960;
            cameraData.actualHeight = 540;
            return frameData;
        }

        private static T GetFieldValue<T>(GBufferPass pass, string fieldName)
        {
            var field = typeof(GBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(pass);
        }

        private static void SetFieldValue<T>(GBufferPass pass, string fieldName, T value)
        {
            var field = typeof(GBufferPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(pass, value);
        }

        private static RenderGraphBuffer CreateImportedStructuredBuffer(string name, int stride)
        {
            var buffer = RenderGraphBuffer.CreateStructured(name, stride);
            buffer.EnsureImportedBuffer();
            return buffer;
        }
    }
}
