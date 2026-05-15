using System;
using System.IO;
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

            AssertNoField("m_DirectionalLightUploadData");
            AssertNoField("m_FiniteLightBoundUploadData");
            AssertNoField("m_LightVolumeDataUploadData");
            AssertNoField("m_LayeredOffsetUploadData");
        }

        [Test]
        public void ResizeStructuredBuffer_ExpandsCapacity_WhenRequiredCountExceedsCurrentCapacity()
        {
            var buffer = RenderGraphBuffer.CreateStructured("TestBuffer", 100, sizeof(uint));

            InvokeResizeStructuredBuffer(buffer, 120, sizeof(uint));

            Assert.That(buffer.desc.Count, Is.EqualTo(120));
            Assert.That(buffer.desc.Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));
        }

        [Test]
        public void ResizeStructuredBuffer_KeepsCapacity_WhenRequiredCountFitsCurrentCapacity()
        {
            var buffer = RenderGraphBuffer.CreateStructured("TestBuffer", 100, sizeof(uint));

            InvokeResizeStructuredBuffer(buffer, 34, sizeof(uint));

            Assert.That(buffer.desc.Count, Is.EqualTo(100));
            Assert.That(buffer.desc.Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));
        }

        [Test]
        public void ResizeStructuredBuffer_ShrinksCapacity_WhenRequiredCountFallsBelowOneThirdCapacity()
        {
            var buffer = RenderGraphBuffer.CreateStructured("TestBuffer", 100, sizeof(uint));

            InvokeResizeStructuredBuffer(buffer, 32, sizeof(uint));

            Assert.That(buffer.desc.Count, Is.EqualTo(32));
            Assert.That(buffer.desc.Stride, Is.EqualTo(sizeof(uint)));
            Assert.That(buffer.desc.Target, Is.EqualTo(GraphicsBuffer.Target.Structured));
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

        private static void InvokeResizeStructuredBuffer(RenderGraphBuffer buffer, int count, int stride)
        {
            var method = typeof(LightGridPass).GetMethod(
                "ResizeStructuredBuffer",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            method.Invoke(null, new object[] { buffer, count, stride });
        }

        private static void AssertNativeArrayField(string fieldName, Type elementType)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            Assert.That(field.FieldType.IsGenericType, Is.True, fieldName);
            Assert.That(field.FieldType.GetGenericTypeDefinition(), Is.EqualTo(typeof(NativeArray<>)), fieldName);
            Assert.That(field.FieldType.GetGenericArguments()[0], Is.EqualTo(elementType), fieldName);
        }

        private static void AssertNoField(string fieldName)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Null, fieldName);
        }

        private static void AssertBindingMode(string fieldName, RenderGraphResourceBindingMode expectedBindingMode)
        {
            var field = typeof(LightGridPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);

            var attr = field.GetCustomAttribute<RenderGraphResource>();
            Assert.That(attr, Is.Not.Null, fieldName);
            Assert.That(attr.BindingMode, Is.EqualTo(expectedBindingMode), fieldName);
        }

        private static string GetPackageFilePath(params string[] relativeParts)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var packageRoots = new[]
            {
                Path.Combine(projectRoot, "Packages", "VividRP"),
                Path.Combine(projectRoot, "Packages", "com.af8a2a.vividrp")
            };

            foreach (var packageRoot in packageRoots)
            {
                var fullPath = Path.Combine(packageRoot, Path.Combine(relativeParts));
                if (File.Exists(fullPath))
                    return fullPath;
            }

            return Path.Combine(packageRoots[0], Path.Combine(relativeParts));
        }
    }
}
