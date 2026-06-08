using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VividRP.Editor.Tests
{
    public sealed class MaterialClassificationComputeTests
    {
        [Test]
        public void MaterialClassificationCompute_UsesTileFeatureFlags_ForVariantTileLists()
        {
            var source = File.ReadAllText(GetComputeShaderSourcePath());

            Assert.That(source, Does.Contain("#include \"Packages/com.af8a2a.vividrp/Shaders/Core/Public/TileClassification.hlsl\""));
            Assert.That(source, Does.Contain("#define CLASSIFY_TILE_SIZE 8"));
            Assert.That(source, Does.Contain("#define BUILD_INDIRECT_THREADS"));
            Assert.That(source, Does.Contain("#define VIVID_MATERIAL_FEATURE_VARIANT_COUNT 7"));
            Assert.That(source, Does.Contain("#define VIVID_MATERIAL_FEATURE_VARIANT_CATCH_ALL 6"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _MaterialTileFeatureFlags;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _MaterialFeatureTileList;"));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> _MaterialFeatureIndirectArgs;"));
            Assert.That(source, Does.Contain("InitializeTileClassification(groupIndex);"));
            Assert.That(source, Does.Contain("SubmitPixelClassification(materialFeatures);"));
            Assert.That(source, Does.Contain("TryDecodeValidMaterialFeatures(gbuffer0.a, materialFeatures);"));
            Assert.That(source, Does.Contain("uint rawFeatureId = (uint)min(round(saturate(encodedMaterialFeatureId) * 255.0), 255.0);"));
            Assert.That(source, Does.Contain("rawFeatureId > VIVID_MATERIALFEATURE_ID_MASK"));
            Assert.That(source, Does.Contain("materialFeatures = 0u;"));
            Assert.That(source, Does.Contain("IsValidMaterialFeatureMask(decodedFeatures)"));
            Assert.That(source, Does.Contain("_MaterialTileFeatureFlags[tileIndex] = gs_TileMask;"));
            Assert.That(source, Does.Contain("uint SelectMaterialFeatureVariant(uint materialFeatures)"));
            Assert.That(source, Does.Contain("uint deferredFeatures = materialFeatures & VIVID_MATERIALFEATURE_DEFERRED_MASK;"));
            Assert.That(source, Does.Contain("return VIVID_MATERIAL_FEATURE_VARIANT_CATCH_ALL;"));
            Assert.That(source, Does.Contain("groupshared uint gs_LocalVariantCounts"));
            Assert.That(source, Does.Contain("groupshared uint gs_GlobalVariantOffsets"));
            Assert.That(source, Does.Contain("groupshared uint gs_LocalTileOffsets"));
            Assert.That(source, Does.Contain("groupshared uint gs_LocalTileVariants"));
            Assert.That(source, Does.Contain("InitializeMaterialFeatureTileListBuild(groupIndex);"));
            Assert.That(source, Does.Contain("InterlockedAdd(gs_LocalVariantCounts[variant], 1u, localTileOffset);"));
            Assert.That(source, Does.Contain("uint argsOffset = groupIndex * VIVID_INDIRECT_ARGS_ELEMENT_COUNT;"));
            Assert.That(source, Does.Contain("InterlockedAdd(_MaterialFeatureIndirectArgs[argsOffset], localTileCount, globalTileOffset);"));
            Assert.That(source, Does.Contain("gs_GlobalVariantOffsets[groupIndex] = globalTileOffset;"));
            Assert.That(source, Does.Contain("TileClassifaction::PackTileCoord(uint2(tileX, tileY));"));
            Assert.That(source, Does.Contain("_MaterialFeatureTileList[variant * _MaterialTileCount + globalTileOffset] = packedTileCoord;"));
            Assert.That(source, Does.Contain("_MaterialFeatureIndirectArgs[argsOffset + 0u] = 0u;"));
            Assert.That(source, Does.Contain("_MaterialFeatureIndirectArgs[argsOffset + 1u] = 1u;"));
            Assert.That(source, Does.Contain("_MaterialFeatureIndirectArgs[argsOffset + 2u] = 1u;"));
            Assert.That(source, Does.Contain("_MaterialFeatureIndirectArgs[argsOffset + 3u] = 0u;"));
            Assert.That(source, Does.Not.Contain("_StandardMaterialIndices"));
            Assert.That(source, Does.Not.Contain("_FabricMaterialIndices"));
            Assert.That(source, Does.Not.Contain("_ClearCoatMaterialIndices"));
            Assert.That(source, Does.Not.Contain("_MaterialClassCounts"));
            Assert.That(source, Does.Not.Contain("WriteMaterialPixelIndex("));
            Assert.That(source, Does.Not.Contain("VertexCountPerInstance"));
            Assert.That(source, Does.Not.Contain("StructuredBuffer<PunctualLightCullData> _PunctualLightCullData;"));
            Assert.That(source, Does.Not.Contain("BuildClusteredLightList"));
            Assert.That(source, Does.Not.Contain("SpotConeIntersectsCluster"));
        }

        [Test]
        public void TileClassificationHelpers_ExposeStructuredDispatchFinalize_ForRenderGraphIndirectArgs()
        {
            var source = File.ReadAllText(GetTileClassificationSourcePath());

            Assert.That(source, Does.Contain("void FinalizeTileClassificationDispatch("));
            Assert.That(source, Does.Contain("RWStructuredBuffer<uint> indirectArgs"));
            Assert.That(source, Does.Contain("uint argsElementOffset"));
            Assert.That(source, Does.Contain("InterlockedAdd(indirectArgs[argsElementOffset], 1, globalTileIndex);"));
            Assert.That(source, Does.Contain("tileList[globalTileIndex] = PackTileCoord(tileCoord);"));
        }

        private static string GetComputeShaderSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Material", "MaterialClassification.compute");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected compute shader source at '{shaderPath}'.");
            return shaderPath;
        }

        private static string GetTileClassificationSourcePath()
        {
            var shaderPath = GetPackageFilePath("Shaders", "Core", "Public", "TileClassification.hlsl");

            Assert.That(File.Exists(shaderPath), Is.True, $"Expected tile classification source at '{shaderPath}'.");
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
