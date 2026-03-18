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
        public void MaterialClassificationCompute_UsesTileClassificationHelpers_ForExclusiveMaterialTileLists()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/TileClassification.hlsl\""));
            Assert.That(source, Does.Contain("#define CLASSIFY_TILE_SIZE 8"));
            Assert.That(source, Does.Contain("InitializeTileClassification(groupIndex);"));
            Assert.That(source, Does.Contain("SubmitPixelClassification(classificationMask);"));
            Assert.That(source, Does.Contain("PackTileCoord("));
            Assert.That(source, Does.Contain("AppendMaterialTile("));
            Assert.That(source, Does.Contain("InterlockedAdd(_MaterialClassCounts[VIVID_GBUFFER_MATERIAL_CLEARCOAT]"));
            Assert.That(source, Does.Contain("InterlockedAdd(_MaterialClassCounts[VIVID_GBUFFER_MATERIAL_FABRIC]"));
            Assert.That(source, Does.Contain("InterlockedAdd(_MaterialClassCounts[VIVID_GBUFFER_MATERIAL_STANDARD]"));
            Assert.That(source, Does.Contain("_StandardIndirectArgs[0] = _MaterialClassCounts[0];"));
            Assert.That(source, Does.Contain("_FabricIndirectArgs[0] = _MaterialClassCounts[1];"));
            Assert.That(source, Does.Contain("_ClearCoatIndirectArgs[0] = _MaterialClassCounts[2];"));
            Assert.That(source, Does.Contain("_StandardIndirectArgs[1] = 1;"));
            Assert.That(source, Does.Contain("_FabricIndirectArgs[1] = 1;"));
            Assert.That(source, Does.Contain("_ClearCoatIndirectArgs[1] = 1;"));
            Assert.That(source, Does.Not.Contain("gs_LocalMaterialCounts"));
            Assert.That(source, Does.Not.Contain("gs_GlobalMaterialOffsets"));
            Assert.That(source, Does.Not.Contain("WriteMaterialPixelIndex("));
            Assert.That(source, Does.Not.Contain("VertexCountPerInstance"));
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
            var shaderPath = GetPackageFilePath("Shaders", "Material", "MaterialClassification.compute");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
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
