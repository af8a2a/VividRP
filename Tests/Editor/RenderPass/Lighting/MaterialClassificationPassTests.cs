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
    public class MaterialClassificationPassTests
    {
        [Test]
        public void Initialize_RegistersGBufferInputsAndClassificationBuffers_WhenPassIsCreated()
        {
            IRenderPass renderPass = new MaterialClassificationPass();

            var resources = renderPass.Initialize();
            var textureEntries = resources.Textures.OrderBy(entry => entry.Name).ToArray();
            var bufferEntries = resources.Buffers.OrderBy(entry => entry.Name).ToArray();

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Depth", "GBuffer0" }));
            Assert.That(bufferEntries.Select(entry => entry.Name), Is.EqualTo(new[]
            {
                "MaterialFeatureIndirectArgs",
                "MaterialFeatureTileList",
                "MaterialTileFeatureFlags"
            }));
        }

        [Test]
        public void Prepare_ResizesClassificationBuffers_WhenCameraSizeChanges()
        {
            var pass = new MaterialClassificationPass();
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
                AssertStructuredBuffer(pass, "m_MaterialTileFeatureFlags", expectedTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_MaterialFeatureTileList", expectedTileCount * 7, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_MaterialFeatureIndirectArgs", 28, sizeof(uint), GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);

                AssertImportedBuffer(pass, "m_MaterialTileFeatureFlags", expectedTileCount, sizeof(uint));
                AssertImportedBuffer(pass, "m_MaterialFeatureTileList", expectedTileCount * 7, sizeof(uint));
                AssertImportedBuffer(pass, "m_MaterialFeatureIndirectArgs", 28, sizeof(uint));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsTrue_ForClassificationPass()
        {
            Assert.That(RenderGraphPassExecutionUtility.SupportsAsyncCompute(typeof(MaterialClassificationPass)), Is.True);
        }

        [Test]
        public void ResolveMaterialClassificationWaveSize_SelectsSupportedWavePath_FromComputeSubGroupSize()
        {
            Assert.That(MaterialClassificationPass.ResolveMaterialClassificationWaveSize(64), Is.EqualTo(64));
            Assert.That(MaterialClassificationPass.ResolveMaterialClassificationWaveSize(32), Is.EqualTo(32));
            Assert.That(MaterialClassificationPass.ResolveMaterialClassificationWaveSize(16), Is.Zero);
            Assert.That(MaterialClassificationPass.ResolveMaterialClassificationWaveSize(0), Is.Zero);
        }

        [Test]
        public void SelectMaterialClassificationKernels_UsesWaveKernels_WhenComputeSubGroupSizeMatches()
        {
            var pass = new MaterialClassificationPass();
            SetFieldValue(pass, "m_ClassifyMaterialFeaturesKernel", 10);
            SetFieldValue(pass, "m_BuildMaterialFeatureIndirectArgsKernel", 20);
            SetFieldValue(pass, "m_ClassifyMaterialFeaturesWave32Kernel", 32);
            SetFieldValue(pass, "m_BuildMaterialFeatureIndirectArgsWave32Kernel", 33);
            SetFieldValue(pass, "m_ClassifyMaterialFeaturesWave64Kernel", 64);
            SetFieldValue(pass, "m_BuildMaterialFeatureIndirectArgsWave64Kernel", 65);

            InvokeSelectMaterialClassificationKernels(pass, 64);

            Assert.That(GetFieldValue<int>(pass, "m_SelectedClassifyMaterialFeaturesKernel"), Is.EqualTo(64));
            Assert.That(GetFieldValue<int>(pass, "m_SelectedBuildMaterialFeatureIndirectArgsKernel"), Is.EqualTo(65));

            InvokeSelectMaterialClassificationKernels(pass, 32);

            Assert.That(GetFieldValue<int>(pass, "m_SelectedClassifyMaterialFeaturesKernel"), Is.EqualTo(32));
            Assert.That(GetFieldValue<int>(pass, "m_SelectedBuildMaterialFeatureIndirectArgsKernel"), Is.EqualTo(33));

            InvokeSelectMaterialClassificationKernels(pass, 16);

            Assert.That(GetFieldValue<int>(pass, "m_SelectedClassifyMaterialFeaturesKernel"), Is.EqualTo(10));
            Assert.That(GetFieldValue<int>(pass, "m_SelectedBuildMaterialFeatureIndirectArgsKernel"), Is.EqualTo(20));
        }

        [Test]
        public void Prepare_ComputesBuildIndirectDispatchGroups_WhenTileCountExceedsSingleWave()
        {
            var pass = new MaterialClassificationPass();
            try
            {
                var frameData = new ContextContainer();
                var cameraData = frameData.GetOrCreate<VividCameraData>();
                cameraData.actualWidth = 320;
                cameraData.actualHeight = 180;

                pass.Prepare(frameData);

                var field = typeof(MaterialClassificationPass).GetField("m_BuildIndirectDispatchGroupCountX", BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(field, Is.Not.Null);
                Assert.That(field.GetValue(pass), Is.EqualTo(15));
            }
            finally
            {
                pass.Dispose();
            }
        }

        private static void AssertTextureSize(MaterialClassificationPass pass, string fieldName, int expectedWidth, int expectedHeight)
        {
            var field = typeof(MaterialClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var texture = (RenderGraphTexture)field.GetValue(pass);
            Assert.That(texture, Is.Not.Null);
            Assert.That(texture.desc.Width, Is.EqualTo(expectedWidth));
            Assert.That(texture.desc.Height, Is.EqualTo(expectedHeight));
        }

        private static void AssertStructuredBuffer(MaterialClassificationPass pass, string fieldName, int expectedCount, int expectedStride, GraphicsBuffer.Target expectedTarget)
        {
            var field = typeof(MaterialClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);

            var buffer = (RenderGraphBuffer)field.GetValue(pass);
            Assert.That(buffer, Is.Not.Null);
            Assert.That(buffer.desc.Count, Is.EqualTo(expectedCount));
            Assert.That(buffer.desc.Stride, Is.EqualTo(expectedStride));
            Assert.That(buffer.desc.Target, Is.EqualTo(expectedTarget));
        }

        private static void AssertImportedBuffer(MaterialClassificationPass pass, string fieldName, int expectedCount, int expectedStride)
        {
            var field = typeof(MaterialClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

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

        private static T GetFieldValue<T>(MaterialClassificationPass pass, string fieldName)
        {
            var field = typeof(MaterialClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(pass);
        }

        private static void SetFieldValue<T>(MaterialClassificationPass pass, string fieldName, T value)
        {
            var field = typeof(MaterialClassificationPass).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(pass, value);
        }

        private static void InvokeSelectMaterialClassificationKernels(MaterialClassificationPass pass, int computeSubGroupSize)
        {
            var method = typeof(MaterialClassificationPass).GetMethod(
                "SelectMaterialClassificationKernels",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(pass, new object[] { computeSubGroupSize });
        }
    }
}
