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
        public void LightGridPass_SourceUploadsDecalDataAndKeepsDecalCategoryShift()
        {
            var source = File.ReadAllText(GetPackageFilePath("Runtime", "RenderPass", "Core", "Lighting", "LightGridPass.cs"));
            var computeSource = File.ReadAllText(GetPackageFilePath("Shaders", "Core", "Private", "Lighting", "lightlistbuild-clustered.compute"));

            Assert.That(source, Does.Contain("m_DecalDataBuffer = RenderGraphBuffer.CreateStructured(\"DecalData\""));
            Assert.That(source, Does.Contain("UploadManagedArray("));
            Assert.That(source, Does.Contain("m_DecalDataBuffer,"));
            Assert.That(source, Does.Contain("m_ShaderVariablesLightListCB._DecalIndexShift = (uint)(m_PunctualLightCount + m_AreaLightCount);"));
            Assert.That(computeSource, Does.Contain("WriteShiftIndex(t, LIGHTCATEGORY_DECAL, _DecalIndexShift);"));
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
