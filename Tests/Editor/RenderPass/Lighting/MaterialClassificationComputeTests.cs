using System.IO;
using NUnit.Framework;
using UnityEngine;

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
            Assert.That(source, Does.Contain("#define BUILD_INDIRECT_THREADS"));
            Assert.That(source, Does.Contain("InitializeTileClassification(groupIndex);"));
            Assert.That(source, Does.Contain("SubmitPixelClassification(classificationMask);"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _MaterialTileClasses;"));
            Assert.That(source, Does.Contain("groupshared uint gs_LocalMaterialCounts"));
            Assert.That(source, Does.Contain("groupshared uint gs_GlobalMaterialOffsets"));
            Assert.That(source, Does.Contain("groupshared uint gs_LocalTileOffsets"));
            Assert.That(source, Does.Contain("groupshared uint gs_LocalTileClasses"));
            Assert.That(source, Does.Contain("_MaterialTileClasses[tileIndex] = SelectMaterialClass(gs_TileMask);"));
            Assert.That(source, Does.Contain("InitializeMaterialTileListBuild(groupIndex);"));
            Assert.That(source, Does.Contain("InterlockedAdd(gs_LocalMaterialCounts[materialClass], 1u, localTileOffset);"));
            Assert.That(source, Does.Contain("InterlockedAdd(_MaterialClassCounts[groupIndex], localTileCount, globalTileOffset);"));
            Assert.That(source, Does.Contain("gs_GlobalMaterialOffsets[groupIndex] = globalTileOffset;"));
            Assert.That(source, Does.Contain("InterlockedAdd(_StandardIndirectArgs[0], count, ignored);"));
            Assert.That(source, Does.Contain("InterlockedAdd(_FabricIndirectArgs[0], count, ignored);"));
            Assert.That(source, Does.Contain("InterlockedAdd(_ClearCoatIndirectArgs[0], count, ignored);"));
            Assert.That(source, Does.Contain("TileClassifaction::PackTileCoord(uint2(tileX, tileY));"));
            Assert.That(source, Does.Contain("AppendMaterialTile(materialClass, globalTileOffset, packedTileCoord);"));
            Assert.That(source, Does.Contain("_StandardIndirectArgs[1] = 1;"));
            Assert.That(source, Does.Contain("_FabricIndirectArgs[1] = 1;"));
            Assert.That(source, Does.Contain("_ClearCoatIndirectArgs[1] = 1;"));
            Assert.That(source, Does.Not.Contain("InterlockedAdd(_MaterialClassCounts[VIVID_GBUFFER_MATERIAL_CLEARCOAT]"));
            Assert.That(source, Does.Not.Contain("InterlockedAdd(_MaterialClassCounts[VIVID_GBUFFER_MATERIAL_FABRIC]"));
            Assert.That(source, Does.Not.Contain("InterlockedAdd(_MaterialClassCounts[VIVID_GBUFFER_MATERIAL_STANDARD]"));
            Assert.That(source, Does.Not.Contain("_StandardIndirectArgs[0] = _MaterialClassCounts[0];"));
            Assert.That(source, Does.Not.Contain("_FabricIndirectArgs[0] = _MaterialClassCounts[1];"));
            Assert.That(source, Does.Not.Contain("_ClearCoatIndirectArgs[0] = _MaterialClassCounts[2];"));
            Assert.That(source, Does.Not.Contain("WriteMaterialPixelIndex("));
            Assert.That(source, Does.Not.Contain("VertexCountPerInstance"));
            Assert.That(source, Does.Not.Contain("StructuredBuffer<PunctualLightCullData> _PunctualLightCullData;"));
            Assert.That(source, Does.Not.Contain("BuildClusteredLightList"));
            Assert.That(source, Does.Not.Contain("SpotConeIntersectsCluster"));
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
