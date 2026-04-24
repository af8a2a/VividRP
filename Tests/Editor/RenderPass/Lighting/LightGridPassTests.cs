using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public class LightGridPassTests
    {
        [Test]
        public void LightGridPass_DoesNotDeclareRawImportedGraphicsBufferFields()
        {
            var rawImportedFields = typeof(LightGridPass)
                .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(GraphicsBuffer))
                .Where(field => field.Name.EndsWith("ImportedBuffer", StringComparison.Ordinal))
                .Select(field => field.Name)
                .ToArray();

            Assert.That(rawImportedFields, Is.Empty);
        }

        [Test]
        public void LightGridPass_HidesInternalBuildBuffers_FromAuthoringPorts()
        {
            AssertBindingMode("m_FiniteLightBoundBuffer", RenderGraphResourceBindingMode.PassOwnedHidden);
            AssertBindingMode("m_LightVolumeDataBuffer", RenderGraphResourceBindingMode.PassOwnedHidden);
            AssertBindingMode("m_ScreenSpaceBoundsBuffer", RenderGraphResourceBindingMode.PassOwnedHidden);
            AssertBindingMode("m_BigTileLightListBuffer", RenderGraphResourceBindingMode.PassOwnedHidden);
            AssertBindingMode("m_LayeredLightListCounterBuffer", RenderGraphResourceBindingMode.PassOwnedHidden);
        }

        [Test]
        public void LightGridPass_KeepsLightingConsumerBuffers_VisibleForAuthoringPorts()
        {
            AssertBindingMode("m_DirectionalLightBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_PunctualLightBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_AreaLightBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_DecalDataBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_LayeredOffsetBuffer", RenderGraphResourceBindingMode.PassOwnedOverrideable);
            AssertBindingMode("m_LayeredLightListBuffer", RenderGraphResourceBindingMode.PassOwnedOverrideable);
            AssertBindingMode("m_LogBaseBuffer", RenderGraphResourceBindingMode.PassOwnedOverrideable);
        }

        [Test]
        public void Prepare_EnsuresImportedBackingBuffers_ForRenderGraphBufferResources()
        {
            var pass = new LightGridPass();

            try
            {
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 128;
                cameraData.actualHeight = 64;

                pass.Prepare(frameData);

                var bufferFields = typeof(LightGridPass)
                    .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Where(field => field.FieldType == typeof(RenderGraphBuffer))
                    .Where(field => field.GetCustomAttribute<RenderGraphResource>() != null)
                    .ToArray();

                Assert.That(bufferFields, Is.Not.Empty);

                foreach (var field in bufferFields)
                {
                    var buffer = (RenderGraphBuffer)field.GetValue(pass);
                    Assert.That(buffer, Is.Not.Null, field.Name);
                    AssertImportedBackingBuffer(buffer, field.Name);
                }
            }
            finally
            {
                pass.Dispose();
            }
        }

        private static void AssertImportedBackingBuffer(RenderGraphBuffer buffer, string fieldName)
        {
            var importedGraphicsBufferProperty = typeof(RenderGraphBuffer).GetProperty(
                "ImportedGraphicsBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(importedGraphicsBufferProperty, Is.Not.Null);

            var importedGraphicsBuffer = (GraphicsBuffer)importedGraphicsBufferProperty.GetValue(buffer);
            Assert.That(importedGraphicsBuffer, Is.Not.Null, fieldName);
            Assert.That(importedGraphicsBuffer.count, Is.GreaterThanOrEqualTo(buffer.desc.Count), fieldName);
            Assert.That(importedGraphicsBuffer.stride, Is.EqualTo(buffer.desc.Stride), fieldName);
        }

        private static void AssertBindingMode(string fieldName, RenderGraphResourceBindingMode expectedBindingMode)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);

            var attr = field.GetCustomAttribute<RenderGraphResource>();
            Assert.That(attr, Is.Not.Null, fieldName);
            Assert.That(attr.BindingMode, Is.EqualTo(expectedBindingMode), fieldName);
        }
    }
}
