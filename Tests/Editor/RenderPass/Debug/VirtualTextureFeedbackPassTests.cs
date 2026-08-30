using System;
using System.IO;
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
    public sealed class VirtualTextureFeedbackPassTests
    {
        [Serializable]
        private sealed class AutoRegisteredVirtualTextureFeedbackPassNode : RenderPassNodeData
        {
            internal override Type GetRegisteredPassType() => typeof(VirtualTextureFeedbackPass);

            internal bool TryGetFeedbackSampleRate(out float value)
            {
                return TryGetFloatParameterValue("m_FeedbackSampleRate", out value);
            }
        }

        [Test]
        public void Initialize_RegistersRenderListGBufferAttachmentsAndDepth()
        {
            IRenderPass renderPass = new VirtualTextureFeedbackPass();

            var resources = renderPass.Initialize();
            var colorEntries = resources.Textures
                .Where(entry => !entry.IsDepthAttachment)
                .OrderBy(entry => entry.AttachmentIndex)
                .ToArray();
            var depthEntry = resources.Textures.Single(entry => entry.IsDepthAttachment);

            Assert.That(resources.RenderLists, Has.Length.EqualTo(1));
            Assert.That(resources.RenderLists[0].Name, Is.EqualTo("RenderList"));
            Assert.That(resources.RenderLists[0].Access, Is.EqualTo(AccessFlags.Read));
            Assert.That(resources.RenderLists[0].RenderList.desc.ShaderTagNames, Is.EqualTo(new[] { VirtualTextureFeedbackPass.VirtualTextureGBufferShaderTagName }));
            Assert.That(resources.RenderLists[0].RenderList.desc.RendererConfiguration, Is.EqualTo(PerObjectData.Lightmaps));
            Assert.That(resources.Textures, Has.Length.EqualTo(6));

            Assert.That(colorEntries, Has.Length.EqualTo(5));
            Assert.That(colorEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "GBuffer0", "GBuffer1", "GBuffer2", "GBuffer3", "GBuffer4" }));
            Assert.That(colorEntries.Select(entry => entry.Access), Is.All.EqualTo(AccessFlags.ReadWrite));
            Assert.That(colorEntries.Select(entry => entry.AttachmentIndex), Is.EqualTo(new[] { 0, 1, 2, 3, 4 }));
            Assert.That(colorEntries.Select(entry => entry.Texture.desc.ClearBuffer), Is.All.EqualTo(false));
            Assert.That(colorEntries[0].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(colorEntries[1].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.A2B10G10R10_UNormPack32));
            Assert.That(colorEntries[2].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R8G8B8A8_UNorm));
            Assert.That(colorEntries[3].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.B10G11R11_UFloatPack32));
            Assert.That(colorEntries[3].Texture.desc.EnableRandomWrite, Is.True);
            Assert.That(colorEntries[4].Texture.desc.ColorFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
            Assert.That(depthEntry.Name, Is.EqualTo("Depth"));
            Assert.That(depthEntry.Access, Is.EqualTo(AccessFlags.ReadWrite));
            Assert.That(depthEntry.Texture.desc.DepthBufferBits, Is.EqualTo(DepthBits.Depth32));
            Assert.That(depthEntry.Texture.desc.ClearBuffer, Is.False);
        }

        [Test]
        public void Prepare_ResizesPassOwnedTargets_WhenCameraSizeChanges()
        {
            var pass = new VirtualTextureFeedbackPass();
            var frameData = CreateFrameData(1280, 720);

            pass.Prepare(frameData);

            AssertTextureSize(pass, "m_GBuffer0", 1280, 720);
            AssertTextureSize(pass, "m_GBuffer1", 1280, 720);
            AssertTextureSize(pass, "m_GBuffer2", 1280, 720);
            AssertTextureSize(pass, "m_GBuffer3", 1280, 720);
            AssertTextureSize(pass, "m_GBuffer4", 1280, 720);
            AssertTextureSize(pass, "m_GBufferDepth", 1280, 720);
        }

        [Test]
        public void Prepare_SelectsGPUDrivenDecalShaderTag_WhenDecalFrameDataIsEnabled()
        {
            var pass = new VirtualTextureFeedbackPass();
            var frameData = CreateFrameData(960, 540);
            frameData.GetOrCreate<VividGPUDrivenDecalData>().isEnabled = true;

            pass.Prepare(frameData);

            var renderList = GetFieldValue<RenderGraphRenderList>(pass, "m_RenderList");
            Assert.That(
                renderList.desc.ShaderTagNames,
                Is.EqualTo(new[] { VirtualTextureFeedbackPass.VirtualTextureGPUDrivenDecalGBufferShaderTagName }));
        }

        [Test]
        public void Prepare_KeepsDefaultShaderTag_WhenGPUDrivenDecalFrameDataIsDisabled()
        {
            var pass = new VirtualTextureFeedbackPass();
            var frameData = CreateFrameData(960, 540);
            frameData.GetOrCreate<VividGPUDrivenDecalData>().isEnabled = false;

            pass.Prepare(frameData);

            var renderList = GetFieldValue<RenderGraphRenderList>(pass, "m_RenderList");
            Assert.That(renderList.desc.ShaderTagNames, Is.EqualTo(new[] { VirtualTextureFeedbackPass.VirtualTextureGBufferShaderTagName }));
        }

        [Test]
        public void BuildFeedbackViewParams_UsesTileMaskShiftAndFrameJitter()
        {
            Assert.That(
                VirtualTextureFeedbackPass.BuildFeedbackViewParamsForTesting(1, 7),
                Is.EqualTo(new Vector4(0f, 0f, 0f, 1f)));
            Assert.That(
                VirtualTextureFeedbackPass.BuildFeedbackViewParamsForTesting(4, 5),
                Is.EqualTo(new Vector4(1f, 1f, 1f, 1f)));
            Assert.That(
                VirtualTextureFeedbackPass.BuildFeedbackViewParamsForTesting(5, 18),
                Is.EqualTo(new Vector4(3f, 2f, 2f, 1f)));
        }

        [Test]
        public void VirtualTextureFeedbackPassNode_DefinesReadWritePortsAndSampleRateOption()
        {
            var graph = RenderGraphTestUtility.CreateGraph();

            try
            {
                var node = new AutoRegisteredVirtualTextureFeedbackPassNode();
                RenderGraphTestUtility.AddTestNode(graph, node);

                Assert.That(node.GetInputPortByName("m_RenderList"), Is.Not.Null);
                for (int index = 0; index < 5; index++)
                {
                    Assert.That(node.GetInputPortByName($"m_GBuffer{index}_In"), Is.Not.Null);
                    Assert.That(node.GetOutputPortByName($"m_GBuffer{index}_Out"), Is.Not.Null);
                }

                Assert.That(node.GetInputPortByName("m_GBufferDepth_In"), Is.Not.Null);
                Assert.That(node.GetOutputPortByName("m_GBufferDepth_Out"), Is.Not.Null);
                Assert.That(node.TryGetFeedbackSampleRate(out float sampleRate), Is.True);
                Assert.That(sampleRate, Is.EqualTo(4f));
            }
            finally
            {
                RenderGraphTestUtility.DeleteGraph(graph);
            }
        }

        [Test]
        public void GeneratedNodeRegistry_IncludesVirtualTextureFeedbackPass()
        {
            var nodeType = RenderPassNodeRegistry.GetNodeType(typeof(VirtualTextureFeedbackPass));

            Assert.That(nodeType, Is.Not.Null);
            Assert.That(nodeType.Name, Is.EqualTo(nameof(VirtualTextureFeedbackPass)));
            Assert.That(RenderPassNodeRegistry.GetPassType(nodeType), Is.EqualTo(typeof(VirtualTextureFeedbackPass)));
        }

        [Test]
        public void FeedbackShaderPasses_DoNotOverwriteSurfaceSummaryAttachmentsOrDepth()
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(VirtualTextureFeedbackPass).Assembly);
            Assert.That(package, Is.Not.Null);
            var shaderPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Material",
                "StandardLayeredLit",
                "StandardLayeredLit.shader");
            var source = File.ReadAllText(shaderPath);

            foreach (var passName in new[] { "VividVTGBuffer", "VividVTGBufferGPUDrivenDecal" })
            {
                var passStart = source.IndexOf($"Name \"{passName}\"", StringComparison.Ordinal);
                Assert.That(passStart, Is.GreaterThanOrEqualTo(0), passName);
                var nextPass = source.IndexOf("        Pass", passStart + passName.Length, StringComparison.Ordinal);
                var passSource = nextPass >= 0
                    ? source.Substring(passStart, nextPass - passStart)
                    : source.Substring(passStart);

                StringAssert.Contains("ZWrite Off", passSource, passName);
                for (var attachmentIndex = 0; attachmentIndex < 5; attachmentIndex++)
                    StringAssert.Contains($"ColorMask 0 {attachmentIndex}", passSource, passName);
            }
        }

        private static ContextContainer CreateFrameData(int width, int height)
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = width;
            cameraData.actualHeight = height;
            return frameData;
        }

        private static void AssertTextureSize(VirtualTextureFeedbackPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var texture = GetFieldValue<RenderGraphTexture>(pass, fieldName);

            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static T GetFieldValue<T>(VirtualTextureFeedbackPass pass, string fieldName)
        {
            var field = typeof(VirtualTextureFeedbackPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(pass);
        }
    }
}
