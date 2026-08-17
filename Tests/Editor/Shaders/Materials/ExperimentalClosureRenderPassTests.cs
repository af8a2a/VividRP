using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Experimental.Material;

namespace VividRP.Editor.Tests
{
    public sealed class ExperimentalClosureRenderPassTests
    {
        [Test]
        public void ClosureBufferPass_RegistersEightAttachmentsAndSharedDepth()
        {
            IRenderPass pass = new ExperimentalClosureBufferPass();
            var resources = pass.Initialize();
            var colorEntries = resources.Textures
                .Where(entry => !entry.IsDepthAttachment)
                .OrderBy(entry => entry.AttachmentIndex)
                .ToArray();

            Assert.That(resources.RenderLists, Has.Length.EqualTo(1));
            Assert.That(
                resources.RenderLists[0].RenderList.desc.ShaderTagNames,
                Is.EqualTo(new[] { "ExperimentalClosureBuffer" }));
            Assert.That(colorEntries, Has.Length.EqualTo(8));
            Assert.That(
                colorEntries.Select(entry => entry.Name),
                Is.EqualTo(new[]
                {
                    "ExperimentalClosureBuffer0",
                    "ExperimentalClosureBuffer1",
                    "ExperimentalClosureBuffer2",
                    "ExperimentalClosureBuffer3",
                    "ExperimentalClosureBuffer4",
                    "ExperimentalClosureBuffer5",
                    "ExperimentalClosureBuffer6",
                    "ExperimentalClosureBuffer7"
                }));
            Assert.That(
                resources.Textures.Single(entry => entry.IsDepthAttachment).Name,
                Is.EqualTo("Depth"));
        }

        [Test]
        public void ClosureClassificationPass_PreparesThreeComplexityQueues()
        {
            var pass = new ExperimentalClosureClassificationPass();
            try
            {
                var frameData = CreateFrameData(320, 180);
                pass.Prepare(frameData);

                const int expectedTileCount = 40 * 23;
                AssertBuffer(pass, "m_TileClasses", expectedTileCount);
                AssertBuffer(pass, "m_TileList", expectedTileCount * 3);
                AssertBuffer(pass, "m_IndirectArgs", 3 * 4);
                AssertTexture(pass, "m_ClosureBuffer0", 320, 180);
                AssertTexture(pass, "m_DepthTexture", 320, 180);
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void ClosureDeferredLightingPass_RegistersClosureInputsAndOutputs()
        {
            IRenderPass renderPass =
                new ExperimentalClosureDeferredLightingPass();
            var resources = renderPass.Initialize();

            Assert.That(
                resources.Textures.Select(entry => entry.Name),
                Is.SupersetOf(new[]
                {
                    "ExperimentalClosureBuffer0",
                    "ExperimentalClosureBuffer1",
                    "ExperimentalClosureBuffer2",
                    "ExperimentalClosureBuffer3",
                    "ExperimentalClosureBuffer4",
                    "ExperimentalClosureBuffer5",
                    "ExperimentalClosureBuffer6",
                    "ExperimentalClosureBuffer7",
                    "Depth",
                    "DirectionalShadowTexture",
                    "GTAOTexture",
                    "ScreenSpaceReflectionOutput",
                    "SkyIBLCubemap",
                    "ExperimentalClosureLighting",
                    "ExperimentalClosureDebug"
                }));
            Assert.That(
                resources.Buffers.Select(entry => entry.Name),
                Is.SupersetOf(new[]
                {
                    "ExperimentalClosureTileList",
                    "ExperimentalClosureIndirectArgs",
                    "DirectionalLights",
                    "PunctualLights",
                    "AreaLights",
                    "ReflectionProbes",
                    "LayeredOffset",
                    "LayeredLightList",
                    "LogBaseBuffer"
                }));
        }

        [Test]
        public void ClosureDeferredLightingPass_ResizesOutputsWithCamera()
        {
            var pass = new ExperimentalClosureDeferredLightingPass();
            var frameData = CreateFrameData(640, 360);

            pass.Prepare(frameData);

            AssertTexture(pass, "m_LightingTexture", 640, 360);
            AssertTexture(pass, "m_DebugTexture", 640, 360);
        }

        private static ContextContainer CreateFrameData(int width, int height)
        {
            var frameData = new ContextContainer();
            var cameraData = frameData.GetOrCreate<VividCameraData>();
            cameraData.actualWidth = width;
            cameraData.actualHeight = height;
            return frameData;
        }

        private static void AssertTexture(
            object pass,
            string fieldName,
            int expectedWidth,
            int expectedHeight)
        {
            var field = pass.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static void AssertBuffer(
            object pass,
            string fieldName,
            int expectedCount)
        {
            var field = pass.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            var buffer = (RenderGraphBuffer)field.GetValue(pass);
            Assert.That(buffer.desc.Count, Is.EqualTo(expectedCount));
            Assert.That(buffer.desc.Stride, Is.EqualTo(sizeof(uint)));
            var importedBufferProperty = typeof(RenderGraphBuffer).GetProperty(
                "ImportedGraphicsBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(importedBufferProperty, Is.Not.Null);
            Assert.That(
                importedBufferProperty.GetValue(buffer),
                Is.Not.Null);
        }
    }
}
