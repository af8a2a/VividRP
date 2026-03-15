using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VividRP.Runtime;

namespace VividRP.Editor.Tests
{
    public sealed class MaterialClassificationComputeTests
    {
        [Test]
        public void MaterialClassificationCompute_UsesTileClassificationHelpers_ForMaterialCompaction()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/TileClassification.hlsl\""));
            Assert.That(source, Does.Contain("#define CLASSIFY_TILE_SIZE 8"));
            Assert.That(source, Does.Contain("InitializeTileClassification(groupIndex);"));
            Assert.That(source, Does.Contain("SubmitPixelClassification(classificationMask);"));
            Assert.That(source, Does.Contain("groupshared uint gs_LocalMaterialCounts"));
            Assert.That(source, Does.Contain("groupshared uint gs_GlobalMaterialOffsets"));
            Assert.That(source, Does.Contain("InterlockedAdd(_MaterialClassCounts[groupIndex], localMaterialCount, gs_GlobalMaterialOffsets[groupIndex]);"));
            Assert.That(source, Does.Contain("WriteMaterialPixelIndex(materialId, gs_GlobalMaterialOffsets[materialId] + localWriteIndex, pixelIndex);"));
            Assert.That(source, Does.Not.Contain("StructuredBuffer<PunctualLightCullData> _PunctualLightCullData;"));
            Assert.That(source, Does.Not.Contain("BuildClusteredLightList"));
            Assert.That(source, Does.Not.Contain("SpotConeIntersectsCluster"));
        }

        [Test]
        public void VividRPCoreResources_DeclaresMaterialClassificationCompute()
        {
            var field = typeof(VividRPCoreResources).GetField(nameof(VividRPCoreResources.MaterialClassificationCompute));

            Assert.That(field, Is.Not.Null);

            var resourcePath = field.GetCustomAttribute<ResourcePathAttribute>();

            Assert.That(resourcePath, Is.Not.Null);
            Assert.That(resourcePath.Path, Is.EqualTo("Shaders/Material/MaterialClassification"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Packages",
                "com.af8a2a.vividrp",
                "Shaders",
                "Material",
                "MaterialClassification.compute"));

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
        }
    }
}
