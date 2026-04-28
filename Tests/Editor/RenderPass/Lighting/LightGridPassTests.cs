using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
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
        public void LightGridPass_MarksInternalScratchBuffersTransient()
        {
            AssertTransientResource("m_ScreenSpaceBoundsBuffer");
            AssertTransientResource("m_LayeredLightListCounterBuffer");
        }

        [Test]
        public void LightGridPass_KeepsExportedInternalBuffersNonTransient()
        {
            AssertNonTransientResource("m_FiniteLightBoundBuffer");
            AssertNonTransientResource("m_LightVolumeDataBuffer");
            AssertNonTransientResource("m_BigTileLightListBuffer");
            AssertNonTransientResource("m_BigTileVolumetricLightListBuffer");
        }

        [Test]
        public void LightGridPass_KeepsLightingConsumerBuffers_VisibleForAuthoringPorts()
        {
            AssertBindingMode("m_DirectionalLightBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_PunctualLightBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_AreaLightBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_DecalDataBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_BigTileLightListBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_BigTileVolumetricLightListBuffer", RenderGraphResourceBindingMode.External);
            AssertBindingMode("m_LayeredOffsetBuffer", RenderGraphResourceBindingMode.PassOwnedOverrideable);
            AssertBindingMode("m_LayeredLightListBuffer", RenderGraphResourceBindingMode.PassOwnedOverrideable);
            AssertBindingMode("m_LogBaseBuffer", RenderGraphResourceBindingMode.PassOwnedOverrideable);
        }

        [Test]
        public void LightGridPass_UsesNativeUploadBuffers_ForPrepareUploads()
        {
            AssertNativeArrayField("m_DirectionalLightUploadNativeData", typeof(VividLightData.DirectionalLightData));
            AssertNativeArrayField("m_PunctualLightUploadNativeData", typeof(VividLightData.PunctualLightData));
            AssertNativeArrayField("m_AreaLightUploadNativeData", typeof(VividLightData.AreaLightData));
            AssertNativeArrayField("m_DecalDataUploadNativeData", typeof(VividLightData.DecalClusterData));
            AssertNativeArrayField("m_FiniteLightBoundUploadNativeData", typeof(VividLightData.SFiniteLightBound));
            AssertNativeArrayField("m_LightVolumeDataUploadNativeData", typeof(VividLightData.LightVolumeData));
            AssertNativeArrayField("m_LayeredOffsetUploadNativeData", typeof(uint));

            var oldManagedLayeredOffsetField = typeof(LightGridPass).GetField(
                "m_LayeredOffsetUploadData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(oldManagedLayeredOffsetField, Is.Null);
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
                    .Where(field => field.GetCustomAttribute<TransientResourceAttribute>() == null)
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

        [Test]
        public void Prepare_DoesNotCreateImportedBackingBuffers_ForTransientScratchBuffers()
        {
            var pass = new LightGridPass();

            try
            {
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 128;
                cameraData.actualHeight = 64;

                pass.Prepare(frameData);

                AssertNoImportedBackingBuffer(pass, "m_ScreenSpaceBoundsBuffer");
                AssertNoImportedBackingBuffer(pass, "m_LayeredLightListCounterBuffer");
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

        private static void AssertNoImportedBackingBuffer(LightGridPass pass, string fieldName)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);

            var buffer = (RenderGraphBuffer)field.GetValue(pass);
            Assert.That(buffer, Is.Not.Null, fieldName);

            var importedGraphicsBufferProperty = typeof(RenderGraphBuffer).GetProperty(
                "ImportedGraphicsBuffer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(importedGraphicsBufferProperty, Is.Not.Null);
            Assert.That(importedGraphicsBufferProperty.GetValue(buffer), Is.Null, fieldName);
        }

        private static void AssertNativeArrayField(string fieldName, Type elementType)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            Assert.That(field.FieldType.IsGenericType, Is.True, fieldName);
            Assert.That(field.FieldType.GetGenericTypeDefinition(), Is.EqualTo(typeof(NativeArray<>)), fieldName);
            Assert.That(field.FieldType.GetGenericArguments()[0], Is.EqualTo(elementType), fieldName);
        }

        private static void AssertBindingMode(string fieldName, RenderGraphResourceBindingMode expectedBindingMode)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);

            var attr = field.GetCustomAttribute<RenderGraphResource>();
            Assert.That(attr, Is.Not.Null, fieldName);
            Assert.That(attr.BindingMode, Is.EqualTo(expectedBindingMode), fieldName);
        }

        private static void AssertTransientResource(string fieldName)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);

            Assert.That(field.GetCustomAttribute<RenderGraphResource>(), Is.Not.Null, fieldName);
            Assert.That(field.GetCustomAttribute<TransientResourceAttribute>(), Is.Not.Null, fieldName);
        }

        private static void AssertNonTransientResource(string fieldName)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);

            Assert.That(field.GetCustomAttribute<RenderGraphResource>(), Is.Not.Null, fieldName);
            Assert.That(field.GetCustomAttribute<TransientResourceAttribute>(), Is.Null, fieldName);
        }
    }
}
