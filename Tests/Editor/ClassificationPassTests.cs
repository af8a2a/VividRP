using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using VividRP.Runtime;
using VividRP.Runtime.RenderPass.Core;

namespace VividRP.Editor.Tests
{
    public class ClassificationPassTests
    {
        [Test]
        public void Initialize_RegistersGBufferInputsAndClassificationBuffers_WhenPassIsCreated()
        {
            IRenderPass renderPass = new ClassificationPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();
            var bufferEntries = resources.Buffers.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Depth", "GBuffer0" }));
            Assert.That(bufferEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "ClearCoatIndirectArgs",
                "ClearCoatMaterialIndices",
                "FabricIndirectArgs",
                "FabricMaterialIndices",
                "MaterialClassCounts",
                "StandardIndirectArgs",
                "StandardMaterialIndices"
            }));
        }

        [Test]
        public void Prepare_ResizesClassificationBuffers_WhenCameraSizeChanges()
        {
            var pass = new ClassificationPass();
            try
            {
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;

                pass.Prepare(frameData);

                AssertTextureSize(pass, "m_GBuffer0", 320, 180);
                AssertTextureSize(pass, "m_DepthTexture", 320, 180);

                var expectedTileCountX = (320 + 7) / 8;
                var expectedTileCountY = (180 + 7) / 8;
                var expectedTileCount = expectedTileCountX * expectedTileCountY;
                AssertStructuredBuffer(pass, "m_StandardMaterialIndices", expectedTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_FabricMaterialIndices", expectedTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_ClearCoatMaterialIndices", expectedTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_MaterialClassCounts", 3, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_StandardIndirectArgs", 4, sizeof(uint), GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);
                AssertStructuredBuffer(pass, "m_FabricIndirectArgs", 4, sizeof(uint), GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);
                AssertStructuredBuffer(pass, "m_ClearCoatIndirectArgs", 4, sizeof(uint), GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);

                AssertImportedBuffer(pass, "m_StandardMaterialIndices", expectedTileCount, sizeof(uint));
                AssertImportedBuffer(pass, "m_FabricMaterialIndices", expectedTileCount, sizeof(uint));
                AssertImportedBuffer(pass, "m_ClearCoatMaterialIndices", expectedTileCount, sizeof(uint));
                AssertImportedBuffer(pass, "m_MaterialClassCounts", 3, sizeof(uint));
                AssertImportedBuffer(pass, "m_StandardIndirectArgs", 4, sizeof(uint));
                AssertImportedBuffer(pass, "m_FabricIndirectArgs", 4, sizeof(uint));
                AssertImportedBuffer(pass, "m_ClearCoatIndirectArgs", 4, sizeof(uint));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsTrue_ForClassificationPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(ClassificationPass)), Is.True);
        }

        private static void AssertTextureSize(ClassificationPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var field = typeof(ClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static void AssertStructuredBuffer(ClassificationPass pass, string fieldName, int expectedCount, int expectedStride, GraphicsBuffer.Target expectedTarget)
        {
            var field = typeof(ClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var buffer = (RenderGraphBuffer)field.GetValue(pass);
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.desc.Count, Is.EqualTo(expectedCount));
            Assert.That(buffer.desc.Stride, Is.EqualTo(expectedStride));
            Assert.That(buffer.desc.Target, Is.EqualTo(expectedTarget));
        }

        private static void AssertImportedBuffer(ClassificationPass pass, string fieldName, int expectedCount, int expectedStride)
        {
            var field = typeof(ClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var buffer = (RenderGraphBuffer)field.GetValue(pass);
            Assert.That(buffer, Is.Not.Null);

            var importedGraphicsBufferProperty = typeof(RenderGraphBuffer).GetProperty(
                "ImportedGraphicsBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(importedGraphicsBufferProperty, Is.Not.Null);

            var importedGraphicsBuffer = (GraphicsBuffer)importedGraphicsBufferProperty.GetValue(buffer);
            Assert.That(importedGraphicsBuffer, Is.Not.Null);
            Assert.That(importedGraphicsBuffer.count, Is.GreaterThanOrEqualTo(expectedCount));
            Assert.That(importedGraphicsBuffer.stride, Is.EqualTo(expectedStride));
        }
    }
}
