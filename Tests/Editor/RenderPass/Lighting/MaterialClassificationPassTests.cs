using System.IO;
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

            Assert.That(textureEntries.Select(entry => entry.Name), Is.EqualTo(new[] { "Depth", "GBuffer0", "GBuffer1" }));
            Assert.That(
                textureEntries.Single(entry => entry.Name == "GBuffer0").Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.R8G8B8A8_SRGB));
            Assert.That(
                textureEntries.Single(entry => entry.Name == "GBuffer1").Texture.desc.ColorFormat,
                Is.EqualTo(GraphicsFormat.A2B10G10R10_UNormPack32));
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
                AssertTextureSize(pass, "m_GBuffer1", 320, 180);
                AssertTextureSize(pass, "m_DepthTexture", 320, 180);

                var expectedTileCountX = (320 + 7) / 8;
                var expectedTileCountY = (180 + 7) / 8;
                var expectedTileCount = expectedTileCountX * expectedTileCountY;
                AssertStructuredBuffer(pass, "m_MaterialTileFeatureFlags", expectedTileCount, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_MaterialFeatureTileList", expectedTileCount * 4, sizeof(uint), GraphicsBuffer.Target.Structured);
                AssertStructuredBuffer(pass, "m_MaterialFeatureIndirectArgs", 16, sizeof(uint), GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments);

                AssertImportedBuffer(pass, "m_MaterialTileFeatureFlags", expectedTileCount, sizeof(uint));
                AssertImportedBuffer(pass, "m_MaterialFeatureTileList", expectedTileCount * 4, sizeof(uint));
                AssertImportedBuffer(pass, "m_MaterialFeatureIndirectArgs", 16, sizeof(uint));
            }
            finally
            {
                pass.Dispose();
            }
        }

        [Test]
        public void SupportsAsyncCompute_ReturnsTrue_ForClassificationPass()
        {
            Assert.That((typeof(MaterialClassificationPass)).SupportsAsyncCompute(), Is.True);
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
            SetFieldValue(pass, "m_ClassifyDeferredExportsKernel", 10);
            SetFieldValue(pass, "m_BuildDeferredVariantIndirectArgsKernel", 20);
            SetFieldValue(pass, "m_ClassifyDeferredExportsWave32Kernel", 32);
            SetFieldValue(pass, "m_BuildDeferredVariantIndirectArgsWave32Kernel", 33);
            SetFieldValue(pass, "m_ClassifyDeferredExportsWave64Kernel", 64);
            SetFieldValue(pass, "m_BuildDeferredVariantIndirectArgsWave64Kernel", 65);

            InvokeSelectMaterialClassificationKernels(pass, 64);

            Assert.That(GetFieldValue<int>(pass, "m_SelectedClassifyDeferredExportsKernel"), Is.EqualTo(64));
            Assert.That(GetFieldValue<int>(pass, "m_SelectedBuildDeferredVariantIndirectArgsKernel"), Is.EqualTo(65));

            InvokeSelectMaterialClassificationKernels(pass, 32);

            Assert.That(GetFieldValue<int>(pass, "m_SelectedClassifyDeferredExportsKernel"), Is.EqualTo(32));
            Assert.That(GetFieldValue<int>(pass, "m_SelectedBuildDeferredVariantIndirectArgsKernel"), Is.EqualTo(33));

            InvokeSelectMaterialClassificationKernels(pass, 16);

            Assert.That(GetFieldValue<int>(pass, "m_SelectedClassifyDeferredExportsKernel"), Is.EqualTo(10));
            Assert.That(GetFieldValue<int>(pass, "m_SelectedBuildDeferredVariantIndirectArgsKernel"), Is.EqualTo(20));
        }

        [Test]
        public void ClassificationShader_UsesDeferredExportClassOnly_ForFourVariantHierarchy()
        {
            UnityEditor.PackageManager.PackageInfo package =
                UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                    typeof(MaterialClassificationPass).Assembly);
            Assert.That(package, Is.Not.Null);
            string path = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Material",
                "MaterialClassification.compute");

            Assert.That(File.Exists(path), Is.True, path);
            string source = File.ReadAllText(path);
            string compactSource = string.Concat(
                source.Where(character => !char.IsWhiteSpace(character)));

            string surfaceSummaryPath = Path.Combine(
                package.resolvedPath,
                "Shaders",
                "Core",
                "Public",
                "SurfaceSummaryGBuffer.hlsl");
            Assert.That(File.Exists(surfaceSummaryPath), Is.True, surfaceSummaryPath);
            string surfaceSummarySource = File.ReadAllText(surfaceSummaryPath);

            StringAssert.Contains("SurfaceSummaryGBuffer.hlsl", source);
            StringAssert.Contains("#define VIVID_DEFERRED_VARIANT_COUNT 4", source);
            StringAssert.Contains("#define VIVID_DEFERRED_CLASS_BIT_FAST_SLAB (1u << 0)", surfaceSummarySource);
            StringAssert.Contains("#define VIVID_DEFERRED_CLASS_BIT_GENERAL_SLAB (1u << 1)", surfaceSummarySource);
            StringAssert.Contains("#define VIVID_DEFERRED_CLASS_BIT_DUAL_SLAB (1u << 2)", surfaceSummarySource);
            StringAssert.Contains("#define VIVID_DEFERRED_CLASS_BIT_CATCH_ALL (1u << 3)", surfaceSummarySource);
            StringAssert.Contains("#pragma kernel ClearDeferredVariantArgs", source);
            StringAssert.Contains("#pragma kernel ClassifyDeferredExports", source);
            StringAssert.Contains("#pragma kernel BuildDeferredVariantIndirectArgs", source);
            StringAssert.Contains(
                "#pragma kernel ClearDualSlabSidecarDrawArgs",
                source);
            StringAssert.Contains(
                "#pragma kernel ClassifyDualSlabSidecarTiles",
                source);
            StringAssert.Contains("Texture2D<float4> _GBuffer1;", source);
            StringAssert.Contains("VividIsSurfaceSummaryGBufferABIValid(gbuffer1.a)", source);
            StringAssert.Contains("VividDecodeDeferredExportHeader(gbuffer0.a)", source);
            StringAssert.Contains("VividGetDeferredExportClass(deferredExportHeader)", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_EMPTY", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_UNLIT", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_FAST_SLAB", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_GENERAL_SLAB", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_DUAL_SLAB", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_SUBSURFACE", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_CATCH_ALL", source);
            StringAssert.Contains("VIVID_DEFERRED_EXPORT_CLASS_ERROR", source);
            StringAssert.Contains(
                "||deferredExportClass==VIVID_DEFERRED_EXPORT_CLASS_ERROR)"
                + "{returnVIVID_DEFERRED_CLASS_BIT_CATCH_ALL;}",
                compactSource);
            StringAssert.Contains("WaveActiveBitOr(deferredClassOneHot)", source);
            StringAssert.Contains("SelectDeferredVariant(_MaterialTileFeatureFlags[tileIndex])", source);
            int catchAllSelection = source.IndexOf("if ((deferredClassMask & VIVID_DEFERRED_CLASS_BIT_CATCH_ALL)");
            int dualSlabSelection = source.IndexOf("if ((deferredClassMask & VIVID_DEFERRED_CLASS_BIT_DUAL_SLAB)");
            int generalSlabSelection = source.IndexOf("if ((deferredClassMask & VIVID_DEFERRED_CLASS_BIT_GENERAL_SLAB)");
            int fastSlabSelection = source.IndexOf("if ((deferredClassMask & VIVID_DEFERRED_CLASS_BIT_FAST_SLAB)");
            Assert.That(catchAllSelection, Is.GreaterThanOrEqualTo(0));
            Assert.That(catchAllSelection, Is.LessThan(dualSlabSelection));
            Assert.That(dualSlabSelection, Is.LessThan(generalSlabSelection));
            Assert.That(generalSlabSelection, Is.LessThan(fastSlabSelection));
            StringAssert.DoesNotContain("VIVID_MATERIALFEATURE_", source);
            StringAssert.DoesNotContain("1u << deferredExportClass", source);
            StringAssert.DoesNotContain("VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_SSR", source);
            StringAssert.DoesNotContain("VIVID_DEFERRED_EXPORT_FLAG_RECEIVE_DECALS", source);
            StringAssert.DoesNotContain("VIVID_DEFERRED_EXPORT_FLAG_HAS_DIFFUSE_IRRADIANCE", source);
            StringAssert.DoesNotContain("_LayerAux", source);
            StringAssert.Contains(
                "_MaterialFeatureIndirectArgs[0u]=3u;"
                + "_MaterialFeatureIndirectArgs[1u]=0u;",
                compactSource);
            StringAssert.Contains(
                "InterlockedAdd(_MaterialFeatureIndirectArgs[1u],"
                + "1u,tileOffset);",
                compactSource);
            StringAssert.Contains(
                "_MaterialFeatureTileList[tileOffset]="
                + "TileClassifaction::PackTileCoord(groupId.xy);",
                compactSource);
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
